#!/bin/bash
# Publiserer en ferdig programfil:
#  - i releasen «siste» (overskrives ved hver endring på main), med notater for gjeldende versjon
#  - i releasen v<VERSION> første gang versjonen bygges (eldre versjoner røres ikke)
# Bruk: release.sh <fil> [fil ...]      (kjøres fra roten av repoet, krever GH_TOKEN)
set -euo pipefail
[ $# -gt 0 ] || { echo "Ingen filer oppgitt"; exit 1; }
VERSION=$(tr -d '[:space:]' < VERSION)
TAG="v$VERSION"
SHA=$(git rev-parse --short HEAD)

# Teksten for denne versjonen fra CHANGELOG.md (fra «## <versjon>» til neste «## »).
NOTES=$(awk -v v="$VERSION" '
  /^## / { if (found) exit; if (index($0, "## " v) == 1) { found = 1; next } }
  found { print }' CHANGELOG.md)
[ -n "$NOTES" ] || NOTES="Se CHANGELOG.md."

# --- Versjonsutgivelsen ---
if ! gh release view "$TAG" >/dev/null 2>&1; then
  gh release create "$TAG" --target "$GITHUB_SHA" --title "ShowCue $VERSION" --notes "$NOTES" 2>/dev/null \
    || echo "Releasen $TAG ble laget av den andre byggejobben samtidig."
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
SISTE_NOTES="Bygges automatisk fra main ved hver endring (sist: $SHA).

Windows: ShowCue-Setup.exe (installasjonsprogram) eller ShowCue.exe (kjøres uten installering).
Mac: ShowCue-Mac.dmg (dra appen til Programmer) eller ShowCue-Mac.zip.

Versjon $VERSION. Hele endringsloggen står i CHANGELOG.md.

$NOTES"
gh release view siste >/dev/null 2>&1 || gh release create siste --prerelease --title "Siste bygg" --notes "$SISTE_NOTES"
gh release edit siste --notes "$SISTE_NOTES" >/dev/null
gh release upload siste "$@" --clobber
