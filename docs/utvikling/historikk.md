# Historikk

| Versjon | PR | Innhold |
|---|---|---|
| 0.27.0 | #34 | Showlogg: hendelser (cue start/stopp/ferdig med hvordan, pause, stopp alt, markør, feil, show åpnet/lagret, programstart/avslutning) i begrenset kø og bakgrunnsskriver, en fil per kjøring i `Logs` (30 beholdes, 5 MB tak), loggvindu med filter og eksport til CSV/tekst, innstillingen «Skriv showlogg» (på). Ren logikk (`ShowLog*`) med tester og stresstest på Windows, tester på Mac |
| 0.26.0 | #35 | Nettside for fjernstyring fra mobil/nettbrett: liten innebygd webtjener (av som standard, PIN påkrevd, sperre mot gjetting, faste ruter), viser neste cue og hva som spiller, knapper for GO/stopp alt/pause/neste/forrige og skrivebeskyttet cueliste; ren logikk (`WebRemote`, `WebRemoteCore`) med tester på begge plattformer |
| 0.25.0 | #31 | Øvelsesmodus: `SkipInRehearsal` per cue og en kjøretidsbryter (aldri lagret) som får GO, auto-fortsett/-følg og triggere til å hoppe over merkede cuer; felt i Tidsstyring, merke i lista, banner, advarsel i Sjekk showet. Ren logikk (`Rehearsal`) med tester på begge plattformer |
| 0.24.0 | #32 | Forhåndslasting av neste cue (innstillingen «Forhåndslast neste cue», av som standard): ren planlegger (`PreloadPlanner`) og regnskap (`PreloadBook`) med tester på begge plattformer, `Preloader` i bakgrunnen på Windows og Mac, stresstest-case |
| 0.23.0 | #33 | Routingforhåndsvalg: navngitte utgangspar i showet (`RoutingPresets`), sett på alle valgte cuer med ett klikk, kopier/lim inn routing, oppsummering av routingen i inspektøren. Ren logikk (`RoutingPresets`, `RouteClipboard`) med tester på begge plattformer |
| 0.22.0 | #36 | Scener (cuelister): `Scene` per cue og `Scenes` i showet, egen nummerering og GO-markør per scene, velger over lista, bytte med hurtigtast/«gå til»/OSC `/scene/{navn}`, «Sjekk showet» godtar samme nummer i ulike scener. Ren logikk (`SceneLogic`) med tester på begge plattformer |
| 0.21.0 | #29 | Klokkeslett-trigger per cue (`ClockTrigger`, «tt:mm:ss», lokal tid) med innstillingen «Klokke-triggere aktive» (av som standard), felt i Tidsstyring og advarsel i Sjekk showet. Ren logikk (`ClockTriggers`) med tester på begge plattformer |
| 0.20.0 | #28 | Ny hovedvindu: tydelig «spilles nå»/«neste» i cuelista med kolonnen Gjenstår, faner i inspektøren, større GO og neste cue, bedre aktive cuer. Ren visningslogikk (`CueDisplay`, `InspectorLayout`) med tester på begge plattformer |
| 0.19.1 | #27 | Robusthet 2: unntak i `CueEngine.Tick` fanges per cue (Windows); Mac begrenser verdier ved lasting som Windows; «Sjekk showet» kjenner gruppe-cuer; innstillinger begrenses ved lasting |
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
