import Foundation

/// Resultatet av å slå opp et «gå til»-mål: scenen (kanonisk navn, tom i et show uten scener) og cuen (nil = bare bytt scene).
struct GotoTarget {
    let scene: String
    let cue: Cue?
}

/// Ren logikk for scener (cuelister). Hver cue hører til én scene (`Cue.scene`); showet har en ordnet liste med scenenavn
/// (`ShowDocument.scenes`). Et show uten scener er én scene uten navn: alle cuer har tom `scene`, og alt oppfører seg som før.
/// Cuene ligger fortsatt i én felles liste i showfila, så eldre versjoner leser den som én lang liste.
///
/// Oppslag av cuenummer (mål for gå til, fade, stopp, gruppe, fjernstyring):
///  1. «Scene:nummer» søker bare i den scenen (hvis scenen finnes).
///  2. Et vanlig nummer søkes først i hjemmescenen (scenen til cuen som peker, eller aktiv scene for fjernstyring), deretter i alle scener (første treff i listen).
/// Samme nummer kan altså brukes i flere scener.
enum SceneLogic {
    /// Skille mellom scene og nummer i «Scene:nummer».
    static let qualifier: Character = ":"

    /// Navnet på den første scenen når et show uten scener får sin andre scene.
    static var defaultName: String { L("Hoved") }

    /// Scenenavn uten tegn som har egen betydning i mållister og OSC-adresser (: , ; / \ og linjeskift), uten mellomrom i endene, maks 60 tegn.
    static func cleanName(_ name: String?) -> String {
        guard let name else { return "" }
        let replaced = String(name.trimmingCharacters(in: .whitespacesAndNewlines).map { ":,;/\\\n\r\t".contains($0) ? " " : $0 })
        let clean = replaced.trimmingCharacters(in: .whitespaces)
        return clean.count > 60 ? String(clean.prefix(60)).trimmingCharacters(in: .whitespaces) : clean
    }

    static func sameScene(_ a: String?, _ b: String?) -> Bool {
        (a ?? "").trimmingCharacters(in: .whitespaces).caseInsensitiveCompare((b ?? "").trimmingCharacters(in: .whitespaces)) == .orderedSame
    }

    static func inScene(_ c: Cue, _ scene: String?) -> Bool { sameScene(c.scene, scene) }

    /// Cuene i scenen, i listerekkefølge.
    static func cues(in scene: String?, of cues: [Cue]) -> [Cue] { cues.filter { inScene($0, scene) } }

    // MARK: Scenelista

    /// Rydder scenelista (renser navn, fjerner tomme og like) og tar med scener som cuene bruker men lista mangler. Hver cue får det kanoniske
    /// scenenavnet; en cue uten scene havner i den første. Gir tom liste (én scene uten navn) når verken lista eller cuene har scener.
    static func normalize(_ names: [String]?, _ cues: [Cue]) -> [String] {
        var result: [String] = []
        func add(_ n: String?) {
            let clean = cleanName(n)
            if !clean.isEmpty, !result.contains(where: { sameScene($0, clean) }) { result.append(clean) }
        }
        (names ?? []).forEach { add($0) }
        cues.forEach { add($0.scene) }
        if result.isEmpty { return result }
        for c in cues {
            let canon = result.first { sameScene($0, c.scene) } ?? result[0]
            if c.scene != canon { c.scene = canon }
        }
        return result
    }

    /// Det kanoniske navnet på en scene som finnes (i lista eller hos en cue), ellers nil.
    static func canonical(_ scenes: [String], _ cues: [Cue], _ name: String?) -> String? {
        let n = cleanName(name)
        if n.isEmpty { return nil }
        return scenes.first { sameScene($0, n) } ?? cues.map(\.scene).first { !$0.isEmpty && sameScene($0, n) }
    }

    /// Legger til en scene sist. Første gang får alle eksisterende cuer navnet `defaultName`.
    /// Returnerer navnet som ble lagt til, eller nil hvis navnet er tomt eller allerede i bruk.
    static func addScene(_ scenes: inout [String], _ cues: [Cue], _ name: String?) -> String? {
        let n = cleanName(name)
        if n.isEmpty { return nil }
        if scenes.isEmpty {
            if sameScene(n, defaultName) { return nil }
            let first = defaultName
            scenes.append(first)
            cues.forEach { $0.scene = first }
        }
        if scenes.contains(where: { sameScene($0, n) }) { return nil }
        scenes.append(n)
        return n
    }

