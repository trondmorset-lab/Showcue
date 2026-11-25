using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using ShowCue.Models;

namespace ShowCue.Engine;

// Showloggen: en logg over hva som skjedde under et show (cuer startet/stoppet, pause, panikk, feil, åpnet/lagret show).
// Alt her er uten WPF, så det kan testes og stresstestes. Loggingen skal aldri blokkere, forsinke eller krasje programmet:
// hendelser legges i en begrenset kø (eldste kastes når den er full, med teller) og skrives av en egen bakgrunnstråd.
// Alle I/O-feil svelges og meldes én gang.

/// <summary>Hva slags hendelse en linje i loggen er.</summary>
public enum ShowLogKind { Start, Finish, Stop, Pause, Resume, StopAll, Playhead, Warning, Error, ShowOpened, ShowSaved, AppStart, AppExit, Dropped }

/// <summary>Hvordan en cue ble startet.</summary>
public enum StartSource { Unknown, Go, AutoFollow, AutoContinue, Hotkey, Osc, Midi, Timecode, Clock, Group, ButtonPanel, Manual }

/// <summary>Grupper av hendelser, brukt til filter i loggvinduet.</summary>
public enum ShowLogCategory { All, Cues, Control, Problems, Show }

/// <summary>Én hendelse. <paramref name="RunMs"/> er millisekunder siden loggen (kjøringen) startet.</summary>
public sealed record ShowLogEvent(DateTimeOffset Time, long RunMs, ShowLogKind Kind, string Number, string Name, string CueType, string How, string Text);

/// <summary>Tekstformat for loggfila og navn på hendelsestyper: én linje per hendelse, felt skilt med tabulator.</summary>
public static class ShowLogFormat
{
    public const int MaxNumber = 40;
    public const int MaxName = 80;
    public const int MaxText = 300;
    public const string FileHeader = "# ShowCue show log v1";
    public const string IsoFormat = "yyyy-MM-dd'T'HH:mm:ss.fffzzz";

