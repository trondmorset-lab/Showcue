import XCTest
@testable import ShowCue

/// Tolking av HTTP-forespørsler: gyldige, ufullstendige, feil og for store. Samme tilfeller som i Windows-versjonen.
final class WebParserTests: XCTestCase {
    private func parse(_ s: String) -> WebParseResult {
        let b = Array(s.utf8)
        return WebRemote.parse(b, length: b.count)
    }

    private let ok = "GET / HTTP/1.1\r\nHost: a\r\n\r\n"

    func testParsesSimpleGet() {
        let r = parse("GET /api/state?x=1 HTTP/1.1\r\nHost: a\r\nCookie: sc_session=abc\r\n\r\n")
        XCTAssertEqual(r.kind, .ok)
        XCTAssertEqual(r.request?.method, "GET")
        XCTAssertEqual(r.request?.path, "/api/state")
        XCTAssertEqual(r.request?.headers["cookie"], "sc_session=abc")
    }

    func testIncompleteWithoutTerminator() {
        XCTAssertEqual(parse("GET / HTTP/1.1\r\nHost: a\r\n").kind, .incomplete)
        XCTAssertEqual(parse("GET / HT").kind, .incomplete)
        XCTAssertEqual(parse("").kind, .incomplete)
    }

    func testRejectsMissingHost() { XCTAssertEqual(parse("GET / HTTP/1.1\r\n\r\n").status, 400) }

    func testRejectsOtherVersions() {
        XCTAssertEqual(parse("GET / HTTP/1.0\r\nHost: a\r\n\r\n").status, 505)
        XCTAssertEqual(parse("GET / HTTP/2.0\r\nHost: a\r\n\r\n").status, 505)
        XCTAssertEqual(parse("GET / FOO/1.1\r\nHost: a\r\n\r\n").status, 400)
        XCTAssertEqual(parse("GET /\r\nHost: a\r\n\r\n").status, 400)
    }

    func testRejectsOtherMethods() {
        for m in ["PUT", "DELETE", "OPTIONS", "HEAD", "PATCH", "CONNECT"] {
            XCTAssertEqual(parse("\(m) / HTTP/1.1\r\nHost: a\r\n\r\n").status, 405, m)
        }
        XCTAssertEqual(parse("get / HTTP/1.1\r\nHost: a\r\n\r\n").status, 400)
    }

    func testRejectsLongRequestLine() {
        XCTAssertEqual(parse("GET /" + String(repeating: "a", count: 3000) + " HTTP/1.1\r\nHost: a\r\n\r\n").status, 414)
        XCTAssertEqual(parse("GET /" + String(repeating: "a", count: 3000)).status, 414)
    }

    func testRejectsTooManyOrTooLargeHeaders() {
        var many = "GET / HTTP/1.1\r\nHost: a\r\n"
        for i in 0..<40 { many += "X-H\(i): v\r\n" }
        many += "\r\n"
        XCTAssertEqual(parse(many).status, 431)

        XCTAssertEqual(parse("GET / HTTP/1.1\r\nHost: a\r\nX-Big: " + String(repeating: "a", count: 9000) + "\r\n\r\n").status, 431)
        // Hodene aldri avsluttet og bufferen full
        XCTAssertEqual(parse("GET / HTTP/1.1\r\nHost: a\r\nX-Big: " + String(repeating: "a", count: 9000)).status, 431)
    }

    func testRejectsHugeBody() {
        XCTAssertEqual(parse("POST /api/cmd HTTP/1.1\r\nHost: a\r\nContent-Length: 5000\r\n\r\n").status, 413)
    }

    func testRejectsBadContentLength() {
        for v in ["abc", "-1", "1 2", "", "99999999999", "+5", "0x10"] {
            XCTAssertEqual(parse("POST /api/cmd HTTP/1.1\r\nHost: a\r\nContent-Length: \(v)\r\n\r\n").status, 400, v)
        }
        XCTAssertEqual(parse("POST /api/cmd HTTP/1.1\r\nHost: a\r\nContent-Length: 3\r\nContent-Length: 3\r\n\r\nabc").status, 400)
        XCTAssertEqual(parse("GET / HTTP/1.1\r\nHost: a\r\nContent-Length: 3\r\n\r\nabc").status, 400)
    }

