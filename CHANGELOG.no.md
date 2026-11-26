# Endringslogg

*[English](CHANGELOG.md)*

Alle endringer som merkes av brukere, nyeste først. Versjonsnummeret står i fila `VERSION`.
Hver versjon får en egen utgivelse på GitHub (`v0.5.0` osv.) med Windows- og Mac-programmet.
Utgivelsen «siste» bygges på nytt ved hver endring og viser det nyeste, også det som ikke er gitt ut ennå.

## 0.27.1 – 2026-11-26

### Rettet
- **Gå til scene** (Windows og Mac): en «Gå til»-cue som bare oppgir en scene (uten cuenummer) svarte alltid «fant ikke cue» og byttet aldri scene. Nå bytter den scene som tiltenkt.
- **Gå til-løkker** (Windows og Mac): «Gå til»-cuer som starter hverandre uten pause (for eksempel to cuer som peker på hverandre) holdt programmet opptatt for alltid. Etter 25 hopp på rad uten pause stoppes kjeden, med melding i statuslinja og en feil i showloggen. Vanlige løkker med lyd eller ventetid imellom påvirkes ikke.
- **Forhåndslasting følger GO** (Windows og Mac): planleggeren holder seg i scenen til GO-markøren (auto-fortsett fører aldri inn i en annen scene), følger en «Gå til»-cue til målet i riktig scene, og hopper over cuer merket «Hopp over i øvelse» når øvelsesmodus er på. Å slå øvelsesmodus av eller på oppdaterer forhåndslastingen med en gang. Dette gjelder bare hvilke filer som forberedes på forhånd.
- **Nettside med scener** (Windows og Mac): cuelista og posisjonen («3 / 12») på nettsiden viser nå aktiv scene. Før ble et cuenummer som brukes i flere scener utheva i alle.
- **Live-inngang** (Windows og Mac): en driver som leverer NaN eller uendelige samples kan ikke lenger sende dem videre til mikseren; de blir til stillhet.
- Klokke-triggere: cuer uten trigger tolkes ikke lenger ved hvert tick (16 ms).

### Tester
- Stresstesten (`tools/stress`) har nye tilfeller for funksjonene som ble slått sammen i 0.27.0: scener sammen med øvelsesmodus, GO og forhåndslasting; redigering av scener; showfiler med de nye feltene (gamle filer, ødelagte verdier, rundtur); routingforhåndsvalg; klokke-triggere over midnatt og hopp i klokka; innstillinger med feil typer og ekstreme tall; planlegging av ASIO-innganger og driverhendelser; avslutning av showloggen og diskfeil; og nettsiden over ekte sockets (ødelagte og for store forespørsler, mange og trege forbindelser, sperre ved feil PIN, en kommando som kaster, stopp med åpne forbindelser, omstart på samme port).
- Nye enhetstester på begge plattformer (`InteractionTests`).

## 0.27.0 – 2026-11-25

### Nytt
- **Showlogg** (Windows og Mac, samme oppførsel): en logg over hva som skjedde under showet, til rapporter og feilsøking. Hver linje har klokkeslett (ISO 8601), tid siden programmet startet og hva som skjedde:
  cue startet (nummer, navn, type og hvordan: GO, auto-follow, auto-continue, hurtigtast, OSC, MIDI, tidskode, klokke, gruppe, knappepanel eller manuelt), cue ferdig eller stoppet, pause og fortsett, stopp alt (panikk), GO-markøren flyttet av brukeren,
  feil og advarsler (fil mangler, lydenhet mistet, feilen som vises på cuen), show åpnet og lagret, og programstart og avslutning.
- **Fil → Showlogg** (på Mac også *Verktøy → Showlogg*) viser hendelsene i denne kjøringen fortløpende, nyeste nederst, med filter (alle, bare feil og advarsler, cuer, styring, show og program).
  *Åpne eldre logg* viser en tidligere kjøring. *Eksporter CSV* og *Eksporter tekst* lagrer det som vises; CSV-fila er trygg å åpne i et regneark (felt som begynner med `=`, `+`, `-` eller `@` gjøres uskadelige).
