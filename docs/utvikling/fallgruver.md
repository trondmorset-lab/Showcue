# Fallgruver

Ting som har gått galt eller nesten gått galt, og hvordan det unngås.

## Lyd og avspilling
- **Unntak i en tidsløkke gjentas:** et unntak fra `a.Tick()` i `CueEngine.Tick` (Windows, hver 16. ms) ga samme modale melding igjen og igjen. Fang per cue (`SafeTick.Run`), sett feil på cuen og fjern den, logg én gang.
- **Grenser må gjelde ved lasting, ikke bare i redigeringen:** på Mac ble ikke `volumeDb`, port, tider o.l. begrenset når showet ble lest. Ny cuetype eller nytt felt: legg grensen i begge modeller
  (Windows-setter, Mac `clampLoadedValues`) og i `AppSettings.Normalize`/`normalize` for innstillinger. Antall filkanaler uten øvre grense gir en enorm routingtabell.
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

- **Forhåndslasting:** aldri bruk en forberedt fil uten å sjekke at den fortsatt stemmer (størrelse/endringstid, samplingsfrekvens, start/slutt/loop); `Take…` skal returnere null, ikke kaste. En forberedt leser/dekoder har én eier: den som tar den, må lukke den. Ikke rør lydløkken for å «hjelpe» forberedelsen.
- Windows: `MediaCache.Images` brukes nå også fra bakgrunnen (`Warm`), så alle tilganger går gjennom `ImagesLock`.

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
- `TabControl` følger ikke de mørke temaene uten en stor mal; inspektørfanene er vanlige knapper med stil som markerer valgt fane (`ActiveInspectorTab`).
- `ProgressBar`/`Slider` trenger egen stil for å se riktig ut i mørke temaer (se `Themes/Controls.xaml`).
- Ikonfiler med bare PNG-bilder vises ikke alltid i små størrelser; bruk BMP (med AND-maske) for 16–128 px.

## SwiftUI / macOS
- `.uiFont(…)` returnerer en `View`; der SwiftUI krever `Text` (f.eks. `ctx.draw(Text(...))` i `Canvas`) må `.font(...)` brukes.
- `scaleEffect` flytter ikke AppKit-kontrollers treff-områder; bruk skalerte fonter/mål for zoom.
- macOS husker app-ikoner: etter nytt ikon, `touch /Applications/ShowCue.app && killall Dock`. Appen setter også ikonet selv ved oppstart.
- Kjøring fra Xcode gir ingen app-pakke (ikonet hentes da fra ressurspakken ved siden av programfila).
- ⌘0 er brukt («gå til»-cue); Option-tastene gir spesialtegn på norsk tastatur.
- En metode med standardargument (`go(explicitStart: Bool = false)`) oppfyller ikke en protokollkrav `go()` og kan ikke brukes som `() -> Void` (`Button(action: model.go)`). Lag en egen overload uten argument og bruk lukning i knappen.
- ⌘R er brukt av øvelsesmodus (Windows: Ctrl+R).

- SwiftUI: en `ViewBuilder`-blokk tar høyst 10 elementer. En `switch` med mange felt per `case` (inspektørfanene) må gruppere i `VStack`.

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

## Scener
- **Filtrert liste og valgt cue:** i WPF setter `DataGrid` `SelectedItem` til null når valgt rad skjules av filteret. Bytte av scene og redigering av scenelista må skje med `_switchingScene` satt, ellers nullstilles GO-markøren til scenen (og «slutt på lista» vises). Sett valgt cue eksplisitt etterpå.
- **Scenenavn i mållister:** `:`, `,`, `;`, `/` og `\` er skilletegn i mål og OSC-adresser og fjernes fra navn (`SceneLogic.CleanName`). Et cuenummer som selv inneholder «:» finnes fortsatt som vanlig nummer hvis scenen før kolonet ikke har cuer.
- Nye cuer, innlimte og flyttede cuer må alltid få `Scene` = aktiv scene (`InsertCues`/`insert`), ellers havner de i en scene som ikke vises.

## Klokke og tid
- `DateTime`-subtraksjon på lokale tidspunkter regner i veggklokketid og ser ikke sommertid: et hopp fra 01:59:59 til 03:00:00 gir «ett sekund». `ClockTriggers.Crossed` sammenligner derfor også faktisk tid (UTC), og Mac sammenligner tidssonens forskyvning før og etter.
