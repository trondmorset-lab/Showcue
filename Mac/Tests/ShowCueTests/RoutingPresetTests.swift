import XCTest
@testable import ShowCue

/// Routingforhåndsvalg: ren logikk uten UI og lydmotor. Samme tilfeller som i Windows-versjonen.
final class RoutingPresetTests: XCTestCase {
    private func P(_ v: (Int, Double)...) -> [RoutingPair] { v.map { RoutingPair($0.0, $0.1) } }

    private func A(_ channels: Int, _ routes: [AudioRoute]? = nil) -> Cue {
        let c = Cue(type: .audio)
        c.filePath = "a.wav"
        c.fileChannels = channels
        c.routes = routes
        return c
    }

    private func R(_ r: [AudioRoute]) -> String {
        r.map { "\($0.source)>\($0.bus)@\(Int($0.gainDb.rounded()))" }.joined(separator: " ")
    }

    func testAddChecksNameAndPairs() {
        var list: [RoutingPreset] = []
        XCTAssertEqual(RoutingPresets.add(&list, name: "  ", pairs: P((1, 0))), .emptyName)
        XCTAssertEqual(RoutingPresets.add(&list, name: nil, pairs: P((1, 0))), .emptyName)
        XCTAssertEqual(RoutingPresets.add(&list, name: String(repeating: "x", count: 41), pairs: P((1, 0))), .nameTooLong)
        XCTAssertEqual(RoutingPresets.add(&list, name: "Hoved", pairs: []), .noPairs)
        XCTAssertEqual(RoutingPresets.add(&list, name: "Hoved", pairs: nil), .noPairs)
        XCTAssertTrue(list.isEmpty)
        XCTAssertEqual(RoutingPresets.add(&list, name: "  Hoved  ", pairs: P((1, 0))), .ok)
        XCTAssertEqual(list[0].name, "Hoved")
        XCTAssertEqual(RoutingPresets.add(&list, name: "hoved", pairs: P((3, 0))), .duplicateName)
        XCTAssertEqual(list.count, 1)
    }

    func testAddStopsAtTheLimit() {
        var list: [RoutingPreset] = []
        for i in 0..<RoutingPresets.maxPresets { XCTAssertEqual(RoutingPresets.add(&list, name: "p\(i)", pairs: P((1, 0))), .ok) }
        XCTAssertEqual(RoutingPresets.add(&list, name: "en til", pairs: P((1, 0))), .tooManyPresets)
        XCTAssertEqual(list.count, RoutingPresets.maxPresets)
    }

    func testRenameUpdateAndRemove() {
        var list: [RoutingPreset] = []
        _ = RoutingPresets.add(&list, name: "Hoved", pairs: P((1, 0)))
        _ = RoutingPresets.add(&list, name: "Monitor", pairs: P((3, -6)))
        XCTAssertEqual(RoutingPresets.rename(&list, index: 1, name: "HOVED"), .duplicateName)
        XCTAssertEqual(RoutingPresets.rename(&list, index: 1, name: ""), .emptyName)
        XCTAssertEqual(RoutingPresets.rename(&list, index: 5, name: "x"), .notFound)
        XCTAssertEqual(RoutingPresets.rename(&list, index: -1, name: "x"), .notFound)
        XCTAssertEqual(RoutingPresets.rename(&list, index: 1, name: "Monitor"), .ok)
        XCTAssertEqual(RoutingPresets.rename(&list, index: 1, name: "Scene-monitor"), .ok)
        XCTAssertEqual(list[1].name, "Scene-monitor")
        XCTAssertEqual(RoutingPresets.formatPairs(list[1]), "3-4:-6")
        XCTAssertEqual(RoutingPresets.update(&list, index: 0, name: "Hoved", pairs: P((1, 0), (5, 0))), .ok)
        XCTAssertEqual(RoutingPresets.formatPairs(list[0]), "1-2, 5-6")
        XCTAssertEqual(RoutingPresets.update(&list, index: 0, name: "Hoved", pairs: []), .noPairs)
        XCTAssertEqual(RoutingPresets.formatPairs(list[0]), "1-2, 5-6")
        XCTAssertTrue(RoutingPresets.remove(&list, index: 0))
        XCTAssertFalse(RoutingPresets.remove(&list, index: 3))
        XCTAssertEqual(list.count, 1)
        XCTAssertEqual(list[0].name, "Scene-monitor")
    }

