import XCTest
@testable import ShowCue

/// Forhåndslasting: hvilke cuer som forberedes for en markør, og regnskapet med grense og rekkefølge for frigjøring.
/// Samme tester som i Windows-versjonen. Selve filåpningen i bakgrunnen krever ekte filer og utstyr og testes ikke her.
final class PreloadTests: XCTestCase {
    private func cue(_ type: CueType, _ path: String = "", number: String = "", mode: ContinueMode = .none, disabled: Bool = false) -> Cue {
        let c = Cue(type: type)
        c.filePath = path; c.number = number; c.continueMode = mode; c.disabled = disabled
        return c
    }
    private func a(_ path: String, number: String = "", mode: ContinueMode = .none, disabled: Bool = false) -> Cue {
        cue(.audio, path, number: number, mode: mode, disabled: disabled)
    }
    private func paths(_ items: [PreloadItem]) -> [String] { items.map(\.path) }

    func testPlanUsesTheCueUnderThePlayhead() {
        let cues = [a("a.wav"), a("b.wav"), a("c.wav")]
        XCTAssertEqual(paths(PreloadPlanner.plan(cues: cues, playheadIndex: 1)), ["b.wav"])
    }

    func testPlanSkipsDisabledCuesLikeGoDoes() {
        let cues = [a("a.wav", disabled: true), a("b.wav", disabled: true), a("c.wav")]
        XCTAssertEqual(paths(PreloadPlanner.plan(cues: cues, playheadIndex: 0)), ["c.wav"])
    }

    func testPlanIsEmptyForNothingToPrepare() {
        XCTAssertTrue(PreloadPlanner.plan(cues: [], playheadIndex: 0).isEmpty)
        let cues = [a("a.wav", disabled: true)]
        XCTAssertTrue(PreloadPlanner.plan(cues: cues, playheadIndex: 0).isEmpty)
        XCTAssertTrue(PreloadPlanner.plan(cues: cues, playheadIndex: -1).isEmpty)
        XCTAssertTrue(PreloadPlanner.plan(cues: cues, playheadIndex: 5).isEmpty)
        XCTAssertTrue(PreloadPlanner.plan(cues: [a("a.wav")], playheadIndex: 0, max: 0).isEmpty)
    }

    func testPlanFollowsAutomaticContinuationsInOrderAndStopsAtThePlainCue() {
        let cues = [a("a.wav", mode: .autoFollow), cue(.video, "b.mp4", mode: .autoContinue), cue(.image, "c.png"), a("d.wav")]
        let plan = PreloadPlanner.plan(cues: cues, playheadIndex: 0)
        XCTAssertEqual(paths(plan), ["a.wav", "b.mp4", "c.png"])
        XCTAssertEqual(plan.map(\.kind), [.audio, .video, .image])
    }

    func testPlanNeverReturnsMoreThanTheLimit() {
        let cues = (0..<8).map { a("\($0).wav", mode: .autoFollow) }
        XCTAssertEqual(PreloadPlanner.plan(cues: cues, playheadIndex: 0).count, PreloadPlanner.maxItems)
        XCTAssertEqual(paths(PreloadPlanner.plan(cues: cues, playheadIndex: 0, max: 2)), ["0.wav", "1.wav"])
        XCTAssertEqual(PreloadPlanner.plan(cues: cues, playheadIndex: 0, max: 99).count, PreloadPlanner.maxItems)
    }

    func testPlanSkipsDisabledCuesInTheChain() {
        let cues = [a("a.wav", mode: .autoFollow), a("b.wav", disabled: true), a("c.wav")]
        XCTAssertEqual(paths(PreloadPlanner.plan(cues: cues, playheadIndex: 0)), ["a.wav", "c.wav"])
    }

    func testPlanIgnoresCuesWithoutPreparableFilesButKeepsFollowing() {
        let cues = [cue(.memo, mode: .autoContinue), cue(.pdf, "x.pdf", mode: .autoContinue), a("", mode: .autoContinue), cue(.video, "v.mp4")]
        XCTAssertEqual(paths(PreloadPlanner.plan(cues: cues, playheadIndex: 0)), ["v.mp4"])
    }

    func testPlanDoesNotRepeatTheSameFile() {
        let cues = [a("a.wav", mode: .autoContinue), a("a.wav", mode: .autoContinue), cue(.video, "a.wav")]
        XCTAssertEqual(PreloadPlanner.plan(cues: cues, playheadIndex: 0).map(\.kind), [.audio, .video])
    }

