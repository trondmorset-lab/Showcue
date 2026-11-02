using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ShowCue.Models;

namespace ShowCue.Engine;

/// <summary>En cue som er avfyrt: venter (pre-wait), spiller, eller holder på å fade ut.</summary>
public abstract class ActiveCue : ObservableObject
{
    protected readonly CueEngine Engine;
    private double _elapsed;
    private double _duration;
    private bool _stopping;

    public Cue Cue { get; }
    public double StartAt { get; set; }
    /// <summary>Start så mange sekunder inn i cuen (prøver: «spill fra her»).</summary>
    public double StartOffset { get; set; }
    public bool Started { get; private set; }
    public bool Done { get; private set; }
    /// <summary>Stoppet manuelt eller av stopp-/fade-cue (utløser ikke auto-follow).</summary>
    public bool WasStopped { get; protected set; }
    public event Action<ActiveCue>? Completed;

    protected ActiveCue(Cue cue, CueEngine engine) { Cue = cue; Engine = engine; }

    public double Elapsed { get => _elapsed; protected set { if (Set(ref _elapsed, value)) RaiseTime(); } }
    /// <summary>0 = ukjent eller uendelig (loop / står til stopp).</summary>
    public double Duration { get => _duration; protected set { if (Set(ref _duration, value)) RaiseTime(); } }
    public bool IsStopping { get => _stopping; protected set { if (Set(ref _stopping, value)) OnPropertyChanged(nameof(StateText)); } }
    public double Progress => Duration > 0 ? Math.Clamp(Elapsed / Duration, 0, 1) : 0;
    public bool HasDuration => Duration > 0;

    public string ElapsedText => !Started ? "-" + TimeFormat.Format(Math.Max(0, StartAt - Engine.Now)) : TimeFormat.Format(Elapsed);
    public string RemainingText => Started && Duration > 0 ? "-" + TimeFormat.Format(Math.Max(0, Duration - Elapsed)) : (Started ? "∞" : "");
    public string StateText => !Started ? "Pre-wait" : IsStopping ? "Fader ut" : "Spiller";

    private void RaiseTime()
    {
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(HasDuration));
        OnPropertyChanged(nameof(ElapsedText));
        OnPropertyChanged(nameof(RemainingText));
    }

    internal void Begin()
    {
        Started = true;
        OnPropertyChanged(nameof(StateText));
        OnBegin();
    }

    /// <summary>Kalles ca. 60 ganger i sekundet fra UI-tråden.</summary>
    internal virtual void Tick()
    {
        if (!Started) OnPropertyChanged(nameof(ElapsedText)); // nedtelling under pre-wait
    }

    protected abstract void OnBegin();

    /// <summary>Stopp, eventuelt med fade (sekunder).</summary>
    public virtual void Stop(double fadeSeconds)
    {
        WasStopped = true;
        if (!Started) Complete();
    }

    /// <summary>Utfør en fade-cue mot denne cuen.</summary>
    public virtual void ApplyFade(FadeCue fade) { }

    /// <summary>Pause / fortsett (tiden for pre-wait og fader står stille av seg selv, se <see cref="CueEngine.PauseAll"/>).</summary>
    public virtual void Pause() { }
    public virtual void Resume() { }

    /// <summary>Slipp loopen («devamp»): spill ferdig og fortsett til slutten. Returnerer false hvis cuen ikke looper.</summary>
    public virtual bool Release() => false;

    protected void Complete()
    {
        if (Done) return;
        Done = true;
        OnCompleted();
        Completed?.Invoke(this);
    }

    protected virtual void OnCompleted() { }

    /// <summary>Endringer i routing og nivå høres med en gang (nyttig under lydsjekk).</summary>
    protected void FollowLiveChanges(IRoutedAudio cue, CuePlayback? pb, string? property)
    {
        if (pb == null || Done) return;
        if (property == nameof(IRoutedAudio.Routes)) pb.SetRouting(cue.EffectiveRoutes(pb.FileChannels));
        else if (property == nameof(IRoutedAudio.VolumeDb) && !IsStopping) pb.AdjustLevel(cue.VolumeDb);
    }
}

// ======================================================================
public sealed class AudioActive : ActiveCue
{
    private CuePlayback? _pb;
    private AudioCue AudioCue => (AudioCue)Cue;