- Én loggfil per kjøring i mappa `Logs` ved siden av `Autolagring` og `Sikkerhetskopier` (Windows `%AppData%\ShowCue\Logs`, Mac `~/Library/Application Support/ShowCue/Logs`). De 30 nyeste filene beholdes, og en fil slutter å vokse ved 5 MB.
- **Skriv showlogg** (*Innstillinger → Sikkerhet*) er **på** som standard. Loggingen bremser eller stopper aldri showet: hendelser legges i en kø og skrives av en bakgrunnstråd, og er køen full kastes de eldste (telles og vises i loggvinduet). Diskfeil meldes én gang og avbryter aldri showet.

## 0.26.0 – 2026-11-25

### Nytt
- **Nettside for fjernstyring** (Windows og Mac, samme oppførsel): en liten innebygd webtjener lar en mobil eller et nettbrett på samme nettverk styre showet fra nettleseren.
  Siden viser neste cue (nummer, navn, type), hva som spiller med tid igjen, og store knapper for **GO**, **Stopp alt**, **Pause/Fortsett**, **Neste** og **Forrige**, pluss en cueliste som bare kan leses.
  Siden bruker de samme styringene som OSC, så sperren mot dobbel-GO gjelder. Showmodus stopper den ikke (som med OSC), men ingenting kan redigeres utenfra.
- Innstillinger: *Innstillinger → Web* med av/på, port (standard 8088) og PIN-kode. Adressen (f.eks. `http://192.168.1.20:8088`) vises der og i statuslinja mens tjeneren går.
- **Sikkerhet:** nettsiden er **av** som standard og krever **PIN-kode** (6–32 bokstaver eller siffer; en tilfeldig lages når den er tom eller lett å gjette, som `123456`).
  Alt unntatt innloggingssiden krever PIN-koden (øktcookie etter innlogging), mislykkede forsøk begrenses med sperre, og tjeneren har bare faste ruter, ingen tilgang til filer, ingen CORS,
  grenser for størrelse/tid/antall forbindelser og bare HTTP/1.1 med lukkede forbindelser. **Trafikken er vanlig HTTP (ikke kryptert), så PIN-koden beskytter bare mot tilfeldig bruk på et nettverk du stoler på.**
  Siden er ennå ikke prøvd på ekte enheter.

## 0.25.0 – 2026-11-25

### Nytt
- **Øvelsesmodus** (Windows og Mac, samme oppførsel): hopp over valgte cuer under øvelser uten å slette eller deaktivere dem.
  - Nytt cuefelt **Hopp over i øvelse** (fanen *Tidsstyring*; lagres som `SkipInRehearsal`, av som standard, så gamle show åpnes uendret). Merkede cuer har en liten *ØV*-merking i cuelista.
  - **Slå modusen av og på** med Ctrl+R (⌘R på Mac), *Oppsett → Øvelsesmodus* eller knappen *Øvelse*. Mens den er på, viser et tydelig felt øverst i vinduet og vindustittelen det.
    Modusen **lagres aldri**: programmet starter alltid med den av.
  - Mens modusen er på, flytter **GO, auto-fortsett og auto-følg** seg forbi merkede cuer og starter dem aldri. Klokkeslett- og tidskode-triggere starter dem heller ikke.
    Å starte en merket cue direkte virker fortsatt: med cuenummer (OSC, MIDI show control), hurtigtast, MIDI-trigger, knappepanel, dobbeltklikk og som mål i en gruppe-cue.
  - **Sjekk showet** advarer mens øvelsesmodus er på og viser hvor mange cuer som er merket.

## 0.24.0 – 2026-11-25

### Nytt
- **Forhåndslast neste cue** (Windows og Mac, samme oppførsel; *Innstillinger → Sikkerhet*, **av** som standard til den er prøvd på ekte utstyr).
  Når den er på, forbereder ShowCue cuen GO-markøren står på, og cuene den starter automatisk etterpå (høyst tre filer om gangen), i en bakgrunnstråd så snart markøren flytter seg:
  lydfiler åpnes og de første sekundene leses på forhånd (Mac: dekodes inn i ringbufferen), lydsporet i en video åpnes på samme måte (Mac laster også videofilas egenskaper), og bilder dekodes.
  «Gå til»-cuer følges til målet, og gruppe-cuer forbereder sine aktive mål. Deaktiverte cuer hoppes over, slik GO gjør.
  Forberedelsen skjer aldri i lydtråden. Filene slippes når markøren flytter seg, ved stopp, når et show åpnes og ved avslutning. Feiler forberedelsen, eller er fila endret, starter cuen akkurat som før.
  Merk: gevinsten i starttid er ikke målt; prøv den på ekte utstyr før du stoler på den.

