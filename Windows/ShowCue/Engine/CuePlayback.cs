using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using ShowCue.Models;
using Cue = ShowCue.Models.Cue;

namespace ShowCue.Engine;

/// <summary>
/// Én avspilling av en lyd-cue. Kjører på lydtråden inne i <see cref="PatchMixer"/>.
/// Håndterer start/slutt-punkt, loop, fade inn/ut, fades fra fade-cues og routing
/// fra filkanaler til busser. Nivå rampes i dB-domenet så fades høres naturlige ut.
/// </summary>
public sealed class CuePlayback : IBusSource, IDisposable
{
    public const double SilenceDb = -80;
    /// <summary>Korteste loop (10 ms ved 48 kHz).</summary>
    private const long MinLoopFrames = 480;

    private readonly record struct Send(int Bus, float Gain);

    private readonly WaveStream _reader;
    private readonly ISampleProvider _source;
    private volatile bool _rewindRequested;
    private volatile bool _releaseRequested;
    private volatile bool _paused;
    private readonly int _srcCh;
    private readonly int _rate;
    private readonly double _startTime;
    private long _framesPerPass;              // long.MaxValue = til fila er slutt
    private readonly long _framesToFileEnd;   // brukes når en loop slippes (spill videre til filslutt)
    private bool _loop;
    private readonly long _fadeOutFrames;
    private float[] _tmp = Array.Empty<float>();
    private volatile Send[][] _routing;

    private readonly object _lock = new();
    private double _currentDb;
    private double _targetDb;
    private double _rampStartDb;
    private long _rampTotal;
    private FadeCurve _curve;
    private long _rampFramesLeft;
    private bool _stopAfterRamp;
    private bool _autoFadeStarted;

    private long _passFrames;
    private long _totalFrames;
    private volatile bool _finished;
    private int _finishedRaised;
    private int _disposed;

    public Cue Cue { get; }
    /// <summary>Kalles fra lydtråden når avspillingen er ferdig.</summary>
    public event Action<CuePlayback>? Finished;

    public int FileChannels => _srcCh;
    public bool IsFinished => _finished;
    public double Elapsed => Interlocked.Read(ref _totalFrames) / (double)_rate;
    public double PassPosition => Interlocked.Read(ref _passFrames) / (double)_rate;
    /// <summary>Lengde på én gjennomspilling i sekunder (0 = ukjent).</summary>
    public double PassLength => _framesPerPass == long.MaxValue ? 0 : _framesPerPass / (double)_rate;

    /// <summary>Innstillinger for én avspilling.</summary>
    public sealed record Options(double VolumeDb, double StartTime, double EndTime, bool Loop, double FadeIn, double FadeOut);

    /// <summary>Spill en lydfil (lyd-cue).</summary>
    /// <param name="offset">Start så mange sekunder inn i cuen (for prøver: «spill fra her»).</param>
    public CuePlayback(AudioCue cue, int sampleRate, double offset = 0)
        : this(cue, new AudioFileReader(cue.FilePath), cue,
               new Options(cue.VolumeDb, cue.StartTime + (offset > 0 ? Math.Min(offset, 86400) : 0), cue.EndTime, cue.Loop, offset > 0 ? 0.02 : cue.FadeIn, cue.FadeOut), sampleRate) { }

