using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace ShowCue.Engine;

/// <summary>Ringbuffer for live-lyd: opptakstråden skriver, lydtråden leser. Holder forsinkelsen lav
/// ved å kaste det eldste når det hoper seg opp (f.eks. mens cuen er pauset).</summary>
public sealed class LiveRing
{
    private readonly float[] _buf;
    private readonly int _channels;
    private readonly int _maxFrames, _trimTo;
    private int _read, _count; // i samples
    private readonly object _lock = new();

    public int Channels => _channels;

    /// <param name="maxLatency">Mer enn dette (sekunder) i bufferen kastes ned til halvparten.</param>
    public LiveRing(int channels, int sampleRate, double maxLatency = 0.1)
    {
        _channels = Math.Max(1, channels);
        _maxFrames = Math.Max(256, (int)(sampleRate * maxLatency));
        _trimTo = _maxFrames / 2;
        _buf = new float[(_maxFrames + sampleRate) * _channels];
    }

    public int BufferedFrames { get { lock (_lock) return _count / _channels; } }

    public void Write(ReadOnlySpan<float> interleaved)
    {
        lock (_lock)
        {
            foreach (var v in interleaved)
            {
                if (_count == _buf.Length) { _read = (_read + 1) % _buf.Length; _count--; }
                _buf[(_read + _count) % _buf.Length] = v;
                _count++;
            }
            if (_count / _channels > _maxFrames)
            {
                int drop = (_count / _channels - _trimTo) * _channels;
                _read = (_read + drop) % _buf.Length;
                _count -= drop;
            }
        }
    }

    /// <summary>Leser alltid <paramref name="count"/> samples; det som mangler fylles med stillhet.</summary>
    public void Read(float[] dst, int offset, int count)
    {
        lock (_lock)
        {
            int n = Math.Min(count, _count - _count % _channels);
            for (int i = 0; i < n; i++) dst[offset + i] = _buf[(_read + i) % _buf.Length];
            _read = (_read + n) % _buf.Length;
            _count -= n;
            Array.Clear(dst, offset + n, count - n);
        }
    }
}

/// <summary>Live-lyd som en uendelig lydstrøm, så <see cref="CuePlayback"/> kan bruke den som en vanlig fil
/// (nivå, fades, pause og routing gjennom lydpatchen).</summary>
public sealed class LiveInputStream : WaveStream, ISampleProvider
{
    private readonly LiveRing _ring;
    private readonly Action<LiveRing> _release;
    private long _position;

    public LiveInputStream(LiveRing ring, int sampleRate, Action<LiveRing> release)
    {
        _ring = ring;
        _release = release;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, ring.Channels);
    }

    public override WaveFormat WaveFormat { get; }
    // «Uendelig» lengde (ti år), så avspillingen aldri tror den er ferdig.
    public override long Length => WaveFormat.AverageBytesPerSecond * 3600L * 24 * 365 * 10;
    public override long Position { get => _position; set { } }

    public int Read(float[] buffer, int offset, int count)
    {
        _ring.Read(buffer, offset, count);
        _position += count * 4L;
        return count;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var f = new float[count / 4];
        Read(f, 0, f.Length);
        Buffer.BlockCopy(f, 0, buffer, offset, f.Length * 4);
        return f.Length * 4;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _release(_ring);
        base.Dispose(disposing);
    }
}

/// <summary>Opptak fra én lydenhet (WASAPI). Åpnes når første live-cue starter og deles av alle live-cuer.
/// Lyden gjøres om til mikserens samplingsfrekvens og skrives til hver lytters ringbuffer.</summary>
public sealed class AudioInput : IDisposable
{
    private WasapiCapture? _capture;
    private string _deviceId = "";
    private int _outRate;
    private WdlResampler? _resampler;
    private readonly List<LiveRing> _rings = new();
    private readonly object _lock = new();
    private float[] _conv = Array.Empty<float>();
    private float[] _out = Array.Empty<float>();

    public int Channels { get; private set; }
    public string DeviceName { get; private set; } = "";

