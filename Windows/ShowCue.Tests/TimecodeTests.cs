using System.Text.Json;
using ShowCue.Engine;
using ShowCue.Models;
using static ShowCue.Tests.TestAudio;

namespace ShowCue.Tests;

/// <summary>Tidskode (MTC/LTC), DMX/Art-Net og live-inngang.</summary>
public class TimecodeTests
{
    // ---------------- Tidskode ----------------

    [Theory]
    [InlineData(TimecodeRate.Fps25, "01:00:00:00", 90000)]
    [InlineData(TimecodeRate.Fps24, "00:00:01:23", 47)]
    [InlineData(TimecodeRate.Fps30, "00:01:00:00", 1800)]
    [InlineData(TimecodeRate.Fps2997Drop, "00:01:00:02", 1800)]   // 00:01:00;00 og ;01 finnes ikke
    [InlineData(TimecodeRate.Fps2997Drop, "00:10:00:00", 17982)]  // hvert tiende minutt droppes ikke
    [InlineData(TimecodeRate.Fps2997Drop, "01:00:00:00", 107892)]
    public void FrameNumbers(TimecodeRate rate, string text, long frame)
    {
        Assert.True(Timecode.TryParse(text, out var tc));
        Assert.Equal(frame, tc.ToFrame(rate));
        Assert.Equal(tc, Timecode.FromFrame(frame, rate));
    }

    [Fact]
    public void DropFrameSkipsTwoFramesEachMinute()
    {
        Assert.Equal(new Timecode(0, 0, 59, 29), Timecode.FromFrame(1799, TimecodeRate.Fps2997Drop));
        Assert.Equal(new Timecode(0, 1, 0, 2), Timecode.FromFrame(1800, TimecodeRate.Fps2997Drop));
        Assert.Equal(new Timecode(0, 9, 59, 29), Timecode.FromFrame(17981, TimecodeRate.Fps2997Drop));
        Assert.Equal(new Timecode(0, 10, 0, 0), Timecode.FromFrame(17982, TimecodeRate.Fps2997Drop));
        for (long n = 0; n < 40000; n += 7)
            Assert.Equal(n, Timecode.FromFrame(n, TimecodeRate.Fps2997Drop).ToFrame(TimecodeRate.Fps2997Drop));
        Assert.Equal("00:01:00;02", new Timecode(0, 1, 0, 2).Format(TimecodeRate.Fps2997Drop));
    }

    [Fact]
    public void ParseAndCross()
    {
        Assert.True(Timecode.TryParse(" 10:20:30.15 ", out var tc));
        Assert.Equal(new Timecode(10, 20, 30, 15), tc);
        Assert.False(Timecode.TryParse("25:00:00:00", out _));
        Assert.False(Timecode.TryParse("1:2:3", out _));
        Assert.Equal("", new AudioCue { TimecodeTrigger = "tull" }.TimecodeTrigger);
        Assert.Equal("01:00:10:00", new AudioCue { TimecodeTrigger = "1:0:10;0" }.TimecodeTrigger);

        var r = TimecodeRate.Fps25;
        Assert.True(Timecode.Crossed(99, 101, 100, r));
        Assert.True(Timecode.Crossed(99, 100, 100, r));
        Assert.False(Timecode.Crossed(100, 101, 100, r));    // allerede passert
        Assert.False(Timecode.Crossed(0, 1000, 100, r));     // spoling hoppet over
        Assert.False(Timecode.Crossed(200, 101, 100, r));    // bakover
    }

    [Fact]
    public void MtcQuarterFramesRoundTrip()
    {
        var rate = TimecodeRate.Fps25;
        var start = new Timecode(1, 59, 58, 20).ToFrame(rate);
        var dec = new MtcDecoder();
        var seen = new List<Timecode>();
        for (long q = 0; q < 8 * 30; q++)
            if (dec.QuarterFrame(Mtc.QuarterFrameAt(q, start, rate)) is Timecode tc) seen.Add(tc);
        Assert.Equal(30, seen.Count);
        // Første komplette rekke (etter kvartbilde 7) er to bilder etter start, så annethvert bilde.
        for (int i = 0; i < seen.Count; i++) Assert.Equal(Timecode.FromFrame(start + 2 + 2 * i, rate), seen[i]);
        Assert.Equal(rate, dec.Rate);
        Assert.Contains(new Timecode(2, 0, 0, 0), seen); // over timeskiftet

        Assert.Equal(new byte[] { 0xF0, 0x7F, 0x7F, 0x01, 0x01, 0x41, 0x02, 0x03, 0x04, 0xF7 }, Mtc.FullFrame(new Timecode(1, 2, 3, 4), TimecodeRate.Fps2997Drop));
        Assert.Equal(new Timecode(1, 2, 3, 4), dec.FullFrame(Mtc.FullFrame(new Timecode(1, 2, 3, 4), TimecodeRate.Fps2997Drop)));
        Assert.Equal(TimecodeRate.Fps2997Drop, dec.Rate);
        Assert.Null(dec.FullFrame(new byte[] { 0xF0, 0x7F, 0x7F, 0x02, 0x01, 0, 0, 0, 0, 0xF7 }));
    }

