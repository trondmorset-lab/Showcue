import Foundation
import AVFoundation
import Combine

/// En cue som er avfyrt: venter (pre-wait), spiller, eller holder på å fade ut.
class ActiveCue: ObservableObject, Identifiable {
    let id = UUID()
    let cue: Cue
    unowned let engine: CueEngine
    var startAt: Double = 0
    /// Start så mange sekunder inn i cuen (prøver: «spill fra her»).
    var startOffset: Double = 0

    @Published private(set) var started = false
    @Published var elapsed: Double = 0
    @Published var duration: Double = 0
    @Published var isStopping = false
    private(set) var done = false
    /// Stoppet manuelt eller av stopp-/fade-cue (utløser ikke auto-follow).
    var wasStopped = false
    var onCompleted: ((ActiveCue) -> Void)?

    init(cue: Cue, engine: CueEngine) {
        self.cue = cue
        self.engine = engine
    }

    var progress: Double { duration > 0 ? min(1, max(0, elapsed / duration)) : 0 }
    var elapsedText: String { started ? TimeFormat.format(elapsed) : "-" + TimeFormat.format(max(0, startAt - engine.now)) }
    var remainingText: String { started ? (duration > 0 ? "-" + TimeFormat.format(max(0, duration - elapsed)) : "∞") : "" }
    var stateText: String { !started ? L("Pre-wait") : isStopping ? L("Fader ut") : L("Spiller") }

    func begin() throws {
        started = true
        try onBegin()
    }

    func onBegin() throws {}
    /// Ca. 60 ganger i sekundet.
    func tick() { if !started { objectWillChange.send() } }

    func stop(fade: Double) {
        wasStopped = true
        if !started { complete() }
    }

    func applyFade(_ fade: Cue) {}

    /// Pause / fortsett (tiden for pre-wait og fader står stille av seg selv, se `CueEngine.pauseAll`).
    func pause() {}
    func resume() {}

    /// Slipp loopen («devamp»): spill ferdig og fortsett til slutten. False hvis cuen ikke looper.
    func release() -> Bool { false }

    // MARK: Spoling (lyd og video som spiller)

    /// Kan spoles i nå (lyd og video som spiller og ikke er på vei ut).
    var canSeek: Bool { false }
    /// Lengden som kan spoles i (sekunder).
    var seekLength: Double { duration }
    /// Posisjonen i runden (sekunder fra startpunktet).
    var seekPosition: Double { elapsed }
    func seek(to seconds: Double) {}

    private var scrubTarget: Double?
    private var scrubAt = Date.distantPast

    /// For spolelinja: posisjonen nå. Rett etter en spoling vises målet, så håndtaket ikke hopper tilbake.
    var scrubPosition: Double {
        if let t = scrubTarget, Date().timeIntervalSince(scrubAt) < 0.3 { return t }
        return seekPosition
    }

    func scrub(to seconds: Double) {
        guard canSeek, abs(seconds - seekPosition) > 0.05 else { return }
        seek(to: seconds)
        scrubTarget = seconds
        scrubAt = Date()
        objectWillChange.send()
    }

    /// Hopp fram eller tilbake (sekunder).
    func nudge(_ seconds: Double) {
        guard canSeek else { return }
        let to = seekPosition + seconds
        seek(to: seekLength > 0 ? min(max(0, to), max(0, seekLength - 0.1)) : max(0, to))
    }

    func complete() {
        guard !done else { return }
        done = true
        onComplete()
        onCompleted?(self)
    }

    func onComplete() {}

    /// Endringer i routing og nivå høres med en gang (nyttig under lydsjekk).
    func followLiveChanges(of playback: @escaping () -> Playback?) -> Set<AnyCancellable> {
        [
            cue.$routes.dropFirst().sink { [weak self] _ in
                guard let self, let pb = playback() else { return }
                DispatchQueue.main.async { pb.setRouting(self.cue.effectiveRoutes(fileChannels: pb.fileChannels)) }
            },
            cue.$volumeDb.dropFirst().sink { [weak self] db in
                guard let self, !self.isStopping else { return }
                playback()?.adjustLevel(db)
            },
        ]
    }
}

