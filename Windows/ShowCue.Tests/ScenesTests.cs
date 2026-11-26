using System.Text.Json;
using ShowCue.Models;

namespace ShowCue.Tests;

/// <summary>Scener (cuelister): scenelista, nummerering per scene, GO-markør innen scenen, oppslag av mål, showfil og forhåndssjekk. Samme tilfeller som på Mac.</summary>
public class SceneTests
{
    private static StopCue C(string scene, string number) => new() { Scene = scene, Number = number };
    private static string[] Numbers(IEnumerable<Cue> cues) => cues.Select(c => c.Number).ToArray();

    private sealed class NoEnv : IPreflightEnvironment
    {
        public FileStatus CheckFile(string path) => FileStatus.Ok;
        public bool AudioOutputExists(AudioDriverType driver, string id) => true;
        public bool InputDeviceExists(string id) => true;
        public bool ScreenExists(int index) => true;
    }

    // ---------- Scenelista ----------

    [Fact]
    public void NormalizeWithoutScenesKeepsOneImplicitScene()
    {
        var cues = new List<Cue> { C("", "1"), C("", "2") };
        Assert.Empty(SceneLogic.Normalize(null, cues));
        Assert.All(cues, c => Assert.Equal("", c.Scene));
    }

    [Fact]
    public void NormalizeTakesSceneNamesFromCuesAndFillsInTheFirstScene()
    {
        var cues = new List<Cue> { C("", "1"), C("Akt 2", "2"), C("akt 1", "3") };
        var scenes = SceneLogic.Normalize(new[] { "Akt 1", " ", "AKT 1" }, cues);
        Assert.Equal(new[] { "Akt 1", "Akt 2" }, scenes);
        Assert.Equal(new[] { "Akt 1", "Akt 2", "Akt 1" }, cues.Select(c => c.Scene));
    }

    [Fact]
    public void SceneNamesLoseCharactersThatBreakTargetLists()
    {
        Assert.Equal("A B C D", SceneLogic.CleanName(" A:B,C;D "));
        Assert.Equal("", SceneLogic.CleanName("  "));
        Assert.Equal("", SceneLogic.CleanName(null));
    }

    [Fact]
    public void AddingTheFirstSceneNamesTheExistingCues()
    {
        var cues = new List<Cue> { C("", "1"), C("", "2") };
        var scenes = new List<string>();
        Assert.Equal("Akt 2", SceneLogic.AddScene(scenes, cues, "Akt 2"));
        Assert.Equal(new[] { SceneLogic.DefaultName, "Akt 2" }, scenes);
        Assert.All(cues, c => Assert.Equal(SceneLogic.DefaultName, c.Scene));
        Assert.Null(SceneLogic.AddScene(scenes, cues, "akt 2")); // navnet er i bruk
        Assert.Null(SceneLogic.AddScene(scenes, cues, "  "));
    }

    [Fact]
    public void RenamingASceneMovesCuesAndTargetsAlong()
    {
        var group = new GroupCue { Scene = "Akt 2", Targets = "Akt 1:3, 4" };
        var jump = new GotoCue { Scene = "Akt 2", TargetScene = "Akt 1" };
        var cues = new List<Cue> { C("Akt 1", "3"), group, jump };
        var scenes = new List<string> { "Akt 1", "Akt 2" };
        Assert.False(SceneLogic.RenameScene(scenes, cues, "Akt 1", "akt 2")); // opptatt
        Assert.True(SceneLogic.RenameScene(scenes, cues, "Akt 1", "Prolog"));
        Assert.Equal(new[] { "Prolog", "Akt 2" }, scenes);
        Assert.Equal("Prolog", cues[0].Scene);
        Assert.Equal("Prolog:3, 4", group.Targets);
        Assert.Equal("Prolog", jump.TargetScene);
    }

    [Fact]
    public void RenamingTheUnnamedSceneGivesItItsFirstName()
    {
        var cues = new List<Cue> { C("", "1") };
        var scenes = new List<string>();
        Assert.True(SceneLogic.RenameScene(scenes, cues, "", "Hele showet"));
        Assert.Equal(new[] { "Hele showet" }, scenes);
        Assert.Equal("Hele showet", cues[0].Scene);
    }

