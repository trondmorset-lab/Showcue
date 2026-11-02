using ShowCue.Engine;
using ShowCue.Models;
using static ShowCue.Tests.TestAudio;

namespace ShowCue.Tests;

public class UndoHistoryTests
{
    [Fact]
    public void UndoAndRedo()
    {
        var h = new UndoHistory();
        h.Reset("a");
        Assert.False(h.CanUndo);
        Assert.True(h.Record("b"));
        Assert.True(h.Record("c"));
        Assert.False(h.Record("c")); // uendret = ingen nytt steg
        Assert.Equal("b", h.Undo());
        Assert.Equal("a", h.Undo());
        Assert.Null(h.Undo());
        Assert.Equal("b", h.Redo());
        Assert.True(h.Record("x")); // ny endring etter angre fjerner «gjør om»
        Assert.False(h.CanRedo);
        Assert.Equal("b", h.Undo());
    }

    [Fact]
    public void LimitDropsOldest()
    {
        var h = new UndoHistory(limit: 3);
        h.Reset("0");
        for (int i = 1; i <= 5; i++) h.Record(i.ToString());
        Assert.Equal("4", h.Undo());
        Assert.Equal("3", h.Undo());
        Assert.Equal("2", h.Undo());
        Assert.False(h.CanUndo);
    }
}

public class ShowSafetyTests
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "showcue-safety", Guid.NewGuid().ToString("N"))).FullName;

    [Fact]
    public void AutosaveAndRecovery()
    {
        var show = Path.Combine(_root, "Konsert.showcue");
        File.WriteAllText(show, "{}");
        File.SetLastWriteTimeUtc(show, DateTime.UtcNow.AddMinutes(-5));
        Assert.Null(ShowSafety.PendingAutosave(show, _root));

        ShowSafety.WriteAutosave(show, "{\"Title\":\"x\"}", _root);
        var pending = ShowSafety.PendingAutosave(show, _root);
        Assert.NotNull(pending);
        Assert.StartsWith(Path.Combine(_root, "Autolagring", "Konsert "), pending);
        Assert.Equal("{\"Title\":\"x\"}", File.ReadAllText(pending!));

        File.SetLastWriteTimeUtc(show, DateTime.UtcNow.AddMinutes(5)); // lagret etterpå → ingen gjenoppretting
        Assert.Null(ShowSafety.PendingAutosave(show, _root));

        ShowSafety.DeleteAutosave(show, _root);
        Assert.False(File.Exists(ShowSafety.AutosavePath(show, _root)));
    }

    [Fact]
    public void UntitledShowAndDistinctPaths()
    {
        ShowSafety.WriteAutosave(null, "{}", _root);
        Assert.NotNull(ShowSafety.PendingAutosave(null, _root));
        Assert.NotEqual(ShowSafety.AutosavePath(Path.Combine(_root, "a", "Show.showcue"), _root),
                        ShowSafety.AutosavePath(Path.Combine(_root, "b", "Show.showcue"), _root));
    }

    [Fact]
    public void BackupsKeepTheNewest()
    {
        var show = Path.Combine(_root, "Revy.showcue");
        File.WriteAllText(show, "v");
        var t = new DateTime(2026, 11, 1, 12, 0, 0);
        for (int i = 0; i < ShowSafety.BackupsToKeep + 5; i++) ShowSafety.Backup(show, _root, t.AddMinutes(i));
        var files = Directory.GetFiles(ShowSafety.BackupDir(_root), "Revy *.showcue");
        Assert.Equal(ShowSafety.BackupsToKeep, files.Length);
        Assert.Contains(files, f => f.EndsWith("Revy 2026-11-01 12.24.00.showcue"));
        Assert.DoesNotContain(files, f => f.EndsWith("Revy 2026-11-01 12.00.00.showcue"));
        Assert.Null(ShowSafety.Backup(Path.Combine(_root, "finnes-ikke.showcue"), _root));
    }
}

public class PreviewOutputTests
{
    [Fact]
    public void DownmixToOnePair()
    {
        Assert.Equal(new[] { (1, 5, 0.0), (1, 6, 0.0) }, AudioRoute.Downmix(1, 5).Select(r => (r.Source, r.Bus, r.GainDb)));
        Assert.Equal(new[] { (1, 3, 0.0), (2, 4, 0.0) }, AudioRoute.Downmix(2, 3).Select(r => (r.Source, r.Bus, r.GainDb)));
        Assert.Equal(new[] { (1, 1, -3.0), (2, 2, -3.0), (3, 1, -3.0), (4, 2, -3.0) }, AudioRoute.Downmix(4, 1).Select(r => (r.Source, r.Bus, r.GainDb)));
    }

    [Fact]
    public void PreviewGoesToListenBusNotTheCueOutputs()
    {
        var path = ConstWav("preview-stereo.wav", 48000, 2, 1.0);
        var pm = new PatchMixer(48000, 8, PatchBus.DefaultPatch(8));
        var cue = new AudioCue { FilePath = path, Routes = new() { new(1, 1), new(2, 2) } };

        var toShow = new AudioPreview(() => pm);
        Assert.True(toShow.GoesToShowOutputs);

        var toHeadphones = new AudioPreview(() => new AudioPreview.Target(pm, 7));
        Assert.False(toHeadphones.GoesToShowOutputs);
        Assert.True(toHeadphones.Play(cue, 0));
        var o = Run(pm, 1000);
        Near(0, At(o, 8, 900, 1), 1e-6);   // ikke til salen (utgang 1/2)
        Near(0, At(o, 8, 900, 2), 1e-6);
        Near(0.1, At(o, 8, 900, 7), 0.002); // venstre → utgang 7
        Near(0.2, At(o, 8, 900, 8), 0.002); // høyre → utgang 8
        toHeadphones.Stop();

        var none = new AudioPreview(() => (AudioPreview.Target?)null);
        Assert.False(none.Play(cue, 0)); // hodetelefonene mangler → ingen lyd (aldri til salen)
    }
}
