# Endringslogg

Alle endringer som merkes av brukere, nyeste først. Versjonsnummeret står i fila `VERSION`.
Hver versjon får en egen utgivelse på GitHub (`v0.5.0` osv.) med Windows- og Mac-programmet.
Utgivelsen «siste» bygges på nytt ved hver endring og viser det nyeste, også det som ikke er gitt ut ennå.

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
