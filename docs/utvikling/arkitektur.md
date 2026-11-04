# Arkitektur

ShowCue er ett program i to utgaver som deler showformat, oversettelser og versjonsnummer:

| | Windows | Mac |
|---|---|---|
| Kode | `Windows/ShowCue` (C#, .NET 10, WPF) | `Mac/Sources/ShowCue` (Swift 5.9, SwiftUI, macOS 13+) |
| Lyd | NAudio: WASAPI eller ASIO | CoreAudio (AUHAL) |
| Video | WPF `MediaElement` (dempet) + lyden pakket ut med Media Foundation | `AVPlayer` (dempet) + lyden dekodet med `AVAssetReader` |
| Tester | `Windows/ShowCue.Tests` (xUnit) | `Mac/Tests/ShowCueTests` (XCTest) |
| Innstillinger | `%AppData%\ShowCue\settings.json` | `~/Library/Application Support/ShowCue/settings.json` |
| Autolagring / kopier | `…\ShowCue\Autolagring`, `…\Sikkerhetskopier` | samme mappenavn under Application Support |

Felles: `VERSION` (versjon), `i18n/en.json` (engelsk), `THIRD-PARTY-NOTICES.txt`, `LICENSE` (GPL-3.0), `assets/` (logo).

## Mapper

```
Windows/ShowCue/
  Models/   Cue-typer (Cues.cs), showfil (ShowDocument), innstillinger (AppSettings, UiZoom), språk (Loc),
            tidskode (Timecode, Mtc, Ltc), DMX, angre (UndoHistory), autolagring/kopier (ShowSafety), MediaTools
  Engine/   CueEngine (GO, aktive cuer, pause), ActiveCues (én klasse per cuetype som spiller),
            AudioEngine + PatchMixer + CuePlayback (lyd), VideoAudio, Waveform, Loudness,
            Osc, OscFeedback, RemoteControl, MidiInput/MidiEngine, NetworkSender, TimecodeOutput, DmxOutput, AudioInput
  Views/    Utgangsvindu, innstillinger, lydpatch, routingmatrise, bølgeform, knappepanel, «Om», ThemeManager
  Themes/   Colors.{Dark,Gray,Light,Night}.xaml og Controls.xaml (stiler)
  MainWindow.xaml(.cs)  Hovedvindu: verktøylinje, cueliste, inspektør, aktive cuer, hurtigtaster
  TextExtension.cs      {local:T '…'} for oversatt tekst i XAML
Mac/Sources/ShowCue/
  Core/     Samme modeller og logikk som Windows/Models + Osc, OscFeedback, RemoteRouter, Mixer (PatchMixer, Voice, SampleRing)
  Audio/    AudioOutput (AUHAL), CoreAudioDevices, Streaming (FrameDecoder, StreamingSource, AudioFeeder), LiveAudio, Waveform
  Engine/   AppModel (alt brukerflaten trenger), CueEngine, ActiveCues, Output (utgangsvindu), MidiSystem, TimecodeDmx
  UI/       SwiftUI-visninger, Theme (farger, uiFont/zoom), ShowCueApp (menyer, AppDelegate, ikon)
  Resources/AppIcon.png  ikon når appen kjøres fra Xcode
Mac/Resources/  Info.plist, AppIcon.icns, Assets.xcassets (ikon som ressurskatalog)
tools/i18n.py   sjekker oversettelsene; tools/stress/ stresstest av kjernen
```

## Lydmotoren

```
lydfil-kanaler ──(routing per cue, dB)──▶ utganger/busser ──(lydpatch)──▶ kanaler på lydkortet
```

- **PatchMixer** (begge): summerer alle kilder (`IBusSource` / `BusSource`) inn i busser, og fordeler bussene til
  lydkortets kanaler etter patchen. Nivåmålere per buss og kanal. Patchen lagres per maskin, ikke i showet.
- **Windows – CuePlayback**: én avspilling. Leser fila direkte i lydtråden (`Render`), med resampler hvis
  frekvensen avviker. Start/slutt, loop, fades i dB-domenet (`FadeCurve`), automatisk fade ut mot slutten,
  pause, slipp loop, spoling (`Seek` – utføres i lydtråden), routing som kan byttes mens det spiller.
- **Mac – Voice + StreamingSource + SampleRing**: lydtråden leser bare ferdig dekodet lyd fra en ringbuffer.
  `AudioFeeder` er én bakgrunnstråd som fyller alle ringbuffere hvert 10. ms. Ringen har merker for rundestart,
  så «posisjon i runden» stemmer også når kilden looper. Spoling tømmer ringen med riktig posisjon i runden og dekoder derfra.
- Alle tider/nivåer som kommer inn i lydmotoren saneres (NaN, uendelig, negative → gyldige verdier).
  En loop må være minst 10 ms, og to tilbakespolinger uten lyd imellom avslutter i stedet for å henge.

## Aktive cuer

`CueEngine` lager én `ActiveCue` per avfyrt cue (pre-wait → spiller → ferdig) og kaller `Tick` ca. 60 ganger i sekundet.
Underklasser: lyd (`AudioActive`), live-inngang (`InputActive`) – begge `PlaybackActive`; video (`VideoActive`),
bilde/PDF/nettside (`StillActive`), lysbildeserie (`SlideshowActive`), fade, stopp, spilleliste, tidskode, lys, og «øyeblikks»-cuer (OSC, MIDI, nettverk, gå til, beskjed).
Felles: stopp med fade, fade-cuer, pause/fortsett, slipp loop, spoling (`CanSeek`, `SeekLength`, `SeekPosition`, `Seek`, `ScrubPosition`, `Nudge`).

**Utganger:** én standardutgang (skjermen i innstillingene, også vist som forhåndsvisning) og ved behov ekstra utganger for cuer med
egen skjerm (`OutputScreen`/`outputScreen`: −2 standard, −1 eget vindu, 0… skjerm). `CueEngine.EnsureOutput(screen)` /
`CueEngine.output(forScreen:)` lager dem ved første bruk. Krysstoning gjelder bare cuer på samme utgang. Forhåndsvisningen viser bare standardutgangen.

**Video:** bildet spilles dempet; lyden går gjennom lydpatchen som en vanlig avspilling. Lyden er «master»:
avviker bildet mer enn 0,12 s fra lyden (sjekkes hvert 2. sekund), flyttes bildet. Ved spoling flyttes begge, og synkroniseringen venter 2 s.

**Lysbildeserie:** `SlideshowActive` lager for hvert lysbilde en midlertidig bilde-, PDF- eller video-cue og en `StillActive`/`VideoActive`
som den eier selv (står ikke i `Active`, tikkes av serien). Forrige lysbilde stoppes med krysstoningen når neste starter.
Byttet sjekkes før lysbildene tikker, så en video krysstones over i neste før den fader ut av seg selv. Hver PDF-side er et lysbilde
(sideantallet hentes i bakgrunnen og huskes i `MediaCache`). Blaing: `CanStep`/`Step(±1)` (`canStep`/`step`), OSC `/cue/{nr}/next|previous`.
Filer i serien behandles som spillelistens (`IFileListCue` / `usesFileList`): finn/samle mediefiler og leting ved åpning.

## Fjernstyring og status

- **OSC inn** (UDP 8000): `/go`, `/stop`, `/panic`, `/hardstop`, `/next`, `/previous`, `/playhead/{nr}`, `/cue/{nr}/start|stop|go|devamp|next|previous`,
  `/hotkey/{tast}`, `/pause`, `/resume`, `/refresh`. Prefikset `/showcue/` er tillatt; argument 0/false ignoreres. Bundles støttes (maks 8 nivåer).
  Dekoderen leser aldri utenfor pakken, uansett hva lengdefeltene påstår.
- **MIDI inn**: faste funksjoner (GO, stopp alt, neste, forrige) og MIDI-trigger per cue (`N:60`, `CC:20`, `PC:5`), MSC GO/STOP, MTC inn for tidskode-triggere.
- **Status ut over OSC** (`OscFeedback`): format «Companion» (egendefinerte variabler `sc_*`, port 12321) eller generisk `/showcue/...`.

## Showfil

JSON (`.showcue`), lesbar. Cuer har `"type"` (`audio`, `video`, `image`, `pdf`, `web`, `fade`, `stop`, `osc`, `midi`, `goto`,
`playlist`, `slideshow`, `memo`, `network`, `timecode`, `light`, `input`). Egenskapsnavn er PascalCase og like på begge plattformer.
Mediefiler letes etter ved siden av showfila og i `Media/` når stien ikke finnes (show flyttes mellom maskiner).
Nøkkelen for knappepanelet er `InButtonPanel` (het `Cart` før 0.12.0; den gamle leses ikke).
Kodenavnet for beskjed-cuen er fortsatt `memo`/`MemoCue` (bare navnet i brukerflaten er endret).

## Språk

Tekster står på norsk i koden og er nøklene i `i18n/en.json`. Windows: `Loc.T("…")`, `Loc.F("… {0}", x)`, XAML `{local:T '…'}`.
Mac: `L("…")`, `L("… {0}", x)`. Språket velges ved oppstart (innstilling, ellers systemspråket) og byttes ved omstart.
Se [regler.md](regler.md#språk-og-tekst).

## Zoom og tekststørrelse

Windows: `LayoutTransform` på hele vinduet (`Zoom`) og på cuelista (`ListTextScale`). Mac: miljøverdien `uiScale` og `.uiFont(…)`
i stedet for `.font(.system(size:))`; cuelista ganger kolonnebredder og radhøyde. `uiFont` kan ikke brukes der SwiftUI krever `Text`
(f.eks. `Canvas`-tegning) – bruk fast `.font` der.

## Ikon

Windows: `Assets/ShowCue.ico` (BMP 16–128 px, PNG 256 px) som `ApplicationIcon`, og på alle vinduer (klassehåndterer i `App`).
Mac: `AppIcon.icns` + `Assets.car` (bygges med `actool` i `build-app.sh`, `CFBundleIconName`), og appen setter
`NSApp.applicationIconImage` ved oppstart (dekker Xcode-kjøring og gammel ikonbuffer). CI sjekker at ikonet er med.
