import XCTest
@testable import ShowCue

/// Tidskode (MTC/LTC), DMX/Art-Net og live-inngang. Samme tester som i Windows-versjonen.
final class TimecodeTests: XCTestCase {

    // MARK: Tidskode

    func testFrameNumbers() {
        let cases: [(TimecodeRate, String, Int)] = [
            (.fps25, "01:00:00:00", 90000), (.fps24, "00:00:01:23", 47), (.fps30, "00:01:00:00", 1800),
            (.fps2997Drop, "00:01:00:02", 1800), (.fps2997Drop, "00:10:00:00", 17982), (.fps2997Drop, "01:00:00:00", 107892),
        ]
        for (rate, text, frame) in cases {
            let tc = Timecode.parse(text)!
            XCTAssertEqual(tc.toFrame(rate), frame, text)
            XCTAssertEqual(Timecode.fromFrame(frame, rate), tc, text)
        }
    }

    func testDropFrameSkipsTwoFramesEachMinute() {
        XCTAssertEqual(Timecode.fromFrame(1799, .fps2997Drop), Timecode(0, 0, 59, 29))
        XCTAssertEqual(Timecode.fromFrame(1800, .fps2997Drop), Timecode(0, 1, 0, 2))
        XCTAssertEqual(Timecode.fromFrame(17981, .fps2997Drop), Timecode(0, 9, 59, 29))
        XCTAssertEqual(Timecode.fromFrame(17982, .fps2997Drop), Timecode(0, 10, 0, 0))
        for n in stride(from: 0, to: 40000, by: 7) { XCTAssertEqual(Timecode.fromFrame(n, .fps2997Drop).toFrame(.fps2997Drop), n) }
        XCTAssertEqual(Timecode(0, 1, 0, 2).format(.fps2997Drop), "00:01:00;02")
    }

    func testParseAndCross() {
        XCTAssertEqual(Timecode.parse(" 10:20:30.15 "), Timecode(10, 20, 30, 15))
        XCTAssertNil(Timecode.parse("25:00:00:00"))
        XCTAssertNil(Timecode.parse("1:2:3"))
        XCTAssertEqual(Timecode.normalize("1:0:10;0"), "01:00:10:00")
        XCTAssertEqual(Timecode.normalize("tull"), "")
        XCTAssertTrue(Timecode.crossed(previous: 99, now: 101, target: 100, rate: .fps25))
        XCTAssertTrue(Timecode.crossed(previous: 99, now: 100, target: 100, rate: .fps25))
        XCTAssertFalse(Timecode.crossed(previous: 100, now: 101, target: 100, rate: .fps25))
        XCTAssertFalse(Timecode.crossed(previous: 0, now: 1000, target: 100, rate: .fps25))
        XCTAssertFalse(Timecode.crossed(previous: 200, now: 101, target: 100, rate: .fps25))
    }

    func testMtcQuarterFramesRoundTrip() {
        let rate = TimecodeRate.fps25
        let start = Timecode(1, 59, 58, 20).toFrame(rate)
        let dec = MtcDecoder()
        var seen: [Timecode] = []
        for q in 0..<(8 * 30) { if let tc = dec.quarterFrame(Mtc.quarterFrame(at: q, startFrame: start, rate)) { seen.append(tc) } }
        XCTAssertEqual(seen.count, 30)
        for (i, tc) in seen.enumerated() { XCTAssertEqual(tc, Timecode.fromFrame(start + 2 + 2 * i, rate)) }
        XCTAssertEqual(dec.rate, rate)
        XCTAssertTrue(seen.contains(Timecode(2, 0, 0, 0)))

        XCTAssertEqual(Mtc.fullFrame(Timecode(1, 2, 3, 4), .fps2997Drop), [0xF0, 0x7F, 0x7F, 0x01, 0x01, 0x41, 0x02, 0x03, 0x04, 0xF7])
        XCTAssertEqual(dec.fullFrame(Mtc.fullFrame(Timecode(1, 2, 3, 4), .fps2997Drop)), Timecode(1, 2, 3, 4))
        XCTAssertEqual(dec.rate, .fps2997Drop)
        XCTAssertNil(dec.fullFrame([0xF0, 0x7F, 0x7F, 0x02, 0x01, 0, 0, 0, 0, 0xF7]))
    }

