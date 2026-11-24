using System.Text.Json;
using ShowCue.Models;

namespace ShowCue.Tests;

/// <summary>Gruppe-cuer: lesing av cuenumre, nummerering på nytt og lagring.</summary>
public class GroupTests
{
    private static List<Cue> Show() => new()
    {
        new StopCue { Number = "1" },
        new StopCue { Number = "2" },
        new StopCue { Number = "3.5" },
        new StopCue { Number = "4" },
        new StopCue { Number = "5", Disabled = true },
        new StopCue { Number = "A1" },
        new StopCue { Number = "" },
    };

    private static string[] Numbers(IEnumerable<Cue> cues) => cues.Select(c => c.Number).ToArray();

    [Fact]
    public void ResolvesNumbersInTheOrderWritten()
    {
        var list = Show();
        var found = CueTargets.Resolve(list, "4, 1;A1", null);
        Assert.Equal(new[] { "4", "1", "A1" }, Numbers(found));
    }

    [Fact]
    public void ResolvesRangesByNumberValue()
    {
        var list = Show();
        Assert.Equal(new[] { "2", "3.5", "4", "5" }, Numbers(CueTargets.Resolve(list, "2-5", null)));
        Assert.Equal(new[] { "2", "3.5", "4", "5" }, Numbers(CueTargets.Resolve(list, "5-2", null))); // omvendt område
    }

    [Fact]
    public void ANumberThatContainsADashIsFoundAsItIs()
    {
        var list = new List<Cue> { new StopCue { Number = "1-2" }, new StopCue { Number = "1" }, new StopCue { Number = "2" } };
        Assert.Equal(new[] { "1-2" }, Numbers(CueTargets.Resolve(list, "1-2", null)));
    }

    [Fact]
    public void SkipsItselfAndDuplicatesAndReportsMissing()
    {
        var list = Show();
        var self = list[0];
        var missing = new List<string>();
        var found = CueTargets.Resolve(list, "1, 2, 2, 9, 10-12, , x", self, missing);
        Assert.Equal(new[] { "2" }, Numbers(found));
        Assert.Equal(new[] { "9", "10-12", "x" }, missing);
    }

    [Fact]
    public void HandlesEmptyAndStrangeInput()
    {
        var list = Show();
        Assert.Empty(CueTargets.Resolve(list, "", null));
        Assert.Empty(CueTargets.Resolve(list, null, null));
        Assert.Empty(CueTargets.Resolve(list, "-", null));
        Assert.Empty(CueTargets.Resolve(list, "NaN-Infinity", null));
        Assert.Empty(CueTargets.Resolve(list, ",;,", null));
    }

    [Fact]
    public void RenumberChangesNumbersAndBothEndsOfARange()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["10"] = "1", ["12"] = "3", ["A1"] = "7" };
        Assert.Equal("1, 3, 7, 99", CueTargets.Renumber("10; 12, a1, 99", map));
        Assert.Equal("1-3", CueTargets.Renumber("10-12", map));
        Assert.Equal("", CueTargets.Renumber("", map));
    }

    [Fact]
    public void GroupRoundTripsInTheShowFile()
    {
        var doc = new ShowDocument();
        doc.Cues.Add(new GroupCue { Number = "9", Targets = " 1, 2, 4-6 " });
        var json = doc.ToJson();
        Assert.Contains("\"type\": \"group\"", json);
        Assert.Contains("\"Targets\": \"1, 2, 4-6\"", json);
        var back = JsonSerializer.Deserialize<ShowDocument>(json, ShowDocument.JsonOptions)!;
        var g = Assert.IsType<GroupCue>(back.Cues[0]);
        Assert.Equal("1, 2, 4-6", g.Targets);
        Assert.Equal("→ 1, 2, 4-6", g.Summary);
        Assert.Equal(json, back.ToJson());
    }

    [Fact]
    public void EmptyGroupShowsThatTheTargetIsMissing()
    {
        Assert.Contains("mål mangler", new GroupCue().Summary);
    }
}