    /// <summary>Kutter teksten til høyst <paramref name="max"/> tegn (siste tegn blir «…»). Deler aldri et surrogatpar.</summary>
    public static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.Length <= max) return s;
        int cut = Math.Max(0, max - 1);
        if (cut > 0 && char.IsHighSurrogate(s[cut - 1])) cut--;
        return s[..cut] + "…";
    }

    private static bool IsControl(char ch) => ch < 0x20 || (ch >= 0x7F && ch <= 0x9F) || ch == ' ' || ch == ' ';

    /// <summary>Gjør teksten til én trygg linjedel: skråstrek og tabulator, linjeskift og andre kontrolltegn skrives som tegnfølger.</summary>
    public static string Escape(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length + 8);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '\t': sb.Append("\\t"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                default:
                    if (!IsControl(ch)) sb.Append(ch);
                    else if (ch < 0x100) sb.Append("\\x").Append(((int)ch).ToString("X2", CultureInfo.InvariantCulture));
                    else sb.Append("\\u").Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Det motsatte av <see cref="Escape"/>. Ukjente eller ufullstendige tegnfølger beholdes som de står.</summary>
    public static string Unescape(string? s)
    {
        if (string.IsNullOrEmpty(s) || s.IndexOf('\\') < 0) return s ?? "";
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            if (ch != '\\' || i + 1 >= s.Length) { sb.Append(ch); continue; }
            var n = s[i + 1];
            switch (n)
            {
                case '\\': sb.Append('\\'); i++; break;
                case 't': sb.Append('\t'); i++; break;
                case 'n': sb.Append('\n'); i++; break;
                case 'r': sb.Append('\r'); i++; break;
                case 'x' when i + 3 < s.Length + 0 && TryHex(s, i + 2, 2, out var x): sb.Append((char)x); i += 3; break;
                case 'u' when i + 5 < s.Length + 0 && TryHex(s, i + 2, 4, out var u): sb.Append((char)u); i += 5; break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }

    private static bool TryHex(string s, int start, int len, out int value)
    {
        value = 0;
        if (start + len > s.Length) return false;
        return int.TryParse(s.AsSpan(start, len), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
    }

    public static string KindToken(ShowLogKind k) => k switch
    {
        ShowLogKind.Start => "start",
        ShowLogKind.Finish => "finish",
        ShowLogKind.Stop => "stop",
        ShowLogKind.Pause => "pause",
        ShowLogKind.Resume => "resume",
        ShowLogKind.StopAll => "stopall",
        ShowLogKind.Playhead => "playhead",
        ShowLogKind.Warning => "warning",
        ShowLogKind.Error => "error",
        ShowLogKind.ShowOpened => "open",
        ShowLogKind.ShowSaved => "save",
        ShowLogKind.AppStart => "appstart",
        ShowLogKind.AppExit => "appexit",
        ShowLogKind.Dropped => "dropped",
        _ => "error",
    };

    public static bool TryKind(string? token, out ShowLogKind kind)
    {
        foreach (var k in Enum.GetValues<ShowLogKind>())
            if (KindToken(k) == token) { kind = k; return true; }
        kind = ShowLogKind.Error;
        return false;
    }

    public static string SourceToken(StartSource s) => s switch
    {
        StartSource.Go => "go",
        StartSource.AutoFollow => "auto-follow",
        StartSource.AutoContinue => "auto-continue",
        StartSource.Hotkey => "hotkey",
        StartSource.Osc => "osc",
        StartSource.Midi => "midi",
        StartSource.Timecode => "timecode",
        StartSource.Clock => "clock",
        StartSource.Group => "group",
        StartSource.ButtonPanel => "button-panel",
        StartSource.Manual => "manual",
        _ => "",
    };

    public static ShowLogCategory CategoryOf(ShowLogKind k) => k switch
    {
        ShowLogKind.Start or ShowLogKind.Finish or ShowLogKind.Stop => ShowLogCategory.Cues,
        ShowLogKind.Pause or ShowLogKind.Resume or ShowLogKind.StopAll or ShowLogKind.Playhead => ShowLogCategory.Control,
        ShowLogKind.Warning or ShowLogKind.Error or ShowLogKind.Dropped => ShowLogCategory.Problems,
        _ => ShowLogCategory.Show,
    };

    public static bool Matches(ShowLogEvent e, ShowLogCategory category) => category == ShowLogCategory.All || CategoryOf(e.Kind) == category;

    /// <summary>Tidspunkt som ISO 8601 med millisekunder og tidssone, f.eks. 2026-11-25T14:03:22.123+01:00.</summary>
    public static string Iso(DateTimeOffset t) => t.ToString(IsoFormat, CultureInfo.InvariantCulture);

    /// <summary>Tid siden kjøringen startet som TT:MM:SS.mmm (timer kan være flere enn to sifre).</summary>
    public static string RunText(long ms)
    {
        if (ms < 0) ms = 0;
        long h = ms / 3_600_000, m = ms / 60_000 % 60, s = ms / 1000 % 60, f = ms % 1000;
        return string.Create(CultureInfo.InvariantCulture, $"{h:00}:{m:00}:{s:00}.{f:000}");
    }

    public static bool TryParseRun(string? text, out long ms)
    {
        ms = 0;
        if (text == null) return false;
        var parts = text.Split(':');
        if (parts.Length != 3) return false;
        var sec = parts[2].Split('.');
        if (sec.Length != 2 || sec[1].Length != 3) return false;
        if (!long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var h)
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var m)
            || !long.TryParse(sec[0], NumberStyles.None, CultureInfo.InvariantCulture, out var s)
            || !long.TryParse(sec[1], NumberStyles.None, CultureInfo.InvariantCulture, out var f)) return false;
        if (m > 59 || s > 59 || h > 100_000) return false;
        ms = ((h * 60 + m) * 60 + s) * 1000 + f;
        return true;
    }

    /// <summary>Én loggline: tid, tid siden start, type, cuenummer, cuetype, hvordan, navn, tekst (skilt med tabulator, uten linjeskift).</summary>
    public static string FormatLine(ShowLogEvent e) => string.Join('\t',
        Iso(e.Time), RunText(e.RunMs), KindToken(e.Kind),
        Escape(Truncate(e.Number, MaxNumber)), Escape(Truncate(e.CueType, MaxNumber)), Escape(Truncate(e.How, MaxNumber)),
        Escape(Truncate(e.Name, MaxName)), Escape(Truncate(e.Text, MaxText)));

    /// <summary>Leser en loggline. Kommentarlinjer (#), tomme linjer og linjer som ikke kan tolkes gir false.</summary>
    public static bool TryParseLine(string? line, out ShowLogEvent ev)
    {
        ev = null!;
        if (string.IsNullOrEmpty(line) || line[0] == '#') return false;
        var f = line.Split('\t');
        if (f.Length != 8) return false;
        if (!DateTimeOffset.TryParseExact(f[0], IsoFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) return false;
        if (!TryParseRun(f[1], out var ms)) return false;
        if (!TryKind(f[2], out var kind)) return false;
        ev = new ShowLogEvent(t, ms, kind, Unescape(f[3]), Unescape(f[6]), Unescape(f[4]), Unescape(f[5]), Unescape(f[7]));
        return true;
    }
}

/// <summary>Eksport av loggen til CSV og ren tekst, og innlesing av en loggfil.</summary>
public static class ShowLogExport
{
    public const string CsvHeader = "time,run,event,cue,type,how,name,text";

    /// <summary>Ett CSV-felt. Felt som begynner med = + - @ (eller tabulator/linjeskift) får en apostrof foran, så et regneark
    /// aldri tolker dem som formel. Felt med komma, anførselstegn eller linjeskift settes i anførselstegn.</summary>
    public static string CsvCell(string? s)
    {
        s ??= "";
        if (s.Length > 0 && s[0] is '=' or '+' or '-' or '@' or '\t' or '\r') s = "'" + s;
        var sb = new StringBuilder(s.Length + 2);
        foreach (var ch in s)
        {
            // Linjeskift beholdes (gyldig i et felt med anførselstegn); andre kontrolltegn blir mellomrom.
            if (ch is '\n' or '\r' || !(ch < 0x20 || (ch >= 0x7F && ch <= 0x9F) || ch is ' ' or ' ')) sb.Append(ch);
            else sb.Append(' ');
        }
        s = sb.ToString();
        bool quote = s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 || s.StartsWith(' ') || s.EndsWith(' ');
        return quote ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    public static string CsvRow(ShowLogEvent e) => string.Join(',',
        CsvCell(ShowLogFormat.Iso(e.Time)), CsvCell(ShowLogFormat.RunText(e.RunMs)), CsvCell(ShowLogFormat.KindToken(e.Kind)),
        CsvCell(ShowLogFormat.Truncate(e.Number, ShowLogFormat.MaxNumber)), CsvCell(ShowLogFormat.Truncate(e.CueType, ShowLogFormat.MaxNumber)),
        CsvCell(ShowLogFormat.Truncate(e.How, ShowLogFormat.MaxNumber)),
        CsvCell(ShowLogFormat.Truncate(e.Name, ShowLogFormat.MaxName)), CsvCell(ShowLogFormat.Truncate(e.Text, ShowLogFormat.MaxText)));

    /// <summary>Hele CSV-fila (overskrift + én rad per hendelse, CRLF).</summary>
    public static string ToCsv(IEnumerable<ShowLogEvent> events)
    {
        var sb = new StringBuilder(CsvHeader).Append("\r\n");
        foreach (var e in events) sb.Append(CsvRow(e)).Append("\r\n");
        return sb.ToString();
    }

    /// <summary>Ren tekst i samme format som loggfila (kan leses inn igjen).</summary>
    public static string ToText(IEnumerable<ShowLogEvent> events)
    {
        var sb = new StringBuilder(ShowLogFormat.FileHeader).Append('\n');
        foreach (var e in events) sb.Append(ShowLogFormat.FormatLine(e)).Append('\n');
        return sb.ToString();
    }

    /// <summary>Leser hendelsene i en loggtekst. Linjer som ikke kan tolkes hoppes over (og telles).</summary>
    public static List<ShowLogEvent> Parse(IEnumerable<string> lines, out int skipped)
    {
        var list = new List<ShowLogEvent>();
        skipped = 0;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#') continue;
            if (ShowLogFormat.TryParseLine(line, out var ev)) list.Add(ev); else skipped++;
        }
        return list;
    }

    /// <summary>Leser en loggfil (også en som programmet skriver til akkurat nå).</summary>
    public static List<ShowLogEvent> ReadFile(string path, out int skipped)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line) lines.Add(line);
        return Parse(lines, out skipped);
    }
}