    func testParsePairsAcceptsValidText() {
        let cases: [(String, String)] = [
            ("1-2", "1-2"), ("1-2, 3-4:-6", "1-2, 3-4:-6"), ("3–4;1-2", "3-4, 1-2"), ("1-2 + 1-2", "1-2"), ("63-64", "63-64"),
            ("5-6:99", "5-6:12"), ("5-6:-100", "5-6:-60"), ("5-6:2.5", "5-6:2.5"),
        ]
        for (text, expected) in cases {
            guard let pairs = RoutingPresets.parsePairs(text) else { XCTFail("skulle kunne leses: \(text)"); continue }
            XCTAssertEqual(RoutingPresets.formatPairs(RoutingPreset("x", pairs)), expected, text)
        }
    }

    func testParsePairsRejectsInvalidText() {
        let bad = ["", "   ", "1", "1-3", "2-1", "0-1", "64-65", "a-b", "1-2:", "1-2:x", "1-2:NaN", "1-2:Infinity", "1-2:-Infinity", "1-2:3:4", "-1-0",
                   "1-2, 3-4, 5-6, 7-8, 9-10, 11-12, 13-14, 15-16, 17-18"]
        for text in bad { XCTAssertNil(RoutingPresets.parsePairs(text), text) }
        XCTAssertNil(RoutingPresets.parsePairs(nil))
    }

    func testPairValuesAreLimitedAndNanSafe() {
        XCTAssertEqual(RoutingPair(1, .nan).gainDb, 0)
        XCTAssertEqual(RoutingPair(1, .infinity).gainDb, 12)
        XCTAssertEqual(RoutingPair(1, -.infinity).gainDb, -60)
        XCTAssertEqual(RoutingPair(-5).bus, 1)
        XCTAssertEqual(RoutingPair(500).bus, 63)
    }

    func testRoutesForMonoStereoAndMultichannel() {
        let main = RoutingPreset("Hoved", P((1, 0)))
        XCTAssertEqual(R(RoutingPresets.routes(main, fileChannels: 1)), "1>1@0 1>2@0")
        XCTAssertEqual(R(RoutingPresets.routes(main, fileChannels: 2)), "1>1@0 2>2@0")
        XCTAssertEqual(R(RoutingPresets.routes(RoutingPreset("M", P((3, -6))), fileChannels: 2)), "1>3@-6 2>4@-6")
        XCTAssertEqual(R(RoutingPresets.routes(main, fileChannels: 6)), "1>1@-3 2>2@-3 3>1@-3 4>2@-3 5>1@-3 6>2@-3")
        XCTAssertEqual(R(RoutingPresets.routes(RoutingPreset("x", P((1, 2))), fileChannels: 3)), "1>1@-1 2>2@-1 3>1@-1")
        XCTAssertTrue(RoutingPresets.routes(main, fileChannels: 0).isEmpty)
        XCTAssertTrue(RoutingPresets.routes(main, fileChannels: -1).isEmpty)
    }

    func testRoutesForTwoPairsAndOverlap() {
        let both = RoutingPreset("Hoved + monitor", P((1, 0), (3, -6)))
        XCTAssertEqual(R(RoutingPresets.routes(both, fileChannels: 1)), "1>1@0 1>2@0 1>3@-6 1>4@-6")
        XCTAssertEqual(R(RoutingPresets.routes(both, fileChannels: 2)), "1>1@0 1>3@-6 2>2@0 2>4@-6")
        // To par som deler en utgang gir ikke samme rute to ganger; det høyeste nivået gjelder.
        let overlap = RoutingPreset("o", P((1, 0), (2, -6)))
        XCTAssertEqual(R(RoutingPresets.routes(overlap, fileChannels: 1)), "1>1@0 1>2@0 1>3@-6")
    }

    func testRoutesSkipPairsOutsideThePatch() {
        let both = RoutingPreset("x", P((1, 0), (3, 0)))
        XCTAssertEqual(R(RoutingPresets.routes(both, fileChannels: 2, busCount: 3)), "1>1@0 2>2@0")
        XCTAssertEqual(R(RoutingPresets.routes(both, fileChannels: 2, busCount: 4)), "1>1@0 1>3@0 2>2@0 2>4@0")
        XCTAssertTrue(RoutingPresets.routes(RoutingPreset("m", P((3, 0))), fileChannels: 2, busCount: 2).isEmpty)
    }