    /// Gir en scene nytt navn. Cuene, «gå til»-målene og «Scene:nummer»-mål følger med. I et show uten scener gir den scenen uten navn sitt første navn.
    /// False hvis navnet er tomt eller opptatt, eller scenen ikke finnes.
    static func renameScene(_ scenes: inout [String], _ cues: [Cue], _ oldName: String?, _ newName: String?) -> Bool {
        let n = cleanName(newName)
        if n.isEmpty { return false }
        if scenes.isEmpty {
            guard sameScene(oldName, "") else { return false }
            scenes.append(n)
            cues.forEach { $0.scene = n }
            return true
        }
        guard let i = scenes.firstIndex(where: { sameScene($0, oldName) }) else { return false }
        if scenes.contains(where: { sameScene($0, n) && !sameScene($0, oldName) }) { return false }
        let old = scenes[i]
        scenes[i] = n
        for c in cues {
            if sameScene(c.scene, old) { c.scene = n }
            if c.type == .goto, sameScene(c.targetScene, old) { c.targetScene = n }
            rewriteTargets(c) { CueTargets.renameQualifier($0, from: old, to: n) }
        }
        return true
    }

    /// Tar bort en scene. Cuene flyttes til scenen foran (eller den neste hvis den var først). Returnerer scenen de havnet i,
    /// eller nil hvis scenen ikke finnes eller er den siste.
    static func deleteScene(_ scenes: inout [String], _ cues: [Cue], _ name: String?) -> String? {
        guard let i = scenes.firstIndex(where: { sameScene($0, name) }), scenes.count >= 2 else { return nil }
        let gone = scenes[i]
        let heir = scenes[i > 0 ? i - 1 : 1]
        scenes.remove(at: i)
        for c in cues {
            if sameScene(c.scene, gone) { c.scene = heir }
            if c.type == .goto, sameScene(c.targetScene, gone) { c.targetScene = heir }
            rewriteTargets(c) { CueTargets.renameQualifier($0, from: gone, to: heir) }
        }
        return heir
    }

    /// Flytter scenen ett steg i rekkefølgen (−1 = mot starten). False hvis den allerede står ytterst.
    static func moveScene(_ scenes: inout [String], _ name: String?, _ delta: Int) -> Bool {
        guard let i = scenes.firstIndex(where: { sameScene($0, name) }), delta != 0 else { return false }
        let j = i + (delta > 0 ? 1 : -1)
        guard j >= 0, j < scenes.count else { return false }
        scenes.swapAt(i, j)
        return true
    }

    /// Scenen ett steg før/etter `current`, eller nil i enden av lista.
    static func neighbour(_ scenes: [String], _ current: String?, _ delta: Int) -> String? {
        guard let i = scenes.firstIndex(where: { sameScene($0, current) }), delta != 0 else { return nil }
        let j = i + (delta > 0 ? 1 : -1)
        return j >= 0 && j < scenes.count ? scenes[j] : nil
    }

    // MARK: Plassering og nummerering

    /// Plassen rett etter siste cue i scenen (eller slutten av lista hvis scenen er tom).
    static func endOfScene(_ cues: [Cue], _ scene: String?) -> Int {
        cues.lastIndex { inScene($0, scene) }.map { $0 + 1 } ?? cues.count
    }

    /// Flytter cuene til slutten av en annen scene. En cue som får samme nummer som en annen i den scenen, får et nytt nummer.
    static func moveToScene(_ cues: inout [Cue], _ moving: [Cue], _ scene: String) {
        let ordered = moving.sorted { a, b in
            (cues.firstIndex { $0 === a } ?? 0) < (cues.firstIndex { $0 === b } ?? 0)
        }
        for c in ordered {
            guard !sameScene(c.scene, scene), let i = cues.firstIndex(where: { $0 === c }) else { continue }
            cues.remove(at: i)
            c.scene = scene
            let number = c.number
            if number.isEmpty || cues.contains(where: { inScene($0, scene) && $0.number.caseInsensitiveCompare(number) == .orderedSame }) {
                c.number = nextNumber(cues, scene)
            }
            cues.insert(c, at: endOfScene(cues, scene))
        }
    }