// MARK: - Lyd og live-inngang

/// Felles for cuer som spilles som én avspilling gjennom lydpatchen (lyd og live-inngang):
/// stopp med fade, fade-cuer, pause, endringer i nivå/routing mens den spiller, og opprydding.
class PlaybackActive: ActiveCue {
    var playback: Playback?
    private var subs: Set<AnyCancellable> = []

    /// Lag avspillingen (startes av onBegin).
    func makePlayback() throws -> Playback { fatalError("overstyres") }

    override func onBegin() throws {
        let pb = try makePlayback()
        playback = pb
        pb.onFinished = { [weak self] in self?.complete() }
        if cue.fileChannels != pb.fileChannels { cue.fileChannels = pb.fileChannels }
        pb.start()
        subs = followLiveChanges { [weak self] in self?.playback }
    }

    override func stop(fade: Double) {
        super.stop(fade: fade)
        guard let pb = playback, !done else { return }
        isStopping = true
        pb.stop(fade: fade)
        if fade <= 0 { complete() }
    }

    override func applyFade(_ f: Cue) {
        guard let pb = playback else { return }
        if f.fadeAudio {
            if f.stopWhenDone { wasStopped = true; isStopping = true }
            pb.fadeTo(f.targetDb, seconds: f.fadeTime, stopWhenDone: f.stopWhenDone, curve: f.curve)
        } else if f.stopWhenDone {
            stop(fade: 0)
        }
    }

    override func pause() { playback?.pause() }
    override func resume() { playback?.resume() }

    override func onComplete() {
        subs.removeAll()
        playback?.cleanup()
    }
}

final class AudioActive: PlaybackActive {
    override func makePlayback() throws -> Playback {
        try CueError.check(cue.filePath)
        let pb = try engine.audio.makePlayback(audio: cue, offset: startOffset)
        duration = cue.loop ? 0 : (pb.passLength ?? 0)
        if cue.fileDuration <= 0, let len = pb.passLength, cue.endTime <= 0 { cue.fileDuration = cue.startTime + len }
        return pb
    }

    override func tick() {
        super.tick()
        guard let pb = playback, started, !done else { return }
        elapsed = cue.loop ? pb.elapsed : pb.passPosition
    }

    override var canSeek: Bool { started && !done && !isStopping && (playback?.seekLength ?? 0) > 0 }
    override var seekLength: Double { playback?.seekLength ?? 0 }
    override var seekPosition: Double { playback?.passPosition ?? 0 }
    override func seek(to seconds: Double) { playback?.seek(to: seconds) }

    override func release() -> Bool {
        guard let pb = playback, pb.isLooping else { return false }
        pb.releaseLoop()
        duration = 0 // ukjent til fila er ferdig
        return true
    }
}

enum CueError: LocalizedError {
    case fileMissing(String)
    case noFile
    var errorDescription: String? {
        switch self {
        case .fileMissing(let p): return L("Fila finnes ikke: {0}", p)
        case .noFile: return L("Ingen fil valgt")
        }
    }

    /// Kast riktig feil hvis cuen ikke har en fil som finnes.
    static func check(_ path: String) throws {
        if path.isEmpty { throw CueError.noFile }
        if !FileManager.default.fileExists(atPath: path) { throw CueError.fileMissing(path) }
    }
}

// MARK: - Visuelt (felles)

class VisualActive: ActiveCue {
    var layers: VisualLayers?
    /// Utgangen (skjermen) denne cuen vises på.
    private(set) var out: OutputCompositor!
    var startedAt: Double = 0
    private var finishAt: Double?

    func makeLayers() throws -> VisualLayers { fatalError("overstyres") }

