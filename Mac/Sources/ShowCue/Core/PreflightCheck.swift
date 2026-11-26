import Foundation

enum PreflightSeverity { case ok, warning, error }

/// Tilstanden til en mediefil: finnes og kan åpnes, finnes ikke, eller finnes men kan ikke leses.
enum PreflightFileStatus { case ok, missing, unreadable }

/// Ett funn i forhåndssjekken. `code` er en fast kode (ikke oversatt) som tester og andre programdeler kan bruke;
/// `message` er teksten som vises. `cueIndex` er raden i cuelista (fra 0), eller −1 når funnet ikke gjelder én bestemt cue.
struct PreflightFinding: Equatable {
    let severity: PreflightSeverity
    let code: String
    let message: String
    var cueIndex = -1
}

/// Det sjekken trenger å vite om maskinen. Egen abstraksjon, så sjekklogikken kan testes uten filer, lydkort og skjermer.
protocol PreflightEnvironment {
    func fileStatus(_ path: String) -> PreflightFileStatus
    /// Finnes lydutgangen med denne CoreAudio-UID-en?
    func audioOutputExists(uid: String) -> Bool
    /// Finnes opptaksenheten med denne UID-en?
    func inputDeviceExists(uid: String) -> Bool
    /// Finnes skjermen med dette nummeret (fra 0)?
    func screenExists(_ index: Int) -> Bool
}

/// Forhåndssjekk før show: går gjennom cuene og maskininnstillingene og lager en liste med funn.
/// Ren logikk uten vinduer; alt som krever maskinen går via `PreflightEnvironment`.
/// Deaktiverte cuer hoppes over (GO hopper over dem), men cue-numrene deres telles med i sjekken for duplikater.
enum PreflightCheck {
    static let mediaEmpty = "mediaEmpty"
    static let mediaMissing = "mediaMissing"
    static let mediaUnreadable = "mediaUnreadable"
    static let listEmpty = "listEmpty"
    static let mediaOk = "mediaOk"
    static let valueInvalid = "valueInvalid"
    static let requiredEmpty = "requiredEmpty"
    static let urlInvalid = "urlInvalid"
    static let timeOrder = "timeOrder"
    static let startBeyondFile = "startBeyondFile"
    static let memoEmpty = "memoEmpty"
    static let targetEmpty = "targetEmpty"
    static let targetMissing = "targetMissing"
    static let targetSelf = "targetSelf"
    static let targetDisabled = "targetDisabled"
    static let groupNoMatch = "groupNoMatch"
    static let clockTriggerOff = "clockTriggerOff"
    static let valuesOk = "valuesOk"
    static let audioDeviceMissing = "audioDeviceMissing"
    static let previewDeviceMissing = "previewDeviceMissing"
    static let inputDeviceMissing = "inputDeviceMissing"
    static let screenMissing = "screenMissing"
    static let devicesOk = "devicesOk"
    static let numberDuplicate = "numberDuplicate"
    static let sceneMissing = "sceneMissing"
    static let numbersOk = "numbersOk"

    /// Kjører hele sjekken. Rekkefølgen er mediefiler, verdier og mål, enheter, cue-numre; en gruppe uten funn får én «ok»-linje.
    static func run(cues: [Cue], settings: AppSettings, environment env: PreflightEnvironment, scenes: [String] = []) -> [PreflightFinding] {
        var media: [PreflightFinding] = [], values: [PreflightFinding] = [], devices: [PreflightFinding] = [], numbers: [PreflightFinding] = []
        var fileCount = 0
        for (i, c) in cues.enumerated() where !c.disabled {
            fileCount += checkMedia(c, i, env, &media)
            checkValues(c, i, cues, &values, scenes)
        }
        checkClockTriggers(cues, settings, &values)
        checkDevices(cues, settings, env, &devices)
        checkNumbers(cues, &numbers)

        var all = media
        if media.isEmpty {
            all.append(PreflightFinding(severity: .ok, code: mediaOk,
                                        message: fileCount == 0 ? L("Showet bruker ingen mediefiler.") : L("{0} mediefil(er) finnes og kan leses.", fileCount)))
        }
        all += values
        if values.isEmpty { all.append(PreflightFinding(severity: .ok, code: valuesOk, message: L("Ingen ugyldige verdier eller mål."))) }
        all += devices
        if devices.isEmpty { all.append(PreflightFinding(severity: .ok, code: devicesOk, message: L("Lydutgang og skjermer finnes."))) }
        all += numbers
        if numbers.isEmpty { all.append(PreflightFinding(severity: .ok, code: numbersOk, message: L("Ingen duplikate cue-numre."))) }
        return all
    }

