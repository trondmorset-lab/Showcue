import Foundation

/// Hvordan en rad i cuelista skal se ut. Rekkefølgen er prioriteten: spilles nå, deaktivert, neste, ellers vanlig.
enum CueRowState { case normal, playing, next, disabled }

/// Fanene i inspektøren.
enum InspectorTab: CaseIterable { case basic, media, timing, advanced }

/// Ren logikk for visning: radtilstand, «gjenstår»-tekst og hvilke inspektørfaner som finnes. Ingen SwiftUI her, så den kan testes.
enum CueDisplay {
    /// En cue som spiller vises alltid som spilt. En deaktivert cue hoppes over av GO, så den er aldri «neste».
    static func rowState(isRunning: Bool, isNext: Bool, disabled: Bool) -> CueRowState {
        if isRunning { return .playing }
        if disabled { return .disabled }
        if isNext { return .next }
        return .normal
    }

    /// Tid igjen som m:ss (rundet opp, så «0:00» aldri vises mens noe fortsatt spiller), eller t:mm:ss over en time.
    /// Ugyldige og negative verdier gir «0:00».
    static func clock(_ seconds: Double) -> String {
        let s = seconds.isNaN || seconds < 0 ? 0 : min(seconds, 359_999)
        let total = Int(s.rounded(.up))
        let h = total / 3600, m = total % 3600 / 60, sec = total % 60
        return h > 0 ? String(format: "%d:%02d:%02d", h, m, sec) : String(format: "%d:%02d", m, sec)
    }

    /// «Gjenstår» i cuelista: tom når cuen ikke spiller (eller venter på pre-wait), «∞» uten kjent varighet
    /// (loop, står til stoppet), ellers tid igjen.
    static func remainingText(started: Bool, duration: Double, elapsed: Double) -> String {
        if !started { return "" }
        if !(duration > 0) || duration.isInfinite { return "∞" }
        return clock(elapsed.isNaN ? duration : duration - elapsed)
    }
}

/// Hvilke faner inspektøren viser for en cuetype (nøkkelen er «type» i showfila), og hvilke av de felles feltene som ligger i hver fane.
/// Cuetypens egne felt ligger i Grunnleggende, og i Media for typene som har lyd, bilde eller video.
enum InspectorLayout {
    private static let mediaTypes: Set<String> = ["audio", "video", "image", "pdf", "web", "playlist", "slideshow", "input"]

    /// Faner med innhold for typen, i visningsrekkefølge. Typer uten lyd/bilde/video har ingen Media-fane.
    static func tabs(for typeKey: String?) -> [InspectorTab] {
        hasMedia(typeKey) ? [.basic, .media, .timing, .advanced] : [.basic, .timing, .advanced]
    }

    static func hasMedia(_ typeKey: String?) -> Bool {
        guard let k = typeKey else { return false }
        return mediaTypes.contains(k.lowercased())
    }

    /// Fanen som skal vises når en annen cue velges: den samme hvis den finnes for typen, ellers Grunnleggende.
    static func resolve(_ current: InspectorTab, for typeKey: String?) -> InspectorTab {
        tabs(for: typeKey).contains(current) ? current : .basic
    }

    /// Felt som alle cuer har (ved siden av nummer og navn, som alltid står over fanene), fordelt på fanene.
    /// Alle feltene fra den gamle, lange lista er med, hver på ett sted.
    static func commonFields(_ tab: InspectorTab) -> [String] {
        switch tab {
        case .basic: return ["disabled"]
        case .timing: return ["preWait", "continueMode", "timecodeTrigger"]
        case .advanced: return ["hotkey", "midiTrigger", "inButtonPanel", "color", "notes"]
        case .media: return []
        }
    }
}
