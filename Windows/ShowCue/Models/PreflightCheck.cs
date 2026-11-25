namespace ShowCue.Models;

public enum PreflightSeverity { Ok, Warning, Error }

/// <summary>Tilstanden til en mediefil: finnes og kan åpnes, finnes ikke, eller finnes men kan ikke leses.</summary>
public enum FileStatus { Ok, Missing, Unreadable }

/// <summary>Ett funn i forhåndssjekken. <see cref="Code"/> er en fast kode (ikke oversatt) som tester og
/// andre programdeler kan bruke; <see cref="Message"/> er teksten som vises. <see cref="CueIndex"/> er raden
/// i cuelista (fra 0), eller −1 når funnet ikke gjelder én bestemt cue.</summary>
public sealed record PreflightFinding(PreflightSeverity Severity, string Code, string Message, int CueIndex = -1);

/// <summary>Det sjekken trenger å vite om maskinen. Egen abstraksjon, så sjekklogikken kan testes uten filer, lydkort og skjermer.</summary>
public interface IPreflightEnvironment
{
    FileStatus CheckFile(string path);
    /// <summary>Finnes lydutgangen (WASAPI-enhetens ID eller ASIO-driverens navn)?</summary>
    bool AudioOutputExists(AudioDriverType driver, string id);
    /// <summary>Finnes opptaksenheten (WASAPI-enhetens ID)?</summary>
    bool InputDeviceExists(string id);
    /// <summary>Finnes skjermen med dette nummeret (fra 0)?</summary>
    bool ScreenExists(int index);
}

/// <summary>Forhåndssjekk før show: går gjennom cuene og maskininnstillingene og lager en liste med funn.
/// Ren logikk uten vinduer; alt som krever maskinen går via <see cref="IPreflightEnvironment"/>.
/// Deaktiverte cuer hoppes over (GO hopper over dem), men cue-numrene deres telles med i sjekken for duplikater.</summary>
public static class PreflightCheck
{
    public const string MediaEmpty = "mediaEmpty";
    public const string MediaMissing = "mediaMissing";
    public const string MediaUnreadable = "mediaUnreadable";
    public const string ListEmpty = "listEmpty";
    public const string MediaOk = "mediaOk";
    public const string ValueInvalid = "valueInvalid";
    public const string RequiredEmpty = "requiredEmpty";
    public const string UrlInvalid = "urlInvalid";
    public const string TimeOrder = "timeOrder";
    public const string StartBeyondFile = "startBeyondFile";
    public const string MemoEmpty = "memoEmpty";
    public const string TargetEmpty = "targetEmpty";
    public const string TargetMissing = "targetMissing";
    public const string TargetSelf = "targetSelf";
    public const string TargetDisabled = "targetDisabled";
    public const string GroupNoMatch = "groupNoMatch";
    public const string ClockTriggerOff = "clockTriggerOff";
    public const string ValuesOk = "valuesOk";
    public const string AudioDeviceMissing = "audioDeviceMissing";
    public const string PreviewDeviceMissing = "previewDeviceMissing";
    public const string InputDeviceMissing = "inputDeviceMissing";
    public const string ScreenMissing = "screenMissing";
    public const string DevicesOk = "devicesOk";
    public const string NumberDuplicate = "numberDuplicate";
    public const string SceneMissing = "sceneMissing";
    public const string NumbersOk = "numbersOk";

    /// <summary>Kjører hele sjekken. Rekkefølgen er mediefiler, verdier og mål, enheter, cue-numre; en gruppe uten funn får én «ok»-linje.</summary>
    public static List<PreflightFinding> Run(IReadOnlyList<Cue> cues, AppSettings settings, IPreflightEnvironment env, IReadOnlyList<string>? scenes = null)
    {
        var media = new List<PreflightFinding>();
        var values = new List<PreflightFinding>();
        var devices = new List<PreflightFinding>();
        var numbers = new List<PreflightFinding>();
        var fileCount = 0;

        for (var i = 0; i < cues.Count; i++)
        {
            if (cues[i].Disabled) continue;
            fileCount += CheckMedia(cues[i], i, env, media);
            CheckValues(cues[i], i, cues, values, scenes);
        }
        CheckClockTriggers(cues, settings, values);
        CheckDevices(cues, settings, env, devices);
        CheckNumbers(cues, numbers);

        var all = new List<PreflightFinding>();
        all.AddRange(media);
        if (media.Count == 0)
            all.Add(new(PreflightSeverity.Ok, MediaOk, fileCount == 0 ? Loc.T("Showet bruker ingen mediefiler.") : Loc.F("{0} mediefil(er) finnes og kan leses.", fileCount)));
        all.AddRange(values);
        if (values.Count == 0) all.Add(new(PreflightSeverity.Ok, ValuesOk, Loc.T("Ingen ugyldige verdier eller mål.")));
        all.AddRange(devices);
        if (devices.Count == 0) all.Add(new(PreflightSeverity.Ok, DevicesOk, Loc.T("Lydutgang og skjermer finnes.")));
        all.AddRange(numbers);
        if (numbers.Count == 0) all.Add(new(PreflightSeverity.Ok, NumbersOk, Loc.T("Ingen duplikate cue-numre.")));
        return all;
    }