    static func count(_ findings: [PreflightFinding], _ severity: PreflightSeverity) -> Int {
        findings.filter { $0.severity == severity }.count
    }

    /// «Cue 3 «Navn»», eller raden hvis cuen ikke har nummer.
    private static func label(_ c: Cue, _ index: Int) -> String {
        c.number.trimmingCharacters(in: .whitespaces).isEmpty
            ? L("Rad {0} «{1}»", index + 1, c.displayName)
            : L("Cue {0} «{1}»", c.number, c.displayName)
    }

    /// Tall til tekst med opptil to desimaler (samme som på Windows).
    private static func num(_ v: Double) -> String {
        let f = NumberFormatter()
        f.minimumFractionDigits = 0
        f.maximumFractionDigits = 2
        f.minimumIntegerDigits = 1
        return f.string(from: NSNumber(value: v)) ?? "\(v)"
    }

    private static func add(_ list: inout [PreflightFinding], _ severity: PreflightSeverity, _ code: String, _ message: String, _ index: Int) {
        list.append(PreflightFinding(severity: severity, code: code, message: message, cueIndex: index))
    }

    // MARK: Mediefiler

    /// Sjekker filene cuen peker på. Returnerer antall filer som ble sjekket.
    private static func checkMedia(_ c: Cue, _ index: Int, _ env: PreflightEnvironment, _ list: inout [PreflightFinding]) -> Int {
        let lbl = label(c, index)
        if c.isFileCue && c.filePath.trimmingCharacters(in: .whitespaces).isEmpty {
            add(&list, .error, mediaEmpty, L("{0}: ingen fil er valgt.", lbl), index)
        }
        if c.usesFileList && !c.files.contains(where: { !$0.trimmingCharacters(in: .whitespaces).isEmpty }) {
            add(&list, .warning, listEmpty, L("{0}: listen har ingen filer.", lbl), index)
        }
        let paths = MediaTools.mediaPaths([c])
        for p in paths {
            switch env.fileStatus(p) {
            case .missing: add(&list, .error, mediaMissing, L("{0}: mediefila finnes ikke: {1}", lbl, p), index)
            case .unreadable: add(&list, .error, mediaUnreadable, L("{0}: mediefila kan ikke leses: {1}", lbl, p), index)
            case .ok: break
            }
        }
        return paths.count
    }

    // MARK: Verdier og mål

