# ShowCue

*[English](README.en.md)*

Cue-basert avspillingsprogram for Windows. Laget for konsert og event:
GO-knapp med cueliste, hurtigtaster for jingler og walk-in, og utgang til projektor.

## Bygge programmet

1. Installer **.NET 10 SDK** (ikke bare runtime): https://dotnet.microsoft.com/download/dotnet/10.0
2. Dobbeltklikk `build.bat`.
3. Ferdig program: `publish\ShowCue.exe` (én fil, trenger ingen installasjon – kan kopieres til show-PC-en).

Alternativt: åpne `ShowCue.sln` i Visual Studio 2022 (17.14 eller nyere) eller 2026 og trykk F5.
Testene ligger i `ShowCue.Tests` og kjøres med `dotnet test ShowCue.Tests` (eller Test Explorer i Visual Studio).
GitHub Actions bygger både `ShowCue.exe` og installasjonsprogrammet `ShowCue-Setup.exe` (Inno Setup, skriptet ligger i
`installer/ShowCue.iss`) og legger dem under *Releases*. Installasjonsprogrammet lager snarvei i Start-menyen,
valgfritt skrivebordsikon og kobler `.showcue`-filer til ShowCue, og kan installere for én bruker uten administratorrettigheter.

Krever Windows 10 (1809) eller nyere, 64-bit.

## Cue-typer

| Type | Hva den gjør |
|---|---|
| ♪ **Lyd** | WAV, MP3, AIFF, FLAC, M4A/AAC, WMA. Nivå (dB), start-/sluttpunkt, loop, fade inn/ut. |
| ▶ **Video** | MP4, MOV, WMV, AVI, MKV. Lyden pakkes ut og går gjennom lydpatchen (se under). Start-/sluttpunkt, loop, fade, krysstoning. |
| ▣ **Bilde** | PNG, JPG, BMP, GIF, TIFF. Valgfri visningstid, fade, krysstoning. |
| ▤ **PDF** | Viser én side. «+ Cue for neste side» lager raskt en cue per side (sangtekster, presentasjoner). |
| ◢ **Fade** | Fader en kjørende cue til et nytt lydnivå og/eller synlighet, valgfritt stopp etterpå. |
| ■ **Stopp** | Stopper én cue (eller alt, hvis mål er tomt), med valgfri fade. |
| ⇢ **OSC** | Sender OSC over UDP til mikser, lysbord, Companion o.l. Støtter int, float, tekst, true/false. |
| ♫ **MIDI** | Note on/off, program change, CC og MSC GO (MIDI Show Control). |

Alle cues har **pre-wait** (forsinkelse før start), **auto-continue** (start neste samtidig),
**auto-follow** (start neste når denne er ferdig), **hurtigtast**, **MIDI-trigger** og **notater**.

### Start og slutt (bølgeform)

Lyd- og video-cuer har en bølgeform i inspektøren der start- og sluttpunktet redigeres visuelt:

- Dra den **grønne** markøren (start) og den **røde** (slutt). Området utenfor er mørklagt, og fade inn/ut vises som oransje linjer.
- **Klikk** flytter den blå markøren, **dobbeltklikk** lytter derfra. **Musehjul** zoomer rundt musepekeren, **Shift + hjul** ruller.
- **▶ Fra start**, **▶ Fra markør** og **▶ Slutten** (de siste 5 sekundene før sluttpunktet) lytter gjennom lydpatchen, uten å påvirke showet.
- **Start = markør** / **Slutt = markør** setter punktet der markøren står – eller der lyttingen er akkurat nå, så du kan trykke mens det spiller.
- **⤢ Stort vindu** åpner en stor redigeringsvisning. Slutt helt til slutten av fila lagres som «0 = filslutt».
- For video starter bildet og lyden på startpunktet, og med loop hopper videoen tilbake til startpunktet.

## Lydpatch og flerkanals lydkort

Lyden går i tre ledd:

```
lydfil-kanaler ──(routing per cue)──▶ utganger ──(lydpatch)──▶ kanaler på lydkortet
```

- **Utganger** (busser) er logiske, f.eks. «Main L», «Main R», «Monitor», «Opptak». Showfila refererer bare
  til utgangene, så showet kan flyttes til en annen maskin med et annet lydkort – bare patchen må endres.
