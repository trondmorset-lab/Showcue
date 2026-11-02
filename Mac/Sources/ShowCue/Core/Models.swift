import Foundation
import Combine

// MARK: - Enums (tekstverdiene er de samme som i Windows-versjonen, så showfilene kan deles)

enum CueType: String, Codable, CaseIterable {
    case audio, video, image, pdf, fade, stop, osc, midi, playlist, goto, memo, network, timecode, light, input, web
}

/// Hvordan en nettverks-cue sender.
enum NetworkProtocol: String, Codable, CaseIterable {
    case udp = "Udp", tcp = "Tcp", http = "Http"

    var label: String {
        switch self {
        case .udp: return "UDP"
        case .tcp: return "TCP"
        case .http: return L("HTTP-forespørsel")
        }
    }
}

/// Formen på en fade: lineær i dB (jevn for øret), S-kurve (myk start og slutt) eller lineær amplitude.
enum FadeCurve: String, Codable, CaseIterable {
    case db = "Db", sCurve = "SCurve", linear = "Linear"

    var label: String {
        switch self {
        case .db: return L("Jevn (lineær i dB)")
        case .sCurve: return L("S-kurve (myk start og slutt)")
        case .linear: return L("Lineær amplitude (som en fader)")
        }
    }

    /// Nivå (dB) `p` (0–1) inn i en fade fra `from` til `to`.
    func db(from: Double, to: Double, at p: Double) -> Double {
        switch self {
        case .sCurve: return from + (to - from) * p * p * (3 - 2 * p)
        case .linear:
            let g = Double(dbToGain(from)) + (Double(dbToGain(to)) - Double(dbToGain(from))) * p
            return g <= 0.00001 ? Voice.silenceDb : max(Voice.silenceDb, 20 * log10(g))
        case .db: return from + (to - from) * p
        }
    }
}

enum ContinueMode: String, Codable, CaseIterable {
    case none = "None", autoContinue = "AutoContinue", autoFollow = "AutoFollow"

    var label: String {
        switch self {
        case .none: return L("Ingen")
        case .autoContinue: return L("Auto-continue (start neste samtidig)")
        case .autoFollow: return L("Auto-follow (start neste når ferdig)")
        }
    }
    var glyph: String {
        switch self {
        case .none: return ""
        case .autoContinue: return "↓"
        case .autoFollow: return "↧"
        }
    }
}

enum VisualStretch: String, Codable, CaseIterable {
    case uniform = "Uniform", uniformToFill = "UniformToFill", fill = "Fill", none = "None"

    var label: String {
        switch self {
        case .uniform: return L("Tilpass (behold format)")
        case .uniformToFill: return L("Fyll (beskjær)")
        case .fill: return L("Strekk")
        case .none: return L("Originalstørrelse")
        }
    }
}

enum MidiMessageType: String, Codable, CaseIterable {
    case noteOn = "NoteOn", noteOff = "NoteOff", programChange = "ProgramChange", controlChange = "ControlChange", mscGo = "MscGo"

    var label: String {
        switch self {
        case .noteOn: return L("Note on")
        case .noteOff: return L("Note off")
        case .programChange: return L("Program change")
        case .controlChange: return L("Control change (CC)")
        case .mscGo: return L("MSC GO (MIDI Show Control)")
        }
    }
}

enum CueColor: String, Codable, CaseIterable {
    case none = "None", red = "Red", orange = "Orange", yellow = "Yellow", green = "Green", blue = "Blue", purple = "Purple", gray = "Gray"

    var label: String {
        switch self {
        case .none: return L("Ingen")
        case .red: return L("Rød")
        case .orange: return L("Oransje")
        case .yellow: return L("Gul")
        case .green: return L("Grønn")
        case .blue: return L("Blå")
        case .purple: return L("Lilla")
        case .gray: return L("Grå")
        }
    }
}

// MARK: - Routing

/// Ett krysspunkt i routingmatrisen: kanal i fila → utgang (buss) i lydpatchen. Begge fra 1.
struct AudioRoute: Codable, Hashable {
    var source: Int
    var bus: Int
    var gainDb: Double

    init(_ source: Int, _ bus: Int, _ gainDb: Double = 0) {
        self.source = source; self.bus = bus; self.gainDb = gainDb
    }

    enum CodingKeys: String, CodingKey { case source = "Source", bus = "Bus", gainDb = "GainDb" }