    /// <summary>Spill en vilkårlig lydstrøm, f.eks. lydsporet i en video (dekodet med Media Foundation).</summary>
    public CuePlayback(Cue owner, WaveStream reader, IRoutedAudio routing, Options o, int sampleRate)
    {
        Cue = owner;
        _rate = sampleRate;

        _reader = reader;
        ISampleProvider samples = reader as ISampleProvider ?? reader.ToSampleProvider();
        _srcCh = reader.WaveFormat.Channels;
        static double Sec(double v) => double.IsFinite(v) ? Math.Clamp(v, 0, 86400) : 0;
        o = o with { StartTime = Sec(o.StartTime), EndTime = Sec(o.EndTime), FadeIn = Sec(o.FadeIn), FadeOut = Sec(o.FadeOut) };
        var total = reader.TotalTime.TotalSeconds;
        _startTime = Math.Min(o.StartTime, Math.Max(0, total - 0.01));
        if (_startTime > 0) _reader.CurrentTime = TimeSpan.FromSeconds(_startTime);

        var end = o.EndTime > 0 ? Math.Min(o.EndTime, total > 0 ? total : o.EndTime) : total;
        _framesPerPass = end > _startTime ? (long)((end - _startTime) * _rate) : long.MaxValue;
        _framesToFileEnd = total > _startTime ? (long)((total - _startTime) * _rate) : _framesPerPass;
        // En loop må være minst én blokk lang, ellers ville lydtråden bare spole tilbake hele tiden.
        _loop = o.Loop && _framesPerPass != long.MaxValue && _framesPerPass >= MinLoopFrames;
        _fadeOutFrames = o.FadeOut > 0 ? (long)(o.FadeOut * _rate) : 0;

        _source = samples.WaveFormat.SampleRate == sampleRate
            ? samples
            : new WdlResamplingSampleProvider(samples, sampleRate);

        _routing = BuildRouting(routing.EffectiveRoutes(_srcCh), _srcCh);

        var levelDb = o.VolumeDb > -60 ? Math.Min(o.VolumeDb, 24) : SilenceDb;
        if (o.FadeIn > 0)
        {
            _currentDb = SilenceDb;
            BeginRamp(levelDb, o.FadeIn, false);
        }
        else
        {
            _currentDb = _targetDb = levelDb;
        }
    }

    private static Send[][] BuildRouting(IReadOnlyList<AudioRoute> routes, int srcCh)
    {
        var r = new Send[srcCh][];
        for (int s = 0; s < srcCh; s++)
            r[s] = routes.Where(x => x.Source == s + 1 && x.Bus >= 1)
                         .Select(x => new Send(x.Bus - 1, PatchMixer.DbToGain(x.GainDb)))
                         .Where(x => x.Gain > 0)
                         .ToArray();
        return r;
    }

    /// <summary>Bytt routing mens det spiller (f.eks. under lydsjekk).</summary>
    public void SetRouting(IReadOnlyList<AudioRoute> routes) => _routing = BuildRouting(routes, _srcCh);

    /// <summary>Fade til absolutt nivå (dB) over gitt tid, med valgt kurve.</summary>
    public void FadeTo(double targetDb, double seconds, bool stopWhenDone, FadeCurve curve = FadeCurve.Db)
    {
        lock (_lock) BeginRamp(targetDb <= -60 ? SilenceDb : targetDb, seconds, stopWhenDone, curve);
    }

    /// <summary>Juster nivå (kort rampe) – bare hvis ingen fade pågår.</summary>
    public void AdjustLevel(double db)
    {
        lock (_lock)
        {
            if (_rampFramesLeft > 0 || _stopAfterRamp || _autoFadeStarted) return;
            BeginRamp(db <= -60 ? SilenceDb : db, 0.05, false);
        }
    }

    public void Stop(double fadeSeconds)
    {
        if (!(fadeSeconds > 0)) { _finished = true; return; }
        FadeTo(SilenceDb, fadeSeconds, true);
    }

    private void BeginRamp(double targetDb, double seconds, bool stopWhenDone, FadeCurve curve = FadeCurve.Db)
    {
        // Ugyldige verdier (NaN, uendelig) skal aldri nå lydtråden.
        targetDb = targetDb > -60 ? Math.Min(targetDb, 24) : SilenceDb;
        seconds = double.IsFinite(seconds) ? Math.Clamp(seconds, 0, 86400) : 0;
        _targetDb = targetDb;
        _stopAfterRamp = stopWhenDone;
        var frames = (long)(seconds * _rate);
        if (frames <= 0)
        {
            _currentDb = targetDb;
            _rampFramesLeft = 0;
            if (stopWhenDone) _finished = true;
            return;
        }
        _rampStartDb = _currentDb;
        _rampTotal = _rampFramesLeft = frames;
        _curve = curve;
    }

    /// <summary>Nivå (dB) <paramref name="p"/> (0–1) inn i en fade fra <paramref name="from"/> til <paramref name="to"/>.</summary>
    public static double CurveDb(FadeCurve curve, double from, double to, double p) => curve switch
    {
        // S-kurve: myk start og slutt.
        FadeCurve.SCurve => from + (to - from) * p * p * (3 - 2 * p),
        // Lineær i amplitude (som en vanlig fader på et analogt bord: faller raskt mot slutten).
        FadeCurve.Linear => GainToDb(PatchMixer.DbToGain(from) + (PatchMixer.DbToGain(to) - PatchMixer.DbToGain(from)) * p),
        _ => from + (to - from) * p,
    };

