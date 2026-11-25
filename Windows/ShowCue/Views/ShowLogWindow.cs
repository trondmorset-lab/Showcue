using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.Win32;
using ShowCue.Engine;
using ShowCue.Models;

namespace ShowCue.Views;

/// <summary>Én rad i loggvinduet.</summary>
public sealed class ShowLogRow
{
    public string Time { get; init; } = "";
    public string Run { get; init; } = "";
    public string Event { get; init; } = "";
    public string Cue { get; init; } = "";
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";
    public string How { get; init; } = "";
    public string Text { get; init; } = "";
}

/// <summary>
/// «Showlogg»: viser hendelsene i denne kjøringen fortløpende (nyeste nederst), med filter, og eksporterer til CSV eller tekst.
/// Kan også åpne en eldre loggfil. Eksporten tar med det som vises (etter filteret).
/// </summary>
public sealed class ShowLogWindow : Window
{
    private readonly ShowLog _log;
    private readonly string _root;
    private readonly ObservableCollection<ShowLogRow> _rows = new();
    private readonly List<ShowLogEvent> _events = new();
    private readonly DataGrid _grid = new();
    private readonly ComboBox _filter = new() { Width = 220, Margin = new Thickness(0, 0, 12, 0) };
    private readonly CheckBox _follow = new() { Content = Loc.T("Følg nyeste"), IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
    private readonly TextBlock _source = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _info = new() { Margin = new Thickness(18, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly Button _live = new() { Content = Loc.T("Gjeldende kjøring"), Width = 140, Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
    private long _seq;
    private string? _viewingFile;
    private const int MaxEvents = 5000;

    public ShowLogWindow(ShowLog log, string root)
    {
        _log = log;
        _root = root;
        Title = Loc.T("Showlogg");
        Width = 1000;
        Height = 560;
        MinWidth = 640;
        MinHeight = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "Panel");
        SetResourceReference(ForegroundProperty, "Text");
        ThemeManager.Register(this);
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/ShowCue.ico"));

        foreach (var (label, cat) in Categories()) _filter.Items.Add(new ComboBoxItem { Content = label, Tag = cat });
        _filter.SelectedIndex = 0;
        _filter.SelectionChanged += (_, _) => Rebuild();

        var choose = new Button { Content = Loc.T("Åpne eldre logg"), Width = 140, Margin = new Thickness(0, 0, 8, 0) };
        choose.Click += (_, _) => ChooseFile();
        _live.Click += (_, _) => { _viewingFile = null; Reload(); };

        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 14, 18, 8) };
        top.Children.Add(_filter);
        top.Children.Add(_follow);
        top.Children.Add(choose);
        top.Children.Add(_live);

        var csv = new Button { Content = Loc.T("Eksporter CSV"), Width = 130, Margin = new Thickness(0, 0, 8, 0) };
        csv.Click += (_, _) => Export(true);
        var txt = new Button { Content = Loc.T("Eksporter tekst"), Width = 130, Margin = new Thickness(0, 0, 8, 0) };
        txt.Click += (_, _) => Export(false);
        var close = new Button { Content = Loc.T("Lukk"), Width = 90, IsCancel = true };
        close.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(18, 8, 18, 14) };
        buttons.Children.Add(csv);
        buttons.Children.Add(txt);
        buttons.Children.Add(close);

        _grid.ItemsSource = _rows;
        _grid.AutoGenerateColumns = false;
        _grid.IsReadOnly = true;
        _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        _grid.GridLinesVisibility = DataGridGridLinesVisibility.None;
        _grid.SelectionMode = DataGridSelectionMode.Extended;
        _grid.Margin = new Thickness(18, 0, 18, 0);
        void Col(string header, string path, double width) =>
            _grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(path), Width = width > 0 ? new DataGridLength(width) : new DataGridLength(1, DataGridLengthUnitType.Star) });
        Col(Loc.T("Tid"), nameof(ShowLogRow.Time), 100);
        Col(Loc.T("Siden start"), nameof(ShowLogRow.Run), 100);
        Col(Loc.T("Hendelse"), nameof(ShowLogRow.Event), 110);
        Col(Loc.T("Cue"), nameof(ShowLogRow.Cue), 50);
        Col(Loc.T("Navn"), nameof(ShowLogRow.Name), 170);
        Col(Loc.T("Type"), nameof(ShowLogRow.Type), 70);
        Col(Loc.T("Hvordan"), nameof(ShowLogRow.How), 100);
        Col(Loc.T("Tekst"), nameof(ShowLogRow.Text), 0);

        var root2 = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        var sourceLine = new StackPanel { Margin = new Thickness(18, 0, 18, 6) };
        sourceLine.Children.Add(_source);
        sourceLine.Children.Add(_info);
        _info.Margin = new Thickness(0, 2, 0, 0);
        DockPanel.SetDock(sourceLine, Dock.Bottom);
        root2.Children.Add(top);
        root2.Children.Add(buttons);
        root2.Children.Add(sourceLine);
        root2.Children.Add(_grid);
        Content = root2;

        _timer.Tick += (_, _) => Poll();
        Loaded += (_, _) => { Reload(); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();
    }

    private static IEnumerable<(string Label, ShowLogCategory Category)> Categories() => new[]
    {
        (Loc.T("Alle hendelser"), ShowLogCategory.All),
        (Loc.T("Bare feil og advarsler"), ShowLogCategory.Problems),
        (Loc.T("Cuer (start og stopp)"), ShowLogCategory.Cues),
        (Loc.T("Styring (pause, stopp alt, markør)"), ShowLogCategory.Control),
        (Loc.T("Show og program"), ShowLogCategory.Show),
    };

    private ShowLogCategory Category => (_filter.SelectedItem as ComboBoxItem)?.Tag is ShowLogCategory c ? c : ShowLogCategory.All;

    private static string Label(ShowLogKind k) => k switch
    {
        ShowLogKind.Start => Loc.T("Start"),
        ShowLogKind.Finish => Loc.T("Ferdig"),
        ShowLogKind.Stop => Loc.T("Stoppet"),
        ShowLogKind.Pause => Loc.T("Pause"),
        ShowLogKind.Resume => Loc.T("Fortsett"),
        ShowLogKind.StopAll => Loc.T("Stopp alt"),
        ShowLogKind.Playhead => Loc.T("Markør"),
        ShowLogKind.Warning => Loc.T("Advarsel"),
        ShowLogKind.Error => Loc.T("Feil"),
        ShowLogKind.ShowOpened => Loc.T("Show åpnet"),
        ShowLogKind.ShowSaved => Loc.T("Show lagret"),
        ShowLogKind.AppStart => Loc.T("Program startet"),
        ShowLogKind.AppExit => Loc.T("Program avsluttet"),
        ShowLogKind.Dropped => Loc.T("Mistet"),
        _ => "",
    };

    private static ShowLogRow ToRow(ShowLogEvent e) => new()
    {
        Time = e.Time.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
        Run = ShowLogFormat.RunText(e.RunMs),
        Event = Label(e.Kind),
        Cue = e.Number,
        Name = e.Name,
        Type = e.CueType,
        How = e.How,
        Text = e.Text,
    };

    private IEnumerable<ShowLogEvent> Visible() => _events.Where(e => ShowLogFormat.Matches(e, Category));

    /// <summary>Henter alt på nytt (ved start, filterbytte eller bytte mellom nåværende kjøring og en fil).</summary>
    private void Reload()
    {
        _events.Clear();
        _seq = 0;
        _live.IsEnabled = _viewingFile != null;
        if (_viewingFile == null)
        {
            _events.AddRange(_log.ReadSince(0, out _seq));
            _source.Text = _log.FilePath != null ? Loc.F("Gjeldende kjøring – {0}", _log.FilePath) : Loc.T("Gjeldende kjøring");
        }
        else
        {
            try
            {
                _events.AddRange(ShowLogExport.ReadFile(_viewingFile, out var skipped));
                _source.Text = Loc.F("Logg: {0}", _viewingFile) + (skipped > 0 ? Loc.F(" ({0} linjer kunne ikke leses)", skipped) : "");
            }
            catch (Exception ex)
            {
                _viewingFile = null;
                _live.IsEnabled = false;
                MessageBox.Show(this, Loc.T("Kunne ikke lese loggen: ") + ex.Message, "ShowCue", MessageBoxButton.OK, MessageBoxImage.Warning);
                Reload();
                return;
            }
        }
        Trim();
        Rebuild();
    }

    private void Trim()
    {
        if (_events.Count > MaxEvents) _events.RemoveRange(0, _events.Count - MaxEvents);
    }

    private void Rebuild()
    {
        _rows.Clear();
        foreach (var e in Visible()) _rows.Add(ToRow(e));
        UpdateInfo();
        ScrollToEnd();
    }

    /// <summary>Henter nye hendelser fra den løpende loggen og legger dem nederst.</summary>
    private void Poll()
    {
        if (_viewingFile == null)
        {
            var fresh = _log.ReadSince(_seq, out _seq);
            if (fresh.Count > 0)
            {
                _events.AddRange(fresh);
                var cat = Category;
                foreach (var e in fresh) if (ShowLogFormat.Matches(e, cat)) _rows.Add(ToRow(e));
                if (_events.Count > MaxEvents)
                {
                    Trim();
                    Rebuild();
                    return;
                }
                ScrollToEnd();
            }
        }
        UpdateInfo();
    }

    private void UpdateInfo()
    {
        var text = Loc.F("{0} hendelser vises av {1}.", _rows.Count, _events.Count);
        if (_viewingFile == null && _log.Dropped > 0) text += " " + Loc.F("{0} hendelser ble kastet fordi køen var full.", _log.Dropped);
        if (_viewingFile == null && _log.Fault != null) text += " " + Loc.F("Skriving til fil feilet: {0}", _log.Fault);
        if (_info.Text != text) _info.Text = text;
    }

    private void ScrollToEnd()
    {
        if (_follow.IsChecked != true || _rows.Count == 0) return;
        try { _grid.ScrollIntoView(_rows[^1]); } catch { /* vinduet er ikke klart ennå */ }
    }

    private void ChooseFile()
    {
        var dir = ShowLogFiles.DirectoryFor(_root);
        var dlg = new OpenFileDialog { Filter = Loc.T("Loggfiler") + "|*.log|" + Loc.T("Alle filer") + "|*.*" };
        if (Directory.Exists(dir)) dlg.InitialDirectory = dir;
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dlg.FileName).Length > 50L * 1024 * 1024)
            {
                MessageBox.Show(this, Loc.T("Fila er for stor til å åpnes som logg."), "ShowCue", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Loc.T("Kunne ikke lese loggen: ") + ex.Message, "ShowCue", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _viewingFile = dlg.FileName;
        Reload();
    }

    private void Export(bool csv)
    {
        var dlg = new SaveFileDialog
        {
            Filter = csv ? Loc.T("CSV-fil") + "|*.csv" : Loc.T("Tekstfil") + "|*.txt",
            DefaultExt = csv ? ".csv" : ".txt",
            FileName = _viewingFile != null ? Path.GetFileNameWithoutExtension(_viewingFile) : "showlog " + DateTime.Now.ToString("yyyy-MM-dd HH.mm", CultureInfo.InvariantCulture),
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var list = Visible().ToList();
            // CSV får byte order mark, så regneark leser æøå riktig.
            File.WriteAllText(dlg.FileName, csv ? ShowLogExport.ToCsv(list) : ShowLogExport.ToText(list), new UTF8Encoding(csv));
            _info.Text = Loc.F("Eksporterte {0} hendelser til {1}", list.Count, dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Loc.T("Kunne ikke eksportere: ") + ex.Message, "ShowCue", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
