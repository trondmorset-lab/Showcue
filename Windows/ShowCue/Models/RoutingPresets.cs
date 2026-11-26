using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShowCue.Models;

/// <summary>Ett stereopar i et routingforhåndsvalg: utgangen til venstre kanal (høyre kanal går til neste utgang) og et ekstra nivå i dB.</summary>
public sealed class RoutingPair
{
    private int _bus = 1;
    private double _gainDb;
    /// <summary>Utgang for venstre kanal, fra 1. Høyre kanal går til utgangen etter.</summary>
    public int Bus { get => _bus; set => _bus = RoutingPresets.ClampLeftBus(value); }
    /// <summary>Nivå i dB som legges til alle rutene i paret (NaN blir 0).</summary>
    public double GainDb { get => _gainDb; set => _gainDb = RoutingPresets.ClampGain(value); }

    public RoutingPair() { }
    public RoutingPair(int bus, double gainDb = 0) { Bus = bus; GainDb = gainDb; }
}

/// <summary>Et navngitt forhåndsvalg for routing («Hovedhøyttalere», «Monitor», «Hoved + monitor»). Lagres i showet og
/// gjelder uansett hvor mange kanaler lydfila har.</summary>
public sealed class RoutingPreset
{
    private string _name = "";
    private List<RoutingPair> _pairs = new();
    public string Name { get => _name; set => _name = (value ?? "").Trim(); }
    /// <summary>Stereoparene lyden sendes til. En ødelagt fil kan ha «null» i lista: slike oppføringer fjernes.</summary>
    public List<RoutingPair> Pairs { get => _pairs; set => _pairs = RoutingPresets.CleanPairs(value); }

    public RoutingPreset() { }
    public RoutingPreset(string name, IEnumerable<RoutingPair> pairs) { Name = name; Pairs = pairs?.ToList() ?? new(); }
}

public enum RoutingPresetError { None, EmptyName, NameTooLong, DuplicateName, NoPairs, TooManyPresets, NotFound }

/// <summary>Resultat av å sette routing på flere cuer: endret, allerede likt, og hoppet over (ingen lyd, eller ingen gyldig utgang).</summary>
public readonly record struct RoutingApplyResult(int Changed, int Unchanged, int Skipped);

/// <summary>Ren logikk for routingforhåndsvalg: lage, endre og slette, gjøre dem om til ruter for en cue, sette dem på flere
/// cuer, kopiere og lime inn, og beskrive routing i tekst. Rører ingen lydtråd, fil eller UI.</summary>
public static class RoutingPresets
{
    public const int MaxBus = 64;
    public const int MaxPairs = 8;
    public const int MaxPresets = 32;
    public const int MaxNameLength = 40;
    public const int MaxChannels = 64;

    /// <summary>Nivå i dB mellom −60 og +12; NaN blir 0, ±uendelig blir grensene.</summary>
    public static double ClampGain(double v) => double.IsNaN(v) ? 0 : Math.Clamp(v, -60, 12);
    /// <summary>Utgang for venstre kanal i et par: 1 til MaxBus − 1 (høyre kanal trenger plass).</summary>
    public static int ClampLeftBus(int v) => Math.Clamp(v, 1, MaxBus - 1);

    /// <summary>Par uten tomme oppføringer og uten to par på samme utgang; maks MaxPairs. Verdiene er begrenset.</summary>
    public static List<RoutingPair> CleanPairs(IEnumerable<RoutingPair?>? pairs)
    {
        var result = new List<RoutingPair>();
        foreach (var p in pairs ?? Enumerable.Empty<RoutingPair?>())
        {
            if (p == null || result.Count >= MaxPairs || result.Any(x => x.Bus == p.Bus)) continue;
            result.Add(new RoutingPair(p.Bus, p.GainDb));
        }
        return result;
    }

    /// <summary>Forhåndsvalg fra en lest fil: tomme eller ugyldige oppføringer, tomme eller like navn og alt over grensene fjernes.</summary>
    public static List<RoutingPreset> CleanList(IEnumerable<RoutingPreset?>? presets)
    {
        var result = new List<RoutingPreset>();
        foreach (var p in presets ?? Enumerable.Empty<RoutingPreset?>())
        {
            if (p == null || result.Count >= MaxPresets) continue;
            var name = (p.Name ?? "").Trim();
            if (name.Length == 0) continue;
            if (name.Length > MaxNameLength) name = name[..MaxNameLength].Trim();
            var pairs = CleanPairs(p.Pairs);
            if (pairs.Count == 0 || result.Any(x => SameName(x.Name, name))) continue;
            result.Add(new RoutingPreset(name, pairs));
        }
        return result;
    }

