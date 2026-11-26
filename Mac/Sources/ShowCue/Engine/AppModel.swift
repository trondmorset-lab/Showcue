import Foundation
import AppKit
import SwiftUI
import AVFoundation
import Combine
import UniformTypeIdentifiers

/// Hele programmets tilstand: showet, utvalg/GO-markør, innstillinger, motorer og fjernstyring.
final class AppModel: ObservableObject, ShowControl {
    static let audioExt = ["wav", "mp3", "aif", "aiff", "aifc", "flac", "m4a", "aac", "caf", "wma"]
    static let videoExt = ["mp4", "mov", "m4v", "avi", "mkv", "wmv"]
    static let imageExt = ["png", "jpg", "jpeg", "bmp", "gif", "tif", "tiff", "heic", "webp"]
    static let showType = UTType(filenameExtension: "showcue") ?? .json

    // Show
    @Published var cues: [Cue] = [] { didSet { observeCues(); updatePreload() } }
    @Published var selection: Set<UUID> = [] { didSet { recordPlayhead(); updatePreload() } }
    /// Scenenavnene i rekkefølge. Tom = én scene uten navn (`SceneLogic`).
    @Published var scenes: [String] = []
    /// Aktiv scene (kanonisk navn; tom i et show uten scener). Cuelista viser bare cuene i den.
    @Published private(set) var activeScene = ""
    /// GO-markøren per scene (`endMarker` = slutten av scenen).
    private var playheads: [String: UUID] = [:]
    private static let endMarker = UUID(uuidString: "00000000-0000-0000-0000-000000000000")!
    /// Fanen som vises i inspektøren.
    @Published var inspectorTab: InspectorTab = .basic
    @Published private(set) var showURL: URL?
    @Published private(set) var isDirty = false
    private var savedData = Data()
    /// Navngitte routingforhåndsvalg i showet (lagres i showfila, ikke i angre-historikken).
    @Published var routingPresets: [RoutingPreset] = []
    /// Routing som er kopiert fra en cue.
    @Published var routeClipboard = RouteClipboard()

    // Status
    @Published var statusText = ""
    /// Teksten fra siste memo-cue (tom = ingen beskjed vises).
    @Published var memoText = ""
    private var panelWindow: NSWindow?
    private var preflightWindow: NSWindow?
    private var loudnessTask: Task<Void, Never>?
    @Published var learnText = ""
    @Published private(set) var remoteStatus = ""
    /// Ting som endres mange ganger i sekundet (egne objekt, så ikke hele vinduet tegnes på nytt).
    let live = LiveState()
    /// Cuen som venter på at brukeren trykker en hurtigtast.
    @Published var hotkeyCapture: Cue?
    /// Cuen som vises i det store start/slutt-vinduet.
    @Published var trimCue: Cue?

    // Innstillinger
    @Published var settings: AppSettings {
        didSet {
            if settings.theme != oldValue.theme || settings.zoom != oldValue.zoom || settings.listTextScale != oldValue.listTextScale {
                settings.save()
            }
        }
    }

    // Motorer
    let audio = AudioEngine()
    let output = OutputCompositor()
    let midi = MidiSystem()
    private(set) var engine: CueEngine!
    let router: RemoteRouter
    private let oscServer = OscServer()
    private var feedback: OscFeedback?
    private var tickCount = 0
    private var lastGo = Date.distantPast
    private var lastPanic = Date.distantPast
    private var cueSubs: [AnyCancellable] = []

    // Angre og showmodus
    private let history = UndoHistory()
    @Published private(set) var canUndo = false
    @Published private(set) var canRedo = false
    /// Showmodus: bare GO, stopp og hurtigtaster virker – ingenting kan endres ved et uhell.
    @Published var showMode = false {
        didSet {
            guard showMode != oldValue else { return }
            NSApp.keyWindow?.makeFirstResponder(nil)
            if showMode { engine.preview.stop() }
            refreshUndo()
            statusText = showMode ? L("Showmodus: redigering er låst (⌘L for å låse opp).") : L("Redigering er låst opp.")
        }
    }
    var isEditable: Bool { !showMode }
    /// Øvelsesmodus: cuer merket «Hopp over i øvelse» hoppes over av GO og auto-fortsett/-følg. Lagres ikke; programmet starter alltid med den av.
    @Published var rehearsalMode = false {
        didSet {
            guard rehearsalMode != oldValue else { return }
            engine.rehearsalMode = rehearsalMode
            let n = Rehearsal.countFlagged(cues)
            statusText = rehearsalMode ? L("Øvelsesmodus er PÅ: {0} cue(r) hoppes over av GO (⌘R for å slå av).", n) : L("Øvelsesmodus er av: alle cuer spilles.")
        }
    }
    private var subs: Set<AnyCancellable> = []

    init() {
        let loaded = AppSettings.load()
        settings = loaded
        router = RemoteRouter(settings: loaded)
        engine = CueEngine(audio: audio, output: output, midi: midi) { [weak self] in self?.cues ?? [] }
        router.show = self
        engine.onLog = { [weak self] in self?.statusText = $0 }
        engine.onPreloadRefresh = { [weak self] in DispatchQueue.main.async { self?.updatePreload() } }
        engine.onTick = { [weak self] in self?.tick() }
        // «Gå til»-cue: vent til GO er ferdig med å flytte markøren, så flytt den til målet.
        engine.onGoto = { [weak self] target, fire in
            DispatchQueue.main.async {
                guard let self else { return }
                guard let t = SceneLogic.resolveGoto(self.cues, scenes: self.scenes, spec: target, home: self.activeScene) else {
                    self.statusText = L("Gå til: fant ikke cue {0}.", target); return
                }
                if let c = t.cue { self.select(c) } else { self.switchScene(t.scene) }
                if fire { self.lastGo = .distantPast; self.go() }
            }
        }
        engine.onMemo = { [weak self] m in
            DispatchQueue.main.async {
                let t = m.text.trimmingCharacters(in: .whitespacesAndNewlines)
                self?.memoText = t.isEmpty ? m.displayName : t
            }
        }
        engine.objectWillChange.sink { [weak self] _ in self?.objectWillChange.send() }.store(in: &subs)
        audio.$status.sink { [weak self] s in self?.statusText = s }.store(in: &subs)
        oscServer.onMessage = { [weak self] m, from in self?.router.handleOsc(m, from: from) }
        midi.onTrigger = { [weak self] t, ch in self?.router.handleMidiTrigger(t, channel: ch) }
        midi.onMsc = { [weak self] m in self?.router.handleMsc(m) }
        midi.onTimecode = { [weak self] tc, r in self?.router.handleTimecode(tc, rate: r) }
        router.onTimecodeStatus = { [weak self] _ in self?.applyStatusLine() }
        engine.dmx.onError = { [weak self] e in DispatchQueue.main.async { self?.statusText = e } }
        newShow(confirm: false)
    }

    /// Startes når vinduet er vist.
    func startup() {
        audio.start(settings)
        engine.configurePreview(settings)
        engine.inputDeviceUID = settings.inputDeviceUID
        engine.setDefaultScreen(settings.videoScreen)
        if settings.videoScreen >= 0 { output.ensureVisible() }
        applyRemoteSettings()
        if !settings.lastShowPath.isEmpty, FileManager.default.fileExists(atPath: settings.lastShowPath) {
            open(URL(fileURLWithPath: settings.lastShowPath), confirm: false)
        } else {
            offerRecovery()
        }
    }

    // MARK: - Utvalg og GO-markør