    /// <summary>Enkel LTC-leser for testen: finner flanker, tolker korte/lange intervaller som 1/0.</summary>
    private static List<Timecode> DecodeLtc(float[] samples, double samplesPerBit)
    {
        var bits = new List<bool>();
        int last = 0;
        bool pendingHalf = false;
        for (int i = 1; i < samples.Length; i++)
        {
            if (Math.Sign(samples[i]) == Math.Sign(samples[i - 1])) continue;
            double len = (i - last) / samplesPerBit;
            last = i;
            if (len > 0.75) { bits.Add(false); pendingHalf = false; }
            else if (pendingHalf) { bits.Add(true); pendingHalf = false; }
            else pendingHalf = true;
        }
        var found = new List<Timecode>();
        for (int i = 0; i + 80 <= bits.Count; i++)
            if (Ltc.Decode(bits.GetRange(i, 80)) is Timecode tc) { found.Add(tc); i += 79; }
        return found;
    }

    [Theory]
    [InlineData(TimecodeRate.Fps25)]
    [InlineData(TimecodeRate.Fps30)]
    [InlineData(TimecodeRate.Fps2997Drop)]
    public void LtcAudioDecodesToSequentialFrames(TimecodeRate rate)
    {
        var m = new PatchMixer(48000, 2, PatchBus.DefaultPatch(2));
        var start = new Timecode(9, 59, 59, 20);
        var ltc = new LtcSource(1, -12, start, rate, 48000);
        m.Add(ltc);
        var o = Run(m, 48000);                                       // 1 sekund
        var right = Enumerable.Range(0, 48000).Select(f => At(o, 2, f, 2)).ToArray();
        Assert.All(Enumerable.Range(0, 48000), f => Assert.Equal(0f, At(o, 2, f, 1))); // bare på utgang 2
        Near(Math.Pow(10, -12 / 20.0), right.Max(), 1e-4);
        var frames = DecodeLtc(right, 48000 / (Timecode.Fps(rate) * 80));
        Assert.True(frames.Count >= Timecode.Nominal(rate) - 2, $"fant {frames.Count} bilder");
        // Første hele bilde er bildet etter start (startbildet begynner ved sample 0 og fanges ikke av leseren).
        long first = frames[0].ToFrame(rate);
        Assert.InRange(first - start.ToFrame(rate), 0, 1);
        for (int i = 0; i < frames.Count; i++) Assert.Equal(Timecode.FromFrame(first + i, rate), frames[i]);
        Assert.Contains(new Timecode(10, 0, 0, 0), frames);
        ltc.Stop(); Run(m, 64);
    }

    [Fact]
    public void LtcBitsHaveSyncWordAndEvenZeros()
    {
        foreach (var rate in Enum.GetValues<TimecodeRate>())
        {
            var b = Ltc.Bits(new Timecode(23, 59, 59, 23), rate);
            Assert.Equal(0, b.Count(x => !x) % 2);
            Assert.Equal(new Timecode(23, 59, 59, 23), Ltc.Decode(b));
            Assert.Equal(rate == TimecodeRate.Fps2997Drop, b[10]);
        }
    }

    // ---------------- DMX / Art-Net ----------------

    [Fact]
    public void DmxLevelsParse()
    {
        Assert.True(DmxLevels.TryParse("1@255, 2-4@50%  10=0\n512@12.4", out var l, out _));
        Assert.Equal(new Dictionary<int, byte> { [1] = 255, [2] = 128, [3] = 128, [4] = 128, [10] = 0, [512] = 12 }, l);
        Assert.True(DmxLevels.TryParse("", out l, out _));
        Assert.Empty(l);
        Assert.False(DmxLevels.TryParse("0@10", out _, out var e1)); Assert.Contains("1–512", e1);
        Assert.False(DmxLevels.TryParse("1@256", out _, out _));
        Assert.False(DmxLevels.TryParse("1@120%", out _, out _));
        Assert.False(DmxLevels.TryParse("5-2@10", out _, out _));
        Assert.False(DmxLevels.TryParse("lys", out _, out _));
        Assert.Equal("3 kanaler", DmxLevels.Summary("1-3@10"));
    }

    [Fact]
    public void ArtDmxPacket()
    {
        var data = new byte[512];
        data[0] = 255; data[511] = 7;
        var p = ArtNet.DmxPacket(0x1234, 9, data);
        Assert.Equal(530, p.Length);
        Assert.Equal("Art-Net\0", System.Text.Encoding.ASCII.GetString(p, 0, 8));
        Assert.Equal(new byte[] { 0x00, 0x50, 0x00, 14, 9, 0, 0x34, 0x12, 0x02, 0x00 }, p[8..18]);
        Assert.Equal(255, p[18]);
        Assert.Equal(7, p[529]);
    }

