import XCTest
@testable import ShowCue

/// Scener (cuelister): scenelista, nummerering per scene, GO-markør innen scenen, oppslag av mål, showfil og forhåndssjekk. Samme tester som i Windows-versjonen.
final class SceneTests: XCTestCase {
    private func c(_ scene: String, _ number: String) -> Cue {
        let q = Cue(type: .stop); q.scene = scene; q.number = number; return q
    }
    private func numbers(_ cues: [Cue]) -> [String] { cues.map(\.number) }

    private struct NoEnv: PreflightEnvironment {
        func fileStatus(_ path: String) -> PreflightFileStatus { .ok }
        func audioOutputExists(uid: String) -> Bool { true }
        func inputDeviceExists(uid: String) -> Bool { true }
        func screenExists(_ index: Int) -> Bool { true }
    }

    // MARK: Scenelista

    func testNormalizeWithoutScenesKeepsOneImplicitScene() {
        let cues = [c("", "1"), c("", "2")]
        XCTAssertTrue(SceneLogic.normalize(nil, cues).isEmpty)
        XCTAssertTrue(cues.allSatisfy { $0.scene == "" })
    }

    func testNormalizeTakesSceneNamesFromCuesAndFillsInTheFirstScene() {
        let cues = [c("", "1"), c("Akt 2", "2"), c("akt 1", "3")]
        let scenes = SceneLogic.normalize(["Akt 1", " ", "AKT 1"], cues)
        XCTAssertEqual(scenes, ["Akt 1", "Akt 2"])
        XCTAssertEqual(cues.map(\.scene), ["Akt 1", "Akt 2", "Akt 1"])
    }

    func testSceneNamesLoseCharactersThatBreakTargetLists() {
        XCTAssertEqual(SceneLogic.cleanName(" A:B,C;D "), "A B C D")
        XCTAssertEqual(SceneLogic.cleanName("  "), "")
        XCTAssertEqual(SceneLogic.cleanName(nil), "")
    }

    func testAddingTheFirstSceneNamesTheExistingCues() {
        let cues = [c("", "1"), c("", "2")]
        var scenes: [String] = []
        XCTAssertEqual(SceneLogic.addScene(&scenes, cues, "Akt 2"), "Akt 2")
        XCTAssertEqual(scenes, [SceneLogic.defaultName, "Akt 2"])
        XCTAssertTrue(cues.allSatisfy { $0.scene == SceneLogic.defaultName })
        XCTAssertNil(SceneLogic.addScene(&scenes, cues, "akt 2")) // navnet er i bruk
        XCTAssertNil(SceneLogic.addScene(&scenes, cues, "  "))
    }

    func testRenamingASceneMovesCuesAndTargetsAlong() {
        let group = Cue(type: .group); group.scene = "Akt 2"; group.targets = "Akt 1:3, 4"
        let jump = Cue(type: .goto); jump.scene = "Akt 2"; jump.targetScene = "Akt 1"
        let cues = [c("Akt 1", "3"), group, jump]
        var scenes = ["Akt 1", "Akt 2"]
        XCTAssertFalse(SceneLogic.renameScene(&scenes, cues, "Akt 1", "akt 2")) // opptatt
        XCTAssertTrue(SceneLogic.renameScene(&scenes, cues, "Akt 1", "Prolog"))
        XCTAssertEqual(scenes, ["Prolog", "Akt 2"])
        XCTAssertEqual(cues[0].scene, "Prolog")
        XCTAssertEqual(group.targets, "Prolog:3, 4")
        XCTAssertEqual(jump.targetScene, "Prolog")
    }

    func testRenamingTheUnnamedSceneGivesItItsFirstName() {
        let cues = [c("", "1")]
        var scenes: [String] = []
        XCTAssertTrue(SceneLogic.renameScene(&scenes, cues, "", "Hele showet"))
        XCTAssertEqual(scenes, ["Hele showet"])
        XCTAssertEqual(cues[0].scene, "Hele showet")
    }

