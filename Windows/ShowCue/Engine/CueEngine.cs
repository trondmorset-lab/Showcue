using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Threading;
using ShowCue.Models;
using ShowCue.Views;

namespace ShowCue.Engine;

/// <summary>
/// Kjører cues: pre-wait, auto-continue, auto-follow, stopp og panikk.
/// All logikk her kjører på UI-tråden; bare selve lydmiksingen går på lydtråden.
/// </summary>
public sealed class CueEngine : IDisposable
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _timer;
    private readonly Func<IList<Cue>> _cueList;
    private OutputWindow? _output;
    /// <summary>Utganger for cuer som har valgt en annen skjerm enn standard, per skjerm.</summary>
    private readonly Dictionary<int, OutputWindow> _extraOutputs = new();

    public AudioEngine Audio { get; } = new();
    /// <summary>Egen lydenhet for forhåndslytting (f.eks. hodetelefoner). Kjører bare når en er valgt.</summary>
    public AudioEngine PreviewAudio { get; } = new();
    private string _previewDevice = "";
    /// <summary>Forhåndslytting i bølgeformredigeringen (går gjennom lydpatchen, men er ikke en del av showet).</summary>
    public AudioPreview Preview { get; }
    public MidiEngine Midi { get; } = new();
    public DmxOutput Dmx { get; } = new();
    public AudioInput Input { get; } = new();
    public AppSettings Settings { get; private set; }
    public ObservableCollection<ActiveCue> Active { get; } = new();
    public double Now => _clock.Elapsed.TotalSeconds;

    /// <summary>Utgangsvinduet ble opprettet (brukes til forhåndsvisning og tastevideresending).</summary>
    public event Action<OutputWindow>? OutputCreated;
    /// <summary>En utgang på en annen skjerm enn standard er laget (tastetrykk derfra skal også nå hovedvinduet).</summary>
    public event Action<OutputWindow>? ExtraOutputCreated;
    public event Action? Ticked;
    public event Action<string>? Log;
    /// <summary>En «gå til»-cue vil flytte GO-markøren (cuenummer, og om mål-cuen skal kjøres).</summary>
    public event Action<string, bool>? GotoRequested;
    public event Action? PauseChanged;
    /// <summary>En memo-cue er kjørt: vis teksten til operatøren.</summary>
    public event Action<MemoCue>? MemoShown;

    /// <summary>Alt står stille: lyd, video og tiden for pre-wait og fader.</summary>
    public bool IsPaused { get; private set; }

    public CueEngine(AppSettings settings, Func<IList<Cue>> cueList)
    {
        Settings = settings;
        _cueList = cueList;
        Preview = new AudioPreview(PreviewTarget);
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    /// <summary>Hvor forhåndslyttingen skal spilles (se Innstillinger → Lyd).</summary>
    private AudioPreview.Target? PreviewTarget()
    {
        if (!string.IsNullOrEmpty(Settings.PreviewDeviceId))
            return PreviewAudio.Mixer is { } pm ? new AudioPreview.Target(pm, 1) : null; // aldri til salen hvis hodetelefonene mangler
        if (Audio.Mixer is not { } m) return null;
        int bus = Settings.PreviewBus is > 0 && Settings.PreviewBus <= m.BusCount ? Settings.PreviewBus : 0;
        return new AudioPreview.Target(m, bus);
    }

    /// <summary>Ta i bruk nye innstillinger. Lyden startes bare på nytt (og alt stoppes) hvis lydoppsettet er endret.</summary>
    public void ApplySettings(AppSettings s, bool restartAudio = true)
    {
        Settings = s;
        if (restartAudio)
        {
            StopAll(0);
            Audio.Start(s);
        }
        if (s.PreviewDeviceId != _previewDevice || (restartAudio && s.PreviewDeviceId != ""))
        {
            Preview.Stop();
            _previewDevice = s.PreviewDeviceId;
            if (_previewDevice == "") PreviewAudio.StopOutput();
            else PreviewAudio.Start(new AppSettings
            {
                AudioDriver = AudioDriverType.Wasapi, WasapiDeviceId = _previewDevice, LatencyMs = s.LatencyMs, Buses = PatchBus.DefaultPatch(2),
            }, fallbackToDefault: false);
        }
        // En ekstra utgang på samme skjerm som den nye standarden trengs ikke lenger.
        if (_extraOutputs.Remove(s.VideoScreen, out var dup)) { dup.AllowClose = true; dup.Close(); }
        if (_output != null)
        {
            _output.Configure(s.VideoScreen);
            if (s.VideoScreen >= 0 && !_output.IsVisible) _output.Show();
        }
        else if (s.VideoScreen >= 0)
        {
            EnsureOutput();
        }
    }

    // ------------------------------------------------------------------
    /// <param name="offset">Start så mange sekunder inn i cuen (prøver: «spill fra her»).</param>
    public void Fire(Cue cue, double offset = 0)
    {
        if (IsPaused) ResumeAll(); // GO etter pause fortsetter alt
        var a = Create(cue);
        a.StartOffset = Math.Max(0, offset);
        a.StartAt = Now + (offset > 0 ? 0 : cue.PreWait);
        a.Completed += OnCompleted;
        cue.Error = null;
        cue.ActiveCount++;
        Active.Add(a);
        if (cue.PreWait > 0) cue.IsPreWaiting = true;
        else StartActive(a);
    }

    private ActiveCue Create(Cue cue) => cue switch
    {
        AudioCue c => new AudioActive(c, this),
        PlaylistCue c => new PlaylistActive(c, this),
        SlideshowCue c => new SlideshowActive(c, this),
        VideoCue c => new VideoActive(c, this),
        StillCue c => new StillActive(c, this),
        FadeCue c => new FadeActive(c, this),
        StopCue c => new StopActive(c, this),
        TimecodeCue c => new TimecodeActive(c, this),
        LightCue c => new LightActive(c, this),
        InputCue c => new InputActive(c, this),
        _ => new InstantActive(cue, this),
    };

    private void StartActive(ActiveCue a)
    {
        a.Cue.IsPreWaiting = Active.Any(x => x.Cue == a.Cue && x != a && !x.Started);
        try
        {
            a.Begin();
        }
        catch (Exception ex)
        {
            a.Cue.Error = ex.Message;
            Log?.Invoke(Loc.F("Cue {0}: {1}", a.Cue.Number, ex.Message));
            if (!a.Done) a.Stop(0);
            if (!a.Done) ForceRemove(a);
        }
        if (a.Cue.ContinueMode == ContinueMode.AutoContinue) FireNextAfter(a.Cue);
    }

    private void OnCompleted(ActiveCue a)
    {
        Active.Remove(a);
        a.Cue.ActiveCount--;
        if (!a.Cue.IsRunning) { a.Cue.Progress = 0; a.Cue.IsPreWaiting = false; }
        if (a.Started && !a.WasStopped && a.Cue.ContinueMode == ContinueMode.AutoFollow)
            FireNextAfter(a.Cue);
    }

    private void ForceRemove(ActiveCue a)
    {
        a.Completed -= OnCompleted;
        if (Active.Remove(a)) a.Cue.ActiveCount--;
    }

    private void FireNextAfter(Cue cue)
    {
        var list = _cueList();
        var idx = list.IndexOf(cue);
        if (idx < 0) return;
        for (int i = idx + 1; i < list.Count; i++)
        {
            if (list[i].Disabled) continue;
            Fire(list[i]);
            return;
        }
    }

    /// <summary>Finn neste cue for GO etter at <paramref name="cue"/> er avfyrt (hopper over kjeder).</summary>
    public static int NextPlayheadIndex(IList<Cue> list, int firedIndex)
    {
        int i = firedIndex;
        // Hopp over cues som blir trigget automatisk av forrige cue.
        while (i < list.Count && list[i].ContinueMode != ContinueMode.None)
        {
            i++;
            while (i < list.Count && list[i].Disabled) i++;
        }
        i++;
        while (i < list.Count && list[i].Disabled) i++;
        return Math.Min(i, list.Count);
    }

    public IEnumerable<ActiveCue> FindActive(string number)
    {
        if (string.IsNullOrWhiteSpace(number)) return Enumerable.Empty<ActiveCue>();
        return Active.Where(a => a.Started && !a.Done && string.Equals(a.Cue.Number, number.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public void Stop(Cue cue, double fade)
    {
        foreach (var a in Active.Where(a => a.Cue == cue).ToList()) a.Stop(fade);
    }

    internal void RequestGoto(string target, bool fire) => GotoRequested?.Invoke(target, fire);
    internal void ShowMemo(MemoCue memo) => MemoShown?.Invoke(memo);

    /// <summary>Pause alt som spiller. Klokka stopper også, så pre-wait, fader og bildetider står stille.</summary>
    public void PauseAll()
    {
        if (IsPaused) return;
        IsPaused = true;
        _clock.Stop();
        foreach (var a in Active.Where(a => a.Started && !a.Done).ToList()) a.Pause();
        PauseChanged?.Invoke();
    }

    public void ResumeAll()
    {
        if (!IsPaused) return;
        IsPaused = false;
        _clock.Start();
        foreach (var a in Active.Where(a => a.Started && !a.Done).ToList()) a.Resume();
        PauseChanged?.Invoke();
    }

    public void TogglePause() { if (IsPaused) ResumeAll(); else PauseAll(); }

    /// <summary>Slipp loopen i cuen (devamp). False hvis den ikke looper.</summary>
    public bool Release(Cue cue) => Active.Where(a => a.Cue == cue && a.Started && !a.Done).ToList().Select(a => a.Release()).ToList().Any(r => r);

    public void StopAll(double fade)
    {
        if (IsPaused) ResumeAll(); // stoppfader skal kunne høres
        Preview.Stop();
        foreach (var a in Active.ToList()) a.Stop(fade);
    }

    // ------------------------------------------------------------------
    public Grid EnsureOutput() => EnsureOutput(-2);

    /// <summary>Utgangen for en cue: −2 = standard (innstillingene), −1 = eget vindu, 0… = skjerm.</summary>
    public Grid EnsureOutput(int screen)
    {
        if (screen < -1 || screen == Settings.VideoScreen)
        {
            if (_output == null)
            {
                _output = new OutputWindow();
                _output.Configure(Settings.VideoScreen);
                OutputCreated?.Invoke(_output);
            }
            if (!_output.IsVisible) _output.Show();
            return _output.LayerHost;
        }
        if (!_extraOutputs.TryGetValue(screen, out var w))
        {
            w = new OutputWindow { Title = Loc.F("ShowCue – utgang {0}", screen < 0 ? Loc.T("vindu") : (screen + 1).ToString()) };
            w.Configure(screen);
            _extraOutputs[screen] = w;
            ExtraOutputCreated?.Invoke(w);
        }
        if (!w.IsVisible) w.Show();
        return w.LayerHost;
    }

    public void ToggleOutput()
    {
        if (_output != null && _output.IsVisible) _output.Hide();
        else EnsureOutput();
    }

    private void Tick()
    {
        var now = Now;
        foreach (var a in Active.ToList())
        {
            if (a.Done) continue;
            if (!a.Started && now >= a.StartAt) StartActive(a);
            if (a.Done) continue;
            a.Tick();
            a.Cue.Progress = a.Progress;
        }
        Ticked?.Invoke();
    }

    /// <summary>Ved avslutning. Hvert steg for seg, så én feil aldri hindrer resten (og programmet i å lukkes).</summary>
    public void Dispose()
    {
        static void Try(Action a) { try { a(); } catch { /* avslutter uansett */ } }
        _timer.Stop();
        Try(() => StopAll(0));
        foreach (var w in _extraOutputs.Values.Append(_output).OfType<OutputWindow>())
            Try(() => { w.AllowClose = true; w.Close(); });
        Try(Audio.Dispose);
        Try(PreviewAudio.Dispose);
        Try(Midi.Dispose);
        Try(Dmx.Dispose);
        Try(Input.Dispose);
    }
}