    public AudioActive(AudioCue cue, CueEngine engine) : base(cue, engine) { }

    protected override void OnBegin()
    {
        if (!File.Exists(AudioCue.FilePath)) throw new FileNotFoundException("Lydfila finnes ikke: " + AudioCue.FilePath);
        _pb = Engine.Audio.Play(AudioCue, StartOffset);
        var pb = _pb;
        // Fila lukkes først når lydtråden har sluppet den.
        pb.Finished += _ => Application.Current.Dispatcher.BeginInvoke(() => { pb.Dispose(); Complete(); });
        Duration = AudioCue.Loop ? 0 : _pb.PassLength;
        if (AudioCue.FileDuration <= 0 && _pb.PassLength > 0 && AudioCue.EndTime <= 0)
            AudioCue.FileDuration = AudioCue.StartTime + _pb.PassLength;
        if (AudioCue.FileChannels != _pb.FileChannels) AudioCue.FileChannels = _pb.FileChannels;
        AudioCue.PropertyChanged += OnCueChanged;
    }

    private void OnCueChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        => FollowLiveChanges(AudioCue, _pb, e.PropertyName);

    internal override void Tick()
    {
        base.Tick();
        if (_pb != null) Elapsed = AudioCue.Loop ? _pb.Elapsed : _pb.PassPosition;
    }

    public override void Stop(double fadeSeconds)
    {
        base.Stop(fadeSeconds);
        if (_pb == null) return;
        IsStopping = true;
        _pb.Stop(fadeSeconds);
        // Uten fade: fjern fra lista med en gang, selv om lydutgangen skulle ha hengt seg.
        if (fadeSeconds <= 0) Complete();
    }

    public override void ApplyFade(FadeCue fade)
    {
        if (_pb == null) return;
        if (fade.FadeAudio)
        {
            if (fade.StopWhenDone) { WasStopped = true; IsStopping = true; }
            _pb.FadeTo(fade.TargetDb, fade.FadeTime, fade.StopWhenDone, fade.Curve);
        }
        else if (fade.StopWhenDone)
        {
            Stop(0);
        }
    }

    public override void Pause() => _pb?.Pause();
    public override void Resume() => _pb?.Resume();

    public override bool Release()
    {
        if (_pb is not { IsLooping: true }) return false;
        _pb.ReleaseLoop();
        Duration = 0; // ukjent til fila er ferdig
        return true;
    }

    protected override void OnCompleted()
    {
        AudioCue.PropertyChanged -= OnCueChanged;
        if (_pb == null) return;
        var pb = _pb;
        // Sørg for at fila lukkes selv om lydutgangen er stoppet/byttet (da kommer aldri Finished).
        Engine.Audio.Mixer?.Remove(pb);
        _ = Task.Delay(500).ContinueWith(_ => pb.Dispose());
    }

}

// ======================================================================
/// <summary>Felles logikk for video, bilde og PDF: lag i utgangsvinduet, opacity-fade.</summary>
public abstract class VisualActive : ActiveCue
{
    protected FrameworkElement? Element;
    protected double StartedAt;
    private double? _finishAt;
    protected VisualCue VCue => (VisualCue)Cue;

    protected VisualActive(VisualCue cue, CueEngine engine) : base(cue, engine) { }

    protected abstract FrameworkElement CreateElement();

    protected override void OnBegin()
    {
        if (VCue is WebCue w) { if (w.ValidUri == null) throw new InvalidOperationException("Ugyldig adresse: " + w.Url); }
        else if (!File.Exists(VCue.FilePath)) throw new FileNotFoundException("Fila finnes ikke: " + VCue.FilePath);
        var host = Engine.EnsureOutput();

        if (VCue.ReplaceOthers)
        {
            // Krysstoning: de andre fades ut på samme tid som denne fades inn.
            foreach (var other in Engine.Active.OfType<VisualActive>().Where(v => v != this && v.Started && !v.Done && !v.IsStopping).ToList())
                other.Stop(VCue.FadeIn > 0 ? VCue.FadeIn : other.VCue.FadeOut);
        }

        Element = CreateElement();
        Element.Opacity = 0;
        host.Children.Add(Element);
        StartedAt = Engine.Now;
        AnimateOpacity(1, VCue.FadeIn);
    }

