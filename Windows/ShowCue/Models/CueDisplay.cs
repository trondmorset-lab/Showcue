namespace ShowCue.Models;

/// <summary>Hvordan en rad i cuelista skal se ut. Rekkefølgen er prioriteten: spilles nå, deaktivert, neste, ellers vanlig.</summary>
public enum CueRowState { Normal, Playing, Next, Disabled }

/// <summary>Fanene i inspektøren.</summary>
public enum InspectorTab { Basic, Media, Timing, Advanced }

/// <summary>Ren logikk for visning: radtilstand, «gjenstår»-tekst og hvilke inspektørfaner som finnes. Ingen WPF her, så den kan testes.</summary>
public static class CueDisplay
{
    /// <summary>En cue som spiller vises alltid som spilt. En deaktivert cue hoppes over av GO, så den er aldri «neste».</summary>
    public static CueRowState RowState(bool isRunning, bool isNext, bool disabled) =>
        isRunning ? CueRowState.Playing
        : disabled ? CueRowState.Disabled
        : isNext ? CueRowState.Next
        : CueRowState.Normal;

    /// <summary>Tid igjen som m:ss (rundet opp, så «0:00» aldri vises mens noe fortsatt spiller), eller t:mm:ss over en time.
    /// Ugyldige og negative verdier gir «0:00».</summary>
    public static string Clock(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
        var total = (long)Math.Ceiling(Math.Min(seconds, 359_999));
        var h = total / 3600;
        var m = total % 3600 / 60;
        var s = total % 60;
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
    }

    /// <summary>«Gjenstår» i cuelista: tom når cuen ikke spiller (eller venter på pre-wait), «∞» uten kjent varighet
    /// (loop, står til stoppet), ellers tid igjen.</summary>
    public static string RemainingText(bool started, double duration, double elapsed)
    {
        if (!started) return "";
        if (!(duration > 0) || double.IsInfinity(duration)) return "∞";
        return Clock(double.IsNaN(elapsed) ? duration : duration - elapsed);
    }
}

/// <summary>Hvilke faner inspektøren viser for en cuetype (nøkkelen er «type» i showfila), og hvilke av de felles feltene som ligger i hver fane.
/// Cuetypens egne felt ligger i Grunnleggende, og i Media for typene som har lyd, bilde eller video.</summary>
public static class InspectorLayout
{
    private static readonly HashSet<string> MediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "audio", "video", "image", "pdf", "web", "playlist", "slideshow", "input",
    };

    /// <summary>Faner med innhold for typen, i visningsrekkefølge. Typer uten lyd/bilde/video har ingen Media-fane.</summary>
    public static IReadOnlyList<InspectorTab> TabsFor(string? typeKey) =>
        HasMedia(typeKey)
            ? new[] { InspectorTab.Basic, InspectorTab.Media, InspectorTab.Timing, InspectorTab.Advanced }
            : new[] { InspectorTab.Basic, InspectorTab.Timing, InspectorTab.Advanced };

    public static bool HasMedia(string? typeKey) => typeKey != null && MediaTypes.Contains(typeKey);

    /// <summary>Fanen som skal vises når en annen cue velges: den samme hvis den finnes for typen, ellers Grunnleggende.</summary>
    public static InspectorTab Resolve(InspectorTab current, string? typeKey) =>
        TabsFor(typeKey).Contains(current) ? current : InspectorTab.Basic;

    /// <summary>Felt som alle cuer har (ved siden av nummer og navn, som alltid står over fanene), fordelt på fanene.
    /// Alle feltene fra den gamle, lange lista er med, hver på ett sted.</summary>
    public static IReadOnlyList<string> CommonFields(InspectorTab tab) => tab switch
    {
        InspectorTab.Basic => new[] { "disabled" },
        InspectorTab.Timing => new[] { "preWait", "skipInRehearsal", "continueMode", "timecodeTrigger", "clockTrigger" },
        InspectorTab.Advanced => new[] { "hotkey", "midiTrigger", "inButtonPanel", "color", "notes" },
        _ => Array.Empty<string>(),
    };
}
