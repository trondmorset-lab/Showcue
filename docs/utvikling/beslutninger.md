# Beslutninger

Viktige valg og begrunnelsen. Nyeste først.

## 0.26.0 – nettside for fjernstyring
- **Sikkerhet er hovedkravet.** Av som standard; PIN er påkrevd (6–32 bokstaver/siffer a–z, A–Z, 0–9, ikke ett tegn gjentatt, ikke på lista over lette koder) og lages tilfeldig (6 siffer, kryptografisk tilfeldighet) når den mangler eller er svak.
  Normaliseres ved lasting (`AppSettings.Normalize` / `normalize()`), og `WebRemoteCore` nekter å lages med en svak PIN, så tjeneren kan aldri starte uten godkjent PIN.
  Tegnsettet er begrenset med vilje: PIN-en trenger da ingen URL-dekoding, og tolkingen av innloggingen blir triviell.
- **Alt som ikke er nett eller tråder er rene funksjoner** med like tester på begge plattformer: `WebRemote` (tolking av HTTP-byte, PIN-regler, JSON, sider, svar) og `WebRemoteCore` (ruter, økter, sperre; tid, tilfeldighet og klientadresse kommer inn som parametre).
  Tjeneren (`WebServer`: Windows `TcpListener`, Mac `NWListener`) er bare lim: les byte med tidsfrist, kall `Handle`, skriv svaret, lukk.
- **Windows bruker `TcpListener`, ikke `HttpListener`:** `HttpListener` med jokertegn (alle nettverkskort) krever administrator/URL-reservasjon. Mac bruker Network-rammeverket.
- **Ruter:** bare `GET /` (innloggingsside eller kontrollside etter om forespørselen har gyldig økt), `POST /api/login`, `GET /api/state`, `GET /api/cues`, `POST /api/cmd`. Alt annet gir 404. Stien fra forespørselen blir aldri brukt mot disken (ingen filer serveres; sidene er strenger i koden), og tolkeren avviser `%`, `\`, `#` og `..` i stien på forhånd.
- **PIN-kontroll:** øktcookie (`sc_session`, 128 bit tilfeldig, `HttpOnly; SameSite=Strict`, 12 timer, maks 32 økter) etter innlogging; i tillegg godtas overskriften `X-ShowCue-Pin` for skript. Sammenligning i konstant tid (`ConstantTimeEquals`, fast løkke på 64 byte; økter sammenlignes mot alle uten å stoppe tidlig).
  Sperre mot gjetting: 5 feil per klient på 60 s gir 60 s sperre (også for riktig PIN mens sperren varer), og 30 feil totalt på 10 min gir 5 min sperre for alle nye innlogginger (dekker mange klienter). Eksisterende økter virker under sperre.
- **CSRF:** `POST /api/cmd` krever overskriften `X-ShowCue: 1` (kan ikke settes av en skjema-innsending fra en annen side, og CORS-forespørsel før sending besvares aldri) i tillegg til `SameSite=Strict`.
- **Grenser:** forespørselslinje 2048 byte, hoder 8192 byte og 32 stk, kropp 1024 byte, bare `HTTP/1.1`, bare GET og POST, ingen `Transfer-Encoding`, ingen doble hoder, bare ASCII og CRLF; 16 samtidige forbindelser (6 per klientadresse), 5 s tidsfrist per forespørsel, alltid `Connection: close`. Ingen CORS-hoder. `Content-Security-Policy` tillater bare innebygd skript/stil og kall mot samme opphav.
- **Bruker de samme styringene som OSC:** `RemoteControl.HandleWeb` (Windows) og `RemoteRouter.handleWeb` (Mac) kaller `IShowControl`/`ShowControl` (`Go`, `StopAll`, `SetPaused`, `MovePlayhead`), så sperren mot dobbel-GO og loggen gjelder. Ingen ny styringslogikk. Showmodus blokkerer ikke (som OSC), men ingen rute kan endre showet.
- **Tråder:** tjeneren går på egne tråder (trådpoolen / en egen kø) og venter aldri på UI-tråden. Kommandoer sendes asynkront til UI-tråden (`Dispatcher.BeginInvoke` / `DispatchQueue.main.async`). Tilstanden bygges av hovedvinduet i den vanlige takten (ca. 4 ganger i sekundet; cuelista ca. 1 gang i sekundet) til ferdige JSON-tekster som tjenerens tråder bare leser. Alle unntak fanges.
- **Sidene** er én HTML-streng hver (innlogging og kontroll) med inline CSS/JS, uten eksterne ressurser. Tekstene settes inn fra `PageTexts` via språksystemet (`Loc.T`/`L`, `{{navn}}` i malen, alt HTML-escapet), så siden følger programmets språk (valgt ved oppstart). Skriptet bruker bare `textContent` (aldri `innerHTML`). Siden henter `/api/state` hvert 500. ms og `/api/cues` hvert 3. s.
- **Ikke gjort:** kryptering (vanlig HTTP; krever sertifikat som ikke kan håndteres uten oppsett), brukernavn/flere PIN-koder, redigering utenfra. Trafikken kan avlyttes på samme nett; PIN-koden beskytter bare mot tilfeldig bruk på et nettverk man stoler på.

