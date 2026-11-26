using System.Text.Json;
using ShowCue.Models;
using Cue = ShowCue.Models.Cue;

namespace ShowCue.Tests;

/// <summary>Routingforhåndsvalg: ren logikk uten UI og lydmotor. Samme tilfeller som på Mac.</summary>
public class RoutingPresetTests
{
    private static List<RoutingPair> P(params (int bus, double gain)[] v) => v.Select(x => new RoutingPair(x.bus, x.gain)).ToList();
    private static AudioCue A(int channels, List<AudioRoute>? routes = null) => new() { FilePath = "a.wav", FileChannels = channels, Routes = routes };
    private static string R(IEnumerable<AudioRoute> r) => string.Join(" ", r.Select(x => $"{x.Source}>{x.Bus}@{x.GainDb:0.#}"));

    [Fact]
    public void AddChecksNameAndPairs()
    {
        var list = new List<RoutingPreset>();
        Assert.Equal(RoutingPresetError.EmptyName, RoutingPresets.Add(list, "  ", P((1, 0))));
        Assert.Equal(RoutingPresetError.EmptyName, RoutingPresets.Add(list, null, P((1, 0))));
        Assert.Equal(RoutingPresetError.NameTooLong, RoutingPresets.Add(list, new string('x', 41), P((1, 0))));
        Assert.Equal(RoutingPresetError.NoPairs, RoutingPresets.Add(list, "Hoved", new List<RoutingPair>()));
        Assert.Equal(RoutingPresetError.NoPairs, RoutingPresets.Add(list, "Hoved", null));
        Assert.Empty(list);
        Assert.Equal(RoutingPresetError.None, RoutingPresets.Add(list, "  Hoved  ", P((1, 0))));
        Assert.Equal("Hoved", list[0].Name);
        Assert.Equal(RoutingPresetError.DuplicateName, RoutingPresets.Add(list, "hoved", P((3, 0))));
        Assert.Single(list);
    }

    [Fact]
    public void AddStopsAtTheLimit()
    {
        var list = new List<RoutingPreset>();
        for (int i = 0; i < RoutingPresets.MaxPresets; i++) Assert.Equal(RoutingPresetError.None, RoutingPresets.Add(list, "p" + i, P((1, 0))));
        Assert.Equal(RoutingPresetError.TooManyPresets, RoutingPresets.Add(list, "en til", P((1, 0))));
        Assert.Equal(RoutingPresets.MaxPresets, list.Count);
    }

    [Fact]
    public void RenameUpdateAndRemove()
    {
        var list = new List<RoutingPreset>();
        RoutingPresets.Add(list, "Hoved", P((1, 0)));
        RoutingPresets.Add(list, "Monitor", P((3, -6)));
        Assert.Equal(RoutingPresetError.DuplicateName, RoutingPresets.Rename(list, 1, "HOVED"));
        Assert.Equal(RoutingPresetError.EmptyName, RoutingPresets.Rename(list, 1, ""));
        Assert.Equal(RoutingPresetError.NotFound, RoutingPresets.Rename(list, 5, "x"));
        Assert.Equal(RoutingPresetError.NotFound, RoutingPresets.Rename(list, -1, "x"));
        Assert.Equal(RoutingPresetError.None, RoutingPresets.Rename(list, 1, "Monitor"));
        Assert.Equal(RoutingPresetError.None, RoutingPresets.Rename(list, 1, "Scene-monitor"));
        Assert.Equal("Scene-monitor", list[1].Name);
        Assert.Equal("3-4:-6", RoutingPresets.FormatPairs(list[1]));
        Assert.Equal(RoutingPresetError.None, RoutingPresets.Update(list, 0, "Hoved", P((1, 0), (5, 0))));
        Assert.Equal("1-2, 5-6", RoutingPresets.FormatPairs(list[0]));
        Assert.Equal(RoutingPresetError.NoPairs, RoutingPresets.Update(list, 0, "Hoved", new List<RoutingPair>()));
        Assert.Equal("1-2, 5-6", RoutingPresets.FormatPairs(list[0]));
        Assert.True(RoutingPresets.Remove(list, 0));
        Assert.False(RoutingPresets.Remove(list, 3));
        Assert.Equal("Scene-monitor", Assert.Single(list).Name);
    }

