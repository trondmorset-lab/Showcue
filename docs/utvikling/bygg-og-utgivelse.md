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

## Skjermbilder (`.github/workflows/skjermbilder.yml`)
Kjøres manuelt under Actions (eller ved push til grenen `manual-skjermbilder`) når brukerflaten er endret.
- Appen startes med `--screenshots <mappe> --language nb|en` (Mac også `-AppleLocale nb_NO` for desimalkomma).
  Den lager et demoshow med egne lyd- og bildefiler (`/Users/Shared/Show/Media`, `C:\Show\Media`), starter lysbildeserien
  og innslippsmusikken, tar bilder av hovedvinduet (mørkt og lyst), lysbildeserien, lydpatchen og innstillingene, og avslutter.
  Koden ligger i `MainWindow.Screenshots.cs` og `Mac/Sources/ShowCue/UI/Screenshots.swift`.
- Bildene tas i dobbel oppløsning og vanlig vindusstørrelse (1440×900), også om skjermen på byggemaskinen er mindre:
  Windows legger ut innholdet i full størrelse og tegner delene hver for seg (vinduet klipper), Mac bruker et eget vindu
  som ikke krympes til skjermen.
- Bildene legges på grenen `skjermbilder-ut/<gren>` og hentes inn i en vanlig commit:
  `git fetch origin skjermbilder-ut/<gren> && git checkout FETCH_HEAD -- docs/skjermbilder`. Se over bildene før de tas inn.
- Windows-maskinene har ingen lydenhet, så lyd spiller bare på Mac-bildene, og Windows-lydpatchen viser en feilmelding
  (den brukes derfor ikke i manualen).

## Versjoner
- `VERSION` i roten brukes av begge bygg (Windows via csproj, Mac via `build-app.sh`). Hold `Mac/Resources/Info.plist` lik.
- Små rettelser: 0.x.**y**. Ny funksjonalitet: 0.**x**.0.
- `CHANGELOG.md` (norsk, full historikk) og `CHANGELOG.en.md` (engelsk, fra 0.11.0).

## Arbeid mot GitHub fra utviklingsmiljøet
- Repoet heter nå `trondmorset-lab/showcue` (tidligere `showcue-`). Bruk REST (`gh api repos/trondmorset-lab/showcue/...`); GraphQL er sperret, så `gh pr`/`gh release download` virker ikke.
  Last ned utgivelsesfiler med `gh api -H "Accept: application/octet-stream" repos/…/releases/assets/<id>`.
- Opprett PR: `gh api repos/…/pulls -f head=<gren> -f base=main -f title=… -F body=@fil.md`.
- Merge (bare når eieren ber om det): `gh api -X PUT repos/…/pulls/<n>/merge -f merge_method=merge`.
- Følg en CI-kjøring: hent siste kjøring for grenen (`actions/runs?branch=…&event=…`), vent til `status=completed`,
  og les feilannotasjoner fra `check-runs/<jobb>/annotations`. Jobblogg: `actions/jobs/<id>/logs`.
- En PR som bygger på en annen gren: sett base til `main` etter at den første er merget (ellers kan GitHub lukke den).

## Lokalt
- Windows: `Windows/build.bat` eller Visual Studio (`ShowCue.sln`). Mac: `Mac/build-app.sh` (Xcode for universell app og ikonkatalog), `Mac/make-dmg.sh`.