## 0.25.0 – øvelsesmodus
- **Merking per cue, bryter i kjøretid.** `SkipInRehearsal` lagres i showfila (valgfri, standard av, gamle show åpnes uendret), men selve modusen er bare i minnet og er **alltid av ved oppstart**, også etter at et show er åpnet med merkede cuer.
  Den lagres hverken i showfila eller innstillingene, så den kan ikke «henge igjen» til forestillingen. Den ligger på motoren (`RehearsalMode`), ikke i showet.
- **Umulig å glemme:** et oransje felt øverst i hovedvinduet (med «Slå av»-knapp) står så lenge modusen er på, knappen i verktøylinjen viser «Øvelse PÅ», vindustittelen sier ØVELSESMODUS, og «Sjekk showet» gir en advarsel.
  Modusen følger programmet, ikke showet: åpnes et annet show, står den på til brukeren slår den av (banneret er synlig hele tiden).
- **Hva hoppes over:** alt som «kjører videre av seg selv» – GO (også «gå til» med GO), auto-fortsett, auto-følg, og klokkeslett- og tidskode-triggere (ingen trykker noe, så merkede cuer skal ikke starte). Markøren flyttes forbi merkede cuer på samme måte som forbi deaktiverte.
  En merket cue som står under markøren når modusen slås på, flyttes ikke: neste GO hopper forbi den (som for deaktiverte), så valget i inspektøren forsvinner ikke uventet.
- **Hva som fortsatt starter en merket cue (eksplisitt valg):** cuenummer (OSC `/cue/{nr}/start|go`, MIDI show control med cuenummer), hurtigtast, MIDI-trigger, knappepanel, dobbeltklikk/«Test» og mål i en gruppe-cue.
  Begrunnelse: operatøren har da pekt på akkurat denne cuen, og målet med modusen er å slippe å fjerne cuer, ikke å stenge for dem. Ikke vurdert som farlig, men det er en bevisst avgrensning: skal en gruppe aldri trekke inn en merket cue, merk gruppen også.
  Deaktiverte cuer startes fortsatt aldri av GO, og `GoCue` på en deaktivert cue hopper videre som før.
- **Alle cuer merket / slutten av lista:** GO gjør ingenting (som for deaktiverte i dag), og markøren settes til slutten (ingen valgt) når ingenting spillbart er igjen etter en cue.
- **Preflight:** advarsel (`rehearsalOn`) når modusen er på, og en merknad med antall (`rehearsalFlagged`, alvorlighet ok, også når modusen er av). Deaktiverte cuer telles ikke. `PreflightCheck.Run/run` fikk en valgfri parameter, så eksisterende kall og tester er uendret.
- Ekstra tastatursnarvei: Ctrl+R (Windows) / ⌘R (Mac); ledig fra før på begge.

