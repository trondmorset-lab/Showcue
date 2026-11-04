using System.IO;

namespace ShowCue.Models;

/// <summary>Mediefiler i et show: finn manglende filer, let dem opp i en mappe («relink»),
/// og samle showet og alle mediefilene i én mappe («collect»), f.eks. før showet flyttes til en annen maskin.</summary>
public static class MediaTools
{
    public static readonly string[] AudioExtensions = { ".wav", ".mp3", ".aif", ".aiff", ".flac", ".m4a", ".aac", ".wma" };
    public static readonly string[] VideoExtensions = { ".mp4", ".mov", ".m4v", ".wmv", ".avi", ".mkv" };
    public static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };

    public static bool HasExtension(string path, string[] extensions) =>
        extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Alle mediefiler showet bruker (fil-cuer, spillelister og lysbildeserier), uten duplikater.</summary>
    public static List<string> MediaPaths(IEnumerable<Cue> cues)
    {
        var all = new List<string>();
        foreach (var c in cues)
        {
            if (c is IFileCue f && !string.IsNullOrEmpty(f.FilePath)) all.Add(f.FilePath);
            if (c is IFileListCue p) all.AddRange(p.Files.Where(s => !string.IsNullOrEmpty(s)));
        }
        return all.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static List<string> Missing(IEnumerable<Cue> cues) => MediaPaths(cues).Where(p => !File.Exists(p)).ToList();

    /// <summary>Filnavnet, også for stier fra den andre plattformen (C:\… på Mac, /… på Windows).</summary>
    public static string FileName(string path)
    {
        var i = path.LastIndexOfAny(new[] { '/', '\\' });
        return i >= 0 ? path[(i + 1)..] : path;
    }

    /// <summary>Let etter manglende filer (på filnavn) i <paramref name="folder"/> og alle undermapper.
    /// Returnerer antall cuer/spor som ble funnet.</summary>
    public static int Relink(IEnumerable<Cue> cues, string folder)
    {
        if (!Directory.Exists(folder)) return 0;
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 12 };
            foreach (var f in Directory.EnumerateFiles(folder, "*", opts)) index.TryAdd(Path.GetFileName(f), f);
        }
        catch (IOException) { }

        string? Find(string path) =>
            !string.IsNullOrEmpty(path) && !File.Exists(path) && index.TryGetValue(FileName(path), out var found) ? found : null;

        var count = 0;
        foreach (var c in cues)
        {
            if (c is IFileCue fc && Find(fc.FilePath) is string p) { fc.FilePath = p; count++; }
            if (c is IFileListCue pl)
            {
                var files = pl.Files.ToList();
                var changed = false;
                for (var i = 0; i < files.Count; i++)
                    if (Find(files[i]) is string q) { files[i] = q; changed = true; count++; }
                if (changed) pl.Files = files;
            }
        }
        return count;
    }

    public sealed record CollectResult(string ShowPath, int Copied, List<string> Missing);

    /// <summary>Kopierer showet til <paramref name="folder"/>/&lt;navn&gt;.showcue og alle mediefilene til
    /// <paramref name="folder"/>/Media. Kopien peker på de nye filene; showet som er åpent endres ikke.
    /// Filer med samme navn fra ulike mapper får « (2)», « (3)» … i navnet.</summary>
    public static CollectResult Collect(ShowDocument doc, string folder, string showName)
    {
        var media = Path.Combine(folder, "Media");
        Directory.CreateDirectory(media);
        var copy = System.Text.Json.JsonSerializer.Deserialize<ShowDocument>(doc.ToJson(), ShowDocument.JsonOptions)!;

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        var copied = 0;

        string Place(string src)
        {
            if (map.TryGetValue(src, out var done)) return done;
            if (!File.Exists(src)) { missing.Add(src); map[src] = src; return src; }
            var name = Path.GetFileNameWithoutExtension(src);
            var ext = Path.GetExtension(src);
            var target = Path.Combine(media, name + ext);
            for (var n = 2; used.Contains(target); n++) target = Path.Combine(media, $"{name} ({n}){ext}");
            used.Add(target);
            if (!string.Equals(Path.GetFullPath(src), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                File.Copy(src, target, overwrite: true);
            copied++;
            return map[src] = target;
        }

        foreach (var c in copy.Cues)
        {
            if (c is IFileCue fc && !string.IsNullOrEmpty(fc.FilePath)) fc.FilePath = Place(fc.FilePath);
            if (c is IFileListCue pl) pl.Files = pl.Files.Select(f => string.IsNullOrEmpty(f) ? f : Place(f)).ToList();
        }

        var showPath = Path.Combine(folder, showName + ".showcue");
        copy.Title = showName;
        copy.Save(showPath);
        return new CollectResult(showPath, copied, missing);
    }
}
