import SwiftUI
import AppKit

/// «Showlogg»: viser hendelsene i denne kjøringen fortløpende (nyeste nederst), med filter, og eksporterer til CSV eller tekst.
/// Kan også åpne en eldre loggfil. Eksporten tar med det som vises (etter filteret).
struct ShowLogView: View {
    let log: ShowLog
    let root: URL

    var body: some View {
        Themed { ShowLogContent(log: log, root: root) }
    }
}

private struct ShowLogContent: View {
    let log: ShowLog
    let root: URL
    @Environment(\.theme) var theme
    @State private var events: [ShowLogEvent] = []
    @State private var lastSeq = 0
    @State private var category: ShowLogCategory = .all
    @State private var follow = true
    @State private var viewingFile: URL?
    @State private var sourceText = ""
    @State private var infoText = ""
    private let timer = Timer.publish(every: 0.3, on: .main, in: .common).autoconnect()
    private static let maxEvents = 5000

    private var visible: [ShowLogEvent] { events.filter { ShowLogFormat.matches($0, category) } }

    private static let categories: [(label: String, value: ShowLogCategory)] = [
        (L("Alle hendelser"), .all),
        (L("Bare feil og advarsler"), .problems),
        (L("Cuer (start og stopp)"), .cues),
        (L("Styring (pause, stopp alt, markør)"), .control),
        (L("Show og program"), .show),
    ]

