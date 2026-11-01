using NAudio.Wave;
using ShowCue.Engine;
using ShowCue.Models;
using static ShowCue.Tests.TestAudio;

namespace ShowCue.Tests;

/// <summary>Lydmotoren: routing, patch, fader, start/slutt og loop. Kjøres uten lydkort (mikseren leses direkte).</summary>
public class AudioEngineTests
{
    private readonly PatchMixer _mixer = new(48000, 8, PatchBus.DefaultPatch(8));
    private static readonly string Stereo = ConstWav("stereo.wav", 48000, 2, 1.0);

    private void StopAndDrain(CuePlayback pb) { pb.Stop(0); Run(_mixer, 256); }

    [Fact]
    public void StereoDefaultRouting()
    {
        var pb = new CuePlayback(new AudioCue { FilePath = Stereo }, 48000);
        _mixer.Add(pb);
        var o = Run(_mixer, 1000);
        Near(0.1, At(o, 8, 500, 1)); Near(0.2, At(o, 8, 500, 2)); Near(0, At(o, 8, 500, 3));
        StopAndDrain(pb);
    }

    [Fact]
    public void MonoGoesToBothSides()
    {
        var pb = new CuePlayback(new AudioCue { FilePath = ConstWav("mono.wav", 48000, 1, 1.0) }, 48000);
        _mixer.Add(pb);
        var o = Run(_mixer, 500);
        Near(0.1, At(o, 8, 100, 1)); Near(0.1, At(o, 8, 100, 2)); Near(0, At(o, 8, 100, 3));
        StopAndDrain(pb);
    }

    [Fact]
    public void CustomRoutingResamplingPatchAndMeters()
    {
        var quad = ConstWav("quad441.wav", 44100, 4, 1.0);
        var pb = new CuePlayback(new AudioCue { FilePath = quad, Routes = new() { new(3, 5, -6.0206), new(4, 8) } }, 48000);
        _mixer.Add(pb);
        var o = Run(_mixer, 4800);
        Assert.Equal(4, pb.FileChannels);
        Near(0.15, At(o, 8, 2000, 5), 0.005); Near(0.4, At(o, 8, 2000, 8), 0.005); Near(0, At(o, 8, 2000, 1));

        pb.SetRouting(new List<AudioRoute> { new(1, 3) }); // endre routing mens den spiller
        o = Run(_mixer, 480);
        Near(0.1, At(o, 8, 200, 3), 0.005); Near(0, At(o, 8, 200, 5));

        var p2 = PatchBus.DefaultPatch(8);
        p2[2].Channels = new() { 7, 8 };
        p2[2].GainDb = 6.0206;
        _mixer.SetPatch(p2); // buss 3 → kanal 7 og 8 med +6 dB
        o = Run(_mixer, 480);
        Near(0.2, At(o, 8, 200, 7), 0.005); Near(0.2, At(o, 8, 200, 8), 0.005); Near(0, At(o, 8, 200, 3));
        Near(0.2, _mixer.BusLevels[2]); Near(0.2, _mixer.ChannelLevels[6]);

        _mixer.SetPatch(PatchBus.DefaultPatch(8));
        StopAndDrain(pb);
        Assert.True(_mixer.BusLevels[2] < 0.2f, "måleren faller etter stopp");

        var (d, ch) = AudioEngine.Probe(quad);
        Near(1.0, d); Assert.Equal(4, ch);
    }

    [Fact]
    public void FadeOutAndStop()
    {
        var pb = new CuePlayback(new AudioCue { FilePath = Stereo }, 48000);
        bool finished = false;
        pb.Finished += _ => finished = true;
        _mixer.Add(pb);
        Run(_mixer, 480);
        pb.Stop(0.1); // 4800 rammer
        var mid = At(Run(_mixer, 2400), 8, 2399, 1);
        Assert.InRange(mid, 0.0001f, 0.0999f);
        var o = Run(_mixer, 4800);
        Assert.True(finished);
        Near(0, At(o, 8, 4000, 1));
    }

    [Fact]
    public void FadeToLevelKeepsPlaying()
    {
        var pb = new CuePlayback(new AudioCue { FilePath = Stereo }, 48000);
        _mixer.Add(pb);
        pb.FadeTo(-20, 0.05, false);
        var o = Run(_mixer, 4800);
        Near(0.01, At(o, 8, 4700, 1), 0.0005);
        Assert.False(pb.IsFinished);
        StopAndDrain(pb);
    }

    [Fact]
    public void StartEndAndLoop()
    {
        var pb = new CuePlayback(new AudioCue { FilePath = Stereo, StartTime = 0.2, EndTime = 0.3, Loop = true }, 48000);
        _mixer.Add(pb);
        Near(0.1, pb.PassLength, 0.001);
        Run(_mixer, 24000);
        Assert.False(pb.IsFinished);
        Near(0.5, pb.Elapsed);
        Assert.True(pb.PassPosition < 0.1);
        StopAndDrain(pb);

        var oneShot = new CuePlayback(new AudioCue { FilePath = Stereo, EndTime = 0.25 }, 48000);
        _mixer.Add(oneShot);
        Run(_mixer, 24000);
        Assert.True(oneShot.IsFinished, "uten loop stopper den ved sluttpunktet");
    }

