using System.IO;
using NAudio.Wave;
using ShowCue.Models;

namespace ShowCue.Engine;

/// <summary>
/// Forbereder cuene GO snart vil starte, i bakgrunnen: åpner lydfila (eller lydsporet i en video), leser de første sekundene
/// så de ligger i filbufferen, og dekoder bilder. Alt skjer utenfor lydtråden og utenfor UI-tråden.
/// En forberedt fil er aldri påkrevd: feiler forberedelsen, eller er fila endret, åpner cuen fila selv som før.
/// Høyst <see cref="PreloadPlanner.MaxItems"/> filer holdes samtidig, og alt slippes når markøren flytter seg, ved stopp, nytt show og avslutning.
/// </summary>
public sealed class Preloader : IDisposable
{
    /// <summary>Hvor mye lyd som leses på forhånd (sekunder).</summary>
    private const double WarmSeconds = 1.5;

    private readonly object _lock = new();
    private readonly PreloadBook _book = new();
    private readonly Dictionary<string, Entry> _entries = new();
    private bool _disposed;

    /// <summary>Slått på i innstillingene («Forhåndslast neste cue»). Av: ingenting forberedes og alt som er holdt slippes.</summary>
    public bool Enabled { get; set; }

    private sealed class Entry
    {
        public required PreloadItem Item;
        public bool Cancelled;
        public bool Ready;
        public bool Failed;
        public long Length;
        public DateTime Written;
        public WaveStream? Reader;
    }

    /// <summary>Kalles fra UI-tråden når markøren eller cuelista er endret.</summary>
    public void Update(IList<Cue> cues, int playheadIndex)
    {
        if (!Enabled) { Clear(); return; }
        try
        {
            var plan = PreloadPlanner.Plan(cues, playheadIndex);
            var starts = new List<Entry>();
            var dispose = new List<WaveStream>();
            lock (_lock)
            {
                if (_disposed) return;
                var diff = _book.Sync(plan.Select(p => p.Key));
                foreach (var key in diff.ToEvict) Drop(key, dispose);
                foreach (var key in diff.ToStart)
                {
                    var e = new Entry { Item = plan.First(p => p.Key == key) };
                    _entries[key] = e;
                    starts.Add(e);
                }
            }
            foreach (var r in dispose) SafeDispose(r);
            foreach (var e in starts) Task.Run(() => Prepare(e));
        }
        catch { /* forberedelse er aldri påkrevd */ }
    }

    /// <summary>Lydfila til en lyd-cue hvis den er forberedt (og uendret). Eierskapet går til den som tar den. Ellers null.</summary>
    public WaveStream? TakeAudio(string path) => Take(PreloadKind.Audio, path);

    /// <summary>Lydsporet til en video hvis det er forberedt (og uendret). Ellers null.</summary>
    public WaveStream? TakeVideoAudio(string path) => Take(PreloadKind.Video, path);

    private WaveStream? Take(PreloadKind kind, string path)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var key = new PreloadItem(kind, path.Trim()).Key;
            Entry? e;
            lock (_lock)
            {
                if (!_entries.TryGetValue(key, out e) || !e.Ready || e.Failed || e.Reader == null) return null;
            }
            // Er fila byttet ut siden den ble forberedt, brukes den ikke.
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != e.Length || info.LastWriteTimeUtc != e.Written)
            {
                var stale = new List<WaveStream>();
                lock (_lock) { _book.Remove(key); Drop(key, stale); }
                foreach (var r in stale) SafeDispose(r);
                return null;
            }
            lock (_lock)
            {
                if (!_entries.TryGetValue(key, out var again) || again != e || e.Reader == null) return null;
                var reader = e.Reader;
                e.Reader = null;
                _entries.Remove(key);
                _book.Remove(key);
                return reader;
            }
        }
        catch { return null; }
    }

    /// <summary>Slipp alt som er forberedt (filene lukkes).</summary>
    public void Clear()
    {
        var dispose = new List<WaveStream>();
        lock (_lock)
        {
            foreach (var key in _book.Clear()) Drop(key, dispose);
            foreach (var key in _entries.Keys.ToList()) Drop(key, dispose);
        }
        foreach (var r in dispose) SafeDispose(r);
    }

    public void Dispose()
    {
        lock (_lock) _disposed = true;
        Clear();
    }

    /// <summary>Fjern en oppføring. Er den ikke ferdig, avbrytes den (bakgrunnsjobben lukker fila selv). Kalles med låsen tatt.</summary>
    private void Drop(string key, List<WaveStream> dispose)
    {
        if (!_entries.Remove(key, out var e)) return;
        e.Cancelled = true;
        if (e.Reader != null) { dispose.Add(e.Reader); e.Reader = null; }
    }

    private void Prepare(Entry e)
    {
        WaveStream? reader = null;
        try
        {
            var path = e.Item.Path;
            if (!File.Exists(path)) throw new FileNotFoundException(path);
            var info = new FileInfo(path);
            if (e.Item.Kind == PreloadKind.Image)
            {
                MediaCache.Warm(path);
            }
            else
            {
                reader = e.Item.Kind == PreloadKind.Audio ? new AudioFileReader(path) : VideoAudio.Open(path);
                if (reader != null) Warm(reader, e.Item.StartTime);
            }
            lock (_lock)
            {
                if (e.Cancelled || _disposed) { /* reader lukkes under */ }
                else
                {
                    e.Length = info.Length;
                    e.Written = info.LastWriteTimeUtc;
                    e.Reader = reader;
                    e.Ready = true;
                    reader = null;
                }
            }
        }
        catch
        {
            lock (_lock) e.Failed = true; // stille: cuen åpner fila selv
        }
        finally
        {
            if (reader != null) SafeDispose(reader);
        }
    }

    /// <summary>Leser de første sekundene fra startpunktet, så de ligger i filbufferen, og setter lesehodet tilbake til starten.</summary>
    private static void Warm(WaveStream reader, double startSeconds)
    {
        var f = reader.WaveFormat;
        if (startSeconds > 0 && startSeconds < reader.TotalTime.TotalSeconds) reader.CurrentTime = TimeSpan.FromSeconds(startSeconds);
        int block = Math.Max(1, f.BlockAlign);
        int bytes = (int)Math.Min(8 * 1024 * 1024, (long)(f.AverageBytesPerSecond * WarmSeconds) / block * block);
        if (bytes > 0)
        {
            var buf = new byte[bytes];
            int total = 0, n;
            while (total < bytes && (n = reader.Read(buf, total, bytes - total)) > 0) total += n;
        }
        reader.Position = 0;
    }

    private static void SafeDispose(IDisposable d)
    {
        try { d.Dispose(); } catch { /* lukkes uansett */ }
    }
}
