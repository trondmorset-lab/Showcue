import Foundation

/// Forhåndslytting: spiller lyden i en cue fra et valgfritt punkt, gjennom lydpatchen, uten å påvirke showet.
final class AudioPreview: ObservableObject {
    /// Hvor lyttingen spilles: en mikser, og første av to utganger (0 = cuens egne utganger).
    struct Target {
        let mixer: PatchMixer
        let leftBus: Int
    }

    private let target: () -> Target?
    private weak var playingMixer: PatchMixer?
    @Published private(set) var cue: Cue?
    @Published private(set) var isPlaying = false
    private(set) var voice: Voice?
    private(set) var stream: StreamingSource?
    private var from = 0.0

    init(target: @escaping () -> Target?) { self.target = target }

    /// Forhåndslytt gjennom cuens egne utganger i `mixer`.
    convenience init(mixer: @escaping () -> PatchMixer?) {
        self.init(target: { mixer().map { Target(mixer: $0, leftBus: 0) } })
    }

    /// True når lyttingen går ut på cuens egne utganger (dvs. til salen), ikke til en egen lytteutgang.
    var goesToShowOutputs: Bool { (target()?.leftBus ?? 0) <= 0 }

    /// Spiller fortsatt (sjekker selve stemmen, ikke bare UI-flagget).
    var isActive: Bool { voice.map { !$0.isFinished } ?? false }

    /// Posisjon i fila (sekunder) mens det spilles, ellers nil.
    var position: Double? {
        guard let v = voice, !v.isFinished else { return nil }
        return from + v.passPosition
    }

    /// Spill fra `from` til `to` (0 = filslutt). Starter man etter sluttpunktet, spilles det til filslutt.
    @discardableResult
    func play(_ cue: Cue, from: Double, to: Double = 0) -> Bool {
        stop()
        guard let t = target(), !cue.filePath.isEmpty, FileManager.default.fileExists(atPath: cue.filePath) else { return false }
        let m = t.mixer
        let url = URL(fileURLWithPath: cue.filePath)
        let decoder: FrameDecoder
        if cue.type == .video {
            guard let d = try? AssetDecoder(url: url, outputRate: m.sampleRate) else { return false }
            decoder = d
        } else if let d = try? AudioFileDecoder(url: url, outputRate: m.sampleRate) {
            decoder = d
        } else if let d = try? AssetDecoder(url: url, outputRate: m.sampleRate) {
            decoder = d
        } else {
            return false
        }
        let start = max(0, from)
        var end = to
        if end > 0 && end <= start + 0.05 { end = 0 }
        let s = StreamingSource(decoder: decoder, sampleRate: m.sampleRate, startTime: start, endTime: end, loop: false)
        let v = Voice(source: s, sampleRate: m.sampleRate, volumeDb: cue.volumeDb, fadeIn: 0.01, fadeOut: 0.02, loop: false,
                      routes: t.leftBus > 0 ? AudioRoute.downmix(fileChannels: decoder.channels, leftBus: t.leftBus)
                                            : cue.effectiveRoutes(fileChannels: decoder.channels))
        v.onFinished = { [weak self, weak v, weak s] in
            DispatchQueue.main.async {
                if let s { AudioFeeder.shared.remove(s) }
                guard let self, let v, self.voice === v else { return }
                self.clear()
            }
        }
        s.fill(minimumFrames: Int(0.3 * m.sampleRate))
        AudioFeeder.shared.add(s)
        voice = v
        stream = s
        self.from = s.startTime
        self.cue = cue
        isPlaying = true
        playingMixer = m
        m.add(v)
        return true
    }

    func stop() {
        guard let v = voice else { return }
        v.stop(fade: 0.02)
        let s = stream
        let m = playingMixer
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.2) {
            if let s { AudioFeeder.shared.remove(s) }
            m?.remove(v)
        }
        clear()
    }

    private func clear() {
        voice = nil
        stream = nil
        cue = nil
        isPlaying = false
    }
}