## 0.23.0 – 2026-11-25

### Nytt
- **Routingforhåndsvalg** (Windows og Mac, samme oppførsel): routingmatrisen per cue fantes fra før; nå kan du sende en cue til f.eks. hovedhøyttalerne eller monitorene med ett klikk, uten å redigere matrisen for hånd.
  Knappen **Forhåndsvalg** under matrisen har:
  - **Navngitte forhåndsvalg som lagres i showet** (f.eks. «Hoved» = utgang 1-2, «Monitor» = 3-4, «Hoved + monitor» = 1-2, 3-4). Et forhåndsvalg er en liste med utgangspar, eventuelt med nivå i dB per par («3-4:-6»), så det passer filer med hvilket som helst antall kanaler: mono går til begge utgangene i hvert par, stereo går venstre/høyre, og flerkanalsfiler summeres annenhver til venstre/høyre med −3 dB. Lag, endre (også nytt navn) og slett fra menyen.
  - **Bruk på alle valgte cuer:** et valgt forhåndsvalg settes på alle valgte cuer som har lyd, og statuslinja sier hvor mange som ble endret og hvor mange som ble hoppet over (ingen lyd, eller ingen utgang i patchen som forhåndsvalget passer til).
  - **Kopier routing / Lim inn routing** mellom cuer, og **Bruk denne routingen på alle valgte cuer**. Kanaler og utganger som mottakeren ikke har, blir utelatt.
  - **Oppsummering av routingen over matrisen:** navnet på forhåndsvalget som passer, «Standard», «Av» eller «Utgang 1-2, 5».
- Showfil: det nye valgfrie feltet `RoutingPresets` (liste med `Name` og `Pairs` med `Bus` og `GainDb`). Eldre filer åpnes som før; eldre versjoner ignorerer feltet. Et skadet felt hindrer aldri at showet åpnes.
- Ingenting av dette kjører i lydtråden: forhåndsvalgene endrer bare routingen til cuen (på hovedtråden), og cuer som spiller følger med nøyaktig som når matrisen redigeres for hånd. Det endrer ikke avspillingen og har ingen innstilling som må slås på.

## 0.22.0 – 2026-11-25

### Nytt
- **Scener (cuelister)** (Windows og Mac, samme oppførsel): et langt show trenger ikke lenger være én lang liste. Cuer hører til en *scene* (f.eks. «Akt 1», «Akt 2»), og hver scene har egen nummerering, egen GO-markør og egen GO-rekkefølge.
  Velg aktiv scene i velgeren over cuelista (◀ ▶ går mellom scener; **Ctrl+Page Up/Down** på Windows, **⌘⌥←/→** på Mac). Menyen *Scener* lager, gir nytt navn, sletter og flytter scener, og flytter valgte cuer til en annen scene.
- **GO hopper aldri til en annen scene av seg selv.** Ved slutten av en scene stopper markøren på «slutt på lista»; auto-fortsett/auto-følg holder seg også innen scenen. Hver scene husker hvor markøren sto.
- **Nummer per scene:** samme cuenummer kan brukes i flere scener. *Nummerer på nytt* gjelder den aktive scenen og oppdaterer mål i fade-, stopp-, gå til- og gruppe-cuer. Mål i fade-, stopp-, gå til- og gruppe-cuer søkes først i cuens egen scene og deretter i alle scener; skriv `Akt 2:5` for å peke på cue 5 i en bestemt scene.
- **Gå til scene:** en «gå til»-cue har et nytt felt *Scene*. Med bare scene byttes scenen (markøren står der den sist var); med nummer hoppes det til den cuen i den scenen. OSC: `/scene/{navn}` (eller `/scene "Akt 2"`) bytter scene. De eksisterende OSC-kommandoene søker først i aktiv scene.
- **Sjekk showet** godtar samme nummer i ulike scener, melder duplikater innen én scene og sjekker at scenene «gå til»-cuer bruker finnes.
- Showfil: nytt valgfritt felt `Scene` per cue og en liste `Scenes`. Show uten scener åpnes som én scene, og eldre versjoner åpner nye filer som én liste.

## 0.21.0 – 2026-11-24

