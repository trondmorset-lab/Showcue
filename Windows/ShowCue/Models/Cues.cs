using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace ShowCue.Models;

public enum ContinueMode { None, AutoContinue, AutoFollow }
public enum VisualStretch { Uniform, UniformToFill, Fill, None }
public enum MidiMessageType { NoteOn, NoteOff, ProgramChange, ControlChange, MscGo }
/// <summary>Fargemerking av cuer i lista.</summary>
public enum CueColor { None, Red, Orange, Yellow, Green, Blue, Purple, Gray }
/// <summary>Formen på en fade: lineær i dB (jevn for øret), S-kurve (myk start og slutt) eller lineær amplitude.</summary>
public enum FadeCurve { Db, SCurve, Linear }
/// <summary>Hvordan en nettverks-cue sender.</summary>
public enum NetworkProtocol { Udp, Tcp, Http }

/// <summary>Cuer med start- og sluttpunkt i en mediefil (lyd og video) – brukes av bølgeform-redigereren.</summary>
public interface ITrimmable : System.ComponentModel.INotifyPropertyChanged
{
    string FilePath { get; }
    double StartTime { get; set; }
    /// <summary>0 = til slutten av fila.</summary>
    double EndTime { get; set; }
    double FadeIn { get; }
    double FadeOut { get; }
    bool Loop { get; }
    /// <summary>Lengden på hele fila (0 = ukjent).</summary>
    double MediaLength { get; }
    bool IsVideo { get; }
}

/// <summary>Cuer med lyd som går gjennom lydpatchen (lyd-cuer og lydsporet i video-cuer).</summary>
public interface IRoutedAudio : System.ComponentModel.INotifyPropertyChanged
{
    int FileChannels { get; set; }
    List<AudioRoute>? Routes { get; set; }
    double VolumeDb { get; set; }
    IReadOnlyList<AudioRoute> EffectiveRoutes(int fileChannels);
}

/// <summary>Grunnklasse for alle cues. Alt som lagres i showfila er vanlige properties;
/// kjøretidsstatus er merket med JsonIgnore.</summary>
// JSON: "type" angir cuetypen (audio, video, …). Se CueJsonConverter – den leser "type" uansett
// hvor i objektet det står, så showfiler fra Mac-versjonen også kan åpnes.
public abstract class Cue : ObservableObject
{
    private string _number = "";
    private string _name = "";
    private string _notes = "";
    private double _preWait;
    private ContinueMode _continueMode;
    private string _hotkey = "";
    private bool _disabled;

    public string Number { get => _number; set => SetCue(ref _number, value?.Trim() ?? ""); }
    public string Name { get => _name; set => SetCue(ref _name, value ?? ""); }
    public string Notes { get => _notes; set => SetCue(ref _notes, value ?? ""); }
    /// <summary>Sekunder fra GO til cuen faktisk starter.</summary>
    public double PreWait { get => _preWait; set => SetCue(ref _preWait, Math.Max(0, value)); }
    public ContinueMode ContinueMode { get => _continueMode; set => SetCue(ref _continueMode, value); }
    /// <summary>Hurtigtast, f.eks. "F1", "Q" eller "1". Tom = ingen.</summary>
    public string Hotkey { get => _hotkey; set { if (SetCue(ref _hotkey, (value ?? "").Trim().ToUpperInvariant())) OnPropertyChanged(nameof(TriggerText)); } }
    /// <summary>Deaktiverte cues hoppes over av GO.</summary>
    public bool Disabled { get => _disabled; set => SetCue(ref _disabled, value); }
    private CueColor _color;
    /// <summary>Fargemerking i lista.</summary>
    public CueColor Color { get => _color; set => SetCue(ref _color, value); }
    private string _midiTrigger = "";
    /// <summary>MIDI-melding som starter cuen, f.eks. "N:60" (note), "CC:20" eller "PC:5". Tom = ingen.</summary>
    public string MidiTrigger { get => _midiTrigger; set { if (SetCue(ref _midiTrigger, value ?? "")) OnPropertyChanged(nameof(TriggerText)); } }

    private bool _inButtonPanel;
    /// <summary>Vis cuen som knapp i knappepanelet. Cuer med hurtigtast vises der uansett.</summary>
    public bool InButtonPanel { get => _inButtonPanel; set => SetCue(ref _inButtonPanel, value); }

    private string _timecodeTrigger = "";
    /// <summary>Start cuen når innkommende tidskode (MTC) passerer dette tidspunktet, «tt:mm:ss:ff». Tom = ingen.</summary>
    public string TimecodeTrigger
    {
        get => _timecodeTrigger;
        set { if (SetCue(ref _timecodeTrigger, Timecode.TryParse(value, out var tc) ? tc.ToString() : "")) OnPropertyChanged(nameof(TriggerText)); }
    }

