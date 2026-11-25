using System.Text;
using ShowCue.Models;

namespace ShowCue.Tests;

/// <summary>Tolking av HTTP-forespørsler: gyldige, ufullstendige, feil og for store.</summary>
public class WebParserTests
{
    private static byte[] B(string s) => Encoding.ASCII.GetBytes(s);
    private static WebParseResult P(string s) { var b = B(s); return WebRemote.Parse(b, b.Length); }
    private const string Ok = "GET / HTTP/1.1\r\nHost: a\r\n\r\n";

    [Fact]
    public void ParsesSimpleGet()
    {
        var r = P("GET /api/state?x=1 HTTP/1.1\r\nHost: a\r\nCookie: sc_session=abc\r\n\r\n");
        Assert.Equal(WebParseKind.Ok, r.Kind);
        Assert.Equal("GET", r.Request!.Method);
        Assert.Equal("/api/state", r.Request.Path);
        Assert.Equal("sc_session=abc", r.Request.Headers["cookie"]);
    }

    [Fact]
    public void IncompleteWithoutTerminator()
    {
        Assert.Equal(WebParseKind.Incomplete, P("GET / HTTP/1.1\r\nHost: a\r\n").Kind);
        Assert.Equal(WebParseKind.Incomplete, P("GET / HT").Kind);
        Assert.Equal(WebParseKind.Incomplete, P("").Kind);
    }

    [Fact]
    public void RejectsMissingHost() => Assert.Equal(400, P("GET / HTTP/1.1\r\n\r\n").Status);

    [Fact]
    public void RejectsOtherVersions()
    {
        Assert.Equal(505, P("GET / HTTP/1.0\r\nHost: a\r\n\r\n").Status);
        Assert.Equal(505, P("GET / HTTP/2.0\r\nHost: a\r\n\r\n").Status);
        Assert.Equal(400, P("GET / FOO/1.1\r\nHost: a\r\n\r\n").Status);
        Assert.Equal(400, P("GET /\r\nHost: a\r\n\r\n").Status);
    }

    [Fact]
    public void RejectsOtherMethods()
    {
        foreach (var m in new[] { "PUT", "DELETE", "OPTIONS", "HEAD", "PATCH", "CONNECT" })
            Assert.Equal(405, P($"{m} / HTTP/1.1\r\nHost: a\r\n\r\n").Status);
        Assert.Equal(400, P("get / HTTP/1.1\r\nHost: a\r\n\r\n").Status);
    }

    [Fact]
    public void RejectsLongRequestLine()
    {
        Assert.Equal(414, P("GET /" + new string('a', 3000) + " HTTP/1.1\r\nHost: a\r\n\r\n").Status);
        Assert.Equal(414, P("GET /" + new string('a', 3000)).Status);
    }

    [Fact]
    public void RejectsTooManyOrTooLargeHeaders()
    {
        var many = new StringBuilder("GET / HTTP/1.1\r\nHost: a\r\n");
        for (int i = 0; i < 40; i++) many.Append($"X-H{i}: v\r\n");
        many.Append("\r\n");
        Assert.Equal(431, P(many.ToString()).Status);

        var big = "GET / HTTP/1.1\r\nHost: a\r\nX-Big: " + new string('a', 9000) + "\r\n\r\n";
        Assert.Equal(431, P(big).Status);
        // Hodene aldri avsluttet og bufferen full
        Assert.Equal(431, P("GET / HTTP/1.1\r\nHost: a\r\nX-Big: " + new string('a', 9000)).Status);
    }

    [Fact]
    public void RejectsHugeBody() =>
        Assert.Equal(413, P("POST /api/cmd HTTP/1.1\r\nHost: a\r\nContent-Length: 5000\r\n\r\n").Status);

    [Fact]
    public void RejectsBadContentLength()
    {
        foreach (var v in new[] { "abc", "-1", "1 2", "", "99999999999", "+5", "0x10" })
            Assert.Equal(400, P($"POST /api/cmd HTTP/1.1\r\nHost: a\r\nContent-Length: {v}\r\n\r\n").Status);
        Assert.Equal(400, P("POST /api/cmd HTTP/1.1\r\nHost: a\r\nContent-Length: 3\r\nContent-Length: 3\r\n\r\nabc").Status);
        Assert.Equal(400, P("GET / HTTP/1.1\r\nHost: a\r\nContent-Length: 3\r\n\r\nabc").Status);
    }

