namespace ShowCue.Models;

/// <summary>Øvelsesmodus: merkede cuer («Hopp over i øvelse») hoppes over av GO, auto-fortsett og auto-følg mens modusen er på.
/// Ren logikk uten vinduer og klokke. Å starte en merket cue direkte (nummer, hurtigtast, OSC, knappepanel, gruppe) virker som før.</summary>
public static class Rehearsal
{
    /// <summary>Cuen hoppes over akkurat nå: modusen er på og cuen er merket.</summary>
    public static bool IsSkipped(Cue cue, bool mode) => mode && cue.SkipInRehearsal;

    /// <summary>GO og auto-fortsett/auto-følg kan starte cuen: den er ikke deaktivert og ikke hoppet over.</summary>
    public static bool IsPlayable(Cue cue, bool mode) => !cue.Disabled && !IsSkipped(cue, mode);

    /// <summary>Første spillbare cue fra og med <paramref name="from"/>, eller lengden på lista hvis det ikke finnes noen (slutten av lista).</summary>
    public static int NextPlayableIndex(IList<Cue> list, int from, bool mode)
    {
        for (int i = Math.Max(0, from); i < list.Count; i++)
            if (IsPlayable(list[i], mode)) return i;
        return list.Count;
    }

    /// <summary>Cuen GO starter når GO-markøren står på <paramref name="playhead"/>: markøren selv hvis den er spillbar, ellers neste spillbare.
    /// Med <paramref name="explicitStart"/> (cuen er valgt med nummer) startes en merket cue likevel; en deaktivert hoppes alltid over.
    /// −1 hvis ingenting kan startes.</summary>
    public static int GoIndex(IList<Cue> list, int playhead, bool mode, bool explicitStart = false)
    {
        if (playhead < 0 || playhead >= list.Count) return -1;
        if (explicitStart && !list[playhead].Disabled) return playhead;
        var i = NextPlayableIndex(list, playhead, mode);
        return i < list.Count ? i : -1;
    }

    /// <summary>Antall aktive (ikke deaktiverte) cuer som er merket «Hopp over i øvelse».</summary>
    public static int CountFlagged(IEnumerable<Cue> cues) => cues.Count(c => !c.Disabled && c.SkipInRehearsal);
}
