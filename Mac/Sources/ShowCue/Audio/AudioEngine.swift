import Foundation
import AVFoundation

/// Én avspilling: en lydfil (lyd-cue, spor i en spilleliste, lydsporet i en video) eller en live-inngang.
final class Playback {
    let voice: Voice
    /// Fila som dekodes i bakgrunnen (nil for live-inngang).
    let stream: StreamingSource?
    weak var engine: AudioEngine?
    var onFinished: (() -> Void)?
    private(set) var started = false

    init(voice: Voice, stream: StreamingSource?) {
        self.voice = voice
        self.stream = stream
    }

    var fileChannels: Int { voice.source.channels }
    var passPosition: Double { voice.passPosition }
    var passLength: Double? { voice.passLength }
    var elapsed: Double { voice.elapsed }

    /// Starter lyden (fyller litt buffer først så starten blir umiddelbar).
    func start() {
        guard !started, let e = engine, let m = e.mixer else { return }
        started = true
        if let s = stream {
            s.fill(minimumFrames: Int(0.3 * s.sampleRate))
            AudioFeeder.shared.add(s)
        }
        m.add(voice)
    }

    func fadeTo(_ db: Double, seconds: Double, stopWhenDone: Bool, curve: FadeCurve = .db) {
        voice.fadeTo(db, seconds: seconds, stopWhenDone: stopWhenDone, curve: curve)
    }
    func adjustLevel(_ db: Double) { voice.adjustLevel(db) }
    func setRouting(_ routes: [AudioRoute]) { voice.setRouting(routes) }
    func requestRewind() { stream?.requestRewind() }
    func pause() { voice.pause() }
    func resume() { voice.resume() }
    var isLooping: Bool { stream?.isLooping ?? false }
    func releaseLoop() { stream?.releaseLoop() }

    func stop(fade: Double) {
        voice.stop(fade: fade)
        if fade <= 0 { cleanup() }
    }

    /// Fjern fra mikser og dekoder (trygt å kalle flere ganger).
    func cleanup() {
        engine?.mixer?.remove(voice)
        if let s = stream { AudioFeeder.shared.remove(s) }
    }
}

/// Lydmotoren: én CoreAudio-utgang med PatchMixer, testtoner og avspillinger.
final class AudioEngine: ObservableObject {
    private let output = AudioOutput()
    @Published private(set) var status = "Lyd ikke startet"
    @Published private(set) var deviceChannels = 0
    @Published private(set) var sampleRate: Double = 0
    @Published private(set) var channelNames: [String] = []
    @Published private(set) var testTones: Set<Int> = []
    private var toneSources: [Int: TestToneSource] = [:]

    var mixer: PatchMixer? { output.isRunning ? mixerRef : nil }
    private var mixerRef: PatchMixer?
    var isRunning: Bool { output.isRunning }

    /// - Parameter fallbackToDefault: prøv systemets standard utgang hvis valgt enhet ikke kan åpnes
    ///   (ikke for forhåndslytting – den skal aldri havne i salen ved en feil).
    func start(_ s: AppSettings, fallbackToDefault: Bool = true) {
        stop()
        do {
            try open(s, uid: s.audioDeviceUID)
        } catch {
            let reason = error.localizedDescription
            guard fallbackToDefault else { status = "Kunne ikke åpne lydenheten: \(reason)"; return }
            do {
                try open(s, uid: "")
                status = "Kunne ikke åpne valgt lydenhet (\(reason)). Bruker systemets standard utgang."
            } catch {
                status = "Ingen lydutgang: \(error.localizedDescription)"
            }
        }
    }

    private func open(_ s: AppSettings, uid: String) throws {
        guard let dev = CoreAudioDevices.device(uid: uid) else {
            throw NSError(domain: "ShowCue.Audio", code: -10, userInfo: [NSLocalizedDescriptionKey: "Lydenheten finnes ikke (er den koblet til?)"])
        }
        let m = try output.start(device: dev, maxChannels: s.maxChannels, bufferFrames: s.bufferFrames, patch: s.buses)
        m.onSourceFinished = { [weak self] src in
            guard let tone = src as? TestToneSource else { return }
            DispatchQueue.main.async { self?.toneFinished(tone) }
        }
        mixerRef = m
        deviceChannels = m.deviceChannels
        sampleRate = m.sampleRate
        channelNames = CoreAudioDevices.channelNames(dev.id, count: m.deviceChannels)
        status = "\(dev.name) · \(m.deviceChannels) kanaler · \(Cue.fmt(m.sampleRate / 1000)) kHz"
    }

    func stop() {
        output.stop()
        mixerRef = nil
        toneSources.removeAll()
        testTones.removeAll()
        deviceChannels = 0
        channelNames = []
    }

    func applyPatch(_ buses: [PatchBus]) { mixer?.setPatch(buses) }

    // MARK: Avspilling

