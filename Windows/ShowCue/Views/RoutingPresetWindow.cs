using System.Windows;
using System.Windows.Controls;
using ShowCue.Models;

namespace ShowCue.Views;

/// <summary>Det routingmatrisen trenger fra hovedvinduet: forhåndsvalgene i showet, kopiert routing og cuene som er valgt.</summary>
public interface IRoutingHost
{
    List<RoutingPreset> RoutingPresets { get; }
    RouteClipboard RouteClipboard { get; }
    /// <summary>Antall utganger i lydpatchen.</summary>
    int BusCount { get; }
    /// <summary>Redigering er slått av i showmodus.</summary>
    bool CanEditRouting { get; }
    /// <summary>Cuene routingen skal settes på: de valgte cuene hvis <paramref name="current"/> er blant dem, ellers bare den.</summary>
    IReadOnlyList<Cue> RoutingTargets(Cue current);
    /// <summary>Gir beskjed om at forhåndsvalgene er endret (så tittelen viser ulagrede endringer) og viser en statusmelding.</summary>
    void RoutingChanged(string status);
}

/// <summary>Dialog for å lage eller endre et routingforhåndsvalg: navn og stereopar («1-2, 3-4:-6»).</summary>
public sealed class RoutingPresetWindow : Window
{
    private readonly TextBox _name = new() { Margin = new Thickness(0, 2, 0, 8), Padding = new Thickness(5, 3, 5, 3) };
    private readonly TextBox _pairs = new() { Margin = new Thickness(0, 2, 0, 4), Padding = new Thickness(5, 3, 5, 3) };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Foreground = System.Windows.Media.Brushes.IndianRed };
    private readonly Func<string, List<RoutingPair>, RoutingPresetError> _commit;

    /// <param name="existing">Forhåndsvalget som endres, eller null for et nytt.</param>
    /// <param name="commit">Lagrer navn og par; gir feilen hvis det ikke lot seg gjøre (vinduet blir da stående).</param>
    public RoutingPresetWindow(RoutingPreset? existing, Func<string, List<RoutingPair>, RoutingPresetError> commit)
    {
        _commit = commit;
        Title = existing == null ? Loc.T("Nytt routingforhåndsvalg") : Loc.T("Endre routingforhåndsvalg");
        Width = 420; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "Panel");
        SetResourceReference(ForegroundProperty, "Text");
        ThemeManager.Register(this);

        _name.Text = existing?.Name ?? "";
        _pairs.Text = existing == null ? "1-2" : RoutingPresets.FormatPairs(existing);

        var hint = new TextBlock
        {
            Text = Loc.T("Hvert par er to utganger, f.eks. «1-2» (hovedhøyttalere) eller «3-4» (monitor). Flere par skilles med komma: «1-2, 3-4». ") +
                   Loc.T("Et nivå i dB kan legges til etter kolon: «3-4:-6»."),
            TextWrapping = TextWrapping.Wrap, FontSize = 11.5,
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Faint");

        var ok = new Button { Content = Loc.T("Lagre"), Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = Loc.T("Avbryt"), Width = 90, IsCancel = true };
        ok.Click += (_, _) => TrySave();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock { Text = Loc.T("Navn") });
        root.Children.Add(_name);
        root.Children.Add(new TextBlock { Text = Loc.T("Utgangspar") });
        root.Children.Add(_pairs);
        root.Children.Add(hint);
        root.Children.Add(_error);
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) => { _name.Focus(); _name.SelectAll(); };
    }

    private void TrySave()
    {
        if (!RoutingPresets.TryParsePairs(_pairs.Text, out var pairs))
        {
            _error.Text = Loc.T("Utgangsparene kan ikke leses. Skriv f.eks. «1-2» eller «1-2, 3-4:-6».");
            return;
        }
        var err = _commit(_name.Text, pairs);
        if (err == RoutingPresetError.None) { DialogResult = true; return; }
        _error.Text = ErrorText(err);
    }

    /// <summary>Feilmelding på norsk for en feil ved lagring av forhåndsvalg.</summary>
    public static string ErrorText(RoutingPresetError err) => err switch
    {
        RoutingPresetError.EmptyName => Loc.T("Skriv et navn."),
        RoutingPresetError.NameTooLong => Loc.F("Navnet kan ha høyst {0} tegn.", RoutingPresets.MaxNameLength),
        RoutingPresetError.DuplicateName => Loc.T("Det finnes allerede et forhåndsvalg med dette navnet."),
        RoutingPresetError.NoPairs => Loc.T("Legg til minst ett utgangspar."),
        RoutingPresetError.TooManyPresets => Loc.F("Det kan være høyst {0} forhåndsvalg.", RoutingPresets.MaxPresets),
        _ => Loc.T("Forhåndsvalget finnes ikke."),
    };
}