    [Fact]
    public void DmxFaderInterpolatesAndTakesOver()
    {
        var f = new DmxFader();
        f.FadeTo(new Dictionary<int, byte> { [1] = 200, [2] = 100 }, 2, now: 10);
        Assert.Equal(0, f.Values(10)[0]);
        Assert.Equal(100, f.Values(11)[0]);
        Assert.Equal(50, f.Values(11)[1]);
        // Ny fade på kanal 1 midt i: fortsetter fra 100.
        f.FadeTo(new Dictionary<int, byte> { [1] = 0 }, 1, now: 11);
        Assert.Equal(100, f.Values(11)[0]);
        Assert.Equal(50, f.Values(11.5)[0]);
        Assert.Equal(75, f.Values(11.5)[1]); // kanal 2 fader videre som før
        Assert.Equal(0, f.Values(12)[0]);
        Assert.Equal(100, f.Values(13)[1]);
        f.FadeTo(new Dictionary<int, byte> { [3] = 33 }, 0, now: 13);
        Assert.Equal(33, f.Values(13)[2]);
    }

    // ---------------- Live-inngang ----------------

    [Fact]
    public void LiveRingKeepsLatencyLowAndPadsWithSilence()
    {
        var r = new LiveRing(2, 48000, maxLatency: 0.1);
        r.Write(Enumerable.Repeat(0.5f, 200).ToArray());
        var dst = new float[400];
        r.Read(dst, 0, 400);
        Assert.All(dst[..200], v => Assert.Equal(0.5f, v));
        Assert.All(dst[200..], v => Assert.Equal(0f, v));
        r.Write(new float[48000 * 2]);                // 1 s på en gang
        Assert.InRange(r.BufferedFrames, 1, 4800);   // kastet ned under 100 ms
    }

    [Fact]
    public void InputGoesThroughRoutingAndFades()
    {
        var m = new PatchMixer(48000, 4, PatchBus.DefaultPatch(4));
        var ring = new LiveRing(1, 48000, maxLatency: 1);
        bool released = false;
        var cue = new InputCue { FileChannels = 1, VolumeDb = -6, Routes = new() { new(1, 3), new(1, 4, -6) }, FadeIn = 0, FadeOut = 0 };
        var pb = new CuePlayback(cue, new LiveInputStream(ring, 48000, _ => released = true), cue,
            new CuePlayback.Options(cue.VolumeDb, 0, 0, false, 0, 0), 48000);
        m.Add(pb);
        ring.Write(Enumerable.Repeat(0.4f, 1000).ToArray());
        var o = Run(m, 1000);
        Near(0.4 * 0.501, At(o, 4, 500, 3), 0.002);        // −6 dB
        Near(0.4 * 0.501 * 0.501, At(o, 4, 500, 4), 0.002); // −6 dB −6 dB
        Assert.Equal(0f, At(o, 4, 500, 1));
        o = Run(m, 1000);                                   // ingenting nytt: stillhet, men fortsatt i gang
        Assert.Equal(0f, At(o, 4, 500, 3));
        Assert.False(pb.IsFinished);
        pb.Stop(0); Run(m, 64);
        pb.Dispose();
        Assert.True(released);
    }

    // ---------------- Showfil ----------------

    [Fact]
    public void NewCueTypesRoundTrip()
    {
        var doc = new ShowDocument();
        doc.Cues.Add(new TimecodeCue { Start = "10:00:00:00", Rate = TimecodeRate.Fps2997Drop, SendLtc = true, LtcBus = 8, Length = 60 });
        doc.Cues.Add(new LightCue { Host = "2.255.255.255", Universe = 3, Levels = "1-4@50%", FadeTime = 5 });
        doc.Cues.Add(new InputCue { FileChannels = 2, VolumeDb = -3, FadeOut = 2 });
        doc.Cues.Add(new AudioCue { FilePath = "a.wav", TimecodeTrigger = "10:00:05:00" });
        var json = doc.ToJson();
        foreach (var t in new[] { "timecode", "light", "input" }) Assert.Contains($"\"type\": \"{t}\"", json);
        Assert.Contains("\"Rate\": \"Fps2997Drop\"", json);
        var back = JsonSerializer.Deserialize<ShowDocument>(json, ShowDocument.JsonOptions)!;
        var tc = Assert.IsType<TimecodeCue>(back.Cues[0]);
        Assert.True(tc.Start == "10:00:00:00" && tc.Rate == TimecodeRate.Fps2997Drop && tc.SendLtc && tc.LtcBus == 8 && tc.Length == 60 && tc.Duration == 60);
        var l = Assert.IsType<LightCue>(back.Cues[1]);
        Assert.True(l.Host == "2.255.255.255" && l.Universe == 3 && l.Levels == "1-4@50%" && l.FadeTime == 5);
        var i = Assert.IsType<InputCue>(back.Cues[2]);
        Assert.True(i.FileChannels == 2 && i.VolumeDb == -3 && i.FadeOut == 2);
        Assert.Equal("10:00:05:00", back.Cues[3].TimecodeTrigger);
        Assert.Contains("TC 10:00:05:00", back.Cues[3].TriggerText);
        Assert.Equal(json, back.ToJson());
        Assert.Equal("01:00:00:00", new TimecodeCue { Start = "ugyldig" }.Start);
    }
}
