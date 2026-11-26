import SwiftUI
import UniformTypeIdentifiers

/// Kolonnebredder (samme som Windows-versjonen) ved vanlig størrelse. Ganges med zoom.
private struct Col {
    let status, number, type, preWait, length, remaining, cont, trigger: CGFloat
    init(_ s: CGFloat) {
        status = 30 * s; number = 60 * s; type = 122 * s
        preWait = 76 * s; length = 76 * s; remaining = 80 * s; cont = 28 * s; trigger = 104 * s
    }
}

struct CueListView: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.theme) var theme
    @Environment(\.uiScale) var scale
    @State private var dropTargeted = false

    var body: some View {
        VStack(spacing: 0) {
            SceneBar()
            header
            ScrollViewReader { proxy in
                ScrollView {
                    LazyVStack(spacing: 0) {
                        ForEach(Array(model.visibleCues.enumerated()), id: \.element.id) { i, cue in
                            CueRow(cue: cue, index: i, selected: model.selection.contains(cue.id))
                                .id(cue.id)
                                .onDrop(of: [.fileURL], isTargeted: nil) { providers in
                                    loadURLs(providers) { model.dropFiles($0, on: cue, index: model.index(of: cue) ?? i) }
                                    return true
                                }
                        }
                    }
                }
                .onChange(of: model.selection) { sel in
                    if let c = model.playhead, sel.count == 1 { withAnimation(.easeOut(duration: 0.12)) { proxy.scrollTo(c.id) } }
                }
            }
            .overlay {
                if model.visibleCues.isEmpty {
                    VStack(spacing: 6) {
                        Text(L("Dra lyd-, video-, bilde- eller PDF-filer hit")).uiFont(18).foregroundColor(theme.muted)
                        Text(L("eller bruk «＋ Media» i verktøylinja")).uiFont(13).foregroundColor(theme.faint)
                    }
                    .allowsHitTesting(false)
                }
            }
            .overlay(RoundedRectangle(cornerRadius: 4).stroke(theme.playhead, lineWidth: dropTargeted ? 2 : 0))
            .onDrop(of: [.fileURL], isTargeted: $dropTargeted) { providers in
                loadURLs(providers) { model.addFiles($0) }
                return true
            }
        }
        .background(theme.panel)
    }

    private var header: some View {
        let col = Col(scale)
        return HStack(spacing: 0) {
            Text("").frame(width: col.status)
            h(L("NR")).frame(width: col.number, alignment: .leading)
            h(L("TYPE")).frame(width: col.type, alignment: .leading)
            h(L("NAVN")).frame(maxWidth: .infinity, alignment: .leading)
            h(L("MÅL / FIL")).frame(maxWidth: .infinity, alignment: .leading)
            h(L("PRE-WAIT")).frame(width: col.preWait, alignment: .leading)
            h(L("LENGDE")).frame(width: col.length, alignment: .leading)
            h(L("GJENSTÅR")).frame(width: col.remaining, alignment: .leading)
            Text("").frame(width: col.cont)
            h(L("TRIGGER")).frame(width: col.trigger, alignment: .leading)
        }
        .padding(.leading, 3)
        .padding(.vertical, 7)
        .overlay(Rectangle().fill(theme.line).frame(height: 1), alignment: .bottom)
    }

    private func h(_ s: String) -> some View {
        Text(s).uiFont(11, weight: .semibold).foregroundColor(theme.muted).padding(.horizontal, 8)
    }

    private func loadURLs(_ providers: [NSItemProvider], then: @escaping ([URL]) -> Void) {
        let group = DispatchGroup()
        var urls: [URL] = []
        let lock = NSLock()
        for p in providers where p.hasItemConformingToTypeIdentifier(UTType.fileURL.identifier) {
            group.enter()
            p.loadItem(forTypeIdentifier: UTType.fileURL.identifier, options: nil) { item, _ in
                defer { group.leave() }
                var url: URL?
                if let d = item as? Data { url = URL(dataRepresentation: d, relativeTo: nil) }
                else if let u = item as? URL { url = u }
                if let u = url { lock.lock(); urls.append(u); lock.unlock() }
            }
        }
        group.notify(queue: .main) { then(urls) }
    }
}

