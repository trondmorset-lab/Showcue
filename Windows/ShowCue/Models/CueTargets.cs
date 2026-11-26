using System.Globalization;

namespace ShowCue.Models;

/// <summary>Leser en liste med cuenumre («2, 5, 8-10») slik gruppe-cuer skriver den.</summary>
public static class CueTargets
{
    /// <summary>
    /// Finner cuene en liste peker på. Hvert ledd er et cuenummer eller et område av tall («8-10» = alle cuer med et tall fra 8 til 10).
    /// Leddene skilles med komma eller semikolon. <paramref name="self"/> tas aldri med, og ingen cue kommer med to ganger.
    /// Ledd som ikke finnes legges i <paramref name="missing"/>.
    /// Scener: «Scene:nummer» søker bare i den scenen. Et vanlig ledd søkes først i hjemmescenen
    /// (<paramref name="homeScene"/>, ellers scenen til <paramref name="self"/>) og deretter i alle scener.
    /// </summary>
    public static List<Cue> Resolve(IEnumerable<Cue> cues, string? spec, Cue? self, List<string>? missing = null, string? homeScene = null)
    {
        var all = cues.ToList();
        var home = homeScene ?? self?.Scene;
        var result = new List<Cue>();
        void Add(Cue c) { if (!ReferenceEquals(c, self) && !result.Contains(c)) result.Add(c); }

        foreach (var raw in (spec ?? "").Split(',', ';'))
        {
            var token = raw.Trim();
            if (token.Length == 0) continue;
            List<Cue>? found;
            if (SceneLogic.TrySplit(token, out var scene, out var rest) && rest.Length > 0 && all.Any(c => SceneLogic.InScene(c, scene)))
                found = Find(all.Where(c => SceneLogic.InScene(c, scene)).ToList(), rest);
            else
            {
                found = home != null ? Find(all.Where(c => SceneLogic.InScene(c, home)).ToList(), token) : null;
                found ??= Find(all, token);
            }
            if (found == null) { missing?.Add(token); continue; }
            found.ForEach(Add);
        }
        return result;
    }

    /// <summary>Treff i en liste cuer: nøyaktig nummer, ellers cuene i et tallområde. Null hvis ingen.</summary>
    private static List<Cue>? Find(List<Cue> pool, string token)
    {
        var exact = pool.FirstOrDefault(c => string.Equals(c.Number, token, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return new List<Cue> { exact };
        if (TryRange(token, out var lo, out var hi))
        {
            var inRange = pool.Where(c => TryNumber(c.Number, out var n) && n >= lo && n <= hi).ToList();
            if (inRange.Count > 0) return inRange;
        }
        return null;
    }

    /// <summary>
    /// Bytter cuenumre i listen etter en tabell (gammelt nummer → nytt), også i endene av et område.
    /// Med <paramref name="qualifier"/> endres bare ledd som står som «Scene:nummer» for den scenen; uten endres bare ledd uten scene.
    /// </summary>
    public static string Renumber(string? spec, IReadOnlyDictionary<string, string> map, string? qualifier = null)
    {
        var parts = new List<string>();
        var prefix = qualifier == null ? null : qualifier + SceneLogic.Qualifier;
        foreach (var raw in (spec ?? "").Split(',', ';'))
        {
            var p = raw.Trim();
            if (p.Length == 0) continue;
            if (prefix != null)
            {
                parts.Add(p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? p[..prefix.Length] + RenumberToken(p[prefix.Length..].Trim(), map) : p);
                continue;
            }
            parts.Add(p.IndexOf(SceneLogic.Qualifier) > 0 && !map.ContainsKey(p) ? p : RenumberToken(p, map));
        }
        return string.Join(", ", parts);
    }

    private static string RenumberToken(string p, IReadOnlyDictionary<string, string> map)
    {
        string Map(string s) => map.TryGetValue(s, out var n) ? n : s;
        if (map.TryGetValue(p, out var whole)) return whole;
        int dash = p.Length > 1 ? p.IndexOf('-', 1) : -1;
        if (dash > 0)
        {
            var left = p[..dash].Trim();
            var right = p[(dash + 1)..].Trim();
            if (map.ContainsKey(left) || map.ContainsKey(right)) return Map(left) + "-" + Map(right);
        }
        return p;
    }

    /// <summary>Bytter scenenavnet i «Scene:nummer»-ledd. Listen røres ikke hvis ingen ledd peker på scenen.</summary>
    public static string RenameQualifier(string? spec, string oldScene, string newScene)
    {
        var text = spec ?? "";
        var prefix = oldScene + SceneLogic.Qualifier;
        var parts = text.Split(',', ';').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        bool changed = false;
        for (int i = 0; i < parts.Count; i++)
            if (parts[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                parts[i] = newScene + SceneLogic.Qualifier + parts[i][prefix.Length..];
                changed = true;
            }
        return changed ? string.Join(", ", parts) : text;
    }

    private static bool TryNumber(string s, out double n) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out n) && double.IsFinite(n);

    private static bool TryRange(string token, out double lo, out double hi)
    {
        lo = hi = 0;
        int dash = token.IndexOf('-', 1);
        if (dash <= 0) return false;
        if (!TryNumber(token[..dash].Trim(), out lo) || !TryNumber(token[(dash + 1)..].Trim(), out hi)) return false;
        if (lo > hi) (lo, hi) = (hi, lo);
        return true;
    }
}