    /// <summary>Hurtigtast, MIDI- og tidskode-trigger samlet, for visning i lista.</summary>
    [JsonIgnore] public string TriggerText => string.Join(" · ",
        new[] { Hotkey, MidiTriggers.Describe(MidiTrigger), TimecodeTrigger.Length > 0 ? "TC " + TimecodeTrigger : "" }.Where(s => !string.IsNullOrEmpty(s)));

    [JsonIgnore] public abstract string TypeName { get; }
    [JsonIgnore] public abstract string TypeIcon { get; }
    [JsonIgnore] public virtual string Summary => "";
    /// <summary>Varighet i sekunder, 0 = ukjent/uendelig.</summary>
    [JsonIgnore] public virtual double Duration => 0;
    [JsonIgnore] public string DurationText => Duration > 0 ? TimeFormat.Format(Duration) : "";
    [JsonIgnore] public string PreWaitText => PreWait > 0 ? TimeFormat.Format(PreWait) : "";
    [JsonIgnore] public string DisplayName => string.IsNullOrWhiteSpace(Name) ? DefaultName : Name;
    [JsonIgnore] protected virtual string DefaultName => TypeName;

    // ---- Kjøretidsstatus ----
    private int _activeCount;
    private double _progress;
    private bool _isPreWaiting;
    private string? _error;

    [JsonIgnore] public bool IsRunning => _activeCount > 0;
    [JsonIgnore] public int ActiveCount
    {
        get => _activeCount;
        set { if (Set(ref _activeCount, Math.Max(0, value))) OnPropertyChanged(nameof(IsRunning)); }
    }
    [JsonIgnore] public double Progress { get => _progress; set => Set(ref _progress, value); }
    [JsonIgnore] public bool IsPreWaiting { get => _isPreWaiting; set => Set(ref _isPreWaiting, value); }
    /// <summary>Siste feilmelding (f.eks. fil mangler). Vises i lista.</summary>
    [JsonIgnore] public string? Error { get => _error; set { if (Set(ref _error, value)) OnPropertyChanged(nameof(HasError)); } }
    [JsonIgnore] public bool HasError => !string.IsNullOrEmpty(_error);

    protected bool SetCue<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (!Set(ref field, value, name)) return false;
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Duration));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(PreWaitText));
        OnPropertyChanged(nameof(DisplayName));
        return true;
    }

    protected static string FileLabel(string path) =>
        string.IsNullOrWhiteSpace(path) ? Loc.T("(ingen fil)") : Path.GetFileName(path);

    /// <summary>Filnavnet uten filtype, eller typenavnet hvis ingen fil er valgt.</summary>
    protected string NameFromFile(string path) =>
        Path.GetFileNameWithoutExtension(path) is { Length: > 0 } n ? n : TypeName;

    public Cue Clone()
    {
        var json = System.Text.Json.JsonSerializer.Serialize<Cue>(this, ShowDocument.JsonOptions);
        return System.Text.Json.JsonSerializer.Deserialize<Cue>(json, ShowDocument.JsonOptions)!;
    }
}

public interface IFileCue
{
    string FilePath { get; set; }
}

// ======================================================================
//  LYD
// ======================================================================
public sealed class AudioCue : Cue, IFileCue, IRoutedAudio, ITrimmable
{
    private string _filePath = "";
    private double _volumeDb;
    private double _startTime;
    private double _endTime;
    private bool _loop;
    private double _fadeIn;
    private double _fadeOut;
    private double _fileDuration;
    private int _fileChannels;
    private List<AudioRoute>? _routes;

    public string FilePath { get => _filePath; set => SetCue(ref _filePath, value ?? ""); }
    /// <summary>Antall kanaler i lydfila. Fylles inn automatisk.</summary>
    public int FileChannels { get => _fileChannels; set => SetCue(ref _fileChannels, Math.Max(0, value)); }
    /// <summary>Routing fra filkanal til utgang (buss). null = standard (mono → 1+2, stereo → 1/2, osv.).
    /// Tom liste = alt av. Listen byttes ut i sin helhet ved endring.</summary>
    public List<AudioRoute>? Routes { get => _routes; set => SetCue(ref _routes, value); }