struct CueRow: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.theme) var theme
    @Environment(\.uiScale) var scale
    @ObservedObject var cue: Cue
    let index: Int
    let selected: Bool

    /// Spilles nå, neste (GO-markøren), deaktivert eller vanlig – se `CueDisplay.rowState`.
    private var state: CueRowState {
        CueDisplay.rowState(isRunning: cue.isRunning, isNext: model.playhead === cue, disabled: cue.disabled)
    }

    private var background: Color {
        switch state {
        case .playing: return theme.runningRow
        case .next: return theme.playheadRow
        case .disabled, .normal:
            // Valgt, men ikke neste (flere valgt, eller deaktivert): nøytral markering.
            if selected { return theme.panel3 }
            if let t = theme.tint(cue.color) { return t }
            return index % 2 == 1 ? theme.rowAlt : theme.panel
        }
    }

    private var stripe: Color {
        switch state {
        case .playing: return theme.running
        case .next: return theme.playhead
        case .disabled, .normal: return selected ? theme.faint : (theme.stripe(cue.color) ?? .clear)
        }
    }

    var body: some View {
        let col = Col(scale)
        return HStack(spacing: 0) {
            ZStack {
                if cue.isRunning { Circle().fill(theme.running).frame(width: 9, height: 9) }
                if cue.isPreWaiting { Circle().fill(theme.wait).frame(width: 9, height: 9) }
                if state == .next { Text("▶").uiFont(11).foregroundColor(theme.playhead) }
                if let e = cue.error {
                    Text("!").uiFont(15, weight: .bold).foregroundColor(theme.danger).help(e)
                }
            }
            .frame(width: col.status)
            Text(cue.number).uiFont(13.5, weight: .semibold, design: .monospaced)
                .padding(.horizontal, 8).frame(width: col.number, alignment: .leading)
            HStack(spacing: 2) {
                Text(cue.typeIcon).frame(width: 16 * scale)
                Text(cue.typeName).uiFont(12).lineLimit(1)
            }
            .foregroundColor(theme.muted).padding(.horizontal, 8).frame(width: col.type, alignment: .leading)
            Text(cue.displayName).lineLimit(1).padding(.horizontal, 8)
                .padding(.trailing, cue.skipInRehearsal ? 34 : 0)
                .frame(maxWidth: .infinity, alignment: .leading)
                .overlay(alignment: .trailing) {
                    if cue.skipInRehearsal {
                        Text(L("ØV")).uiFont(10, weight: .bold).foregroundColor(theme.wait)
                            .padding(.horizontal, 3)
                            .overlay(RoundedRectangle(cornerRadius: 3).stroke(theme.wait))
                            .padding(.trailing, 6)
                            .help(L("Hoppes over i øvelsesmodus"))
                    }
                }
                .overlay(alignment: .bottom) {
                    if cue.isRunning {
                        ProgressBar(value: cue.progress, height: 4).padding(.horizontal, 8).offset(y: 9)
                    }
                }
            Text(cue.summary).foregroundColor(theme.muted).lineLimit(1).padding(.horizontal, 8)
                .frame(maxWidth: .infinity, alignment: .leading)
            Text(cue.preWaitText).uiFont(13, design: .monospaced).foregroundColor(theme.wait)
                .padding(.horizontal, 8).frame(width: col.preWait, alignment: .leading)
            Text(cue.durationText).uiFont(13, design: .monospaced)
                .padding(.horizontal, 8).frame(width: col.length, alignment: .leading)
            Text(cue.remainingText).uiFont(13, weight: .semibold, design: .monospaced).foregroundColor(theme.running)
                .padding(.horizontal, 8).frame(width: col.remaining, alignment: .leading)
            Text(cue.continueMode.glyph).uiFont(16).foregroundColor(theme.playhead)
                .frame(width: col.cont)
            Text(cue.triggerText).uiFont(12.5, design: .monospaced).foregroundColor(theme.wait).lineLimit(1)
                .padding(.horizontal, 8).frame(width: col.trigger, alignment: .leading)
        }
        .uiFont(13.5)
        .frame(height: 34 * scale)
        .background(background)
        .overlay(Rectangle().fill(stripe).frame(width: 4), alignment: .leading)
        .overlay(Rectangle().fill(theme.gridLine).frame(height: 1), alignment: .bottom)
        .opacity(state == .disabled ? 0.4 : 1)
        .contentShape(Rectangle())
        .onTapGesture {
            NSApp.keyWindow?.makeFirstResponder(nil)
            let flags = NSEvent.modifierFlags
            model.click(cue, command: flags.contains(.command), shift: flags.contains(.shift))
            // Dobbeltklikk: test cuen uten å flytte GO-markøren.
            if NSApp.currentEvent?.clickCount == 2 { model.fire(cue) }
        }
        .contextMenu {
            Button(L("Test (spill nå)")) { model.fire(cue) }
            Button(L("Stopp")) { model.stop(cue) }
            Divider()
            Menu(L("Farge")) {
                ForEach(CueColor.allCases, id: \.self) { c in Button(c.label) { cue.color = c } }
            }
            Button(cue.disabled ? L("Aktiver") : L("Deaktiver")) { cue.disabled.toggle() }
            Button(cue.skipInRehearsal ? L("Ikke hopp over i øvelse") : L("Hopp over i øvelse")) { cue.skipInRehearsal.toggle() }
            Button(L("Dupliser")) { model.select(cue); model.duplicateSelected() }
            if model.sceneItems.count > 1 {
                Menu(L("Flytt til scene")) {
                    ForEach(model.sceneItems.filter { !SceneLogic.sameScene($0, model.activeSceneLabel) }, id: \.self) { name in
                        Button(name) { if !model.selection.contains(cue.id) { model.select(cue) }; model.moveSelectedToScene(name) }
                    }
                }
            }
        }
    }
}

