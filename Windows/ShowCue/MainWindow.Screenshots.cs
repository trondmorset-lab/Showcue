using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ShowCue.Models;
using ShowCue.Views;

namespace ShowCue;

/// <summary>
/// Skjermbildemodus for manualene: <c>ShowCue.exe --screenshots &lt;mappe&gt; [--language nb|en]</c>.
/// Lager et demoshow med egne lyd- og bildefiler, tar bilder av vinduene og avslutter.
/// Kjøres av arbeidsflyten «Skjermbilder» på GitHub.
/// </summary>
public partial class MainWindow
{
    private const double ShotScale = 2;

    /// <summary>Mappa skjermbildene lagres i (null = vanlig oppstart).</summary>
    public static string? ScreenshotDir { get; set; }

    private string Demo(string nb, string en) => Loc.Language == "en" ? en : nb;

    private async void RunScreenshots(string dir)
    {
        var log = new List<string>();
        _autosaveTimer.Stop();
        try
        {
            Directory.CreateDirectory(dir);
            var media = Path.Combine(Path.GetPathRoot(Path.GetTempPath()) ?? "C:\\", "Show", "Media");
            Directory.CreateDirectory(media);
            var doc = BuildDemoShow(media);
            SetShow(doc, Path.Combine(Path.GetDirectoryName(media)!, Demo("Vårkonsert", "Spring concert") + ".showcue"));
            // Video og PDF i demoen er bare navn (filene lages ikke); de skal ikke vises som manglende.
            foreach (var c in Cues) c.Error = null;
            StatusText = "";
            WindowState = WindowState.Normal;
            Left = 0; Top = 0;

            // Lysbildeserien og innslippsmusikken spiller, så «Aktive cuer» og utgangen viser noe.
            var slides = Cues.OfType<SlideshowCue>().First();
            var walkIn = Cues.OfType<AudioCue>().First();
            Engine.Fire(slides);
            try { Engine.Fire(walkIn); } catch (Exception ex) { log.Add("lyd: " + ex.Message); }
            await Task.Delay(2500);
            if (walkIn.HasError) { log.Add("lyd: " + walkIn.Error); Engine.Stop(walkIn, 0); walkIn.Error = null; }
            foreach (var c in Cues) c.Error = null;
            StatusText = "";
            Activate();

            SelectedCue = Cues.OfType<AudioCue>().Skip(1).First();
            await Shot(this, Path.Combine(dir, "hovedvindu.png"), log, MainSize);

            SelectedCue = slides;
            await Shot(this, Path.Combine(dir, "lysbildeserie.png"), log, MainSize);

            var theme = ThemeManager.Current;
            ThemeManager.Apply(AppTheme.Light);
            SelectedCue = Cues.OfType<VideoCue>().First();
            await Shot(this, Path.Combine(dir, "hovedvindu-lyst.png"), log, MainSize);
            ThemeManager.Apply(theme);
            SelectedCue = Cues.OfType<AudioCue>().Skip(1).First();

            var patch = new PatchWindow(Engine.Audio, _settings.Buses, () => { }) { Owner = this, Left = 0, Top = 0 };
            patch.Show();
            await Shot(patch, Path.Combine(dir, "lydpatch.png"), log);
            patch.Close();

            var settings = new SettingsWindow(_settings.Copy(), Remote) { Owner = this, Left = 0, Top = 0 };
            settings.Show();
            await Shot(settings, Path.Combine(dir, "innstillinger.png"), log);
            settings.Close();
        }
        catch (Exception ex)
        {
            log.Add("feil: " + ex);
        }
        try { File.WriteAllLines(Path.Combine(dir, "logg.txt"), log); } catch { }
        Environment.Exit(0);
    }

    /// <summary>Innholdet i hovedvinduet ved 1440×900 (uten tittellinje og kant).</summary>
    private static readonly Size MainSize = new(1424, 861);

