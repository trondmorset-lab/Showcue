using System.Text.Json;
using ShowCue.Engine;
using ShowCue.Models;
using Cue = ShowCue.Models.Cue;

namespace ShowCue.Tests;

public class ShowFileTests
{
    private static ShowDocument AllTypes()
    {
        var doc = new ShowDocument();
        doc.Cues.Add(new AudioCue { Number = "1", FilePath = @"C:\lyd\intro.wav", VolumeDb = -6, Loop = true, FadeIn = 2, Hotkey = "f1" });
        doc.Cues.Add(new VideoCue { Number = "2", FilePath = @"C:\v\a.mp4", VolumeDb = -6, StartTime = 12.5, EndTime = 40, ContinueMode = ContinueMode.AutoFollow });
        doc.Cues.Add(new ImageCue { Number = "3", FilePath = @"C:\b\logo.png", HoldTime = 5 });
        doc.Cues.Add(new PdfCue { Number = "4", FilePath = @"C:\p\tekst.pdf", Page = 3 });
        doc.Cues.Add(new FadeCue { Number = "5", Target = "1", TargetDb = -20, StopWhenDone = false });
        doc.Cues.Add(new StopCue { Number = "6", FadeTime = 2 });
        doc.Cues.Add(new OscCue { Number = "7", Address = "/ch/01/mix/fader", Arguments = "0.75" });
        doc.Cues.Add(new MidiCue { Number = "8", MessageType = MidiMessageType.MscGo, MscCueNumber = "12.5" });
        return doc;
    }

    [Fact]
    public void RuntimeFieldsAreNotSaved()
    {
        var doc = AllTypes();
        doc.Cues[0].ActiveCount = 3;
        doc.Cues[0].Error = "x";
        var json = doc.ToJson();
        Assert.DoesNotContain("ActiveCount", json);
        Assert.DoesNotContain("Summary", json);
        Assert.DoesNotContain("\"Error\"", json);
        Assert.Contains("\"type\": \"audio\"", json);
        Assert.Contains("\"type\": \"midi\"", json);
    }

    [Fact]
    public void RoundTripAllCueTypes()
    {
        var doc = AllTypes();
        var json = doc.ToJson();
        var path = Path.GetTempFileName();
        doc.Save(path);
        var back = ShowDocument.Load(path);

        Assert.Equal(8, back.Cues.Count);
        var a = Assert.IsType<AudioCue>(back.Cues[0]);
        Assert.True(a.VolumeDb == -6 && a.Loop && a.FadeIn == 2 && a.Hotkey == "F1");
        var v = Assert.IsType<VideoCue>(back.Cues[1]);
        Assert.True(v.VolumeDb == -6 && v.StartTime == 12.5 && v.EndTime == 40 && v.ContinueMode == ContinueMode.AutoFollow);
        Assert.Equal(3, Assert.IsType<PdfCue>(back.Cues[3]).Page);
        var f = Assert.IsType<FadeCue>(back.Cues[4]);
        Assert.True(f.Target == "1" && !f.StopWhenDone && f.TargetDb == -20);
        var m = Assert.IsType<MidiCue>(back.Cues[7]);
        Assert.True(m.MessageType == MidiMessageType.MscGo && m.MscCueNumber == "12.5");
        Assert.Equal(json, back.ToJson()); // samme JSON etter rundtur (brukes til «ulagrede endringer»)

        var clone = back.Cues[0].Clone();
        Assert.NotSame(back.Cues[0], clone);
        Assert.Equal(@"C:\lyd\intro.wav", Assert.IsType<AudioCue>(clone).FilePath);
    }

