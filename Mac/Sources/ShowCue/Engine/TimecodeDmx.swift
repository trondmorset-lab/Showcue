import Foundation
import Darwin

/// Sender MTC-kvartbilder fra en egen tråd (100–120 meldinger i sekundet).
final class MtcSender {
    private let send: ([UInt8]) -> Void
    private let startFrame: Int
    private let rate: TimecodeRate
    private let lock = NSLock()
    private var running = true
    private var pausedAt: Double?
    private var pausedTotal = 0.0
    private let begin = Date()
    private var next = 0

    /// `send` sender én MIDI-melding (kalles fra MTC-tråden).
    init(send: @escaping ([UInt8]) -> Void, start: Timecode, rate: TimecodeRate) {
        self.send = send
        self.rate = rate
        startFrame = start.toFrame(rate)
        send(Mtc.fullFrame(start, rate)) // mottakeren hopper rett til starttiden
        let t = Thread { [weak self] in self?.run() }
        t.qualityOfService = .userInteractive
        t.name = "MTC"
        t.start()
    }

    private var elapsed: Double {
        lock.lock(); defer { lock.unlock() }
        let now = Date().timeIntervalSince(begin)
        return (pausedAt ?? now) - pausedTotal
    }

    func pause() { lock.lock(); if pausedAt == nil { pausedAt = Date().timeIntervalSince(begin) }; lock.unlock() }
    func resume() {
        lock.lock()
        if let p = pausedAt { pausedTotal += Date().timeIntervalSince(begin) - p; pausedAt = nil }
        lock.unlock()
    }
    func stop() { lock.lock(); running = false; lock.unlock() }
    private var isRunning: Bool { lock.lock(); defer { lock.unlock() }; return running }

    private func run() {
        let qps = rate.fps * 4
        while isRunning {
            let due = Int(elapsed * qps)
            if due - next > 16 { next = due - due % 8 } // hengt etter: start en ny rekke
            while next <= due && isRunning {
                send([0xF1, Mtc.quarterFrame(at: next, startFrame: startFrame, rate)])
                next += 1
            }
            usleep(1000)
        }
    }
}

/// Sender DMX over Art-Net. Hvert univers (per mottaker) har egne nivåer og fades.
/// Sendes 40 ganger i sekundet mens noe endres, ellers hvert sekund så nodene holder nivået.
final class DmxOutput {
    private final class Universe {
        let host: String, number: Int
        let fader = DmxFader()
        var sequence: UInt8 = 0
        var last = [UInt8](repeating: 0, count: 512)
        var lastSent = -Double.infinity
        var addr: sockaddr_in?
        init(host: String, number: Int) { self.host = host; self.number = number }
    }

    private var universes: [String: Universe] = [:]
    private let lock = NSLock()
    private let begin = Date()
    private let queue = DispatchQueue(label: "showcue.dmx", qos: .userInitiated)
    private var timer: DispatchSourceTimer?
    private let fd: Int32
    var onError: ((String) -> Void)?

    init() {
        fd = socket(AF_INET, SOCK_DGRAM, 0)
        var on: Int32 = 1
        if fd >= 0 { setsockopt(fd, SOL_SOCKET, SO_BROADCAST, &on, socklen_t(MemoryLayout<Int32>.size)) }
    }

    deinit {
        timer?.cancel()
        if fd >= 0 { close(fd) }
    }

    private var now: Double { Date().timeIntervalSince(begin) }

    func fade(host: String, universe: Int, to levels: [Int: UInt8], seconds: Double) {
        lock.lock()
        let key = "\(host)#\(universe)"
        let u = universes[key] ?? Universe(host: host, number: universe)
        universes[key] = u
        u.fader.fade(to: levels, seconds: seconds, now: now)
        u.lastSent = -.infinity // send med en gang
        lock.unlock()
        startTimer()
    }

    private func startTimer() {
        guard timer == nil else { return }
        let t = DispatchSource.makeTimerSource(queue: queue)
        t.schedule(deadline: .now(), repeating: .milliseconds(25))
        t.setEventHandler { [weak self] in self?.tick() }
        t.resume()
        timer = t
    }

    private func tick() {
        var out: [(String, sockaddr_in?, [UInt8])] = []
        lock.lock()
        let t = now
        for u in universes.values {
            let v = u.fader.values(t)
            if t - u.lastSent < 1 && v == u.last { continue }
            u.last = v
            u.lastSent = t
            u.sequence = u.sequence % 255 + 1
            if u.addr == nil { u.addr = UDP.resolve(u.host, ArtNet.port) }
            out.append((u.host, u.addr, ArtNet.dmxPacket(universe: u.number, sequence: u.sequence, data: v)))
        }
        lock.unlock()
        for (host, resolved, packet) in out {
            guard fd >= 0, var addr = resolved else { onError?("Art-Net: ugyldig adresse \(host)"); continue }
            let sent = packet.withUnsafeBytes { buf in
                withUnsafePointer(to: &addr) { p in
                    p.withMemoryRebound(to: sockaddr.self, capacity: 1) { sendto(fd, buf.baseAddress, buf.count, 0, $0, socklen_t(MemoryLayout<sockaddr_in>.size)) }
                }
            }
            if sent < 0 { onError?("Art-Net til \(host): \(String(cString: strerror(errno)))") }
        }
    }
}