    [Fact]
    public void DeletingASceneMovesItsCuesToTheOneBefore()
    {
        var cues = new List<Cue> { C("A", "1"), C("B", "1"), C("C", "1") };
        var scenes = new List<string> { "A", "B", "C" };
        Assert.Equal("A", SceneLogic.DeleteScene(scenes, cues, "B"));
        Assert.Equal(new[] { "A", "C" }, scenes);
        Assert.Equal(new[] { "A", "A", "C" }, cues.Select(c => c.Scene));
        Assert.Equal("C", SceneLogic.DeleteScene(scenes, cues, "A")); // først i lista: neste scene
        Assert.Null(SceneLogic.DeleteScene(scenes, cues, "C"));        // siste scene kan ikke fjernes
    }

    [Fact]
    public void ScenesCanBeReorderedAndNeighboursFound()
    {
        var scenes = new List<string> { "A", "B", "C" };
        Assert.True(SceneLogic.MoveScene(scenes, "C", -1));
        Assert.Equal(new[] { "A", "C", "B" }, scenes);
        Assert.False(SceneLogic.MoveScene(scenes, "A", -1));
        Assert.Equal("B", SceneLogic.Neighbour(scenes, "C", 1));
        Assert.Null(SceneLogic.Neighbour(scenes, "B", 1));
        Assert.Null(SceneLogic.Neighbour(scenes, "X", 1));
    }

    // ---------- Nummerering ----------

    [Fact]
    public void NextNumberCountsOnlyTheScene()
    {
        var cues = new List<Cue> { C("A", "4"), C("B", "9"), C("A", "2.5") };
        Assert.Equal("5", SceneLogic.NextNumber(cues, "A"));
        Assert.Equal("10", SceneLogic.NextNumber(cues, "B"));
        Assert.Equal("1", SceneLogic.NextNumber(cues, "C"));
    }

    [Fact]
    public void RenumberingASceneLeavesOtherScenesAndUpdatesTargets()
    {
        var a5 = C("Akt 1", "5");
        var a9 = C("Akt 1", "9");
        var fadeA = new FadeCue { Scene = "Akt 1", Target = "9" };
        var b5 = C("Akt 2", "5");
        var fadeB = new FadeCue { Scene = "Akt 2", Target = "Akt 1:9" };
        var fadeB2 = new FadeCue { Scene = "Akt 2", Target = "5" };
        var groupA = new GroupCue { Scene = "Akt 1", Targets = "5, 9, Akt 2:5" };
        var cues = new List<Cue> { a5, a9, fadeA, b5, fadeB, fadeB2, groupA };
        var map = SceneLogic.Renumber(cues, "Akt 1");
        Assert.Equal(new[] { "1", "2", "3" }, new[] { a5.Number, a9.Number, fadeA.Number });
        Assert.Equal("4", groupA.Number);
        Assert.Equal("5", b5.Number);
        Assert.Equal("2", fadeA.Target);
        Assert.Equal("Akt 1:2", fadeB.Target);
        Assert.Equal("5", fadeB2.Target);
        Assert.Equal("1, 2, Akt 2:5", groupA.Targets);
        Assert.Equal("1", map["5"]);
    }

    [Fact]
    public void RenumberingWithoutScenesWorksAsBefore()
    {
        var a = C("", "10");
        var b = C("", "20");
        var go = new GotoCue { Target = "20" };
        var cues = new List<Cue> { a, b, go };
        SceneLogic.Renumber(cues, "");
        Assert.Equal(new[] { "1", "2", "3" }, Numbers(cues));
        Assert.Equal("2", go.Target);
    }