    [Fact]
    public void MacShowWithTypeLastAndUnknownFields()
    {
        var macJson = "{\"FormatVersion\":1,\"Title\":\"m\",\"Cues\":[{\"Number\":\"9\",\"FilePath\":\"/Users/t/a.wav\",\"Routes\":[{\"GainDb\":-3,\"Bus\":4,\"Source\":1}],\"Ukjent\":true,\"type\":\"audio\"},{\"Target\":\"9\",\"type\":\"fade\",\"FadeTime\":2}]}";
        var md = JsonSerializer.Deserialize<ShowDocument>(macJson, ShowDocument.JsonOptions)!;
        var mac = Assert.IsType<AudioCue>(md.Cues[0]);
        Assert.True(mac.Number == "9" && mac.Routes![0].Bus == 4 && mac.Routes[0].GainDb == -3);
        Assert.Equal(2, Assert.IsType<FadeCue>(md.Cues[1]).FadeTime);
        var re = md.ToJson();
        Assert.True(re.IndexOf("\"type\": \"audio\"", StringComparison.Ordinal) < re.IndexOf("\"Number\": \"9\"", StringComparison.Ordinal), "Windows skriver «type» først");
    }

    [Fact]
    public void UnknownCueTypeGivesClearError()
        => Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<ShowDocument>("{\"Cues\":[{\"type\":\"tull\"}]}", ShowDocument.JsonOptions));

    [Fact]
    public void LegacyVideoVolumePercent()
    {
        var oldJson = "{\"FormatVersion\":1,\"Title\":\"x\",\"Cues\":[{\"type\":\"video\",\"FilePath\":\"a.mp4\",\"Volume\":50},{\"type\":\"video\",\"FilePath\":\"b.mp4\",\"Volume\":0},{\"type\":\"video\",\"FilePath\":\"c.mp4\"}]}";
        var od = JsonSerializer.Deserialize<ShowDocument>(oldJson, ShowDocument.JsonOptions)!;
        TestAudio.Near(-6.02, Assert.IsType<VideoCue>(od.Cues[0]).VolumeDb);
        Assert.Equal(-60, Assert.IsType<VideoCue>(od.Cues[1]).VolumeDb);
        Assert.Equal(0, Assert.IsType<VideoCue>(od.Cues[2]).VolumeDb);
        var nj = od.ToJson();
        Assert.DoesNotContain("\"Volume\"", nj);
        Assert.Contains("VolumeDb", nj);
    }

    [Fact]
    public void RoutingColourAndMidiTriggerAreSaved()
    {
        var doc = new ShowDocument();
        doc.Cues.Add(new AudioCue { FilePath = "q.wav", Routes = new() { new(3, 5, -6.0206), new(4, 8) } });
        doc.Cues.Add(new AudioCue { FilePath = "s.wav", MidiTrigger = "N:61" });
        doc.Cues.Add(new VideoCue { VolumeDb = -3, Routes = new() { new(1, 2) }, Color = CueColor.Purple });
        var back = JsonSerializer.Deserialize<ShowDocument>(doc.ToJson(), ShowDocument.JsonOptions)!;
        var b0 = Assert.IsType<AudioCue>(back.Cues[0]);
        Assert.True(b0.Routes!.Count == 2 && b0.Routes[0].Bus == 5);
        var b1 = Assert.IsType<AudioCue>(back.Cues[1]);
        Assert.Null(b1.Routes); // null = standard routing
        Assert.Equal("N:61", b1.MidiTrigger);
        Assert.Equal("Note 61", b1.TriggerText);
        var vr = Assert.IsType<VideoCue>(back.Cues[2]);
        Assert.True(vr.VolumeDb == -3 && vr.Routes!.Count == 1 && vr.Color == CueColor.Purple);
    }

    [Fact]
    public void SettingsWithPatchRoundTrip()
    {
        var st = new AppSettings();
        st.Buses[4].Channels = new() { 33, 34 };
        st.Buses[4].Name = "Monitor";
        st.OscInPort = 9000;
        var st2 = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(st, ShowDocument.JsonOptions), ShowDocument.JsonOptions)!;
        Assert.Equal(8, st2.Buses.Count);
        Assert.Equal("Monitor", st2.Buses[4].Name);
        Assert.Equal(new[] { 33, 34 }, st2.Buses[4].Channels);
        Assert.Equal(9000, st2.OscInPort);
    }
}

