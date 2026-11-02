import Foundation

/// Språk i brukerflaten. Teksten i koden er norsk og er samtidig nøkkelen i oversettelsesfila (i18n/en.json).
enum Loc {
    /// Språkene programmet kan vises på (kode, navn).
    static let languages: [(code: String, name: String)] = [("", "Samme som systemet / Same as system"), ("nb", "Norsk"), ("en", "English")]

    /// Gjeldende språk: «nb» eller «en».
    private(set) static var language = "nb"
    private static var table: [String: String] = [:]

    /// Velger språk. Tom kode betyr systemets språk (norsk på norske maskiner, ellers engelsk).
    static func use(_ code: String, english: URL? = Bundle.main.url(forResource: "en", withExtension: "json")) {
        language = resolve(code, system: Locale.preferredLanguages.first ?? "en")
        table = [:]
        guard language == "en", let url = english, let data = try? Data(contentsOf: url),
              let t = try? JSONDecoder().decode([String: String].self, from: data) else { return }
        table = t
    }

    static func resolve(_ code: String, system: String) -> String {
        if code == "nb" || code == "en" { return code }
        let s = system.lowercased()
        return s.hasPrefix("nb") || s.hasPrefix("nn") || s.hasPrefix("no") ? "nb" : "en"
    }

    /// Teksten på gjeldende språk, med {0}, {1} … fylt inn.
    static func text(_ key: String, _ args: [Any]) -> String {
        var s = table[key].flatMap { $0.isEmpty ? nil : $0 } ?? key
        for (i, a) in args.enumerated() { s = s.replacingOccurrences(of: "{\(i)}", with: "\(a)") }
        return s
    }
}

/// Oversatt tekst: `L("Lagre")`, `L("Fant {0} filer", n)`.
func L(_ key: String, _ args: Any...) -> String { Loc.text(key, args) }