    protected static Stretch ToStretch(VisualStretch s) => s switch
    {
        VisualStretch.UniformToFill => Stretch.UniformToFill,
        VisualStretch.Fill => Stretch.Fill,
        VisualStretch.None => Stretch.None,
        _ => Stretch.Uniform,
    };

    protected void AnimateOpacity(double to, double seconds, FadeCurve curve = FadeCurve.Db)
    {
        if (Element == null) return;
        if (seconds <= 0)
        {
            Element.BeginAnimation(UIElement.OpacityProperty, null);
            Element.Opacity = to;
            return;
        }
        var anim = new DoubleAnimation(to, TimeSpan.FromSeconds(seconds)) { FillBehavior = FillBehavior.HoldEnd };
        if (curve == FadeCurve.SCurve) anim.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
        Element.BeginAnimation(UIElement.OpacityProperty, anim, HandoffBehavior.SnapshotAndReplace);
    }

    internal override void Tick()
    {
        base.Tick();
        if (_finishAt is double t && Engine.Now >= t) Finish();
    }

    public override void Stop(double fadeSeconds)
    {
        base.Stop(fadeSeconds);
        if (!Started || Done) return;
        IsStopping = true;
        AnimateOpacity(0, fadeSeconds);
        OnStopping(fadeSeconds);
        if (fadeSeconds <= 0) Finish();
        else _finishAt = Engine.Now + fadeSeconds;
    }

    protected virtual void OnStopping(double fadeSeconds) { }

    public override void ApplyFade(FadeCue fade)
    {
        if (Element == null || Done) return;
        if (fade.FadeVisual) AnimateOpacity(fade.TargetOpacity / 100.0, fade.FadeTime, fade.Curve);
        if (fade.StopWhenDone)
        {
            WasStopped = true;
            IsStopping = true;
            _finishAt = Engine.Now + fade.FadeTime;
        }
    }

    protected void Finish()
    {
        if (Done) return;
        _finishAt = null;
        OnRemove();
        if (Element != null)
        {
            Element.BeginAnimation(UIElement.OpacityProperty, null);
            if (Element.Parent is Panel p) p.Children.Remove(Element);
        }
        Complete();
    }

    protected virtual void OnRemove() { }
}

// ======================================================================
public sealed class StillActive : VisualActive
{
    private Image? _image;
    private StillCue SCue => (StillCue)Cue;

    public StillActive(StillCue cue, CueEngine engine) : base(cue, engine) { }

    protected override FrameworkElement CreateElement()
    {
        if (SCue is WebCue web) { Duration = SCue.Duration; return CreateWeb(web.ValidUri!); }
        _image = new Image { Stretch = ToStretch(SCue.Stretch), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);

        if (SCue is ImageCue ic)
        {
            _image.Source = MediaCache.GetImage(ic.FilePath);
        }
        else if (SCue is PdfCue pc)
        {
            var task = MediaCache.GetPdfPage(pc.FilePath, pc.Page);
            if (task.IsCompletedSuccessfully) _image.Source = task.Result;
            else _ = LoadPdfAsync(task);
        }
        Duration = SCue.Duration;
        return _image;
    }

    /// <summary>Nettside: WebView2 i komposisjonsmodus, så den kan fades og vises i forhåndsvisningen som et bilde.</summary>
    private FrameworkElement CreateWeb(Uri uri)
    {
        var web = new Microsoft.Web.WebView2.Wpf.WebView2CompositionControl
        {
            // Programmappa er ikke skrivbar etter installering: nettleserdata legges i brukerens AppData.
            CreationProperties = new Microsoft.Web.WebView2.Wpf.CoreWebView2CreationProperties
            {
                UserDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShowCue", "WebView2"),
            },
            DefaultBackgroundColor = System.Drawing.Color.Black,
        };
        web.CoreWebView2InitializationCompleted += (_, e) =>
        {
            if (!e.IsSuccess) { Cue.Error = "Nettside: " + (e.InitializationException?.Message ?? "WebView2 mangler") + " (installer Microsoft Edge WebView2 Runtime)"; return; }
            web.CoreWebView2.NavigationCompleted += (_, n) => { if (!n.IsSuccess) Cue.Error = $"Nettside: kunne ikke åpne ({n.WebErrorStatus})"; };
        };
        web.Source = uri;
        return web;
    }

    protected override void OnRemove()
    {
        if (Element is IDisposable d) Application.Current.Dispatcher.BeginInvoke(() => d.Dispose());
    }

