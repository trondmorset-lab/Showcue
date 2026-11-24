using System.Text.Json;
using ShowCue.Engine;
using ShowCue.Models;
using static ShowCue.Tests.TestAudio;

namespace ShowCue.Tests;

/// <summary>Feil funnet med stresstesting: ugyldige verdier, ødelagte pakker og loop-grensetilfeller skal aldri
/// få lydtråden til å henge, gi NaN i lyden eller stoppe lagringen.</summary>
public class RobustnessTests
{
    [Fact]
    public void TinyLoopDoesNotHangTheAudioThread()
    {
        var oneSample = Wav("rb-one.wav", 48000, 1, 1, (_, _) => 0.5f);
        var mixer = new PatchMixer(48000, 2, PatchBus.DefaultPatch(2));
        var pb = new CuePlayback(new AudioCue { FilePath = oneSample, Loop = true }, 48000);
        mixer.Add(pb);
        var t = Task.Run(() => Run(mixer, 4800));
        Assert.True(t.Wait(TimeSpan.FromSeconds(5)), "lydtråden hang");
    }

    [Fact]
    public void SafeTickCatchesExceptionsAndReportsTheMessage()
    {
        var ran = 0;
        Assert.True(SafeTick.Run(() => ran++, out var ok));
        Assert.Equal(1, ran);
        Assert.Equal("", ok);
        Assert.False(SafeTick.Run(() => throw new InvalidOperationException("feil i cue"), out var msg));
        Assert.Equal("feil i cue", msg);
        Assert.False(SafeTick.Run(() => throw new Exception(""), out var noText));
        Assert.Equal("Exception", noText); // tom melding gir typenavnet, aldri tom feiltekst
    }

    /// <summary>Samme showfil og samme forventede verdier som i Mac-testen: begge plattformer begrenser likt ved lasting.</summary>
    [Fact]
    public void CueValuesAreClampedOnLoad()
    {
        var json = """
            {"Cues":[
              {"type":"audio","VolumeDb":1e300,"StartTime":-5,"EndTime":1e12,"FadeIn":-1,"FadeOut":1e300,"PreWait":-3,"FileChannels":100000000,
               "Routes":[{"Source":1,"Bus":1,"GainDb":500}]},
              {"type":"osc","Port":99999},
              {"type":"network","Port":-4},
              {"type":"fade","TargetDb":-1e9,"TargetOpacity":900,"FadeTime":1e9},
              {"type":"light","Universe":999999,"FadeTime":1e9},
              {"type":"midi","Channel":99,"Data1":-1,"Data2":500,"MscDeviceId":1000,"MscCommandFormat":-9},
              {"type":"timecode","LtcBus":9999,"LtcLevelDb":50,"Length":-1},
              {"type":"playlist","Crossfade":1e9},
              {"type":"slideshow","Crossfade":1e9},
              {"type":"input","FileChannels":0}
            ]}
            """;
        var c = JsonSerializer.Deserialize<ShowDocument>(json, ShowDocument.JsonOptions)!.Cues;
        var a = (AudioCue)c[0];
        Assert.Equal(new[] { 12.0, 0, 86400, 0, 86400, 0 }, new[] { a.VolumeDb, a.StartTime, a.EndTime, a.FadeIn, a.FadeOut, a.PreWait });
        Assert.Equal(1024, a.FileChannels);
        Assert.Equal(12, a.Routes![0].GainDb);
        Assert.Equal(65535, ((OscCue)c[1]).Port);
        Assert.Equal(1, ((NetworkCue)c[2]).Port);
        var f = (FadeCue)c[3];
        Assert.Equal(new[] { -60.0, 100, 86400 }, new[] { f.TargetDb, f.TargetOpacity, f.FadeTime });
        var l = (LightCue)c[4];
        Assert.Equal(32767, l.Universe);
        Assert.Equal(600, l.FadeTime);
        var m = (MidiCue)c[5];
        Assert.Equal(new[] { 16, 0, 127, 127, 0 }, new[] { m.Channel, m.Data1, m.Data2, m.MscDeviceId, m.MscCommandFormat });
        var t = (TimecodeCue)c[6];
        Assert.Equal(new[] { 64.0, 0, 0 }, new[] { t.LtcBus, t.Length, t.LtcLevelDb });
        Assert.Equal(30, ((PlaylistCue)c[7]).Crossfade);
        Assert.Equal(10, ((SlideshowCue)c[8]).Crossfade);
        Assert.Equal(1, ((InputCue)c[9]).FileChannels);
    }