    /// Lager en avspilling av en lyd-cue (startes med `start()`). `offset` = start så mange sekunder inn (prøver).
    func makePlayback(audio cue: Cue, offset: Double = 0) throws -> Playback {
        guard let m = mixer else { throw NSError(domain: "ShowCue.Audio", code: -20, userInfo: [NSLocalizedDescriptionKey: "Lydutgangen er ikke startet. Sjekk innstillinger."]) }
        let url = URL(fileURLWithPath: cue.filePath)
        let decoder: FrameDecoder
        do { decoder = try AudioFileDecoder(url: url, outputRate: m.sampleRate) }
        catch { decoder = try AssetDecoder(url: url, outputRate: m.sampleRate) } // reserve for formater AVAudioFile ikke åpner
        let stream = StreamingSource(decoder: decoder, sampleRate: m.sampleRate, startTime: cue.startTime + max(0, offset), endTime: cue.endTime, loop: cue.loop)
        let voice = Voice(source: stream, sampleRate: m.sampleRate, volumeDb: cue.volumeDb, fadeIn: offset > 0 ? 0.02 : cue.fadeIn, fadeOut: cue.fadeOut,
                          loop: stream.loop, routes: cue.effectiveRoutes(fileChannels: decoder.channels))
        return wire(voice, stream)
    }

    /// Ett spor i en spilleliste: hele fila, med spillelistens nivå og routing.
    func makePlayback(track path: String, for cue: Cue, fadeIn: Double, offset: Double = 0) throws -> Playback {
        guard let m = mixer else { throw NSError(domain: "ShowCue.Audio", code: -20, userInfo: [NSLocalizedDescriptionKey: "Lydutgangen er ikke startet. Sjekk innstillinger."]) }
        let url = URL(fileURLWithPath: path)
        let decoder: FrameDecoder
        do { decoder = try AudioFileDecoder(url: url, outputRate: m.sampleRate) }
        catch { decoder = try AssetDecoder(url: url, outputRate: m.sampleRate) }
        let stream = StreamingSource(decoder: decoder, sampleRate: m.sampleRate, startTime: offset, endTime: 0, loop: false)
        let voice = Voice(source: stream, sampleRate: m.sampleRate, volumeDb: cue.volumeDb, fadeIn: fadeIn, fadeOut: 0,
                          loop: false, routes: cue.effectiveRoutes(fileChannels: decoder.channels))
        return wire(voice, stream)
    }

    /// Lydsporet i en video. nil hvis videoen ikke har lyd. `start` = hvor i fila lyden starter.
    func makePlayback(videoAudio cue: Cue, start: Double? = nil) -> Playback? {
        guard let m = mixer, let decoder = try? AssetDecoder(url: URL(fileURLWithPath: cue.filePath), outputRate: m.sampleRate) else { return nil }
        let stream = StreamingSource(decoder: decoder, sampleRate: m.sampleRate, startTime: start ?? cue.startTime, endTime: cue.endTime, loop: cue.loop)
        // Fade ut styres av video-cuen (samme tid for bilde og lyd).
        let voice = Voice(source: stream, sampleRate: m.sampleRate, volumeDb: cue.volumeDb, fadeIn: cue.fadeIn, fadeOut: 0,
                          loop: stream.loop, routes: cue.effectiveRoutes(fileChannels: decoder.channels))
        return wire(voice, stream)
    }

    /// Live-inngang med cuens nivå, fades og routing. Ringbufferen lukkes når stemmen er borte.
    func makePlayback(live ring: LiveRing, for cue: Cue, onClose: @escaping (LiveRing) -> Void) throws -> Playback {
        guard let m = mixer else { throw NSError(domain: "ShowCue.Audio", code: -20, userInfo: [NSLocalizedDescriptionKey: "Lydutgangen er ikke startet. Sjekk innstillinger."]) }
        let src = LiveSource(ring: ring)
        src.onClose = onClose
        let voice = Voice(source: src, sampleRate: m.sampleRate, volumeDb: cue.volumeDb, fadeIn: cue.fadeIn, fadeOut: cue.fadeOut,
                          loop: true, routes: cue.effectiveRoutes(fileChannels: ring.channels))
        return wire(voice, nil)
    }

    private func wire(_ voice: Voice, _ stream: StreamingSource?) -> Playback {
        let p = Playback(voice: voice, stream: stream)
        p.engine = self
        voice.onFinished = { [weak p] in
            guard let p else { return }
            p.cleanup()
            p.onFinished?()
        }
        return p
    }

    // MARK: Testtone

    func setTestTone(bus: Int, on: Bool) {
        guard let m = mixer else { return }
        if on, toneSources[bus] == nil {
            let t = TestToneSource(busIndex: bus)
            toneSources[bus] = t
            m.add(t)
            testTones.insert(bus)
        } else if !on, let t = toneSources.removeValue(forKey: bus) {
            t.stop()
            testTones.remove(bus)
        }
    }

    func stopAllTestTones() {
        toneSources.values.forEach { $0.stop() }
        toneSources.removeAll()
        testTones.removeAll()
    }

    private func toneFinished(_ t: TestToneSource) {
        if toneSources[t.busIndex] === t { toneSources.removeValue(forKey: t.busIndex); testTones.remove(t.busIndex) }
    }

    // MARK: Undersøk filer

    /// Lengde og kanaler i en lydfil (0, 0 ved feil).
    static func probeAudio(_ path: String) -> (duration: Double, channels: Int) {
        let url = URL(fileURLWithPath: path)
        if let f = try? AVAudioFile(forReading: url) {
            let fmt = f.processingFormat
            return (fmt.sampleRate > 0 ? Double(f.length) / fmt.sampleRate : 0, Int(fmt.channelCount))
        }
        return AssetDecoder.probe(url: url)
    }
}