### Nytt
- **Klokkeslett-trigger** (Windows og Mac, samme oppførsel): en cue kan starte av seg selv når klokka på maskinen passerer et valgt klokkeslett (tt:mm:ss, lokal tid), f.eks. «dørene åpner 19:00:00».
  Feltet *Klokkeslett-trigger* står i fanen *Tidsstyring*, og klokkeslettet vises i kolonnen for triggere i cuelista («Kl. 19:00:00»).
  Hver cue starter én gang når klokkeslettet passeres. Cuer startes ikke etter dvale, ved store hopp i klokka (mer enn 2 sekunder mellom to sjekker) eller ved omstilling til sommer-/vintertid.
- **Klokke-triggere aktive** (*Innstillinger → Sikkerhet*) slår funksjonen av og på. Den er **av** som standard, så ingenting starter uventet.
- **Sjekk showet** advarer når cuer har klokketrigger, men innstillingen er av.

## 0.20.0 – 2026-11-24

### Endret
- **Ny, tydeligere hovedvindu** (Windows og Mac, samme oppførsel; ingen funksjoner er fjernet, og snarveiene er uendret):
  - **Cuelista:** raden som *spilles nå* er grønn med fremdriftslinje og ny kolonne **Gjenstår** (tid igjen, «∞» uten kjent lengde).
    Cuen *GO starter* har blå kant og ▶. Deaktiverte cuer er dempet og regnes ikke som neste.
  - **Panelet for valgt cue** er delt i faner: *Grunnleggende*, *Lyd/media*, *Tidsstyring* og *Avansert*. Alle feltene er med, bare gruppert.
    Bare faner med innhold for cuetypen vises (typer uten lyd, bilde eller video har ingen Lyd/media-fane). Fanen beholdes når du velger en annen cue av en type som har den.
  - **Toppfeltet:** større GO-knapp med «Mellomrom», og *Neste cue* i større skrift med type og blå kant.
  - **Aktive cuer:** tid igjen i stor, grønn skrift, og mer plass i bunnpanelet.
## 0.19.1 – 2026-11-24

### Rettet
- **Windows: en cue som feiler midt i et show** gir ikke lenger en ny feilmelding hvert 16. millisekund. Cuen får feil, stoppes og tas ut av lista, og feilen loggføres én gang; de andre cuene spiller videre.
- **Mac: verdier i showfila begrenses ved lasting**, som på Windows (nivå −60…+12 dB, tider 0–86 400 s, porter 1–65 535, MIDI-verdier, universe, antall kanaler). En ødelagt eller håndredigert fil kan ikke lenger gi enorme eller ugyldige verdier.
- **Sjekk showet kjenner gruppe-cuer** (Windows og Mac): tom mål-liste er feil, mål som ikke finnes er feil, deaktiverte mål gir advarsel, og en gruppe som ikke treffer noen cue er feil.
- **Innstillinger begrenses ved lasting** på Windows og Mac (porter, MIDI-kanal, dobbel-GO-sperre, forsinkelse, fade-tid for stopp alt, antall kanaler, knappepanelets kolonner, patchens nivå og kanaler m.m.).
- Antall kanaler i en lydfil begrenses til 1024 på Windows, så en ødelagt verdi ikke kan gi en enorm routingtabell.

## 0.19.0 – 2026-11-24

### Nytt
- **Sjekk showet** (*Fil → Sjekk showet*; på Mac også under *Verktøy*): en forhåndssjekk før forestillingen som viser en liste med feil, advarsler og ok.
  Den kontrollerer at alle mediefiler finnes og kan leses, at fade-, stopp- og «gå til»-cuer peker på en cue som finnes,
  at påkrevde felt er fylt ut og tider er gyldige, at valgt lydutgang, lydinngang og skjermer finnes, og at ingen cue-numre er like.
  Sjekken endrer ikke showet. Samme regler og tekster på Windows og Mac.

## 0.18.0 – 2026-11-24

### Nytt
- **Windows: live-innganger via ASIO (eksperimentelt).** Under *Innstillinger → Lyd* (ASIO) kan du slå på *Bruk ASIO-innganger for live-inngang*
  og velge første inngang og antall innganger. Inngangene hentes fra samme ASIO-økt som utgangen, så en inngang-cue kan bruke
  f.eks. et nettverkslydkort i ASIO-modus. Funksjonen er **av som standard** og er ikke prøvd på ekte ASIO-utstyr ennå;
  når den er av, virker live-inngang (WASAPI) og lydutgangen nøyaktig som før.
  Har driveren ingen innganger, eller kan den ikke åpne dem, spiller lyden videre uten innganger, og årsaken vises i lydstatusen.