    func testPlanFollowsGotoToItsTarget() {
        let g = Cue(type: .goto); g.target = "7"
        let cues = [g, a("a.wav"), a("t.wav", number: "7")]
        XCTAssertEqual(paths(PreloadPlanner.plan(cues: cues, playheadIndex: 0)), ["t.wav"])
    }

    func testPlanHandlesGotoWithMissingTargetAndLoops() {
        let missing = Cue(type: .goto); missing.target = "9"
        XCTAssertTrue(PreloadPlanner.plan(cues: [missing, a("a.wav")], playheadIndex: 0).isEmpty)
        let empty = Cue(type: .goto)
        XCTAssertTrue(PreloadPlanner.plan(cues: [empty, a("a.wav")], playheadIndex: 0).isEmpty)
        let g1 = Cue(type: .goto); g1.number = "1"; g1.target = "2"
        let g2 = Cue(type: .goto); g2.number = "2"; g2.target = "1"
        XCTAssertTrue(PreloadPlanner.plan(cues: [g1, g2], playheadIndex: 0).isEmpty)
        let me = Cue(type: .goto); me.number = "1"; me.target = "1"
        XCTAssertTrue(PreloadPlanner.plan(cues: [me], playheadIndex: 0).isEmpty)
    }

    func testPlanPreparesTheEnabledTargetsOfAGroup() {
        let g = Cue(type: .group); g.number = "9"; g.targets = "2, 3, 4"
        let cues = [g, a("x.wav", number: "1"), a("b.wav", number: "2"), a("off.wav", number: "3", disabled: true), cue(.video, "d.mp4", number: "4")]
        XCTAssertEqual(paths(PreloadPlanner.plan(cues: cues, playheadIndex: 0)), ["b.wav", "d.mp4"])
    }

    func testPlanReturnsTheStartTimeAsAHint() {
        let c = a("a.wav"); c.startTime = 12.5
        XCTAssertEqual(PreloadPlanner.plan(cues: [c], playheadIndex: 0)[0].startTime, 12.5)
    }

    func testBookStartsNewKeysUpToTheLimit() {
        var book = PreloadBook(capacity: 3)
        let diff = book.sync(["a", "b", "c", "d", "e"])
        XCTAssertEqual(diff.toStart, ["a", "b", "c"])
        XCTAssertTrue(diff.toEvict.isEmpty)
        XCTAssertEqual(book.keys, ["a", "b", "c"])
    }

    func testBookEvictsWhatIsNoLongerWantedOldestFirst() {
        var book = PreloadBook(capacity: 3)
        book.sync(["a", "b", "c"])
        let diff = book.sync(["c", "d"])
        XCTAssertEqual(diff.toStart, ["d"])
        XCTAssertEqual(diff.toEvict, ["a", "b"])
        XCTAssertEqual(book.keys, ["c", "d"])
    }

    func testBookKeepsWhatIsAlreadyHeldAndIgnoresDuplicates() {
        var book = PreloadBook(capacity: 3)
        book.sync(["a", "b"])
        let diff = book.sync(["b", "b", "a", ""])
        XCTAssertTrue(diff.toStart.isEmpty)
        XCTAssertTrue(diff.toEvict.isEmpty)
        XCTAssertEqual(book.keys, ["b", "a"])
    }

    func testBookNeverHoldsMoreThanTheLimitEvenAfterManySyncs() {
        var book = PreloadBook(capacity: 3)
        var g = SystemRandomNumberGenerator()
        for _ in 0..<500 {
            let n = Int.random(in: 0..<9, using: &g)
            book.sync((0..<n).map { _ in "k\(Int.random(in: 0..<6, using: &g))" })
            XCTAssertLessThanOrEqual(book.keys.count, 3)
            XCTAssertEqual(book.keys.count, Set(book.keys).count)
        }
    }

    func testBookRemoveAndClearRelease() {
        var book = PreloadBook(capacity: 3)
        book.sync(["a", "b", "c"])
        XCTAssertTrue(book.remove("b"))
        XCTAssertFalse(book.remove("b"))
        XCTAssertEqual(book.clear(), ["a", "c"])
        XCTAssertTrue(book.keys.isEmpty)
        XCTAssertEqual(book.sync(["x"]).toStart, ["x"])
    }

    func testSettingIsOffByDefaultAndSurvivesLoading() throws {
        XCTAssertFalse(AppSettings().preloadNextCue)
        XCTAssertFalse(try JSONDecoder().decode(AppSettings.self, from: Data("{}".utf8)).preloadNextCue)
        var s = AppSettings(); s.preloadNextCue = true
        let back = try JSONDecoder().decode(AppSettings.self, from: JSONEncoder().encode(s))
        XCTAssertTrue(back.preloadNextCue)
    }
}