    /// <summary>Gjeldende routing (standard hvis ingen er satt).</summary>
    public IReadOnlyList<AudioRoute> EffectiveRoutes(int fileChannels) => _routes ?? AudioRoute.Default(fileChannels);
    /// <summary>Nivå i dB (0 = uendret, -60 eller lavere = stille).</summary>
    public double VolumeDb { get => _volumeDb; set => SetCue(ref _volumeDb, Math.Clamp(value, -60, 12)); }
    public double StartTime { get => _startTime; set => SetCue(ref _startTime, Math.Max(0, value)); }
    /// <summary>0 = spill til slutten av fila.</summary>
    public double EndTime { get => _endTime; set => SetCue(ref _endTime, Math.Max(0, value)); }
    public bool Loop { get => _loop; set => SetCue(ref _loop, value); }
    public double FadeIn { get => _fadeIn; set => SetCue(ref _fadeIn, Math.Max(0, value)); }
    /// <summary>Brukes ved stopp, og automatisk mot slutten av fila når cuen ikke looper.</summary>
    public double FadeOut { get => _fadeOut; set => SetCue(ref _fadeOut, Math.Max(0, value)); }
    /// <summary>Lengden på lydfila. Fylles inn automatisk.</summary>
    public double FileDuration { get => _fileDuration; set { if (SetCue(ref _fileDuration, value)) OnPropertyChanged(nameof(FileDurationText)); } }
    [JsonIgnore] public string FileDurationText => Loc.F("Filens lengde: {0:0.0} s", FileDuration);
    private double? _loudness;
    /// <summary>Målt lydstyrke (LUFS, integrert) mellom start og slutt, før nivået. null = ikke målt.</summary>
    public double? Loudness { get => _loudness; set { if (SetCue(ref _loudness, value)) OnPropertyChanged(nameof(LoudnessText)); } }
    [JsonIgnore] public string LoudnessText => Loudness is double l ? Loc.F("{0:0.0} LUFS (målt)", l) : Loc.T("Ikke målt");
    [JsonIgnore] public double MediaLength => FileDuration;
    [JsonIgnore] public bool IsVideo => false;

    public override string TypeName => Loc.T("Lyd");
    public override string TypeIcon => "♪";
    protected override string DefaultName => NameFromFile(FilePath);
    public override string Summary => FileLabel(FilePath) + (Loop ? "  ⟲" : "") + (VolumeDb != 0 ? $"  {VolumeDb:+0.#;-0.#} dB" : "")
        + (Routes != null ? "  → " + RouteSummary : "");

    private string RouteSummary
    {
        get
        {
            var buses = Routes!.Select(r => r.Bus).Distinct().OrderBy(b => b).ToList();
            return buses.Count == 0 ? Loc.T("av") : Loc.T("ut ") + string.Join(",", buses);
        }
    }
    public override double Duration
    {
        get
        {
            if (Loop) return 0;
            var end = EndTime > 0 ? Math.Min(EndTime, FileDuration > 0 ? FileDuration : EndTime) : FileDuration;
            return Math.Max(0, end - StartTime);
        }
    }
}

// ======================================================================
//  VISUELT (video, bilde, PDF)
// ======================================================================
public abstract class VisualCue : Cue, IFileCue
{
    private string _filePath = "";
    private double _fadeIn = 0.5;
    private double _fadeOut = 0.5;
    private bool _replaceOthers = true;
    private VisualStretch _stretch = VisualStretch.Uniform;

    public string FilePath { get => _filePath; set => SetCue(ref _filePath, value ?? ""); }
    public double FadeIn { get => _fadeIn; set => SetCue(ref _fadeIn, Math.Max(0, value)); }
    public double FadeOut { get => _fadeOut; set => SetCue(ref _fadeOut, Math.Max(0, value)); }
    /// <summary>Når cuen starter, fades andre bilder/videoer ut (krysstoning).</summary>
    public bool ReplaceOthers { get => _replaceOthers; set => SetCue(ref _replaceOthers, value); }
    public VisualStretch Stretch { get => _stretch; set => SetCue(ref _stretch, value); }

    protected override string DefaultName => NameFromFile(FilePath);
}

public sealed class VideoCue : VisualCue, IRoutedAudio, ITrimmable
{
    private double _startTime;
    private double _endTime;
    private double _volumeDb;
    private bool _loop;
    private double _knownDuration;
    private int _fileChannels = -1;
    private List<AudioRoute>? _routes;

    /// <summary>Nivå på videoens lydspor i dB. Lyden går gjennom lydpatchen akkurat som en lyd-cue.</summary>
    public double VolumeDb { get => _volumeDb; set => SetCue(ref _volumeDb, Math.Clamp(value, -60, 12)); }

    /// <summary>Eldre showfiler (0.1/0.2) lagret nivå i prosent. Leses inn og gjøres om til dB; skrives aldri.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Volume
    {
        get => null;
        set { if (value is double v) VolumeDb = v <= 0.1 ? -60 : 20 * Math.Log10(Math.Min(v, 100) / 100.0); }
    }

