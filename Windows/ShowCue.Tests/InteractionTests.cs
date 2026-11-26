using ShowCue.Engine;
using ShowCue.Models;

namespace ShowCue.Tests;

/// <summary>Regresjonstester for feil som oppstod der de sammenslåtte funksjonene møtes (scener, øvelsesmodus, forhåndslasting,
/// «gå til», live-inngang). Samme tilfeller finnes i Mac-versjonen (InteractionTests.swift).</summary>
public class InteractionTests
{
    private static AudioCue A(string scene, string number, string path, ContinueMode mode = ContinueMode.None, bool skip = false, bool disabled = false) =>
        new() { Scene = scene, Number = number, FilePath = path, ContinueMode = mode, SkipInRehearsal = skip, Disabled = disabled };
    private static string[] Paths(List<PreloadItem> items) => items.Select(i => i.Path).ToArray();

    // ---------- «Gå til» ----------

    [Fact]
    public void SceneOnlyGotoTargetIsResolvedByResolveGotoNotByNumberLookup()
    {
        // Feilen: hovedvinduet slo først opp målet «Akt 2:» som et cuenummer, fant ingenting og ga «fant ikke cue».
        var list = new List<Cue> { A("Akt 1", "1", "a.wav"), A("Akt 2", "1", "b.wav") };
        var spec = SceneLogic.GotoSpec("", "Akt 2");
        Assert.Equal("Akt 2:", spec);
        Assert.Null(SceneLogic.FindByNumber(list, spec, "Akt 1"));
        var t = SceneLogic.ResolveGoto(list, new[] { "Akt 1", "Akt 2" }, spec, "Akt 1");
        Assert.NotNull(t);
        Assert.Equal("Akt 2", t!.Scene);
        Assert.Null(t.Cue);
    }

    [Fact]
    public void GotoLoopGuardStopsARunawayChainButNotSlowLoops()
    {
        var g = new GotoLoopGuard();
        long t = 1000;
        int allowed = 0;
        for (int i = 0; i < 1000; i++) { if (g.Allow(t)) allowed++; t += 1; } // 1 ms mellom hver: løpsk
        Assert.Equal(GotoLoopGuard.MaxBurst, allowed);
        // En pause lenger enn vinduet slipper igjennom igjen.
        t += GotoLoopGuard.WindowMs + 1;
        Assert.True(g.Allow(t));

        var slow = new GotoLoopGuard();
        for (int i = 0; i < 500; i++) Assert.True(slow.Allow(i * 1000L)); // én gang i sekundet: aldri stoppet
    }

    [Fact]
    public void GotoLoopGuardSurvivesClockGoingBackwardsAndReset()
    {
        var g = new GotoLoopGuard();
        for (int i = 0; i < 100; i++) g.Allow(5000);
        Assert.False(g.Allow(5000));
        Assert.True(g.Allow(10)); // tiden gikk bakover: tell på nytt
        g.Reset();
        for (int i = 0; i < GotoLoopGuard.MaxBurst; i++) Assert.True(g.Allow(7));
        Assert.False(g.Allow(7));
        Assert.True(new GotoLoopGuard().Allow(long.MinValue));
        Assert.True(new GotoLoopGuard().Allow(long.MaxValue));
    }

    // ---------- Forhåndslasting x scener x øvelsesmodus ----------

    [Fact]
    public void PreloadChainNeverLeavesTheScene()
    {
        // Feilen: planen fulgte «fortsett automatisk» over i neste scene, som GO og motoren aldri gjør.
        var list = new List<Cue>
        {
            A("Akt 1", "1", "a1.wav", ContinueMode.AutoContinue),
            A("Akt 2", "1", "b1.wav"),
            A("Akt 1", "2", "a2.wav"),
        };
        Assert.Equal(new[] { "a1.wav", "a2.wav" }, Paths(PreloadPlanner.Plan(list, 0)));
        // Siste cue i sin scene med fortsett automatisk: ingenting etter den.
        var tail = new List<Cue> { A("Akt 1", "1", "a1.wav", ContinueMode.AutoContinue), A("Akt 2", "1", "b1.wav") };
        Assert.Equal(new[] { "a1.wav" }, Paths(PreloadPlanner.Plan(tail, 0)));
    }

