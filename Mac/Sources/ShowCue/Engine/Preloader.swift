import Foundation
import AVFoundation

/// Forbereder cuene GO snart vil starte, i bakgrunnen: åpner lydfila (eller lydsporet i en video), dekoder litt lyd inn i ringbufferen,
/// laster videofilas egenskaper og dekoder bilder. Alt skjer utenfor lydtråden og utenfor hovedtråden (på én egen kø, én fil om gangen).
/// En forberedt fil er aldri påkrevd: feiler forberedelsen, eller er fila endret, lager cuen avspillingen selv som før.
/// Høyst `PreloadPlanner.maxItems` filer holdes samtidig, og alt slippes når markøren flytter seg, ved stopp, nytt show og avslutning.
final class Preloader {
    /// Hvor mye lyd som dekodes på forhånd (sekunder).
    static let warmSeconds = 0.5

    private final class Entry {
        let item: PreloadItem
        var cancelled = false
        var ready = false
        var size: UInt64 = 0
        var modified: Date?
        var rate = 0.0
        var stream: StreamingSource?
        var asset: AVURLAsset?
        init(item: PreloadItem) { self.item = item }
    }

    private let lock = NSLock()
    private var book = PreloadBook()
    private var entries: [String: Entry] = [:]
    private var enabled = false
    private let queue = DispatchQueue(label: "showcue.preload", qos: .utility)

    /// Kalles fra hovedtråden når markøren, cuelista eller innstillingen er endret.
    /// `sampleRate` er lydutgangens frekvens (nil = lyden er ikke startet, da forberedes ikke lyd).
    func update(cues: [Cue], playheadIndex: Int, sampleRate: Double?, enabled on: Bool, rehearsal: Bool = false) {
        lock.lock(); enabled = on; lock.unlock()
        guard on else { clear(); return }
        let plan = PreloadPlanner.plan(cues: cues, playheadIndex: playheadIndex, rehearsal: rehearsal)
        var starts: [Entry] = []
        lock.lock()
        let diff = book.sync(plan.map(\.key))
        for key in diff.toEvict { drop(key) }
        for key in diff.toStart {
            guard let item = plan.first(where: { $0.key == key }) else { continue }
            let e = Entry(item: item)
            entries[key] = e
            starts.append(e)
        }
        lock.unlock()
        for e in starts { queue.async { [weak self] in self?.prepare(e, sampleRate: sampleRate) } }
    }

    /// Den ferdig dekodede lydfila til en lyd-cue (eller lydsporet i en video) hvis den er forberedt med akkurat disse innstillingene og fila er uendret.
    /// Eierskapet går til den som tar den. Ellers nil.
    func takeStream(kind: PreloadKind, path: String, sampleRate: Double, startTime: Double, endTime: Double, loop: Bool) -> StreamingSource? {
        guard let e = find(kind, path), e.rate == sampleRate,
              abs(e.item.startTime - Preloader.clamp(startTime)) < 1e-6, abs(e.item.endTime - Preloader.clamp(endTime)) < 1e-6,
              e.item.loop == loop else { return nil }
        lock.lock(); defer { lock.unlock() }
        guard !e.cancelled, let s = e.stream else { return nil }
        e.stream = nil
        return s
    }

    /// Videofila (med egenskapene allerede lastet) hvis den er forberedt og uendret. Ellers nil.
    func takeVideoAsset(path: String) -> AVURLAsset? {
        guard let e = find(.video, path) else { return nil }
        lock.lock(); defer { lock.unlock() }
        guard !e.cancelled, let a = e.asset else { return nil }
        e.asset = nil
        return a
    }

    /// Slipp alt som er forberedt.
    func clear() {
        lock.lock()
        for key in book.clear() { drop(key) }
        for key in Array(entries.keys) { drop(key) }
        lock.unlock()
    }

    // MARK: Intern

    private static func clamp(_ v: Double) -> Double { v.isFinite ? min(max(v, 0), 86400) : 0 }

    /// Fjerner en oppføring. Er den ikke ferdig, avbrytes den (bakgrunnsjobben kaster resultatet). Kalles med låsen tatt.
    private func drop(_ key: String) {
        guard let e = entries.removeValue(forKey: key) else { return }
        e.cancelled = true
        e.stream = nil
        e.asset = nil
    }

    /// Oppføringen for en fil hvis den er ferdig forberedt og fila ikke er byttet ut siden. Er den byttet ut, slippes oppføringen.
    private func find(_ kind: PreloadKind, _ path: String) -> Entry? {
        lock.lock()
        guard enabled else { lock.unlock(); return nil }
        let key = PreloadItem(kind: kind, path: path.trimmingCharacters(in: .whitespaces)).key
        guard let e = entries[key], e.ready, !e.cancelled else { lock.unlock(); return nil }
        lock.unlock()
        let (size, modified) = Preloader.stamp(path)
        if size != e.size || modified != e.modified {
            lock.lock(); book.remove(key); drop(key); lock.unlock()
            return nil
        }
        return e
    }

    private static func stamp(_ path: String) -> (UInt64, Date?) {
        let a = try? FileManager.default.attributesOfItem(atPath: path)
        return ((a?[.size] as? NSNumber)?.uint64Value ?? 0, a?[.modificationDate] as? Date)
    }

    private func isCancelled(_ e: Entry) -> Bool { lock.lock(); defer { lock.unlock() }; return e.cancelled }

    /// Kjører på forberedelseskøen. Alle feil svelges: cuen lager da avspillingen selv.
    private func prepare(_ e: Entry, sampleRate: Double?) {
        guard !isCancelled(e) else { return }
        let path = e.item.path
        guard FileManager.default.fileExists(atPath: path) else { return }
        let (size, modified) = Preloader.stamp(path)
        let url = URL(fileURLWithPath: path)
        var stream: StreamingSource?
        var asset: AVURLAsset?

        switch e.item.kind {
        case .image:
            MediaCache.shared.prefetch(path: path)
        case .audio:
            guard let rate = sampleRate, rate > 0 else { return }
            let decoder: FrameDecoder
            if let d = try? AudioFileDecoder(url: url, outputRate: rate) { decoder = d }
            else if let d = try? AssetDecoder(url: url, outputRate: rate) { decoder = d }
            else { return }
            stream = warm(StreamingSource(decoder: decoder, sampleRate: rate, startTime: e.item.startTime, endTime: e.item.endTime, loop: e.item.loop))
        case .video:
            let a = AVURLAsset(url: url)
            let done = DispatchSemaphore(value: 0)
            a.loadValuesAsynchronously(forKeys: ["playable", "duration", "tracks"]) { done.signal() }
            _ = done.wait(timeout: .now() + 5)
            asset = a
            if let rate = sampleRate, rate > 0, let decoder = try? AssetDecoder(url: url, outputRate: rate) {
                stream = warm(StreamingSource(decoder: decoder, sampleRate: rate, startTime: e.item.startTime, endTime: e.item.endTime, loop: e.item.loop))
            }
        }

        lock.lock()
        if !e.cancelled {
            e.size = size; e.modified = modified; e.rate = sampleRate ?? 0
            e.stream = stream; e.asset = asset
            e.ready = true
        }
        lock.unlock()
    }

    /// Fyller ringbufferen med litt lyd, så starten er klar. Kilden er ikke koblet til dekodertråden ennå.
    private func warm(_ s: StreamingSource) -> StreamingSource {
        s.fill(minimumFrames: Int(Preloader.warmSeconds * s.sampleRate))
        return s
    }
}
