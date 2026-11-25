import Foundation

/// Øvelsesmodus: merkede cuer («Hopp over i øvelse») hoppes over av GO, auto-fortsett og auto-følg mens modusen er på.
/// Ren logikk uten vinduer og klokke. Å starte en merket cue direkte (nummer, hurtigtast, OSC, knappepanel, gruppe) virker som før.
enum Rehearsal {
    /// Cuen hoppes over akkurat nå: modusen er på og cuen er merket.
    static func isSkipped(_ cue: Cue, mode: Bool) -> Bool { mode && cue.skipInRehearsal }

    /// GO og auto-fortsett/auto-følg kan starte cuen: den er ikke deaktivert og ikke hoppet over.
    static func isPlayable(_ cue: Cue, mode: Bool) -> Bool { !cue.disabled && !isSkipped(cue, mode: mode) }

    /// Første spillbare cue fra og med `from`, eller lengden på lista hvis det ikke finnes noen (slutten av lista).
    static func nextPlayableIndex(_ list: [Cue], from: Int, mode: Bool) -> Int {
        var i = max(0, from)
        while i < list.count {
            if isPlayable(list[i], mode: mode) { return i }
            i += 1
        }
        return list.count
    }

    /// Cuen GO starter når GO-markøren står på `playhead`: markøren selv hvis den er spillbar, ellers neste spillbare.
    /// Med `explicitStart` (cuen er valgt med nummer) startes en merket cue likevel; en deaktivert hoppes alltid over.
    /// −1 hvis ingenting kan startes.
    static func goIndex(_ list: [Cue], playhead: Int, mode: Bool, explicitStart: Bool = false) -> Int {
        guard playhead >= 0, playhead < list.count else { return -1 }
        if explicitStart && !list[playhead].disabled { return playhead }
        let i = nextPlayableIndex(list, from: playhead, mode: mode)
        return i < list.count ? i : -1
    }

    /// Antall aktive (ikke deaktiverte) cuer som er merket «Hopp over i øvelse».
    static func countFlagged(_ cues: [Cue]) -> Int { cues.filter { !$0.disabled && $0.skipInRehearsal }.count }
}