/// <summary>Valg av loggfiler: navn, og hvilke gamle filer som skal slettes.</summary>
public static class ShowLogFiles
{
    public const int FilesToKeep = 30;
    public const long MaxFileBytes = 5L * 1024 * 1024;

    public static string DirectoryFor(string root) => Path.Combine(root, "Logs");

    /// <summary>Filnavn for en kjøring (med løpenummer hvis flere starter samme sekund).</summary>
    public static string FileName(DateTimeOffset start, int attempt = 1) =>
        $"showlog {start.ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture)}{(attempt > 1 ? $" ({attempt})" : "")}.log";

    private static readonly System.Text.RegularExpressions.Regex NamePattern =
        new(@"^showlog \d{4}-\d{2}-\d{2} \d{2}\.\d{2}\.\d{2}( \(\d+\))?\.log$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static bool IsLogName(string? name) => name != null && NamePattern.IsMatch(name);

    /// <summary>Filene som skal slettes så bare de <paramref name="keep"/> nyeste (etter navn = tidspunkt) står igjen.
    /// Filer som ikke ser ut som loggfiler røres aldri.</summary>
    public static List<string> ToDelete(IEnumerable<string> names, int keep)
    {
        return names.Where(IsLogName).OrderByDescending(n => n, StringComparer.Ordinal).Skip(Math.Max(0, keep)).ToList();
    }

    /// <summary>Loggfilene i mappa, nyeste først (tom liste hvis mappa ikke finnes).</summary>
    public static List<string> List(string root)
    {
        try
        {
            var dir = DirectoryFor(root);
            if (!Directory.Exists(dir)) return new();
            return Directory.GetFiles(dir).Where(f => IsLogName(Path.GetFileName(f))).OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal).ToList();
        }
        catch { return new(); }
    }
}

