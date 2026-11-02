import SwiftUI

/// Knappepanel: store knapper som starter og stopper cuer direkte, uten å flytte GO-markøren. Viser cuer merket «Vis i knappepanelet» og cuer med hurtigtast.
struct ButtonPanelView: View {
    @EnvironmentObject var model: AppModel

    var body: some View {
        Themed { PanelContent() }
    }

    static func isPanelCue(_ c: Cue) -> Bool { c.inButtonPanel || !c.hotkey.isEmpty }
}

private struct PanelContent: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.theme) var theme

    var body: some View {
        let tiles = model.cues.filter(ButtonPanelView.isPanelCue)
        let cols = max(2, min(8, model.settings.buttonPanelColumns))
        VStack(spacing: 0) {
            HStack(spacing: 10) {
                Text(L("Trykk på en knapp for å starte cuen, og trykk igjen for å stoppe den. GO-markøren flyttes ikke – fint for jingler, lydeffekter og berøringsskjerm."))
                    .foregroundColor(theme.muted).uiFont(12.5)
                Spacer()
                Text(L("Kolonner")).foregroundColor(theme.muted).uiFont(12.5)
                Stepper("\(cols)", value: Binding(get: { cols }, set: { model.settings.buttonPanelColumns = $0; model.settings.save() }), in: 2...8)
                    .frame(width: 80)
                Button(model.engine.isPaused ? L("▶ Fortsett") : L("⏸ Pause"), action: model.togglePause).buttonStyle(PanelButtonStyle())
                Button(L("STOPP ALT"), action: model.panic).buttonStyle(PanelButtonStyle(fill: theme.danger, foreground: .white))
            }
            .padding(.horizontal, 12).padding(.vertical, 8)
            .background(theme.panel)
            Rectangle().fill(theme.line).frame(height: 1)
            if tiles.isEmpty {
                Text(L("Ingen knapper ennå. Velg en cue, og kryss av for «Vis i knappepanelet» i panelet til høyre (cuer med hurtigtast kommer også hit)."))
                    .foregroundColor(theme.faint).multilineTextAlignment(.center).padding(40)
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            } else {
                ScrollView {
                    LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 12), count: cols), spacing: 12) {
                        ForEach(tiles) { PanelTile(cue: $0) }
                    }
                    .padding(14)
                }
            }
        }
        .frame(minWidth: 360, minHeight: 300)
        .background(theme.bg)
    }
}

private struct PanelTile: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.theme) var theme
    @ObservedObject var cue: Cue

    var body: some View {
        Button { model.toggleHot(cue) } label: {
            VStack(alignment: .leading, spacing: 6) {
                RoundedRectangle(cornerRadius: 2).fill(theme.stripe(cue.color) ?? .clear).frame(height: 5)
                HStack {
                    Text(cue.number).uiFont(13, weight: .bold, design: .monospaced).foregroundColor(theme.playhead)
                    Spacer()
                    Text(cue.hotkey).uiFont(13, weight: .bold, design: .monospaced).foregroundColor(theme.wait)
                }
                Text(cue.displayName).uiFont(19, weight: .semibold).lineLimit(2).multilineTextAlignment(.leading)
                Spacer(minLength: 0)
                if cue.isRunning {
                    ProgressView(value: min(1, max(0, cue.progress))).tint(theme.running)
                }
            }
            .padding(12)
            .frame(maxWidth: .infinity, minHeight: 120, maxHeight: 120, alignment: .topLeading)
            .background(RoundedRectangle(cornerRadius: 8).fill(cue.isRunning ? theme.runningRow : theme.panel3))
            .overlay(RoundedRectangle(cornerRadius: 8).stroke(cue.isRunning ? theme.running : theme.line, lineWidth: cue.isRunning ? 2 : 1))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .disabled(cue.disabled)
        .opacity(cue.disabled ? 0.45 : 1)
    }
}