    func testRejectsChunkedAndDuplicateHeaders() {
        XCTAssertEqual(parse("POST /api/cmd HTTP/1.1\r\nHost: a\r\nTransfer-Encoding: chunked\r\n\r\n").status, 400)
        XCTAssertEqual(parse("GET / HTTP/1.1\r\nHost: a\r\nHost: b\r\n\r\n").status, 400)
    }

    func testRejectsLooseLineFeedsNulAndNonAscii() {
        XCTAssertEqual(parse("GET / HTTP/1.1\nHost: a\r\n\r\n").status, 400)
        XCTAssertEqual(parse("GET / HTTP/1.1\r\nHost: a\nX: y\r\n\r\n").status, 400)
        XCTAssertEqual(parse("GET / HTTP/1.1\r\nHost: a\rX: y\r\n\r\n").status, 400)
        XCTAssertEqual(parse("GET / HTTP/1.1\r\nHost: a\0b\r\n\r\n").status, 400)
        let all: [UInt8] = Array("GET /".utf8) + [0xC3, 0xA6] + Array(" HTTP/1.1\r\nHost: a\r\n\r\n".utf8)
        XCTAssertEqual(WebRemote.parse(all, length: all.count).status, 400)
    }

    func testRejectsPathTraversalAndEncodedPaths() {
        for path in ["/../etc/passwd", "/a/../b", "/..", "/%2e%2e/x", "/..%2f", "/a%2Fb", "/a\\b", "/x#y", "/..\\..\\windows"] {
            XCTAssertEqual(parse("GET \(path) HTTP/1.1\r\nHost: a\r\n\r\n").status, 400, path)
        }
        XCTAssertEqual(parse("GET relative HTTP/1.1\r\nHost: a\r\n\r\n").status, 400)
        XCTAssertEqual(parse("GET /a b HTTP/1.1\r\nHost: a\r\n\r\n").status, 400)
        XCTAssertEqual(parse("GET  / HTTP/1.1\r\nHost: a\r\n\r\n").status, 400)
    }

    func testRejectsFoldedAndBrokenHeaders() {
        XCTAssertEqual(parse("GET / HTTP/1.1\r\nHost: a\r\n folded\r\n\r\n").status, 400)
        XCTAssertEqual(parse("GET / HTTP/1.1\r\nHost a\r\n\r\n").status, 400)
        XCTAssertEqual(parse("GET / HTTP/1.1\r\n: x\r\nHost: a\r\n\r\n").status, 400)
        XCTAssertEqual(parse("GET / HTTP/1.1\r\nBad Name: x\r\nHost: a\r\n\r\n").status, 400)
    }

    func testPostNeedsLengthAndWaitsForBody() {
        XCTAssertEqual(parse("POST /api/cmd HTTP/1.1\r\nHost: a\r\n\r\n").status, 411)
        XCTAssertEqual(parse("POST /api/cmd HTTP/1.1\r\nHost: a\r\nContent-Length: 6\r\n\r\ncmd=").kind, .incomplete)
        let done = parse("POST /api/cmd HTTP/1.1\r\nHost: a\r\nContent-Length: 6\r\n\r\ncmd=go EXTRA")
        XCTAssertEqual(done.kind, .ok)
        XCTAssertEqual(String(decoding: done.request?.body ?? [], as: UTF8.self), "cmd=go")
    }

    func testSmallFuzzNeverCrashes() {
        var g = SystemRandomNumberGenerator()
        for _ in 0..<5000 {
            let n = Int.random(in: 0..<400, using: &g)
            let b = (0..<n).map { _ in UInt8.random(in: 0...255, using: &g) }
            _ = WebRemote.parse(b, length: Int.random(in: 0...(n + 2), using: &g))
        }
        XCTAssertEqual(parse(ok).kind, .ok)
    }
}

/// Ruter, PIN, sperre og svar. Samme tilfeller som i Windows-versjonen.
final class WebRemoteCoreTests: XCTestCase {
    private let pin = "483920"
    private var nowMs: Int64 = 1_000_000
    private var counter = 0
    private var core: WebRemoteCore!