    private static func checkValues(_ c: Cue, _ index: Int, _ all: [Cue], _ list: inout [PreflightFinding], _ scenes: [String] = []) {
        let lbl = label(c, index)
        func time(_ field: String, _ v: Double) {
            if !v.isFinite || v < 0 { add(&list, .error, valueInvalid, L("{0}: «{1}» er ikke en gyldig tid.", lbl, field), index) }
        }
        func level(_ field: String, _ v: Double) {
            if !v.isFinite { add(&list, .error, valueInvalid, L("{0}: «{1}» er ikke et gyldig tall.", lbl, field), index) }
        }
        func required(_ field: String, _ v: String) {
            if v.trimmingCharacters(in: .whitespaces).isEmpty { add(&list, .error, requiredEmpty, L("{0}: «{1}» må fylles ut.", lbl, field), index) }
        }
        func url(_ u: String, web: Bool) {
            if !(web ? isWebAddress(u) : isHttpUrl(u)) { add(&list, .error, urlInvalid, L("{0}: nettadressen er ugyldig: {1}", lbl, u), index) }
        }

        time(L("Forsinkelse"), c.preWait)

        let trimmable = c.type == .audio || c.type == .video
        if trimmable {
            time(L("Starttid"), c.startTime)
            time(L("Sluttid"), c.endTime)
            time(L("Fade inn"), c.fadeIn)
            time(L("Fade ut"), c.fadeOut)
            let length = c.type == .audio ? c.fileDuration : c.knownDuration
            if c.startTime.isFinite && c.endTime.isFinite {
                if c.endTime > 0 && c.endTime <= c.startTime {
                    add(&list, .error, timeOrder, L("{0}: sluttiden ({1} s) må være etter starttiden ({2} s).", lbl, num(c.endTime), num(c.startTime)), index)
                } else if length > 0 && c.startTime >= length {
                    add(&list, .error, startBeyondFile, L("{0}: starttiden ({1} s) er etter slutten av fila ({2} s).", lbl, num(c.startTime), num(length)), index)
                }
            }
        } else if c.type == .image || c.type == .pdf || c.type == .web {
            time(L("Fade inn"), c.fadeIn)
            time(L("Fade ut"), c.fadeOut)
        }
        if c.type == .image || c.type == .pdf || c.type == .web { time(L("Visningstid"), c.holdTime) }
        if audioTypes.contains(c.type) { level(L("Nivå"), c.volumeDb) }

        switch c.type {
        case .web:
            url(c.url, web: true)
        case .playlist:
            time(L("Krysstoning"), c.crossfade)
            time(L("Fade ut"), c.fadeOut)
        case .slideshow:
            time(L("Tid per bilde"), c.slideTime)
            time(L("Krysstoning"), c.crossfade)
        case .input:
            time(L("Fade inn"), c.fadeIn)
            time(L("Fade ut"), c.fadeOut)
        case .fade:
            time(L("Fade-tid"), c.fadeTime)
            level(L("Mål-nivå"), c.targetDb)
            level(L("Mål-synlighet"), c.targetOpacity)
            checkTarget(c, index, required: true, all, &list)
        case .stop:
            time(L("Fade-tid"), c.fadeTime)
            checkTarget(c, index, required: false, all, &list)
        case .goto:
            checkGoto(c, index, all, scenes, &list)
        case .group:
            checkGroup(c, index, all, &list)
        case .osc:
            required(L("Adresse til mottaker"), c.host)
            required(L("OSC-adresse"), c.address)
        case .network:
            if c.networkProtocol == .http { url(c.url, web: false) } else { required(L("Adresse til mottaker"), c.host) }
        case .light:
            required(L("Adresse til mottaker"), c.host)
            time(L("Fade-tid"), c.fadeTime)
        case .timecode:
            time(L("Varighet"), c.length)
        case .midi:
            if c.messageType == .mscGo { required(L("MSC cue-nummer"), c.mscCueNumber) }
        case .memo:
            if c.text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                add(&list, .warning, memoEmpty, L("{0}: beskjeden er tom.", lbl), index)
            }
        default: break
        }
    }

    /// Mål-cuen slås opp på nummer, uten hensyn til store/små bokstaver (som i GO-motoren).
    private static func checkTarget(_ c: Cue, _ index: Int, required: Bool, _ all: [Cue], _ list: inout [PreflightFinding],
                                    target: String? = nil, home: String? = nil) {
        let lbl = label(c, index)
        let t = (target ?? c.target).trimmingCharacters(in: .whitespaces)
        if t.isEmpty {
            if required { add(&list, .error, targetEmpty, L("{0}: mål-cue er ikke valgt.", lbl), index) }
            return
        }
        let hits = SceneLogic.matching(all, t, home: home ?? c.scene)
        if hits.isEmpty {
            add(&list, .error, targetMissing, L("{0}: mål-cuen «{1}» finnes ikke.", lbl, t), index)
        } else if hits.allSatisfy({ $0 === c }) {
            add(&list, .warning, targetSelf, L("{0}: peker på seg selv.", lbl), index)
        } else if hits.allSatisfy({ $0.disabled }) {
            add(&list, .warning, targetDisabled, L("{0}: mål-cuen «{1}» er deaktivert.", lbl, t), index)
        }
    }

    /// «Gå til»: scenen må finnes, og nummeret søkes i målscenen. Bare scene (uten nummer) er lov.
    private static func checkGoto(_ g: Cue, _ index: Int, _ all: [Cue], _ scenes: [String], _ list: inout [PreflightFinding]) {
        let scene = g.targetScene
        if !scene.isEmpty, SceneLogic.canonical(scenes, all, scene) == nil {
            add(&list, .error, sceneMissing, L("{0}: scenen «{1}» finnes ikke.", label(g, index), scene), index)
            return
        }
        if !scene.isEmpty && g.target.trimmingCharacters(in: .whitespaces).isEmpty { return }
        checkTarget(g, index, required: true, all, &list, target: SceneLogic.gotoSpec(target: g.target, targetScene: scene), home: scene.isEmpty ? nil : scene)
    }

    /// Gruppe-cue: listen må ha mål, målene må finnes, og minst én cue må treffes. Deaktiverte mål gir advarsel (GO hopper over dem).
    private static func checkGroup(_ g: Cue, _ index: Int, _ all: [Cue], _ list: inout [PreflightFinding]) {
        let lbl = label(g, index)
        if g.targets.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            add(&list, .error, targetEmpty, L("{0}: gruppen har ingen mål-cuer.", lbl), index)
            return
        }
        let r = CueTargets.resolve(all, g.targets, excluding: g)
        if !r.missing.isEmpty {
            add(&list, .error, targetMissing, L("{0}: mål-cuene finnes ikke: {1}", lbl, r.missing.joined(separator: ", ")), index)
        } else if r.cues.isEmpty {
            add(&list, .error, groupNoMatch, L("{0}: gruppen treffer ingen cuer.", lbl), index)
        }
        let disabled = r.cues.filter { $0.disabled }
        if !disabled.isEmpty {
            add(&list, .warning, targetDisabled, L("{0}: mål-cuene er deaktivert: {1}", lbl, disabled.map { $0.number }.joined(separator: ", ")), index)
        }
    }

    /// «https://…» eller «http://…» med noe etter, uten mellomrom.
    static func isHttpUrl(_ url: String) -> Bool {
        let u = url.trimmingCharacters(in: .whitespacesAndNewlines)
        let lower = u.lowercased()
        let rest: Substring
        if lower.hasPrefix("http://") { rest = u.dropFirst(7) }
        else if lower.hasPrefix("https://") { rest = u.dropFirst(8) }
        else { return false }
        return !rest.isEmpty && !rest.contains(where: { $0.isWhitespace })
    }

    /// Adresse en nettside-cue kan vise: http(s)-adresse, file-adresse, eller en adresse uten «://» (f.eks. «www.x.no» eller en filsti).
    static func isWebAddress(_ url: String) -> Bool {
        let u = url.trimmingCharacters(in: .whitespacesAndNewlines)
        if u.isEmpty { return false }
        if !u.contains("://") { return true }
        return isHttpUrl(u) || (u.lowercased().hasPrefix("file://") && u.count > 7)
    }

    // MARK: Enheter

    /// Cuetyper som sender lyd gjennom lydpatchen.
    private static let audioTypes: [CueType] = [.audio, .video, .playlist, .slideshow, .input]

    private static func screen(of c: Cue) -> Int? { c.isVisual || c.type == .slideshow ? c.outputScreen : nil }

    /// Cuer med klokketrigger gjør ingenting når «Klokke-triggere aktive» er av i innstillingene.
    private static func checkClockTriggers(_ cues: [Cue], _ settings: AppSettings, _ list: inout [PreflightFinding]) {
        if settings.clockTriggersEnabled { return }
        let withTrigger = cues.enumerated().filter { !$0.element.disabled && !$0.element.clockTrigger.isEmpty }
        guard let first = withTrigger.first else { return }
        add(&list, .warning, clockTriggerOff,
            L("{0} cue(r) har klokketrigger, men «Klokke-triggere aktive» er av i innstillingene. Cuene starter ikke av seg selv.", withTrigger.count), first.offset)
    }

    private static func checkDevices(_ cues: [Cue], _ s: AppSettings, _ env: PreflightEnvironment, _ list: inout [PreflightFinding]) {
        let active = cues.enumerated().filter { !$0.element.disabled }

        // Lydutgang: bare relevant når showet har cuer som lager lyd. Tom UID = standardenheten, som alltid finnes.
        var usesAudio = false
        for x in active where audioTypes.contains(x.element.type) || (x.element.type == .timecode && x.element.sendLtc) { usesAudio = true }
        if usesAudio && !s.audioDeviceUID.isEmpty && !env.audioOutputExists(uid: s.audioDeviceUID) {
            add(&list, .error, audioDeviceMissing, L("Valgt lydutgang finnes ikke. Velg lydutgang under Innstillinger."), -1)
        }
        if !s.previewDeviceUID.isEmpty && !env.audioOutputExists(uid: s.previewDeviceUID) {
            add(&list, .warning, previewDeviceMissing, L("Valgt lydutgang for forhåndslytting finnes ikke."), -1)
        }
        let inputs = active.filter { $0.element.type == .input }.count
        if inputs > 0 && !s.inputDeviceUID.isEmpty && !env.inputDeviceExists(uid: s.inputDeviceUID) {
            add(&list, .error, inputDeviceMissing, L("Valgt lydinngang finnes ikke, men {0} live-inngang-cue(r) bruker den.", inputs), -1)
        }

        // Skjermer: finnes ikke skjermen, vises cuen i et vanlig vindu. Derfor advarsel og ikke feil.
        var screens: [(cue: Cue, index: Int, screen: Int)] = []
        for x in active { if let sc = screen(of: x.element) { screens.append((cue: x.element, index: x.offset, screen: sc)) } }
        if s.videoScreen >= 0 && screens.contains(where: { $0.screen == -2 }) && !env.screenExists(s.videoScreen) {
            add(&list, .warning, screenMissing, L("Skjerm {0} fra innstillingene finnes ikke. Visuelle cuer uten egen skjerm vises i et vanlig vindu.", s.videoScreen + 1), -1)
        }
        for x in screens where x.screen >= 0 && !env.screenExists(x.screen) {
            add(&list, .warning, screenMissing, L("{0}: skjerm {1} finnes ikke. Cuen vises i et vanlig vindu.", label(x.cue, x.index), x.screen + 1), x.index)
        }
    }

    // MARK: Cue-numre

    /// Samme nummer i ulike scener er lov; to cuer med samme nummer i én scene (eller i et show uten scener) gir feil.
    private static func checkNumbers(_ cues: [Cue], _ list: inout [PreflightFinding]) {
        var order: [String] = [], rows: [String: [Int]] = [:], shown: [String: String] = [:], sceneOf: [String: String] = [:]
        for (i, c) in cues.enumerated() {
            let n = c.number.trimmingCharacters(in: .whitespaces)
            if n.isEmpty { continue }
            let key = n.lowercased() + "\u{1}" + c.scene.lowercased()
            if rows[key] == nil { order.append(key); shown[key] = n; sceneOf[key] = c.scene }
            rows[key, default: []].append(i)
        }
        for key in order {
            guard let r = rows[key], r.count > 1 else { continue }
            let rowText = r.map { String($0 + 1) }.joined(separator: ", ")
            let scene = sceneOf[key] ?? ""
            let message = scene.isEmpty
                ? L("Cue-nummer «{0}» brukes av {1} cuer (rad {2}).", shown[key] ?? key, r.count, rowText)
                : L("Cue-nummer «{0}» brukes av {1} cuer i scenen «{2}» (rad {3}).", shown[key] ?? key, r.count, scene, rowText)
            add(&list, .error, numberDuplicate, message, r[0])
        }
    }
}
