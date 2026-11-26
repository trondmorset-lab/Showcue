import Foundation

/// Leser en liste med cuenumre («2, 5, 8-10») slik gruppe-cuer skriver den.
enum CueTargets {
    /// Finner cuene en liste peker på. Hvert ledd er et cuenummer eller et område av tall («8-10» = alle cuer med et tall fra 8 til 10).
    /// Leddene skilles med komma eller semikolon. `selfCue` tas aldri med, og ingen cue kommer med to ganger.
    /// Ledd som ikke finnes returneres som `missing`.
    /// Scener: «Scene:nummer» søker bare i den scenen. Et vanlig ledd søkes først i hjemmescenen
    /// (`homeScene`, ellers scenen til `selfCue`) og deretter i alle scener.
    static func resolve(_ cues: [Cue], _ spec: String, excluding selfCue: Cue? = nil, homeScene: String? = nil) -> (cues: [Cue], missing: [String]) {
        var result: [Cue] = []
        var missing: [String] = []
        let home = homeScene ?? selfCue?.scene
        func add(_ c: Cue) {
            if c !== selfCue, !result.contains(where: { $0 === c }) { result.append(c) }
        }
        for raw in spec.split(whereSeparator: { $0 == "," || $0 == ";" }) {
            let token = raw.trimmingCharacters(in: .whitespaces)
            if token.isEmpty { continue }
            var found: [Cue]?
            if let (scene, rest) = SceneLogic.split(token), !rest.isEmpty, cues.contains(where: { SceneLogic.inScene($0, scene) }) {
                found = find(cues.filter { SceneLogic.inScene($0, scene) }, rest)
            } else {
                if let home { found = find(cues.filter { SceneLogic.inScene($0, home) }, token) }
                if found == nil { found = find(cues, token) }
            }
            guard let hits = found else { missing.append(token); continue }
            hits.forEach(add)
        }
        return (result, missing)
    }

    /// Treff i en liste cuer: nøyaktig nummer, ellers cuene i et tallområde. nil hvis ingen.
    private static func find(_ pool: [Cue], _ token: String) -> [Cue]? {
        if let exact = pool.first(where: { $0.number.caseInsensitiveCompare(token) == .orderedSame }) { return [exact] }
        if let (lo, hi) = range(token) {
            let inRange = pool.filter { c in number(c.number).map { $0 >= lo && $0 <= hi } ?? false }
            if !inRange.isEmpty { return inRange }
        }
        return nil
    }

    /// Bytter cuenumre i listen etter en tabell (gammelt nummer med små bokstaver → nytt), også i endene av et område.
    /// Med `qualifier` endres bare ledd som står som «Scene:nummer» for den scenen; uten endres bare ledd uten scene.
    static func renumber(_ spec: String, _ map: [String: String], qualifier: String? = nil) -> String {
        var parts: [String] = []
        let prefix = qualifier.map { $0 + String(SceneLogic.qualifier) }
        for raw in spec.split(whereSeparator: { $0 == "," || $0 == ";" }) {
            let p = raw.trimmingCharacters(in: .whitespaces)
            if p.isEmpty { continue }
            if let prefix {
                if p.lowercased().hasPrefix(prefix.lowercased()) {
                    let head = String(p.prefix(prefix.count))
                    parts.append(head + renumberToken(String(p.dropFirst(prefix.count)).trimmingCharacters(in: .whitespaces), map))
                } else {
                    parts.append(p)
                }
                continue
            }
            if p.contains(SceneLogic.qualifier) && map[p.lowercased()] == nil { parts.append(p); continue }
            parts.append(renumberToken(p, map))
        }
        return parts.joined(separator: ", ")
    }

    private static func renumberToken(_ p: String, _ map: [String: String]) -> String {
        func mapped(_ s: String) -> String { map[s.lowercased()] ?? s }
        if let whole = map[p.lowercased()] { return whole }
        if let dash = dashIndex(p) {
            let left = String(p[..<dash]).trimmingCharacters(in: .whitespaces)
            let right = String(p[p.index(after: dash)...]).trimmingCharacters(in: .whitespaces)
            if map[left.lowercased()] != nil || map[right.lowercased()] != nil { return mapped(left) + "-" + mapped(right) }
        }
        return p
    }

    /// Bytter scenenavnet i «Scene:nummer»-ledd. Listen røres ikke hvis ingen ledd peker på scenen.
    static func renameQualifier(_ spec: String, from old: String, to new: String) -> String {
        let prefix = (old + String(SceneLogic.qualifier)).lowercased()
        var parts = spec.split(whereSeparator: { $0 == "," || $0 == ";" }).map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }
        var changed = false
        for i in parts.indices where parts[i].lowercased().hasPrefix(prefix) {
            parts[i] = new + String(SceneLogic.qualifier) + String(parts[i].dropFirst(prefix.count))
            changed = true
        }
        return changed ? parts.joined(separator: ", ") : spec
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
