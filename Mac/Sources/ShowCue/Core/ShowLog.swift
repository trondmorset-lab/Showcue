import Foundation

// Showloggen: en logg over hva som skjedde under et show (cuer startet/stoppet, pause, panikk, feil, åpnet/lagret show).
// Samme oppførsel og samme filformat som Windows-versjonen. Loggingen skal aldri blokkere, forsinke eller krasje programmet:
// hendelser legges i en begrenset kø (eldste kastes når den er full, med teller) og skrives av en egen bakgrunnstråd.
// Alle I/O-feil svelges og meldes én gang.

/// Hva slags hendelse en linje i loggen er.
enum ShowLogKind: CaseIterable { case start, finish, stop, pause, resume, stopAll, playhead, warning, error, showOpened, showSaved, appStart, appExit, dropped }

/// Hvordan en cue ble startet.
enum StartSource: CaseIterable { case unknown, go, autoFollow, autoContinue, hotkey, osc, midi, timecode, clock, group, buttonPanel, manual }

/// Grupper av hendelser, brukt til filter i loggvinduet.
enum ShowLogCategory { case all, cues, control, problems, show }

/// Et tidspunkt: millisekunder siden 1970 (UTC) og tidssonens forskyvning i minutter.
struct ShowLogTime: Equatable {
    var unixMs: Int64
    var offsetMinutes: Int

    static func now() -> ShowLogTime {
        let d = Date()
        return ShowLogTime(unixMs: Int64((d.timeIntervalSince1970 * 1000).rounded(.down)),
                           offsetMinutes: TimeZone.current.secondsFromGMT(for: d) / 60)
    }

    func adding(ms: Int64) -> ShowLogTime { ShowLogTime(unixMs: unixMs + ms, offsetMinutes: offsetMinutes) }
}

/// Én hendelse. `runMs` er millisekunder siden loggen (kjøringen) startet.
struct ShowLogEvent: Equatable {
    var time: ShowLogTime
    var runMs: Int64
    var kind: ShowLogKind
    var number = ""
    var name = ""
    var cueType = ""
    var how = ""
    var text = ""
}

/// Tekstformat for loggfila og navn på hendelsestyper: én linje per hendelse, felt skilt med tabulator.
enum ShowLogFormat {
    static let maxNumber = 40
    static let maxName = 80
    static let maxText = 300
    static let fileHeader = "# ShowCue show log v1"

    /// Kutter teksten til høyst `max` tegn (siste tegn blir «…»).
    static func truncate(_ s: String?, _ max: Int) -> String {
        guard let s, !s.isEmpty else { return "" }
        if s.count <= max { return s }
        return String(s.prefix(Swift.max(0, max - 1))) + "…"
    }

    private static func isControl(_ v: UInt32) -> Bool { v < 0x20 || (v >= 0x7F && v <= 0x9F) || v == 0x2028 || v == 0x2029 }

    private static func hex(_ v: UInt32, _ digits: Int) -> String {
        let h = String(v, radix: 16, uppercase: true)
        return String(repeating: "0", count: Swift.max(0, digits - h.count)) + h
    }

    /// Gjør teksten til én trygg linjedel: skråstrek og tabulator, linjeskift og andre kontrolltegn skrives som tegnfølger.
    static func escape(_ s: String?) -> String {
        guard let s, !s.isEmpty else { return "" }
        var out = ""
        for u in s.unicodeScalars {
            switch u {
            case "\\": out += "\\\\"
            case "\t": out += "\\t"
            case "\n": out += "\\n"
            case "\r": out += "\\r"
            default:
                if !isControl(u.value) { out.unicodeScalars.append(u) }
                else if u.value < 0x100 { out += "\\x" + hex(u.value, 2) }
                else { out += "\\u" + hex(u.value, 4) }
            }
        }
        return out
    }

    private static func hexValue(_ scalars: [Unicode.Scalar], _ start: Int, _ len: Int) -> UInt32? {
        guard start + len <= scalars.count else { return nil }
        var v: UInt32 = 0
        for i in start..<(start + len) {
            guard let d = Character(scalars[i]).hexDigitValue else { return nil }
            v = v * 16 + UInt32(d)
        }
        return v
    }