    func testCleanListDropsBrokenEntries() {
        let list = RoutingPresets.cleanList([
            nil,
            RoutingPreset("", P((1, 0))),
            RoutingPreset("Uten par", []),
            RoutingPreset("Hoved", P((1, 0), (1, -3))),
            RoutingPreset("HOVED", P((3, 0))),
            RoutingPreset(String(repeating: "y", count: 60), P((3, 0))),
        ])
        XCTAssertEqual(list.count, 2)
        XCTAssertEqual(list[0].name, "Hoved")
        XCTAssertEqual(RoutingPresets.formatPairs(list[0]), "1-2")
        XCTAssertEqual(list[1].name.count, 40)
        XCTAssertTrue(RoutingPresets.cleanList(nil).isEmpty)
    }

    func testApplyToSelectionSetsRoutesOnRoutedCuesWithAudioOnly() {
        let stereo = A(2), mono = A(1), silent = A(0)
        let already = A(2, [AudioRoute(1, 3), AudioRoute(2, 4)])
        let other = Cue(type: .memo)
        let preset = RoutingPreset("Monitor", P((3, 0)))
        let r = RoutingPresets.apply([stereo, mono, silent, already, other, nil, stereo], preset: preset)
        XCTAssertEqual(r, RoutingApplyResult(2, 1, 3))
        XCTAssertEqual(R(stereo.routes!), "1>3@0 2>4@0")
        XCTAssertEqual(R(mono.routes!), "1>3@0 1>4@0")
        XCTAssertNil(silent.routes)
        XCTAssertEqual(R(already.routes!), "1>3@0 2>4@0")
    }

    func testApplyDoesNotSilenceACueWhenThePresetFitsNoOutput() {
        let cue = A(2)
        let r = RoutingPresets.apply([cue], preset: RoutingPreset("Monitor", P((3, 0))), busCount: 2)
        XCTAssertEqual(r, RoutingApplyResult(0, 0, 1))
        XCTAssertNil(cue.routes)
    }

    func testCopyAndPasteRoutingBetweenCues() {
        let src = A(2, [AudioRoute(1, 3, -6), AudioRoute(2, 4, -6), AudioRoute(2, 9, 0)])
        var clip = RouteClipboard()
        XCTAssertFalse(clip.hasContent)
        XCTAssertFalse(clip.copy(Cue(type: .memo)))
        XCTAssertTrue(clip.copy(src))
        src.routes![0].gainDb = 0 // senere endring i kilden slår ikke gjennom
        XCTAssertEqual(clip.routes![0].gainDb, -6)

        let mono = A(1), stereo = A(2), noAudio = A(0)
        let r = RoutingPresets.paste([mono, stereo, noAudio], clipboard: clip, busCount: 8)
        XCTAssertEqual(r, RoutingApplyResult(2, 0, 1))
        XCTAssertEqual(R(mono.routes!), "1>3@-6") // kanal 2 og utgang 9 finnes ikke her
        XCTAssertEqual(R(stereo.routes!), "1>3@-6 2>4@-6")
        XCTAssertEqual(RoutingPresets.paste([mono, stereo, noAudio], clipboard: clip, busCount: 8), RoutingApplyResult(0, 2, 1))
    }

    func testPasteStandardAndAllOffAndEmptyClipboard() {
        let cue = A(2, [AudioRoute(1, 5)])
        var clip = RouteClipboard()
        XCTAssertEqual(RoutingPresets.paste([cue], clipboard: clip), RoutingApplyResult(0, 0, 0))
        clip.copy(A(2))
        XCTAssertTrue(clip.hasContent)
        XCTAssertNil(clip.routes)
        XCTAssertEqual(RoutingPresets.paste([cue], clipboard: clip).changed, 1)
        XCTAssertNil(cue.routes)
        clip.copy(A(2, []))
        XCTAssertEqual(RoutingPresets.paste([cue], clipboard: clip).changed, 1)
        XCTAssertEqual(cue.routes?.count, 0)
        clip.clear()
        XCTAssertFalse(clip.hasContent)
    }

    func testPasteSkipsCuesThatWouldGoSilentByMistake() {
        let src = A(8, [AudioRoute(7, 7), AudioRoute(8, 8)])
        var clip = RouteClipboard()
        clip.copy(src)
        let small = A(2)
        XCTAssertEqual(RoutingPresets.paste([small], clipboard: clip), RoutingApplyResult(0, 0, 1))
        XCTAssertNil(small.routes)
    }