    override func onBegin() throws {
        if cue.type == .web {
            guard cue.webURL != nil else { throw NSError(domain: "ShowCue.Web", code: 1, userInfo: [NSLocalizedDescriptionKey: L("Ugyldig adresse: {0}", cue.url)]) }
        } else { try CueError.check(cue.filePath) }
        out = engine.output(forScreen: cue.outputScreen)
        if cue.replaceOthers {
            // Krysstoning: de andre på samme utgang fades ut mens denne fades inn.
            for other in engine.active.compactMap({ $0 as? VisualActive })
            where other !== self && other.out === out && other.started && !other.done && !other.isStopping {
                other.stop(fade: cue.fadeIn > 0 ? cue.fadeIn : other.cue.fadeOut)
            }
        }
        let l = try makeLayers()
        layers = l
        startedAt = engine.now
        l.setOpacity(1, duration: cue.fadeIn)
    }

    override func tick() {
        super.tick()
        if let t = finishAt, engine.now >= t { finish() }
    }

    override func stop(fade: Double) {
        super.stop(fade: fade)
        guard started, !done else { return }
        isStopping = true
        layers?.setOpacity(0, duration: fade)
        onStopping(fade)
        if fade <= 0 { finish() } else { finishAt = engine.now + fade }
    }

    func onStopping(_ fade: Double) {}

    override func applyFade(_ f: Cue) {
        guard let l = layers, !done else { return }
        if f.fadeVisual { l.setOpacity(Float(f.targetOpacity / 100), duration: f.fadeTime, curve: f.curve) }
        if f.stopWhenDone {
            wasStopped = true
            isStopping = true
            finishAt = engine.now + f.fadeTime
        }
    }

    func finish() {
        guard !done else { return }
        finishAt = nil
        onRemove()
        layers?.remove()
        complete()
    }

    func onRemove() {}
}

// MARK: - Bilde og PDF

final class StillActive: VisualActive {
    override func makeLayers() throws -> VisualLayers {
        if cue.type == .web, let url = cue.webURL {
            duration = cue.duration
            let c = cue
            return out.addWeb(url) { msg in c.error = L("Nettside: ") + msg }
        }
        let page = cue.type == .pdf ? cue.page : 0
        var image = MediaCache.shared.cached(path: cue.filePath, page: page)
        if image == nil { image = MediaCache.shared.image(path: cue.filePath, page: page) }
        guard let img = image else {
            throw CueError.fileMissing(cue.type == .pdf ? L("{0} (side {1})", cue.filePath, cue.page) : cue.filePath)
        }
        duration = cue.duration
        return out.addImage(img, stretch: cue.stretch)
    }

    override func tick() {
        super.tick()
        guard started, !done else { return }
        elapsed = engine.now - startedAt
        if cue.holdTime > 0, !isStopping, elapsed >= cue.fadeIn + cue.holdTime {
            stop(fade: cue.fadeOut)
            wasStopped = false // naturlig slutt → auto-follow skal virke
        }
    }
}

// MARK: - Video

/// Bildet spilles av AVPlayer (dempet). Lyden pakkes ut og spilles gjennom lydpatchen, og er «master»:
/// glir bildet mer enn 0,12 s fra lyden (f.eks. fordi lydkortets klokke og Mac-klokka går litt ulikt), justeres bildet.
final class VideoActive: VisualActive {
    private var player: AVPlayer?
    private var audio: Playback?
    private var audioStarted = false
    private var statusObs: NSKeyValueObservation?
    private var endObs: NSObjectProtocol?
    private var lastResync: Double = 0
    private var lastLoop: Double = -10
    private var seekedToStart = false
    private var released = false
    /// Hvor i fila avspillingen starter (startpunkt + eventuell «spill fra her»). Loopen går også hit.
    private var startTime: Double = 0
    private var endTime: Double = 0
    private var subs: Set<AnyCancellable> = []
    static let maxDrift = 0.12