    /// <summary>Antall kanaler på opptaksenheten (0 hvis den ikke finnes).</summary>
    public static int ProbeChannels(string deviceId)
    {
        try
        {
            using var en = new MMDeviceEnumerator();
            using var dev = string.IsNullOrEmpty(deviceId) ? en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console) : en.GetDevice(deviceId);
            using var c = dev.AudioClient;
            return c.MixFormat.Channels;
        }
        catch { return 0; }
    }

    /// <summary>Ny lytter. Starter opptaket om nødvendig.</summary>
    public LiveRing Open(string deviceId, int outputRate)
    {
        lock (_lock)
        {
            if (_capture == null || deviceId != _deviceId || outputRate != _outRate) Start(deviceId, outputRate);
            var ring = new LiveRing(Channels, outputRate);
            _rings.Add(ring);
            return ring;
        }
    }

    public void Close(LiveRing ring)
    {
        lock (_lock) _rings.Remove(ring);
    }

    private void Start(string deviceId, int outputRate)
    {
        StopCapture();
        _rings.Clear(); // kanalantallet kan endres med ny enhet; cuer som spiller blir stille
        using var en = new MMDeviceEnumerator();
        var dev = string.IsNullOrEmpty(deviceId) ? en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console) : en.GetDevice(deviceId);
        var cap = new WasapiCapture(dev, true, 10);
        Channels = cap.WaveFormat.Channels;
        DeviceName = dev.FriendlyName;
        _deviceId = deviceId;
        _outRate = outputRate;
        if (cap.WaveFormat.SampleRate != outputRate)
        {
            _resampler = new WdlResampler();
            _resampler.SetMode(true, 2, false);
            _resampler.SetFilterParms();
            _resampler.SetFeedMode(true);
            _resampler.SetRates(cap.WaveFormat.SampleRate, outputRate);
        }
        else _resampler = null;
        cap.DataAvailable += OnData;
        cap.StartRecording();
        _capture = cap;
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        var fmt = ((WasapiCapture)sender!).WaveFormat;
        int frames = ToFloat(e.Buffer, e.BytesRecorded, fmt, ref _conv);
        int ch = fmt.Channels;
        ReadOnlySpan<float> data = _conv.AsSpan(0, frames * ch);
        if (_resampler != null)
        {
            int need = _resampler.ResamplePrepare(frames, ch, out var inBuf, out var inOff);
            data[..(Math.Min(need, frames) * ch)].CopyTo(inBuf.AsSpan(inOff));
            int maxOut = (int)((long)frames * _outRate / fmt.SampleRate) + 16;
            if (_out.Length < maxOut * ch) _out = new float[maxOut * ch];
            int got = _resampler.ResampleOut(_out, 0, Math.Min(need, frames), maxOut, ch);
            data = _out.AsSpan(0, got * ch);
        }
        lock (_lock) foreach (var r in _rings) r.Write(data);
    }

    private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");

    /// <summary>Gjør opptaksbufferen om til float (32-bit float, 16/24/32-bit heltall). Returnerer antall rammer.</summary>
    public static int ToFloat(byte[] src, int bytes, WaveFormat fmt, ref float[] dst)
    {
        int bits = fmt.BitsPerSample;
        bool isFloat = fmt.Encoding == WaveFormatEncoding.IeeeFloat
                       || (fmt is WaveFormatExtensible x && x.SubFormat == IeeeFloatSubFormat);
        int bps = bits / 8;
        int samples = bytes / bps;
        if (dst.Length < samples) dst = new float[samples];
        for (int i = 0, o = 0; i < samples; i++, o += bps)
        {
            dst[i] = isFloat && bits == 32 ? BitConverter.ToSingle(src, o)
                : bits == 16 ? BitConverter.ToInt16(src, o) / 32768f
                : bits == 24 ? ((src[o] | (src[o + 1] << 8) | ((sbyte)src[o + 2] << 16)) / 8388608f)
                : bits == 32 ? BitConverter.ToInt32(src, o) / 2147483648f
                : 0f;
        }
        return samples / Math.Max(1, fmt.Channels);
    }

    private void StopCapture()
    {
        var c = _capture;
        _capture = null;
        if (c == null) return;
        c.DataAvailable -= OnData;
        try { c.StopRecording(); } catch { }
        try { c.Dispose(); } catch { }
    }

    public void Dispose()
    {
        lock (_lock) { StopCapture(); _rings.Clear(); }
    }
}