    private async Task LoadPdfAsync(Task<System.Windows.Media.Imaging.BitmapSource> task)
    {
        try
        {
            var bmp = await task;
            if (_image != null && !Done) _image.Source = bmp;
        }
        catch (Exception ex)
        {
            Cue.Error = "PDF: " + ex.Message;
            Stop(0);
        }
    }

    internal override void Tick()
    {
        base.Tick();
        if (!Started || Done) return;
        Elapsed = Engine.Now - StartedAt;
        if (SCue.HoldTime > 0 && !IsStopping && Elapsed >= SCue.FadeIn + SCue.HoldTime)
        {
            Stop(SCue.FadeOut);
            WasStopped = false; // naturlig slutt → auto-follow skal fortsatt virke
        }
    }
}

// ======================================================================
public sealed class VideoActive : VisualActive
{
    private MediaElement? _media;
    private CuePlayback? _audio;
    private bool _audioStarted;
    private bool _released;
    private double _lastResync;
    private VideoCue VidCue => (VideoCue)Cue;
    /// <summary>Hvor i fila avspillingen starter (startpunkt + eventuell «spill fra her»).</summary>
    private double Begin => VidCue.StartTime + StartOffset;
    /// <summary>Sluttpunktet gjelder ikke lenger når loopen er sluppet (da spilles resten av fila).</summary>
    private double EndPoint => _released ? 0 : VidCue.EndTime;

    /// <summary>Største avvik mellom bilde og lyd før bildet justeres (sekunder).</summary>
    private const double MaxDrift = 0.12;

    public VideoActive(VideoCue cue, CueEngine engine) : base(cue, engine) { }

    protected override FrameworkElement CreateElement()
    {
        // Lyden spilles av ShowCue sin egen lydmotor (gjennom lydpatchen), så MediaElement er alltid dempet.
        _media = new MediaElement
        {
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Manual,
            Stretch = ToStretch(VidCue.Stretch),
            IsMuted = true,
            Volume = 0,
            ScrubbingEnabled = false,
        };
        PrepareAudio();

        _media.MediaOpened += (_, _) =>
        {
            if (_media.NaturalDuration.HasTimeSpan) VidCue.KnownDuration = _media.NaturalDuration.TimeSpan.TotalSeconds;
            var end = VidCue.EffectiveEnd;
            Duration = VidCue.Loop || end <= 0 ? 0 : Math.Max(0, end - Begin);
            if (Begin > 0) _media.Position = TimeSpan.FromSeconds(Begin);
            StartAudio(); // bilde og lyd starter samtidig
        };
        _media.MediaEnded += (_, _) => ReachedEnd();
        _media.MediaFailed += (_, e) =>
        {
            Cue.Error = "Video: " + e.ErrorException?.Message;
            Finish();
        };
        _media.Source = new Uri(VidCue.FilePath);
        _media.Play();
        VidCue.PropertyChanged += OnCueChanged;
        return _media;
    }

    private void PrepareAudio()
    {
        if (VidCue.Routes is { Count: 0 }) return; // lyd slått av for denne cuen
        var mixer = Engine.Audio.Mixer;
        if (mixer == null) return;
        var reader = VideoAudio.Open(VidCue.FilePath);
        if (reader == null)
        {
            if (VidCue.FileChannels != 0) VidCue.FileChannels = 0;
            return;
        }
        if (VidCue.FileChannels != reader.WaveFormat.Channels) VidCue.FileChannels = reader.WaveFormat.Channels;
        // Fade ut håndteres av VideoActive (samme tid for bilde og lyd), derfor FadeOut = 0 her.
        var o = new CuePlayback.Options(VidCue.VolumeDb, Begin, VidCue.EndTime, VidCue.Loop, VidCue.FadeIn, 0);
        _audio = new CuePlayback(VidCue, reader, VidCue, o, mixer.WaveFormat.SampleRate);
        var pb = _audio;
        pb.Finished += _ => Application.Current.Dispatcher.BeginInvoke(pb.Dispose);
    }

    private void StartAudio()
    {
        if (_audio == null || _audioStarted || Done) return;
        _audioStarted = true;
        Engine.Audio.Mixer?.Add(_audio);
        _lastResync = Engine.Now;
    }

    private void OnCueChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        => FollowLiveChanges(VidCue, _audio, e.PropertyName);

