import Foundation
import QuartzCore

/// Kjører cuer: pre-wait, auto-continue, auto-follow, stopp og panikk. Alt skjer på hovedtråden;
/// bare selve lydmiksingen og dekodingen går på egne tråder.
final class CueEngine: ObservableObject {
    let audio: AudioEngine
    /// Standard utgang for video, bilde og PDF (skjermen i innstillingene). Vises også som forhåndsvisning.
    let output: OutputCompositor
    /// Skjermen standardutgangen står på (−1 = eget vindu).
    private(set) var defaultScreen = -1
    /// Utganger for cuer som har valgt en annen skjerm enn standard, per skjerm.
    private(set) var extraOutputs: [Int: OutputCompositor] = [:]
    let midi: MidiSystem
    /// Art-Net-utgang for lys-cuer.
    let dmx = DmxOutput()
    /// Opptak for live-inngang-cuer (startes ved første bruk).
    let input = AudioInput()
    /// Inngangen live-cuer bruker (fra innstillingene).
    var inputDeviceUID = ""
    /// Forhåndslytting i bølgeformredigeringen (ikke en del av showet).
    private(set) lazy var preview = AudioPreview(target: { [weak self] in self?.previewTarget() })
    /// Egen lydenhet for forhåndslytting (f.eks. hodetelefoner). Kjører bare når en er valgt.
    let previewAudio = AudioEngine()
    private var previewDeviceUID = ""
    private var previewBus = 0
    private let cueList: () -> [Cue]
    private let t0 = CACurrentMediaTime()
    private var timer: Timer?

    @Published private(set) var active: [ActiveCue] = []
    var onTick: (() -> Void)?
    var onLog: ((String) -> Void)?
    /// En «gå til»-cue vil flytte GO-markøren (cuenummer, og om mål-cuen skal kjøres).
    var onGoto: ((String, Bool) -> Void)?
    /// En memo-cue er kjørt: vis teksten til operatøren.
    var onMemo: ((Cue) -> Void)?

    /// Alt står stille: lyd, video og tiden for pre-wait og fader.
    @Published private(set) var isPaused = false
    private var pausedTotal = 0.0
    private var pausedAt = 0.0

    /// Showklokka. Står stille under pause, så pre-wait, fader og bildetider venter.
    var now: Double { (isPaused ? pausedAt : CACurrentMediaTime()) - t0 - pausedTotal }

    init(audio: AudioEngine, output: OutputCompositor, midi: MidiSystem, cueList: @escaping () -> [Cue]) {
        self.audio = audio
        self.output = output
        self.midi = midi
        self.cueList = cueList
        let t = Timer(timeInterval: 1.0 / 60, repeats: true) { [weak self] _ in self?.tick() }
        RunLoop.main.add(t, forMode: .common) // går også mens menyer er åpne
        timer = t
    }

    deinit { timer?.invalidate() }

    // MARK: Utganger

    /// Utgangen for en cue: −2 = standard (innstillingene), −1 = eget vindu, 0… = skjerm.
    func output(forScreen screen: Int) -> OutputCompositor {
        if screen < -1 || screen == defaultScreen { return output }
        if let o = extraOutputs[screen] { return o }
        let o = OutputCompositor()
        o.configure(screen: screen)
        extraOutputs[screen] = o
        return o
    }

    /// Standardskjermen er endret i innstillingene.
    func setDefaultScreen(_ screen: Int) {
        defaultScreen = screen
        output.configure(screen: screen)
        // En ekstra utgang på samme skjerm som den nye standarden trengs ikke lenger.
        if let o = extraOutputs.removeValue(forKey: screen) { o.hide() }
    }

    /// Alle utganger som finnes (standard først).
    var allOutputs: [OutputCompositor] { [output] + extraOutputs.keys.sorted().compactMap { extraOutputs[$0] } }

    // MARK: Forhåndslytting

    /// Hvor forhåndslyttingen skal spilles (se Innstillinger → Lyd).
    private func previewTarget() -> AudioPreview.Target? {
        if !previewDeviceUID.isEmpty {
            return previewAudio.mixer.map { AudioPreview.Target(mixer: $0, leftBus: 1) } // aldri til salen hvis hodetelefonene mangler
        }
        guard let m = audio.mixer else { return nil }
        let bus = previewBus > 0 && previewBus <= m.busCount ? previewBus : 0
        return AudioPreview.Target(mixer: m, leftBus: bus)
    }

    func configurePreview(_ s: AppSettings) {
        previewBus = s.previewBus
        guard s.previewDeviceUID != previewDeviceUID else { return }
        preview.stop()
        previewDeviceUID = s.previewDeviceUID
        if previewDeviceUID.isEmpty {
            previewAudio.stop()
        } else {
            var p = AppSettings()
            p.audioDeviceUID = previewDeviceUID
            p.maxChannels = 2
            p.bufferFrames = s.bufferFrames
            p.buses = PatchBus.defaultPatch(2)
            previewAudio.start(p, fallbackToDefault: false)
        }
    }

    // MARK: Avfyring

