import Foundation
import Network

/// Sender nettverks-cuer: tekst over UDP eller TCP, eller en HTTP-forespørsel. Samme format som Windows-versjonen.
enum NetworkSender {
    private static let escapes: [Character: UInt8] = ["r": 13, "n": 10, "t": 9, "0": 0, "\\": 92]

    /// Gjør om \r \n \t \0 \\ og \xHH til bytes. Resten sendes som UTF-8.
    static func unescape(_ text: String) -> Data {
        var out = Data()
        let chars = Array(text)
        var i = 0
        while i < chars.count {
            let ch = chars[i]
            if ch == "\\", i + 1 < chars.count {
                let next = chars[i + 1]
                if let b = escapes[next] { out.append(b); i += 2; continue }
                if next == "x" || next == "X", i + 3 < chars.count, let b = UInt8(String(chars[i + 2...i + 3]), radix: 16) {
                    out.append(b); i += 4; continue
                }
            }
            out.append(contentsOf: Array(String(ch).utf8))
            i += 1
        }
        return out
    }

    /// Sender cuen. Returnerer nil når det gikk bra, ellers en feilmelding.
    static func send(protocol proto: NetworkProtocol, host: String, port: Int, message: String,
                     url: String, method: String, timeout: TimeInterval = 3) async -> String? {
        switch proto {
        case .udp, .tcp:
            guard let p = NWEndpoint.Port(rawValue: UInt16(clamping: max(1, min(65535, port)))) else { return L("Ugyldig port") }
            let conn = NWConnection(host: NWEndpoint.Host(host), port: p, using: proto == .udp ? .udp : .tcp)
            return await withCheckedContinuation { (cont: CheckedContinuation<String?, Never>) in
                let once = Once(cont)
                let queue = DispatchQueue(label: "showcue.network")
                conn.stateUpdateHandler = { state in
                    switch state {
                    case .ready:
                        conn.send(content: unescape(message), completion: .contentProcessed { err in
                            once.resume(err.map { $0.localizedDescription })
                            // TCP: la dataene gå ut før forbindelsen lukkes.
                            queue.asyncAfter(deadline: .now() + 0.2) { conn.cancel() }
                        })
                    case .failed(let e), .waiting(let e):
                        once.resume(e.localizedDescription); conn.cancel()
                    default: break
                    }
                }
                conn.start(queue: queue)
                queue.asyncAfter(deadline: .now() + timeout) { once.resume(L("Tidsavbrudd – fikk ikke kontakt")); conn.cancel() }
            }
        case .http:
            guard let u = URL(string: url), let scheme = u.scheme?.lowercased(), scheme == "http" || scheme == "https", u.host != nil else {
                return L("Ugyldig adresse (må starte med http:// eller https://)")
            }
            var req = URLRequest(url: u, timeoutInterval: 5)
            req.httpMethod = method.uppercased()
            if !message.isEmpty {
                req.httpBody = unescape(message)
                let body = message.trimmingCharacters(in: .whitespaces)
                req.setValue((body.hasPrefix("{") || body.hasPrefix("[") ? "application/json" : "text/plain") + L("; charset=utf-8"),
                             forHTTPHeaderField: "Content-Type")
            }
            do {
                let (_, resp) = try await URLSession.shared.data(for: req)
                guard let h = resp as? HTTPURLResponse else { return nil }
                return (200..<300).contains(h.statusCode) ? nil
                    : "HTTP \(h.statusCode) \(HTTPURLResponse.localizedString(forStatusCode: h.statusCode).capitalized)"
            } catch {
                return (error as? URLError)?.code == .timedOut ? L("Tidsavbrudd – fikk ikke kontakt") : error.localizedDescription
            }
        }
    }

    static func send(_ cue: Cue) async -> String? {
        await send(protocol: cue.networkProtocol, host: cue.host, port: cue.port, message: cue.message, url: cue.url, method: cue.method)
    }

    /// Fortsettelsen skal bare gjenopptas én gang (svar, feil eller tidsavbrudd – det som kommer først).
    private final class Once {
        private var cont: CheckedContinuation<String?, Never>?
        private let lock = NSLock()
        init(_ c: CheckedContinuation<String?, Never>) { cont = c }
        func resume(_ v: String?) {
            lock.lock(); let c = cont; cont = nil; lock.unlock()
            c?.resume(returning: v)
        }
    }
}
