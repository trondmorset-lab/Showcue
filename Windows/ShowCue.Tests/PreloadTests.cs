using System.Text.Json;
using ShowCue.Models;

namespace ShowCue.Tests;

/// <summary>Forhåndslasting: hvilke cuer som forberedes for en markør, og regnskapet med grense og rekkefølge for frigjøring.
/// Samme tester som i Mac-versjonen. Selve filåpningen i bakgrunnen krever ekte filer og utstyr og testes ikke her.</summary>
public class PreloadTests
{
    private static AudioCue A(string path, string number = "", ContinueMode mode = ContinueMode.None, bool disabled = false) =>
        new() { FilePath = path, Number = number, ContinueMode = mode, Disabled = disabled };
    private static VideoCue V(string path, ContinueMode mode = ContinueMode.None) => new() { FilePath = path, ContinueMode = mode };
    private static ImageCue I(string path, ContinueMode mode = ContinueMode.None) => new() { FilePath = path, ContinueMode = mode };
    private static string[] Paths(List<PreloadItem> items) => items.Select(i => i.Path).ToArray();

    [Fact]
    public void PlanUsesTheCueUnderThePlayhead()
    {
        var cues = new List<Cue> { A("a.wav"), A("b.wav"), A("c.wav") };
        Assert.Equal(new[] { "b.wav" }, Paths(PreloadPlanner.Plan(cues, 1)));
    }

    [Fact]
    public void PlanSkipsDisabledCuesLikeGoDoes()
    {
        var cues = new List<Cue> { A("a.wav", disabled: true), A("b.wav", disabled: true), A("c.wav") };
        Assert.Equal(new[] { "c.wav" }, Paths(PreloadPlanner.Plan(cues, 0)));
    }

    [Fact]
    public void PlanIsEmptyForNothingToPrepare()
    {
        Assert.Empty(PreloadPlanner.Plan(null, 0));
        Assert.Empty(PreloadPlanner.Plan(new List<Cue>(), 0));
        var cues = new List<Cue> { A("a.wav", disabled: true) };
        Assert.Empty(PreloadPlanner.Plan(cues, 0));
        Assert.Empty(PreloadPlanner.Plan(cues, -1));
        Assert.Empty(PreloadPlanner.Plan(cues, 5));
        Assert.Empty(PreloadPlanner.Plan(new List<Cue> { A("a.wav") }, 0, 0));
    }

    [Fact]
    public void PlanFollowsAutomaticContinuationsInOrderAndStopsAtThePlainCue()
    {
        var cues = new List<Cue> { A("a.wav", mode: ContinueMode.AutoFollow), V("b.mp4", ContinueMode.AutoContinue), I("c.png"), A("d.wav") };
        var plan = PreloadPlanner.Plan(cues, 0);
        Assert.Equal(new[] { "a.wav", "b.mp4", "c.png" }, Paths(plan));
        Assert.Equal(new[] { PreloadKind.Audio, PreloadKind.Video, PreloadKind.Image }, plan.Select(p => p.Kind).ToArray());
    }

    [Fact]
    public void PlanNeverReturnsMoreThanTheLimit()
    {
        var cues = Enumerable.Range(0, 8).Select(i => (Cue)A($"{i}.wav", mode: ContinueMode.AutoFollow)).ToList();
        Assert.Equal(PreloadPlanner.MaxItems, PreloadPlanner.Plan(cues, 0).Count);
        Assert.Equal(new[] { "0.wav", "1.wav" }, Paths(PreloadPlanner.Plan(cues, 0, 2)));
        Assert.Equal(PreloadPlanner.MaxItems, PreloadPlanner.Plan(cues, 0, 99).Count);
    }

    [Fact]
    public void PlanSkipsDisabledCuesInTheChain()
    {
        var cues = new List<Cue> { A("a.wav", mode: ContinueMode.AutoFollow), A("b.wav", disabled: true), A("c.wav") };
        Assert.Equal(new[] { "a.wav", "c.wav" }, Paths(PreloadPlanner.Plan(cues, 0)));
    }

    [Fact]
    public void PlanIgnoresCuesWithoutPreparableFilesButKeepsFollowing()
    {
        var cues = new List<Cue>
        {
            new MemoCue { ContinueMode = ContinueMode.AutoContinue },
            new PdfCue { FilePath = "x.pdf", ContinueMode = ContinueMode.AutoContinue },
            A("", mode: ContinueMode.AutoContinue),
            V("v.mp4"),
        };
        Assert.Equal(new[] { "v.mp4" }, Paths(PreloadPlanner.Plan(cues, 0)));
    }

    [Fact]
    public void PlanDoesNotRepeatTheSameFile()
    {
        var cues = new List<Cue> { A("a.wav", mode: ContinueMode.AutoContinue), A("a.wav", mode: ContinueMode.AutoContinue), V("a.wav") };
        var plan = PreloadPlanner.Plan(cues, 0);
        Assert.Equal(new[] { PreloadKind.Audio, PreloadKind.Video }, plan.Select(p => p.Kind).ToArray());
    }

