#!/bin/bash
# Bygger ShowCue.app (krever Xcode eller Xcode Command Line Tools: xcode-select --install).
set -euo pipefail
cd "$(dirname "$0")"

echo "Bygger ShowCue …"
# Universell app (Apple Silicon + Intel). Krever Xcode; med bare Command Line Tools: UNIVERSAL=0 ./build-app.sh
if [ "${UNIVERSAL:-1}" = "1" ]; then
    swift build -c release --arch arm64 --arch x86_64
    BIN_DIR=$(swift build -c release --arch arm64 --arch x86_64 --show-bin-path)
else
    swift build -c release
    BIN_DIR=$(swift build -c release --show-bin-path)
fi

APP=build/ShowCue.app
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN_DIR/ShowCue" "$APP/Contents/MacOS/ShowCue"
cp Resources/Info.plist "$APP/Contents/Info.plist"
cp ../LICENSE "$APP/Contents/Resources/LICENSE.txt"
cp ../THIRD-PARTY-NOTICES.txt "$APP/Contents/Resources/THIRD-PARTY-NOTICES.txt"
cp Resources/AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"
# Programikonet som ressurskatalog (Assets.car), slik nyere macOS helst vil ha det. AppIcon.icns over er reserve
# hvis actool (følger med Xcode) ikke finnes.
if xcrun --find actool >/dev/null 2>&1 && xcrun actool Resources/Assets.xcassets --compile "$APP/Contents/Resources" \
        --platform macosx --minimum-deployment-target 13.0 --app-icon AppIcon \
        --output-partial-info-plist build/icon-info.plist >/dev/null; then
    /usr/libexec/PlistBuddy -c "Add :CFBundleIconName string AppIcon" "$APP/Contents/Info.plist" 2>/dev/null \
        || /usr/libexec/PlistBuddy -c "Set :CFBundleIconName AppIcon" "$APP/Contents/Info.plist"
    echo "Ikon: Assets.car + AppIcon.icns"
else
    echo "Ikon: AppIcon.icns (actool mangler – installer Xcode for ressurskatalog)"
fi
# Engelsk oversettelse av brukerflaten (felles med Windows).
cp ../i18n/en.json "$APP/Contents/Resources/en.json"
# Versjon fra VERSION i roten av repoet (felles for Windows og Mac).
VERSION=$(tr -d '[:space:]' < ../VERSION)
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $VERSION" "$APP/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion $VERSION" "$APP/Contents/Info.plist"
codesign --force --deep --sign - "$APP"

ARCHS=$(lipo -archs "$APP/Contents/MacOS/ShowCue")
echo "Arkitekturer: $ARCHS"
if [ "${UNIVERSAL:-1}" = "1" ] && ! { [[ "$ARCHS" == *arm64* ]] && [[ "$ARCHS" == *x86_64* ]]; }; then
    echo "Feil: appen er ikke universell ($ARCHS)." >&2
    exit 1
fi

echo ""
echo "Ferdig: $(pwd)/$APP"
echo "Dra den til Programmer-mappa, eller start med:  open \"$APP\""
