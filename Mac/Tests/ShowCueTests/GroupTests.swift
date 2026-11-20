import XCTest
@testable import ShowCue

/// Gruppe-cuer: lesing av cuenumre, nummerering på nytt og lagring. Samme tester som i Windows-versjonen.
final class GroupTests: XCTestCase {
    private func show() -> [Cue] {
        func stop(_ number: String, disabled: Bool = false) -> Cue {
            let c = Cue(type: .stop); c.number = number; c.disabled = disabled; return c
        }
        return [stop("1"), stop("2"), stop("3.5"), stop("4"), stop("5", disabled: true), stop("A1"), stop("")]
    }

    private func numbers(_ cues: [Cue]) -> [String] { cues.map(\.number) }

    func testResolvesNumbersInTheOrderWritten() {
        let found = CueTargets.resolve(show(), "4, 1;A1")
        XCTAssertEqual(numbers(found.cues), ["4", "1", "A1"])
    }

    func testResolvesRangesByNumberValue() {
        let list = show()
        XCTAssertEqual(numbers(CueTargets.resolve(list, "2-5").cues), ["2", "3.5", "4", "5"])
        XCTAssertEqual(numbers(CueTargets.resolve(list, "5-2").cues), ["2", "3.5", "4", "5"]) // omvendt område
    }

    func testANumberThatContainsADashIsFoundAsItIs() {
        let list = ["1-2", "1", "2"].map { n -> Cue in let c = Cue(type: .stop); c.number = n; return c }
        XCTAssertEqual(numbers(CueTargets.resolve(list, "1-2").cues), ["1-2"])
    }

    func testSkipsItselfAndDuplicatesAndReportsMissing() {
        let list = show()
        let found = CueTargets.resolve(list, "1, 2, 2, 9, 10-12, , x", excluding: list[0])
        XCTAssertEqual(numbers(found.cues), ["2"])
        XCTAssertEqual(found.missing, ["9", "10-12", "x"])
    }

    func testHandlesEmptyAndStrangeInput() {
        let list = show()
        for spec in ["", "-", "NaN-Infinity", ",;,"] {
            XCTAssertTrue(CueTargets.resolve(list, spec).cues.isEmpty, spec)
        }
    }

    func testRenumberChangesNumbersAndBothEndsOfARange() {
        let map = ["10": "1", "12": "3", "a1": "7"]
        XCTAssertEqual(CueTargets.renumber("10; 12, a1, 99", map), "1, 3, 7, 99")
        XCTAssertEqual(CueTargets.renumber("10-12", map), "1-3")
        XCTAssertEqual(CueTargets.renumber("", map), "")
    }

    func testGroupRoundTripsInTheShowFile() throws {
        var doc = ShowDocument()
        let g = Cue(type: .group); g.number = "9"; g.targets = " 1, 2, 4-6 "
        doc.cues = [g]
        let data = try doc.jsonData()
        let json = String(decoding: data, as: UTF8.self)
        XCTAssertTrue(json.contains("\"type\" : \"group\""), json)
        XCTAssertTrue(json.contains("\"Targets\" : \"1, 2, 4-6\""), json)
        let back = try JSONDecoder().decode(ShowDocument.self, from: data)
        XCTAssertEqual(back.cues[0].type, .group)
        XCTAssertEqual(back.cues[0].targets, "1, 2, 4-6")
        XCTAssertEqual(back.cues[0].summary, "→ 1, 2, 4-6")
        XCTAssertEqual(try back.jsonData(), data)
    }

    func testEmptyGroupShowsThatTheTargetIsMissing() {
        XCTAssertTrue(Cue(type: .group).summary.contains("mål mangler"))
    }
}