    [Fact]
    public void NonFiniteValuesAreSanitized()
    {
        var c = new AudioCue { StartTime = double.NaN, EndTime = double.PositiveInfinity, FadeIn = -1, FadeOut = 1e300, VolumeDb = double.NaN, FileDuration = double.NaN };
        Assert.Equal(0, c.StartTime);
        Assert.Equal(0, c.EndTime); // uendelig regnes som ugyldig
        Assert.Equal(0, c.FadeIn);
        Assert.Equal(86400, c.FadeOut);
        Assert.Equal(0, c.VolumeDb);
        Assert.Equal(0, c.FileDuration);
        Assert.Equal(0, new AudioRoute(1, 1, double.NaN).GainDb);
        var doc = new ShowDocument();
        doc.Cues.Add(c);
        doc.Cues.Add(new FadeCue { TargetDb = double.NegativeInfinity, FadeTime = double.NaN });
        doc.ToJson(); // skal kunne lagres
    }

    [Fact]
    public void PlaybackNeverOutputsNaN()
    {
        var file = Wav("rb-s.wav", 48000, 2, 4800, (f, _) => 0.1f);
        var mixer = new PatchMixer(48000, 2, PatchBus.DefaultPatch(2));
        var pb = new CuePlayback(new AudioCue { FilePath = file, Loop = true }, 48000, offset: double.NaN);
        mixer.Add(pb);
        pb.FadeTo(double.NaN, double.PositiveInfinity, false);
        pb.AdjustLevel(double.NaN);
        Assert.All(Run(mixer, 2048), v => Assert.True(float.IsFinite(v)));
        pb.Stop(double.NaN);
        Assert.Equal(0f, PatchMixer.DbToGain(double.NaN));
    }

    [Fact]
    public void TruncatedOscPacketsAreIgnored()
    {
        var full = Osc.Encode("/cue/12/start", new List<object> { 1, "x" });
        for (int n = 0; n <= full.Length; n++)
            Osc.Decode(full[..n], n); // ingen unntak, uansett hvor pakken er kuttet
        var lying = Osc.Encode("/go", new List<object>());
        Osc.Decode(lying, lying.Length + 100); // pakken påstår at den er lengre enn den er
    }

    [Fact]
    public void ClockHandlesHugeAndInvalidTimes()
    {
        Assert.Equal("99:59:59", OscFeedback.Clock(1e12));
        Assert.Equal("0:00", OscFeedback.Clock(double.NaN));
        Assert.Equal("99:59:59", OscFeedback.Clock(double.PositiveInfinity));
        Assert.Equal("", TimeFormat.Format(double.NaN));
        Assert.Equal("99:59:59", TimeFormat.Format(1e15));
        Assert.Equal(0, LoudnessMeter.GainFor(double.NaN, -23));
    }

    [Fact]
    public void VeryLongShowNamesStillGetAutosaveAndBackups()
    {
        var root = Path.Combine(Path.GetTempPath(), "showcue-rb", Guid.NewGuid().ToString("N"));
        var show = Path.Combine(root, new string('x', 200) + ".showcue");
        Directory.CreateDirectory(root);
        File.WriteAllText(show, "{}");
        File.SetLastWriteTimeUtc(show, DateTime.UtcNow.AddMinutes(-1)); // autolagringen må være nyere enn showfila
        ShowSafety.WriteAutosave(show, "{}", root);
        Assert.NotNull(ShowSafety.PendingAutosave(show, root));
        Assert.NotNull(ShowSafety.Backup(show, root));
    }