    override func setUp() {
        super.setUp()
        core = try! WebRemoteCore(pin: pin, loginPage: Array("LOGIN-PAGE".utf8), controlPage: Array("CONTROL-PAGE".utf8),
                                  now: { [unowned self] in self.nowMs },
                                  random: { [unowned self] n in
                                      var b = [UInt8](repeating: 0, count: n)
                                      self.counter += 1
                                      b[0] = UInt8(self.counter & 0xFF); b[1] = UInt8((self.counter >> 8) & 0xFF); b[2] = 0x5A
                                      return b
                                  })
        core.setState("{\"state\":1}", cues: "{\"cues\":[]}")
    }

    private func req(_ method: String, _ path: String, body: String = "", headers: [String] = []) -> [UInt8] {
        var s = "\(method) \(path) HTTP/1.1\r\nHost: a\r\n"
        for h in headers { s += h + "\r\n" }
        if method == "POST" { s += "Content-Length: \(body.utf8.count)\r\n" }
        s += "\r\n" + body
        return Array(s.utf8)
    }

    private func send(_ bytes: [UInt8], client: String = "10.0.0.1") -> WebReply { core.handle(bytes, length: bytes.count, client: client) }
    private func send(_ method: String, _ path: String, body: String = "", client: String = "10.0.0.1", headers: [String] = []) -> WebReply {
        send(req(method, path, body: body, headers: headers), client: client)
    }

    private func text(_ r: WebReply) -> String { String(decoding: r.response, as: UTF8.self) }
    private func status(_ r: WebReply) -> Int { Int(text(r).dropFirst(9).prefix(3)) ?? -1 }
    private func body(_ r: WebReply) -> String {
        let t = text(r)
        guard let range = t.range(of: "\r\n\r\n") else { return "" }
        return String(t[range.upperBound...])
    }
    private func header(_ r: WebReply, _ name: String) -> String? {
        for line in text(r).components(separatedBy: "\r\n").dropFirst() {
            if line.isEmpty { return nil }
            guard let c = line.range(of: ": ") else { continue }
            if line[line.startIndex..<c.lowerBound].lowercased() == name.lowercased() { return String(line[c.upperBound...]) }
        }
        return nil
    }

    private func login(_ client: String = "10.0.0.1") -> String {
        let r = send("POST", "/api/login", body: "pin=" + pin, client: client)
        XCTAssertEqual(status(r), 200)
        return String((header(r, "Set-Cookie") ?? "").split(separator: ";").first ?? "")
    }

    func testLoginPageIsShownWithoutPin() {
        let r = send("GET", "/")
        XCTAssertEqual(status(r), 200)
        XCTAssertEqual(body(r), "LOGIN-PAGE")
        XCTAssertNil(header(r, "Set-Cookie"))
    }

    func testEverythingElseNeedsPin() {
        for path in ["/api/state", "/api/cues"] {
            let r = send("GET", path)
            XCTAssertEqual(status(r), 401)
            XCTAssertFalse(body(r).contains("state\":1"))
        }
        let cmd = send("POST", "/api/cmd", body: "cmd=go", headers: ["X-ShowCue: 1"])
        XCTAssertEqual(status(cmd), 401)
        XCTAssertNil(cmd.command)
    }

    func testWrongPinIsRejected() {
        let r = send("POST", "/api/login", body: "pin=000111")
        XCTAssertEqual(status(r), 401)
        XCTAssertNil(header(r, "Set-Cookie"))
        XCTAssertEqual(status(send("POST", "/api/login", body: "pin=")), 401)
        XCTAssertEqual(status(send("POST", "/api/login", body: "pin=" + pin + "0")), 401)
        XCTAssertEqual(status(send("POST", "/api/login", body: "x=" + pin)), 400)
    }

    func testCorrectPinGivesCookieThatWorks() {
        let cookie = login()
        let set = header(send("POST", "/api/login", body: "pin=" + pin), "Set-Cookie") ?? ""
        XCTAssertTrue(set.contains("HttpOnly"))
        XCTAssertTrue(set.contains("SameSite=Strict"))
        let state = send("GET", "/api/state", headers: ["Cookie: " + cookie])
        XCTAssertEqual(status(state), 200)
        XCTAssertEqual(body(state), "{\"state\":1}")
        XCTAssertEqual(body(send("GET", "/api/cues", headers: ["Cookie: other=1; " + cookie])), "{\"cues\":[]}")
        XCTAssertEqual(body(send("GET", "/", headers: ["Cookie: " + cookie])), "CONTROL-PAGE")
    }

