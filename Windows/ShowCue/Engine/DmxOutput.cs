using System.Diagnostics;
using System.Net.Sockets;
using ShowCue.Models;

namespace ShowCue.Engine;

/// <summary>Sender DMX over Art-Net. Hvert univers (per mottaker) har egne nivåer og fades.
/// Sendes 40 ganger i sekundet mens noe endres, ellers hvert sekund så nodene holder nivået.</summary>
public sealed class DmxOutput : IDisposable
{
    private sealed class Universe
    {
        public required string Host;
        public required int Number;
        public readonly DmxFader Fader = new();
        public byte Sequence;
        public byte[] Last = new byte[512];
        public double LastSent = double.NegativeInfinity;
    }

    private readonly Dictionary<(string, int), Universe> _universes = new();
    private readonly object _lock = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly System.Threading.Timer _timer;
    private UdpClient? _udp;

    public event Action<string>? Error;

    public DmxOutput() => _timer = new System.Threading.Timer(_ => Tick(), null, 25, 25);

    private double Now => _clock.Elapsed.TotalSeconds;

    public void FadeTo(string host, int universe, IReadOnlyDictionary<int, byte> levels, double seconds)
    {
        lock (_lock)
        {
            var key = (host, universe);
            if (!_universes.TryGetValue(key, out var u)) _universes[key] = u = new Universe { Host = host, Number = universe };
            u.Fader.FadeTo(levels, seconds, Now);
            u.LastSent = double.NegativeInfinity; // send med en gang
        }
    }

    private void Tick()
    {
        List<(string Host, byte[] Packet)> send = new();
        lock (_lock)
        {
            var now = Now;
            foreach (var u in _universes.Values)
            {
                var values = u.Fader.Values(now);
                if (now - u.LastSent < 1 && values.AsSpan().SequenceEqual(u.Last)) continue;
                u.Last = values;
                u.LastSent = now;
                u.Sequence = (byte)(u.Sequence % 255 + 1);
                send.Add((u.Host, ArtNet.DmxPacket(u.Number, u.Sequence, values)));
            }
        }
        foreach (var (host, packet) in send)
        {
            try
            {
                _udp ??= new UdpClient { EnableBroadcast = true };
                _udp.Send(packet, packet.Length, host, ArtNet.Port);
            }
            catch (Exception ex) { Error?.Invoke($"Art-Net til {host}: {ex.Message}"); }
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        _udp?.Dispose();
    }
}