- Mac trenger ingen endring: CoreAudio gir live-innganger uten at utgangen må deles.

## 0.17.0 – 2026-11-20

### Nytt
- **Gruppe-cue** (❖ *Styring → Gruppe*): starter flere andre cuer med én GO. Skriv cuenumrene som skal startes, f.eks. `2, 5, 8-10`
  (`8-10` tar med alle cuer med et tall fra 8 til 10). Hver cue i gruppen følger sin egen pre-wait og fortsett-innstilling,
  og deaktiverte cuer hoppes over. Finnes en cue ikke, startes de andre og gruppen viser hvilken som mangler.
  En gruppe kan ikke starte seg selv, og grupper som starter grupper stopper på åtte nivåer.
  *Nummerer* oppdaterer numrene i gruppene, også i endene av et område.

## 0.16.1 – 2026-11-24

### Rettet
- **Ødelagte showfiler:** på Windows gjør én ugyldig verdi (tekst i et tallfelt, ukjent valg) ikke lenger hele showet uåpnelig; feltet får standardverdi og resten av cuen leses som vanlig.
  Tomme oppføringer («null») i cuelista, filllister og routing hoppes over på begge plattformer, og et show uten navn eller cuer åpnes tomt.
- **Cue uten gyldig type:** Mac gjetter ikke lenger «Stopp» for en cue som mangler eller har ukjent type (kunne stoppe alt ved GO); filen avvises med melding, som på Windows.
- **OSC inn (Windows):** en blob med enorm lengde i en pakke kunne gi ugyldig posisjon og få resten av meldingen til å gå tapt.
- **Lys (Windows):** «NaN» og «Infinity» godtas ikke lenger som DMX-nivå (Mac avviste dem fra før).
- **Nettverks-cue:** `\xHH` tolkes bare med to ekte sifre, likt på Windows og Mac (Windows godtok mellomrom, Mac godtok fortegn).
- **Avslutning (Windows):** én feil i oppryddingen hindrer ikke lenger at lydutgangen frigjøres og programmet lukkes.

## 0.16.0 – 2026-11-04

### Nytt
- **Lysbildeserie:** en ny cue-type som viser bilder, PDF-sider og videoer etter hverandre på én skjerm, som en presentasjon.
  Legg til filer under *＋ Media → Lysbildeserie* (Ctrl+Shift+B / ⇧⌘B) og sorter dem med ↑ / ↓.
  Bilder og PDF-sider vises i valgt tid (*Tid per bilde*), hver side i en PDF blir et eget lysbilde, og videoer spiller til de er ferdige.
  Lysbildene krysstones, serien kan starte forfra etter siste lysbilde (loop) og spilles i tilfeldig rekkefølge.
  Slå av *Bytt automatisk* for å bla manuelt.
- **Bla i lysbildeserien:** ⏮ / ⏭ under *Aktive cuer*, eller OSC `/cue/{nr}/next` og `/cue/{nr}/previous`.
- **Skjermbilder i manualene** (README for Windows og Mac, norsk og engelsk), tatt av programmet selv.

### Rettet
- **Windows: nedtrekkslister** for lydenhet, skjerm og MIDI-kanal viste «[nøkkel, navn]» i stedet for bare navnet når de var lukket.
- **Windows: fillista** i spilleliste og lysbildeserie var hvit i mørkt tema; den følger nå temaet.
- **Typekolonnen i cuelista** er bredere, så «Lysbildeserie», «Spilleliste» og «Beskjed» ikke kuttes (Mac: ikke delt over to linjer).

## 0.15.0 – 2026-11-03

### Nytt
- **Skjerm per cue:** video-, bilde-, PDF- og nettside-cuer kan vises på en annen skjerm enn standard (eller i eget vindu).
  Velg under *Skjerm* i panelet til høyre; *Standard* bruker skjermen fra *Innstillinger → Video*.
  Krysstoning («Erstatt andre bilder/videoer») gjelder bare cuer på samme skjerm.

### Rettet
- **Mac: programmet lot seg ikke lukke etter at en video hadde spilt.** Utgangsvinduet holdt appen i gang når hovedvinduet ble
  lukket, og spørsmål ved avslutning kunne havne bak et utgangsvindu i fullskjerm. Nå avslutter lukking av hovedvinduet programmet
  (med de vanlige spørsmålene), og utgangsvinduene skjules mens spørsmålene vises.