    [Fact]
    public void PlanFollowsGotoToItsTarget()
    {
        var cues = new List<Cue> { new GotoCue { Target = "7" }, A("a.wav"), A("t.wav", "7") };
        Assert.Equal(new[] { "t.wav" }, Paths(PreloadPlanner.Plan(cues, 0)));
    }

    [Fact]
    public void PlanHandlesGotoWithMissingTargetAndLoops()
    {
        Assert.Empty(PreloadPlanner.Plan(new List<Cue> { new GotoCue { Target = "9" }, A("a.wav") }, 0));
        Assert.Empty(PreloadPlanner.Plan(new List<Cue> { new GotoCue { Target = "" }, A("a.wav") }, 0));
        var loop = new List<Cue> { new GotoCue { Number = "1", Target = "2" }, new GotoCue { Number = "2", Target = "1" } };
        Assert.Empty(PreloadPlanner.Plan(loop, 0));
        var self = new List<Cue> { new GotoCue { Number = "1", Target = "1" } };
        Assert.Empty(PreloadPlanner.Plan(self, 0));
    }

    [Fact]
    public void PlanPreparesTheEnabledTargetsOfAGroup()
    {
        var cues = new List<Cue>
        {
            new GroupCue { Number = "9", Targets = "2, 3, 4" },
            A("x.wav", "1"), A("b.wav", "2"), A("off.wav", "3", disabled: true), new VideoCue { FilePath = "d.mp4", Number = "4" },
        };
        Assert.Equal(new[] { "b.wav", "d.mp4" }, Paths(PreloadPlanner.Plan(cues, 0)));
    }

    [Fact]
    public void PlanReturnsTheStartTimeAsAHint()
    {
        var cues = new List<Cue> { new AudioCue { FilePath = "a.wav", StartTime = 12.5 } };
        Assert.Equal(12.5, PreloadPlanner.Plan(cues, 0)[0].StartTime);
    }

    [Fact]
    public void BookStartsNewKeysUpToTheLimit()
    {
        var book = new PreloadBook(3);
        var diff = book.Sync(new[] { "a", "b", "c", "d", "e" });
        Assert.Equal(new[] { "a", "b", "c" }, diff.ToStart);
        Assert.Empty(diff.ToEvict);
        Assert.Equal(new[] { "a", "b", "c" }, book.Keys);
    }

    [Fact]
    public void BookEvictsWhatIsNoLongerWantedOldestFirst()
    {
        var book = new PreloadBook(3);
        book.Sync(new[] { "a", "b", "c" });
        var diff = book.Sync(new[] { "c", "d" });
        Assert.Equal(new[] { "d" }, diff.ToStart);
        Assert.Equal(new[] { "a", "b" }, diff.ToEvict);
        Assert.Equal(new[] { "c", "d" }, book.Keys);
    }

    [Fact]
    public void BookKeepsWhatIsAlreadyHeldAndIgnoresDuplicates()
    {
        var book = new PreloadBook(3);
        book.Sync(new[] { "a", "b" });
        var diff = book.Sync(new[] { "b", "b", "a", "" });
        Assert.Empty(diff.ToStart);
        Assert.Empty(diff.ToEvict);
        Assert.Equal(new[] { "b", "a" }, book.Keys);
    }

    [Fact]
    public void BookNeverHoldsMoreThanTheLimitEvenAfterManySyncs()
    {
        var book = new PreloadBook(3);
        var rng = new Random(4);
        for (int i = 0; i < 500; i++)
        {
            book.Sync(Enumerable.Range(0, rng.Next(0, 9)).Select(_ => "k" + rng.Next(6)));
            Assert.True(book.Keys.Count <= 3);
            Assert.Equal(book.Keys.Count, book.Keys.Distinct().Count());
        }
    }

    [Fact]
    public void BookRemoveAndClearRelease()
    {
        var book = new PreloadBook(3);
        book.Sync(new[] { "a", "b", "c" });
        Assert.True(book.Remove("b"));
        Assert.False(book.Remove("b"));
        Assert.Equal(new[] { "a", "c" }, book.Clear());
        Assert.Empty(book.Keys);
        Assert.Equal(new[] { "x" }, book.Sync(new[] { "x" }).ToStart);
    }

    [Fact]
    public void SettingIsOffByDefaultAndSurvivesLoading()
    {
        Assert.False(new AppSettings().PreloadNextCue);
        Assert.False(AppSettings.Normalize(JsonSerializer.Deserialize<AppSettings>("{}", ShowDocument.JsonOptions)!).PreloadNextCue);
        var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings { PreloadNextCue = true }, ShowDocument.JsonOptions), ShowDocument.JsonOptions)!;
        Assert.True(AppSettings.Normalize(back).PreloadNextCue);
    }
}
