using ShowCue.Engine;
using ShowCue.Models;
using Cue = ShowCue.Models.Cue;

namespace ShowCue.Tests;

/// <summary>Forhåndssjekk før show. Sjekklogikken testes med en falsk maskin (filer, lydenheter, skjermer). Samme tilfeller som på Mac.</summary>
public class PreflightTests
{
    private sealed class FakeEnv : IPreflightEnvironment
    {
        public Dictionary<string, FileStatus> Files = new();
        public HashSet<string> AudioOutputs = new();
        public HashSet<string> Inputs = new();
        public int Screens = 1;
        public FileStatus CheckFile(string path) => Files.TryGetValue(path, out var s) ? s : FileStatus.Missing;
        public bool AudioOutputExists(AudioDriverType driver, string id) => AudioOutputs.Contains(id);
        public bool InputDeviceExists(string id) => Inputs.Contains(id);
        public bool ScreenExists(int index) => index >= 0 && index < Screens;
    }

    private static List<PreflightFinding> Run(IReadOnlyList<Cue> cues, FakeEnv? env = null, AppSettings? settings = null) =>
        PreflightCheck.Run(cues, settings ?? new AppSettings(), env ?? new FakeEnv());

    private static string[] Codes(List<PreflightFinding> f, PreflightSeverity s) => f.Where(x => x.Severity == s).Select(x => x.Code).ToArray();

    [Fact]
    public void EmptyShowOnlyGivesOkLines()
    {
        var f = Run(new List<Cue>());
        Assert.Equal(new[] { "mediaOk", "valuesOk", "devicesOk", "numbersOk" }, f.Select(x => x.Code));
        Assert.All(f, x => Assert.Equal(PreflightSeverity.Ok, x.Severity));
    }

    [Fact]
    public void MediaFilesMustExistAndBeReadable()
    {
        var env = new FakeEnv();
        env.Files["a.wav"] = FileStatus.Ok;
        env.Files["b.wav"] = FileStatus.Unreadable;
        var cues = new List<Cue>
        {
            new AudioCue { Number = "1", FilePath = "a.wav" },
            new AudioCue { Number = "2", FilePath = "b.wav" },
            new AudioCue { Number = "3", FilePath = "borte.wav" },
            new ImageCue { Number = "4" },
            new PlaylistCue { Number = "5", Files = new() { "a.wav", "borte2.wav" } },
            new SlideshowCue { Number = "6" },
            new WebCue { Number = "7", Url = "https://example.org" },
        };
        var f = Run(cues, env);
        Assert.Equal(new[] { "mediaUnreadable", "mediaMissing", "mediaEmpty", "mediaMissing" }, Codes(f, PreflightSeverity.Error).Take(4));
        Assert.Equal(new[] { 1, 2, 3, 4 }, f.Where(x => x.Severity == PreflightSeverity.Error).Select(x => x.CueIndex));
        Assert.Equal(new[] { "listEmpty" }, Codes(f, PreflightSeverity.Warning));
        Assert.DoesNotContain(f, x => x.Code == "mediaOk");
    }

    [Fact]
    public void AllMediaOkIsCountedOncePerFile()
    {
        var env = new FakeEnv();
        env.Files["a.wav"] = FileStatus.Ok;
        env.Files["b.png"] = FileStatus.Ok;
        var f = Run(new List<Cue> { new AudioCue { Number = "1", FilePath = "a.wav" }, new ImageCue { Number = "2", FilePath = "b.png" }, new PlaylistCue { Number = "3", Files = new() { "a.wav", "A.WAV" } } }, env);
        Assert.Equal(PreflightSeverity.Ok, f.Single(x => x.Code == "mediaOk").Severity);
        Assert.Empty(Codes(f, PreflightSeverity.Error));
    }

    [Fact]
    public void DisabledCuesAreSkippedButTheirNumbersCount()
    {
        var cues = new List<Cue>
        {
            new AudioCue { Number = "1", FilePath = "borte.wav", Disabled = true },
            new FadeCue { Number = "1", Target = "99", Disabled = true },
        };
        Assert.Equal(new[] { "numberDuplicate" }, Codes(Run(cues), PreflightSeverity.Error));
    }