    /// Mono → utgang 1 og 2, ellers kanal n → utgang n.
    static func defaults(fileChannels: Int) -> [AudioRoute] {
        if fileChannels <= 1 { return [AudioRoute(1, 1), AudioRoute(1, 2)] }
        return (1...fileChannels).map { AudioRoute($0, $0) }
    }

    /// Alt til ett stereopar (f.eks. hodetelefoner): mono → begge, ellers annenhver kanal venstre/høyre.
    /// Flerkanals filer dempes 3 dB så summen ikke blir for sterk.
    static func downmix(fileChannels: Int, leftBus: Int) -> [AudioRoute] {
        if fileChannels <= 1 { return [AudioRoute(1, leftBus), AudioRoute(1, leftBus + 1)] }
        let gain: Double = fileChannels > 2 ? -3 : 0
        return (1...fileChannels).map { AudioRoute($0, leftBus + ($0 - 1) % 2, gain) }
    }
}

// MARK: - Cue

/// Én cue. Alle typer deler klasse (enklere for SwiftUI og JSON); `type` bestemmer hvilke felt som brukes.
final class Cue: ObservableObject, Identifiable, Codable {
    let id = UUID()
    let type: CueType

    // Felles
    @Published var number = ""
    @Published var name = ""
    @Published var notes = ""
    @Published var preWait: Double = 0
    @Published var continueMode: ContinueMode = .none
    @Published var hotkey = "" { didSet { let u = hotkey.uppercased(); if u != hotkey { hotkey = u } } }
    @Published var disabled = false
    @Published var color: CueColor = .none
    /// "N:60", "CC:20", "PC:5" eller tom.
    @Published var midiTrigger = ""
    /// Vis cuen som knapp i knappepanelet. Cuer med hurtigtast vises der uansett.
    @Published var inButtonPanel = false
    /// Start cuen når innkommende tidskode (MTC) passerer dette tidspunktet, «tt:mm:ss:ff». Tom = ingen.
    @Published var timecodeTrigger = ""

    // Filer (lyd, video, bilde, PDF)
    @Published var filePath = ""

    // Lyd og videolyd
    @Published var volumeDb: Double = 0
    @Published var startTime: Double = 0
    @Published var endTime: Double = 0
    @Published var loop = false
    @Published var fadeIn: Double = 0
    @Published var fadeOut: Double = 0
    @Published var fileDuration: Double = 0
    /// Kanaler i lydfila / lydsporet. For video: 0 = ingen lyd, -1 = ikke undersøkt.
    @Published var fileChannels = 0
    /// nil = standard routing, tom = alt av.
    @Published var routes: [AudioRoute]? = nil
    /// Målt lydstyrke (LUFS, integrert) mellom start og slutt, før nivået. nil = ikke målt.
    @Published var loudness: Double? = nil

    // Visuelt
    @Published var replaceOthers = true
    @Published var stretch: VisualStretch = .uniform
    @Published var knownDuration: Double = 0
    @Published var holdTime: Double = 0
    @Published var page = 1

    // Fade / stopp
    @Published var target = ""
    @Published var fadeTime: Double = 0
    @Published var fadeAudio = true
    @Published var targetDb: Double = -60
    @Published var fadeVisual = true
    @Published var targetOpacity: Double = 0
    @Published var stopWhenDone = true
    @Published var curve: FadeCurve = .db
    /// Stopp-cue: slipp loopen i stedet for å stoppe («devamp»).
    @Published var release = false

    // Gå til
    /// Kjør mål-cuen også (GO), ikke bare flytt markøren.
    @Published var fire = false

    // Spilleliste
    @Published var files: [String] = []
    @Published var shuffle = false
    /// Krysstoning mellom sporene (sekunder).
    @Published var crossfade: Double = 2

    // OSC
    @Published var host = "127.0.0.1"
    @Published var port = 8000
    @Published var address = "/"
    @Published var arguments = ""

    // Memo
    @Published var text = ""

    // Nettverk (bruker også host og port)
    @Published var networkProtocol: NetworkProtocol = .udp
    /// Teksten som sendes (UDP/TCP) eller innholdet i forespørselen (HTTP). \r \n \t \\ og \xHH tolkes.
    @Published var message = ""
    @Published var url = "http://"
    @Published var method = "GET"

