using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ShowCue.Models;
using ShowCue.Views;

namespace ShowCue;

/// <summary>
/// Routingmatrise for en lyd-cue eller lydsporet i en video-cue: rader = kanaler i lydfila, kolonner = utganger i lydpatchen.
/// Hver rute er et nivå i dB; tom rute = ikke routet.
/// </summary>
public sealed class RoutingMatrix : UserControl
{
    public static readonly DependencyProperty CueProperty = DependencyProperty.Register(
        nameof(Cue), typeof(IRoutedAudio), typeof(RoutingMatrix), new PropertyMetadata(null, (d, e) => ((RoutingMatrix)d).OnCueChanged(e)));

    public static readonly DependencyProperty BusesProperty = DependencyProperty.Register(
        nameof(Buses), typeof(IReadOnlyList<PatchBus>), typeof(RoutingMatrix), new PropertyMetadata(null, (d, _) => ((RoutingMatrix)d).Rebuild()));

    public IRoutedAudio? Cue { get => (IRoutedAudio?)GetValue(CueProperty); set => SetValue(CueProperty, value); }
    public IReadOnlyList<PatchBus>? Buses { get => (IReadOnlyList<PatchBus>?)GetValue(BusesProperty); set => SetValue(BusesProperty, value); }

    private const int MaxRows = 16;
    private readonly Grid _grid = new();
    private readonly TextBlock _summary = new() { FontSize = 11.5, Margin = new Thickness(0, 0, 0, 4) };
    private bool _updating;

    public RoutingMatrix()
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(MakeButton(Loc.T("Standard"), Loc.T("Mono → 1+2, stereo → 1/2 osv."), () => SetRoutes(null)));
        buttons.Children.Add(MakeButton(Loc.T("Alt av"), Loc.T("Fjern all routing"), () => SetRoutes(new List<AudioRoute>())));
        buttons.Children.Add(MakeButton(Loc.T("Sum til mono"), Loc.T("Alle kanaler til utgang 1 og 2 (−6 dB hver)"), () =>
        {
            int ch = Math.Max(1, Cue?.FileChannels ?? 1);
            SetRoutes(Enumerable.Range(1, ch).SelectMany(c => new[] { new AudioRoute(c, 1, ch > 1 ? -6 : 0), new AudioRoute(c, 2, ch > 1 ? -6 : 0) }).ToList());
        }));

        Button? presetButton = null;
        presetButton = MakeButton(Loc.T("Forhåndsvalg ▾"), Loc.T("Lagrede routinger for showet, og kopier/lim inn routing mellom cuer"), () => ShowPresetMenu(presetButton!));
        buttons.Children.Add(presetButton);
        _summary.SetResourceReference(TextBlock.ForegroundProperty, "Muted");

