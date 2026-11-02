import Foundation

/// Bilderate for tidskode. 29,97 er alltid «drop frame». Tekstverdiene er de samme som i Windows-versjonen.
enum TimecodeRate: String, Codable, CaseIterable {
    case fps24 = "Fps24", fps25 = "Fps25", fps2997Drop = "Fps2997Drop", fps30 = "Fps30"

    /// Bilder per sekund i tellingen (30 for 29,97 drop frame).
    var nominal: Int { self == .fps24 ? 24 : self == .fps25 ? 25 : 30 }
    /// Faktiske bilder per sekund.
    var fps: Double { self == .fps2997Drop ? 30000.0 / 1001 : Double(nominal) }
    var isDrop: Bool { self == .fps2997Drop }

    var label: String {
        switch self {
        case .fps24: return "24 fps (film)"
        case .fps25: return "25 fps (PAL/Europa)"
        case .fps2997Drop: return "29,97 fps drop frame (NTSC)"
        case .fps30: return "30 fps"
        }
    }

    var mtcCode: Int { self == .fps24 ? 0 : self == .fps25 ? 1 : self == .fps2997Drop ? 2 : 3 }
    static func fromMtc(_ c: Int) -> TimecodeRate { [.fps24, .fps25, .fps2997Drop, .fps30][max(0, min(3, c))] }
}

/// SMPTE-tidskode tt:mm:ss:ff, regnes om til og fra et fortløpende bildenummer.
struct Timecode: Equatable, Hashable, CustomStringConvertible {
    var hours = 0, minutes = 0, seconds = 0, frames = 0

    init(_ h: Int, _ m: Int, _ s: Int, _ f: Int) { hours = h; minutes = m; seconds = s; frames = f }

    func toFrame(_ r: TimecodeRate) -> Int {
        let fps = r.nominal
        let n = ((hours * 60 + minutes) * 60 + seconds) * fps + frames
        guard r.isDrop else { return n }
        let mins = hours * 60 + minutes
        return n - 2 * (mins - mins / 10)
    }

    static func fromFrame(_ frame: Int, _ r: TimecodeRate) -> Timecode {
        let fps = r.nominal
        var n = frame
        if r.isDrop {
            let perDay = 24 * 6 * 17982
            n = ((n % perDay) + perDay) % perDay
            let d = n / 17982, m = n % 17982
            n += 18 * d + (m > 1 ? 2 * ((m - 2) / 1798) : 0)
        } else {
            let perDay = 24 * 3600 * fps
            n = ((n % perDay) + perDay) % perDay
        }
        return Timecode(n / (3600 * fps) % 24, n / (60 * fps) % 60, n / fps % 60, n % fps)
    }

    func toSeconds(_ r: TimecodeRate) -> Double { Double(toFrame(r)) / r.fps }
    static func fromSeconds(_ s: Double, _ r: TimecodeRate) -> Timecode { fromFrame(Int((s * r.fps + 1e-6).rounded(.down)), r) }

    func format(_ r: TimecodeRate) -> String {
        String(format: "%02d:%02d:%02d%@%02d", hours, minutes, seconds, r.isDrop ? ";" : ":", frames)
    }
    var description: String { String(format: "%02d:%02d:%02d:%02d", hours, minutes, seconds, frames) }

    /// Leser «tt:mm:ss:ff» (også med ; eller . som skille). nil ved feil format.
    static func parse(_ text: String) -> Timecode? {
        let parts = text.trimmingCharacters(in: .whitespaces).split(whereSeparator: { ":;.,".contains($0) }).map(String.init)
        guard parts.count == 4, parts.allSatisfy({ !$0.isEmpty && $0.allSatisfy(\.isASCII) && $0.allSatisfy(\.isNumber) }) else { return nil }
        let v = parts.compactMap { Int($0) }
        guard v.count == 4, v[0] <= 23, v[1] <= 59, v[2] <= 59, v[3] <= 29 else { return nil }
        return Timecode(v[0], v[1], v[2], v[3])
    }

    /// Normalisert tekst, eller tom tekst hvis den ikke er gyldig.
    static func normalize(_ text: String) -> String { parse(text)?.description ?? "" }

    /// Har tidskoden passert `target` fra forrige til nå? Hopp på mer enn to sekunder (spoling) og bakover teller ikke.
    static func crossed(previous: Int, now: Int, target: Int, rate: TimecodeRate) -> Bool {
        now >= previous && Double(now - previous) <= 2 * rate.fps && previous < target && target <= now
    }
}

