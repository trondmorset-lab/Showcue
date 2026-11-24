import XCTest
@testable import ShowCue

/// Feil funnet med stresstesting: ugyldige verdier, ødelagte pakker og loop-grensetilfeller skal aldri
/// få appen til å krasje, henge eller gjøre showet umulig å lagre (samme tester som i Windows-versjonen).
final class RobustnessTests: XCTestCase {
    func testTinyLoopDoesNotHangTheDecoder() {
        let src = StreamingSource(decoder: RampDecoder(channels: 1, seconds: 1 / 48000.0), sampleRate: 48000, startTime: 0, endTime: 0, loop: true)
        XCTAssertFalse(src.isLooping, "en loop på ett sample er for kort")
        let done = expectation(description: "fill")
        DispatchQueue.global().async { src.fill(); done.fulfill() }
        wait(for: [done], timeout: 5)
    }

    func testNonFiniteTimesNeverCrash() {
        XCTAssertEqual(frameCount(.nan, 48000), 0)
        XCTAssertEqual(frameCount(.infinity, 48000), 0)
        XCTAssertEqual(frameCount(-1, 48000), 0)
        XCTAssertEqual(safeSeconds(.nan), 0)
        XCTAssertEqual(dbToGain(.nan), 0)
        _ = StreamingSource(decoder: RampDecoder(), sampleRate: 48000, startTime: .nan, endTime: .infinity, loop: true)
        _ = Timecode.fromSeconds(.infinity, .fps25)
        let v = Voice(source: StreamingSource(decoder: RampDecoder(), sampleRate: 48000, startTime: 0, endTime: 0, loop: false),
                      sampleRate: 48000, volumeDb: .nan, fadeIn: .infinity, fadeOut: 1e300, loop: false, routes: AudioRoute.defaults(fileChannels: 2))
        v.fadeTo(.nan, seconds: .nan, stopWhenDone: false)
        v.stop(fade: .nan)
    }

    func testShowWithNonFiniteValuesCanBeSavedAndDuplicated() throws {
        let c = Cue(type: .audio)
        c.fileDuration = .nan
        XCTAssertEqual(c.fileDuration, 0)
        c.startTime = .infinity
        c.loudness = .nan
        c.routes = [AudioRoute(1, 1, .nan)]
        _ = try JSONEncoder().encode(c)
        _ = c.clone()
    }

    func testTruncatedOscPacketsAreIgnored() {
        let full = [UInt8](Osc.encode("/cue/12/start", [.int(1), .string("x")]))
        for n in 0...full.count { _ = Osc.decode(Data(full[0..<n])) }
        var blob = [UInt8](Osc.encode("/x", []))
        blob += [UInt8](",b\0\0".utf8) + [0xFF, 0xFF, 0xFF, 0xFF]
        _ = Osc.decode(Data(blob))
    }

    func testClockHandlesHugeAndInvalidTimes() {
        XCTAssertEqual(OscFeedback.clock(1e12), "99:59:59")
        XCTAssertEqual(OscFeedback.clock(.infinity), "99:59:59")
        XCTAssertEqual(OscFeedback.clock(.nan), "0:00")
        XCTAssertEqual(TimeFormat.format(1e15), "99:59:59")
        XCTAssertEqual(LoudnessMeter.gain(for: .nan, target: -23), 0)
    }

    func testOscBlobWithHugeLengthIsIgnored() {
        // Typetaggene «b» og «i», og en blob som påstår at den er nesten 2 GB lang.
        let bytes: [UInt8] = [0x2F, 0x78, 0, 0, 0x2C, 0x62, 0x69, 0, 0x7F, 0xFF, 0xFF, 0xFF, 0, 0, 0, 5]
        let msgs = Osc.decode(Data(bytes))
        XCTAssertEqual(msgs.count, 1)
        XCTAssertTrue(msgs[0].args.isEmpty)
    }

    func testDmxLevelsRejectNonFiniteNumbers() throws {
        XCTAssertThrowsError(try DmxLevels.parse("1@NaN"))
        XCTAssertThrowsError(try DmxLevels.parse("1@NaN%"))
        XCTAssertThrowsError(try DmxLevels.parse("1@Infinity"))
        XCTAssertEqual(try DmxLevels.parse("1@255 2@50%").count, 2)
    }

    func testHexEscapeNeedsTwoHexDigits() {
        XCTAssertEqual(NetworkSender.unescape("\\x41"), Data([0x41]))
        XCTAssertEqual(NetworkSender.unescape("\\x 4A"), Data("\\x 4A".utf8)) // mellomrom er ikke et sifferpar
        XCTAssertEqual(NetworkSender.unescape("\\x+4A"), Data("\\x+4A".utf8)) // fortegn heller ikke
    }

    func testDamagedShowFileStillOpens() throws {
        let json = #"""
        {"Title":null,"Cues":[null,{"type":"fade","Target":"5","FadeTime":"tre"},
         {"type":"audio","FilePath":"a.wav","VolumeDb":-6,"Loop":"ja","Routes":[{"Source":1,"Bus":2,"GainDb":0}]},
         {"type":"playlist","Files":["b.wav"]},{"type":"osc","Host":"h","Port":"åtte"}]}
        """#
        let doc = try JSONDecoder().decode(ShowDocument.self, from: Data(json.utf8))
        XCTAssertEqual(doc.cues.count, 4) // «null» er ingen cue
        XCTAssertEqual(doc.cues[0].target, "5")
        XCTAssertEqual(doc.cues[0].fadeTime, 3) // ugyldig verdi gir standardverdi
        XCTAssertEqual(doc.cues[1].volumeDb, -6) // resten av cuen er intakt
        XCTAssertFalse(doc.cues[1].loop)
        XCTAssertEqual(doc.cues[2].files, ["b.wav"])
        XCTAssertEqual(doc.cues[3].host, "h")
        XCTAssertEqual(doc.cues[3].port, 8000)
        _ = try doc.jsonData() // og kan lagres igjen

        XCTAssertTrue(try JSONDecoder().decode(ShowDocument.self, from: Data(#"{"Cues":null}"#.utf8)).cues.isEmpty)
        // Uten eller med ukjent cuetype avvises filen, i stedet for å gjette «stopp».
        XCTAssertThrowsError(try JSONDecoder().decode(ShowDocument.self, from: Data(#"{"Cues":[{"Number":"1"}]}"#.utf8)))
        XCTAssertThrowsError(try JSONDecoder().decode(ShowDocument.self, from: Data(#"{"Cues":[{"type":"tull"}]}"#.utf8)))
    }
}
