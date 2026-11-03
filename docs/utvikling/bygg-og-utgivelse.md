# Bygg og utgivelse

## CI (`.github/workflows/build.yml`)
Kjører på pull requests og på push til `main`.

| Jobb | Steg |
|---|---|
| Windows (`windows-2025`, .NET 10) | i18n-sjekk (`tools/i18n.py`) → `dotnet test` → stresstest (én runde) → lisenssjekk (NuGet-lisenser ordrett i `THIRD-PARTY-NOTICES.txt`) → `dotnet publish` (én selvstendig `ShowCue.exe`) → Inno Setup (`ShowCue-Setup.exe`) |
| Mac (macOS, Xcode) | `swift build`/`swift test` (feil samles og vises som merknader av `.github/scripts/annotate.py`) → `build-app.sh` (universell app) → ikonsjekk → zip og dmg |
| Utgivelse (bare `main`) | `.github/scripts/release.sh`: oppdaterer «siste» hver gang, og lager `v<VERSION>` når versjonen er ny. Teksten hentes fra `CHANGELOG.md`. SHA-256 og opprinnelsesattest for hver fil. |

- Actions er låst til commit-SHA; Dependabot foreslår oppdateringer ukentlig (Actions og NuGet i `Windows/`).
  NAudio-versjonen i `tools/stress/stress.csproj` oppdateres for hånd sammen med den i `Windows/ShowCue/ShowCue.csproj`.
- `global.json` låser .NET 10 SDK for hele repoet.
- `main` er beskyttet: tar bare imot endringer som har bestått byggene.

## Versjoner
- `VERSION` i roten brukes av begge bygg (Windows via csproj, Mac via `build-app.sh`). Hold `Mac/Resources/Info.plist` lik.
- Små rettelser: 0.x.**y**. Ny funksjonalitet: 0.**x**.0.
- `CHANGELOG.md` (norsk, full historikk) og `CHANGELOG.en.md` (engelsk, fra 0.11.0).

## Arbeid mot GitHub fra utviklingsmiljøet
- Bruk REST (`gh api repos/trondmorset-lab/showcue-/...`); GraphQL er sperret, så `gh pr`/`gh release download` virker ikke.
  Last ned utgivelsesfiler med `gh api -H "Accept: application/octet-stream" repos/…/releases/assets/<id>`.
- Opprett PR: `gh api repos/…/pulls -f head=<gren> -f base=main -f title=… -F body=@fil.md`.
- Merge (bare når eieren ber om det): `gh api -X PUT repos/…/pulls/<n>/merge -f merge_method=merge`.
- Følg en CI-kjøring: hent siste kjøring for grenen (`actions/runs?branch=…&event=…`), vent til `status=completed`,
  og les feilannotasjoner fra `check-runs/<jobb>/annotations`. Jobblogg: `actions/jobs/<id>/logs`.
- En PR som bygger på en annen gren: sett base til `main` etter at den første er merget (ellers kan GitHub lukke den).

## Lokalt
- Windows: `Windows/build.bat` eller Visual Studio (`ShowCue.sln`). Mac: `Mac/build-app.sh` (Xcode for universell app og ikonkatalog), `Mac/make-dmg.sh`.
