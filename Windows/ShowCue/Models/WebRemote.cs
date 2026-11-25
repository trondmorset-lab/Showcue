using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ShowCue.Models;

/// <summary>Kommandoene nettsiden kan sende. Utføres av <c>RemoteControl</c> via samme grensesnitt som OSC.</summary>
public enum WebCommand { Go, StopAll, Pause, Resume, Next, Previous }

public enum WebParseKind { Incomplete, Ok, Error }

/// <summary>En lest HTTP-forespørsel. Overskriftsnavn er med små bokstaver.</summary>
public sealed class WebRequest
{
    public string Method { get; init; } = "";
    public string Path { get; init; } = "";
    public Dictionary<string, string> Headers { get; init; } = new();
    public byte[] Body { get; init; } = Array.Empty<byte>();
}

/// <summary>Resultat av tolking: ufullstendig (les mer), ferdig forespørsel, eller feil med HTTP-statuskode.</summary>
public sealed record WebParseResult(WebParseKind Kind, int Status, WebRequest? Request);

/// <summary>Svaret på ett kall til <see cref="WebRemoteCore.Handle"/>. <c>Complete</c> = false betyr «les flere byte».</summary>
public sealed record WebReply(bool Complete, byte[] Response, WebCommand? Command);

public sealed record WebPlaying(string Number, string Name, string Type, double Elapsed, double Remaining, bool HasDuration, bool PreWaiting);
public sealed record WebNext(string Number, string Name, string Type, string Notes);
public sealed record WebStateInput(string ShowName, bool Paused, bool ShowMode, WebNext? Next, int Position, int Count, IReadOnlyList<WebPlaying> Playing);
public sealed record WebCueInfo(string Number, string Name, string Type, bool Disabled);

/// <summary>
/// Ren logikk for nettsiden som styrer showet fra mobil eller nettbrett: tolking av HTTP, PIN-regler, JSON og sidene.
/// Ingen nettverk, ingen filer og ingen tråder her, så alt kan testes med byte-matriser.
/// </summary>
public static class WebRemote
{
    public const int DefaultPort = 8088;
    public const int MaxRequestLine = 2048;
    public const int MaxHeaderBytes = 8192;
    public const int MaxHeaders = 32;
    public const int MaxBody = 1024;
    public const int MaxRequestBytes = MaxHeaderBytes + MaxBody;
    public const int PinMin = 6;
    public const int PinMax = 32;
    public const int MaxListedCues = 1000;
    public const int MaxListedPlaying = 50;

    private static readonly string[] WeakPins =
    {
        "123456", "1234567", "12345678", "123456789", "1234567890", "654321", "987654321", "123123", "121212", "112233", "012345",
        "password", "passord", "qwerty", "qwerty123", "abc123", "abcdef", "letmein", "welcome", "admin", "showcue", "showcue1"
    };

    public static byte[] SystemRandom(int count) => RandomNumberGenerator.GetBytes(count);

