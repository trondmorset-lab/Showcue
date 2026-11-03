using System.Text.Json;
using ShowCue.Engine;
using ShowCue.Models;
using static ShowCue.Tests.TestAudio;

namespace ShowCue.Tests;

/// <summary>Pause, slipp loop, start fra tid, fadekurver og de nye cuetypene.</summary>
public class PlaybackTests
{
    private readonly PatchMixer _mixer = new(48000, 8, PatchBus.DefaultPatch(8));
    /// <summary>Stereo, verdi = tid/10 på begge kanaler, 3 sekunder.</summary>
    private static readonly string Ramp = Wav("pb-ramp.wav", 48000, 2, 48000 * 3, (f, _) => f / 48000f / 10f);

    [Fact]
    public void PauseHoldsPositionAndResumeContinues()
    {
        var pb = new CuePlayback(new AudioCue { FilePath = Ramp }, 48000);
        _mixer.Add(pb);
        Run(_mixer, 4800);
        var pos = pb.PassPosition;
        pb.Pause();
        var o = Run(_mixer, 9600);
        Assert.All(o, v => Assert.Equal(0f, v));
        Assert.Equal(pos, pb.PassPosition);
        pb.Resume();
        o = Run(_mixer, 100);
        Near((0.1 + 50 / 48000.0) / 10, At(o, 8, 50, 1), 0.001); // fortsetter fra 0,1 s
        pb.Stop(0); Run(_mixer, 64);
    }

    [Fact]
    public void StartOffset()
    {
        var pb = new CuePlayback(new AudioCue { FilePath = Ramp, StartTime = 0.5 }, 48000, offset: 1.0);
        _mixer.Add(pb);
        var o = Run(_mixer, 2000);
        Near((1.5 + 1500 / 48000.0) / 10, At(o, 8, 1500, 1), 0.002);
        pb.Stop(0); Run(_mixer, 64);
    }

    [Fact]
    public void ReleaseLoopPlaysPastTheEndPoint()
    {
        var pb = new CuePlayback(new AudioCue { FilePath = Ramp, StartTime = 0.5, EndTime = 1.0, Loop = true }, 48000);
        _mixer.Add(pb);
        Run(_mixer, 48000);          // 2 runder à 0,5 s
        Assert.True(pb.IsLooping);
        Near(0.5, pb.PassPosition, 0.01); // ved slutten av runde 2 (fila står på 1,0 s)
        pb.ReleaseLoop();
        Assert.False(pb.IsLooping);
        Run(_mixer, 36000);          // 0,75 s: forbi det gamle sluttpunktet (1,0 s i fila)
        Assert.False(pb.IsFinished);
        var o = Run(_mixer, 100);
        Near((1.0 + 0.75 + 50 / 48000.0) / 10, At(o, 8, 50, 1), 0.002);
        Run(_mixer, 48000 * 2);      // resten av fila
        Assert.True(pb.IsFinished);
    }

    [Theory]
    [InlineData(FadeCurve.Db, 0.5, -40)]
    [InlineData(FadeCurve.SCurve, 0.5, -40)]
    [InlineData(FadeCurve.SCurve, 0.1, -2.240)]
    [InlineData(FadeCurve.Linear, 0.5, -6.0206)]
    public void FadeCurves(FadeCurve curve, double p, double expectedDb)
        => Near(expectedDb, CuePlayback.CurveDb(curve, 0, curve == FadeCurve.Linear ? CuePlayback.SilenceDb : -80, p), 0.01);

    [Fact]
    public void CurveEndpoints()
    {
        foreach (var c in Enum.GetValues<FadeCurve>())
        {
            Near(0, CuePlayback.CurveDb(c, 0, -20, 0), 1e-9);
            Near(-20, CuePlayback.CurveDb(c, 0, -20, 1), 1e-4);
        }
    }

