import Foundation

/// Mediefiler i et show: finn manglende filer, let dem opp i en mappe («relink»),
/// og samle showet og alle mediefilene i én mappe («collect»), f.eks. før showet flyttes til en annen maskin.
enum MediaTools {
    /// Alle mediefiler showet bruker (fil-cuer og spillelister), uten duplikater.
    static func mediaPaths(_ cues: [Cue]) -> [String] {
        var seen = Set<String>(), all: [String] = []
        for c in cues {
            var paths: [String] = []
            if c.isFileCue && !c.filePath.isEmpty { paths.append(c.filePath) }
            if c.usesFileList { paths += c.files.filter { !$0.isEmpty } }
            for p in paths where seen.insert(p.lowercased()).inserted { all.append(p) }
        }
        return all
    }

    static func missing(_ cues: [Cue]) -> [String] {
        mediaPaths(cues).filter { !FileManager.default.fileExists(atPath: $0) }
    }

    /// Let etter manglende filer (på filnavn, uten hensyn til store/små bokstaver) i `folder` og alle undermapper.
    /// Returnerer antall cuer/spor som ble funnet.
    @discardableResult
    static func relink(_ cues: [Cue], in folder: URL) -> Int {
        let fm = FileManager.default
        var index: [String: String] = [:]
        if let e = fm.enumerator(at: folder, includingPropertiesForKeys: [.isRegularFileKey], options: [.skipsHiddenFiles]) {
            for case let u as URL in e where (try? u.resourceValues(forKeys: [.isRegularFileKey]).isRegularFile) == true {
                let key = u.lastPathComponent.lowercased()
                if index[key] == nil { index[key] = u.path }
            }
        }
        func find(_ path: String) -> String? {
            guard !path.isEmpty, !fm.fileExists(atPath: path) else { return nil }
            return index[Cue.lastPathComponent(path).lowercased()]
        }
        var count = 0
        for c in cues {
            if c.isFileCue, let p = find(c.filePath) { c.filePath = p; count += 1 }
            if c.usesFileList {
                var files = c.files, changed = false
                for i in files.indices { if let q = find(files[i]) { files[i] = q; changed = true; count += 1 } }
                if changed { c.files = files }
            }
        }
        return count
    }

    struct CollectResult { let showURL: URL; let copied: Int; let missing: [String] }

    /// Kopierer showet til `folder`/<navn>.showcue og alle mediefilene til `folder`/Media.
    /// Kopien peker på de nye filene; showet som er åpent endres ikke.
    /// Filer med samme navn fra ulike mapper får « (2)», « (3)» … i navnet.
    static func collect(_ doc: ShowDocument, to folder: URL, name: String) throws -> CollectResult {
        let fm = FileManager.default
        let media = folder.appendingPathComponent("Media", isDirectory: true)
        try fm.createDirectory(at: media, withIntermediateDirectories: true)
        var copy = try JSONDecoder().decode(ShowDocument.self, from: doc.jsonData())

        var map: [String: String] = [:], used = Set<String>(), missing: [String] = [], copied = 0
        func place(_ src: String) throws -> String {
            if let done = map[src] { return done }
            guard fm.fileExists(atPath: src) else { missing.append(src); map[src] = src; return src }
            let u = URL(fileURLWithPath: src)
            let stem = u.deletingPathExtension().lastPathComponent, ext = u.pathExtension
            func file(_ n: String) -> URL { media.appendingPathComponent(ext.isEmpty ? n : "\(n).\(ext)") }
            var target = file(stem), n = 2
            while used.contains(target.path.lowercased()) { target = file("\(stem) (\(n))"); n += 1 }
            used.insert(target.path.lowercased())
            if u.standardizedFileURL.path != target.standardizedFileURL.path {
                try? fm.removeItem(at: target)
                try fm.copyItem(at: u, to: target)
            }
            copied += 1
            map[src] = target.path
            return target.path
        }
        for c in copy.cues {
            if c.isFileCue && !c.filePath.isEmpty { c.filePath = try place(c.filePath) }
            if c.usesFileList { c.files = try c.files.map { $0.isEmpty ? $0 : try place($0) } }
        }
        copy.title = name
        let url = folder.appendingPathComponent("\(name).showcue")
        try copy.save(to: url)
        return CollectResult(showURL: url, copied: copied, missing: missing)
    }
}
