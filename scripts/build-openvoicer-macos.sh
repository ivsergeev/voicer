#!/bin/bash
# Build OpenVoicer for macOS — .app bundle + .dmg installer
# Usage: ./build-openvoicer-macos.sh [Configuration]
# Example: ./build-openvoicer-macos.sh Release
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
CONFIGURATION="${1:-Release}"
VERSION="1.0.0"
APP_NAME="OpenVoicer"

echo "=== OpenVoicer macOS Build ==="

# --- Phase 1: Check prerequisites ---
echo ""
echo "--- Checking prerequisites ---"

if ! command -v dotnet &>/dev/null; then
    echo "ERROR: .NET SDK not found. Install from https://dot.net" >&2
    exit 1
fi
echo "dotnet: $(dotnet --version)"

if ! command -v hdiutil &>/dev/null; then
    echo "ERROR: hdiutil not found. This script must run on macOS." >&2
    exit 1
fi
echo "hdiutil: OK"

# --- Phase 2: Build for each architecture ---
OUTPUT_DIR="$ROOT_DIR/output"
mkdir -p "$OUTPUT_DIR"

for RUNTIME in osx-x64 osx-arm64; do
    ARCH_SUFFIX="${RUNTIME#osx-}"

    # --- Phase 2a: Publish application ---
    echo ""
    echo "--- Publishing ($CONFIGURATION, $RUNTIME) ---"
    PUBLISH_DIR="$ROOT_DIR/publish-openvoicer-$RUNTIME"
    rm -rf "$PUBLISH_DIR"

    dotnet publish "$ROOT_DIR/src/OpenVoicer/OpenVoicer.csproj" \
        -c "$CONFIGURATION" \
        -r "$RUNTIME" \
        --self-contained \
        -o "$PUBLISH_DIR"

    PUBLISH_SIZE=$(du -sh "$PUBLISH_DIR" | cut -f1)
    echo "Published: $PUBLISH_DIR ($PUBLISH_SIZE)"

    # --- Phase 2b: Create .app bundle ---
    echo ""
    echo "--- Creating $APP_NAME.app ($RUNTIME) ---"

    APP_STAGING="$ROOT_DIR/publish-openvoicer-$RUNTIME-app"
    APP_DIR="$APP_STAGING/$APP_NAME.app"
    rm -rf "$APP_STAGING"

    mkdir -p "$APP_DIR/Contents/MacOS"
    mkdir -p "$APP_DIR/Contents/Resources"

    # Copy published files into Contents/MacOS
    cp -R "$PUBLISH_DIR/"* "$APP_DIR/Contents/MacOS/"

    # Copy Info.plist
    cp "$ROOT_DIR/installer/macos/OpenVoicer-Info.plist" "$APP_DIR/Contents/Info.plist"

    # Generate .icns icon
    ICON_SOURCE="$ROOT_DIR/installer/icons/icon-1024.png"
    if [ -f "$ICON_SOURCE" ] && command -v iconutil &>/dev/null; then
        echo "Generating .icns icon..."
        # Generate OpenVoicer icon PNG first
        if command -v python3 &>/dev/null && [ -f "$SCRIPT_DIR/generate-icon.py" ]; then
            python3 "$SCRIPT_DIR/generate-icon.py" 2>/dev/null || true
        fi

        # Use openvoicer-specific icon if available, otherwise generic
        OV_ICON_SOURCE="$ROOT_DIR/installer/icons/openvoicer-1024.png"
        if [ ! -f "$OV_ICON_SOURCE" ]; then
            OV_ICON_SOURCE="$ICON_SOURCE"
        fi

        ICONSET_DIR="$APP_STAGING/openvoicer.iconset"
        mkdir -p "$ICONSET_DIR"
        sips -z 16 16     "$OV_ICON_SOURCE" --out "$ICONSET_DIR/icon_16x16.png"      >/dev/null 2>&1
        sips -z 32 32     "$OV_ICON_SOURCE" --out "$ICONSET_DIR/icon_16x16@2x.png"   >/dev/null 2>&1
        sips -z 32 32     "$OV_ICON_SOURCE" --out "$ICONSET_DIR/icon_32x32.png"      >/dev/null 2>&1
        sips -z 64 64     "$OV_ICON_SOURCE" --out "$ICONSET_DIR/icon_32x32@2x.png"   >/dev/null 2>&1
        sips -z 128 128   "$OV_ICON_SOURCE" --out "$ICONSET_DIR/icon_128x128.png"    >/dev/null 2>&1
        sips -z 256 256   "$OV_ICON_SOURCE" --out "$ICONSET_DIR/icon_128x128@2x.png" >/dev/null 2>&1
        sips -z 256 256   "$OV_ICON_SOURCE" --out "$ICONSET_DIR/icon_256x256.png"    >/dev/null 2>&1
        sips -z 512 512   "$OV_ICON_SOURCE" --out "$ICONSET_DIR/icon_256x256@2x.png" >/dev/null 2>&1
        sips -z 512 512   "$OV_ICON_SOURCE" --out "$ICONSET_DIR/icon_512x512.png"    >/dev/null 2>&1
        sips -z 1024 1024 "$OV_ICON_SOURCE" --out "$ICONSET_DIR/icon_512x512@2x.png" >/dev/null 2>&1
        iconutil -c icns "$ICONSET_DIR" -o "$APP_DIR/Contents/Resources/openvoicer.icns"
        rm -rf "$ICONSET_DIR"
        echo "Icon: OK"
    else
        echo "NOTE: No icon source — using default macOS icon"
    fi

    # Ensure main executable is executable
    chmod +x "$APP_DIR/Contents/MacOS/OpenVoicer"

    # --- Phase 2c: Create .dmg ---
    echo ""
    echo "--- Creating .dmg ($RUNTIME) ---"

    DMG_NAME="OpenVoicer-${VERSION}-macos-${ARCH_SUFFIX}.dmg"
    DMG_PATH="$OUTPUT_DIR/$DMG_NAME"
    rm -f "$DMG_PATH"

    # Create staging directory with .app and Applications symlink
    DMG_STAGING="$ROOT_DIR/publish-openvoicer-$RUNTIME-dmg"
    rm -rf "$DMG_STAGING"
    mkdir -p "$DMG_STAGING"
    cp -R "$APP_DIR" "$DMG_STAGING/"
    ln -s /Applications "$DMG_STAGING/Applications"

    # Create compressed DMG
    hdiutil create \
        -volname "$APP_NAME" \
        -srcfolder "$DMG_STAGING" \
        -ov \
        -format UDZO \
        "$DMG_PATH"

    # Clean up staging
    rm -rf "$DMG_STAGING" "$APP_STAGING" "$PUBLISH_DIR"

    DMG_SIZE=$(du -h "$DMG_PATH" | cut -f1)
    echo "  $DMG_NAME ($DMG_SIZE)"
done

# --- Report ---
echo ""
echo "=== Build complete ==="
echo "Output directory: $OUTPUT_DIR/"
ls -lh "$OUTPUT_DIR"/OpenVoicer-*-macos-*.dmg 2>/dev/null
echo ""
echo "To install: open the .dmg and drag OpenVoicer to Applications"
echo "NOTE: On first launch, right-click > Open to bypass Gatekeeper (unsigned app)"