    /// <summary>
    /// Venter til vinduet er tegnet ferdig, og lagrer innholdet som PNG (2x oppløsning).
    /// Med <paramref name="client"/> legges innholdet ut i den størrelsen, også når skjermen på byggemaskinen er mindre
    /// enn vinduet. Vinduet klipper da innholdet, så delene tegnes hver for seg.
    /// </summary>
    private static async Task Shot(Window w, string file, List<string> log, Size? client = null)
    {
        if (w.Content is not FrameworkElement root) return;
        var m = root.Margin;
        try
        {
            if (client is Size c) { root.Width = c.Width; root.Height = c.Height; }
            await Task.Delay(1200);
            await w.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            w.UpdateLayout();
            var size = new Size(root.ActualWidth + m.Left + m.Right, root.ActualHeight + m.Top + m.Bottom);
            var bmp = new RenderTargetBitmap((int)(size.Width * ShotScale), (int)(size.Height * ShotScale),
                96 * ShotScale, 96 * ShotScale, PixelFormats.Pbgra32);
            var bg = new DrawingVisual();
            using (var dc = bg.RenderOpen()) dc.DrawRectangle(w.Background ?? Brushes.Black, null, new Rect(size));
            bmp.Render(bg);
            if (client != null)
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) bmp.Render(VisualTreeHelper.GetChild(root, i));
            else
                bmp.Render(root);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(file);
            png.Save(fs);
            log.Add(Path.GetFileName(file) + $": {bmp.PixelWidth}x{bmp.PixelHeight}");
        }
        catch (Exception ex)
        {
            log.Add(Path.GetFileName(file) + ": " + ex.Message);
        }
        finally
        {
            root.Width = double.NaN;
            root.Height = double.NaN;
        }
    }

    private ShowDocument BuildDemoShow(string media)
    {
        string Wav(string name, double seconds, double baseHz)
        {
            var path = Path.Combine(media, name);
            DemoMedia.WriteWav(path, seconds, baseHz);
            return path;
        }
        string[] slideTexts = Loc.Language == "en"
            ? new[] { "Welcome", "Tonight's programme", "Interval – 20 min", "Thank you!" }
            : new[] { "Velkommen", "Kveldens program", "Pause – 20 min", "Takk for i kveld!" };
        var slides = slideTexts.Select((t, i) =>
        {
            var p = Path.Combine(media, $"lysbilde-{i + 1}.png");
            DemoMedia.WriteSlide(p, t, i);
            return p;
        }).ToList();

        var doc = new ShowDocument();
        void Add(Cue c) { c.Number = (doc.Cues.Count + 1).ToString(); doc.Cues.Add(c); }

        Add(new AudioCue { Name = Demo("Innslipp-musikk", "Walk-in music"), FilePath = Wav(Demo("innslipp.wav", "walk-in.wav"), 185, 220), Loop = true, VolumeDb = -6, Hotkey = "F1", Color = CueColor.Green });
        Add(new FadeCue { Name = Demo("Fade ut innslipp", "Fade out walk-in"), Target = "1", FadeTime = 5, FadeAudio = true, TargetDb = -60, StopWhenDone = true });
        Add(new MemoCue { Name = Demo("Vent på konferansier", "Wait for the host"), Text = Demo("Vent på at konferansier går av scenen", "Wait for the host to leave the stage") });
        Add(new VideoCue { FilePath = Path.Combine(media, Demo("aapning.mp4", "opening.mp4")), Name = Demo("Åpningsvideo", "Opening video"), KnownDuration = 92.4, FileChannels = 2, Color = CueColor.Blue });
        Add(new OscCue { Name = Demo("Lys: åpning", "Lights: opening"), Host = "192.168.1.50", Port = 8000, Address = "/lys/cue/1", PreWait = 2, Color = CueColor.Orange });
        Add(new SlideshowCue { Name = Demo("Program og sponsorer", "Programme and sponsors"), Files = slides, SlideTime = 30, Color = CueColor.Purple });
        Add(new PdfCue { FilePath = Path.Combine(media, Demo("sangtekster.pdf", "lyrics.pdf")), Name = Demo("Sangtekst – vers 1", "Lyrics – verse 1"), Page = 1 });
        Add(new AudioCue { Name = Demo("Applaus", "Applause"), FilePath = Wav(Demo("applaus.wav", "applause.wav"), 6.8, 330), Hotkey = "F2", FadeOut = 1, Color = CueColor.Yellow });
        Add(new PlaylistCue { Name = Demo("Pausemusikk", "Interval music"), Files = new() { Wav("pause-1.wav", 30, 196), Wav("pause-2.wav", 30, 247), Wav("pause-3.wav", 30, 262) }, Hotkey = "F3", Crossfade = 3 });
        Add(new LightCue { Name = Demo("Lys: pause", "Lights: interval"), Host = "192.168.1.60", Universe = 1, Levels = "1-12@60%", FadeTime = 3 });
        Add(new StopCue { Name = Demo("Stopp alt", "Stop all"), FadeTime = 3 });
        return doc;
    }
}