    /// <summary>Videoen nådde sluttpunktet (eller slutten av fila).</summary>
    private void ReachedEnd()
    {
        if (_media == null || Done) return;
        if (VidCue.Loop && !IsStopping && !_released)
        {
            _media.Position = TimeSpan.FromSeconds(Begin); // samme sted som lyden looper til
            _media.Play();
            // Lyden looper selv ved sluttpunktet; spol bare tilbake hvis den ikke allerede har gjort det.
            if (_audio != null && _audio.PassPosition > 1.0) _audio.RequestRewind();
            _lastResync = Engine.Now;
        }
        else Finish();
    }

    internal override void Tick()
    {
        base.Tick();
        if (_media == null || Done) return;
        var pos = _media.Position.TotalSeconds;
        Elapsed = Math.Max(0, pos - Begin);

        // Sluttpunkt før slutten av fila.
        if (EndPoint > 0 && pos >= EndPoint && (VidCue.Loop || !IsStopping))
        {
            ReachedEnd();
            if (Done) return;
        }

        // Lyden er «master». Lydkortet (f.eks. Dante-klokka) og PC-klokka går aldri helt likt,
        // så bildet justeres hvis det glir mer enn MaxDrift fra lyden.
        if (_audio != null && _audioStarted && !IsStopping && Engine.Now - _lastResync > 2)
        {
            var audioPos = _audio.PassPosition;
            if (audioPos > 0.5 && Math.Abs(audioPos - Elapsed) > MaxDrift && (Duration == 0 || audioPos < Duration - 1))
            {
                _media.Position = TimeSpan.FromSeconds(Begin + audioPos);
                _lastResync = Engine.Now;
            }
        }

        // Automatisk fade ut mot slutten av videoen.
        if (!VidCue.Loop && !IsStopping && VidCue.FadeOut > 0 && Duration > 0 && Duration - Elapsed <= VidCue.FadeOut)
        {
            Stop(Math.Max(0.05, Duration - Elapsed));
            WasStopped = false;
        }
    }

    protected override void OnStopping(double fadeSeconds) => _audio?.Stop(fadeSeconds);

    public override void ApplyFade(FadeCue fade)
    {
        base.ApplyFade(fade);
        if (fade.FadeAudio) _audio?.FadeTo(fade.TargetDb, fade.FadeTime, false, fade.Curve);
    }

    public override void Pause() { _media?.Pause(); _audio?.Pause(); }
    public override void Resume() { _media?.Play(); _audio?.Resume(); _lastResync = Engine.Now; }

    public override bool Release()
    {
        if (!VidCue.Loop || _released || IsStopping) return false;
        _released = true;
        _audio?.ReleaseLoop();
        if (VidCue.KnownDuration > 0) Duration = Math.Max(0, VidCue.KnownDuration - VidCue.StartTime);
        return true;
    }

    protected override void OnRemove()
    {
        VidCue.PropertyChanged -= OnCueChanged;
        if (_audio != null)
        {
            var pb = _audio;
            pb.Stop(0);
            Engine.Audio.Mixer?.Remove(pb);
            if (!_audioStarted) pb.Dispose();
            else _ = Task.Delay(500).ContinueWith(_ => pb.Dispose());
        }
        if (_media == null) return;
        _media.Stop();
        _media.Close();
        _media.Source = null;
    }
}

// ======================================================================
/// <summary>Fade- og stopp-cue: gjør jobben ved start og «varer» så lenge faden tar.</summary>
public abstract class TimedActive : ActiveCue
{
    private double _begin;
    protected TimedActive(Cue cue, CueEngine engine) : base(cue, engine) { }

    protected abstract double Seconds { get; }
    protected abstract void Act();

    protected override void OnBegin()
    {
        _begin = Engine.Now;
        Duration = Seconds;
        Act();
        if (Seconds <= 0) Complete();
    }

    internal override void Tick()
    {
        base.Tick();
        if (!Started || Done) return;
        Elapsed = Engine.Now - _begin;
        if (Elapsed >= Seconds) Complete();
    }

    public override void Stop(double fadeSeconds) { base.Stop(fadeSeconds); Complete(); }
}