- **Lydpatch** (*Oppsett → Lydpatch*): én rad per utgang og én kolonne per kanal på lydkortet. Kryss av for
  hvor hver utgang skal ut. En utgang kan gå til flere kanaler. Nivå per utgang i dB.
  **Test** spiller rosa støy (−18 dBFS) på utgangen for linjesjekk, og nivåmålere vises per utgang og per kanal.
  Med ASIO vises kanalnavnene fra driveren (f.eks. Dante-kanalene) når du holder musa over kolonneoverskriften.
- **Routing per lyd-cue**: matrisen i panelet til høyre, med filkanaler som rader og utganger som kolonner. Skriv nivå i dB,
  eller dobbeltklikk en rute for å slå av/på. «Standard» betyr mono → 1+2, stereo → 1/2, og flerkanalsfiler kanal for kanal.
  Endringer i routing og nivå høres med en gang mens cuen spiller.

**Lyd i video («de-embedding»):** lydsporet i videofiler dekodes av Windows (Media Foundation: AAC, PCM, WMA …)
og spilles av ShowCue sin egen lydmotor. Det går gjennom lydpatchen akkurat som en lyd-cue, med
routingmatrise, nivå i dB, fader og fade-cuer. Lyden styrer tiden: hvis bildet glir mer enn 0,12 s
(f.eks. fordi lydkortets klokke og PC-klokka går litt ulikt), justeres bildet. Knappen **«Pakk ut lyd til egen lyd-cue»**
lagrer lydsporet som WAV ved siden av videoen og lager en lyd-cue som starter samtidig. Da kan lyden redigeres
for seg (start/slutt, loop) eller byttes ut med en egen miks.

**Eksempel – Dante Virtual Soundcard:**
1. Start DVS i **ASIO-modus** og velg antall kanaler (f.eks. 64×64) og samplingsfrekvens i Dante Controller.
2. ShowCue → *Innstillinger → Lyd*: velg **ASIO** og driveren «Dante Virtual Soundcard (x64)».
3. *Lydpatch*: kryss av hvilke Dante-kanaler hver utgang skal på. Rut kanalene videre i Dante Controller.

DVS i WDM-modus (og andre flerkanals kort uten ASIO) fungerer også via **WASAPI**. Da brukes enhetens kanaler
og samplingsfrekvens direkte. ASIO anbefales for lavest forsinkelse.

## Fjernstyring: OSC og MIDI inn

**OSC** (UDP, port 8000 som standard – samme som Companion-modulen «Generic OSC» sender til):

| Adresse | Funksjon |
|---|---|
| `/go` | GO |
| `/stop` | Stopp alt (med panikk-fade) |
| `/panic` | Som Esc (to ganger innen 1 s = umiddelbart) |
| `/hardstop` | Stopp alt umiddelbart |
| `/next`, `/previous` | Flytt GO-markøren |
| `/playhead/{nr}` eller `/select/{nr}` | Sett markøren til cue |
| `/cue/{nr}/start` | Start cue (markøren flyttes ikke) |
| `/cue/{nr}/stop` | Stopp cue (med cuens fade ut) |
| `/cue/{nr}/go` eller `/go/{nr}` | Sett markøren til cue og kjør GO |
| `/hotkey/{tast}` | Som å trykke hurtigtasten, f.eks. `/hotkey/F1` |
| `/pause`, `/resume`, `/pause/toggle` | Pause / fortsett alt |
| `/cue/{nr}/devamp` | Slipp loopen i en cue (spiller ferdig og fortsetter til filslutt) |
| `/cue/{nr}/next`, `/cue/{nr}/previous` | Bla til neste / forrige lysbilde i en lysbildeserie som kjører |

- Cuenummer kan også sendes som argument: `/cue/start "12"`.
- Meldinger med argument 0 eller false ignoreres, så knapper som sender «sluppet» ikke trigger to ganger.
- Prefikset `/showcue/...` er også tillatt. OSC-bundles støttes.
- Statuslinja nederst viser porten, maskinens IP-adresser og siste mottatte melding.
- Windows-brannmuren spør første gang: tillat ShowCue på private nettverk.

**Bitfocus Companion / Stream Deck:** bruk modulen «Generic OSC», sett mål-IP til show-PC-en (porten 8000 er allerede modulens standard),
og send f.eks. `/go` eller `/cue/5/start`.

