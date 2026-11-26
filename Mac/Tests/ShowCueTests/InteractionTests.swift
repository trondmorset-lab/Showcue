import XCTest
@testable import ShowCue

/// Regresjonstester for feil som oppstod der de sammenslåtte funksjonene møtes (scener, øvelsesmodus, forhåndslasting,
/// «gå til», live-inngang). Samme tilfeller som i Windows-versjonen (InteractionTests.cs).
final class InteractionTests: XCTestCase {
    private func a(_ scene: String, _ number: String, _ path: String, mode: ContinueMode = .none, skip: Bool = false, disabled: Bool = false) -> Cue {
        let c = Cue(type: .audio)
        c.scene = scene; c.number = number; c.filePath = path; c.continueMode = mode; c.skipInRehearsal = skip; c.disabled = disabled
        return c
    }
    private func paths(_ items: [PreloadItem]) -> [String] { items.map(\.path) }

    // MARK: «Gå til»

    func testSceneOnlyGotoTargetIsResolvedByResolveGotoNotByNumberLookup() {
        // Feilen: hovedmodellen slo først opp målet «Akt 2:» som et cuenummer, fant ingenting og ga «fant ikke cue».
        let list = [a("Akt 1", "1", "a.wav"), a("Akt 2", "1", "b.wav")]
        let spec = SceneLogic.gotoSpec(target: "", targetScene: "Akt 2")
        XCTAssertEqual(spec, "Akt 2:")
        XCTAssertNil(SceneLogic.find(list, spec, home: "Akt 1"))
        let t = SceneLogic.resolveGoto(list, scenes: ["Akt 1", "Akt 2"], spec: spec, home: "Akt 1")
        XCTAssertEqual(t?.scene, "Akt 2")
        XCTAssertNil(t?.cue)
    }

    func testGotoLoopGuardStopsARunawayChainButNotSlowLoops() {
        var g = GotoLoopGuard()
        var t: Int64 = 1000
        var allowed = 0
        for _ in 0..<1000 { if g.allow(nowMs: t) { allowed += 1 }; t += 1 } // 1 ms mellom hver: løpsk
        XCTAssertEqual(allowed, GotoLoopGuard.maxBurst)
        t += GotoLoopGuard.windowMs + 1
        XCTAssertTrue(g.allow(nowMs: t)) // en pause lenger enn vinduet slipper igjennom igjen

        var slow = GotoLoopGuard()
        for i in 0..<500 { XCTAssertTrue(slow.allow(nowMs: Int64(i) * 1000)) } // én gang i sekundet: aldri stoppet
    }

    func testGotoLoopGuardSurvivesClockGoingBackwardsAndReset() {
        var g = GotoLoopGuard()
        for _ in 0..<100 { _ = g.allow(nowMs: 5000) }
        XCTAssertFalse(g.allow(nowMs: 5000))
        XCTAssertTrue(g.allow(nowMs: 10)) // tiden gikk bakover: tell på nytt
        g.reset()
        for _ in 0..<GotoLoopGuard.maxBurst { XCTAssertTrue(g.allow(nowMs: 7)) }
        XCTAssertFalse(g.allow(nowMs: 7))
        var lo = GotoLoopGuard(); XCTAssertTrue(lo.allow(nowMs: Int64.min))
        var hi = GotoLoopGuard(); XCTAssertTrue(hi.allow(nowMs: Int64.max))
    }

    // MARK: Forhåndslasting x scener x øvelsesmodus

    func testPreloadChainNeverLeavesTheScene() {
        // Feilen: planen fulgte «fortsett automatisk» over i neste scene, som GO og motoren aldri gjør.
        let list = [a("Akt 1", "1", "a1.wav", mode: .autoContinue), a("Akt 2", "1", "b1.wav"), a("Akt 1", "2", "a2.wav")]
        XCTAssertEqual(paths(PreloadPlanner.plan(cues: list, playheadIndex: 0)), ["a1.wav", "a2.wav"])
        let tail = [a("Akt 1", "1", "a1.wav", mode: .autoContinue), a("Akt 2", "1", "b1.wav")]
        XCTAssertEqual(paths(PreloadPlanner.plan(cues: tail, playheadIndex: 0)), ["a1.wav"])
    }

