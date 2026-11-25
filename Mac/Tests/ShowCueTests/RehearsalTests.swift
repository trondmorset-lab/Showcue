import XCTest
@testable import ShowCue

/// Øvelsesmodus: ren logikk for hvilken cue GO og auto-fortsett starter. Samme tilfeller som i Windows-versjonen.
final class RehearsalTests: XCTestCase {
    /// Lager en liste fra tegn: «.» vanlig, «s» merket, «d» deaktivert, «x» merket og deaktivert.
    private func list(_ pattern: String) -> [Cue] {
        pattern.enumerated().map { (i, ch) -> Cue in
            let c = Cue(type: .stop)
            c.number = String(i + 1)
            c.skipInRehearsal = ch == "s" || ch == "x"
            c.disabled = ch == "d" || ch == "x"
            return c
        }
    }

    func testNextPlayableIndexSkipsDisabledAndFlaggedOnlyInRehearsalMode() {
        let cases: [(String, Int, Bool, Int)] = [
            ("...", 0, false, 0), ("...", 1, false, 1), ("s..", 0, false, 0), ("s..", 0, true, 1), ("..s.", 2, true, 3),
            (".s..", 1, true, 2), ("ss.s", 0, true, 2), ("d.", 0, false, 1), ("ds.", 0, true, 2), ("xs.", 0, true, 2),
            ("..s", 2, true, 3), ("sss", 0, true, 3), ("sss", 0, false, 0), ("...", 3, true, 3), ("", 0, true, 0), ("...", -5, false, 0),
        ]
        for (pattern, from, mode, expected) in cases {
            XCTAssertEqual(Rehearsal.nextPlayableIndex(list(pattern), from: from, mode: mode), expected, "\(pattern) \(from) \(mode)")
        }
    }

    func testGoIndexStartsThePlayheadCueWhenItCanBePlayed() {
        XCTAssertEqual(Rehearsal.goIndex(list("..."), playhead: 0, mode: true), 0)
        XCTAssertEqual(Rehearsal.goIndex(list("s.."), playhead: 0, mode: true), 1)
        XCTAssertEqual(Rehearsal.goIndex(list("s.."), playhead: 0, mode: false), 0)
        XCTAssertEqual(Rehearsal.goIndex(list(".d."), playhead: 1, mode: false), 2)
    }

    func testGoIndexIsMinusOneWhenNothingCanStartOrThePlayheadIsOutside() {
        XCTAssertEqual(Rehearsal.goIndex(list("sss"), playhead: 0, mode: true), -1)
        XCTAssertEqual(Rehearsal.goIndex(list(".ss"), playhead: 1, mode: true), -1)
        XCTAssertEqual(Rehearsal.goIndex(list("dd"), playhead: 0, mode: false), -1)
        XCTAssertEqual(Rehearsal.goIndex(list("..."), playhead: 3, mode: true), -1)
        XCTAssertEqual(Rehearsal.goIndex(list("..."), playhead: -1, mode: true), -1)
        XCTAssertEqual(Rehearsal.goIndex(list(""), playhead: 0, mode: true), -1)
    }

    func testExplicitStartRunsAFlaggedCueButNeverADisabledOne() {
        XCTAssertEqual(Rehearsal.goIndex(list("s.."), playhead: 0, mode: true, explicitStart: true), 0)
        XCTAssertEqual(Rehearsal.goIndex(list("sss"), playhead: 0, mode: true, explicitStart: true), 0)
        XCTAssertEqual(Rehearsal.goIndex(list("d.s"), playhead: 0, mode: true, explicitStart: true), 1)
        XCTAssertEqual(Rehearsal.goIndex(list("x.s"), playhead: 0, mode: true, explicitStart: true), 1)
    }

    func testIsSkippedAndIsPlayableFollowTheModeAndTheFlags() {
        let flagged = Cue(type: .stop); flagged.skipInRehearsal = true
        let plain = Cue(type: .stop)
        let off = Cue(type: .stop); off.disabled = true
        XCTAssertFalse(Rehearsal.isSkipped(flagged, mode: false))
        XCTAssertTrue(Rehearsal.isSkipped(flagged, mode: true))
        XCTAssertFalse(Rehearsal.isSkipped(plain, mode: true))
        XCTAssertTrue(Rehearsal.isPlayable(flagged, mode: false))
        XCTAssertFalse(Rehearsal.isPlayable(flagged, mode: true))
        XCTAssertTrue(Rehearsal.isPlayable(plain, mode: true))
        XCTAssertFalse(Rehearsal.isPlayable(off, mode: false))
    }

    func testGotoAndGroupCuesAreSkippedLikeAnyOtherFlaggedCue() {
        let g = Cue(type: .goto); g.number = "1"; g.target = "5"; g.fire = true; g.skipInRehearsal = true
        let grp = Cue(type: .group); grp.number = "2"; grp.targets = "3-4"; grp.skipInRehearsal = true
        let a = Cue(type: .audio); a.number = "3"
        let cues = [g, grp, a]
        XCTAssertEqual(Rehearsal.nextPlayableIndex(cues, from: 0, mode: true), 2)
        XCTAssertEqual(Rehearsal.nextPlayableIndex(cues, from: 0, mode: false), 0)
        XCTAssertEqual(Rehearsal.goIndex(cues, playhead: 0, mode: true), 2)
    }

    func testCountFlaggedCountsOnlyActiveFlaggedCues() {
        XCTAssertEqual(Rehearsal.countFlagged(list("...")), 0)
        XCTAssertEqual(Rehearsal.countFlagged(list("s.s.")), 2)
        XCTAssertEqual(Rehearsal.countFlagged(list("sx.d")), 1)
        XCTAssertEqual(Rehearsal.countFlagged(list("")), 0)
    }