    /// Neste ledige hele nummer i scenen (størst + 1).
    static func nextNumber(_ cues: [Cue], _ scene: String?) -> String {
        let maxN = cues.filter { inScene($0, scene) }
            .compactMap { Double($0.number.replacingOccurrences(of: ",", with: ".")) }
            .filter { $0.isFinite }.max() ?? 0
        return String(Int(min(maxN, 1e9).rounded(.down)) + 1)
    }

    /// Gir cuene i scenen nye nummer (1, 2, 3 …) i listerekkefølge, og oppdaterer målene som pekte på de gamle numrene:
    /// vanlige nummer hos cuer som peker inn i scenen, og «Scene:nummer» hos alle. Returnerer tabellen gammelt (små bokstaver) → nytt nummer.
    @discardableResult
    static func renumber(_ cues: [Cue], _ scene: String?) -> [String: String] {
        var map: [String: String] = [:]
        var n = 0
        for c in cues where inScene(c, scene) {
            n += 1
            let number = String(n)
            if !c.number.isEmpty, map[c.number.lowercased()] == nil { map[c.number.lowercased()] = number }
            c.number = number
        }
        let qualifierName = cleanName(scene)
        for c in cues {
            let home = c.type == .goto && !c.targetScene.isEmpty ? c.targetScene : c.scene
            let pointsIntoScene = sameScene(home, scene)
            rewriteTargets(c) { spec in
                let s = pointsIntoScene ? CueTargets.renumber(spec, map) : spec
                return qualifierName.isEmpty ? s : CueTargets.renumber(s, map, qualifier: qualifierName)
            }
        }
        return map
    }

    /// Bruker `map` på målfeltet til en fade-, stopp-, «gå til»- eller gruppe-cue.
    static func rewriteTargets(_ c: Cue, _ map: (String) -> String) {
        switch c.type {
        case .fade, .stop, .goto: c.target = map(c.target)
        case .group: c.targets = map(c.targets)
        default: break
        }
    }

    // MARK: GO-markør og rekkefølge innen en scene

    /// Indeksen GO-markøren skal til etter at cuen på `fired` er avfyrt: hopper over cuer som starter automatisk og deaktiverte
    /// (i øvelsesmodus også merkede). Gir `list.count` ved slutten.
    static func nextPlayheadIndex(_ list: [Cue], fired: Int, rehearsal: Bool = false) -> Int {
        var i = fired
        while i < list.count && list[i].continueMode != .none {
            i = Rehearsal.nextPlayableIndex(list, from: i + 1, mode: rehearsal)
        }
        i = Rehearsal.nextPlayableIndex(list, from: i + 1, mode: rehearsal)
        return min(i, list.count)
    }

    /// Neste cue for GO etter at `fired` er avfyrt, bare innen samme scene. nil = slutten av scenen (GO hopper aldri til en annen scene).
    static func nextPlayhead(_ cues: [Cue], after fired: Cue, rehearsal: Bool = false) -> Cue? {
        let list = Self.cues(in: fired.scene, of: cues)
        guard let idx = list.firstIndex(where: { $0 === fired }) else { return nil }
        let n = nextPlayheadIndex(list, fired: idx, rehearsal: rehearsal)
        return n < list.count ? list[n] : nil
    }

    /// Første cue etter `cue` i samme scene som kan spilles: ikke deaktivert, og i øvelsesmodus ikke merket (for auto-fortsett).
    static func nextEnabled(_ cues: [Cue], after cue: Cue, rehearsal: Bool = false) -> Cue? {
        let list = Self.cues(in: cue.scene, of: cues)
        guard let idx = list.firstIndex(where: { $0 === cue }) else { return nil }
        let n = Rehearsal.nextPlayableIndex(list, from: idx + 1, mode: rehearsal)
        return n < list.count ? list[n] : nil
    }

    /// Første ikke-deaktiverte cue i scenen (der GO-markøren starter).
    static func firstEnabled(_ cues: [Cue], _ scene: String?) -> Cue? { cues.first { inScene($0, scene) && !$0.disabled } }

