#!/bin/bash
set -e

APP_NAME="ShokzMP3Sync"
PUBLISH_DIR="src/ShokzMP3Sync/bin/Release/net9.0/osx-arm64/publish"
APP_BUNDLE="build/${APP_NAME}.app"

echo "=== Building ${APP_NAME}.app ==="

# 1. Publish (not single-file, to keep native libs alongside)
echo "[1/3] Publishing..."
dotnet publish src/ShokzMP3Sync -c Release -r osx-arm64 --self-contained true -verbosity:quiet

# 2. Create .app bundle structure
echo "[2/3] Creating .app bundle..."
rm -rf "${APP_BUNDLE}"
mkdir -p "${APP_BUNDLE}/Contents/MacOS"
mkdir -p "${APP_BUNDLE}/Contents/Resources"

# Copy all published files into MacOS/
cp -R "${PUBLISH_DIR}/"* "${APP_BUNDLE}/Contents/MacOS/"
cp packaging/macos/Info.plist "${APP_BUNDLE}/Contents/Info.plist"

# Copy icon if exists
if [ -f "packaging/macos/AppIcon.icns" ]; then
    cp packaging/macos/AppIcon.icns "${APP_BUNDLE}/Contents/Resources/AppIcon.icns"
fi

# 3. Make executable
chmod +x "${APP_BUNDLE}/Contents/MacOS/${APP_NAME}"

TOTAL_SIZE=$(du -sh "${APP_BUNDLE}" | cut -f1)

echo "[3/3] Done!"
echo ""
echo "Output: ${APP_BUNDLE} (${TOTAL_SIZE})"
echo ""
echo "To install: cp -r ${APP_BUNDLE} /Applications/"