    /// Hvert tegn er en cue: «.» vanlig, «s» merket, «d» deaktivert, og «c»/«f» som «.» med auto-fortsett/auto-følg.
    private func chain(_ pattern: String) -> [Cue] {
        pattern.map { (ch) -> Cue in
            let c = Cue(type: .stop)
            c.skipInRehearsal = ch == "s"
            c.disabled = ch == "d"
            c.continueMode = ch == "c" ? .autoContinue : ch == "f" ? .autoFollow : .none
            return c
        }
    }

    func testNextPlayheadIndexSkipsChainsDisabledAndFlaggedCues() {
        let cases: [(String, Int, Bool, Int)] = [
            ("...", 0, false, 1), (".s.", 0, false, 1), (".s.", 0, true, 2), ("..s", 1, true, 3), ("s..", 0, true, 1),
            ("cs..", 0, true, 3), ("cs..", 0, false, 2), ("csf..", 0, true, 4), ("cdc..", 0, true, 4), ("css", 0, true, 3), ("sss", 0, true, 3),
        ]
        for (pattern, fired, mode, expected) in cases {
            XCTAssertEqual(CueEngine.nextPlayheadIndex(chain(pattern), fired: fired, rehearsal: mode), expected, "\(pattern) \(fired) \(mode)")
        }
    }

    func testNextPlayheadIndexWithoutTheModeBehavesAsBefore() {
        let l = chain(".s.")
        XCTAssertEqual(CueEngine.nextPlayheadIndex(l, fired: 0), CueEngine.nextPlayheadIndex(l, fired: 0, rehearsal: false))
    }

    func testFieldDefaultsToOffAndIsSavedWithTheSameKey() throws {
        XCTAssertFalse(Cue(type: .audio).skipInRehearsal)
        let a = Cue(type: .audio); a.filePath = "a.wav"; a.skipInRehearsal = true
        let b = Cue(type: .fade)
        var doc = ShowDocument()
        doc.cues = [a, b]
        let data = try doc.jsonData()
        let json = String(decoding: data, as: UTF8.self)
        XCTAssertTrue(json.contains("\"SkipInRehearsal\" : true"), json)
        let back = try JSONDecoder().decode(ShowDocument.self, from: data)
        XCTAssertTrue(back.cues[0].skipInRehearsal)
        XCTAssertFalse(back.cues[1].skipInRehearsal)
        XCTAssertEqual(try back.jsonData(), data)
    }

    func testOldShowFilesWithoutTheFieldStillLoadWithTheFieldOff() throws {
        let a = Cue(type: .audio); a.filePath = "a.wav"; a.skipInRehearsal = true
        var doc = ShowDocument()
        doc.cues = [a]
        var json = String(decoding: try doc.jsonData(), as: UTF8.self)
        json = json.replacingOccurrences(of: "\"SkipInRehearsal\" : true,", with: "").replacingOccurrences(of: "\"SkipInRehearsal\" : true", with: "\"Unknown\" : 1")
        XCTAssertFalse(json.contains("SkipInRehearsal"))
        let back = try JSONDecoder().decode(ShowDocument.self, from: Data(json.utf8))
        XCTAssertFalse(back.cues[0].skipInRehearsal)
    }

    private final class NoEnv: PreflightEnvironment {
        func fileStatus(_ path: String) -> PreflightFileStatus { .ok }
        func audioOutputExists(uid: String) -> Bool { true }
        func inputDeviceExists(uid: String) -> Bool { true }
        func screenExists(_ index: Int) -> Bool { true }
    }

    private func memo(_ number: String, flagged: Bool = false, disabled: Bool = false) -> Cue {
        let c = Cue(type: .memo); c.number = number; c.text = "x"; c.skipInRehearsal = flagged; c.disabled = disabled
        return c
    }

    func testPreflightWarnsWhenTheModeIsOnAndCountsFlaggedCues() {
        let cues = [memo("1", flagged: true), memo("2"), memo("3", flagged: true), memo("4", flagged: true, disabled: true)]
        let on = PreflightCheck.run(cues: cues, settings: AppSettings(), environment: NoEnv(), rehearsalMode: true)
        let w = on.filter { $0.code == PreflightCheck.rehearsalOn }
        XCTAssertEqual(w.count, 1)
        XCTAssertEqual(w.first?.severity, .warning)
        XCTAssertTrue(w.first?.message.contains("2") ?? false)
        let note = on.filter { $0.code == PreflightCheck.rehearsalFlagged }
        XCTAssertEqual(note.count, 1)
        XCTAssertEqual(note.first?.severity, .ok)
        XCTAssertTrue(note.first?.message.contains("2") ?? false)

        let off = PreflightCheck.run(cues: cues, settings: AppSettings(), environment: NoEnv())
        XCTAssertFalse(off.contains { $0.code == PreflightCheck.rehearsalOn })
        XCTAssertEqual(off.filter { $0.code == PreflightCheck.rehearsalFlagged }.count, 1)
    }

    func testPreflightSaysNothingAboutRehearsalWhenOffAndNothingIsFlagged() {
        let cues = [memo("1")]
        let r = PreflightCheck.run(cues: cues, settings: AppSettings(), environment: NoEnv())
        XCTAssertFalse(r.contains { $0.code == PreflightCheck.rehearsalOn || $0.code == PreflightCheck.rehearsalFlagged })
        let on = PreflightCheck.run(cues: cues, settings: AppSettings(), environment: NoEnv(), rehearsalMode: true)
        XCTAssertEqual(on.filter { $0.code == PreflightCheck.rehearsalOn }.count, 1)
        XCTAssertFalse(on.contains { $0.code == PreflightCheck.rehearsalFlagged })
    }
}
