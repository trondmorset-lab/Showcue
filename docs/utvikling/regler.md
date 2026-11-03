# Regler

Faste regler fra eieren av prosjektet. Følg dem i all kode, tekst og dokumentasjon.

## Kode og navn
- **Ingen referanser til andre teknologier, programmer eller selskaper i kildekoden.** Kommentarer beskriver hva koden gjør –
  ikke hvor ideen kommer fra. Ingenting skal kunne tolkes som at kode er lånt fra eller «inspirert av» andre programmer.
- **Nøytrale navn** der det går (f.eks. `ButtonPanel`, ikke et produktnavn). Kjente produkter kan nevnes som eksempler
  for brukeren («f.eks. Companion»), aldri som opphav.
- Kommentarer og brukerflate på norsk (bokmål). Kommentarer forklarer *hvorfor* når det ikke er opplagt.
- Hold koden slank: fjern død kode, unngå unødvendige lag. Samme funksjon og testnavn på begge plattformer der det går.

## Språk og tekst
- All tekst i brukerflaten går gjennom oversettelsen (`Loc.T`/`Loc.F`, `{local:T '…'}`, `L(…)`). Nye tekster må få engelsk
  oversettelse i `i18n/en.json` (`python3 tools/i18n.py --update`, fyll inn, `python3 tools/i18n.py` skal gi 0 feil).
- `{0}`, `{1}` … i stedet for sammensatte strenger; plassholderne må være de samme på engelsk.
- Ikke oversett protokollnavn, OSC-adresser, statusverdier som sendes til andre programmer (`spiller`/`venter`/`klar`) eller filnavn på disk.
- Menyvalg og knapper slutter **ikke** på «…». Statusmeldinger som pågår («Leser bølgeform …») beholder «…».
- Flertall av cue er **cuer** på norsk.
- Engelsk: britisk stavemåte (colour, normalise), «audio interface» for lydkort, “ ” som anførselstegn.
- Navn i brukerflaten: *Knappepanel* (ikke Cart), *Beskjed* (ikke Memo – forskjellig fra feltet *Notater*), *Utstyr* (ikke «Signal ut»).

## Arbeidsflyt
- Alt går via egen gren og pull request mot `main`. CI må være grønn (Windows og Mac) før merge.
- **Merge bare når eieren uttrykkelig ber om det.** Aldri force-push eller omskriv `main` uten eieren.
- Øk `VERSION` (og `Mac/Resources/Info.plist`) og skriv i begge endringslogger i samme PR som endringen; utgivelsen lages
  automatisk når `main` får en ny versjon.
- Commit-meldinger og PR-tekster på norsk, med attribusjonslinjene som gjelder for økta.
- Kjør aldri skript som kan pushe til GitHub som en «test»: git-proxyen i utviklingsmiljøet er autentisert, så de *vil* pushe.