    func testDeletingASceneMovesItsCuesToTheOneBefore() {
        let cues = [c("A", "1"), c("B", "1"), c("C", "1")]
        var scenes = ["A", "B", "C"]
        XCTAssertEqual(SceneLogic.deleteScene(&scenes, cues, "B"), "A")
        XCTAssertEqual(scenes, ["A", "C"])
        XCTAssertEqual(cues.map(\.scene), ["A", "A", "C"])
        XCTAssertEqual(SceneLogic.deleteScene(&scenes, cues, "A"), "C") // først i lista: neste scene
        XCTAssertNil(SceneLogic.deleteScene(&scenes, cues, "C"))        // siste scene kan ikke fjernes
    }

    func testScenesCanBeReorderedAndNeighboursFound() {
        var scenes = ["A", "B", "C"]
        XCTAssertTrue(SceneLogic.moveScene(&scenes, "C", -1))
        XCTAssertEqual(scenes, ["A", "C", "B"])
        XCTAssertFalse(SceneLogic.moveScene(&scenes, "A", -1))
        XCTAssertEqual(SceneLogic.neighbour(scenes, "C", 1), "B")
        XCTAssertNil(SceneLogic.neighbour(scenes, "B", 1))
        XCTAssertNil(SceneLogic.neighbour(scenes, "X", 1))
    }

    // MARK: Nummerering

    func testNextNumberCountsOnlyTheScene() {
        let cues = [c("A", "4"), c("B", "9"), c("A", "2.5")]
        XCTAssertEqual(SceneLogic.nextNumber(cues, "A"), "5")
        XCTAssertEqual(SceneLogic.nextNumber(cues, "B"), "10")
        XCTAssertEqual(SceneLogic.nextNumber(cues, "C"), "1")
    }

    func testRenumberingASceneLeavesOtherScenesAndUpdatesTargets() {
        let a5 = c("Akt 1", "5"), a9 = c("Akt 1", "9")
        let fadeA = Cue(type: .fade); fadeA.scene = "Akt 1"; fadeA.target = "9"
        let b5 = c("Akt 2", "5")
        let fadeB = Cue(type: .fade); fadeB.scene = "Akt 2"; fadeB.target = "Akt 1:9"
        let fadeB2 = Cue(type: .fade); fadeB2.scene = "Akt 2"; fadeB2.target = "5"
        let groupA = Cue(type: .group); groupA.scene = "Akt 1"; groupA.targets = "5, 9, Akt 2:5"
        let cues = [a5, a9, fadeA, b5, fadeB, fadeB2, groupA]
        let map = SceneLogic.renumber(cues, "Akt 1")
        XCTAssertEqual([a5.number, a9.number, fadeA.number], ["1", "2", "3"])
        XCTAssertEqual(groupA.number, "4")
        XCTAssertEqual(b5.number, "5")
        XCTAssertEqual(fadeA.target, "2")
        XCTAssertEqual(fadeB.target, "Akt 1:2")
        XCTAssertEqual(fadeB2.target, "5")
        XCTAssertEqual(groupA.targets, "1, 2, Akt 2:5")
        XCTAssertEqual(map["5"], "1")
    }

    func testRenumberingWithoutScenesWorksAsBefore() {
        let a = c("", "10"), b = c("", "20")
        let go = Cue(type: .goto); go.target = "20"
        let cues = [a, b, go]
        SceneLogic.renumber(cues, "")
        XCTAssertEqual(numbers(cues), ["1", "2", "3"])
        XCTAssertEqual(go.target, "2")
    }

    func testMovingCuesToAnotherSceneAppendsThemAndAvoidsDuplicateNumbers() {
        let a1 = c("Akt 1", "1"), a2 = c("Akt 1", "2"), b1 = c("Akt 2", "1")
        var cues = [a1, a2, b1]
        SceneLogic.moveToScene(&cues, [a1], "Akt 2")
        XCTAssertTrue(cues[0] === a2 && cues[1] === b1 && cues[2] === a1)
        XCTAssertEqual(a1.scene, "Akt 2")
        XCTAssertEqual(a1.number, "2") // 1 var opptatt i Akt 2
        XCTAssertEqual(SceneLogic.endOfScene(cues, "Akt 2"), 3)
        XCTAssertEqual(SceneLogic.endOfScene(cues, "Akt 1"), 1)
        XCTAssertEqual(SceneLogic.endOfScene(cues, "Ukjent"), 3)
    }