    [Fact]
    public void FadeToWithSCurveIsGentleAtTheStart()
    {
        var lin = new CuePlayback(new AudioCue { FilePath = Ramp, StartTime = 2 }, 48000);
        var s = new CuePlayback(new AudioCue { FilePath = Ramp, StartTime = 2 }, 48000);
        var m1 = new PatchMixer(48000, 2, PatchBus.DefaultPatch(2));
        var m2 = new PatchMixer(48000, 2, PatchBus.DefaultPatch(2));
        m1.Add(lin); m2.Add(s);
        lin.FadeTo(-40, 1, false, FadeCurve.Db);
        s.FadeTo(-40, 1, false, FadeCurve.SCurve);
        var a = Run(m1, 4800); var b = Run(m2, 4800); // 0,1 s inn i faden
        Assert.True(At(b, 2, 4799, 1) > At(a, 2, 4799, 1), "S-kurven har falt mindre etter 10 %");
    }

    [Fact]
    public void NewCueTypesRoundTrip()
    {
        var doc = new ShowDocument();
        doc.Cues.Add(new PlaylistCue { Files = new() { @"C:\m\a.mp3", @"C:\m\b.wav" }, Shuffle = true, Loop = false, Crossfade = 4, VolumeDb = -3, FadeOut = 5 });
        doc.Cues.Add(new GotoCue { Target = "12", Fire = true });
        doc.Cues.Add(new StopCue { Target = "3", Release = true });
        doc.Cues.Add(new FadeCue { Target = "1", Curve = FadeCurve.SCurve });
        var json = doc.ToJson();
        Assert.Contains("\"type\": \"playlist\"", json);
        Assert.Contains("\"type\": \"goto\"", json);
        var back = JsonSerializer.Deserialize<ShowDocument>(json, ShowDocument.JsonOptions)!;
        var p = Assert.IsType<PlaylistCue>(back.Cues[0]);
        Assert.Equal(new[] { @"C:\m\a.mp3", @"C:\m\b.wav" }, p.Files);
        Assert.True(p.Shuffle && !p.Loop && p.Crossfade == 4 && p.VolumeDb == -3 && p.FadeOut == 5);
        var g = Assert.IsType<GotoCue>(back.Cues[1]);
        Assert.True(g.Target == "12" && g.Fire);
        Assert.True(Assert.IsType<StopCue>(back.Cues[2]).Release);
        Assert.Equal(FadeCurve.SCurve, Assert.IsType<FadeCue>(back.Cues[3]).Curve);
        Assert.Equal(json, back.ToJson());
        Assert.Contains("slipp loop", back.Cues[2].Summary);
        Assert.Equal(0, back.Cues[2].Duration);
    }

    [Fact]
    public void SeekJumpsWhilePlayingAndWhilePaused()
    {
        var pb = new CuePlayback(new AudioCue { FilePath = Ramp }, 48000);
        _mixer.Add(pb);
        Run(_mixer, 4800);
        pb.Seek(2.0);
        var o = Run(_mixer, 100);
        Near((2.0 + 50 / 48000.0) / 10, At(o, 8, 50, 1), 0.001);
        Near(3.0, pb.SeekLength, 0.001);

        pb.Pause();
        pb.Seek(0.5);
        Run(_mixer, 480);
        Near(0.5, pb.PassPosition, 0.001); // spolingen skjer også under pause
        pb.Resume();
        o = Run(_mixer, 100);
        Near((0.5 + 50 / 48000.0) / 10, At(o, 8, 50, 1), 0.001);

        pb.Seek(99); // forbi slutten: hopper til slutten, og cuen blir ferdig
        Run(_mixer, 64);
        Assert.True(pb.IsFinished);
    }

    [Fact]
    public void SeekingBackOutOfTheAutoFadeRestoresTheLevel()
    {
        var pb = new CuePlayback(new AudioCue { FilePath = Ramp, FadeOut = 1 }, 48000);
        _mixer.Add(pb);
        pb.Seek(2.5); // inne i fade ut-en mot slutten
        Run(_mixer, 48000 / 4);
        pb.Seek(0.5);
        Run(_mixer, 48000 / 10); // kort rampe tilbake til fullt nivå
        var o = Run(_mixer, 100);
        Near((0.6 + 50 / 48000.0) / 10, At(o, 8, 50, 1), 0.002);
        pb.Stop(0); Run(_mixer, 64);
    }
}