    func testPreloadGotoFollowsTheTargetInTheRightScene() {
        let go = Cue(type: .goto)
        go.scene = "Akt 1"; go.number = "9"; go.target = "1"; go.targetScene = "Akt 2"
        let list = [a("Akt 1", "1", "a1.wav"), go, a("Akt 2", "1", "b1.wav")]
        XCTAssertEqual(paths(PreloadPlanner.plan(cues: list, playheadIndex: 1)), ["b1.wav"])
        go.targetScene = "" // uten scene-angivelse søkes hjemmescenen først
        XCTAssertEqual(paths(PreloadPlanner.plan(cues: list, playheadIndex: 1)), ["a1.wav"])
        go.target = ""; go.targetScene = "Akt 2" // bare scenebytte: ingen fil å forberede
        XCTAssertTrue(PreloadPlanner.plan(cues: list, playheadIndex: 1).isEmpty)
    }

    func testPreloadSkipsFlaggedCuesInRehearsalMode() {
        let list = [a("", "1", "a.wav", skip: true), a("", "2", "b.wav", mode: .autoContinue, skip: true), a("", "3", "c.wav"), a("", "4", "d.wav")]
        XCTAssertEqual(paths(PreloadPlanner.plan(cues: list, playheadIndex: 0)), ["a.wav"])
        XCTAssertEqual(paths(PreloadPlanner.plan(cues: list, playheadIndex: 0, rehearsal: true)), ["c.wav"])
        let chain = [a("", "1", "a.wav", mode: .autoContinue), a("", "2", "b.wav", skip: true), a("", "3", "c.wav")]
        XCTAssertEqual(paths(PreloadPlanner.plan(cues: chain, playheadIndex: 0)), ["a.wav", "b.wav"])
        XCTAssertEqual(paths(PreloadPlanner.plan(cues: chain, playheadIndex: 0, rehearsal: true)), ["a.wav", "c.wav"])
    }

    // MARK: GO, auto-fortsett og scener x øvelsesmodus

    func testGoAndAutoContinueAgreeAcrossScenesAndRehearsal() {
        let list = [a("A", "1", "", mode: .autoContinue), a("B", "1", ""), a("A", "2", "", skip: true), a("A", "3", ""), a("B", "2", "")]
        let first = list[0]
        // Uten øvelsesmodus: A1 starter A2 selv, og GO-markøren står på A3.
        XCTAssertEqual(SceneLogic.nextEnabled(list, after: first, rehearsal: false)?.number, "2")
        XCTAssertEqual(SceneLogic.nextPlayhead(list, after: first, rehearsal: false)?.number, "3")
        // Med øvelsesmodus hoppes A2 over: A1 starter A3, og scenen er til ende. Scene B røres aldri.
        XCTAssertEqual(SceneLogic.nextEnabled(list, after: first, rehearsal: true)?.number, "3")
        XCTAssertEqual(SceneLogic.nextEnabled(list, after: first, rehearsal: true)?.scene, "A")
        XCTAssertNil(SceneLogic.nextPlayhead(list, after: first, rehearsal: true))
    }

    // MARK: Live-inngang

    func testLiveRingNeverPassesOnNonFiniteSamples() {
        let ring = LiveRing(channels: 1, sampleRate: 48000, maxLatency: 1)
        ring.write([0.5, Float.nan, Float.infinity, -Float.infinity, -0.25])
        var dst = [Float](repeating: 9, count: 5)
        dst.withUnsafeMutableBufferPointer { ring.read($0.baseAddress!, frames: 5) }
        XCTAssertEqual(dst, [0.5, 0, 0, 0, -0.25])
    }
}