### Status ut over OSC

*Innstillinger → OSC → Status ut*: skriv inn IP-adressen til mottakeren og porten. For Companion er det port **12321**
(Companions OSC-port; slå på OSC under *Settings → Protocols* i Companion).

**Format «Companion»** setter egendefinerte variabler. Opprett disse under *Variables → Custom Variables* i Companion
(du trenger bare dem du vil bruke):

| Variabel | Innhold | Eksempel |
|---|---|---|
| `sc_next_number`, `sc_next_name`, `sc_next_notes` | Cuen ved GO-markøren | `3`, `Intro-video` |
| `sc_playing`, `sc_playing_number` | Sist startede cue som spiller | `Applaus-jingle` |
| `sc_playing_all` | Alle som spiller | `Walk-in · Applaus-jingle` |
| `sc_playing_count` | Antall som spiller | `2` |
| `sc_remaining`, `sc_remaining_name` | Tid igjen på cuen som slutter først | `-0:05` |
| `sc_elapsed` | Tid spilt på sist startede cue | `1:23` |
| `sc_running` | Nummer på alle som spiller | `1,7` |
| `sc_playhead` | Markørens posisjon | `3/11` |
| `sc_state` | `klar`, `venter` (pre-wait) eller `spiller` | |
| `sc_show` | Navn på showet | |

Bruk dem i knappetekst, f.eks. `$(internal:custom_sc_next_name)`, og i feedback (f.eks. rød knapp når
`sc_state` = `spiller`). Bare endrede verdier sendes (ca. 10 ganger i sekundet), pluss alt hvert 5. sekund.
Companion kan be om alt på nytt med `/refresh`.

**Format «Generisk»** sender `/showcue/next/name`, `/showcue/remaining`, `/showcue/playing/count` (heltall) osv.,
pluss `/showcue/cue/{nr}/running 1|0` når en cue starter og stopper. Passer for andre programmer eller moduler som lytter på OSC.

**MIDI inn** (*Innstillinger → MIDI inn*):
- Velg inngang og kanal (eller alle).
- **Faste funksjoner:** GO, Stopp alt, Neste og Forrige kan knyttes til en note, CC eller program change med **Lær**.
- **Per cue:** «MIDI-trigger» i panelet til høyre. Trykk **Lær** og deretter en tast/knapp på kontrolleren.
  Cuen startes, eller stoppes hvis den allerede spiller, akkurat som en hurtigtast.
- CC trigger når verdien går fra under 64 til 64 eller mer, så en fader ikke trigger hele tiden.
- **MIDI Show Control:** GO og STOP med cuenummer fra lysbord o.l., med valgfri device ID.

OSC og MIDI **ut** gjøres med OSC- og MIDI-cuer i cuelista.

## Betjening

| Tast | Funksjon |
|---|---|
| **Mellomrom** | GO – kjør cuen ved markøren og flytt til neste |
| **Esc** | Stopp alt med kort fade. Trykk igjen innen 1 s for umiddelbar stopp |
| **↑ / ↓** | Flytt GO-markøren |
| **F1–F12, A–Z, 0–9** | Hurtigtaster (start/stopp cuen uten å flytte markøren) |
| **Ctrl+1 … Ctrl+8** | Legg til lyd, video, bilde, PDF, fade, stopp, OSC, MIDI |
| **Ctrl+9 / Ctrl+0** | Legg til spilleliste / gå til-cue |
| **Ctrl+Shift+B** | Legg til lysbildeserie |
| **Ctrl+P** | Pause / fortsett alt |
| **Ctrl+Shift+M / Ctrl+Shift+N** | Legg til beskjed / nettverks-cue |
| **Ctrl+K** | Knappepanel |
| **Ctrl + / Ctrl −** | Zoom inn / ut (vanlig størrelse i *Oppsett*-menyen) |
| **Ctrl+Shift+T / L / I / W** | Legg til tidskode-, lys-, inngang-, nettside-cue |
| **Ctrl+D** | Dupliser |
| **Ctrl+↑ / Ctrl+↓** | Flytt cue opp/ned |
| **Delete** | Slett valgte cues |
| **Ctrl+S / Ctrl+O / Ctrl+N** | Lagre / åpne / nytt show |
| **Ctrl+Z / Ctrl+Y** | Angre / gjør om (også Ctrl+Shift+Z) |
| **Ctrl+L** | Showmodus av/på |
| **Enter** (i et felt) | Bekreft og gå tilbake til lista |

