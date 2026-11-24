# Fallgruver

Ting som har gått galt eller nesten gått galt, og hvordan det unngås.

## Lyd og avspilling
- **Uendelig tilbakespoling:** en loop kortere enn én blokk (start og slutt nesten like, eller en fil på ett sample) fikk
  lyd-/dekodertråden til å spole tilbake for alltid. Krav: loop ≥ 10 ms, og avbryt hvis to tilbakespolinger skjer uten lyd imellom.
- **NaN/uendelig:** `Math.Max(0, NaN)` og `Math.Clamp(NaN, …)` gir NaN i C#. I Swift krasjer `Int(NaN)`/`Int(.infinity)` og for store tall
  hele appen. Bruk `Seconds()`/`Range()` (Windows-modellen) og `frameCount()`/`safeSeconds()` (Mac). `JSONEncoder`/`System.Text.Json`
  nekter NaN – et ugyldig tall gjør showet umulig å lagre (Mac: `try!` i `clone()` krasjet).
- `dbToGain(NaN)` ga NaN i hele mikseren. Bruk `db > -60` (usann for NaN), ikke `db <= -60`.
- `TimeSpan.FromSeconds` med enorme verdier kaster; klokkevisninger begrenses til 99:59:59.
- **Ødelagte OSC-pakker:** les aldri utenfor pakken (lengdefelt kan lyve, blob-lengde kan være negativ). Mac krasjet på `d[s..<s]` utenfor tabellen.
- **ASIO:** en driver eies av én økt. Åpne aldri innganger og utganger hver for seg; bruk `InitRecordAndPlayback` og `AudioAvailable`.
  `AsioOut` kan bare initialiseres én gang: feiler `InitRecordAndPlayback`, må objektet kastes og et nytt lages. `InputChannelOffset`
  må settes *før* init (feil gir unntak ved for høy forskyvning). `GetAsInterleavedSamples` kaster for ukjente lydformater; fang det i
  hendelsen, ellers faller driver-tråden. Ingen minnetildeling eller låsing i `AudioAvailable`.
- **Lengdefelt og addisjon:** `(n + 3) & ~3` på en `int` som er nesten `int.MaxValue` blir negativ (Windows). Sjekk `n <= resten av pakken` før du regner med lengden.
- **`double.TryParse` godtar «NaN» og «Infinity»:** `v < 0 || v > 255` er usann for NaN. Skriv `!(v >= 0 && v <= 255)`.
- **`byte.TryParse(…, HexNumber)` godtar mellomrom, Swifts `UInt8(_, radix: 16)` godtar «+»:** sjekk at begge tegn er sifre først (`\xHH`).
- Endre aldri en `ObservableCollection` mens den gjennomgås: bruk `.ToList()` før `Pause()`/`Resume()`/`Stop()` på aktive cuer.

## Showfiler
- Windows leste en cue med ett ugyldig felt (f.eks. `"FadeTime": "tre"`) som feil for hele filen. Nå leses feltene hver for seg (`CueJsonConverter.ReadFieldByField`),
  og «null» i `Cues`, `Files` og `Routes` fjernes i egenskapene. Mac har alltid brukt standardverdi per felt.
- Mac gjettet cuetype `stop` når `type` manglet; en slik cue stopper alt ved GO. Mangler eller er typen ukjent, avvises filen (som på Windows).

## Avslutning
- Mac: et vindu som bare skjules når det lukkes (utgangsvinduet), hindrer «avslutt når siste vindu lukkes». Hovedvinduet må avslutte appen selv.
- Fullskjerm-utgang ligger over vanlige vinduer: skjul den før `NSAlert`/`MessageBox`, ellers ser programmet ut til å henge.
- Windows: `OnClosing` kjører hvert oppryddingssteg for seg (`Try`), ellers kunne én feil hoppe over `Engine.Dispose()`.
- Windows: skjulte vinduer holder WPF-prosessen i gang med standard `ShutdownMode`; én feil i oppryddingen kan hindre resten.

## WPF / XAML
- `{local:T '…'}`: krøllparenteser i teksten må escapes (`\{ \}`), ellers byggefeil «Unexpected closing brace».
- `Loc.T(...)` kan ikke brukes i `is`-mønstre, `case` eller `const` (må være konstant).
- En `OneWay`-binding brytes hvis brukeren endrer verdien (f.eks. `Slider`); bruk `TwoWay` mot en egenskap som tolker endringen.
- `ProgressBar`/`Slider` trenger egen stil for å se riktig ut i mørke temaer (se `Themes/Controls.xaml`).
- Ikonfiler med bare PNG-bilder vises ikke alltid i små størrelser; bruk BMP (med AND-maske) for 16–128 px.

## SwiftUI / macOS
- `.uiFont(…)` returnerer en `View`; der SwiftUI krever `Text` (f.eks. `ctx.draw(Text(...))` i `Canvas`) må `.font(...)` brukes.
- `scaleEffect` flytter ikke AppKit-kontrollers treff-områder; bruk skalerte fonter/mål for zoom.
- macOS husker app-ikoner: etter nytt ikon, `touch /Applications/ShowCue.app && killall Dock`. Appen setter også ikonet selv ved oppstart.
- Kjøring fra Xcode gir ingen app-pakke (ikonet hentes da fra ressurspakken ved siden av programfila).
- ⌘0 er brukt («gå til»-cue); Option-tastene gir spesialtegn på norsk tastatur.

## Tester
- Tidsstempler: to filer skrevet rett etter hverandre kan få samme tid på Windows. Sett eldre tid eksplisitt når testen sammenligner.
- Tester med statisk språkstatus (`Loc`) må ikke endre språket globalt (testene kjører parallelt).

## Arbeidsmiljø og git
- Git-proxyen i utviklingsmiljøet er autentisert: et skript med `git push` pusher selv om tokenet er «falskt». Kjør aldri slike skript som test.
- `pkill -f <mønster>` kan drepe skallet som kjører kommandoen (mønsteret står i egen kommandolinje). Bruk prosess-ID.
- `pip download`/verktøy som skriver i arbeidsmappa: en tilfeldig `.whl` ble sjekket inn i 0.11.0. Se over `git status` før commit.
- `git filter-branch` på commits signert av GitHub: begrens området (`<commit>^..main`), ellers skrives hele historikken om.
- En PR med en annen PR-gren som base blir lukket/merget sammen med den; sett base til `main`.
- Byggesjekken på `main` kan feile selv om PR-en var grønn (ustabil test). Utgivelsen stopper da; rett testen i en ny PR.