    public static int Count(IEnumerable<PreflightFinding> findings, PreflightSeverity severity) => findings.Count(f => f.Severity == severity);

    /// <summary>«Cue 3 «Navn»», eller raden hvis cuen ikke har nummer.</summary>
    private static string Label(Cue c, int index) => string.IsNullOrWhiteSpace(c.Number)
        ? Loc.F("Rad {0} «{1}»", index + 1, c.DisplayName)
        : Loc.F("Cue {0} «{1}»", c.Number, c.DisplayName);

    /// <summary>Tall til tekst med opptil to desimaler (samme på begge plattformer).</summary>
    private static string Num(double v) => v.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture);

    // ---------------- Mediefiler ----------------

    /// <summary>Sjekker filene cuen peker på. Returnerer antall filer som ble sjekket.</summary>
    private static int CheckMedia(Cue c, int index, IPreflightEnvironment env, List<PreflightFinding> list)
    {
        var label = Label(c, index);
        // Nettside-cuer arver FilePath, men bruker adresse og ikke fil.
        if (c is IFileCue fc && c is not WebCue && string.IsNullOrWhiteSpace(fc.FilePath))
            list.Add(new(PreflightSeverity.Error, MediaEmpty, Loc.F("{0}: ingen fil er valgt.", label), index));
        if (c is IFileListCue fl && !fl.Files.Any(f => !string.IsNullOrWhiteSpace(f)))
            list.Add(new(PreflightSeverity.Warning, ListEmpty, Loc.F("{0}: listen har ingen filer.", label), index));

        var paths = MediaTools.MediaPaths(new[] { c });
        foreach (var p in paths)
        {
            switch (env.CheckFile(p))
            {
                case FileStatus.Missing:
                    list.Add(new(PreflightSeverity.Error, MediaMissing, Loc.F("{0}: mediefila finnes ikke: {1}", label, p), index));
                    break;
                case FileStatus.Unreadable:
                    list.Add(new(PreflightSeverity.Error, MediaUnreadable, Loc.F("{0}: mediefila kan ikke leses: {1}", label, p), index));
                    break;
            }
        }
        return paths.Count;
    }

    // ---------------- Verdier og mål ----------------

    private static void CheckValues(Cue c, int index, IReadOnlyList<Cue> all, List<PreflightFinding> list, IReadOnlyList<string>? scenes = null)
    {
        var label = Label(c, index);
        void Time(string field, double seconds)
        {
            if (!double.IsFinite(seconds) || seconds < 0)
                list.Add(new(PreflightSeverity.Error, ValueInvalid, Loc.F("{0}: «{1}» er ikke en gyldig tid.", label, field), index));
        }
        void Level(string field, double level)
        {
            if (!double.IsFinite(level))
                list.Add(new(PreflightSeverity.Error, ValueInvalid, Loc.F("{0}: «{1}» er ikke et gyldig tall.", label, field), index));
        }
        void Required(string field, string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                list.Add(new(PreflightSeverity.Error, RequiredEmpty, Loc.F("{0}: «{1}» må fylles ut.", label, field), index));
        }
        void Url(string? url, bool web)
        {
            if (!(web ? IsWebAddress(url) : IsHttpUrl(url)))
                list.Add(new(PreflightSeverity.Error, UrlInvalid, Loc.F("{0}: nettadressen er ugyldig: {1}", label, url ?? ""), index));
        }

        Time(Loc.T("Forsinkelse"), c.PreWait);

        if (c is ITrimmable t)
        {
            Time(Loc.T("Starttid"), t.StartTime);
            Time(Loc.T("Sluttid"), t.EndTime);
            Time(Loc.T("Fade inn"), t.FadeIn);
            Time(Loc.T("Fade ut"), t.FadeOut);
            if (double.IsFinite(t.StartTime) && double.IsFinite(t.EndTime))
            {
                if (t.EndTime > 0 && t.EndTime <= t.StartTime)
                    list.Add(new(PreflightSeverity.Error, TimeOrder,
                        Loc.F("{0}: sluttiden ({1} s) må være etter starttiden ({2} s).", label, Num(t.EndTime), Num(t.StartTime)), index));
                else if (t.MediaLength > 0 && t.StartTime >= t.MediaLength)
                    list.Add(new(PreflightSeverity.Error, StartBeyondFile,
                        Loc.F("{0}: starttiden ({1} s) er etter slutten av fila ({2} s).", label, Num(t.StartTime), Num(t.MediaLength)), index));
            }
        }
        else if (c is VisualCue v)
        {
            Time(Loc.T("Fade inn"), v.FadeIn);
            Time(Loc.T("Fade ut"), v.FadeOut);
        }
        if (c is StillCue s) Time(Loc.T("Visningstid"), s.HoldTime);
        if (c is IRoutedAudio r) Level(Loc.T("Nivå"), r.VolumeDb);

        switch (c)
        {
            case WebCue w:
                Url(w.Url, web: true);
                break;
            case PlaylistCue p:
                Time(Loc.T("Krysstoning"), p.Crossfade);
                Time(Loc.T("Fade ut"), p.FadeOut);
                break;
            case SlideshowCue ss:
                Time(Loc.T("Tid per bilde"), ss.SlideTime);
                Time(Loc.T("Krysstoning"), ss.Crossfade);
                break;
            case InputCue ic:
                Time(Loc.T("Fade inn"), ic.FadeIn);
                Time(Loc.T("Fade ut"), ic.FadeOut);
                break;
            case FadeCue f:
                Time(Loc.T("Fade-tid"), f.FadeTime);
                Level(Loc.T("Mål-nivå"), f.TargetDb);
                Level(Loc.T("Mål-synlighet"), f.TargetOpacity);
                CheckTarget(c, index, f.Target, required: true, all, list);
                break;
            case StopCue st:
                Time(Loc.T("Fade-tid"), st.FadeTime);
                CheckTarget(c, index, st.Target, required: false, all, list);
                break;
            case GotoCue g:
                CheckGoto(g, index, all, scenes, list);
                break;
            case GroupCue gr:
                CheckGroup(gr, index, all, list);
                break;
            case OscCue o:
                Required(Loc.T("Adresse til mottaker"), o.Host);
                Required(Loc.T("OSC-adresse"), o.Address);
                break;
            case NetworkCue n:
                if (n.Protocol == NetworkProtocol.Http) Url(n.Url, web: false);
                else Required(Loc.T("Adresse til mottaker"), n.Host);
                break;
            case LightCue l:
                Required(Loc.T("Adresse til mottaker"), l.Host);
                Time(Loc.T("Fade-tid"), l.FadeTime);
                break;
            case TimecodeCue tc:
                Time(Loc.T("Varighet"), tc.Length);
                break;
            case MidiCue m when m.MessageType == MidiMessageType.MscGo:
                Required(Loc.T("MSC cue-nummer"), m.MscCueNumber);
                break;
            case MemoCue memo when string.IsNullOrWhiteSpace(memo.Text):
                list.Add(new(PreflightSeverity.Warning, MemoEmpty, Loc.F("{0}: beskjeden er tom.", label), index));
                break;
        }
    }

    /// <summary>Cuer med klokketrigger gjør ingenting når «Klokke-triggere aktive» er av i innstillingene.</summary>
    private static void CheckClockTriggers(IReadOnlyList<Cue> cues, AppSettings settings, List<PreflightFinding> list)
    {
        if (settings.ClockTriggersEnabled) return;
        var withTrigger = cues.Select((c, i) => (c, i)).Where(x => !x.c.Disabled && x.c.ClockTrigger.Length > 0).ToList();
        if (withTrigger.Count == 0) return;
        list.Add(new(PreflightSeverity.Warning, ClockTriggerOff,
            Loc.F("{0} cue(r) har klokketrigger, men «Klokke-triggere aktive» er av i innstillingene. Cuene starter ikke av seg selv.", withTrigger.Count),
            withTrigger[0].i));
    }

    /// <summary>Mål-cuen slås opp på nummer, uten hensyn til store/små bokstaver (som i GO-motoren).</summary>
    private static void CheckTarget(Cue c, int index, string? target, bool required, IReadOnlyList<Cue> all, List<PreflightFinding> list, string? home = null)
    {
        var label = Label(c, index);
        var t = (target ?? "").Trim();
        if (t.Length == 0)
        {
            if (required) list.Add(new(PreflightSeverity.Error, TargetEmpty, Loc.F("{0}: mål-cue er ikke valgt.", label), index));
            return;
        }
        var hits = SceneLogic.Matching(all, t, home ?? c.Scene);
        if (hits.Count == 0)
            list.Add(new(PreflightSeverity.Error, TargetMissing, Loc.F("{0}: mål-cuen «{1}» finnes ikke.", label, t), index));
        else if (hits.All(x => ReferenceEquals(x, c)))
            list.Add(new(PreflightSeverity.Warning, TargetSelf, Loc.F("{0}: peker på seg selv.", label), index));
        else if (hits.All(x => x.Disabled))
            list.Add(new(PreflightSeverity.Warning, TargetDisabled, Loc.F("{0}: mål-cuen «{1}» er deaktivert.", label, t), index));
    }

    /// <summary>«Gå til»: scenen må finnes, og nummeret søkes i målscenen. Bare scene (uten nummer) er lov.</summary>
    private static void CheckGoto(GotoCue g, int index, IReadOnlyList<Cue> all, IReadOnlyList<string>? scenes, List<PreflightFinding> list)
    {
        var scene = g.TargetScene;
        if (scene.Length > 0 && SceneLogic.Canonical(scenes ?? Array.Empty<string>(), all, scene) == null)
        {
            list.Add(new(PreflightSeverity.Error, SceneMissing, Loc.F("{0}: scenen «{1}» finnes ikke.", Label(g, index), scene), index));
            return;
        }
        if (scene.Length > 0 && g.Target.Length == 0) return;
        CheckTarget(g, index, SceneLogic.GotoSpec(g.Target, scene), required: true, all, list, home: scene.Length > 0 ? scene : null);
    }

    /// <summary>Gruppe-cue: listen må ha mål, målene må finnes, og minst én cue må treffes. Deaktiverte mål gir advarsel (GO hopper over dem).</summary>
    private static void CheckGroup(GroupCue g, int index, IReadOnlyList<Cue> all, List<PreflightFinding> list)
    {
        var label = Label(g, index);
        if (string.IsNullOrWhiteSpace(g.Targets))
        {
            list.Add(new(PreflightSeverity.Error, TargetEmpty, Loc.F("{0}: gruppen har ingen mål-cuer.", label), index));
            return;
        }
        var missing = new List<string>();
        var found = CueTargets.Resolve(all, g.Targets, g, missing);
        if (missing.Count > 0)
            list.Add(new(PreflightSeverity.Error, TargetMissing, Loc.F("{0}: mål-cuene finnes ikke: {1}", label, string.Join(", ", missing)), index));
        else if (found.Count == 0)
            list.Add(new(PreflightSeverity.Error, GroupNoMatch, Loc.F("{0}: gruppen treffer ingen cuer.", label), index));
        var disabled = found.Where(x => x.Disabled).ToList();
        if (disabled.Count > 0)
            list.Add(new(PreflightSeverity.Warning, TargetDisabled,
                Loc.F("{0}: mål-cuene er deaktivert: {1}", label, string.Join(", ", disabled.Select(x => x.Number))), index));
    }

    /// <summary>«https://…» eller «http://…» med noe etter, uten mellomrom.</summary>
    public static bool IsHttpUrl(string? url)
    {
        var u = (url ?? "").Trim();
        var rest = u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? u[7..]
            : u.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? u[8..] : null;
        return rest is { Length: > 0 } && !rest.Any(char.IsWhiteSpace);
    }

    /// <summary>Adresse en nettside-cue kan vise: http(s)-adresse, file-adresse, eller en adresse uten «://» (f.eks. «www.x.no» eller en filsti).</summary>
    public static bool IsWebAddress(string? url)
    {
        var u = (url ?? "").Trim();
        if (u.Length == 0) return false;
        if (!u.Contains("://")) return true;
        return IsHttpUrl(u) || (u.StartsWith("file://", StringComparison.OrdinalIgnoreCase) && u.Length > 7);
    }

    // ---------------- Enheter ----------------

    private static int? ScreenOf(Cue c) => c switch
    {
        VisualCue v => v.OutputScreen,
        SlideshowCue s => s.OutputScreen,
        _ => null,
    };

    private static void CheckDevices(IReadOnlyList<Cue> cues, AppSettings s, IPreflightEnvironment env, List<PreflightFinding> list)
    {
        var active = cues.Select((c, i) => (c, i)).Where(x => !x.c.Disabled).ToList();

        // Lydutgang: bare relevant når showet har cuer som lager lyd. Tom ID = standardenheten, som alltid finnes.
        var usesAudio = active.Any(x => x.c is IRoutedAudio || x.c is TimecodeCue { SendLtc: true });
        if (usesAudio)
        {
            var wasapi = s.AudioDriver == AudioDriverType.Wasapi;
            var id = wasapi ? s.WasapiDeviceId : s.AsioDriverName;
            var ok = wasapi ? string.IsNullOrEmpty(id) || env.AudioOutputExists(s.AudioDriver, id)
                            : !string.IsNullOrEmpty(id) && env.AudioOutputExists(s.AudioDriver, id);
            if (!ok)
                list.Add(new(PreflightSeverity.Error, AudioDeviceMissing, Loc.T("Valgt lydutgang finnes ikke. Velg lydutgang under Innstillinger.")));
        }
        if (!string.IsNullOrEmpty(s.PreviewDeviceId) && !env.AudioOutputExists(AudioDriverType.Wasapi, s.PreviewDeviceId))
            list.Add(new(PreflightSeverity.Warning, PreviewDeviceMissing, Loc.T("Valgt lydutgang for forhåndslytting finnes ikke.")));

        var inputs = active.Count(x => x.c is InputCue);
        if (inputs > 0 && !string.IsNullOrEmpty(s.InputDeviceId) && !env.InputDeviceExists(s.InputDeviceId))
            list.Add(new(PreflightSeverity.Error, InputDeviceMissing, Loc.F("Valgt lydinngang finnes ikke, men {0} live-inngang-cue(r) bruker den.", inputs)));

        // Skjermer: finnes ikke skjermen, vises cuen i et vanlig vindu. Derfor advarsel og ikke feil.
        var screens = active.Select(x => (x.c, x.i, screen: ScreenOf(x.c))).Where(x => x.screen != null).ToList();
        if (s.VideoScreen >= 0 && screens.Any(x => x.screen == -2) && !env.ScreenExists(s.VideoScreen))
            list.Add(new(PreflightSeverity.Warning, ScreenMissing,
                Loc.F("Skjerm {0} fra innstillingene finnes ikke. Visuelle cuer uten egen skjerm vises i et vanlig vindu.", s.VideoScreen + 1)));
        foreach (var (c, i, screen) in screens)
            if (screen >= 0 && !env.ScreenExists(screen.Value))
                list.Add(new(PreflightSeverity.Warning, ScreenMissing, Loc.F("{0}: skjerm {1} finnes ikke. Cuen vises i et vanlig vindu.", Label(c, i), screen.Value + 1), i));
    }

    // ---------------- Cue-numre ----------------

    /// <summary>Samme nummer i ulike scener er lov; to cuer med samme nummer i én scene (eller i et show uten scener) gir feil.</summary>
    private static void CheckNumbers(IReadOnlyList<Cue> cues, List<PreflightFinding> list)
    {
        var groups = cues.Select((c, i) => (n: c.Number.Trim(), i, scene: c.Scene)).Where(x => x.n.Length > 0)
            .GroupBy(x => (x.n.ToUpperInvariant(), x.scene.ToUpperInvariant())).Where(g => g.Count() > 1);
        foreach (var g in groups)
        {
            var first = g.First();
            if (first.scene.Length == 0)
                list.Add(new(PreflightSeverity.Error, NumberDuplicate,
                    Loc.F("Cue-nummer «{0}» brukes av {1} cuer (rad {2}).", first.n, g.Count(), string.Join(", ", g.Select(x => x.i + 1))), first.i));
            else
                list.Add(new(PreflightSeverity.Error, NumberDuplicate,
                    Loc.F("Cue-nummer «{0}» brukes av {1} cuer i scenen «{2}» (rad {3}).", first.n, g.Count(), first.scene, string.Join(", ", g.Select(x => x.i + 1))), first.i));
        }
    }
}
