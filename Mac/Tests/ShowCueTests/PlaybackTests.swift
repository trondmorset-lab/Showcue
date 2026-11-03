import XCTest
@testable import ShowCue

/// Dekoder uten fil: verdi = tid/10 på alle kanaler.
final class RampDecoder: FrameDecoder {
    let channels: Int
    let duration: Double
    let rate: Double
    private var pos = 0
    init(channels: Int = 2, seconds: Double = 3, rate: Double = 48000) { self.channels = channels; duration = seconds; self.rate = rate }
    func seek(to seconds: Double) { pos = Int(seconds * rate) }
    func read(into dst: UnsafeMutablePointer<Float>, maxFrames: Int) -> Int {
        let n = min(maxFrames, Int(duration * rate) - pos)
        guard n > 0 else { return 0 }
        for f in 0..<n { for c in 0..<channels { dst[f * channels + c] = Float(Double(pos + f) / rate / 10) } }
        pos += n
        return n
    }
}

/// Pause, slipp loop, fadekurver og de nye cuetypene (samme tester som i Windows-versjonen).
final class PlaybackTests: XCTestCase {
    let rate = 48000.0
    private func at(_ o: [Float], _ D: Int, _ f: Int, _ ch1: Int) -> Float { o[f * D + ch1 - 1] }

    func testPauseHoldsPositionAndResumeContinues() {
        let m = PatchMixer(sampleRate: rate, deviceChannels: 8, patch: PatchBus.defaultPatch(8))
        let src = StreamingSource(decoder: RampDecoder(), sampleRate: rate, startTime: 0, endTime: 0, loop: false)
        src.fill()
        let v = Voice(source: src, sampleRate: rate, volumeDb: 0, fadeIn: 0, fadeOut: 0, loop: false, routes: AudioRoute.defaults(fileChannels: 2))
        m.add(v)
        _ = m.renderInterleaved(frames: 4800)
        let pos = src.passPosition
        v.pause()
        XCTAssertTrue(v.isPaused)
        XCTAssertTrue(m.renderInterleaved(frames: 9600).allSatisfy { $0 == 0 })
        XCTAssertEqual(src.passPosition, pos)
        XCTAssertTrue(m.contains(v), "pauset stemme blir i mikseren")
        v.resume()
        let o = m.renderInterleaved(frames: 100)
        XCTAssertEqual(Double(at(o, 8, 50, 1)), (0.1 + 50 / rate) / 10, accuracy: 0.001)
        v.stop(fade: 0); _ = m.renderInterleaved(frames: 64)
    }

    func testSeekJumpsWhilePlaying() {
        let src = StreamingSource(decoder: RampDecoder(), sampleRate: rate, startTime: 0, endTime: 0, loop: false)
        src.fill()
        var buf = [Float](repeating: 0, count: 4800 * 2)
        XCTAssertEqual(src.read(&buf, frames: 4800), 4800)
        XCTAssertEqual(src.seekLength, Int(3 * rate))
        XCTAssertEqual(src.seek(toFrame: Int(2 * rate)), Int(2 * rate))
        XCTAssertEqual(src.passPosition, Int(2 * rate), "posisjonen i runden følger spolingen")
        XCTAssertEqual(src.read(&buf, frames: 100), 100)
        XCTAssertEqual(Double(buf[50 * 2]), (2.0 + 50 / rate) / 10, accuracy: 0.001)
        XCTAssertEqual(src.seek(toFrame: Int(99 * rate)), Int(3 * rate) - 1, "forbi slutten: siste ramme")
    }

    func testSeekInsideALoopStaysInTheLoop() {
        let src = StreamingSource(decoder: RampDecoder(), sampleRate: rate, startTime: 0.5, endTime: 1.0, loop: true, bufferSeconds: 4)
        src.fill()
        XCTAssertEqual(src.seek(toFrame: Int(0.4 * rate)), Int(0.4 * rate))
        var buf = [Float](repeating: 0, count: 100 * 2)
        XCTAssertEqual(src.read(&buf, frames: 100), 100)
        XCTAssertEqual(Double(buf[50 * 2]), (0.9 + 50 / rate) / 10, accuracy: 0.001)
        XCTAssertTrue(src.isLooping)
    }

