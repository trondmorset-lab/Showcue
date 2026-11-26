import Foundation

/// Hva slags forberedelse en fil trenger.
enum PreloadKind: Int { case audio, video, image }

/// Én fil som kan forberedes før GO. `key` avgjør om to elementer er «det samme» (type og sti); resten er hint om hvordan fila skal spilles.
struct PreloadItem: Equatable {
    let kind: PreloadKind
    let path: String
    var startTime: Double = 0
    var endTime: Double = 0
    var loop = false
    var key: String { "\(kind.rawValue)|\(path)" }
}

/// Ren logikk for hvilke cuer som forberedes i bakgrunnen: cuen GO vil starte nå, og cuene den starter automatisk etterpå.
/// Ingen filtilgang og ingen tråder her, så det kan testes uten maskinvare.
enum PreloadPlanner {
    /// Største antall forberedte cuer samtidig.
    static let maxItems = 3
    /// Lengste kjede av cuer (hopp, gå til, fortsett) som følges, så en løkke aldri henger.
    private static let maxSteps = 16

    /// Filene som bør være klare for GO-markøren på `playheadIndex`, viktigst først.
    /// Cuen under markøren (eller neste som ikke er deaktivert, slik GO velger), så cuene den starter automatisk
    /// (fortsett automatisk / følg automatisk), «gå til» følges til målet, og en gruppe gir sine mål.
    /// Deaktiverte cuer, PDF, nettsider, spillelister og lysbildeserier forberedes ikke.
    static func plan(cues: [Cue], playheadIndex: Int, max limit: Int = maxItems) -> [PreloadItem] {
        var result: [PreloadItem] = []
        let limit = Swift.min(Swift.max(limit, 0), maxItems)
        guard limit > 0, playheadIndex >= 0, playheadIndex < cues.count else { return result }

        var visited = Set<Int>()
        var i = firstEnabled(cues, from: playheadIndex)
        var step = 0
        while i >= 0, i < cues.count, step < maxSteps, result.count < limit, visited.insert(i).inserted {
            step += 1
            let cue = cues[i]
            if cue.type == .goto {
                let target = indexOfNumber(cues, cue.target)
                if target < 0 { break }
                i = firstEnabled(cues, from: target) // GO på en deaktivert mål-cue hopper videre
                continue
            }
            if cue.type == .group {
                for t in CueTargets.resolve(cues, cue.targets, excluding: cue).cues where !t.disabled { add(&result, t, limit) }
            } else {
                add(&result, cue, limit)
            }
            if cue.continueMode == .none { break }
            i = firstEnabled(cues, from: i + 1)
        }
        return result
    }

    /// Første cue fra `from` som ikke er deaktivert (−1 hvis ingen).
    private static func firstEnabled(_ cues: [Cue], from: Int) -> Int {
        var i = Swift.max(0, from)
        while i < cues.count { if !cues[i].disabled { return i }; i += 1 }
        return -1
    }

    private static func indexOfNumber(_ cues: [Cue], _ number: String) -> Int {
        let n = number.trimmingCharacters(in: .whitespaces)
        if n.isEmpty { return -1 }
        return cues.firstIndex { $0.number.caseInsensitiveCompare(n) == .orderedSame } ?? -1
    }

    private static func add(_ result: inout [PreloadItem], _ cue: Cue, _ limit: Int) {
        guard result.count < limit else { return }
        let kind: PreloadKind
        switch cue.type {
        case .audio: kind = .audio
        case .video: kind = .video
        case .image: kind = .image
        default: return
        }
        let path = cue.filePath.trimmingCharacters(in: .whitespaces)
        if path.isEmpty { return }
        let start = cue.startTime.isFinite ? Swift.min(Swift.max(cue.startTime, 0), 86400) : 0
        let end = cue.endTime.isFinite ? Swift.min(Swift.max(cue.endTime, 0), 86400) : 0
        let item = PreloadItem(kind: kind, path: path, startTime: kind == .image ? 0 : start, endTime: kind == .image ? 0 : end,
                               loop: kind == .image ? false : cue.loop)
        if !result.contains(where: { $0.key == item.key }) { result.append(item) }
    }
}

/// Hva som må gjøres for å gå fra de forberedte filene til de ønskede.
struct PreloadDiff: Equatable {
    var toStart: [String]
    var toEvict: [String]
}

/// Regnskap over hvilke filer som er forberedt (eller under forberedelse), med fast øvre grense.
/// Ren logikk uten filer og tråder. Det som ikke lenger ønskes fjernes, eldste først, og aldri flere enn `capacity` beholdes.
struct PreloadBook {
    let capacity: Int
    /// Nøklene som holdes nå, i prioritetsrekkefølge (viktigst først).
    private(set) var keys: [String] = []

    init(capacity: Int = PreloadPlanner.maxItems) { self.capacity = Swift.min(Swift.max(capacity, 1), 16) }

    func contains(_ key: String) -> Bool { keys.contains(key) }

    /// Gjør de ønskede nøklene (viktigst først) til de som holdes. Dobbelte og alt utover grensen ignoreres.
    @discardableResult
    mutating func sync(_ wanted: [String]) -> PreloadDiff {
        var keep: [String] = []
        for k in wanted {
            if keep.count >= capacity { break }
            if !k.isEmpty, !keep.contains(k) { keep.append(k) }
        }
        let evict = keys.filter { !keep.contains($0) } // i den rekkefølgen de ble lagt til: eldste først
        let start = keep.filter { !keys.contains($0) }
        keys = keep
        return PreloadDiff(toStart: start, toEvict: evict)
    }

    /// Slutt å holde en nøkkel (f.eks. fordi den ble overlevert til en avspilling). False hvis den ikke var med.
    @discardableResult
    mutating func remove(_ key: String) -> Bool {
        guard let i = keys.firstIndex(of: key) else { return false }
        keys.remove(at: i)
        return true
    }

    /// Slipp alt. Gir nøklene som ble holdt, eldste først.
    @discardableResult
    mutating func clear() -> [String] {
        let all = keys
        keys = []
        return all
    }
}
