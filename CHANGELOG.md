# Endringslogg

*[English](CHANGELOG.en.md)*

Alle endringer som merkes av brukere, nyeste først. Versjonsnummeret står i fila `VERSION`.
Hver versjon får en egen utgivelse på GitHub (`v0.5.0` osv.) med Windows- og Mac-programmet.
Utgivelsen «siste» bygges på nytt ved hver endring og viser det nyeste, også det som ikke er gitt ut ennå.

## 0.13.0 – 2026-11-02

### Nytt
- **Engelsk språk:** hele brukerflaten finnes på norsk og engelsk. Velg under *Innstillinger → Utseende → Språk*;
  standard følger språket i operativsystemet. Det nye språket brukes neste gang ShowCue startes.
- Engelske versjoner av README-ene og endringsloggen (`README.en.md`, `CHANGELOG.en.md`).
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
