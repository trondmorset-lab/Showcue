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
| Loudness extremes, Waveform, LTC source, OSC feedback, Dmx fader, Undo history, Safety/Media tools paths | Grensetilfeller |
| Network sender | UDP/TCP/HTTP mot lokale mottakere og ugyldige adresser – skal aldri henge eller kaste |