## 0.24.0 – forhåndslasting av neste cue
- **Hvor starttiden kommer fra (resonnert, ikke målt):** Windows åpner `AudioFileReader`/`MediaFoundationReader` først i `AudioActive.Start` (åpning, header, `CurrentTime`-søk, resampler) og første lesing av fila skjer i lydtråden (kald filbuffer). Mac lager `AVAudioFile`/`AVAssetReader`, ringbuffer (2 s) og dekoder 0,3 s på hovedtråden ved GO. Video: `MediaElement`/`AVPlayerItem` opprettes først ved GO og må åpne fila før bilde og lyd starter sammen; det er sannsynligvis den største delen for video. Bilder og PDF var allerede forhåndslastet for hele showet (`MediaCache`), så der gjør forhåndslasting lite.
- Planleggeren er ren (`PreloadPlanner.Plan`/`plan`): cuen GO starter (første ikke-deaktiverte fra markøren), så kjeden av cuer med fortsett automatisk/følg automatisk, «gå til» følges til målet (løkker og manglende mål stopper), gruppe gir sine aktive mål. Høyst 3 filer, ingen doble, bare lyd/video/bilde (PDF, nett, spilleliste, lysbildeserie forberedes ikke).
- Regnskapet (`PreloadBook`) fjerner det som ikke lenger ønskes (eldste først) og holder aldri flere enn grensen. Selve filene håndteres av `Preloader` (Windows `Engine/Preloader.cs`, Mac `Engine/Preloader.swift`), alltid utenfor lydtråden og UI-tråden; ingen endringer i selve lydløkkene (`Render`/mikseren).
- En forberedt fil er aldri påkrevd: `Take…` gir null når noe ikke stemmer (feilet, ikke ferdig, fila endret – størrelse/tid, annen samplingsfrekvens, start/slutt/loop), og da åpner cuen fila som før. Windows: leseren overleveres til `CuePlayback` (eierskap), og kaster konstruktøren, lukkes den og fila åpnes på vanlig måte. Mac: `StreamingSource` med 0,5 s i ringen overleveres; `Playback.start` hopper over egen fylling når ringen allerede har 0,3 s.
- **Ikke gjort:** forvarming av selve videospilleren (Windows `MediaElement` må ligge i utgangsvinduets visuelle tre; Mac `AVPlayer`). Mac laster bare `AVURLAsset`-egenskapene på forhånd, Windows leser filbufferen og åpner lydsporet. Det er første kandidat hvis målingen på ekte maskin viser at video fortsatt er treg.
- Innstillingen `PreloadNextCue`/`preloadNextCue` er **av** som standard (maskininnstilling) til eieren har prøvd den på ekte utstyr. Slippes ved stopp (også ved lydomstart), nytt show, relink, avslått innstilling og avslutning, fordi åpne filer ellers ikke kan byttes eller slettes.

## 0.23.0 – routingforhåndsvalg
- Routingmatrisen per cue fantes fra før (filkanal → utgang med dB). Det som manglet for en tekniker som vil sende en cue til hovedhøyttalere eller monitor raskt, var navngitte valg, å sette dem på flere cuer, kopiere/lime inn og en oppsummering. Det er det som er bygd; matrisen er uendret.
- Et forhåndsvalg er en liste med **stereopar** (`Bus` = utgang for venstre kanal, høyre går til neste) med valgfritt nivå per par, ikke en ferdig matrise. Dermed passer det filer med mono, stereo eller flere kanaler, og regelen for kanalene er den samme som i `AudioRoute.Downmix` (mono til begge, flerkanal annenhver venstre/høyre med −3 dB). Enkeltutganger (f.eks. en mono sub) dekkes ikke ennå; de kan stilles inn i matrisen.
- Forhåndsvalg lagres **i showet** (valgfritt felt `RoutingPresets`, PascalCase, alltid skrevet), ikke i maskininnstillingene: de beskriver hva showet vil, og følger med når showet flyttes. Utgangsnumrene er patch-utganger, og patchen er per maskin, så «3-4» betyr samme utgangspar på alle maskiner.
- «Sett på valgte cuer» hopper over cuer uten lyd og par som ikke får plass i patchen (`busCount`), og en cue blir aldri stum ved et uhell: gir forhåndsvalget/innlimingen ingen ruter igjen, røres cuen ikke og telles som «hoppet over».
- Lesing av `RoutingPresets` er tolerant på begge plattformer: feil type gir tom liste, en skadet oppføring hoppes over, navn/par/nivå begrenses (NaN → 0, ±uendelig → grensene). Et valgfritt felt skal aldri hindre at et show åpnes.
- Ingen innstilling som må slås på: funksjonen endrer bare `Routes` på cuene, på hovedtråden, og avspillingen følger med slik den alltid har gjort ved redigering av matrisen. Ingenting i lydtråden er rørt.
- Ikke gjort (bevisst smalt): forhåndsvalg i angre-historikken (setting på cuer angres, endring av selve lista gjør ikke), egen kolonne i cuelista (oppsummeringen står i inspektøren), forhåndsvalg i autolagringen på Mac (den lagrer bare cuene), OSC-kommando for å velge forhåndsvalg.

