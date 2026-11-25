using System.Text.Json;
using ShowCue.Models;
using Cue = ShowCue.Models.Cue;

namespace ShowCue.Tests;

/// <summary>Klokkeslett-trigger: ren logikk uten klokke. Samme tilfeller som på Mac.</summary>
public class ClockTriggerTests
{
    private static DateTime T(int d, int h, int m, int s, int ms = 0) => new(2026, 11, d, h, m, s, ms);

    [Theory]
    [InlineData("19:00:00", "19:00:00")]
    [InlineData("7:5:9", "07:05:09")]
    [InlineData(" 23:59:59 ", "23:59:59")]
    [InlineData("24:00:00", "")]
    [InlineData("12:60:00", "")]
    [InlineData("12:00:60", "")]
    [InlineData("12:00", "")]
    [InlineData("12:00:00:00", "")]
    [InlineData("tull", "")]
    [InlineData("-1:00:00", "")]
    [InlineData("", "")]
    public void NormalizeKeepsOnlyValidTimes(string input, string expected)
    {
        Assert.Equal(expected, ClockTriggers.Normalize(input));
        Assert.Equal(expected.Length > 0, ClockTriggers.TryParse(input, out _));
    }

    [Fact]
    public void NullTextIsNotATime()
    {
        Assert.False(ClockTriggers.TryParse(null, out _));
        Assert.Equal("", ClockTriggers.Normalize(null));
    }

    [Fact]
    public void CrossedWhenTheTimePassesInsideTheInterval()
    {
        Assert.True(ClockTriggers.Crossed(T(24, 18, 59, 59, 900), T(24, 19, 0, 0, 100), "19:00:00"));
        Assert.False(ClockTriggers.Crossed(T(24, 18, 59, 58), T(24, 18, 59, 59, 500), "19:00:00"));
        Assert.False(ClockTriggers.Crossed(T(24, 19, 0, 0, 100), T(24, 19, 0, 0, 200), "19:00:00"));
    }

    [Fact]
    public void ExactlyAtTheTimeCountsOnlyInTheIntervalThatEndsThere()
    {
        Assert.True(ClockTriggers.Crossed(T(24, 18, 59, 59, 500), T(24, 19, 0, 0), "19:00:00"));
        Assert.False(ClockTriggers.Crossed(T(24, 19, 0, 0), T(24, 19, 0, 0, 500), "19:00:00"));
    }

    [Fact]
    public void WorksAcrossMidnight()
    {
        Assert.True(ClockTriggers.Crossed(T(24, 23, 59, 59, 500), T(25, 0, 0, 0, 500), "00:00:00"));
        Assert.False(ClockTriggers.Crossed(T(24, 23, 59, 59, 500), T(25, 0, 0, 0, 500), "23:59:59"));
        Assert.True(ClockTriggers.Crossed(T(24, 23, 59, 58, 500), T(24, 23, 59, 59, 500), "23:59:59"));
        Assert.False(ClockTriggers.Crossed(T(24, 23, 59, 59, 500), T(25, 0, 0, 0, 500), "00:00:01"));
        Assert.True(ClockTriggers.Crossed(T(25, 0, 0, 0, 500), T(25, 0, 0, 1, 500), "00:00:01"));
    }

    [Fact]
    public void BackwardsJumpAndStandingStillNeverFire()
    {
        Assert.False(ClockTriggers.Crossed(T(24, 19, 0, 1), T(24, 18, 59, 59), "19:00:00"));
        Assert.False(ClockTriggers.Crossed(T(24, 19, 0, 1), T(24, 19, 0, 1), "19:00:01"));
        Assert.False(ClockTriggers.Crossed(T(25, 0, 0, 1), T(24, 23, 59, 59), "00:00:00"));
    }

    [Fact]
    public void LargeForwardJumpsAreIgnored()
    {
        // Intervall på høyst to sekunder gjelder; mer er dvale, klokkestilling eller sommertid.
        Assert.True(ClockTriggers.Crossed(T(24, 18, 59, 58, 500), T(24, 19, 0, 0, 500), "19:00:00"));
        Assert.False(ClockTriggers.Crossed(T(24, 18, 59, 58, 400), T(24, 19, 0, 0, 500), "19:00:00"));
        Assert.False(ClockTriggers.Crossed(T(24, 18, 0, 0), T(24, 20, 0, 0), "19:00:00"));
        Assert.False(ClockTriggers.Crossed(T(24, 23, 0, 0), T(25, 7, 0, 0), "00:00:00"));
    }

    [Fact]
    public void DaylightSavingJumpsAreIgnored()
    {
        var one = TimeSpan.FromSeconds(1);
        // Vårens omstilling: veggklokka hopper fra 01:59:59 til 03:00:00 på ett sekund faktisk tid.
        Assert.False(ClockTriggers.Crossed(new DateTime(2027, 3, 28, 1, 59, 59), new DateTime(2027, 3, 28, 3, 0, 0), one, "02:30:00"));
        Assert.False(ClockTriggers.Crossed(new DateTime(2027, 3, 28, 1, 59, 59), new DateTime(2027, 3, 28, 3, 0, 0), one, "03:00:00"));
        // Høstens omstilling: veggklokka går en time tilbake.
        Assert.False(ClockTriggers.Crossed(new DateTime(2026, 10, 25, 2, 59, 59), new DateTime(2026, 10, 25, 2, 0, 0), one, "02:30:00"));
        // Faktisk tid som ikke stemmer med veggklokka (klokka stilt) gir også hopp.
        Assert.False(ClockTriggers.Crossed(T(24, 18, 59, 59, 500), T(24, 19, 0, 0, 100), TimeSpan.FromMinutes(5), "19:00:00"));
        Assert.True(ClockTriggers.Crossed(T(24, 18, 59, 59, 500), T(24, 19, 0, 0, 100), TimeSpan.FromMilliseconds(600), "19:00:00"));
    }

