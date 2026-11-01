using System.Reflection;
using System.Text;
using System.Windows.Threading;
using ShowCue.Engine;
using ShowCue.Models;

namespace ShowCue.Tests;

public class OscTests
{
    [Fact]
    public void EncodeFloatMatchesSpec()
    {
        var hex = Convert.ToHexString(Osc.Encode("/ch/01/mix/fader", Osc.ParseArguments("0.75")));
        // "/ch/01/mix/fader" = 16 byte + 4 null; ",f" + 2 null; 0.75f = 3F400000
        Assert.Equal(Convert.ToHexString(Encoding.ASCII.GetBytes("/ch/01/mix/fader")) + "00000000" + "2C660000" + "3F400000", hex);
    }

    [Fact]
    public void EncodeIntAndString()
    {
        Assert.Equal("2F676F00" + "2C697300" + "00000007" + "61620000", Convert.ToHexString(Osc.Encode("/go", new List<object> { 7, "ab" })));
        Assert.Equal(8, Osc.Encode("/x", new List<object>()).Length);
    }

    [Fact]
    public void ParseArguments()
    {
        var a = Osc.ParseArguments("1 0,5 hei \"to ord\" true");
        Assert.Equal(5, a.Count);
        Assert.IsType<int>(a[0]);
        Assert.IsType<float>(a[1]);
        Assert.Equal("hei", a[2]);
        Assert.Equal("to ord", a[3]);
        Assert.Equal(true, a[4]);
    }

    [Fact]
    public void DecodeRoundTrip()
    {
        var enc = Osc.Encode("/cue/12/start", new List<object> { 1, 0.5f, "hei", true });
        var dec = Assert.Single(Osc.Decode(enc, enc.Length));
        Assert.Equal("/cue/12/start", dec.Address);
        Assert.Equal(new object[] { 1, 0.5f, "hei", true }, dec.Args);
    }

    [Fact]
    public void DecodeBundle()
    {
        var ms = new MemoryStream();
        ms.Write(Encoding.ASCII.GetBytes("#bundle\0"));
        ms.Write(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 });
        foreach (var m in new[] { Osc.Encode("/go", new List<object>()), Osc.Encode("/cue/3/stop", new List<object>()) })
        {
            var len = BitConverter.GetBytes(m.Length);
            Array.Reverse(len);
            ms.Write(len);
            ms.Write(m);
        }
        var bun = ms.ToArray();
        var dec = Osc.Decode(bun, bun.Length);
        Assert.Equal(new[] { "/go", "/cue/3/stop" }, dec.Select(d => d.Address));
    }

    [Fact]
    public void DecodeIsRobust()
    {
        Assert.Empty(Assert.Single(Osc.Decode(new byte[] { 0x2F, 0x67, 0x6F, 0x00 }, 4)).Args); // eldre sendere uten typetagger
        Assert.Empty(Osc.Decode(new byte[] { 1, 2, 3 }, 3));                                     // søppel
        var trunc = Osc.Encode("/x", new List<object> { 5 });
        Assert.Single(Osc.Decode(trunc, trunc.Length - 2));                                     // avkuttet pakke
    }
}

public class MscTests
{
    [Fact]
    public void GoRoundTrip()
    {
        var msc = MscCommand.Parse(MidiEngine.BuildMscGo(127, 1, "12.5"));
        Assert.NotNull(msc);
        Assert.Equal(MscCommand.Go, msc!.Command);
        Assert.Equal("12.5", msc.CueNumber);
        Assert.Equal(127, msc.DeviceId);
    }

    [Fact]
    public void StopWithCueList()
    {
        var msc = MscCommand.Parse(new byte[] { 0xF0, 0x7F, 1, 2, 1, 2, (byte)'3', 0, (byte)'1', 0xF7 });
        Assert.NotNull(msc);
        Assert.Equal(2, msc!.Command);
        Assert.Equal("3", msc.CueNumber);
    }

    [Fact]
    public void NonMscSysexIsIgnored() => Assert.Null(MscCommand.Parse(new byte[] { 0xF0, 0x7E, 1, 2 }));
}