    /// Det motsatte av `escape`. Ukjente eller ufullstendige tegnfølger beholdes som de står.
    static func unescape(_ s: String?) -> String {
        guard let s, s.contains("\\") else { return s ?? "" }
        let sc = Array(s.unicodeScalars)
        var out = ""
        var i = 0
        while i < sc.count {
            let ch = sc[i]
            if ch != "\\" || i + 1 >= sc.count { out.unicodeScalars.append(ch); i += 1; continue }
            switch sc[i + 1] {
            case "\\": out += "\\"; i += 2
            case "t": out += "\t"; i += 2
            case "n": out += "\n"; i += 2
            case "r": out += "\r"; i += 2
            case "x":
                if let v = hexValue(sc, i + 2, 2), let u = Unicode.Scalar(v) { out.unicodeScalars.append(u); i += 4 }
                else { out.unicodeScalars.append(ch); i += 1 }
            case "u":
                if let v = hexValue(sc, i + 2, 4), let u = Unicode.Scalar(v) { out.unicodeScalars.append(u); i += 6 }
                else { out.unicodeScalars.append(ch); i += 1 }
            default: out.unicodeScalars.append(ch); i += 1
            }
        }
        return out
    }

    static func kindToken(_ k: ShowLogKind) -> String {
        switch k {
        case .start: return "start"
        case .finish: return "finish"
        case .stop: return "stop"
        case .pause: return "pause"
        case .resume: return "resume"
        case .stopAll: return "stopall"
        case .playhead: return "playhead"
        case .warning: return "warning"
        case .error: return "error"
        case .showOpened: return "open"
        case .showSaved: return "save"
        case .appStart: return "appstart"
        case .appExit: return "appexit"
        case .dropped: return "dropped"
        }
    }

    static func kind(fromToken token: String) -> ShowLogKind? { ShowLogKind.allCases.first { kindToken($0) == token } }

    static func sourceToken(_ s: StartSource) -> String {
        switch s {
        case .unknown: return ""
        case .go: return "go"
        case .autoFollow: return "auto-follow"
        case .autoContinue: return "auto-continue"
        case .hotkey: return "hotkey"
        case .osc: return "osc"
        case .midi: return "midi"
        case .timecode: return "timecode"
        case .clock: return "clock"
        case .group: return "group"
        case .buttonPanel: return "button-panel"
        case .manual: return "manual"
        }
    }

    static func category(of k: ShowLogKind) -> ShowLogCategory {
        switch k {
        case .start, .finish, .stop: return .cues
        case .pause, .resume, .stopAll, .playhead: return .control
        case .warning, .error, .dropped: return .problems
        case .showOpened, .showSaved, .appStart, .appExit: return .show
        }
    }

    static func matches(_ e: ShowLogEvent, _ category: ShowLogCategory) -> Bool { category == .all || self.category(of: e.kind) == category }

    private static func pad(_ v: Int, _ n: Int) -> String {
        let s = String(v)
        return String(repeating: "0", count: Swift.max(0, n - s.count)) + s
    }

    private static func floorDiv(_ a: Int64, _ b: Int64) -> Int64 { a >= 0 ? a / b : -((-a + b - 1) / b) }

    /// Dato fra antall dager siden 1970-01-01 (proleptisk gregoriansk kalender).
    private static func civil(fromDays z0: Int64) -> (y: Int, m: Int, d: Int) {
        let z = z0 + 719468
        let era = floorDiv(z, 146097)
        let doe = z - era * 146097
        let yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365
        let y = yoe + era * 400
        let doy = doe - (365 * yoe + yoe / 4 - yoe / 100)
        let mp = (5 * doy + 2) / 153
        let d = doy - (153 * mp + 2) / 5 + 1
        let m = mp < 10 ? mp + 3 : mp - 9
        return (Int(m <= 2 ? y + 1 : y), Int(m), Int(d))
    }