        var root = new StackPanel();
        root.Children.Add(_summary);
        root.Children.Add(new ScrollViewer
        {
            Content = _grid,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });
        root.Children.Add(buttons);
        Content = root;
        // Tegn på nytt ved temabytte (rutene får farger fra temaet).
        Loaded += (_, _) => { ThemeManager.Changed += Rebuild; UpdateSummary(); };
        Unloaded += (_, _) => ThemeManager.Changed -= Rebuild;
    }

    private Button MakeButton(string text, string tip, Action onClick)
    {
        var b = new Button { Content = text, ToolTip = tip, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(8, 3, 8, 3), FontSize = 11.5 };
        b.Click += (_, _) => onClick();
        return b;
    }

    private void OnCueChanged(DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is IRoutedAudio old) old.PropertyChanged -= Cue_PropertyChanged;
        if (e.NewValue is IRoutedAudio cue) cue.PropertyChanged += Cue_PropertyChanged;
        Rebuild();
    }

    private void Cue_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_updating) return;
        if (e.PropertyName is nameof(AudioCue.Routes) or nameof(AudioCue.FileChannels)) Rebuild();
    }

    private void Rebuild()
    {
        // Husk hvilken rute som har fokus, så Tab/klikk mellom ruter fungerer etter ny tegning.
        var focusedKey = _grid.Children.OfType<TextBox>().FirstOrDefault(t => t.IsKeyboardFocusWithin)?.Tag;
        _grid.Children.Clear();
        _grid.RowDefinitions.Clear();
        _grid.ColumnDefinitions.Clear();
        var cue = Cue;
        var buses = Buses;
        UpdateSummary();
        if (cue == null || buses == null) return;

        int rows = Math.Clamp(cue.FileChannels > 0 ? cue.FileChannels : 2, 1, MaxRows);
        var routes = cue.EffectiveRoutes(rows);

        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int b = 0; b < buses.Count; b++) _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        for (int r = 0; r <= rows; r++) _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var muted = (System.Windows.Media.Brush)FindResource("Muted");
        for (int b = 0; b < buses.Count; b++)
        {
            var h = new TextBlock
            {
                Text = (b + 1).ToString(),
                ToolTip = Loc.F("Utgang {0}: {1}", b + 1, buses[b].Name),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = muted,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 3),
            };
            Grid.SetColumn(h, b + 1);
            _grid.Children.Add(h);
        }

        for (int r = 0; r < rows; r++)
        {
            var label = new TextBlock
            {
                Text = rows == 2 ? (r == 0 ? Loc.T("Venstre") : Loc.T("Høyre")) : rows == 1 ? Loc.T("Mono") : Loc.F("Kanal {0}", r + 1),
                Foreground = muted,
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            Grid.SetRow(label, r + 1);
            _grid.Children.Add(label);

            for (int b = 0; b < buses.Count; b++)
            {
                int src = r + 1, bus = b + 1;
                var route = routes.FirstOrDefault(x => x.Source == src && x.Bus == bus);
                var tb = new TextBox
                {
                    Text = route == null ? "" : FormatDb(route.GainDb),
                    Margin = new Thickness(1),
                    Padding = new Thickness(2, 3, 2, 3),
                    FontSize = 12,
                    TextAlignment = TextAlignment.Center,
                    ToolTip = Loc.F("Kanal {0} → {1}", src, buses[b].Name),
                    Tag = (src, bus),
                };
                if (focusedKey is (int fs, int fb) && fs == src && fb == bus)
                    Dispatcher.BeginInvoke(() => { tb.Focus(); tb.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
                if (route != null) tb.Background = (System.Windows.Media.Brush)FindResource("RunningRow");
                tb.LostKeyboardFocus += (_, _) => Commit(src, bus, tb.Text);
                tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(src, bus, tb.Text); };
                tb.MouseDoubleClick += (_, e) =>
                {
                    Commit(src, bus, string.IsNullOrWhiteSpace(tb.Text) ? "0" : "");
                    e.Handled = true;
                };
                Grid.SetRow(tb, r + 1);
                Grid.SetColumn(tb, b + 1);
                _grid.Children.Add(tb);
            }
        }
    }

    /// <summary>Teksten over matrisen: navnet på forhåndsvalget routingen svarer til, «Standard», «Av» eller hvilke utganger det går til.</summary>
    private void UpdateSummary()
    {
        var cue = Cue;
        var buses = Buses;
        _summary.Text = cue == null || buses == null ? ""
            : Loc.F("Routing: {0}", RoutingPresets.Describe(cue.Routes, cue.FileChannels, Host?.RoutingPresets, buses.Count));
    }

    private IRoutingHost? Host => Window.GetWindow(this) as IRoutingHost;

    private ShowCue.Models.Cue? CueModel => Cue as ShowCue.Models.Cue;

    /// <summary>Menyen under «Forhåndsvalg»: sett et forhåndsvalg på cuen (og de andre valgte cuene), lag, endre og slett forhåndsvalg,
    /// og kopier/lim inn routing. Alt skjer på hovedtråden; ingenting her rører lydtråden (routingen byttes av den vanlige lyttingen på endringer).</summary>
    private void ShowPresetMenu(Button anchor)
    {
        var host = Host;
        var cue = CueModel;
        if (host == null || cue == null) return;
        bool edit = host.CanEditRouting;
        int busCount = host.BusCount;
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };

        MenuItem Item(string header, Action act, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, IsEnabled = enabled };
            mi.Click += (_, _) => { act(); UpdateSummary(); };
            return mi;
        }

        var presets = host.RoutingPresets;
        if (presets.Count == 0) menu.Items.Add(new MenuItem { Header = Loc.T("Ingen forhåndsvalg ennå"), IsEnabled = false });
        foreach (var p in presets.ToList())
            menu.Items.Add(Item($"{p.Name}   ({RoutingPresets.FormatPairs(p)})", () =>
                Report(host, RoutingPresets.Apply(host.RoutingTargets(cue), p, busCount), p.Name), edit));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Loc.T("Nytt forhåndsvalg"), () => OpenEditor(host, null, -1), edit && presets.Count < RoutingPresets.MaxPresets));

        var editMenu = new MenuItem { Header = Loc.T("Endre forhåndsvalg"), IsEnabled = edit && presets.Count > 0 };
        var deleteMenu = new MenuItem { Header = Loc.T("Slett forhåndsvalg"), IsEnabled = edit && presets.Count > 0 };
        for (int i = 0; i < presets.Count; i++)
        {
            int index = i;
            var p = presets[i];
            editMenu.Items.Add(Item(p.Name, () => OpenEditor(host, p, index)));
            deleteMenu.Items.Add(Item(p.Name, () =>
            {
                if (RoutingPresets.Remove(presets, index)) host.RoutingChanged(Loc.F("Slettet forhåndsvalget «{0}».", p.Name));
            }));
        }
        menu.Items.Add(editMenu);
        menu.Items.Add(deleteMenu);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Loc.T("Kopier routing"), () =>
        {
            if (host.RouteClipboard.Copy(cue)) host.RoutingChanged(Loc.T("Routingen er kopiert."));
        }));
        menu.Items.Add(Item(Loc.T("Lim inn routing"), () =>
            Report(host, RoutingPresets.Paste(host.RoutingTargets(cue), host.RouteClipboard, busCount), null), edit && host.RouteClipboard.HasContent));
        menu.Items.Add(Item(Loc.T("Bruk denne routingen på alle valgte cuer"), () =>
            Report(host, RoutingPresets.ApplyRoutes(host.RoutingTargets(cue).Where(c => !ReferenceEquals(c, cue)), Cue?.Routes, busCount), null),
            edit && host.RoutingTargets(cue).Count > 1));
        anchor.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private static void Report(IRoutingHost host, RoutingApplyResult r, string? presetName)
    {
        var text = presetName != null ? Loc.F("Routing «{0}» satt på {1} cue(r).", presetName, r.Changed) : Loc.F("Routing satt på {0} cue(r).", r.Changed);
        if (r.Unchanged > 0) text += Loc.F(" {0} hadde den fra før.", r.Unchanged);
        if (r.Skipped > 0) text += Loc.F(" {0} hoppet over (ingen lyd eller ingen gyldig utgang).", r.Skipped);
        host.RoutingChanged(text);
    }

    private void OpenEditor(IRoutingHost host, RoutingPreset? existing, int index)
    {
        var presets = host.RoutingPresets;
        var win = new RoutingPresetWindow(existing, (name, pairs) =>
            existing == null ? RoutingPresets.Add(presets, name, pairs) : RoutingPresets.Update(presets, index, name, pairs))
        { Owner = Window.GetWindow(this) };
        if (win.ShowDialog() == true) host.RoutingChanged(Loc.T("Forhåndsvalget er lagret."));
    }

    private static string FormatDb(double db) => db.ToString("0.#", CultureInfo.CurrentCulture);

    private void Commit(int src, int bus, string text)
    {
        var cue = Cue;
        if (cue == null) return;
        int rows = Math.Clamp(cue.FileChannels > 0 ? cue.FileChannels : 2, 1, MaxRows);
        var list = cue.EffectiveRoutes(rows).Select(r => new AudioRoute(r.Source, r.Bus, r.GainDb)).ToList();
        var existing = list.FirstOrDefault(r => r.Source == src && r.Bus == bus);

        text = text.Trim();
        if (text.Length == 0 || text.ToLowerInvariant() is "-" or "-inf" or "av" or "off")
        {
            if (existing == null) return;
            list.Remove(existing);
        }
        else
        {
            if (!double.TryParse(text.Replace('.', ','), NumberStyles.Float, new CultureInfo("nb-NO"), out var db)) { Rebuild(); return; }
            db = Math.Clamp(db, -60, 12);
            if (existing != null && Math.Abs(existing.GainDb - db) < 0.001) return;
            if (existing != null) existing.GainDb = db;
            else list.Add(new AudioRoute(src, bus, db));
        }
        SetRoutes(list.OrderBy(r => r.Source).ThenBy(r => r.Bus).ToList());
    }

    private void SetRoutes(List<AudioRoute>? routes)
    {
        if (Cue == null) return;
        _updating = true;
        try { Cue.Routes = routes; }
        finally { _updating = false; }
        // Tegn på nytt etter at fokus er flyttet, så Tab mellom ruter fortsatt virker.
        Dispatcher.BeginInvoke(Rebuild, System.Windows.Threading.DispatcherPriority.Background);
    }
}
