import XCTest
import AVFoundation
import Network
@testable import ShowCue

/// Lydstyrke (LUFS), mediefiler (relink/collect), nettverks- og memo-cuer. Samme tester som i Windows-versjonen.
final class ToolsTests: XCTestCase {
    private var root: URL!

    override func setUp() {
        root = FileManager.default.temporaryDirectory.appendingPathComponent("showcue-tools-\(UUID().uuidString)", isDirectory: true)
        try? FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
    }

    /// Stereo WAV der verdien i hver ramme gis av `value`.
    private func wav(_ name: String, rate: Double, frames: Int, _ value: (Int) -> Float) throws -> URL {
        let url = root.appendingPathComponent(name)
        let fmt = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: rate, channels: 2, interleaved: false)!
        let buf = AVAudioPCMBuffer(pcmFormat: fmt, frameCapacity: AVAudioFrameCount(frames))!
        buf.frameLength = AVAudioFrameCount(frames)
        for i in 0..<frames { let v = value(i); buf.floatChannelData![0][i] = v; buf.floatChannelData![1][i] = v }
        // Egen blokk: fila må lukkes (AVAudioFile frigjøres) før den kan leses.
        do {
            let file = try AVAudioFile(forWriting: url, settings: fmt.settings, commonFormat: .pcmFormatFloat32, interleaved: false)
            try file.write(from: buf)
        }
        return url
    }

    private func sine(_ name: String, rate: Double, dbfs: Double, seconds: Double, silenceAfter: Double = 0) throws -> URL {
        let amp = pow(10, dbfs / 20), n = Int(rate * seconds)
        return try wav(name, rate: rate, frames: n + Int(rate * silenceAfter)) { f in
            f < n ? Float(amp * sin(2 * .pi * 1000 * Double(f) / rate)) : 0
        }
    }

    // MARK: Lydstyrke

    func testSineReadsAsEbuReference() throws {
        // EBU Tech 3341, test 1/2: stereo 1 kHz sinus på −23 dBFS = −23,0 LUFS (±0,1).
        for (rate, db) in [(48000.0, -23.0), (44100.0, -23.0), (48000.0, -33.0)] {
            let lufs = try XCTUnwrap(LoudnessMeter.measure(file: sine("sine\(Int(rate))\(Int(db)).wav", rate: rate, dbfs: db, seconds: 5)))
            XCTAssertEqual(lufs, db, accuracy: 0.1, "\(rate) Hz, \(db) dBFS")
        }
    }

    func testSilenceIsGatedAway() throws {
        let lufs = try XCTUnwrap(LoudnessMeter.measure(file: sine("gate.wav", rate: 48000, dbfs: -23, seconds: 5, silenceAfter: 5)))
        XCTAssertEqual(lufs, -23, accuracy: 0.2)
        XCTAssertNil(try LoudnessMeter.measure(file: wav("silent.wav", rate: 48000, frames: 96000) { _ in 0 }))
        XCTAssertNil(try LoudnessMeter.measure(file: sine("short.wav", rate: 48000, dbfs: -20, seconds: 0.3)), "kortere enn én blokk")
    }

    func testMeasuresOnlyBetweenStartAndEnd() throws {
        let url = try wav("twolevels.wav", rate: 48000, frames: 48000 * 6) { f in
            Float(pow(10, (f < 48000 * 3 ? -30 : -20) / 20.0) * sin(2 * .pi * 1000 * Double(f) / 48000))
        }
        XCTAssertEqual(try XCTUnwrap(LoudnessMeter.measure(file: url, start: 3)), -20, accuracy: 0.15)
        XCTAssertEqual(try XCTUnwrap(LoudnessMeter.measure(file: url, start: 0, end: 3)), -30, accuracy: 0.15)
    }

    func testGainForTarget() {
        XCTAssertEqual(LoudnessMeter.gain(for: -18, target: -23), -5)
        XCTAssertEqual(LoudnessMeter.gain(for: -30.5, target: -23), 7.5)
        XCTAssertEqual(LoudnessMeter.gain(for: -50, target: -23), 12)
    }

    // MARK: Mediefiler

    @discardableResult
    private func touch(_ dir: URL, _ name: String) -> String {
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let u = dir.appendingPathComponent(name)
        try? Data(name.utf8).write(to: u)
        return u.path
    }

    private func real(_ p: String) -> String { URL(fileURLWithPath: p).resolvingSymlinksInPath().path }

    private func cue(_ t: CueType, _ path: String = "") -> Cue { let c = Cue(type: t); c.filePath = path; return c }

    func testRelinkFindsMovedFilesInSubfolders() {
        let dir = root.appendingPathComponent("relink")
        let a = touch(dir.appendingPathComponent("lyd"), "intro.wav")
        let b = touch(dir.appendingPathComponent("bilder/akt1"), "Plakat.PNG")
        let t = touch(dir.appendingPathComponent("lyd"), "pause.mp3")
        let pl = Cue(type: .playlist); pl.files = ["E:\\pause.mp3", "E:\\finnes-ikke.mp3"]
        let cues = [cue(.audio, "D:\\Gammel\\intro.wav"), cue(.image, "/Users/x/Show/plakat.png"), pl, cue(.audio, a)]
        XCTAssertEqual(MediaTools.missing(cues).count, 4)
        XCTAssertEqual(MediaTools.relink(cues, in: dir), 3)
        XCTAssertEqual(real(cues[0].filePath), real(a))
        XCTAssertEqual(real(cues[1].filePath), real(b))
        XCTAssertEqual(real(pl.files[0]), real(t))
        XCTAssertEqual(pl.files[1], "E:\\finnes-ikke.mp3")
        XCTAssertEqual(MediaTools.missing(cues), ["E:\\finnes-ikke.mp3"])
    }

    func testCollectCopiesShowAndMedia() throws {
        let src = root.appendingPathComponent("collect-src")
        let a1 = touch(src.appendingPathComponent("a"), "lyd.wav")
        let a2 = touch(src.appendingPathComponent("b"), "lyd.wav")
        let img = touch(src, "bilde.png")
        let pl = Cue(type: .playlist); pl.files = [a1, src.appendingPathComponent("borte.mp3").path]
        var doc = ShowDocument()
        doc.cues = [cue(.audio, a1), cue(.audio, a2), cue(.image, img), pl]

        let target = root.appendingPathComponent("collect-out")
        let r = try MediaTools.collect(doc, to: target, name: "Revy")
        XCTAssertEqual(r.copied, 3)
        XCTAssertEqual(r.missing.count, 1)
        XCTAssertEqual(r.showURL.lastPathComponent, "Revy.showcue")
        XCTAssertEqual(doc.cues[0].filePath, a1, "originalen er uendret")

        let copy = try ShowDocument.load(from: r.showURL)
        let media = target.appendingPathComponent("Media")
        func name(_ p: String) -> String { URL(fileURLWithPath: p).standardizedFileURL.path }
        XCTAssertEqual(name(copy.cues[0].filePath), name(media.appendingPathComponent("lyd.wav").path))
        XCTAssertEqual(name(copy.cues[1].filePath), name(media.appendingPathComponent("lyd (2).wav").path))
        XCTAssertEqual(try String(contentsOf: media.appendingPathComponent("lyd (2).wav")), "lyd.wav")
        XCTAssertEqual(name(copy.cues[2].filePath), name(media.appendingPathComponent("bilde.png").path))
        XCTAssertEqual(name(copy.cues[3].files[0]), name(media.appendingPathComponent("lyd.wav").path))
    }

    func testLoadFindsPlaylistMediaNextToTheShow() throws {
        let dir = root.appendingPathComponent("load")
        let song = touch(dir.appendingPathComponent("Media"), "sang.mp3")
        let pl = Cue(type: .playlist); pl.files = ["C:\\USB\\sang.mp3"]
        var doc = ShowDocument()
        doc.cues = [pl]
        let url = dir.appendingPathComponent("Show.showcue")
        try doc.save(to: url)
        let back = try ShowDocument.load(from: url)
        XCTAssertEqual(URL(fileURLWithPath: back.cues[0].files[0]).standardizedFileURL.path, URL(fileURLWithPath: song).standardizedFileURL.path)
    }

    // MARK: Nettverk

    /// Verdi som deles med tilbakekall på andre tråder.
    private final class Box<T> {
        private let lock = NSLock()
        private var v: T
        init(_ v: T) { self.v = v }
        var value: T {
            get { lock.lock(); defer { lock.unlock() }; return v }
            set { lock.lock(); v = newValue; lock.unlock() }
        }
    }

    func testUnescape() {
        XCTAssertEqual(NetworkSender.unescape("%1A\\r\\n"), Data([0x25, 0x31, 0x41, 13, 10]))
        XCTAssertEqual(NetworkSender.unescape("\\x02x\\xff\\\\\\t\\0"), Data([0x02, 0x78, 0xFF, 0x5C, 9, 0]))
        XCTAssertEqual(NetworkSender.unescape("ø \\q \\x"), Data("ø \\q \\x".utf8))
    }

    /// Lokal server som tar imot én UDP-melding eller én TCP-forbindelse.
    private final class Server {
        let listener: NWListener
        private let queue = DispatchQueue(label: "test.server")
        var port: Int { Int(listener.port?.rawValue ?? 0) }

        init(udp: Bool, onConnection: @escaping (NWConnection) -> Void) throws {
            listener = try NWListener(using: udp ? .udp : .tcp, on: .any)
            let ready = DispatchSemaphore(value: 0)
            listener.stateUpdateHandler = { if case .ready = $0 { ready.signal() } }
            listener.newConnectionHandler = { [queue] c in c.start(queue: queue); onConnection(c) }
            listener.start(queue: queue)
            _ = ready.wait(timeout: .now() + 5)
        }
        deinit { listener.cancel() }
    }

    func testSendsUdp() async throws {
        let got = expectation(description: "udp")
        let text = Box("")
        let server = try Server(udp: true) { c in
            c.receiveMessage { data, _, _, _ in text.value = String(decoding: data ?? Data(), as: UTF8.self); got.fulfill() }
        }
        let err = await NetworkSender.send(protocol: .udp, host: "127.0.0.1", port: server.port, message: "GO\\r", url: "", method: "GET")
        XCTAssertNil(err)
        await fulfillment(of: [got], timeout: 5)
        XCTAssertEqual(text.value, "GO\r")
    }

    func testSendsTcp() async throws {
        let got = expectation(description: "tcp")
        let text = Box("")
        let server = try Server(udp: false) { c in
            c.receive(minimumIncompleteLength: 9, maximumLength: 64) { data, _, _, _ in
                text.value = String(decoding: data ?? Data(), as: UTF8.self); got.fulfill()
            }
        }
        let err = await NetworkSender.send(protocol: .tcp, host: "127.0.0.1", port: server.port, message: "%1POWR 0\\r", url: "", method: "GET")
        XCTAssertNil(err)
        await fulfillment(of: [got], timeout: 5)
        XCTAssertEqual(text.value, "%1POWR 0\r")
    }

    func testTcpReportsConnectionErrors() async throws {
        var port = 0
        do { let s = try Server(udp: false) { _ in }; port = s.port } // lukkes igjen: ingen lytter på porten
        try await Task.sleep(nanoseconds: 200_000_000)
        let err = await NetworkSender.send(protocol: .tcp, host: "127.0.0.1", port: port, message: "x", url: "", method: "GET", timeout: 2)
        XCTAssertNotNil(err)
    }

    /// Leser én HTTP-forespørsel og svarer med gitt status.
    private func serveHttp(_ c: NWConnection, status: String, _ done: @escaping (String) -> Void) {
        let acc = Box(Data())
        func read() {
            c.receive(minimumIncompleteLength: 1, maximumLength: 65536) { data, _, complete, err in
                if let data { acc.value.append(data) }
                let text = String(decoding: acc.value, as: UTF8.self)
                if let r = text.range(of: "\r\n\r\n") {
                    let head = text[..<r.lowerBound]
                    let len = head.split(separator: "\r\n").first { $0.lowercased().hasPrefix("content-length:") }
                        .flatMap { Int($0.split(separator: ":")[1].trimmingCharacters(in: .whitespaces)) } ?? 0
                    if text[r.upperBound...].utf8.count >= len {
                        let resp = "HTTP/1.1 \(status)\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"
                        c.send(content: Data(resp.utf8), completion: .contentProcessed { _ in c.cancel() })
                        done(text)
                        return
                    }
                }
                if complete || err != nil { done(text); return }
                read()
            }
        }
        read()
    }

    func testSendsHttp() async throws {
        let got = expectation(description: "http")
        let req = Box("")
        let server = try Server(udp: false) { [unowned self] c in self.serveHttp(c, status: "200 OK") { req.value = $0; got.fulfill() } }
        let err = await NetworkSender.send(protocol: .http, host: "", port: 0, message: "{\"on\":true}",
                                           url: "http://127.0.0.1:\(server.port)/api/scene?id=3", method: "post")
        XCTAssertNil(err)
        await fulfillment(of: [got], timeout: 5)
        XCTAssertTrue(req.value.hasPrefix("POST /api/scene?id=3 HTTP/1.1"), req.value)
        XCTAssertTrue(req.value.lowercased().contains("application/json"), req.value)
        XCTAssertTrue(req.value.hasSuffix("{\"on\":true}"), req.value)

        let got2 = expectation(description: "http2")
        let req2 = Box("")
        let server2 = try Server(udp: false) { [unowned self] c in self.serveHttp(c, status: "404 Not Found") { req2.value = $0; got2.fulfill() } }
        let err2 = await NetworkSender.send(protocol: .http, host: "", port: 0, message: "", url: "http://127.0.0.1:\(server2.port)/x", method: "GET")
        await fulfillment(of: [got2], timeout: 5)
        XCTAssertTrue(req2.value.hasPrefix("GET /x"), req2.value)
        XCTAssertEqual(err2, "HTTP 404 Not Found")

        let bad = await NetworkSender.send(protocol: .http, host: "", port: 0, message: "", url: "ftp://x", method: "GET")
        XCTAssertNotNil(bad)
    }

    // MARK: Nettside

    func testWebCueAddresses() throws {
        let c = Cue(type: .web)
        XCTAssertNil(c.webURL)
        c.url = "time.is"
        XCTAssertEqual(c.webURL?.absoluteString, "https://time.is")
        c.url = "https://time.is/Oslo"
        XCTAssertEqual(c.displayName, "time.is")
        c.url = "ftp://x.no"
        XCTAssertNil(c.webURL)
        c.url = touch(root.appendingPathComponent("web"), "klokke.html")
        XCTAssertTrue(c.webURL?.isFileURL ?? false)

        let w = Cue(type: .web); w.url = "http://10.0.0.5/score"; w.holdTime = 30; w.fadeIn = 1
        var doc = ShowDocument(); doc.cues = [w]
        let data = try doc.jsonData()
        XCTAssertTrue(String(decoding: data, as: UTF8.self).contains("\"type\" : \"web\""))
        let b = try JSONDecoder().decode(ShowDocument.self, from: data).cues[0]
        XCTAssertTrue(b.type == .web && b.url == "http://10.0.0.5/score" && b.holdTime == 30 && b.duration == 1 + 30 + 0.5)
        XCTAssertTrue(b.isVisual)
        XCTAssertTrue(MediaTools.mediaPaths([b]).isEmpty)

        // Fra Windows: WebCue har også et tomt FilePath-felt.
        let win = #"{"FormatVersion":1,"Cues":[{"type":"web","Number":"1","FilePath":"","Url":"https://example.com","HoldTime":0}]}"#
        XCTAssertEqual(try JSONDecoder().decode(ShowDocument.self, from: Data(win.utf8)).cues[0].url, "https://example.com")
    }

    // MARK: Showfil

    func testNewCueTypesRoundTrip() throws {
        let m = Cue(type: .memo); m.text = "Vent på applaus\nså GO"; m.inButtonPanel = true
        let n = Cue(type: .network); n.networkProtocol = .http; n.method = "PUT"; n.url = "http://10.0.0.5/x"; n.message = "a"
        let a = Cue(type: .audio); a.filePath = "a.wav"; a.loudness = -17.3
        var doc = ShowDocument()
        doc.cues = [m, n, a]
        let data = try doc.jsonData()
        let json = String(decoding: data, as: UTF8.self)
        XCTAssertTrue(json.contains("\"type\" : \"memo\""), json)
        XCTAssertTrue(json.contains("\"type\" : \"network\""))
        XCTAssertTrue(json.contains("\"Protocol\" : \"Http\""))
        let back = try JSONDecoder().decode(ShowDocument.self, from: data)
        XCTAssertTrue(back.cues[0].inButtonPanel)
        XCTAssertEqual(back.cues[0].displayName, "Vent på applaus")
        XCTAssertTrue(back.cues[1].networkProtocol == .http && back.cues[1].method == "PUT" && back.cues[1].url == "http://10.0.0.5/x"
                      && back.cues[1].message == "a")
        XCTAssertEqual(back.cues[2].loudness, -17.3)
        XCTAssertNil(Cue(type: .audio).loudness)
        XCTAssertEqual(Cue(type: .network).port, 9000)
        XCTAssertEqual(try back.jsonData(), data)
    }

    func testReadsWindowsNetworkAndMemo() throws {
        let json = """
        {"FormatVersion":1,"Cues":[
          {"type":"network","Number":"1","InButtonPanel":false,"Protocol":"Tcp","Host":"10.0.0.9","Port":4352,"Message":"%1POWR 1\\\\r","Url":"http://","Method":"GET"},
          {"type":"memo","Number":"2","InButtonPanel":true,"Text":"Pause"},
          {"type":"audio","Number":"3","FilePath":"a.wav","Loudness":null}
        ]}
        """
        let doc = try JSONDecoder().decode(ShowDocument.self, from: Data(json.utf8))
        XCTAssertEqual(doc.cues[0].networkProtocol, .tcp)
        XCTAssertEqual(doc.cues[0].port, 4352)
        XCTAssertEqual(doc.cues[0].message, "%1POWR 1\\r")
        XCTAssertEqual(doc.cues[1].text, "Pause")
        XCTAssertTrue(doc.cues[1].inButtonPanel)
        XCTAssertNil(doc.cues[2].loudness)
    }

    func testZoomStepsUpAndDownWithinLimits() {
        XCTAssertEqual(UIZoom.step(1.0, 1), 1.1)
        XCTAssertEqual(UIZoom.step(1.0, -1), 0.9)
        XCTAssertEqual(UIZoom.step(1.17, 1), 1.25)   // mellom to trinn: neste hele trinn
        XCTAssertEqual(UIZoom.step(2.0, 1), 2.0)
        XCTAssertEqual(UIZoom.step(0.8, -1), 0.8)
        XCTAssertEqual(UIZoom.clamp(5), 2.0)
        XCTAssertEqual(UIZoom.clamp(.nan), 1.0)
        XCTAssertEqual(UIZoom.clampList(0.5), 1.0)
        XCTAssertEqual(UIZoom.percent(1.25), "125 %")
    }

    func testLanguageFollowsSettingThenSystem() {
        XCTAssertEqual(Loc.resolve("", system: "nb-NO"), "nb")
        XCTAssertEqual(Loc.resolve("", system: "nn"), "nb")
        XCTAssertEqual(Loc.resolve("", system: "en-US"), "en")
        XCTAssertEqual(Loc.resolve("en", system: "nb-NO"), "en")
        XCTAssertEqual(Loc.resolve("nb", system: "en-US"), "nb")
    }

    func testEnglishTableIsComplete() throws {
        let url = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
            .appendingPathComponent("../../../i18n/en.json").standardizedFileURL
        let table = try JSONDecoder().decode([String: String].self, from: Data(contentsOf: url))
        XCTAssertEqual(table["Lagre"], "Save")
        XCTAssertTrue(table.values.allSatisfy { !$0.isEmpty })
    }
}