    [Fact]
    public void TargetsMustExist()
    {
        var cues = new List<Cue>
        {
            new MemoCue { Number = "1", Text = "Intro" },
            new FadeCue { Number = "2", Target = "" },
            new FadeCue { Number = "3", Target = "99" },
            new GotoCue { Number = "4", Target = " a1 " },
            new StopCue { Number = "5", Target = "" },          // stopp alt
            new StopCue { Number = "6", Target = "1" },
            new GotoCue { Number = "7", Target = "7" },         // peker på seg selv
            new MemoCue { Number = "A1", Text = "x", Disabled = true },
            new GotoCue { Number = "8", Target = "a1" },        // mål er deaktivert
        };
        var f = Run(cues);
        Assert.Equal(new[] { "targetEmpty", "targetMissing" }, Codes(f, PreflightSeverity.Error));
        Assert.Equal(new[] { "targetDisabled", "targetSelf", "targetDisabled" }, Codes(f, PreflightSeverity.Warning));
    }

    [Fact]
    public void RequiredFieldsAndAddresses()
    {
        var cues = new List<Cue>
        {
            new OscCue { Number = "1", Host = "", Address = "/x" },
            new OscCue { Number = "2", Host = "10.0.0.1", Address = "" },
            new NetworkCue { Number = "3", Protocol = NetworkProtocol.Udp, Host = " " },
            new NetworkCue { Number = "4", Protocol = NetworkProtocol.Http, Url = "http://" },
            new NetworkCue { Number = "5", Protocol = NetworkProtocol.Http, Url = "https://x.no/a" },
            new LightCue { Number = "6", Host = "" },
            new MidiCue { Number = "7", MessageType = MidiMessageType.MscGo, MscCueNumber = "" },
            new WebCue { Number = "8", Url = "https://" },
            new WebCue { Number = "9", Url = "www.x.no" },
            new MemoCue { Number = "10", Text = "  " },
        };
        var f = Run(cues);
        Assert.Equal(new[] { "requiredEmpty", "requiredEmpty", "requiredEmpty", "urlInvalid", "requiredEmpty", "requiredEmpty", "urlInvalid" }, Codes(f, PreflightSeverity.Error));
        Assert.Equal(new[] { "memoEmpty" }, Codes(f, PreflightSeverity.Warning));
    }

    [Fact]
    public void WebAddressRules()
    {
        Assert.True(PreflightCheck.IsWebAddress("https://x.no"));
        Assert.True(PreflightCheck.IsWebAddress("www.x.no"));
        Assert.True(PreflightCheck.IsWebAddress("file:///tmp/a.html"));
        Assert.False(PreflightCheck.IsWebAddress(""));
        Assert.False(PreflightCheck.IsWebAddress("https://"));
        Assert.False(PreflightCheck.IsWebAddress("ftp://x.no"));
        Assert.True(PreflightCheck.IsHttpUrl("HTTP://x"));
        Assert.False(PreflightCheck.IsHttpUrl("http://a b"));
    }

    [Fact]
    public void StartAndEndMustMakeSense()
    {
        var env = new FakeEnv();
        env.Files["a.wav"] = FileStatus.Ok;
        var cues = new List<Cue>
        {
            new AudioCue { Number = "1", FilePath = "a.wav", StartTime = 10, EndTime = 5 },
            new AudioCue { Number = "2", FilePath = "a.wav", StartTime = 30, FileDuration = 20 },
            new AudioCue { Number = "3", FilePath = "a.wav", StartTime = 5, EndTime = 8, FileDuration = 20 },
        };
        Assert.Equal(new[] { "timeOrder", "startBeyondFile" }, Codes(Run(cues, env), PreflightSeverity.Error));
    }

    [Fact]
    public void ModelSanitizesInvalidNumbersSoNoFindingArises()
    {
        // Windows-modellen gjør NaN, uendelig og negative tider om til gyldige verdier. Sjekken har samme regel som på Mac for ugyldige tall.
        var f = new FadeCue { Number = "1", Target = "2", FadeTime = double.NaN, PreWait = -3 };
        var cues = new List<Cue> { f, new AudioCue { Number = "2" } };
        Assert.DoesNotContain(Run(cues), x => x.Code == "valueInvalid");
    }

    [Fact]
    public void AudioOutputMustExistWhenShowUsesAudio()
    {
        var cues = new List<Cue> { new AudioCue { Number = "1" } };
        var env = new FakeEnv();
        env.Files[""] = FileStatus.Ok;
        // Standardenheten (tom ID) finnes alltid.
        Assert.DoesNotContain(Run(cues, env), x => x.Code == "audioDeviceMissing");
        var s = new AppSettings { AudioDriver = AudioDriverType.Wasapi, WasapiDeviceId = "enhet-1" };
        Assert.Contains(Run(cues, env, s), x => x.Code == "audioDeviceMissing" && x.Severity == PreflightSeverity.Error);
        env.AudioOutputs.Add("enhet-1");
        Assert.DoesNotContain(Run(cues, env, s), x => x.Code == "audioDeviceMissing");
        // ASIO uten valgt driver, og med driver som mangler.
        var asio = new AppSettings { AudioDriver = AudioDriverType.Asio, AsioDriverName = "" };
        Assert.Contains(Run(cues, env, asio), x => x.Code == "audioDeviceMissing");
        asio.AsioDriverName = "Kort";
        Assert.Contains(Run(cues, env, asio), x => x.Code == "audioDeviceMissing");
        env.AudioOutputs.Add("Kort");
        Assert.DoesNotContain(Run(cues, env, asio), x => x.Code == "audioDeviceMissing");
        // Uten lydcuer spiller utgangen ingen rolle.
        Assert.DoesNotContain(Run(new List<Cue> { new MemoCue { Number = "1", Text = "x" } }, new FakeEnv(), s), x => x.Code == "audioDeviceMissing");
    }