    [Fact]
    public void RejectsChunkedAndDuplicateHeaders()
    {
        Assert.Equal(400, P("POST /api/cmd HTTP/1.1\r\nHost: a\r\nTransfer-Encoding: chunked\r\n\r\n").Status);
        Assert.Equal(400, P("GET / HTTP/1.1\r\nHost: a\r\nHost: b\r\n\r\n").Status);
    }

    [Fact]
    public void RejectsLooseLineFeedsNulAndNonAscii()
    {
        Assert.Equal(400, P("GET / HTTP/1.1\nHost: a\r\n\r\n").Status);
        Assert.Equal(400, P("GET / HTTP/1.1\r\nHost: a\nX: y\r\n\r\n").Status);
        Assert.Equal(400, P("GET / HTTP/1.1\r\nHost: a\rX: y\r\n\r\n").Status);
        Assert.Equal(400, P("GET / HTTP/1.1\r\nHost: a\0b\r\n\r\n").Status);
        var raw = new byte[] { (byte)'G', (byte)'E', (byte)'T', (byte)' ', (byte)'/', 0xC3, 0xA6 };
        var all = raw.Concat(B(" HTTP/1.1\r\nHost: a\r\n\r\n")).ToArray();
        Assert.Equal(400, WebRemote.Parse(all, all.Length).Status);
    }

    [Fact]
    public void RejectsPathTraversalAndEncodedPaths()
    {
        foreach (var path in new[] { "/../etc/passwd", "/a/../b", "/..", "/%2e%2e/x", "/..%2f", "/a%2Fb", "/a\\b", "/x#y", "/..\\..\\windows" })
            Assert.Equal(400, P($"GET {path} HTTP/1.1\r\nHost: a\r\n\r\n").Status);
        Assert.Equal(400, P("GET relative HTTP/1.1\r\nHost: a\r\n\r\n").Status);
        Assert.Equal(400, P("GET /a b HTTP/1.1\r\nHost: a\r\n\r\n").Status);
        Assert.Equal(400, P("GET  / HTTP/1.1\r\nHost: a\r\n\r\n").Status);
    }

    [Fact]
    public void RejectsFoldedAndBrokenHeaders()
    {
        Assert.Equal(400, P("GET / HTTP/1.1\r\nHost: a\r\n folded\r\n\r\n").Status);
        Assert.Equal(400, P("GET / HTTP/1.1\r\nHost a\r\n\r\n").Status);
        Assert.Equal(400, P("GET / HTTP/1.1\r\n: x\r\nHost: a\r\n\r\n").Status);
        Assert.Equal(400, P("GET / HTTP/1.1\r\nBad Name: x\r\nHost: a\r\n\r\n").Status);
    }

    [Fact]
    public void PostNeedsLengthAndWaitsForBody()
    {
        Assert.Equal(411, P("POST /api/cmd HTTP/1.1\r\nHost: a\r\n\r\n").Status);
        Assert.Equal(WebParseKind.Incomplete, P("POST /api/cmd HTTP/1.1\r\nHost: a\r\nContent-Length: 6\r\n\r\ncmd=").Kind);
        var done = P("POST /api/cmd HTTP/1.1\r\nHost: a\r\nContent-Length: 6\r\n\r\ncmd=go EXTRA");
        Assert.Equal(WebParseKind.Ok, done.Kind);
        Assert.Equal("cmd=go", Encoding.ASCII.GetString(done.Request!.Body));
    }

    [Fact]
    public void SmallFuzzNeverThrows()
    {
        var rng = new Random(7);
        for (int i = 0; i < 5000; i++)
        {
            var b = new byte[rng.Next(0, 400)];
            rng.NextBytes(b);
            var r = WebRemote.Parse(b, rng.Next(0, b.Length + 3));
            Assert.True(r.Kind is WebParseKind.Incomplete or WebParseKind.Error or WebParseKind.Ok);
        }
        Assert.Equal(WebParseKind.Ok, P(Ok).Kind);
    }
}

