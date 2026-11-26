import Foundation

/// Ett stereopar i et routingforhåndsvalg: utgangen til venstre kanal (høyre kanal går til neste utgang) og et ekstra nivå i dB.
struct RoutingPair: Codable, Equatable {
    /// Utgang for venstre kanal, fra 1. Høyre kanal går til utgangen etter.
    var bus: Int { didSet { bus = RoutingPresets.clampLeftBus(bus) } }
    /// Nivå i dB som legges til alle rutene i paret (NaN blir 0).
    var gainDb: Double { didSet { gainDb = RoutingPresets.clampGain(gainDb) } }

    init(_ bus: Int, _ gainDb: Double = 0) {
        self.bus = RoutingPresets.clampLeftBus(bus)
        self.gainDb = RoutingPresets.clampGain(gainDb)
    }

    enum CodingKeys: String, CodingKey { case bus = "Bus", gainDb = "GainDb" }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        self.init(try c.decodeIfPresent(Int.self, forKey: .bus) ?? 1, try c.decodeIfPresent(Double.self, forKey: .gainDb) ?? 0)
    }

    func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(bus, forKey: .bus)
        try c.encode(gainDb.isFinite ? gainDb : 0, forKey: .gainDb)
    }
}

/// Et navngitt forhåndsvalg for routing («Hovedhøyttalere», «Monitor», «Hoved + monitor»). Lagres i showet og
/// gjelder uansett hvor mange kanaler lydfila har.
struct RoutingPreset: Codable, Equatable {
    var name: String
    var pairs: [RoutingPair]

    init(_ name: String, _ pairs: [RoutingPair]) {
        self.name = name.trimmingCharacters(in: .whitespacesAndNewlines)
        self.pairs = RoutingPresets.cleanPairs(pairs)
    }

    enum CodingKeys: String, CodingKey { case name = "Name", pairs = "Pairs" }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        // «null» i lista over par er ingen par og hoppes over; et par med feil type gjør hele forhåndsvalget ugyldig.
        let raw = try c.decodeIfPresent([RoutingPair?].self, forKey: .pairs) ?? []
        self.init(try c.decodeIfPresent(String.self, forKey: .name) ?? "", raw.compactMap { $0 })
    }
}

/// Leser ett forhåndsvalg i lista uten å stoppe resten hvis det er skadet.
struct LossyRoutingPreset: Decodable {
    let value: RoutingPreset?
    init(from decoder: Decoder) throws { value = try? RoutingPreset(from: decoder) }
}

enum RoutingPresetError: Equatable {
    case ok, emptyName, nameTooLong, duplicateName, noPairs, tooManyPresets, notFound
}

/// Resultat av å sette routing på flere cuer: endret, allerede likt, og hoppet over (ingen lyd, eller ingen gyldig utgang).
struct RoutingApplyResult: Equatable {
    var changed: Int
    var unchanged: Int
    var skipped: Int
    init(_ changed: Int, _ unchanged: Int, _ skipped: Int) { self.changed = changed; self.unchanged = unchanged; self.skipped = skipped }
}

/// Ren logikk for routingforhåndsvalg: lage, endre og slette, gjøre dem om til ruter for en cue, sette dem på flere
/// cuer, kopiere og lime inn, og beskrive routing i tekst. Rører ingen lydtråd, fil eller UI.
enum RoutingPresets {
    static let maxBus = 64
    static let maxPairs = 8
    static let maxPresets = 32
    static let maxNameLength = 40
    static let maxChannels = 64

    /// Nivå i dB mellom −60 og +12; NaN blir 0, ±uendelig blir grensene.
    static func clampGain(_ v: Double) -> Double { v.isNaN ? 0 : min(12, max(-60, v)) }
    /// Utgang for venstre kanal i et par: 1 til maxBus − 1 (høyre kanal trenger plass).
    static func clampLeftBus(_ v: Int) -> Int { min(maxBus - 1, max(1, v)) }

    /// Par uten to par på samme utgang; maks maxPairs. Verdiene er begrenset.
    static func cleanPairs(_ pairs: [RoutingPair?]?) -> [RoutingPair] {
        var result: [RoutingPair] = []
        for case let p? in pairs ?? [] {
            if result.count >= maxPairs || result.contains(where: { $0.bus == p.bus }) { continue }
            result.append(RoutingPair(p.bus, p.gainDb))
        }
        return result
    }

