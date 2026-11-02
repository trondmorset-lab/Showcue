using System.Globalization;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using ShowCue.Models;

namespace ShowCue.Engine;

/// <summary>Sender nettverks-cuer: tekst over UDP eller TCP, eller en HTTP-forespørsel.</summary>
public static class NetworkSender
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static readonly TimeSpan TcpTimeout = TimeSpan.FromSeconds(3);

    /// <summary>Gjør om \r \n \t \0 \\ og \xHH til bytes. Resten sendes som UTF-8.</summary>
    public static byte[] Unescape(string text)
    {
        var bytes = new List<byte>();
        var plain = new StringBuilder();
        void Flush() { bytes.AddRange(Encoding.UTF8.GetBytes(plain.ToString())); plain.Clear(); }

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch != '\\' || i + 1 >= text.Length) { plain.Append(ch); continue; }
            var next = text[i + 1];
            byte? b = next switch { 'r' => (byte)13, 'n' => (byte)10, 't' => (byte)9, '0' => (byte)0, '\\' => (byte)'\\', _ => null };
            if (b is byte known) { Flush(); bytes.Add(known); i++; continue; }
            if ((next == 'x' || next == 'X') && i + 3 < text.Length
                && byte.TryParse(text.AsSpan(i + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
            {
                Flush(); bytes.Add(hex); i += 3; continue;
            }
            plain.Append(ch);
        }
        Flush();
        return bytes.ToArray();
    }

    /// <summary>Sender cuen. Returnerer null når det gikk bra, ellers en feilmelding.</summary>
    public static async Task<string?> SendAsync(NetworkCue cue, CancellationToken cancel = default)
    {
        try
        {
            switch (cue.Protocol)
            {
                case NetworkProtocol.Udp:
                {
                    using var udp = new UdpClient();
                    var data = Unescape(cue.Message);
                    await udp.SendAsync(data, data.Length, cue.Host, cue.Port).WaitAsync(cancel);
                    return null;
                }
                case NetworkProtocol.Tcp:
                {
                    using var tcp = new TcpClient();
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                    cts.CancelAfter(TcpTimeout);
                    await tcp.ConnectAsync(cue.Host, cue.Port, cts.Token);
                    var data = Unescape(cue.Message);
                    await tcp.GetStream().WriteAsync(data, cts.Token);
                    return null;
                }
                default:
                {
                    if (!Uri.TryCreate(cue.Url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
                        return "Ugyldig adresse (må starte med http:// eller https://)";
                    using var req = new HttpRequestMessage(new HttpMethod(cue.Method), uri);
                    if (cue.Message.Length > 0)
                    {
                        var body = cue.Message.TrimStart();
                        var type = body.StartsWith('{') || body.StartsWith('[') ? "application/json" : "text/plain";
                        req.Content = new ByteArrayContent(Unescape(cue.Message));
                        req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(type) { CharSet = "utf-8" };
                    }
                    using var resp = await Http.SendAsync(req, cancel);
                    return resp.IsSuccessStatusCode ? null : $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}";
                }
            }
        }
        catch (OperationCanceledException) { return "Tidsavbrudd – fikk ikke kontakt"; }
        catch (Exception ex) { return ex.Message; }
    }
}
