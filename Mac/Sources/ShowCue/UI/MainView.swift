import SwiftUI

struct MainView: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.theme) var theme

    var body: some View {
        VStack(spacing: 0) {
            TopBar()
            ToolbarRow()
            HSplitView {
                CueListView()
                    .environment(\.uiScale, zoom * CGFloat(model.settings.listTextScale))
                    .frame(minWidth: 520, maxWidth: .infinity, maxHeight: .infinity)
                InspectorView()
                    .frame(minWidth: 300, idealWidth: 360, maxWidth: 480, maxHeight: .infinity)
            }
            .frame(maxHeight: .infinity)
            Rectangle().fill(theme.bg).frame(height: 5)
            BottomPanels()
                .frame(height: 230 * zoom)
            StatusBar()
        }
        .environment(\.uiScale, zoom)
        .controlSize(zoom >= 1.25 ? .large : zoom < 0.95 ? .small : .regular)
        .background(theme.bg)
        .navigationTitle(model.windowTitle)
    }

    private var zoom: CGFloat { CGFloat(model.settings.zoom) }
}

// MARK: - Toppfelt med GO

struct TopBar: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.theme) var theme

    var body: some View {
        HStack(spacing: 18) {
            Button(action: model.go) {
                Text("GO").uiFont(30, weight: .bold).foregroundColor(theme.goText)
                    .frame(width: 170, height: 92)
                    .background(RoundedRectangle(cornerRadius: 6).fill(theme.go))
            }
            .buttonStyle(.plain)
            .help(L("Mellomrom"))

            NextCueInfo(cue: model.playhead)
                .frame(maxWidth: .infinity, alignment: .leading)
                .overlay(alignment: .leading) { if !model.memoText.isEmpty { MemoBanner() } }

            VStack(alignment: .trailing, spacing: 2) {
                ClockText(live: model.live)
                let n = model.engine.active.count
                Text(n == 0 ? " " : L("{0} aktive", n)).uiFont(12).foregroundColor(theme.muted)
            }
            MeterView(live: model.live)
                .frame(width: 26, height: 84)
                .help(L("Utgangsnivå, utgang 1 og 2 (−60 til 0 dBFS)"))
            Button(action: model.togglePause) {
                VStack(spacing: 2) {
                    Text(model.engine.isPaused ? "▶" : "⏸").uiFont(22)
                    Text(model.engine.isPaused ? L("Fortsett") : L("Pause")).uiFont(12)
                }
                .frame(width: 92, height: 92)
                .background(RoundedRectangle(cornerRadius: 6).fill(model.engine.isPaused ? theme.wait.opacity(0.35) : theme.panel3))
                .overlay(RoundedRectangle(cornerRadius: 6).stroke(theme.line))
            }
            .buttonStyle(.plain)
            .help(L("Pause / fortsett alt (⌘P). GO fortsetter også."))
            Button(action: model.panic) {
                VStack(spacing: 2) {
                    Text(L("STOPP ALT")).uiFont(17, weight: .semibold)
                    Text(L("Esc")).uiFont(11).opacity(0.8)
                }
                .foregroundColor(.white)
                .frame(width: 130, height: 92)
                .background(RoundedRectangle(cornerRadius: 6).fill(theme.danger))
            }
            .buttonStyle(.plain)
            .help(L("Stopp alt med kort fade. Trykk to ganger for umiddelbar stopp."))
        }
        .padding(.horizontal, 14).padding(.vertical, 12)
        .background(theme.panel)
        .overlay(Rectangle().fill(theme.line).frame(height: 1), alignment: .bottom)
    }
}

/// Beskjeden fra en memo-cue, over «neste cue» til den lukkes.
struct MemoBanner: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.theme) var theme

    var body: some View {
        HStack(alignment: .top, spacing: 10) {
            VStack(alignment: .leading, spacing: 2) {
                Text(L("BESKJED")).uiFont(11, weight: .semibold).foregroundColor(theme.wait)
                Text(model.memoText).uiFont(22, weight: .semibold).lineLimit(2)
            }
            Spacer(minLength: 0)
            Button("✕") { model.closeMemo() }.buttonStyle(PanelButtonStyle()).help(L("Lukk beskjeden"))
        }
        .padding(.horizontal, 14).padding(.vertical, 8)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 6).fill(theme.panel3))
        .overlay(RoundedRectangle(cornerRadius: 6).stroke(theme.wait, lineWidth: 2))
    }
}

struct NextCueInfo: View {
    @Environment(\.theme) var theme
    let cue: Cue?

