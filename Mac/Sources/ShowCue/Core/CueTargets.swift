import Foundation

/// Leser en liste med cuenumre («2, 5, 8-10») slik gruppe-cuer skriver den.
enum CueTargets {
    /// Finner cuene en liste peker på. Hvert ledd er et cuenummer eller et område av tall («8-10» = alle cuer med et tall fra 8 til 10).
    /// Leddene skilles med komma eller semikolon. `selfCue` tas aldri med, og ingen cue kommer med to ganger.
    /// Ledd som ikke finnes returneres som `missing`.
    static func resolve(_ cues: [Cue], _ spec: String, excluding selfCue: Cue? = nil) -> (cues: [Cue], missing: [String]) {
        var result: [Cue] = []
        var missing: [String] = []
        func add(_ c: Cue) {
            if c !== selfCue, !result.contains(where: { $0 === c }) { result.append(c) }
        }
        for raw in spec.split(whereSeparator: { $0 == "," || $0 == ";" }) {
            let token = raw.trimmingCharacters(in: .whitespaces)
            if token.isEmpty { continue }
            if let exact = cues.first(where: { $0.number.caseInsensitiveCompare(token) == .orderedSame }) { add(exact); continue }
            if let (lo, hi) = range(token) {
                let inRange = cues.filter { c in number(c.number).map { $0 >= lo && $0 <= hi } ?? false }
                if !inRange.isEmpty { inRange.forEach(add); continue }
            }
            missing.append(token)
        }
        return (result, missing)
    }

    /// Bytter cuenumre i listen etter en tabell (gammelt nummer med små bokstaver → nytt), også i endene av et område.
    static func renumber(_ spec: String, _ map: [String: String]) -> String {
        func mapped(_ s: String) -> String { map[s.lowercased()] ?? s }
        var parts: [String] = []
        for raw in spec.split(whereSeparator: { $0 == "," || $0 == ";" }) {
            let p = raw.trimmingCharacters(in: .whitespaces)
            if p.isEmpty { continue }
            if let whole = map[p.lowercased()] { parts.append(whole); continue }
            if let dash = dashIndex(p) {
                let left = String(p[..<dash]).trimmingCharacters(in: .whitespaces)
                let right = String(p[p.index(after: dash)...]).trimmingCharacters(in: .whitespaces)
                if map[left.lowercased()] != nil || map[right.lowercased()] != nil { parts.append(mapped(left) + "-" + mapped(right)); continue }
            }
            parts.append(p)
        }
        return parts.joined(separator: ", ")
    }

    private static func number(_ s: String) -> Double? {
        guard let d = Double(s.trimmingCharacters(in: .whitespaces)), d.isFinite else { return nil }
        return d
    }

    /// Plassen til bindestreken som skiller et område (ikke et eventuelt minustegn først).
    private static func dashIndex(_ token: String) -> String.Index? {
        guard token.count > 1 else { return nil }
        return token.dropFirst().firstIndex(of: "-")
    }

    private static func range(_ token: String) -> (Double, Double)? {
        guard let dash = dashIndex(token),
              let a = number(String(token[..<dash])),
              let b = number(String(token[token.index(after: dash)...])) else { return nil }
        return (min(a, b), max(a, b))
    }
}