    // Tidskode (bruker også device for MIDI-utgang)
    @Published var tcStart = "01:00:00:00"
    @Published var rate: TimecodeRate = .fps25
    @Published var sendMtc = true
    @Published var sendLtc = false
    @Published var ltcBus = 1
    @Published var ltcLevelDb: Double = -12
    /// Hvor lenge tidskoden går (sekunder). 0 = til cuen stoppes.
    @Published var length: Double = 0

    // Lys (bruker også host og fadeTime)
    @Published var universe = 0
    /// «kanal@verdi», f.eks. «1@255 2-4@50% 10@0».
    @Published var levels = "1@255"

    // MIDI
    @Published var device = ""
    @Published var messageType: MidiMessageType = .noteOn
    @Published var channel = 1
    @Published var data1 = 60
    @Published var data2 = 127
    @Published var mscDeviceId = 127
    @Published var mscCommandFormat = 1
    @Published var mscCueNumber = "1"

    // Kjøretid (lagres ikke)
    @Published var activeCount = 0
    @Published var progress: Double = 0
    @Published var isPreWaiting = false
    @Published var error: String? = nil
    var isRunning: Bool { activeCount > 0 }

    init(type: CueType) {
        self.type = type
        switch type {
        case .video:
            fadeIn = 0.5; fadeOut = 0.5; fileChannels = -1
        case .image, .pdf:
            fadeIn = 0.5; fadeOut = 0.5
        case .fade:
            fadeTime = 3
        case .playlist:
            loop = true; fadeOut = 3; fileChannels = 2
        case .network:
            port = 9000
        case .light:
            host = "255.255.255.255"; fadeTime = 2
        case .input:
            fadeIn = 0.5; fadeOut = 1; fileChannels = 2
        case .web:
            fadeIn = 0.5; fadeOut = 0.5; url = "https://"
        default: break
        }
    }

    // MARK: Egenskaper

    var isFileCue: Bool { [.audio, .video, .image, .pdf].contains(type) }
    var isVisual: Bool { [.video, .image, .pdf, .web].contains(type) }

    /// Nettside-cue: adressen som gyldig http(s)- eller fil-adresse. «www.x.no» får https:// foran.
    var webURL: URL? {
        let u = url.trimmingCharacters(in: .whitespaces)
        if !u.isEmpty, !u.contains("://"), FileManager.default.fileExists(atPath: u) { return URL(fileURLWithPath: u) }
        guard let parsed = URL(string: u.contains("://") ? u : "https://" + u), let scheme = parsed.scheme?.lowercased() else { return nil }
        if scheme == "file" { return parsed }
        return (scheme == "http" || scheme == "https") && !(parsed.host ?? "").isEmpty ? parsed : nil
    }
    var hasNoAudio: Bool { type == .video && fileChannels == 0 }

    var typeName: String {
        switch type {
        case .audio: return L("Lyd")
        case .video: return "Video"
        case .image: return L("Bilde")
        case .pdf: return "PDF"
        case .fade: return "Fade"
        case .stop: return L("Stopp")
        case .osc: return "OSC"
        case .midi: return "MIDI"
        case .playlist: return L("Spilleliste")
        case .goto: return L("Gå til")
        case .memo: return L("Beskjed")
        case .network: return L("Nettverk")
        case .timecode: return L("Tidskode")
        case .light: return L("Lys")
        case .input: return L("Inngang")
        case .web: return L("Nettside")
        }
    }

    var typeIcon: String {
        switch type {
        case .audio: return "♪"
        case .video: return "▶"
        case .image: return "▣"
        case .pdf: return "▤"
        case .fade: return "◢"
        case .stop: return "■"
        case .osc: return "⇢"
        case .midi: return "♫"
        case .playlist: return "☰"
        case .goto: return "↪"
        case .memo: return "✎"
        case .network: return "⇄"
        case .timecode: return "⏱"
        case .light: return "✺"
        case .input: return "◉"
        case .web: return "◍"
        }
    }

    var fileName: String { Cue.lastPathComponent(filePath) }
    private var fileStem: String {
        let n = fileName
        if let dot = n.lastIndex(of: "."), dot > n.startIndex { return String(n[..<dot]) }
        return n
    }
    private var fileLabel: String { filePath.isEmpty ? L("(ingen fil)") : fileName }

    /// Fungerer både for Mac-stier og Windows-stier (fra show laget på Windows).
    static func lastPathComponent(_ path: String) -> String {
        let parts = path.split(whereSeparator: { $0 == "/" || $0 == "\\" })
        return parts.last.map(String.init) ?? path
    }