    [Theory]
    [InlineData("1-2", "1-2")]
    [InlineData("1-2, 3-4:-6", "1-2, 3-4:-6")]
    [InlineData("3–4;1-2", "3-4, 1-2")]
    [InlineData("1-2 + 1-2", "1-2")]
    [InlineData("63-64", "63-64")]
    [InlineData("5-6:99", "5-6:12")]
    [InlineData("5-6:-100", "5-6:-60")]
    [InlineData("5-6:2.5", "5-6:2.5")]
    public void ParsePairsAcceptsValidText(string text, string expected)
    {
        Assert.True(RoutingPresets.TryParsePairs(text, out var pairs));
        Assert.Equal(expected, RoutingPresets.FormatPairs(new RoutingPreset("x", pairs)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1")]
    [InlineData("1-3")]
    [InlineData("2-1")]
    [InlineData("0-1")]
    [InlineData("64-65")]
    [InlineData("a-b")]
    [InlineData("1-2:")]
    [InlineData("1-2:x")]
    [InlineData("1-2:NaN")]
    [InlineData("1-2:Infinity")]
    [InlineData("1-2:-Infinity")]
    [InlineData("1-2:3:4")]
    [InlineData("-1-0")]
    [InlineData("1-2, 3-4, 5-6, 7-8, 9-10, 11-12, 13-14, 15-16, 17-18")]
    public void ParsePairsRejectsInvalidText(string text)
    {
        Assert.False(RoutingPresets.TryParsePairs(text, out var pairs));
        Assert.Empty(pairs);
    }

    [Fact]
    public void PairValuesAreLimitedAndNanSafe()
    {
        Assert.Equal(0, new RoutingPair(1, double.NaN).GainDb);
        Assert.Equal(12, new RoutingPair(1, double.PositiveInfinity).GainDb);
        Assert.Equal(-60, new RoutingPair(1, double.NegativeInfinity).GainDb);
        Assert.Equal(1, new RoutingPair(-5).Bus);
        Assert.Equal(63, new RoutingPair(500).Bus);
    }

    [Fact]
    public void RoutesForMonoStereoAndMultichannel()
    {
        var main = new RoutingPreset("Hoved", P((1, 0)));
        Assert.Equal("1>1@0 1>2@0", R(RoutingPresets.Routes(main, 1)));
        Assert.Equal("1>1@0 2>2@0", R(RoutingPresets.Routes(main, 2)));
        Assert.Equal("1>3@-6 2>4@-6", R(RoutingPresets.Routes(new RoutingPreset("M", P((3, -6))), 2)));
        Assert.Equal("1>1@-3 2>2@-3 3>1@-3 4>2@-3 5>1@-3 6>2@-3", R(RoutingPresets.Routes(main, 6)));
        Assert.Equal("1>1@-1 2>2@-1 3>1@-1", R(RoutingPresets.Routes(new RoutingPreset("x", P((1, 2))), 3)));
        Assert.Empty(RoutingPresets.Routes(main, 0));
        Assert.Empty(RoutingPresets.Routes(main, -1));
    }

    [Fact]
    public void RoutesForTwoPairsAndOverlap()
    {
        var both = new RoutingPreset("Hoved + monitor", P((1, 0), (3, -6)));
        Assert.Equal("1>1@0 1>2@0 1>3@-6 1>4@-6", R(RoutingPresets.Routes(both, 1)));
        Assert.Equal("1>1@0 1>3@-6 2>2@0 2>4@-6", R(RoutingPresets.Routes(both, 2)));
        // To par som deler en utgang gir ikke samme rute to ganger; det høyeste nivået gjelder.
        var overlap = new RoutingPreset("o", P((1, 0), (2, -6)));
        Assert.Equal("1>1@0 1>2@0 1>3@-6", R(RoutingPresets.Routes(overlap, 1)));
    }

    [Fact]
    public void RoutesSkipPairsOutsideThePatch()
    {
        var both = new RoutingPreset("x", P((1, 0), (3, 0)));
        Assert.Equal("1>1@0 2>2@0", R(RoutingPresets.Routes(both, 2, busCount: 3)));
        Assert.Equal("1>1@0 1>3@0 2>2@0 2>4@0", R(RoutingPresets.Routes(both, 2, busCount: 4)));
        Assert.Empty(RoutingPresets.Routes(new RoutingPreset("m", P((3, 0))), 2, busCount: 2));
    }

    [Fact]
    public void CleanListDropsBrokenEntries()
    {
        var list = RoutingPresets.CleanList(new RoutingPreset?[]
        {
            null,
            new RoutingPreset("", P((1, 0))),
            new RoutingPreset("Uten par", new List<RoutingPair>()),
            new RoutingPreset("Hoved", P((1, 0), (1, -3))),
            new RoutingPreset("HOVED", P((3, 0))),
            new RoutingPreset(new string('y', 60), P((3, 0))),
        });
        Assert.Equal(2, list.Count);
        Assert.Equal("Hoved", list[0].Name);
        Assert.Equal("1-2", RoutingPresets.FormatPairs(list[0]));
        Assert.Equal(40, list[1].Name.Length);
        Assert.Empty(RoutingPresets.CleanList(null));
    }

    [Fact]
    public void ApplyToSelectionSetsRoutesOnRoutedCuesWithAudioOnly()
    {
        var stereo = A(2);
        var mono = A(1);
        var silent = A(0);
        var already = A(2, new() { new(1, 3), new(2, 4) });
        var other = new MemoCue { Number = "9", Text = "x" };
        var preset = new RoutingPreset("Monitor", P((3, 0)));
        var r = RoutingPresets.Apply(new Cue?[] { stereo, mono, silent, already, other, null, stereo }, preset);
        Assert.Equal(new RoutingApplyResult(2, 1, 3), r);
        Assert.Equal("1>3@0 2>4@0", R(stereo.Routes!));
        Assert.Equal("1>3@0 1>4@0", R(mono.Routes!));
        Assert.Null(silent.Routes);
        Assert.Equal("1>3@0 2>4@0", R(already.Routes!));
    }

    [Fact]
    public void ApplyDoesNotSilenceACueWhenThePresetFitsNoOutput()
    {
        var cue = A(2);
        var r = RoutingPresets.Apply(new Cue?[] { cue }, new RoutingPreset("Monitor", P((3, 0))), busCount: 2);
        Assert.Equal(new RoutingApplyResult(0, 0, 1), r);
        Assert.Null(cue.Routes);
    }

    [Fact]
    public void CopyAndPasteRoutingBetweenCues()
    {
        var src = A(2, new() { new(1, 3, -6), new(2, 4, -6), new(2, 9, 0) });
        var clip = new RouteClipboard();
        Assert.False(clip.HasContent);
        Assert.False(clip.Copy(new MemoCue { Number = "1", Text = "x" }));
        Assert.True(clip.Copy(src));
        src.Routes![0].GainDb = 0; // senere endring i kilden slår ikke gjennom
        Assert.Equal(-6, clip.Routes![0].GainDb);

        var mono = A(1);
        var stereo = A(2);
        var noAudio = A(0);
        var r = RoutingPresets.Paste(new Cue?[] { mono, stereo, noAudio }, clip, busCount: 8);
        Assert.Equal(new RoutingApplyResult(2, 0, 1), r);
        Assert.Equal("1>3@-6", R(mono.Routes!)); // kanal 2 og utgang 9 finnes ikke her
        Assert.Equal("1>3@-6 2>4@-6", R(stereo.Routes!));
        Assert.Equal(new RoutingApplyResult(0, 2, 1), RoutingPresets.Paste(new Cue?[] { mono, stereo, noAudio }, clip, busCount: 8));
    }

    [Fact]
    public void PasteStandardAndAllOffAndEmptyClipboard()
    {
        var cue = A(2, new() { new(1, 5) });
        var clip = new RouteClipboard();
        Assert.Equal(new RoutingApplyResult(0, 0, 0), RoutingPresets.Paste(new Cue?[] { cue }, clip));
        clip.Copy(A(2));
        Assert.True(clip.HasContent);
        Assert.Null(clip.Routes);
        Assert.Equal(1, RoutingPresets.Paste(new Cue?[] { cue }, clip).Changed);
        Assert.Null(cue.Routes);
        clip.Copy(A(2, new List<AudioRoute>()));
        Assert.Equal(1, RoutingPresets.Paste(new Cue?[] { cue }, clip).Changed);
        Assert.Empty(cue.Routes!);
        clip.Clear();
        Assert.False(clip.HasContent);
    }

    [Fact]
    public void PasteSkipsCuesThatWouldGoSilentByMistake()
    {
        var src = A(8, new() { new(7, 7), new(8, 8) });
        var clip = new RouteClipboard();
        clip.Copy(src);
        var small = A(2);
        Assert.Equal(new RoutingApplyResult(0, 0, 1), RoutingPresets.Paste(new Cue?[] { small }, clip));
        Assert.Null(small.Routes);
    }

    [Fact]
    public void CleanRoutesLimitsDuplicatesAndNan()
    {
        var clean = RoutingPresets.CleanRoutes(new AudioRoute?[]
        {
            null, new(0, 1), new(1, 0), new(3, 1), new(1, 65), new(1, 2, double.NaN), new(1, 2, -3), new(2, 1, double.PositiveInfinity),
        }, fileChannels: 2);
        Assert.Equal("1>2@0 2>1@12", R(clean));
    }

    [Fact]
    public void DescribeNamesTheRouting()
    {
        var presets = new List<RoutingPreset> { new("Hoved", P((1, 0))), new("Monitor", P((3, 0))) };
        Assert.Equal("Standard", RoutingPresets.Describe(null, 2, presets));
        Assert.Equal("Av", RoutingPresets.Describe(new List<AudioRoute>(), 2, presets));
        Assert.Equal("Monitor", RoutingPresets.Describe(RoutingPresets.Routes(presets[1], 2), 2, presets));
        Assert.Equal("Utgang 1-2, 5", RoutingPresets.Describe(new List<AudioRoute> { new(1, 1), new(2, 2), new(1, 5) }, 2, presets));
        Assert.Equal("Utgang 3-4", RoutingPresets.Describe(RoutingPresets.Routes(presets[1], 2), 2, null));
        Assert.Equal("1-3, 5, 7-8", RoutingPresets.Ranges(new[] { 8, 1, 2, 3, 5, 7, 2 }));
        Assert.Equal("", RoutingPresets.Ranges(Array.Empty<int>()));
    }

    [Fact]
    public void ShowFileRoundTripAndOldFilesWithoutPresets()
    {
        var doc = new ShowDocument();
        RoutingPresets.Add(doc.RoutingPresets, "Hoved + monitor", P((1, 0), (3, -6)));
        var json = doc.ToJson();
        Assert.Contains("\"RoutingPresets\"", json);
        Assert.Contains("\"Pairs\"", json);
        Assert.Contains("\"GainDb\": -6", json);
        var back = JsonSerializer.Deserialize<ShowDocument>(json, ShowDocument.JsonOptions)!;
        Assert.Equal("1-2, 3-4:-6", RoutingPresets.FormatPairs(Assert.Single(back.RoutingPresets)));
        Assert.Equal("Hoved + monitor", back.RoutingPresets[0].Name);

        var old = JsonSerializer.Deserialize<ShowDocument>("{\"FormatVersion\":1,\"Title\":\"x\",\"Cues\":[]}", ShowDocument.JsonOptions)!;
        Assert.Empty(old.RoutingPresets);
    }

    [Theory]
    [InlineData("\"RoutingPresets\":\"tull\"")]
    [InlineData("\"RoutingPresets\":null")]
    [InlineData("\"RoutingPresets\":{\"a\":1}")]
    [InlineData("\"RoutingPresets\":[1,\"x\",null,{\"Name\":5}]")]
    public void BrokenPresetFieldDoesNotStopTheShowFromOpening(string field)
    {
        var doc = JsonSerializer.Deserialize<ShowDocument>("{\"Title\":\"x\"," + field + ",\"Cues\":[]}", ShowDocument.JsonOptions)!;
        Assert.Empty(doc.RoutingPresets);
    }

    [Fact]
    public void BrokenEntriesAreSkippedAndGoodOnesKept()
    {
        var json = "{\"RoutingPresets\":[{\"Name\":\"Bra\",\"Pairs\":[{\"Bus\":3,\"GainDb\":-2},null]},{\"Name\":\"Skadet\",\"Pairs\":[{\"Bus\":\"x\"}]},{\"Name\":\"\",\"Pairs\":[{\"Bus\":1}]},{\"Name\":\"Uten\",\"Pairs\":[]}]}";
        var doc = JsonSerializer.Deserialize<ShowDocument>(json, ShowDocument.JsonOptions)!;
        Assert.Equal("Bra", Assert.Single(doc.RoutingPresets).Name);
        Assert.Equal("3-4:-2", RoutingPresets.FormatPairs(doc.RoutingPresets[0]));
    }
}