    [Fact]
    public void InvalidOrEmptyTriggerNeverFires()
    {
        Assert.False(ClockTriggers.Crossed(T(24, 18, 59, 59, 900), T(24, 19, 0, 0, 100), ""));
        Assert.False(ClockTriggers.Crossed(T(24, 18, 59, 59, 900), T(24, 19, 0, 0, 100), "tull"));
        Assert.False(ClockTriggers.Crossed(T(24, 18, 59, 59, 900), T(24, 19, 0, 0, 100), null));
    }

    [Fact]
    public void DueSkipsDisabledAndEmptyAndFiresEachCueOncePerPassing()
    {
        var a = new AudioCue { Number = "1", ClockTrigger = "19:00:00" };
        var b = new AudioCue { Number = "2", ClockTrigger = "19:00:00", Disabled = true };
        var c = new AudioCue { Number = "3" };
        var d = new AudioCue { Number = "4", ClockTrigger = "19:00:01" };
        var cues = new List<Cue> { a, b, c, d };

        var fired = new List<string>();
        var t = T(24, 18, 59, 58, 500);
        for (int i = 0; i < 40; i++) // ticks à 100 ms: 18:59:58,5 → 19:00:02,5
        {
            var next = t.AddMilliseconds(100);
            fired.AddRange(ClockTriggers.Due(cues, t, next).Select(x => x.Number));
            t = next;
        }
        Assert.Equal(new[] { "1", "4" }, fired);
    }

    [Fact]
    public void SeveralCuesWithTheSameTimeAllFire()
    {
        var cues = new List<Cue> { new AudioCue { Number = "1", ClockTrigger = "19:00:00" }, new FadeCue { Number = "2", ClockTrigger = "19:00:00" } };
        Assert.Equal(2, ClockTriggers.Due(cues, T(24, 18, 59, 59, 500), T(24, 19, 0, 0, 500)).Count);
    }

    [Fact]
    public void CueFieldIsNormalisedSavedWithTheSameKeyAndShownInTheTriggerText()
    {
        Assert.Equal("", new AudioCue { ClockTrigger = "tull" }.ClockTrigger);
        var doc = new ShowDocument();
        doc.Cues.Add(new AudioCue { FilePath = "a.wav", ClockTrigger = "9:5:0", Hotkey = "F1" });
        var json = doc.ToJson();
        Assert.Contains("\"ClockTrigger\": \"09:05:00\"", json);
        var back = JsonSerializer.Deserialize<ShowDocument>(json, ShowDocument.JsonOptions)!;
        Assert.Equal("09:05:00", back.Cues[0].ClockTrigger);
        Assert.Contains("F1", back.Cues[0].TriggerText);
        Assert.Contains("09:05:00", back.Cues[0].TriggerText);
        Assert.Equal(json, back.ToJson());

        var bad = JsonSerializer.Deserialize<ShowDocument>(json.Replace("09:05:00", "tull"), ShowDocument.JsonOptions)!;
        Assert.Equal("", bad.Cues[0].ClockTrigger);
    }

    [Fact]
    public void SettingDefaultsToOffAndIsSaved()
    {
        Assert.False(new AppSettings().ClockTriggersEnabled);
        var s = new AppSettings { ClockTriggersEnabled = true };
        var back = AppSettings.Normalize(JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s, ShowDocument.JsonOptions), ShowDocument.JsonOptions)!);
        Assert.True(back.ClockTriggersEnabled);
        Assert.False(AppSettings.Normalize(JsonSerializer.Deserialize<AppSettings>("{}", ShowDocument.JsonOptions)!).ClockTriggersEnabled);
    }

    private sealed class NoEnv : IPreflightEnvironment
    {
        public FileStatus CheckFile(string path) => FileStatus.Ok;
        public bool AudioOutputExists(AudioDriverType driver, string id) => true;
        public bool InputDeviceExists(string id) => true;
        public bool ScreenExists(int index) => true;
    }

    [Fact]
    public void PreflightWarnsWhenCuesHaveClockTriggersButTheSettingIsOff()
    {
        var cues = new List<Cue> { new MemoCue { Number = "1", Text = "x", ClockTrigger = "19:00:00" }, new MemoCue { Number = "2", Text = "y" } };
        var off = PreflightCheck.Run(cues, new AppSettings(), new NoEnv());
        var w = Assert.Single(off, f => f.Code == PreflightCheck.ClockTriggerOff);
        Assert.Equal(PreflightSeverity.Warning, w.Severity);
        Assert.Equal(0, w.CueIndex);
        Assert.DoesNotContain(PreflightCheck.ValuesOk, off.Select(f => f.Code));

        var on = PreflightCheck.Run(cues, new AppSettings { ClockTriggersEnabled = true }, new NoEnv());
        Assert.DoesNotContain(on, f => f.Code == PreflightCheck.ClockTriggerOff);

        cues[0].Disabled = true;
        Assert.DoesNotContain(PreflightCheck.Run(cues, new AppSettings(), new NoEnv()), f => f.Code == PreflightCheck.ClockTriggerOff);
    }
}
