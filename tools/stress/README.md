# Stresstest av programkjernen

Kjører den delen av Windows-koden som ikke trenger WPF (modeller, lydmikser, avspilling, OSC/MIDI/MSC,
tidskode, DMX, nettverks-cuer, autolagring) om og om igjen med tilfeldige og ødelagte data.
Virker på Windows, macOS og Linux. CI kjører én runde ved hver pull request.

```
dotnet run -c Release --project tools/stress -- [minutter] [filter]
```

- `minutter` – hvor lenge den skal gå (0 = én runde, ca. 45 s). Hver runde bruker nye tilfeldige data (frø = runde × 7919).
- `filter` – kjør bare testene med dette i navnet, f.eks. `Playback`.
- Uten NuGet-tilgang: `-p:NAudioDll=/sti/til/NAudio.dll`.

Feil skrives samlet til `stress-report.txt` ved siden av programmet, med et eksempel per feiltype. Henger en test i mer
enn 3 minutter, avbrytes kjøringen og saken står i `hang.txt`. Minnebruken skrives etter hver runde.

| Test | Hva den gjør |
|---|---|
| DmxLevels, Timecode, OSC decode/arguments, MSC parse, Network unescape, Timecode decode | Tilfeldig og ødelagt tekst/bytes inn i tolkerne – skal aldri kaste |
| Remote OSC routing | Tilfeldige OSC-adresser og argumenter mot fjernstyringen |
| Show roundtrip / mutation / save-load files | Tilfeldige show lagres og åpnes (skal bli identiske), og ødelagte showfiler åpnes |
| Playback edge cases | Avspilling med ekstreme verdier (NaN, uendelig, tomme filer, ett sample, loop), fades, pause, spoling |
| Mixer concurrency | 8 s lydtråd og kontrolltråd samtidig: start/stopp, routing, patch, fades, spoling |
| Show log flood | Seks tråder fyller showloggens begrensede kø med tilfeldig tekst: ingen feil, filen under taket, gamle filer slettet, filen kan leses inn igjen, CSV-felt med formeltegn får apostrof |
| Loudness extremes, Waveform, LTC source, OSC feedback, Dmx fader, Undo history, Safety/Media tools paths | Grensetilfeller |
| Preload planning | Tilfeldige cuelister (gå til-løkker, grupper, deaktiverte) mot planleggeren for forhåndslasting og regnskapet: aldri unntak, aldri over grensen, ingen doble |
| Network sender | UDP/TCP/HTTP mot lokale mottakere og ugyldige adresser – skal aldri henge eller kaste |
| Scenes x rehearsal | Tilfeldige cuelister med scener, deaktiverte og merkede cuer: GO gjennom hver scene med og uten øvelsesmodus. Markøren går alltid framover, auto-fortsett forlater aldri scenen, og forhåndslastingen planlegger bare det GO starter |
| Scene operations | Tilfeldig legg til / nytt navn / slett / flytt / nummerer om scener: reglene gjelder etter hvert steg, og showfila får samme innhold etter åpne + lagre |
| Show file new fields | Scener, routingforhåndsvalg, klokketrigger og «Hopp over i øvelse»: rundtur, gamle filer uten feltene og ødelagte verdier |
| Routing presets | Tilfeldige operasjoner på forhåndsvalg (NaN, uendelig, utenfor grensene), ruter fra forhåndsvalg, kopier/lim inn |
| Clock triggers | Tolker med tull, nøyaktig én utløsing over midnatt, aldri utløsing ved hopp i klokka (sommertid, stilling, dvale) |
| Settings | Innstillingsfil med feil typer og ekstreme tall: åpnes eller avvises, og grensene gjelder etter `Normalize` |
| ASIO input | Planlegging med ekstreme tall, kanalomforming, driverhendelser med NaN/uendelig/feil kanalantall mens lyttere åpnes og lukkes |
| Show log dispose | Avslutning av loggen mens fire tråder skriver, og disk som feiler (meldes én gang) |
| Web server sockets | Nettsiden mot ekte sockets: ødelagte og for store forespørsler, feil PIN og sperre, 60 samtidige og 10 trege forbindelser, kommando som kaster, stopp med åpne forbindelser, omstart på samme port (tar ca. 20 s) |
