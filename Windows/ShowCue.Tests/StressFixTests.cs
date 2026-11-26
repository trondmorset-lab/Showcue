using System.IO;
using ShowCue.Models;
using Cue = ShowCue.Models.Cue;

namespace ShowCue.Tests;

/// <summary>Rettelser etter stresstesten (0.27.2). Samme tilfeller som på Mac.</summary>
public class StressFixTests
{
    [Fact]
    public void ChainDrainRunsLongChainsWithoutNesting()
    {
        var chain = new ChainDrain<int>();
        int depth = 0, maxDepth = 0, ran = 0;
        void Run(int n)
        {
            depth++; maxDepth = Math.Max(maxDepth, depth); ran++;
            if (n < 200_000) chain.Push(n + 1, Run);
            depth--;
        }
        chain.Push(0, Run);
        Assert.Equal(200_001, ran);
        Assert.Equal(1, maxDepth);
    }

    [Fact]
    public void ChainDrainRecoversAfterAnException()
    {
        var chain = new ChainDrain<int>();
        Assert.Throws<InvalidOperationException>(() => chain.Push(1, _ => throw new InvalidOperationException()));
        int ran = 0;
        chain.Push(2, _ => ran++);
        Assert.Equal(1, ran);
    }

    [Fact]
    public void StalledClockCheckIsReportedButNotFired()
    {
        var prev = new DateTime(2026, 11, 24, 18, 59, 50);
        var now = new DateTime(2026, 11, 24, 19, 0, 5);
        var real = TimeSpan.FromSeconds(15);
        Assert.False(ClockTriggers.Crossed(prev, now, real, "19:00:00"));
        Assert.True(ClockTriggers.Missed(prev, now, real, "19:00:00"));
        Assert.False(ClockTriggers.Missed(prev, now, real, "20:00:00"));
        // Normalt intervall er ikke en tapt trigger.
        Assert.False(ClockTriggers.Missed(new DateTime(2026, 11, 24, 18, 59, 59, 500), new DateTime(2026, 11, 24, 19, 0, 0, 100), TimeSpan.FromMilliseconds(600), "19:00:00"));
        // Dvale (lang tid) og klokkestilling (veggklokka og faktisk tid er uenige) er ikke en hang.
        Assert.False(ClockTriggers.Missed(new DateTime(2026, 11, 24, 18, 0, 0), new DateTime(2026, 11, 24, 20, 0, 0), TimeSpan.FromHours(2), "19:00:00"));
        Assert.False(ClockTriggers.Missed(prev, now, TimeSpan.FromSeconds(3), "19:00:00"));
    }

    [Fact]
    public void MissedCuesSkipsDisabledAndEmptyTriggers()
    {
        var prev = new DateTime(2026, 11, 24, 18, 59, 50);
        var now = new DateTime(2026, 11, 24, 19, 0, 5);
        var cues = new List<Cue>
        {
            new AudioCue { Number = "1", ClockTrigger = "19:00:00" },
            new AudioCue { Number = "2", ClockTrigger = "19:00:00", Disabled = true },
            new AudioCue { Number = "3", ClockTrigger = "" },
        };
        Assert.Equal(new[] { "1" }, ClockTriggers.MissedCues(cues, prev, now).Select(c => c.Number).ToArray());
    }

    [Theory]
    [InlineData("{\"Scenes\": 5}")]
    [InlineData("{\"Scenes\": \"A\"}")]
    [InlineData("{\"Scenes\": {\"x\": 1}}")]
    [InlineData("{\"Scenes\": null}")]
    public void ShowWithWrongSceneTypeStillOpens(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), "showcue-scenes-" + Guid.NewGuid().ToString("N") + ".showcue");
        try
        {
            File.WriteAllText(path, json);
            var doc = ShowDocument.Load(path);
            Assert.Empty(doc.Scenes);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SceneListSkipsNonTextEntries()
    {
        var path = Path.Combine(Path.GetTempPath(), "showcue-scenes-" + Guid.NewGuid().ToString("N") + ".showcue");
        try
        {
            File.WriteAllText(path, "{\"Scenes\": [\"Akt 1\", 7, null, \"Akt 2\"]}");
            Assert.Equal(new[] { "Akt 1", "Akt 2" }, ShowDocument.Load(path).Scenes.ToArray());
        }
        finally { File.Delete(path); }
    }
}
