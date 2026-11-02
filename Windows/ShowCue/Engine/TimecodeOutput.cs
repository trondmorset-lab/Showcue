using System.Diagnostics;
using System.Runtime.InteropServices;
using ShowCue.Models;

namespace ShowCue.Engine;

/// <summary>LTC som lyd på én buss i lydpatchen. Kjører på lydtråden.</summary>
public sealed class LtcSource : IBusSource
{
    private readonly int _bus;
    private readonly float _amp;
    private readonly TimecodeRate _rate;
    private readonly double _bitsPerSample;
    private long _frame;
    private bool[] _bits;
    private double _phase;      // posisjon i bilde, målt i bit (0–80)
    private float _level;
    private volatile bool _stopped, _paused;

    public LtcSource(int busIndex, double levelDb, Timecode start, TimecodeRate rate, int sampleRate)
    {
        _bus = busIndex;
        _amp = PatchMixer.DbToGain(levelDb);
        _rate = rate;
        _bitsPerSample = Timecode.Fps(rate) * 80 / sampleRate;
        _frame = start.ToFrame(rate);
        _bits = Ltc.Bits(start, rate);
        _level = _amp;
    }

    public void Stop() => _stopped = true;
    public void Pause() => _paused = true;
    public void Resume() => _paused = false;

    public bool Render(float[] bus, int frames, int busCount)
    {
        if (_stopped) return false;
        if (_paused || _bus < 0 || _bus >= busCount) return true;
        for (int f = 0; f < frames; f++)
        {
            bus[f * busCount + _bus] += _level;
            double next = _phase + _bitsPerSample;
            int bit = (int)_phase;
            // Overgang i starten av hver bit, og midt i biten for 1.
            if ((int)next != bit) _level = -_level;
            else if (_bits[bit] && _phase - bit < 0.5 && next - bit >= 0.5) _level = -_level;
            _phase = next;
            if (_phase >= 80)
            {
                _phase -= 80;
                _bits = Ltc.Bits(Timecode.FromFrame(++_frame, _rate), _rate);
            }
        }
        return true;
    }
}

/// <summary>Sender MTC-kvartbilder til en MIDI-utgang fra en egen tråd (100–120 meldinger i sekundet).</summary>
public sealed class MtcSender : IDisposable
{
    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint ms);

    private readonly Action<int> _sendShort;
    private readonly long _startFrame;
    private readonly TimecodeRate _rate;
    private readonly Stopwatch _clock = new();
    private readonly Thread _thread;
    private volatile bool _running = true;
    private long _next;

    /// <param name="sendShort">Sender én kort MIDI-melding (status | data1 &lt;&lt; 8).</param>
    public MtcSender(Action<int> sendShort, Action<byte[]> sendSysex, Timecode start, TimecodeRate rate)
    {
        _sendShort = sendShort;
        _rate = rate;
        _startFrame = start.ToFrame(rate);
        sendSysex(Mtc.FullFrame(start, rate)); // mottakeren hopper rett til starttiden
        _thread = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "MTC" };
        _clock.Start();
        _thread.Start();
    }

    public void Pause() => _clock.Stop();
    public void Resume() => _clock.Start();

    private void Run()
    {
        try { timeBeginPeriod(1); } catch { }
        try
        {
            double qps = Timecode.Fps(_rate) * 4;
            while (_running)
            {
                long due = (long)(_clock.Elapsed.TotalSeconds * qps);
                if (due - _next > 16) _next = due - due % 8; // hengt etter (f.eks. maskinen sov): start en ny rekke
                for (; _next <= due && _running; _next++)
                    _sendShort(0xF1 | (Mtc.QuarterFrameAt(_next, _startFrame, _rate) << 8));
                Thread.Sleep(1);
            }
        }
        catch { /* MIDI-enheten forsvant */ }
        finally { try { timeEndPeriod(1); } catch { } }
    }

    public void Dispose() => _running = false;
}