public class RemoteControlTests
{
    private sealed class FakeShow : IShowControl
    {
        public List<string> Log = new();
        public List<string> Remote = new();
        public void Go() => Log.Add("go");
        public void StopAll() => Log.Add("stopall");
        public void Panic() => Log.Add("panic");
        public void HardStop() => Log.Add("hardstop");
        public void MovePlayhead(int d) => Log.Add("move " + d);
        public bool SelectCue(string n) { Log.Add("select " + n); return true; }
        public bool StartCue(string n) { Log.Add("start " + n); return true; }
        public bool StopCue(string n) { Log.Add("stop " + n); return true; }
        public bool GoCue(string n) { Log.Add("gocue " + n); return true; }
        public bool FireHotkey(string k) { Log.Add("hotkey " + k); return true; }
        public int FireMidiTrigger(string t) { Log.Add("midi " + t); return 1; }
        public void RemoteLog(string t) => Remote.Add(t);
        public void RefreshFeedback() => Log.Add("refresh");
        public void SetPaused(bool? p) => Log.Add("pause " + (p?.ToString() ?? "toggle"));
        public bool ReleaseCue(string n) { Log.Add("release " + n); return true; }
    }

    private readonly FakeShow _show = new();
    private readonly AppSettings _settings = new() { MidiGo = "N:60", MidiStopAll = "CC:20" };
    private readonly RemoteControl _rc;

    public RemoteControlTests() => _rc = new RemoteControl(_show, Dispatcher.CurrentDispatcher, _settings);

    private void Osc1(string addr, params object[] args) => _rc.HandleOsc(new OscMessage(addr, args), "test");
    private void Midi(string trigger) => Invoke("HandleMidi", trigger, 1);
    private void Msc(MscCommand m) => Invoke("HandleMsc", m);
    private void Invoke(string method, params object[] args)
        => typeof(RemoteControl).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(_rc, args);

    [Fact]
    public void GoWithArguments()
    {
        Osc1("/go"); Osc1("/go", 1f); Osc1("/go", 0f); Osc1("/go", 0);
        Assert.Equal(2, _show.Log.Count(x => x == "go")); // /go 0 ignoreres (knapp sluppet)
    }

    [Fact]
    public void OscCommands()
    {
        Osc1("/cue/12/start"); Osc1("/cue/1.5/stop"); Osc1("/cue/7/go"); Osc1("/cue/start", "9"); Osc1("/cue/start", 4);
        Osc1("/playhead/3"); Osc1("/select/next"); Osc1("/previous"); Osc1("/hotkey/f1"); Osc1("/stop"); Osc1("/panic"); Osc1("/hardstop");
        Osc1("/showcue/go"); Osc1("/go", "22"); Osc1("/go/23"); Osc1("/refresh");
        Osc1("/pause"); Osc1("/resume"); Osc1("/pause/toggle"); Osc1("/cue/4/devamp");
        Assert.Equal(new[] { "start 12", "stop 1.5", "gocue 7", "start 9", "start 4", "select 3", "move 1", "move -1", "hotkey F1",
                             "stopall", "panic", "hardstop", "go", "gocue 22", "gocue 23", "refresh",
                             "pause True", "pause False", "pause toggle", "release 4" }, _show.Log);
    }

    [Fact]
    public void UnknownOscIsLogged()
    {
        Osc1("/tull");
        Assert.Contains("ukjent", _show.Remote.Last());
    }

    [Fact]
    public void MidiTriggersAndLearn()
    {
        Midi("N:60"); Midi("CC:20"); Midi("N:61");
        Assert.Equal(new[] { "go", "panic", "midi N:61" }, _show.Log);

        string? learned = null;
        _rc.BeginLearn(t => learned = t);
        _show.Log.Clear();
        Midi("N:60");
        Assert.Equal("N:60", learned);
        Assert.Empty(_show.Log); // «Lær» fanger meldingen uten å utføre den
        Assert.False(_rc.IsLearning);
    }

    [Fact]
    public void MscIn()
    {
        Msc(new MscCommand(127, 1, MscCommand.Go, "5"));
        Msc(new MscCommand(127, 1, MscCommand.Go, ""));
        _settings.MscInDeviceId = 3;
        Msc(new MscCommand(4, 1, MscCommand.Go, "6")); // feil device-ID ignoreres
        Assert.Equal(new[] { "gocue 5", "go" }, _show.Log);
    }
}

public class OscFeedbackTests
{
    private static readonly ShowStatus Status = new("Konsert", "3", "Walk-in", "Vent på lys", 3, 10, new List<PlayingCue> {
        new("1", "Intro", 12.3, 47.6, true, false), new("2", "Jingle", 2, 4.2, true, false),
        new("5", "Loop", 30, 0, false, false), new("7", "Venter", 0, 0, false, true) });