    override func makeLayers() throws -> VisualLayers {
        let item = AVPlayerItem(url: URL(fileURLWithPath: cue.filePath))
        startTime = max(0, cue.startTime) + startOffset
        endTime = cue.endTime > startTime ? cue.endTime : 0
        if endTime > 0 { item.forwardPlaybackEndTime = CMTime(seconds: endTime, preferredTimescale: 600) }
        let p = AVPlayer(playerItem: item)
        p.isMuted = true
        p.volume = 0
        p.actionAtItemEnd = .pause
        player = p

        if !(cue.routes?.isEmpty ?? false) {
            audio = engine.audio.makePlayback(videoAudio: cue, start: startTime)
            if let a = audio { if cue.fileChannels != a.fileChannels { cue.fileChannels = a.fileChannels } }
            else if engine.audio.isRunning, cue.fileChannels != 0 { cue.fileChannels = 0 }
        }

        statusObs = item.observe(\.status, options: [.new]) { [weak self] item, _ in
            DispatchQueue.main.async { self?.itemStatusChanged(item) }
        }
        endObs = NotificationCenter.default.addObserver(forName: .AVPlayerItemDidPlayToEndTime, object: item, queue: .main) { [weak self] _ in
            self?.reachedEnd()
        }
        subs = followLiveChanges { [weak self] in self?.audio }

        if startTime == 0 { p.play() } // ellers: spol først når fila er klar (itemStatusChanged)
        return out.addVideo(p, stretch: cue.stretch)
    }

    private func itemStatusChanged(_ item: AVPlayerItem) {
        guard !done else { return }
        switch item.status {
        case .readyToPlay:
            let d = item.duration.seconds
            if d.isFinite && d > 0 { cue.knownDuration = d }
            let end = endTime > 0 ? (d.isFinite && d > 0 ? min(endTime, d) : endTime) : (d.isFinite ? d : 0)
            duration = cue.loop || end <= 0 ? 0 : max(0, end - startTime)
            if startTime > 0, let p = player, !seekedToStart {
                seekedToStart = true
                p.seek(to: CMTime(seconds: startTime, preferredTimescale: 600), toleranceBefore: .zero, toleranceAfter: .zero) { [weak self] _ in
                    DispatchQueue.main.async {
                        guard let self, !self.done else { return }
                        self.player?.play()
                        self.startAudio() // bilde og lyd starter samtidig
                    }
                }
            } else {
                startAudio() // bilde og lyd starter samtidig
            }
        case .failed:
            cue.error = "Video: " + (item.error?.localizedDescription ?? L("ukjent feil"))
            finish()
        default: break
        }
    }

    private func startAudio() {
        guard let a = audio, !audioStarted, !done else { return }
        audioStarted = true
        a.start()
        lastResync = engine.now
    }

    private func reachedEnd() {
        guard !done else { return }
        if cue.loop && !isStopping && !released {
            guard engine.now - lastLoop > 0.3 else { return } // både sluttvarsel og takt kan melde samme slutt
            lastLoop = engine.now
            player?.seek(to: CMTime(seconds: startTime, preferredTimescale: 600), toleranceBefore: .zero, toleranceAfter: .zero)
            player?.play()
            // Lyden looper selv ved sluttpunktet; spol bare tilbake hvis den ikke allerede har startet en ny runde.
            if let a = audio, a.passPosition > 1.0 { a.requestRewind() }
            lastResync = engine.now
        } else {
            finish()
        }
    }

    override func tick() {
        super.tick()
        guard let p = player, started, !done else { return }
        let pos = p.currentTime().seconds
        if pos.isFinite {
            elapsed = max(0, pos - startTime)
            // Reserve hvis sluttvarselet ikke kommer (f.eks. etter spoling).
            if endTime > 0, pos >= endTime - 0.005, cue.loop || !isStopping { reachedEnd(); return }
        }

        if let a = audio, audioStarted, !isStopping, engine.now - lastResync > 2 {
            let audioPos = a.passPosition
            if audioPos > 0.5, abs(audioPos - elapsed) > VideoActive.maxDrift, duration == 0 || audioPos < duration - 1 {
                p.seek(to: CMTime(seconds: startTime + audioPos, preferredTimescale: 600), toleranceBefore: .zero, toleranceAfter: .zero)
                lastResync = engine.now
            }
        }

        // Automatisk fade ut mot slutten.
        if !cue.loop, !isStopping, cue.fadeOut > 0, duration > 0, duration - elapsed <= cue.fadeOut {
            stop(fade: max(0.05, duration - elapsed))
            wasStopped = false
        }
    }

