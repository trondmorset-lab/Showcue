namespace ShowCue.Models;

/// <summary>Hva slags forberedelse en fil trenger.</summary>
public enum PreloadKind { Audio, Video, Image }

/// <summary>Én fil som kan forberedes før GO. <see cref="Key"/> avgjør om to elementer er «det samme» (type og sti);
/// <see cref="StartTime"/> er bare et hint om hvor i fila avspillingen begynner.</summary>
public sealed record PreloadItem(PreloadKind Kind, string Path, double StartTime = 0)
{
    public string Key => (int)Kind + "|" + Path;
}

/// <summary>
/// Ren logikk for hvilke cuer som forberedes i bakgrunnen: cuen GO vil starte nå, og cuene den starter automatisk etterpå.
/// Ingen filtilgang og ingen tråder her, så det kan testes uten maskinvare.
/// </summary>
public static class PreloadPlanner
{
    /// <summary>Største antall forberedte cuer samtidig.</summary>
    public const int MaxItems = 3;
    /// <summary>Lengste kjede av cuer (hopp, gå til, fortsett) som følges, så en løkke aldri henger.</summary>
    private const int MaxSteps = 16;

    /// <summary>
    /// Filene som bør være klare for GO-markøren på <paramref name="playheadIndex"/>, viktigst først.
    /// Cuen under markøren (eller neste som ikke er deaktivert, slik GO velger), så cuene den starter automatisk
    /// (fortsett automatisk / følg automatisk), «gå til» følges til målet, og en gruppe gir sine mål.
    /// Deaktiverte cuer, PDF, nettsider, spillelister og lysbildeserier forberedes ikke.
    /// Planen følger det GO og motoren gjør: «neste» er alltid neste i samme scene (aldri en cue i en annen scene), «gå til» følges til
    /// målet i riktig scene, og i øvelsesmodus (<paramref name="rehearsal"/>) hoppes merkede cuer over.
    /// </summary>
    public static List<PreloadItem> Plan(IList<Cue>? cues, int playheadIndex, int max = MaxItems, bool rehearsal = false)
    {
        var result = new List<PreloadItem>();
        max = Math.Clamp(max, 0, MaxItems);
        if (cues == null || max == 0 || playheadIndex < 0 || playheadIndex >= cues.Count) return result;

        var visited = new HashSet<int>();
        int i = NextPlayable(cues, playheadIndex, cues[playheadIndex].Scene, rehearsal);
        for (int step = 0; i >= 0 && i < cues.Count && step < MaxSteps && result.Count < max && visited.Add(i); step++)
        {
            var cue = cues[i];
            if (cue is GotoCue go)
            {
                var targetCue = SceneLogic.FindByNumber(cues, SceneLogic.GotoSpec(go.Target, go.TargetScene), go.Scene);
                int target = targetCue == null ? -1 : cues.IndexOf(targetCue);
                if (target < 0) break; // mangler målet, eller bare scenen byttes: ingen fil å forberede
                i = NextPlayable(cues, target, targetCue!.Scene, rehearsal); // GO på en deaktivert mål-cue hopper videre
                continue;
            }
            if (cue is GroupCue group)
            {
                foreach (var t in CueTargets.Resolve(cues, group.Targets, group))
                    if (!t.Disabled) Add(result, t, max);
            }
            else Add(result, cue, max);

            if (cue.ContinueMode == ContinueMode.None) break;
            i = NextPlayable(cues, i + 1, cue.Scene, rehearsal);
        }
        return result;
    }

    /// <summary>Første cue fra <paramref name="from"/> i <paramref name="scene"/> som GO kan starte (ikke deaktivert, og i øvelsesmodus ikke merket). −1 hvis ingen.</summary>
    private static int NextPlayable(IList<Cue> cues, int from, string? scene, bool rehearsal)
    {
        for (int i = Math.Max(0, from); i < cues.Count; i++)
            if (SceneLogic.InScene(cues[i], scene) && Rehearsal.IsPlayable(cues[i], rehearsal)) return i;
        return -1;
    }

    private static void Add(List<PreloadItem> result, Cue cue, int max)
    {
        if (result.Count >= max) return;
        PreloadItem? item = cue switch
        {
            AudioCue a => Make(PreloadKind.Audio, a.FilePath, a.StartTime),
            VideoCue v => Make(PreloadKind.Video, v.FilePath, v.StartTime),
            ImageCue im => Make(PreloadKind.Image, im.FilePath, 0),
            _ => null,
        };
        if (item != null && !result.Any(r => r.Key == item.Key)) result.Add(item);
    }

    private static PreloadItem? Make(PreloadKind kind, string? path, double start)
    {
        path = (path ?? "").Trim();
        if (path.Length == 0) return null;
        return new PreloadItem(kind, path, double.IsFinite(start) ? Math.Clamp(start, 0, 86400) : 0);
    }
}

/// <summary>Hva som må gjøres for å gå fra de forberedte filene til de ønskede.</summary>
public sealed record PreloadDiff(List<string> ToStart, List<string> ToEvict);

/// <summary>
/// Regnskap over hvilke filer som er forberedt (eller under forberedelse), med fast øvre grense.
/// Ren logikk uten filer og tråder. Det som ikke lenger ønskes fjernes, eldste først, og aldri flere enn <see cref="Capacity"/> beholdes.
/// </summary>
public sealed class PreloadBook
{
    private readonly List<string> _keys = new();
    public int Capacity { get; }

    public PreloadBook(int capacity = PreloadPlanner.MaxItems) => Capacity = Math.Clamp(capacity, 1, 16);

    /// <summary>Nøklene som holdes nå, i prioritetsrekkefølge (viktigst først).</summary>
    public IReadOnlyList<string> Keys => _keys;
    public bool Contains(string key) => _keys.Contains(key);

    /// <summary>Gjør de ønskede nøklene (viktigst først) til de som holdes. Dobbelte og alt utover grensen ignoreres.</summary>
    public PreloadDiff Sync(IEnumerable<string> wanted)
    {
        var keep = new List<string>();
        foreach (var k in wanted)
        {
            if (keep.Count >= Capacity) break;
            if (!string.IsNullOrEmpty(k) && !keep.Contains(k)) keep.Add(k);
        }
        var evict = _keys.Where(k => !keep.Contains(k)).ToList();      // i den rekkefølgen de ble lagt til: eldste først
        var start = keep.Where(k => !_keys.Contains(k)).ToList();
        _keys.Clear();
        _keys.AddRange(keep);
        return new PreloadDiff(start, evict);
    }

    /// <summary>Slutt å holde en nøkkel (f.eks. fordi den ble overlevert til en avspilling). False hvis den ikke var med.</summary>
    public bool Remove(string key) => _keys.Remove(key);

    /// <summary>Slipp alt. Gir nøklene som ble holdt, eldste først.</summary>
    public List<string> Clear()
    {
        var all = _keys.ToList();
        _keys.Clear();
        return all;
    }
}