/// <summary>Fade-cue: starter fades på målene.</summary>
public sealed class FadeActive : TimedActive
{
    private FadeCue F => (FadeCue)Cue;
    public FadeActive(FadeCue cue, CueEngine engine) : base(cue, engine) { }
    protected override double Seconds => F.FadeTime;

    protected override void Act()
    {
        var targets = Engine.FindActive(F.Target).ToList();
        if (targets.Count == 0) Cue.Error = string.IsNullOrEmpty(F.Target) ? "Mangler mål-cue" : $"Cue {F.Target} kjører ikke";
        foreach (var t in targets) t.ApplyFade(F);
    }
}

/// <summary>Stopp-cue: stopper målet (eller alt) med fade – eller slipper loopen («devamp»).</summary>
public sealed class StopActive : TimedActive
{
    private StopCue S => (StopCue)Cue;
    public StopActive(StopCue cue, CueEngine engine) : base(cue, engine) { }
    protected override double Seconds => S.Release ? 0 : S.FadeTime;

    protected override void Act()
    {
        var targets = string.IsNullOrEmpty(S.Target)
            ? Engine.Active.Where(a => a != this && a is not StopActive).ToList()
            : Engine.FindActive(S.Target).ToList();
        if (!S.Release) { foreach (var t in targets) t.Stop(S.FadeTime); return; }
        if (!targets.Select(t => t.Release()).ToList().Any(released => released))
            Cue.Error = string.IsNullOrEmpty(S.Target) ? "Ingen cue looper" : $"Cue {S.Target} looper ikke";
    }
}

// ======================================================================
/// <summary>Spilleliste: spor etter spor (eller tilfeldig), med krysstoning, gjennom lydpatchen.</summary>
public sealed class PlaylistActive : ActiveCue
{
    private static readonly Random Rng = new();
    private readonly List<CuePlayback> _playing = new();
    private List<string> _order = new();
    private int _index = -1;
    private CuePlayback? _current;
    private bool _releasing;
    private double _begin;
    private PlaylistCue P => (PlaylistCue)Cue;

    public PlaylistActive(PlaylistCue cue, CueEngine engine) : base(cue, engine) { }

    /// <summary>Navnet på sporet som spiller nå.</summary>
    public string CurrentTrack => _index >= 0 && _index < _order.Count ? Path.GetFileNameWithoutExtension(_order[_index]) : "";

    private List<string> NewOrder()
    {
        var files = P.Files.Where(File.Exists).ToList();
        return P.Shuffle ? files.OrderBy(_ => Rng.Next()).ToList() : files;
    }

    protected override void OnBegin()
    {
        _order = NewOrder();
        if (_order.Count == 0) throw new FileNotFoundException("Spillelista har ingen filer som finnes.");
        _begin = Engine.Now;
        P.PropertyChanged += OnCueChanged;
        StartNext(0);
    }

    /// <summary>Start neste spor (med fade inn). False når lista er ferdig.</summary>
    private bool StartNext(double fadeIn)
    {
        if (++_index >= _order.Count)
        {
            if (!P.Loop || _releasing) { _current = null; return false; }
            _order = NewOrder();
            _index = 0;
            if (_order.Count == 0) { _current = null; return false; }
        }
        var mixer = Engine.Audio.Mixer ?? throw new InvalidOperationException("Lydutgangen er ikke startet. Sjekk innstillinger.");
        var o = new CuePlayback.Options(P.VolumeDb, StartOffset > 0 && _index == 0 ? StartOffset : 0, 0, false, fadeIn, 0);
        var pb = new CuePlayback(P, new NAudio.Wave.AudioFileReader(_order[_index]), P, o, mixer.WaveFormat.SampleRate);
        pb.Finished += done => Application.Current.Dispatcher.BeginInvoke(() => { _playing.Remove(done); done.Dispose(); });
        _playing.Add(pb);
        _current = pb;
        mixer.Add(pb);
        OnPropertyChanged(nameof(CurrentTrack));
        return true;
    }

    internal override void Tick()
    {
        base.Tick();
        if (!Started || Done) return;
        Elapsed = Engine.Now - _begin;
        if (_playing.All(p => p.IsFinished) && (_current == null || IsStopping)) { Complete(); return; }
        if (IsStopping || _current is not { } cur) return;

        double left = cur.PassLength - cur.PassPosition;
        if (!_releasing && P.Crossfade > 0 && cur.PassLength > 0 && left <= P.Crossfade && !cur.IsFinished)
        {
            cur.FadeTo(CuePlayback.SilenceDb, Math.Max(0.05, left), true);
            StartNext(P.Crossfade);
        }
        else if (cur.IsFinished)
        {
            StartNext(0);
        }
    }