struct ProgressBar: View {
    @Environment(\.theme) var theme
    let value: Double
    var height: CGFloat = 6

    var body: some View {
        GeometryReader { g in
            ZStack(alignment: .leading) {
                Capsule().fill(theme.panel3)
                Capsule().fill(theme.running).frame(width: g.size.width * CGFloat(min(1, max(0, value))))
            }
        }
        .frame(height: height)
    }
}

// MARK: - Scenevelger

/// Velger scene over cuelista (hver scene er en egen cueliste med egen nummerering og GO-markør), med menyen for å lage, gi nytt navn, slette og flytte.
struct SceneBar: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.theme) var theme

    var body: some View {
        HStack(spacing: 6) {
            Text(L("Scene")).foregroundColor(theme.muted)
            Button("◀") { model.stepScene(-1) }.help(L("Forrige scene (⌘⌥←)"))
            Picker("", selection: Binding(get: { model.activeSceneLabel }, set: { model.selectSceneLabel($0) })) {
                ForEach(model.sceneItems, id: \.self) { Text($0).tag($0) }
            }
            .labelsHidden()
            .frame(minWidth: 170, maxWidth: 260)
            .help(L("Velg scene. GO bytter aldri scene av seg selv."))
            Button("▶") { model.stepScene(1) }.help(L("Neste scene (⌘⌥→)"))
            Menu(L("Scener")) {
                Button(L("Ny scene")) { model.addSceneDialog() }
                Button(L("Gi scenen nytt navn")) { model.renameSceneDialog() }
                Button(L("Slett scenen")) { model.deleteSceneDialog() }
                Divider()
                Button(L("Flytt scenen mot starten")) { model.moveActiveScene(-1) }
                Button(L("Flytt scenen mot slutten")) { model.moveActiveScene(1) }
                Menu(L("Flytt valgte cuer til scene")) {
                    ForEach(model.sceneItems.filter { !SceneLogic.sameScene($0, model.activeSceneLabel) }, id: \.self) { name in
                        Button(name) { model.moveSelectedToScene(name) }
                    }
                }
            }
            .fixedSize()
            Spacer()
        }
        .uiFont(13)
        .padding(.horizontal, 8).padding(.vertical, 5)
        .background(theme.panel)
    }
}