    func testForgedCookiesAreRejected() {
        _ = login()
        let forged = ["sc_session=", "sc_session=0000", "sc_session=" + String(repeating: "a", count: 32),
                      "sc_session=" + String(repeating: "A", count: 32), "sc_session=" + String(repeating: "a", count: 33), "x=1"]
        for c in forged { XCTAssertEqual(status(send("GET", "/api/state", headers: ["Cookie: " + c])), 401, c) }
    }

    func testSessionsExpireAndAreCapped() {
        let first = login()
        nowMs += WebRemoteCore.sessionLifetimeMs + 1
        XCTAssertEqual(status(send("GET", "/api/state", headers: ["Cookie: " + first])), 401)
        let old = login()
        for _ in 0..<(WebRemoteCore.maxSessions + 3) { _ = login() }
        XCTAssertEqual(status(send("GET", "/api/state", headers: ["Cookie: " + old])), 401)
    }

    func testLockoutAfterFiveFailuresThenReleases() {
        for _ in 0..<WebRemoteCore.maxFailures { XCTAssertEqual(status(send("POST", "/api/login", body: "pin=999111")), 401) }
        let locked = send("POST", "/api/login", body: "pin=" + pin) // riktig PIN hjelper ikke under sperre
        XCTAssertEqual(status(locked), 429)
        XCTAssertNotNil(header(locked, "Retry-After"))
        XCTAssertNil(header(locked, "Set-Cookie"))
        XCTAssertEqual(status(send("POST", "/api/login", body: "pin=" + pin, client: "10.0.0.2")), 200) // andre klienter påvirkes ikke
        nowMs += WebRemoteCore.lockoutMs + 1
        XCTAssertEqual(status(send("POST", "/api/login", body: "pin=" + pin)), 200)
    }

    func testExistingSessionWorksDuringLockout() {
        let cookie = login("10.0.0.9")
        for _ in 0..<WebRemoteCore.maxFailures { _ = send("POST", "/api/login", body: "pin=999111", client: "10.0.0.9") }
        XCTAssertEqual(status(send("POST", "/api/login", body: "pin=" + pin, client: "10.0.0.9")), 429)
        XCTAssertEqual(status(send("GET", "/api/state", client: "10.0.0.9", headers: ["Cookie: " + cookie])), 200)
    }

    func testGlobalLockoutStopsManyClients() {
        for i in 0..<WebRemoteCore.globalMaxFailures { _ = send("POST", "/api/login", body: "pin=999111", client: "10.1.0.\(i)") }
        XCTAssertEqual(status(send("POST", "/api/login", body: "pin=" + pin, client: "10.9.9.9")), 429)
        nowMs += WebRemoteCore.globalLockoutMs + 1
        XCTAssertEqual(status(send("POST", "/api/login", body: "pin=" + pin, client: "10.9.9.9")), 200)
    }

    func testPinInHeaderWorksAndCountsFailures() {
        XCTAssertEqual(status(send("GET", "/api/state", client: "10.0.0.3", headers: ["X-ShowCue-Pin: " + pin])), 200)
        for _ in 0..<WebRemoteCore.maxFailures {
            XCTAssertEqual(status(send("GET", "/api/state", client: "10.0.0.3", headers: ["X-ShowCue-Pin: 111222"])), 401)
        }
        XCTAssertEqual(status(send("GET", "/api/state", client: "10.0.0.3", headers: ["X-ShowCue-Pin: " + pin])), 429)
    }

    func testCommandsMapAndNeedHeader() {
        let cookie = "Cookie: " + login()
        let map: [(String, WebCommand)] = [("go", .go), ("stop", .stopAll), ("pause", .pause), ("resume", .resume), ("next", .next), ("previous", .previous)]
        for (word, cmd) in map {
            let r = send("POST", "/api/cmd", body: "cmd=" + word, headers: [cookie, "X-ShowCue: 1"])
            XCTAssertEqual(status(r), 200, word)
            XCTAssertEqual(r.command, cmd, word)
        }
        let noHeader = send("POST", "/api/cmd", body: "cmd=go", headers: [cookie])
        XCTAssertEqual(status(noHeader), 403)
        XCTAssertNil(noHeader.command)
    }