    private static func days(fromCivil y0: Int, _ m: Int, _ d: Int) -> Int64 {
        let y = Int64(m <= 2 ? y0 - 1 : y0)
        let era = floorDiv(y, 400)
        let yoe = y - era * 400
        let mp = Int64(m > 2 ? m - 3 : m + 9)
        let doy = (153 * mp + 2) / 5 + Int64(d) - 1
        let doe = yoe * 365 + yoe / 4 - yoe / 100 + doy
        return era * 146097 + doe - 719468
    }

    /// Tidspunkt som ISO 8601 med millisekunder og tidssone, f.eks. 2026-11-25T14:03:22.123+01:00.
    static func iso(_ t: ShowLogTime) -> String {
        let local = t.unixMs + Int64(t.offsetMinutes) * 60_000
        let day = floorDiv(local, 86_400_000)
        let msOfDay = Int(local - day * 86_400_000)
        let c = civil(fromDays: day)
        let sign = t.offsetMinutes < 0 ? "-" : "+"
        let off = abs(t.offsetMinutes)
        return "\(pad(c.y, 4))-\(pad(c.m, 2))-\(pad(c.d, 2))T\(pad(msOfDay / 3_600_000, 2)):\(pad(msOfDay / 60_000 % 60, 2)):\(pad(msOfDay / 1000 % 60, 2)).\(pad(msOfDay % 1000, 3))\(sign)\(pad(off / 60, 2)):\(pad(off % 60, 2))"
    }

    /// Leser tidspunktet slik `iso` skriver det (nøyaktig dette formatet, ellers nil).
    static func parseIso(_ text: String) -> ShowLogTime? {
        let u = Array(text.utf8)
        guard u.count == 29, u[4] == 45, u[7] == 45, u[10] == 84, u[13] == 58, u[16] == 58, u[19] == 46, u[23] == 43 || u[23] == 45, u[26] == 58 else { return nil }
        func num(_ a: Int, _ n: Int) -> Int? {
            var v = 0
            for i in a..<(a + n) { guard u[i] >= 48, u[i] <= 57 else { return nil }; v = v * 10 + Int(u[i] - 48) }
            return v
        }
        guard let y = num(0, 4), let mo = num(5, 2), let d = num(8, 2), let h = num(11, 2), let mi = num(14, 2), let s = num(17, 2),
              let f = num(20, 3), let oh = num(24, 2), let om = num(27, 2) else { return nil }
        guard (1...12).contains(mo), (1...31).contains(d), h < 24, mi < 60, s < 60, oh < 24, om < 60 else { return nil }
        // Datoer som ikke finnes (30. februar) avvises ved å regne tilbake.
        let day = days(fromCivil: y, mo, d)
        let back = civil(fromDays: day)
        guard back.y == y, back.m == mo, back.d == d else { return nil }
        let offset = (oh * 60 + om) * (u[23] == 45 ? -1 : 1)
        let local = day * 86_400_000 + Int64(((h * 60 + mi) * 60 + s) * 1000 + f)
        return ShowLogTime(unixMs: local - Int64(offset) * 60_000, offsetMinutes: offset)
    }

    /// Tid siden kjøringen startet som TT:MM:SS.mmm (timer kan være flere enn to sifre).
    static func runText(_ ms0: Int64) -> String {
        let ms = Swift.max(0, ms0)
        return "\(pad(Int(ms / 3_600_000), 2)):\(pad(Int(ms / 60_000 % 60), 2)):\(pad(Int(ms / 1000 % 60), 2)).\(pad(Int(ms % 1000), 3))"
    }

    static func parseRun(_ text: String) -> Int64? {
        let parts = text.split(separator: ":", omittingEmptySubsequences: false)
        guard parts.count == 3 else { return nil }
        let sec = parts[2].split(separator: ".", omittingEmptySubsequences: false)
        guard sec.count == 2, sec[1].count == 3 else { return nil }
        func n(_ s: Substring) -> Int64? { s.isEmpty || !s.utf8.allSatisfy({ $0 >= 48 && $0 <= 57 }) ? nil : Int64(s) }
        guard let h = n(parts[0]), let m = n(parts[1]), let s = n(sec[0]), let f = n(sec[1]) else { return nil }
        guard m <= 59, s <= 59, h <= 100_000 else { return nil }
        return ((h * 60 + m) * 60 + s) * 1000 + f
    }