    // MARK: GO-markør

    func testGoMovesWithinTheSceneAndSkipsChainsAndDisabled() {
        let x1 = c("A", "1"), y1 = c("B", "1"), x2 = c("A", "2"), x3 = c("A", "3"), x4 = c("A", "4")
        x2.continueMode = .autoContinue
        let list = [x1, y1, x2, x3, x4]
        XCTAssertTrue(SceneLogic.nextPlayhead(list, after: x1) === x2)
        XCTAssertTrue(SceneLogic.nextPlayhead(list, after: x2) === x4) // x2 starter x3 automatisk
        x2.continueMode = .none
        x2.disabled = true
        XCTAssertTrue(SceneLogic.nextPlayhead(list, after: x1) === x3)
    }

    func testGoNeverJumpsToAnotherSceneAtTheEnd() {
        let x1 = c("A", "1"), x2 = c("A", "2"), y1 = c("B", "1")
        let list = [x1, x2, y1]
        XCTAssertNil(SceneLogic.nextPlayhead(list, after: x2))
        XCTAssertNil(SceneLogic.nextEnabled(list, after: x2))
        XCTAssertTrue(SceneLogic.firstEnabled(list, "B") === y1)
    }

    func testStepAndMoveStayInTheScene() {
        let x1 = c("A", "1"), y1 = c("B", "1"), x2 = c("A", "2")
        let list = [x1, y1, x2]
        let inA = SceneLogic.cues(in: "A", of: list)
        XCTAssertTrue(SceneLogic.step(inA, from: nil, 1) === x1)
        XCTAssertTrue(SceneLogic.step(inA, from: nil, -1) === x2)
        XCTAssertTrue(SceneLogic.step(inA, from: x1, 1) === x2)
        XCTAssertTrue(SceneLogic.step(inA, from: x2, 1) === x2) // stopper i enden
        XCTAssertEqual(SceneLogic.neighbourIndex(list, 0, 1), 2)
        XCTAssertEqual(SceneLogic.neighbourIndex(list, 2, -1), 0)
        XCTAssertEqual(SceneLogic.neighbourIndex(list, 1, 1), -1)
        XCTAssertEqual(SceneLogic.neighbourIndex(list, 0, -1), -1)
    }

    // MARK: Oppslag av mål

    private func twoActs() -> (list: [Cue], a1: Cue, a2: Cue, b1: Cue, b2: Cue) {
        let a1 = c("Akt 1", "1"), a2 = c("Akt 1", "2"), b1 = c("Akt 2", "1"), b2 = c("Akt 2", "2")
        return ([a1, b1, a2, b2], a1, a2, b1, b2)
    }

    func testNumbersAreFoundInTheHomeSceneFirstThenEverywhere() {
        var t = twoActs()
        let only7 = c("Akt 2", "7")
        t.list.append(only7)
        XCTAssertTrue(SceneLogic.find(t.list, "2", home: "Akt 2") === t.b2)
        XCTAssertTrue(SceneLogic.find(t.list, "2", home: "Akt 1") === t.a2)
        XCTAssertTrue(SceneLogic.find(t.list, "7", home: "Akt 1") === only7) // finnes ikke i hjemmescenen: søker alle
        XCTAssertTrue(SceneLogic.find(t.list, "1", home: nil) === t.a1)       // uten hjemmescene: første treff i lista
        XCTAssertNil(SceneLogic.find(t.list, "99", home: "Akt 1"))
    }

