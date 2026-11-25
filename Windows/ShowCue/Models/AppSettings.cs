using System.IO;
using System.Text.Json;

namespace ShowCue.Models;

public enum AudioDriverType { Wasapi, Asio }
public enum AppTheme { Dark, Light, Night, Gray }
public enum OscFeedbackFormat { Companion, Generic }

/// <summary>En logisk utgang i lydpatchen.</summary>
public sealed class PatchBus
{
    public string Name { get; set; } = "";
    public double GainDb { get; set; }
    /// <summary>Kanaler på lydkortet (fra 1) bussen sendes til.</summary>
    public List<int> Channels { get; set; } = new();

    public PatchBus Copy() => new() { Name = Name, GainDb = GainDb, Channels = new(Channels) };

    public static List<PatchBus> DefaultPatch(int count)
    {
        var list = new List<PatchBus>();
        for (int i = 1; i <= count; i++)
            list.Add(new PatchBus { Name = i switch { 1 => "Main L", 2 => "Main R", _ => Loc.F("Utgang {0}", i) }, Channels = { i } });
        return list;
    }
}

/// <summary>Maskininnstillinger (lagres i %AppData%\ShowCue, ikke i showfila).</summary>
public sealed class AppSettings
{
    public AudioDriverType AudioDriver { get; set; } = AudioDriverType.Wasapi;
    /// <summary>WASAPI-enhetens ID. Tom = Windows standardenhet.</summary>
    public string WasapiDeviceId { get; set; } = "";
    public string AsioDriverName { get; set; } = "";
    /// <summary>Maks antall ASIO-kanaler som åpnes (nettverkslydkort kan ha 64 eller flere).</summary>
    public int AsioMaxChannels { get; set; } = 64;
    /// <summary>Hent live-innganger fra ASIO-driveren (samme økt som utgangen). Av som standard til det er prøvd på ekte utstyr;
    /// da brukes opptaksenheten (WASAPI) som før.</summary>
    public bool AsioInputEnabled { get; set; }
    /// <summary>Første ASIO-inngang som brukes, talt fra 1.</summary>
    public int AsioInputFirstChannel { get; set; } = 1;
    /// <summary>Antall ASIO-innganger som hentes fra og med første.</summary>
    public int AsioInputChannels { get; set; } = 2;
    /// <summary>Ønsket samplingsfrekvens. For WASAPI brukes enhetens egen frekvens.</summary>
    public int SampleRate { get; set; } = 48000;

    /// <summary>Lydpatch: logiske utganger (busser) som lyd-cues routes til, og hvilke kanaler på lydkortet de går til.</summary>
    public List<PatchBus> Buses { get; set; } = PatchBus.DefaultPatch(8);

    // ---- OSC inn ----
    public bool OscInEnabled { get; set; } = true;
    /// <summary>Standard port for OSC inn (8000, vanlig standard for OSC-kontrollere, f.eks. Companion).</summary>
    public int OscInPort { get; set; } = 8000;

    // ---- Nettside (fjernstyring fra mobil/nettbrett) ----
    /// <summary>Av som standard. Når den er på, krever alt unntatt innloggingssiden PIN-koden.</summary>
    public bool WebRemoteEnabled { get; set; }
    public int WebRemotePort { get; set; } = WebRemote.DefaultPort;
    /// <summary>PIN-kode (6–32 bokstaver/siffer). Lages tilfeldig når den mangler eller er for lett å gjette.</summary>
    public string WebRemotePin { get; set; } = "";

    // ---- OSC-status ut (til kontrollpaneler) ----
    public bool OscFeedbackEnabled { get; set; }
    public string OscFeedbackHost { get; set; } = "127.0.0.1";
    /// <summary>Standard port for status ut (12321, f.eks. Companion).</summary>
    public int OscFeedbackPort { get; set; } = 12321;
    public OscFeedbackFormat OscFeedbackFormat { get; set; } = OscFeedbackFormat.Companion;

    // ---- Utseende ----
    /// <summary>Språk i brukerflaten: «nb», «en» eller tom (systemets språk). Tar effekt ved neste oppstart.</summary>
    public string Language { get; set; } = "";
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    /// <summary>Størrelse på hele hovedvinduet (1 = vanlig), se <see cref="UiZoom"/>.</summary>
    public double Zoom { get; set; } = 1.0;
    /// <summary>Ekstra tekststørrelse i cuelista, i tillegg til zoom (1 = vanlig).</summary>
    public double ListTextScale { get; set; } = 1.0;

    // ---- MIDI inn ----
    /// <summary>Navn på MIDI-inngang. Tom = av.</summary>
    public string MidiInDevice { get; set; } = "";
    /// <summary>0 = alle kanaler, ellers 1–16.</summary>
    public int MidiInChannel { get; set; }
    public string MidiGo { get; set; } = "";
    public string MidiStopAll { get; set; } = "";
    public string MidiNext { get; set; } = "";
    public string MidiPrevious { get; set; } = "";
    /// <summary>Ta imot MIDI Show Control (GO/STOP med cuenummer).</summary>
    public bool MscInEnabled { get; set; } = true;
    /// <summary>MSC device ID vi svarer på (127 = alle).</summary>
    public int MscInDeviceId { get; set; } = 127;
    /// <summary>Start cuer med tidskode-trigger fra innkommende MTC (på MIDI-inngangen).</summary>
    public bool MtcInEnabled { get; set; }
    /// <summary>Start cuer med klokketrigger når veggklokka passerer klokkeslettet. Av som standard, så ingenting starter uventet.</summary>
    public bool ClockTriggersEnabled { get; set; }
    /// <summary>Lydenhet for live-inngang-cuer (WASAPI-opptaksenhet, tom = Windows standard).</summary>
    public string InputDeviceId { get; set; } = "";
    public int LatencyMs { get; set; } = 60;

