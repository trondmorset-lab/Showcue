using System.Text.Json;
using ShowCue.Engine;
using ShowCue.Models;
using Cue = ShowCue.Models.Cue;

namespace ShowCue.Tests;

/// <summary>Øvelsesmodus: ren logikk for hvilken cue GO og auto-fortsett starter. Samme tilfeller som på Mac.</summary>
public class RehearsalTests
{
    /// <summary>Lager en liste fra tegn: «.» vanlig, «s» merket, «d» deaktivert, «x» merket og deaktivert.</summary>
    private static List<Cue> L(string pattern) => pattern.Select((ch, i) => (Cue)new StopCue
    {
        Number = (i + 1).ToString(),
        SkipInRehearsal = ch is 's' or 'x',
        Disabled = ch is 'd' or 'x',
    }).ToList();

    [Theory]
    [InlineData("...", 0, false, 0)]
    [InlineData("...", 1, false, 1)]
    [InlineData("s..", 0, false, 0)]   // modus av: merking har ingen virkning
    [InlineData("s..", 0, true, 1)]
    [InlineData("..s.", 2, true, 3)]
    [InlineData(".s..", 1, true, 2)]
    [InlineData("ss.s", 0, true, 2)]
    [InlineData("d.", 0, false, 1)]
    [InlineData("ds.", 0, true, 2)]
    [InlineData("xs.", 0, true, 2)]
    [InlineData("..s", 2, true, 3)]    // bare merkede igjen: slutten av lista
    [InlineData("sss", 0, true, 3)]    // alle merket
    [InlineData("sss", 0, false, 0)]
    [InlineData("...", 3, true, 3)]    // forbi slutten
    [InlineData("", 0, true, 0)]
    [InlineData("...", -5, false, 0)]
    public void NextPlayableIndexSkipsDisabledAndFlaggedOnlyInRehearsalMode(string pattern, int from, bool mode, int expected) =>
        Assert.Equal(expected, Rehearsal.NextPlayableIndex(L(pattern), from, mode));

    [Fact]
    public void GoIndexStartsThePlayheadCueWhenItCanBePlayed()
    {
        Assert.Equal(0, Rehearsal.GoIndex(L("..."), 0, true));
        Assert.Equal(1, Rehearsal.GoIndex(L("s.."), 0, true));
        Assert.Equal(0, Rehearsal.GoIndex(L("s.."), 0, false));
        Assert.Equal(2, Rehearsal.GoIndex(L(".d."), 1, false));
    }

    [Fact]
    public void GoIndexIsMinusOneWhenNothingCanStartOrThePlayheadIsOutside()
    {
        Assert.Equal(-1, Rehearsal.GoIndex(L("sss"), 0, true));
        Assert.Equal(-1, Rehearsal.GoIndex(L(".ss"), 1, true));
        Assert.Equal(-1, Rehearsal.GoIndex(L("dd"), 0, false));
        Assert.Equal(-1, Rehearsal.GoIndex(L("..."), 3, true));
        Assert.Equal(-1, Rehearsal.GoIndex(L("..."), -1, true));
        Assert.Equal(-1, Rehearsal.GoIndex(L(""), 0, true));
    }

    [Fact]
    public void ExplicitStartRunsAFlaggedCueButNeverADisabledOne()
    {
        Assert.Equal(0, Rehearsal.GoIndex(L("s.."), 0, true, explicitStart: true));
        Assert.Equal(0, Rehearsal.GoIndex(L("sss"), 0, true, explicitStart: true));
        Assert.Equal(1, Rehearsal.GoIndex(L("d.s"), 0, true, explicitStart: true));
        Assert.Equal(1, Rehearsal.GoIndex(L("x.s"), 0, true, explicitStart: true));
    }

    [Fact]
    public void IsSkippedAndIsPlayableFollowTheModeAndTheFlags()
    {
        var flagged = new StopCue { SkipInRehearsal = true };
        var plain = new StopCue();
        var off = new StopCue { Disabled = true };
        Assert.False(Rehearsal.IsSkipped(flagged, false));
        Assert.True(Rehearsal.IsSkipped(flagged, true));
        Assert.False(Rehearsal.IsSkipped(plain, true));
        Assert.True(Rehearsal.IsPlayable(flagged, false));
        Assert.False(Rehearsal.IsPlayable(flagged, true));
        Assert.True(Rehearsal.IsPlayable(plain, true));
        Assert.False(Rehearsal.IsPlayable(off, false));
    }

    [Fact]
    public void GotoAndGroupCuesAreSkippedLikeAnyOtherFlaggedCue()
    {
        var list = new List<Cue>
        {
            new GotoCue { Number = "1", Target = "5", Fire = true, SkipInRehearsal = true },
            new GroupCue { Number = "2", Targets = "3-4", SkipInRehearsal = true },
            new AudioCue { Number = "3" },
        };
        Assert.Equal(2, Rehearsal.NextPlayableIndex(list, 0, true));
        Assert.Equal(0, Rehearsal.NextPlayableIndex(list, 0, false));
        Assert.Equal(2, Rehearsal.GoIndex(list, 0, true));
    }

    [Fact]
    public void CountFlaggedCountsOnlyActiveFlaggedCues()
    {
        Assert.Equal(0, Rehearsal.CountFlagged(L("...")));
        Assert.Equal(2, Rehearsal.CountFlagged(L("s.s.")));
        Assert.Equal(1, Rehearsal.CountFlagged(L("sx.d")));
        Assert.Equal(0, Rehearsal.CountFlagged(L("")));
    }

    private static ContinueMode M(char c) => c switch { 'c' => ContinueMode.AutoContinue, 'f' => ContinueMode.AutoFollow, _ => ContinueMode.None };