    /// Forhåndsvalg fra en lest fil: tomme eller ugyldige oppføringer, tomme eller like navn og alt over grensene fjernes.
    static func cleanList(_ presets: [RoutingPreset?]?) -> [RoutingPreset] {
        var result: [RoutingPreset] = []
        for case let p? in presets ?? [] {
            if result.count >= maxPresets { break }
            var name = p.name.trimmingCharacters(in: .whitespacesAndNewlines)
            if name.isEmpty { continue }
            if name.count > maxNameLength { name = String(name.prefix(maxNameLength)).trimmingCharacters(in: .whitespacesAndNewlines) }
            let pairs = cleanPairs(p.pairs)
            if pairs.isEmpty || result.contains(where: { sameName($0.name, name) }) { continue }
            result.append(RoutingPreset(name, pairs))
        }
        return result
    }

    private static func sameName(_ a: String, _ b: String) -> Bool { a.caseInsensitiveCompare(b) == .orderedSame }

    private static func checkName(_ list: [RoutingPreset], _ name: String?, ignoring index: Int) -> (RoutingPresetError, String) {
        let clean = (name ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        if clean.isEmpty { return (.emptyName, clean) }
        if clean.count > maxNameLength { return (.nameTooLong, clean) }
        for (i, p) in list.enumerated() where i != index && sameName(p.name, clean) { return (.duplicateName, clean) }
        return (.ok, clean)
    }

    /// Legger til et nytt forhåndsvalg sist i lista. Ved feil endres ingenting.
    static func add(_ list: inout [RoutingPreset], name: String?, pairs: [RoutingPair?]?) -> RoutingPresetError {
        if list.count >= maxPresets { return .tooManyPresets }
        let (err, clean) = checkName(list, name, ignoring: -1)
        if err != .ok { return err }
        let p = cleanPairs(pairs)
        if p.isEmpty { return .noPairs }
        list.append(RoutingPreset(clean, p))
        return .ok
    }

    /// Endrer navn og par på forhåndsvalget på plassen `index`. Samme navn som det har fra før er lov.
    static func update(_ list: inout [RoutingPreset], index: Int, name: String?, pairs: [RoutingPair?]?) -> RoutingPresetError {
        if index < 0 || index >= list.count { return .notFound }
        let (err, clean) = checkName(list, name, ignoring: index)
        if err != .ok { return err }
        let p = cleanPairs(pairs)
        if p.isEmpty { return .noPairs }
        list[index] = RoutingPreset(clean, p)
        return .ok
    }

    /// Gir nytt navn og beholder parene.
    static func rename(_ list: inout [RoutingPreset], index: Int, name: String?) -> RoutingPresetError {
        if index < 0 || index >= list.count { return .notFound }
        let pairs = list[index].pairs
        return update(&list, index: index, name: name, pairs: pairs)
    }

    @discardableResult
    static func remove(_ list: inout [RoutingPreset], index: Int) -> Bool {
        if index < 0 || index >= list.count { return false }
        list.remove(at: index)
        return true
    }

    /// Leser par som tekst: «1-2, 3-4:-6». Hvert par er to nabo-utganger (n–n+1), og et nivå i dB kan stå etter kolon (punktum som desimaltegn).
    /// Skilletegn mellom parene: komma, semikolon, «+» eller mellomrom. Tom tekst, feil format, tall utenfor 1–maxBus, nivå som ikke er et tall
    /// (NaN, uendelig) eller flere par enn maxPairs gir nil. Par på samme utgang telles én gang (det første gjelder). Nivå begrenses til −60 … +12 dB.
    static func parsePairs(_ text: String?) -> [RoutingPair]? {
        let normalized = (text ?? "").replacingOccurrences(of: "–", with: "-").replacingOccurrences(of: "−", with: "-")
        let tokens = normalized.split(whereSeparator: { ",; +".contains($0) })
        if tokens.isEmpty { return nil }
        func number(_ s: Substring) -> Int? { s.isEmpty || !s.allSatisfy({ $0.isASCII && $0.isNumber }) ? nil : Int(s) }
        var pairs: [RoutingPair] = []
        for t in tokens {
            let gainSplit = t.split(separator: ":", omittingEmptySubsequences: false)
            let parts = gainSplit[0].split(separator: "-", omittingEmptySubsequences: false)
            var gain = 0.0
            if gainSplit.count > 2 { return nil }
            if gainSplit.count == 2 {
                guard let g = Double(gainSplit[1]), g.isFinite else { return nil }
                gain = g
            }
            guard parts.count == 2, let a = number(parts[0]), let b = number(parts[1]), b == a + 1, a >= 1, b <= maxBus else { return nil }
            if !pairs.contains(where: { $0.bus == a }) { pairs.append(RoutingPair(a, gain)) }
        }
        return pairs.count > maxPairs ? nil : pairs
    }

    private static func gainText(_ g: Double) -> String {
        let r = (g * 10).rounded(.toNearestOrAwayFromZero) / 10
        return r == r.rounded() ? String(Int(r)) : String(format: "%.1f", locale: Locale(identifier: "en_US_POSIX"), r)
    }

    /// Parene som tekst: «1-2, 3-4:-6» (nivået står bare med når det ikke er 0).
    static func formatPairs(_ preset: RoutingPreset) -> String {
        preset.pairs.map { "\($0.bus)-\($0.bus + 1)" + (abs($0.gainDb) < 0.0001 ? "" : ":" + gainText($0.gainDb)) }.joined(separator: ", ")
    }

    /// Utgangene som tekst med områder: «1-2, 5».
    static func ranges(_ buses: [Int]) -> String {
        let list = Array(Set(buses)).sorted()
        var parts: [String] = []
        var i = 0
        while i < list.count {
            var j = i
            while j + 1 < list.count && list[j + 1] == list[j] + 1 { j += 1 }
            parts.append(j > i ? "\(list[i])-\(list[j])" : "\(list[i])")
            i = j + 1
        }
        return parts.joined(separator: ", ")
    }

    /// Rutene et forhåndsvalg gir for en fil med `fileChannels` kanaler. Mono går til begge utgangene i hvert par,
    /// stereo går venstre/høyre, og flere kanaler summeres annenhver til venstre/høyre med −3 dB. Parets nivå legges til.
    /// Par som ikke får plass i `busCount` utganger hoppes over (0 = ingen grense). Ingen kanaler gir tom liste.
    static func routes(_ preset: RoutingPreset, fileChannels: Int, busCount: Int = 0) -> [AudioRoute] {
        let ch = min(maxChannels, max(0, fileChannels))
        var best: [[Int]: Double] = [:]
        func put(_ source: Int, _ bus: Int, _ gain: Double) {
            let g = clampGain(gain)
            if let old = best[[source, bus]], old >= g { return }
            best[[source, bus]] = g
        }
        if ch > 0 {
            for p in cleanPairs(preset.pairs) {
                if busCount > 0 && p.bus + 1 > busCount { continue }
                if ch == 1 { put(1, p.bus, p.gainDb); put(1, p.bus + 1, p.gainDb); continue }
                let base: Double = ch > 2 ? -3 : 0
                for c in 1...ch { put(c, p.bus + (c - 1) % 2, base + p.gainDb) }
            }
        }
        return best.keys.sorted { ($0[0], $0[1]) < ($1[0], $1[1]) }.map { AudioRoute($0[0], $0[1], best[$0]!) }
    }

    /// Routing med kanaler og utganger innenfor grensene, uten like (kanal, utgang) to ganger og med nivå begrenset.
    /// Kanaler over `fileChannels` og utganger over `busCount` (0 = ingen grense) fjernes.
    static func cleanRoutes(_ routes: [AudioRoute?]?, fileChannels: Int, busCount: Int = 0) -> [AudioRoute] {
        let ch = min(maxChannels, max(0, fileChannels))
        let maxB = busCount > 0 ? min(busCount, maxBus) : maxBus
        var seen = Set<[Int]>()
        var result: [AudioRoute] = []
        for case let r? in routes ?? [] {
            guard r.source >= 1, r.source <= ch, r.bus >= 1, r.bus <= maxB else { continue }
            if seen.insert([r.source, r.bus]).inserted { result.append(AudioRoute(r.source, r.bus, clampGain(r.gainDb))) }
        }
        return result.sorted { ($0.source, $0.bus) < ($1.source, $1.bus) }
    }

    /// Er to routinger like? nil = standard og er bare lik nil. Nivå regnes som like innenfor 0,001 dB.
    static func sameRoutes(_ a: [AudioRoute]?, _ b: [AudioRoute]?) -> Bool {
        guard let a, let b else { return a == nil && b == nil }
        let x = a.sorted { ($0.source, $0.bus) < ($1.source, $1.bus) }
        let y = b.sorted { ($0.source, $0.bus) < ($1.source, $1.bus) }
        if x.count != y.count { return false }
        for i in x.indices where x[i].source != y[i].source || x[i].bus != y[i].bus || abs(x[i].gainDb - y[i].gainDb) > 0.001 { return false }
        return true
    }

    /// Feilmelding på norsk for en feil ved lagring av forhåndsvalg.
    static func errorText(_ err: RoutingPresetError) -> String {
        switch err {
        case .emptyName: return L("Skriv et navn.")
        case .nameTooLong: return L("Navnet kan ha høyst {0} tegn.", maxNameLength)
        case .duplicateName: return L("Det finnes allerede et forhåndsvalg med dette navnet.")
        case .noPairs: return L("Legg til minst ett utgangspar.")
        case .tooManyPresets: return L("Det kan være høyst {0} forhåndsvalg.", maxPresets)
        default: return L("Forhåndsvalget finnes ikke.")
        }
    }

    /// Typene som har routing (de samme som har en routingmatrise i panelet).
    static func isRouted(_ cue: Cue) -> Bool {
        switch cue.type {
        case .audio, .video, .playlist, .slideshow, .input: return true
        default: return false
        }
    }

    /// Gir cuen routingen forhåndsvalget gir for dens kanaler. Cuer uten lyd eller uten utgang som får plass hoppes over.
    static func apply(_ targets: [Cue?], preset: RoutingPreset, busCount: Int = 0) -> RoutingApplyResult {
        each(targets) { ch in
            let r = routes(preset, fileChannels: ch, busCount: busCount)
            if r.isEmpty { return (false, nil) }
            return (true, r)
        }
    }

    /// Setter den samme routingen (eller standard, ved nil) på alle cuene. Kanaler og utganger som ikke finnes i en cue fjernes.
    /// Er det ikke noe igjen av en routing som hadde ruter, hoppes cuen over så den ikke blir stum ved et uhell.
    static func applyRoutes(_ targets: [Cue?], routes: [AudioRoute]?, busCount: Int = 0) -> RoutingApplyResult {
        each(targets) { ch in
            guard let routes else { return (true, nil) }
            let clean = cleanRoutes(routes, fileChannels: ch, busCount: busCount)
            if clean.isEmpty && !routes.isEmpty { return (false, nil) }
            return (true, clean)
        }
    }

    /// Limer inn kopiert routing på cuene.
    static func paste(_ targets: [Cue?], clipboard: RouteClipboard, busCount: Int = 0) -> RoutingApplyResult {
        clipboard.hasContent ? applyRoutes(targets, routes: clipboard.routes, busCount: busCount) : RoutingApplyResult(0, 0, 0)
    }

    private static func each(_ targets: [Cue?], _ make: (Int) -> (ok: Bool, routes: [AudioRoute]?)) -> RoutingApplyResult {
        var changed = 0, same = 0, skipped = 0
        var seen = Set<ObjectIdentifier>()
        var sawNil = false
        for t in targets {
            guard let c = t else {
                if !sawNil { sawNil = true; skipped += 1 }
                continue
            }
            if !seen.insert(ObjectIdentifier(c)).inserted { continue }
            guard isRouted(c), c.fileChannels > 0 else { skipped += 1; continue }
            let (ok, routes) = make(c.fileChannels)
            if !ok { skipped += 1; continue }
            if sameRoutes(c.routes, routes) { same += 1; continue }
            c.routes = routes
            changed += 1
        }
        return RoutingApplyResult(changed, same, skipped)
    }

    /// Kort beskrivelse av routingen: navnet på et forhåndsvalg som gir akkurat denne routingen, ellers «Standard», «Av»
    /// eller «Utgang 1-2, 5».
    static func describe(_ routes: [AudioRoute]?, fileChannels: Int, presets: [RoutingPreset]?, busCount: Int = 0) -> String {
        guard let routes else { return L("Standard") }
        if routes.isEmpty { return L("Av") }
        for p in presets ?? [] where sameRoutes(routes, self.routes(p, fileChannels: fileChannels, busCount: busCount)) { return p.name }
        return L("Utgang {0}", ranges(routes.map { $0.bus }))
    }
}

/// Kopiert routing fra en cue, klar til å limes inn på andre. Lagrer en kopi, så senere endringer i kilden ikke slår gjennom.
struct RouteClipboard {
    private(set) var hasContent = false
    /// Kopiert routing. nil = standard.
    private(set) var routes: [AudioRoute]? = nil

    /// Kopierer routingen til cuen. Cuer uten routing (f.eks. bilde) gir false og endrer ingenting.
    @discardableResult
    mutating func copy(_ cue: Cue?) -> Bool {
        guard let cue, RoutingPresets.isRouted(cue) else { return false }
        routes = cue.routes
        hasContent = true
        return true
    }

    mutating func clear() { hasContent = false; routes = nil }
}