    [Fact]
    public void MovingCuesToAnotherSceneAppendsThemAndAvoidsDuplicateNumbers()
    {
        var a1 = C("Akt 1", "1");
        var a2 = C("Akt 1", "2");
        var b1 = C("Akt 2", "1");
        var cues = new List<Cue> { a1, a2, b1 };
        SceneLogic.MoveToScene(cues, new[] { a1 }, "Akt 2");
        Assert.Equal(new Cue[] { a2, b1, a1 }, cues);
        Assert.Equal("Akt 2", a1.Scene);
        Assert.Equal("2", a1.Number); // 1 var opptatt i Akt 2
        Assert.Equal(3, SceneLogic.EndOfScene(cues, "Akt 2"));
        Assert.Equal(1, SceneLogic.EndOfScene(cues, "Akt 1"));
        Assert.Equal(3, SceneLogic.EndOfScene(cues, "Ukjent"));
    }

    // ---------- GO-markør ----------

    [Fact]
    public void GoMovesWithinTheSceneAndSkipsChainsAndDisabled()
    {
        var x1 = C("A", "1");
        var y1 = C("B", "1");
        var x2 = C("A", "2");
        x2.ContinueMode = ContinueMode.AutoContinue;
        var x3 = C("A", "3");
        var x4 = C("A", "4");
        var list = new List<Cue> { x1, y1, x2, x3, x4 };
        Assert.Same(x2, SceneLogic.NextPlayhead(list, x1));
        Assert.Same(x4, SceneLogic.NextPlayhead(list, x2)); // x2 starter x3 automatisk
        x2.ContinueMode = ContinueMode.None;
        x2.Disabled = true;
        Assert.Same(x3, SceneLogic.NextPlayhead(list, x1));
    }

    [Fact]
    public void GoNeverJumpsToAnotherSceneAtTheEnd()
    {
        var x1 = C("A", "1");
        var x2 = C("A", "2");
        var y1 = C("B", "1");
        var list = new List<Cue> { x1, x2, y1 };
        Assert.Null(SceneLogic.NextPlayhead(list, x2));
        Assert.Null(SceneLogic.NextEnabledAfter(list, x2));
        Assert.Same(y1, SceneLogic.FirstEnabled(list, "B"));
    }

    [Fact]
    public void StepAndMoveStayInTheScene()
    {
        var x1 = C("A", "1");
        var y1 = C("B", "1");
        var x2 = C("A", "2");
        var list = new List<Cue> { x1, y1, x2 };
        var inA = SceneLogic.CuesIn(list, "A");
        Assert.Same(x1, SceneLogic.Step(inA, null, 1));
        Assert.Same(x2, SceneLogic.Step(inA, null, -1));
        Assert.Same(x2, SceneLogic.Step(inA, x1, 1));
        Assert.Same(x2, SceneLogic.Step(inA, x2, 1)); // stopper i enden
        Assert.Equal(2, SceneLogic.NeighbourIndex(list, 0, 1));
        Assert.Equal(0, SceneLogic.NeighbourIndex(list, 2, -1));
        Assert.Equal(-1, SceneLogic.NeighbourIndex(list, 1, 1));
        Assert.Equal(-1, SceneLogic.NeighbourIndex(list, 0, -1));
    }

    // ---------- Oppslag av mål ----------

    private static List<Cue> TwoActs(out Cue a1, out Cue a2, out Cue b1, out Cue b2)
    {
        a1 = C("Akt 1", "1"); a2 = C("Akt 1", "2"); b1 = C("Akt 2", "1"); b2 = C("Akt 2", "2");
        return new List<Cue> { a1, b1, a2, b2 };
    }

    [Fact]
    public void NumbersAreFoundInTheHomeSceneFirstThenEverywhere()
    {
        var list = TwoActs(out var a1, out var a2, out var b1, out var b2);
        var only7 = C("Akt 2", "7");
        list.Add(only7);
        Assert.Same(b2, SceneLogic.FindByNumber(list, "2", "Akt 2"));
        Assert.Same(a2, SceneLogic.FindByNumber(list, "2", "Akt 1"));
        Assert.Same(only7, SceneLogic.FindByNumber(list, "7", "Akt 1")); // finnes ikke i hjemmescenen: søker alle
        Assert.Same(a1, SceneLogic.FindByNumber(list, "1", null));        // uten hjemmescene: første treff i lista
        Assert.Null(SceneLogic.FindByNumber(list, "99", "Akt 1"));
    }

