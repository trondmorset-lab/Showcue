import Foundation

/// Kommandoene nettsiden kan sende. Utføres av `RemoteRouter` via samme grensesnitt som OSC.
enum WebCommand: Equatable { case go, stopAll, pause, resume, next, previous }

enum WebParseKind { case incomplete, ok, error }

/// En lest HTTP-forespørsel. Overskriftsnavn er med små bokstaver.
struct WebRequest {
    var method = ""
    var path = ""
    var headers: [String: String] = [:]
    var body: [UInt8] = []
}

/// Resultat av tolking: ufullstendig (les mer), ferdig forespørsel, eller feil med HTTP-statuskode.
struct WebParseResult {
    var kind: WebParseKind
    var status: Int
    var request: WebRequest?
}

/// Svaret på ett kall til `WebRemoteCore.handle`. `complete` = false betyr «les flere byte».
struct WebReply {
    var complete: Bool
    var response: [UInt8]
    var command: WebCommand?
}

struct WebPlaying {
    var number: String, name: String, type: String
    var elapsed: Double, remaining: Double
    var hasDuration: Bool, preWaiting: Bool
}
struct WebNext { var number: String, name: String, type: String, notes: String }
struct WebStateInput {
    var showName: String
    var paused: Bool
    var showMode: Bool
    var next: WebNext?
    var position: Int
    var count: Int
    var playing: [WebPlaying]
}
struct WebCueInfo { var number: String, name: String, type: String, disabled: Bool }

enum WebRemoteError: Error { case unacceptablePin }

/// Ren logikk for nettsiden som styrer showet fra mobil eller nettbrett: tolking av HTTP, PIN-regler, JSON og sidene.
/// Ingen nettverk, ingen filer og ingen tråder her, så alt kan testes med byte-matriser.
enum WebRemote {
    static let defaultPort = 8088
    static let maxRequestLine = 2048
    static let maxHeaderBytes = 8192
    static let maxHeaders = 32
    static let maxBody = 1024
    static let maxRequestBytes = maxHeaderBytes + maxBody
    static let pinMin = 6
    static let pinMax = 32
    static let maxListedCues = 1000
    static let maxListedPlaying = 50

    private static let weakPins: [String] = [
        "123456", "1234567", "12345678", "123456789", "1234567890", "654321", "987654321", "123123", "121212", "112233", "012345",
        "password", "passord", "qwerty", "qwerty123", "abc123", "abcdef", "letmein", "welcome", "admin", "showcue", "showcue1",
    ]

    static func systemRandom(_ count: Int) -> [UInt8] {
        var g = SystemRandomNumberGenerator()
        return (0..<count).map { _ in UInt8.random(in: 0...255, using: &g) }
    }

    // MARK: PIN

    private static func isAsciiLetterOrDigit(_ b: UInt8) -> Bool {
        (b >= 48 && b <= 57) || (b >= 65 && b <= 90) || (b >= 97 && b <= 122)
    }

    /// PIN: 6–32 bokstaver (a–z, A–Z) eller siffer, ikke bare ett tegn gjentatt og ikke på lista over lette koder.
    static func isAcceptablePin(_ pin: String?) -> Bool {
        guard let pin else { return false }
        let bytes = Array(pin.utf8)
        guard bytes.count >= pinMin, bytes.count <= pinMax else { return false }
        guard bytes.allSatisfy(isAsciiLetterOrDigit) else { return false }
        if bytes.allSatisfy({ $0 == bytes[0] }) { return false }
        return !weakPins.contains { $0.caseInsensitiveCompare(pin) == .orderedSame }
    }

    /// Lager en tilfeldig 6-sifret PIN (aldri en lett kode).
    static func generatePin(random: ((Int) -> [UInt8])? = nil) -> String {
        let rnd = random ?? systemRandom
        let range: UInt32 = 1_000_000
        let limit = UInt32.max - (UInt32.max % range) // unngår skjevhet
        for _ in 0..<1000 {
            let b = rnd(4)
            guard b.count >= 4 else { continue }
            let v = UInt32(b[0]) | UInt32(b[1]) << 8 | UInt32(b[2]) << 16 | UInt32(b[3]) << 24
            if v >= limit { continue }
            let pin = String(format: "%06d", Int(v % range))
            if isAcceptablePin(pin) { return pin }
        }
        return "739518" // nås bare med en ødelagt tilfeldighetskilde i test
    }