/// <summary>Ruter, PIN, sperre og svar.</summary>
public class WebRemoteCoreTests
{
    private const string Pin = "483920";
    private long _now = 1_000_000;
    private int _counter;
    private readonly WebRemoteCore _core;

    public WebRemoteCoreTests()
    {
        _core = new WebRemoteCore(Pin, Encoding.ASCII.GetBytes("LOGIN-PAGE"), Encoding.ASCII.GetBytes("CONTROL-PAGE"), () => _now, Random);
        _core.SetState("{\"state\":1}", "{\"cues\":[]}");
    }

    private byte[] Random(int n)
    {
        var b = new byte[n];
        var c = ++_counter;
        b[0] = (byte)c; b[1] = (byte)(c >> 8); b[2] = 0x5A;
        return b;
    }

    private static byte[] Req(string method, string path, string body = "", params string[] headers)
    {
        var sb = new StringBuilder($"{method} {path} HTTP/1.1\r\nHost: a\r\n");
        foreach (var h in headers) sb.Append(h).Append("\r\n");
        if (method == "POST") sb.Append("Content-Length: ").Append(Encoding.ASCII.GetByteCount(body)).Append("\r\n");
        sb.Append("\r\n").Append(body);
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private WebReply Send(byte[] req, string client = "10.0.0.1") => _core.Handle(req, req.Length, client);
    private WebReply Send(string method, string path, string body = "", string client = "10.0.0.1", params string[] headers) =>
        Send(Req(method, path, body, headers), client);

    private static string Text(WebReply r) => Encoding.UTF8.GetString(r.Response);
    private static int Status(WebReply r) => int.Parse(Text(r).Substring(9, 3));
    private static string Body(WebReply r) => Text(r).Substring(Text(r).IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4);
    private static string? Header(WebReply r, string name) =>
        Text(r).Split("\r\n").Skip(1).TakeWhile(l => l.Length > 0).Select(l => l.Split(": ", 2)).FirstOrDefault(p => p[0].Equals(name, StringComparison.OrdinalIgnoreCase))?[1];

    private string Login(string client = "10.0.0.1")
    {
        var r = Send("POST", "/api/login", "pin=" + Pin, client);
        Assert.Equal(200, Status(r));
        var cookie = Header(r, "Set-Cookie")!;
        return cookie.Split(';')[0];
    }

    [Fact]
    public void LoginPageIsShownWithoutPin()
    {
        var r = Send("GET", "/");
        Assert.Equal(200, Status(r));
        Assert.Equal("LOGIN-PAGE", Body(r));
        Assert.Null(Header(r, "Set-Cookie"));
    }

    [Fact]
    public void EverythingElseNeedsPin()
    {
        foreach (var path in new[] { "/api/state", "/api/cues" })
        {
            var r = Send("GET", path);
            Assert.Equal(401, Status(r));
            Assert.DoesNotContain("state", Body(r).Replace("auth", ""));
        }
        var cmd = Send("POST", "/api/cmd", "cmd=go", "10.0.0.1", "X-ShowCue: 1");
        Assert.Equal(401, Status(cmd));
        Assert.Null(cmd.Command);
    }

    [Fact]
    public void WrongPinIsRejected()
    {
        var r = Send("POST", "/api/login", "pin=000111");
        Assert.Equal(401, Status(r));
        Assert.Null(Header(r, "Set-Cookie"));
        Assert.Equal(401, Status(Send("POST", "/api/login", "pin=")));
        Assert.Equal(401, Status(Send("POST", "/api/login", "pin=" + Pin + "0")));
        Assert.Equal(400, Status(Send("POST", "/api/login", "x=" + Pin)));
    }

    [Fact]
    public void CorrectPinGivesCookieThatWorks()
    {
        var cookie = Login();
        var set = Header(Send("POST", "/api/login", "pin=" + Pin), "Set-Cookie")!;
        Assert.Contains("HttpOnly", set);
        Assert.Contains("SameSite=Strict", set);
        var state = Send("GET", "/api/state", "", "10.0.0.1", "Cookie: " + cookie);
        Assert.Equal(200, Status(state));
        Assert.Equal("{\"state\":1}", Body(state));
        Assert.Equal("{\"cues\":[]}", Body(Send("GET", "/api/cues", "", "10.0.0.1", "Cookie: other=1; " + cookie)));
        Assert.Equal("CONTROL-PAGE", Body(Send("GET", "/", "", "10.0.0.1", "Cookie: " + cookie)));
    }

    [Fact]
    public void ForgedCookiesAreRejected()
    {
        Login();
        foreach (var c in new[] { "sc_session=", "sc_session=0000", "sc_session=" + new string('a', 32), "sc_session=" + new string('A', 32), "sc_session=" + new string('a', 33), "x=1" })
            Assert.Equal(401, Status(Send("GET", "/api/state", "", "10.0.0.1", "Cookie: " + c)));
    }

    [Fact]
    public void SessionsExpireAndAreCapped()
    {
        var first = Login();
        _now += WebRemoteCore.SessionLifetimeMs + 1;
        Assert.Equal(401, Status(Send("GET", "/api/state", "", "10.0.0.1", "Cookie: " + first)));
        var old = Login();
        for (int i = 0; i < WebRemoteCore.MaxSessions + 3; i++) Login();
        Assert.Equal(401, Status(Send("GET", "/api/state", "", "10.0.0.1", "Cookie: " + old)));
    }

    [Fact]
    public void LockoutAfterFiveFailuresThenReleases()
    {
        for (int i = 0; i < WebRemoteCore.MaxFailures; i++)
            Assert.Equal(401, Status(Send("POST", "/api/login", "pin=999111")));
        var locked = Send("POST", "/api/login", "pin=" + Pin); // riktig PIN hjelper ikke under sperre
        Assert.Equal(429, Status(locked));
        Assert.NotNull(Header(locked, "Retry-After"));
        Assert.Null(Header(locked, "Set-Cookie"));
        Assert.Equal(200, Status(Send("POST", "/api/login", "pin=" + Pin, "10.0.0.2"))); // andre klienter påvirkes ikke
        _now += WebRemoteCore.LockoutMs + 1;
        Assert.Equal(200, Status(Send("POST", "/api/login", "pin=" + Pin)));
    }

    [Fact]
    public void ExistingSessionWorksDuringLockout()
    {
        var cookie = Login("10.0.0.9");
        for (int i = 0; i < WebRemoteCore.MaxFailures; i++) Send("POST", "/api/login", "pin=999111", "10.0.0.9");
        Assert.Equal(429, Status(Send("POST", "/api/login", "pin=" + Pin, "10.0.0.9")));
        Assert.Equal(200, Status(Send("GET", "/api/state", "", "10.0.0.9", "Cookie: " + cookie)));
    }

    [Fact]
    public void GlobalLockoutStopsManyClients()
    {
        for (int i = 0; i < WebRemoteCore.GlobalMaxFailures; i++)
            Send("POST", "/api/login", "pin=999111", "10.1.0." + i);
        Assert.Equal(429, Status(Send("POST", "/api/login", "pin=" + Pin, "10.9.9.9")));
        _now += WebRemoteCore.GlobalLockoutMs + 1;
        Assert.Equal(200, Status(Send("POST", "/api/login", "pin=" + Pin, "10.9.9.9")));
    }

    [Fact]
    public void PinInHeaderWorksAndCountsFailures()
    {
        Assert.Equal(200, Status(Send("GET", "/api/state", "", "10.0.0.3", "X-ShowCue-Pin: " + Pin)));
        for (int i = 0; i < WebRemoteCore.MaxFailures; i++)
            Assert.Equal(401, Status(Send("GET", "/api/state", "", "10.0.0.3", "X-ShowCue-Pin: 111222")));
        Assert.Equal(429, Status(Send("GET", "/api/state", "", "10.0.0.3", "X-ShowCue-Pin: " + Pin)));
    }

    [Fact]
    public void CommandsMapAndNeedHeader()
    {
        var cookie = "Cookie: " + Login();
        var map = new Dictionary<string, WebCommand>
        {
            ["go"] = WebCommand.Go, ["stop"] = WebCommand.StopAll, ["pause"] = WebCommand.Pause,
            ["resume"] = WebCommand.Resume, ["next"] = WebCommand.Next, ["previous"] = WebCommand.Previous
        };
        foreach (var (word, cmd) in map)
        {
            var r = Send("POST", "/api/cmd", "cmd=" + word, "10.0.0.1", cookie, "X-ShowCue: 1");
            Assert.Equal(200, Status(r));
            Assert.Equal(cmd, r.Command);
        }
        var noHeader = Send("POST", "/api/cmd", "cmd=go", "10.0.0.1", cookie);
        Assert.Equal(403, Status(noHeader));
        Assert.Null(noHeader.Command);
    }

    [Fact]
    public void UnknownCommandsAreRejected()
    {
        var cookie = "Cookie: " + Login();
        foreach (var body in new[] { "", "cmd=", "cmd=GO", "cmd=go&x=1", "cmd=edit", "cmd=delete", "go", "cmd=go\r\n", "cmd=" + new string('a', 100) })
        {
            var r = Send("POST", "/api/cmd", body, "10.0.0.1", cookie, "X-ShowCue: 1");
            Assert.Equal(400, Status(r));
            Assert.Null(r.Command);
        }
    }

    [Fact]
    public void OnlyFixedRoutesExist()
    {
        var cookie = "Cookie: " + Login();
        foreach (var path in new[] { "/index.html", "/etc/passwd", "/api/", "/api/state/", "/api", "/api/edit", "/api/save", "/static/app.js", "/favicon.ico", "//", "/API/state" })
            Assert.Equal(404, Status(Send("GET", path, "", "10.0.0.1", cookie)));
        Assert.Equal(404, Status(Send("GET", "/etc/passwd")));
        Assert.Equal(400, Status(Send("GET", "/static/../secret", "", "10.0.0.1", cookie)));
    }

    [Fact]
    public void WrongMethodsAreRejected()
    {
        var cookie = "Cookie: " + Login();
        Assert.Equal(405, Status(Send("POST", "/api/state", "x", "10.0.0.1", cookie)));
        Assert.Equal(405, Status(Send("POST", "/api/cues", "x", "10.0.0.1", cookie)));
        Assert.Equal(405, Status(Send("GET", "/api/cmd", "", "10.0.0.1", cookie)));
        Assert.Equal(405, Status(Send("GET", "/api/login")));
        Assert.Equal(405, Status(Send("POST", "/", "x")));
        Assert.Equal(405, Status(Send("OPTIONS", "/api/state")));
    }

    [Fact]
    public void ResponsesAreClosedAndHaveNoCors()
    {
        var cookie = "Cookie: " + Login();
        foreach (var r in new[] { Send("GET", "/"), Send("GET", "/api/state"), Send("GET", "/nope"), Send("GET", "/api/state", "", "10.0.0.1", cookie), Send("OPTIONS", "/") })
        {
            var t = Text(r);
            Assert.StartsWith("HTTP/1.1 ", t);
            Assert.Contains("Connection: close\r\n", t);
            Assert.Contains("Cache-Control: no-store\r\n", t);
            Assert.Contains("Content-Security-Policy: default-src 'none'", t);
            Assert.DoesNotContain("Access-Control", t, StringComparison.OrdinalIgnoreCase);
            int declared = int.Parse(Header(r, "Content-Length")!);
            Assert.Equal(declared, Encoding.UTF8.GetByteCount(Body(r)));
        }
    }

    [Fact]
    public void OversizeAndMalformedInputGetsErrorStatus()
    {
        var huge = Encoding.ASCII.GetBytes(new string('A', WebRemote.MaxRequestBytes + 10));
        Assert.Equal(414, Status(Send(huge)));
        var lines = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: a\r\nX: " + new string('a', 9000));
        Assert.Equal(431, Status(Send(lines)));
        Assert.Equal(400, Status(Send(Encoding.ASCII.GetBytes("\r\n\r\n"))));
        Assert.False(Send(Encoding.ASCII.GetBytes("GET / HTT")).Complete);
        Assert.Equal(413, Status(Send("POST", "/api/login", new string('x', 2000))));
    }

    [Fact]
    public void ConstantTimeEqualsCases()
    {
        Assert.True(WebRemote.ConstantTimeEquals("abc123", "abc123"));
        Assert.True(WebRemote.ConstantTimeEquals("", ""));
        Assert.False(WebRemote.ConstantTimeEquals("abc123", "abc124"));
        Assert.False(WebRemote.ConstantTimeEquals("abc123", "abc12"));
        Assert.False(WebRemote.ConstantTimeEquals("abc12", "abc123"));
        Assert.False(WebRemote.ConstantTimeEquals("", "a"));
        var longText = new string('a', 100);
        Assert.False(WebRemote.ConstantTimeEquals(longText, longText));
    }

    [Fact]
    public void WeakAndMalformedPinsAreNotAccepted()
    {
        foreach (var p in new[] { "", "12345", "123456", "000000", "111111", "aaaaaaaa", "password", "Passord", "654321", "12 3456", "abc-123456", "æøå123456", new string('a', 33) + "b" })
            Assert.False(WebRemote.IsAcceptablePin(p), p);
        foreach (var p in new[] { "483920", "abcdef12", "K7mQ2xP9", new string('a', 31) + "b" })
            Assert.True(WebRemote.IsAcceptablePin(p), p);
        Assert.False(WebRemote.IsAcceptablePin(null));
        Assert.Throws<ArgumentException>(() => new WebRemoteCore("123456", Array.Empty<byte>(), Array.Empty<byte>(), () => 0));
        Assert.Throws<ArgumentException>(() => new WebRemoteCore("", Array.Empty<byte>(), Array.Empty<byte>(), () => 0));
    }

    [Fact]
    public void GeneratedPinIsAcceptable()
    {
        for (int i = 0; i < 200; i++)
        {
            var p = WebRemote.GeneratePin();
            Assert.Equal(6, p.Length);
            Assert.True(WebRemote.IsAcceptablePin(p), p);
        }
        // Tilfeldighet som gir lette koder hoppes over: først 000000 og 123456, så en vanlig verdi.
        var seq = new Queue<byte[]>(new[] { new byte[4], BitConverter.GetBytes(123456u), BitConverter.GetBytes(739518u) });
        Assert.Equal("739518", WebRemote.GeneratePin(_ => seq.Dequeue()));
    }

    [Fact]
    public void NormalizePinKeepsGoodAndReplacesBad()
    {
        Assert.Equal("483920", WebRemote.NormalizePin(" 483920 ", true));
        Assert.True(WebRemote.IsAcceptablePin(WebRemote.NormalizePin("", true)));
        Assert.True(WebRemote.IsAcceptablePin(WebRemote.NormalizePin(null, true)));
        Assert.True(WebRemote.IsAcceptablePin(WebRemote.NormalizePin("123456", true)));
        Assert.Equal("", WebRemote.NormalizePin("", false));
        Assert.Equal("", WebRemote.NormalizePin("123456", false));
    }

    [Fact]
    public void SettingsAreNormalisedOnLoad()
    {
        var s = AppSettings.Normalize(new AppSettings { WebRemoteEnabled = true, WebRemotePin = "", WebRemotePort = 80 });
        Assert.True(WebRemote.IsAcceptablePin(s.WebRemotePin));
        Assert.Equal(WebRemote.DefaultPort, s.WebRemotePort);
        var off = AppSettings.Normalize(new AppSettings());
        Assert.False(off.WebRemoteEnabled);
        Assert.Equal("", off.WebRemotePin);
        Assert.Equal(8088, off.WebRemotePort);
        var kept = AppSettings.Normalize(new AppSettings { WebRemoteEnabled = true, WebRemotePin = "K7mQ2xP9", WebRemotePort = 9090 });
        Assert.Equal("K7mQ2xP9", kept.WebRemotePin);
        Assert.Equal(9090, kept.WebRemotePort);
        Assert.True(AppSettings.Normalize(new AppSettings { WebRemotePort = 70000 }).WebRemotePort == 8088);
    }
}

/// <summary>JSON og sider.</summary>
public class WebRemoteContentTests
{
    [Fact]
    public void JsonEscapeHandlesSpecialCharacters()
    {
        Assert.Equal("a\\\"b\\\\c", WebRemote.JsonEscape("a\"b\\c"));
        Assert.Equal("\\u000a\\u0009\\u0000", WebRemote.JsonEscape("\n\t\0"));
        Assert.Equal("\\u003cscript\\u003e\\u0026", WebRemote.JsonEscape("<script>&"));
        Assert.Equal("\\u2028", WebRemote.JsonEscape(" "));
        Assert.Equal("Æøå", WebRemote.JsonEscape("Æøå"));
    }