    [Fact]
    public void QualifiedNumbersOnlyLookInThatScene()
    {
        var list = TwoActs(out var a1, out _, out var b1, out _);
        Assert.Same(b1, SceneLogic.FindByNumber(list, "akt 2:1", "Akt 1"));
        Assert.Same(a1, SceneLogic.FindByNumber(list, "Akt 1:1", "Akt 2"));
        Assert.Null(SceneLogic.FindByNumber(list, "Akt 1:7", "Akt 2")); // scenen finnes, men ikke nummeret
        Assert.Null(SceneLogic.FindByNumber(list, "Akt 3:1", "Akt 2")); // scenen finnes ikke
    }

    [Fact]
    public void GroupTargetsUseTheGroupsSceneFirst()
    {
        var list = TwoActs(out var a1, out var a2, out var b1, out var b2);
        var group = new GroupCue { Scene = "Akt 2", Targets = "1, 2" };
        list.Add(group);
        Assert.Equal(new Cue[] { b1, b2 }, CueTargets.Resolve(list, group.Targets, group));
        group.Targets = "Akt 1:1, 2";
        Assert.Equal(new Cue[] { a1, b2 }, CueTargets.Resolve(list, group.Targets, group));
        Assert.Equal(new Cue[] { a1, a2 }, CueTargets.Resolve(list, "1-2", null, null, "Akt 1"));
        var missing = new List<string>();
        CueTargets.Resolve(list, "Akt 3:1, 9", group, missing);
        Assert.Equal(new[] { "Akt 3:1", "9" }, missing);
    }

    [Fact]
    public void GotoSpecAndResolveGoto()
    {
        Assert.Equal("5", SceneLogic.GotoSpec("5", ""));
        Assert.Equal("Akt 2:5", SceneLogic.GotoSpec("5", "Akt 2"));
        Assert.Equal("Akt 2:", SceneLogic.GotoSpec("", "Akt 2"));
        Assert.Equal("Akt 1:5", SceneLogic.GotoSpec("Akt 1:5", "Akt 2"));

        var list = TwoActs(out _, out _, out _, out var b2);
        var scenes = new[] { "Akt 1", "Akt 2" };
        var scene = SceneLogic.ResolveGoto(list, scenes, "akt 2:", "Akt 1");
        Assert.Equal("Akt 2", scene!.Scene);
        Assert.Null(scene.Cue);
        Assert.Null(SceneLogic.ResolveGoto(list, scenes, "Akt 3:", "Akt 1"));
        Assert.Same(b2, SceneLogic.ResolveGoto(list, scenes, "Akt 2:2", "Akt 1")!.Cue);
        Assert.Equal("Akt 2", SceneLogic.ResolveGoto(list, scenes, "Akt 2:2", "Akt 1")!.Scene);
        Assert.Null(SceneLogic.ResolveGoto(list, scenes, "", "Akt 1"));
    }

    [Fact]
    public void ShowsWithoutScenesResolveAsBefore()
    {
        var list = new List<Cue> { C("", "1"), C("", "2"), C("", "3") };
        var group = new GroupCue { Targets = "3, 1-2" };
        Assert.Equal(new[] { "3", "1", "2" }, Numbers(CueTargets.Resolve(list, group.Targets, group)));
        Assert.Same(list[1], SceneLogic.FindByNumber(list, "2", ""));
    }

    // ---------- Showfil ----------

    [Fact]
    public void ScenesSurviveSavingAndLoading()
    {
        var doc = new ShowDocument();
        doc.Cues.Add(new GotoCue { Scene = "Akt 1", Number = "1", Target = "2", TargetScene = "Akt 2" });
        doc.Cues.Add(C("Akt 2", "2"));
        doc.Scenes = new List<string> { "Akt 1", "Akt 2" };
        var json = doc.ToJson();
        Assert.Contains("\"Scenes\"", json);
        var back = JsonSerializer.Deserialize<ShowDocument>(json, ShowDocument.JsonOptions)!;
        back.NormalizeScenes();
        Assert.Equal(new[] { "Akt 1", "Akt 2" }, back.Scenes);
        Assert.Equal(new[] { "Akt 1", "Akt 2" }, back.Cues.Select(c => c.Scene));
        var go = (GotoCue)back.Cues[0];
        Assert.Equal("Akt 2", go.TargetScene);
        Assert.Equal("Akt 2:2", SceneLogic.GotoSpec(go.Target, go.TargetScene));
    }