## 0.22.0 – scener (cuelister)
- **Én felles cueliste i filen, scene som felt på cuen.** Ny valgfri `Scene` (navn) per cue og `Scenes` (ordnet liste med navn) i showet; `TargetScene` på gå til-cuen. Cuene ligger fortsatt i én `Cues`-liste, så eldre versjoner ignorerer feltene og leser alt som én liste.
  Ingen scener = `Scenes` tom og alle `Scene` tomme = én scene uten navn (vist som «Hoved»). Første gang en ny scene lages får de eksisterende cuene navnet «Hoved». `ShowDocument.NormalizeScenes`/`normalizeScenes` retter opp ved lasting (manglende/ukjente navn, dubletter) og gir hver cue det kanoniske navnet; en cue uten scene havner i den første.
- **Ren logikk i `SceneLogic`** (Windows `Models/Scenes.cs`, Mac `Core/Scenes.swift`, tester `ScenesTests.cs`/`SceneTests.swift` med samme tilfeller): scenelista (legg til, nytt navn, slett, flytt), nummerering per scene (`NextNumber`, `Renumber`), GO-markør (`NextPlayhead`, `NextEnabledAfter`, `Step`, `NeighbourIndex`), oppslag av mål og «gå til»-mål. Filen ligger i `Models/`, så stresstesten (`tools/stress`) får den uten endring i `stress.csproj`.
- **Oppslag av cuenummer:** (1) `Scene:nummer` søker bare i den scenen (hvis scenen har cuer; ellers leses teksten som et vanlig nummer). (2) Et vanlig nummer søkes først i *hjemmescenen* og deretter i alle scener (første treff i lista).
  Hjemmescene = scenen til cuen som har målet (fade, stopp, gruppe, auto-fortsett) og aktiv scene for fjernstyring (OSC `/cue/{nr}/…`, `/playhead`, MSC) og for «gå til». Området «8-10» i en gruppe følger samme regel (hjemmescenen først, så alle). Uten scener er hjemmescenen alle cuer, så oppførselen er som før.
  Scenenavn kan ikke inneholde `: , ; / \` (renses bort), så navnene kan stå i mållister og OSC-adresser.
- **Nummerer på nytt** gjelder aktiv scene. Mål oppdateres: vanlige nummer hos cuer som peker inn i scenen, og `Scene:nummer` hos alle. Nytt navn og sletting av scene oppdaterer `Scene:`-mål og `TargetScene`. Slettede scener flytter cuene til scenen foran (ingen cuer slettes). Cuer som flyttes til en annen scene får nytt nummer hvis nummeret er opptatt der.
- **GO bytter aldri scene av seg selv:** markøren går til «slutt på lista» ved slutten av scenen, og auto-fortsett/auto-følg søker bare innen scenen. Bytte skjer med velgeren, Ctrl+Page Up/Down (Windows) / ⌥⌘←/→ (Mac), `/scene/{navn}` (OSC) eller en «gå til»-cue med scene (brukerens eget valg). Hver scene husker sin GO-markør.
  Hurtigtaster, MIDI-, tidskode- og klokketriggere er globale (virker på tvers av scener).
- **Visning:** cuelista viser bare aktiv scene (Windows `ListCollectionView` med filter, `VisibleCues`; Mac `visibleCues`). Valg av en cue i en annen scene (OSC, gå til, angre) bytter scene først. Inspektørfaner og `InspectorLayout` er uendret.
- **`IShowControl.SelectScene`/`ShowControl.selectScene` har standardimplementasjon** (usann), så stresstestens og eldre falske implementasjoner bygger uendret. «Sjekk showet» tar scenelista som valgfritt argument; samme nummer i ulike scener er lov, duplikat innen én scene er fortsatt feil (som før), og `TargetScene` må finnes (`sceneMissing`).
- Ikke gjort: egen scenevelger i knappepanelet, OSC `/scene/next`, og valg om at GO kan gå videre til neste scene automatisk (bruk en «gå til»-cue på slutten av scenen).

## 0.21.0 – klokkeslett-trigger
- Ny trigger per cue, `ClockTrigger` («tt:mm:ss», lokal tid, tom = av), ugyldig tekst blir tom (som `TimecodeTrigger`). Samme nøkkel og oppførsel på begge plattformer.
- Logikken er ren og tar tidspunktene som argument (`ClockTriggers.Crossed`/`Due` på Windows, `ClockTriggers.crossed`/`due` på Mac): «passert» betyr forrige < klokkeslett ≤ nå, så hver cue starter høyst én gang per passering.
  Intervall på mer enn 2 s, bakover eller null (dvale, klokkestilling), og intervall der UTC-forskyvningen endres (sommertid), starter ingenting. Bedre å gå glipp av en trigger enn å starte en cue uventet.
- Innstillingen «Klokke-triggere aktive» er **av** som standard (maskininnstilling, ikke i showfila), så et show som åpnes på en annen maskin aldri starter noe av seg selv. «Sjekk showet» advarer når cuer har klokketrigger og innstillingen er av.
- Cuene startes som ved tidskode-trigger (`Engine.Fire`/`engine.fire`), også i showmodus; ingen egen redigeringslås, siden tidskode- og MIDI-triggere heller ikke har det. Ikke lagt i `IShowControl`/`RemoteRouter` (ingen fjernstyring involvert), så stresstesten er uberørt.
- Sjekken kjøres i den vanlige takten (Windows `OnTick`, Mac `tick`), ikke med egen timer.

## 0.20.0 – nytt hovedvindu
- **Eierens godkjente forslag (mockup) er bygd innenfor de eksisterende temaene**: samme farger (`Running`/`RunningRow`, `Playhead`/`PlayheadRow`), samme font og samme kontroller. Ingen motor, showformat eller snarveier er endret.
- **Visningsregler er ren logikk med tester på begge plattformer** (`CueDisplay` og `InspectorLayout`, Windows `Models/CueDisplay.cs`, Mac `Core/CueDisplay.swift`, `CueDisplayTests`):
  radtilstand (spilles nå > deaktivert > neste > vanlig), «gjenstår»-tekst (m:ss rundet opp, «∞» uten varighet, tom før start) og hvilke faner en cuetype har.
  XAML og SwiftUI bruker resultatet; regelen står ikke to steder.
- **«Neste» er cuen GO starter.** En deaktivert cue hoppes over av GO, så den vises aldri som neste (den kan likevel være valgt for redigering, med nøytral markering).
- **Faner:** Grunnleggende (typens egne felt + *Deaktivert*), Lyd/media (bare typer med lyd, bilde eller video), Tidsstyring (pre-wait, etterpå, tidskode-trigger), Avansert (hurtigtast, MIDI-trigger, knappepanel, farge, notater).
  Nummer og navn står alltid over fanene. Testen `CommonFieldsKeepEveryFieldFromTheOldListExactlyOnce` sikrer at ingen felt forsvinner. Fanen beholdes ved bytte av cue hvis den finnes for den nye typen.
- **Windows: faneknappene er vanlige knapper med egen stil** (ikke `TabControl`, som ikke følger de mørke temaene uten en stor mal). Lyd/media-innholdet er egne `DataTemplate`-er i en `ContentControl` med lokale ressurser, bundet til `MediaCue` (null for typer uten fanen).
- **«Gjenstår» i lista** settes av `CueEngine.Tick` (Windows) / `tick()` (Mac) på `Cue.RemainingText`/`remainingText` (kjøretid, lagres ikke) og endres bare når teksten endres (ca. én gang i sekundet).
- Ikke gjort: egen fargestripe for cuetype (stripen i venstre kant er allerede brukerens cuefarge), fremdriftslinje over hele raden på Windows (står under navnet, som før), og beskjed-banneret dekker fortsatt «Neste cue» mens det vises.
## 0.19.1 – robusthet 2
- **Tidsløkka fanger unntak per aktiv cue** (`SafeTick.Run`, Windows `Engine/`). Feiler en cue: `Cue.Error` settes, én loggmelding, `Stop(0)` og `ForceRemove`. Når cuen er fjernet gjentas ingenting. `Ticked`-hendelsen fanges også og logges én gang.
  Swift har ingen unntak i tidsløkka, så Mac trenger ikke tilsvarende.
- **Grenser ved lasting er felles:** Mac bruker de samme grensene som Windows-modellen (`Cue.clampLoadedValues`, `AppSettings.normalize`); Windows har `AppSettings.Normalize` (offentlig, testet).
  Grensene i innstillinger følger feltene i innstillingsvinduet. Antall filkanaler er satt til maks 1024 (begge plattformer) fordi `AudioRoute.Default` lager én rad per kanal.
- **Gå til-cue som peker på seg selv er fortsatt bare en advarsel** (bevisst, uendret). Gruppe-cuer i «Sjekk showet»: tomt mål = feil (`targetEmpty`), mål som ikke finnes = feil (`targetMissing`),
  deaktiverte mål = advarsel (`targetDisabled`), gruppe uten treff (f.eks. bare seg selv) = feil (`groupNoMatch`). Gruppen unntas selv av `CueTargets.Resolve`.

## 0.19.0 – sjekk showet
- **Ren sjekklogikk adskilt fra GUI:** `PreflightCheck.Run` (Windows, `Models/`) og `PreflightCheck.run` (Mac, `Core/`) tar cuene, maskininnstillingene og en liten
  abstraksjon (`IPreflightEnvironment` / `PreflightEnvironment`: finnes filen, finnes lydutgangen/inngangen, finnes skjermen) og returnerer en liste funn
  med fast kode (`mediaMissing`, `targetMissing` …), alvorlighet og tekst. Testene bruker en falsk maskin, så de trenger ingen filer eller maskinvare.
  Den ekte maskinen er `SystemPreflightEnvironment` (Windows `Engine/`, Mac `Engine/`).
- Mediefiler hentes med `MediaTools.MediaPaths`/`mediaPaths` (samme logikk som «finn manglende mediefiler»), ikke en ny kopi.
- **Alvorlighet:** feil = showet går ikke som planlagt (fil, mål, lydutgang, påkrevd felt, duplikat nummer); advarsel = programmet går videre med en reserveløsning eller det er sannsynligvis en glipp
  (manglende skjerm vises i et vanlig vindu, deaktivert eller selvrefererende mål, tom beskjed/liste, manglende lytteutgang).
- Deaktiverte cuer hoppes over (GO hopper over dem), men numrene deres telles med for duplikater. Lydutgangen sjekkes bare hvis showet har cuer som lager lyd;
  tom enhets-ID betyr standardenheten og regnes som funnet. Stopp-cue uten mål betyr «stopp alt» og er gyldig.
- Gruppe-cuer ble lagt til i 0.19.1.
- Windows-modellen gjør ugyldige tall om til gyldige (NaN, negative tider), Mac-modellen gjør ikke det; sjekken har samme regel på begge og fanger tallene på Mac.
  Testene er derfor like unntatt dette ene tilfellet.
- Vinduet er minimalt (liste + «Sjekk på nytt») og følger eksisterende mønster: kodebygd vindu på Windows, eget `NSWindow` med SwiftUI på Mac.
  Hovedvinduet er ikke endret ut over ett menyvalg (en GUI-redesign er under godkjenning hos eieren).

## 0.18.0 – ASIO-innganger (Windows)
- **Innganger i samme økt som utgangen.** En ASIO-driver eies av én økt, så live-innganger kan ikke åpnes separat: `AudioEngine`
  bruker `InitRecordAndPlayback` og hører på `AudioAvailable`. Lyden går inn i de eksisterende `LiveRing`/`LiveInputStream`.
  Samme samplingsfrekvens som utgangen, så ingen omsampling.
- **Av som standard** (`AsioInputEnabled = false`), fordi funksjonen ikke er prøvd på ekte ASIO-utstyr. Er den av, kjører
  utgangssti og WASAPI-opptak nøyaktig som før (`asio.Init(...)`).
- **Ikke blokkerende i lydtråden.** `AsioInputFeed` lager alle buffere på forhånd (`PrepareBuffer`) og bytter hele lytterlisten
  (kopi ved endring) i stedet for å låse; en blokk som er større enn forventet forkastes og telles (`DroppedBlocks`).
  `LiveRing` har fortsatt sin lås, men i ASIO-løypa skriver og leser samme driver-tråd etter hverandre, så den er aldri i konflikt.
  Unntatt lydformat (f.eks. MSB-formater) gir `Failed` i stedet for unntak i driver-tråden.
- **Feil gir tekst, ikke krasj eller stille bytte:** kan ikke inngangene åpnes, startes økta på nytt med bare utganger (lyden
  spiller videre) og årsaken står i lydstatusen; en inngang-cue gir feilmelding i stedet for å bruke WASAPI-enheten.
- Logikk som kan testes uten maskinvare ligger i `AsioInputPlanner`, `ChannelMap` og `AsioInputFeed` (xUnit i `AsioInputTests`),
  også med lagde buffere i stedet for en driver.
- Mac: ingen endring; CoreAudio åpner innganger uavhengig av utgangen.

## 0.17.0 – gruppe-cue
- **Gruppen peker på cuer med nummer** (som stopp-, fade- og gå til-cuer), ikke på cuer inni seg. Lista forblir flat, og cuene i gruppen kan
  fortsatt kjøres og stoppes hver for seg. Ingen nytt lag av mapper å holde styr på i lista, i showfila eller i fjernstyringen.
- **Alle startes samtidig.** Rekkefølge og forsinkelse styres av hver cues egen pre-wait og fortsett-innstilling, så gruppen trenger ingen egne tidsregler.
- Leddene skilles med komma eller semikolon; `8-10` er et område av tall. Et cuenummer som selv inneholder bindestrek finnes først som det er.
  Lesingen ligger i `CueTargets` (samme på begge plattformer og testet likt).
- Gruppen kan ikke starte seg selv, og grupper som starter grupper stopper på 8 nivåer (`MaxGroupDepth`). To grupper som starter hverandre
  med pre-wait kan fortsatt gå i ring, men hver runde er et eget GO med forsinkelse.
- Manglende mål gir en feil på gruppen, men de andre starter. Deaktiverte mål hoppes over, som for GO.
- Typenavn i showfila: `group`, feltet heter `Targets`.

## 0.16.0 – skjermbilder i manualene
- Skjermbildene tas av appen selv på ekte Windows- og Mac-maskiner (arbeidsflyten «Skjermbilder»), med et demoshow
  som lages i koden. Da viser manualene alltid dagens brukerflate, og de kan lages på nytt med ett klikk.
- Demoshowet bruker nøytrale navn og adresser og lager sine egne filer (ingen tredjeparts innhold i bildene).

## 0.16.0 – lysbildeserie
- **Egen cuetype** i stedet for en «autokjør»-innstilling på hver bilde-cue: én cue i lista holder hele serien, og den kan stoppes,
  fades og blas i som én enhet. Lysbildene gjenbruker bilde-/video-avspillingen, så skalering, skjermvalg og videolyd virker likt.
- Lysbildene ligger ikke i lista over aktive cuer (serien styrer dem), så stopp-/fade-cuer treffer serien, og *Aktive cuer* viser én rad
  med «Lysbilde 3/10».
- PDF-sider blir egne lysbilder, så en eksportert presentasjon kan brukes direkte.
- Uten loop avsluttes serien etter siste lysbilde (auto-follow virker). Ved manuell blaing stopper serien aldri av seg selv.
- En bilde-/video-cue med «Erstatt andre» stopper også en lysbildeserie på samme skjerm, og omvendt.

## 0.15.0 – skjerm per cue og avslutning
- Skjermvalg ligger på cuen (følger showfila), med «Standard» som peker til innstillingene, så et show kan flyttes til en maskin med
  andre skjermer uten å endre hver cue. Finnes ikke skjermen, vises cuen i et vanlig vindu.
- **Mac: lukking av hovedvinduet avslutter appen.** Utgangsvinduet skjules bare når det lukkes, så appen ble stående uten synlig vindu.
  Lukkeknappen spør om avslutning før vinduet lukkes; ⌘W avslutter etterpå (og åpner hovedvinduet igjen hvis man avbryter).
  Utgangsvinduer skjules mens avslutningsspørsmålene vises (fullskjerm ligger over alt).
- **Windows:** `ShutdownMode=OnMainWindowClose`, hvert oppryddingssteg i egen `try`, og en vakttråd som avslutter prosessen etter 8 s
  hvis en driver henger.

## 0.14.0 – spoling og tomme media-cuer
- **Spoling i lydtråden/dekodertråden**, ikke fra UI: UI legger inn et ønske (`Seek`), tråden som eier fila utfører det.
  Ingen låser mot lydtråden, ingen klikk, GO påvirkes ikke. Virker under pause (behandles før pause-sjekken).
- Spoles det tilbake ut av den automatiske fade ut-en, rampes nivået tilbake til det det var før faden.
- Spolelinja viser målet i 0,3 s etter et drag, så håndtaket ikke hopper tilbake før lyden er der.
- Video: bilde og lyd flyttes sammen, synkroniseringen venter 2 s. `ScrubbingEnabled` på Windows så bildet vises under pause.
- **Media-cuer lages tomme** fra *＋ Media* (ingen fildialog). Fil via *Bla* eller ved å slippe én fil på cuen.
  «Legg til flere filer» skjer fortsatt ved å dra filer inn i lista.

## 0.13.1 – robusthet og ikon
- Verdier saneres både i modellen (settere) og i lydmotoren (forsvar i dybden): NaN/uendelig → 0, tider 0–86 400 s,
  nivå −60…+12 dB (lydmotoren tåler opptil +24). JSON kan ikke lagre NaN, så ugyldige tall ville ellers stoppet lagring.
- Minste loop 10 ms + vern mot gjentatt tilbakespoling uten fremdrift.
- Windows-ikon med BMP-bilder for små størrelser (PNG bare for 256 px); Mac-ikon også som ressurskatalog og satt ved oppstart.

## 0.13.0 – engelsk
- **Norsk tekst er nøkkelen** (ikke kunstige ID-er): koden forblir lesbar på norsk, og manglende oversettelse faller tilbake til norsk.
- Én felles fil (`i18n/en.json`) for begge plattformer; egen enkel oppslagsmekanisme (ikke `Localizable.strings`), så Mac og Windows oppfører seg likt og det virker også fra Xcode.
- Språkbytte ved omstart (enklest og trygt under show). Standard: systemets språk.
- **Grått tema** (dempet lys) lagt til etter ønske om noe mindre hvitt enn Lys. Lagres som `Gray` sist i enumen (rekkefølgen på eksisterende verdier endres ikke).

## 0.12.0 – brukerflate
- Cart → **Knappepanel** (forklarende navn). Memo → **Beskjed** (ikke «Notat», fordi *Notater* er et eget felt som vises ved NESTE CUE).
  Signal ut → **Utstyr**.
- Zoom: Windows bruker `LayoutTransform` (ekte zoom); Mac skalerer fonter og mål (`uiScale`), fordi `scaleEffect` ikke flytter AppKit-kontroller og treff-områder riktig.
- Ctrl/⌘ + og − for zoom; «vanlig størrelse» bare i menyen (Ctrl+0/⌘0 er «gå til»-cue, og Ctrl+Alt er AltGr på norsk tastatur).

## 0.11.x – verktøylinje og logo
- Verktøylinja samlet i nedtrekksmenyer (Fil, ＋ Media, Styring, Utstyr, Rediger, Oppsett). Angre/gjør om, Knappepanel og Showmodus er egne knapper.
- Nettside-cue: Windows WebView2 (følger med Windows 10/11), Mac WKWebView. Lyd fra siden går ikke gjennom lydpatchen.
- Logo: «selburose» med GO-symbol (nordisk mønster + teknologisk), mørk blå bakgrunn, grønt play-symbol.

## 0.10.x – lisens
- GPL-3.0 med eieren som opphavsrettshaver. `THIRD-PARTY-NOTICES.txt` med NAudio (MIT), WebView2 SDK (BSD, sjekkes ordrett i CI), .NET (MIT), Art-Net-merknad.
  Kodeker leveres av operativsystemet.
- Eieren kan senere tilby programmet på andre vilkår (dobbel lisens) for kode han selv eier; bidrag fra andre krever samtykke.

## 0.16.1 – robusthet
- Showfiler leses tolerant der det ikke gir tap av data: ugyldig verdi i ett felt gir standardverdi for det feltet (Windows som Mac), «null» i lister hoppes over.
  Ukjent eller manglende cuetype avvises derimot på begge plattformer, så en cue aldri blir noe annet enn den var.