/// <summary>Kø med fast størrelse: er den full, kastes den eldste, og en teller holder rede på hvor mange. Trygg å bruke fra flere tråder.</summary>
public sealed class BoundedQueue<T>
{
    private readonly object _gate = new();
    private readonly Queue<T> _items = new();
    private long _dropped;
    private long _droppedTotal;

    public int Capacity { get; }
    public BoundedQueue(int capacity) { Capacity = Math.Max(1, capacity); }

    public int Count { get { lock (_gate) return _items.Count; } }
    /// <summary>Antall kastet siden sist <see cref="TakeDropped"/>.</summary>
    public long PendingDropped { get { lock (_gate) return _dropped; } }
    /// <summary>Antall kastet totalt.</summary>
    public long DroppedTotal { get { lock (_gate) return _droppedTotal; } }

    /// <summary>Legger til. Returnerer false hvis den eldste måtte kastes for å gi plass.</summary>
    public bool Enqueue(T item)
    {
        lock (_gate)
        {
            bool dropped = false;
            while (_items.Count >= Capacity) { _items.Dequeue(); _dropped++; _droppedTotal++; dropped = true; }
            _items.Enqueue(item);
            return !dropped;
        }
    }

    /// <summary>Flytter inntil <paramref name="max"/> elementer (eldst først) til <paramref name="into"/>. Returnerer antallet.</summary>
    public int DrainTo(List<T> into, int max)
    {
        lock (_gate)
        {
            int n = 0;
            while (n < max && _items.Count > 0) { into.Add(_items.Dequeue()); n++; }
            return n;
        }
    }

