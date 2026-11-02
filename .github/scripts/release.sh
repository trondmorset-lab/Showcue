#!/bin/bash
# Publiserer ferdige programfiler (kjøres av utgivelsesjobben når både Windows og Mac er bygget og testet):
#  - i releasen v<VERSION> første gang versjonen bygges (eldre versjoner røres ikke)
#  - i releasen «siste», som overskrives ved hver endring på main og får taggen flyttet til denne commiten
# Bruk: release.sh <fil> [fil ...]      (kjøres fra roten av repoet, krever GH_TOKEN, GITHUB_SHA og GITHUB_REPOSITORY)
set -euo pipefail
[ $# -gt 0 ] || { echo "Ingen filer oppgitt"; exit 1; }
for f in "$@"; do [ -f "$f" ] || { echo "Mangler $f"; exit 1; }; done

VERSION=$(tr -d '[:space:]' < VERSION)
TAG="v$VERSION"
SHA=${GITHUB_SHA:0:7}

# Teksten for denne versjonen fra CHANGELOG.md (fra «## <versjon>» til neste «## »).
NOTES=$(awk -v v="$VERSION" '
  /^## / { if (found) exit; if (index($0, "## " v) == 1) { found = 1; next } }
  found { print }' CHANGELOG.md)
[ -n "$NOTES" ] || NOTES="Se CHANGELOG.md."

FILES_TEXT="Windows: ShowCue-Setup.exe (installasjonsprogram) eller ShowCue.exe (kjøres uten installering).
Mac: ShowCue-Mac.dmg (dra appen til Programmer) eller ShowCue-Mac.zip.
Lisenser for ShowCue og komponentene som følger med: THIRD-PARTY-NOTICES.txt.

Filene er bygget og testet av GitHub Actions. Opprinnelsen kan kontrolleres med
\`gh attestation verify <fil> -R $GITHUB_REPOSITORY\`."

# --- Versjonsutgivelsen ---
if ! gh release view "$TAG" >/dev/null 2>&1; then
  gh release create "$TAG" --target "$GITHUB_SHA" --title "ShowCue $VERSION" --notes "$NOTES

$FILES_TEXT"
fi
EXISTING=$(gh release view "$TAG" --json assets --jq '.assets[].name')
for FILE in "$@"; do
  NAME=$(basename "$FILE")
  if grep -qx "$NAME" <<<"$EXISTING"; then
    echo "$TAG har allerede $NAME – røres ikke."
  else
    gh release upload "$TAG" "$FILE"
  fi
done

# --- «siste» ---
SISTE_NOTES="Bygges automatisk fra main ved hver endring (sist: $SHA). Versjon $VERSION.

$FILES_TEXT

Hele endringsloggen står i CHANGELOG.md.

$NOTES"
if gh release view siste >/dev/null 2>&1; then
  # Flytt taggen til denne commiten, så «Source code»-nedlastingene stemmer med programfilene.
  gh api -X PATCH "repos/$GITHUB_REPOSITORY/git/refs/tags/siste" -f sha="$GITHUB_SHA" -F force=true >/dev/null
  gh release edit siste --notes "$SISTE_NOTES" >/dev/null
else
  gh release create siste --target "$GITHUB_SHA" --prerelease --title "Siste bygg" --notes "$SISTE_NOTES"
fi
gh release upload siste "$@" --clobber
