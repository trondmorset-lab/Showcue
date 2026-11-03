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
}