    /// GO-markøren: den første valgte cuen i lista.
    var playhead: Cue? { cues.first { selection.contains($0.id) } }
    var selectedCues: [Cue] { cues.filter { selection.contains($0.id) } }
    func index(of c: Cue) -> Int? { cues.firstIndex { $0 === c } }

    /// Velger cuen (GO-markøren). Står cuen i en annen scene, byttes det til den scenen først.
    func select(_ c: Cue?) {
        if let c, !SceneLogic.sameScene(c.scene, activeScene) { activeScene = c.scene }
        selection = c.map { [$0.id] } ?? []
    }

    // MARK: - Scener

    /// Cuene i aktiv scene (det cuelista viser).
    var visibleCues: [Cue] { SceneLogic.cues(in: activeScene, of: cues) }
    /// Navnene i scenevelgeren. Et show uten scener har én scene.
    var sceneItems: [String] { scenes.isEmpty ? [SceneLogic.defaultName] : scenes }
    var activeSceneLabel: String { activeScene.isEmpty ? SceneLogic.defaultName : activeScene }

    private func recordPlayhead() { playheads[activeScene] = playhead?.id ?? AppModel.endMarker }

    /// GO-markøren scenen hadde sist (første aktive cue hvis den ikke har vært brukt).
    private func playhead(for scene: String) -> Cue? {
        guard let id = playheads[scene] else { return SceneLogic.firstEnabled(cues, scene) }
        if id == AppModel.endMarker { return nil }
        if let c = cues.first(where: { $0.id == id }), SceneLogic.inScene(c, scene) { return c }
        return SceneLogic.firstEnabled(cues, scene)
    }

    /// Bytter scene og flytter GO-markøren til den sist brukte i den scenen. GO bytter aldri scene av seg selv.
    func switchScene(_ scene: String) {
        guard !SceneLogic.sameScene(scene, activeScene) else { return }
        activeScene = scene
        select(playhead(for: scene))
        statusText = L("Scene: {0}", activeSceneLabel)
    }

    /// Valg i scenevelgeren (visningsnavn).
    func selectSceneLabel(_ label: String) {
        guard !scenes.isEmpty, let scene = SceneLogic.canonical(scenes, cues, label) else { return }
        switchScene(scene)
    }

    func stepScene(_ delta: Int) {
        if let next = SceneLogic.neighbour(scenes, activeScene, delta) { switchScene(next) }
    }

    @discardableResult
    func selectScene(_ name: String) -> Bool {
        if scenes.isEmpty { return SceneLogic.sameScene(name, SceneLogic.defaultName) }
        guard let scene = SceneLogic.canonical(scenes, cues, name) else { return false }
        switchScene(scene)
        return true
    }

    /// Endrer scenelista og velger aktiv scene etterpå (GO-markøren i scenen beholdes hvis cuen er der fortsatt).
    private func editScenes(_ edit: () -> Void, activeAfter: () -> String) {
        guard isEditable else { return }
        let keep = playhead
        edit()
        activeScene = activeAfter()
        playheads = playheads.filter { key, _ in scenes.contains { SceneLogic.sameScene($0, key) } || (scenes.isEmpty && key.isEmpty) }
        if let keep, cues.contains(where: { $0 === keep }), SceneLogic.inScene(keep, activeScene) { select(keep) }
        else { select(playhead(for: activeScene)) }
        objectWillChange.send()
        updateDirty()
    }

    /// Ber om en tekst (navn på scene) i en liten dialog. nil hvis brukeren avbryter.
    private func askName(_ title: String, _ message: String, initial: String = "") -> String? {
        NSApp.keyWindow?.makeFirstResponder(nil)
        let a = NSAlert()
        a.messageText = title
        a.informativeText = message
        let field = NSTextField(frame: NSRect(x: 0, y: 0, width: 260, height: 24))
        field.stringValue = initial
        a.accessoryView = field
        a.addButton(withTitle: L("OK"))
        a.addButton(withTitle: L("Avbryt"))
        a.window.initialFirstResponder = field
        return a.runModal() == .alertFirstButtonReturn ? field.stringValue : nil
    }

    func addSceneDialog() {
        guard isEditable, let name = askName(L("Ny scene"), L("Navn på scenen (f.eks. Akt 2):")) else { return }
        var added: String?
        editScenes({
            let oldKey = activeScene, oldPlayhead = playhead
            added = SceneLogic.addScene(&scenes, cues, name)
            // Den første scenen får navn: GO-markøren den hadde følger med.
            if oldKey.isEmpty, let first = scenes.first { playheads[""] = nil; playheads[first] = oldPlayhead?.id ?? AppModel.endMarker }
        }, activeAfter: { added ?? (!scenes.isEmpty && activeScene.isEmpty ? scenes[0] : activeScene) })
        statusText = added.map { L("Scene «{0}» er laget.", $0) } ?? L("Kunne ikke lage scenen (tomt navn eller navnet er i bruk).")
    }

    func renameSceneDialog() {
        guard isEditable, let name = askName(L("Gi scenen nytt navn"), L("Nytt navn på scenen:"), initial: activeSceneLabel) else { return }
        let old = activeScene
        var ok = false
        var now = old
        editScenes({
            ok = SceneLogic.renameScene(&scenes, cues, old, name)
            guard ok else { return }
            now = SceneLogic.cleanName(name)
            if let p = playheads.removeValue(forKey: old) { playheads[now] = p }
        }, activeAfter: { now })
        if !ok { statusText = L("Kunne ikke endre navnet (tomt navn eller navnet er i bruk).") }
    }

    func deleteSceneDialog() {
        guard isEditable else { return }
        guard scenes.count >= 2 else { statusText = L("Showet må ha minst én scene."); return }
        let a = NSAlert()
        a.messageText = L("Slette scenen «{0}»?", activeSceneLabel)
        a.informativeText = L("Cuene i den flyttes til scenen foran.")
        a.addButton(withTitle: L("Slett"))
        a.addButton(withTitle: L("Avbryt"))
        guard a.runModal() == .alertFirstButtonReturn else { return }
        let gone = activeScene
        var heir: String?
        editScenes({ heir = SceneLogic.deleteScene(&scenes, cues, gone); playheads[gone] = nil }, activeAfter: { heir ?? gone })
    }

    func moveActiveScene(_ delta: Int) {
        let name = activeScene
        editScenes({ _ = SceneLogic.moveScene(&scenes, name, delta) }, activeAfter: { name })
    }

    /// Flytter de valgte cuene til slutten av en annen scene.
    func moveSelectedToScene(_ scene: String) {
        let sel = selectedCues
        guard isEditable, !sel.isEmpty else { return }
        editScenes({
            var list = cues
            SceneLogic.moveToScene(&list, sel, scene)
            cues = list
        }, activeAfter: { activeScene })
        statusText = L("Flyttet {0} cue(r) til scenen «{1}».", sel.count, scene)
    }

    func movePlayhead(_ delta: Int) {
        let list = visibleCues
        guard !list.isEmpty else { return }
        select(SceneLogic.step(list, from: playhead, delta))
    }

    /// Klikk i lista (med ⌘ for flere, ⇧ for område).
    func click(_ c: Cue, command: Bool, shift: Bool) {
        if command {
            if selection.contains(c.id) { selection.remove(c.id) } else { selection.insert(c.id) }
        } else if shift, let anchor = playhead.flatMap({ p in visibleCues.firstIndex { $0 === p } }), let i = visibleCues.firstIndex(where: { $0 === c }) {
            selection = Set(visibleCues[min(anchor, i)...max(anchor, i)].map(\.id))
        } else {
            select(c)
        }
    }