    [Fact]
    public void OldShowsLoadAsOneScene()
    {
        var doc = JsonSerializer.Deserialize<ShowDocument>("{\"Cues\":[{\"type\":\"stop\",\"Number\":\"1\"},{\"type\":\"stop\",\"Number\":\"2\"}]}", ShowDocument.JsonOptions)!;
        doc.NormalizeScenes();
        Assert.Empty(doc.Scenes);
        Assert.All(doc.Cues, c => Assert.Equal("", c.Scene));
    }

    [Fact]
    public void BrokenSceneListsAreCleanedWhenLoaded()
    {
        var doc = JsonSerializer.Deserialize<ShowDocument>("{\"Scenes\":[\"A\",null,\"a\",\"\",\"B\"],\"Cues\":[{\"type\":\"stop\",\"Scene\":\"C\"},{\"type\":\"stop\"}]}", ShowDocument.JsonOptions)!;
        doc.NormalizeScenes();
        Assert.Equal(new[] { "A", "B", "C" }, doc.Scenes);
        Assert.Equal(new[] { "C", "A" }, doc.Cues.Select(c => c.Scene));
    }

    // ---------- Forhåndssjekk ----------

    private static List<PreflightFinding> Check(List<Cue> cues, params string[] scenes) =>
        PreflightCheck.Run(cues, new AppSettings(), new NoEnv(), scenes);

    [Fact]
    public void PreflightAcceptsTheSameNumberInDifferentScenes()
    {
        var f = Check(new List<Cue> { C("Akt 1", "1"), C("Akt 2", "1") }, "Akt 1", "Akt 2");
        Assert.DoesNotContain(f, x => x.Code == PreflightCheck.NumberDuplicate);
        Assert.Contains(f, x => x.Code == PreflightCheck.NumbersOk);
    }

    [Fact]
    public void PreflightReportsDuplicatesInsideOneScene()
    {
        var f = Check(new List<Cue> { C("Akt 1", "1"), C("Akt 2", "1"), C("Akt 1", "1") }, "Akt 1", "Akt 2");
        Assert.Single(f, x => x.Code == PreflightCheck.NumberDuplicate);
    }

    [Fact]
    public void PreflightChecksGotoScenes()
    {
        var target = C("Akt 2", "1");
        var onlyScene = new GotoCue { Scene = "Akt 1", Number = "5", TargetScene = "Akt 2" };
        var withNumber = new GotoCue { Scene = "Akt 1", Number = "6", TargetScene = "Akt 2", Target = "1" };
        var bad = new GotoCue { Scene = "Akt 1", Number = "7", TargetScene = "Finnes ikke" };
        var f = Check(new List<Cue> { target, onlyScene, withNumber, bad }, "Akt 1", "Akt 2");
        Assert.Equal(new[] { PreflightCheck.SceneMissing }, f.Where(x => x.Severity == PreflightSeverity.Error).Select(x => x.Code));
    }

    [Fact]
    public void PreflightFindsTargetsInTheHomeSceneAndElsewhere()
    {
        var fade = new FadeCue { Scene = "Akt 2", Number = "9", Target = "1" };
        var f = Check(new List<Cue> { C("Akt 1", "1"), fade }, "Akt 1", "Akt 2");
        Assert.DoesNotContain(f, x => x.Code == PreflightCheck.TargetMissing); // finnes i en annen scene
        fade.Target = "Akt 2:1";
        f = Check(new List<Cue> { C("Akt 1", "1"), fade }, "Akt 1", "Akt 2");
        Assert.Contains(f, x => x.Code == PreflightCheck.TargetMissing); // finnes ikke i Akt 2
    }
}