    func testQualifiedNumbersOnlyLookInThatScene() {
        let t = twoActs()
        XCTAssertTrue(SceneLogic.find(t.list, "akt 2:1", home: "Akt 1") === t.b1)
        XCTAssertTrue(SceneLogic.find(t.list, "Akt 1:1", home: "Akt 2") === t.a1)
        XCTAssertNil(SceneLogic.find(t.list, "Akt 1:7", home: "Akt 2")) // scenen finnes, men ikke nummeret
        XCTAssertNil(SceneLogic.find(t.list, "Akt 3:1", home: "Akt 2")) // scenen finnes ikke
    }

    func testGroupTargetsUseTheGroupsSceneFirst() {
        var t = twoActs()
        let group = Cue(type: .group); group.scene = "Akt 2"; group.targets = "1, 2"
        t.list.append(group)
        XCTAssertTrue(CueTargets.resolve(t.list, group.targets, excluding: group).cues.elementsEqual([t.b1, t.b2], by: { $0 === $1 }))
        group.targets = "Akt 1:1, 2"
        XCTAssertTrue(CueTargets.resolve(t.list, group.targets, excluding: group).cues.elementsEqual([t.a1, t.b2], by: { $0 === $1 }))
        XCTAssertTrue(CueTargets.resolve(t.list, "1-2", homeScene: "Akt 1").cues.elementsEqual([t.a1, t.a2], by: { $0 === $1 }))
        XCTAssertEqual(CueTargets.resolve(t.list, "Akt 3:1, 9", excluding: group).missing, ["Akt 3:1", "9"])
    }

    func testGotoSpecAndResolveGoto() {
        XCTAssertEqual(SceneLogic.gotoSpec(target: "5", targetScene: ""), "5")
        XCTAssertEqual(SceneLogic.gotoSpec(target: "5", targetScene: "Akt 2"), "Akt 2:5")
        XCTAssertEqual(SceneLogic.gotoSpec(target: "", targetScene: "Akt 2"), "Akt 2:")
        XCTAssertEqual(SceneLogic.gotoSpec(target: "Akt 1:5", targetScene: "Akt 2"), "Akt 1:5")

        let t = twoActs()
        let scenes = ["Akt 1", "Akt 2"]
        let scene = SceneLogic.resolveGoto(t.list, scenes: scenes, spec: "akt 2:", home: "Akt 1")
        XCTAssertEqual(scene?.scene, "Akt 2")
        XCTAssertNil(scene?.cue)
        XCTAssertNil(SceneLogic.resolveGoto(t.list, scenes: scenes, spec: "Akt 3:", home: "Akt 1"))
        let hit = SceneLogic.resolveGoto(t.list, scenes: scenes, spec: "Akt 2:2", home: "Akt 1")
        XCTAssertTrue(hit?.cue === t.b2)
        XCTAssertEqual(hit?.scene, "Akt 2")
        XCTAssertNil(SceneLogic.resolveGoto(t.list, scenes: scenes, spec: "", home: "Akt 1"))
    }

    func testShowsWithoutScenesResolveAsBefore() {
        let list = [c("", "1"), c("", "2"), c("", "3")]
        let group = Cue(type: .group); group.targets = "3, 1-2"
        XCTAssertEqual(numbers(CueTargets.resolve(list, group.targets, excluding: group).cues), ["3", "1", "2"])
        XCTAssertTrue(SceneLogic.find(list, "2", home: "") === list[1])
    }

    // MARK: Showfil

    func testScenesSurviveSavingAndLoading() throws {
        var doc = ShowDocument()
        let go = Cue(type: .goto); go.scene = "Akt 1"; go.number = "1"; go.target = "2"; go.targetScene = "Akt 2"
        doc.cues = [go, c("Akt 2", "2")]
        doc.scenes = ["Akt 1", "Akt 2"]
        let data = try doc.jsonData()
        XCTAssertTrue(String(decoding: data, as: UTF8.self).contains("\"Scenes\""))
        let back = try JSONDecoder().decode(ShowDocument.self, from: data)
        XCTAssertEqual(back.scenes, ["Akt 1", "Akt 2"])
        XCTAssertEqual(back.cues.map(\.scene), ["Akt 1", "Akt 2"])
        XCTAssertEqual(back.cues[0].targetScene, "Akt 2")
        XCTAssertEqual(SceneLogic.gotoSpec(target: back.cues[0].target, targetScene: back.cues[0].targetScene), "Akt 2:2")
    }