    // MARK: - GO / stopp

    /// `explicitStart`: cuen er valgt med nummer, så en cue merket «Hopp over i øvelse» startes likevel.
    func go() { go(explicitStart: false) }

    func go(explicitStart: Bool) {
        guard Date().timeIntervalSince(lastGo) * 1000 >= Double(settings.doubleGoProtectionMs) else { return }
        lastGo = Date()
        guard let cue = playhead, let at = index(of: cue) else { return }
        // Deaktiverte cuer (og merkede, i øvelsesmodus) hoppes over: start neste cue som kan spilles.
        let idx = Rehearsal.goIndex(cues, playhead: at, mode: rehearsalMode, explicitStart: explicitStart)
        guard idx >= 0 else { return }
        engine.fire(cues[idx])
        let n = CueEngine.nextPlayheadIndex(cues, fired: idx, rehearsal: rehearsalMode)
        select(n < cues.count ? cues[n] : nil)
        guard var cue = playhead else { return }
        if cue.disabled {
            // Hopp til neste aktive cue i scenen og kjør den.
            guard let next = SceneLogic.nextEnabled(cues, after: cue) else { return }
            cue = next
        }
        engine.fire(cue)
        // GO-markøren flyttes bare innen scenen; slutten av scenen gir «slutt på lista».
        select(SceneLogic.nextPlayhead(cues, after: cue))
    }

    func panic() {
        let hard = Date().timeIntervalSince(lastPanic) < 1
        lastPanic = Date()
        engine.stopAll(fade: hard ? 0 : settings.panicFadeTime)
        statusText = hard ? L("Alt stoppet umiddelbart.")
            : L("Stopper alt (fade {0} s). Trykk Esc igjen for umiddelbar stopp.", Cue.fmt(settings.panicFadeTime))
    }

    func stopAll() { engine.stopAll(fade: settings.panicFadeTime) }
    func hardStop() { engine.stopAll(fade: 0) }

    static func fadeOut(of c: Cue) -> Double {
        if c.type == .slideshow { return c.crossfade }
        return (c.type == .audio || c.type == .playlist || c.isVisual) ? c.fadeOut : 0
    }

    func togglePause() {
        engine.togglePause()
        statusText = engine.isPaused ? L("Pause – alt står stille. ⌘P, Pause-knappen eller GO fortsetter.") : L("Fortsetter.")
    }

    func setPaused(_ paused: Bool?) {
        switch paused {
        case nil: togglePause()
        case true?: if !engine.isPaused { togglePause() }
        case false?: if engine.isPaused { togglePause() }
        }
    }

    @discardableResult
    func releaseCue(_ number: String) -> Bool { find(number).map { engine.release($0) } ?? false }

    /// Bla fram (+1) eller tilbake (−1) i lysbildeserier med dette nummeret som kjører.
    @discardableResult
    func stepCue(_ number: String, _ delta: Int) -> Bool {
        let running = engine.findActive(number).filter(\.canStep)
        running.forEach { $0.step(delta) }
        return !running.isEmpty
    }

    func releaseSelected() {
        guard let c = playhead else { return }
        if !engine.release(c) { statusText = L("Cuen looper ikke (eller spiller ikke).") }
    }

    /// Hurtigtast/MIDI-trigger: lyd og bilde/video startes og stoppes (av/på), resten bare startes.
    func toggleHot(_ c: Cue) {
        if c.isRunning && (c.type == .audio || c.type == .playlist || c.type == .slideshow || c.isVisual) { engine.stop(c, fade: AppModel.fadeOut(of: c)) }
        else { engine.fire(c) }
    }

    func fire(_ c: Cue) { engine.fire(c) }
    func stop(_ c: Cue) { engine.stop(c, fade: AppModel.fadeOut(of: c)) }

    // MARK: - ShowControl (fjernstyring)

    /// Cuen et nummer peker på: aktiv scene først, deretter alle scener; «Scene:nummer» bare i den scenen.
    private func find(_ number: String) -> Cue? { SceneLogic.find(cues, number, home: activeScene) }

    func selectCue(_ number: String) -> Bool { guard let c = find(number) else { return false }; select(c); return true }
    func startCue(_ number: String) -> Bool { guard let c = find(number) else { return false }; engine.fire(c); return true }
    func stopCue(_ number: String) -> Bool { guard let c = find(number) else { return false }; stop(c); return true }
    func goCue(_ number: String) -> Bool {
        guard let c = find(number) else { return false }
        select(c)
        lastGo = .distantPast // eksplisitt cue: ingen dobbel-GO-sperre
        go(explicitStart: true)
        return true
    }
    func fireHotkey(_ key: String) -> Bool {
        guard let c = cues.first(where: { $0.hotkey == key && !$0.disabled }) else { return false }
        toggleHot(c)
        return true
    }
    func fireMidiTrigger(_ trigger: String) -> Int {
        let list = cues.filter { $0.midiTrigger == trigger && !$0.disabled }
        list.forEach(toggleHot)
        return list.count
    }
    func remoteLog(_ text: String) { statusText = text }
    func refreshFeedback() { feedback?.requestRefresh() }

    func fireTimecodeTriggers(from previous: Int, to frame: Int, rate: TimecodeRate) -> Int {
        // Triggere starter av seg selv, så merkede cuer hoppes over i øvelsesmodus.
        let due = cues.filter { c in
            guard Rehearsal.isPlayable(c, mode: rehearsalMode), let t = Timecode.parse(c.timecodeTrigger) else { return false }
            return Timecode.crossed(previous: previous, now: frame, target: t.toFrame(rate), rate: rate)
        }
        due.forEach { engine.fire($0) }
        return due.count
    }

    /// Klokketrigger: start cuer når veggklokka passerer klokkeslettet siden forrige sjekk. Cuene startes som ved tidskode-trigger.
    func checkClockTriggers(now: Date) {
        let previous = lastClockCheck
        lastClockCheck = now
        guard settings.clockTriggersEnabled, let previous else { return }
        let due = ClockTriggers.due(cues, previous: previous, now: now).filter { !Rehearsal.isSkipped($0, mode: rehearsalMode) }
        due.forEach { engine.fire($0) }
        if !due.isEmpty {
            statusText = L("Klokke {0} → {1}", Self.clockFormatter.string(from: now), due.count == 1 ? L("1 cue") : L("{0} cuer", due.count))
        }
    }

    // MARK: - Show-fil

    /// Be om at cuen GO-markøren står på (og cuene den starter automatisk) forberedes i bakgrunnen.
    func updatePreload() {
        guard let engine else { return }
        engine.preloader.update(cues: cues, playheadIndex: playhead.flatMap(index(of:)) ?? -1,
                                sampleRate: audio.sampleRate > 0 ? audio.sampleRate : nil, enabled: settings.preloadNextCue)
    }