    func testUnknownCommandsAreRejected() {
        let cookie = "Cookie: " + login()
        for b in ["", "cmd=", "cmd=GO", "cmd=go&x=1", "cmd=edit", "cmd=delete", "go", "cmd=go\r\n", "cmd=" + String(repeating: "a", count: 100)] {
            let r = send("POST", "/api/cmd", body: b, headers: [cookie, "X-ShowCue: 1"])
            XCTAssertEqual(status(r), 400, b)
            XCTAssertNil(r.command)
        }
    }

    func testOnlyFixedRoutesExist() {
        let cookie = "Cookie: " + login()
        for path in ["/index.html", "/etc/passwd", "/api/", "/api/state/", "/api", "/api/edit", "/api/save", "/static/app.js", "/favicon.ico", "//", "/API/state"] {
            XCTAssertEqual(status(send("GET", path, headers: [cookie])), 404, path)
        }
        XCTAssertEqual(status(send("GET", "/etc/passwd")), 404)
        XCTAssertEqual(status(send("GET", "/static/../secret", headers: [cookie])), 400)
    }

    func testWrongMethodsAreRejected() {
        let cookie = "Cookie: " + login()
        XCTAssertEqual(status(send("POST", "/api/state", body: "x", headers: [cookie])), 405)
        XCTAssertEqual(status(send("POST", "/api/cues", body: "x", headers: [cookie])), 405)
        XCTAssertEqual(status(send("GET", "/api/cmd", headers: [cookie])), 405)
        XCTAssertEqual(status(send("GET", "/api/login")), 405)
        XCTAssertEqual(status(send("POST", "/", body: "x")), 405)
        XCTAssertEqual(status(send("OPTIONS", "/api/state")), 405)
    }

    func testResponsesAreClosedAndHaveNoCors() {
        let cookie = "Cookie: " + login()
        let replies = [send("GET", "/"), send("GET", "/api/state"), send("GET", "/nope"), send("GET", "/api/state", headers: [cookie]), send("OPTIONS", "/")]
        for r in replies {
            let t = text(r)
            XCTAssertTrue(t.hasPrefix("HTTP/1.1 "))
            XCTAssertTrue(t.contains("Connection: close\r\n"))
            XCTAssertTrue(t.contains("Cache-Control: no-store\r\n"))
            XCTAssertTrue(t.contains("Content-Security-Policy: default-src 'none'"))
            XCTAssertFalse(t.lowercased().contains("access-control"))
            XCTAssertEqual(Int(header(r, "Content-Length") ?? ""), body(r).utf8.count)
        }
    }

    func testOversizeAndMalformedInputGetsErrorStatus() {
        let huge = [UInt8](repeating: 65, count: WebRemote.maxRequestBytes + 10)
        XCTAssertEqual(status(send(huge)), 414)
        XCTAssertEqual(status(send(Array(("GET / HTTP/1.1\r\nHost: a\r\nX: " + String(repeating: "a", count: 9000)).utf8))), 431)
        XCTAssertEqual(status(send(Array("\r\n\r\n".utf8))), 400)
        XCTAssertFalse(send(Array("GET / HTT".utf8)).complete)
        XCTAssertEqual(status(send("POST", "/api/login", body: String(repeating: "x", count: 2000))), 413)
    }

    func testConstantTimeEqualsCases() {
        XCTAssertTrue(WebRemote.constantTimeEquals("abc123", "abc123"))
        XCTAssertTrue(WebRemote.constantTimeEquals("", ""))
        XCTAssertFalse(WebRemote.constantTimeEquals("abc123", "abc124"))
        XCTAssertFalse(WebRemote.constantTimeEquals("abc123", "abc12"))
        XCTAssertFalse(WebRemote.constantTimeEquals("abc12", "abc123"))
        XCTAssertFalse(WebRemote.constantTimeEquals("", "a"))
        let long = String(repeating: "a", count: 100)
        XCTAssertFalse(WebRemote.constantTimeEquals(long, long))
    }

    func testWeakAndMalformedPinsAreNotAccepted() {
        for p in ["", "12345", "123456", "000000", "111111", "aaaaaaaa", "password", "Passord", "654321", "12 3456", "abc-123456", "æøå123456",
                  String(repeating: "a", count: 33) + "b"] {
            XCTAssertFalse(WebRemote.isAcceptablePin(p), p)
        }
        for p in ["483920", "abcdef12", "K7mQ2xP9", String(repeating: "a", count: 31) + "b"] {
            XCTAssertTrue(WebRemote.isAcceptablePin(p), p)
        }
        XCTAssertFalse(WebRemote.isAcceptablePin(nil))
        XCTAssertThrowsError(try WebRemoteCore(pin: "123456", loginPage: [], controlPage: [], now: { 0 }))
        XCTAssertThrowsError(try WebRemoteCore(pin: "", loginPage: [], controlPage: [], now: { 0 }))
    }