    var displayName: String {
        if !name.trimmingCharacters(in: .whitespaces).isEmpty { return name }
        switch type {
        case .audio, .video, .image: return fileStem.isEmpty ? typeName : fileStem
        case .pdf: return (fileStem.isEmpty ? typeName : fileStem) + " s.\(page)"
        case .fade: return target.isEmpty ? "Fade" : L("Fade cue {0}", target)
        case .stop:
            if release { return (L("Slipp loop ") + target).trimmingCharacters(in: .whitespaces) }
            return target.isEmpty ? L("Stopp alt") : L("Stopp cue {0}", target)
        case .osc: return "OSC " + address
        case .midi: return "MIDI"
        case .playlist: return L("Spilleliste")
        case .goto: return target.isEmpty ? L("Gå til") : L("Gå til {0}", target)
        case .memo: return memoFirstLine.isEmpty ? L("Beskjed") : memoFirstLine
        case .network:
            return networkProtocol == .http ? "HTTP \(method)" : "\(networkProtocol.rawValue.uppercased()) \(host)"
        case .timecode: return L("Tidskode ") + tcStart
        case .light: return L("Lys")
        case .input: return "Live-inngang"
        case .web: return webURL.flatMap { $0.isFileURL ? nil : $0.host } ?? L("Nettside")
        }
    }

    var summary: String {
        switch type {
        case .audio:
            var s = fileLabel
            if loop { s += "  ⟲" }
            if volumeDb != 0 { s += "  " + Cue.signedDb(volumeDb) + " dB" }
            if let r = routes { s += "  → " + Cue.routeSummary(r) }
            return s
        case .video:
            var s = fileLabel
            if loop { s += "  ⟲" }
            if fileChannels == 0 { s += L("  (ingen lyd)") }
            else if let r = routes, r.isEmpty { s += L("  (lyd av)") }
            else if volumeDb != 0 { s += "  " + Cue.signedDb(volumeDb) + " dB" }
            return s
        case .image:
            return fileLabel
        case .pdf:
            return "\(fileLabel)  s. \(page)"
        case .fade:
            var s = target.isEmpty ? L("(mål mangler)") : "→ " + target
            if fadeAudio { s += targetDb <= -60 ? "  −∞ dB" : "  " + Cue.fmt(targetDb) + " dB" }
            if stopWhenDone { s += L("  + stopp") }
            return s
        case .stop:
            return (target.isEmpty ? L("Alle cues") : "→ " + target)
                + (release ? L("  slipp loop") : fadeTime > 0 ? L("  fade {0}s", Cue.fmt(fadeTime)) : "")
        case .playlist:
            return L("{0} spor", files.count) + (shuffle ? L("  tilfeldig") : "") + (loop ? "  ⟲" : "")
                + (crossfade > 0 ? L("  krysstoning {0}s", Cue.fmt(crossfade)) : "")
        case .goto:
            return (target.isEmpty ? L("(mål mangler)") : "→ " + target) + (fire ? "  + GO" : "")
        case .memo:
            return memoFirstLine
        case .timecode:
            return L("{0} @ {1} fps", tcStart, rate.label.split(separator: " ")[0]) + (sendMtc ? "  MTC" : "") + (sendLtc ? L("  LTC → ut {0}", ltcBus) : "")
        case .light:
            return L("Univers {0}  {1}", universe, DmxLevels.summary(levels)) + (fadeTime > 0 ? L("  fade {0}s", Cue.fmt(fadeTime)) : "")
        case .web:
            return url
        case .input:
            var s = L("{0} kanal(er)", fileChannels)
            if volumeDb != 0 { s += "  " + Cue.signedDb(volumeDb) + " dB" }
            if let r = routes { s += "  → " + Cue.routeSummary(r) }
            return s
        case .network:
            if networkProtocol == .http { return "\(method) \(url)" }
            return "\(networkProtocol.rawValue.uppercased()) \(host):\(port)  \(message)".trimmingCharacters(in: .whitespaces)
        case .osc:
            return "\(host):\(port)  \(address) \(arguments)".trimmingCharacters(in: .whitespaces)
        case .midi:
            let s: String
            switch messageType {
            case .noteOn: s = L("Note on  ch{0}  {1} / {2}", channel, data1, data2)
            case .noteOff: s = L("Note off  ch{0}  {1}", channel, data1)
            case .programChange: s = L("Program  ch{0}  {1}", channel, data1)
            case .controlChange: s = "CC  ch\(channel)  \(data1) = \(data2)"
            case .mscGo: s = L("MSC GO  cue {0}", mscCueNumber)
            }
            return s + (device.isEmpty ? "" : "  [\(device)]")
        }
    }

