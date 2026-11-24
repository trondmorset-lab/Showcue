import XCTest
@testable import ShowCue

/// Klokkeslett-trigger: ren logikk uten klokke. Samme tilfeller som i Windows-versjonen.
final class ClockTriggerTests: XCTestCase {
    private let oslo: Calendar = {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = TimeZone(identifier: "Europe/Oslo")!
        return c
    }()

    private func t(_ day: Int, _ h: Int, _ m: Int, _ s: Int, _ ms: Int = 0, month: Int = 11, year: Int = 2026) -> Date {
        var c = DateComponents()
        c.year = year; c.month = month; c.day = day; c.hour = h; c.minute = m; c.second = s; c.nanosecond = ms * 1_000_000
        return oslo.date(from: c)!
    }

    private func crossed(_ a: Date, _ b: Date, _ trigger: String?) -> Bool {
        ClockTriggers.crossed(previous: a, now: b, trigger: trigger, calendar: oslo)
    }

    func testNormalizeKeepsOnlyValidTimes() {
        let cases: [(String, String)] = [
            ("19:00:00", "19:00:00"), ("7:5:9", "07:05:09"), (" 23:59:59 ", "23:59:59"), ("24:00:00", ""), ("12:60:00", ""),
            ("12:00:60", ""), ("12:00", ""), ("12:00:00:00", ""), ("tull", ""), ("-1:00:00", ""), ("", ""),
        ]
        for (input, expected) in cases {
            XCTAssertEqual(ClockTriggers.normalize(input), expected, input)
            XCTAssertEqual(ClockTriggers.parse(input) != nil, !expected.isEmpty, input)
        }
        XCTAssertNil(ClockTriggers.parse(nil))
        XCTAssertEqual(ClockTriggers.normalize(nil), "")
    }

    func testCrossedWhenTheTimePassesInsideTheInterval() {
        XCTAssertTrue(crossed(t(24, 18, 59, 59, 900), t(24, 19, 0, 0, 100), "19:00:00"))
        XCTAssertFalse(crossed(t(24, 18, 59, 58), t(24, 18, 59, 59, 500), "19:00:00"))
        XCTAssertFalse(crossed(t(24, 19, 0, 0, 100), t(24, 19, 0, 0, 200), "19:00:00"))
    }

    func testExactlyAtTheTimeCountsOnlyInTheIntervalThatEndsThere() {
        XCTAssertTrue(crossed(t(24, 18, 59, 59, 500), t(24, 19, 0, 0), "19:00:00"))
        XCTAssertFalse(crossed(t(24, 19, 0, 0), t(24, 19, 0, 0, 500), "19:00:00"))
    }

    func testWorksAcrossMidnight() {
        XCTAssertTrue(crossed(t(24, 23, 59, 59, 500), t(25, 0, 0, 0, 500), "00:00:00"))
        XCTAssertFalse(crossed(t(24, 23, 59, 59, 500), t(25, 0, 0, 0, 500), "23:59:59"))
        XCTAssertTrue(crossed(t(24, 23, 59, 58, 500), t(24, 23, 59, 59, 500), "23:59:59"))
        XCTAssertFalse(crossed(t(24, 23, 59, 59, 500), t(25, 0, 0, 0, 500), "00:00:01"))
        XCTAssertTrue(crossed(t(25, 0, 0, 0, 500), t(25, 0, 0, 1, 500), "00:00:01"))
    }

    func testBackwardsJumpAndStandingStillNeverFire() {
        XCTAssertFalse(crossed(t(24, 19, 0, 1), t(24, 18, 59, 59), "19:00:00"))
        XCTAssertFalse(crossed(t(24, 19, 0, 1), t(24, 19, 0, 1), "19:00:01"))
        XCTAssertFalse(crossed(t(25, 0, 0, 1), t(24, 23, 59, 59), "00:00:00"))
    }

    func testLargeForwardJumpsAreIgnored() {
        XCTAssertTrue(crossed(t(24, 18, 59, 58, 500), t(24, 19, 0, 0, 500), "19:00:00"))
        XCTAssertFalse(crossed(t(24, 18, 59, 58, 400), t(24, 19, 0, 0, 500), "19:00:00"))
        XCTAssertFalse(crossed(t(24, 18, 0, 0), t(24, 20, 0, 0), "19:00:00"))
        XCTAssertFalse(crossed(t(24, 23, 0, 0), t(25, 7, 0, 0), "00:00:00"))
    }

    func testDaylightSavingJumpsAreIgnored() {
        // Vårens omstilling: veggklokka hopper fra 01:59:59 til 03:00:00 på ett sekund faktisk tid.
        let springBefore = t(28, 1, 59, 59, month: 3, year: 2027)
        let springAfter = springBefore.addingTimeInterval(1)
        XCTAssertFalse(crossed(springBefore, springAfter, "02:30:00"))
        XCTAssertFalse(crossed(springBefore, springAfter, "03:00:00"))
        // Høstens omstilling: 02:59:59 (sommertid) blir 02:00:00 (normaltid) ett sekund senere.
        let autumnBefore = Date(timeIntervalSince1970: 1_792_889_999) // 2026-10-25 00:59:59 UTC = 02:59:59 CEST
        XCTAssertFalse(crossed(autumnBefore, autumnBefore.addingTimeInterval(1), "02:30:00"))
        XCTAssertFalse(crossed(autumnBefore, autumnBefore.addingTimeInterval(1), "02:00:00"))
    }