    [Fact]
    public void StateJsonHasNextAndPlaying()
    {
        var json = WebRemote.BuildStateJson(new WebStateInput("Show \"1\"", true, true,
            new WebNext("3", "Intro <b>", "Lyd", "Merk\nlinje"), 3, 20,
            new[] { new WebPlaying("2", "Musikk", "Lyd", 12.34, 47.66, true, false), new WebPlaying("4", "Venter", "Video", 0, 0, false, true) }));
        Assert.Equal("{\"v\":1,\"show\":\"Show \\\"1\\\"\",\"paused\":true,\"lock\":true,\"pos\":3,\"count\":20,"
            + "\"next\":{\"n\":\"3\",\"name\":\"Intro \\u003cb\\u003e\",\"type\":\"Lyd\",\"notes\":\"Merk\\u000alinje\"},"
            + "\"playing\":[{\"n\":\"2\",\"name\":\"Musikk\",\"type\":\"Lyd\",\"el\":12.3,\"rem\":47.7,\"dur\":true,\"pre\":false},"
            + "{\"n\":\"4\",\"name\":\"Venter\",\"type\":\"Video\",\"el\":0.0,\"rem\":0.0,\"dur\":false,\"pre\":true}]}", json);
    }

    [Fact]
    public void StateJsonWithoutNextAndBadNumbers()
    {
        var json = WebRemote.BuildStateJson(new WebStateInput("", false, false, null, -5, -1,
            new[] { new WebPlaying("1", "x", "Lyd", double.NaN, double.PositiveInfinity, true, false) }));
        Assert.Contains("\"next\":null", json);
        Assert.Contains("\"pos\":0,\"count\":0", json);
        Assert.Contains("\"el\":0.0,\"rem\":0.0", json);
    }

