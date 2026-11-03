using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NAudio.Wave;
using Cue = ShowCue.Models.Cue;
using ShowCue.Engine;
using ShowCue.Models;

// Stresstest av ShowCue-kjernen (det som ikke trenger WPF): fuzzing av inndata, rundtur av showfiler,
// lydmikseren under samtidige endringer og grensetilfeller i avspilling. Se README.md i denne mappa.
public static class Program
{
    static readonly ConcurrentDictionary<string, (int Count, string Example)> Bugs = new();
    static Random Rng = new(12345);
    static readonly string Dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "showcue-stress")).FullName;

    static double BaselineMem;
    static void Bug(string suite, Exception e, string input)
    {
        var top = e.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains("ShowCue"))?.Trim() ?? "";
        var key = $"{suite}: {e.GetType().Name}: {e.Message.Split('\n')[0]} @ {top}";
        Bugs.AddOrUpdate(key, _ => (1, input), (_, v) => (v.Count + 1, v.Example));
    }
    static void Bug(string suite, string what, string input) =>
        Bugs.AddOrUpdate($"{suite}: {what}", _ => (1, input), (_, v) => (v.Count + 1, v.Example));

    public static int Main(string[] args)
    {
        // stress [minutter] [filter]: kjører alle testene om og om igjen med nye tilfeldige data til tiden er ute.
        var minutes = args.Length > 0 ? double.Parse(args[0], System.Globalization.CultureInfo.InvariantCulture) : 0;
        var only = args.Length > 1 ? args[1] : null;
        var deadline = DateTime.UtcNow.AddMinutes(minutes);
        var report = Path.Combine(AppContext.BaseDirectory, "stress-report.txt");
        int round = 0;
        do
        {
            round++;
            Rng = new Random(round * 7919);
            Console.WriteLine($"--- Runde {round} (frø {round * 7919}) {DateTime.Now:HH:mm:ss} ---");
            void Run(string name, Action a)
            {
                if (only != null && !name.Contains(only)) return;
                var sw = Stopwatch.StartNew();
                CurrentCase = "";
                var t = new Thread(() => { try { a(); } catch (Exception e) { Bug(name, e, "(suite krasjet)"); } }) { IsBackground = true };
                t.Start();
                if (!t.Join(TimeSpan.FromMinutes(3)))
                {
                    Bug(name, "HENGER: suiten ble ikke ferdig på 3 minutter", CurrentCase);
                    File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "hang.txt"), $"{name}: {CurrentCase}\n");
                    Console.WriteLine($"{name} HENGER: {CurrentCase}");
                    var text = new StringBuilder();
                    foreach (var (k, v) in Bugs.OrderBy(b => b.Key)) text.Append($"[{v.Count}x] {k}\n      eksempel: {v.Example}\n");
                    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "stress-report.txt"), text.ToString());
                    Environment.Exit(3);
                }
                Console.WriteLine($"{name,-28} {sw.Elapsed.TotalSeconds,6:0.0} s");
            }
            Run("DmxLevels", FuzzDmx);
            Run("Timecode", FuzzTimecode);
            Run("OSC decode", FuzzOscDecode);
            Run("OSC arguments", FuzzOscArgs);
            Run("Remote OSC routing", FuzzRemote);
            Run("MSC parse", FuzzMsc);
            Run("Network unescape", FuzzUnescape);
            Run("Show roundtrip", ShowRoundtrip);
            Run("Show mutation", ShowMutation);
            Run("Playback edge cases", PlaybackEdges);
            Run("Mixer concurrency", MixerConcurrency);
            Run("Loudness extremes", LoudnessExtremes);
            Run("Safety paths", SafetyPaths);
            Run("Media tools paths", MediaPaths);
            Run("Timecode decode", FuzzLtcDecode);
            Run("Dmx fader", FuzzDmxFader);
            Run("Network sender", NetworkSend);
            Run("LTC source", LtcRender);
            Run("Waveform", WaveformCompute);
            Run("OSC feedback", FeedbackValues);
            Run("Undo history", UndoFuzz);
            Run("Show save/load files", SaveLoadFiles);
            var text = new StringBuilder($"Runder: {round}, feiltyper: {Bugs.Count}\n\n");
            foreach (var (k, v) in Bugs.OrderBy(b => b.Key)) text.Append($"[{v.Count}x] {k}\n      eksempel: {v.Example}\n");
            File.WriteAllText(report, text.ToString());
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var mem = GC.GetTotalMemory(true) / 1e6; var ws = Environment.WorkingSet / 1e6;
            var threads = System.Diagnostics.Process.GetCurrentProcess().Threads.Count;
            Console.WriteLine($"Feiltyper så langt: {Bugs.Count}   minne {mem:0.0} MB (prosess {ws:0} MB, {threads} tråder)");
            if (round == 3) BaselineMem = mem;
            if (round > 10 && mem > BaselineMem * 3 + 50) Bug("Minne", $"minnebruken vokser: {BaselineMem:0} → {mem:0} MB", $"runde {round}");
        } while (DateTime.UtcNow < deadline);
        Console.WriteLine(File.ReadAllText(report));
        return Bugs.IsEmpty ? 0 : 1;
    }

    // ---------- tilfeldige data ----------
    static readonly string[] Pieces = { "", " ", "@", "%", "-", ",", ";", "\n", "0", "1", "255", "256", "512", "513", "-1", "99999999999999999999",
        "1.5", "1,5", "NaN", "∞", "e", "1e400", "é", "«", "\0", "\"", "'", "{", "}", "[", "]", "/", "\\", ":", "::", "00:00:00:00", "23:59:59:29",
        "99:99:99:99", "true", "false", "null", "abc", "F1", "N:60", "CC:", "PC:x", "🔒", new string('a', 300) };
    static string RandText() { var sb = new StringBuilder(); for (int i = Rng.Next(8); i >= 0; i--) sb.Append(Rng.Next(4) == 0 ? (char)Rng.Next(1, 0xD7FF) : Pieces[Rng.Next(Pieces.Length)]); return sb.ToString(); }
    static byte[] RandBytes(int max) { var b = new byte[Rng.Next(max)]; Rng.NextBytes(b); return b; }

    static void FuzzDmx()
    {
        for (int i = 0; i < 200_000; i++)
        {
            var t = RandText();
            try
            {
                if (DmxLevels.TryParse(t, out var l, out _))
                    foreach (var (ch, _) in l) if (ch < 1 || ch > 512) Bug("DmxLevels", $"kanal utenfor 1–512: {ch}", t);
                DmxLevels.Summary(t);
            }
            catch (Exception e) { Bug("DmxLevels", e, t); }
        }
    }

    static void FuzzTimecode()
    {
        foreach (TimecodeRate r in Enum.GetValues<TimecodeRate>())
        {
            for (long n = 0; n < 200_000; n += Rng.Next(1, 37))
            {
                try
                {
                    var tc = Timecode.FromFrame(n, r);
                    if (tc.ToFrame(r) != n) { Bug("Timecode", $"FromFrame/ToFrame stemmer ikke ({r})", n.ToString()); continue; }
                    if (!Timecode.TryParse(tc.Format(r), out var back) || back.ToFrame(r) != n) Bug("Timecode", $"Format/TryParse rundtur ({r})", tc.Format(r));
                    var bits = Ltc.Bits(tc, r);
                    var dec = Ltc.Decode(bits);
                    if (dec == null || dec.Value.ToFrame(r) != n) Bug("Timecode", $"LTC Bits/Decode rundtur ({r})", tc.Format(r));
                    for (int p = 0; p < 8; p++) Mtc.QuarterFrame(tc, r, p);
                    Mtc.FullFrame(tc, r);
                }
                catch (Exception e) { Bug("Timecode", e, $"{n} {r}"); }
            }
        }
        for (int i = 0; i < 100_000; i++)
        {
            var t = RandText();
            try { if (Timecode.TryParse(t, out var tc)) foreach (TimecodeRate r in Enum.GetValues<TimecodeRate>()) { tc.ToFrame(r); tc.Format(r); } }
            catch (Exception e) { Bug("Timecode", e, t); }
        }
        foreach (var s in new[] { double.NaN, double.PositiveInfinity, -1, 1e15, double.MaxValue })
            try { Timecode.FromSeconds(s, TimecodeRate.Fps25); } catch (Exception e) { Bug("Timecode", e, $"FromSeconds({s})"); }
    }

    static void FuzzOscDecode()
    {
        var valid = Osc.Encode("/cue/12/start", new List<object> { 1, 2.5f, "tekst", true });
        for (int i = 0; i < 300_000; i++)
        {
            byte[] b;
            if (Rng.Next(2) == 0) b = RandBytes(64);
            else
            {
                b = (byte[])valid.Clone();
                for (int k = Rng.Next(1, 4); k > 0; k--) b[Rng.Next(b.Length)] = (byte)Rng.Next(256);
                if (Rng.Next(3) == 0) b = b[..Rng.Next(b.Length)];
            }
            try { Osc.Decode(b, b.Length); }
            catch (Exception e) { Bug("OSC decode", e, Convert.ToHexString(b)); }
        }
        // Bundles inni bundles
        try
        {
            var inner = Osc.Encode("/go", new List<object>());
            var bundle = new List<byte>();
            bundle.AddRange(Encoding.ASCII.GetBytes("#bundle\0")); bundle.AddRange(new byte[8]);
            var size = BitConverter.GetBytes(inner.Length); Array.Reverse(size); bundle.AddRange(size); bundle.AddRange(inner);
            var b = bundle.ToArray();
            for (int depth = 0; depth < 200; depth++)
            {
                var outer = new List<byte>(); outer.AddRange(Encoding.ASCII.GetBytes("#bundle\0")); outer.AddRange(new byte[8]);
                var sz = BitConverter.GetBytes(b.Length); Array.Reverse(sz); outer.AddRange(sz); outer.AddRange(b);
                b = outer.ToArray();
            }
            Osc.Decode(b, b.Length);
        }
        catch (Exception e) { Bug("OSC decode", e, "200 nestede bundles"); }
    }

    static void FuzzOscArgs()
    {
        for (int i = 0; i < 100_000; i++)
        {
            var t = RandText();
            try { var a = Osc.ParseArguments(t); Osc.Encode("/x", a); }
            catch (Exception e) { Bug("OSC arguments", e, t); }
        }
    }

    sealed class FakeShow : IShowControl
    {
        public void Go() { } public void StopAll() { } public void Panic() { } public void HardStop() { }
        public void MovePlayhead(int delta) { }
        public bool SelectCue(string n) => Check(n); public bool StartCue(string n) => Check(n); public bool StopCue(string n) => Check(n);
        public bool GoCue(string n) => Check(n); public bool FireHotkey(string k) => Check(k); public int FireMidiTrigger(string t) => 0;
        public void RemoteLog(string text) { } public void RefreshFeedback() { } public void SetPaused(bool? p) { }
        public bool ReleaseCue(string n) => Check(n); public int FireTimecodeTriggers(long a, long b, TimecodeRate r) => 0;
        static bool Check(string s) { if (s == null) throw new ArgumentNullException(nameof(s), "null cuenummer sendt til showet"); return true; }
    }

    static void FuzzRemote()
    {
        var rc = new RemoteControl(new FakeShow(), System.Windows.Threading.Dispatcher.CurrentDispatcher, new AppSettings { OscInEnabled = false, MidiInDevice = "" });
        string[] parts = { "", "showcue", "cue", "go", "stop", "start", "panic", "hardstop", "next", "previous", "prev", "playhead", "select", "hotkey",
            "pause", "resume", "toggle", "devamp", "release", "refresh", "status", "12", "1.5", "F1", "", "//", "ÆØÅ" };
        for (int i = 0; i < 200_000; i++)
        {
            var addr = "/" + string.Join("/", Enumerable.Range(0, Rng.Next(5)).Select(_ => parts[Rng.Next(parts.Length)]));
            var args = new List<object>();
            for (int k = Rng.Next(3); k > 0; k--)
                args.Add(Rng.Next(6) switch { 0 => Rng.Next(-2, 3), 1 => (float)Rng.NextDouble() * 2 - 1, 2 => RandText(), 3 => Rng.Next(2) == 0, 4 => float.NaN, _ => new byte[3] });
            try { rc.HandleOsc(new OscMessage(addr, args), "127.0.0.1"); }
            catch (Exception e) { Bug("Remote OSC routing", e, addr + " " + string.Join(" ", args)); }
        }
        for (int i = 0; i < 20_000; i++)
            try { rc.HandleTimecode(new Timecode(Rng.Next(-2, 30), Rng.Next(-2, 70), Rng.Next(-2, 70), Rng.Next(-2, 40)), (TimecodeRate)Rng.Next(4)); }
            catch (Exception e) { Bug("Remote timecode", e, i.ToString()); }
    }

    static void FuzzMsc()
    {
        for (int i = 0; i < 200_000; i++)
        {
            var b = RandBytes(24);
            if (b.Length > 2 && Rng.Next(2) == 0) { b[0] = 0xF0; b[1] = 0x7F; b[^1] = 0xF7; if (b.Length > 3) b[3] = 0x02; }
            try { MscCommand.Parse(b); } catch (Exception e) { Bug("MSC parse", e, Convert.ToHexString(b)); }
        }
        try { MscCommand.Parse(null); MscCommand.Parse(Array.Empty<byte>()); } catch (Exception e) { Bug("MSC parse", e, "null/tom"); }
    }

    static void FuzzUnescape()
    {
        string[] esc = { "\\", "\\x", "\\x0", "\\xZZ", "\\xFF", "\\n", "\\r", "\\t", "\\\\", "\\q", "æ" };
        for (int i = 0; i < 100_000; i++)
        {
            var t = string.Concat(Enumerable.Range(0, Rng.Next(6)).Select(_ => Rng.Next(2) == 0 ? esc[Rng.Next(esc.Length)] : RandText()));
            try { NetworkSender.Unescape(t); } catch (Exception e) { Bug("Network unescape", e, t); }
        }
    }

    // ---------- showfiler ----------
    static readonly string[] Types = { "audio", "video", "image", "pdf", "fade", "stop", "osc", "midi", "goto", "playlist", "memo", "network", "web", "timecode", "light", "input" };

    static ShowDocument RandomShow()
    {
        var json = new JsonObject { ["FormatVersion"] = 1, ["Title"] = RandText() };
        var cues = new JsonArray();
        for (int i = Rng.Next(1, 25); i > 0; i--)
        {
            var c = new JsonObject { ["type"] = Types[Rng.Next(Types.Length)], ["Number"] = Rng.Next(100).ToString(), ["Name"] = RandText() };
            cues.Add(c);
        }
        json["Cues"] = cues;
        var doc = JsonSerializer.Deserialize<ShowDocument>(json.ToJsonString(), ShowDocument.JsonOptions)!;
        // Tilfeldige verdier i alle skrivbare egenskaper
        foreach (var cue in doc.Cues)
            foreach (var p in cue.GetType().GetProperties().Where(p => p.CanWrite && p.GetSetMethod() != null))
            {
                if (Rng.Next(3) != 0) continue;
                object? v = p.PropertyType switch
                {
                    var t when t == typeof(string) => RandText(),
                    var t when t == typeof(double) => Rng.Next(8) switch { 0 => double.NaN, 1 => -1e9, 2 => 1e9, 3 => 0, _ => Rng.NextDouble() * 200 - 100 },
                    var t when t == typeof(double?) => Rng.Next(3) == 0 ? null : Rng.NextDouble() * 60 - 50,
                    var t when t == typeof(int) => Rng.Next(6) switch { 0 => int.MinValue, 1 => int.MaxValue, 2 => -1, 3 => 0, _ => Rng.Next(1, 600) },
                    var t when t == typeof(bool) => Rng.Next(2) == 0,
                    var t when t.IsEnum => Enum.GetValues(t).GetValue(Rng.Next(Enum.GetValues(t).Length)),
                    var t when t == typeof(List<AudioRoute>) => Rng.Next(3) == 0 ? null : Enumerable.Range(0, Rng.Next(6)).Select(_ => new AudioRoute(Rng.Next(-1, 70), Rng.Next(-1, 70), Rng.NextDouble() * 80 - 70)).ToList(),
                    var t when t == typeof(List<string>) => Enumerable.Range(0, Rng.Next(4)).Select(_ => RandText()).ToList(),
                    _ => p.GetValue(cue),
                };
                try { p.SetValue(cue, v); } catch { }
            }
        return doc;
    }

    static void Exercise(Cue c, string where)
    {
        try
        {
            _ = c.DisplayName; _ = c.Summary; _ = c.TypeName; _ = c.TriggerText;
            if (c is IRoutedAudio r) foreach (var n in new[] { 0, 1, 2, 6, 64 }) r.EffectiveRoutes(n);
            if (c is LightCue l) DmxLevels.TryParse(l.Levels, out _, out _);
        }
        catch (Exception e) { Bug(where, e, $"{c.GetType().Name} {JsonSerializer.Serialize<Cue>(c, ShowDocument.JsonOptions)}"); }
    }

    static void ShowRoundtrip()
    {
        for (int i = 0; i < 3000; i++)
        {
            var doc = RandomShow();
            string a = "";
            try
            {
                a = doc.ToJson();
                var back = JsonSerializer.Deserialize<ShowDocument>(a, ShowDocument.JsonOptions)!;
                var b = back.ToJson();
                if (a != b) { Bug("Show roundtrip", "lagret show blir annerledes etter åpne + lagre", FirstDiff(a, b)); }
                foreach (var c in back.Cues) Exercise(c, "Show roundtrip (cue)");
            }
            catch (Exception e) { Bug("Show roundtrip", e, a.Length > 300 ? a[..300] : a); }
        }
    }

    static string FirstDiff(string a, string b)
    {
        int i = 0; while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
        return $"…{a.Substring(Math.Max(0, i - 60), Math.Min(120, a.Length - Math.Max(0, i - 60)))}…  VS  …{b.Substring(Math.Max(0, i - 60), Math.Min(120, b.Length - Math.Max(0, i - 60)))}…";
    }

    static void ShowMutation()
    {
        var baseJson = RandomShow().ToJson();
        var path = Path.Combine(Dir, "mut.showcue");
        for (int i = 0; i < 20_000; i++)
        {
            var s = i % 50 == 0 ? (baseJson = RandomShow().ToJson()) : baseJson;
            var sb = new StringBuilder(s);
            for (int k = Rng.Next(1, 4); k > 0 && sb.Length > 0; k--)
            {
                int pos = Rng.Next(sb.Length);
                switch (Rng.Next(5))
                {
                    case 0: sb[pos] = (char)Rng.Next(32, 127); break;
                    case 1: sb.Insert(pos, Pieces[Rng.Next(Pieces.Length)]); break;
                    case 2: sb.Remove(pos, Math.Min(Rng.Next(1, 20), sb.Length - pos)); break;
                    case 3: sb.Length = pos; break;
                    default:
                        var str = sb.ToString();
                        str = System.Text.RegularExpressions.Regex.Replace(str, @": (-?[0-9.]+|true|false|""[^""]*"")", m => Rng.Next(10) == 0 ? ": " + new[] { "null", "[]", "{}", "1e999", "-0", "\"x\"", "true", "99999999999" }[Rng.Next(8)] : m.Value);
                        sb = new StringBuilder(str); break;
                }
            }
            var text = sb.ToString();
            // Kutt aldri et surrogatpar (gir ugyldig UTF-16, som ingen editor lagrer)
            text = new string(text.Where((ch, idx) => !char.IsSurrogate(ch) || (char.IsHighSurrogate(ch) && idx + 1 < text.Length && char.IsLowSurrogate(text[idx + 1])) || (char.IsLowSurrogate(ch) && idx > 0 && char.IsHighSurrogate(text[idx - 1]))).ToArray());
            File.WriteAllText(path, text);
            ShowDocument? doc = null;
            try { doc = ShowDocument.Load(path); }
            catch (JsonException) { continue; }
            catch (InvalidDataException) { continue; }
            catch (Exception e) { Bug("Show mutation (åpne)", e, text.Length > 200 ? text[..200] : text); continue; }
            try
            {
                if (doc.Cues == null) { Bug("Show mutation", "Cues er null etter åpning", text[..Math.Min(200, text.Length)]); continue; }
                foreach (var c in doc.Cues)
                {
                    if (c == null) { Bug("Show mutation", "null-cue i lista etter åpning", text[..Math.Min(200, text.Length)]); continue; }
                    Exercise(c, "Show mutation (cue)");
                }
                doc.ToJson();
                MediaTools.MediaPaths(doc.Cues.Where(c => c != null));
            }
            catch (Exception e) { Bug("Show mutation (bruk)", e, text.Length > 200 ? text[..200] : text); }
        }
    }

    // ---------- lyd ----------
    static string Wav(string name, int rate, int ch, int frames)
    {
        var path = Path.Combine(Dir, name);
        if (File.Exists(path)) return path;
        using var w = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(rate, ch));
        var buf = new float[Math.Max(1, frames) * ch];
        for (int i = 0; i < buf.Length; i++) buf[i] = (float)Math.Sin(i * 0.01) * 0.5f;
        if (frames > 0) w.WriteSamples(buf, 0, buf.Length);
        return path;
    }

    public static volatile string CurrentCase = "";
    static void PlaybackEdges()
    {
        var files = new[] { Wav("empty.wav", 48000, 2, 0), Wav("one.wav", 48000, 1, 1), Wav("short.wav", 44100, 2, 100), Wav("s.wav", 48000, 2, 48000),
                            Wav("six.wav", 96000, 6, 9600), Wav("odd.wav", 22050, 3, 5000) };
        double[] times = { -5, 0, 0.001, 0.5, 1, 1e9, double.NaN, double.PositiveInfinity };
        double[] dbs = { -1000, -60, -12, 0, 12, 1e9, double.NaN };
        for (int i = 0; i < 6000; i++)
        {
            var cue = new AudioCue
            {
                FilePath = files[Rng.Next(files.Length)], StartTime = times[Rng.Next(times.Length)], EndTime = times[Rng.Next(times.Length)],
                Loop = Rng.Next(2) == 0, FadeIn = times[Rng.Next(times.Length)], FadeOut = times[Rng.Next(times.Length)], VolumeDb = dbs[Rng.Next(dbs.Length)],
            };
            if (Rng.Next(3) == 0) cue.Routes = Enumerable.Range(0, Rng.Next(5)).Select(_ => new AudioRoute(Rng.Next(-1, 8), Rng.Next(-1, 70), dbs[Rng.Next(dbs.Length)])).ToList();
            var desc = $"start={cue.StartTime} end={cue.EndTime} loop={cue.Loop} in={cue.FadeIn} out={cue.FadeOut} db={cue.VolumeDb} fil={Path.GetFileName(cue.FilePath)} routes={cue.Routes?.Count}";
            CurrentCase = desc;
            var mixer = new PatchMixer(48000, Rng.Next(1, 9), PatchBus.DefaultPatch(Rng.Next(1, 9)));
            CuePlayback? pb = null;
            var sw = Stopwatch.StartNew();
            try
            {
                pb = new CuePlayback(cue, 48000, offset: times[Rng.Next(times.Length)]);
                mixer.Add(pb);
                var buf = new float[512 * mixer.DeviceChannels];
                for (int k = 0; k < 40 && sw.ElapsedMilliseconds < 2000; k++)
                {
                    switch (Rng.Next(12))
                    {
                        case 0: pb.Stop(times[Rng.Next(times.Length)]); break;
                        case 1: pb.FadeTo(dbs[Rng.Next(dbs.Length)], times[Rng.Next(times.Length)], Rng.Next(2) == 0, (FadeCurve)Rng.Next(3)); break;
                        case 2: pb.ReleaseLoop(); break;
                        case 3: pb.Pause(); break;
                        case 4: pb.Resume(); break;
                        case 5: pb.AdjustLevel(dbs[Rng.Next(dbs.Length)]); break;
                        case 6: pb.RequestRewind(); break;
                        case 8: pb.Seek(times[Rng.Next(times.Length)]); break;
                        case 9: pb.Seek(Rng.NextDouble() * 2); break;
                        case 7: pb.SetRouting(Enumerable.Range(0, Rng.Next(4)).Select(_ => new AudioRoute(Rng.Next(-1, 8), Rng.Next(-1, 70), 0)).ToList()); break;
                    }
                    mixer.Read(buf, 0, buf.Length);
                    foreach (var f in buf) if (!float.IsFinite(f)) { Bug("Playback edge cases", "NaN/uendelig i lydutgangen", desc); break; }
                    _ = pb.Elapsed; _ = pb.PassPosition; _ = pb.PassLength;
                }
                if (sw.ElapsedMilliseconds >= 2000) Bug("Playback edge cases", "avspilling bruker over 2 s på 40 blokker (heng?)", desc);
            }
            catch (Exception e) { Bug("Playback edge cases", e, desc); }
            finally { try { pb?.Dispose(); } catch (Exception e) { Bug("Playback dispose", e, desc); } }
        }
    }

    static void MixerConcurrency()
    {
        var file = Wav("loop.wav", 48000, 2, 4800);
        var mixer = new PatchMixer(48000, 8, PatchBus.DefaultPatch(8));
        var stop = DateTime.UtcNow.AddSeconds(8);
        var errors = new ConcurrentBag<Exception>();
        var live = new ConcurrentQueue<CuePlayback>();
        var audio = new Thread(() =>
        {
            var buf = new float[256 * 8];
            while (DateTime.UtcNow < stop)
                try
                {
                    mixer.Read(buf, 0, buf.Length);
                    foreach (var f in buf) if (!float.IsFinite(f)) { errors.Add(new Exception("NaN i mikseren")); break; }
                    _ = mixer.BusLevels; _ = mixer.ChannelLevels;
                }
                catch (Exception e) { errors.Add(e); }
        });
        var control = new Thread(() =>
        {
            while (DateTime.UtcNow < stop)
                try
                {
                    switch (Rng.Next(8))
                    {
                        case 0:
                            var pb = new CuePlayback(new AudioCue { FilePath = file, Loop = true, FadeIn = 0.01 }, 48000);
                            mixer.Add(pb); live.Enqueue(pb); break;
                        case 1: if (live.TryDequeue(out var a)) { a.Stop(0); mixer.Remove(a); a.Dispose(); } break;
                        case 2: mixer.SetPatch(PatchBus.DefaultPatch(Rng.Next(1, 12))); break;
                        case 3: if (live.TryPeek(out var b)) b.SetRouting(Enumerable.Range(0, 3).Select(i => new AudioRoute(Rng.Next(1, 3), Rng.Next(1, 14), 0)).ToList()); break;
                        case 4: if (live.TryPeek(out var c)) c.FadeTo(-20, 0.05, false); break;
                        case 7: if (live.TryPeek(out var d)) d.Seek(Rng.NextDouble() * 0.2); break;
                        case 5: mixer.Add(new TestToneSource(Rng.Next(0, 14))); break;
                        case 6: if (Rng.Next(50) == 0) mixer.Clear(); break;
                    }
                }
                catch (Exception e) { errors.Add(e); }
        });
        audio.Start(); control.Start(); audio.Join(); control.Join();
        foreach (var e in errors) Bug("Mixer concurrency", e, "8 s samtidig avspilling og endringer");
    }

    static void LoudnessExtremes()
    {
        float[][] inputs =
        {
            Array.Empty<float>(), new float[1], Enumerable.Repeat(float.NaN, 9600).ToArray(), Enumerable.Repeat(float.PositiveInfinity, 9600).ToArray(),
            Enumerable.Repeat(1e30f, 96000).ToArray(), Enumerable.Repeat(1e-30f, 96000).ToArray(),
        };
        foreach (var ch in new[] { 1, 2, 5, 6, 8 })
            foreach (var rate in new[] { 8000, 44100, 48000, 192000 })
                foreach (var inp in inputs)
                    try
                    {
                        var m = new LoudnessMeter(ch, rate); m.Add(inp); m.Add(inp);
                        var v = m.Integrated;
                        if (v is double d && !double.IsFinite(d)) Bug("Loudness extremes", $"ikke-endelig LUFS ({d})", $"ch={ch} rate={rate} len={inp.Length} v0={(inp.Length > 0 ? inp[0] : 0)}");
                        if (v is double d2) LoudnessMeter.GainFor(d2, -23);
                    }
                    catch (Exception e) { Bug("Loudness extremes", e, $"ch={ch} rate={rate} len={inp.Length}"); }
        try { LoudnessMeter.MeasureFile(Wav("empty.wav", 48000, 2, 0)); } catch (Exception e) { Bug("Loudness extremes", e, "tom fil"); }
        var g = LoudnessMeter.GainFor(double.NaN, -23);
        if (!double.IsFinite(g)) Bug("Loudness extremes", $"GainFor(NaN) gir {g}", "");
    }

    static void SafetyPaths()
    {
        string[] names = { "", "a", "show.showcue", "../../etc/passwd.showcue", "C:\\x\\y.showcue", "con.showcue", "a:b.showcue", new string('x', 300) + ".showcue", "æøå 🔒.showcue", "    .showcue" };
        var root = Path.Combine(Dir, "safety");
        foreach (var n in names)
        {
            try
            {
                var p = ShowSafety.AutosavePath(n, root);
                if (!Path.GetFullPath(p).StartsWith(Path.GetFullPath(root))) Bug("Safety paths", "autolagring havner utenfor mappa", n);
                ShowSafety.WriteAutosave(n, "{}", root); ShowSafety.PendingAutosave(n, root); ShowSafety.DeleteAutosave(n, root);
                var real = Path.Combine(Dir, "real.showcue"); File.WriteAllText(real, "{}");
                for (int i = 0; i < 25; i++) ShowSafety.Backup(real, root, DateTime.Now.AddSeconds(i));
                var count = Directory.GetFiles(ShowSafety.BackupDir(root), "real *.showcue").Length;
                if (count > 20) Bug("Safety paths", $"{count} sikkerhetskopier (skal være maks 20)", n);
            }
            catch (Exception e) { Bug("Safety paths", e, n); }
        }
        try { ShowSafety.AutosavePath(null, root); ShowSafety.PendingAutosave(null, root); } catch (Exception e) { Bug("Safety paths", e, "null"); }
    }

    static void MediaPaths()
    {
        string[] paths = { "", "x", "/a/b/c.wav", "C:\\Lyd\\fil.wav", "\\\\server\\share\\f.wav", "fil:med:kolon.wav", "../../f.wav", new string('y', 400) + ".wav", "æ/ø/å.wav", "f\0.wav" };
        foreach (var p in paths)
            try
            {
                MediaTools.FileName(p);
                var cues = new List<Cue> { new AudioCue { FilePath = p }, new PlaylistCue { Files = new() { p, p } }, new ImageCue { FilePath = p } };
                MediaTools.Missing(cues);
                MediaTools.Relink(cues, Dir);
                var doc = new ShowDocument(); foreach (var c in cues) doc.Cues.Add(c);
                MediaTools.Collect(doc, Path.Combine(Dir, "collect" + Rng.Next()), "Show");
            }
            catch (Exception e) { Bug("Media tools paths", e, p); }
    }

    static void FuzzLtcDecode()
    {
        for (int i = 0; i < 200_000; i++)
        {
            var bits = Enumerable.Range(0, Rng.Next(0, 100)).Select(_ => Rng.Next(2) == 0).ToArray();
            try { Ltc.Decode(bits); } catch (Exception e) { Bug("Timecode decode", e, bits.Length.ToString()); }
        }
    }

    static void FuzzDmxFader()
    {
        var f = new DmxFader();
        for (int i = 0; i < 100_000; i++)
        {
            try
            {
                var levels = new Dictionary<int, byte>();
                for (int k = Rng.Next(5); k > 0; k--) levels[Rng.Next(-5, 520)] = (byte)Rng.Next(256);
                double[] t = { -1, 0, 1, double.NaN, double.PositiveInfinity, 1e12 };
                f.FadeTo(levels, t[Rng.Next(t.Length)], t[Rng.Next(t.Length)]);
                var v = f.Values(t[Rng.Next(t.Length)]);
                if (v.Length != 512) Bug("Dmx fader", "feil antall kanaler", "");
            }
            catch (Exception e) { Bug("Dmx fader", e, i.ToString()); }
        }
    }

    static void NetworkSend()
    {
        // Lokal mottaker for UDP og TCP, og en HTTP-server som svarer tregt eller feil.
        var udp = new System.Net.Sockets.UdpClient(0); int udpPort = ((System.Net.IPEndPoint)udp.Client.LocalEndPoint!).Port;
        var tcpL = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); tcpL.Start(); int tcpPort = ((System.Net.IPEndPoint)tcpL.LocalEndpoint).Port;
        var cts = new CancellationTokenSource();
        _ = Task.Run(async () => { while (!cts.IsCancellationRequested) try { var c = await tcpL.AcceptTcpClientAsync(cts.Token); if (Rng.Next(2) == 0) c.Close(); } catch { } });
        _ = Task.Run(async () => { while (!cts.IsCancellationRequested) try { await udp.ReceiveAsync(cts.Token); } catch { } });
        string[] hosts = { "127.0.0.1", "localhost", "", " ", "999.1.1.1", "::1", "ikke.en.vert.invalid", "256.256.256.256" };
        string[] urls = { "", "http://127.0.0.1:1/", "http://127.0.0.1:" + tcpPort + "/x", "ftp://x", "http://", "https://[::1]:1/", "javascript:alert(1)", "http://ikke.en.vert.invalid/" };
        var tasks = new List<Task>();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 300; i++)
        {
            var cue = new NetworkCue
            {
                Protocol = (NetworkProtocol)Rng.Next(3), Host = hosts[Rng.Next(hosts.Length)],
                Port = new[] { udpPort, tcpPort, 1, 65535, 0, -5, 70000 }[Rng.Next(7)], Message = RandText(),
                Url = urls[Rng.Next(urls.Length)], Method = new[] { "GET", "POST", "PUT", "DELETE", "", "BAD METHOD" }[Rng.Next(6)],
            };
            var desc = $"{cue.Protocol} {cue.Host}:{cue.Port} {cue.Url} {cue.Method}";
            tasks.Add(Task.Run(async () =>
            {
                var t0 = Stopwatch.StartNew();
                try { await NetworkSender.SendAsync(cue); }
                catch (Exception e) { Bug("Network sender", e, desc); }
                if (t0.Elapsed.TotalSeconds > 15) Bug("Network sender", $"brukte {t0.Elapsed.TotalSeconds:0} s", desc);
            }));
        }
        Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(90));
        if (tasks.Any(t => !t.IsCompleted)) Bug("Network sender", "sending ble ikke ferdig på 90 s", "");
        cts.Cancel(); udp.Dispose(); tcpL.Stop();
    }

    static void LtcRender()
    {
        for (int i = 0; i < 300; i++)
        {
            var rate = (TimecodeRate)Rng.Next(4);
            var bus = Rng.Next(-2, 12);
            var desc = $"bus={bus} rate={rate}";
            try
            {
                var src = new LtcSource(bus, new[] { -40.0, 0, double.NaN, -1000, 50 }[Rng.Next(5)], new Timecode(Rng.Next(24), Rng.Next(60), Rng.Next(60), Rng.Next(30)), rate, new[] { 44100, 48000, 96000, 8000 }[Rng.Next(4)]);
                var busCount = Rng.Next(1, 9);
                var buf = new float[512 * busCount];
                for (int k = 0; k < 50; k++)
                {
                    if (k == 20) src.Pause(); if (k == 25) src.Resume();
                    Array.Clear(buf); src.Render(buf, 512, busCount);
                    if (buf.Any(f => !float.IsFinite(f))) { Bug("LTC source", "NaN i LTC-lyden", desc); break; }
                }
            }
            catch (Exception e) { Bug("LTC source", e, desc); }
        }
    }

    static void WaveformCompute()
    {
        foreach (var f in new[] { "empty.wav", "one.wav", "short.wav", "six.wav", "odd.wav" })
            foreach (var buckets in new[] { 0, 1, 7, 2000, 1_000_000 })
                foreach (var total in new long[] { 0, 1, 100, -5, long.MaxValue / 2 })
                    try
                    {
                        using var r = new AudioFileReader(Wav(f, 48000, 2, 10));
                        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        var sw = Stopwatch.StartNew();
                        WaveformData.Compute(r, total, buckets, cts.Token);
                        if (sw.Elapsed.TotalSeconds > 4) Bug("Waveform", "bølgeform tok over 4 s", $"{f} buckets={buckets} total={total}");
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception e) { Bug("Waveform", e, $"{f} buckets={buckets} total={total}"); }
    }

    static void FeedbackValues()
    {
        double[] t = { 0, -1, 1e9, double.NaN, double.PositiveInfinity, 3599.9, 86400 };
        for (int i = 0; i < 20_000; i++)
        {
            var playing = Enumerable.Range(0, Rng.Next(5)).Select(_ => new PlayingCue(RandText(), RandText(), t[Rng.Next(t.Length)], t[Rng.Next(t.Length)], Rng.Next(2) == 0, Rng.Next(2) == 0)).ToList();
            var s = new ShowStatus(RandText(), RandText(), RandText(), RandText(), Rng.Next(-1, 50), Rng.Next(-1, 50), playing);
            try
            {
                foreach (var (k, v) in OscFeedback.Values(s)) Osc.Encode("/x/" + k, new List<object> { v });
                OscFeedback.Clock(t[Rng.Next(t.Length)]); TimeFormat.Format(t[Rng.Next(t.Length)] * (Rng.Next(2) == 0 ? -1e6 : 1));
            }
            catch (Exception e) { Bug("OSC feedback", e, string.Join(",", playing.Select(p => $"{p.Elapsed}/{p.Remaining}"))); }
        }
    }

    static void UndoFuzz()
    {
        var u = new UndoHistory(Rng.Next(1, 30));
        var model = new List<string>(); // det brukeren skal se
        u.Reset("0"); var history = new List<string> { "0" }; int pos = 0;
        for (int i = 0; i < 100_000; i++)
        {
            try
            {
                switch (Rng.Next(3))
                {
                    case 0: var st = Rng.Next(1000).ToString(); if (u.Record(st)) { history.RemoveRange(pos + 1, history.Count - pos - 1); history.Add(st); pos++; } break;
                    case 1: var a = u.Undo(); if (a != null) { pos--; } break;
                    case 2: var b = u.Redo(); if (b != null) { pos++; } break;
                }
                if (u.Current != history[pos]) { Bug("Undo history", "angre/gjør om havnet i feil tilstand", i.ToString()); u.Reset("0"); history = new() { "0" }; pos = 0; }
            }
            catch (Exception e) { Bug("Undo history", e, i.ToString()); }
        }
    }

    static void SaveLoadFiles()
    {
        for (int i = 0; i < 300; i++)
        {
            var doc = RandomShow();
            var path = Path.Combine(Dir, $"save{i % 5}.showcue");
            try
            {
                doc.Save(path);
                var back = ShowDocument.Load(path);
                if (back.Cues.Count != doc.Cues.Count) Bug("Show save/load files", "antall cuer endret seg", path);
                ShowSafety.Backup(path, Path.Combine(Dir, "safety2"));
            }
            catch (Exception e) { Bug("Show save/load files", e, path); }
        }
    }
}