    func testCleanRoutesLimitsDuplicatesAndNan() {
        let clean = RoutingPresets.cleanRoutes([
            nil, AudioRoute(0, 1), AudioRoute(1, 0), AudioRoute(3, 1), AudioRoute(1, 65), AudioRoute(1, 2, .nan), AudioRoute(1, 2, -3), AudioRoute(2, 1, .infinity),
        ], fileChannels: 2)
        XCTAssertEqual(R(clean), "1>2@0 2>1@12")
    }

    func testDescribeNamesTheRouting() {
        let presets = [RoutingPreset("Hoved", P((1, 0))), RoutingPreset("Monitor", P((3, 0)))]
        XCTAssertEqual(RoutingPresets.describe(nil, fileChannels: 2, presets: presets), "Standard")
        XCTAssertEqual(RoutingPresets.describe([], fileChannels: 2, presets: presets), "Av")
        XCTAssertEqual(RoutingPresets.describe(RoutingPresets.routes(presets[1], fileChannels: 2), fileChannels: 2, presets: presets), "Monitor")
        XCTAssertEqual(RoutingPresets.describe([AudioRoute(1, 1), AudioRoute(2, 2), AudioRoute(1, 5)], fileChannels: 2, presets: presets), "Utgang 1-2, 5")
        XCTAssertEqual(RoutingPresets.describe(RoutingPresets.routes(presets[1], fileChannels: 2), fileChannels: 2, presets: nil), "Utgang 3-4")
        XCTAssertEqual(RoutingPresets.ranges([8, 1, 2, 3, 5, 7, 2]), "1-3, 5, 7-8")
        XCTAssertEqual(RoutingPresets.ranges([]), "")
    }

    func testShowFileRoundTripAndOldFilesWithoutPresets() throws {
        var doc = ShowDocument()
        XCTAssertEqual(RoutingPresets.add(&doc.routingPresets, name: "Hoved + monitor", pairs: P((1, 0), (3, -6))), .ok)
        let data = try doc.jsonData()
        let json = String(decoding: data, as: UTF8.self)
        XCTAssertTrue(json.contains("\"RoutingPresets\""))
        XCTAssertTrue(json.contains("\"Pairs\""))
        XCTAssertTrue(json.contains("\"GainDb\" : -6"))
        let back = try JSONDecoder().decode(ShowDocument.self, from: data)
        XCTAssertEqual(back.routingPresets.count, 1)
        XCTAssertEqual(RoutingPresets.formatPairs(back.routingPresets[0]), "1-2, 3-4:-6")
        XCTAssertEqual(back.routingPresets[0].name, "Hoved + monitor")

        let old = try JSONDecoder().decode(ShowDocument.self, from: Data("{\"FormatVersion\":1,\"Title\":\"x\",\"Cues\":[]}".utf8))
        XCTAssertTrue(old.routingPresets.isEmpty)
    }

    func testBrokenPresetFieldDoesNotStopTheShowFromOpening() throws {
        let fields = ["\"RoutingPresets\":\"tull\"", "\"RoutingPresets\":null", "\"RoutingPresets\":{\"a\":1}", "\"RoutingPresets\":[1,\"x\",null,{\"Name\":5}]"]
        for f in fields {
            let doc = try JSONDecoder().decode(ShowDocument.self, from: Data(("{\"Title\":\"x\"," + f + ",\"Cues\":[]}").utf8))
            XCTAssertTrue(doc.routingPresets.isEmpty, f)
        }
    }

    func testBrokenEntriesAreSkippedAndGoodOnesKept() throws {
        let json = "{\"RoutingPresets\":[{\"Name\":\"Bra\",\"Pairs\":[{\"Bus\":3,\"GainDb\":-2},null]},{\"Name\":\"Skadet\",\"Pairs\":[{\"Bus\":\"x\"}]},{\"Name\":\"\",\"Pairs\":[{\"Bus\":1}]},{\"Name\":\"Uten\",\"Pairs\":[]}]}"
        let doc = try JSONDecoder().decode(ShowDocument.self, from: Data(json.utf8))
        XCTAssertEqual(doc.routingPresets.count, 1)
        XCTAssertEqual(doc.routingPresets[0].name, "Bra")
        XCTAssertEqual(RoutingPresets.formatPairs(doc.routingPresets[0]), "3-4:-2")
    }
}