    private var preloadPending = false
    /// Endringer i en cue (fil, deaktivert, fortsett, mål) kan endre hva som skal forberedes; samles opp så det ikke kjøres for hver endring.
    private func schedulePreloadUpdate() {
        guard !preloadPending else { return }
        preloadPending = true
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) { [weak self] in
            self?.preloadPending = false
            self?.updatePreload()
        }
    }

    private func observeCues() {
        cueSubs = cues.map { $0.objectWillChange.sink { [weak self] _ in self?.markMaybeDirty(); self?.schedulePreloadUpdate() } }
    }

    private var dirtyCheckPending = false
    private func markMaybeDirty() {
        guard !dirtyCheckPending else { return }
        dirtyCheckPending = true
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) { [weak self] in
            guard let self else { return }
            self.dirtyCheckPending = false
            self.updateDirty()
        }
    }

    /// Bare cuene (uten tittel), brukt til angre-historikken.
    private func cueData() -> Data {
        var doc = ShowDocument()
        doc.cues = cues
        doc.scenes = scenes
        return (try? doc.jsonData()) ?? Data()
    }

    private func currentData() -> Data {
        var doc = ShowDocument()
        doc.title = showURL?.deletingPathExtension().lastPathComponent ?? L("Show")
        doc.cues = cues
        doc.routingPresets = routingPresets
        doc.scenes = scenes
        return (try? doc.jsonData()) ?? Data()
    }

    func updateDirty() {
        let d = currentData() != savedData
        if d != isDirty { isDirty = d }
        if history.record(cueData()) { refreshUndo() }
    }

    var windowTitle: String {
        (showURL?.deletingPathExtension().lastPathComponent ?? L("Uten navn")) + (isDirty ? " •" : "") + (showMode ? L("  🔒 SHOWMODUS") : "") + (rehearsalMode ? L("  🎭 ØVELSESMODUS") : "")
            + L(" — ShowCue ") + AppModel.version
    }

    // MARK: - Angre / gjør om

    private func refreshUndo() {
        canUndo = isEditable && history.canUndo
        canRedo = isEditable && history.canRedo
    }

    func undo() {
        guard isEditable else { return }
        NSApp.keyWindow?.makeFirstResponder(nil)
        updateDirty() // ta med endringer som ikke er registrert ennå
        if let d = history.undo() { replaceCues(d); statusText = L("Angret.") }
    }

    func redo() {
        guard isEditable else { return }
        NSApp.keyWindow?.makeFirstResponder(nil)
        updateDirty()
        if let d = history.redo() { replaceCues(d); statusText = L("Gjort om.") }
    }

    /// Bytt ut cuene med en lagret tilstand. Showfil, innstillinger og det som spiller røres ikke.
    private func replaceCues(_ data: Data) {
        guard let doc = try? JSONDecoder().decode(ShowDocument.self, from: data) else { return }
        let idx = playhead.flatMap { p in visibleCues.firstIndex { $0 === p } } ?? 0
        cues = doc.cues
        scenes = doc.scenes
        if scenes.isEmpty { activeScene = "" }
        else if !scenes.contains(where: { SceneLogic.sameScene($0, activeScene) }) { activeScene = scenes[0] }
        playheads = [:]
        let inScene = visibleCues
        select(inScene.isEmpty ? nil : inScene[min(max(0, idx), inScene.count - 1)])
        updateDirty()
        refreshUndo()
    }

    // MARK: - Autolagring og gjenoppretting

    private func autosave() {
        updateDirty()
        guard isDirty else { return }
        do { try ShowSafety.writeAutosave(for: showURL, data: currentData()) }
        catch { statusText = L("Autolagring feilet: {0}", error.localizedDescription) }
    }

    /// Tilby å hente tilbake ulagrede endringer etter krasj eller strømbrudd.
    func offerRecovery() {
        guard let file = ShowSafety.pendingAutosave(for: showURL) else { return }
        let date = ((try? FileManager.default.attributesOfItem(atPath: file.path))?[.modificationDate] as? Date) ?? Date()
        let f = DateFormatter()
        f.dateFormat = "dd.MM HH:mm"
        let a = NSAlert()
        a.messageText = L("Gjenopprette ulagrede endringer?")
        a.informativeText = L("Det finnes ulagrede endringer i «{0}» fra {1} (programmet ble ikke avsluttet normalt).", showURL?.deletingPathExtension().lastPathComponent ?? L("Uten navn"), f.string(from: date))
        a.addButton(withTitle: L("Hent tilbake"))
        a.addButton(withTitle: L("Forkast"))
        guard a.runModal() == .alertFirstButtonReturn else { ShowSafety.deleteAutosave(for: showURL); return }
        guard let data = try? Data(contentsOf: file) else { alert(L("Kunne ikke lese autolagringen.")); return }
        replaceCues(data)
        statusText = L("Ulagrede endringer er hentet tilbake. Husk å lagre.")
    }

    /// Programversjonen fra Info.plist (settes fra VERSION-fila når appen bygges).
    static var version: String { Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "dev" }

    /// Skjul utgangsvinduene (før spørsmål som ellers kunne havne bak et fullskjermvindu). Returnerer dem som var synlige.
    func hideOutputsForDialog() -> [OutputCompositor] {
        let visible = engine.allOutputs.filter(\.isVisible)
        visible.forEach { $0.hide() }
        return visible
    }

    func restoreOutputs(_ outputs: [OutputCompositor]) { outputs.forEach { $0.ensureVisible() } }

    func confirmDiscard() -> Bool {
        updateDirty()
        guard isDirty else { return true }
        let a = NSAlert()
        a.messageText = L("Vil du lagre endringene i showet?")
        a.addButton(withTitle: L("Lagre"))
        a.addButton(withTitle: L("Ikke lagre"))
        a.addButton(withTitle: L("Avbryt"))
        switch a.runModal() {
        case .alertFirstButtonReturn: return save(as: false)
        case .alertSecondButtonReturn: ShowSafety.deleteAutosave(for: showURL); return true
        default: return false
        }
    }

    func newShow(confirm: Bool = true) {
        if confirm && !confirmDiscard() { return }
        setShow(ShowDocument(), url: nil)
        statusText = L("Nytt show.")
    }

    func openDialog() {
        guard confirmDiscard() else { return }
        let p = NSOpenPanel()
        p.allowedContentTypes = [AppModel.showType]
        p.allowsMultipleSelection = false
        if p.runModal() == .OK, let url = p.url { open(url, confirm: false) }
    }

    func open(_ url: URL, confirm: Bool = true) {
        if confirm && !confirmDiscard() { return }
        do {
            let doc = try ShowDocument.load(from: url)
            setShow(doc, url: url)
            settings.lastShowPath = url.path
            settings.save()
            if !statusText.hasPrefix(L("Advarsel")) { statusText = L("Åpnet {0}", url.path) }
            offerRecovery()
            offerRelink()
        } catch {
            alert(L("Kunne ikke åpne showet: {0}", error.localizedDescription))
        }
    }

    @discardableResult
    func save(as saveAs: Bool) -> Bool {
        NSApp.keyWindow?.makeFirstResponder(nil) // fullfør redigering i tekstfelt
        var url = showURL
        if saveAs || url == nil {
            let p = NSSavePanel()
            p.allowedContentTypes = [AppModel.showType]
            p.nameFieldStringValue = url?.lastPathComponent ?? "Show.showcue"
            guard p.runModal() == .OK, let u = p.url else { return false }
            url = u
        }
        guard let target = url else { return false }
        do {
            var doc = ShowDocument()
            doc.title = target.deletingPathExtension().lastPathComponent
            doc.cues = cues
            doc.routingPresets = routingPresets
            doc.scenes = scenes
            ShowSafety.backup(target) // forrige versjon, før den lagres over
            try doc.save(to: target)
            ShowSafety.deleteAutosave(for: showURL)
            ShowSafety.deleteAutosave(for: target)
            showURL = target
            savedData = currentData()
            isDirty = false
            settings.lastShowPath = target.path
            settings.save()
            statusText = L("Lagret {0}", target.path)
            return true
        } catch {
            alert(L("Kunne ikke lagre: {0}", error.localizedDescription))
            return false
        }
    }

    func setShow(_ doc: ShowDocument, url: URL?) {
        engine.stopAll(fade: 0)
        var doc = doc
        doc.normalizeScenes()
        cues = doc.cues
        routingPresets = doc.routingPresets
        scenes = doc.scenes
        activeScene = scenes.first ?? ""
        playheads = [:]
        showURL = url
        for c in cues where c.type == .audio && (c.fileDuration <= 0 || c.fileChannels <= 0) && FileManager.default.fileExists(atPath: c.filePath) {
            let p = AudioEngine.probeAudio(c.filePath)
            c.fileDuration = p.duration; c.fileChannels = p.channels
        }
        MediaCache.shared.clear()
        engine.preloader.clear() // nytt show: slipp alt som var forberedt
        MediaCache.shared.preload(cues)
        for c in cues where c.type == .video && c.fileChannels < 0 { probeVideo(c) }
        savedData = currentData()
        updatePreload()
        isDirty = false
        history.reset(cueData())
        refreshUndo()
        select(SceneLogic.firstEnabled(cues, activeScene))
        memoText = ""
        let missing = cues.filter { $0.isFileCue && !$0.filePath.isEmpty && !FileManager.default.fileExists(atPath: $0.filePath) }
        missing.forEach { $0.error = L("Fila finnes ikke: {0}", $0.filePath) }
        if !missing.isEmpty { statusText = L("Advarsel: {0} mediefil(er) mangler. De er merket med ! i lista.", missing.count) }
    }

    func alert(_ text: String) {
        let a = NSAlert()
        a.messageText = text
        a.runModal()
    }

    // MARK: - Redigering

    private var insertIndex: Int {
        let idx = selectedCues.compactMap(index(of:))
        return idx.max().map { $0 + 1 } ?? SceneLogic.endOfScene(cues, activeScene)
    }

    func insert(_ new: [Cue], at: Int? = nil) {
        guard isEditable else { return }
        var i = min(at ?? insertIndex, cues.count)
        var list = cues
        for c in new {
            c.scene = activeScene // nye cuer havner alltid i aktiv scene
            if c.number.isEmpty { c.number = SceneLogic.nextNumber(list, activeScene) }
            list.insert(c, at: i); i += 1
        }
        cues = list
        if let last = new.last { select(last) }
        updateDirty()
    }

    func cue(forFile url: URL) -> Cue? {
        let ext = url.pathExtension.lowercased()
        let path = url.path
        if AppModel.audioExt.contains(ext) {
            let c = Cue(type: .audio); c.filePath = path
            let p = AudioEngine.probeAudio(path); c.fileDuration = p.duration; c.fileChannels = p.channels
            return c
        }
        if AppModel.videoExt.contains(ext) {
            let c = Cue(type: .video); c.filePath = path; probeVideo(c); return c
        }
        if AppModel.imageExt.contains(ext) { let c = Cue(type: .image); c.filePath = path; MediaCache.shared.preload([c]); return c }
        if ext == "pdf" { let c = Cue(type: .pdf); c.filePath = path; MediaCache.shared.preload([c]); return c }
        return nil
    }

    func probeVideo(_ c: Cue) {
        let path = c.filePath
        guard FileManager.default.fileExists(atPath: path) else { return }
        DispatchQueue.global(qos: .userInitiated).async {
            let p = AssetDecoder.probe(url: URL(fileURLWithPath: path))
            DispatchQueue.main.async {
                guard c.filePath == path else { return }
                c.fileChannels = p.channels
                if c.knownDuration <= 0 && p.duration > 0 { c.knownDuration = p.duration }
            }
        }
    }

    func addFiles(_ urls: [URL], at: Int? = nil) {
        guard isEditable else { return }
        let new = urls.sorted { $0.lastPathComponent.localizedStandardCompare($1.lastPathComponent) == .orderedAscending }.compactMap(cue(forFile:))
        if new.isEmpty { statusText = L("Ingen støttede filer."); return }
        insert(new, at: at)
        statusText = L("La til {0} cue(s).", new.count)
    }

    /// Filer sluppet på en rad: én fil på en media-cue uten fil (av samme type) blir cuens fil, ellers nye cuer under raden.
    func dropFiles(_ urls: [URL], on target: Cue, index: Int) {
        guard isEditable else { return }
        if urls.count == 1, [.audio, .video, .image, .pdf].contains(target.type), target.filePath.isEmpty,
           let made = cue(forFile: urls[0]), made.type == target.type {
            setFile(target, urls[0].path)
            statusText = L("La fila inn i cue {0}.", target.number)
            return
        }
        addFiles(urls, at: index + 1)
    }

    func pickFiles(_ types: [String], multiple: Bool) -> [URL] {
        let p = NSOpenPanel()
        p.allowedContentTypes = types.compactMap { UTType(filenameExtension: $0) }
        p.allowsMultipleSelection = multiple
        return p.runModal() == .OK ? p.urls : []
    }

    func add(_ type: CueType) {
        guard isEditable else { return }
        NSApp.keyWindow?.makeFirstResponder(nil)
        switch type {
        // Lyd, video, bilde, PDF og spilleliste lages tomme; fila velges i panelet til høyre (Bla),
        // eller ved å dra en fil inn på cuen.
        case .fade:
            let c = Cue(type: .fade)
            if let t = playhead, t.type == .audio || t.isVisual { c.target = t.number }
            insert([c])
        case .midi:
            let c = Cue(type: .midi); c.device = MidiSystem.destinations().first ?? ""; insert([c])
        case .timecode:
            let c = Cue(type: .timecode); c.device = MidiSystem.destinations().first ?? ""; insert([c])
        case .input:
            let c = Cue(type: .input); c.fileChannels = max(1, AudioInput.probeChannels(deviceUID: settings.inputDeviceUID)); insert([c])
        default:
            insert([Cue(type: type)])
        }
    }

    /// Legg til spor i en spilleliste, eller bilder, PDF-er og videoer i en lysbildeserie.
    func addTracks(to c: Cue) {
        guard isEditable else { return }
        if c.type == .slideshow {
            let urls = pickFiles(AppModel.imageExt + ["pdf"] + AppModel.videoExt, multiple: true)
            c.files += urls.map(\.path)
            return
        }
        let urls = pickFiles(AppModel.audioExt, multiple: true)
        guard !urls.isEmpty else { return }
        c.files += urls.map(\.path)
        c.fileChannels = max(c.fileChannels, urls.map { AudioEngine.probeAudio($0.path).channels }.max() ?? 2)
    }

    func browseFile(for c: Cue) {
        guard isEditable else { return }
        let ext: [String]
        switch c.type {
        case .audio: ext = AppModel.audioExt
        case .video: ext = AppModel.videoExt
        case .image: ext = AppModel.imageExt
        default: ext = ["pdf"]
        }
        guard let url = pickFiles(ext, multiple: false).first else { return }
        setFile(c, url.path)
    }

    func setFile(_ c: Cue, _ path: String) {
        c.filePath = path
        c.error = FileManager.default.fileExists(atPath: path) ? nil : L("Fila finnes ikke")
        switch c.type {
        case .audio: let p = AudioEngine.probeAudio(path); c.fileDuration = p.duration; c.fileChannels = p.channels
        case .video: c.knownDuration = 0; c.fileChannels = -1; probeVideo(c)
        default: MediaCache.shared.preload([c])
        }
    }

    func duplicateSelected() {
        guard isEditable else { return }
        let sel = selectedCues
        guard !sel.isEmpty else { return }
        insert(sel.map { let k = $0.clone(); k.number = ""; k.hotkey = ""; k.midiTrigger = ""; return k })
    }

    func deleteSelected() {
        guard isEditable else { return }
        let sel = selectedCues
        guard !sel.isEmpty else { return }
        let a = NSAlert()
        a.messageText = sel.count == 1 ? L("Slette cue {0} «{1}»?", sel[0].number, sel[0].displayName) : L("Slette {0} cuer?", sel.count)
        a.addButton(withTitle: L("Slett"))
        a.addButton(withTitle: L("Avbryt"))
        guard a.runModal() == .alertFirstButtonReturn else { return }
        let first = visibleCues.firstIndex { c in sel.contains { $0 === c } } ?? 0
        sel.forEach { engine.stop($0, fade: 0) }
        cues.removeAll { c in sel.contains { $0 === c } }
        let left = visibleCues
        select(left.isEmpty ? nil : left[min(first, left.count - 1)])
        updateDirty()
    }

    func moveSelected(_ delta: Int) {
        guard isEditable else { return }
        guard let c = playhead, let i = index(of: c) else { return }
        let j = SceneLogic.neighbourIndex(cues, i, delta) // nærmeste cue i samme scene
        guard j >= 0 else { return }
        cues.swapAt(i, j)
        updateDirty()
    }

    func renumber() {
        guard isEditable else { return }
        let a = NSAlert()
        a.messageText = scenes.isEmpty ? L("Gi alle cuer nye nummer (1, 2, 3)?") : L("Gi alle cuer i scenen «{0}» nye nummer (1, 2, 3)?", activeSceneLabel)
        a.informativeText = L("Fade-, stopp-, gå til- og gruppe-cuer oppdateres automatisk.")
        a.addButton(withTitle: L("Nummerer"))
        a.addButton(withTitle: L("Avbryt"))
        guard a.runModal() == .alertFirstButtonReturn else { return }
        SceneLogic.renumber(cues, activeScene)
    }

    func setColor(_ color: CueColor) {
        guard isEditable else { return }
        (selectedCues.isEmpty ? [playhead].compactMap { $0 } : selectedCues).forEach { $0.color = color }
    }

    func pdfNextPage(_ c: Cue) {
        guard isEditable else { return }
        let k = c.clone()
        k.number = ""; k.hotkey = ""; k.midiTrigger = ""; k.name = ""
        k.page = c.page + 1
        insert([k], at: (index(of: c) ?? cues.count - 1) + 1)
    }

    // MARK: - Mediefiler: finn og samle

    /// Etter åpning: mangler det mediefiler, tilbys det å lete etter dem i en mappe.
    func offerRelink() {
        let missing = MediaTools.missing(cues)
        guard !missing.isEmpty else { return }
        let a = NSAlert()
        a.messageText = missing.count == 1 ? L("Fant ikke mediefila «{0}».", Cue.lastPathComponent(missing[0]))
            : L("Fant ikke {0} mediefiler.", missing.count)
        a.informativeText = L("Vil du velge en mappe å lete i? Undermapper tas med.")
        a.addButton(withTitle: L("Velg mappe"))
        a.addButton(withTitle: L("Ikke nå"))
        if a.runModal() == .alertFirstButtonReturn { relink() }
        else { statusText = L("Advarsel: {0} mediefil(er) mangler. Fil → Finn manglende mediefiler.", missing.count) }
    }

    func relinkMenu() {
        guard isEditable else { return }
        if MediaTools.missing(cues).isEmpty { statusText = L("Ingen mediefiler mangler."); return }
        relink()
    }

    private func pickFolder(_ message: String, start: URL? = nil) -> URL? {
        let p = NSOpenPanel()
        p.canChooseFiles = false
        p.canChooseDirectories = true
        p.canCreateDirectories = true
        p.message = message
        p.prompt = L("Velg")
        if let start { p.directoryURL = start }
        return p.runModal() == .OK ? p.url : nil
    }

    private func relink() {
        guard let folder = pickFolder(L("Velg mappa mediefilene ligger i"), start: showURL?.deletingLastPathComponent()) else { return }
        let found = MediaTools.relink(cues, in: folder)
        for c in cues where c.type == .audio && c.fileDuration <= 0 && FileManager.default.fileExists(atPath: c.filePath) {
            let p = AudioEngine.probeAudio(c.filePath); c.fileDuration = p.duration; c.fileChannels = p.channels
        }
        for c in cues where c.isFileCue && FileManager.default.fileExists(atPath: c.filePath) { c.error = nil }
        MediaCache.shared.clear()
        engine.preloader.clear()
        MediaCache.shared.preload(cues)
        updatePreload()
        let left = MediaTools.missing(cues).count
        statusText = L("Fant {0} fil(er).", found) + (left > 0 ? L(" {0} mangler fortsatt.", left) : L(" Alle mediefiler er på plass."))
        updateDirty()
    }

    func collectShow() {
        NSApp.keyWindow?.makeFirstResponder(nil)
        guard let folder = pickFolder(L("Velg (eller lag) en mappe for showet og mediefilene")) else { return }
        var doc = ShowDocument()
        doc.cues = cues
        doc.routingPresets = routingPresets
        let name = showURL?.deletingPathExtension().lastPathComponent ?? L("Show")
        do {
            let r = try MediaTools.collect(doc, to: folder, name: name)
            let a = NSAlert()
            a.messageText = L("Showet og {0} mediefil(er) er kopiert", r.copied)
            a.informativeText = L("{0}\n\nShowfil: {1}, mediefiler i undermappa «Media».", folder.path, r.showURL.lastPathComponent)
                + (r.missing.isEmpty ? "" : L("\n\n{0} fil(er) manglet og ble ikke kopiert.", r.missing.count))
                + L("\n\nVil du åpne den samlede kopien nå?")
            a.addButton(withTitle: L("Åpne kopien"))
            a.addButton(withTitle: L("Ferdig"))
            if a.runModal() == .alertFirstButtonReturn { open(r.showURL) }
        } catch {
            alert(L("Kunne ikke samle showet: {0}", error.localizedDescription))
        }
    }

    // MARK: - Routingforhåndsvalg

    /// Cuene routingen skal settes på: de valgte cuene hvis `cue` er blant dem, ellers bare den.
    func routingTargets(for cue: Cue) -> [Cue] {
        let sel = selectedCues
        return sel.contains { $0 === cue } ? sel : [cue]
    }

    private func reportRouting(_ r: RoutingApplyResult, preset: String?) {
        var text = preset.map { L("Routing «{0}» satt på {1} cue(r).", $0, r.changed) } ?? L("Routing satt på {0} cue(r).", r.changed)
        if r.unchanged > 0 { text += L(" {0} hadde den fra før.", r.unchanged) }
        if r.skipped > 0 { text += L(" {0} hoppet over (ingen lyd eller ingen gyldig utgang).", r.skipped) }
        statusText = text
        updateDirty()
    }

    func applyRoutingPreset(_ preset: RoutingPreset, to cue: Cue) {
        guard isEditable else { return }
        reportRouting(RoutingPresets.apply(routingTargets(for: cue), preset: preset, busCount: settings.buses.count), preset: preset.name)
    }

    func copyRouting(of cue: Cue) {
        if routeClipboard.copy(cue) { statusText = L("Routingen er kopiert.") }
    }

    func pasteRouting(to cue: Cue) {
        guard isEditable else { return }
        reportRouting(RoutingPresets.paste(routingTargets(for: cue), clipboard: routeClipboard, busCount: settings.buses.count), preset: nil)
    }

    /// Setter cuens routing på alle de andre valgte cuene.
    func applyRouting(of cue: Cue) {
        guard isEditable else { return }
        let others = routingTargets(for: cue).filter { $0 !== cue }
        reportRouting(RoutingPresets.applyRoutes(others, routes: cue.routes, busCount: settings.buses.count), preset: nil)
    }

    /// Lagrer et nytt forhåndsvalg (`index` nil) eller endrer et som finnes. Gir feilen hvis det ikke lot seg gjøre.
    func saveRoutingPreset(index: Int?, name: String, pairs: [RoutingPair]) -> RoutingPresetError {
        guard isEditable else { return .notFound }
        var list = routingPresets
        let err = index.map { RoutingPresets.update(&list, index: $0, name: name, pairs: pairs) } ?? RoutingPresets.add(&list, name: name, pairs: pairs)
        if err == .ok {
            routingPresets = list
            statusText = L("Forhåndsvalget er lagret.")
            updateDirty()
        }
        return err
    }

    func deleteRoutingPreset(at index: Int) {
        guard isEditable else { return }
        var list = routingPresets
        let name = index >= 0 && index < list.count ? list[index].name : ""
        if RoutingPresets.remove(&list, index: index) {
            routingPresets = list
            statusText = L("Slettet forhåndsvalget «{0}».", name)
            updateDirty()
        }
    }

    // MARK: - Forhåndssjekk

    /// Kjører forhåndssjekken mot gjeldende show og maskininnstillinger.
    func runPreflight() -> [PreflightFinding] {
        NSApp.keyWindow?.makeFirstResponder(nil)
        return PreflightCheck.run(cues: cues, settings: settings, environment: SystemPreflightEnvironment(), rehearsalMode: rehearsalMode)
        return PreflightCheck.run(cues: cues, settings: settings, environment: SystemPreflightEnvironment(), scenes: scenes)
    }

    /// «Sjekk showet»: åpner vinduet med funnene (sjekken kjøres hver gang vinduet vises).
    func showPreflight() {
        if let w = preflightWindow { w.close(); preflightWindow = nil }
        let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 720, height: 520),
                         styleMask: [.titled, .closable, .resizable], backing: .buffered, defer: false)
        w.title = L("Sjekk showet")
        w.isReleasedWhenClosed = false
        w.contentView = NSHostingView(rootView: PreflightView().environmentObject(self))
        w.center()
        w.makeKeyAndOrderFront(nil)
        preflightWindow = w
    }

    // MARK: - Lydstyrke

    func normalize(_ list: [Cue]) {
        guard isEditable else { return }
        NSApp.keyWindow?.makeFirstResponder(nil)
        let targets = list.filter { $0.type == .audio && FileManager.default.fileExists(atPath: $0.filePath) }
        guard !targets.isEmpty else { statusText = L("Ingen lyd-cuer med fil å normalisere."); return }
        loudnessTask?.cancel()
        let goal = settings.loudnessTarget
        loudnessTask = Task { @MainActor [weak self] in
            var silent = 0, n = 0
            for c in targets {
                n += 1
                self?.statusText = L("Måler lydstyrke {0}/{1}: {2} …", n, targets.count, c.displayName)
                let (path, start, end) = (c.filePath, c.startTime, c.endTime)
                let result: Result<Double?, Error> = await Task.detached(priority: .userInitiated) {
                    Result { try LoudnessMeter.measure(file: URL(fileURLWithPath: path), start: start, end: end, isCancelled: { Task.isCancelled }) }
                }.value
                if Task.isCancelled { return }
                switch result {
                case .failure(let e): c.error = L("Lydstyrke: {0}", e.localizedDescription)
                case .success(let lufs?):
                    c.loudness = (lufs * 10).rounded() / 10
                    c.volumeDb = LoudnessMeter.gain(for: lufs, target: goal)
                case .success(nil):
                    c.loudness = nil; silent += 1
                }
            }
            self?.statusText = L("Normalisert {0} lyd-cue(r) til {1} LUFS.", targets.count - silent, Cue.fmt(goal))
                + (silent > 0 ? L(" {0} var stille eller for korte.", silent) : "")
            self?.updateDirty()
        }
    }

    func normalizeSelected() { normalize(selectedCues.isEmpty ? [playhead].compactMap { $0 } : selectedCues) }
    func normalizeAll() { normalize(cues) }

    // MARK: - Knappepanel

    func showButtonPanel() {
        if let w = panelWindow { w.makeKeyAndOrderFront(nil); return }
        let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 900, height: 620),
                         styleMask: [.titled, .closable, .resizable, .miniaturizable], backing: .buffered, defer: false)
        w.title = L("ShowCue – knappepanel")
        w.isReleasedWhenClosed = false
        w.contentView = NSHostingView(rootView: ButtonPanelView().environmentObject(self))
        w.center()
        w.makeKeyAndOrderFront(nil)
        panelWindow = w
    }

    func closeMemo() { memoText = "" }

    // MARK: - Videolyd

    func extractVideoAudio(_ v: Cue) {
        guard isEditable else { return }
        guard v.type == .video, FileManager.default.fileExists(atPath: v.filePath) else { return }
        statusText = L("Pakker ut lyd fra {0} …", v.fileName)
        let src = URL(fileURLWithPath: v.filePath)
        DispatchQueue.global(qos: .userInitiated).async {
            let result = Result { try VideoAudioExtractor.extract(src) { p in
                DispatchQueue.main.async { self.statusText = L("Pakker ut lyd fra {0} … {1} %", v.fileName, Int(p * 100)) }
            } }
            DispatchQueue.main.async {
                switch result {
                case .failure(let e):
                    self.alert(L("Kunne ikke pakke ut lyden: {0}", e.localizedDescription))
                case .success(let wav):
                    let a = Cue(type: .audio)
                    a.filePath = wav.path
                    let p = AudioEngine.probeAudio(wav.path); a.fileDuration = p.duration; a.fileChannels = p.channels
                    a.name = v.displayName + L(" (lyd)")
                    a.volumeDb = v.volumeDb; a.fadeIn = v.fadeIn; a.fadeOut = v.fadeOut; a.loop = v.loop; a.color = v.color
                    // Var videolyden slått av, får lyd-cuen standard routing.
                    a.routes = (v.routes?.isEmpty ?? true) ? nil : v.routes
                    a.continueMode = v.continueMode
                    v.continueMode = .autoContinue // videoen starter lyd-cuen samtidig
                    v.routes = []                   // og blir selv stum
                    self.insert([a], at: (self.index(of: v) ?? self.cues.count - 1) + 1)
                    self.select(v)
                    self.statusText = L("Lyden er lagret som {0} og lagt inn som cue {1} (starter samtidig med videoen).", wav.lastPathComponent, a.number)
                }
            }
        }
    }

    // MARK: - Innstillinger og fjernstyring

    func applySettings(_ new: AppSettings) {
        let restartAudio = new.audioKey != settings.audioKey
        if new.language != settings.language { statusText = L("Det nye språket brukes neste gang ShowCue startes.") }
        var s = new
        s.buses = settings.buses // patchen redigeres i eget vindu
        s.lastShowPath = settings.lastShowPath
        settings = s
        settings.save()
        if restartAudio {
            engine.stopAll(fade: 0)
            audio.start(settings)
        }
        engine.configurePreview(settings)
        engine.inputDeviceUID = settings.inputDeviceUID
        engine.setDefaultScreen(settings.videoScreen)
        if settings.videoScreen >= 0 { output.ensureVisible() }
        applyRemoteSettings()
        updatePreload()
    }

    /// Zoom inn (`direction` > 0), ut (< 0) eller tilbake til vanlig størrelse (0).
    func zoom(_ direction: Int) {
        settings.zoom = direction == 0 ? 1 : UIZoom.step(settings.zoom, direction)
        statusText = L("Zoom: {0}", UIZoom.percent(settings.zoom))
    }

    func applyPatch() {
        audio.applyPatch(settings.buses)
        settings.save()
        objectWillChange.send()
    }

    func applyRemoteSettings() {
        router.settings = settings
        var parts: [String] = []
        if settings.oscInEnabled {
            do {
                try oscServer.start(port: settings.oscInPort)
                let ips = UDP.localAddresses()
                parts.append(L("OSC inn: port {0}", settings.oscInPort) + (ips.isEmpty ? "" : "  (" + ips.joined(separator: ", ") + ")"))
            } catch {
                parts.append(L("OSC inn: {0}", error.localizedDescription))
            }
        } else {
            oscServer.stop()
            parts.append(L("OSC inn: av"))
        }
        do {
            try midi.openInput(named: settings.midiInDevice, channel: settings.midiInChannel)
            parts.append(midi.isInputOpen ? L("MIDI inn: {0}", settings.midiInDevice) + (settings.midiInChannel == 0 ? L(" (alle kanaler)") : L(" (kanal {0})", settings.midiInChannel)) : L("MIDI inn: av"))
        } catch {
            parts.append(L("MIDI inn: {0}", error.localizedDescription))
        }
        if settings.oscFeedbackEnabled {
            let f = OscFeedback.forTarget(host: settings.oscFeedbackHost, port: settings.oscFeedbackPort)
            f.format = settings.oscFeedbackFormat
            feedback = f
            parts.append(L("Status ut: {0}:{1}", settings.oscFeedbackHost, settings.oscFeedbackPort))
        } else {
            feedback = nil
        }
        remoteParts = parts
        applyStatusLine()
    }

    private var remoteParts: [String] = []
    private func applyStatusLine() {
        remoteStatus = (remoteParts + (router.timecodeStatus.isEmpty ? [] : [router.timecodeStatus])).joined(separator: "   ·   ")
    }

    func learnMidi(for c: Cue) {
        guard midi.isInputOpen else {
            alert(L("Velg en MIDI-inngang under Innstillinger → MIDI inn først."))
            return
        }
        learnText = L("Venter på MIDI … trykk en tast eller knapp på kontrolleren")
        router.beginLearn { [weak self] t in
            guard let self else { return }
            self.learnText = ""
            let other = self.cues.first { $0 !== c && $0.midiTrigger == t }
            c.midiTrigger = t
            self.statusText = L("Cue {0} startes nå av {1}", c.number, MidiTriggers.describe(t)) + (other.map { L(" (cue {0} har samme trigger)", $0.number) } ?? "")
        }
    }

    func cancelLearn() {
        router.cancelLearn()
        learnText = ""
    }

    // MARK: - Takt

    private var lastClockCheck: Date?

    private func tick() {
        tickCount += 1
        if tickCount % 1800 == 0 { autosave() } // hvert 30. sekund
        if tickCount % 2 == 0, let m = audio.mixer {
            live.meterL = max(MeterMath.fraction(m.busLevel(0)), live.meterL - 0.05)
            live.meterR = max(MeterMath.fraction(m.busLevel(1)), live.meterR - 0.05)
        }
        if tickCount % 6 == 0, let f = feedback { f.update(buildStatus(), now: engine.now) }
        checkClockTriggers(now: Date())
        if tickCount % 15 == 0 {
            let c = Self.clockFormatter.string(from: Date())
            if c != live.clockText { live.clockText = c }
        }
    }

    private static let clockFormatter: DateFormatter = {
        let f = DateFormatter(); f.dateFormat = "HH:mm:ss"; return f
    }()

    func buildStatus() -> ShowStatus {
        let next = playhead
        let playing = engine.active.filter { !$0.done }.map {
            PlayingCue(number: $0.cue.number, name: $0.cue.displayName, elapsed: $0.started ? $0.elapsed : 0,
                       remaining: $0.duration > 0 ? max(0, $0.duration - $0.elapsed) : 0, hasDuration: $0.duration > 0,
                       preWaiting: !$0.started)
        }
        return ShowStatus(showName: showURL?.deletingPathExtension().lastPathComponent ?? L("Uten navn"),
                          nextNumber: next?.number ?? "", nextName: next?.displayName ?? "", nextNotes: next?.notes ?? "",
                          playheadPosition: next.flatMap(index(of:)).map { $0 + 1 } ?? 0, cueCount: cues.count, playing: playing)
    }
}