    override func onStopping(_ fade: Double) { audio?.stop(fade: fade) }

    override func applyFade(_ f: Cue) {
        super.applyFade(f)
        if f.fadeAudio { audio?.fadeTo(f.targetDb, seconds: f.fadeTime, stopWhenDone: false, curve: f.curve) }
    }

    override func pause() { player?.pause(); audio?.pause() }
    override func resume() { player?.play(); audio?.resume(); lastResync = engine.now }

    override var canSeek: Bool { player != nil && started && !done && !isStopping && seekLength > 0 }
    override var seekLength: Double {
        if duration > 0 { return duration }
        let end = endTime > 0 ? endTime : cue.knownDuration
        return end > startTime ? end - startTime : 0
    }
    override func seek(to seconds: Double) {
        guard let p = player, seconds.isFinite else { return }
        let s = min(max(0, seconds), max(0, seekLength - 0.05))
        p.seek(to: CMTime(seconds: startTime + s, preferredTimescale: 600), toleranceBefore: .zero, toleranceAfter: .zero)
        audio?.seek(to: s)
        lastResync = engine.now // gi bilde og lyd tid til å finne hverandre igjen
        elapsed = s
    }

    override func release() -> Bool {
        guard cue.loop, !released, !isStopping else { return false }
        released = true
        endTime = 0 // sluttpunktet gjelder ikke lenger: spill resten av fila
        player?.currentItem?.forwardPlaybackEndTime = .invalid
        audio?.releaseLoop()
        if cue.knownDuration > 0 { duration = max(0, cue.knownDuration - startTime) }
        return true
    }

    override func onRemove() {
        subs.removeAll()
        statusObs?.invalidate()
        if let o = endObs { NotificationCenter.default.removeObserver(o) }
        audio?.stop(fade: 0)
        audio?.cleanup()
        player?.pause()
        player?.replaceCurrentItem(with: nil)
    }
}

// MARK: - Fade og stopp

/// Fade- og stopp-cue: gjør jobben ved start og «varer» så lenge faden tar.
class TimedActive: ActiveCue {
    private var begin: Double = 0

    func act() {}
    /// Hvor lenge cuen varer (stopp-cue som slipper en loop er ferdig med en gang).
    var seconds: Double { cue.duration }

    override func onBegin() throws {
        begin = engine.now
        duration = seconds
        act()
        if seconds <= 0 { complete() }
    }

    override func tick() {
        super.tick()
        guard started, !done else { return }
        elapsed = engine.now - begin
        if elapsed >= seconds { complete() }
    }

    override func stop(fade: Double) { super.stop(fade: fade); complete() }
}

final class FadeActive: TimedActive {
    override func act() {
        let targets = engine.findActive(cue.target)
        if targets.isEmpty { cue.error = cue.target.isEmpty ? L("Mangler mål-cue") : L("Cue {0} kjører ikke", cue.target) }
        targets.forEach { $0.applyFade(cue) }
    }
}

/// Stopp-cue: stopper målet (eller alt) med fade – eller slipper loopen («devamp»).
final class StopActive: TimedActive {
    override func act() {
        let targets = cue.target.isEmpty
            ? engine.active.filter { $0 !== self && !($0 is StopActive) }
            : engine.findActive(cue.target)
        guard cue.release else { targets.forEach { $0.stop(fade: cue.fadeTime) }; return }
        if !targets.map({ $0.release() }).contains(true) {
            cue.error = cue.target.isEmpty ? L("Ingen cue looper") : L("Cue {0} looper ikke", cue.target)
        }
    }
}

// MARK: - Spilleliste

/// Spor etter spor (eller tilfeldig), med krysstoning, gjennom lydpatchen.
final class PlaylistActive: ActiveCue {
    private var playing: [Playback] = []
    private var order: [String] = []
    private var index = -1
    private var current: Playback?
    private var releasing = false
    private var begin: Double = 0
    private var subs: Set<AnyCancellable> = []