    private void OnCueChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        foreach (var pb in _playing) FollowLiveChanges(P, pb, e.PropertyName);
    }

    public override void Stop(double fadeSeconds)
    {
        base.Stop(fadeSeconds);
        if (!Started || Done) return;
        IsStopping = true;
        foreach (var pb in _playing) pb.Stop(fadeSeconds);
        if (fadeSeconds <= 0) Complete();
    }

    public override void ApplyFade(FadeCue fade)
    {
        if (!fade.FadeAudio) { if (fade.StopWhenDone) Stop(0); return; }
        if (fade.StopWhenDone) { WasStopped = true; IsStopping = true; }
        foreach (var pb in _playing) pb.FadeTo(fade.TargetDb, fade.FadeTime, fade.StopWhenDone, fade.Curve);
    }

    public override void Pause() { foreach (var pb in _playing) pb.Pause(); }
    public override void Resume() { foreach (var pb in _playing) pb.Resume(); }

    /// <summary>For en spilleliste betyr «slipp»: spill ferdig sporet som går, og stopp.</summary>
    public override bool Release()
    {
        if (_releasing || IsStopping) return false;
        _releasing = true;
        return true;
    }

    protected override void OnCompleted()
    {
        P.PropertyChanged -= OnCueChanged;
        foreach (var pb in _playing.ToList())
        {
            pb.Stop(0);
            Engine.Audio.Mixer?.Remove(pb);
            _ = Task.Delay(500).ContinueWith(_ => pb.Dispose());
        }
        _playing.Clear();
    }
}

// ======================================================================
/// <summary>OSC og MIDI: sendes umiddelbart og er ferdige med en gang.</summary>
public sealed class InstantActive : ActiveCue
{
    public InstantActive(Cue cue, CueEngine engine) : base(cue, engine) { }

    protected override void OnBegin()
    {
        try
        {
            switch (Cue)
            {
                case OscCue o: Osc.Send(o.Host, o.Port, o.Address, o.Arguments); break;
                case MidiCue m: Engine.Midi.Send(m); break;
                case GotoCue g: Engine.RequestGoto(g.Target, g.Fire); break;
                case MemoCue m: Engine.ShowMemo(m); break;
                case NetworkCue n: SendNetwork(n); break;
            }
        }
        finally
        {
            Complete();
        }
    }

    /// <summary>Sendes i bakgrunnen så GO aldri venter på nettverket. Feil vises på cuen.</summary>
    private static async void SendNetwork(NetworkCue n)
    {
        var error = await NetworkSender.SendAsync(n);
        if (error != null) n.Error = "Nettverk: " + error;
    }
}

// ======================================================================
/// <summary>Tidskode-cue: MTC til en MIDI-utgang og/eller LTC som lyd, fra starttiden.</summary>
public sealed class TimecodeActive : ActiveCue
{
    private TimecodeCue T => (TimecodeCue)Cue;
    private LtcSource? _ltc;
    private MtcSender? _mtc;
    private double _begin;

    public TimecodeActive(TimecodeCue cue, CueEngine engine) : base(cue, engine) { }

    /// <summary>Tidskoden som sendes nå (vises i lista over aktive cuer).</summary>
    public string Current => Timecode.FromSeconds(T.StartTimecode.ToSeconds(T.Rate) + Elapsed, T.Rate).Format(T.Rate);

    protected override void OnBegin()
    {
        if (!T.SendMtc && !T.SendLtc) throw new InvalidOperationException("Velg MTC og/eller LTC");
        var start = Timecode.FromSeconds(T.StartTimecode.ToSeconds(T.Rate) + StartOffset, T.Rate);
        if (T.SendLtc)
        {
            var mixer = Engine.Audio.Mixer ?? throw new InvalidOperationException("Lydutgangen er ikke startet. Sjekk innstillinger.");
            _ltc = new LtcSource(T.LtcBus - 1, T.LtcLevelDb, start, T.Rate, mixer.WaveFormat.SampleRate);
            mixer.Add(_ltc);
        }
        if (T.SendMtc)
        {
            var device = T.Device;
            var midi = Engine.Midi;
            _mtc = new MtcSender(m => midi.SendShort(device, m), b => midi.SendSysex(device, b), start, T.Rate);
        }
        _begin = Engine.Now;
        Duration = T.Length;
    }