    func testGeneratedPinIsAcceptable() {
        for _ in 0..<200 {
            let p = WebRemote.generatePin()
            XCTAssertEqual(p.count, 6)
            XCTAssertTrue(WebRemote.isAcceptablePin(p), p)
        }
        // Tilfeldighet som gir lette koder hoppes over: først 000000 og 123456, så en vanlig verdi.
        func bytes(_ v: UInt32) -> [UInt8] { [UInt8(v & 0xFF), UInt8((v >> 8) & 0xFF), UInt8((v >> 16) & 0xFF), UInt8((v >> 24) & 0xFF)] }
        var seq = [bytes(0), bytes(123456), bytes(739518)]
        XCTAssertEqual(WebRemote.generatePin(random: { _ in seq.removeFirst() }), "739518")
    }

    func testNormalizePinKeepsGoodAndReplacesBad() {
        XCTAssertEqual(WebRemote.normalizePin(" 483920 ", enabled: true), "483920")
        XCTAssertTrue(WebRemote.isAcceptablePin(WebRemote.normalizePin("", enabled: true)))
        XCTAssertTrue(WebRemote.isAcceptablePin(WebRemote.normalizePin(nil, enabled: true)))
        XCTAssertTrue(WebRemote.isAcceptablePin(WebRemote.normalizePin("123456", enabled: true)))
        XCTAssertEqual(WebRemote.normalizePin("", enabled: false), "")
        XCTAssertEqual(WebRemote.normalizePin("123456", enabled: false), "")
    }

    func testSettingsAreNormalisedOnLoad() throws {
        var s = AppSettings()
        s.webRemoteEnabled = true; s.webRemotePin = ""; s.webRemotePort = 80
        s.normalize()
        XCTAssertTrue(WebRemote.isAcceptablePin(s.webRemotePin))
        XCTAssertEqual(s.webRemotePort, WebRemote.defaultPort)

        var off = AppSettings()
        off.normalize()
        XCTAssertFalse(off.webRemoteEnabled)
        XCTAssertEqual(off.webRemotePin, "")
        XCTAssertEqual(off.webRemotePort, 8088)

        var kept = AppSettings()
        kept.webRemoteEnabled = true; kept.webRemotePin = "K7mQ2xP9"; kept.webRemotePort = 9090
        kept.normalize()
        XCTAssertEqual(kept.webRemotePin, "K7mQ2xP9")
        XCTAssertEqual(kept.webRemotePort, 9090)

        var big = AppSettings()
        big.webRemotePort = 70000
        big.normalize()
        XCTAssertEqual(big.webRemotePort, 8088)

        // Fra en håndredigert fil
        let data = Data("{\"webRemoteEnabled\":true,\"webRemotePin\":\"123456\",\"webRemotePort\":22}".utf8)
        let loaded = try JSONDecoder().decode(AppSettings.self, from: data)
        XCTAssertTrue(WebRemote.isAcceptablePin(loaded.webRemotePin))
        XCTAssertNotEqual(loaded.webRemotePin, "123456")
        XCTAssertEqual(loaded.webRemotePort, 8088)
    }
}

/// JSON og sider.
final class WebRemoteContentTests: XCTestCase {
    func testJsonEscapeHandlesSpecialCharacters() {
        XCTAssertEqual(WebRemote.jsonEscape("a\"b\\c"), "a\\\"b\\\\c")
        XCTAssertEqual(WebRemote.jsonEscape("\n\t\0"), "\\u000a\\u0009\\u0000")
        XCTAssertEqual(WebRemote.jsonEscape("<script>&"), "\\u003cscript\\u003e\\u0026")
        XCTAssertEqual(WebRemote.jsonEscape("\u{2028}"), "\\u2028")
        XCTAssertEqual(WebRemote.jsonEscape("Æøå"), "Æøå")
    }