    var memoFirstLine: String {
        String(text.split(separator: "\n", maxSplits: 1, omittingEmptySubsequences: false).first ?? "").trimmingCharacters(in: .whitespaces)
    }

    /// Lengden på hele fila (lyd eller video), 0 = ukjent.
    var mediaLength: Double { type == .video ? knownDuration : fileDuration }

    /// Sluttpunktet i en video (0 = ukjent).
    var videoEnd: Double {
        endTime > 0 ? (knownDuration > 0 ? min(endTime, knownDuration) : endTime) : knownDuration
    }

    /// Varighet i sekunder, 0 = ukjent/uendelig.
    var duration: Double {
        switch type {
        case .audio:
            if loop { return 0 }
            let end = endTime > 0 ? min(endTime, fileDuration > 0 ? fileDuration : endTime) : fileDuration
            return max(0, end - startTime)
        case .video:
            let end = videoEnd
            return loop || end <= 0 ? 0 : max(0, end - startTime)
        case .image, .pdf, .web: return holdTime > 0 ? fadeIn + holdTime + fadeOut : 0
        case .fade: return fadeTime
        case .stop: return release ? 0 : fadeTime
        case .light: return fadeTime
        case .timecode: return length
        default: return 0
        }
    }

    var durationText: String { duration > 0 ? TimeFormat.format(duration) : "" }
    var preWaitText: String { preWait > 0 ? TimeFormat.format(preWait) : "" }
    var triggerText: String {
        [hotkey, MidiTriggers.describe(midiTrigger), timecodeTrigger.isEmpty ? "" : "TC " + timecodeTrigger]
            .filter { !$0.isEmpty }.joined(separator: " · ")
    }

    var startTimecode: Timecode { Timecode.parse(tcStart) ?? Timecode(1, 0, 0, 0) }

    func effectiveRoutes(fileChannels ch: Int) -> [AudioRoute] { routes ?? AudioRoute.defaults(fileChannels: ch) }

    static func routeSummary(_ r: [AudioRoute]) -> String {
        let buses = Array(Set(r.map { $0.bus })).sorted()
        return buses.isEmpty ? L("av") : L("ut ") + buses.map(String.init).joined(separator: ",")
    }

    private static let numberFormatter: NumberFormatter = {
        let f = NumberFormatter()
        f.locale = Locale(identifier: "nb_NO")
        f.minimumFractionDigits = 0
        f.maximumFractionDigits = 1
        return f
    }()
    static func fmt(_ v: Double) -> String { numberFormatter.string(from: NSNumber(value: v)) ?? "\(v)" }
    static func signedDb(_ v: Double) -> String { (v > 0 ? "+" : "") + fmt(v) }

    func clone() -> Cue {
        let data = try! JSONEncoder().encode(self)
        return try! JSONDecoder().decode(Cue.self, from: data)
    }

    // MARK: Codable – samme feltnavn som Windows-versjonen (PascalCase)

    enum K: String, CodingKey {
        case type, Number, Name, Notes, PreWait, ContinueMode, Hotkey, Disabled, Color, MidiTrigger
        case FilePath, VolumeDb, Volume, StartTime, EndTime, Loop, FadeIn, FadeOut, FileDuration, FileChannels, Routes
        case ReplaceOthers, Stretch, KnownDuration, HoldTime, Page
        case Target, FadeTime, FadeAudio, TargetDb, FadeVisual, TargetOpacity, StopWhenDone
        case Host, Port, Address, Arguments
        case Device, MessageType, Channel, Data1, Data2, MscDeviceId, MscCommandFormat, MscCueNumber
        case Curve, Release, Fire, Files, Shuffle, Crossfade
        case InButtonPanel, Loudness, Text, Message, Url, Method
        case TimecodeTrigger, Start, Rate, SendMtc, SendLtc, LtcBus, LtcLevelDb, Length, Universe, Levels
        case protocolKey = "Protocol"
    }

