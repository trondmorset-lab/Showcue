import XCTest
@testable import ShowCue

/// Rettelser etter stresstesten (0.27.2). Samme tilfeller som i Windows-versjonen.
final class StressFixTests: XCTestCase {
    private let oslo: Calendar = {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = TimeZone(identifier: "Europe/Oslo")!
        return c
    }()

    private func t(_ day: Int, _ h: Int, _ m: Int, _ s: Int, _ ms: Int = 0) -> Date {
        var c = DateComponents()
        c.year = 2026; c.month = 11; c.day = day; c.hour = h; c.minute = m; c.second = s; c.nanosecond = ms * 1_000_000
        return oslo.date(from: c)!
    }

    func testChainDrainRunsLongChainsWithoutNesting() {
        let chain = ChainDrain<Int>()
        var depth = 0, maxDepth = 0, ran = 0
        func run(_ n: Int) {
            depth += 1; maxDepth = max(maxDepth, depth); ran += 1
            if n < 200_000 { chain.push(n + 1, run: run) }
            depth -= 1
        }
        chain.push(0, run: run)
        XCTAssertEqual(ran, 200_001)
        XCTAssertEqual(maxDepth, 1)
    }

    func testStalledClockCheckIsReportedButNotFired() {
        let prev = t(24, 18, 59, 50), now = t(24, 19, 0, 5)
        XCTAssertFalse(ClockTriggers.crossed(previous: prev, now: now, trigger: "19:00:00", calendar: oslo))
        XCTAssertTrue(ClockTriggers.missed(previous: prev, now: now, realElapsed: 15, trigger: "19:00:00", calendar: oslo))
        XCTAssertFalse(ClockTriggers.missed(previous: prev, now: now, realElapsed: 15, trigger: "20:00:00", calendar: oslo))
        // Normalt intervall er ikke en tapt trigger.
        XCTAssertFalse(ClockTriggers.missed(previous: t(24, 18, 59, 59, 500), now: t(24, 19, 0, 0, 100), realElapsed: 0.6, trigger: "19:00:00", calendar: oslo))
        // Dvale og klokkestilling er ikke en hang.
        XCTAssertFalse(ClockTriggers.missed(previous: t(24, 18, 0, 0), now: t(24, 20, 0, 0), realElapsed: 7200, trigger: "19:00:00", calendar: oslo))
        XCTAssertFalse(ClockTriggers.missed(previous: prev, now: now, realElapsed: 3, trigger: "19:00:00", calendar: oslo))
    }

    func testMissedCuesSkipsDisabledAndEmptyTriggers() {
        func cue(_ n: String, _ trigger: String, disabled: Bool = false) -> Cue {
            let c = Cue(type: .audio); c.number = n; c.clockTrigger = trigger; c.disabled = disabled; return c
        }
        let cues = [cue("1", "19:00:00"), cue("2", "19:00:00", disabled: true), cue("3", "")]
        let missed = ClockTriggers.missedCues(cues, previous: t(24, 18, 59, 50), now: t(24, 19, 0, 5), realElapsed: 15, calendar: oslo)
        XCTAssertEqual(missed.map { $0.number }, ["1"])
    }

    func testShowWithWrongSceneTypeStillOpens() throws {
        for json in [#"{"Scenes": 5}"#, #"{"Scenes": "A"}"#, #"{"Scenes": {"x": 1}}"#, #"{"Scenes": null}"#] {
            let doc = try JSONDecoder().decode(ShowDocument.self, from: Data(json.utf8))
            XCTAssertTrue(doc.scenes.isEmpty, json)
        }
    }
}