- **Dra filer** fra Utforsker rett inn i cuelista. Slipper du én fil på en media-cue uten fil, får cuen fila.
- **＋ Media** lager en tom cue (lyd, video, bilde, PDF, spilleliste); velg fila senere med *Bla* i panelet til høyre.
- **Dobbeltklikk** en cue for å teste den uten å flytte markøren.
- Hurtigtaster på lyd/bilde/video fungerer som av/på: trykk én gang for å starte, én gang til for å stoppe (med cuens fade ut).
- F-tastene virker også mens du skriver i et felt; bokstaver og tall gjør det ikke.
- GO har dobbel-GO-sperre (300 ms) så tasteprell ikke hopper over en cue.

## Pause, spillelister og loop

- **Pause** (knappen ved STOPP ALT eller Ctrl+P): fryser all lyd, video, fader og pre-wait. Trykk igjen, eller GO, for å fortsette.
- **Spilleliste:** en cue med flere lydfiler som spilles etter hverandre med krysstoning. Velg tilfeldig rekkefølge og
  om lista skal starte forfra. Fade ut brukes når den stoppes. Fin for innslipp og pause.
- **Lysbildeserie:** en cue med bilder, PDF-er og videoer som vises etter hverandre på én skjerm, som en presentasjon.
  Bilder og PDF-sider vises i *Tid per bilde* sekunder (hver PDF-side blir et eget lysbilde), videoer spiller til de er ferdige,
  og lysbildene krysstones. Velg loop og tilfeldig rekkefølge, eller slå av *Bytt automatisk* og bla med ⏮ / ⏭ under *Aktive cuer*
  (eller OSC `/cue/{nr}/next` og `/cue/{nr}/previous`). Stopp cuen for å avslutte serien.
- **Gå til:** flytter GO-markøren til en annen cue (f.eks. tilbake til starten av en sekvens), og kan kjøre den med en gang.
- **Slipp loop (devamp):** kryss av «Slipp loop» i en stopp-cue. Mål-cuen (lyd, video eller spilleliste) spiller ferdig
  runden og fortsetter til slutten av fila i stedet for å stoppe. ⤼-knappen i panelet gjør det samme for valgt cue.
- **Spoling mens det spiller:** lyd- og video-cuer under *Aktive cuer* har en spolelinje. Dra i den for å spole i
  sanntid, eller hopp 5 sekunder med ⏪ / ⏩. Bilde og lyd i en video følger hverandre. Virker også under pause.
- **Kjør herfra:** knappen under bølgeformen starter cuen fra den blå markøren, på de vanlige utgangene – nyttig på prøver.
- **Fadekurver:** fade-cuer kan bruke *jevn* (lineær i dB, standard), *S-kurve* (myk start og slutt) eller
  *lineær amplitude* (som en fysisk fader).

## Verktøylinja

| Meny | Innhold |
|---|---|
| **Fil** | Nytt, åpne, lagre, samle show, finn manglende mediefiler |
| **＋ Media** | Lyd, video, bilde, PDF, nettside, spilleliste, live-inngang |
| **Styring** | Fade, stopp, gå til, beskjed |
| **Utstyr** | Styr annet utstyr: OSC, MIDI, nettverk, tidskode, lys (Art-Net) |
| **Rediger** | Dupliser, slett, flytt, nummerer, normaliser lydstyrke |
| **Oppsett** | Lydpatch, utgangsvinduet, innstillinger, tema, om ShowCue |

Angre/gjør om, **▦ Knappepanel** og **🔒 Showmodus** er egne knapper. Hurtigtastene står i menyene.

**Nettside-cue** (◍): viser en nettside (f.eks. `https://time.is`) eller en lokal .html-fil fullskjerm på utgangen,
med fade og visningstid som et bilde. Windows bruker Microsoft Edge WebView2, som er innebygd i Windows 10 og 11.
Lyd fra siden går til systemets lydutgang, ikke gjennom lydpatchen.

## Knappepanel, nettverk, beskjed og lydstyrke