    var body: some View {
        let rows = visible
        VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 12) {
                Picker("", selection: $category) {
                    ForEach(Array(Self.categories.enumerated()), id: \.offset) { _, c in Text(c.label).tag(c.value) }
                }
                .labelsHidden().frame(width: 260)
                Toggle(L("Følg nyeste"), isOn: $follow)
                Button(L("Åpne eldre logg")) { chooseFile() }
                Button(L("Gjeldende kjøring")) { viewingFile = nil; reload() }.disabled(viewingFile == nil)
                Spacer()
            }
            .padding(12)
            Rectangle().fill(theme.line).frame(height: 1)
            header
            ScrollViewReader { proxy in
                ScrollView {
                    LazyVStack(alignment: .leading, spacing: 0) {
                        ForEach(Array(rows.enumerated()), id: \.offset) { i, e in row(e).id(i) }
                    }
                    .padding(.horizontal, 12)
                }
                .onChange(of: rows.count) { n in
                    if follow, n > 0 { proxy.scrollTo(n - 1, anchor: .bottom) }
                }
            }
            Rectangle().fill(theme.line).frame(height: 1)
            VStack(alignment: .leading, spacing: 2) {
                Text(sourceText).uiFont(12).lineLimit(1).truncationMode(.middle)
                Text(infoText).uiFont(12).foregroundColor(theme.muted)
            }
            .padding(.horizontal, 12).padding(.top, 6)
            HStack {
                Spacer()
                Button(L("Eksporter CSV")) { export(csv: true, rows) }
                Button(L("Eksporter tekst")) { export(csv: false, rows) }
            }
            .padding(12)
        }
        .frame(minWidth: 760, minHeight: 320)
        .background(theme.bg)
        .onAppear { reload() }
        .onReceive(timer) { _ in poll() }
        .onChange(of: category) { _ in updateInfo() }
    }

    private var header: some View {
        HStack(spacing: 8) {
            cell(L("Tid"), 96); cell(L("Siden start"), 96); cell(L("Hendelse"), 110); cell(L("Cue"), 46)
            cell(L("Navn"), 150); cell(L("Type"), 64); cell(L("Hvordan"), 96)
            Text(L("Tekst")).uiFont(12, weight: .semibold).frame(maxWidth: .infinity, alignment: .leading)
        }
        .padding(.horizontal, 12).padding(.vertical, 5)
    }

    private func cell(_ s: String, _ w: CGFloat) -> some View {
        Text(s).uiFont(12, weight: .semibold).frame(width: w, alignment: .leading)
    }

    private func row(_ e: ShowLogEvent) -> some View {
        let color: Color = e.kind == .error ? theme.danger : (e.kind == .warning || e.kind == .dropped ? theme.wait : theme.text)
        return HStack(spacing: 8) {
            Text(Self.localTime(e.time)).frame(width: 96, alignment: .leading)
            Text(ShowLogFormat.runText(e.runMs)).frame(width: 96, alignment: .leading)
            Text(Self.label(e.kind)).frame(width: 110, alignment: .leading)
            Text(e.number).frame(width: 46, alignment: .leading)
            Text(e.name).lineLimit(1).frame(width: 150, alignment: .leading)
            Text(e.cueType).frame(width: 64, alignment: .leading)
            Text(e.how).frame(width: 96, alignment: .leading)
            Text(e.text).lineLimit(1).frame(maxWidth: .infinity, alignment: .leading)
        }
        .uiFont(12, design: .monospaced)
        .foregroundColor(color)
        .padding(.vertical, 2)
    }

    private static func localTime(_ t: ShowLogTime) -> String {
        // Tiden i tidssonen hendelsen ble logget i: de siste 12 tegnene før sonen i ISO-teksten (TT:MM:SS.mmm).
        let iso = ShowLogFormat.iso(t)
        return String(iso.dropFirst(11).prefix(12))
    }

    private static func label(_ k: ShowLogKind) -> String {
        switch k {
        case .start: return L("Start")
        case .finish: return L("Ferdig")
        case .stop: return L("Stoppet")
        case .pause: return L("Pause")
        case .resume: return L("Fortsett")
        case .stopAll: return L("Stopp alt")
        case .playhead: return L("Markør")
        case .warning: return L("Advarsel")
        case .error: return L("Feil")
        case .showOpened: return L("Show åpnet")
        case .showSaved: return L("Show lagret")
        case .appStart: return L("Program startet")
        case .appExit: return L("Program avsluttet")
        case .dropped: return L("Mistet")
        }
    }

    /// Henter alt på nytt (ved start eller bytte mellom nåværende kjøring og en fil).
    private func reload() {
        if let file = viewingFile {
            do {
                let r = try ShowLogExport.readFile(file)
                events = Array(r.events.suffix(Self.maxEvents))
                sourceText = L("Logg: {0}", file.path) + (r.skipped > 0 ? L(" ({0} linjer kunne ikke leses)", r.skipped) : "")
            } catch {
                viewingFile = nil
                alert(L("Kunne ikke lese loggen: {0}", error.localizedDescription))
                reload()
                return
            }
        } else {
            let r = log.readSince(0)
            events = Array(r.events.suffix(Self.maxEvents))
            lastSeq = r.last
            sourceText = log.filePath.map { L("Gjeldende kjøring – {0}", $0) } ?? L("Gjeldende kjøring")
        }
        updateInfo()
    }

    /// Henter nye hendelser fra den løpende loggen og legger dem nederst.
    private func poll() {
        guard viewingFile == nil else { return }
        let r = log.readSince(lastSeq)
        lastSeq = r.last
        if !r.events.isEmpty {
            events.append(contentsOf: r.events)
            if events.count > Self.maxEvents { events.removeFirst(events.count - Self.maxEvents) }
        }
        updateInfo()
    }

    private func updateInfo() {
        var text = L("{0} hendelser vises av {1}.", visible.count, events.count)
        if viewingFile == nil, log.dropped > 0 { text += " " + L("{0} hendelser ble kastet fordi køen var full.", log.dropped) }
        if viewingFile == nil, let f = log.fault { text += " " + L("Skriving til fil feilet: {0}", f) }
        if text != infoText { infoText = text }
    }

    private func alert(_ text: String) {
        let a = NSAlert()
        a.messageText = text
        a.runModal()
    }

    private func chooseFile() {
        let p = NSOpenPanel()
        p.allowsMultipleSelection = false
        let dir = ShowLogFiles.directory(root: root)
        if FileManager.default.fileExists(atPath: dir.path) { p.directoryURL = dir }
        guard p.runModal() == .OK, let url = p.url else { return }
        if let size = (try? FileManager.default.attributesOfItem(atPath: url.path))?[.size] as? Int, size > 50 * 1024 * 1024 {
            alert(L("Fila er for stor til å åpnes som logg."))
            return
        }
        viewingFile = url
        reload()
    }

    private func export(csv: Bool, _ rows: [ShowLogEvent]) {
        let p = NSSavePanel()
        p.nameFieldStringValue = (viewingFile?.deletingPathExtension().lastPathComponent ?? "showlog") + (csv ? ".csv" : ".txt")
        guard p.runModal() == .OK, let url = p.url else { return }
        do {
            var data = Data()
            // CSV får byte order mark, så regneark leser æøå riktig.
            if csv { data.append(contentsOf: [0xEF, 0xBB, 0xBF]) }
            data.append(Data((csv ? ShowLogExport.toCSV(rows) : ShowLogExport.toText(rows)).utf8))
            try data.write(to: url, options: .atomic)
            infoText = L("Eksporterte {0} hendelser til {1}", rows.count, url.path)
        } catch {
            alert(L("Kunne ikke eksportere: {0}", error.localizedDescription))
        }
    }
}