    /// Én loggline: tid, tid siden start, type, cuenummer, cuetype, hvordan, navn, tekst (skilt med tabulator, uten linjeskift).
    static func formatLine(_ e: ShowLogEvent) -> String {
        [iso(e.time), runText(e.runMs), kindToken(e.kind),
         escape(truncate(e.number, maxNumber)), escape(truncate(e.cueType, maxNumber)), escape(truncate(e.how, maxNumber)),
         escape(truncate(e.name, maxName)), escape(truncate(e.text, maxText))].joined(separator: "\t")
    }

    /// Leser en loggline. Kommentarlinjer (#), tomme linjer og linjer som ikke kan tolkes gir nil.
    static func parseLine(_ line: String?) -> ShowLogEvent? {
        guard let line, !line.isEmpty, !line.hasPrefix("#") else { return nil }
        let f = line.split(separator: "\t", omittingEmptySubsequences: false).map(String.init)
        guard f.count == 8, let t = parseIso(f[0]), let run = parseRun(f[1]), let k = kind(fromToken: f[2]) else { return nil }
        return ShowLogEvent(time: t, runMs: run, kind: k, number: unescape(f[3]), name: unescape(f[6]), cueType: unescape(f[4]), how: unescape(f[5]), text: unescape(f[7]))
    }
}

/// Eksport av loggen til CSV og ren tekst, og innlesing av en loggfil.
enum ShowLogExport {
    static let csvHeader = "time,run,event,cue,type,how,name,text"

    /// Ett CSV-felt. Felt som begynner med = + - @ (eller tabulator/linjeskift) får en apostrof foran, så et regneark
    /// aldri tolker dem som formel. Felt med komma, anførselstegn eller linjeskift settes i anførselstegn.
    static func csvCell(_ s0: String?) -> String {
        var s = s0 ?? ""
        if let first = s.unicodeScalars.first, ["=", "+", "-", "@", "\t", "\r"].contains(first) { s = "'" + s }
        var clean = String.UnicodeScalarView()
        for u in s.unicodeScalars {
            // Linjeskift beholdes (gyldig i et felt med anførselstegn); andre kontrolltegn blir mellomrom.
            let control = u.value < 0x20 || (u.value >= 0x7F && u.value <= 0x9F) || u.value == 0x2028 || u.value == 0x2029
            if u == "\n" || u == "\r" || !control { clean.append(u) } else { clean.append(" ") }
        }
        s = String(clean)
        let sc = s.unicodeScalars
        let quote = sc.contains(where: { $0 == "," || $0 == "\"" || $0 == "\n" || $0 == "\r" }) || sc.first == " " || sc.last == " "
        return quote ? "\"" + s.replacingOccurrences(of: "\"", with: "\"\"") + "\"" : s
    }

    static func csvRow(_ e: ShowLogEvent) -> String {
        [csvCell(ShowLogFormat.iso(e.time)), csvCell(ShowLogFormat.runText(e.runMs)), csvCell(ShowLogFormat.kindToken(e.kind)),
         csvCell(ShowLogFormat.truncate(e.number, ShowLogFormat.maxNumber)), csvCell(ShowLogFormat.truncate(e.cueType, ShowLogFormat.maxNumber)),
         csvCell(ShowLogFormat.truncate(e.how, ShowLogFormat.maxNumber)),
         csvCell(ShowLogFormat.truncate(e.name, ShowLogFormat.maxName)), csvCell(ShowLogFormat.truncate(e.text, ShowLogFormat.maxText))].joined(separator: ",")
    }

    /// Hele CSV-fila (overskrift + én rad per hendelse, CRLF).
    static func toCSV(_ events: [ShowLogEvent]) -> String {
        csvHeader + "\r\n" + events.map { csvRow($0) + "\r\n" }.joined()
    }