    private func newOrder() -> [String] {
        let files = cue.files.filter { FileManager.default.fileExists(atPath: $0) }
        return cue.shuffle ? files.shuffled() : files
    }

    override func onBegin() throws {
        order = newOrder()
        guard !order.isEmpty else { throw CueError.fileMissing(L("(spillelista har ingen filer som finnes)")) }
        begin = engine.now
        subs = followLiveChanges { [weak self] in self?.current }
        try startNext(fadeIn: 0)
    }

    /// Start neste spor (med fade inn). False når lista er ferdig.
    @discardableResult
    private func startNext(fadeIn: Double) throws -> Bool {
        index += 1
        if index >= order.count {
            guard cue.loop, !releasing else { current = nil; return false }
            order = newOrder()
            index = 0
            guard !order.isEmpty else { current = nil; return false }
        }
        let pb = try engine.audio.makePlayback(track: order[index], for: cue, fadeIn: fadeIn,
                                               offset: startOffset > 0 && playing.isEmpty && index == 0 ? startOffset : 0)
        pb.onFinished = { [weak self, weak pb] in self?.playing.removeAll { $0 === pb } }
        playing.append(pb)
        current = pb
        pb.start()
        objectWillChange.send()
        return true
    }

    override func tick() {
        super.tick()
        guard started, !done else { return }
        elapsed = engine.now - begin
        if playing.allSatisfy({ $0.voice.isFinished }) && (current == nil || isStopping) { complete(); return }
        guard !isStopping, let cur = current else { return }

        let left = (cur.passLength ?? 0) - cur.passPosition
        do {
            if !releasing, cue.crossfade > 0, (cur.passLength ?? 0) > 0, left <= cue.crossfade, !cur.voice.isFinished {
                cur.fadeTo(Voice.silenceDb, seconds: max(0.05, left), stopWhenDone: true)
                try startNext(fadeIn: cue.crossfade)
            } else if cur.voice.isFinished {
                try startNext(fadeIn: 0)
            }
        } catch {
            cue.error = error.localizedDescription
            current = nil
        }
    }

    override func stop(fade: Double) {
        super.stop(fade: fade)
        guard started, !done else { return }
        isStopping = true
        playing.forEach { $0.stop(fade: fade) }
        if fade <= 0 { complete() }
    }

    override func applyFade(_ f: Cue) {
        guard f.fadeAudio else { if f.stopWhenDone { stop(fade: 0) }; return }
        if f.stopWhenDone { wasStopped = true; isStopping = true }
        playing.forEach { $0.fadeTo(f.targetDb, seconds: f.fadeTime, stopWhenDone: f.stopWhenDone, curve: f.curve) }
    }

    override func pause() { playing.forEach { $0.pause() } }
    override func resume() { playing.forEach { $0.resume() } }

    /// For en spilleliste betyr «slipp»: spill ferdig sporet som går, og stopp.
    override func release() -> Bool {
        guard !releasing, !isStopping else { return false }
        releasing = true
        return true
    }

    override func onComplete() {
        subs.removeAll()
        playing.forEach { $0.stop(fade: 0) }
        playing.removeAll()
    }
}

// MARK: - OSC og MIDI (umiddelbare)

final class InstantActive: ActiveCue {
    override func onBegin() throws {
        defer { complete() }
        switch cue.type {
        case .osc:
            guard OscSender.shared.send(host: cue.host, port: cue.port, address: cue.address.isEmpty ? "/" : cue.address,
                                        args: Osc.parseArguments(cue.arguments)) else {
                throw NSError(domain: "ShowCue.OSC", code: 1, userInfo: [NSLocalizedDescriptionKey: L("Kunne ikke sende til {0}:{1}", cue.host, cue.port)])
            }
        case .midi:
            try engine.midi.send(MidiOut.bytes(for: cue), device: cue.device)
        case .goto:
            engine.requestGoto(cue.target, fire: cue.fire)
        case .memo:
            engine.onMemo?(cue)
        case .network:
            // Sendes i bakgrunnen så GO aldri venter på nettverket. Feil vises på cuen.
            let c = cue
            let (p, host, port, msg, url, method) = (c.networkProtocol, c.host, c.port, c.message, c.url, c.method)
            Task { @MainActor in
                if let err = await NetworkSender.send(protocol: p, host: host, port: port, message: msg, url: url, method: method) {
                    c.error = L("Nettverk: ") + err
                }
            }
        default: break
        }
    }
}