    /// Flytter markøren `delta` steg i cuene i en scene, men ikke ut over endene.
    static func step(_ sceneCues: [Cue], from current: Cue?, _ delta: Int) -> Cue? {
        if sceneCues.isEmpty { return nil }
        var idx = current.flatMap { c in sceneCues.firstIndex { $0 === c } } ?? -1
        if idx < 0 { idx = delta > 0 ? -1 : sceneCues.count }
        return sceneCues[max(0, min(sceneCues.count - 1, idx + delta))]
    }

    /// Plassen til nærmeste cue i samme scene i retning av `delta` (for «flytt opp/ned»), eller −1.
    static func neighbourIndex(_ cues: [Cue], _ index: Int, _ delta: Int) -> Int {
        guard index >= 0, index < cues.count, delta != 0 else { return -1 }
        let dir = delta > 0 ? 1 : -1
        var i = index + dir
        while i >= 0 && i < cues.count {
            if inScene(cues[i], cues[index].scene) { return i }
            i += dir
        }
        return -1
    }

    // MARK: Oppslag av mål

    /// Deler «Scene:nummer» i scene og nummer. Nummeret kan være tomt («Scene:» = hele scenen).
    static func split(_ token: String?) -> (scene: String, rest: String)? {
        let t = (token ?? "").trimmingCharacters(in: .whitespaces)
        guard let i = t.firstIndex(of: qualifier), i != t.startIndex else { return nil }
        let scene = String(t[..<i]).trimmingCharacters(in: .whitespaces)
        let rest = String(t[t.index(after: i)...]).trimmingCharacters(in: .whitespaces)
        if scene.isEmpty { return nil }
        return (scene: scene, rest: rest)
    }

    private static func sameNumber(_ c: Cue, _ n: String) -> Bool {
        c.number.trimmingCharacters(in: .whitespaces).caseInsensitiveCompare(n) == .orderedSame
    }

    /// Cuene et nummer peker på. «Scene:nummer» (der scenen har cuer) gir treffene i den scenen. Et vanlig nummer gir treffene i
    /// `home` hvis det finnes noen der, ellers treffene i alle scener. `home` = nil hopper over hjemmescenen.
    static func matching(_ cues: [Cue], _ token: String?, home: String?) -> [Cue] {
        let t = (token ?? "").trimmingCharacters(in: .whitespaces)
        if t.isEmpty { return [] }
        if let (scene, rest) = split(t), !rest.isEmpty, cues.contains(where: { inScene($0, scene) }) {
            return cues.filter { inScene($0, scene) && sameNumber($0, rest) }
        }
        if let home {
            let inHome = cues.filter { inScene($0, home) && sameNumber($0, t) }
            if !inHome.isEmpty { return inHome }
        }
        return cues.filter { sameNumber($0, t) }
    }

    /// Første cue et nummer peker på (se `matching`), eller nil.
    static func find(_ cues: [Cue], _ token: String?, home: String?) -> Cue? { matching(cues, token, home: home).first }

    /// Målteksten en «gå til»-cue sender til motoren: nummeret, «Scene:nummer», eller «Scene:» når bare scenen skal byttes.
    static func gotoSpec(target: String?, targetScene: String?) -> String {
        let t = (target ?? "").trimmingCharacters(in: .whitespaces)
        let s = cleanName(targetScene)
        if s.isEmpty { return t }
        if t.isEmpty { return s + String(qualifier) }
        if let i = t.firstIndex(of: qualifier), i != t.startIndex { return t }
        return s + String(qualifier) + t
    }

    /// Slår opp en målstreng fra `gotoSpec`. nil hvis scenen eller cuen ikke finnes.
    static func resolveGoto(_ cues: [Cue], scenes: [String], spec: String?, home: String?) -> GotoTarget? {
        let t = (spec ?? "").trimmingCharacters(in: .whitespaces)
        if t.isEmpty { return nil }
        if let (sc, rest) = split(t), rest.isEmpty {
            return canonical(scenes, cues, sc).map { GotoTarget(scene: $0, cue: nil) }
        }
        return find(cues, t, home: home).map { GotoTarget(scene: $0.scene, cue: $0) }
    }
}