    func testInvalidOrEmptyTriggerNeverFires() {
        XCTAssertFalse(crossed(t(24, 18, 59, 59, 900), t(24, 19, 0, 0, 100), ""))
        XCTAssertFalse(crossed(t(24, 18, 59, 59, 900), t(24, 19, 0, 0, 100), "tull"))
        XCTAssertFalse(crossed(t(24, 18, 59, 59, 900), t(24, 19, 0, 0, 100), nil))
    }

    private func cue(_ number: String, trigger: String = "", disabled: Bool = false) -> Cue {
        let c = Cue(type: .audio)
        c.number = number; c.clockTrigger = trigger; c.disabled = disabled
        return c
    }

    func testDueSkipsDisabledAndEmptyAndFiresEachCueOncePerPassing() {
        let cues = [cue("1", trigger: "19:00:00"), cue("2", trigger: "19:00:00", disabled: true), cue("3"), cue("4", trigger: "19:00:01")]
        var fired: [String] = []
        var prev = t(24, 18, 59, 58, 500)
        for _ in 0..<40 { // ticks à 100 ms: 18:59:58,5 → 19:00:02,5
            let next = prev.addingTimeInterval(0.1)
            fired += ClockTriggers.due(cues, previous: prev, now: next, calendar: oslo).map { $0.number }
            prev = next
        }
        XCTAssertEqual(fired, ["1", "4"])
    }

    func testSeveralCuesWithTheSameTimeAllFire() {
        let cues = [cue("1", trigger: "19:00:00"), cue("2", trigger: "19:00:00")]
        XCTAssertEqual(ClockTriggers.due(cues, previous: t(24, 18, 59, 59, 500), now: t(24, 19, 0, 0, 500), calendar: oslo).count, 2)
    }

    func testCueFieldIsNormalisedSavedWithTheSameKeyAndShownInTheTriggerText() throws {
        XCTAssertEqual(cue("1", trigger: "tull").clockTrigger, "")
        let a = cue("1", trigger: "9:5:0"); a.hotkey = "F1"
        var doc = ShowDocument()
        doc.cues = [a]
        let data = try doc.jsonData()
        let json = String(decoding: data, as: UTF8.self)
        XCTAssertTrue(json.contains("\"ClockTrigger\" : \"09:05:00\""), json)
        let back = try JSONDecoder().decode(ShowDocument.self, from: data)
        XCTAssertEqual(back.cues[0].clockTrigger, "09:05:00")
        XCTAssertTrue(back.cues[0].triggerText.contains("F1"))
        XCTAssertTrue(back.cues[0].triggerText.contains("09:05:00"))
        XCTAssertEqual(try back.jsonData(), data)

        let bad = try JSONDecoder().decode(ShowDocument.self, from: Data(json.replacingOccurrences(of: "09:05:00", with: "tull").utf8))
        XCTAssertEqual(bad.cues[0].clockTrigger, "")
    }

    func testSettingDefaultsToOffAndIsSaved() throws {
        XCTAssertFalse(AppSettings().clockTriggersEnabled)
        var s = AppSettings(); s.clockTriggersEnabled = true
        let back = try JSONDecoder().decode(AppSettings.self, from: JSONEncoder().encode(s))
        XCTAssertTrue(back.clockTriggersEnabled)
        XCTAssertFalse(try JSONDecoder().decode(AppSettings.self, from: Data("{}".utf8)).clockTriggersEnabled)
    }

    private final class NoEnv: PreflightEnvironment {
        func fileStatus(_ path: String) -> PreflightFileStatus { .ok }
        func audioOutputExists(uid: String) -> Bool { true }
        func inputDeviceExists(uid: String) -> Bool { true }
        func screenExists(_ index: Int) -> Bool { true }
    }

    func testPreflightWarnsWhenCuesHaveClockTriggersButTheSettingIsOff() {
        let a = Cue(type: .memo); a.number = "1"; a.text = "x"; a.clockTrigger = "19:00:00"
        let b = Cue(type: .memo); b.number = "2"; b.text = "y"
        let off = PreflightCheck.run(cues: [a, b], settings: AppSettings(), environment: NoEnv())
        let hits = off.filter { $0.code == PreflightCheck.clockTriggerOff }
        XCTAssertEqual(hits.count, 1)
        XCTAssertEqual(hits.first?.severity, .warning)
        XCTAssertEqual(hits.first?.cueIndex, 0)
        XCTAssertFalse(off.contains { $0.code == PreflightCheck.valuesOk })

        var on = AppSettings(); on.clockTriggersEnabled = true
        XCTAssertFalse(PreflightCheck.run(cues: [a, b], settings: on, environment: NoEnv()).contains { $0.code == PreflightCheck.clockTriggerOff })

        a.disabled = true
        XCTAssertFalse(PreflightCheck.run(cues: [a, b], settings: AppSettings(), environment: NoEnv()).contains { $0.code == PreflightCheck.clockTriggerOff })
    }
}