    /// Ren tekst i samme format som loggfila (kan leses inn igjen).
    static func toText(_ events: [ShowLogEvent]) -> String {
        ShowLogFormat.fileHeader + "\n" + events.map { ShowLogFormat.formatLine($0) + "\n" }.joined()
    }

    /// Leser hendelsene i en loggtekst. Linjer som ikke kan tolkes hoppes over (og telles).
    static func parse(_ lines: [String]) -> (events: [ShowLogEvent], skipped: Int) {
        var list: [ShowLogEvent] = []
        var skipped = 0
        for raw in lines {
            var line = raw
            while line.unicodeScalars.last == "\r" { line = String(line.unicodeScalars.dropLast()) }
            if line.isEmpty || line.hasPrefix("#") { continue }
            if let e = ShowLogFormat.parseLine(line) { list.append(e) } else { skipped += 1 }
        }
        return (list, skipped)
    }

    static func readFile(_ url: URL) throws -> (events: [ShowLogEvent], skipped: Int) {
        let text = try String(contentsOf: url, encoding: .utf8)
        return parse(text.components(separatedBy: "\n"))
    }
}

/// Valg av loggfiler: navn, og hvilke gamle filer som skal slettes.
enum ShowLogFiles {
    static let filesToKeep = 30
    static let maxFileBytes = 5 * 1024 * 1024

    static func directory(root: URL) -> URL { root.appendingPathComponent("Logs") }

    /// Filnavn for en kjøring (med løpenummer hvis flere starter samme sekund).
    static func fileName(start: ShowLogTime, attempt: Int = 1) -> String {
        let u = Array(ShowLogFormat.iso(start))
        let stamp = String(u[0..<10]) + " " + String(u[11..<13]) + "." + String(u[14..<16]) + "." + String(u[17..<19])
        return "showlog \(stamp)" + (attempt > 1 ? " (\(attempt))" : "") + ".log"
    }

    static func isLogName(_ name: String?) -> Bool {
        guard let name else { return false }
        return name.range(of: #"^showlog \d{4}-\d{2}-\d{2} \d{2}\.\d{2}\.\d{2}( \(\d+\))?\.log$"#, options: .regularExpression) != nil
    }

    /// Filene som skal slettes så bare de `keep` nyeste (etter navn = tidspunkt) står igjen.
    /// Filer som ikke ser ut som loggfiler røres aldri.
    static func toDelete(_ names: [String], keep: Int) -> [String] {
        Array(names.filter(isLogName).sorted { $1.utf8.lexicographicallyPrecedes($0.utf8) }.dropFirst(Swift.max(0, keep)))
    }

    /// Loggfilene i mappa, nyeste først (tom liste hvis mappa ikke finnes).
    static func list(root: URL) -> [URL] {
        let dir = directory(root: root)
        guard let names = try? FileManager.default.contentsOfDirectory(atPath: dir.path) else { return [] }
        return names.filter(isLogName).sorted { $1.utf8.lexicographicallyPrecedes($0.utf8) }.map { dir.appendingPathComponent($0) }
    }
}

/// Kø med fast størrelse: er den full, kastes den eldste, og en teller holder rede på hvor mange. Trygg å bruke fra flere tråder.
final class BoundedQueue<T> {
    private let lock = NSLock()
    private var items: [T] = []
    private var head = 0
    private var dropped = 0
    private var droppedAll = 0
    let capacity: Int

    init(capacity: Int) { self.capacity = Swift.max(1, capacity) }

    var count: Int { lock.lock(); defer { lock.unlock() }; return items.count - head }
    /// Antall kastet siden sist `takeDropped`.
    var pendingDropped: Int { lock.lock(); defer { lock.unlock() }; return dropped }
    /// Antall kastet totalt.
    var droppedTotal: Int { lock.lock(); defer { lock.unlock() }; return droppedAll }

    /// Legger til. Returnerer false hvis den eldste måtte kastes for å gi plass.
    @discardableResult
    func enqueue(_ item: T) -> Bool {
        lock.lock(); defer { lock.unlock() }
        var ok = true
        while items.count - head >= capacity { head += 1; dropped += 1; droppedAll += 1; ok = false }
        items.append(item)
        if head > 1024 && head > items.count / 2 { items.removeFirst(head); head = 0 }
        return ok
    }