    /// Godkjent PIN beholdes. Ellers: ny PIN når nettsiden er på, tom når den er av.
    static func normalizePin(_ pin: String?, enabled: Bool, random: ((Int) -> [UInt8])? = nil) -> String {
        let p = (pin ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        if isAcceptablePin(p) { return p }
        return enabled ? generatePin(random: random) : ""
    }

    /// Sammenligner to tekster i like lang tid uansett hvor de skiller seg (maks 64 byte; lengre gir alltid false).
    static func constantTimeEquals(_ a: String, _ b: String) -> Bool {
        let x = Array(a.utf8), y = Array(b.utf8)
        var diff = x.count ^ y.count
        for i in 0..<64 {
            let p = i < x.count ? Int(x[i]) : 0
            let q = i < y.count ? Int(y[i]) : 0
            diff |= p ^ q
        }
        return diff == 0 && x.count <= 64
    }

    // MARK: HTTP-tolking

    private static func fail(_ status: Int) -> WebParseResult { WebParseResult(kind: .error, status: status, request: nil) }
    private static let pending = WebParseResult(kind: .incomplete, status: 0, request: nil)

    private static func indexOfCrLf(_ d: [UInt8], _ n: Int) -> Int? {
        var i = 0
        while i + 1 < n { if d[i] == 13 && d[i + 1] == 10 { return i }; i += 1 }
        return nil
    }

    private static func indexOfHeaderEnd(_ d: [UInt8], _ n: Int) -> Int? {
        var i = 0
        while i + 3 < n { if d[i] == 13 && d[i + 1] == 10 && d[i + 2] == 13 && d[i + 3] == 10 { return i }; i += 1 }
        return nil
    }

    private static func isTokenByte(_ b: UInt8) -> Bool {
        isAsciiLetterOrDigit(b) || "!#$%&'*+-.^_`|~".utf8.contains(b)
    }

    private static func isDigits(_ s: String) -> Bool { !s.isEmpty && s.utf8.allSatisfy { $0 >= 48 && $0 <= 57 } }

    /// Deler ASCII-bytene i `d[0..<end]` på CRLF.
    private static func splitLines(_ d: [UInt8], _ end: Int) -> [String] {
        var lines: [String] = []
        var start = 0
        var i = 0
        while i < end {
            if d[i] == 13 && i + 1 < end && d[i + 1] == 10 {
                lines.append(String(decoding: d[start..<i], as: UTF8.self))
                i += 2
                start = i
            } else {
                i += 1
            }
        }
        lines.append(String(decoding: d[start..<end], as: UTF8.self))
        return lines
    }

    /// Tolker en HTTP/1.1-forespørsel fra byte som er mottatt så langt. Strengt: bare ASCII, bare CRLF, bare GET og POST,
    /// ingen kodede tegn i stien, ingen «..», ingen delt kroppsoverføring, grenser for linje, hoder og kropp.
    static func parse(_ data: [UInt8], length: Int) -> WebParseResult {
        let n = max(0, min(length, data.count))
        guard let lineEnd = indexOfCrLf(data, n) else { return n > maxRequestLine ? fail(414) : pending }
        if lineEnd > maxRequestLine { return fail(414) }
        guard let headEnd = indexOfHeaderEnd(data, n) else { return n >= maxHeaderBytes ? fail(431) : pending }
        let bodyStart = headEnd + 4
        if bodyStart > maxHeaderBytes { return fail(431) }

        for i in 0..<bodyStart {
            let b = data[i]
            if b == 13 { if i + 1 >= bodyStart || data[i + 1] != 10 { return fail(400) } }
            else if b == 10 { if i == 0 || data[i - 1] != 13 { return fail(400) } }
            else if (b < 0x20 && b != 9) || b >= 0x7F { return fail(400) }
        }

        let lines = splitLines(data, headEnd)
        let parts = lines[0].split(separator: " ", omittingEmptySubsequences: false).map(String.init)
        if parts.count != 3 || parts.contains(where: { $0.isEmpty }) { return fail(400) }
        let method = parts[0]
        if method.utf8.count > 16 || !method.utf8.allSatisfy({ $0 >= 65 && $0 <= 90 }) { return fail(400) }
        let target = parts[1]
        if !target.hasPrefix("/") || target.contains("#") || target.contains("%") || target.contains("\\") { return fail(400) }
        let path = target.firstIndex(of: "?").map { String(target[target.startIndex..<$0]) } ?? target
        if path.contains("..") { return fail(400) }
        if parts[2] != "HTTP/1.1" { return fail(parts[2].hasPrefix("HTTP/") ? 505 : 400) }
        if method != "GET" && method != "POST" { return fail(405) }

        if lines.count - 1 > maxHeaders { return fail(431) }
        var headers: [String: String] = [:]
        for line in lines.dropFirst() {
            guard let first = line.utf8.first, first != 32, first != 9 else { return fail(400) }
            guard let colon = line.firstIndex(of: ":"), colon != line.startIndex else { return fail(400) }
            let name = String(line[line.startIndex..<colon])
            if !name.utf8.allSatisfy(isTokenByte) { return fail(400) }
            let value = String(line[line.index(after: colon)...]).trimmingCharacters(in: CharacterSet(charactersIn: " \t"))
            let key = name.lowercased()
            if headers[key] != nil { return fail(400) }
            headers[key] = value
        }
        if headers["transfer-encoding"] != nil || headers["host"] == nil { return fail(400) }

        var contentLength = 0
        var hasLength = false
        if let cl = headers["content-length"] {
            hasLength = true
            if cl.utf8.count > 7 || !isDigits(cl) { return fail(400) }
            contentLength = Int(cl) ?? 0
            if contentLength > maxBody { return fail(413) }
        }
        if method == "GET" && contentLength != 0 { return fail(400) }
        if method == "POST" && !hasLength { return fail(411) }
        if n - bodyStart < contentLength { return pending }

        let body = Array(data[bodyStart..<(bodyStart + contentLength)])
        return WebParseResult(kind: .ok, status: 200, request: WebRequest(method: method, path: path, headers: headers, body: body))
    }

    // MARK: Svar

    static func reason(_ status: Int) -> String {
        switch status {
        case 200: return "OK"
        case 400: return "Bad Request"
        case 401: return "Unauthorized"
        case 403: return "Forbidden"
        case 404: return "Not Found"
        case 405: return "Method Not Allowed"
        case 408: return "Request Timeout"
        case 411: return "Length Required"
        case 413: return "Content Too Large"
        case 414: return "URI Too Long"
        case 429: return "Too Many Requests"
        case 431: return "Request Header Fields Too Large"
        case 503: return "Service Unavailable"
        case 505: return "HTTP Version Not Supported"
        default: return "Error"
        }
    }

    private static let csp = "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'"

    /// Bygger et helt HTTP/1.1-svar. Alltid «Connection: close», aldri CORS-hoder, aldri hurtigbuffer.
    static func buildResponse(_ status: Int, contentType: String, body: [UInt8], extraHeaders: [String] = []) -> [UInt8] {
        var head = "HTTP/1.1 \(status) \(reason(status))\r\n"
        head += "Content-Type: \(contentType)\r\n"
        head += "Content-Length: \(body.count)\r\n"
        head += "Connection: close\r\n"
        head += "Cache-Control: no-store\r\n"
        head += "X-Content-Type-Options: nosniff\r\n"
        head += "X-Frame-Options: DENY\r\n"
        head += "Referrer-Policy: no-referrer\r\n"
        head += "Content-Security-Policy: \(csp)\r\n"
        for h in extraHeaders { head += h + "\r\n" }
        head += "\r\n"
        return Array(head.utf8) + body
    }

    /// Kort tekstsvar med bare statusteksten (ingenting fra forespørselen speiles tilbake).
    static func plainResponse(_ status: Int, extraHeaders: [String] = []) -> [UInt8] {
        buildResponse(status, contentType: "text/plain; charset=utf-8", body: Array(reason(status).utf8), extraHeaders: extraHeaders)
    }

    // MARK: JSON

    static func jsonEscape(_ s: String) -> String {
        var out = ""
        for u in s.unicodeScalars {
            switch u.value {
            case 0x22: out += "\\\""
            case 0x5C: out += "\\\\"
            case 0x3C, 0x3E, 0x26, 0x2028, 0x2029: out += String(format: "\\u%04x", u.value)
            case 0..<0x20: out += String(format: "\\u%04x", u.value)
            default: out.unicodeScalars.append(u)
            }
        }
        return out
    }

    private static func cut(_ s: String, _ max: Int) -> String { s.count <= max ? s : String(s.prefix(max)) }
    private static func str(_ s: String, _ max: Int) -> String { "\"" + jsonEscape(cut(s, max)) + "\"" }
    private static func num(_ v: Double) -> String {
        let x = v.isFinite ? (min(max(v, 0), 1e9) * 10).rounded(.toNearestOrEven) / 10 : 0
        return String(format: "%.1f", x)
    }
    private static func bool(_ b: Bool) -> String { b ? "true" : "false" }

    /// Tilstanden siden henter ca. to ganger i sekundet.
    static func buildStateJson(_ s: WebStateInput) -> String {
        var o = "{\"v\":1,\"show\":" + str(s.showName, 80)
        o += ",\"paused\":" + bool(s.paused)
        o += ",\"lock\":" + bool(s.showMode)
        o += ",\"pos\":\(max(0, s.position)),\"count\":\(max(0, s.count))"
        o += ",\"next\":"
        if let n = s.next {
            o += "{\"n\":" + str(n.number, 40)
            o += ",\"name\":" + str(n.name, 80)
            o += ",\"type\":" + str(n.type, 40)
            o += ",\"notes\":" + str(n.notes, 200) + "}"
        } else {
            o += "null"
        }
        o += ",\"playing\":["
        var first = true
        for p in s.playing.prefix(maxListedPlaying) {
            if !first { o += "," }
            first = false
            o += "{\"n\":" + str(p.number, 40)
            o += ",\"name\":" + str(p.name, 80)
            o += ",\"type\":" + str(p.type, 40)
            o += ",\"el\":" + num(p.elapsed)
            o += ",\"rem\":" + num(p.remaining)
            o += ",\"dur\":" + bool(p.hasDuration)
            o += ",\"pre\":" + bool(p.preWaiting) + "}"
        }
        o += "]}"
        return o
    }

    /// Cuelista (bare lesing): nummer, navn, type og om cuen er deaktivert.
    static func buildCuesJson(_ cues: [WebCueInfo]) -> String {
        var o = "{\"cues\":["
        var first = true
        for c in cues.prefix(maxListedCues) {
            if !first { o += "," }
            first = false
            o += "{\"n\":" + str(c.number, 40)
            o += ",\"name\":" + str(c.name, 80)
            o += ",\"type\":" + str(c.type, 40)
            o += ",\"off\":" + bool(c.disabled) + "}"
        }
        o += "]}"
        return o
    }

    // MARK: Sider

    static func htmlEscape(_ s: String) -> String {
        s.replacingOccurrences(of: "&", with: "&amp;").replacingOccurrences(of: "<", with: "&lt;")
            .replacingOccurrences(of: ">", with: "&gt;").replacingOccurrences(of: "\"", with: "&quot;")
            .replacingOccurrences(of: "'", with: "&#39;")
    }

    /// Tekstene i sidene (norsk er nøkkelen i oversettelsen). Nøklene brukes som {{navn}} i malene.
    static func pageTexts(language: String) -> [String: String] {
        [
            "lang": language,
            "next": L("Neste cue"),
            "playing": L("Spiller nå"),
            "nothing": L("Ingenting spiller"),
            "end": L("— slutt på lista —"),
            "stopAll": L("Stopp alt"),
            "pause": L("Pause"),
            "resume": L("Fortsett"),
            "nextBtn": L("Neste"),
            "prevBtn": L("Forrige"),
            "cues": L("Cuer"),
            "lost": L("Ingen kontakt med ShowCue"),
            "wait": L("Venter"),
            "showMode": L("Showmodus"),
            "pinLabel": L("PIN-kode"),
            "login": L("Logg inn"),
            "wrong": L("Feil PIN-kode"),
            "locked": L("For mange forsøk. Vent et minutt og prøv igjen."),
        ]
    }

    /// Setter tekstene inn i malen. Alt går gjennom `htmlEscape`, og ukjente navn blir tomme.
    static func fill(_ template: String, _ texts: [String: String]) -> String {
        var out = ""
        var rest = Substring(template)
        while let open = rest.range(of: "{{") {
            guard let close = rest.range(of: "}}", range: open.upperBound..<rest.endIndex) else { break }
            out += rest[rest.startIndex..<open.lowerBound]
            let key = String(rest[open.upperBound..<close.lowerBound])
            if let v = texts[key] { out += htmlEscape(v) }
            rest = rest[close.upperBound...]
        }
        out += rest
        return out
    }

    static func loginPage(_ texts: [String: String]) -> [UInt8] { Array(fill(loginTemplate, texts).utf8) }
    static func controlPage(_ texts: [String: String]) -> [UInt8] { Array(fill(controlTemplate, texts).utf8) }

    /// Adressen som vises for brukeren, f.eks. «http://192.168.1.20:8088». Tom adresseliste gir «localhost».
    static func url(_ addresses: [String], port: Int) -> String {
        "http://" + (addresses.first ?? "localhost") + ":" + String(port)
    }

    /// Kort navn på kommandoen, til loggen.
    static func commandName(_ c: WebCommand) -> String {
        switch c {
        case .go: return "GO"
        case .stopAll: return L("stopp alt")
        case .pause: return L("pause")
        case .resume: return L("fortsett")
        case .next: return L("neste")
        case .previous: return L("forrige")
        }
    }

    private static let loginTemplate = #"""
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
"""#

    private static let controlTemplate = #"""
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
"""#
}

/// Ruter og PIN-kontroll for nettsiden. Tar imot byte, gir byte tilbake, og kjenner bare tid, tilfeldighet og klientadresse
/// som parametre. Trådsikker (ett lås rundt øktene og forsøkene som feilet).
/// Faste ruter: GET / (innloggingssiden eller kontrollsiden), POST /api/login, GET /api/state, GET /api/cues, POST /api/cmd.
/// Alt annet gir 404. Ingen sti fra forespørselen brukes mot disken.
final class WebRemoteCore {
    static let maxFailures = 5
    static let failureWindowMs: Int64 = 60_000
    static let lockoutMs: Int64 = 60_000
    static let globalMaxFailures = 30
    static let globalWindowMs: Int64 = 600_000
    static let globalLockoutMs: Int64 = 300_000
    static let sessionLifetimeMs: Int64 = 12 * 3600 * 1000
    static let maxSessions = 32
    static let maxTrackedClients = 256
    static let cookieName = "sc_session"

    private final class Attempts {
        var failures: [Int64] = []
        var lockedUntil: Int64 = 0
    }

    private let pin: String
    private let now: () -> Int64
    private let random: (Int) -> [UInt8]
    private let loginPage: [UInt8]
    private let controlPage: [UInt8]
    private let lock = NSLock()
    private var clients: [String: Attempts] = [:]
    private let global = Attempts()
    private var sessions: [(token: String, expires: Int64)] = []
    private var state = "{\"v\":1}"
    private var cues = "{\"cues\":[]}"

    /// - Parameter now: stigende klokke i millisekunder.
    init(pin: String, loginPage: [UInt8], controlPage: [UInt8], now: @escaping () -> Int64, random: ((Int) -> [UInt8])? = nil) throws {
        guard WebRemote.isAcceptablePin(pin) else { throw WebRemoteError.unacceptablePin }
        self.pin = pin
        self.loginPage = loginPage
        self.controlPage = controlPage
        self.now = now
        self.random = random ?? WebRemote.systemRandom
    }

    func setState(_ json: String) { lock.lock(); state = json; lock.unlock() }
    func setCues(_ json: String) { lock.lock(); cues = json; lock.unlock() }
    func setState(_ stateJson: String, cues cuesJson: String) { lock.lock(); state = stateJson; cues = cuesJson; lock.unlock() }

    private static let waiting = WebReply(complete: false, response: [], command: nil)
    private static func done(_ response: [UInt8], _ command: WebCommand? = nil) -> WebReply { WebReply(complete: true, response: response, command: command) }
    private static func json(_ status: Int, _ body: String, _ extra: [String] = []) -> WebReply {
        done(WebRemote.buildResponse(status, contentType: "application/json; charset=utf-8", body: Array(body.utf8), extraHeaders: extra))
    }

    /// Behandler byte mottatt så langt fra én klient (`client` = adressen).
    func handle(_ data: [UInt8], length: Int, client rawClient: String) -> WebReply {
        let client = rawClient.count > 64 ? String(rawClient.prefix(64)) : rawClient
        let parsed = WebRemote.parse(data, length: length)
        switch parsed.kind {
        case .incomplete: return length >= WebRemote.maxRequestBytes ? Self.done(WebRemote.plainResponse(413)) : Self.waiting
        case .error: return Self.done(WebRemote.plainResponse(parsed.status))
        case .ok: break
        }
        let r = parsed.request!
        let t = now()

        switch r.path {
        case "/":
            if r.method != "GET" { return Self.done(WebRemote.plainResponse(405, extraHeaders: ["Allow: GET"])) }
            let a = authorize(r, client, t)
            if let d = a.deny { return d }
            return Self.done(WebRemote.buildResponse(200, contentType: "text/html; charset=utf-8", body: a.ok ? controlPage : loginPage))

        case "/api/login":
            if r.method != "POST" { return Self.done(WebRemote.plainResponse(405, extraHeaders: ["Allow: POST"])) }
            return login(r, client, t)

        case "/api/state", "/api/cues":
            if r.method != "GET" { return Self.done(WebRemote.plainResponse(405, extraHeaders: ["Allow: GET"])) }
            let a = authorize(r, client, t)
            if !a.ok { return a.deny ?? Self.json(401, "{\"error\":\"auth\"}") }
            lock.lock(); let body = r.path == "/api/state" ? state : cues; lock.unlock()
            return Self.json(200, body)

        case "/api/cmd":
            if r.method != "POST" { return Self.done(WebRemote.plainResponse(405, extraHeaders: ["Allow: POST"])) }
            let a = authorize(r, client, t)
            if !a.ok { return a.deny ?? Self.json(401, "{\"error\":\"auth\"}") }
            // Egen overskrift gjør at andre nettsider ikke kan sende kommandoer med sin egen skjema-innsending.
            if r.headers["x-showcue"] != "1" { return Self.json(403, "{\"error\":\"header\"}") }
            guard let cmd = Self.parseCommand(r.body) else { return Self.json(400, "{\"error\":\"command\"}") }
            return Self.done(WebRemote.buildResponse(200, contentType: "application/json; charset=utf-8", body: Array("{\"ok\":true}".utf8)), cmd)

        default:
            return Self.done(WebRemote.plainResponse(404))
        }
    }

    private static func parseCommand(_ body: [UInt8]) -> WebCommand? {
        if body.count > 32 { return nil }
        switch String(decoding: body, as: UTF8.self) {
        case "cmd=go": return .go
        case "cmd=stop": return .stopAll
        case "cmd=pause": return .pause
        case "cmd=resume": return .resume
        case "cmd=next": return .next
        case "cmd=previous": return .previous
        default: return nil
        }
    }

    private func login(_ r: WebRequest, _ client: String, _ t: Int64) -> WebReply {
        if let retry = lockedFor(client, t) { return Self.locked(retry) }
        let body = String(decoding: r.body, as: UTF8.self)
        guard body.hasPrefix("pin=") else { return Self.json(400, "{\"error\":\"body\"}") }
        if !WebRemote.constantTimeEquals(String(body.dropFirst(4)), pin) {
            recordFailure(client, t)
            return Self.json(401, "{\"ok\":false}")
        }
        let token = random(16).map { String(format: "%02x", $0) }.joined()
        lock.lock()
        sessions.removeAll { $0.expires <= t }
        while sessions.count >= Self.maxSessions { sessions.removeFirst() }
        sessions.append((token, t + Self.sessionLifetimeMs))
        clients[client]?.failures.removeAll()
        lock.unlock()
        return Self.json(200, "{\"ok\":true}",
                         ["Set-Cookie: \(Self.cookieName)=\(token); Path=/; HttpOnly; SameSite=Strict; Max-Age=\(Self.sessionLifetimeMs / 1000)"])
    }

    private static func locked(_ retrySeconds: Int) -> WebReply { json(429, "{\"error\":\"locked\"}", ["Retry-After: \(retrySeconds)"]) }

    /// Gyldig økt-cookie eller PIN i overskriften «X-ShowCue-Pin». `deny` er satt når svaret allerede er avgjort (sperret / feil PIN).
    private func authorize(_ r: WebRequest, _ client: String, _ t: Int64) -> (ok: Bool, deny: WebReply?) {
        if let cookie = r.headers["cookie"], hasValidSession(cookie, t) { return (true, nil) }
        if let given = r.headers["x-showcue-pin"] {
            if let retry = lockedFor(client, t) { return (false, Self.locked(retry)) }
            if WebRemote.constantTimeEquals(given, pin) { return (true, nil) }
            recordFailure(client, t)
            return (false, Self.json(401, "{\"error\":\"auth\"}"))
        }
        return (false, nil)
    }

    private func hasValidSession(_ cookieHeader: String, _ t: Int64) -> Bool {
        let prefix = Self.cookieName + "="
        for part in cookieHeader.split(separator: ";") {
            let item = part.trimmingCharacters(in: .whitespaces)
            guard item.hasPrefix(prefix) else { continue }
            let v = String(item.dropFirst(prefix.count))
            guard v.utf8.count == 32, v.utf8.allSatisfy({ ($0 >= 48 && $0 <= 57) || ($0 >= 97 && $0 <= 102) }) else { continue }
            var found = false
            lock.lock()
            // Går gjennom alle øktene uten å stoppe tidlig, så tiden ikke avslører noe.
            for s in sessions where WebRemote.constantTimeEquals(v, s.token) && s.expires > t { found = true }
            lock.unlock()
            if found { return true }
        }
        return false
    }

    // MARK: Sperre mot gjetting

    private func attempts(_ client: String, _ t: Int64) -> Attempts {
        if let a = clients[client] { return a }
        var key = client
        if clients.count >= Self.maxTrackedClients {
            for (k, v) in clients where v.lockedUntil <= t && (v.failures.isEmpty || t - v.failures[0] > Self.failureWindowMs) { clients[k] = nil }
            if clients.count >= Self.maxTrackedClients { key = "*" } // full tabell: alle nye deler én teller
            if let a = clients[key] { return a }
        }
        let a = Attempts()
        clients[key] = a
        return a
    }

    /// Sekunder til sperren er over, eller nil når det er lov å prøve.
    func lockedFor(_ client: String, _ t: Int64) -> Int? {
        lock.lock(); defer { lock.unlock() }
        let until = max(attempts(client, t).lockedUntil, global.lockedUntil)
        guard until > t else { return nil }
        return Int(max(1, (until - t + 999) / 1000))
    }

    private func recordFailure(_ client: String, _ t: Int64) {
        lock.lock(); defer { lock.unlock() }
        Self.record(attempts(client, t), t, Self.maxFailures, Self.failureWindowMs, Self.lockoutMs)
        Self.record(global, t, Self.globalMaxFailures, Self.globalWindowMs, Self.globalLockoutMs)
    }

    private static func record(_ a: Attempts, _ t: Int64, _ max: Int, _ window: Int64, _ lockout: Int64) {
        while let first = a.failures.first, t - first > window { a.failures.removeFirst() }
        a.failures.append(t)
        if a.failures.count >= max {
            a.lockedUntil = t + lockout
            a.failures.removeAll()
        }
    }
}