    private static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.CurrentCultureIgnoreCase);

    private static RoutingPresetError CheckName(List<RoutingPreset> list, string? name, int ignoreIndex, out string clean)
    {
        clean = (name ?? "").Trim();
        if (clean.Length == 0) return RoutingPresetError.EmptyName;
        if (clean.Length > MaxNameLength) return RoutingPresetError.NameTooLong;
        for (int i = 0; i < list.Count; i++)
            if (i != ignoreIndex && SameName(list[i].Name, clean)) return RoutingPresetError.DuplicateName;
        return RoutingPresetError.None;
    }

    /// <summary>Legger til et nytt forhåndsvalg sist i lista. Ved feil endres ingenting.</summary>
    public static RoutingPresetError Add(List<RoutingPreset> list, string? name, IEnumerable<RoutingPair?>? pairs)
    {
        if (list.Count >= MaxPresets) return RoutingPresetError.TooManyPresets;
        var err = CheckName(list, name, -1, out var clean);
        if (err != RoutingPresetError.None) return err;
        var p = CleanPairs(pairs);
        if (p.Count == 0) return RoutingPresetError.NoPairs;
        list.Add(new RoutingPreset(clean, p));
        return RoutingPresetError.None;
    }

    /// <summary>Endrer navn og par på forhåndsvalget på plassen <paramref name="index"/>. Samme navn som det har fra før er lov.</summary>
    public static RoutingPresetError Update(List<RoutingPreset> list, int index, string? name, IEnumerable<RoutingPair?>? pairs)
    {
        if (index < 0 || index >= list.Count) return RoutingPresetError.NotFound;
        var err = CheckName(list, name, index, out var clean);
        if (err != RoutingPresetError.None) return err;
        var p = CleanPairs(pairs);
        if (p.Count == 0) return RoutingPresetError.NoPairs;
        list[index] = new RoutingPreset(clean, p);
        return RoutingPresetError.None;
    }

    /// <summary>Gir nytt navn og beholder parene.</summary>
    public static RoutingPresetError Rename(List<RoutingPreset> list, int index, string? name)
        => index < 0 || index >= list.Count ? RoutingPresetError.NotFound : Update(list, index, name, list[index].Pairs);

    public static bool Remove(List<RoutingPreset> list, int index)
    {
        if (index < 0 || index >= list.Count) return false;
        list.RemoveAt(index);
        return true;
    }

    /// <summary>Leser par som tekst: «1-2, 3-4:-6». Hvert par er to nabo-utganger (n–n+1), og et nivå i dB kan stå etter kolon (punktum som desimaltegn).
    /// Skilletegn mellom parene: komma, semikolon, «+» eller mellomrom. Tom tekst, feil format, tall utenfor 1–MaxBus, nivå som ikke er et tall
    /// (NaN, uendelig) eller flere par enn MaxPairs gir false. Par på samme utgang telles én gang (det første gjelder). Nivå begrenses til −60 … +12 dB.</summary>
    public static bool TryParsePairs(string? text, out List<RoutingPair> pairs)
    {
        pairs = new List<RoutingPair>();
        var tokens = (text ?? "").Replace('–', '-').Replace('−', '-')
            .Split(new[] { ',', ';', '+', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return false;
        foreach (var t in tokens)
        {
            var gainSplit = t.Split(':');
            var parts = gainSplit[0].Split('-');
            double gain = 0;
            if (gainSplit.Length > 2
                || (gainSplit.Length == 2 && (!double.TryParse(gainSplit[1], NumberStyles.Float, CultureInfo.InvariantCulture, out gain) || !double.IsFinite(gain)))
                || parts.Length != 2
                || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var a)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var b)
                || b != a + 1 || a < 1 || b > MaxBus) { pairs = new List<RoutingPair>(); return false; }
            if (!pairs.Any(p => p.Bus == a)) pairs.Add(new RoutingPair(a, gain));
        }
        if (pairs.Count > MaxPairs) { pairs = new List<RoutingPair>(); return false; }
        return true;
    }

    /// <summary>Parene som tekst: «1-2, 3-4:-6» (nivået står bare med når det ikke er 0).</summary>
    public static string FormatPairs(RoutingPreset preset) => string.Join(", ", preset.Pairs.Select(p =>
        $"{p.Bus}-{p.Bus + 1}" + (Math.Abs(p.GainDb) < 0.0001 ? "" : ":" + p.GainDb.ToString("0.#", CultureInfo.InvariantCulture))));

    /// <summary>Utgangene som tekst med områder: «1-2, 5».</summary>
    public static string Ranges(IEnumerable<int> buses)
    {
        var list = buses.Distinct().OrderBy(b => b).ToList();
        var parts = new List<string>();
        for (int i = 0; i < list.Count;)
        {
            int j = i;
            while (j + 1 < list.Count && list[j + 1] == list[j] + 1) j++;
            parts.Add(j > i ? $"{list[i]}-{list[j]}" : list[i].ToString(CultureInfo.InvariantCulture));
            i = j + 1;
        }
        return string.Join(", ", parts);
    }

    /// <summary>Rutene et forhåndsvalg gir for en fil med <paramref name="fileChannels"/> kanaler. Mono går til begge utgangene i hvert par,
    /// stereo går venstre/høyre, og flere kanaler summeres annenhver til venstre/høyre med −3 dB. Parets nivå legges til.
    /// Par som ikke får plass i <paramref name="busCount"/> utganger hoppes over (0 = ingen grense). Ingen kanaler gir tom liste.</summary>
    public static List<AudioRoute> Routes(RoutingPreset preset, int fileChannels, int busCount = 0)
    {
        int ch = Math.Clamp(fileChannels, 0, MaxChannels);
        var best = new Dictionary<(int Source, int Bus), double>();
        void Put(int source, int bus, double gain)
        {
            gain = ClampGain(gain);
            if (!best.TryGetValue((source, bus), out var old) || gain > old) best[(source, bus)] = gain;
        }
        if (ch > 0)
            foreach (var p in CleanPairs(preset?.Pairs))
            {
                if (busCount > 0 && p.Bus + 1 > busCount) continue;
                if (ch == 1) { Put(1, p.Bus, p.GainDb); Put(1, p.Bus + 1, p.GainDb); continue; }
                double baseGain = ch > 2 ? -3 : 0;
                for (int c = 1; c <= ch; c++) Put(c, p.Bus + (c - 1) % 2, baseGain + p.GainDb);
            }
        return best.OrderBy(kv => kv.Key.Source).ThenBy(kv => kv.Key.Bus).Select(kv => new AudioRoute(kv.Key.Source, kv.Key.Bus, kv.Value)).ToList();
    }

    /// <summary>Routing med kanaler og utganger innenfor grensene, uten like (kanal, utgang) to ganger og med nivå begrenset.
    /// Kanaler over <paramref name="fileChannels"/> og utganger over <paramref name="busCount"/> (0 = ingen grense) fjernes.</summary>
    public static List<AudioRoute> CleanRoutes(IEnumerable<AudioRoute?>? routes, int fileChannels, int busCount = 0)
    {
        int ch = Math.Clamp(fileChannels, 0, MaxChannels);
        int maxBus = busCount > 0 ? Math.Min(busCount, MaxBus) : MaxBus;
        var seen = new HashSet<(int, int)>();
        var result = new List<AudioRoute>();
        foreach (var r in routes ?? Enumerable.Empty<AudioRoute?>())
            if (r != null && r.Source >= 1 && r.Source <= ch && r.Bus >= 1 && r.Bus <= maxBus && seen.Add((r.Source, r.Bus)))
                result.Add(new AudioRoute(r.Source, r.Bus, r.GainDb));
        return result.OrderBy(r => r.Source).ThenBy(r => r.Bus).ToList();
    }

    /// <summary>Er to routinger like? null = standard og er bare lik null. Nivå regnes som like innenfor 0,001 dB.</summary>
    public static bool SameRoutes(IReadOnlyList<AudioRoute>? a, IReadOnlyList<AudioRoute>? b)
    {
        if (a == null || b == null) return a == null && b == null;
        var x = a.Where(r => r != null).OrderBy(r => r.Source).ThenBy(r => r.Bus).ToList();
        var y = b.Where(r => r != null).OrderBy(r => r.Source).ThenBy(r => r.Bus).ToList();
        if (x.Count != y.Count) return false;
        for (int i = 0; i < x.Count; i++)
            if (x[i].Source != y[i].Source || x[i].Bus != y[i].Bus || Math.Abs(x[i].GainDb - y[i].GainDb) > 0.001) return false;
        return true;
    }

    /// <summary>Gir cuen routingen forhåndsvalget gir for dens kanaler. Cuer uten lyd eller uten utgang som får plass hoppes over.</summary>
    public static RoutingApplyResult Apply(IEnumerable<Cue?> targets, RoutingPreset preset, int busCount = 0)
        => Each(targets, (c, ch) =>
        {
            var routes = Routes(preset, ch, busCount);
            if (routes.Count == 0) return (false, null);
            return (true, routes);
        });

    /// <summary>Setter den samme routingen (eller standard, ved null) på alle cuene. Kanaler og utganger som ikke finnes i en cue fjernes.
    /// Er det ikke noe igjen av en routing som hadde ruter, hoppes cuen over så den ikke blir stum ved et uhell.</summary>
    public static RoutingApplyResult ApplyRoutes(IEnumerable<Cue?> targets, IReadOnlyList<AudioRoute>? routes, int busCount = 0)
        => Each(targets, (c, ch) =>
        {
            if (routes == null) return (true, null);
            var clean = CleanRoutes(routes, ch, busCount);
            if (clean.Count == 0 && routes.Count > 0) return (false, null);
            return (true, clean);
        });

    /// <summary>Limer inn kopiert routing på cuene.</summary>
    public static RoutingApplyResult Paste(IEnumerable<Cue?> targets, RouteClipboard clipboard, int busCount = 0)
        => clipboard.HasContent ? ApplyRoutes(targets, clipboard.Routes, busCount) : new RoutingApplyResult(0, 0, 0);

    private static RoutingApplyResult Each(IEnumerable<Cue?> targets, Func<IRoutedAudio, int, (bool ok, List<AudioRoute>? routes)> make)
    {
        int changed = 0, same = 0, skipped = 0;
        foreach (var c in (targets ?? Enumerable.Empty<Cue?>()).Distinct())
        {
            if (c is not IRoutedAudio r || r.FileChannels <= 0) { skipped++; continue; }
            var (ok, routes) = make(r, r.FileChannels);
            if (!ok) { skipped++; continue; }
            if (SameRoutes(r.Routes, routes)) { same++; continue; }
            r.Routes = routes;
            changed++;
        }
        return new RoutingApplyResult(changed, same, skipped);
    }

    /// <summary>Kort beskrivelse av routingen: navnet på et forhåndsvalg som gir akkurat denne routingen, ellers «Standard», «Av»
    /// eller «Utgang 1-2, 5».</summary>
    public static string Describe(IReadOnlyList<AudioRoute>? routes, int fileChannels, IEnumerable<RoutingPreset>? presets, int busCount = 0)
    {
        if (routes == null) return Loc.T("Standard");
        var live = routes.Where(r => r != null).ToList();
        if (live.Count == 0) return Loc.T("Av");
        foreach (var p in presets ?? Enumerable.Empty<RoutingPreset>())
            if (p != null && SameRoutes(live, Routes(p, fileChannels, busCount))) return p.Name;
        return Loc.F("Utgang {0}", Ranges(live.Select(r => r.Bus)));
    }
}