- **Windows: sikrere avslutning.** Hvert steg i oppryddingen kjøres for seg, alle utgangsvinduer lukkes, programmet avsluttes når
  hovedvinduet lukkes, og et sikkerhetsnett avslutter prosessen hvis en lyd- eller videodriver skulle henge.

## 0.14.0 – 2026-11-03

### Nytt
- **Spoling i sanntid:** lyd- og video-cuer som spiller har en spolelinje under *Aktive cuer*. Dra for å spole,
  eller hopp 5 sekunder med ⏪ / ⏩. Bilde og lyd i video følger hverandre, og det virker også under pause.
  Spoler du tilbake fra den automatiske fade ut-en mot slutten, kommer nivået tilbake.
- **Media-cuer uten fil:** *＋ Media* lager en tom lyd-, video-, bilde-, PDF- eller spilleliste-cue med en gang.
  Velg fila senere med *Bla*, eller slipp en fil på cuen. En cue uten fil viser «Ingen fil valgt» hvis den kjøres.

## 0.13.1 – 2026-11-03

### Rettet
- **Logoen vises i Dock og på oppgavelinja:** Mac-appen har nå ikonet som ressurskatalog (slik nyere macOS vil ha det)
  og setter det selv ved oppstart. Windows-ikonet er laget på nytt i klassisk format for alle størrelser, og settes på alle vinduer.
- **Lydavspillingen kunne henge** (programmet fryse) ved en loop kortere enn ett sample, f.eks. med start- og sluttmarkør nesten oppå hverandre.
- **Ugyldige tall** (NaN/uendelig) i tider og nivåer ga feil lyd og gjorde at showet ikke kunne lagres; på Mac kunne appen krasje.
- **Ødelagte OSC-pakker** fra nettverket kunne gi feil (Windows) eller krasj (Mac).
- Status ut over OSC tåler svært lange tider; autolagring og sikkerhetskopier tåler svært lange filnavn.
- «Normaliser lydstyrke» setter ikke lenger −60 dB hvis målingen er ugyldig.

## 0.13.0 – 2026-11-02

### Nytt
- **Engelsk språk:** hele brukerflaten finnes på norsk og engelsk. Velg under *Innstillinger → Utseende → Språk*;
  standard følger språket i operativsystemet. Det nye språket brukes neste gang ShowCue startes.
- Engelske versjoner av README-ene og endringsloggen (`README.md`, `CHANGELOG.md`).
- **Grått tema** («Grå (dempet lys)»): lyst, men mindre hvitt enn Lys – skånsomt for øynene i lange økter.

### Endret
- Tekstene i programmet er gått over på begge språk: «cuer» brukes overalt, utdaterte hint (☀-knappen, menyplassering) er rettet.
- Mac: programikonet vises alltid i Dock, også når appen startes fra Xcode eller macOS har et gammelt ikon lagret.

## 0.12.0 – 2026-11-02

### Nytt
- **Zoom:** gjør hele hovedvinduet større eller mindre (80–200 %) med Ctrl/⌘ og + / −, fra *Oppsett*-menyen
  eller under *Innstillinger → Utseende*. Valget huskes.
- **Tekst i cuelista:** Vanlig, Stor eller Ekstra stor – gjør bare cuelista større, i tillegg til zoom.

### Endret
- **Knappepanel** (tidligere «Cart»): store knapper som starter og stopper cuer direkte uten å flytte GO-markøren.
  Avkrysningen i cue-panelet heter nå «Vis i knappepanelet». Show lagret med eldre versjoner må krysses av på nytt.
- **Beskjed-cue** (tidligere «Memo»): viser en beskjed til operatøren i toppfeltet.
- **Utstyr** (tidligere «Signal ut»): menyen for lys, miksere, videoservere og andre programmer.
- Menyvalg og knapper har ikke lenger «…» på slutten.

## 0.11.1 – 2026-11-02

### Nytt
- **Ny logo** (selburose med GO-symbol): programikon på Windows og Mac, i installasjonsprogrammet og under «Om ShowCue».

## 0.11.0 – 2026-11-02

### Nytt
- **Nettside-cue** (◍, Ctrl+Shift+W / ⇧⌘W): viser en nettside eller lokal .html-fil fullskjerm på utgangen,
  med fade inn/ut og visningstid som et bilde. Windows bruker Microsoft Edge WebView2 (innebygd i Windows 10/11).