- **Knappepanel** (▦ Knappepanel eller Ctrl+K): et eget vindu med store knapper som starter og stopper cuer direkte,
  uten å flytte GO-markøren – nyttig for jingler, lydeffekter og applaus-stikk, og på berøringsskjerm. Viser cuer med
  hurtigtast og cuer merket «Vis i knappepanelet». Trykk for å starte, trykk igjen for å stoppe (med cuens fade ut).
  Antall kolonner kan justeres.
- **Nettverks-cue** (⇄ Nett): sender tekst over **UDP** eller **TCP**, eller en **HTTP**-forespørsel. `\r`, `\n`, `\t`
  og `\xHH` i meldingen gir linjeskift, tab og enkeltbytes. Eksempel PJLink-projektor av: TCP, port 4352, `%1POWR 0\r`.
  Feil (f.eks. ingen kontakt) vises på cuen; GO venter aldri på nettverket.
- **Beskjed-cue** (✎ Beskjed): en beskjed til operatøren («Vent på applaus») som vises stort øverst til den lukkes.
- **Normaliser lydstyrke:** *Normaliser* i panelet for en lyd-cue, eller *Rediger → Normaliser lydstyrke*, måler
  lydstyrken (integrert LUFS etter EBU R128) mellom start og slutt og setter nivået så cuen treffer målet
  (standard −23 LUFS, endres under *Innstillinger → Sikkerhet*).

## Tidskode, lys og live-inngang

- **Tidskode-cue** (⏱ TC): sender MTC til en MIDI-utgang og/eller LTC som lyd til en egen utgang i lydpatchen,
  fra starttiden (f.eks. 01:00:00:00). Lengde 0 = til cuen stoppes. Pause stopper tidskoden.
- **Tidskode-trigger:** skriv et tidspunkt (tt:mm:ss:ff) i panelet, og slå på *MTC inn* under *Innstillinger → MIDI*.
  Cuen starter når innkommende MIDI Time Code passerer tidspunktet (ikke ved spoling forbi).
- **Lys-cue** (✺ Lys): sender DMX over Art-Net (UDP 6454) til en node eller som kringkasting. Nivåer skrives
  `kanal@verdi`, f.eks. `1@255 2-4@50% 10@0`, og fades over valgt tid. Kanaler som ikke nevnes, beholder nivået.
- **Live-inngang** (◉ Inn): lyden fra inngangen valgt under *Innstillinger → Lyd* (WASAPI: mikrofon, linje inn,
  nettverkslydkort i WDM-modus) går gjennom routingmatrisen og lydpatchen med nivå og fades. Forsinkelse ca. 20–60 ms.
  ASIO-innganger støttes ikke ennå – bruk nettverkslydkortet i WDM-modus.

## Mediefiler: finn og samle

- Mangler det filer når et show åpnes, tilbyr ShowCue å lete i en mappe du velger (med undermapper), på filnavn.
  Det samme finnes under *Fil → Finn manglende mediefiler*.
- *Fil → Samle show og mediefiler i én mappe* kopierer showet og alle filene det bruker til en mappe, med filene i
  `Media/`. Kopier mappa til en minnepinne eller en annen maskin (også Mac), så finner showet filene selv.

## Trygt under forestillingen

- **Showmodus** (🔒-knappen eller Ctrl+L): låser all redigering. GO, stopp, Esc, hurtigtaster, fjernstyring og
  hurtigknapper virker som vanlig, men ingenting kan flyttes, slettes eller endres ved et uhell. Tittellinja viser SHOWMODUS.
- **Angre/gjør om** (Ctrl+Z / Ctrl+Y, ↶ ↷ i verktøylinja): angrer endringer i showet – nye, slettede og flyttede cuer
  og alle felt. I et tekstfelt angrer Ctrl+Z skrivingen i feltet.
- **Autolagring:** ulagrede endringer skrives hvert 30. sekund til `%AppData%\ShowCue\Autolagring`. Krasjer programmet
  eller går strømmen, tilbys endringene gjenopprettet neste gang showet åpnes.
- **Sikkerhetskopier:** hver gang du lagrer, kopieres forrige versjon til `%AppData%\ShowCue\Sikkerhetskopier`
  (de 20 siste per show beholdes).
