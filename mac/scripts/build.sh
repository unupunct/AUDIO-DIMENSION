#!/bin/bash
# Builds "Audio Dimension.app" with swiftc only (works with the Command Line Tools, no full Xcode).
# Usage: scripts/build.sh [debug|release]      env: ARCHS="x86_64 arm64"  MIN_OS=12.0  VERSION=1.0.0-preview.1
set -euo pipefail
cd "$(dirname "$0")/.."
CONFIG="${1:-release}"; MIN_OS="${MIN_OS:-12.0}"; ARCHS="${ARCHS:-$(uname -m)}"; VERSION="${VERSION:-1.0.0-preview.1}"
OPT="-Onone -g"; [ "$CONFIG" = "release" ] && OPT="-O"
OUT=build; APP="$OUT/Audio Dimension.app"
rm -rf "$APP" "$OUT/obj"; mkdir -p "$OUT/obj" "$APP/Contents/MacOS" "$APP/Contents/Resources"
SOURCES=$(find Sources/ADMac -name '*.swift')
SLICES=()
for a in $ARCHS; do
  swiftc $OPT -parse-as-library -module-name AudioDimension -target "$a-apple-macos$MIN_OS" $SOURCES -o "$OUT/obj/AudioDimension-$a"
  SLICES+=("$OUT/obj/AudioDimension-$a")
done
if [ ${#SLICES[@]} -gt 1 ]; then lipo -create "${SLICES[@]}" -output "$APP/Contents/MacOS/AudioDimension"; else cp "${SLICES[0]}" "$APP/Contents/MacOS/AudioDimension"; fi
cp Resources/builtin-presets.json "$APP/Contents/Resources/"
cp -R Resources/Thumbnails "$APP/Contents/Resources/Thumbnails"
[ -f Resources/AppIcon.icns ] && cp Resources/AppIcon.icns "$APP/Contents/Resources/"
cp ../LICENSE ../THIRD_PARTY_NOTICES.md "$APP/Contents/Resources/" 2>/dev/null || true
cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleName</key><string>Audio Dimension</string>
<key>CFBundleDisplayName</key><string>Audio Dimension</string>
<key>CFBundleIdentifier</key><string>com.unupunct.AudioDimension</string>
<key>CFBundleExecutable</key><string>AudioDimension</string>
<key>CFBundleIconFile</key><string>AppIcon</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>${VERSION%%-*}</string>
<key>CFBundleVersion</key><string>1</string>
<key>LSMinimumSystemVersion</key><string>$MIN_OS</string>
<key>LSApplicationCategoryType</key><string>public.app-category.music</string>
<key>NSHighResolutionCapable</key><true/>
<key>NSPrincipalClass</key><string>NSApplication</string>
<key>NSMicrophoneUsageDescription</key><string>Audio Dimension listens to your microphone or audio input only while you press Start or Record, to draw the visuals and record your audio.</string>
</dict></plist>
PLIST
codesign --force --sign - "$APP"
echo "Built $APP ($(lipo -archs "$APP/Contents/MacOS/AudioDimension"))"