    convenience init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: K.self)
        let t = (try? c.decode(CueType.self, forKey: .type)) ?? .stop
        self.init(type: t)
        func v<T: Decodable>(_ k: K, _ def: T) -> T { ((try? c.decodeIfPresent(T.self, forKey: k)) ?? nil) ?? def }

        number = v(.Number, ""); name = v(.Name, ""); notes = v(.Notes, "")
        preWait = v(.PreWait, 0.0); continueMode = v(.ContinueMode, ContinueMode.none)
        hotkey = v(.Hotkey, ""); disabled = v(.Disabled, false); color = v(.Color, CueColor.none)
        midiTrigger = v(.MidiTrigger, "")
        filePath = v(.FilePath, "")
        volumeDb = v(.VolumeDb, 0.0)
        if t == .video, (try? c.decodeIfPresent(Double.self, forKey: .VolumeDb)) == nil,
           let pct = (try? c.decodeIfPresent(Double.self, forKey: .Volume)) ?? nil {
            // Eldre Windows-show lagret videovolum i prosent.
            volumeDb = pct <= 0.1 ? -60 : 20 * log10(min(pct, 100) / 100)
        }
        startTime = v(.StartTime, 0.0); endTime = v(.EndTime, 0.0); loop = v(.Loop, loop)
        fadeIn = v(.FadeIn, fadeIn); fadeOut = v(.FadeOut, fadeOut)
        fileDuration = v(.FileDuration, 0.0); fileChannels = v(.FileChannels, fileChannels)
        routes = (try? c.decodeIfPresent([AudioRoute].self, forKey: .Routes)) ?? nil
        replaceOthers = v(.ReplaceOthers, true); stretch = v(.Stretch, VisualStretch.uniform)
        knownDuration = v(.KnownDuration, 0.0); holdTime = v(.HoldTime, 0.0); page = max(1, v(.Page, 1))
        target = v(.Target, ""); fadeTime = v(.FadeTime, fadeTime); fadeAudio = v(.FadeAudio, true)
        targetDb = v(.TargetDb, -60.0); fadeVisual = v(.FadeVisual, true); targetOpacity = v(.TargetOpacity, 0.0)
        stopWhenDone = v(.StopWhenDone, true)
        host = v(.Host, "127.0.0.1"); port = v(.Port, 8000); address = v(.Address, "/"); arguments = v(.Arguments, "")
        device = v(.Device, ""); messageType = v(.MessageType, MidiMessageType.noteOn); channel = v(.Channel, 1)
        data1 = v(.Data1, 60); data2 = v(.Data2, 127); mscDeviceId = v(.MscDeviceId, 127)
        mscCommandFormat = v(.MscCommandFormat, 1); mscCueNumber = v(.MscCueNumber, "1")
        curve = v(.Curve, FadeCurve.db); release = v(.Release, false); fire = v(.Fire, false)
        files = v(.Files, [String]()); shuffle = v(.Shuffle, false); crossfade = v(.Crossfade, crossfade)
        inButtonPanel = v(.InButtonPanel, false); loudness = (try? c.decodeIfPresent(Double.self, forKey: .Loudness)) ?? nil
        text = v(.Text, ""); networkProtocol = v(.protocolKey, NetworkProtocol.udp); message = v(.Message, "")
        url = v(.Url, t == .web ? "https://" : "http://"); method = v(.Method, "GET").uppercased()
        if t == .network { port = v(.Port, 9000) }
        if t == .light { host = v(.Host, "255.255.255.255") }
        timecodeTrigger = Timecode.normalize(v(.TimecodeTrigger, ""))
        tcStart = Timecode.parse(v(.Start, "")).map(\.description) ?? "01:00:00:00"
        rate = v(.Rate, TimecodeRate.fps25); sendMtc = v(.SendMtc, true); sendLtc = v(.SendLtc, false)
        ltcBus = max(1, v(.LtcBus, 1)); ltcLevelDb = v(.LtcLevelDb, -12.0); length = v(.Length, 0.0)
        universe = v(.Universe, 0); levels = v(.Levels, "1@255")
    }

    func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: K.self)
        try c.encode(type, forKey: .type)
        try c.encode(number, forKey: .Number)
        try c.encode(name, forKey: .Name)
        try c.encode(notes, forKey: .Notes)
        try c.encode(preWait, forKey: .PreWait)
        try c.encode(continueMode, forKey: .ContinueMode)
        try c.encode(hotkey, forKey: .Hotkey)
        try c.encode(disabled, forKey: .Disabled)
        try c.encode(color, forKey: .Color)
        try c.encode(midiTrigger, forKey: .MidiTrigger)
        try c.encode(inButtonPanel, forKey: .InButtonPanel)
        try c.encode(timecodeTrigger, forKey: .TimecodeTrigger)
        switch type {
        case .audio:
            try c.encode(filePath, forKey: .FilePath)
            try c.encode(volumeDb, forKey: .VolumeDb)
            try c.encode(startTime, forKey: .StartTime)
            try c.encode(endTime, forKey: .EndTime)
            try c.encode(loop, forKey: .Loop)
            try c.encode(fadeIn, forKey: .FadeIn)
            try c.encode(fadeOut, forKey: .FadeOut)
            try c.encode(fileDuration, forKey: .FileDuration)
            try c.encode(fileChannels, forKey: .FileChannels)
            try c.encode(routes, forKey: .Routes)
            try c.encode(loudness, forKey: .Loudness)
        case .video, .image, .pdf:
            try c.encode(filePath, forKey: .FilePath)
            try c.encode(fadeIn, forKey: .FadeIn)
            try c.encode(fadeOut, forKey: .FadeOut)
            try c.encode(replaceOthers, forKey: .ReplaceOthers)
            try c.encode(stretch, forKey: .Stretch)
            if type == .video {
                try c.encode(volumeDb, forKey: .VolumeDb)
                try c.encode(startTime, forKey: .StartTime)
                try c.encode(endTime, forKey: .EndTime)
                try c.encode(loop, forKey: .Loop)
                try c.encode(knownDuration, forKey: .KnownDuration)
                try c.encode(fileChannels, forKey: .FileChannels)
                try c.encode(routes, forKey: .Routes)
            } else {
                try c.encode(holdTime, forKey: .HoldTime)
                if type == .pdf { try c.encode(page, forKey: .Page) }
            }
        case .fade:
            try c.encode(target, forKey: .Target)
            try c.encode(fadeTime, forKey: .FadeTime)
            try c.encode(fadeAudio, forKey: .FadeAudio)
            try c.encode(targetDb, forKey: .TargetDb)
            try c.encode(fadeVisual, forKey: .FadeVisual)
            try c.encode(targetOpacity, forKey: .TargetOpacity)
            try c.encode(stopWhenDone, forKey: .StopWhenDone)
            try c.encode(curve, forKey: .Curve)
        case .stop:
            try c.encode(target, forKey: .Target)
            try c.encode(fadeTime, forKey: .FadeTime)
            try c.encode(release, forKey: .Release)
        case .goto:
            try c.encode(target, forKey: .Target)
            try c.encode(fire, forKey: .Fire)
        case .playlist:
            try c.encode(files, forKey: .Files)
            try c.encode(shuffle, forKey: .Shuffle)
            try c.encode(loop, forKey: .Loop)
            try c.encode(crossfade, forKey: .Crossfade)
            try c.encode(volumeDb, forKey: .VolumeDb)
            try c.encode(fadeOut, forKey: .FadeOut)
            try c.encode(fileChannels, forKey: .FileChannels)
            try c.encode(routes, forKey: .Routes)
        case .web:
            try c.encode(url, forKey: .Url)
            try c.encode(fadeIn, forKey: .FadeIn)
            try c.encode(fadeOut, forKey: .FadeOut)
            try c.encode(replaceOthers, forKey: .ReplaceOthers)
            try c.encode(stretch, forKey: .Stretch)
            try c.encode(holdTime, forKey: .HoldTime)
        case .timecode:
            try c.encode(tcStart, forKey: .Start)
            try c.encode(rate, forKey: .Rate)
            try c.encode(sendMtc, forKey: .SendMtc)
            try c.encode(device, forKey: .Device)
            try c.encode(sendLtc, forKey: .SendLtc)
            try c.encode(ltcBus, forKey: .LtcBus)
            try c.encode(ltcLevelDb, forKey: .LtcLevelDb)
            try c.encode(length, forKey: .Length)
        case .light:
            try c.encode(host, forKey: .Host)
            try c.encode(universe, forKey: .Universe)
            try c.encode(levels, forKey: .Levels)
            try c.encode(fadeTime, forKey: .FadeTime)
        case .input:
            try c.encode(fileChannels, forKey: .FileChannels)
            try c.encode(routes, forKey: .Routes)
            try c.encode(volumeDb, forKey: .VolumeDb)
            try c.encode(fadeIn, forKey: .FadeIn)
            try c.encode(fadeOut, forKey: .FadeOut)
        case .memo:
            try c.encode(text, forKey: .Text)
        case .network:
            try c.encode(networkProtocol, forKey: .protocolKey)
            try c.encode(host, forKey: .Host)
            try c.encode(port, forKey: .Port)
            try c.encode(message, forKey: .Message)
            try c.encode(url, forKey: .Url)
            try c.encode(method, forKey: .Method)
        case .osc:
            try c.encode(host, forKey: .Host)
            try c.encode(port, forKey: .Port)
            try c.encode(address, forKey: .Address)
            try c.encode(arguments, forKey: .Arguments)
        case .midi:
            try c.encode(device, forKey: .Device)
            try c.encode(messageType, forKey: .MessageType)
            try c.encode(channel, forKey: .Channel)
            try c.encode(data1, forKey: .Data1)
            try c.encode(data2, forKey: .Data2)
            try c.encode(mscDeviceId, forKey: .MscDeviceId)
            try c.encode(mscCommandFormat, forKey: .MscCommandFormat)
            try c.encode(mscCueNumber, forKey: .MscCueNumber)
        }
    }
}