    var body: some View {
        if let c = cue {
            NextCueContent(cue: c)
        } else {
            VStack(alignment: .leading, spacing: 2) {
                SectionHeader(L("NESTE CUE"))
                Text(L("— slutt på lista —")).uiFont(26, weight: .semibold).foregroundColor(theme.muted)
            }
        }
    }
}

private struct NextCueContent: View {
    @Environment(\.theme) var theme
    @ObservedObject var cue: Cue

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            SectionHeader(L("NESTE CUE"))
            HStack(alignment: .firstTextBaseline, spacing: 14) {
                Text(cue.number).uiFont(26, weight: .bold, design: .monospaced).foregroundColor(theme.playhead)
                Text(cue.displayName).uiFont(26, weight: .semibold).lineLimit(1)
            }
            Text(cue.summary).uiFont(13).foregroundColor(theme.muted).lineLimit(1)
            if !cue.notes.isEmpty {
                Text(cue.notes).uiFont(13).foregroundColor(theme.wait).lineLimit(1)
            }
        }
    }
}

struct ClockText: View {
    @ObservedObject var live: LiveState
    var body: some View { Text(live.clockText.isEmpty ? "--:--:--" : live.clockText).uiFont(30, design: .monospaced) }
}

struct MeterView: View {
    @ObservedObject var live: LiveState
    @Environment(\.theme) var theme

    var body: some View {
        HStack(spacing: 3) {
            bar(live.meterL)
            bar(live.meterR)
        }
    }

    private func bar(_ v: Float) -> some View {
        GeometryReader { g in
            ZStack(alignment: .bottom) {
                RoundedRectangle(cornerRadius: 2).fill(theme.panel3)
                LinearGradient(stops: [.init(color: Color(hex: 0x25B566), location: 0), .init(color: Color(hex: 0x25B566), location: 0.75),
                                       .init(color: Color(hex: 0xE3A33A), location: 0.86), .init(color: Color(hex: 0xE5484D), location: 0.97)],
                               startPoint: .bottom, endPoint: .top)
                    .frame(height: g.size.height)
                    .mask(alignment: .bottom) { Rectangle().frame(height: g.size.height * CGFloat(v)) }
                    .clipShape(RoundedRectangle(cornerRadius: 2))
            }
        }
    }
}

// MARK: - Verktøylinje

