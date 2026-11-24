# Beslutninger

Viktige valg og begrunnelsen. Nyeste først.

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
- Gruppe-cuer finnes ikke i showformatet ennå, så «gruppe utenfor hoved-lista» er ikke sjekket.
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