    /// <summary>Henter og nullstiller telleren for kastede elementer.</summary>
    public long TakeDropped()
    {
        lock (_gate) { var d = _dropped; _dropped = 0; return d; }
    }
}

/// <summary>Anger i hvilken sammenheng en cue startes (satt av den som kaller, lest av motoren). Gjelder bare den tråden.</summary>
public readonly struct ViaScope : IDisposable
{
    private readonly StartSource _previous;
    internal ViaScope(StartSource previous) { _previous = previous; }
    public void Dispose() => ShowLog.SetAmbient(_previous);
}

/// <summary>
/// Loggen for én kjøring av programmet. Hendelser legges i en begrenset kø og skrives til en loggfil av en bakgrunnstråd;
/// de siste hendelsene holdes også i minnet til loggvinduet. <see cref="Record"/> kaster aldri og venter aldri på disk.
/// </summary>
public sealed class ShowLog : IDisposable
{
    // ---- Felles tilgang for resten av programmet ----
    public static ShowLog? Current { get; private set; }
    /// <summary>Gjør denne til programmets logg (null = ingen). Den forrige lukkes ikke.</summary>
    public static void Install(ShowLog? log) => Current = log;

    [ThreadStatic] private static StartSource _ambient;
    internal static void SetAmbient(StartSource s) => _ambient = s;
    /// <summary>Hvordan det som skjer nå ble utløst (satt med <see cref="Via"/>), eller Unknown.</summary>
    public static StartSource CurrentSource => _ambient;

    /// <summary>Merker alt som logges og startes på denne tråden til scopet avsluttes. Med <paramref name="keepExisting"/>
    /// beholdes en kilde som allerede er satt lenger ute (f.eks. OSC som kaller GO).</summary>
    public static ViaScope Via(StartSource source, bool keepExisting = false)
    {
        var previous = _ambient;
        if (!(keepExisting && previous != StartSource.Unknown)) _ambient = source;
        return new ViaScope(previous);
    }

    /// <summary>Logger en hendelse i programmets logg (ingenting hvis ingen er installert). Kaster aldri.</summary>
    public static void Add(ShowLogKind kind, string? number = null, string? name = null, string? cueType = null, StartSource how = StartSource.Unknown, string? text = null)
        => Current?.Record(kind, number, name, cueType, how, text);

    /// <summary>Logger en hendelse for en cue (nummer, navn og type hentes fra cuen). Kaster aldri.</summary>
    public static void AddCue(ShowLogKind kind, Cue? cue, StartSource how = StartSource.Unknown, string? text = null)
    {
        try
        {
            if (Current is not { } log || cue == null) return;
            log.Record(kind, cue.Number, cue.DisplayName, cue.TypeKey, how, text);
        }
        catch { /* logging skal aldri påvirke showet */ }
    }

    // ---- Instans ----
    private readonly string? _root;
    private readonly Func<DateTimeOffset> _clock;
    private readonly BoundedQueue<ShowLogEvent> _queue;
    private readonly int _memoryLimit;
    private readonly long _maxFileBytes;
    private readonly int _filesToKeep;
    private readonly object _memGate = new();
    private readonly Queue<(long Seq, ShowLogEvent Event)> _memory = new();
    private long _seq;
    private readonly AutoResetEvent _signal = new(false);
    private readonly Thread? _thread;
    private int _stopping, _busy, _faultReported, _disposed;
    private volatile bool _writeFile;
    private FileStream? _stream;
    private long _bytes;
    private bool _failed, _full;

    /// <summary>Tidspunktet kjøringen startet (tid siden start regnes herfra).</summary>
    public DateTimeOffset RunStart { get; }
    /// <summary>Loggfila som er i bruk (null til den første hendelsen er skrevet, eller hvis fil ikke brukes).</summary>
    public string? FilePath { get; private set; }
    /// <summary>Første feil ved skriving til fil (null = ingen feil). Meldes bare én gang.</summary>
    public string? Fault { get; private set; }
    /// <summary>Skriving til fil feilet (kalles én gang, fra bakgrunnstråden).</summary>
    public event Action<string>? FaultReported;