    private static string? V(List<(string A, object V)> sent, string key)
        => sent.Where(x => x.A == $"/custom-variable/sc_{key}/value").Select(x => x.V?.ToString()).LastOrDefault();

    [Fact]
    public void CompanionVariables()
    {
        var sent = new List<(string A, object V)>();
        var fb = new OscFeedback((a, args) => sent.Add((a, args[0])));
        fb.Update(Status, 0);
        Assert.Equal("Walk-in", V(sent, "next_name"));
        Assert.Equal("3", V(sent, "next_number"));
        Assert.Equal("3/10", V(sent, "playhead"));
        Assert.Equal("Konsert", V(sent, "show"));
        Assert.Equal("Loop", V(sent, "playing"));
        Assert.Equal("3", V(sent, "playing_count"));
        Assert.Equal("Intro · Jingle · Loop", V(sent, "playing_all"));
        Assert.Equal("1,2,5", V(sent, "running"));
        Assert.Equal("-0:05", V(sent, "remaining"));
        Assert.Equal("Jingle", V(sent, "remaining_name"));
        Assert.Equal("spiller", V(sent, "state"));
        Assert.All(sent, x => Assert.IsType<string>(x.V));
    }

    [Fact]
    public void OnlyChangesAreSentAndFullRefresh()
    {
        var sent = new List<(string A, object V)>();
        var fb = new OscFeedback((a, args) => sent.Add((a, args[0])));
        fb.Update(Status, 0);
        int full = sent.Count;
        sent.Clear();
        fb.Update(Status, 0.1);
        Assert.Empty(sent);
        fb.Update(Status with { NextName = "Pause" }, 0.2);
        Assert.Equal("/custom-variable/sc_next_name/value", Assert.Single(sent).A);
        sent.Clear();
        fb.Update(Status, 6); // full oppfriskning etter 5 s
        Assert.Equal(full, sent.Count);
        sent.Clear();
        fb.RequestRefresh();
        fb.Update(Status, 6.1);
        Assert.Equal(full, sent.Count);
    }

    [Fact]
    public void GenericFormat()
    {
        var sent = new List<(string A, object V)>();
        var fb = new OscFeedback((a, args) => sent.Add((a, args[0]))) { Format = OscFeedbackFormat.Generic };
        fb.Update(Status, 0);
        Assert.Contains(sent, x => x.A == "/showcue/next/name" && (string)x.V == "Walk-in");
        Assert.Contains(sent, x => x.A == "/showcue/playing/count" && x.V is int c && c == 3);
        Assert.Equal(3, sent.Count(x => x.A.StartsWith("/showcue/cue/") && x.A.EndsWith("/running") && x.V is int r && r == 1));
        Assert.DoesNotContain(sent, x => x.A == "/showcue/cue/7/running"); // pre-wait teller ikke
        sent.Clear();
        fb.Update(Status with { Playing = Status.Playing.Where(p => p.Number != "2").ToList() }, 0.5);
        Assert.Contains(sent, x => x.A == "/showcue/cue/2/running" && x.V is int z && z == 0);
        Assert.DoesNotContain(sent, x => x.A == "/showcue/cue/1/running");
    }

    [Fact]
    public void IdleStatusAndClock()
    {
        var sent = new List<(string A, object V)>();
        new OscFeedback((a, args) => sent.Add((a, args[0]))).Update(new ShowStatus("S", "", "", "", 0, 4, new List<PlayingCue>()), 0);
        Assert.Equal("slutt/4", V(sent, "playhead"));
        Assert.Equal("klar", V(sent, "state"));
        Assert.Equal("", V(sent, "remaining"));
        Assert.Equal("1:02:05", OscFeedback.Clock(3725));
        Assert.Equal("0:59", OscFeedback.Clock(59.9));
    }

    [Fact]
    public void UdpRoundTripToLocalServer()
    {
        var srv = new OscServer();
        srv.Start(39123);
        var got = new System.Collections.Concurrent.ConcurrentQueue<OscMessage>();
        srv.MessageReceived += (m, _) => got.Enqueue(m);
        try
        {
            OscFeedback.ForTarget("127.0.0.1", 39123).Update(Status, 0);
            var until = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < until && !got.Any(m => m.Address == "/custom-variable/sc_next_name/value")) Thread.Sleep(20);
        }
        finally { srv.Stop(); }
        Assert.Contains(got, m => m.Address == "/custom-variable/sc_next_name/value" && (string)m.Args[0] == "Walk-in");
    }
}
