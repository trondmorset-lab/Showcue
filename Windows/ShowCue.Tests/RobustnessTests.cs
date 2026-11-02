using System.Text.Json;
using ShowCue.Engine;
using ShowCue.Models;
using static ShowCue.Tests.TestAudio;

namespace ShowCue.Tests;

/// <summary>Feil funnet med stresstesting: ugyldige verdier, ødelagte pakker og loop-grensetilfeller skal aldri
/// få lydtråden til å henge, gi NaN i lyden eller stoppe lagringen.</summary>
public class RobustnessTests
{
    [Fact]
    public void TinyLoopDoesNotHangTheAudioThread()
    {
        var oneSample = Wav("rb-one.wav", 48000, 1, 1, (_, _) => 0.5f);
        var mixer = new PatchMixer(48000, 2, PatchBus.DefaultPatch(2));
        var pb = new CuePlayback(new AudioCue { FilePath = oneSample, Loop = true }, 48000);
        mixer.Add(pb);
        var t = Task.Run(() => Run(mixer, 4800));
        Assert.True(t.Wait(TimeSpan.FromSeconds(5)), "lydtråden hang");
    }

    [Fact]
    public void NonFiniteValuesAreSanitized()
    {
        var c = new AudioCue { StartTime = double.NaN, EndTime = double.PositiveInfinity, FadeIn = -1, FadeOut = 1e300, VolumeDb = double.NaN, FileDuration = double.NaN };
        Assert.Equal(0, c.StartTime);
        Assert.Equal(0, c.EndTime); // uendelig regnes som ugyldig
        Assert.Equal(0, c.FadeIn);
        Assert.Equal(86400, c.FadeOut);
        Assert.Equal(0, c.VolumeDb);
        Assert.Equal(0, c.FileDuration);
        Assert.Equal(0, new AudioRoute(1, 1, double.NaN).GainDb);
        var doc = new ShowDocument();
        doc.Cues.Add(c);
        doc.Cues.Add(new FadeCue { TargetDb = double.NegativeInfinity, FadeTime = double.NaN });
        doc.ToJson(); // skal kunne lagres
    }

    [Fact]
    public void PlaybackNeverOutputsNaN()
    {
        var file = Wav("rb-s.wav", 48000, 2, 4800, (f, _) => 0.1f);
        var mixer = new PatchMixer(48000, 2, PatchBus.DefaultPatch(2));
        var pb = new CuePlayback(new AudioCue { FilePath = file, Loop = true }, 48000, offset: double.NaN);
        mixer.Add(pb);
        pb.FadeTo(double.NaN, double.PositiveInfinity, false);
        pb.AdjustLevel(double.NaN);
        Assert.All(Run(mixer, 2048), v => Assert.True(float.IsFinite(v)));
        pb.Stop(double.NaN);
        Assert.Equal(0f, PatchMixer.DbToGain(double.NaN));
    }

    [Fact]
    public void TruncatedOscPacketsAreIgnored()
    {
        var full = Osc.Encode("/cue/12/start", new List<object> { 1, "x" });
        for (int n = 0; n <= full.Length; n++)
            Osc.Decode(full[..n], n); // ingen unntak, uansett hvor pakken er kuttet
        var lying = Osc.Encode("/go", new List<object>());
        Osc.Decode(lying, lying.Length + 100); // pakken påstår at den er lengre enn den er
    }

    [Fact]
    public void ClockHandlesHugeAndInvalidTimes()
    {
        Assert.Equal("99:59:59", OscFeedback.Clock(1e12));
        Assert.Equal("0:00", OscFeedback.Clock(double.NaN));
        Assert.Equal("99:59:59", OscFeedback.Clock(double.PositiveInfinity));
        Assert.Equal("", TimeFormat.Format(double.NaN));
        Assert.Equal("99:59:59", TimeFormat.Format(1e15));
        Assert.Equal(0, LoudnessMeter.GainFor(double.NaN, -23));
    }

    [Fact]
    public void VeryLongShowNamesStillGetAutosaveAndBackups()
    {
        var root = Path.Combine(Path.GetTempPath(), "showcue-rb", Guid.NewGuid().ToString("N"));
        var show = Path.Combine(root, new string('x', 200) + ".showcue");
        Directory.CreateDirectory(root);
        File.WriteAllText(show, "{}");
        ShowSafety.WriteAutosave(show, "{}", root);
        Assert.NotNull(ShowSafety.PendingAutosave(show, root));
        Assert.NotNull(ShowSafety.Backup(show, root));
    }
}
