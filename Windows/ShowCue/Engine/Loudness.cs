using NAudio.Wave;

namespace ShowCue.Engine;

/// <summary>Integrert lydstyrke (LUFS) etter ITU-R BS.1770-4 / EBU R128:
/// K-vekting, blokker på 400 ms med 75 % overlapp, absolutt port på −70 LUFS og relativ port 10 LU under snittet.</summary>
public sealed class LoudnessMeter
{
    private readonly int _channels;
    private readonly Biquad[] _shelf, _highpass;
    private readonly double[] _weights;
    private readonly int _step;            // 100 ms
    private readonly double[] _sum;        // kvadratsum per kanal i inneværende 100 ms
    private int _inStep;
    private readonly Queue<double[]> _last = new(); // de fire siste 100 ms-delene
    private readonly List<double> _blocks = new();   // effekt per 400 ms-blokk

    public LoudnessMeter(int channels, int sampleRate)
    {
        _channels = Math.Max(1, channels);
        _shelf = new Biquad[_channels];
        _highpass = new Biquad[_channels];
        for (var c = 0; c < _channels; c++) { _shelf[c] = Biquad.Shelf(sampleRate); _highpass[c] = Biquad.HighPass(sampleRate); }
        _weights = Enumerable.Range(0, _channels).Select(c => Weight(c, _channels)).ToArray();
        _step = Math.Max(1, sampleRate / 10);
        _sum = new double[_channels];
    }

    /// <summary>Kanalvekter: 1 for front, 1,41 for surround, 0 for LFE i 5.1 (L R C LFE Ls Rs).</summary>
    private static double Weight(int c, int n)
    {
        if (n == 6) return c == 3 ? 0 : c >= 4 ? 1.41 : 1;
        if (n == 5) return c >= 3 ? 1.41 : 1;
        return 1;
    }

    /// <summary>Legg til interleaved samples.</summary>
    public void Add(ReadOnlySpan<float> interleaved)
    {
        var frames = interleaved.Length / _channels;
        for (var f = 0; f < frames; f++)
        {
            for (var c = 0; c < _channels; c++)
            {
                var y = _highpass[c].Process(_shelf[c].Process(interleaved[f * _channels + c]));
                _sum[c] += y * y;
            }
            if (++_inStep == _step) EndStep();
        }
    }

    private void EndStep()
    {
        _last.Enqueue((double[])_sum.Clone());
        Array.Clear(_sum);
        _inStep = 0;
        if (_last.Count > 4) _last.Dequeue();
        if (_last.Count < 4) return;
        double power = 0;
        for (var c = 0; c < _channels; c++) power += _weights[c] * _last.Sum(s => s[c]) / (4.0 * _step);
        _blocks.Add(power);
    }

    private static double Lufs(double power) => -0.691 + 10 * Math.Log10(power);

    /// <summary>Integrert lydstyrke, eller null hvis lyden er for kort (under 400 ms) eller stille.</summary>
    public double? Integrated
    {
        get
        {
            var abs = _blocks.Where(p => p > 0 && Lufs(p) > -70).ToList();
            if (abs.Count == 0) return null;
            var relGate = Lufs(abs.Average()) - 10;
            var gated = abs.Where(p => Lufs(p) > relGate).ToList();
            return gated.Count == 0 ? null : Lufs(gated.Average());
        }
    }

    /// <summary>Måler en lydfil mellom start og slutt (sekunder, slutt 0 = filslutt).</summary>
    public static double? MeasureFile(string path, double start = 0, double end = 0, CancellationToken cancel = default)
    {
        using var reader = new AudioFileReader(path);
        var fmt = reader.WaveFormat;
        if (start > 0) reader.CurrentTime = TimeSpan.FromSeconds(start);
        var meter = new LoudnessMeter(fmt.Channels, fmt.SampleRate);
        long remaining = end > start ? (long)((end - start) * fmt.SampleRate) * fmt.Channels : long.MaxValue;
        var buf = new float[fmt.SampleRate / 10 * fmt.Channels];
        while (remaining > 0 && !cancel.IsCancellationRequested)
        {
            var n = reader.Read(buf, 0, (int)Math.Min(buf.Length, remaining));
            if (n <= 0) break;
            meter.Add(buf.AsSpan(0, n));
            remaining -= n;
        }
        return cancel.IsCancellationRequested ? null : meter.Integrated;
    }

    /// <summary>Nivået (dB) som gir målet, begrenset til cuens område −60…+12 dB.</summary>
    public static double GainFor(double measured, double target) => Math.Round(Math.Clamp(target - measured, -60, 12), 1);

    private struct Biquad
    {
        private double _b0, _b1, _b2, _a1, _a2, _z1, _z2;

        public double Process(double x)
        {
            var y = _b0 * x + _z1;
            _z1 = _b1 * x - _a1 * y + _z2;
            _z2 = _b2 * x - _a2 * y;
            return y;
        }

        // Koeffisientene regnes ut for hver samplingsfrekvens (samme formler som libebur128),
        // og gir nøyaktig BS.1770-verdiene ved 48 kHz.
        public static Biquad Shelf(double rate)
        {
            const double f0 = 1681.974450955533, g = 3.999843853973347, q = 0.7071752369554196;
            var k = Math.Tan(Math.PI * f0 / rate);
            var vh = Math.Pow(10, g / 20);
            var vb = Math.Pow(vh, 0.4996667741545416);
            var a0 = 1 + k / q + k * k;
            return new Biquad
            {
                _b0 = (vh + vb * k / q + k * k) / a0, _b1 = 2 * (k * k - vh) / a0, _b2 = (vh - vb * k / q + k * k) / a0,
                _a1 = 2 * (k * k - 1) / a0, _a2 = (1 - k / q + k * k) / a0,
            };
        }

        public static Biquad HighPass(double rate)
        {
            const double f0 = 38.13547087602444, q = 0.5003270373238773;
            var k = Math.Tan(Math.PI * f0 / rate);
            var a0 = 1 + k / q + k * k;
            return new Biquad { _b0 = 1, _b1 = -2, _b2 = 1, _a1 = 2 * (k * k - 1) / a0, _a2 = (1 - k / q + k * k) / a0 };
        }
    }
}