    /// <summary>Antall kanaler i lydsporet. 0 = ingen lyd, -1 = ikke undersøkt ennå.</summary>
    public int FileChannels { get => _fileChannels; set { if (SetCue(ref _fileChannels, value)) OnPropertyChanged(nameof(HasNoAudio)); } }
    /// <summary>Routing fra lydsporets kanaler til utganger. null = standard.</summary>
    public List<AudioRoute>? Routes { get => _routes; set => SetCue(ref _routes, value); }
    public IReadOnlyList<AudioRoute> EffectiveRoutes(int fileChannels) => _routes ?? AudioRoute.Default(fileChannels);
    [JsonIgnore] public bool HasNoAudio => _fileChannels == 0;
    public bool Loop { get => _loop; set => SetCue(ref _loop, value); }
    /// <summary>Fylles inn første gang videoen åpnes.</summary>
    public double KnownDuration { get => _knownDuration; set => SetCue(ref _knownDuration, value); }
    /// <summary>Startpunkt i videoen (sekunder).</summary>
    public double StartTime { get => _startTime; set => SetCue(ref _startTime, Math.Max(0, value)); }
    /// <summary>Sluttpunkt i videoen (sekunder). 0 = til slutten.</summary>
    public double EndTime { get => _endTime; set => SetCue(ref _endTime, Math.Max(0, value)); }
    [JsonIgnore] public double MediaLength => KnownDuration;
    [JsonIgnore] public bool IsVideo => true;
    /// <summary>Faktisk sluttpunkt (sekunder), 0 = ukjent.</summary>
    [JsonIgnore] public double EffectiveEnd => EndTime > 0 ? (KnownDuration > 0 ? Math.Min(EndTime, KnownDuration) : EndTime) : KnownDuration;

    public override string TypeName => "Video";
    public override string TypeIcon => "▶";
    public override string Summary => FileLabel(FilePath) + (Loop ? "  ⟲" : "")
        + (FileChannels == 0 ? Loc.T("  (ingen lyd)") : Routes is { Count: 0 } ? Loc.T("  (lyd av)") : VolumeDb != 0 ? $"  {VolumeDb:+0.#;-0.#} dB" : "");
    public override double Duration => Loop || EffectiveEnd <= 0 ? 0 : Math.Max(0, EffectiveEnd - StartTime);
}

public abstract class StillCue : VisualCue
{
    private double _holdTime;
    /// <summary>Sekunder bildet står før det fades ut. 0 = til det stoppes.</summary>
    public double HoldTime { get => _holdTime; set => SetCue(ref _holdTime, Math.Max(0, value)); }
    public override double Duration => HoldTime > 0 ? FadeIn + HoldTime + FadeOut : 0;
}

public sealed class ImageCue : StillCue
{
    public override string TypeName => Loc.T("Bilde");
    public override string TypeIcon => "▣";
    public override string Summary => FileLabel(FilePath);
}

public sealed class PdfCue : StillCue
{
    private int _page = 1;
    /// <summary>Sidenummer, starter på 1.</summary>
    public int Page { get => _page; set => SetCue(ref _page, Math.Max(1, value)); }

    public override string TypeName => "PDF";
    public override string TypeIcon => "▤";
    public override string Summary => Loc.F("{0}  s. {1}", FileLabel(FilePath), Page);
    protected override string DefaultName => base.DefaultName + Loc.F(" s.{0}", Page);
}

/// <summary>Nettside på utgangen (f.eks. en klokke, resultatliste eller presentasjon i nettleseren).</summary>
public sealed class WebCue : StillCue
{
    private string _url = "https://";

    public string Url { get => _url; set => SetCue(ref _url, value?.Trim() ?? ""); }

    /// <summary>Adressen som en gyldig http(s)- eller fil-adresse, ellers null. «www.x.no» får https:// foran.</summary>
    [JsonIgnore] public Uri? ValidUri
    {
        get
        {
            var u = Url;
            if (u.Length > 0 && !u.Contains("://") && File.Exists(u)) return new Uri(Path.GetFullPath(u));
            if (u.Length > 0 && !u.Contains("://")) u = "https://" + u;
            return Uri.TryCreate(u, UriKind.Absolute, out var uri) && (uri.Scheme is "http" or "https" or "file") && (uri.IsFile || uri.Host.Length > 0) ? uri : null;
        }
    }

    public override string TypeName => Loc.T("Nettside");
    public override string TypeIcon => "◍";
    protected override string DefaultName => ValidUri is { IsFile: false } u ? u.Host : Loc.T("Nettside");
    public override string Summary => Url;
}

// ======================================================================
//  KONTROLL
// ======================================================================
public sealed class FadeCue : Cue
{
    private string _target = "";
    private double _fadeTime = 3;
    private bool _fadeAudio = true;
    private double _targetDb = -60;
    private bool _fadeVisual = true;
    private double _targetOpacity;
    private bool _stopWhenDone = true;
    private FadeCurve _curve;