    private static double GainToDb(double g) => g <= 0.00001 ? SilenceDb : Math.Max(SilenceDb, 20 * Math.Log10(g));

    /// <summary>Hopp tilbake til start (trådsikkert; utføres på lydtråden). Brukes når en video looper.</summary>
    public void RequestRewind() => _rewindRequested = true;

    /// <summary>Slipp loopen («devamp»): runden som spiller fullføres, så spilles resten av fila til slutt.</summary>
    public void ReleaseLoop() => _releaseRequested = true;
    public bool IsLooping => _loop && !_releaseRequested;

    /// <summary>Pause: lyden holder på der den er (også fader) til <see cref="Resume"/>.</summary>
    public void Pause() => _paused = true;
    public void Resume() => _paused = false;
    public bool IsPaused => _paused;

    public bool Render(float[] bus, int frames, int busCount)
    {
        if (_finished) { RaiseFinished(); return false; }
        if (_paused) return true;
        if (_rewindRequested) { _rewindRequested = false; Rewind(); }
        if (_releaseRequested)
        {
            _releaseRequested = false;
            if (_loop)
            {
                _loop = false;
                if (_framesToFileEnd > _framesPerPass) _framesPerPass = _framesToFileEnd;
            }
        }

        if (_tmp.Length < frames * _srcCh) _tmp = new float[frames * _srcCh];
        var routing = _routing;
        int written = 0;
        int writtenAtRewind = -1; // to tilbakespolinger uten lyd imellom = fila gir ikke mer lyd; stopp i stedet for å henge

        while (written < frames && !_finished)
        {
            long passLeft = _framesPerPass - _passFrames;
            if (passLeft <= 0)
            {
                if (_loop && writtenAtRewind != written) { writtenAtRewind = written; Rewind(); continue; }
                break;
            }
            int toRead = (int)Math.Min(frames - written, passLeft);
            int got = _source.Read(_tmp, 0, toRead * _srcCh) / _srcCh;
            if (got == 0)
            {
                if (_loop && _passFrames > 0 && writtenAtRewind != written) { writtenAtRewind = written; Rewind(); continue; }
                break;
            }
            int keep = MixInto(bus, written, got, busCount, routing);
            written += keep;
            _passFrames += keep;
            Interlocked.Add(ref _totalFrames, keep);
            if (keep < got) break; // stoppet midt i blokka etter fade
        }

        if (written < frames) _finished = true;
        if (_finished) RaiseFinished();
        return !_finished;
    }

    /// <summary>Gain-konvolutt + routing til busser. Returnerer antall rammer som ble brukt.</summary>
    private int MixInto(float[] bus, int frameOffset, int frames, int busCount, Send[][] routing)
    {
        lock (_lock)
        {
            for (int f = 0; f < frames; f++)
            {
                // Automatisk fade ut mot slutten av fila.
                if (_fadeOutFrames > 0 && !_loop && !_autoFadeStarted &&
                    _framesPerPass - (_passFrames + f) <= _fadeOutFrames)
                {
                    _autoFadeStarted = true;
                    BeginRamp(SilenceDb, (_framesPerPass - (_passFrames + f)) / (double)_rate, false);
                }

                if (_rampFramesLeft > 0)
                {
                    _currentDb = CurveDb(_curve, _rampStartDb, _targetDb, 1 - (double)(--_rampFramesLeft) / _rampTotal);
                    if (_rampFramesLeft == 0)
                    {
                        _currentDb = _targetDb;
                        if (_stopAfterRamp)
                        {
                            _finished = true;
                            return f;
                        }
                    }
                }

                float g = _currentDb <= SilenceDb + 0.01 ? 0f : (float)Math.Pow(10, _currentDb / 20.0);
                if (g == 0f) continue;
                int src = f * _srcCh;
                int dst = (frameOffset + f) * busCount;
                for (int c = 0; c < _srcCh; c++)
                {
                    float v = _tmp[src + c] * g;
                    foreach (var s in routing[c])
                        if (s.Bus < busCount) bus[dst + s.Bus] += v * s.Gain;
                }
            }
            return frames;
        }
    }

    private void Rewind()
    {
        _reader.CurrentTime = TimeSpan.FromSeconds(_startTime);
        _passFrames = 0;
    }

    private void RaiseFinished()
    {
        if (Interlocked.Exchange(ref _finishedRaised, 1) == 0) Finished?.Invoke(this);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _reader.Dispose();
    }
}