    [Fact]
    public void CuesJsonIsLimitedAndEscaped()
    {
        var cues = Enumerable.Range(1, 1500).Select(i => new WebCueInfo(i.ToString(), "C\"" + i, "Lyd", i % 2 == 0));
        var json = WebRemote.BuildCuesJson(cues);
        Assert.StartsWith("{\"cues\":[{\"n\":\"1\",\"name\":\"C\\\"1\",\"type\":\"Lyd\",\"off\":false}", json);
        Assert.Equal(WebRemote.MaxListedCues, json.Split("\"n\":").Length - 1);
        Assert.Equal("{\"cues\":[]}", WebRemote.BuildCuesJson(Array.Empty<WebCueInfo>()));
        var longName = WebRemote.BuildCuesJson(new[] { new WebCueInfo("1", new string('x', 500), "Lyd", false) });
        Assert.True(longName.Length < 200);
    }

    [Fact]
    public void PagesAreSelfContained()
    {
        var texts = WebRemote.PageTexts("nb");
        foreach (var page in new[] { WebRemote.LoginPage(texts), WebRemote.ControlPage(texts) })
        {
            var html = Encoding.UTF8.GetString(page);
            Assert.DoesNotContain("{{", html);
            Assert.DoesNotContain("http://", html);
            Assert.DoesNotContain("https://", html);
            Assert.DoesNotContain("src=", html);
            Assert.DoesNotContain("href=", html);
            Assert.DoesNotContain("@import", html);
            Assert.DoesNotContain("innerHTML", html);
            Assert.Contains("<script>", html);
        }
    }

    [Fact]
    public void PageTextsAreEscaped()
    {
        var texts = new Dictionary<string, string> { ["lang"] = "nb", ["next"] = "<b>\"x\"&'" };
        var html = Encoding.UTF8.GetString(WebRemote.ControlPage(texts));
        Assert.Contains("&lt;b&gt;&quot;x&quot;&amp;&#39;", html);
        Assert.DoesNotContain("<b>\"x\"", html);
    }

    [Fact]
    public void UrlShowsFirstAddressOrLocalhost()
    {
        Assert.Equal("http://192.168.1.20:8088", WebRemote.Url(new[] { "192.168.1.20", "10.0.0.2" }, 8088));
        Assert.Equal("http://localhost:9000", WebRemote.Url(Array.Empty<string>(), 9000));
    }
}