    /// - Parameter offset: start så mange sekunder inn i cuen (prøver: «spill fra her»).
    func fire(_ cue: Cue, offset: Double = 0) {
        if isPaused { resumeAll() } // GO etter pause fortsetter alt
        let a = make(cue)
        a.startOffset = max(0, offset)
        a.startAt = now + (offset > 0 ? 0 : cue.preWait)
        a.onCompleted = { [weak self] in self?.completed($0) }
        cue.error = nil
        cue.activeCount += 1
        active.append(a)
        if cue.preWait > 0 { cue.isPreWaiting = true } else { start(a) }
    }

    private func make(_ cue: Cue) -> ActiveCue {
        switch cue.type {
        case .audio: return AudioActive(cue: cue, engine: self)
        case .video: return VideoActive(cue: cue, engine: self)
        case .image, .pdf, .web: return StillActive(cue: cue, engine: self)
        case .fade: return FadeActive(cue: cue, engine: self)
        case .stop: return StopActive(cue: cue, engine: self)
        case .playlist: return PlaylistActive(cue: cue, engine: self)
        case .slideshow: return SlideshowActive(cue: cue, engine: self)
        case .timecode: return TimecodeActive(cue: cue, engine: self)
        case .light: return LightActive(cue: cue, engine: self)
        case .input: return InputActive(cue: cue, engine: self)
        case .osc, .midi, .goto, .memo, .network: return InstantActive(cue: cue, engine: self)
        }
    }

    private func start(_ a: ActiveCue) {
        a.cue.isPreWaiting = active.contains { $0.cue === a.cue && $0 !== a && !$0.started }
        do {
            try a.begin()
        } catch {
            a.cue.error = error.localizedDescription
            onLog?(L("Cue {0}: {1}", a.cue.number, error.localizedDescription))
            if !a.done { a.stop(fade: 0) }
            if !a.done { forceRemove(a) }
        }
        if a.cue.continueMode == .autoContinue { fireNext(after: a.cue) }
    }

    private func completed(_ a: ActiveCue) {
        active.removeAll { $0 === a }
        a.cue.activeCount = max(0, a.cue.activeCount - 1)
        if !a.cue.isRunning { a.cue.progress = 0; a.cue.isPreWaiting = false }
        if a.started && !a.wasStopped && a.cue.continueMode == .autoFollow { fireNext(after: a.cue) }
    }

    private func forceRemove(_ a: ActiveCue) {
        a.onCompleted = nil
        if active.contains(where: { $0 === a }) {
            active.removeAll { $0 === a }
            a.cue.activeCount = max(0, a.cue.activeCount - 1)
        }
    }

    private func fireNext(after cue: Cue) {
        let list = cueList()
        guard let i = list.firstIndex(where: { $0 === cue }) else { return }
        if let next = list[(i + 1)...].first(where: { !$0.disabled }) { fire(next) }
    }

    /// Indeksen GO-markøren skal til etter at cuen på `fired` er avfyrt (hopper over kjeder og deaktiverte).
    static func nextPlayheadIndex(_ list: [Cue], fired: Int) -> Int {
        var i = fired
        while i < list.count && list[i].continueMode != .none {
            i += 1
            while i < list.count && list[i].disabled { i += 1 }
        }
        i += 1
        while i < list.count && list[i].disabled { i += 1 }
        return min(i, list.count)
    }

    func findActive(_ number: String) -> [ActiveCue] {
        let n = number.trimmingCharacters(in: .whitespaces)
        guard !n.isEmpty else { return [] }
        return active.filter { $0.started && !$0.done && $0.cue.number.caseInsensitiveCompare(n) == .orderedSame }
    }

    func stop(_ cue: Cue, fade: Double) {
        active.filter { $0.cue === cue }.forEach { $0.stop(fade: fade) }
    }

    // MARK: Pause og slipp loop

    func pauseAll() {
        guard !isPaused else { return }
        pausedAt = CACurrentMediaTime()
        isPaused = true
        active.filter { $0.started && !$0.done }.forEach { $0.pause() }
    }

    func resumeAll() {
        guard isPaused else { return }
        pausedTotal += CACurrentMediaTime() - pausedAt
        isPaused = false
        active.filter { $0.started && !$0.done }.forEach { $0.resume() }
    }

    func togglePause() { if isPaused { resumeAll() } else { pauseAll() } }

    /// Slipp loopen i cuen («devamp»). False hvis den ikke looper.
    @discardableResult
    func release(_ cue: Cue) -> Bool {
        active.filter { $0.cue === cue && $0.started && !$0.done }.map { $0.release() }.contains(true)
    }

    func requestGoto(_ target: String, fire: Bool) { onGoto?(target, fire) }

    func stopAll(fade: Double) {
        if isPaused { resumeAll() } // stoppfader skal kunne høres
        preview.stop()
        active.forEach { $0.stop(fade: fade) }
    }

    // MARK: Takt

    private func tick() {
        let t = now
        for a in active {
            if a.done { continue }
            if !a.started && t >= a.startAt { start(a) }
            if a.done { continue }
            a.tick()
            let p = a.progress
            if abs(a.cue.progress - p) > 0.002 { a.cue.progress = p }
        }
        onTick?()
    }
}
