import SwiftUI

/// «Sjekk showet»: viser funnene fra forhåndssjekken som en liste (feil, advarsler og ok). «Sjekk på nytt» kjører sjekken igjen.
struct PreflightView: View {
    @EnvironmentObject var model: AppModel

    var body: some View {
        Themed { PreflightContent() }
    }
}

private struct PreflightContent: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.theme) var theme
    @State private var findings: [PreflightFinding] = []

    var body: some View {
        let errors = PreflightCheck.count(findings, .error)
        let warnings = PreflightCheck.count(findings, .warning)
        VStack(alignment: .leading, spacing: 0) {
            Text(errors + warnings == 0 ? L("Alt ser bra ut.") : L("{0} feil og {1} advarsler.", errors, warnings))
                .uiFont(15, weight: .semibold)
                .padding(.horizontal, 18).padding(.vertical, 14)
            Rectangle().fill(theme.line).frame(height: 1)
            ScrollView {
                VStack(alignment: .leading, spacing: 6) {
                    ForEach(Array(findings.enumerated()), id: \.offset) { _, f in
                        HStack(alignment: .firstTextBaseline, spacing: 8) {
                            Text(icon(f.severity)).uiFont(13, weight: .bold).foregroundColor(color(f.severity)).frame(width: 18)
                            Text(f.message).uiFont(13).fixedSize(horizontal: false, vertical: true)
                            Spacer(minLength: 0)
                        }
                    }
                }
                .padding(18)
                .frame(maxWidth: .infinity, alignment: .leading)
            }
            Rectangle().fill(theme.line).frame(height: 1)
            HStack {
                Spacer()
                Button(L("Sjekk på nytt")) { findings = model.runPreflight() }
            }
            .padding(12)
        }
        .frame(minWidth: 480, minHeight: 300)
        .background(theme.bg)
        .onAppear { findings = model.runPreflight() }
    }

    private func icon(_ s: PreflightSeverity) -> String {
        switch s {
        case .error: return "✕"
        case .warning: return "⚠"
        case .ok: return "✓"
        }
    }

    private func color(_ s: PreflightSeverity) -> Color {
        switch s {
        case .error: return theme.danger
        case .warning: return theme.wait
        case .ok: return theme.running
        }
    }
}