// MARK: - Tidskode, lys og live-inngang

/// Tidskode-cue: MTC til en MIDI-utgang og/eller LTC som lyd, fra starttiden.
final class TimecodeActive: ActiveCue {
    private var ltc: LtcSource?
    private var mtc: MtcSender?
    private var begin = 0.0

    override func onBegin() throws {
        guard cue.sendMtc || cue.sendLtc else {
            throw NSError(domain: "ShowCue.TC", code: 1, userInfo: [NSLocalizedDescriptionKey: L("Velg MTC og/eller LTC")])
        }
        let start = Timecode.fromSeconds(cue.startTimecode.toSeconds(cue.rate) + startOffset, cue.rate)
        if cue.sendLtc {
            guard let m = engine.audio.mixer else {
                throw NSError(domain: "ShowCue.Audio", code: -20, userInfo: [NSLocalizedDescriptionKey: L("Lydutgangen er ikke startet. Sjekk innstillinger.")])
            }
            let src = LtcSource(busIndex: cue.ltcBus - 1, levelDb: cue.ltcLevelDb, start: start, rate: cue.rate, sampleRate: m.sampleRate)
            m.add(src)
            ltc = src
        }
        if cue.sendMtc {
            let midi = engine.midi, device = cue.device
            mtc = MtcSender(send: { try? midi.send($0, device: device) }, start: start, rate: cue.rate)
        }
        begin = engine.now
        duration = cue.length
    }

    override func tick() {
        super.tick()
        guard started, !done else { return }
        elapsed = engine.now - begin
        if cue.length > 0 && elapsed >= cue.length { complete() }
    }

    override func pause() { ltc?.pause(); mtc?.pause() }
    override func resume() { ltc?.resume(); mtc?.resume() }
    override func stop(fade: Double) { super.stop(fade: fade); complete() }

    override func onComplete() {
        ltc?.stop()
        mtc?.stop()
    }
}

/// Lys-cue: fader DMX-kanaler over Art-Net. Å stoppe cuen lar lyset stå (som på et lysbord).
final class LightActive: TimedActive {
    override func onBegin() throws {
        let levels = try DmxLevels.parse(cue.levels)
        guard !cue.host.trimmingCharacters(in: .whitespaces).isEmpty else {
            throw NSError(domain: "ShowCue.DMX", code: 1, userInfo: [NSLocalizedDescriptionKey: L("Mangler IP-adresse for Art-Net")])
        }
        engine.dmx.fade(host: cue.host.trimmingCharacters(in: .whitespaces), universe: cue.universe, to: levels, seconds: cue.fadeTime)
        try super.onBegin()
    }
}

/// Live-inngang gjennom lydpatchen (mikrofon, linje inn).
final class InputActive: PlaybackActive {
    private var begin = 0.0

    override func makePlayback() throws -> Playback {
        guard let m = engine.audio.mixer else {
            throw NSError(domain: "ShowCue.Audio", code: -20, userInfo: [NSLocalizedDescriptionKey: L("Lydutgangen er ikke startet. Sjekk innstillinger.")])
        }
        let ring: LiveRing
        do { ring = try engine.input.open(deviceUID: engine.inputDeviceUID, outputRate: m.sampleRate) }
        catch { throw NSError(domain: "ShowCue.Input", code: 1, userInfo: [NSLocalizedDescriptionKey: L("Kunne ikke åpne inngangen: {0}", error.localizedDescription)]) }
        let input = engine.input
        begin = engine.now
        return try engine.audio.makePlayback(live: ring, for: cue) { [weak input] r in input?.close(r) }
    }

    override func tick() {
        super.tick()
        if started && !done { elapsed = engine.now - begin }
    }
}