    [Fact]
    public void PreviewAndInputDevices()
    {
        var env = new FakeEnv();
        var s = new AppSettings { PreviewDeviceId = "hode", InputDeviceId = "mik" };
        var f = Run(new List<Cue> { new InputCue { Number = "1" } }, env, s);
        Assert.Equal(new[] { "inputDeviceMissing" }, Codes(f, PreflightSeverity.Error));
        Assert.Equal(new[] { "previewDeviceMissing" }, Codes(f, PreflightSeverity.Warning));
        env.Inputs.Add("mik");
        env.AudioOutputs.Add("hode");
        Assert.All(Run(new List<Cue> { new InputCue { Number = "1" } }, env, s), x => Assert.Equal(PreflightSeverity.Ok, x.Severity));
        // Ingen live-inngang i showet: inngangsenheten sjekkes ikke.
        Assert.Empty(Codes(Run(new List<Cue>(), new FakeEnv(), new AppSettings { InputDeviceId = "mik" }), PreflightSeverity.Error));
    }

    [Fact]
    public void ScreensMustExist()
    {
        var env = new FakeEnv { Screens = 2 };
        var img = new ImageCue { Number = "1" };
        env.Files[""] = FileStatus.Ok;
        var s = new AppSettings { VideoScreen = 2 };
        var f = Run(new List<Cue> { img }, env, s);
        Assert.Contains(f, x => x.Code == "screenMissing" && x.CueIndex == -1 && x.Severity == PreflightSeverity.Warning);
        // Egen skjerm på cuen.
        var own = new ImageCue { Number = "2", OutputScreen = 3 };
        var f2 = Run(new List<Cue> { own }, env, new AppSettings { VideoScreen = 2 });
        Assert.Equal(new[] { 0 }, f2.Where(x => x.Code == "screenMissing").Select(x => x.CueIndex));
        // Lysbildeserier og eksisterende skjerm.
        var show = new SlideshowCue { Number = "3", OutputScreen = 1, Files = new() { "x.png" } };
        env.Files["x.png"] = FileStatus.Ok;
        Assert.DoesNotContain(Run(new List<Cue> { show }, env, new AppSettings { VideoScreen = -1 }), x => x.Code == "screenMissing");
        show.OutputScreen = 5;
        Assert.Contains(Run(new List<Cue> { show }, env, new AppSettings { VideoScreen = -1 }), x => x.Code == "screenMissing");
    }

    [Fact]
    public void DuplicateNumbersIgnoreCaseAndSpacesButNotEmptyNumbers()
    {
        var cues = new List<Cue>
        {
            new MemoCue { Number = "1", Text = "a" },
            new MemoCue { Number = " 1 ", Text = "b" },
            new MemoCue { Number = "A", Text = "c" },
            new MemoCue { Number = "a", Text = "d" },
            new MemoCue { Number = "", Text = "e" },
            new MemoCue { Number = "", Text = "f" },
            new MemoCue { Number = "2", Text = "g" },
        };
        var f = Run(cues);
        var dup = f.Where(x => x.Code == "numberDuplicate").ToList();
        Assert.Equal(2, dup.Count);
        Assert.Equal(new[] { 0, 2 }, dup.Select(x => x.CueIndex));
        Assert.DoesNotContain(f, x => x.Code == "numbersOk");
    }

    [Fact]
    public void SystemEnvironmentSeesFilesAndDirectories()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "showcue-preflight", Guid.NewGuid().ToString("N"))).FullName;
        var file = Path.Combine(dir, "a.wav");
        File.WriteAllText(file, "x");
        var env = new SystemPreflightEnvironment();
        Assert.Equal(FileStatus.Ok, env.CheckFile(file));
        Assert.Equal(FileStatus.Missing, env.CheckFile(Path.Combine(dir, "borte.wav")));
        Assert.Equal(FileStatus.Missing, env.CheckFile(dir));
    }
}