### Endret
- **Ryddigere verktøylinje:** knappene er samlet i nedtrekksmenyer – *Fil*, *＋ Media*, *Styring*, *Signal ut*,
  *Rediger* og *Oppsett*. Angre/gjør om, Cart og Showmodus er fortsatt egne knapper. Alle hurtigtaster er de samme.

## 0.10.1 – 2026-11-02

### Nytt
- **Om ShowCue** (knappen «Om» på Windows, ShowCue-menyen på Mac): versjon, opphavsrett, at programmet leveres uten
  garanti, og lisensene.
- `THIRD-PARTY-NOTICES.txt` med lisensene for komponenter fra andre (NAudio, .NET) og merknaden for Art-Net.
  Følger med i installasjonsprogrammet, i Mac-appen og diskbildet, og i hver utgivelse.

## 0.10.0 – 2026-11-02

### Nytt
- **Tidskode-cue** (⏱ TC): sender MTC til en MIDI-utgang og/eller LTC som lyd på en utgang i lydpatchen,
  fra et valgt starttidspunkt (24, 25, 29,97 drop frame eller 30 fps). Pause stopper tidskoden.
- **Tidskode-trigger:** hver cue kan starte når innkommende MTC passerer et tidspunkt (slå på MTC inn under Innstillinger → MIDI).
- **Lys-cue** (✺ Lys): fader DMX-kanaler over Art-Net, f.eks. `1@255 2-4@50%`, med fade-tid. Å stoppe cuen lar lyset stå.
- **Live-inngang** (◉ Inn): mikrofon eller linje inn gjennom lydpatchen, med nivå, fades og routingmatrise.
  Inngangen velges under Innstillinger → Lyd.

## 0.9.0 – 2026-11-01

### Nytt
- **Cart-vindu** (▦ Cart, Ctrl+K / ⌘K): store knapper for berøringsskjerm med farge, nummer og fremdrift.
  Viser cuer med hurtigtast og cuer merket «Knapp i cart-vinduet». Trykk for å starte, trykk igjen for å stoppe.
- **Finn manglende mediefiler:** når et show åpnes og filer mangler, kan du velge en mappe å lete i (undermapper tas med).
  Også under *Verktøy*. Gjelder lyd, video, bilde, PDF og spillelister.
- **Samle show:** kopierer showet og alle mediefilene til én mappe (`Media/`), klar til å flyttes til en annen maskin.
- **Nettverks-cue** (⇄ Nett, Ctrl+Shift+N / ⇧⌘N): send tekst over UDP eller TCP, eller en HTTP-forespørsel
  (GET/POST/PUT/DELETE). Til projektorer (PJLink), mediaservere, PTZ-kamera, smarthus og lignende.
- **Memo-cue** (✎ Memo, Ctrl+Shift+M / ⇧⌘M): en beskjed til operatøren som vises stort øverst til den lukkes.
- **Normaliser lydstyrke:** måler lydstyrken (LUFS, EBU R128) mellom start og slutt og setter nivået så alle
  lyd-cuer høres like sterke. Per cue i panelet, eller for valgte/alle under *Verktøy*. Målet velges under Innstillinger.

### Endret
- «Nummerer» oppdaterer nå også mål i gå til-cuer.
- Spillelister finner også flyttede filer ved siden av showfila og i `Media`-mappa.

## 0.8.0 – 2026-11-01

### Nytt
- **Pause og fortsett alt** (Pause-knappen ved STOPP ALT, Ctrl+P / ⌘P, OSC `/pause`, `/resume`, `/pause/toggle`, MSC Resume):
  lyd, video, fader og pre-wait fryses og fortsetter der de var. GO fortsetter også.
- **Spilleliste-cue** (Ctrl+9 / ⌘9): flere lydfiler etter hverandre med krysstoning, tilfeldig rekkefølge og gjentakelse.
  Passer for innslipps- og pausemusikk. Går gjennom lydpatchen som vanlige lydcuer.
- **Gå til-cue** (Ctrl+0 / ⌘0): flytter GO-markøren til en annen cue, og kan kjøre den med en gang.
- **Slipp loop (devamp):** en stopp-cue kan slippe loopen i en lyd-, video- eller spilleliste-cue i stedet for å stoppe den.
  Cuen spiller ferdig runden og fortsetter til slutten av fila. Også fra inspektøren (⤼) og OSC `/cue/{nr}/devamp`.
