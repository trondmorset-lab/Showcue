#!/bin/bash
# Publishes the finished program files (run by the release job when both Windows and Mac are built and tested):
#  - in the release v<VERSION> the first time the version is built (older versions are left untouched)
#  - in the release "siste", which is overwritten on every change to main and gets its tag moved to this commit
# Usage: release.sh <file> [file ...]   (run from the repo root; needs GH_TOKEN, GITHUB_SHA and GITHUB_REPOSITORY)
set -euo pipefail
[ $# -gt 0 ] || { echo "Ingen filer oppgitt"; exit 1; }
for f in "$@"; do [ -f "$f" ] || { echo "Mangler $f"; exit 1; }; done

VERSION=$(tr -d '[:space:]' < VERSION)
TAG="v$VERSION"
SHA=${GITHUB_SHA:0:7}

# The text for this version from CHANGELOG.md (from "## <version>" to the next "## ").
NOTES=$(awk -v v="$VERSION" '
  /^## / { if (found) exit; if (index($0, "## " v) == 1) { found = 1; next } }
  found { print }' CHANGELOG.md)
[ -n "$NOTES" ] || NOTES="See CHANGELOG.md."

FILES_TEXT="Windows: ShowCue-Setup.exe (installer) or ShowCue.exe (runs without installing).
Mac: ShowCue-Mac.dmg (drag the app to Applications) or ShowCue-Mac.zip.
Licenses for ShowCue and the bundled components: THIRD-PARTY-NOTICES.txt.

The files are built and tested by GitHub Actions. Their origin can be verified with
\`gh attestation verify <fil> -R $GITHUB_REPOSITORY\`."

# --- Version release ---
if ! gh release view "$TAG" >/dev/null 2>&1; then
  gh release create "$TAG" --target "$GITHUB_SHA" --title "ShowCue $VERSION" --notes "$NOTES

$FILES_TEXT"
fi
EXISTING=$(gh release view "$TAG" --json assets --jq '.assets[].name')
for FILE in "$@"; do
  NAME=$(basename "$FILE")
  if grep -qx "$NAME" <<<"$EXISTING"; then
    echo "$TAG already has $NAME - left untouched."
  else
    gh release upload "$TAG" "$FILE"
  fi
done

# --- «siste» ---
SISTE_NOTES="Built automatically from main on every change (latest: $SHA). Version $VERSION.

$FILES_TEXT

The full changelog is in CHANGELOG.md.

$NOTES"
if gh release view siste >/dev/null 2>&1; then
  # Move the tag to this commit so the "Source code" downloads match the program files.
  gh api -X PATCH "repos/$GITHUB_REPOSITORY/git/refs/tags/siste" -f sha="$GITHUB_SHA" -F force=true >/dev/null
  gh release edit siste --notes "$SISTE_NOTES" >/dev/null
else
  gh release create siste --target "$GITHUB_SHA" --prerelease --title "Siste bygg" --notes "$SISTE_NOTES"
fi
gh release upload siste "$@" --clobber