    [Fact]
    public void PreloadGotoFollowsTheTargetInTheRightScene()
    {
        var go = new GotoCue { Scene = "Akt 1", Number = "9", Target = "1", TargetScene = "Akt 2" };
        var list = new List<Cue> { A("Akt 1", "1", "a1.wav"), go, A("Akt 2", "1", "b1.wav") };
        Assert.Equal(new[] { "b1.wav" }, Paths(PreloadPlanner.Plan(list, 1)));
        // Uten scene-angivelse søkes hjemmescenen først.
        go.TargetScene = "";
        Assert.Equal(new[] { "a1.wav" }, Paths(PreloadPlanner.Plan(list, 1)));
        // Bare scenebytte: ingen fil å forberede, og aldri unntak.
        go.Target = ""; go.TargetScene = "Akt 2";
        Assert.Empty(PreloadPlanner.Plan(list, 1));
    }

    [Fact]
    public void PreloadSkipsFlaggedCuesInRehearsalMode()
    {
        var list = new List<Cue>
        {
            A("", "1", "a.wav", skip: true), A("", "2", "b.wav", ContinueMode.AutoContinue, skip: true), A("", "3", "c.wav"), A("", "4", "d.wav"),
        };
        Assert.Equal(new[] { "a.wav" }, Paths(PreloadPlanner.Plan(list, 0)));
        Assert.Equal(new[] { "c.wav" }, Paths(PreloadPlanner.Plan(list, 0, PreloadPlanner.MaxItems, rehearsal: true)));
        // Auto-fortsett hopper over en merket cue, som motoren gjør.
        var chain = new List<Cue> { A("", "1", "a.wav", ContinueMode.AutoContinue), A("", "2", "b.wav", skip: true), A("", "3", "c.wav") };
        Assert.Equal(new[] { "a.wav", "b.wav" }, Paths(PreloadPlanner.Plan(chain, 0)));
        Assert.Equal(new[] { "a.wav", "c.wav" }, Paths(PreloadPlanner.Plan(chain, 0, PreloadPlanner.MaxItems, rehearsal: true)));
    }

    // ---------- GO, auto-fortsett og scener x øvelsesmodus ----------

    [Fact]
    public void GoAndAutoContinueAgreeAcrossScenesAndRehearsal()
    {
        var list = new List<Cue>
        {
            A("A", "1", "", ContinueMode.AutoContinue), A("B", "1", ""), A("A", "2", "", skip: true), A("A", "3", ""), A("B", "2", ""),
        };
        var first = list[0];
        // Uten øvelsesmodus: A1 starter A2 selv, og GO-markøren står på A3.
        Assert.Equal("2", SceneLogic.NextEnabledAfter(list, first, false)!.Number);
        Assert.Equal("3", SceneLogic.NextPlayhead(list, first, false)!.Number);
        // Med øvelsesmodus hoppes A2 over: A1 starter A3, og scenen er til ende. Scene B røres aldri.
        Assert.Equal("3", SceneLogic.NextEnabledAfter(list, first, true)!.Number);
        Assert.Equal("A", SceneLogic.NextEnabledAfter(list, first, true)!.Scene);
        Assert.Null(SceneLogic.NextPlayhead(list, first, true));
    }

    // ---------- Live-inngang ----------

    [Fact]
    public void LiveRingNeverPassesOnNonFiniteSamples()
    {
        var ring = new LiveRing(1, 48000, 1);
        ring.Write(new[] { 0.5f, float.NaN, float.PositiveInfinity, float.NegativeInfinity, -0.25f });
        var dst = new float[5];
        ring.Read(dst, 0, 5);
        Assert.Equal(new[] { 0.5f, 0f, 0f, 0f, -0.25f }, dst);
    }
}