    /// <summary>Cue-nummeret som skal fades.</summary>
    public string Target { get => _target; set => SetCue(ref _target, value?.Trim() ?? ""); }
    public double FadeTime { get => _fadeTime; set => SetCue(ref _fadeTime, Math.Max(0, value)); }
    public bool FadeAudio { get => _fadeAudio; set => SetCue(ref _fadeAudio, value); }
    public double TargetDb { get => _targetDb; set => SetCue(ref _targetDb, Math.Clamp(value, -60, 12)); }
    public bool FadeVisual { get => _fadeVisual; set => SetCue(ref _fadeVisual, value); }
    /// <summary>Synlighet 0–100 %.</summary>
    public double TargetOpacity { get => _targetOpacity; set => SetCue(ref _targetOpacity, Math.Clamp(value, 0, 100)); }
    public bool StopWhenDone { get => _stopWhenDone; set => SetCue(ref _stopWhenDone, value); }
    /// <summary>Formen på lydfaden.</summary>
    public FadeCurve Curve { get => _curve; set => SetCue(ref _curve, value); }

    public override string TypeName => "Fade";
    public override string TypeIcon => "◢";
    protected override string DefaultName => string.IsNullOrEmpty(Target) ? "Fade" : Loc.F("Fade cue {0}", Target);
    public override string Summary
    {
        get
        {
            var t = string.IsNullOrEmpty(Target) ? Loc.T("(mål mangler)") : "→ " + Target;
            var lvl = FadeAudio ? (TargetDb <= -60 ? "  −∞ dB" : $"  {TargetDb:0.#} dB") : "";
            return t + lvl + (StopWhenDone ? Loc.T("  + stopp") : "");
        }
    }
    public override double Duration => FadeTime;
}

public sealed class StopCue : Cue
{
    private string _target = "";
    private double _fadeTime;
    private bool _release;

    /// <summary>Cue-nummeret som skal stoppes. Tomt = stopp alt.</summary>
    public string Target { get => _target; set => SetCue(ref _target, value?.Trim() ?? ""); }
    public double FadeTime { get => _fadeTime; set => SetCue(ref _fadeTime, Math.Max(0, value)); }
    /// <summary>Slipp loopen i stedet for å stoppe («devamp»): runden fullføres og resten av fila spilles.
    /// For spillelister: spill ferdig sporet som går, og stopp.</summary>
    public bool Release { get => _release; set => SetCue(ref _release, value); }

    public override string TypeName => Loc.T("Stopp");
    public override string TypeIcon => "■";
    protected override string DefaultName => Release ? Loc.F("Slipp loop {0}", Target).TrimEnd() : string.IsNullOrEmpty(Target) ? Loc.T("Stopp alt") : Loc.F("Stopp cue {0}", Target);
    public override string Summary => (string.IsNullOrEmpty(Target) ? Loc.T("Alle cues") : "→ " + Target)
        + (Release ? Loc.T("  slipp loop") : FadeTime > 0 ? Loc.F("  fade {0:0.#}s", FadeTime) : "");
    public override double Duration => Release ? 0 : FadeTime;
}

/// <summary>Flytter GO-markøren til en annen cue (og kjører den, hvis valgt). For hopp og repetisjoner.</summary>
public sealed class GotoCue : Cue
{
    private string _target = "";
    private bool _fire;

    public string Target { get => _target; set => SetCue(ref _target, value?.Trim() ?? ""); }
    /// <summary>Kjør mål-cuen også (som GO), ikke bare flytt markøren.</summary>
    public bool Fire { get => _fire; set => SetCue(ref _fire, value); }

    public override string TypeName => Loc.T("Gå til");
    public override string TypeIcon => "↪";
    protected override string DefaultName => string.IsNullOrEmpty(Target) ? Loc.T("Gå til") : Loc.F("Gå til {0}", Target);
    public override string Summary => (string.IsNullOrEmpty(Target) ? Loc.T("(mål mangler)") : "→ " + Target) + (Fire ? "  + GO" : "");
}

// ======================================================================
//  SPILLELISTE
// ======================================================================
/// <summary>Flere lydfiler etter hverandre (pausemusikk, innslipp): i rekkefølge eller tilfeldig, med krysstoning.</summary>
public sealed class PlaylistCue : Cue, IRoutedAudio
{
    private List<string> _files = new();
    private bool _shuffle;
    private bool _loop = true;
    private double _crossfade = 2;
    private double _volumeDb;
    private double _fadeOut = 3;
    private int _fileChannels = 2;
    private List<AudioRoute>? _routes;

    /// <summary>Filene i spillelista. Listen byttes ut i sin helhet ved endring.</summary>
    public List<string> Files { get => _files; set => SetCue(ref _files, value ?? new()); }
    public bool Shuffle { get => _shuffle; set => SetCue(ref _shuffle, value); }
    /// <summary>Start på nytt når lista er ferdig.</summary>
    public bool Loop { get => _loop; set => SetCue(ref _loop, value); }
    /// <summary>Krysstoning mellom sporene (sekunder).</summary>
    public double Crossfade { get => _crossfade; set => SetCue(ref _crossfade, Math.Clamp(value, 0, 30)); }
    public double VolumeDb { get => _volumeDb; set => SetCue(ref _volumeDb, Math.Clamp(value, -60, 12)); }
    /// <summary>Fade ut når spillelista stoppes.</summary>
    public double FadeOut { get => _fadeOut; set => SetCue(ref _fadeOut, Math.Max(0, value)); }
    /// <summary>Antall kanaler i routingmatrisen (det største antallet blant filene).</summary>
    public int FileChannels { get => _fileChannels; set => SetCue(ref _fileChannels, Math.Max(1, value)); }
    public List<AudioRoute>? Routes { get => _routes; set => SetCue(ref _routes, value); }
    public IReadOnlyList<AudioRoute> EffectiveRoutes(int fileChannels) => _routes ?? AudioRoute.Default(fileChannels);

