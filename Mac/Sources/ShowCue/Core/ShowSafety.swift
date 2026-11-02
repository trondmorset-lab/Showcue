import Foundation
import CryptoKit

/// Autolagring og sikkerhetskopier, så et krasj eller strømbrudd ikke koster arbeid.
///  - Autolagring: ulagrede endringer skrives jevnlig til en egen fil og tilbys gjenopprettet ved neste åpning.
///  - Sikkerhetskopier: før et show lagres over, kopieres forrige versjon (de 20 siste beholdes).
/// Alt ligger i programmets egen mappe, ikke ved siden av showfila.
enum ShowSafety {
    static let backupsToKeep = 20

    static var defaultRoot: URL {
        (FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? URL(fileURLWithPath: NSHomeDirectory())).appendingPathComponent("ShowCue")
    }

    /// Autolagringsfila for et show (nil = ulagret show).
    static func autosaveURL(for show: URL?, root: URL = defaultRoot) -> URL {
        let dir = root.appendingPathComponent("Autolagring")
        guard let show else { return dir.appendingPathComponent("Uten navn.showcue") }
        let full = show.standardizedFileURL.path.lowercased()
        let hash = SHA256.hash(data: Data(full.utf8)).prefix(4).map { String(format: "%02X", $0) }.joined()
        return dir.appendingPathComponent("\(show.deletingPathExtension().lastPathComponent) \(hash).showcue")
    }

    static func writeAutosave(for show: URL?, data: Data, root: URL = defaultRoot) throws {
        let url = autosaveURL(for: show, root: root)
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try data.write(to: url, options: .atomic)
    }

    static func deleteAutosave(for show: URL?, root: URL = defaultRoot) {
        try? FileManager.default.removeItem(at: autosaveURL(for: show, root: root))
    }

    private static func modified(_ url: URL) -> Date? {
        (try? FileManager.default.attributesOfItem(atPath: url.path))?[.modificationDate] as? Date
    }

    /// Autolagring som er nyere enn selve showfila (= ulagrede endringer fra forrige gang), ellers nil.
    static func pendingAutosave(for show: URL?, root: URL = defaultRoot) -> URL? {
        let a = autosaveURL(for: show, root: root)
        guard let aDate = modified(a) else { return nil }
        guard let show, let sDate = modified(show) else { return a }
        return aDate > sDate ? a : nil
    }

    static func backupDir(root: URL = defaultRoot) -> URL { root.appendingPathComponent("Sikkerhetskopier") }

    private static let stamp: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = L("yyyy-MM-dd HH.mm.ss")
        return f
    }()

    /// Kopier showfila slik den er nå (før den lagres over). Beholder de `backupsToKeep` siste per show.
    @discardableResult
    static func backup(_ show: URL, root: URL = defaultRoot, now: Date = Date()) -> URL? {
        let fm = FileManager.default
        guard fm.fileExists(atPath: show.path) else { return nil }
        let dir = backupDir(root: root)
        try? fm.createDirectory(at: dir, withIntermediateDirectories: true)
        let name = show.deletingPathExtension().lastPathComponent
        let target = dir.appendingPathComponent("\(name) \(stamp.string(from: now)).showcue")
        try? fm.removeItem(at: target)
        guard (try? fm.copyItem(at: show, to: target)) != nil else { return nil }

        let prefix = name + " "
        let all = ((try? fm.contentsOfDirectory(atPath: dir.path)) ?? [])
            .filter { $0.hasPrefix(prefix) && $0.hasSuffix(".showcue") && $0.count == prefix.count + 19 + 8 }
            .sorted(by: >)
        for old in all.dropFirst(backupsToKeep) { try? fm.removeItem(at: dir.appendingPathComponent(old)) }
        return target
    }
}
