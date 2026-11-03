# Testing

## Testsett
- **Windows** `Windows/ShowCue.Tests` (xUnit, kjøres i CI): modeller og showfil, lydmotor og avspilling (`PlaybackTests`,
  `AudioEngineTests`), fjernstyring (`RemoteTests`), tidskode/DMX (`TimecodeTests`), verktøy og språk (`ToolsTests`),
  autolagring/angre (`SafetyTests`), og `RobustnessTests` (feil funnet med stresstest).
  `TestAudio` lager testfiler (konstant verdi per kanal eller rampe = tid/10, så posisjonen kan leses rett av utgangen).
- **Mac** `Mac/Tests/ShowCueTests` (XCTest): de samme testene der koden finnes på Mac. `RampDecoder` er en dekoder uten fil.
- Nye funksjoner får tester på begge plattformer. Rettede feil får en regresjonstest.

## Sjekke kode uten Windows eller Mac (Linux-miljøet)
WPF og SwiftUI kan ikke bygges der, og NuGet/dotnet-nedlasting er sperret. Det som brukes:
- **Testsettet uten WPF:** et eget konsollprosjekt som kompilerer `Windows/ShowCue/Models/*.cs`, de WPF-frie filene i `Engine/`
  og testene, med små erstatninger for `Dispatcher`/`Application` og xUnit-`Assert`, mot en lokalt bygd NAudio (fra kildekode).
  .NET 8 SDK lokalt (global.json krever 10): bygg fra en kopi utenfor repoet.
- **Syntaks for hele Windows-prosjektet:** bygg alle `.cs` uten WPF-referanser og se bare på `CS1xxx`-feil (parserfeil).
  Semantiske feil i WPF-kode (f.eks. ikke-konstante mønstre) fanges først av CI.
- **XAML:** parse som XML. Markup-utvidelser valideres først av CI.
- **Swift:** `tree-sitter-swift` (pip) for syntaks. Typefeil fanges først av CI på Mac.
- **Oversettelser:** `python3 tools/i18n.py`.

## Stresstest (`tools/stress`)
Se [tools/stress/README.md](../../tools/stress/README.md). Én runde går i CI; lange kjøringer gjøres lokalt.

Resultat november 2026: første runder fant feilene som ble rettet i 0.13.1 (se [fallgruver.md](fallgruver.md)).
Etterpå: 176 runder på 2 timer uten feil og uten minnevekst (styrt minne stabilt ca. 2,5 MB).
Spoling (0.14.0) er lagt inn i testen og kjørt 10 runder uten feil.

Ikke dekket av stresstesten: WPF/SwiftUI, video, PDF, ekte lydkort og MIDI-enheter.