    internal override void Tick()
    {
        base.Tick();
        if (!Started || Done) return;
        Elapsed = Engine.Now - _begin;
        OnPropertyChanged(nameof(Current));
        if (T.Length > 0 && Elapsed >= T.Length) Complete();
    }

    public override void Pause() { _ltc?.Pause(); _mtc?.Pause(); }
    public override void Resume() { _ltc?.Resume(); _mtc?.Resume(); }
    public override void Stop(double fadeSeconds) { base.Stop(fadeSeconds); Complete(); }

    protected override void OnCompleted()
    {
        _ltc?.Stop();
        _mtc?.Dispose();
    }
}

/// <summary>Lys-cue: fader DMX-kanaler over Art-Net. Å stoppe cuen lar lyset stå (som på et lysbord).</summary>
public sealed class LightActive : TimedActive
{
    private LightCue L => (LightCue)Cue;
    public LightActive(LightCue cue, CueEngine engine) : base(cue, engine) { }
    protected override double Seconds => L.FadeTime;

    protected override void Act()
    {
        if (!DmxLevels.TryParse(L.Levels, out var levels, out var error)) throw new FormatException("Nivåer: " + error);
        if (string.IsNullOrWhiteSpace(L.Host)) throw new InvalidOperationException("Mangler IP-adresse for Art-Net");
        Engine.Dmx.FadeTo(L.Host, L.Universe, levels, L.FadeTime);
    }
}

/// <summary>Live-inngang gjennom lydpatchen (mikrofon, linje inn).</summary>
public sealed class InputActive : ActiveCue
{
    private CuePlayback? _pb;
    private InputCue I => (InputCue)Cue;
    private double _begin;

    public InputActive(InputCue cue, CueEngine engine) : base(cue, engine) { }

    protected override void OnBegin()
    {
        var mixer = Engine.Audio.Mixer ?? throw new InvalidOperationException("Lydutgangen er ikke startet. Sjekk innstillinger.");
        int rate = mixer.WaveFormat.SampleRate;
        LiveRing ring;
        try { ring = Engine.Input.Open(Engine.Settings.InputDeviceId, rate); }
        catch (Exception ex) { throw new InvalidOperationException("Kunne ikke åpne inngangen: " + ex.Message); }
        if (I.FileChannels != ring.Channels) I.FileChannels = ring.Channels;
        var stream = new LiveInputStream(ring, rate, Engine.Input.Close);
        var pb = _pb = new CuePlayback(I, stream, I, new CuePlayback.Options(I.VolumeDb, 0, 0, false, I.FadeIn, I.FadeOut), rate);
        pb.Finished += _ => Application.Current.Dispatcher.BeginInvoke(() => { pb.Dispose(); Complete(); });
        mixer.Add(pb);
        _begin = Engine.Now;
        I.PropertyChanged += OnCueChanged;
    }

    private void OnCueChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => FollowLiveChanges(I, _pb, e.PropertyName);

    internal override void Tick()
    {
        base.Tick();
        if (Started && !Done) Elapsed = Engine.Now - _begin;
    }

    public override void Stop(double fadeSeconds)
    {
        base.Stop(fadeSeconds);
        if (_pb == null) return;
        IsStopping = true;
        _pb.Stop(fadeSeconds);
        if (fadeSeconds <= 0) Complete();
    }

    public override void ApplyFade(FadeCue fade)
    {
        if (_pb == null) return;
        if (fade.FadeAudio)
        {
            if (fade.StopWhenDone) { WasStopped = true; IsStopping = true; }
            _pb.FadeTo(fade.TargetDb, fade.FadeTime, fade.StopWhenDone, fade.Curve);
        }
        else if (fade.StopWhenDone) Stop(0);
    }

    public override void Pause() => _pb?.Pause();
    public override void Resume() => _pb?.Resume();

    protected override void OnCompleted()
    {
        I.PropertyChanged -= OnCueChanged;
        if (_pb == null) return;
        var pb = _pb;
        Engine.Audio.Mixer?.Remove(pb);
        _ = Task.Delay(500).ContinueWith(_ => pb.Dispose());
    }
}
