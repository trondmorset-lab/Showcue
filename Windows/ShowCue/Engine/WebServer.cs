using System.Net;
using System.Net.Sockets;
using ShowCue.Models;

namespace ShowCue.Engine;

/// <summary>
/// Liten HTTP-tjener for nettsiden som styrer showet. Egen <see cref="TcpListener"/> (krever ikke administratorrettigheter),
/// egne tråder fra trådpoolen, og ingenting her rører lydmotoren: kommandoer leveres til <see cref="Command"/>, som sender
/// dem videre til UI-tråden. Alle unntak fanges. Hver forbindelse leser én forespørsel (med tidsfrist), svarer og lukkes.
/// Tolking og PIN-kontroll ligger i <see cref="WebRemoteCore"/>.
/// </summary>
public sealed class WebServer : IDisposable
{
    public const int MaxConnections = 16;
    public const int MaxPerClient = 6;
    public const int RequestTimeoutMs = 5000;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private int _active;
    private readonly Dictionary<string, int> _perClient = new();
    private readonly object _lock = new();

    /// <summary>Kalles på en bakgrunnstråd med kommandoen og klientadressen. Mottakeren må selv flytte arbeidet til UI-tråden.</summary>
    public Action<WebCommand, string>? Command { get; set; }
    public bool IsRunning => _listener != null;
    public int Port { get; private set; }

    public void Start(int port, WebRemoteCore core)
    {
        Stop();
        var listener = new TcpListener(IPAddress.Any, port) { ExclusiveAddressUse = true };
        listener.Start(32);
        var cts = new CancellationTokenSource();
        _listener = listener;
        _cts = cts;
        Port = port;
        _ = Task.Run(() => AcceptLoop(listener, core, cts.Token));
    }

    public void Stop()
    {
        var cts = _cts;
        var listener = _listener;
        _cts = null;
        _listener = null;
        try { cts?.Cancel(); } catch { }
        try { listener?.Stop(); } catch { }
        lock (_lock) { _perClient.Clear(); _active = 0; }
    }

    public void Dispose() => Stop();

    private async Task AcceptLoop(TcpListener listener, WebRemoteCore core, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (Exception)
            {
                if (ct.IsCancellationRequested) return;
                try { await Task.Delay(100, ct); } catch { return; }
                continue;
            }
            _ = Task.Run(() => Serve(client, core, ct));
        }
    }

    private bool TryEnter(string ip)
    {
        lock (_lock)
        {
            if (_active >= MaxConnections) return false;
            _perClient.TryGetValue(ip, out var n);
            if (n >= MaxPerClient) return false;
            _perClient[ip] = n + 1;
            _active++;
            return true;
        }
    }

    private void Leave(string ip)
    {
        lock (_lock)
        {
            if (_perClient.TryGetValue(ip, out var n)) { if (n <= 1) _perClient.Remove(ip); else _perClient[ip] = n - 1; }
            if (_active > 0) _active--;
        }
    }

    private async Task Serve(TcpClient client, WebRemoteCore core, CancellationToken ct)
    {
        string ip = "?";
        bool entered = false;
        try
        {
            if (client.Client.RemoteEndPoint is IPEndPoint ep) ip = ep.Address.IsIPv4MappedToIPv6 ? ep.Address.MapToIPv4().ToString() : ep.Address.ToString();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(RequestTimeoutMs);
            var stream = client.GetStream();
            if (!TryEnter(ip))
            {
                await stream.WriteAsync(WebRemote.PlainResponse(503), deadline.Token);
                return;
            }
            entered = true;
            client.NoDelay = true;
            var buf = new byte[WebRemote.MaxRequestBytes];
            int len = 0;
            while (len < buf.Length)
            {
                int n = await stream.ReadAsync(buf.AsMemory(len, buf.Length - len), deadline.Token);
                if (n <= 0) return;
                len += n;
                var reply = core.Handle(buf, len, ip);
                if (!reply.Complete) continue;
                if (reply.Command is { } cmd)
                {
                    try { Command?.Invoke(cmd, ip); } catch { /* mottakeren skal aldri velte tjeneren */ }
                }
                await stream.WriteAsync(reply.Response, deadline.Token);
                return;
            }
        }
        catch (OperationCanceledException)
        {
            // Tidsfristen gikk ut (eller tjeneren stoppes): lukk uten svar.
        }
        catch
        {
            // Frakoblet klient eller nettverksfeil.
        }
        finally
        {
            if (entered) Leave(ip);
            try { client.Close(); } catch { }
        }
    }
}
