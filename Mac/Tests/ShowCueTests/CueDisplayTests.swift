import XCTest
@testable import ShowCue

/// Visningslogikk for cuelista og inspektøren: radtilstand, «gjenstår» og faner. Samme tester som i Windows-versjonen.
final class CueDisplayTests: XCTestCase {
    func testRowStatePrioritisesPlayingThenDisabledThenNext() {
        XCTAssertEqual(CueDisplay.rowState(isRunning: true, isNext: true, disabled: true), .playing)
        XCTAssertEqual(CueDisplay.rowState(isRunning: true, isNext: false, disabled: false), .playing)
        XCTAssertEqual(CueDisplay.rowState(isRunning: false, isNext: true, disabled: true), .disabled)
        XCTAssertEqual(CueDisplay.rowState(isRunning: false, isNext: false, disabled: true), .disabled)
        XCTAssertEqual(CueDisplay.rowState(isRunning: false, isNext: true, disabled: false), .next)
        XCTAssertEqual(CueDisplay.rowState(isRunning: false, isNext: false, disabled: false), .normal)
    }

    func testClockRoundsUpToWholeSeconds() {
        let cases: [(Double, String)] = [
            (0, "0:00"), (0.2, "0:01"), (12.2, "0:13"), (13, "0:13"), (59.9, "1:00"), (150, "2:30"),
            (3600, "1:00:00"), (3723.5, "1:02:04"), (-5, "0:00"), (.nan, "0:00"), (.infinity, "99:59:59"),
        ]
        for (seconds, expected) in cases { XCTAssertEqual(CueDisplay.clock(seconds), expected, "\(seconds)") }
    }

    func testRemainingTextIsEmptyUntilTheCueHasStarted() {
        XCTAssertEqual(CueDisplay.remainingText(started: false, duration: 30, elapsed: 0), "")
    }

    func testRemainingTextShowsInfinityWithoutAKnownDuration() {
        XCTAssertEqual(CueDisplay.remainingText(started: true, duration: 0, elapsed: 5), "∞")
        XCTAssertEqual(CueDisplay.remainingText(started: true, duration: .nan, elapsed: 5), "∞")
        XCTAssertEqual(CueDisplay.remainingText(started: true, duration: .infinity, elapsed: 5), "∞")
    }

    func testRemainingTextCountsDownAndNeverGoesNegative() {
        XCTAssertEqual(CueDisplay.remainingText(started: true, duration: 30, elapsed: 17), "0:13")
        XCTAssertEqual(CueDisplay.remainingText(started: true, duration: 30, elapsed: 0), "0:30")
        XCTAssertEqual(CueDisplay.remainingText(started: true, duration: 30, elapsed: 45), "0:00")
        XCTAssertEqual(CueDisplay.remainingText(started: true, duration: 30, elapsed: .nan), "0:30")
    }

    private let allTypes = ["audio", "video", "image", "pdf", "fade", "stop", "osc", "midi", "goto", "playlist",
                            "memo", "network", "web", "slideshow", "timecode", "light", "input", "group"]

    func testMediaTabExistsOnlyForTypesWithAudioPictureOrVideo() {
        let media: Set<String> = ["audio", "video", "image", "pdf", "web", "playlist", "slideshow", "input"]
        for t in allTypes {
            let tabs = InspectorLayout.tabs(for: t)
            XCTAssertEqual(tabs.contains(.media), media.contains(t), t)
            XCTAssertEqual(tabs.first, .basic)
            XCTAssertTrue(tabs.contains(.timing))
            XCTAssertTrue(tabs.contains(.advanced))
        }
    }

    func testTabsAreInFixedOrderAndUnknownTypesGetTheCommonOnes() {
        XCTAssertEqual(InspectorLayout.tabs(for: "audio"), [.basic, .media, .timing, .advanced])
        XCTAssertEqual(InspectorLayout.tabs(for: "fade"), [.basic, .timing, .advanced])
        XCTAssertEqual(InspectorLayout.tabs(for: nil), [.basic, .timing, .advanced])
        XCTAssertEqual(InspectorLayout.tabs(for: "noe-helt-annet"), [.basic, .timing, .advanced])
        XCTAssertTrue(InspectorLayout.hasMedia("AUDIO"))
    }

    func testResolveKeepsTheTabWhenItExistsAndFallsBackToBasic() {
        XCTAssertEqual(InspectorLayout.resolve(.media, for: "video"), .media)
        XCTAssertEqual(InspectorLayout.resolve(.media, for: "fade"), .basic)
        XCTAssertEqual(InspectorLayout.resolve(.timing, for: "fade"), .timing)
        XCTAssertEqual(InspectorLayout.resolve(.advanced, for: nil), .advanced)
    }

    func testCommonFieldsKeepEveryFieldFromTheOldListExactlyOnce() {
        let all = InspectorTab.allCases.flatMap { InspectorLayout.commonFields($0) }
        let expected = ["disabled", "preWait", "continueMode", "timecodeTrigger", "hotkey", "midiTrigger", "inButtonPanel", "color", "notes"]
        XCTAssertEqual(all.sorted(), expected.sorted())
        XCTAssertEqual(all.count, Set(all).count)
    }

    func testCueTypeKeysMatchTheShowFileAndEveryTypeIsCovered() {
        XCTAssertEqual(CueType.allCases.map(\.rawValue).sorted(), allTypes.sorted())
    }

    func testRemainingTextOnTheCueIsRuntimeOnly() {
        let c = Cue(type: .audio)
        XCTAssertEqual(c.remainingText, "")
        c.remainingText = CueDisplay.remainingText(started: true, duration: 30, elapsed: 17)
        XCTAssertEqual(c.remainingText, "0:13")
        let json = String(data: try! JSONEncoder().encode(c), encoding: .utf8)!
        XCTAssertFalse(json.contains("emaining"))
    }
}