struct ToolbarRow: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.theme) var theme
    @Environment(\.openWindow) var openWindow

    var body: some View {
        HStack(spacing: 0) {
            menu(L("Fil")) {
                Button(L("Nytt show")) { model.newShow() }
                Button(L("Åpne")) { model.openDialog() }
                Button(L("Lagre")) { model.save(as: false) }
                Button(L("Lagre som")) { model.save(as: true) }
                Divider()
                Button(L("Samle show og mediefiler i én mappe")) { model.collectShow() }
                Button(L("Finn manglende mediefiler")) { model.relinkMenu() }.disabled(model.showMode)
            }
            Button("↶") { model.undo() }.disabled(!model.canUndo).help(L("Angre (⌘Z)"))
            Button("↷") { model.redo() }.disabled(!model.canRedo).help(L("Gjør om (⇧⌘Z)"))
            divider
            Group {
                menu(L("＋ Media")) {
                    Button(L("♪  Lyd (⌘1)")) { model.add(.audio) }
                    Button(L("▶  Video (⌘2)")) { model.add(.video) }
                    Button(L("▣  Bilde (⌘3)")) { model.add(.image) }
                    Button(L("▤  PDF (⌘4)")) { model.add(.pdf) }
                    Button(L("◍  Nettside (⇧⌘W)")) { model.add(.web) }
                    Divider()
                    Button(L("☰  Spilleliste (⌘9)")) { model.add(.playlist) }
                    Button(L("◉  Live-inngang (⇧⌘I)")) { model.add(.input) }
                }
                .help(L("Legg til lyd, video, bilde, PDF, nettside og mer"))
                menu(L("Styring")) {
                    Button(L("◢  Fade – valgt cue (⌘5)")) { model.add(.fade) }
                    Button(L("■  Stopp (⌘6)")) { model.add(.stop) }
                    Button(L("↪  Gå til (⌘0)")) { model.add(.goto) }
                    Button(L("✎  Beskjed (⇧⌘M)")) { model.add(.memo) }
                }
                .help(L("Fade, stopp, gå til og beskjed"))
                menu(L("Utstyr")) {
                    Button(L("⇢  OSC (⌘7)")) { model.add(.osc) }
                    Button(L("♫  MIDI (⌘8)")) { model.add(.midi) }
                    Button(L("⇄  Nettverk – UDP/TCP/HTTP (⇧⌘N)")) { model.add(.network) }
                    Button(L("⏱  Tidskode – MTC/LTC (⌥⌘T)")) { model.add(.timecode) }
                    Button(L("✺  Lys – DMX/Art-Net (⇧⌘L)")) { model.add(.light) }
                }
                .help(L("Styr annet utstyr: lys, miksere, videoservere og andre programmer (OSC, MIDI, nettverk, tidskode, DMX)"))
                menu(L("Rediger")) {
                    Button(L("Dupliser (⌘D)")) { model.duplicateSelected() }
                    Button(L("Slett (⌫)")) { model.deleteSelected() }
                    Button(L("Flytt opp (⌘↑)")) { model.moveSelected(-1) }
                    Button(L("Flytt ned (⌘↓)")) { model.moveSelected(1) }
                    Button(L("Nummerer alle (1, 2, 3)")) { model.renumber() }
                    Divider()
                    Button(L("Normaliser lydstyrke for valgte lyd-cuer")) { model.normalizeSelected() }
                    Button(L("Normaliser lydstyrke for alle lyd-cuer")) { model.normalizeAll() }
                }
            }
            .disabled(model.showMode)
            Spacer(minLength: 8)
            Button(L("▦ Knappepanel")) { model.showButtonPanel() }.help(L("Store knapper som starter og stopper cuer direkte, uten å flytte GO-markøren – for jingler, lydeffekter og berøringsskjerm (⌘K)"))
            Button(model.showMode ? L("🔓 Lås opp") : L("🔒 Showmodus")) { model.showMode.toggle() }
                .help(L("Lås redigering under forestillingen: bare GO, stopp og hurtigtaster virker (⌘L)"))
            menu(L("Oppsett")) {
                Button(L("Lydpatch")) { openWindow(id: "patch") }
                Button(L("Vis/skjul utgangsvinduet (⇧⌘U)")) { model.output.toggle() }
                SettingsButton()
                Button(L("Bytt tema {0} (⇧⌘T)", themeGlyph)) { model.settings.theme = model.settings.theme.next }
                Divider()
                Button(L("Zoom inn (⌘+)")) { model.zoom(1) }
                Button(L("Zoom ut (⌘−)")) { model.zoom(-1) }
                Button(L("Vanlig størrelse (nå {0})", UIZoom.percent(model.settings.zoom))) { model.zoom(0) }
                Picker(L("Tekst i cuelista"), selection: $model.settings.listTextScale) {
                    ForEach(UIZoom.listSizes.indices, id: \.self) { i in Text(UIZoom.listSizes[i].label).tag(UIZoom.listSizes[i].scale) }
                }
                Divider()
                Button(L("Om ShowCue")) { AboutPanel.show() }
            }
        }
        .buttonStyle(ToolButtonStyle())
        .lineLimit(1)
        .padding(.horizontal, 8).padding(.vertical, 5)
        .background(theme.panel)
        .overlay(Rectangle().fill(theme.line).frame(height: 1), alignment: .bottom)
    }

    /// Nedtrekksmeny i verktøylinja.
    private func menu<Content: View>(_ title: String, @ViewBuilder _ content: () -> Content) -> some View {
        Menu(title, content: content)
            .menuStyle(.borderlessButton)
            .fixedSize()
            .uiFont(12.5)
            .padding(.horizontal, 6)
    }

    private var themeGlyph: String {
        switch model.settings.theme {
        case .dark: return "☀︎"
        case .light: return "☾"
        case .night: return "◐"
        }
    }

    private var divider: some View {
        Rectangle().fill(theme.line).frame(width: 1, height: 20).padding(.horizontal, 8)
    }
}

/// «Innstillinger» – SettingsLink på macOS 14+, ellers menykommandoen.
struct SettingsButton: View {
    var body: some View {
        if #available(macOS 14.0, *) {
            SettingsLink { Text(L("Innstillinger")) }
        } else {
            Button(L("Innstillinger")) {
                NSApp.sendAction(Selector(("showSettingsWindow:")), to: nil, from: nil)
            }
        }
    }
}

// MARK: - Statuslinje

struct StatusBar: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.theme) var theme

    var body: some View {
        HStack {
            Text(model.statusText).foregroundColor(theme.muted).lineLimit(1).truncationMode(.tail)
            Spacer(minLength: 16)
            Text(model.remoteStatus).foregroundColor(theme.faint).lineLimit(1)
                .help(L("Mellomrom = GO · Esc = stopp alt · ↑↓ = flytt markør · dra filer inn i lista"))
        }
        .uiFont(11.5)
        .padding(.horizontal, 12).padding(.vertical, 4)
        .background(theme.panel)
        .overlay(Rectangle().fill(theme.line).frame(height: 1), alignment: .top)
    }
}
