import Foundation

/// Klokkeslett-trigger: en cue starter når veggklokka (lokal tid) passerer et klokkeslett «tt:mm:ss».
/// Ren logikk uten klokke og maskinvare; kalleren gir inn forrige og nåværende tidspunkt.
enum ClockTriggers {
    /// Lengste intervall mellom to sjekker som regnes som vanlig gange. Lengre opphold (dvale, klokkestilling)
    /// regnes som hopp, og da starter ingen cuer.
    static let maxInterval: TimeInterval = 2

    /// Leser «tt:mm:ss» (ett eller to sifre per del). Tom tekst, feil format eller verdier utenfor 0–23/0–59/0–59 gir nil.
    static func parse(_ text: String?) -> (hours: Int, minutes: Int, seconds: Int)? {
        let parts = (text ?? "").trimmingCharacters(in: .whitespaces).split(separator: ":", omittingEmptySubsequences: false)
        guard parts.count == 3 else { return nil }
        var v: [Int] = []
        for p in parts {
            guard (1...2).contains(p.count), p.allSatisfy({ $0.isASCII && $0.isNumber }), let n = Int(p) else { return nil }
            v.append(n)
        }
        guard v[0] <= 23, v[1] <= 59, v[2] <= 59 else { return nil }
        return (v[0], v[1], v[2])
    }

    /// Gyldig tekst i fast format «tt:mm:ss», ellers tom tekst.
    static func normalize(_ text: String?) -> String {
        guard let t = parse(text) else { return "" }
        return String(format: "%02d:%02d:%02d", t.hours, t.minutes, t.seconds)
    }

    /// Ble klokkeslettet passert fra `previous` til `now`? Passert betyr forrige < klokkeslettet ≤ nå. Tåler midnatt.
    /// Intervall som ikke er framover, eller som er lengre enn `maxInterval`, gir alltid false, og det samme gjør et intervall
    /// der tidssonens forskyvning endres (omstilling til sommer- eller vintertid).
    static func crossed(previous: Date, now: Date, trigger: String?, calendar: Calendar = .current) -> Bool {
        guard let t = parse(trigger) else { return false }
        let elapsed = now.timeIntervalSince(previous)
        guard elapsed > 0, elapsed <= maxInterval else { return false }
        guard calendar.timeZone.secondsFromGMT(for: previous) == calendar.timeZone.secondsFromGMT(for: now) else { return false }
        // Intervallet er kort og kan høyst spenne over én midnatt: sjekk klokkeslettet på begge datoene.
        var days = [calendar.startOfDay(for: previous)]
        let nowDay = calendar.startOfDay(for: now)
        if nowDay != days[0] { days.append(nowDay) }
        for day in days {
            var comps = calendar.dateComponents([.year, .month, .day], from: day)
            comps.hour = t.hours; comps.minute = t.minutes; comps.second = t.seconds; comps.nanosecond = 0
            guard let target = calendar.date(from: comps) else { continue }
            if previous < target && target <= now { return true }
        }
        return false
    }

    /// Cuene som skal starte: ikke deaktiverte, med klokketrigger som ble passert. Hver cue er med høyst én gang.
    static func due(_ cues: [Cue], previous: Date, now: Date, calendar: Calendar = .current) -> [Cue] {
        cues.filter { !$0.disabled && !$0.clockTrigger.isEmpty && crossed(previous: previous, now: now, trigger: $0.clockTrigger, calendar: calendar) }
    }
}
