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
                    .frame(minWidth: 520, maxWidth: .infinity, maxHeight: .infinity)
                InspectorView()
                    .frame(minWidth: 300, idealWidth: 360, maxWidth: 480, maxHeight: .infinity)
            }
            .frame(maxHeight: .infinity)
            Rectangle().fill(theme.bg).frame(height: 5)
            BottomPanels()
                .frame(height: 230)
            StatusBar()
        }
        .background(theme.bg)
        .navigationTitle(model.windowTitle)
    }
}

// MARK: - Toppfelt med GO

struct TopBar: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.theme) var theme

    var body: some View {
        HStack(spacing: 18) {
            Button(action: model.go) {
                Text("GO").font(.system(size: 30, weight: .bold)).foregroundColor(theme.goText)
                    .frame(width: 170, height: 92)
                    .background(RoundedRectangle(cornerRadius: 6).fill(theme.go))
            }
            .buttonStyle(.plain)
            .help("Mellomrom")

            NextCueInfo(cue: model.playhead)
                .frame(maxWidth: .infinity, alignment: .leading)
                .overlay(alignment: .leading) { if !model.memoText.isEmpty { MemoBanner() } }

            VStack(alignment: .trailing, spacing: 2) {
                ClockText(live: model.live)
                let n = model.engine.active.count
                Text(n == 0 ? " " : "\(n) aktive").font(.system(size: 12)).foregroundColor(theme.muted)
            }
            MeterView(live: model.live)
                .frame(width: 26, height: 84)
                .help("Utgangsnivå, utgang 1 og 2 (−60 til 0 dBFS)")
            Button(action: model.togglePause) {
                VStack(spacing: 2) {
                    Text(model.engine.isPaused ? "▶" : "⏸").font(.system(size: 22))
                    Text(model.engine.isPaused ? "Fortsett" : "Pause").font(.system(size: 12))
                }
                .frame(width: 92, height: 92)
                .background(RoundedRectangle(cornerRadius: 6).fill(model.engine.isPaused ? theme.wait.opacity(0.35) : theme.panel3))
                .overlay(RoundedRectangle(cornerRadius: 6).stroke(theme.line))
            }
            .buttonStyle(.plain)
            .help("Pause / fortsett alt (⌘P). GO fortsetter også.")
            Button(action: model.panic) {
                VStack(spacing: 2) {
                    Text("STOPP ALT").font(.system(size: 17, weight: .semibold))
                    Text("Esc").font(.system(size: 11)).opacity(0.8)
                }
                .foregroundColor(.white)
                .frame(width: 130, height: 92)
                .background(RoundedRectangle(cornerRadius: 6).fill(theme.danger))
            }
            .buttonStyle(.plain)
            .help("Stopp alt med kort fade. Trykk to ganger for umiddelbar stopp.")
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
                Text("BESKJED").font(.system(size: 11, weight: .semibold)).foregroundColor(theme.wait)
                Text(model.memoText).font(.system(size: 22, weight: .semibold)).lineLimit(2)
            }
            Spacer(minLength: 0)
            Button("✕") { model.closeMemo() }.buttonStyle(PanelButtonStyle()).help("Lukk beskjeden")
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
                SectionHeader("NESTE CUE")
                Text("— slutt på lista —").font(.system(size: 26, weight: .semibold)).foregroundColor(theme.muted)
            }
        }
    }
}

private struct NextCueContent: View {
    @Environment(\.theme) var theme
    @ObservedObject var cue: Cue

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            SectionHeader("NESTE CUE")
            HStack(alignment: .firstTextBaseline, spacing: 14) {
                Text(cue.number).font(.system(size: 26, weight: .bold, design: .monospaced)).foregroundColor(theme.playhead)
                Text(cue.displayName).font(.system(size: 26, weight: .semibold)).lineLimit(1)
            }
            Text(cue.summary).font(.system(size: 13)).foregroundColor(theme.muted).lineLimit(1)
            if !cue.notes.isEmpty {
                Text(cue.notes).font(.system(size: 13)).foregroundColor(theme.wait).lineLimit(1)
            }
        }
    }
}