- **Forhåndslytting til hodetelefoner:** under *Innstillinger → Lyd* velger du en egen lydenhet (f.eks. PC-ens
  hodetelefonutgang) eller en utgang i lydpatchen for lyttingen i bølgeformen. Da høres ikke lyttingen i salen.
  Uten dette går lyttingen ut på cuens vanlige utganger, og bølgeformen viser en advarsel.

## Utseende

Fire fargetemaer, som byttes med en gang via *Oppsett → Bytt tema* eller *Innstillinger → Utseende*:
**Mørk** (grafitt), **Grå** (dempet og mindre hvitt enn Lys), **Lys** (dagslys og prøver) og **Nattmodus** (ekstra dempet for mørke scener).
Tittellinja i Windows følger temaet.

**Zoom og tekststørrelse:** *Oppsett → Zoom inn / Zoom ut* (Ctrl + og Ctrl −) gjør hele hovedvinduet 80–200 % stort.
*Oppsett → Tekst i cuelista* (Vanlig, Stor, Ekstra stor) gjør bare cuelista større, i tillegg.
Begge finnes også under *Innstillinger → Utseende* og huskes til neste gang.

**Språk:** norsk eller engelsk under *Innstillinger → Utseende → Språk*. Standard følger språket i Windows
(norsk på norske maskiner, ellers engelsk). Det nye språket brukes neste gang ShowCue startes.

**Cuefarger:** gi cuer en farge (rød, oransje, gul, grønn, blå, lilla, grå) i panelet til høyre. Raden blir tonet,
og hurtigknappen får en fargestripe. Velg flere cuer for å farge alle på én gang.
Farger er tilpasset hvert tema (dempet i mørk og natt, pastell i grå og lys).

## Innstillinger

- **Lyd:** WASAPI (vanlige lydkort, USB og nettverkslydkort i WDM-modus) eller **ASIO** (proffe lydkort og nettverkslydkort).
  Faller automatisk tilbake til Windows standardenhet hvis valgt kort mangler. Se *Lydpatch* over.
- **OSC inn / MIDI inn:** se *Fjernstyring* over.
- **Video/bilde/PDF-utgang:** velg skjerm/projektor for fullskjerm, eller «Vindu» for testing på én skjerm.
  Hver visuell cue kan også få sin egen skjerm (*Skjerm* i panelet til høyre); *Standard* bruker skjermen herfra.
  Utgangen vises også som forhåndsvisning nede til høyre.
- Innstillinger og lydpatch lagres per maskin i `%AppData%\ShowCue\settings.json`.

## Showfiler

Shows lagres som `.showcue` (lesbar JSON). Flytter du showet til en annen maskin, legg mediefilene
i samme mappe som showfila eller i en undermappe `Media` – da finner ShowCue dem automatisk.
Manglende filer merkes med rødt **!** i lista.

## Mac-versjon

Det finnes en egen Mac-versjon i mappa `Mac/`. Showfilene kan deles: show lagret på Mac åpnes i Windows-versjon 0.4+,
og show fra Windows åpnes på Mac. ShowCue finner mediefilene igjen hvis de ligger ved siden av showfila eller i en undermappe `Media`.

## Kjente begrensninger

- Video spilles med Windows' innebygde mediemotor. MP4/H.264 med AAC-lyd er tryggest; enkelte MKV/HEVC-filer
  kan kreve kodeker fra Microsoft Store. Videoer med flere lydspor: bare det første brukes.
- Bilde og lyd starter samtidig når videoen er åpnet. Store 4K-filer kan bruke litt tid på å åpne – legg gjerne
  inn et lite pre-wait på cuen før i stedet for å trykke GO i siste sekund.
- Ingen gruppe-cues ennå.

## Mappestruktur

```
ShowCue/
  Models/      Cue-typer, showfil, innstillinger og språk (Loc)
  Engine/      Lydmotor: PatchMixer (busser → kanaler), CuePlayback (routing, fades),
               AudioEngine (WASAPI/ASIO), VideoAudio, OSC inn/ut, OscFeedback, MIDI inn/ut,
               RemoteControl, cue-motor
  Views/       Utgangsvindu, innstillinger, lydpatch, routingmatrise, temabytte
  Themes/      Colors.Dark/Light/Night.xaml (farger) og Controls.xaml (stiler)
  MainWindow   Hovedvindu: cueliste, GO, inspector, aktive cues, hurtigtaster
```