    public override string TypeName => Loc.T("Spilleliste");
    public override string TypeIcon => "☰";
    public override string Summary => Loc.F("{0} spor", Files.Count) + (Shuffle ? Loc.T("  tilfeldig") : "") + (Loop ? "  ⟲" : "")
        + (Crossfade > 0 ? Loc.F("  krysstoning {0:0.#}s", Crossfade) : "");
}

// ======================================================================
//  MEMO OG NETTVERK
// ======================================================================
/// <summary>Beskjed til operatøren (f.eks. «Vent på applaus»). Vises stort når cuen kjøres, gjør ellers ingenting.</summary>
public sealed class MemoCue : Cue
{
    private string _text = "";

    public string Text { get => _text; set => SetCue(ref _text, value ?? ""); }

    public override string TypeName => Loc.T("Beskjed");
    public override string TypeIcon => "✎";
    protected override string DefaultName => FirstLine is { Length: > 0 } l ? l : Loc.T("Beskjed");
    public override string Summary => FirstLine;
    private string FirstLine => Text.Split('\n', 2)[0].Trim();
}

/// <summary>Sender tekst over UDP eller TCP, eller en HTTP-forespørsel (styring av projektorer, mediaservere, PTZ-kamera …).</summary>
public sealed class NetworkCue : Cue
{
    private NetworkProtocol _protocol = NetworkProtocol.Udp;
    private string _host = "127.0.0.1";
    private int _port = 9000;
    private string _message = "";
    private string _url = "http://";
    private string _method = "GET";

    public NetworkProtocol Protocol { get => _protocol; set => SetCue(ref _protocol, value); }
    public string Host { get => _host; set => SetCue(ref _host, value?.Trim() ?? ""); }
    public int Port { get => _port; set => SetCue(ref _port, Math.Clamp(value, 1, 65535)); }
    /// <summary>Teksten som sendes (UDP/TCP) eller innholdet i forespørselen (HTTP). \r \n \t \\ og \xHH tolkes.</summary>
    public string Message { get => _message; set => SetCue(ref _message, value ?? ""); }
    public string Url { get => _url; set => SetCue(ref _url, value?.Trim() ?? ""); }
    /// <summary>GET, POST, PUT eller DELETE.</summary>
    public string Method { get => _method; set => SetCue(ref _method, string.IsNullOrWhiteSpace(value) ? "GET" : value.Trim().ToUpperInvariant()); }

    public override string TypeName => Loc.T("Nettverk");
    public override string TypeIcon => "⇄";
    protected override string DefaultName => Protocol == NetworkProtocol.Http ? $"HTTP {Method}" : $"{Protocol.ToString().ToUpperInvariant()} {Host}";
    public override string Summary => Protocol == NetworkProtocol.Http
        ? $"{Method} {Url}"
        : $"{Protocol.ToString().ToUpperInvariant()} {Host}:{Port}  {Message}".TrimEnd();
}

// ======================================================================
//  TIDSKODE, LYS OG LIVE-INNGANG
// ======================================================================
/// <summary>Sender tidskode (MTC til en MIDI-utgang og/eller LTC som lyd på en utgang) fra et starttidspunkt.</summary>
public sealed class TimecodeCue : Cue
{
    private string _start = "01:00:00:00";
    private TimecodeRate _rate = TimecodeRate.Fps25;
    private bool _sendMtc = true;
    private string _device = "";
    private bool _sendLtc;
    private int _ltcBus = 1;
    private double _ltcLevelDb = -12;
    private double _length;

    public string Start { get => _start; set => SetCue(ref _start, Timecode.TryParse(value, out var tc) ? tc.ToString() : _start); }
    public TimecodeRate Rate { get => _rate; set => SetCue(ref _rate, value); }
    public bool SendMtc { get => _sendMtc; set => SetCue(ref _sendMtc, value); }
    /// <summary>MIDI-utgang for MTC (tom = første).</summary>
    public string Device { get => _device; set => SetCue(ref _device, value ?? ""); }
    public bool SendLtc { get => _sendLtc; set => SetCue(ref _sendLtc, value); }
    /// <summary>Utgang i lydpatchen for LTC (fra 1).</summary>
    public int LtcBus { get => _ltcBus; set => SetCue(ref _ltcBus, Math.Clamp(value, 1, 64)); }
    public double LtcLevelDb { get => _ltcLevelDb; set => SetCue(ref _ltcLevelDb, Math.Clamp(value, -40, 0)); }
    /// <summary>Hvor lenge tidskoden går (sekunder). 0 = til cuen stoppes.</summary>
    public double Length { get => _length; set => SetCue(ref _length, Math.Max(0, value)); }