    func testReleaseLoopPlaysToTheEndOfTheFile() {
        let src = StreamingSource(decoder: RampDecoder(), sampleRate: rate, startTime: 0.5, endTime: 1.0, loop: true, bufferSeconds: 4)
        XCTAssertTrue(src.isLooping)
        src.fill(minimumFrames: 36000) // 0,5 s runde 1 + litt av runde 2 er dekodet
        var buf = [Float](repeating: 0, count: 24000 * 2)
        XCTAssertEqual(src.read(&buf, frames: 12000), 12000) // 0,25 s inn i runde 1
        src.releaseLoop()
        XCTAssertFalse(src.isLooping)
        XCTAssertEqual(src.passLength, Int(2.5 * rate), "runden går nå til filslutt")
        src.fill()
        XCTAssertEqual(src.read(&buf, frames: 12000), 12000) // slutten av runde 1
        XCTAssertEqual(src.read(&buf, frames: 100), 100)     // forbi det gamle sluttpunktet, ikke tilbake til 0,5 s
        XCTAssertEqual(Double(buf[50 * 2]), (1.0 + 50 / rate) / 10, accuracy: 0.001)
        var total = 24100
        while true {
            src.fill()
            let n = src.read(&buf, frames: 24000)
            if n == 0 { break }
            total += n
        }
        XCTAssertEqual(total, Int(2.5 * rate))
        XCTAssertTrue(src.isEnded)
    }

    func testFadeCurves() {
        XCTAssertEqual(FadeCurve.db.db(from: 0, to: -80, at: 0.5), -40, accuracy: 0.01)
        XCTAssertEqual(FadeCurve.sCurve.db(from: 0, to: -80, at: 0.5), -40, accuracy: 0.01)
        XCTAssertEqual(FadeCurve.sCurve.db(from: 0, to: -80, at: 0.1), -2.240, accuracy: 0.01)
        XCTAssertEqual(FadeCurve.linear.db(from: 0, to: Voice.silenceDb, at: 0.5), -6.0206, accuracy: 0.01)
        for c in FadeCurve.allCases {
            XCTAssertEqual(c.db(from: 0, to: -20, at: 0), 0, accuracy: 1e-9)
            XCTAssertEqual(c.db(from: 0, to: -20, at: 1), -20, accuracy: 1e-4)
        }
    }

    func testSCurveFadeIsGentleAtTheStart() {
        func level(_ curve: FadeCurve) -> Float {
            let m = PatchMixer(sampleRate: rate, deviceChannels: 2, patch: PatchBus.defaultPatch(2))
            let v = Voice(source: ConstSource(channels: 2, frames: nil), sampleRate: rate, volumeDb: 0, fadeIn: 0, fadeOut: 0, loop: false,
                          routes: AudioRoute.defaults(fileChannels: 2))
            m.add(v)
            v.fadeTo(-40, seconds: 1, stopWhenDone: false, curve: curve)
            return m.renderInterleaved(frames: 4800)[4799 * 2]
        }
        XCTAssertGreaterThan(level(.sCurve), level(.db), "S-kurven har falt mindre etter 10 %")
    }

    func testNewCueTypesRoundTrip() throws {
        var doc = ShowDocument()
        let p = Cue(type: .playlist)
        p.files = ["/m/a.mp3", "/m/b.wav"]; p.shuffle = true; p.loop = false; p.crossfade = 4; p.volumeDb = -3; p.fadeOut = 5
        let g = Cue(type: .goto); g.target = "12"; g.fire = true
        let s = Cue(type: .stop); s.target = "3"; s.release = true
        let f = Cue(type: .fade); f.target = "1"; f.curve = .sCurve
        doc.cues = [p, g, s, f]
        let data = try doc.jsonData()
        let json = String(decoding: data, as: UTF8.self)
        XCTAssertTrue(json.contains("\"type\" : \"playlist\""), json)
        XCTAssertTrue(json.contains("\"type\" : \"goto\""))
        XCTAssertTrue(json.contains("\"Curve\" : \"SCurve\""))
        let back = try JSONDecoder().decode(ShowDocument.self, from: data)
        XCTAssertEqual(back.cues.map(\.type), [.playlist, .goto, .stop, .fade])
        let bp = back.cues[0]
        XCTAssertEqual(bp.files, ["/m/a.mp3", "/m/b.wav"])
        XCTAssertTrue(bp.shuffle && !bp.loop && bp.crossfade == 4 && bp.volumeDb == -3 && bp.fadeOut == 5)
        XCTAssertTrue(back.cues[1].target == "12" && back.cues[1].fire)
        XCTAssertTrue(back.cues[2].release)
        XCTAssertTrue(back.cues[2].summary.contains("slipp loop"))
        XCTAssertEqual(back.cues[2].duration, 0)
        XCTAssertEqual(back.cues[3].curve, .sCurve)
        XCTAssertEqual(try back.jsonData(), data)
    }

    func testReadsWindowsPlaylist() throws {
        let json = """
        {"FormatVersion":1,"Cues":[{"type":"playlist","Number":"1","Files":["C:\\\\m\\\\a.mp3"],"Crossfade":3,"Shuffle":false,"Loop":true}]}
        """
        let doc = try JSONDecoder().decode(ShowDocument.self, from: Data(json.utf8))
        XCTAssertEqual(doc.cues.first?.type, .playlist)
        XCTAssertEqual(doc.cues.first?.files, ["C:\\m\\a.mp3"])
        XCTAssertEqual(doc.cues.first?.crossfade, 3)
        XCTAssertEqual(doc.cues.first?.loop, true)
    }
}