final class LiveState: ObservableObject {
    @Published var meterL: Float = 0
    @Published var meterR: Float = 0
    @Published var clockText = ""
}

enum MeterMath {
    /// Toppnivå (lineært) → andel av måler (−60…0 dB).
    static func fraction(_ peak: Float) -> Float {
        guard peak > 0.001 else { return 0 }
        return min(1, max(0, (20 * log10(peak) + 60) / 60))
    }
}

/// Pakker ut lydsporet i en video til WAV (32-bit flyttall, original frekvens og kanaler).
enum VideoAudioExtractor {
    static func extract(_ video: URL, progress: (Double) -> Void) throws -> URL {
        let probe = AssetDecoder.probe(url: video)
        guard probe.channels > 0 else { throw DecoderError.noAudio }
        let rate = 48000.0
        let dec = try AssetDecoder(url: video, outputRate: rate)
        var out = video.deletingPathExtension().appendingPathExtension("wav")
        out = out.deletingLastPathComponent().appendingPathComponent(video.deletingPathExtension().lastPathComponent + L(" (lyd).wav"))
        var n = 2
        while FileManager.default.fileExists(atPath: out.path) {
            out = out.deletingLastPathComponent().appendingPathComponent(video.deletingPathExtension().lastPathComponent + L(" (lyd) {0}.wav", n)); n += 1
        }
        let ch = dec.channels
        var settings: [String: Any] = [
            AVFormatIDKey: kAudioFormatLinearPCM, AVSampleRateKey: rate, AVNumberOfChannelsKey: ch,
            AVLinearPCMBitDepthKey: 32, AVLinearPCMIsFloatKey: true, AVLinearPCMIsBigEndianKey: false, AVLinearPCMIsNonInterleaved: false,
        ]
        if ch > 2 {
            var layout = AudioChannelLayout()
            layout.mChannelLayoutTag = kAudioChannelLayoutTag_DiscreteInOrder | UInt32(ch)
            settings[AVChannelLayoutKey] = Data(bytes: &layout, count: MemoryLayout<AudioChannelLayout>.size)
        }
        let file = try AVAudioFile(forWriting: out, settings: settings, commonFormat: .pcmFormatFloat32, interleaved: true)
        let chunk = 8192
        guard let buf = AVAudioPCMBuffer(pcmFormat: file.processingFormat, frameCapacity: AVAudioFrameCount(chunk)),
              let data = buf.floatChannelData?[0] else { throw DecoderError.unsupported(L("Minnefeil")) }
        let total = max(1.0, dec.duration * rate)
        var written = 0.0
        while true {
            let got = dec.read(into: data, maxFrames: chunk)
            if got == 0 { break }
            buf.frameLength = AVAudioFrameCount(got)
            try file.write(from: buf)
            written += Double(got)
            progress(min(1, written / total))
        }
        return out
    }
}