    /// <param name="root">Programmets datamappe (loggene havner i «Logs» under den). Null = ingen fil, bare minne.</param>
    /// <param name="writeFile">Skriv til fil. Kan slås av og på med <see cref="WriteFile"/>.</param>
    /// <param name="clock">Klokke (for testing).</param>
    public ShowLog(string? root, bool writeFile = true, Func<DateTimeOffset>? clock = null, int queueCapacity = 4096,
        int memoryLimit = 5000, long maxFileBytes = ShowLogFiles.MaxFileBytes, int filesToKeep = ShowLogFiles.FilesToKeep)
    {
        _root = root;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _queue = new BoundedQueue<ShowLogEvent>(queueCapacity);
        _memoryLimit = Math.Max(1, memoryLimit);
        _maxFileBytes = Math.Max(1024, maxFileBytes);
        _filesToKeep = Math.Max(1, filesToKeep);
        _writeFile = writeFile;
        RunStart = _clock();
        if (root != null)
        {
            _thread = new Thread(WriterLoop) { IsBackground = true, Name = "Showlogg", Priority = ThreadPriority.BelowNormal };
            _thread.Start();
        }
    }

    public bool WriteFile { get => _writeFile; set => _writeFile = value; }
    /// <summary>Antall hendelser som er kastet fordi køen var full.</summary>
    public long Dropped => _queue.DroppedTotal;
    /// <summary>Antall hendelser i køen som ikke er skrevet ennå.</summary>
    public int Pending => _queue.Count;