    /// Flytter inntil `max` elementer (eldst først) til `into`. Returnerer antallet.
    func drain(into out: inout [T], max: Int) -> Int {
        lock.lock(); defer { lock.unlock() }
        var n = 0
        while n < max && head < items.count { out.append(items[head]); head += 1; n += 1 }
        if head == items.count { items.removeAll(keepingCapacity: true); head = 0 }
        return n
    }

    /// Henter og nullstiller telleren for kastede elementer.
    func takeDropped() -> Int { lock.lock(); defer { lock.unlock() }; let d = dropped; dropped = 0; return d }
}

/// Loggen for én kjøring av programmet. Hendelser legges i en begrenset kø og skrives til en loggfil av en bakgrunnstråd;
/// de siste hendelsene holdes også i minnet til loggvinduet. `record` kaster aldri og venter aldri på disk.
final class ShowLog {
    // MARK: Felles tilgang for resten av programmet

    static var current: ShowLog?

    private static let viaKey = "showcue.showlog.via"

    /// Hvordan det som skjer nå ble utløst (satt med `via`), eller `.unknown`. Gjelder bare den tråden.
    static var currentSource: StartSource {
        if let i = Thread.current.threadDictionary[viaKey] as? Int, StartSource.allCases.indices.contains(i) { return StartSource.allCases[i] }
        return .unknown
    }

    /// Merker alt som logges og startes på denne tråden mens `body` kjører. Med `keepExisting` beholdes en kilde som
    /// allerede er satt lenger ute (f.eks. OSC som kaller GO).
    @discardableResult
    static func via<R>(_ source: StartSource, keepExisting: Bool = false, _ body: () throws -> R) rethrows -> R {
        let previous = currentSource
        if !(keepExisting && previous != .unknown) {
            Thread.current.threadDictionary[viaKey] = StartSource.allCases.firstIndex(of: source) ?? 0
        }
        defer { Thread.current.threadDictionary[viaKey] = StartSource.allCases.firstIndex(of: previous) ?? 0 }
        return try body()
    }

    /// Logger en hendelse i programmets logg (ingenting hvis ingen er installert). Kaster aldri.
    static func add(_ kind: ShowLogKind, number: String? = nil, name: String? = nil, cueType: String? = nil, how: StartSource = .unknown, text: String? = nil) {
        current?.record(kind, number: number, name: name, cueType: cueType, how: how, text: text)
    }

    /// Logger en hendelse for en cue (nummer, navn og type hentes fra cuen). Kaster aldri.
    static func add(_ kind: ShowLogKind, cue: Cue?, how: StartSource = .unknown, text: String? = nil) {
        guard let log = current, let cue else { return }
        log.record(kind, number: cue.number, name: cue.displayName, cueType: cue.type.rawValue, how: how, text: text)
    }

    // MARK: Instans

    private let root: URL?
    private let clock: () -> ShowLogTime
    private let queue: BoundedQueue<ShowLogEvent>
    private let memoryLimit: Int
    private let maxFileBytes: Int
    private let filesToKeep: Int
    private let gate = NSLock()          // minne, tellere og tilstand
    private var memory: [(seq: Int, event: ShowLogEvent)] = []
    private var seq = 0
    private var writeFileFlag: Bool
    private var stopping = false
    private var busy = false
    private var disposed = false
    private var faultReported = false
    private var faultText: String?
    private let signal = DispatchSemaphore(value: 0)
    private let finished = DispatchSemaphore(value: 0)
    private var started = false
    // Bare skrivetråden rører disse:
    private var handle: FileHandle?
    private var bytes = 0
    private var failed = false
    private var full = false
    private var path: String?

    /// Tidspunktet kjøringen startet (tid siden start regnes herfra).
    let runStart: ShowLogTime
    /// Skriving til fil feilet (kalles én gang, fra bakgrunnstråden).
    var onFault: ((String) -> Void)?