- **Kjør herfra:** start en lyd- eller videocue fra markøren i bølgeformen, på de vanlige utgangene.
- **Fadekurver:** fade-cuer kan velge jevn (lineær i dB), S-kurve eller lineær amplitude.

## 0.7.0 – 2026-11-01

### Nytt
- **Showmodus** (Ctrl+L / ⌘L): låser all redigering under forestillingen. GO, stopp, hurtigtaster og fjernstyring virker som vanlig.
- **Angre og gjør om** (Ctrl+Z / Ctrl+Y, ⌘Z / ⇧⌘Z) for alle endringer i showet.
- **Autolagring** hvert 30. sekund, med tilbud om å hente tilbake ulagrede endringer etter krasj eller strømbrudd.
- **Sikkerhetskopier:** forrige versjon av showet kopieres hver gang du lagrer (de 20 siste beholdes).
- **Forhåndslytting til hodetelefoner:** velg egen lydenhet eller en utgang i lydpatchen for lyttingen i bølgeformen,
  så den ikke høres i salen. Bølgeformen advarer når lyttingen går til salen.

## 0.6.2 – 2026-11-01

### Endret
- Standard OSC-port for fjernstyring er nå **8000**, samme port som Companion-modulen «Generic OSC» sender til,
  så Companion virker uten å endre porten. Gjelder nye installasjoner; har du allerede lagret en port, beholdes den.
  Nye OSC-cuer sender også til port 8000 som standard.
- Intern opprydding: ubrukt kode fjernet og duplisert logikk slått sammen (ingen endring i hvordan programmet virker).

## 0.6.1 – 2026-11-01

### Endret
- ShowCue er nå fri programvare under **GNU GPL versjon 3** (se `LICENSE`). Installasjonsprogrammet viser lisensen.
- Windows-versjonen bruker **.NET 10** (støttet til november 2028). .NET 8 får ikke lenger sikkerhetsoppdateringer etter 10. november 2026.
- Windows-versjonen testes automatisk før den publiseres, slik Mac-versjonen allerede ble.
- Utgivelser publiseres først når både Windows og Mac er bygget og testet, og hver fil får sjekksum og opprinnelsesattest.
- «Siste bygg» peker nå på riktig versjon av kildekoden.

## 0.6.0 – 2026-11-01

### Nytt
- **Installasjonsprogram for Windows** (`ShowCue-Setup.exe`): snarvei i Start-menyen, valgfritt skrivebordsikon,
  `.showcue`-filer åpnes med ShowCue, og avinstallering fra Innstillinger → Apper. Kan installeres uten administratorrettigheter.
- **Diskbilde for Mac** (`ShowCue-Mac.dmg`): dra ShowCue til Programmer.
- Dobbeltklikk på en `.showcue`-fil åpner showet i ShowCue (Windows og Mac).

### Endret
- Riktig fremgangsmåte for å åpne appen første gang på nyere macOS (Systeminnstillinger → Personvern og sikkerhet → Åpne likevel).

## 0.5.0 – 2026-10-30

### Nytt
- **Redigering av start og slutt med bølgeform** for lyd- og video-cuer: dra den grønne (start) og røde (slutt)
  markøren, området utenfor er mørklagt, og fade inn/ut vises i bølgeformen.
- **Forhåndslytting gjennom lydpatchen**: fra start, fra markøren eller de siste 5 sekundene før slutt, uten å påvirke showet.
  «Start = markør» og «Slutt = markør» kan trykkes mens det spiller.
- Zoom (musehjul på Windows, knip eller −/+ på Mac) og et eget **stort redigeringsvindu**.
- **Video-cuer har start- og sluttpunkt.** Bilde og lyd starter på startpunktet og holdes i synk, og loop går mellom punktene.
- Versjonsnummeret vises i vindustittelen.

### Endret
- Panikk / Stopp alt stopper også forhåndslytting.

## 0.4.0 – 2026-10-29

Første utgave på GitHub, for både Windows og Mac.

- Cuetyper: lyd, video, bilde, PDF, fade, stopp, OSC og MIDI (inkl. MSC).
- Lydpatch for flerkanals lydkort (Dante, ASIO/CoreAudio) med routingmatrise per cue.
- Lyden i videoer går gjennom lydpatchen og holdes i synk med bildet.
- Fjernstyring med OSC, MIDI og MSC, og status tilbake til Companion over OSC.
- Lyst, mørkt og natt-tema, og fargemerking av cuer.
- Showfiler kan deles mellom Windows og Mac.
