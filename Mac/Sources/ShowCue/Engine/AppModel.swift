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
    @Published var cues: [Cue] = [] { didSet { observeCues() } }
    @Published var selection: Set<UUID> = []
    @Published private(set) var showURL: URL?
    @Published private(set) var isDirty = false
    private var savedData = Data()

    // Status
    @Published var statusText = ""
    /// Teksten fra siste memo-cue (tom = ingen beskjed vises).
    @Published var memoText = ""
    private var panelWindow: NSWindow?
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
    private var subs: Set<AnyCancellable> = []

    init() {
        let loaded = AppSettings.load()
        settings = loaded
        router = RemoteRouter(settings: loaded)
        engine = CueEngine(audio: audio, output: output, midi: midi) { [weak self] in self?.cues ?? [] }
        router.show = self
        engine.onLog = { [weak self] in self?.statusText = $0 }
        engine.onTick = { [weak self] in self?.tick() }
        // «Gå til»-cue: vent til GO er ferdig med å flytte markøren, så flytt den til målet.
        engine.onGoto = { [weak self] target, fire in
            DispatchQueue.main.async {
                guard let self else { return }
                guard let c = self.find(target) else { self.statusText = L("Gå til: fant ikke cue {0}.", target); return }
                self.select(c)
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
        output.configure(screen: settings.videoScreen)
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

    func select(_ c: Cue?) {
        selection = c.map { [$0.id] } ?? []
    }

    func movePlayhead(_ delta: Int) {
        guard !cues.isEmpty else { return }
        let i = playhead.flatMap(index(of:)) ?? (delta > 0 ? -1 : cues.count)
        select(cues[max(0, min(cues.count - 1, i + delta))])
    }

    /// Klikk i lista (med ⌘ for flere, ⇧ for område).
    func click(_ c: Cue, command: Bool, shift: Bool) {
        if command {
            if selection.contains(c.id) { selection.remove(c.id) } else { selection.insert(c.id) }
        } else if shift, let anchor = playhead.flatMap(index(of:)), let i = index(of: c) {
            selection = Set(cues[min(anchor, i)...max(anchor, i)].map(\.id))
        } else {
            select(c)
        }
    }

    // MARK: - GO / stopp

    func go() {
        guard Date().timeIntervalSince(lastGo) * 1000 >= Double(settings.doubleGoProtectionMs) else { return }
        lastGo = Date()
        guard var cue = playhead, var idx = index(of: cue) else { return }
        if cue.disabled {
            guard let next = cues[(idx + 1)...].first(where: { !$0.disabled }), let ni = index(of: next) else { return }
            cue = next; idx = ni
        }
        engine.fire(cue)
        let n = CueEngine.nextPlayheadIndex(cues, fired: idx)
        select(n < cues.count ? cues[n] : nil)
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

    static func fadeOut(of c: Cue) -> Double { (c.type == .audio || c.type == .playlist || c.isVisual) ? c.fadeOut : 0 }

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

    func releaseSelected() {
        guard let c = playhead else { return }
        if !engine.release(c) { statusText = L("Cuen looper ikke (eller spiller ikke).") }
    }

    /// Hurtigtast/MIDI-trigger: lyd og bilde/video startes og stoppes (av/på), resten bare startes.
    func toggleHot(_ c: Cue) {
        if c.isRunning && (c.type == .audio || c.type == .playlist || c.isVisual) { engine.stop(c, fade: AppModel.fadeOut(of: c)) }
        else { engine.fire(c) }
    }

    func fire(_ c: Cue) { engine.fire(c) }
    func stop(_ c: Cue) { engine.stop(c, fade: AppModel.fadeOut(of: c)) }

    // MARK: - ShowControl (fjernstyring)

    private func find(_ number: String) -> Cue? {
        let n = number.trimmingCharacters(in: .whitespaces)
        return cues.first { $0.number.caseInsensitiveCompare(n) == .orderedSame }
    }

    func selectCue(_ number: String) -> Bool { guard let c = find(number) else { return false }; select(c); return true }
    func startCue(_ number: String) -> Bool { guard let c = find(number) else { return false }; engine.fire(c); return true }
    func stopCue(_ number: String) -> Bool { guard let c = find(number) else { return false }; stop(c); return true }
    func goCue(_ number: String) -> Bool {
        guard let c = find(number) else { return false }
        select(c)
        lastGo = .distantPast // eksplisitt cue: ingen dobbel-GO-sperre
        go()
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
        let due = cues.filter { c in
            guard !c.disabled, let t = Timecode.parse(c.timecodeTrigger) else { return false }
            return Timecode.crossed(previous: previous, now: frame, target: t.toFrame(rate), rate: rate)
        }
        due.forEach { engine.fire($0) }
        return due.count
    }

    // MARK: - Show-fil

    private func observeCues() {
        cueSubs = cues.map { $0.objectWillChange.sink { [weak self] _ in self?.markMaybeDirty() } }
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
        return (try? doc.jsonData()) ?? Data()
    }

    private func currentData() -> Data {
        var doc = ShowDocument()
        doc.title = showURL?.deletingPathExtension().lastPathComponent ?? L("Show")
        doc.cues = cues
        return (try? doc.jsonData()) ?? Data()
    }

    func updateDirty() {
        let d = currentData() != savedData
        if d != isDirty { isDirty = d }
        if history.record(cueData()) { refreshUndo() }
    }

    var windowTitle: String {
        (showURL?.deletingPathExtension().lastPathComponent ?? L("Uten navn")) + (isDirty ? " •" : "") + (showMode ? L("  🔒 SHOWMODUS") : "")
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
        let idx = playhead.flatMap(index(of:)) ?? 0
        cues = doc.cues
        select(cues.isEmpty ? nil : cues[min(max(0, idx), cues.count - 1)])
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

    private func setShow(_ doc: ShowDocument, url: URL?) {
        engine.stopAll(fade: 0)
        cues = doc.cues
        showURL = url
        for c in cues where c.type == .audio && (c.fileDuration <= 0 || c.fileChannels <= 0) && FileManager.default.fileExists(atPath: c.filePath) {
            let p = AudioEngine.probeAudio(c.filePath)
            c.fileDuration = p.duration; c.fileChannels = p.channels
        }
        MediaCache.shared.clear()
        MediaCache.shared.preload(cues)
        for c in cues where c.type == .video && c.fileChannels < 0 { probeVideo(c) }
        savedData = currentData()
        isDirty = false
        history.reset(cueData())
        refreshUndo()
        select(cues.first { !$0.disabled })
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
        return (idx.max() ?? (cues.count - 1)) + 1
    }

    func insert(_ new: [Cue], at: Int? = nil) {
        guard isEditable else { return }
        var i = min(at ?? insertIndex, cues.count)
        var list = cues
        for c in new {
            if c.number.isEmpty { c.number = nextNumber(in: list) }
            list.insert(c, at: i); i += 1
        }
        cues = list
        if let last = new.last { select(last) }
        updateDirty()
    }

    private func nextNumber(in list: [Cue]) -> String {
        let maxN = list.compactMap { Double($0.number.replacingOccurrences(of: ",", with: ".")) }.max() ?? 0
        return String(Int(maxN.rounded(.down)) + 1)
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
        case .audio: addFiles(pickFiles(AppModel.audioExt, multiple: true))
        case .video: addFiles(pickFiles(AppModel.videoExt, multiple: true))
        case .image: addFiles(pickFiles(AppModel.imageExt, multiple: true))
        case .pdf: addFiles(pickFiles(["pdf"], multiple: true))
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
        case .playlist:
            let urls = pickFiles(AppModel.audioExt, multiple: true)
            guard !urls.isEmpty else { return }
            let c = Cue(type: .playlist)
            c.files = urls.map(\.path)
            c.fileChannels = max(1, urls.map { AudioEngine.probeAudio($0.path).channels }.max() ?? 2)
            insert([c])
        default:
            insert([Cue(type: type)])
        }
    }

    /// Legg til spor i en spilleliste.
    func addTracks(to c: Cue) {
        guard isEditable else { return }
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
        let first = sel.compactMap(index(of:)).min() ?? 0
        sel.forEach { engine.stop($0, fade: 0) }
        cues.removeAll { c in sel.contains { $0 === c } }
        select(cues.isEmpty ? nil : cues[min(first, cues.count - 1)])
        updateDirty()
    }

    func moveSelected(_ delta: Int) {
        guard isEditable else { return }
        guard let c = playhead, let i = index(of: c) else { return }
        let j = i + delta
        guard j >= 0 && j < cues.count else { return }
        cues.swapAt(i, j)
        updateDirty()
    }

    func renumber() {
        guard isEditable else { return }
        let a = NSAlert()
        a.messageText = L("Gi alle cuer nye nummer (1, 2, 3)?")
        a.informativeText = L("Fade-, stopp- og gå til-cuer oppdateres automatisk.")
        a.addButton(withTitle: L("Nummerer"))
        a.addButton(withTitle: L("Avbryt"))
        guard a.runModal() == .alertFirstButtonReturn else { return }
        var map: [String: String] = [:]
        for (i, c) in cues.enumerated() {
            let n = String(i + 1)
            if !c.number.isEmpty, map[c.number.lowercased()] == nil { map[c.number.lowercased()] = n }
            c.number = n
        }
        for c in cues where c.type == .fade || c.type == .stop || c.type == .goto {
            if let n = map[c.target.lowercased()] { c.target = n }
        }
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
        MediaCache.shared.preload(cues)
        let left = MediaTools.missing(cues).count
        statusText = L("Fant {0} fil(er).", found) + (left > 0 ? L(" {0} mangler fortsatt.", left) : L(" Alle mediefiler er på plass."))
        updateDirty()
    }

    func collectShow() {
        NSApp.keyWindow?.makeFirstResponder(nil)
        guard let folder = pickFolder(L("Velg (eller lag) en mappe for showet og mediefilene")) else { return }
        var doc = ShowDocument()
        doc.cues = cues
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
        output.configure(screen: settings.videoScreen)
        if settings.videoScreen >= 0 { output.ensureVisible() }
        applyRemoteSettings()
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

    private func tick() {
        tickCount += 1
        if tickCount % 1800 == 0 { autosave() } // hvert 30. sekund
        if tickCount % 2 == 0, let m = audio.mixer {
            live.meterL = max(MeterMath.fraction(m.busLevel(0)), live.meterL - 0.05)
            live.meterR = max(MeterMath.fraction(m.busLevel(1)), live.meterR - 0.05)
        }
        if tickCount % 6 == 0, let f = feedback { f.update(buildStatus(), now: engine.now) }
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