    /// - Parameters:
    ///   - root: Programmets datamappe (loggene havner i «Logs» under den). nil = ingen fil, bare minne.
    ///   - writeFile: Skriv til fil. Kan slås av og på med `writeFile`.
    ///   - clock: Klokke (for testing).
    init(root: URL?, writeFile: Bool = true, clock: @escaping () -> ShowLogTime = ShowLogTime.now, queueCapacity: Int = 4096,
         memoryLimit: Int = 5000, maxFileBytes: Int = ShowLogFiles.maxFileBytes, filesToKeep: Int = ShowLogFiles.filesToKeep) {
        self.root = root
        self.clock = clock
        self.queue = BoundedQueue<ShowLogEvent>(capacity: queueCapacity)
        self.memoryLimit = Swift.max(1, memoryLimit)
        self.maxFileBytes = Swift.max(1024, maxFileBytes)
        self.filesToKeep = Swift.max(1, filesToKeep)
        self.writeFileFlag = writeFile
        self.runStart = clock()
        if root != nil {
            started = true
            let t = Thread { [weak self] in self?.writerLoop() }
            t.name = "Showlogg"
            t.qualityOfService = .utility
            t.start()
        }
    }

    var writeFile: Bool {
        get { gate.lock(); defer { gate.unlock() }; return writeFileFlag }
        set { gate.lock(); writeFileFlag = newValue; gate.unlock() }
    }

    /// Loggfila som er i bruk (nil til den første hendelsen er skrevet, eller hvis fil ikke brukes).
    var filePath: String? { gate.lock(); defer { gate.unlock() }; return path }
    /// Første feil ved skriving til fil (nil = ingen feil). Meldes bare én gang.
    var fault: String? { gate.lock(); defer { gate.unlock() }; return faultText }
    /// Antall hendelser som er kastet fordi køen var full.
    var dropped: Int { queue.droppedTotal }
    /// Antall hendelser i køen som ikke er skrevet ennå.
    var pending: Int { queue.count }
    /// Antall hendelser som er logget i denne kjøringen.
    var total: Int { gate.lock(); defer { gate.unlock() }; return seq }

    /// Logger en hendelse. Kaster aldri og venter aldri på disk.
    func record(_ kind: ShowLogKind, number: String? = nil, name: String? = nil, cueType: String? = nil, how: StartSource = .unknown, text: String? = nil) {
        var source = how
        if source == .unknown { source = ShowLog.currentSource }
        let now = clock()
        let ev = ShowLogEvent(time: now, runMs: Swift.max(0, now.unixMs - runStart.unixMs), kind: kind,
                              number: ShowLogFormat.truncate(number, ShowLogFormat.maxNumber), name: ShowLogFormat.truncate(name, ShowLogFormat.maxName),
                              cueType: ShowLogFormat.truncate(cueType, ShowLogFormat.maxNumber), how: ShowLogFormat.sourceToken(source),
                              text: ShowLogFormat.truncate(text, ShowLogFormat.maxText))
        gate.lock()
        if disposed { gate.unlock(); return }
        addToMemoryLocked(ev)
        let write = writeFileFlag && started
        gate.unlock()
        if write {
            queue.enqueue(ev)
            signal.signal()
        }
    }

    private func addToMemoryLocked(_ ev: ShowLogEvent) {
        seq += 1
        memory.append((seq, ev))
        if memory.count > memoryLimit + 256 { memory.removeFirst(memory.count - memoryLimit) } // sjelden kutting, ikke per hendelse
    }

    /// Hendelsene som er nyere enn `after` (0 = alle som ligger i minnet), og nummeret på den siste.
    func readSince(_ after: Int) -> (events: [ShowLogEvent], last: Int) {
        gate.lock(); defer { gate.unlock() }
        let recent = memory.suffix(memoryLimit)
        return (recent.filter { $0.seq > after }.map { $0.event }, seq)
    }

    func snapshot() -> [ShowLogEvent] { readSince(0).events }