/// <summary>Lager små demofiler (lyd og lysbilder) til skjermbildemodus.</summary>
internal static class DemoMedia
{
    /// <summary>Rolig akkord med langsom puls (16-bit stereo, 8 kHz) – nok til å gi en bølgeform.</summary>
    public static void WriteWav(string path, double seconds, double baseHz)
    {
        const int rate = 8000;
        int frames = (int)(seconds * rate);
        using var w = new BinaryWriter(File.Create(path));
        int dataBytes = frames * 4;
        w.Write("RIFF"u8); w.Write(36 + dataBytes); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(rate); w.Write(rate * 4); w.Write((short)4); w.Write((short)16);
        w.Write("data"u8); w.Write(dataBytes);
        for (int i = 0; i < frames; i++)
        {
            double t = (double)i / rate;
            double env = 0.55 + 0.35 * Math.Sin(2 * Math.PI * t / 4.3) * Math.Sin(2 * Math.PI * t / 11.0);
            double fade = Math.Min(1, Math.Min(t / 0.5, (seconds - t) / 0.8));
            double v = (Math.Sin(2 * Math.PI * baseHz * t) + 0.5 * Math.Sin(2 * Math.PI * baseHz * 1.5 * t) + 0.3 * Math.Sin(2 * Math.PI * baseHz * 2 * t)) / 1.8;
            short s = (short)(v * env * Math.Max(0, fade) * 0.5 * short.MaxValue);
            w.Write(s); w.Write(s);
        }
    }

    private static readonly Color[][] Palettes =
    {
        new[] { Color.FromRgb(0x1B, 0x2C, 0x44), Color.FromRgb(0x0B, 0x15, 0x24) },
        new[] { Color.FromRgb(0x24, 0x3B, 0x2F), Color.FromRgb(0x0E, 0x1C, 0x16) },
        new[] { Color.FromRgb(0x3A, 0x24, 0x44), Color.FromRgb(0x16, 0x0E, 0x1E) },
        new[] { Color.FromRgb(0x44, 0x2E, 0x1B), Color.FromRgb(0x1E, 0x14, 0x0B) },
    };

    /// <summary>Lysbilde i 1920×1080 med mørk fargetone og stor tekst.</summary>
    public static void WriteSlide(string path, string text, int index)
    {
        var p = Palettes[index % Palettes.Length];
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(p[0], p[1], 90), null, new Rect(0, 0, 1920, 1080));
            var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 120, Brushes.White, 1.0)
            { TextAlignment = TextAlignment.Center, MaxTextWidth = 1700 };
            dc.DrawText(ft, new Point(960 - 850, 540 - ft.Height / 2));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x5C, 0xD6, 0x8A)), null, new Rect(860, 540 + ft.Height / 2 + 30, 200, 6));
        }
        var bmp = new RenderTargetBitmap(1920, 1080, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        png.Save(fs);
    }
}