    // ------------------------------------------------------------------ PIN
    /// <summary>PIN: 6–32 bokstaver (a–z, A–Z) eller siffer, ikke bare ett tegn gjentatt og ikke på lista over lette koder.</summary>
    public static bool IsAcceptablePin(string? pin)
    {
        if (pin == null || pin.Length < PinMin || pin.Length > PinMax) return false;
        foreach (var c in pin) if (!char.IsAsciiLetterOrDigit(c)) return false;
        if (pin.All(c => c == pin[0])) return false;
        foreach (var w in WeakPins) if (string.Equals(pin, w, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    /// <summary>Lager en tilfeldig 6-sifret PIN (aldri en lett kode).</summary>
    public static string GeneratePin(Func<int, byte[]>? random = null)
    {
        random ??= SystemRandom;
        const uint Range = 1_000_000;
        uint limit = uint.MaxValue - (uint.MaxValue % Range); // unngår skjevhet
        for (int guard = 0; guard < 1000; guard++)
        {
            var b = random(4);
            uint v = BitConverter.ToUInt32(b, 0);
            if (v >= limit) continue;
            var pin = (v % Range).ToString("D6", CultureInfo.InvariantCulture);
            if (IsAcceptablePin(pin)) return pin;
        }
        return "739518"; // nås bare med en ødelagt tilfeldighetskilde i test
    }

    /// <summary>Godkjent PIN beholdes. Ellers: ny PIN når nettsiden er på, tom når den er av.</summary>
    public static string NormalizePin(string? pin, bool enabled, Func<int, byte[]>? random = null)
    {
        var p = (pin ?? "").Trim();
        if (IsAcceptablePin(p)) return p;
        return enabled ? GeneratePin(random) : "";
    }

    /// <summary>Sammenligner to tekster i like lang tid uansett hvor de skiller seg (maks 64 byte; lengre gir alltid false).</summary>
    public static bool ConstantTimeEquals(string a, string b)
    {
        var x = Encoding.UTF8.GetBytes(a);
        var y = Encoding.UTF8.GetBytes(b);
        int diff = x.Length ^ y.Length;
        for (int i = 0; i < 64; i++)
        {
            int p = i < x.Length ? x[i] : 0;
            int q = i < y.Length ? y[i] : 0;
            diff |= p ^ q;
        }
        return diff == 0 && x.Length <= 64;
    }

    // ------------------------------------------------------------------ HTTP-tolking
    private static WebParseResult Fail(int status) => new(WebParseKind.Error, status, null);
    private static readonly WebParseResult Pending = new(WebParseKind.Incomplete, 0, null);

    private static int IndexOfCrLf(byte[] d, int length)
    {
        for (int i = 0; i + 1 < length; i++) if (d[i] == '\r' && d[i + 1] == '\n') return i;
        return -1;
    }

    private static int IndexOfHeaderEnd(byte[] d, int length)
    {
        for (int i = 0; i + 3 < length; i++)
            if (d[i] == '\r' && d[i + 1] == '\n' && d[i + 2] == '\r' && d[i + 3] == '\n') return i;
        return -1;
    }

    private static bool IsTokenChar(char c) =>
        char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-.^_`|~".Contains(c);

    /// <summary>
    /// Tolker en HTTP/1.1-forespørsel fra byte som er mottatt så langt. Strengt: bare ASCII, bare CRLF, bare GET og POST,
    /// ingen kodede tegn i stien, ingen «..», ingen delt kroppsoverføring, grenser for linje, hoder og kropp.
    /// </summary>
    public static WebParseResult Parse(byte[] data, int length)
    {
        length = Math.Clamp(length, 0, data.Length);
        int lineEnd = IndexOfCrLf(data, length);
        if (lineEnd < 0) return length > MaxRequestLine ? Fail(414) : Pending;
        if (lineEnd > MaxRequestLine) return Fail(414);
        int headEnd = IndexOfHeaderEnd(data, length);
        if (headEnd < 0) return length >= MaxHeaderBytes ? Fail(431) : Pending;
        int bodyStart = headEnd + 4;
        if (bodyStart > MaxHeaderBytes) return Fail(431);

        for (int i = 0; i < bodyStart; i++)
        {
            byte b = data[i];
            if (b == '\r') { if (i + 1 >= bodyStart || data[i + 1] != '\n') return Fail(400); }
            else if (b == '\n') { if (i == 0 || data[i - 1] != '\r') return Fail(400); }
            else if ((b < 0x20 && b != '\t') || b >= 0x7F) return Fail(400);
        }

        var lines = Encoding.ASCII.GetString(data, 0, headEnd).Split("\r\n");
        var parts = lines[0].Split(' ');
        if (parts.Length != 3 || parts.Any(p => p.Length == 0)) return Fail(400);
        var method = parts[0];
        if (method.Length > 16 || !method.All(char.IsAsciiLetterUpper)) return Fail(400);
        var target = parts[1];
        if (target[0] != '/' || target.Contains('#') || target.Contains('%') || target.Contains('\\')) return Fail(400);
        int q = target.IndexOf('?');
        var path = q >= 0 ? target[..q] : target;
        if (path.Contains("..")) return Fail(400);
        if (parts[2] != "HTTP/1.1") return Fail(parts[2].StartsWith("HTTP/", StringComparison.Ordinal) ? 505 : 400);
        if (method != "GET" && method != "POST") return Fail(405);

        if (lines.Length - 1 > MaxHeaders) return Fail(431);
        var headers = new Dictionary<string, string>();
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0 || line[0] == ' ' || line[0] == '\t') return Fail(400);
            int colon = line.IndexOf(':');
            if (colon <= 0) return Fail(400);
            var name = line[..colon];
            if (!name.All(IsTokenChar)) return Fail(400);
            if (!headers.TryAdd(name.ToLowerInvariant(), line[(colon + 1)..].Trim(' ', '\t'))) return Fail(400);
        }
        if (headers.ContainsKey("transfer-encoding") || !headers.ContainsKey("host")) return Fail(400);

        int contentLength = 0;
        bool hasLength = false;
        if (headers.TryGetValue("content-length", out var cl))
        {
            hasLength = true;
            if (cl.Length is 0 or > 7 || !cl.All(char.IsAsciiDigit)) return Fail(400);
            contentLength = int.Parse(cl, CultureInfo.InvariantCulture);
            if (contentLength > MaxBody) return Fail(413);
        }
        if (method == "GET" && contentLength != 0) return Fail(400);
        if (method == "POST" && !hasLength) return Fail(411);
        if (length - bodyStart < contentLength) return Pending;

        var body = new byte[contentLength];
        Array.Copy(data, bodyStart, body, 0, contentLength);
        return new WebParseResult(WebParseKind.Ok, 200, new WebRequest { Method = method, Path = path, Headers = headers, Body = body });
    }

    // ------------------------------------------------------------------ Svar
    public static string Reason(int status) => status switch
    {
        200 => "OK",
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        408 => "Request Timeout",
        411 => "Length Required",
        413 => "Content Too Large",
        414 => "URI Too Long",
        429 => "Too Many Requests",
        431 => "Request Header Fields Too Large",
        503 => "Service Unavailable",
        505 => "HTTP Version Not Supported",
        _ => "Error"
    };

    private const string Csp = "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    /// <summary>Bygger et helt HTTP/1.1-svar. Alltid «Connection: close», aldri CORS-hoder, aldri hurtigbuffer.</summary>
    public static byte[] BuildResponse(int status, string contentType, byte[] body, params string[] extraHeaders)
    {
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 ").Append(status).Append(' ').Append(Reason(status)).Append("\r\n");
        sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
        sb.Append("Content-Length: ").Append(body.Length).Append("\r\n");
        sb.Append("Connection: close\r\n");
        sb.Append("Cache-Control: no-store\r\n");
        sb.Append("X-Content-Type-Options: nosniff\r\n");
        sb.Append("X-Frame-Options: DENY\r\n");
        sb.Append("Referrer-Policy: no-referrer\r\n");
        sb.Append("Content-Security-Policy: ").Append(Csp).Append("\r\n");
        foreach (var h in extraHeaders) sb.Append(h).Append("\r\n");
        sb.Append("\r\n");
        var head = Encoding.ASCII.GetBytes(sb.ToString());
        var all = new byte[head.Length + body.Length];
        Array.Copy(head, all, head.Length);
        Array.Copy(body, 0, all, head.Length, body.Length);
        return all;
    }

    /// <summary>Kort tekstsvar med bare statusteksten (ingen innhold fra forespørselen speiles tilbake).</summary>
    public static byte[] PlainResponse(int status, params string[] extraHeaders) =>
        BuildResponse(status, "text/plain; charset=utf-8", Encoding.ASCII.GetBytes(Reason(status)), extraHeaders);

    // ------------------------------------------------------------------ JSON
    public static string JsonEscape(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '<': case '>': case '&': case ' ': case ' ':
                    sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    private static string Cut(string? s, int max)
    {
        s ??= "";
        if (s.Length <= max) return s;
        int n = char.IsHighSurrogate(s[max - 1]) ? max - 1 : max;
        return s[..n];
    }

    private static string Str(string? s, int max) => "\"" + JsonEscape(Cut(s, max)) + "\"";

    private static string Num(double v) =>
        (double.IsFinite(v) ? Math.Round(Math.Max(0, Math.Min(v, 1e9)), 1) : 0).ToString("0.0", CultureInfo.InvariantCulture);

    private static string Bool(bool b) => b ? "true" : "false";

    /// <summary>Tilstanden siden henter ca. to ganger i sekundet.</summary>
    public static string BuildStateJson(WebStateInput s)
    {
        var sb = new StringBuilder(512);
        sb.Append("{\"v\":1,\"show\":").Append(Str(s.ShowName, 80));
        sb.Append(",\"paused\":").Append(Bool(s.Paused));
        sb.Append(",\"lock\":").Append(Bool(s.ShowMode));
        sb.Append(",\"pos\":").Append(Math.Max(0, s.Position).ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"count\":").Append(Math.Max(0, s.Count).ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"next\":");
        if (s.Next is { } n)
            sb.Append("{\"n\":").Append(Str(n.Number, 40)).Append(",\"name\":").Append(Str(n.Name, 80))
              .Append(",\"type\":").Append(Str(n.Type, 40)).Append(",\"notes\":").Append(Str(n.Notes, 200)).Append('}');
        else sb.Append("null");
        sb.Append(",\"playing\":[");
        bool first = true;
        foreach (var p in s.Playing.Take(MaxListedPlaying))
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"n\":").Append(Str(p.Number, 40)).Append(",\"name\":").Append(Str(p.Name, 80))
              .Append(",\"type\":").Append(Str(p.Type, 40))
              .Append(",\"el\":").Append(Num(p.Elapsed)).Append(",\"rem\":").Append(Num(p.Remaining))
              .Append(",\"dur\":").Append(Bool(p.HasDuration)).Append(",\"pre\":").Append(Bool(p.PreWaiting)).Append('}');
        }
        sb.Append("]}");
        return sb.ToString();
    }

    /// <summary>Cuelista (bare lesing): nummer, navn, type og om cuen er deaktivert.</summary>
    public static string BuildCuesJson(IEnumerable<WebCueInfo> cues)
    {
        var sb = new StringBuilder(1024);
        sb.Append("{\"cues\":[");
        bool first = true;
        foreach (var c in cues.Take(MaxListedCues))
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"n\":").Append(Str(c.Number, 40)).Append(",\"name\":").Append(Str(c.Name, 80))
              .Append(",\"type\":").Append(Str(c.Type, 40)).Append(",\"off\":").Append(Bool(c.Disabled)).Append('}');
        }
        sb.Append("]}");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ Sider
    public static string HtmlEscape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");

    /// <summary>Tekstene i sidene (norsk er nøkkelen i oversettelsen). Nøklene brukes som {{navn}} i malene.</summary>
    public static Dictionary<string, string> PageTexts(string language) => new()
    {
        ["lang"] = language,
        ["next"] = Loc.T("Neste cue"),
        ["playing"] = Loc.T("Spiller nå"),
        ["nothing"] = Loc.T("Ingenting spiller"),
        ["end"] = Loc.T("— slutt på lista —"),
        ["stopAll"] = Loc.T("Stopp alt"),
        ["pause"] = Loc.T("Pause"),
        ["resume"] = Loc.T("Fortsett"),
        ["nextBtn"] = Loc.T("Neste"),
        ["prevBtn"] = Loc.T("Forrige"),
        ["cues"] = Loc.T("Cuer"),
        ["lost"] = Loc.T("Ingen kontakt med ShowCue"),
        ["wait"] = Loc.T("Venter"),
        ["showMode"] = Loc.T("Showmodus"),
        ["pinLabel"] = Loc.T("PIN-kode"),
        ["login"] = Loc.T("Logg inn"),
        ["wrong"] = Loc.T("Feil PIN-kode"),
        ["locked"] = Loc.T("For mange forsøk. Vent et minutt og prøv igjen."),
    };

    /// <summary>Setter tekstene inn i malen. Alt går gjennom HtmlEscape, og ukjente navn blir tomme.</summary>
    public static string Fill(string template, IReadOnlyDictionary<string, string> texts)
    {
        var sb = new StringBuilder(template.Length + 512);
        int i = 0;
        while (i < template.Length)
        {
            int open = template.IndexOf("{{", i, StringComparison.Ordinal);
            if (open < 0) { sb.Append(template, i, template.Length - i); break; }
            int close = template.IndexOf("}}", open + 2, StringComparison.Ordinal);
            if (close < 0) { sb.Append(template, i, template.Length - i); break; }
            sb.Append(template, i, open - i);
            var key = template.Substring(open + 2, close - open - 2);
            if (texts.TryGetValue(key, out var v)) sb.Append(HtmlEscape(v));
            i = close + 2;
        }
        return sb.ToString();
    }

    public static byte[] LoginPage(IReadOnlyDictionary<string, string> texts) => Encoding.UTF8.GetBytes(Fill(LoginTemplate, texts));
    public static byte[] ControlPage(IReadOnlyDictionary<string, string> texts) => Encoding.UTF8.GetBytes(Fill(ControlTemplate, texts));

    /// <summary>Adressen som vises for brukeren, f.eks. «http://192.168.1.20:8088». Tom adresseliste gir «localhost».</summary>
    public static string Url(IReadOnlyList<string> addresses, int port) =>
        "http://" + (addresses.Count > 0 ? addresses[0] : "localhost") + ":" + port.ToString(CultureInfo.InvariantCulture);

    /// <summary>Kort navn på kommandoen, til loggen.</summary>
    public static string CommandName(WebCommand c) => c switch
    {
        WebCommand.Go => "GO",
        WebCommand.StopAll => Loc.T("stopp alt"),
        WebCommand.Pause => Loc.T("pause"),
        WebCommand.Resume => Loc.T("fortsett"),
        WebCommand.Next => Loc.T("neste"),
        _ => Loc.T("forrige")
    };

    private const string LoginTemplate = """
<!doctype html>
<html lang="{{lang}}"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<meta name="color-scheme" content="dark">
<title>ShowCue</title>
<style>
*{box-sizing:border-box}
body{margin:0;background:#14171c;color:#e8ecf1;font:16px/1.35 system-ui,-apple-system,Segoe UI,sans-serif;-webkit-user-select:none;user-select:none}
header{display:flex;justify-content:space-between;align-items:center;padding:10px 14px;background:#1d222a}
#conn{font-size:13px;color:#7bd88f}#conn.bad{color:#ff7a7a}
main{padding:12px 14px 30px;max-width:720px;margin:0 auto}
.lbl{font-size:12px;letter-spacing:.08em;text-transform:uppercase;color:#8d97a5;margin:16px 0 4px}
.card{background:#1d222a;border-radius:10px;padding:12px 14px}
#nn{font-size:28px;font-weight:700;overflow-wrap:anywhere}#nt,#nz,#lk{color:#8d97a5}#lk{color:#ffb454}
.row{display:flex;justify-content:space-between;gap:10px;padding:5px 0;border-bottom:1px solid #2a313b}
.row:last-child{border:0}.t{font-variant-numeric:tabular-nums;color:#ffd27a}
.btns{display:grid;grid-template-columns:1fr 1fr;gap:10px;margin-top:14px}
button{font:inherit;font-weight:700;font-size:20px;padding:20px 10px;border:0;border-radius:12px;background:#2e3745;color:#fff}
button:active{filter:brightness(1.35)}button:disabled{opacity:.4}
#go{grid-column:1/3;background:#1f9d55;font-size:44px;padding:34px 10px}
#stop{background:#c53030}
ul{list-style:none;margin:0;padding:0}li{display:flex;gap:10px;padding:7px 10px;border-bottom:1px solid #2a313b;overflow-wrap:anywhere}
li.nx{background:#27405e}li.pl{color:#ffd27a}li.off{opacity:.45}.no{min-width:3em;color:#8d97a5}.ty{margin-left:auto;color:#8d97a5;font-size:13px}
input{font:inherit;font-size:22px;width:100%;padding:14px;margin:10px 0;border-radius:10px;border:1px solid #3a4452;background:#0f1216;color:#fff;-webkit-user-select:text;user-select:text}
#msg{min-height:1.5em;color:#ff7a7a}
</style></head><body>
<header><b>ShowCue</b></header>
<main>
<div class="lbl">{{pinLabel}}</div>
<form id="f" autocomplete="off">
<input id="pin" type="password" maxlength="32" autocapitalize="off" autocorrect="off" spellcheck="false" autofocus>
<button id="go" type="submit" style="width:100%;font-size:24px;padding:18px">{{login}}</button>
</form>
<div id="msg"></div>
<div id="tx" hidden data-wrong="{{wrong}}" data-locked="{{locked}}" data-lost="{{lost}}"></div>
</main>
<script>
(function(){
var $=function(i){return document.getElementById(i)};
var D=$('tx').dataset;
$('f').onsubmit=function(e){e.preventDefault();
var v=$('pin').value;$('msg').textContent='';
var x=new XMLHttpRequest();x.open('POST','/api/login');x.timeout=4000;
x.setRequestHeader('Content-Type','application/x-www-form-urlencoded');x.setRequestHeader('X-ShowCue','1');
x.onload=function(){if(x.status===200)location.reload();
else $('msg').textContent=x.status===429?D.locked:(x.status===401?D.wrong:D.lost)};
x.onerror=function(){$('msg').textContent=D.lost};x.ontimeout=x.onerror;
x.send('pin='+encodeURIComponent(v))};
})();
</script></body></html>
""";

    private const string ControlTemplate = """
<!doctype html>
<html lang="{{lang}}"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<meta name="color-scheme" content="dark">
<title>ShowCue</title>
<style>
*{box-sizing:border-box}
body{margin:0;background:#14171c;color:#e8ecf1;font:16px/1.35 system-ui,-apple-system,Segoe UI,sans-serif;-webkit-user-select:none;user-select:none}
header{display:flex;justify-content:space-between;align-items:center;padding:10px 14px;background:#1d222a}
#conn{font-size:13px;color:#7bd88f}#conn.bad{color:#ff7a7a}
main{padding:12px 14px 30px;max-width:720px;margin:0 auto}
.lbl{font-size:12px;letter-spacing:.08em;text-transform:uppercase;color:#8d97a5;margin:16px 0 4px}
.card{background:#1d222a;border-radius:10px;padding:12px 14px}
#nn{font-size:28px;font-weight:700;overflow-wrap:anywhere}#nt,#nz,#lk{color:#8d97a5}#lk{color:#ffb454}
.row{display:flex;justify-content:space-between;gap:10px;padding:5px 0;border-bottom:1px solid #2a313b}
.row:last-child{border:0}.t{font-variant-numeric:tabular-nums;color:#ffd27a}
.btns{display:grid;grid-template-columns:1fr 1fr;gap:10px;margin-top:14px}
button{font:inherit;font-weight:700;font-size:20px;padding:20px 10px;border:0;border-radius:12px;background:#2e3745;color:#fff}
button:active{filter:brightness(1.35)}button:disabled{opacity:.4}
#go{grid-column:1/3;background:#1f9d55;font-size:44px;padding:34px 10px}
#stop{background:#c53030}
ul{list-style:none;margin:0;padding:0}li{display:flex;gap:10px;padding:7px 10px;border-bottom:1px solid #2a313b;overflow-wrap:anywhere}
li.nx{background:#27405e}li.pl{color:#ffd27a}li.off{opacity:.45}.no{min-width:3em;color:#8d97a5}.ty{margin-left:auto;color:#8d97a5;font-size:13px}
input{font:inherit;font-size:22px;width:100%;padding:14px;margin:10px 0;border-radius:10px;border:1px solid #3a4452;background:#0f1216;color:#fff;-webkit-user-select:text;user-select:text}
#msg{min-height:1.5em;color:#ff7a7a}
</style></head><body>
<header><b>ShowCue <span id="show"></span></b><span id="conn"></span></header>
<main>
<div class="lbl">{{next}}</div>
<div class="card"><div id="nn"></div><div id="nt"></div><div id="nz"></div></div>
<div class="lbl">{{playing}}</div>
<div class="card" id="pl"></div>
<div id="lk"></div>
<div class="btns">
<button id="go">GO</button>
<button id="stop">{{stopAll}}</button>
<button id="pause">{{pause}}</button>
<button id="nx">{{nextBtn}}</button>
<button id="pv">{{prevBtn}}</button>
</div>
<div class="lbl">{{cues}}</div>
<ul id="cl"></ul>
</main>
<div id="tx" hidden data-pause="{{pause}}" data-resume="{{resume}}" data-none="{{nothing}}" data-end="{{end}}" data-lost="{{lost}}" data-wait="{{wait}}" data-lock="{{showMode}}"></div>
<script>
(function(){
var $=function(i){return document.getElementById(i)};
var D=$('tx').dataset;
var paused=false,nextNo='',playNo={},items=[],busy=false;
function fmt(s){s=Math.max(0,Math.round(s));var m=Math.floor(s/60);s=s%60;return m+':'+(s<10?'0':'')+s}
function el(t,c,x){var e=document.createElement(t);if(c)e.className=c;if(x!==undefined)e.textContent=x;return e}
function req(m,u,b,cb){var x=new XMLHttpRequest();x.open(m,u);x.timeout=4000;
if(m==='POST'){x.setRequestHeader('Content-Type','application/x-www-form-urlencoded');x.setRequestHeader('X-ShowCue','1')}
x.onload=function(){cb(x.status,x.responseText)};x.onerror=function(){cb(0,'')};x.ontimeout=function(){cb(0,'')};x.send(b||null)}
function conn(ok){var c=$('conn');c.textContent=ok?'●':D.lost;c.className=ok?'':'bad';
var b=document.getElementsByTagName('button');for(var i=0;i<b.length;i++)b[i].disabled=!ok}
function mark(){for(var i=0;i<items.length;i++){var o=items[i];
o.li.className=(o.off?'off ':'')+(o.n===nextNo?'nx ':'')+(playNo[o.n]?'pl':'')}}
function render(d){
$('show').textContent=d.show||'';
paused=!!d.paused;$('pause').textContent=paused?D.resume:D.pause;
$('lk').textContent=d.lock?D.lock:'';
var n=d.next;nextNo=n?n.n:'';
$('nn').textContent=n?(n.n?n.n+'  ':'')+n.name:D.end;
$('nt').textContent=n?n.type+(d.count?'  ·  '+d.pos+' / '+d.count:''):'';
$('nz').textContent=n?n.notes:'';
var p=$('pl');p.textContent='';playNo={};
if(!d.playing.length)p.appendChild(el('div','row',D.none));
d.playing.forEach(function(c){playNo[c.n]=1;var r=el('div','row');
r.appendChild(el('span','',c.n+'  '+c.name));
r.appendChild(el('span','t',c.pre?D.wait:(c.dur?'−'+fmt(c.rem):fmt(c.el))));p.appendChild(r)});
mark()}
function poll(){req('GET','/api/state',null,function(s,t){
if(s===401){location.reload();return}
var ok=false;if(s===200){try{render(JSON.parse(t));ok=true}catch(e){}}
conn(ok);setTimeout(poll,500)})}
function loadCues(){req('GET','/api/cues',null,function(s,t){
if(s===200){try{var d=JSON.parse(t),u=$('cl');u.textContent='';items=[];
d.cues.forEach(function(c){var li=el('li');li.appendChild(el('span','no',c.n));li.appendChild(el('span','',c.name));li.appendChild(el('span','ty',c.type));
u.appendChild(li);items.push({li:li,n:c.n,off:c.off})});mark()}catch(e){}}
setTimeout(loadCues,3000)})}
function cmd(c){if(c==='go'){if(busy)return;busy=true;setTimeout(function(){busy=false},300)}
req('POST','/api/cmd','cmd='+c,function(s){if(s===401)location.reload()})}
$('go').onclick=function(){cmd('go')};
$('stop').onclick=function(){cmd('stop')};
$('pause').onclick=function(){cmd(paused?'resume':'pause')};
$('nx').onclick=function(){cmd('next')};
$('pv').onclick=function(){cmd('previous')};
poll();loadCues();
})();
</script></body></html>
""";
}

/// <summary>
/// Ruter og PIN-kontroll for nettsiden. Tar imot byte, gir byte tilbake, og kjenner bare tid, tilfeldighet og klientadresse
/// som parametre. Tråd-sikker (ett lås rundt øktene og forsøkene som feilet).
/// Faste ruter: GET / (innloggingssiden eller kontrollsiden), POST /api/login, GET /api/state, GET /api/cues, POST /api/cmd.
/// Alt annet gir 404. Ingen sti fra forespørselen brukes mot disken.
/// </summary>
public sealed class WebRemoteCore
{
    public const int MaxFailures = 5;
    public const long FailureWindowMs = 60_000;
    public const long LockoutMs = 60_000;
    public const int GlobalMaxFailures = 30;
    public const long GlobalWindowMs = 600_000;
    public const long GlobalLockoutMs = 300_000;
    public const long SessionLifetimeMs = 12 * 3600 * 1000L;
    public const int MaxSessions = 32;
    public const int MaxTrackedClients = 256;
    public const string CookieName = "sc_session";

    private sealed class Attempts
    {
        public readonly Queue<long> Failures = new();
        public long LockedUntil;
    }

    private readonly string _pin;
    private readonly Func<long> _now;
    private readonly Func<int, byte[]> _random;
    private readonly byte[] _loginPage;
    private readonly byte[] _controlPage;
    private readonly object _lock = new();
    private readonly Dictionary<string, Attempts> _clients = new();
    private readonly Attempts _global = new();
    private readonly List<(string Token, long Expires)> _sessions = new();
    private volatile string _state = "{\"v\":1}";
    private volatile string _cues = "{\"cues\":[]}";

    /// <param name="nowMs">Stigende klokke i millisekunder.</param>
    public WebRemoteCore(string pin, byte[] loginPage, byte[] controlPage, Func<long> nowMs, Func<int, byte[]>? random = null)
    {
        if (!WebRemote.IsAcceptablePin(pin)) throw new ArgumentException("PIN er ikke godkjent.", nameof(pin));
        _pin = pin;
        _loginPage = loginPage;
        _controlPage = controlPage;
        _now = nowMs;
        _random = random ?? WebRemote.SystemRandom;
    }

    public void SetState(string stateJson, string cuesJson)
    {
        _state = stateJson;
        _cues = cuesJson;
    }

    public void SetState(string stateJson) => _state = stateJson;
    public void SetCues(string cuesJson) => _cues = cuesJson;

    private static readonly WebReply Pending = new(false, Array.Empty<byte>(), null);
    private static WebReply Done(byte[] response, WebCommand? command = null) => new(true, response, command);
    private static WebReply Json(int status, string body, params string[] extra) =>
        Done(WebRemote.BuildResponse(status, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(body), extra));

    /// <summary>Behandler byte mottatt så langt fra én klient (<paramref name="client"/> = adressen).</summary>
    public WebReply Handle(byte[] data, int length, string client)
    {
        client = client.Length > 64 ? client[..64] : client;
        var parsed = WebRemote.Parse(data, length);
        if (parsed.Kind == WebParseKind.Incomplete)
            return length >= WebRemote.MaxRequestBytes ? Done(WebRemote.PlainResponse(413)) : Pending;
        if (parsed.Kind == WebParseKind.Error) return Done(WebRemote.PlainResponse(parsed.Status));
        var r = parsed.Request!;
        long now = _now();
        WebReply? deny;

        switch (r.Path)
        {
            case "/":
                if (r.Method != "GET") return Done(WebRemote.PlainResponse(405, "Allow: GET"));
                var auth = Authorize(r, client, now, out deny);
                if (deny != null) return deny;
                return Done(WebRemote.BuildResponse(200, "text/html; charset=utf-8", auth ? _controlPage : _loginPage));

            case "/api/login":
                if (r.Method != "POST") return Done(WebRemote.PlainResponse(405, "Allow: POST"));
                return Login(r, client, now);

            case "/api/state":
            case "/api/cues":
                if (r.Method != "GET") return Done(WebRemote.PlainResponse(405, "Allow: GET"));
                if (!Authorize(r, client, now, out deny)) return deny ?? Json(401, "{\"error\":\"auth\"}");
                return Json(200, r.Path == "/api/state" ? _state : _cues);

            case "/api/cmd":
                if (r.Method != "POST") return Done(WebRemote.PlainResponse(405, "Allow: POST"));
                if (!Authorize(r, client, now, out deny)) return deny ?? Json(401, "{\"error\":\"auth\"}");
                // Egen overskrift gjør at andre nettsider ikke kan sende kommandoer med sin egen skjema-innsending.
                if (!r.Headers.TryGetValue("x-showcue", out var xs) || xs != "1") return Json(403, "{\"error\":\"header\"}");
                var cmd = ParseCommand(r.Body);
                if (cmd == null) return Json(400, "{\"error\":\"command\"}");
                return Done(WebRemote.BuildResponse(200, "application/json; charset=utf-8", Encoding.ASCII.GetBytes("{\"ok\":true}")), cmd);

            default:
                return Done(WebRemote.PlainResponse(404));
        }
    }

    private static WebCommand? ParseCommand(byte[] body)
    {
        if (body.Length > 32) return null;
        var text = Encoding.ASCII.GetString(body);
        return text switch
        {
            "cmd=go" => WebCommand.Go,
            "cmd=stop" => WebCommand.StopAll,
            "cmd=pause" => WebCommand.Pause,
            "cmd=resume" => WebCommand.Resume,
            "cmd=next" => WebCommand.Next,
            "cmd=previous" => WebCommand.Previous,
            _ => null
        };
    }

    private WebReply Login(WebRequest r, string client, long now)
    {
        if (IsLocked(client, now, out var retry)) return Locked(retry);
        var body = Encoding.ASCII.GetString(r.Body);
        if (!body.StartsWith("pin=", StringComparison.Ordinal)) return Json(400, "{\"error\":\"body\"}");
        if (!WebRemote.ConstantTimeEquals(body[4..], _pin))
        {
            RecordFailure(client, now);
            return Json(401, "{\"ok\":false}");
        }
        var token = Convert.ToHexString(_random(16)).ToLowerInvariant();
        lock (_lock)
        {
            _sessions.RemoveAll(s => s.Expires <= now);
            while (_sessions.Count >= MaxSessions) _sessions.RemoveAt(0);
            _sessions.Add((token, now + SessionLifetimeMs));
            if (_clients.TryGetValue(client, out var a)) a.Failures.Clear();
        }
        return Json(200, "{\"ok\":true}",
            $"Set-Cookie: {CookieName}={token}; Path=/; HttpOnly; SameSite=Strict; Max-Age={SessionLifetimeMs / 1000}");
    }

    private static WebReply Locked(int retrySeconds) =>
        Json(429, "{\"error\":\"locked\"}", "Retry-After: " + retrySeconds.ToString(CultureInfo.InvariantCulture));

    /// <summary>Gyldig økt-cookie eller PIN i overskriften «X-ShowCue-Pin». <paramref name="deny"/> settes når svaret allerede er avgjort (sperret / feil PIN).</summary>
    private bool Authorize(WebRequest r, string client, long now, out WebReply? deny)
    {
        deny = null;
        if (r.Headers.TryGetValue("cookie", out var cookie) && HasValidSession(cookie, now)) return true;
        if (r.Headers.TryGetValue("x-showcue-pin", out var pin))
        {
            if (IsLocked(client, now, out var retry)) { deny = Locked(retry); return false; }
            if (WebRemote.ConstantTimeEquals(pin, _pin)) return true;
            RecordFailure(client, now);
            deny = Json(401, "{\"error\":\"auth\"}");
        }
        return false;
    }

    private bool HasValidSession(string cookieHeader, long now)
    {
        foreach (var part in cookieHeader.Split(';'))
        {
            var t = part.Trim();
            if (!t.StartsWith(CookieName + "=", StringComparison.Ordinal)) continue;
            var v = t[(CookieName.Length + 1)..];
            if (v.Length != 32 || !v.All(char.IsAsciiHexDigitLower)) continue;
            bool found = false;
            lock (_lock)
            {
                // Går gjennom alle øktene uten å stoppe tidlig, så tiden ikke avslører noe.
                foreach (var s in _sessions)
                    if (WebRemote.ConstantTimeEquals(v, s.Token) & s.Expires > now) found = true;
            }
            if (found) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ Sperre mot gjetting
    private Attempts ClientAttempts(string client, long now)
    {
        if (_clients.TryGetValue(client, out var a)) return a;
        if (_clients.Count >= MaxTrackedClients)
        {
            foreach (var key in _clients.Where(k => k.Value.LockedUntil <= now && (k.Value.Failures.Count == 0 || now - k.Value.Failures.Peek() > FailureWindowMs)).Select(k => k.Key).ToList())
                _clients.Remove(key);
            if (_clients.Count >= MaxTrackedClients) client = "*"; // full tabell: alle nye deler én teller
            if (_clients.TryGetValue(client, out a)) return a;
        }
        a = new Attempts();
        _clients[client] = a;
        return a;
    }

    public bool IsLocked(string client, long now, out int retrySeconds)
    {
        lock (_lock)
        {
            long until = Math.Max(ClientAttempts(client, now).LockedUntil, _global.LockedUntil);
            if (until > now)
            {
                retrySeconds = (int)Math.Max(1, (until - now + 999) / 1000);
                return true;
            }
        }
        retrySeconds = 0;
        return false;
    }

    private void RecordFailure(string client, long now)
    {
        lock (_lock)
        {
            Record(ClientAttempts(client, now), now, MaxFailures, FailureWindowMs, LockoutMs);
            Record(_global, now, GlobalMaxFailures, GlobalWindowMs, GlobalLockoutMs);
        }
    }

    private static void Record(Attempts a, long now, int max, long window, long lockout)
    {
        while (a.Failures.Count > 0 && now - a.Failures.Peek() > window) a.Failures.Dequeue();
        a.Failures.Enqueue(now);
        if (a.Failures.Count >= max)
        {
            a.LockedUntil = now + lockout;
            a.Failures.Clear();
        }
    }
}
