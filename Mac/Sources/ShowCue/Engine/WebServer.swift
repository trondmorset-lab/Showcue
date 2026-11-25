import Foundation
import Network

/// Liten HTTP-tjener for nettsiden som styrer showet. Egen kø (ikke hovedtråden), og ingenting her rører lydmotoren:
/// kommandoer leveres til `onCommand` på hovedtråden. Hver forbindelse leser én forespørsel (med tidsfrist), svarer og lukkes.
/// Tolking og PIN-kontroll ligger i `WebRemoteCore`.
final class WebServer {
    static let maxConnections = 16
    static let maxPerClient = 6
    static let requestTimeout: TimeInterval = 5

    private final class Session {
        let conn: NWConnection
        let ip: String
        var buffer: [UInt8] = []
        var finished = false
        var counted = false
        var timeout: DispatchWorkItem?
        init(conn: NWConnection, ip: String) { self.conn = conn; self.ip = ip }
    }

    private let queue = DispatchQueue(label: "showcue.web")
    private var listener: NWListener?
    private var core: WebRemoteCore?
    private var sessions: [ObjectIdentifier: Session] = [:]
    private var active = 0
    private var perClient: [String: Int] = [:]

    /// Kalles på hovedtråden med kommandoen og klientadressen.
    var onCommand: ((WebCommand, String) -> Void)?
    /// Kalles på hovedtråden hvis tjeneren faller ut etter oppstart (f.eks. porten er opptatt).
    var onFailure: ((String) -> Void)?
    private(set) var isRunning = false
    private(set) var port = 0

    func start(port: Int, core: WebRemoteCore) throws {
        stop()
        guard let p = NWEndpoint.Port(rawValue: UInt16(clamping: port)), port > 0 else {
            throw NSError(domain: "Web", code: 1, userInfo: [NSLocalizedDescriptionKey: L("Port {0} er opptatt eller utilgjengelig", port)])
        }
        let l = try NWListener(using: .tcp, on: p)
        l.newConnectionHandler = { [weak self] conn in self?.accept(conn) }
        l.stateUpdateHandler = { [weak self, weak l] state in
            if case .failed(let error) = state {
                let message = error.localizedDescription
                DispatchQueue.main.async { if let l { self?.listenerFailed(l, message) } }
            }
        }
        queue.sync { self.core = core }
        l.start(queue: queue)
        listener = l
        self.port = port
        isRunning = true
    }

    private func listenerFailed(_ l: NWListener, _ message: String) {
        guard listener === l else { return }
        stop()
        onFailure?(message)
    }

    func stop() {
        let l = listener
        listener = nil
        isRunning = false
        l?.cancel()
        queue.async { [weak self] in
            guard let self else { return }
            self.core = nil
            for s in Array(self.sessions.values) { self.finish(s) }
            self.perClient.removeAll()
            self.active = 0
        }
    }

    deinit { listener?.cancel() }

    // MARK: Forbindelser (alt under kjører på `queue`)

    private static func clientIP(_ endpoint: NWEndpoint) -> String {
        guard case let .hostPort(host, _) = endpoint else { return "?" }
        let text: String
        switch host {
        case .ipv4(let a): text = "\(a)"
        case .ipv6(let a): text = "\(a)"
        case .name(let n, _): text = n
        @unknown default: text = "?"
        }
        return text.split(separator: "%").first.map(String.init) ?? text
    }

    private func accept(_ conn: NWConnection) {
        let s = Session(conn: conn, ip: Self.clientIP(conn.endpoint))
        sessions[ObjectIdentifier(s)] = s
        conn.stateUpdateHandler = { [weak self] state in
            switch state {
            case .failed, .cancelled: self?.finish(s)
            default: break
            }
        }
        let t = DispatchWorkItem { [weak self] in self?.finish(s) }
        s.timeout = t
        queue.asyncAfter(deadline: .now() + Self.requestTimeout, execute: t)
        conn.start(queue: queue)

        guard active < Self.maxConnections, perClient[s.ip, default: 0] < Self.maxPerClient else {
            send(s, WebRemote.plainResponse(503))
            return
        }
        active += 1
        perClient[s.ip, default: 0] += 1
        s.counted = true
        receive(s)
    }

    private func receive(_ s: Session) {
        guard !s.finished else { return }
        let room = WebRemote.maxRequestBytes - s.buffer.count
        guard room > 0 else { finish(s); return }
        s.conn.receive(minimumIncompleteLength: 1, maximumLength: room) { [weak self] data, _, isComplete, error in
            guard let self, !s.finished else { return }
            if error != nil { self.finish(s); return }
            if let data, !data.isEmpty {
                s.buffer.append(contentsOf: data)
                guard let core = self.core else { self.finish(s); return }
                let reply = core.handle(s.buffer, length: s.buffer.count, client: s.ip)
                if reply.complete {
                    if let cmd = reply.command {
                        let ip = s.ip
                        DispatchQueue.main.async { [weak self] in self?.onCommand?(cmd, ip) }
                    }
                    self.send(s, reply.response)
                    return
                }
                self.receive(s)
            } else if isComplete {
                self.finish(s)
            } else {
                self.receive(s)
            }
        }
    }

    private func send(_ s: Session, _ bytes: [UInt8]) {
        guard !s.finished else { return }
        s.conn.send(content: Data(bytes), contentContext: .finalMessage, isComplete: true,
                    completion: .contentProcessed { [weak self] _ in self?.finish(s) })
    }

    private func finish(_ s: Session) {
        guard !s.finished else { return }
        s.finished = true
        s.timeout?.cancel()
        s.timeout = nil
        s.conn.cancel()
        if s.counted {
            active = max(0, active - 1)
            if let n = perClient[s.ip] { if n <= 1 { perClient[s.ip] = nil } else { perClient[s.ip] = n - 1 } }
        }
        sessions[ObjectIdentifier(s)] = nil
    }
}
