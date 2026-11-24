using System.Globalization;

namespace ShowCue.Models;

/// <summary>Leser en liste med cuenumre («2, 5, 8-10») slik gruppe-cuer skriver den.</summary>
public static class CueTargets
{
    /// <summary>
    /// Finner cuene en liste peker på. Hvert ledd er et cuenummer eller et område av tall («8-10» = alle cuer med et tall fra 8 til 10).
    /// Leddene skilles med komma eller semikolon. <paramref name="self"/> tas aldri med, og ingen cue kommer med to ganger.
    /// Ledd som ikke finnes legges i <paramref name="missing"/>.
    /// </summary>
    public static List<Cue> Resolve(IEnumerable<Cue> cues, string? spec, Cue? self, List<string>? missing = null)
    {
        var all = cues.ToList();
        var result = new List<Cue>();
        void Add(Cue c) { if (!ReferenceEquals(c, self) && !result.Contains(c)) result.Add(c); }

        foreach (var raw in (spec ?? "").Split(',', ';'))
        {
            var token = raw.Trim();
            if (token.Length == 0) continue;
            var exact = all.FirstOrDefault(c => string.Equals(c.Number, token, StringComparison.OrdinalIgnoreCase));
            if (exact != null) { Add(exact); continue; }
            if (TryRange(token, out var lo, out var hi))
            {
                var inRange = all.Where(c => TryNumber(c.Number, out var n) && n >= lo && n <= hi).ToList();
                if (inRange.Count > 0) { inRange.ForEach(Add); continue; }
            }
            missing?.Add(token);
        }
        return result;
    }

    /// <summary>Bytter cuenumre i listen etter en tabell (gammelt nummer → nytt), også i endene av et område.</summary>
    public static string Renumber(string? spec, IReadOnlyDictionary<string, string> map)
    {
        string Map(string s) => map.TryGetValue(s, out var n) ? n : s;
        var parts = new List<string>();
        foreach (var raw in (spec ?? "").Split(',', ';'))
        {
            var p = raw.Trim();
            if (p.Length == 0) continue;
            if (map.TryGetValue(p, out var whole)) { parts.Add(whole); continue; }
            int dash = p.IndexOf('-', 1);
            if (dash > 0)
            {
                var left = p[..dash].Trim();
                var right = p[(dash + 1)..].Trim();
                if (map.ContainsKey(left) || map.ContainsKey(right)) { parts.Add(Map(left) + "-" + Map(right)); continue; }
            }
            parts.Add(p);
        }
        return string.Join(", ", parts);
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
