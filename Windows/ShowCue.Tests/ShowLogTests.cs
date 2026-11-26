using System.Text.Json;
using ShowCue.Engine;
using ShowCue.Models;

namespace ShowCue.Tests;

/// <summary>Showloggen: formatering, escaping, CSV, filrotasjon, begrenset kø og skriving til fil.</summary>
public class ShowLogTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 25, 14, 3, 22, 123, TimeSpan.FromHours(1));

    private static ShowLogEvent Ev(ShowLogKind kind = ShowLogKind.Start, string text = "", string name = "Intro", long run = 83456) =>
        new(T0, run, kind, "3", name, "audio", "go", text);

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "showcue-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void TruncateKeepsShortTextAndCutsLongText()
    {
        Assert.Equal("", ShowLogFormat.Truncate(null, 5));
        Assert.Equal("abc", ShowLogFormat.Truncate("abc", 5));
        Assert.Equal("abcde", ShowLogFormat.Truncate("abcde", 5));
        var cut = ShowLogFormat.Truncate("abcdefghij", 5);
        Assert.Equal("abcd…", cut);
        Assert.Equal(5, cut.Length);
        Assert.Equal(80, ShowLogFormat.Truncate(new string('x', 5000), 80).Length);
    }

    [Fact]
    public void EscapeRemovesControlCharactersAndRoundTrips()
    {
        var raw = "a\tb\nc\rd\\e\u0001\u007f\u2028é";
        var esc = ShowLogFormat.Escape(raw);
        Assert.Equal("a\\tb\\nc\\rd\\\\e\\x01\\x7F\\u2028é", esc);
        Assert.DoesNotContain(esc, ch => ch < 0x20);
        Assert.Equal(raw, ShowLogFormat.Unescape(esc));
        Assert.Equal("\\q", ShowLogFormat.Unescape("\\q")); // ukjent tegnfølge beholdes
        Assert.Equal("x\\", ShowLogFormat.Unescape("x\\"));
    }

    [Fact]
    public void RunTextFormatsAndParses()
    {
        Assert.Equal("00:00:00.000", ShowLogFormat.RunText(0));
        Assert.Equal("00:00:00.000", ShowLogFormat.RunText(-5));
        Assert.Equal("00:01:23.456", ShowLogFormat.RunText(83456));
        Assert.Equal("100:00:00.000", ShowLogFormat.RunText(360_000_000));
        Assert.True(ShowLogFormat.TryParseRun("100:00:00.000", out var ms));
        Assert.Equal(360_000_000, ms);
        Assert.False(ShowLogFormat.TryParseRun("00:61:00.000", out _));
        Assert.False(ShowLogFormat.TryParseRun("tull", out _));
        Assert.False(ShowLogFormat.TryParseRun("00:00:00.5", out _));
    }

    [Fact]
    public void FormatLineGivesExactTextLine()
    {
        Assert.Equal("2026-11-25T14:03:22.123+01:00\t00:01:23.456\tstart\t3\taudio\tgo\tIntro\t", ShowLogFormat.FormatLine(Ev()));
        var line = ShowLogFormat.FormatLine(Ev(ShowLogKind.Error, "linje1\nlinje2\ttab", "Navn\twith tab"));
        Assert.DoesNotContain('\n', line);
        Assert.Equal(8, line.Split('\t').Length);
        var long80 = ShowLogFormat.FormatLine(Ev(text: new string('z', 1000), name: new string('n', 1000)));
        Assert.True(long80.Length < 500);
    }

    [Fact]
    public void ParseLineRoundTripsAndRejectsGarbage()
    {
        var e = Ev(ShowLogKind.Warning, "fil \\\\server\\x.wav mangler\nlinje 2", "Æøå 🔒");
        Assert.True(ShowLogFormat.TryParseLine(ShowLogFormat.FormatLine(e), out var back));
        Assert.Equal(e.Kind, back.Kind);
        Assert.Equal(e.Text, back.Text);
        Assert.Equal(e.Name, back.Name);
        Assert.Equal(e.RunMs, back.RunMs);
        Assert.Equal(e.Time, back.Time);
        Assert.Equal(TimeSpan.FromHours(1), back.Time.Offset);
        Assert.False(ShowLogFormat.TryParseLine("# kommentar", out _));
        Assert.False(ShowLogFormat.TryParseLine("", out _));
        Assert.False(ShowLogFormat.TryParseLine("a\tb\tc", out _));
        Assert.False(ShowLogFormat.TryParseLine("ikke-tid\t00:00:00.000\tstart\t1\taudio\tgo\tx\t", out _));
        Assert.False(ShowLogFormat.TryParseLine("2026-11-25T14:03:22.123+01:00\t00:00:00.000\tukjent\t1\taudio\tgo\tx\t", out _));
    }

    [Fact]
    public void ParseTextSkipsBadLinesAndCountsThem()
    {
        var text = ShowLogExport.ToText(new[] { Ev(), Ev(ShowLogKind.Finish) }) + "skrap\n\n";
        var list = ShowLogExport.Parse(text.Split('\n'), out var skipped);
        Assert.Equal(2, list.Count);
        Assert.Equal(1, skipped);
    }

    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("", "")]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+1", "'+1")]
    [InlineData("-1", "'-1")]
    [InlineData("@x", "'@x")]
    [InlineData("\tx", "' x")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("l1\nl2", "\"l1\nl2\"")]
    [InlineData(" lead", "\" lead\"")]
    [InlineData("a\u0001b", "a b")]
    public void CsvCellEscapesAndNeutralisesFormulas(string input, string expected) =>
        Assert.Equal(expected, ShowLogExport.CsvCell(input));

    [Fact]
    public void CsvHasHeaderAndOneRowPerEvent()
    {
        var csv = ShowLogExport.ToCsv(new[] { Ev(ShowLogKind.Error, "=cmd|' /C calc'!A0"), Ev(ShowLogKind.Finish, "a,b") });
        var lines = csv.Split("\r\n");
        Assert.Equal("time,run,event,cue,type,how,name,text", lines[0]);
        Assert.Equal("2026-11-25T14:03:22.123+01:00,00:01:23.456,error,3,audio,go,Intro,'=cmd|' /C calc'!A0", lines[1]);
        Assert.Equal("2026-11-25T14:03:22.123+01:00,00:01:23.456,finish,3,audio,go,Intro,\"a,b\"", lines[2]);
        Assert.Equal("", lines[3]);
    }

    [Fact]
    public void FileNamesAndRotation()
    {
        Assert.Equal("showlog 2026-11-25 14.03.22.log", ShowLogFiles.FileName(T0));
        Assert.Equal("showlog 2026-11-25 14.03.22 (2).log", ShowLogFiles.FileName(T0, 2));
        Assert.True(ShowLogFiles.IsLogName("showlog 2026-11-25 14.03.22.log"));
        Assert.True(ShowLogFiles.IsLogName("showlog 2026-11-25 14.03.22 (3).log"));
        Assert.False(ShowLogFiles.IsLogName("notes.log"));
        Assert.False(ShowLogFiles.IsLogName("showlog 2026-11-25 14.03.22.log.bak"));
        Assert.False(ShowLogFiles.IsLogName(null));

        var names = new[]
        {
            "showlog 2026-11-20 10.00.00.log", "showlog 2026-11-22 10.00.00.log", "showlog 2026-11-21 10.00.00.log",
            "showlog 2026-11-24 10.00.00.log", "showlog 2026-11-23 10.00.00.log", "readme.txt", "Show.showcue",
        };
        var del = ShowLogFiles.ToDelete(names, 3);
        Assert.Equal(new[] { "showlog 2026-11-21 10.00.00.log", "showlog 2026-11-20 10.00.00.log" }, del);
        Assert.Empty(ShowLogFiles.ToDelete(names, 30));
        Assert.Equal(5, ShowLogFiles.ToDelete(names, 0).Count);
    }

    [Fact]
    public void BoundedQueueDropsOldestAndCounts()
    {
        var q = new BoundedQueue<int>(3);
        Assert.True(q.Enqueue(1));
        Assert.True(q.Enqueue(2));
        Assert.True(q.Enqueue(3));
        Assert.False(q.Enqueue(4));
        Assert.False(q.Enqueue(5));
        Assert.Equal(3, q.Count);
        Assert.Equal(2, q.PendingDropped);
        var got = new List<int>();
        Assert.Equal(3, q.DrainTo(got, 10));
        Assert.Equal(new[] { 3, 4, 5 }, got);
        Assert.Equal(2, q.TakeDropped());
        Assert.Equal(0, q.TakeDropped());
        Assert.Equal(2, q.DroppedTotal);
        Assert.Equal(1, new BoundedQueue<int>(0).Capacity);
        var part = new List<int>();
        q.Enqueue(7); q.Enqueue(8);
        Assert.Equal(1, q.DrainTo(part, 1));
        Assert.Equal(1, q.Count);
    }

    [Fact]
    public void RecordKeepsTimesAndSourceAndLimitsMemory()
    {
        var now = T0;
        using var log = new ShowLog(null, clock: () => now, memoryLimit: 3);
        now = T0.AddMilliseconds(1500);
        log.Record(ShowLogKind.Start, "1", "A", "audio", StartSource.Go);
        using (ShowLog.Via(StartSource.Osc))
        {
            log.Record(ShowLogKind.Pause);
            using (ShowLog.Via(StartSource.Midi, keepExisting: true)) log.Record(ShowLogKind.Resume);
        }
        log.Record(ShowLogKind.StopAll);
        log.Record(ShowLogKind.Error, text: new string('x', 1000));
        Assert.Equal(5, log.Total);
        var snap = log.Snapshot();
        Assert.Equal(3, snap.Count);
        Assert.Equal(new[] { "resume", "stopall", "error" }, snap.Select(e => ShowLogFormat.KindToken(e.Kind)));
        Assert.Equal("osc", snap[0].How);
        Assert.Equal("", snap[1].How);
        Assert.Equal(1500, snap[0].RunMs);
        Assert.Equal(ShowLogFormat.MaxText, snap[2].Text.Length);
        var more = log.ReadSince(4, out var last);
        Assert.Single(more);
        Assert.Equal(5, last);
    }

    [Fact]
    public void WritesFileWithHeaderAndLines()
    {
        var root = TempDir();
        try
        {
            using (var log = new ShowLog(root, clock: () => T0))
            {
                log.Record(ShowLogKind.AppStart, text: "ShowCue test");
                log.Record(ShowLogKind.Start, "1", "Intro\nny linje", "audio", StartSource.Go);
                Assert.True(log.Flush());
                Assert.NotNull(log.FilePath);
                Assert.Equal("showlog 2026-11-25 14.03.22.log", Path.GetFileName(log.FilePath));
            }
            var lines = File.ReadAllLines(Path.Combine(ShowLogFiles.DirectoryFor(root), "showlog 2026-11-25 14.03.22.log"));
            Assert.StartsWith(ShowLogFormat.FileHeader, lines[0]);
            Assert.Equal(3, lines.Length);
            Assert.Contains("\tappstart\t", lines[1]);
            Assert.Contains("\tstart\t1\taudio\tgo\tIntro\\nny linje\t", lines[2]);
            var back = ShowLogExport.ReadFile(Path.Combine(ShowLogFiles.DirectoryFor(root), "showlog 2026-11-25 14.03.22.log"), out var skipped);
            Assert.Equal(2, back.Count);
            Assert.Equal(0, skipped);
            Assert.Equal("Intro\nny linje", back[1].Name);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void KeepsOnlyTheNewestFilesAndNeverTouchesOthers()
    {
        var root = TempDir();
        try
        {
            var dir = ShowLogFiles.DirectoryFor(root);
            Directory.CreateDirectory(dir);
            for (int d = 1; d <= 5; d++) File.WriteAllText(Path.Combine(dir, $"showlog 2026-11-0{d} 10.00.00.log"), "x");
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "x");
            using (var log = new ShowLog(root, clock: () => T0, filesToKeep: 3))
            {
                log.Record(ShowLogKind.AppStart);
                Assert.True(log.Flush());
            }
            var left = Directory.GetFiles(dir).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList();
            Assert.Equal(new[] { "notes.txt", "showlog 2026-11-04 10.00.00.log", "showlog 2026-11-05 10.00.00.log", "showlog 2026-11-25 14.03.22.log" }, left);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ContinuesInANewFileAtTheSizeLimit()
    {
        var root = TempDir();
        try
        {
            using (var log = new ShowLog(root, clock: () => T0, maxFileBytes: 1024))
            {
                for (int i = 0; i < 100; i++) log.Record(ShowLogKind.Start, i.ToString(), "Cue " + i, "audio", StartSource.Go);
                Assert.True(log.Flush());
                Assert.Equal(100, log.Total);
            }
            var files = ShowLogFiles.List(root);
            Assert.True(files.Count > 1);
            foreach (var f in files) Assert.True(new FileInfo(f).Length <= 1024);
            var oldest = File.ReadAllText(files[^1]);
            Assert.Contains("the log continues in the next file", oldest);
            Assert.Contains("| continued", File.ReadAllText(files[0]));
            // Den siste hendelsen står i den nyeste fila.
            Assert.Contains("Cue 99", File.ReadAllText(files[0]));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SameSecondFilesSortByNumberNotText()
    {
        var names = new[]
        {
            "showlog 2026-11-25 14.03.22.log", "showlog 2026-11-25 14.03.22 (2).log", "showlog 2026-11-25 14.03.22 (10).log",
            "showlog 2026-11-25 14.03.21 (12).log",
        };
        // Beholder bare den nyeste: alt annet skal slettes.
        var del = ShowLogFiles.ToDelete(names, 1);
        Assert.Equal(3, del.Count);
        Assert.DoesNotContain("showlog 2026-11-25 14.03.22 (10).log", del);
        Assert.Equal("showlog 2026-11-25 14.03.22 (2).log", del[0]);
    }

    [Fact]
    public void FileErrorIsSwallowedAndReportedOnce()
    {
        var root = TempDir();
        try
        {
            var blocker = Path.Combine(root, "data");
            File.WriteAllText(blocker, "this is a file, not a folder");
            int reports = 0;
            using var log = new ShowLog(blocker, clock: () => T0);
            log.FaultReported += _ => Interlocked.Increment(ref reports);
            for (int i = 0; i < 20; i++) log.Record(ShowLogKind.Start, i.ToString());
            log.Flush();
            log.Record(ShowLogKind.Stop, "1");
            log.Flush();
            Assert.NotNull(log.Fault);
            Assert.Equal(1, reports);
            Assert.Equal(21, log.Total); // minnet virker selv om filen ikke gjør det
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void NoFileIsCreatedWhenWritingIsOff()
    {
        var root = TempDir();
        try
        {
            using (var log = new ShowLog(root, writeFile: false, clock: () => T0))
            {
                log.Record(ShowLogKind.AppStart);
                Assert.True(log.Flush());
                Assert.Equal(1, log.Total);
            }
            Assert.False(Directory.Exists(ShowLogFiles.DirectoryFor(root)));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ManyThreadsNeverThrowAndNothingIsLostInMemory()
    {
        var root = TempDir();
        try
        {
            var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();
            using (var log = new ShowLog(root, queueCapacity: 16, memoryLimit: 100))
            {
                var threads = Enumerable.Range(0, 6).Select(t => new Thread(() =>
                {
                    try { for (int i = 0; i < 2000; i++) log.Record(ShowLogKind.Start, i.ToString(), "T" + t, "audio", StartSource.Go, "x\ny"); }
                    catch (Exception e) { errors.Add(e); }
                })).ToList();
                threads.ForEach(t => t.Start());
                threads.ForEach(t => t.Join());
                Assert.True(log.Flush(5000));
                Assert.Empty(errors);
                Assert.True(log.Total >= 12000); // alle ble talt i minnet (pluss evt. «dropped»-linjer)
                Assert.True(log.Snapshot().Count <= 100 + 1);
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void StaticFacadeDoesNothingWithoutLogAndNeverThrows()
    {
        var before = ShowLog.Current;
        try
        {
            ShowLog.Install(null);
            ShowLog.Add(ShowLogKind.Error, text: "x");
            ShowLog.AddCue(ShowLogKind.Start, null);
            var mine = new ShowLog(null);
            ShowLog.Install(mine);
            ShowLog.AddCue(ShowLogKind.Start, new AudioCue { Number = "7", Name = "Fanfare" }, StartSource.Hotkey);
            var e = Assert.Single(mine.Snapshot());
            Assert.Equal("7", e.Number);
            Assert.Equal("Fanfare", e.Name);
            Assert.Equal("audio", e.CueType);
            Assert.Equal("hotkey", e.How);
            mine.Dispose();
            mine.Record(ShowLogKind.Error); // etter Dispose: ingen feil
        }
        finally { ShowLog.Install(before); }
    }

    [Fact]
    public void CategoriesFilterTheEvents()
    {
        Assert.True(ShowLogFormat.Matches(Ev(ShowLogKind.Error), ShowLogCategory.Problems));
        Assert.True(ShowLogFormat.Matches(Ev(ShowLogKind.Warning), ShowLogCategory.Problems));
        Assert.False(ShowLogFormat.Matches(Ev(ShowLogKind.Start), ShowLogCategory.Problems));
        Assert.True(ShowLogFormat.Matches(Ev(ShowLogKind.Start), ShowLogCategory.Cues));
        Assert.True(ShowLogFormat.Matches(Ev(ShowLogKind.StopAll), ShowLogCategory.Control));
        Assert.True(ShowLogFormat.Matches(Ev(ShowLogKind.ShowSaved), ShowLogCategory.Show));
        Assert.True(ShowLogFormat.Matches(Ev(ShowLogKind.AppExit), ShowLogCategory.All));
    }

    [Fact]
    public void SettingIsOnByDefaultAndSurvivesRoundTrip()
    {
        Assert.True(new AppSettings().WriteShowLog);
        Assert.True(AppSettings.Normalize(JsonSerializer.Deserialize<AppSettings>("{}", ShowDocument.JsonOptions)!).WriteShowLog);
        var off = new AppSettings { WriteShowLog = false };
        Assert.False(AppSettings.Normalize(off.Copy()).WriteShowLog);
    }
}
