using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ShowCue.Models;

/// <summary>
/// Autolagring og sikkerhetskopier, så et krasj eller strømbrudd ikke koster arbeid.
///  - Autolagring: ulagrede endringer skrives jevnlig til en egen fil og tilbys gjenopprettet ved neste åpning.
///  - Sikkerhetskopier: før et show lagres over, kopieres forrige versjon (de 20 siste beholdes).
/// Alt ligger i programmets egen mappe, ikke ved siden av showfila.
/// </summary>
public static class ShowSafety
{
    public const int BackupsToKeep = 20;

    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShowCue");

    /// <summary>Autolagringsfila for et show (null = ulagret show).</summary>
    public static string AutosavePath(string? showPath, string? root = null)
    {
        var dir = Path.Combine(root ?? DefaultRoot, "Autolagring");
        if (string.IsNullOrEmpty(showPath)) return Path.Combine(dir, "Uten navn.showcue");
        var full = Path.GetFullPath(showPath);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full.ToLowerInvariant())))[..8];
        var name = Path.GetFileNameWithoutExtension(full);
        if (name.Length > 80) name = name[..80]; // svært lange filnavn skal ikke stoppe autolagringen
        return Path.Combine(dir, $"{name} {hash}.showcue");
    }

    public static void WriteAutosave(string? showPath, string json, string? root = null)
    {
        var path = AutosavePath(showPath, root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }

    public static void DeleteAutosave(string? showPath, string? root = null)
    {
        try { File.Delete(AutosavePath(showPath, root)); } catch { }
    }

    /// <summary>Autolagring som er nyere enn selve showfila (= ulagrede endringer fra forrige gang), ellers null.</summary>
    public static string? PendingAutosave(string? showPath, string? root = null)
    {
        var a = AutosavePath(showPath, root);
        if (!File.Exists(a)) return null;
        if (string.IsNullOrEmpty(showPath) || !File.Exists(showPath)) return a;
        return File.GetLastWriteTimeUtc(a) > File.GetLastWriteTimeUtc(showPath) ? a : null;
    }

    public static string BackupDir(string? root = null) => Path.Combine(root ?? DefaultRoot, "Sikkerhetskopier");

    /// <summary>Kopier showfila slik den er nå (før den lagres over). Beholder de <see cref="BackupsToKeep"/> siste per show.</summary>
    public static string? Backup(string showPath, string? root = null, DateTime? now = null)
    {
        if (!File.Exists(showPath)) return null;
        var dir = BackupDir(root);
        Directory.CreateDirectory(dir);
        var name = Path.GetFileNameWithoutExtension(showPath);
        if (name.Length > 80) name = name[..80];
        var target = Path.Combine(dir, $"{name} {(now ?? DateTime.Now):yyyy-MM-dd HH.mm.ss}.showcue");
        File.Copy(showPath, target, overwrite: true);

        var old = Directory.GetFiles(dir, name + " ????-??-?? ??.??.??.showcue").OrderByDescending(f => f, StringComparer.Ordinal).Skip(BackupsToKeep);
        foreach (var f in old) { try { File.Delete(f); } catch { } }
        return target;
    }
}
