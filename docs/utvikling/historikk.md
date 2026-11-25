# Historikk

| Versjon | PR | Innhold |
|---|---|---|
| 0.23.0 | (åpen) | Routingforhåndsvalg: navngitte utgangspar i showet (`RoutingPresets`), sett på alle valgte cuer med ett klikk, kopier/lim inn routing, oppsummering av routingen i inspektøren. Ren logikk (`RoutingPresets`, `RouteClipboard`) med tester på begge plattformer |
| 0.21.0 | (åpen) | Klokkeslett-trigger per cue (`ClockTrigger`, «tt:mm:ss», lokal tid) med innstillingen «Klokke-triggere aktive» (av som standard), felt i Tidsstyring og advarsel i Sjekk showet. Ren logikk (`ClockTriggers`) med tester på begge plattformer |
| 0.20.0 | #28 (åpen) | Ny hovedvindu: tydelig «spilles nå»/«neste» i cuelista med kolonnen Gjenstår, faner i inspektøren, større GO og neste cue, bedre aktive cuer. Ren visningslogikk (`CueDisplay`, `InspectorLayout`) med tester på begge plattformer |
| 0.19.1 | (åpen) | Robusthet 2: unntak i `CueEngine.Tick` fanges per cue (Windows); Mac begrenser verdier ved lasting som Windows; «Sjekk showet» kjenner gruppe-cuer; innstillinger begrenses ved lasting |
| 0.19.0 | #26 | Sjekk showet: forhåndssjekk før show (mediefiler, verdier og mål, lydutgang/skjerm, duplikate cue-numre) med liste over feil og advarsler |
| 0.18.0 | #24 | Windows: live-innganger via ASIO i samme økt som utgangen (eksperimentelt, av som standard) |
| 0.17.0 | #21 | Gruppe-cue: starter flere cuer med én GO (numre og områder) |
| 0.16.1 | #25 | Robusthet: ødelagte showfiler (feltvis lesing på Windows, «null» hoppes over, cue uten type avvises på Mac), OSC-blob, DMX «NaN», `\xHH`, avslutning |
| 0.16.0 | #17 | Lysbildeserie: bilder, PDF-sider og videoer etter hverandre med krysstoning, loop, tilfeldig rekkefølge og blaing (⏮ / ⏭, OSC) |
| 0.15.0 | #16 | Skjerm per visuell cue; Mac lar seg lukke etter video; sikrere avslutning på Windows |
| 0.14.0 | #14, #15 | Spoling i sanntid i lyd og video (spolelinje, ±5 s, også under pause); media-cuer uten fil. Utviklingsnotater og stresstest i CI (#15) |
| 0.13.1 | #12, #13 | Ikon i Dock/oppgavelinja; rettelser fra stresstest (heng i loop, NaN, OSC, lange tider/filnavn); ustabil test rettet |
| 0.13.0 | #11 | Engelsk brukerflate (felles `i18n/en.json`), engelske README/endringslogg, grått tema, tekstgjennomgang |
| 0.12.0 | #10 | Knappepanel (før Cart), Beskjed (før Memo), Utstyr (før Signal ut), ingen «…» i menyer, zoom og tekststørrelse |
| 0.11.1 | #9 | Logo (selburose med GO) som programikon |
| 0.11.0 | #8 | Verktøylinje med nedtrekksmenyer, nettside-cue, nøytrale navn, opprydding i død kode |
| 0.10.1 | #7 | `THIRD-PARTY-NOTICES.txt` og «Om ShowCue» |
| 0.10.0 | #6 | Tidskode (MTC/LTC), lys over Art-Net, live-inngang |
| 0.9.0 | #5 | Knappepanel (Cart), finn/samle mediefiler, nettverks- og beskjed-cue, lydstyrke-normalisering |
| 0.8.0 | #4 | Pause, spillelister, gå til, slipp loop, kjør herfra, fadekurver |
| 0.7.0 | #3 | Showmodus, angre, autolagring, lytteutgang |
| 0.6.2 | #2 | Forenkling, standard OSC-port 8000 |
| 0.6.x | #1 | Bygg og utgivelse etter beste praksis, GPL-3.0, .NET 10 |

Commit-historikken ble ryddet 2. november 2026 (én commit-melding skrevet om, tagger flyttet). Gamle PR-referanser #2–#6 hos GitHub
kan fortsatt peke på de opprinnelige commitene; bare GitHub Support kan fjerne dem.
