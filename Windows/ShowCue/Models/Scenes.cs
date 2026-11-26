using System.Globalization;

namespace ShowCue.Models;

/// <summary>Resultatet av å slå opp et «gå til»-mål: scenen (kanonisk navn, tom i et show uten scener) og cuen (null = bare bytt scene).</summary>
public sealed record GotoTarget(string Scene, Cue? Cue);

/// <summary>
/// Ren logikk for scener (cuelister). Hver cue hører til én scene (<see cref="Cue.Scene"/>); showet har en ordnet liste med scenenavn
/// (<see cref="ShowDocument.Scenes"/>). Et show uten scener er én scene uten navn: alle cuer har tom <c>Scene</c>, og alt oppfører seg som før.
/// Cuene ligger fortsatt i én felles liste i showfila, så eldre versjoner leser den som én lang liste.
///
/// Oppslag av cuenummer (mål for gå til, fade, stopp, gruppe, fjernstyring):
///  1. «Scene:nummer» søker bare i den scenen (hvis scenen finnes).
///  2. Et vanlig nummer søkes først i hjemmescenen (scenen til cuen som peker, eller aktiv scene for fjernstyring), deretter i alle scener (første treff i listen).
/// Samme nummer kan altså brukes i flere scener.
/// </summary>
public static class SceneLogic
{
    /// <summary>Skille mellom scene og nummer i «Scene:nummer».</summary>
    public const char Qualifier = ':';

    /// <summary>Navnet på den første scenen når et show uten scener får sin andre scene.</summary>
    public static string DefaultName => Loc.T("Hoved");