    [Fact]
    public void OscBlobWithHugeLengthIsIgnored()
    {
        // Typetaggene «b» og «i», og en blob som påstår at den er nesten 2 GB lang.
        var bytes = new List<byte> { (byte)'/', (byte)'x', 0, 0 };
        bytes.AddRange(new byte[] { (byte)',', (byte)'b', (byte)'i', 0 });
        bytes.AddRange(new byte[] { 0x7F, 0xFF, 0xFF, 0xFF });
        bytes.AddRange(new byte[] { 0, 0, 0, 5 });
        var arr = bytes.ToArray();
        var msg = Assert.Single(Osc.Decode(arr, arr.Length));
        Assert.Empty(msg.Args);
    }

    [Fact]
    public void DmxLevelsRejectNonFiniteNumbers()
    {
        Assert.False(DmxLevels.TryParse("1@NaN", out _, out _));
        Assert.False(DmxLevels.TryParse("1@NaN%", out _, out _));
        Assert.False(DmxLevels.TryParse("1@Infinity", out _, out _));
        Assert.True(DmxLevels.TryParse("1@255 2@50%", out var ok, out _));
        Assert.Equal(2, ok.Count);
    }

    [Fact]
    public void HexEscapeNeedsTwoHexDigits()
    {
        Assert.Equal(new byte[] { 0x41 }, NetworkSender.Unescape("\\x41"));
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes("\\x 4A"), NetworkSender.Unescape("\\x 4A")); // mellomrom er ikke et sifferpar
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes("\\x+4A"), NetworkSender.Unescape("\\x+4A")); // fortegn heller ikke
    }

    [Fact]
    public void DamagedShowFileStillOpens()
    {
        var json = "{\"Title\":null,\"Cues\":[null,{\"type\":\"fade\",\"Target\":\"5\",\"FadeTime\":\"tre\"},"
            + "{\"type\":\"audio\",\"FilePath\":\"a.wav\",\"VolumeDb\":-6,\"Loop\":\"ja\",\"Routes\":[null,{\"Source\":1,\"Bus\":2,\"GainDb\":0}]},"
            + "{\"type\":\"playlist\",\"Files\":[null,\"b.wav\"]},{\"type\":\"osc\",\"Host\":\"h\",\"Port\":\"åtte\"}]}";
        var doc = JsonSerializer.Deserialize<ShowDocument>(json, ShowDocument.JsonOptions)!;
        Assert.Equal("", doc.Title);
        Assert.Equal(4, doc.Cues.Count); // «null» er ingen cue
        var fade = Assert.IsType<FadeCue>(doc.Cues[0]);
        Assert.Equal("5", fade.Target);
        Assert.Equal(3, fade.FadeTime); // ugyldig verdi gir standardverdi
        var audio = Assert.IsType<AudioCue>(doc.Cues[1]);
        Assert.Equal(-6, audio.VolumeDb); // resten av cuen er intakt
        Assert.False(audio.Loop);
        Assert.Single(audio.Routes!);
        Assert.Equal(new List<string> { "b.wav" }, Assert.IsType<PlaylistCue>(doc.Cues[2]).Files);
        var osc = Assert.IsType<OscCue>(doc.Cues[3]);
        Assert.Equal("h", osc.Host);
        Assert.Equal(8000, osc.Port);
        doc.ToJson(); // og kan lagres igjen

        Assert.Empty(JsonSerializer.Deserialize<ShowDocument>("{\"Cues\":null}", ShowDocument.JsonOptions)!.Cues);
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<ShowDocument>("{\"Cues\":[{\"Number\":\"1\"}]}", ShowDocument.JsonOptions)); // uten cuetype
    }
}
