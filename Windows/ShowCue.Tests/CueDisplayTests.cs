using ShowCue.Models;

namespace ShowCue.Tests;

/// <summary>Visningslogikk for cuelista og inspektøren: radtilstand, «gjenstår» og faner. Samme tester som i Mac-versjonen.</summary>
public class CueDisplayTests
{
    [Fact]
    public void RowStatePrioritisesPlayingThenDisabledThenNext()
    {
        Assert.Equal(CueRowState.Playing, CueDisplay.RowState(true, true, true));
        Assert.Equal(CueRowState.Playing, CueDisplay.RowState(true, false, false));
        Assert.Equal(CueRowState.Disabled, CueDisplay.RowState(false, true, true));
        Assert.Equal(CueRowState.Disabled, CueDisplay.RowState(false, false, true));
        Assert.Equal(CueRowState.Next, CueDisplay.RowState(false, true, false));
        Assert.Equal(CueRowState.Normal, CueDisplay.RowState(false, false, false));
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(0.2, "0:01")]
    [InlineData(12.2, "0:13")]
    [InlineData(13, "0:13")]
    [InlineData(59.9, "1:00")]
    [InlineData(150, "2:30")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3723.5, "1:02:04")]
    [InlineData(-5, "0:00")]
    [InlineData(double.NaN, "0:00")]
    [InlineData(double.PositiveInfinity, "99:59:59")]
    public void ClockRoundsUpToWholeSeconds(double seconds, string expected) =>
        Assert.Equal(expected, CueDisplay.Clock(seconds));

    [Fact]
    public void RemainingTextIsEmptyUntilTheCueHasStarted() =>
        Assert.Equal("", CueDisplay.RemainingText(false, 30, 0));

    [Fact]
    public void RemainingTextShowsInfinityWithoutAKnownDuration()
    {
        Assert.Equal("∞", CueDisplay.RemainingText(true, 0, 5));
        Assert.Equal("∞", CueDisplay.RemainingText(true, double.NaN, 5));
        Assert.Equal("∞", CueDisplay.RemainingText(true, double.PositiveInfinity, 5));
    }

    [Fact]
    public void RemainingTextCountsDownAndNeverGoesNegative()
    {
        Assert.Equal("0:13", CueDisplay.RemainingText(true, 30, 17));
        Assert.Equal("0:30", CueDisplay.RemainingText(true, 30, 0));
        Assert.Equal("0:00", CueDisplay.RemainingText(true, 30, 45));
        Assert.Equal("0:30", CueDisplay.RemainingText(true, 30, double.NaN));
    }

    private static readonly string[] AllTypes =
    {
        "audio", "video", "image", "pdf", "fade", "stop", "osc", "midi", "goto", "playlist",
        "memo", "network", "web", "slideshow", "timecode", "light", "input", "group",
    };

    [Fact]
    public void MediaTabExistsOnlyForTypesWithAudioPictureOrVideo()
    {
        var media = new[] { "audio", "video", "image", "pdf", "web", "playlist", "slideshow", "input" };
        foreach (var t in AllTypes)
        {
            var tabs = InspectorLayout.TabsFor(t);
            Assert.Equal(media.Contains(t), tabs.Contains(InspectorTab.Media));
            Assert.Equal(InspectorTab.Basic, tabs[0]);
            Assert.Contains(InspectorTab.Timing, tabs);
            Assert.Contains(InspectorTab.Advanced, tabs);
        }
    }

    [Fact]
    public void TabsAreInFixedOrderAndUnknownTypesGetTheCommonOnes()
    {
        Assert.Equal(new[] { InspectorTab.Basic, InspectorTab.Media, InspectorTab.Timing, InspectorTab.Advanced }, InspectorLayout.TabsFor("audio"));
        Assert.Equal(new[] { InspectorTab.Basic, InspectorTab.Timing, InspectorTab.Advanced }, InspectorLayout.TabsFor("fade"));
        Assert.Equal(new[] { InspectorTab.Basic, InspectorTab.Timing, InspectorTab.Advanced }, InspectorLayout.TabsFor(null));
        Assert.Equal(new[] { InspectorTab.Basic, InspectorTab.Timing, InspectorTab.Advanced }, InspectorLayout.TabsFor("noe-helt-annet"));
        Assert.True(InspectorLayout.HasMedia("AUDIO"));
    }

    [Fact]
    public void ResolveKeepsTheTabWhenItExistsAndFallsBackToBasic()
    {
        Assert.Equal(InspectorTab.Media, InspectorLayout.Resolve(InspectorTab.Media, "video"));
        Assert.Equal(InspectorTab.Basic, InspectorLayout.Resolve(InspectorTab.Media, "fade"));
        Assert.Equal(InspectorTab.Timing, InspectorLayout.Resolve(InspectorTab.Timing, "fade"));
        Assert.Equal(InspectorTab.Advanced, InspectorLayout.Resolve(InspectorTab.Advanced, null));
    }

    [Fact]
    public void CommonFieldsKeepEveryFieldFromTheOldListExactlyOnce()
    {
        var all = Enum.GetValues<InspectorTab>().SelectMany(t => InspectorLayout.CommonFields(t)).ToList();
        var expected = new[] { "disabled", "preWait", "skipInRehearsal", "continueMode", "timecodeTrigger", "clockTrigger", "hotkey", "midiTrigger", "inButtonPanel", "color", "notes" };
        Assert.Equal(expected.OrderBy(x => x), all.OrderBy(x => x));
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public void CueTypeKeysMatchTheShowFileAndEveryTypeIsCovered()
    {
        Cue[] cues =
        {
            new AudioCue(), new VideoCue(), new ImageCue(), new PdfCue(), new FadeCue(), new StopCue(), new OscCue(), new MidiCue(),
            new GotoCue(), new PlaylistCue(), new MemoCue(), new NetworkCue(), new WebCue(), new SlideshowCue(), new TimecodeCue(),
            new LightCue(), new InputCue(), new GroupCue(),
        };
        Assert.Equal(AllTypes.OrderBy(x => x), cues.Select(c => c.TypeKey).OrderBy(x => x));
        foreach (var c in cues) Assert.Contains($"\"type\":\"{c.TypeKey}\"", System.Text.Json.JsonSerializer.Serialize<Cue>(c, ShowDocument.JsonOptions).Replace(" ", ""));
    }

    [Fact]
    public void RemainingTextOnTheCueFollowsTheEngineAndIsNotSaved()
    {
        var c = new AudioCue { Name = "A" };
        Assert.Equal("", c.RemainingText);
        c.RemainingText = CueDisplay.RemainingText(true, 30, 17);
        Assert.Equal("0:13", c.RemainingText);
        var json = System.Text.Json.JsonSerializer.Serialize<Cue>(c, ShowDocument.JsonOptions);
        Assert.DoesNotContain("Remaining", json);
        Assert.DoesNotContain("TypeKey", json);
    }
}