/// MIDI Time Code: kvartbilder (F1 xx) og fullbilde-meldinger (F0 7F 7F 01 01 … F7).
enum Mtc {
    static func quarterFrame(_ tc: Timecode, _ r: TimecodeRate, piece: Int) -> UInt8 {
        let v: Int
        switch piece {
        case 0: v = tc.frames & 0xF
        case 1: v = tc.frames >> 4
        case 2: v = tc.seconds & 0xF
        case 3: v = tc.seconds >> 4
        case 4: v = tc.minutes & 0xF
        case 5: v = tc.minutes >> 4
        case 6: v = tc.hours & 0xF
        default: v = (tc.hours >> 4) | (r.mtcCode << 1)
        }
        return UInt8((piece << 4) | (v & 0xF))
    }

    /// Kvartbilde nr. `index` i en strøm som starter på `startFrame`. Hver rekke på 8 bærer bildet der rekken startet.
    static func quarterFrame(at index: Int, startFrame: Int, _ r: TimecodeRate) -> UInt8 {
        quarterFrame(Timecode.fromFrame(startFrame + index / 8 * 2, r), r, piece: index % 8)
    }

    static func fullFrame(_ tc: Timecode, _ r: TimecodeRate) -> [UInt8] {
        [0xF0, 0x7F, 0x7F, 0x01, 0x01, UInt8((r.mtcCode << 5) | tc.hours), UInt8(tc.minutes), UInt8(tc.seconds), UInt8(tc.frames), 0xF7]
    }
}

/// Leser innkommende MTC. Gir tidskoden når en hel rekke kvartbilder (eller et fullbilde) er mottatt.
final class MtcDecoder {
    private var nibbles = [Int](repeating: 0, count: 8)
    private var expected = 0
    private(set) var rate: TimecodeRate = .fps25

    func quarterFrame(_ data: UInt8) -> Timecode? {
        let piece = Int(data >> 4) & 7
        guard piece == expected else {
            expected = piece == 0 ? 1 : 0
            if piece == 0 { nibbles[0] = Int(data & 0xF) }
            return nil
        }
        nibbles[piece] = Int(data & 0xF)
        expected = (piece + 1) % 8
        guard piece == 7 else { return nil }
        rate = .fromMtc((nibbles[7] >> 1) & 3)
        let tc = Timecode(nibbles[6] | ((nibbles[7] & 1) << 4), nibbles[4] | (nibbles[5] << 4),
                          nibbles[2] | (nibbles[3] << 4), nibbles[0] | (nibbles[1] << 4))
        // Rekken tok to bilder å sende: nå er vi to bilder videre.
        return Timecode.fromFrame(tc.toFrame(rate) + 2, rate)
    }

    func fullFrame(_ b: [UInt8]) -> Timecode? {
        guard b.count >= 10, b[0] == 0xF0, b[1] == 0x7F, b[3] == 0x01, b[4] == 0x01 else { return nil }
        rate = .fromMtc(Int(b[5] >> 5) & 3)
        expected = 0
        return Timecode(Int(b[5] & 0x1F), Int(b[6]), Int(b[7]), Int(b[8]))
    }
}

/// Lineær tidskode (LTC): 80 bit per bilde, bifasekoding.
enum Ltc {
    static func bits(_ tc: Timecode, _ r: TimecodeRate) -> [Bool] {
        var b = [Bool](repeating: false, count: 80)
        func put(_ start: Int, _ count: Int, _ value: Int) { for i in 0..<count { b[start + i] = (value >> i) & 1 == 1 } }
        put(0, 4, tc.frames % 10); put(8, 2, tc.frames / 10)
        b[10] = r.isDrop
        put(16, 4, tc.seconds % 10); put(24, 3, tc.seconds / 10)
        put(32, 4, tc.minutes % 10); put(40, 3, tc.minutes / 10)
        put(48, 4, tc.hours % 10); put(56, 2, tc.hours / 10)
        for i in 66...77 { b[i] = true }   // synkord 0011 1111 1111 1101
        b[79] = true
        if b.filter({ !$0 }).count % 2 == 1 { b[r == .fps25 ? 59 : 27] = true } // polaritetsbit
        return b
    }

    static func decode(_ b: [Bool]) -> Timecode? {
        guard b.count >= 80, !b[64], !b[65], !b[78], b[79], (66...77).allSatisfy({ b[$0] }) else { return nil }
        func get(_ start: Int, _ count: Int) -> Int { (0..<count).reduce(0) { $0 | (b[start + $1] ? 1 << $1 : 0) } }
        return Timecode(get(48, 4) + 10 * get(56, 2), get(32, 4) + 10 * get(40, 3), get(16, 4) + 10 * get(24, 3), get(0, 4) + 10 * get(8, 2))
    }
}