public class CueModelTests
{
    [Fact]
    public void AudioDuration()
    {
        var ac = new AudioCue { FileDuration = 100, StartTime = 10, EndTime = 40 };
        Assert.Equal(30, ac.Duration);
        ac.EndTime = 0;
        Assert.Equal(90, ac.Duration);
        ac.Loop = true;
        Assert.Equal(0, ac.Duration);
    }

    [Fact]
    public void VideoStartAndEnd()
    {
        var v = new VideoCue { StartTime = 10, EndTime = 30 };
        v.KnownDuration = 60;
        Assert.Equal(30, v.EffectiveEnd);
        Assert.Equal(20, v.Duration);
        v.EndTime = 0;
        Assert.Equal(50, v.Duration);
        v.EndTime = 90;
        Assert.Equal(60, v.EffectiveEnd);
    }

    [Fact]
    public void ImageDurationAndSummaries()
    {
        Assert.Equal(7, new ImageCue { HoldTime = 5, FadeIn = 1, FadeOut = 1 }.Duration);
        Assert.Contains("−∞", new FadeCue { Target = "3", TargetDb = -60 }.Summary);
        Assert.Contains("ut 5,8", new AudioCue { FilePath = "q.wav", Routes = new() { new(3, 5), new(4, 8) } }.Summary);
        Assert.Contains("ingen lyd", new VideoCue { FileChannels = 0, FilePath = "a.mp4" }.Summary);
        Assert.Contains("lyd av", new VideoCue { Routes = new() }.Summary);
    }

    [Theory]
    [InlineData(83.45, "01:23.4")]
    [InlineData(3725, "1:02:05")]
    public void TimeFormatting(double seconds, string expected) => Assert.Equal(expected, TimeFormat.Format(seconds));

    private static List<Cue> L(params (ContinueMode m, bool dis)[] xs)
        => xs.Select(x => (Cue)new StopCue { ContinueMode = x.m, Disabled = x.dis }).ToList();

    [Fact]
    public void PlayheadSkipsChainsAndDisabled()
    {
        var N = ContinueMode.None; var AC = ContinueMode.AutoContinue; var AF = ContinueMode.AutoFollow;
        Assert.Equal(1, CueEngine.NextPlayheadIndex(L((N, false), (N, false), (N, false)), 0));
        Assert.Equal(3, CueEngine.NextPlayheadIndex(L((AC, false), (AF, false), (N, false), (N, false)), 0));
        Assert.Equal(2, CueEngine.NextPlayheadIndex(L((N, false), (N, true), (N, false)), 0));
        Assert.Equal(3, CueEngine.NextPlayheadIndex(L((AC, false), (N, true), (N, false), (N, false)), 0));
        Assert.Equal(1, CueEngine.NextPlayheadIndex(L((N, false)), 0));
    }

    [Fact]
    public void OutputScreenIsSavedPerVisualCue()
    {
        var doc = new ShowDocument();
        doc.Cues.Add(new VideoCue { FilePath = "a.mp4", OutputScreen = 1 });
        doc.Cues.Add(new ImageCue { FilePath = "b.png" });
        doc.Cues.Add(new PdfCue { OutputScreen = -1 });
        var back = JsonSerializer.Deserialize<ShowDocument>(doc.ToJson(), ShowDocument.JsonOptions)!;
        Assert.Equal(1, ((VideoCue)back.Cues[0]).OutputScreen);
        Assert.Equal(-2, ((ImageCue)back.Cues[1]).OutputScreen); // standard
        Assert.Equal(-1, ((PdfCue)back.Cues[2]).OutputScreen);
        Assert.Equal(-2, new VideoCue { OutputScreen = -99 }.OutputScreen);
        Assert.Equal(-2, JsonSerializer.Deserialize<ShowDocument>("{\"Cues\":[{\"type\":\"video\"}]}", ShowDocument.JsonOptions)!.Cues.OfType<VideoCue>().Single().OutputScreen);
    }
}