    /// <summary>Scenenavn uten tegn som har egen betydning i mållister og OSC-adresser (: , ; / \ og linjeskift), uten mellomrom i endene, maks 60 tegn.</summary>
    public static string CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var chars = name.Trim().Select(ch => ch is ':' or ',' or ';' or '/' or '\\' or '\n' or '\r' or '\t' ? ' ' : ch).ToArray();
        var clean = new string(chars).Trim();
        return clean.Length > 60 ? clean[..60].Trim() : clean;
    }

    public static bool SameScene(string? a, string? b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

    public static bool InScene(Cue c, string? scene) => SameScene(c.Scene, scene);

    /// <summary>Cuene i scenen, i listerekkefølge.</summary>
    public static List<Cue> CuesIn(IEnumerable<Cue> cues, string? scene) => cues.Where(c => InScene(c, scene)).ToList();

    // ------------------------------------------------------------------
    //  Scenelista
    // ------------------------------------------------------------------

    /// <summary>
    /// Rydder scenelista (renser navn, fjerner tomme og like) og tar med scener som cuene bruker men lista mangler. Hver cue får det kanoniske
    /// scenenavnet; en cue uten scene havner i den første. Gir tom liste (én scene uten navn) når verken lista eller cuene har scener.
    /// </summary>
    public static List<string> Normalize(IEnumerable<string?>? names, IList<Cue> cues)
    {
        var result = new List<string>();
        void Add(string? n)
        {
            var clean = CleanName(n);
            if (clean.Length > 0 && !result.Any(r => SameScene(r, clean))) result.Add(clean);
        }
        foreach (var n in names ?? Array.Empty<string?>()) Add(n);
        foreach (var c in cues) Add(c.Scene);
        if (result.Count == 0) return result;
        foreach (var c in cues)
        {
            var canon = result.FirstOrDefault(r => SameScene(r, c.Scene)) ?? result[0];
            if (c.Scene != canon) c.Scene = canon;
        }
        return result;
    }

    /// <summary>Det kanoniske navnet på en scene som finnes (i lista eller hos en cue), ellers null.</summary>
    public static string? Canonical(IEnumerable<string> scenes, IEnumerable<Cue> cues, string? name)
    {
        var n = CleanName(name);
        if (n.Length == 0) return null;
        return scenes.FirstOrDefault(s => SameScene(s, n)) ?? cues.Select(c => c.Scene).FirstOrDefault(s => SameScene(s, n) && s.Length > 0);
    }

    /// <summary>Legger til en scene sist. Første gang får alle eksisterende cuer navnet <see cref="DefaultName"/>.
    /// Returnerer navnet som ble lagt til, eller null hvis navnet er tomt eller allerede i bruk.</summary>
    public static string? AddScene(List<string> scenes, IList<Cue> cues, string? name)
    {
        var n = CleanName(name);
        if (n.Length == 0) return null;
        if (scenes.Count == 0)
        {
            if (SameScene(n, DefaultName)) return null;
            var first = DefaultName;
            scenes.Add(first);
            foreach (var c in cues) c.Scene = first;
        }
        if (scenes.Any(s => SameScene(s, n))) return null;
        scenes.Add(n);
        return n;
    }

    /// <summary>Gir en scene nytt navn. Cuene, «gå til»-målene og «Scene:nummer»-mål følger med. I et show uten scener gir den scenen uten navn sitt første navn.
    /// False hvis navnet er tomt eller opptatt, eller scenen ikke finnes.</summary>
    public static bool RenameScene(List<string> scenes, IList<Cue> cues, string? oldName, string? newName)
    {
        var n = CleanName(newName);
        if (n.Length == 0) return false;
        if (scenes.Count == 0)
        {
            if (!SameScene(oldName, "")) return false;
            scenes.Add(n);
            foreach (var c in cues) c.Scene = n;
            return true;
        }
        int i = scenes.FindIndex(s => SameScene(s, oldName));
        if (i < 0) return false;
        if (scenes.Any(s => SameScene(s, n) && !SameScene(s, oldName))) return false;
        var old = scenes[i];
        scenes[i] = n;
        foreach (var c in cues)
        {
            if (SameScene(c.Scene, old)) c.Scene = n;
            if (c is GotoCue g && SameScene(g.TargetScene, old)) g.TargetScene = n;
            RewriteTargets(c, spec => CueTargets.RenameQualifier(spec, old, n));
        }
        return true;
    }

    /// <summary>Tar bort en scene. Cuene flyttes til scenen foran (eller den neste hvis den var først). Returnerer scenen de havnet i,
    /// eller null hvis scenen ikke finnes eller er den siste.</summary>
    public static string? DeleteScene(List<string> scenes, IList<Cue> cues, string? name)
    {
        int i = scenes.FindIndex(s => SameScene(s, name));
        if (i < 0 || scenes.Count < 2) return null;
        var gone = scenes[i];
        var heir = scenes[i > 0 ? i - 1 : 1];
        scenes.RemoveAt(i);
        foreach (var c in cues)
        {
            if (SameScene(c.Scene, gone)) c.Scene = heir;
            if (c is GotoCue g && SameScene(g.TargetScene, gone)) g.TargetScene = heir;
            RewriteTargets(c, spec => CueTargets.RenameQualifier(spec, gone, heir));
        }
        return heir;
    }

    /// <summary>Flytter scenen ett steg i rekkefølgen (−1 = mot starten). False hvis den allerede står ytterst.</summary>
    public static bool MoveScene(List<string> scenes, string? name, int delta)
    {
        int i = scenes.FindIndex(s => SameScene(s, name));
        int j = i + Math.Sign(delta);
        if (i < 0 || delta == 0 || j < 0 || j >= scenes.Count) return false;
        (scenes[i], scenes[j]) = (scenes[j], scenes[i]);
        return true;
    }

    /// <summary>Scenen ett steg før/etter <paramref name="current"/>, eller null i enden av lista.</summary>
    public static string? Neighbour(IReadOnlyList<string> scenes, string? current, int delta)
    {
        int i = -1;
        for (int k = 0; k < scenes.Count; k++) if (SameScene(scenes[k], current)) { i = k; break; }
        int j = i + Math.Sign(delta);
        return i < 0 || delta == 0 || j < 0 || j >= scenes.Count ? null : scenes[j];
    }

    // ------------------------------------------------------------------
    //  Plassering og nummerering
    // ------------------------------------------------------------------

    /// <summary>Plassen rett etter siste cue i scenen (eller slutten av lista hvis scenen er tom).</summary>
    public static int EndOfScene(IList<Cue> cues, string? scene)
    {
        for (int i = cues.Count - 1; i >= 0; i--)
            if (InScene(cues[i], scene)) return i + 1;
        return cues.Count;
    }

    /// <summary>Flytter cuene til slutten av en annen scene. En cue som får samme nummer som en annen i den scenen, får et nytt nummer.</summary>
    public static void MoveToScene(IList<Cue> cues, IEnumerable<Cue> moving, string scene)
    {
        foreach (var c in moving.OrderBy(m => cues.IndexOf(m)).ToList())
        {
            if (SameScene(c.Scene, scene) || !cues.Remove(c)) continue;
            c.Scene = scene;
            var number = c.Number;
            if (number.Length == 0 || cues.Any(x => InScene(x, scene) && string.Equals(x.Number, number, StringComparison.OrdinalIgnoreCase)))
                c.Number = NextNumber(cues, scene);
            cues.Insert(EndOfScene(cues, scene), c);
        }
    }

    /// <summary>Neste ledige hele nummer i scenen (størst + 1).</summary>
    public static string NextNumber(IEnumerable<Cue> cues, string? scene)
    {
        double max = 0;
        foreach (var c in cues)
            if (InScene(c, scene) && double.TryParse(c.Number, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n))
                max = Math.Max(max, n);
        return ((long)Math.Floor(Math.Min(max, 1e9)) + 1).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Gir cuene i scenen nye nummer (1, 2, 3 …) i listerekkefølge, og oppdaterer målene som pekte på de gamle numrene:
    /// vanlige nummer hos cuer som peker inn i scenen, og «Scene:nummer» hos alle. Returnerer tabellen gammelt → nytt nummer.
    /// </summary>
    public static Dictionary<string, string> Renumber(IList<Cue> cues, string? scene)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int n = 0;
        foreach (var c in cues.Where(c => InScene(c, scene)).ToList())
        {
            var number = (++n).ToString(CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(c.Number)) map.TryAdd(c.Number, number);
            c.Number = number;
        }
        var qualifier = CleanName(scene);
        foreach (var c in cues)
        {
            var home = c is GotoCue { TargetScene.Length: > 0 } g ? g.TargetScene : c.Scene;
            bool pointsIntoScene = InScene(c, home) && SameScene(home, scene);
            RewriteTargets(c, spec =>
            {
                var s = pointsIntoScene ? CueTargets.Renumber(spec, map) : spec;
                return qualifier.Length > 0 ? CueTargets.Renumber(s, map, qualifier) : s;
            });
        }
        return map;
    }

    /// <summary>Bruker <paramref name="map"/> på målfeltet til en fade-, stopp-, «gå til»- eller gruppe-cue.</summary>
    public static void RewriteTargets(Cue c, Func<string, string> map)
    {
        switch (c)
        {
            case FadeCue f: f.Target = map(f.Target); break;
            case StopCue s: s.Target = map(s.Target); break;
            case GotoCue g: g.Target = map(g.Target); break;
            case GroupCue gr: gr.Targets = map(gr.Targets); break;
        }
    }

    // ------------------------------------------------------------------
    //  GO-markør og rekkefølge innen en scene
    // ------------------------------------------------------------------

    /// <summary>Indeksen GO-markøren skal til etter at cuen på <paramref name="firedIndex"/> er avfyrt: hopper over cuer som starter automatisk
    /// og deaktiverte (i øvelsesmodus også merkede). Gir <c>list.Count</c> ved slutten.</summary>
    public static int NextPlayheadIndex(IList<Cue> list, int firedIndex, bool rehearsal = false)
    {
        int i = firedIndex;
        // Hopp over cues som blir trigget automatisk av forrige cue.
        while (i < list.Count && list[i].ContinueMode != ContinueMode.None)
            i = Rehearsal.NextPlayableIndex(list, i + 1, rehearsal);
        i = Rehearsal.NextPlayableIndex(list, i + 1, rehearsal);
        return Math.Min(i, list.Count);
    }

    /// <summary>Neste cue for GO etter at <paramref name="fired"/> er avfyrt, bare innen samme scene. Null = slutten av scenen (GO hopper aldri til en annen scene).</summary>
    public static Cue? NextPlayhead(IList<Cue> cues, Cue fired, bool rehearsal = false)
    {
        var list = CuesIn(cues, fired.Scene);
        int idx = list.IndexOf(fired);
        if (idx < 0) return null;
        int n = NextPlayheadIndex(list, idx, rehearsal);
        return n < list.Count ? list[n] : null;
    }

    /// <summary>Første cue etter <paramref name="cue"/> i samme scene som kan spilles: ikke deaktivert, og i øvelsesmodus ikke merket (for auto-fortsett).</summary>
    public static Cue? NextEnabledAfter(IList<Cue> cues, Cue cue, bool rehearsal = false)
    {
        var list = CuesIn(cues, cue.Scene);
        int idx = list.IndexOf(cue);
        if (idx < 0) return null;
        int n = Rehearsal.NextPlayableIndex(list, idx + 1, rehearsal);
        return n < list.Count ? list[n] : null;
    }

    /// <summary>Første ikke-deaktiverte cue i scenen (der GO-markøren starter).</summary>
    public static Cue? FirstEnabled(IEnumerable<Cue> cues, string? scene) => cues.FirstOrDefault(c => InScene(c, scene) && !c.Disabled);

    /// <summary>Flytter markøren <paramref name="delta"/> steg i cuene i en scene, men ikke ut over endene.</summary>
    public static Cue? Step(IList<Cue> sceneCues, Cue? current, int delta)
    {
        if (sceneCues.Count == 0) return null;
        int idx = current != null ? sceneCues.IndexOf(current) : -1;
        if (idx < 0) idx = delta > 0 ? -1 : sceneCues.Count;
        return sceneCues[Math.Clamp(idx + delta, 0, sceneCues.Count - 1)];
    }

    /// <summary>Plassen til nærmeste cue i samme scene i retning av <paramref name="delta"/> (for «flytt opp/ned»), eller −1.</summary>
    public static int NeighbourIndex(IList<Cue> cues, int index, int delta)
    {
        if (index < 0 || index >= cues.Count || delta == 0) return -1;
        int dir = Math.Sign(delta);
        for (int i = index + dir; i >= 0 && i < cues.Count; i += dir)
            if (InScene(cues[i], cues[index].Scene)) return i;
        return -1;
    }

    // ------------------------------------------------------------------
    //  Oppslag av mål
    // ------------------------------------------------------------------

    /// <summary>Deler «Scene:nummer» i scene og nummer. Nummeret kan være tomt («Scene:» = hele scenen).</summary>
    public static bool TrySplit(string? token, out string scene, out string rest)
    {
        scene = rest = "";
        var t = (token ?? "").Trim();
        int i = t.IndexOf(Qualifier);
        if (i <= 0) return false;
        scene = t[..i].Trim();
        rest = t[(i + 1)..].Trim();
        return scene.Length > 0;
    }

    private static bool SameNumber(Cue c, string number) => string.Equals(c.Number.Trim(), number, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Cuene et nummer peker på. «Scene:nummer» (der scenen har cuer) gir treffene i den scenen. Et vanlig nummer gir treffene i
    /// <paramref name="home"/> hvis det finnes noen der, ellers treffene i alle scener. <paramref name="home"/> = null hopper over hjemmescenen.
    /// </summary>
    public static List<Cue> Matching(IEnumerable<Cue> cues, string? token, string? home)
    {
        var all = cues.ToList();
        var t = (token ?? "").Trim();
        if (t.Length == 0) return new List<Cue>();
        if (TrySplit(t, out var scene, out var rest) && rest.Length > 0 && all.Any(c => InScene(c, scene)))
            return all.Where(c => InScene(c, scene) && SameNumber(c, rest)).ToList();
        if (home != null)
        {
            var inHome = all.Where(c => InScene(c, home) && SameNumber(c, t)).ToList();
            if (inHome.Count > 0) return inHome;
        }
        return all.Where(c => SameNumber(c, t)).ToList();
    }

    /// <summary>Første cue et nummer peker på (se <see cref="Matching"/>), eller null.</summary>
    public static Cue? FindByNumber(IEnumerable<Cue> cues, string? token, string? home) => Matching(cues, token, home).FirstOrDefault();

    /// <summary>Målteksten en «gå til»-cue sender til motoren: nummeret, «Scene:nummer», eller «Scene:» når bare scenen skal byttes.</summary>
    public static string GotoSpec(string? target, string? targetScene)
    {
        var t = (target ?? "").Trim();
        var s = CleanName(targetScene);
        if (s.Length == 0) return t;
        if (t.Length == 0) return s + Qualifier;
        return t.IndexOf(Qualifier) > 0 ? t : s + Qualifier + t;
    }

    /// <summary>Slår opp en målstreng fra <see cref="GotoSpec"/>. Null hvis scenen eller cuen ikke finnes.</summary>
    public static GotoTarget? ResolveGoto(IList<Cue> cues, IEnumerable<string> scenes, string? spec, string? home)
    {
        var t = (spec ?? "").Trim();
        if (t.Length == 0) return null;
        if (TrySplit(t, out var sc, out var rest) && rest.Length == 0)
            return Canonical(scenes, cues, sc) is { } name ? new GotoTarget(name, null) : null;
        var cue = FindByNumber(cues, t, home);
        return cue == null ? null : new GotoTarget(cue.Scene, cue);
    }
}
