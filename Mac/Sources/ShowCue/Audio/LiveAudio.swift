import Foundation
import AVFoundation
import CoreAudio
import AudioToolbox

// MARK: - LTC som lyd

/// LTC på én buss i lydpatchen. Kjører på lydtråden.
final class LtcSource: BusSource {
    private let bus: Int
    private let amp: Float
    private let rate: TimecodeRate
    private let bitsPerSample: Double
    private var frame: Int
    private var bits: [Bool]
    private var phase = 0.0   // posisjon i bildet, målt i bit (0–80)
    private var level: Float
    private let lock = UnfairLock()
    private var stopped = false, paused = false

    init(busIndex: Int, levelDb: Double, start: Timecode, rate: TimecodeRate, sampleRate: Double) {
        bus = busIndex
        amp = dbToGain(levelDb)
        self.rate = rate
        bitsPerSample = rate.fps * 80 / sampleRate
        frame = start.toFrame(rate)
        bits = Ltc.bits(start, rate)
        level = amp
    }

    func stop() { lock.locked { stopped = true } }
    func pause() { lock.locked { paused = true } }
    func resume() { lock.locked { paused = false } }

    func render(bus out: UnsafeMutablePointer<Float>, frames: Int, busCount: Int) -> Bool {
        lock.lock(); defer { lock.unlock() }
        if stopped { return false }
        if paused || bus < 0 || bus >= busCount { return true }
        for f in 0..<frames {
            out[f * busCount + bus] += level
            let next = phase + bitsPerSample
            let bit = Int(phase)
            // Overgang i starten av hver bit, og midt i biten for 1.
            if Int(next) != bit { level = -level }
            else if bits[bit] && phase - Double(bit) < 0.5 && next - Double(bit) >= 0.5 { level = -level }
            phase = next
            if phase >= 80 {
                phase -= 80
                frame += 1
                bits = Ltc.bits(Timecode.fromFrame(frame, rate), rate)
            }
        }
        return true
    }
}

// MARK: - Live-inngang

/// Ringbuffer for live-lyd: opptakstråden skriver, lydtråden leser. Holder forsinkelsen lav ved å kaste det
/// eldste når det hoper seg opp (f.eks. mens cuen er pauset).
final class LiveRing {
    let channels: Int
    private var buf: [Float]
    private var readIdx = 0, count = 0 // i samples
    private let maxFrames: Int, trimTo: Int
    private let lock = UnfairLock()

    init(channels: Int, sampleRate: Double, maxLatency: Double = 0.1) {
        self.channels = max(1, channels)
        maxFrames = max(256, Int(sampleRate * maxLatency))
        trimTo = maxFrames / 2
        buf = [Float](repeating: 0, count: (maxFrames + Int(sampleRate)) * self.channels)
    }

    var bufferedFrames: Int { lock.locked { count / channels } }

    func write(_ src: UnsafePointer<Float>, samples: Int) {
        lock.lock(); defer { lock.unlock() }
        let cap = buf.count
        for i in 0..<samples {
            if count == cap { readIdx = (readIdx + 1) % cap; count -= 1 }
            buf[(readIdx + count) % cap] = src[i]
            count += 1
        }
        if count / channels > maxFrames {
            let drop = (count / channels - trimTo) * channels
            readIdx = (readIdx + drop) % cap
            count -= drop
        }
    }

    func write(_ samples: [Float]) { samples.withUnsafeBufferPointer { write($0.baseAddress!, samples: samples.count) } }

    /// Leser alltid `frames` rammer; det som mangler fylles med stillhet.
    func read(_ dst: UnsafeMutablePointer<Float>, frames: Int) {
        lock.lock(); defer { lock.unlock() }
        let want = frames * channels
        let n = min(want, count - count % channels)
        let cap = buf.count
        for i in 0..<n { dst[i] = buf[(readIdx + i) % cap] }
        readIdx = (readIdx + n) % cap
        count -= n
        if n < want { (dst + n).update(repeating: 0, count: want - n) }
    }
}

/// Live-lyd som kilde for en stemme (Voice): uendelig lang, så nivå, fades, pause og routing virker som for filer.
final class LiveSource: PcmSource {
    let ring: LiveRing
    private var position = 0
    var onClose: ((LiveRing) -> Void)?

    init(ring: LiveRing) { self.ring = ring }
    deinit { onClose?(ring) }

