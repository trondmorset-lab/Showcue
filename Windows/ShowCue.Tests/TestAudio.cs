using NAudio.Wave;
using ShowCue.Engine;

namespace ShowCue.Tests;

/// <summary>Hjelpere for lydtestene: lager testfiler og leser fra mikseren.</summary>
internal static class TestAudio
{
    private static readonly string Dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "showcue-tests", Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>Hver kanal har konstant verdi 0.1 × (kanal+1), så vi ser nøyaktig hvor den havner.</summary>
    public static string ConstWav(string name, int rate, int ch, double seconds)
        => Wav(name, rate, ch, (int)(rate * seconds), (_, c) => 0.1f * (c + 1));

    public static string Wav(string name, int rate, int ch, int frames, Func<int, int, float> value)
    {
        var path = Path.Combine(Dir, name);
        using var w = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(rate, ch));
        var buf = new float[frames * ch];
        for (int f = 0; f < frames; f++) for (int c = 0; c < ch; c++) buf[f * ch + c] = value(f, c);
        w.WriteSamples(buf, 0, buf.Length);
        return path;
    }

    public static string Path16(string name, int rate, float left, float right, double seconds)
    {
        var path = Path.Combine(Dir, name);
        using var w = new WaveFileWriter(path, new WaveFormat(rate, 16, 2));
        var fr = new float[(int)(rate * seconds) * 2];
        for (int i = 0; i < fr.Length; i += 2) { fr[i] = left; fr[i + 1] = right; }
        w.WriteSamples(fr, 0, fr.Length);
        return path;
    }

    public static float[] Run(PatchMixer m, int frames)
    {
        var b = new float[frames * m.DeviceChannels];
        Assert.Equal(b.Length, m.Read(b, 0, b.Length));
        return b;
    }

    /// <summary>Verdien på kanal <paramref name="ch1"/> (1-basert) i ramme <paramref name="frame"/>.</summary>
    public static float At(float[] buf, int deviceChannels, int frame, int ch1) => buf[frame * deviceChannels + ch1 - 1];

    public static void Near(double expected, double actual, double tol = 0.01)
        => Assert.True(Math.Abs(expected - actual) <= tol, $"Forventet {expected} ± {tol}, fikk {actual}");
}
