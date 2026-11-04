# Beslutninger

Viktige valg og begrunnelsen. Nyeste først.

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