    func testStateJsonHasNextAndPlaying() {
        let json = WebRemote.buildStateJson(WebStateInput(
            showName: "Show \"1\"", paused: true, showMode: true,
            next: WebNext(number: "3", name: "Intro <b>", type: "Lyd", notes: "Merk\nlinje"), position: 3, count: 20,
            playing: [WebPlaying(number: "2", name: "Musikk", type: "Lyd", elapsed: 12.34, remaining: 47.66, hasDuration: true, preWaiting: false),
                      WebPlaying(number: "4", name: "Venter", type: "Video", elapsed: 0, remaining: 0, hasDuration: false, preWaiting: true)]))
        XCTAssertEqual(json, "{\"v\":1,\"show\":\"Show \\\"1\\\"\",\"paused\":true,\"lock\":true,\"pos\":3,\"count\":20,"
            + "\"next\":{\"n\":\"3\",\"name\":\"Intro \\u003cb\\u003e\",\"type\":\"Lyd\",\"notes\":\"Merk\\u000alinje\"},"
            + "\"playing\":[{\"n\":\"2\",\"name\":\"Musikk\",\"type\":\"Lyd\",\"el\":12.3,\"rem\":47.7,\"dur\":true,\"pre\":false},"
            + "{\"n\":\"4\",\"name\":\"Venter\",\"type\":\"Video\",\"el\":0.0,\"rem\":0.0,\"dur\":false,\"pre\":true}]}")
    }

    func testStateJsonWithoutNextAndBadNumbers() {
        let json = WebRemote.buildStateJson(WebStateInput(
            showName: "", paused: false, showMode: false, next: nil, position: -5, count: -1,
            playing: [WebPlaying(number: "1", name: "x", type: "Lyd", elapsed: .nan, remaining: .infinity, hasDuration: true, preWaiting: false)]))
        XCTAssertTrue(json.contains("\"next\":null"))
        XCTAssertTrue(json.contains("\"pos\":0,\"count\":0"))
        XCTAssertTrue(json.contains("\"el\":0.0,\"rem\":0.0"))
    }

    func testCuesJsonIsLimitedAndEscaped() {
        let cues = (1...1500).map { WebCueInfo(number: String($0), name: "C\"\($0)", type: "Lyd", disabled: $0 % 2 == 0) }
        let json = WebRemote.buildCuesJson(cues)
        XCTAssertTrue(json.hasPrefix("{\"cues\":[{\"n\":\"1\",\"name\":\"C\\\"1\",\"type\":\"Lyd\",\"off\":false}"))
        XCTAssertEqual(json.components(separatedBy: "\"n\":").count - 1, WebRemote.maxListedCues)
        XCTAssertEqual(WebRemote.buildCuesJson([]), "{\"cues\":[]}")
        let longName = WebRemote.buildCuesJson([WebCueInfo(number: "1", name: String(repeating: "x", count: 500), type: "Lyd", disabled: false)])
        XCTAssertLessThan(longName.count, 200)
    }

    func testPagesAreSelfContained() {
        let texts = WebRemote.pageTexts(language: "nb")
        for page in [WebRemote.loginPage(texts), WebRemote.controlPage(texts)] {
            let html = String(decoding: page, as: UTF8.self)
            XCTAssertFalse(html.contains("{{"))
            XCTAssertFalse(html.contains("http://"))
            XCTAssertFalse(html.contains("https://"))
            XCTAssertFalse(html.contains("src="))
            XCTAssertFalse(html.contains("href="))
            XCTAssertFalse(html.contains("@import"))
            XCTAssertFalse(html.contains("innerHTML"))
            XCTAssertTrue(html.contains("<script>"))
        }
    }

    func testPageTextsAreEscaped() {
        let html = String(decoding: WebRemote.controlPage(["lang": "nb", "next": "<b>\"x\"&'"]), as: UTF8.self)
        XCTAssertTrue(html.contains("&lt;b&gt;&quot;x&quot;&amp;&#39;"))
        XCTAssertFalse(html.contains("<b>\"x\""))
    }

    func testUrlShowsFirstAddressOrLocalhost() {
        XCTAssertEqual(WebRemote.url(["192.168.1.20", "10.0.0.2"], port: 8088), "http://192.168.1.20:8088")
        XCTAssertEqual(WebRemote.url([], port: 9000), "http://localhost:9000")
    }
}
