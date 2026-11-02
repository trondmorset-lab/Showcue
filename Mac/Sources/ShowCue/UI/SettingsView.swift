import SwiftUI

struct SettingsView: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.theme) var theme
    @State private var draft = AppSettings()
    @State private var loaded = false
    @State private var devices: [AudioDeviceInfo] = []
    @State private var midiInputs: [String] = []
    @State private var learning: String? = nil

    var body: some View {
        VStack(spacing: 0) {
            TabView {
                page { audioTab }.tabItem { Text(L("Lyd")) }
                page { videoTab }.tabItem { Text("Video") }
                page { oscTab }.tabItem { Text("OSC") }
                page { midiTab }.tabItem { Text(L("MIDI inn")) }
                page { lookTab }.tabItem { Text(L("Utseende")) }
                page { safetyTab }.tabItem { Text(L("Sikkerhet")) }
            }
            HStack {
                Text(draft.audioKey != model.settings.audioKey ? L("Nytt lydoppsett stopper alt som spiller.") : "")
                    .uiFont(11.5).foregroundColor(theme.wait)
                Spacer()
                Button(L("Tilbakestill")) { draft = model.settings }
                Button(L("Bruk")) { model.applySettings(draft); draft = model.settings }
                    .keyboardShortcut(.defaultAction)
            }
            .padding(14)
        }
        .frame(width: 580, height: 640)
        .background(theme.panel)
        .onAppear {
            if !loaded { draft = model.settings; loaded = true }
            devices = CoreAudioDevices.outputDevices()
            midiInputs = MidiSystem.sources()
        }
        .onDisappear { model.router.cancelLearn() }
    }

    private func page<C: View>(@ViewBuilder _ c: () -> C) -> some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 0) { c() }
                .padding(.horizontal, 18).padding(.vertical, 12)
                .frame(maxWidth: .infinity, alignment: .leading)
        }
    }

    // MARK: Lyd

    private var audioTab: some View {
        VStack(alignment: .leading, spacing: 0) {
            FieldLabel(L("Lydenhet"))
            Picker("", selection: $draft.audioDeviceUID) {
                Text(L("Systemets standard utgang")).tag("")
                ForEach(devices) { Text($0.label).tag($0.uid) }
            }.labelsHidden()
            Hint(L("Nettverkslydkort (f.eks. Dante Virtual Soundcard), flerkanals USB/Thunderbolt-kort og aggregerte enheter vises her. Alle kanalene åpnes (opptil maks nedenfor), og lydpatchen bestemmer hvor hver utgang går."))
            Pair {
                FieldLabel(L("Maks antall kanaler")); IntInput(value: $draft.maxChannels, range: 2...512)
            } _: {
                FieldLabel(L("Buffer (rammer)"))
                Picker("", selection: $draft.bufferFrames) {
                    ForEach([64, 128, 256, 512, 1024], id: \.self) { Text("\($0)").tag($0) }
                }.labelsHidden()
            }
            Hint(L("Lavere buffer gir raskere respons på GO. 256 er trygt for de fleste; øk ved knaking."))
            Hint(L("Samplingsfrekvensen følger enheten (for nettverkslydkort: settes i kortets eget kontrollprogram). Filer med annen frekvens konverteres automatisk."))
            Button(L("Oppdater liste")) { devices = CoreAudioDevices.outputDevices() }.buttonStyle(PanelButtonStyle()).padding(.top, 10)

            SectionHeader(L("FORHÅNDSLYTTING (BØLGEFORMEN)")).padding(.top, 18)
            FieldLabel(L("Egen lydenhet, f.eks. hodetelefoner"))
            Picker("", selection: $draft.previewDeviceUID) {
                Text(L("Ingen egen enhet")).tag("")
                ForEach(devices) { Text($0.label).tag($0.uid) }
            }.labelsHidden()
            FieldLabel(L("…eller utgang i lydpatchen (første av to, 0 = cuens egne utganger)"))
            IntInput(value: $draft.previewBus, range: 0...128).frame(width: 90)
            Hint(L("Da kan du lytte på cuer under forestillingen uten at det høres i salen. Med 0 og ingen enhet går lyttingen ut på cuens vanlige utganger."))

            SectionHeader(L("LIVE-INNGANG")).padding(.top, 18)
            FieldLabel(L("Inngang for inngang-cuer"))
            Picker("", selection: $draft.inputDeviceUID) {
                Text(L("Systemets standard inngang")).tag("")
                ForEach(CoreAudioDevices.inputDevices(), id: \.uid) { Text($0.label).tag($0.uid) }
            }.labelsHidden()
            Hint(L("Mikrofon, linje inn eller nettverkslydkort. macOS spør om tilgang til mikrofonen første gang."))
        }
    }

    // MARK: Video

    private var videoTab: some View {
        VStack(alignment: .leading, spacing: 0) {
            FieldLabel(L("Utgang for video, bilde og PDF"))
            Picker("", selection: $draft.videoScreen) {
                Text(L("Vindu (ikke fullskjerm)")).tag(-1)
                ForEach(Array(OutputCompositor.screenNames().enumerated()), id: \.offset) { i, n in Text(n).tag(i) }
            }.labelsHidden()
            Hint(L("Velg projektoren/skjermen. «Vindu» gir et vanlig vindu, nyttig for å teste på én skjerm."))
        }
    }

    // MARK: OSC

    private var oscTab: some View {
        VStack(alignment: .leading, spacing: 0) {
            Toggle(L("Ta imot OSC (fjernstyring fra kontrollpaneler og lysbord, f.eks. Companion eller TouchOSC)"), isOn: $draft.oscInEnabled)
            FieldLabel(L("UDP-port")); IntInput(value: $draft.oscInPort, range: 1...65535)
            let ips = UDP.localAddresses()
            Hint(ips.isEmpty ? L("Fant ingen nettverksadresser.") : L("Send til: {0}  (port {1}). Fra samme maskin: 127.0.0.1.", ips.joined(separator: L("  eller  ")), draft.oscInPort))
            FieldLabel(L("Kommandoer")).padding(.top, 8)
            VStack(alignment: .leading, spacing: 3) {
                ForEach([("/go", "GO"), ("/stop", L("stopp alt (med fade)")), ("/panic", L("som Esc (2× = umiddelbart)")),
                         ("/hardstop", L("stopp alt umiddelbart")), ("/next  /previous", L("flytt markøren")),
                         ("/playhead/12", L("sett markøren til cue 12")), ("/cue/12/start", L("start cue 12")),
                         ("/cue/12/stop", L("stopp cue 12")), ("/cue/12/go", L("markør til 12 + GO")),
                         ("/hotkey/F1", L("som å trykke F1")), ("/refresh", L("send status på nytt"))], id: \.0) { a, b in
                    HStack(spacing: 0) {
                        Text(a).frame(width: 150, alignment: .leading)
                        Text(b).foregroundColor(theme.muted)
                    }
                }
            }
            .uiFont(12, design: .monospaced)
            .padding(10)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(RoundedRectangle(cornerRadius: 4).fill(theme.panel2))
            Hint(L("Argument 0 ignoreres (knapp sluppet). macOS kan spørre om tilgang til lokalt nettverk første gang – tillat det."))

            Rectangle().fill(theme.line).frame(height: 1).padding(.vertical, 14)
            SectionHeader(L("STATUS UT (F.EKS. TIL COMPANION)"))
            Toggle(L("Send status: neste cue, hva som spiller, tid igjen"), isOn: $draft.oscFeedbackEnabled).padding(.top, 6)
            HStack(alignment: .top, spacing: 10) {
                VStack(alignment: .leading, spacing: 0) { FieldLabel(L("Mottaker (IP)")); TextInput($draft.oscFeedbackHost) }
                VStack(alignment: .leading, spacing: 0) { FieldLabel(L("Port")); IntInput(value: $draft.oscFeedbackPort, range: 1...65535) }.frame(width: 100)
            }
            FieldLabel(L("Format"))
            Picker("", selection: $draft.oscFeedbackFormat) {
                ForEach(OscFeedbackFormat.allCases, id: \.self) { Text($0.label).tag($0) }
            }.labelsHidden()
            Hint(L("Companion: setter egendefinerte variabler (Custom Variables) via Companions OSC-port (standard 12321). Opprett variablene i Companion først: sc_next_name, sc_next_number, sc_playing, sc_remaining, sc_state m.fl. – se README. Bruk dem i knappetekst som $(internal:custom_sc_next_name)."))
            Hint(L("Generisk: /showcue/next/name, /showcue/remaining … og /showcue/cue/{nr}/running 1/0."))
        }
    }

    // MARK: MIDI

    private var midiTab: some View {
        VStack(alignment: .leading, spacing: 0) {
            FieldLabel(L("MIDI-inngang"))
            Picker("", selection: $draft.midiInDevice) {
                Text(L("(av)")).tag("")
                ForEach(midiInputs, id: \.self) { Text($0).tag($0) }
                if !draft.midiInDevice.isEmpty && !midiInputs.contains(draft.midiInDevice) {
                    Text(L("{0} (ikke tilkoblet)", draft.midiInDevice)).tag(draft.midiInDevice)
                }
            }.labelsHidden()
            FieldLabel(L("Kanal"))
            Picker("", selection: $draft.midiInChannel) {
                Text(L("Alle kanaler")).tag(0)
                ForEach(1...16, id: \.self) { Text(L("Kanal {0}", $0)).tag($0) }
            }.labelsHidden()

            FieldLabel(L("Faste funksjoner")).padding(.top, 8)
            learnRow("GO", \.midiGo)
            learnRow(L("Stopp alt"), \.midiStopAll)
            learnRow(L("Neste"), \.midiNext)
            learnRow(L("Forrige"), \.midiPrevious)
            Hint(L("«Lær»: klikk, og trykk en tast/knapp på kontrolleren. Lær bruker inngangen som er aktiv nå – bytter du inngang, trykk «Bruk» først. Hver cue kan også få sin egen MIDI-trigger i panelet til høyre i hovedvinduet."))

            Toggle(L("Ta imot MIDI Time Code (starter cuer med tidskode-trigger)"), isOn: $draft.mtcInEnabled).padding(.top, 14)
            Toggle(L("Ta imot MIDI Show Control (MSC GO / STOP med cuenummer)"), isOn: $draft.mscInEnabled).padding(.top, 8)
            FieldLabel(L("MSC device ID (127 = alle)")); IntInput(value: $draft.mscInDeviceId, range: 0...127)
        }
    }

    private func learnRow(_ label: String, _ key: WritableKeyPath<AppSettings, String>) -> some View {
        HStack(spacing: 6) {
            Text(label).foregroundColor(theme.muted).frame(width: 90, alignment: .leading)
            Text(learning == label ? L("Venter på MIDI …") : (draft[keyPath: key].isEmpty ? L("(ingen)") : MidiTriggers.describe(draft[keyPath: key])))
                .uiFont(13, design: .monospaced)
                .foregroundColor(learning == label ? theme.wait : theme.text)
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 6).padding(.vertical, 5)
                .background(RoundedRectangle(cornerRadius: 4).fill(theme.panel2))
                .overlay(RoundedRectangle(cornerRadius: 4).stroke(theme.line))
            Button(L("Lær")) {
                guard model.midi.isInputOpen else {
                    model.alert(L("Ingen MIDI-inngang er aktiv ennå. Velg inngang og trykk «Bruk» først."))
                    return
                }
                learning = label
                model.router.beginLearn { t in
                    draft[keyPath: key] = t
                    learning = nil
                }
            }
            Button("✕") { model.router.cancelLearn(); learning = nil; draft[keyPath: key] = "" }
        }
        .buttonStyle(PanelButtonStyle())
        .padding(.vertical, 3)
    }

    // MARK: Utseende og sikkerhet

    private var lookTab: some View {
        VStack(alignment: .leading, spacing: 0) {
            FieldLabel("Språk / Language")
            Picker("", selection: $draft.language) {
                ForEach(Loc.languages, id: \.code) { Text($0.name).tag($0.code) }
            }
            .labelsHidden()
            .frame(width: 260)
            Hint(L("Tar effekt neste gang ShowCue startes."))

            FieldLabel(L("Fargetema"))
            Picker("", selection: Binding(get: { model.settings.theme }, set: { t in model.settings.theme = t; draft.theme = t })) {
                ForEach(AppTheme.allCases, id: \.self) { Text($0.label).tag($0) }
            }
            .labelsHidden()
            .pickerStyle(.radioGroup)
            Hint(L("Mørk passer de fleste kontrollrom. Grå er et dempet, lyst tema som er mindre hvitt enn Lys. Lys er lettest å lese i dagslys og på prøver. Nattmodus er ekstra dempet for mørke scener og sidescene. Temaet byttes med en gang, også fra Oppsett-menyen eller med ⇧⌘T."))

            FieldLabel(L("Zoom"))
            Picker("", selection: Binding(get: { model.settings.zoom }, set: { z in model.settings.zoom = z; draft.zoom = z })) {
                ForEach(UIZoom.steps, id: \.self) { Text(UIZoom.percent($0)).tag($0) }
            }
            .labelsHidden()
            .frame(width: 120)
            Hint(L("Gjør tekst, knapper og cuelista i hovedvinduet større eller mindre. Også med ⌘+ og ⌘−. Fint på store skjermer, for lang avstand til skjermen eller små bærbare."))

            FieldLabel(L("Tekst i cuelista"))
            Picker("", selection: Binding(get: { model.settings.listTextScale }, set: { v in model.settings.listTextScale = v; draft.listTextScale = v })) {
                ForEach(UIZoom.listSizes.indices, id: \.self) { i in Text(UIZoom.listSizes[i].label).tag(UIZoom.listSizes[i].scale) }
            }
            .labelsHidden()
            .pickerStyle(.radioGroup)
            Hint(L("Gjør bare cuelista større, i tillegg til zoom – lettere å lese under forestilling."))
        }
    }

    private var safetyTab: some View {
        VStack(alignment: .leading, spacing: 0) {
            FieldLabel(L("Stopp alt: fade-tid (s)")); NumberInput(value: $draft.panicFadeTime, range: 0...30)
            FieldLabel(L("Dobbel-GO-sperre (ms)")); IntInput(value: $draft.doubleGoProtectionMs, range: 0...2000)
            Hint(L("GO ignoreres hvis det kommer innen denne tiden etter forrige GO (prell på tast eller kontroller)."))
            FieldLabel(L("Mål for «Normaliser lydstyrke» (LUFS)")); NumberInput(value: $draft.loudnessTarget, range: -40...(-5))
            Hint(L("−23 er EBU R128 (kringkasting). Lavere tall = svakere. Alle lyd-cuer som normaliseres får samme opplevde lydstyrke."))
        }
    }
}