struct ClockText: View {
    @ObservedObject var live: LiveState
    var body: some View { Text(live.clockText.isEmpty ? "--:--:--" : live.clockText).font(.system(size: 30, design: .monospaced)) }
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
            menu("Fil") {
                Button("Nytt show") { model.newShow() }
                Button("Åpne …") { model.openDialog() }
                Button("Lagre") { model.save(as: false) }
                Button("Lagre som …") { model.save(as: true) }
                Divider()
                Button("Samle show og mediefiler i én mappe …") { model.collectShow() }
                Button("Finn manglende mediefiler …") { model.relinkMenu() }.disabled(model.showMode)
            }
            Button("↶") { model.undo() }.disabled(!model.canUndo).help("Angre (⌘Z)")
            Button("↷") { model.redo() }.disabled(!model.canRedo).help("Gjør om (⇧⌘Z)")
            divider
            Group {
                menu("＋ Media") {
                    Button("♪  Lyd … (⌘1)") { model.add(.audio) }
                    Button("▶  Video … (⌘2)") { model.add(.video) }
                    Button("▣  Bilde … (⌘3)") { model.add(.image) }
                    Button("▤  PDF … (⌘4)") { model.add(.pdf) }
                    Button("◍  Nettside (⇧⌘W)") { model.add(.web) }
                    Divider()
                    Button("☰  Spilleliste … (⌘9)") { model.add(.playlist) }
                    Button("◉  Live-inngang (⇧⌘I)") { model.add(.input) }
                }
                .help("Legg til lyd, video, bilde, PDF, nettside …")
                menu("Styring") {
                    Button("◢  Fade – valgt cue (⌘5)") { model.add(.fade) }
                    Button("■  Stopp (⌘6)") { model.add(.stop) }
                    Button("↪  Gå til (⌘0)") { model.add(.goto) }
                    Button("✎  Memo (⇧⌘M)") { model.add(.memo) }
                }
                .help("Fade, stopp, gå til og memo")
                menu("Signal ut") {
                    Button("⇢  OSC (⌘7)") { model.add(.osc) }
                    Button("♫  MIDI (⌘8)") { model.add(.midi) }
                    Button("⇄  Nettverk – UDP/TCP/HTTP (⇧⌘N)") { model.add(.network) }
                    Button("⏱  Tidskode – MTC/LTC (⌥⌘T)") { model.add(.timecode) }
                    Button("✺  Lys – DMX/Art-Net (⇧⌘L)") { model.add(.light) }
                }
                .help("OSC, MIDI, nettverk, tidskode og lys")
                menu("Rediger") {
                    Button("Dupliser (⌘D)") { model.duplicateSelected() }
                    Button("Slett (⌫)") { model.deleteSelected() }
                    Button("Flytt opp (⌘↑)") { model.moveSelected(-1) }
                    Button("Flytt ned (⌘↓)") { model.moveSelected(1) }
                    Button("Nummerer alle 1, 2, 3 …") { model.renumber() }
                    Divider()
                    Button("Normaliser lydstyrke for valgte lyd-cuer") { model.normalizeSelected() }
                    Button("Normaliser lydstyrke for alle lyd-cuer") { model.normalizeAll() }
                }
            }
            .disabled(model.showMode)
            Spacer(minLength: 8)
            Button("▦ Cart") { model.showCart() }.help("Store knapper for berøringsskjerm (⌘K)")
            Button(model.showMode ? "🔓 Lås opp" : "🔒 Showmodus") { model.showMode.toggle() }
                .help("Lås redigering under forestillingen: bare GO, stopp og hurtigtaster virker (⌘L)")
            menu("Oppsett") {
                Button("Lydpatch …") { openWindow(id: "patch") }
                Button("Vis/skjul utgangsvinduet (⇧⌘U)") { model.output.toggle() }
                SettingsButton()
                Button("Bytt tema \(themeGlyph) (⇧⌘T)") { model.settings.theme = model.settings.theme.next }
                Divider()
                Button("Om ShowCue") { AboutPanel.show() }
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
            .font(.system(size: 12.5))
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
            SettingsLink { Text("Innstillinger") }
        } else {
            Button("Innstillinger") {
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
                .help("Mellomrom = GO · Esc = stopp alt · ↑↓ = flytt markør · dra filer inn i lista")
        }
        .font(.system(size: 11.5))
        .padding(.horizontal, 12).padding(.vertical, 4)
        .background(theme.panel)
        .overlay(Rectangle().fill(theme.line).frame(height: 1), alignment: .top)
    }
}