    /// Venter (høyst `timeout` sekunder) til alt i køen er skrevet. Brukes ved avslutning og i tester.
    @discardableResult
    func flush(timeout: TimeInterval = 2) -> Bool {
        guard started else { return true }
        let end = Date().addingTimeInterval(timeout)
        while Date() <= end {
            gate.lock(); let isBusy = busy; gate.unlock()
            if queue.count == 0 && queue.pendingDropped == 0 && !isBusy { return true }
            signal.signal()
            Thread.sleep(forTimeInterval: 0.002)
        }
        return false
    }

    // MARK: Skrivetråden

    private func writerLoop() {
        var batch: [ShowLogEvent] = []
        while true {
            _ = signal.wait(timeout: .now() + 0.3)
            gate.lock(); let stop = stopping; busy = true; gate.unlock()
            while true {
                batch.removeAll(keepingCapacity: true)
                if queue.drain(into: &batch, max: 512) == 0 { break }
                writeBatch(batch)
            }
            let lost = queue.takeDropped()
            if lost > 0 {
                let now = clock()
                let ev = ShowLogEvent(time: now, runMs: Swift.max(0, now.unixMs - runStart.unixMs), kind: .dropped,
                                      text: "\(lost) events were lost (log queue full)")
                gate.lock(); addToMemoryLocked(ev); gate.unlock()
                writeBatch([ev])
            }
            gate.lock(); busy = false; gate.unlock()
            if stop { break }
        }
        closeFile()
        finished.signal()
    }

    private func writeBatch(_ batch: [ShowLogEvent]) {
        guard writeFile, let root, !failed, !full else { return }
        do {
            if handle == nil { try openFile(root) }
            var out = ""
            for ev in batch {
                let line = ShowLogFormat.formatLine(ev) + "\n"
                let size = line.utf8.count
                if bytes + size + 200 > maxFileBytes {
                    out += "# size limit reached; later events are not written to this file\n"
                    full = true
                    break
                }
                out += line
                bytes += size
            }
            try handle?.write(contentsOf: Data(out.utf8))
        } catch {
            failed = true // skriving gis opp for resten av kjøringen; feilen meldes én gang
            reportFault(error.localizedDescription)
            closeFile()
        }
    }

    private func openFile(_ root: URL) throws {
        let fm = FileManager.default
        let dir = ShowLogFiles.directory(root: root)
        try fm.createDirectory(at: dir, withIntermediateDirectories: true)
        // Behold de nyeste, så det er plass til den nye fila.
        for old in ShowLogFiles.toDelete((try? fm.contentsOfDirectory(atPath: dir.path)) ?? [], keep: filesToKeep - 1) {
            try? fm.removeItem(at: dir.appendingPathComponent(old))
        }
        var attempt = 1
        while true {
            let url = dir.appendingPathComponent(ShowLogFiles.fileName(start: runStart, attempt: attempt))
            let fd = open(url.path, O_WRONLY | O_CREAT | O_EXCL, 0o644)
            if fd >= 0 {
                handle = FileHandle(fileDescriptor: fd, closeOnDealloc: true)
                gate.lock(); path = url.path; gate.unlock()
                break
            }
            if errno == EEXIST && attempt < 50 { attempt += 1; continue }
            throw NSError(domain: NSPOSIXErrorDomain, code: Int(errno), userInfo: [NSLocalizedDescriptionKey: String(cString: strerror(errno))])
        }
        let header = Data((ShowLogFormat.fileHeader + " | started " + ShowLogFormat.iso(runStart) + "\n").utf8)
        try handle?.write(contentsOf: header)
        bytes = header.count
    }

    private func closeFile() {
        try? handle?.close()
        handle = nil
    }

    private func reportFault(_ message: String) {
        gate.lock()
        if faultReported { gate.unlock(); return }
        faultReported = true
        faultText = message
        gate.unlock()
        onFault?(message)
    }

    /// Skriver ut det som står i køen og lukker fila. Venter høyst et par sekunder.
    func close() {
        gate.lock()
        if disposed { gate.unlock(); return }
        disposed = true
        stopping = true
        gate.unlock()
        guard started else { return }
        signal.signal()
        _ = finished.wait(timeout: .now() + 1.5)
    }
}