    // ---- Forhåndslytting (bølgeformen) ----
    /// <summary>Egen lydenhet for forhåndslytting, f.eks. hodetelefonutgangen (WASAPI-ID). Tom = ingen.</summary>
    public string PreviewDeviceId { get; set; } = "";
    /// <summary>Første av to utganger i lydpatchen for forhåndslytting. 0 = cuens egne utganger (salen).</summary>
    public int PreviewBus { get; set; }

    /// <summary>-1 = eget vindu (for testing på én skjerm), ellers skjermindeks.</summary>
    public int VideoScreen { get; set; } = -1;
    /// <summary>Mål for «Normaliser lydstyrke» (LUFS). −23 er EBU R128.</summary>
    public double LoudnessTarget { get; set; } = -23;
    /// <summary>Antall kolonner i knappepanelet.</summary>
    public int ButtonPanelColumns { get; set; } = 4;

    public double PanicFadeTime { get; set; } = 1.0;
    /// <summary>Hindrer dobbel-GO ved prell på tasten.</summary>
    public int DoubleGoProtectionMs { get; set; } = 300;

    public string LastShowPath { get; set; } = "";

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShowCue", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return Normalize(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), ShowDocument.JsonOptions) ?? new());
        }
        catch { /* ødelagt fil → standardverdier */ }
        return new AppSettings();
    }

    /// <summary>Retter innstillinger fra en ødelagt eller håndredigert fil: tall utenfor grensene (samme som i innstillingsvinduet)
    /// settes til nærmeste grense, og patchen får ingen tomme oppføringer.</summary>
    public static AppSettings Normalize(AppSettings s)
    {
        // En tom eller manglende patch (f.eks. fra eldre versjon) gir standardpatch.
        if (s.Buses != null && s.Buses.Contains(null!)) s.Buses = s.Buses.Where(b => b != null).ToList();
        if (s.Buses == null || s.Buses.Count == 0) s.Buses = PatchBus.DefaultPatch(8);
        foreach (var b in s.Buses)
        {
            b.Name ??= "";
            b.GainDb = double.IsFinite(b.GainDb) ? Math.Clamp(b.GainDb, -60, 12) : 0;
            b.Channels = (b.Channels ?? new()).Where(c => c is >= 1 and <= 512).ToList();
        }
        s.AsioMaxChannels = Math.Clamp(s.AsioMaxChannels, 2, 128);
        s.SampleRate = Math.Clamp(s.SampleRate, 8000, 192000);
        s.OscInPort = Math.Clamp(s.OscInPort, 1, 65535);
        s.OscFeedbackPort = Math.Clamp(s.OscFeedbackPort, 1, 65535);
        s.WebRemotePort = s.WebRemotePort is >= 1024 and <= 65535 ? s.WebRemotePort : WebRemote.DefaultPort;
        s.WebRemotePin = WebRemote.NormalizePin(s.WebRemotePin, s.WebRemoteEnabled);
        s.MidiInChannel = Math.Clamp(s.MidiInChannel, 0, 16);
        s.MscInDeviceId = Math.Clamp(s.MscInDeviceId, 0, 127);
        s.LatencyMs = Math.Clamp(s.LatencyMs, 10, 500);
        s.PreviewBus = Math.Clamp(s.PreviewBus, 0, 128);
        s.VideoScreen = Math.Clamp(s.VideoScreen, -1, 15);
        s.LoudnessTarget = double.IsFinite(s.LoudnessTarget) ? Math.Clamp(s.LoudnessTarget, -40, -5) : -23;
        s.ButtonPanelColumns = Math.Clamp(s.ButtonPanelColumns, 2, 8);
        s.PanicFadeTime = double.IsFinite(s.PanicFadeTime) ? Math.Clamp(s.PanicFadeTime, 0, 30) : 1;
        s.DoubleGoProtectionMs = Math.Clamp(s.DoubleGoProtectionMs, 0, 2000);
        s.AsioInputFirstChannel = ShowCue.Engine.AsioInputPlanner.ClampFirst(s.AsioInputFirstChannel);
        s.AsioInputChannels = ShowCue.Engine.AsioInputPlanner.ClampCount(s.AsioInputChannels);
        s.Zoom = UiZoom.Clamp(s.Zoom);
        s.ListTextScale = UiZoom.ClampList(s.ListTextScale);
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, ShowDocument.JsonOptions));
        }
        catch { }
    }

    public AppSettings Copy() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, ShowDocument.JsonOptions), ShowDocument.JsonOptions)!;
}

/// <summary>Trinn for zoom av hovedvinduet og tekststørrelse i cuelista.</summary>
public static class UiZoom
{
    public static readonly double[] Steps = { 0.8, 0.9, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0 };
    public static readonly (string Label, double Scale)[] ListSizes = { (Loc.T("Vanlig"), 1.0), (Loc.T("Stor"), 1.25), (Loc.T("Ekstra stor"), 1.5) };

    public static double Clamp(double z) => double.IsFinite(z) ? Math.Clamp(z, Steps[0], Steps[^1]) : 1;
    public static double ClampList(double s) => double.IsFinite(s) ? Math.Clamp(s, 1, 2) : 1;

    /// <summary>Neste trinn opp (<paramref name="direction"/> &gt; 0) eller ned fra <paramref name="z"/>.</summary>
    public static double Step(double z, int direction) => direction > 0
        ? Steps.FirstOrDefault(s => s > z + 0.001, Steps[^1])
        : Steps.LastOrDefault(s => s < z - 0.001, Steps[0]);

    public static string Percent(double z) => $"{Math.Round(z * 100)} %";
}