    func testMidiParserReadsQuarterFrames() {
        let p = MidiByteParser()
        let ev = p.feed([0xF1, 0x05, 0x90, 60, 100, 0xF1, 0x17, 61, 100])
        var q: [UInt8] = []
        var notes = 0
        for e in ev {
            if case .quarterFrame(let b) = e { q.append(b) }
            if case .noteOn = e { notes += 1 }
        }
        XCTAssertEqual(q, [0x05, 0x17])
        XCTAssertEqual(notes, 1, "F1 avbryter running status")
    }

    /// Enkel LTC-leser for testen: finner flanker, tolker korte/lange intervaller som 1/0.
    private func decodeLtc(_ s: [Float], samplesPerBit: Double) -> [Timecode] {
        var bits: [Bool] = []
        var last = 0, pendingHalf = false
        for i in 1..<s.count where (s[i] >= 0) != (s[i - 1] >= 0) {
            let len = Double(i - last) / samplesPerBit
            last = i
            if len > 0.75 { bits.append(false); pendingHalf = false }
            else if pendingHalf { bits.append(true); pendingHalf = false }
            else { pendingHalf = true }
        }
        var found: [Timecode] = []
        var i = 0
        while i + 80 <= bits.count {
            if let tc = Ltc.decode(Array(bits[i..<i + 80])) { found.append(tc); i += 80 } else { i += 1 }
        }
        return found
    }

    func testLtcAudioDecodesToSequentialFrames() {
        for rate in [TimecodeRate.fps25, .fps30, .fps2997Drop] {
            let m = PatchMixer(sampleRate: 48000, deviceChannels: 2, patch: PatchBus.defaultPatch(2))
            let start = Timecode(9, 59, 59, 20)
            let ltc = LtcSource(busIndex: 1, levelDb: -12, start: start, rate: rate, sampleRate: 48000)
            m.add(ltc)
            let o = m.renderInterleaved(frames: 48000)
            let right = (0..<48000).map { o[$0 * 2 + 1] }
            XCTAssertTrue((0..<48000).allSatisfy { o[$0 * 2] == 0 }, "bare på utgang 2")
            XCTAssertEqual(Double(right.max()!), pow(10, -12 / 20.0), accuracy: 1e-4)
            let frames = decodeLtc(right, samplesPerBit: 48000 / (rate.fps * 80))
            XCTAssertGreaterThanOrEqual(frames.count, rate.nominal - 2, "\(rate)")
            guard let first = frames.first?.toFrame(rate) else { continue }
            XCTAssertTrue((0...1).contains(first - start.toFrame(rate)))
            for (i, tc) in frames.enumerated() { XCTAssertEqual(tc, Timecode.fromFrame(first + i, rate)) }
            XCTAssertTrue(frames.contains(Timecode(10, 0, 0, 0)))
            ltc.stop(); _ = m.renderInterleaved(frames: 64)
        }
    }

    func testLtcBitsHaveSyncWordAndEvenZeros() {
        for rate in TimecodeRate.allCases {
            let b = Ltc.bits(Timecode(23, 59, 59, 23), rate)
            XCTAssertEqual(b.filter { !$0 }.count % 2, 0)
            XCTAssertEqual(Ltc.decode(b), Timecode(23, 59, 59, 23))
            XCTAssertEqual(b[10], rate == .fps2997Drop)
        }
    }

    func testRouterFiresTimecodeTriggersWhenEnabled() {
        let fake = FakeShow()
        var s = AppSettings()
        let r = RemoteRouter(settings: s)
        r.show = fake
        r.handleTimecode(Timecode(1, 0, 0, 0), rate: .fps25)
        XCTAssertFalse(fake.log.contains { $0.hasPrefix("tc") })
        s.mtcInEnabled = true
        r.settings = s
        r.handleTimecode(Timecode(1, 0, 0, 0), rate: .fps25)
        r.handleTimecode(Timecode(1, 0, 0, 2), rate: .fps25)
        XCTAssertEqual(fake.log.filter { $0.hasPrefix("tc") }, ["tc 90000-90002"])
        XCTAssertEqual(r.timecodeStatus, "MTC inn: 01:00:00:02")
    }