    [Fact]
    public void FadeIn()
    {
        var pb = new CuePlayback(new AudioCue { FilePath = Stereo, FadeIn = 0.1 }, 48000);
        _mixer.Add(pb);
        var o = Run(_mixer, 4800);
        Assert.True(At(o, 8, 10, 1) < 0.001);
        Near(0.1, At(o, 8, 4799, 1), 0.002);
        StopAndDrain(pb);
    }

    [Fact]
    public void AllOffIsSilent()
    {
        var pb = new CuePlayback(new AudioCue { FilePath = Stereo, Routes = new() }, 48000);
        _mixer.Add(pb);
        Assert.All(Run(_mixer, 480), v => Assert.Equal(0f, v));
        StopAndDrain(pb);
    }

    private sealed class BrokenSource : IBusSource
    {
        public bool Render(float[] b, int f, int c) => throw new InvalidOperationException();
    }

    [Fact]
    public void BrokenSourceDoesNotStopOthers()
    {
        _mixer.Add(new BrokenSource());
        var pb = new CuePlayback(new AudioCue { FilePath = Stereo }, 48000);
        _mixer.Add(pb);
        Near(0.1, At(Run(_mixer, 480), 8, 100, 1));
        StopAndDrain(pb);
    }

    [Fact]
    public void TestToneLevelAndBus()
    {
        var tt = new TestToneSource(3);
        _mixer.Add(tt);
        var o = Run(_mixer, 96000);
        double sum = 0; int n = 0;
        for (int f = 4800; f < 96000; f++) { var v = At(o, 8, f, 4); sum += v * v; n++; }
        Near(-18, 10 * Math.Log10(sum / n), 1.5);
        Assert.True(o.Where((v, i) => i % 8 != 3).All(v => v == 0), "testtonen går bare til kanal 4");
        tt.Stop();
    }

    [Fact]
    public void VideoAudioStreamWithRewind()
    {
        var pcm = Path16("pcm16.wav", 44100, 0.25f, 0.5f, 1.0);
        var vc = new VideoCue { FilePath = "x.mp4", VolumeDb = -6.0206, Routes = new() { new(1, 4), new(2, 6) } };
        var vpb = new CuePlayback(vc, new WaveFileReader(pcm), vc, new CuePlayback.Options(vc.VolumeDb, 0, 0, true, 0, 0), 48000);
        Assert.Equal(2, vpb.FileChannels);
        Assert.Same(vc, vpb.Cue);
        _mixer.Add(vpb);
        var o = Run(_mixer, 4800);
        Near(0.125, At(o, 8, 3000, 4), 0.003); Near(0.25, At(o, 8, 3000, 6), 0.003); Near(0, At(o, 8, 3000, 1));
        Run(_mixer, 9600);
        var before = vpb.PassPosition;
        vpb.RequestRewind();
        Run(_mixer, 480);
        Assert.True(before > 0.25 && vpb.PassPosition < 0.02, $"rewind {before:0.00} → {vpb.PassPosition:0.000}");
        StopAndDrain(vpb);
    }
}

/// <summary>Bølgeform og forhåndslytting i start/slutt-redigeringen.</summary>
public class WaveformTests
{
    [Fact]
    public void MinMaxPerBucket()
    {
        var path = Wav("wave.wav", 48000, 1, 96000, (f, _) => f < 48000 ? 0.5f : -0.25f);
        using var r = new AudioFileReader(path);
        var wd = WaveformData.Compute(r, r.Length / r.WaveFormat.BlockAlign, 100);
        Assert.Equal(100, wd.Buckets);
        Near(2.0, wd.Duration, 0.001);
        Near(0.5, wd.Max[10], 1e-4); Near(0, wd.Min[10], 1e-4);
        Near(-0.25, wd.Min[70], 1e-4); Near(0, wd.Max[70], 1e-4);

        var cached = WaveformCache.Get(path, false).Result;
        Assert.NotNull(cached);
        Near(2.0, cached!.Duration, 0.001);
        Assert.Same(cached, WaveformCache.Get(path, false).Result);
    }

    [Fact]
    public void PreviewThroughPatch()
    {
        var path = Wav("ramp.wav", 48000, 1, 48000 * 3, (f, _) => f / 48000f / 10f); // verdi = tid/10
        var pm = new PatchMixer(48000, 8, PatchBus.DefaultPatch(8));
        var prev = new AudioPreview(() => pm);
        var cue = new AudioCue { FilePath = path, StartTime = 0.5, EndTime = 2.0 };

        Assert.True(prev.Play(cue, 1.5, cue.EndTime));
        var o = Run(pm, 1000);
        Near((1.5 + 900 / 48000.0) / 10, At(o, 8, 900, 1), 0.002);
        Near((1.5 + 900 / 48000.0) / 10, At(o, 8, 900, 2), 0.002);
        Assert.True(prev.IsPlaying);
        Near(1.5 + 1000 / 48000.0, prev.Position ?? 0, 0.002);

        Run(pm, 48000);
        Assert.False(prev.IsPlaying, "stopper ved sluttpunktet");
        Assert.Null(prev.Position);

        prev.Play(cue, 2.5, cue.EndTime); // etter sluttpunktet: spiller til filslutt
        Run(pm, 1000);
        Assert.True(prev.IsPlaying);
        Near((2.5 + 1050 / 48000.0) / 10, At(Run(pm, 100), 8, 50, 1), 0.002);

        prev.Stop();
        Run(pm, 2000);
        Near(0, At(Run(pm, 100), 8, 50, 1), 1e-6);
    }
}