    /// <summary>Logger en hendelse. Kaster aldri og venter aldri på disk.</summary>
    public void Record(ShowLogKind kind, string? number = null, string? name = null, string? cueType = null, StartSource how = StartSource.Unknown, string? text = null)
    {
        try
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            if (how == StartSource.Unknown) how = _ambient;
            var now = _clock();
            var ev = new ShowLogEvent(now, Math.Max(0, (long)(now - RunStart).TotalMilliseconds), kind,
                ShowLogFormat.Truncate(number, ShowLogFormat.MaxNumber), ShowLogFormat.Truncate(name, ShowLogFormat.MaxName),
                ShowLogFormat.Truncate(cueType, ShowLogFormat.MaxNumber), ShowLogFormat.SourceToken(how), ShowLogFormat.Truncate(text, ShowLogFormat.MaxText));
            AddToMemory(ev);
            if (_writeFile && _thread != null)
            {
                _queue.Enqueue(ev);
                _signal.Set();
            }
        }
        catch { /* logging skal aldri påvirke showet */ }
    }

    private void AddToMemory(ShowLogEvent ev)
    {
        lock (_memGate)
        {
            _memory.Enqueue((++_seq, ev));
            while (_memory.Count > _memoryLimit) _memory.Dequeue();
        }
    }

    /// <summary>Antall hendelser som er logget i denne kjøringen.</summary>
    public long Total { get { lock (_memGate) return _seq; } }

    /// <summary>Hendelsene som er nyere enn <paramref name="afterSeq"/> (0 = alle som ligger i minnet), og nummeret på den siste.</summary>
    public List<ShowLogEvent> ReadSince(long afterSeq, out long lastSeq)
    {
        lock (_memGate)
        {
            lastSeq = _seq;
            var list = new List<ShowLogEvent>();
            foreach (var (s, e) in _memory) if (s > afterSeq) list.Add(e);
            return list;
        }
    }

    public List<ShowLogEvent> Snapshot() => ReadSince(0, out _);

    /// <summary>Venter (høyst <paramref name="timeoutMs"/>) til alt i køen er skrevet. Brukes ved avslutning og i tester.</summary>
    public bool Flush(int timeoutMs = 2000)
    {
        if (_thread == null) return true;
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds <= timeoutMs)
        {
            if (_queue.Count == 0 && _queue.PendingDropped == 0 && Volatile.Read(ref _busy) == 0) return true;
            try { _signal.Set(); } catch { return true; }
            Thread.Sleep(2);
        }
        return false;
    }

    // ---- Skrivetråden ----
    private void WriterLoop()
    {
        var batch = new List<ShowLogEvent>(256);
        while (true)
        {
            try { _signal.WaitOne(300); } catch { return; }
            bool stopping = Volatile.Read(ref _stopping) != 0;
            Interlocked.Exchange(ref _busy, 1);
            try
            {
                while (true)
                {
                    batch.Clear();
                    if (_queue.DrainTo(batch, 512) == 0) break;
                    WriteBatch(batch);
                }
                var lost = _queue.TakeDropped();
                if (lost > 0)
                {
                    var now = _clock();
                    var ev = new ShowLogEvent(now, Math.Max(0, (long)(now - RunStart).TotalMilliseconds), ShowLogKind.Dropped, "", "", "", "",
                        lost.ToString(CultureInfo.InvariantCulture) + " events were lost (log queue full)");
                    AddToMemory(ev);
                    batch.Clear();
                    batch.Add(ev);
                    WriteBatch(batch);
                }
            }
            catch (Exception ex) { ReportFault(ex); }
            finally { Interlocked.Exchange(ref _busy, 0); }
            if (stopping) break;
        }
        CloseFile();
    }

    private void WriteBatch(List<ShowLogEvent> batch)
    {
        if (!_writeFile || _root == null || _failed || _full) return;
        try
        {
            if (_stream == null) OpenFile();
            var sb = new StringBuilder();
            foreach (var ev in batch)
            {
                var line = ShowLogFormat.FormatLine(ev) + "\n";
                var size = Encoding.UTF8.GetByteCount(line);
                if (_bytes + size + 200 > _maxFileBytes)
                {
                    sb.Append("# size limit reached; later events are not written to this file\n");
                    _full = true;
                    break;
                }
                sb.Append(line);
                _bytes += size;
            }
            var data = new UTF8Encoding(false).GetBytes(sb.ToString());
            _stream!.Write(data, 0, data.Length);
            _stream.Flush();
        }
        catch (Exception ex)
        {
            _failed = true; // skriving gis opp for resten av kjøringen; feilen meldes én gang
            ReportFault(ex);
            CloseFile();
        }
    }

    private void OpenFile()
    {
        var dir = ShowLogFiles.DirectoryFor(_root!);
        Directory.CreateDirectory(dir);
        // Behold de nyeste, så det er plass til den nye fila.
        foreach (var old in ShowLogFiles.ToDelete(Directory.GetFiles(dir).Select(f => Path.GetFileName(f)), _filesToKeep - 1))
        {
            try { File.Delete(Path.Combine(dir, old)); } catch { /* en fil som er låst får stå */ }
        }
        for (int attempt = 1; ; attempt++)
        {
            var path = Path.Combine(dir, ShowLogFiles.FileName(RunStart, attempt));
            try
            {
                _stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                FilePath = path;
                break;
            }
            catch (IOException) when (attempt < 50 && File.Exists(path)) { /* navnet er tatt: prøv neste løpenummer */ }
        }
        var header = ShowLogFormat.FileHeader + " | started " + ShowLogFormat.Iso(RunStart) + "\n";
        var bytes = new UTF8Encoding(false).GetBytes(header);
        _stream!.Write(bytes, 0, bytes.Length);
        _bytes = bytes.Length;
    }

    private void CloseFile()
    {
        try { _stream?.Dispose(); } catch { }
        _stream = null;
    }

    private void ReportFault(Exception ex)
    {
        if (Interlocked.Exchange(ref _faultReported, 1) != 0) return;
        Fault = ex.Message;
        try { FaultReported?.Invoke(ex.Message); } catch { }
    }

    /// <summary>Skriver ut det som står i køen og lukker fila. Venter høyst et par sekunder.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try
        {
            Interlocked.Exchange(ref _stopping, 1);
            try { _signal.Set(); } catch { }
            _thread?.Join(1500);
        }
        catch { /* avslutter uansett */ }
    }
}