    // MARK: DMX / Art-Net

    func testDmxLevelsParse() throws {
        XCTAssertEqual(try DmxLevels.parse("1@255, 2-4@50%  10=0\n512@12.4"), [1: 255, 2: 128, 3: 128, 4: 128, 10: 0, 512: 12])
        XCTAssertEqual(try DmxLevels.parse(""), [:])
        XCTAssertThrowsError(try DmxLevels.parse("0@10")) { XCTAssertTrue($0.localizedDescription.contains("1–512")) }
        XCTAssertThrowsError(try DmxLevels.parse("1@256"))
        XCTAssertThrowsError(try DmxLevels.parse("1@120%"))
        XCTAssertThrowsError(try DmxLevels.parse("5-2@10"))
        XCTAssertThrowsError(try DmxLevels.parse("lys"))
        XCTAssertEqual(DmxLevels.summary("1-3@10"), "3 kanaler")
    }

    func testArtDmxPacket() {
        var data = [UInt8](repeating: 0, count: 512)
        data[0] = 255; data[511] = 7
        let p = ArtNet.dmxPacket(universe: 0x1234, sequence: 9, data: data)
        XCTAssertEqual(p.count, 530)
        XCTAssertEqual(Array(p[0..<8]), Array("Art-Net".utf8) + [0])
        XCTAssertEqual(Array(p[8..<18]), [0x00, 0x50, 0x00, 14, 9, 0, 0x34, 0x12, 0x02, 0x00])
        XCTAssertEqual(p[18], 255)
        XCTAssertEqual(p[529], 7)
    }

    func testDmxFaderInterpolatesAndTakesOver() {
        let f = DmxFader()
        f.fade(to: [1: 200, 2: 100], seconds: 2, now: 10)
        XCTAssertEqual(f.values(10)[0], 0)
        XCTAssertEqual(f.values(11)[0], 100)
        XCTAssertEqual(f.values(11)[1], 50)
        f.fade(to: [1: 0], seconds: 1, now: 11)
        XCTAssertEqual(f.values(11)[0], 100)
        XCTAssertEqual(f.values(11.5)[0], 50)
        XCTAssertEqual(f.values(11.5)[1], 75)
        XCTAssertEqual(f.values(12)[0], 0)
        XCTAssertEqual(f.values(13)[1], 100)
        f.fade(to: [3: 33], seconds: 0, now: 13)
        XCTAssertEqual(f.values(13)[2], 33)
    }

    // MARK: Live-inngang

    func testLiveRingKeepsLatencyLowAndPadsWithSilence() {
        let r = LiveRing(channels: 2, sampleRate: 48000, maxLatency: 0.1)
        r.write([Float](repeating: 0.5, count: 200))
        var dst = [Float](repeating: 9, count: 400)
        dst.withUnsafeMutableBufferPointer { r.read($0.baseAddress!, frames: 200) }
        XCTAssertTrue(dst[..<200].allSatisfy { $0 == 0.5 })
        XCTAssertTrue(dst[200...].allSatisfy { $0 == 0 })
        r.write([Float](repeating: 0, count: 48000 * 2))
        XCTAssertTrue((1...4800).contains(r.bufferedFrames))
    }

    func testInputGoesThroughRouting() {
        let m = PatchMixer(sampleRate: 48000, deviceChannels: 4, patch: PatchBus.defaultPatch(4))
        let ring = LiveRing(channels: 1, sampleRate: 48000, maxLatency: 1)
        var released = false
        do {
            let src = LiveSource(ring: ring)
            src.onClose = { _ in released = true }
            let v = Voice(source: src, sampleRate: 48000, volumeDb: -6, fadeIn: 0, fadeOut: 0, loop: true,
                          routes: [AudioRoute(1, 3), AudioRoute(1, 4, -6)])
            m.add(v)
            ring.write([Float](repeating: 0.4, count: 1000))
            var o = m.renderInterleaved(frames: 1000)
            XCTAssertEqual(o[500 * 4 + 2], 0.4 * 0.501, accuracy: 0.002)
            XCTAssertEqual(o[500 * 4 + 3], 0.4 * 0.501 * 0.501, accuracy: 0.002)
            XCTAssertEqual(o[500 * 4 + 0], 0)
            o = m.renderInterleaved(frames: 1000)
            XCTAssertEqual(o[500 * 4 + 2], 0, "ingenting nytt: stillhet")
            XCTAssertTrue(m.contains(v), "fortsatt i gang")
            v.stop(fade: 0); _ = m.renderInterleaved(frames: 64)
        }
        _ = m.renderInterleaved(frames: 64) // mikseren slipper stemmen, så kilden frigjøres
        _ = m.renderInterleaved(frames: 64)
        XCTAssertTrue(released)
    }

