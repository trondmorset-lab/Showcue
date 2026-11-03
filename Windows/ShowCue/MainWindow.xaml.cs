using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using ShowCue.Engine;
using ShowCue.Models;
using ShowCue.Views;

namespace ShowCue;

public partial class MainWindow : Window, INotifyPropertyChanged, IShowControl
{
    // ---------- Filtyper ----------
    private static readonly string[] AudioExt = { ".wav", ".mp3", ".aif", ".aiff", ".flac", ".m4a", ".aac", ".wma" };
    private static readonly string[] VideoExt = { ".mp4", ".mov", ".m4v", ".wmv", ".avi", ".mkv" };
    private static readonly string[] ImageExt = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };
    private static readonly string[] PdfExt = { ".pdf" };
    private static string ShowFilter => Loc.T("ShowCue-show|*.showcue|Alle filer|*.*");

    /// <summary>Filter for fildialogen, f.eks. «Lydfiler|*.wav;*.mp3|Alle filer|*.*».</summary>
    private static string Filter(string name, string[] ext) => Loc.F("{0}|{1}|Alle filer|*.*", name, string.Join(";", ext.Select(e => "*" + e)));

    private static string FilterFor(object cueOrType) => cueOrType switch
    {
        AudioCue or "audio" => Filter(Loc.T("Lydfiler"), AudioExt),
        VideoCue or "video" => Filter(Loc.T("Videofiler"), VideoExt),
        ImageCue or "image" => Filter(Loc.T("Bilder"), ImageExt),
        _ => Filter("PDF", PdfExt),
    };

    private AppSettings _settings;
    private ShowDocument _show = new();
    private string? _showPath;
    private string _savedJson = "";
    private Cue? _selected;
    private string _statusText = "";
    private DateTime _lastGo = DateTime.MinValue;
    private DateTime _lastPanic = DateTime.MinValue;
    private double _meterL, _meterR;
    private string _lastClock = "";
    private PatchWindow? _patchWindow;
    private ButtonPanelWindow? _panelWindow;
    private string _memoText = "";
    private CancellationTokenSource? _loudnessCancel;
    private OscFeedback? _feedback;
    private int _feedbackTick;

    // Angre, autolagring og showmodus
    private readonly UndoHistory _history = new();
    private readonly System.Windows.Threading.DispatcherTimer _undoTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly System.Windows.Threading.DispatcherTimer _autosaveTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private bool _restoring;
    private bool _showMode;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    public CueEngine Engine { get; }
    public RemoteControl Remote { get; }
    /// <summary>Lydpatchens utganger (ny liste ved hver endring, så routingmatrisen tegnes på nytt).</summary>
    public IReadOnlyList<PatchBus> Buses => _settings.Buses.ToList();
    public string RemoteStatus => Remote.OscStatus + "   ·   " + Remote.MidiStatus
        + (Remote.TimecodeStatus.Length > 0 ? "   ·   " + Remote.TimecodeStatus : "")
        + (_settings.OscFeedbackEnabled ? Loc.F("   ·   Status ut: {0}:{1}", _settings.OscFeedbackHost, _settings.OscFeedbackPort) : "");
    private string _learnText = "";
    /// <summary>Vises i inspector mens vi venter på en MIDI-melding.</summary>
    public string LearnText { get => _learnText; set { _learnText = value; Raise(); } }
    public List<string> MidiDevices { get; private set; } = MidiEngine.GetDevices();
    public ListCollectionView HotkeyCues { get; private set; } = null!;

    public ObservableCollection<Cue> Cues => _show.Cues;

    public Cue? SelectedCue
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            if (_selected != null) _selected.PropertyChanged -= Selected_PropertyChanged;
            _selected = value;
            if (_selected != null) _selected.PropertyChanged += Selected_PropertyChanged;
            Raise();
            Raise(nameof(HasSelection));
            Raise(nameof(NoSelection));
            Raise(nameof(NextCueTitle));
            if (_selected != null) CueGrid.ScrollIntoView(_selected);
        }
    }

    public bool HasSelection => _selected != null;
    public bool NoSelection => _selected == null;
    public bool IsEmpty => Cues.Count == 0;
    public bool NothingActive => Engine.Active.Count == 0;
    public bool NoHotkeys => HotkeyCues.Count == 0;
    public string NextCueTitle => _selected?.DisplayName ?? (Cues.Count > 0 ? Loc.T("— slutt på lista —") : "");
    public string ActiveCountText => Engine.Active.Count == 0 ? "" : Loc.F("{0} aktive", Engine.Active.Count);
    public string StatusText { get => _statusText; set { _statusText = value; Raise(); } }
    public string WindowTitle =>
        Loc.F("{0}{1}{2} — ShowCue {3}", (_showPath != null ? Path.GetFileNameWithoutExtension(_showPath) : Loc.T("Uten navn")), (IsDirty ? " •" : ""), (_showMode ? Loc.T("  🔒 SHOWMODUS") : ""), AppVersion);

    /// <summary>Programversjonen (fra VERSION-fila ved bygging), f.eks. «0.5.0».</summary>
    public static string AppVersion
    {
        get
        {
            var v = typeof(MainWindow).Assembly.GetName().Version;
            return v == null ? "" : $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
        }
    }

    private bool IsDirty => _show.ToJson() != _savedJson;

    /// <summary>Showmodus: bare GO, stopp og hurtigtaster virker – ingenting kan endres ved et uhell.</summary>
    public bool IsShowMode
    {
        get => _showMode;
        set
        {
            if (_showMode == value) return;
            CommitFocusedTextBox();
            _showMode = value;
            if (value) Engine.Preview.Stop();
            Raise(); Raise(nameof(IsEditable)); Raise(nameof(ShowModeLabel)); RaiseUndo(); RaiseTitle();
            StatusText = value ? Loc.T("Showmodus: redigering er låst (Ctrl+L for å låse opp).") : Loc.T("Redigering er låst opp.");
        }
    }
    public bool IsEditable => !_showMode;
    public string PauseGlyph => Engine.IsPaused ? "▶" : "⏸";
    /// <summary>Teksten fra siste memo-cue (tom = ingen beskjed vises).</summary>
    public string MemoText { get => _memoText; set { _memoText = value ?? ""; Raise(); } }
    public string PauseText => Engine.IsPaused ? Loc.T("Fortsett") : Loc.T("Pause");
    public string ShowModeLabel => _showMode ? Loc.T("🔓 Lås opp") : Loc.T("🔒 Showmodus");
    public bool CanUndo => IsEditable && _history.CanUndo;
    public bool CanRedo => IsEditable && _history.CanRedo;
    private void RaiseUndo() { Raise(nameof(CanUndo)); Raise(nameof(CanRedo)); }

    public MainWindow()
    {
        _settings = AppSettings.Load();
        ThemeManager.Apply(_settings.Theme);
        ThemeManager.Register(this);
        Engine = new CueEngine(_settings, () => Cues);
        WaveformEditor.Engine = Engine;
        Remote = new RemoteControl(this, Dispatcher, _settings);
        Remote.StatusChanged += () => Raise(nameof(RemoteStatus));

        _undoTimer.Tick += (_, _) => FlushUndo();
        _autosaveTimer.Tick += (_, _) => Autosave();
        _autosaveTimer.Start();

        InitializeComponent();
        SetShow(new ShowDocument(), null);
        DataContext = this;

        Engine.Audio.StatusChanged += s => Dispatcher.BeginInvoke(() => StatusText = s);
        Engine.Log += s => StatusText = s;
        Engine.Ticked += OnTick;
        Engine.OutputCreated += OnOutputCreated;
        Engine.ExtraOutputCreated += w =>
        {
            w.ForwardKey += (_, e) => HandleKey(e);
            Dispatcher.BeginInvoke(() => { Activate(); }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        };
        Engine.PauseChanged += () =>
        {
            Raise(nameof(PauseGlyph)); Raise(nameof(PauseText));
            StatusText = Engine.IsPaused ? Loc.T("Pause – alt står stille. Ctrl+P, Pause-knappen eller GO fortsetter.") : Loc.T("Fortsetter.");
        };
        // «Gå til»-cue: vent til GO er ferdig med å flytte markøren, så flytt den til målet.
        Engine.GotoRequested += (target, fire) => Dispatcher.BeginInvoke(() =>
        {
            var c = FindCue(target);
            if (c == null) { StatusText = Loc.F("Gå til: fant ikke cue {0}.", target); return; }
            SelectedCue = c;
            if (fire) { _lastGo = DateTime.MinValue; Go(); }
        });
        Engine.MemoShown += m => Dispatcher.BeginInvoke(() => MemoText = m.Text.Trim().Length > 0 ? m.Text.Trim() : m.DisplayName);
        Engine.Active.CollectionChanged += (_, _) => { Raise(nameof(NothingActive)); Raise(nameof(ActiveCountText)); };

        PreviewKeyDown += (_, e) => HandleKey(e);
        Closing += OnClosing;
        Loaded += (_, _) =>
        {
            Engine.ApplySettings(_settings);
            Remote.Apply(_settings);
            ApplyFeedbackSettings();
            // Dobbeltklikk på en .showcue-fil starter ShowCue med fila som argument.
            var arg = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(a => File.Exists(a));
            if (arg != null) TryOpen(arg);
            else if (!string.IsNullOrEmpty(_settings.LastShowPath) && File.Exists(_settings.LastShowPath))
                TryOpen(_settings.LastShowPath);
            else OfferRecovery();
            Activate();
            CueGrid.Focus();
        };
    }

    // ==================================================================
    //  Show-håndtering
    // ==================================================================
    private void SetShow(ShowDocument doc, string? path)
    {
        Engine.StopAll(0);
        if (_show != null) _show.Cues.CollectionChanged -= Cues_CollectionChanged;
        foreach (var c in _show!.Cues) c.PropertyChanged -= Cue_PropertyChanged;

        _show = doc;
        _showPath = path;
        _show.Cues.CollectionChanged += Cues_CollectionChanged;
        foreach (var c in _show.Cues) c.PropertyChanged += Cue_PropertyChanged;

        HotkeyCues = new ListCollectionView(_show.Cues)
        {
            Filter = o => o is Cue c && !string.IsNullOrEmpty(c.Hotkey),
            IsLiveFiltering = true,
        };
        HotkeyCues.LiveFilteringProperties.Add(nameof(Cue.Hotkey));
        ((INotifyCollectionChanged)HotkeyCues).CollectionChanged += (_, _) => Raise(nameof(NoHotkeys));

        // Fyll inn manglende lengder og forhåndslast bilder/PDF.
        foreach (var a in _show.Cues.OfType<AudioCue>().Where(a => (a.FileDuration <= 0 || a.FileChannels <= 0) && File.Exists(a.FilePath)))
            (a.FileDuration, a.FileChannels) = AudioEngine.Probe(a.FilePath);
        MediaCache.Clear();
        MediaCache.Preload(_show.Cues);
        foreach (var v in _show.Cues.OfType<VideoCue>().Where(v => v.FileChannels < 0)) ProbeVideoAudio(v);

        _savedJson = _show.ToJson();
        _history.Reset(_savedJson);
        RaiseUndo();
        Raise(nameof(Cues));
        Raise(nameof(HotkeyCues));
        Raise(nameof(NoHotkeys));
        _panelWindow?.SetCues(_show.Cues);
        MemoText = "";
        Raise(nameof(IsEmpty));
        SelectedCue = _show.Cues.FirstOrDefault(c => !c.Disabled);
        RaiseTitle();

        var missing = _show.Cues.Where(c => c is IFileCue f && !string.IsNullOrEmpty(f.FilePath) && !File.Exists(f.FilePath)).ToList();
        foreach (var c in missing) c.Error = Loc.T("Fila finnes ikke: ") + ((IFileCue)c).FilePath;
        if (missing.Count > 0) StatusText = Loc.F("Advarsel: {0} mediefil(er) mangler. De er merket med ! i lista.", missing.Count);
    }

    private void Cues_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null) foreach (Cue c in e.NewItems) c.PropertyChanged += Cue_PropertyChanged;
        if (e.OldItems != null) foreach (Cue c in e.OldItems) c.PropertyChanged -= Cue_PropertyChanged;
        ScheduleUndo();
        Raise(nameof(IsEmpty));
        Raise(nameof(NextCueTitle));
        RaiseTitle();
    }

    private void Cue_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not Cue c) return;
        // Kjøretidsstatus endrer ikke showet.
        if (e.PropertyName is nameof(Cue.Progress) or nameof(Cue.ActiveCount) or nameof(Cue.IsRunning)
            or nameof(Cue.IsPreWaiting) or nameof(Cue.Error) or nameof(Cue.HasError)) return;
        ScheduleUndo();

        if (e.PropertyName == nameof(IFileCue.FilePath))
        {
            if (c is AudioCue a) (a.FileDuration, a.FileChannels) = File.Exists(a.FilePath) ? AudioEngine.Probe(a.FilePath) : (0, 0);
            if (c is VideoCue v) { v.KnownDuration = 0; v.FileChannels = -1; ProbeVideoAudio(v); }
            c.Error = c is IFileCue f && !string.IsNullOrEmpty(f.FilePath) && !File.Exists(f.FilePath) ? Loc.T("Fila finnes ikke") : null;
            if (c is StillCue) MediaCache.Preload(new[] { c });
        }
        if (e.PropertyName == nameof(PdfCue.Page)) MediaCache.Preload(new[] { c });
        if (e.PropertyName == nameof(Cue.Summary)) RaiseTitle();
    }

    private void Selected_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Cue.DisplayName)) Raise(nameof(NextCueTitle));
    }

    private void RaiseTitle() => Raise(nameof(WindowTitle));

    private bool ConfirmDiscard()
    {
        if (!IsDirty) return true;
        var r = MessageBox.Show(this, Loc.T("Vil du lagre endringene i showet?"), "ShowCue", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.Cancel) return false;
        if (r == MessageBoxResult.Yes) return Save(false);
        ShowSafety.DeleteAutosave(_showPath);
        return true;
    }

    private bool Save(bool saveAs)
    {
        CommitFocusedTextBox();
        var path = _showPath;
        if (saveAs || path == null)
        {
            var dlg = new SaveFileDialog { Filter = ShowFilter, DefaultExt = ".showcue", FileName = path != null ? Path.GetFileName(path) : "Show.showcue" };
            if (dlg.ShowDialog(this) != true) return false;
            path = dlg.FileName;
        }
        try
        {
            _show.Title = Path.GetFileNameWithoutExtension(path);
            try { ShowSafety.Backup(path); } catch { /* sikkerhetskopi skal aldri hindre lagring */ }
            _show.Save(path);
            ShowSafety.DeleteAutosave(_showPath);
            ShowSafety.DeleteAutosave(path);
            _showPath = path;
            _savedJson = _show.ToJson();
            _settings.LastShowPath = path;
            _settings.Save();
            StatusText = Loc.T("Lagret ") + path;
            RaiseTitle();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Loc.T("Kunne ikke lagre: ") + ex.Message, "ShowCue", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void TryOpen(string path)
    {
        try
        {
            SetShow(ShowDocument.Load(path), path);
            _settings.LastShowPath = path;
            _settings.Save();
            if (string.IsNullOrEmpty(StatusText) || !StatusText.StartsWith(Loc.T("Advarsel"))) StatusText = Loc.T("Åpnet ") + path;
            OfferRecovery();
            OfferRelink();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Loc.T("Kunne ikke åpne showet: ") + ex.Message, "ShowCue", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        CommitFocusedTextBox();
        if (Engine.Active.Count > 0 &&
            MessageBox.Show(this, Loc.T("Cuer spiller fortsatt. Vil du avslutte?"), "ShowCue", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        if (!ConfirmDiscard()) { e.Cancel = true; return; }
        // Sikkerhetsnett: henger en lyd- eller videodriver under avslutningen, avsluttes prosessen likevel etter 8 sekunder.
        new Thread(() => { Thread.Sleep(8000); Environment.Exit(0); }) { IsBackground = true, Name = "Avslutningsvakt" }.Start();
        ShowSafety.DeleteAutosave(_showPath);
        _settings.Save();
        _patchWindow?.Close();
        _panelWindow?.Close();
        _loudnessCancel?.Cancel();
        Remote.Dispose();
        Engine.Dispose();
    }

    // ==================================================================
    //  GO / stopp
    // ==================================================================
    private void Go()
    {
        if ((DateTime.Now - _lastGo).TotalMilliseconds < _settings.DoubleGoProtectionMs) return;
        _lastGo = DateTime.Now;

        var cue = SelectedCue;
        if (cue == null) return;
        int idx = Cues.IndexOf(cue);
        if (cue.Disabled)
        {
            // Hopp til neste aktive cue og kjør den.
            var next = Cues.Skip(idx + 1).FirstOrDefault(c => !c.Disabled);
            if (next == null) return;
            cue = next;
            idx = Cues.IndexOf(cue);
        }
        Engine.Fire(cue);
        int nextIdx = CueEngine.NextPlayheadIndex(Cues, idx);
        SelectedCue = nextIdx < Cues.Count ? Cues[nextIdx] : null;
    }

    private void Panic()
    {
        var hard = (DateTime.Now - _lastPanic).TotalSeconds < 1.0;
        _lastPanic = DateTime.Now;
        Engine.StopAll(hard ? 0 : _settings.PanicFadeTime);
        StatusText = hard ? Loc.T("Alt stoppet umiddelbart.") : Loc.F("Stopper alt (fade {0:0.#} s). Trykk Esc igjen for umiddelbar stopp.", _settings.PanicFadeTime);
    }

    private static double FadeOutOf(Cue c) => c switch
    {
        AudioCue a => a.FadeOut,
        VisualCue v => v.FadeOut,
        PlaylistCue p => p.FadeOut,
        _ => 0
    };

    private void ToggleHotCue(Cue cue)
    {
        if (cue.IsRunning && cue is AudioCue or VisualCue or PlaylistCue) Engine.Stop(cue, FadeOutOf(cue));
        else Engine.Fire(cue);
    }

    // ==================================================================
    //  Tastatur
    // ==================================================================
    private void HandleKey(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        var focused = Keyboard.FocusedElement;

        if (key == Key.Escape)
        {
            if (Remote.IsLearning) { Remote.CancelLearn(); LearnText = ""; }
            Panic();
            if (focused is TextBox) CueGrid.Focus();
            e.Handled = true;
            return;
        }
        if (focused == HotkeyBox) return; // tastefangeren håndterer selv

        bool typing = focused is TextBox { IsReadOnly: false };
        bool inCombo = focused is ComboBox or ComboBoxItem;

        if (mods == ModifierKeys.Control)
        {
            switch (key)
            {
                case Key.S: Save(false); e.Handled = true; return;
                case Key.O: Open_Click(this, new RoutedEventArgs()); e.Handled = true; return;
                case Key.N: New_Click(this, new RoutedEventArgs()); e.Handled = true; return;
                case Key.L: IsShowMode = !IsShowMode; e.Handled = true; return;
                case Key.P: Engine.TogglePause(); e.Handled = true; return;
                case Key.K: ButtonPanel_Click(this, new RoutedEventArgs()); e.Handled = true; return;
                case Key.OemPlus or Key.Add: SetZoom(1); e.Handled = true; return;
                case Key.OemMinus or Key.Subtract: SetZoom(-1); e.Handled = true; return;
            }
            if (!typing)
            {
                switch (key)
                {
                    case Key.Z: Undo(); e.Handled = true; return;
                    case Key.Y: Redo(); e.Handled = true; return;
                    case Key.D: Duplicate_Click(this, new RoutedEventArgs()); e.Handled = true; return;
                    case Key.Up: MoveSelected(-1); e.Handled = true; return;
                    case Key.Down: MoveSelected(1); e.Handled = true; return;
                    case >= Key.D0 and <= Key.D9:
                        AddCue(new[] { "goto", "audio", "video", "image", "pdf", "fade", "stop", "osc", "midi", "playlist" }[key - Key.D0]);
                        e.Handled = true; return;
                }
            }
        }

        if (mods == (ModifierKeys.Control | ModifierKeys.Shift) && !typing)
        {
            switch (key)
            {
                case Key.Z: Redo(); e.Handled = true; return;
                case Key.M: AddCue("memo"); e.Handled = true; return;
                case Key.N: AddCue("network"); e.Handled = true; return;
                case Key.T: AddCue("timecode"); e.Handled = true; return;
                case Key.L: AddCue("light"); e.Handled = true; return;
                case Key.I: AddCue("input"); e.Handled = true; return;
                case Key.W: AddCue("web"); e.Handled = true; return;
            }
        }

        if (typing)
        {
            if (key == Key.Enter && focused is TextBox { AcceptsReturn: false })
            {
                CommitFocusedTextBox();
                CueGrid.Focus();
                e.Handled = true;
                return;
            }
            // F-tast-hurtigtaster virker også mens man skriver.
            if (key is >= Key.F1 and <= Key.F24 && mods == ModifierKeys.None && FireHotkey(key)) e.Handled = true;
            return;
        }

        if (mods != ModifierKeys.None) return;

        switch (key)
        {
            case Key.Space:
                Go();
                e.Handled = true;
                return;
            case Key.Up when !inCombo:
                MovePlayhead(-1); e.Handled = true; return;
            case Key.Down when !inCombo:
                MovePlayhead(1); e.Handled = true; return;
            case Key.Home when !inCombo:
                SelectedCue = Cues.FirstOrDefault(); e.Handled = true; return;
            case Key.Delete:
                Delete_Click(this, new RoutedEventArgs()); e.Handled = true; return;
        }

        if (FireHotkey(key)) e.Handled = true;
    }

    private bool FireHotkey(Key key) => KeyName(key) is string name && FireHotkey(name);

    /// <summary>Start (eller stopp, hvis den spiller) cuen med denne hurtigtasten.</summary>
    private bool FireHotkey(string name)
    {
        var cue = Cues.FirstOrDefault(c => c.Hotkey == name && !c.Disabled);
        if (cue == null) return false;
        ToggleHotCue(cue);
        return true;
    }

    // ==================================================================
    //  Fjernstyring (OSC / MIDI)
    // ==================================================================
    private Cue? FindCue(string number) =>
        Cues.FirstOrDefault(c => string.Equals(c.Number, number.Trim(), StringComparison.OrdinalIgnoreCase));

    void IShowControl.Go() => Go();
    void IShowControl.StopAll() => Engine.StopAll(_settings.PanicFadeTime);
    void IShowControl.Panic() => Panic();
    void IShowControl.HardStop() => Engine.StopAll(0);
    void IShowControl.MovePlayhead(int delta) => MovePlayhead(delta);

    bool IShowControl.SelectCue(string number)
    {
        var c = FindCue(number);
        if (c == null) return false;
        SelectedCue = c;
        return true;
    }

    bool IShowControl.StartCue(string number)
    {
        var c = FindCue(number);
        if (c == null) return false;
        Engine.Fire(c);
        return true;
    }

    bool IShowControl.StopCue(string number)
    {
        var c = FindCue(number);
        if (c == null) return false;
        Engine.Stop(c, FadeOutOf(c));
        return true;
    }

    bool IShowControl.GoCue(string number)
    {
        var c = FindCue(number);
        if (c == null) return false;
        SelectedCue = c;
        _lastGo = DateTime.MinValue; // eksplisitt cue: ingen dobbel-GO-sperre
        Go();
        return true;
    }

    bool IShowControl.FireHotkey(string key) => FireHotkey(key);

    int IShowControl.FireMidiTrigger(string trigger)
    {
        var cues = Cues.Where(c => c.MidiTrigger == trigger && !c.Disabled).ToList();
        foreach (var c in cues) ToggleHotCue(c);
        return cues.Count;
    }

    void IShowControl.RemoteLog(string text) => StatusText = text;

    void IShowControl.SetPaused(bool? paused)
    {
        if (paused == null) Engine.TogglePause();
        else if (paused.Value) Engine.PauseAll();
        else Engine.ResumeAll();
    }

    bool IShowControl.ReleaseCue(string number) => FindCue(number) is { } c && Engine.Release(c);

    int IShowControl.FireTimecodeTriggers(long previousFrame, long frame, TimecodeRate rate)
    {
        var due = Cues.Where(c => !c.Disabled && Timecode.TryParse(c.TimecodeTrigger, out var t)
                                  && Timecode.Crossed(previousFrame, frame, t.ToFrame(rate), rate)).ToList();
        foreach (var c in due) Engine.Fire(c);
        return due.Count;
    }

    private static string? KeyName(Key k)
    {
        if (k is >= Key.A and <= Key.Z) return k.ToString();
        if (k is >= Key.D0 and <= Key.D9) return ((int)(k - Key.D0)).ToString();
        if (k is >= Key.NumPad0 and <= Key.NumPad9) return "NUM" + (int)(k - Key.NumPad0);
        if (k is >= Key.F1 and <= Key.F24) return k.ToString();
        return null;
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (SelectedCue == null || !IsEditable) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Escape) return; // panikk håndteres av vinduet
        if (key is Key.Back or Key.Delete) { SelectedCue.Hotkey = ""; e.Handled = true; return; }
        if (key is Key.Tab or Key.Enter) { CueGrid.Focus(); e.Handled = true; return; }
        var name = KeyName(key);
        if (name != null)
        {
            var other = Cues.FirstOrDefault(c => c != SelectedCue && c.Hotkey == name);
            if (other != null) { other.Hotkey = ""; StatusText = Loc.F("Hurtigtast {0} flyttet fra cue {1}.", name, other.Number); }
            SelectedCue.Hotkey = name;
            CueGrid.Focus();
        }
        e.Handled = true;
    }

    private void MovePlayhead(int delta)
    {
        if (Cues.Count == 0) return;
        int idx = SelectedCue != null ? Cues.IndexOf(SelectedCue) : (delta > 0 ? -1 : Cues.Count);
        idx = Math.Clamp(idx + delta, 0, Cues.Count - 1);
        SelectedCue = Cues[idx];
    }

    private void CommitFocusedTextBox()
    {
        if (Keyboard.FocusedElement is TextBox tb)
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }

    // ==================================================================
    //  Legge til / redigere cues
    // ==================================================================
    private string NextNumber()
    {
        double max = 0;
        foreach (var c in Cues)
            if (double.TryParse(c.Number, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n))
                max = Math.Max(max, n);
        return ((int)Math.Floor(max) + 1).ToString();
    }

    private int InsertIndex()
    {
        var selected = CueGrid.SelectedItems.Cast<Cue>().ToList();
        if (selected.Count == 0) return Cues.Count;
        return selected.Max(c => Cues.IndexOf(c)) + 1;
    }

    private void InsertCues(IList<Cue> newCues, int at)
    {
        if (!IsEditable) return;
        foreach (var c in newCues)
        {
            if (string.IsNullOrEmpty(c.Number)) c.Number = NextNumber();
            Cues.Insert(Math.Min(at++, Cues.Count), c);
        }
        if (newCues.Count > 0)
        {
            CueGrid.SelectedItems.Clear();
            SelectedCue = newCues[newCues.Count - 1];
        }
    }

    private void AddCue(string type)
    {
        if (!IsEditable) return;
        CommitFocusedTextBox();
        var at = InsertIndex();
        var target = SelectedCue;
        List<Cue> list = type switch
        {
            // Media-cuer lages tomme; fila velges i panelet til høyre (Bla), eller ved å dra en fil inn på cuen.
            "audio" => new() { new AudioCue() },
            "video" => new() { new VideoCue() },
            "image" => new() { new ImageCue() },
            "pdf" => new() { new PdfCue() },
            "playlist" => new() { new PlaylistCue() },
            "goto" => new() { new GotoCue() },
            "fade" => new() { new FadeCue { Target = target is AudioCue or VisualCue ? target.Number : "" } },
            "stop" => new() { new StopCue() },
            "osc" => new() { new OscCue() },
            "memo" => new() { new MemoCue() },
            "web" => new() { new WebCue() },
            "timecode" => new() { new TimecodeCue { Device = MidiDevices.FirstOrDefault() ?? "" } },
            "light" => new() { new LightCue() },
            "input" => new() { new InputCue { FileChannels = Math.Max(1, AudioInput.ProbeChannels(_settings.InputDeviceId)) } },
            "network" => new() { new NetworkCue() },
            _ => new() { new MidiCue { Device = MidiDevices.FirstOrDefault() ?? "" } },
        };
        InsertCues(list, at);
    }

    private IEnumerable<string> PickFiles(string filter, bool multi)
    {
        var dlg = new OpenFileDialog { Filter = filter, Multiselect = multi };
        return dlg.ShowDialog(this) == true ? dlg.FileNames : Array.Empty<string>();
    }

    private Cue? CreateCueForFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (AudioExt.Contains(ext))
        {
            var (dur, ch) = AudioEngine.Probe(path);
            return new AudioCue { FilePath = path, FileDuration = dur, FileChannels = ch };
        }
        if (VideoExt.Contains(ext))
        {
            var v = new VideoCue { FilePath = path };
            ProbeVideoAudio(v);
            return v;
        }
        if (ImageExt.Contains(ext)) return new ImageCue { FilePath = path };
        if (PdfExt.Contains(ext)) return new PdfCue { FilePath = path };
        return null;
    }

    private void AddCue_Click(object sender, RoutedEventArgs e) => AddCue((string)((FrameworkElement)sender).Tag);

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        CommitFocusedTextBox();
        var selected = CueGrid.SelectedItems.Cast<Cue>().OrderBy(c => Cues.IndexOf(c)).ToList();
        if (selected.Count == 0) return;
        var at = InsertIndex();
        var copies = selected.Select(c => { var k = c.Clone(); k.Number = ""; k.Hotkey = ""; return k; }).ToList();
        InsertCues(copies, at);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (!IsEditable) return;
        var selected = CueGrid.SelectedItems.Cast<Cue>().ToList();
        if (selected.Count == 0) return;
        var text = selected.Count == 1 ? Loc.F("Slette cue {0} «{1}»?", selected[0].Number, selected[0].DisplayName) : Loc.F("Slette {0} cuer?", selected.Count);
        if (MessageBox.Show(this, text, "ShowCue", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        int idx = selected.Min(c => Cues.IndexOf(c));
        foreach (var c in selected)
        {
            Engine.Stop(c, 0);
            Cues.Remove(c);
        }
        SelectedCue = Cues.Count == 0 ? null : Cues[Math.Min(idx, Cues.Count - 1)];
    }

    private void MoveSelected(int delta)
    {
        if (SelectedCue == null || !IsEditable) return;
        var c = SelectedCue;
        int i = Cues.IndexOf(c), j = i + delta;
        if (j < 0 || j >= Cues.Count) return;
        Cues.Move(i, j);
        SelectedCue = null;
        SelectedCue = c;
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveSelected(1);

    private void Renumber_Click(object sender, RoutedEventArgs e)
    {
        if (!IsEditable) return;
        if (MessageBox.Show(this, Loc.T("Gi alle cuer nye nummer (1, 2, 3)?\nFade-, stopp- og gå til-cuer oppdateres automatisk."), "ShowCue",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < Cues.Count; i++)
        {
            var n = (i + 1).ToString();
            if (!string.IsNullOrEmpty(Cues[i].Number)) map.TryAdd(Cues[i].Number, n);
            Cues[i].Number = n;
        }
        foreach (var f in Cues.OfType<FadeCue>()) if (map.TryGetValue(f.Target, out var t)) f.Target = t;
        foreach (var s in Cues.OfType<StopCue>()) if (map.TryGetValue(s.Target, out var t)) s.Target = t;
        foreach (var g in Cues.OfType<GotoCue>()) if (map.TryGetValue(g.Target, out var t)) g.Target = t;
    }

    private void PdfNextPage_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCue is not PdfCue pc) return;
        var copy = (PdfCue)pc.Clone();
        copy.Number = "";
        copy.Hotkey = "";
        copy.Page = pc.Page + 1;
        if (!string.IsNullOrEmpty(pc.Name)) copy.Name = "";
        InsertCues(new List<Cue> { copy }, Cues.IndexOf(pc) + 1);
    }

    private void BrowseFile_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not IFileCue fc) return;
        var file = PickFiles(FilterFor(fc), false).FirstOrDefault();
        if (file != null) fc.FilePath = file;
    }

    // ---------- Dra og slipp ----------
    private void CueGrid_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = IsEditable && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void CueGrid_Drop(object sender, DragEventArgs e)
    {
        if (!IsEditable) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        var row = FindParent<DataGridRow>(e.OriginalSource as DependencyObject);

        // Én fil sluppet på en media-cue uten fil (av samme type): gi cuen fila.
        if (files.Length == 1 && row?.Item is IFileCue { FilePath: "" } empty && CreateCueForFile(files[0])?.GetType() == empty.GetType())
        {
            empty.FilePath = files[0];
            StatusText = Loc.F("La fila inn i cue {0}.", ((Cue)empty).Number);
            return;
        }
        var cues = files.OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase).Select(CreateCueForFile).OfType<Cue>().ToList();
        if (cues.Count == 0) { StatusText = Loc.T("Ingen støttede filer i det du slapp."); return; }

        // Sett inn under raden det ble sluppet på, ellers nederst.
        int at = Cues.Count;
        if (row?.Item is Cue c) at = Cues.IndexOf(c) + 1;
        InsertCues(cues, at);
        StatusText = Loc.F("La til {0} cue(s).", cues.Count);
    }

    private static T? FindParent<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null && d is not T) d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }

    private void CueGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Dobbeltklikk på en rad kjører den (som test) uten å flytte markøren.
        if (FindParent<DataGridRow>(e.OriginalSource as DependencyObject)?.Item is Cue c) Engine.Fire(c);
    }

    // ==================================================================
    //  Knapper
    // ==================================================================
    private void Go_Click(object sender, RoutedEventArgs e) => Go();
    private void Pause_Click(object sender, RoutedEventArgs e) => Engine.TogglePause();

    private void ReleaseSelected_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCue != null && !Engine.Release(SelectedCue)) StatusText = Loc.T("Cuen looper ikke (eller spiller ikke).");
    }

    // ---------- Spilleliste ----------
    private static int MaxChannels(IEnumerable<string> files) => Math.Max(1, files.Select(f => AudioEngine.Probe(f).Channels).DefaultIfEmpty(2).Max());

    private static (PlaylistCue? cue, ListBox? box) PlaylistOf(object sender) =>
        (((FrameworkElement)sender).DataContext as PlaylistCue, ((FrameworkElement)sender).Tag as ListBox);

    private void PlaylistAdd_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not PlaylistCue p) return;
        var files = PickFiles(FilterFor("audio"), true).ToList();
        if (files.Count == 0) return;
        p.Files = p.Files.Concat(files).ToList();
        p.FileChannels = Math.Max(p.FileChannels, MaxChannels(files));
    }

    private void PlaylistRemove_Click(object sender, RoutedEventArgs e)
    {
        var (p, box) = PlaylistOf(sender);
        if (p == null || box == null || box.SelectedIndex < 0) return;
        int i = box.SelectedIndex;
        p.Files = p.Files.Where((_, j) => j != i).ToList();
        box.SelectedIndex = Math.Min(i, p.Files.Count - 1);
    }

    private void PlaylistUp_Click(object sender, RoutedEventArgs e) => MovePlaylistItem(sender, -1);
    private void PlaylistDown_Click(object sender, RoutedEventArgs e) => MovePlaylistItem(sender, 1);

    private void MovePlaylistItem(object sender, int delta)
    {
        var (p, box) = PlaylistOf(sender);
        if (p == null || box == null) return;
        int i = box.SelectedIndex, j = i + delta;
        if (i < 0 || j < 0 || j >= p.Files.Count) return;
        var list = p.Files.ToList();
        (list[i], list[j]) = (list[j], list[i]);
        p.Files = list;
        box.SelectedIndex = j;
    }
    private void Panic_Click(object sender, RoutedEventArgs e) => Panic();
    private void TestCue_Click(object sender, RoutedEventArgs e) { CommitFocusedTextBox(); if (SelectedCue != null) Engine.Fire(SelectedCue); }
    private void StopSelected_Click(object sender, RoutedEventArgs e) { if (SelectedCue != null) Engine.Stop(SelectedCue, FadeOutOf(SelectedCue)); }

    private void SeekActive_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ActiveCue a, Tag: string t })
            a.Nudge(double.Parse(t, CultureInfo.InvariantCulture));
    }

    private void StopActive_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is ActiveCue a) a.Stop(FadeOutOf(a.Cue));
    }

    private void HotButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is Cue c) ToggleHotCue(c);
    }

    private void CloseMemo_Click(object sender, RoutedEventArgs e) => MemoText = "";

    private void About_Click(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();

    private void TimecodeTrigger_LostFocus(object sender, RoutedEventArgs e)
    {
        var box = (TextBox)sender;
        if (SelectedCue == null) return;
        var text = box.Text.Trim();
        if (text.Length > 0 && !Timecode.TryParse(text, out _))
        {
            StatusText = Loc.T("Tidskode må skrives tt:mm:ss:ff, f.eks. 01:00:10:00.");
            box.Text = SelectedCue.TimecodeTrigger;
            return;
        }
        SelectedCue.TimecodeTrigger = text;
        box.Text = SelectedCue.TimecodeTrigger;
    }

    // ==================================================================
    //  Knappepanel, mediefiler og lydstyrke
    // ==================================================================
    private void ButtonPanel_Click(object sender, RoutedEventArgs e)
    {
        if (_panelWindow != null) { _panelWindow.Activate(); return; }
        _panelWindow = new ButtonPanelWindow(_settings.ButtonPanelColumns, ToggleHotCue, () => Engine.TogglePause(), Panic,
            cols => { _settings.ButtonPanelColumns = cols; _settings.Save(); }) { Owner = this };
        _panelWindow.SetCues(_show.Cues);
        _panelWindow.Closed += (_, _) => _panelWindow = null;
        _panelWindow.Show();
    }

    private void Menu_Click(object sender, RoutedEventArgs e)
    {
        var b = (Button)sender;
        b.ContextMenu.PlacementTarget = b;
        b.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        b.ContextMenu.IsOpen = true;
    }

    /// <summary>Etter åpning: mangler det mediefiler, tilbys det å lete etter dem i en mappe.</summary>
    private void OfferRelink()
    {
        var missing = MediaTools.Missing(Cues);
        if (missing.Count == 0) return;
        var text = missing.Count == 1 ? Loc.F("Fant ikke mediefila «{0}».", MediaTools.FileName(missing[0])) : Loc.F("Fant ikke {0} mediefiler.", missing.Count);
        if (MessageBox.Show(this, text + Loc.T("\n\nVil du velge en mappe å lete i (undermapper tas med)?"), "ShowCue",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            Relink();
        else StatusText = Loc.F("Advarsel: {0} mediefil(er) mangler. Fil → Finn manglende mediefiler.", missing.Count);
    }

    private void Relink_Click(object sender, RoutedEventArgs e)
    {
        if (!IsEditable) return;
        if (MediaTools.Missing(Cues).Count == 0) { StatusText = Loc.T("Ingen mediefiler mangler."); return; }
        Relink();
    }

    private void Relink()
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("Velg mappa mediefilene ligger i") };
        if (_showPath != null) dlg.InitialDirectory = Path.GetDirectoryName(_showPath);
        if (dlg.ShowDialog(this) != true) return;
        var found = MediaTools.Relink(Cues, dlg.FolderName);
        foreach (var a in Cues.OfType<AudioCue>().Where(a => a.FileDuration <= 0 && File.Exists(a.FilePath)))
            (a.FileDuration, a.FileChannels) = AudioEngine.Probe(a.FilePath);
        foreach (var c in Cues.Where(c => c is IFileCue f && File.Exists(f.FilePath))) c.Error = null;
        MediaCache.Clear();
        MediaCache.Preload(Cues);
        var left = MediaTools.Missing(Cues).Count;
        StatusText = Loc.F("Fant {0} fil(er).", found) + (left > 0 ? Loc.F(" {0} mangler fortsatt.", left) : Loc.T(" Alle mediefiler er på plass."));
    }

    private void Collect_Click(object sender, RoutedEventArgs e)
    {
        CommitFocusedTextBox();
        var dlg = new OpenFolderDialog { Title = Loc.T("Velg (eller lag) en mappe for showet og mediefilene") };
        if (dlg.ShowDialog(this) != true) return;
        var name = _showPath != null ? Path.GetFileNameWithoutExtension(_showPath) : Loc.T("Show");
        try
        {
            var r = MediaTools.Collect(_show, dlg.FolderName, name);
            var msg = Loc.F("Showet og {0} mediefil(er) er kopiert til\n{1}\n\nShowfil: {2}, mediefiler i undermappa «Media».", r.Copied, dlg.FolderName, Path.GetFileName(r.ShowPath))
                      + (r.Missing.Count > 0 ? Loc.F("\n\n{0} fil(er) manglet og ble ikke kopiert.", r.Missing.Count) : "")
                      + Loc.T("\n\nVil du åpne den samlede kopien nå?");
            if (MessageBox.Show(this, msg, Loc.T("Samle show"), MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes && ConfirmDiscard())
                TryOpen(r.ShowPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Loc.T("Kunne ikke samle showet: ") + ex.Message, "ShowCue", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Normalize_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCue is AudioCue a) Normalize(new[] { a });
    }

    private void NormalizeSelected_Click(object sender, RoutedEventArgs e) => Normalize(CueGrid.SelectedItems.OfType<AudioCue>().ToList());
    private void NormalizeAll_Click(object sender, RoutedEventArgs e) => Normalize(Cues.OfType<AudioCue>().ToList());

    /// <summary>Måler lydstyrken i bakgrunnen og setter nivået så cuene treffer målet i innstillingene.</summary>
    private async void Normalize(IReadOnlyList<AudioCue> cues)
    {
        if (!IsEditable) return;
        CommitFocusedTextBox();
        cues = cues.Where(c => File.Exists(c.FilePath)).ToList();
        if (cues.Count == 0) { StatusText = Loc.T("Ingen lyd-cuer med fil å normalisere."); return; }
        _loudnessCancel?.Cancel();
        var cancel = _loudnessCancel = new CancellationTokenSource();
        var target = _settings.LoudnessTarget;
        int done = 0, silent = 0;
        foreach (var c in cues)
        {
            StatusText = Loc.F("Måler lydstyrke {0}/{1}: {2} …", ++done, cues.Count, c.DisplayName);
            var (path, start, end) = (c.FilePath, c.StartTime, c.EndTime);
            double? lufs;
            try { lufs = await Task.Run(() => LoudnessMeter.MeasureFile(path, start, end, cancel.Token)); }
            catch (Exception ex) { c.Error = Loc.T("Lydstyrke: ") + ex.Message; continue; }
            if (cancel.IsCancellationRequested) return;
            c.Loudness = lufs.HasValue ? Math.Round(lufs.Value, 1) : null;
            if (lufs is double l) c.VolumeDb = LoudnessMeter.GainFor(l, target);
            else silent++;
        }
        StatusText = Loc.F("Normalisert {0} lyd-cue(r) til {1:0.#} LUFS.", cues.Count - silent, target) + (silent > 0 ? Loc.F(" {0} var stille eller for korte.", silent) : "");
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        SetShow(new ShowDocument(), null);
        StatusText = Loc.T("Nytt show.");
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var dlg = new OpenFileDialog { Filter = ShowFilter };
        if (dlg.ShowDialog(this) == true) TryOpen(dlg.FileName);
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Save(false);
    private void SaveAs_Click(object sender, RoutedEventArgs e) => Save(true);
    private void ToggleOutput_Click(object sender, RoutedEventArgs e) => Engine.ToggleOutput();

    private static string AudioKey(AppSettings s) =>
        $"{s.AudioDriver}|{s.WasapiDeviceId}|{s.AsioDriverName}|{s.AsioMaxChannels}|{s.SampleRate}|{s.LatencyMs}";

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow(_settings.Copy(), Remote) { Owner = this };
        if (win.ShowDialog() != true) return;
        win.Result.LastShowPath = _settings.LastShowPath;
        win.Result.Buses = _settings.Buses; // patchen redigeres i eget vindu

        // Bare endringer i lydoppsettet krever omstart av lyden (som stopper alt som spiller).
        bool restartAudio = AudioKey(win.Result) != AudioKey(_settings);
        if (restartAudio && Engine.Active.Count > 0 &&
            MessageBox.Show(this, Loc.T("Nytt lydoppsett stopper alt som spiller. Fortsette?"), "ShowCue",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            win.Result.AudioDriver = _settings.AudioDriver; win.Result.WasapiDeviceId = _settings.WasapiDeviceId;
            win.Result.AsioDriverName = _settings.AsioDriverName; win.Result.AsioMaxChannels = _settings.AsioMaxChannels;
            win.Result.SampleRate = _settings.SampleRate; win.Result.LatencyMs = _settings.LatencyMs;
            restartAudio = false;
        }
        bool languageChanged = win.Result.Language != _settings.Language;
        _settings = win.Result;
        _settings.Save();
        if (languageChanged)
            MessageBox.Show(this, Loc.T("Det nye språket brukes neste gang ShowCue startes."), "ShowCue", MessageBoxButton.OK, MessageBoxImage.Information);
        Engine.ApplySettings(_settings, restartAudio);
        Remote.Apply(_settings);
        ApplyFeedbackSettings();
        if (ThemeManager.Current != _settings.Theme) ThemeManager.Apply(_settings.Theme);
        Raise(nameof(Zoom));
        Raise(nameof(ListTextScale));
        MidiDevices = MidiEngine.GetDevices();
        Raise(nameof(MidiDevices));
    }

    // ---- Zoom og tekststørrelse ----

    public double Zoom => _settings.Zoom;
    public double ListTextScale => _settings.ListTextScale;

    /// <summary>Zoom inn (<paramref name="direction"/> &gt; 0), ut (&lt; 0) eller tilbake til vanlig størrelse (0).</summary>
    private void SetZoom(int direction)
    {
        _settings.Zoom = direction == 0 ? 1 : UiZoom.Step(_settings.Zoom, direction);
        _settings.Save();
        Raise(nameof(Zoom));
        StatusText = Loc.F("Zoom: {0}", UiZoom.Percent(_settings.Zoom));
    }

    private void Zoom_Click(object sender, RoutedEventArgs e) =>
        SetZoom(int.Parse((string)((FrameworkElement)sender).Tag, CultureInfo.InvariantCulture));

    private void ListScaleMenu_Opened(object sender, RoutedEventArgs e)
    {
        ListScaleMenu.Items.Clear();
        foreach (var (label, scale) in UiZoom.ListSizes)
        {
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = Math.Abs(scale - _settings.ListTextScale) < 0.01 };
            item.Click += (_, _) =>
            {
                _settings.ListTextScale = scale;
                _settings.Save();
                Raise(nameof(ListTextScale));
            };
            ListScaleMenu.Items.Add(item);
        }
    }

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        _settings.Theme = ThemeManager.Next(ThemeManager.Current);
        ThemeManager.Apply(_settings.Theme);
        _settings.Save();
    }

    private void CueColor_Click(object sender, RoutedEventArgs e)
    {
        if (!IsEditable) return;
        if (((FrameworkElement)sender).Tag is not CueColor color) return;
        var targets = CueGrid.SelectedItems.Cast<Cue>().ToList();
        if (targets.Count == 0 && SelectedCue != null) targets.Add(SelectedCue);
        foreach (var c in targets) c.Color = color;
    }

    // ---------- Videolyd ----------
    /// <summary>Finn ut (i bakgrunnen) om videoen har lyd og hvor mange kanaler.</summary>
    private void ProbeVideoAudio(VideoCue v)
    {
        var path = v.FilePath;
        if (!File.Exists(path)) return;
        Task.Run(() => VideoAudio.Probe(path)).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully && v.FilePath == path)
            {
                v.FileChannels = t.Result.Channels;
                if (v.KnownDuration <= 0 && t.Result.Duration > 0) v.KnownDuration = t.Result.Duration;
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private async void ExtractVideoAudio_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCue is not VideoCue v || !File.Exists(v.FilePath)) return;
        var btn = (Button)sender;
        btn.IsEnabled = false;
        try
        {
            var progress = new Progress<double>(p => StatusText = Loc.F("Pakker ut lyd fra {0} … {1:P0}", Path.GetFileName(v.FilePath), p));
            var wav = await VideoAudio.ExtractAsync(v.FilePath, null, progress);
            var (dur, ch) = AudioEngine.Probe(wav);
            var audio = new AudioCue
            {
                FilePath = wav,
                FileDuration = dur,
                FileChannels = ch,
                Name = v.DisplayName + Loc.T(" (lyd)"),
                VolumeDb = v.VolumeDb,
                FadeIn = v.FadeIn,
                FadeOut = v.FadeOut,
                Loop = v.Loop,
                // Var videolyden slått av, får lyd-cuen standard routing.
                Routes = v.Routes is { Count: > 0 } ? v.Routes.Select(r => new AudioRoute(r.Source, r.Bus, r.GainDb)).ToList() : null,
                Color = v.Color,
            };
            // Videoen starter lyd-cuen samtidig, og blir selv stum.
            audio.ContinueMode = v.ContinueMode;
            v.ContinueMode = ContinueMode.AutoContinue;
            v.Routes = new List<AudioRoute>();
            InsertCues(new List<Cue> { audio }, Cues.IndexOf(v) + 1);
            SelectedCue = v;
            StatusText = Loc.F("Lyden er lagret som {0} og lagt inn som cue {1} (starter samtidig med videoen).", Path.GetFileName(wav), audio.Number);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Loc.T("Kunne ikke pakke ut lyden: ") + ex.Message, "ShowCue", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            btn.IsEnabled = true;
        }
    }

    // ---------- OSC-status ut ----------
    private void ApplyFeedbackSettings()
    {
        _feedback = _settings.OscFeedbackEnabled
            ? OscFeedback.ForTarget(_settings.OscFeedbackHost, _settings.OscFeedbackPort)
            : null;
        if (_feedback != null) _feedback.Format = _settings.OscFeedbackFormat;
        Raise(nameof(RemoteStatus));
    }

    private ShowStatus BuildStatus()
    {
        var next = SelectedCue;
        var playing = Engine.Active
            .Where(a => !a.Done)
            .Select(a => new PlayingCue(a.Cue.Number, a.Cue.DisplayName, a.Started ? a.Elapsed : 0,
                a.Duration > 0 ? Math.Max(0, a.Duration - a.Elapsed) : 0, a.Duration > 0, !a.Started))
            .ToList();
        return new ShowStatus(
            _showPath != null ? Path.GetFileNameWithoutExtension(_showPath) : Loc.T("Uten navn"),
            next?.Number ?? "", next?.DisplayName ?? "", next?.Notes ?? "",
            next != null ? Cues.IndexOf(next) + 1 : 0, Cues.Count, playing);
    }

    void IShowControl.RefreshFeedback() => _feedback?.RequestRefresh();

    private void Patch_Click(object sender, RoutedEventArgs e)
    {
        if (_patchWindow != null) { _patchWindow.Activate(); return; }
        _patchWindow = new PatchWindow(Engine.Audio, _settings.Buses, OnPatchChanged) { Owner = this };
        _patchWindow.Closed += (_, _) =>
        {
            _patchWindow = null;
            _settings.Save();
            Activate();
        };
        _patchWindow.Show();
    }

    private void OnPatchChanged()
    {
        Engine.Audio.ApplyPatch(_settings.Buses);
        Raise(nameof(Buses));
    }

    // ---------- MIDI-trigger per cue ----------
    private void MidiLearn_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCue == null) return;
        if (!Remote.MidiOpen)
        {
            MessageBox.Show(this, Loc.T("Velg en MIDI-inngang under Innstillinger → MIDI inn først."), "ShowCue", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var cue = SelectedCue;
        LearnText = Loc.T("Venter på MIDI … trykk en tast/knapp på kontrolleren");
        Remote.BeginLearn(t =>
        {
            LearnText = "";
            var other = Cues.FirstOrDefault(c => c != cue && c.MidiTrigger == t);
            cue.MidiTrigger = t;
            StatusText = Loc.F("Cue {0} startes nå av {1}", cue.Number, MidiTriggers.Describe(t)) + (other != null ? Loc.F(" (cue {0} har samme trigger)", other.Number) : "");
        });
    }

    private void MidiClear_Click(object sender, RoutedEventArgs e)
    {
        Remote.CancelLearn();
        LearnText = "";
        if (SelectedCue != null) SelectedCue.MidiTrigger = "";
    }

    // ==================================================================
    //  Angre / gjør om
    // ==================================================================
    private void ScheduleUndo()
    {
        if (_restoring) return;
        _undoTimer.Stop();
        _undoTimer.Start();
    }

    /// <summary>Husk showet slik det er nå som ett angre-steg.</summary>
    private void FlushUndo()
    {
        _undoTimer.Stop();
        if (_history.Record(_show.ToJson())) RaiseUndo();
    }

    private void Undo()
    {
        if (!IsEditable) return;
        CommitFocusedTextBox();
        FlushUndo();
        if (_history.Undo() is { } state) { ReplaceCues(state); StatusText = Loc.T("Angret."); }
    }

    private void Redo()
    {
        if (!IsEditable) return;
        CommitFocusedTextBox();
        FlushUndo();
        if (_history.Redo() is { } state) { ReplaceCues(state); StatusText = Loc.T("Gjort om."); }
    }

    /// <summary>Bytt ut cuene med en lagret tilstand. Showfil, innstillinger og det som spiller røres ikke.</summary>
    private void ReplaceCues(string json)
    {
        var doc = System.Text.Json.JsonSerializer.Deserialize<ShowDocument>(json, ShowDocument.JsonOptions)!;
        int idx = SelectedCue != null ? Cues.IndexOf(SelectedCue) : 0;
        _restoring = true;
        try
        {
            while (Cues.Count > 0) Cues.RemoveAt(Cues.Count - 1);
            foreach (var c in doc.Cues) Cues.Add(c);
        }
        finally { _restoring = false; }
        SelectedCue = Cues.Count == 0 ? null : Cues[Math.Clamp(idx, 0, Cues.Count - 1)];
        RaiseUndo();
        RaiseTitle();
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => Redo();

    // ==================================================================
    //  Autolagring og gjenoppretting
    // ==================================================================
    private void Autosave()
    {
        if (!IsDirty) return;
        try { ShowSafety.WriteAutosave(_showPath, _show.ToJson()); }
        catch (Exception ex) { StatusText = Loc.T("Autolagring feilet: ") + ex.Message; }
    }

    /// <summary>Tilby å hente tilbake ulagrede endringer etter krasj eller strømbrudd.</summary>
    private void OfferRecovery()
    {
        if (ShowSafety.PendingAutosave(_showPath) is not { } file) return;
        var when = File.GetLastWriteTime(file).ToString("dd.MM HH:mm");
        var r = MessageBox.Show(this,
            Loc.F("Det finnes ulagrede endringer i «{0}» fra {1} ", (_showPath != null ? Path.GetFileNameWithoutExtension(_showPath) : Loc.T("Uten navn")), when) +
            Loc.T("(programmet ble ikke avsluttet normalt).\n\nVil du hente dem tilbake?"), Loc.T("Gjenopprett"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) { ShowSafety.DeleteAutosave(_showPath); return; }
        try
        {
            ReplaceCues(File.ReadAllText(file));
            FlushUndo();
            StatusText = Loc.T("Ulagrede endringer er hentet tilbake. Husk å lagre.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Loc.T("Kunne ikke lese autolagringen: ") + ex.Message, "ShowCue", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowMode_Click(object sender, RoutedEventArgs e) => IsShowMode = !IsShowMode;

    // ==================================================================
    //  Utgang, måler og klokke
    // ==================================================================
    private void OnOutputCreated(OutputWindow w)
    {
        w.ForwardKey += (_, e) => HandleKey(e);
        PreviewRect.Fill = new VisualBrush(w.LayerHost) { Stretch = Stretch.Uniform };
        // Utgangsvinduet skal ikke stjele fokus fra operatøren.
        Dispatcher.BeginInvoke(() => { Activate(); }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private void OnTick()
    {
        var levels = Engine.Audio.Mixer?.BusLevels ?? Array.Empty<float>();
        float l = levels.Length > 0 ? levels[0] : 0, r = levels.Length > 1 ? levels[1] : l;
        _meterL = Math.Max(MeterMath.ToFraction(l), _meterL - 0.025);
        _meterR = Math.Max(MeterMath.ToFraction(r), _meterR - 0.025);
        MeterLScale.ScaleY = _meterL;
        MeterRScale.ScaleY = _meterR;

        // Status ut over OSC ca. 10 ganger i sekundet (bare endringer sendes).
        if (_feedback != null && ++_feedbackTick % 6 == 0)
        {
            try { _feedback.Update(BuildStatus(), Engine.Now); }
            catch { /* nettverksfeil skal aldri påvirke showet */ }
        }

        var clock = DateTime.Now.ToString("HH:mm:ss");
        if (clock != _lastClock)
        {
            _lastClock = clock;
            ClockText.Text = clock;
            RaiseTitle();
        }
    }
}
