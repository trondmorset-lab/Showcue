import Foundation
import AVFoundation

/// Integrert lydstyrke (LUFS) etter ITU-R BS.1770-4 / EBU R128:
/// K-vekting, blokker på 400 ms med 75 % overlapp, absolutt port på −70 LUFS og relativ port 10 LU under snittet.
/// Samme beregning som i Windows-versjonen.
final class LoudnessMeter {
    private struct Biquad {
        var b0 = 0.0, b1 = 0.0, b2 = 0.0, a1 = 0.0, a2 = 0.0, z1 = 0.0, z2 = 0.0

        mutating func process(_ x: Double) -> Double {
            let y = b0 * x + z1
            z1 = b1 * x - a1 * y + z2
            z2 = b2 * x - a2 * y
            return y
        }

        // K-vektingen (ITU-R BS.1770) regnes om til hver samplingsfrekvens med bilineær transformasjon.
        static func shelf(_ rate: Double) -> Biquad {
            let f0 = 1681.974450955533, g = 3.999843853973347, q = 0.7071752369554196
            let k = tan(Double.pi * f0 / rate)
            let vh = pow(10, g / 20), vb = pow(vh, 0.4996667741545416)
            let a0 = 1 + k / q + k * k
            return Biquad(b0: (vh + vb * k / q + k * k) / a0, b1: 2 * (k * k - vh) / a0, b2: (vh - vb * k / q + k * k) / a0,
                          a1: 2 * (k * k - 1) / a0, a2: (1 - k / q + k * k) / a0)
        }

        static func highPass(_ rate: Double) -> Biquad {
            let f0 = 38.13547087602444, q = 0.5003270373238773
            let k = tan(Double.pi * f0 / rate)
            let a0 = 1 + k / q + k * k
            return Biquad(b0: 1, b1: -2, b2: 1, a1: 2 * (k * k - 1) / a0, a2: (1 - k / q + k * k) / a0)
        }
    }

    private let channels: Int
    private var shelf: [Biquad], highPass: [Biquad]
    private let weights: [Double]
    private let step: Int
    private var sum: [Double]
    private var inStep = 0
    private var last: [[Double]] = []
    private var blocks: [Double] = []

    init(channels: Int, sampleRate: Double) {
        self.channels = max(1, channels)
        shelf = Array(repeating: .shelf(sampleRate), count: self.channels)
        highPass = Array(repeating: .highPass(sampleRate), count: self.channels)
        let n = self.channels
        weights = (0..<n).map { c in
            if n == 6 { return c == 3 ? 0 : c >= 4 ? 1.41 : 1 }
            if n == 5 { return c >= 3 ? 1.41 : 1 }
            return 1
        }
        step = max(1, Int(sampleRate) / 10)
        sum = Array(repeating: 0, count: n)
    }

    /// Legg til interleaved samples.
    func add(_ samples: UnsafePointer<Float>, frames: Int) {
        for f in 0..<frames {
            for c in 0..<channels {
                let y = highPass[c].process(shelf[c].process(Double(samples[f * channels + c])))
                sum[c] += y * y
            }
            inStep += 1
            if inStep == step { endStep() }
        }
    }

    func add(_ samples: [Float]) { samples.withUnsafeBufferPointer { add($0.baseAddress!, frames: samples.count / channels) } }

    private func endStep() {
        last.append(sum)
        sum = Array(repeating: 0, count: channels)
        inStep = 0
        if last.count > 4 { last.removeFirst() }
        guard last.count == 4 else { return }
        var power = 0.0
        for c in 0..<channels { power += weights[c] * last.reduce(0) { $0 + $1[c] } / (4.0 * Double(step)) }
        blocks.append(power)
    }

    private static func lufs(_ p: Double) -> Double { -0.691 + 10 * log10(p) }

    /// Integrert lydstyrke, eller nil hvis lyden er for kort (under 400 ms) eller stille.
    var integrated: Double? {
        let abs = blocks.filter { $0 > 0 && LoudnessMeter.lufs($0) > -70 }
        guard !abs.isEmpty else { return nil }
        let rel = LoudnessMeter.lufs(abs.reduce(0, +) / Double(abs.count)) - 10
        let gated = abs.filter { LoudnessMeter.lufs($0) > rel }
        return gated.isEmpty ? nil : LoudnessMeter.lufs(gated.reduce(0, +) / Double(gated.count))
    }

    /// Måler en lydfil mellom start og slutt (sekunder, slutt 0 = filslutt).
    static func measure(file url: URL, start: Double = 0, end: Double = 0, isCancelled: () -> Bool = { false }) throws -> Double? {
        let file = try AVAudioFile(forReading: url, commonFormat: .pcmFormatFloat32, interleaved: false)
        let fmt = file.processingFormat
        let rate = fmt.sampleRate, ch = Int(fmt.channelCount)
        if start > 0 { file.framePosition = min(file.length, AVAudioFramePosition(start * rate)) }
        var remaining = file.length - file.framePosition
        if end > start { remaining = min(remaining, AVAudioFramePosition((end - start) * rate)) }
        let meter = LoudnessMeter(channels: ch, sampleRate: rate)
        let cap = AVAudioFrameCount(max(1, Int(rate) / 10))
        guard let buf = AVAudioPCMBuffer(pcmFormat: fmt, frameCapacity: cap) else { return nil }
        var inter = [Float](repeating: 0, count: Int(cap) * ch)
        while remaining > 0 && !isCancelled() {
            try file.read(into: buf, frameCount: AVAudioFrameCount(min(AVAudioFramePosition(cap), remaining)))
            let n = Int(buf.frameLength)
            if n == 0 { break }
            for c in 0..<ch { let src = buf.floatChannelData![c]; for f in 0..<n { inter[f * ch + c] = src[f] } }
            inter.withUnsafeBufferPointer { meter.add($0.baseAddress!, frames: n) }
            remaining -= AVAudioFramePosition(n)
        }
        return isCancelled() ? nil : meter.integrated
    }

    /// Nivået (dB) som gir målet, begrenset til cuens område −60…+12 dB.
    static func gain(for measured: Double, target: Double) -> Double {
        (min(12, max(-60, target - measured)) * 10).rounded() / 10
    }
}
