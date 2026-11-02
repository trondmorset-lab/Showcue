#!/bin/bash
# Lager ShowCue-Mac.dmg: et diskbilde med ShowCue.app og en snarvei til Programmer-mappa.
# Kjør ./build-app.sh først.
set -euo pipefail
cd "$(dirname "$0")"

APP=build/ShowCue.app
[ -d "$APP" ] || { echo "Fant ikke $APP – kjør ./build-app.sh først."; exit 1; }
VERSION=$(tr -d '[:space:]' < ../VERSION)

STAGE=build/dmg
rm -rf "$STAGE" build/ShowCue-Mac.dmg
mkdir -p "$STAGE"
ditto "$APP" "$STAGE/ShowCue.app"
ln -s /Applications "$STAGE/Programmer"
cp ../LICENSE "$STAGE/LICENSE.txt"
cp ../THIRD-PARTY-NOTICES.txt "$STAGE/THIRD-PARTY-NOTICES.txt"
cat > "$STAGE/Les meg.txt" <<EOF
ShowCue $VERSION for Mac

Installering:
  Dra ShowCue over på «Programmer».

Første gang du åpner ShowCue:
  Appen er ikke notarisert av Apple ennå, så macOS stopper den første gang.
  Åpne Systeminnstillinger → Personvern og sikkerhet, bla ned og trykk «Åpne likevel».
  Etter det starter ShowCue som vanlig.

  macOS spør også om tilgang til lokalt nettverk. Si ja, ellers virker ikke OSC.

ShowCue er fri programvare under GNU GPL versjon 3 (se LICENSE.txt), uten noen form for garanti.
Endringslogg og kildekode: https://github.com/trondmorset-lab/Showcue-
EOF

hdiutil create -volname "ShowCue $VERSION" -srcfolder "$STAGE" -fs HFS+ -format UDZO -imagekey zlib-level=9 -ov build/ShowCue-Mac.dmg
rm -rf "$STAGE"
echo "Ferdig: $(pwd)/build/ShowCue-Mac.dmg"