// MARK: - Showfil

/// Innholdet i en .showcue-fil (samme format som Windows-versjonen).
struct ShowDocument: Codable {
    var formatVersion = 1
    var title = L("Nytt show")
    var cues: [Cue] = []

    enum CodingKeys: String, CodingKey { case formatVersion = "FormatVersion", title = "Title", cues = "Cues" }

    init() {}

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        formatVersion = (try? c.decodeIfPresent(Int.self, forKey: .formatVersion)) ?? 1
        title = ((try? c.decodeIfPresent(String.self, forKey: .title)) ?? nil) ?? L("Show")
        cues = try c.decodeIfPresent([Cue].self, forKey: .cues) ?? []
    }

    func jsonData() throws -> Data {
        let e = JSONEncoder()
        // Sorterte nøkler gir alltid lik fil for likt show (brukes også til «ulagrede endringer»).
        // Windows-versjonen (0.4+) leser "type" uansett hvor i objektet det står.
        e.outputFormatting = [.prettyPrinted, .withoutEscapingSlashes, .sortedKeys]
        return try e.encode(self)
    }

    func save(to url: URL) throws {
        try jsonData().write(to: url, options: .atomic)
    }

    /// Leser et show. Mediefiler som er flyttet (f.eks. fra en Windows-PC) letes opp ved siden av
    /// showfila, i undermappa «Media», og i Media-mappa ett nivå opp.
    static func load(from url: URL) throws -> ShowDocument {
        var doc = try JSONDecoder().decode(ShowDocument.self, from: Data(contentsOf: url))
        let dir = url.deletingLastPathComponent()
        let fm = FileManager.default
        func locate(_ path: String) -> String {
            if path.isEmpty || fm.fileExists(atPath: path) { return path }
            let name = Cue.lastPathComponent(path)
            let candidates = [dir.appendingPathComponent(name),
                              dir.appendingPathComponent("Media").appendingPathComponent(name),
                              dir.deletingLastPathComponent().appendingPathComponent("Media").appendingPathComponent(name)]
            return candidates.first(where: { fm.fileExists(atPath: $0.path) })?.path ?? path
        }
        for cue in doc.cues where cue.isFileCue { cue.filePath = locate(cue.filePath) }
        for cue in doc.cues where cue.type == .playlist {
            let moved = cue.files.map(locate)
            if moved != cue.files { cue.files = moved }
        }
        doc.title = url.deletingPathExtension().lastPathComponent
        return doc
    }
}

// MARK: - Tid

enum TimeFormat {
    /// mm:ss.t, eller t:mm:ss over en time.
    static func format(_ seconds: Double) -> String {
        guard seconds.isFinite else { return "" }
        let neg = seconds < 0
        let s = abs(seconds)
        let total = Int(s)
        let h = total / 3600, m = (total % 3600) / 60, sec = total % 60
        let tenth = Int((s - Double(total)) * 10)
        let str = h > 0 ? String(format: "%d:%02d:%02d", h, m, sec) : String(format: "%02d:%02d.%d", m, sec, tenth)
        return neg ? "-" + str : str
    }
}
