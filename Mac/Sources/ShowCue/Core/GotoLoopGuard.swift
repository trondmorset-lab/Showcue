import Foundation

/// Stopper en løpsk kjede av «gå til»-cuer som starter hverandre uten at det går tid (for eksempel to cuer som peker på hverandre).
/// Hver kjøring av en «gå til»-cue som starter målet meldes til `allow`; kommer mer enn `maxBurst` tett på hverandre
/// (hver innen `windowMs` etter forrige), avvises resten. Vanlige sløyfer med lyd eller pause imellom påvirkes ikke.
/// Ren logikk: tiden gis inn av kalleren (stigende millisekunder).
struct GotoLoopGuard {
    static let maxBurst = 25
    static let windowMs: Int64 = 200

    private var last: Int64 = 0
    private var any = false
    private var count = 0

    /// True hvis «gå til» kan utføres nå.
    mutating func allow(nowMs: Int64) -> Bool {
        if !any || nowMs < last || UInt64(bitPattern: nowMs &- last) > UInt64(Self.windowMs) { count = 0 } // uten overflyt, selv med ekstreme tider
        any = true
        last = nowMs
        if count < Int.max { count += 1 }
        return count <= Self.maxBurst
    }

    mutating func reset() { any = false; count = 0 }
}