    [JsonIgnore] public Timecode StartTimecode => Timecode.TryParse(Start, out var tc) ? tc : default;

    public override string TypeName => Loc.T("Tidskode");
    public override string TypeIcon => "⏱";
    protected override string DefaultName => Loc.T("Tidskode ") + Start;
    public override string Summary => Loc.F("{0} @ {1} fps", Start, Timecode.Label(Rate).Split(' ')[0])
        + (SendMtc ? "  MTC" : "") + (SendLtc ? Loc.F("  LTC → ut {0}", LtcBus) : "");
    public override double Duration => Length;
}

/// <summary>Lys: fader DMX-kanaler i et Art-Net-univers til nye nivåer.</summary>
public sealed class LightCue : Cue
{
    private string _host = "255.255.255.255";
    private int _universe;
    private string _levels = "1@255";
    private double _fadeTime = 2;

    /// <summary>Art-Net-node eller kringkasting (255.255.255.255, 2.255.255.255 …).</summary>
    public string Host { get => _host; set => SetCue(ref _host, value?.Trim() ?? ""); }
    /// <summary>Art-Net-univers (0 = det første).</summary>
    public int Universe { get => _universe; set => SetCue(ref _universe, Math.Clamp(value, 0, 32767)); }
    /// <summary>«kanal@verdi», f.eks. «1@255 2-4@50% 10@0».</summary>
    public string Levels { get => _levels; set => SetCue(ref _levels, value ?? ""); }
    public double FadeTime { get => _fadeTime; set => SetCue(ref _fadeTime, Math.Clamp(value, 0, 600)); }

    public override string TypeName => Loc.T("Lys");
    public override string TypeIcon => "✺";
    public override string Summary => Loc.F("Univers {0}  {1}", Universe, DmxLevels.Summary(Levels)) + (FadeTime > 0 ? Loc.F("  fade {0:0.#}s", FadeTime) : "");
    public override double Duration => FadeTime;
}

/// <summary>Live-inngang (mikrofon, linje) gjennom lydpatchen, med nivå, fades og routing.</summary>
public sealed class InputCue : Cue, IRoutedAudio
{
    private int _fileChannels = 2;
    private List<AudioRoute>? _routes;
    private double _volumeDb;
    private double _fadeIn = 0.5;
    private double _fadeOut = 1;

    /// <summary>Antall kanaler fra inngangen (settes fra lydenheten).</summary>
    public int FileChannels { get => _fileChannels; set => SetCue(ref _fileChannels, Math.Clamp(value, 1, 64)); }
    public List<AudioRoute>? Routes { get => _routes; set => SetCue(ref _routes, value); }
    public IReadOnlyList<AudioRoute> EffectiveRoutes(int fileChannels) => _routes ?? AudioRoute.Default(fileChannels);
    public double VolumeDb { get => _volumeDb; set => SetCue(ref _volumeDb, Math.Clamp(value, -60, 12)); }
    public double FadeIn { get => _fadeIn; set => SetCue(ref _fadeIn, Math.Max(0, value)); }
    public double FadeOut { get => _fadeOut; set => SetCue(ref _fadeOut, Math.Max(0, value)); }

    public override string TypeName => Loc.T("Inngang");
    public override string TypeIcon => "◉";
    protected override string DefaultName => Loc.T("Live-inngang");
    public override string Summary => Loc.F("{0} kanal(er)", FileChannels) + (VolumeDb != 0 ? $"  {VolumeDb:+0.#;-0.#} dB" : "")
        + (Routes != null ? Loc.T("  → ut ") + string.Join(",", Routes.Select(r => r.Bus).Distinct().OrderBy(b => b)) : "");
}

public sealed class OscCue : Cue
{
    private string _host = "127.0.0.1";
    private int _port = 8000;
    private string _address = "/";
    private string _arguments = "";

    public string Host { get => _host; set => SetCue(ref _host, value?.Trim() ?? ""); }
    public int Port { get => _port; set => SetCue(ref _port, Math.Clamp(value, 1, 65535)); }
    public string Address { get => _address; set => SetCue(ref _address, value?.Trim() ?? "/"); }
    /// <summary>Mellomromsseparert: 1 = int, 0.5 = float, tekst eller "tekst med mellomrom" = string.</summary>
    public string Arguments { get => _arguments; set => SetCue(ref _arguments, value ?? ""); }

    public override string TypeName => "OSC";
    public override string TypeIcon => "⇢";
    protected override string DefaultName => "OSC " + Address;
    public override string Summary => $"{Host}:{Port}  {Address} {Arguments}".TrimEnd();
}