    /// <summary>Hvert tegn er en cue: «.» vanlig, «s» merket, «d» deaktivert, og «c»/«f» som «.» med auto-fortsett/auto-følg.</summary>
    private static List<Cue> Chain(string pattern) => pattern.Select(ch => (Cue)new StopCue
    {
        SkipInRehearsal = ch == 's',
        Disabled = ch == 'd',
        ContinueMode = M(ch),
    }).ToList();

    [Theory]
    [InlineData("...", 0, false, 1)]
    [InlineData(".s.", 0, false, 1)]    // modus av: som før
    [InlineData(".s.", 0, true, 2)]
    [InlineData("..s", 1, true, 3)]
    [InlineData("s..", 0, true, 1)]
    [InlineData("cs..", 0, true, 3)]    // auto-fortsett starter cue 2 (hopper over den merkede), markøren går forbi
    [InlineData("cs..", 0, false, 2)]   // modus av: cue 1 starter den merkede cuen, markøren går forbi kjeden til cue 3
    [InlineData("csf..", 0, true, 4)]
    [InlineData("cdc..", 0, true, 4)]
    [InlineData("css", 0, true, 3)]     // ingenting igjen å auto-starte: slutten av lista
    [InlineData("sss", 0, true, 3)]
    public void NextPlayheadIndexSkipsChainsDisabledAndFlaggedCues(string pattern, int fired, bool mode, int expected) =>
        Assert.Equal(expected, CueEngine.NextPlayheadIndex(Chain(pattern), fired, mode));

    [Fact]
    public void NextPlayheadIndexWithoutTheModeBehavesAsBefore()
    {
        var list = Chain(".s.");
        Assert.Equal(CueEngine.NextPlayheadIndex(list, 0), CueEngine.NextPlayheadIndex(list, 0, false));
    }

    [Fact]
    public void FieldDefaultsToOffAndIsSavedWithTheSameKey()
    {
        Assert.False(new AudioCue().SkipInRehearsal);
        var doc = new ShowDocument();
        doc.Cues.Add(new AudioCue { FilePath = "a.wav", SkipInRehearsal = true });
        doc.Cues.Add(new FadeCue());
        var json = doc.ToJson();
        Assert.Contains("\"SkipInRehearsal\": true", json);
        var back = JsonSerializer.Deserialize<ShowDocument>(json, ShowDocument.JsonOptions)!;
        Assert.True(back.Cues[0].SkipInRehearsal);
        Assert.False(back.Cues[1].SkipInRehearsal);
        Assert.Equal(json, back.ToJson());
    }

    [Fact]
    public void OldShowFilesWithoutTheFieldStillLoadWithTheFieldOff()
    {
        var doc = new ShowDocument();
        doc.Cues.Add(new AudioCue { FilePath = "a.wav", SkipInRehearsal = true });
        var json = doc.ToJson().Replace("\"SkipInRehearsal\": true,", "").Replace("\"SkipInRehearsal\": true", "\"Unknown\": 1");
        Assert.DoesNotContain("SkipInRehearsal", json);
        var back = JsonSerializer.Deserialize<ShowDocument>(json, ShowDocument.JsonOptions)!;
        Assert.False(back.Cues[0].SkipInRehearsal);
    }

    private sealed class NoEnv : IPreflightEnvironment
    {
        public FileStatus CheckFile(string path) => FileStatus.Ok;
        public bool AudioOutputExists(AudioDriverType driver, string id) => true;
        public bool InputDeviceExists(string id) => true;
        public bool ScreenExists(int index) => true;
    }

    [Fact]
    public void PreflightWarnsWhenTheModeIsOnAndCountsFlaggedCues()
    {
        var cues = new List<Cue>
        {
            new MemoCue { Number = "1", Text = "x", SkipInRehearsal = true },
            new MemoCue { Number = "2", Text = "y" },
            new MemoCue { Number = "3", Text = "z", SkipInRehearsal = true },
            new MemoCue { Number = "4", Text = "w", SkipInRehearsal = true, Disabled = true },
        };
        var on = PreflightCheck.Run(cues, new AppSettings(), new NoEnv(), rehearsalMode: true);
        var w = Assert.Single(on, f => f.Code == PreflightCheck.RehearsalOn);
        Assert.Equal(PreflightSeverity.Warning, w.Severity);
        Assert.Contains("2", w.Message);
        var note = Assert.Single(on, f => f.Code == PreflightCheck.RehearsalFlagged);
        Assert.Equal(PreflightSeverity.Ok, note.Severity);
        Assert.Contains("2", note.Message);

        var off = PreflightCheck.Run(cues, new AppSettings(), new NoEnv());
        Assert.DoesNotContain(off, f => f.Code == PreflightCheck.RehearsalOn);
        Assert.Single(off, f => f.Code == PreflightCheck.RehearsalFlagged);
    }

    [Fact]
    public void PreflightSaysNothingAboutRehearsalWhenOffAndNothingIsFlagged()
    {
        var cues = new List<Cue> { new MemoCue { Number = "1", Text = "x" } };
        var r = PreflightCheck.Run(cues, new AppSettings(), new NoEnv());
        Assert.DoesNotContain(r, f => f.Code is PreflightCheck.RehearsalOn or PreflightCheck.RehearsalFlagged);
        var on = PreflightCheck.Run(cues, new AppSettings(), new NoEnv(), rehearsalMode: true);
        Assert.Single(on, f => f.Code == PreflightCheck.RehearsalOn);
        Assert.DoesNotContain(on, f => f.Code == PreflightCheck.RehearsalFlagged);
    }
}
