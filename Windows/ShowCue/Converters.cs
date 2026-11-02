using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ShowCue.Models;

namespace ShowCue;

/// <summary>Viser enum-verdier med norske navn.</summary>
public sealed class EnumTextConverter : IValueConverter
{
    private static readonly Dictionary<object, string> Names = new()
    {
        [ContinueMode.None] = Loc.T("Ingen"),
        [ContinueMode.AutoContinue] = Loc.T("Auto-continue (start neste samtidig)"),
        [ContinueMode.AutoFollow] = Loc.T("Auto-follow (start neste når ferdig)"),
        [VisualStretch.Uniform] = Loc.T("Tilpass (behold format)"),
        [VisualStretch.UniformToFill] = Loc.T("Fyll (beskjær)"),
        [VisualStretch.Fill] = Loc.T("Strekk"),
        [VisualStretch.None] = Loc.T("Originalstørrelse"),
        [MidiMessageType.NoteOn] = Loc.T("Note on"),
        [MidiMessageType.NoteOff] = Loc.T("Note off"),
        [MidiMessageType.ProgramChange] = Loc.T("Program change"),
        [MidiMessageType.ControlChange] = Loc.T("Control change (CC)"),
        [MidiMessageType.MscGo] = Loc.T("MSC GO (MIDI Show Control)"),
        [AudioDriverType.Wasapi] = Loc.T("WASAPI (vanlige lydkort)"),
        [AudioDriverType.Asio] = Loc.T("ASIO (proff lydkort, lav latens)"),
        [AppTheme.Dark] = Loc.T("Mørk"),
        [AppTheme.Light] = Loc.T("Lys"),
        [AppTheme.Night] = Loc.T("Nattmodus (ekstra dempet)"),
        [OscFeedbackFormat.Companion] = Loc.T("Companion (Custom Variables)"),
        [OscFeedbackFormat.Generic] = Loc.T("Generisk (/showcue/…)"),
        [CueColor.None] = Loc.T("Ingen"),
        [CueColor.Red] = Loc.T("Rød"),
        [CueColor.Orange] = Loc.T("Oransje"),
        [CueColor.Yellow] = Loc.T("Gul"),
        [CueColor.Green] = Loc.T("Grønn"),
        [CueColor.Blue] = Loc.T("Blå"),
        [CueColor.Purple] = Loc.T("Lilla"),
        [CueColor.Gray] = Loc.T("Grå"),
        [FadeCurve.Db] = Loc.T("Jevn (lineær i dB)"),
        [FadeCurve.SCurve] = Loc.T("S-kurve (myk start og slutt)"),
        [FadeCurve.Linear] = Loc.T("Lineær amplitude (som en fader)"),
        [NetworkProtocol.Udp] = "UDP",
        [NetworkProtocol.Tcp] = "TCP",
        [NetworkProtocol.Http] = Loc.T("HTTP-forespørsel"),
        [TimecodeRate.Fps24] = Timecode.Label(TimecodeRate.Fps24),
        [TimecodeRate.Fps25] = Timecode.Label(TimecodeRate.Fps25),
        [TimecodeRate.Fps2997Drop] = Timecode.Label(TimecodeRate.Fps2997Drop),
        [TimecodeRate.Fps30] = Timecode.Label(TimecodeRate.Fps30),
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value != null && Names.TryGetValue(value, out var s) ? s : value?.ToString() ?? "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => DependencyProperty.UnsetValue;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b ? !b : value;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b ? !b : value;
}

/// <summary>Full sti → filnavn (spillelister).</summary>
public sealed class FileNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string s ? System.IO.Path.GetFileName(s) : "";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}

/// <summary>Kort symbol for continue-modus i cuelista.</summary>
public sealed class ContinueGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        ContinueMode.AutoContinue => "↓",
        ContinueMode.AutoFollow => "↧",
        _ => ""
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}

/// <summary>Synlig når verdien er lik parameteren (brukes til MIDI-felt).</summary>
public sealed class EqualsToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var match = parameter is string p && (p.Split('|').Contains(value?.ToString()));
        return match ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}

/// <summary>Toppnivå (lineært) → høyde i prosent av meter (−60..0 dB).</summary>
public static class MeterMath
{
    public static double ToFraction(float peak)
    {
        if (peak <= 0.001f) return 0;
        var db = 20 * Math.Log10(peak);
        return Math.Clamp((db + 60) / 60.0, 0, 1);
    }
}

public static class EnumValues
{
    public static Array ContinueModes { get; } = Enum.GetValues(typeof(ContinueMode));
    public static Array Stretches { get; } = Enum.GetValues(typeof(VisualStretch));
    public static Array MidiTypes { get; } = Enum.GetValues(typeof(MidiMessageType));
    public static Array AudioDrivers { get; } = Enum.GetValues(typeof(AudioDriverType));
    public static Array Themes { get; } = Enum.GetValues(typeof(AppTheme));
    public static Array FeedbackFormats { get; } = Enum.GetValues(typeof(OscFeedbackFormat));
    public static Array CueColors { get; } = Enum.GetValues(typeof(CueColor));
    public static Array FadeCurves { get; } = Enum.GetValues(typeof(FadeCurve));
    public static Array NetworkProtocols { get; } = Enum.GetValues(typeof(NetworkProtocol));
    public static Array TimecodeRates { get; } = Enum.GetValues(typeof(TimecodeRate));
    public static string[] HttpMethods { get; } = { "GET", "POST", "PUT", "DELETE" };
}

public sealed class MidiTriggerTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string s && s.Length > 0 ? MidiTriggers.Describe(s) : Loc.T("(ingen)");
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}

public sealed class NonEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string s && s.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}

/// <summary>Cuefarge → penselen «Cue{Farge}Stripe» fra gjeldende tema (for fargevelgeren og hurtigknapper).</summary>
public sealed class CueColorBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not CueColor c || c == CueColor.None) return System.Windows.Media.Brushes.Transparent;
        return Application.Current.TryFindResource($"Cue{c}Stripe") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Transparent;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}