public sealed class MidiCue : Cue
{
    private string _device = "";
    private MidiMessageType _messageType = MidiMessageType.NoteOn;
    private int _channel = 1;
    private int _data1 = 60;
    private int _data2 = 127;
    private int _mscDeviceId = 127;
    private int _mscCommandFormat = 1;
    private string _mscCueNumber = "1";

    public string Device { get => _device; set => SetCue(ref _device, value ?? ""); }
    public MidiMessageType MessageType { get => _messageType; set => SetCue(ref _messageType, value); }
    public int Channel { get => _channel; set => SetCue(ref _channel, Math.Clamp(value, 1, 16)); }
    /// <summary>Note / program / controller.</summary>
    public int Data1 { get => _data1; set => SetCue(ref _data1, Math.Clamp(value, 0, 127)); }
    /// <summary>Velocity / verdi.</summary>
    public int Data2 { get => _data2; set => SetCue(ref _data2, Math.Clamp(value, 0, 127)); }
    /// <summary>MSC device ID (127 = alle).</summary>
    public int MscDeviceId { get => _mscDeviceId; set => SetCue(ref _mscDeviceId, Math.Clamp(value, 0, 127)); }
    /// <summary>MSC command format (1 = lys, 0x10 = lyd …).</summary>
    public int MscCommandFormat { get => _mscCommandFormat; set => SetCue(ref _mscCommandFormat, Math.Clamp(value, 0, 127)); }
    public string MscCueNumber { get => _mscCueNumber; set => SetCue(ref _mscCueNumber, value?.Trim() ?? ""); }

    public override string TypeName => "MIDI";
    public override string TypeIcon => "♫";
    public override string Summary => MessageType switch
    {
        MidiMessageType.NoteOn => Loc.F("Note on  ch{0}  {1} / {2}", Channel, Data1, Data2),
        MidiMessageType.NoteOff => Loc.F("Note off  ch{0}  {1}", Channel, Data1),
        MidiMessageType.ProgramChange => Loc.F("Program  ch{0}  {1}", Channel, Data1),
        MidiMessageType.ControlChange => $"CC  ch{Channel}  {Data1} = {Data2}",
        MidiMessageType.MscGo => Loc.F("MSC GO  cue {0}", MscCueNumber),
        _ => ""
    } + (string.IsNullOrEmpty(Device) ? "" : $"  [{Device}]");
}

/// <summary>Ett krysspunkt i en lyd-cues routingmatrise.</summary>
public sealed class AudioRoute
{
    /// <summary>Kanal i lydfila, fra 1.</summary>
    public int Source { get; set; }
    /// <summary>Utgang/buss i lydpatchen, fra 1.</summary>
    public int Bus { get; set; }
    public double GainDb { get; set; }

    public AudioRoute() { }
    public AudioRoute(int source, int bus, double gainDb = 0) { Source = source; Bus = bus; GainDb = gainDb; }

    /// <summary>Mono → utgang 1 og 2. Ellers kanal n → utgang n.</summary>
    public static List<AudioRoute> Default(int fileChannels)
    {
        if (fileChannels <= 1) return new() { new(1, 1), new(1, 2) };
        return Enumerable.Range(1, fileChannels).Select(c => new AudioRoute(c, c)).ToList();
    }

    /// <summary>Alt til ett stereopar (f.eks. hodetelefoner): mono → begge, ellers annenhver kanal venstre/høyre.
    /// Flerkanals filer dempes 3 dB så summen ikke blir for sterk.</summary>
    public static List<AudioRoute> Downmix(int fileChannels, int leftBus)
    {
        if (fileChannels <= 1) return new() { new(1, leftBus), new(1, leftBus + 1) };
        double gain = fileChannels > 2 ? -3 : 0;
        return Enumerable.Range(1, fileChannels).Select(c => new AudioRoute(c, leftBus + (c - 1) % 2, gain)).ToList();
    }
}

/// <summary>MIDI-triggere lagres som tekst: "N:60", "CC:20", "PC:5".</summary>
public static class MidiTriggers
{
    public static string Describe(string trigger)
    {
        if (string.IsNullOrEmpty(trigger)) return "";
        var p = trigger.Split(':');
        if (p.Length != 2) return trigger;
        return p[0] switch
        {
            "N" => $"Note {p[1]}",
            "CC" => $"CC {p[1]}",
            "PC" => $"Prog {p[1]}",
            _ => trigger
        };
    }
}

public static class TimeFormat
{
    public static string Format(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return "";
        var neg = seconds < 0;
        seconds = Math.Abs(seconds);
        var ts = TimeSpan.FromSeconds(seconds);
        var s = ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}"
            : $"{ts.Minutes:00}:{ts.Seconds:00}.{ts.Milliseconds / 100}";
        return neg ? "-" + s : s;
    }
}