    // MARK: Showfil

    func testNewCueTypesRoundTrip() throws {
        let t = Cue(type: .timecode); t.tcStart = "10:00:00:00"; t.rate = .fps2997Drop; t.sendLtc = true; t.ltcBus = 8; t.length = 60
        let l = Cue(type: .light); l.host = "2.255.255.255"; l.universe = 3; l.levels = "1-4@50%"; l.fadeTime = 5
        let i = Cue(type: .input); i.fileChannels = 2; i.volumeDb = -3; i.fadeOut = 2
        let a = Cue(type: .audio); a.filePath = "a.wav"; a.timecodeTrigger = "10:00:05:00"
        var doc = ShowDocument()
        doc.cues = [t, l, i, a]
        let data = try doc.jsonData()
        let json = String(decoding: data, as: UTF8.self)
        for k in ["timecode", "light", "input"] { XCTAssertTrue(json.contains("\"type\" : \"\(k)\""), k) }
        XCTAssertTrue(json.contains("\"Rate\" : \"Fps2997Drop\""))
        let back = try JSONDecoder().decode(ShowDocument.self, from: data)
        let bt = back.cues[0]
        XCTAssertTrue(bt.tcStart == "10:00:00:00" && bt.rate == .fps2997Drop && bt.sendLtc && bt.ltcBus == 8 && bt.length == 60 && bt.duration == 60)
        let bl = back.cues[1]
        XCTAssertTrue(bl.host == "2.255.255.255" && bl.universe == 3 && bl.levels == "1-4@50%" && bl.fadeTime == 5 && bl.duration == 5)
        XCTAssertTrue(back.cues[2].fileChannels == 2 && back.cues[2].volumeDb == -3 && back.cues[2].fadeOut == 2)
        XCTAssertEqual(back.cues[3].timecodeTrigger, "10:00:05:00")
        XCTAssertTrue(back.cues[3].triggerText.contains("TC 10:00:05:00"))
        XCTAssertEqual(try back.jsonData(), data)
        XCTAssertEqual(Cue(type: .light).host, "255.255.255.255")
    }

    func testReadsWindowsTimecodeLightAndInput() throws {
        let json = """
        {"FormatVersion":1,"Cues":[
          {"type":"timecode","Number":"1","TimecodeTrigger":"","Start":"01:00:00:00","Rate":"Fps25","SendMtc":true,"Device":"IAC","SendLtc":false,"LtcBus":1,"LtcLevelDb":-12,"Length":0},
          {"type":"light","Number":"2","Host":"10.0.0.50","Universe":1,"Levels":"1@255","FadeTime":3},
          {"type":"input","Number":"3","FileChannels":1,"Routes":null,"VolumeDb":0,"FadeIn":0.5,"FadeOut":1},
          {"type":"audio","Number":"4","FilePath":"a.wav","TimecodeTrigger":"01:00:10:00"}
        ]}
        """
        let doc = try JSONDecoder().decode(ShowDocument.self, from: Data(json.utf8))
        XCTAssertEqual(doc.cues.map(\.type), [.timecode, .light, .input, .audio])
        XCTAssertEqual(doc.cues[0].device, "IAC")
        XCTAssertEqual(doc.cues[1].host, "10.0.0.50")
        XCTAssertEqual(doc.cues[1].fadeTime, 3)
        XCTAssertEqual(doc.cues[2].fileChannels, 1)
        XCTAssertEqual(doc.cues[3].timecodeTrigger, "01:00:10:00")
    }
}
