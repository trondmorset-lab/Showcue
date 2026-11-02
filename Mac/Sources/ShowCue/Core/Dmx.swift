import Foundation

/// DMX-nivåer skrevet som tekst: «1@255 2-4@50% 10@0» (kanal@verdi, verdi 0–255 eller prosent).
enum DmxLevels {
    enum ParseError: LocalizedError {
        case invalid(String)
        var errorDescription: String? { if case .invalid(let s) = self { return s }; return nil }
    }

    static func parse(_ text: String) throws -> [Int: UInt8] {
        var levels: [Int: UInt8] = [:]
        for raw in text.split(whereSeparator: { " ,;\n\r\t".contains($0) }).map(String.init) {
            let at = raw.split(omittingEmptySubsequences: false, whereSeparator: { $0 == "@" || $0 == "=" }).map(String.init)
            guard at.count == 2 else { throw ParseError.invalid("«\(raw)»: skriv kanal@verdi, f.eks. 1@255 eller 1-4@50%") }
            let range = at[0].split(separator: "-", omittingEmptySubsequences: false).map(String.init)
            guard range.count <= 2, let from = Int(range[0]), let to = Int(range[range.count - 1]),
                  from >= 1, to <= 512, to >= from else { throw ParseError.invalid("«\(raw)»: kanal må være 1–512") }
            var v = at[1].trimmingCharacters(in: .whitespaces)
            let value: Double
            if v.hasSuffix("%") {
                v.removeLast()
                guard let pct = Double(v.replacingOccurrences(of: ",", with: ".")), pct >= 0, pct <= 100 else {
                    throw ParseError.invalid("«\(raw)»: prosent må være 0–100")
                }
                value = pct * 255 / 100
            } else {
                guard let d = Double(v.replacingOccurrences(of: ",", with: ".")), d >= 0, d <= 255 else {
                    throw ParseError.invalid("«\(raw)»: verdi må være 0–255")
                }
                value = d
            }
            for c in from...to { levels[c] = UInt8(value.rounded(.toNearestOrAwayFromZero)) }
        }
        return levels
    }

    static func summary(_ text: String) -> String {
        guard let l = try? parse(text), !l.isEmpty else { return "" }
        return l.count == 1 ? "kanal \(l.keys.first!)" : "\(l.count) kanaler"
    }
}

/// Art-Net (ArtDmx, UDP port 6454): ett univers med 512 DMX-kanaler.
enum ArtNet {
    static let port = 6454

    static func dmxPacket(universe: Int, sequence: UInt8, data: [UInt8]) -> [UInt8] {
        var p = Array("Art-Net".utf8) + [0]
        p += [0x00, 0x50, 0, 14, sequence, 0, UInt8(universe & 0xFF), UInt8((universe >> 8) & 0x7F), 0x02, 0x00]
        var d = Array(data.prefix(512))
        d += [UInt8](repeating: 0, count: 512 - d.count)
        return p + d
    }
}

/// Nivåene i ett DMX-univers, med fades. Tiden gis utenfra (sekunder), så den kan testes.
final class DmxFader {
    private var from = [Double](repeating: 0, count: 512)
    private var to = [Double](repeating: 0, count: 512)
    private var start = [Double](repeating: 0, count: 512)
    private var length = [Double](repeating: 0, count: 512)
    private let lock = NSLock()

    /// Fade kanalene til nye verdier. En kanal som allerede fader, fortsetter fra der den er nå.
    func fade(to levels: [Int: UInt8], seconds: Double, now: Double) {
        lock.lock(); defer { lock.unlock() }
        for (ch, v) in levels where ch >= 1 && ch <= 512 {
            let i = ch - 1
            from[i] = value(i, now)
            to[i] = Double(v)
            start[i] = now
            length[i] = max(0, seconds)
        }
    }

    private func value(_ i: Int, _ now: Double) -> Double {
        if length[i] <= 0 || now >= start[i] + length[i] { return to[i] }
        let p = min(1, max(0, (now - start[i]) / length[i]))
        return from[i] + (to[i] - from[i]) * p
    }

    func values(_ now: Double) -> [UInt8] {
        lock.lock(); defer { lock.unlock() }
        return (0..<512).map { UInt8(min(255, max(0, value($0, now))).rounded(.toNearestOrAwayFromZero)) }
    }
}
