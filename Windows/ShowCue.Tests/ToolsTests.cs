using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ShowCue.Engine;
using ShowCue.Models;
using static ShowCue.Tests.TestAudio;

namespace ShowCue.Tests;

/// <summary>Lydstyrke (LUFS), mediefiler (relink/collect), nettverks- og memo-cuer.</summary>
public class ToolsTests
{
    private static readonly string Root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "showcue-tools", Guid.NewGuid().ToString("N"))).FullName;

    private static string Sine(string name, int rate, double dbfs, double seconds, double silenceAfter = 0)
    {
        var amp = Math.Pow(10, dbfs / 20);
        var frames = (int)(rate * seconds);
        return Wav(name, rate, 2, frames + (int)(rate * silenceAfter),
            (f, _) => f < frames ? (float)(amp * Math.Sin(2 * Math.PI * 1000 * f / rate)) : 0f);
    }

    // ---------------- Lydstyrke ----------------

    [Theory]
    [InlineData(48000, -23)]
    [InlineData(44100, -23)]
    [InlineData(48000, -33)]
    public void SineReadsAsEbuReference(int rate, double dbfs)
    {
        // EBU Tech 3341, test 1/2: stereo 1 kHz sinus på −23 dBFS = −23,0 LUFS (±0,1).
        var lufs = LoudnessMeter.MeasureFile(Sine($"sine{rate}{dbfs}.wav", rate, dbfs, 5));
        Assert.NotNull(lufs);
        Near(dbfs, lufs!.Value, 0.1);
    }

    [Fact]
    public void SilenceIsGatedAway()
    {
        // 5 s tone + 5 s stillhet: den absolutte porten (−70 LUFS) fjerner stillheten.
        var lufs = LoudnessMeter.MeasureFile(Sine("gate.wav", 48000, -23, 5, silenceAfter: 5));
        Near(-23, lufs!.Value, 0.2); // blokkene som overlapper slutten på tonen trekker litt ned
        Assert.Null(LoudnessMeter.MeasureFile(Wav("silent.wav", 48000, 2, 48000 * 2, (_, _) => 0f)));
        Assert.Null(LoudnessMeter.MeasureFile(Sine("short.wav", 48000, -20, 0.3))); // kortere enn én blokk
    }

    [Fact]
    public void MeasuresOnlyBetweenStartAndEnd()
    {
        // 3 s på −30, så 3 s på −20. Bare den andre delen måles.
        var path = Wav("twolevels.wav", 48000, 2, 48000 * 6, (f, _) =>
            (float)(Math.Pow(10, (f < 48000 * 3 ? -30 : -20) / 20.0) * Math.Sin(2 * Math.PI * 1000 * f / 48000)));
        Near(-20, LoudnessMeter.MeasureFile(path, start: 3)!.Value, 0.15);
        Near(-30, LoudnessMeter.MeasureFile(path, start: 0, end: 3)!.Value, 0.15);
    }

    [Fact]
    public void GainForTarget()
    {
        Assert.Equal(-5, LoudnessMeter.GainFor(-18, -23));
        Assert.Equal(7.5, LoudnessMeter.GainFor(-30.5, -23));
        Assert.Equal(12, LoudnessMeter.GainFor(-50, -23));
    }

    // ---------------- Mediefiler ----------------

    private static string Touch(string dir, string name)
    {
        Directory.CreateDirectory(dir);
        var p = Path.Combine(dir, name);
        File.WriteAllText(p, name);
        return p;
    }

    [Fact]
    public void RelinkFindsMovedFilesInSubfolders()
    {
        var dir = Path.Combine(Root, "relink");
        var a = Touch(Path.Combine(dir, "lyd"), "intro.wav");
        var b = Touch(Path.Combine(dir, "bilder", "akt1"), "Plakat.PNG");
        var t = Touch(Path.Combine(dir, "lyd"), "pause.mp3");
        var cues = new List<Cue>
        {
            new AudioCue { FilePath = @"D:\Gammel\intro.wav" },
            new ImageCue { FilePath = "/Users/x/Show/plakat.png" },
            new PlaylistCue { Files = new() { @"E:\pause.mp3", @"E:\finnes-ikke.mp3" } },
            new AudioCue { FilePath = a }, // finnes allerede
        };
        Assert.Equal(4, MediaTools.Missing(cues).Count);
        Assert.Equal(3, MediaTools.Relink(cues, dir));
        Assert.Equal(a, ((AudioCue)cues[0]).FilePath);
        Assert.Equal(b, ((ImageCue)cues[1]).FilePath);
        Assert.Equal(new[] { t, @"E:\finnes-ikke.mp3" }, ((PlaylistCue)cues[2]).Files);
        Assert.Equal(new[] { @"E:\finnes-ikke.mp3" }, MediaTools.Missing(cues));
    }

    [Fact]
    public void CollectCopiesShowAndMedia()
    {
        var src = Path.Combine(Root, "collect-src");
        var a1 = Touch(Path.Combine(src, "a"), "lyd.wav");
        var a2 = Touch(Path.Combine(src, "b"), "lyd.wav"); // samme navn, annen fil
        var img = Touch(src, "bilde.png");
        var doc = new ShowDocument();
        doc.Cues.Add(new AudioCue { Number = "1", FilePath = a1 });
        doc.Cues.Add(new AudioCue { Number = "2", FilePath = a2 });
        doc.Cues.Add(new ImageCue { Number = "3", FilePath = img });
        doc.Cues.Add(new PlaylistCue { Number = "4", Files = new() { a1, Path.Combine(src, "borte.mp3") } });

        var target = Path.Combine(Root, "collect-out");
        var r = MediaTools.Collect(doc, target, "Revy");
        Assert.Equal(3, r.Copied);
        Assert.Single(r.Missing);
        Assert.Equal(Path.Combine(target, "Revy.showcue"), r.ShowPath);
        Assert.Equal(a1, ((AudioCue)doc.Cues[0]).FilePath); // originalen er uendret

        var copy = ShowDocument.Load(r.ShowPath);
        var media = Path.Combine(target, "Media");
        Assert.Equal(Path.Combine(media, "lyd.wav"), ((AudioCue)copy.Cues[0]).FilePath);
        Assert.Equal(Path.Combine(media, "lyd (2).wav"), ((AudioCue)copy.Cues[1]).FilePath);
        Assert.Equal("lyd.wav", File.ReadAllText(Path.Combine(media, "lyd (2).wav")));
        Assert.Equal(Path.Combine(media, "bilde.png"), ((ImageCue)copy.Cues[2]).FilePath);
        Assert.Equal(Path.Combine(media, "lyd.wav"), ((PlaylistCue)copy.Cues[3]).Files[0]);
    }

    [Fact]
    public void LoadFindsMediaNextToTheShowIncludingPlaylists()
    {
        var dir = Path.Combine(Root, "load");
        var song = Touch(Path.Combine(dir, "Media"), "sang.mp3");
        var doc = new ShowDocument();
        doc.Cues.Add(new PlaylistCue { Files = new() { "/Volumes/USB/sang.mp3" } });
        doc.Cues.Add(new AudioCue { FilePath = @"C:\Show\Media\sang.mp3" });
        var path = Path.Combine(dir, "Show.showcue");
        doc.Save(path);
        var back = ShowDocument.Load(path);
        Assert.Equal(song, ((PlaylistCue)back.Cues[0]).Files[0]);
        Assert.Equal(song, ((AudioCue)back.Cues[1]).FilePath);
    }

    // ---------------- Nettverk ----------------

    [Fact]
    public void Unescape()
    {
        Assert.Equal(new byte[] { (byte)'%', (byte)'1', (byte)'A', 13, 10 }, NetworkSender.Unescape(@"%1A\r\n"));
        Assert.Equal(new byte[] { 0x02, (byte)'x', 0xFF, (byte)'\\', 9, 0 }, NetworkSender.Unescape(@"\x02x\xff\\\t\0"));
        Assert.Equal(Encoding.UTF8.GetBytes("ø \\q \\x"), NetworkSender.Unescape(@"ø \q \x"));
    }

    [Fact]
    public async Task SendsUdp()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
        var receive = server.ReceiveAsync();
        Assert.Null(await NetworkSender.SendAsync(new NetworkCue { Protocol = NetworkProtocol.Udp, Host = "127.0.0.1", Port = port, Message = @"GO\r" }));
        var got = await receive.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("GO\r", Encoding.UTF8.GetString(got.Buffer));
    }

    [Fact]
    public async Task SendsTcp()
    {
        var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        try
        {
            var port = ((IPEndPoint)server.LocalEndpoint).Port;
            var accept = server.AcceptTcpClientAsync();
            Assert.Null(await NetworkSender.SendAsync(new NetworkCue { Protocol = NetworkProtocol.Tcp, Host = "127.0.0.1", Port = port, Message = @"%1POWR 0\r" }));
            using var client = await accept.WaitAsync(TimeSpan.FromSeconds(5));
            var buf = new byte[64];
            var n = await client.GetStream().ReadAsync(buf).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("%1POWR 0\r", Encoding.ASCII.GetString(buf, 0, n));
        }
        finally { server.Stop(); }
    }

    [Fact]
    public async Task TcpReportsConnectionErrors()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop(); // ingen lytter på porten nå
        Assert.NotNull(await NetworkSender.SendAsync(new NetworkCue { Protocol = NetworkProtocol.Tcp, Host = "127.0.0.1", Port = port, Message = "x" }));
    }

    /// <summary>Minimal HTTP-server: leser én forespørsel og svarer med gitt status.</summary>
    private static async Task<string> ServeOnce(TcpListener server, string status)
    {
        using var client = await server.AcceptTcpClientAsync();
        var stream = client.GetStream();
        var sb = new StringBuilder();
        var buf = new byte[4096];
        while (true)
        {
            var n = await stream.ReadAsync(buf);
            if (n == 0) break;
            sb.Append(Encoding.UTF8.GetString(buf, 0, n));
            var text = sb.ToString();
            var head = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (head < 0) continue;
            var lenLine = text[..head].Split("\r\n").FirstOrDefault(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
            var len = lenLine == null ? 0 : int.Parse(lenLine.Split(':')[1].Trim());
            if (Encoding.UTF8.GetByteCount(text[(head + 4)..]) >= len) break;
        }
        var resp = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(resp);
        return sb.ToString();
    }

    [Fact]
    public async Task SendsHttp()
    {
        var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        try
        {
            var port = ((IPEndPoint)server.LocalEndpoint).Port;
            var serve = ServeOnce(server, "200 OK");
            var cue = new NetworkCue { Protocol = NetworkProtocol.Http, Method = "post", Url = $"http://127.0.0.1:{port}/api/scene?id=3", Message = "{\"on\":true}" };
            Assert.Equal("POST", cue.Method);
            Assert.Null(await NetworkSender.SendAsync(cue));
            var req = await serve.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.StartsWith("POST /api/scene?id=3 HTTP/1.1", req);
            Assert.Contains("application/json", req);
            Assert.EndsWith("{\"on\":true}", req);

            serve = ServeOnce(server, "404 Not Found");
            var err = await NetworkSender.SendAsync(new NetworkCue { Protocol = NetworkProtocol.Http, Url = $"http://127.0.0.1:{port}/x" });
            Assert.StartsWith("GET /x", await serve.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal("HTTP 404 Not Found", err);
        }
        finally { server.Stop(); }

        Assert.NotNull(await NetworkSender.SendAsync(new NetworkCue { Protocol = NetworkProtocol.Http, Url = "ftp://x" }));
    }

    // ---------------- Nettside ----------------

    [Fact]
    public void WebCueAddresses()
    {
        Assert.Equal("https://time.is/", new WebCue { Url = "time.is" }.ValidUri!.AbsoluteUri);
        Assert.Equal("time.is", new WebCue { Url = "https://time.is/Oslo" }.DisplayName);
        Assert.Null(new WebCue { Url = "https://" }.ValidUri);
        Assert.Null(new WebCue { Url = "ftp://x.no" }.ValidUri);
        var html = Touch(Path.Combine(Root, "web"), "klokke.html");
        Assert.True(new WebCue { Url = html }.ValidUri!.IsFile);

        var doc = new ShowDocument();
        doc.Cues.Add(new WebCue { Url = "http://10.0.0.5/score", HoldTime = 30, FadeIn = 1 });
        var json = doc.ToJson();
        Assert.Contains("\"type\": \"web\"", json);
        var w = Assert.IsType<WebCue>(JsonSerializer.Deserialize<ShowDocument>(json, ShowDocument.JsonOptions)!.Cues[0]);
        Assert.True(w.Url == "http://10.0.0.5/score" && w.HoldTime == 30 && w.FadeIn == 1 && w.Duration == 1 + 30 + 0.5);
        Assert.Empty(MediaTools.MediaPaths(doc.Cues)); // ingen fil å lete etter
    }

    // ---------------- Showfil ----------------

    [Fact]
    public void NewCueTypesRoundTrip()
    {
        var doc = new ShowDocument();
        doc.Cues.Add(new MemoCue { Text = "Vent på applaus\nså GO", InButtonPanel = true });
        doc.Cues.Add(new NetworkCue { Protocol = NetworkProtocol.Http, Method = "PUT", Url = "http://10.0.0.5/x", Message = "a" });
        doc.Cues.Add(new AudioCue { FilePath = "a.wav", Loudness = -17.3 });
        var json = doc.ToJson();
        Assert.Contains("\"type\": \"memo\"", json);
        Assert.Contains("\"type\": \"network\"", json);
        Assert.Contains("\"Protocol\": \"Http\"", json);
        var back = JsonSerializer.Deserialize<ShowDocument>(json, ShowDocument.JsonOptions)!;
        var m = Assert.IsType<MemoCue>(back.Cues[0]);
        Assert.True(m.InButtonPanel);
        Assert.Equal("Vent på applaus", m.DisplayName);
        var n = Assert.IsType<NetworkCue>(back.Cues[1]);
        Assert.True(n.Protocol == NetworkProtocol.Http && n.Method == "PUT" && n.Url == "http://10.0.0.5/x" && n.Message == "a");
        Assert.Equal(-17.3, Assert.IsType<AudioCue>(back.Cues[2]).Loudness);
        Assert.Null(new AudioCue().Loudness);
        Assert.Equal(json, back.ToJson());
    }

    [Fact]
    public void ZoomStepsUpAndDownWithinLimits()
    {
        Assert.Equal(1.1, UiZoom.Step(1.0, 1));
        Assert.Equal(0.9, UiZoom.Step(1.0, -1));
        Assert.Equal(1.25, UiZoom.Step(1.17, 1));   // mellom to trinn: neste hele trinn
        Assert.Equal(2.0, UiZoom.Step(2.0, 1));
        Assert.Equal(0.8, UiZoom.Step(0.8, -1));
        Assert.Equal(2.0, UiZoom.Clamp(5));
        Assert.Equal(1.0, UiZoom.Clamp(double.NaN));
        Assert.Equal(1.0, UiZoom.ClampList(0.5));
        Assert.Equal("125 %", UiZoom.Percent(1.25));
    }

    [Theory]
    [InlineData("", "nb", "nb")]
    [InlineData("", "nn", "nb")]
    [InlineData("", "sv", "en")]
    [InlineData("en", "nb", "en")]
    [InlineData("nb", "en", "nb")]
    public void LanguageFollowsSettingThenSystem(string setting, string system, string expected) =>
        Assert.Equal(expected, Loc.Resolve(setting, system));

    [Fact]
    public void EnglishTableIsBundled()
    {
        using var stream = typeof(Loc).Assembly.GetManifestResourceStream("en.json");
        Assert.NotNull(stream);
        var table = Loc.ReadTable(stream!);
        Assert.Equal("Save", table["Lagre"]);
        Assert.All(table, kv => Assert.False(string.IsNullOrEmpty(kv.Value), kv.Key));
    }
}