    var channels: Int { ring.channels }
    func read(_ dst: UnsafeMutablePointer<Float>, frames: Int) -> Int {
        ring.read(dst, frames: frames)
        position += frames
        return frames
    }
    var isEnded: Bool { false }
    var passPosition: Int { position }
    var passLength: Int? { nil }
}

/// Opptak fra én lydenhet (AVAudioEngine). Startes når første live-cue starter og deles av alle live-cuer.
/// Lyden gjøres om til mikserens samplingsfrekvens og skrives til hver lytters ringbuffer.
final class AudioInput {
    private var engine: AVAudioEngine?
    private var deviceUID = ""
    private var outRate = 0.0
    private var rings: [LiveRing] = []
    private let lock = NSLock()
    private(set) var channels = 0

    /// Lytter på inngangen. Starter opptaket om nødvendig.
    func open(deviceUID uid: String, outputRate: Double) throws -> LiveRing {
        lock.lock(); defer { lock.unlock() }
        if engine == nil || uid != deviceUID || outputRate != outRate { try start(uid, outputRate) }
        let r = LiveRing(channels: channels, sampleRate: outputRate)
        rings.append(r)
        return r
    }

    func close(_ ring: LiveRing) {
        lock.lock(); rings.removeAll { $0 === ring }; lock.unlock()
    }

    private func start(_ uid: String, _ rate: Double) throws {
        stopLocked()
        rings.removeAll() // kanalantallet kan endres med ny enhet
        let e = AVAudioEngine()
        let input = e.inputNode
        if !uid.isEmpty, let id = CoreAudioDevices.deviceID(forUID: uid), let unit = input.audioUnit {
            var dev = id
            let err = AudioUnitSetProperty(unit, kAudioOutputUnitProperty_CurrentDevice, kAudioUnitScope_Global, 0, &dev,
                                           UInt32(MemoryLayout<AudioDeviceID>.size))
            if err != noErr { throw NSError(domain: "ShowCue.Input", code: Int(err), userInfo: [NSLocalizedDescriptionKey: L("Kunne ikke velge inngangen (feil {0})", err)]) }
        }
        let inFormat = input.outputFormat(forBus: 0)
        guard inFormat.channelCount > 0, inFormat.sampleRate > 0 else {
            throw NSError(domain: "ShowCue.Input", code: -1, userInfo: [NSLocalizedDescriptionKey: L("Fant ingen lydinngang. Sjekk at ShowCue har tilgang til mikrofonen (Systeminnstillinger → Personvern).")])
        }
        let ch = Int(inFormat.channelCount)
        guard let outFormat = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: rate, channels: AVAudioChannelCount(ch), interleaved: true),
              let converter = AVAudioConverter(from: inFormat, to: outFormat) else {
            throw NSError(domain: "ShowCue.Input", code: -2, userInfo: [NSLocalizedDescriptionKey: L("Kunne ikke gjøre om lydformatet fra inngangen")])
        }
        channels = ch
        input.installTap(onBus: 0, bufferSize: 512, format: inFormat) { [weak self] buffer, _ in
            guard let self else { return }
            let cap = AVAudioFrameCount(Double(buffer.frameLength) * rate / inFormat.sampleRate) + 32
            guard let out = AVAudioPCMBuffer(pcmFormat: outFormat, frameCapacity: cap) else { return }
            var fed = false
            var err: NSError?
            converter.convert(to: out, error: &err) { _, status in
                if fed { status.pointee = .noDataNow; return nil }
                fed = true
                status.pointee = .haveData
                return buffer
            }
            guard err == nil, out.frameLength > 0, let p = out.floatChannelData?[0] else { return }
            let samples = Int(out.frameLength) * ch
            self.lock.lock()
            let targets = self.rings
            self.lock.unlock()
            for r in targets { r.write(p, samples: samples) }
        }
        e.prepare()
        try e.start()
        engine = e
        deviceUID = uid
        outRate = rate
    }

    private func stopLocked() {
        engine?.inputNode.removeTap(onBus: 0)
        engine?.stop()
        engine = nil
    }

    func stop() { lock.lock(); stopLocked(); rings.removeAll(); lock.unlock() }

    /// Antall kanaler på inngangen (0 hvis ukjent).
    static func probeChannels(deviceUID uid: String) -> Int {
        if !uid.isEmpty, let id = CoreAudioDevices.deviceID(forUID: uid) { return CoreAudioDevices.inputChannelCount(id) }
        let e = AVAudioEngine()
        return Int(e.inputNode.outputFormat(forBus: 0).channelCount)
    }
}