    func testOldShowsLoadAsOneScene() throws {
        let json = "{\"Cues\":[{\"type\":\"stop\",\"Number\":\"1\"},{\"type\":\"stop\",\"Number\":\"2\"}]}"
        let doc = try JSONDecoder().decode(ShowDocument.self, from: Data(json.utf8))
        XCTAssertTrue(doc.scenes.isEmpty)
        XCTAssertTrue(doc.cues.allSatisfy { $0.scene == "" })
    }

    func testBrokenSceneListsAreCleanedWhenLoaded() throws {
        let json = "{\"Scenes\":[\"A\",null,\"a\",\"\",\"B\"],\"Cues\":[{\"type\":\"stop\",\"Scene\":\"C\"},{\"type\":\"stop\"}]}"
        let doc = try JSONDecoder().decode(ShowDocument.self, from: Data(json.utf8))
        XCTAssertEqual(doc.scenes, ["A", "B", "C"])
        XCTAssertEqual(doc.cues.map(\.scene), ["C", "A"])
    }

    // MARK: Forhåndssjekk

    private func check(_ cues: [Cue], _ scenes: [String]) -> [PreflightFinding] {
        PreflightCheck.run(cues: cues, settings: AppSettings(), environment: NoEnv(), scenes: scenes)
    }

    func testPreflightAcceptsTheSameNumberInDifferentScenes() {
        let f = check([c("Akt 1", "1"), c("Akt 2", "1")], ["Akt 1", "Akt 2"])
        XCTAssertFalse(f.contains { $0.code == PreflightCheck.numberDuplicate })
        XCTAssertTrue(f.contains { $0.code == PreflightCheck.numbersOk })
    }

    func testPreflightReportsDuplicatesInsideOneScene() {
        let f = check([c("Akt 1", "1"), c("Akt 2", "1"), c("Akt 1", "1")], ["Akt 1", "Akt 2"])
        XCTAssertEqual(f.filter { $0.code == PreflightCheck.numberDuplicate }.count, 1)
    }

    func testPreflightChecksGotoScenes() {
        let target = c("Akt 2", "1")
        func jump(_ number: String, scene: String, to number2: String = "") -> Cue {
            let g = Cue(type: .goto); g.scene = "Akt 1"; g.number = number; g.targetScene = scene; g.target = number2; return g
        }
        let f = check([target, jump("5", scene: "Akt 2"), jump("6", scene: "Akt 2", to: "1"), jump("7", scene: "Finnes ikke")], ["Akt 1", "Akt 2"])
        XCTAssertEqual(f.filter { $0.severity == .error }.map(\.code), [PreflightCheck.sceneMissing])
    }

    func testPreflightFindsTargetsInTheHomeSceneAndElsewhere() {
        let fade = Cue(type: .fade); fade.scene = "Akt 2"; fade.number = "9"; fade.target = "1"
        var f = check([c("Akt 1", "1"), fade], ["Akt 1", "Akt 2"])
        XCTAssertFalse(f.contains { $0.code == PreflightCheck.targetMissing }) // finnes i en annen scene
        fade.target = "Akt 2:1"
        f = check([c("Akt 1", "1"), fade], ["Akt 1", "Akt 2"])
        XCTAssertTrue(f.contains { $0.code == PreflightCheck.targetMissing }) // finnes ikke i Akt 2
    }

    // MARK: Fjernstyring

    func testOscSwitchesScene() {
        let fake = FakeShow()
        let r = RemoteRouter(settings: AppSettings())
        r.show = fake
        r.handleOsc(OscMessage(address: "/scene/Akt 2", args: []), from: "test")
        r.handleOsc(OscMessage(address: "/showcue/scene", args: [.string("Akt 3")]), from: "test")
        XCTAssertEqual(fake.log, ["scene Akt 2", "scene Akt 3"])
    }
}