/// <summary>Kopiert routing fra en cue, klar til å limes inn på andre. Lagrer en kopi, så senere endringer i kilden ikke slår gjennom.</summary>
public sealed class RouteClipboard
{
    public bool HasContent { get; private set; }
    /// <summary>Kopiert routing. null = standard.</summary>
    public List<AudioRoute>? Routes { get; private set; }

    /// <summary>Kopierer routingen til cuen. Cuer uten routing (f.eks. bilde) gir false og endrer ingenting.</summary>
    public bool Copy(Cue? cue)
    {
        if (cue is not IRoutedAudio r) return false;
        Routes = r.Routes?.Where(x => x != null).Select(x => new AudioRoute(x.Source, x.Bus, x.GainDb)).ToList();
        HasContent = true;
        return true;
    }

    public void Clear() { HasContent = false; Routes = null; }
}

/// <summary>Leser lista med forhåndsvalg i showfila. Feil type, eller en skadet oppføring, gir tom liste eller hopper over oppføringen
/// i stedet for at hele showet ikke lar seg åpne.</summary>
public sealed class RoutingPresetListConverter : JsonConverter<List<RoutingPreset>>
{
    public override List<RoutingPreset>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var list = new List<RoutingPreset?>();
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return new List<RoutingPreset>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            try { list.Add(e.Deserialize<RoutingPreset>(options)); }
            catch (Exception) { /* skadet oppføring hoppes over */ }
        }
        return RoutingPresets.CleanList(list);
    }

    public override void Write(Utf8JsonWriter writer, List<RoutingPreset> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var p in value) JsonSerializer.Serialize(writer, p, options);
        writer.WriteEndArray();
    }
}
