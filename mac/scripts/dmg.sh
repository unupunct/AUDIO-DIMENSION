#!/bin/bash
# Packages build/Audio Dimension.app into a compressed .dmg with an Applications shortcut.
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION="${VERSION:-1.0.0-preview.1}"
ARCH="$(lipo -archs "build/Audio Dimension.app/Contents/MacOS/AudioDimension" | tr ' ' '-')"
DMG="build/AudioDimension-$VERSION-macOS-$ARCH.dmg"
STAGE=build/dmg-stage
rm -rf "$STAGE" "$DMG"; mkdir -p "$STAGE"
cp -R "build/Audio Dimension.app" "$STAGE/"
ln -s /Applications "$STAGE/Applications"
cat > "$STAGE/READ ME FIRST.txt" <<TXT
Audio Dimension $VERSION for macOS ($ARCH)

1. Drag "Audio Dimension" onto the Applications folder.
2. The app is not notarized. The first time, right-click it in Applications, choose Open, then Open again.
3. For system audio install a virtual driver such as BlackHole (see the project page).

https://github.com/unupunct/AUDIO-DIMENSION
TXT
hdiutil create -volname "Audio Dimension" -srcfolder "$STAGE" -ov -format UDZO "$DMG" >/dev/null
rm -rf "$STAGE"
echo "Built $DMG ($(du -h "$DMG" | cut -f1))"
shasum -a 256 "$DMG"
