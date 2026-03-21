#!/bin/bash
# Build OpenVoicer for Linux — .deb package + .tar.gz archive
# Usage: ./build-openvoicer-linux.sh [Configuration] [Runtime]
# Example: ./build-openvoicer-linux.sh Release linux-x64
#          ./build-openvoicer-linux.sh Release linux-arm64
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
CONFIGURATION="${1:-Release}"
RUNTIME="${2:-linux-x64}"
VERSION="1.0.0"
APP_NAME="openvoicer"

echo "=== OpenVoicer Linux Build ($RUNTIME) ==="

# --- Phase 1: Check prerequisites ---
echo ""
echo "--- Checking prerequisites ---"

if ! command -v dotnet &>/dev/null; then
    echo "ERROR: .NET SDK not found. Install from https://dot.net" >&2
    exit 1
fi
echo "dotnet: $(dotnet --version)"

SKIP_DEB=""
if ! command -v dpkg-deb &>/dev/null; then
    echo "WARNING: dpkg-deb not found. .deb package will be skipped."
    SKIP_DEB=1
else
    echo "dpkg-deb: OK"
fi

if ! command -v tar &>/dev/null; then
    echo "ERROR: tar not found." >&2
    exit 1
fi
echo "tar: OK"

# Determine architecture
case "$RUNTIME" in
    linux-x64)   DEB_ARCH="amd64";  ARCH_SUFFIX="x64" ;;
    linux-arm64) DEB_ARCH="arm64";  ARCH_SUFFIX="arm64" ;;
    *)
        echo "ERROR: Unsupported runtime '$RUNTIME'. Use linux-x64 or linux-arm64." >&2
        exit 1
        ;;
esac

# --- Phase 2: Publish application ---
echo ""
echo "--- Publishing application ($CONFIGURATION, $RUNTIME) ---"
PUBLISH_DIR="$ROOT_DIR/publish-openvoicer-$RUNTIME"
rm -rf "$PUBLISH_DIR"

dotnet publish "$ROOT_DIR/src/OpenVoicer/OpenVoicer.csproj" \
    -c "$CONFIGURATION" \
    -r "$RUNTIME" \
    --self-contained \
    -o "$PUBLISH_DIR"

PUBLISH_SIZE=$(du -sh "$PUBLISH_DIR" | cut -f1)
echo "Published: $PUBLISH_DIR ($PUBLISH_SIZE)"

OUTPUT_DIR="$ROOT_DIR/output"
mkdir -p "$OUTPUT_DIR"

# --- Phase 3a: Create .deb package ---
if [ -z "$SKIP_DEB" ]; then
    echo ""
    echo "--- Creating .deb package ---"

    DEB_ROOT="$ROOT_DIR/publish-openvoicer-$RUNTIME-deb"
    rm -rf "$DEB_ROOT"

    # Create directory structure
    mkdir -p "$DEB_ROOT/DEBIAN"
    mkdir -p "$DEB_ROOT/opt/openvoicer"
    mkdir -p "$DEB_ROOT/usr/share/applications"
    mkdir -p "$DEB_ROOT/usr/share/icons/hicolor/256x256/apps"

    # Copy published files
    cp -R "$PUBLISH_DIR/"* "$DEB_ROOT/opt/openvoicer/"
    chmod +x "$DEB_ROOT/opt/openvoicer/OpenVoicer"

    # Copy desktop entry
    cp "$ROOT_DIR/installer/linux/openvoicer.desktop" "$DEB_ROOT/usr/share/applications/"

    # Copy icon if available
    ICON_SOURCE="$ROOT_DIR/installer/icons/icon-256.png"
    if [ -f "$ICON_SOURCE" ]; then
        cp "$ICON_SOURCE" "$DEB_ROOT/usr/share/icons/hicolor/256x256/apps/openvoicer.png"
        cp "$ICON_SOURCE" "$DEB_ROOT/opt/openvoicer/openvoicer.png"
    fi

    # Create DEBIAN/control with computed size and architecture
    INSTALLED_SIZE=$(du -sk "$DEB_ROOT/opt" "$DEB_ROOT/usr" 2>/dev/null | awk '{s+=$1} END {print s}')
    sed "s/SIZE_PLACEHOLDER/$INSTALLED_SIZE/; s/Architecture: amd64/Architecture: $DEB_ARCH/" \
        "$ROOT_DIR/installer/linux/DEBIAN/openvoicer-control" > "$DEB_ROOT/DEBIAN/control"

    # Copy maintainer scripts
    cp "$ROOT_DIR/installer/linux/DEBIAN/openvoicer-postinst" "$DEB_ROOT/DEBIAN/postinst"
    cp "$ROOT_DIR/installer/linux/DEBIAN/openvoicer-prerm" "$DEB_ROOT/DEBIAN/prerm"
    chmod 755 "$DEB_ROOT/DEBIAN/postinst"
    chmod 755 "$DEB_ROOT/DEBIAN/prerm"

    # Set proper permissions
    find "$DEB_ROOT" -type d -exec chmod 755 {} \;
    find "$DEB_ROOT/opt/openvoicer" -type f -exec chmod 644 {} \;
    chmod 755 "$DEB_ROOT/opt/openvoicer/OpenVoicer"
    find "$DEB_ROOT/opt/openvoicer" -name "*.so" -exec chmod 755 {} \;
    find "$DEB_ROOT/opt/openvoicer" -name "*.so.*" -exec chmod 755 {} \;

    # Build .deb
    DEB_NAME="${APP_NAME}_${VERSION}_${DEB_ARCH}.deb"
    dpkg-deb --build --root-owner-group "$DEB_ROOT" "$OUTPUT_DIR/$DEB_NAME"

    rm -rf "$DEB_ROOT"
    DEB_SIZE=$(du -h "$OUTPUT_DIR/$DEB_NAME" | cut -f1)
    echo "  $DEB_NAME ($DEB_SIZE)"
fi

# --- Phase 3b: Create .tar.gz archive ---
echo ""
echo "--- Creating .tar.gz archive ---"

TAR_STAGING="$ROOT_DIR/publish-openvoicer-$RUNTIME-tar"
TAR_DIR_NAME="openvoicer-$VERSION"
rm -rf "$TAR_STAGING"
mkdir -p "$TAR_STAGING/$TAR_DIR_NAME"

# Copy published files
cp -R "$PUBLISH_DIR/"* "$TAR_STAGING/$TAR_DIR_NAME/"
chmod +x "$TAR_STAGING/$TAR_DIR_NAME/OpenVoicer"
find "$TAR_STAGING/$TAR_DIR_NAME" -name "*.so" -exec chmod +x {} \;
find "$TAR_STAGING/$TAR_DIR_NAME" -name "*.so.*" -exec chmod +x {} \;

# Copy desktop entry, icon, install/uninstall scripts
cp "$ROOT_DIR/installer/linux/openvoicer.desktop" "$TAR_STAGING/$TAR_DIR_NAME/"
if [ -f "$ROOT_DIR/installer/icons/icon-256.png" ]; then
    cp "$ROOT_DIR/installer/icons/icon-256.png" "$TAR_STAGING/$TAR_DIR_NAME/openvoicer.png"
fi
cp "$ROOT_DIR/installer/linux/openvoicer-install.sh" "$TAR_STAGING/$TAR_DIR_NAME/install.sh"
cp "$ROOT_DIR/installer/linux/openvoicer-uninstall.sh" "$TAR_STAGING/$TAR_DIR_NAME/uninstall.sh"
chmod +x "$TAR_STAGING/$TAR_DIR_NAME/install.sh"
chmod +x "$TAR_STAGING/$TAR_DIR_NAME/uninstall.sh"

# Create tarball
TAR_NAME="openvoicer-${VERSION}-linux-${ARCH_SUFFIX}.tar.gz"
tar -czf "$OUTPUT_DIR/$TAR_NAME" -C "$TAR_STAGING" "$TAR_DIR_NAME"

rm -rf "$TAR_STAGING" "$PUBLISH_DIR"
TAR_SIZE=$(du -h "$OUTPUT_DIR/$TAR_NAME" | cut -f1)
echo "  $TAR_NAME ($TAR_SIZE)"

# --- Phase 4: Report ---
echo ""
echo "=== Build complete ==="
echo "Output directory: $OUTPUT_DIR/"
ls -lh "$OUTPUT_DIR"/${APP_NAME}*${ARCH_SUFFIX}* 2>/dev/null || true
echo ""
if [ -z "$SKIP_DEB" ]; then
    echo "Install .deb:     sudo dpkg -i $OUTPUT_DIR/$DEB_NAME"
fi
echo "Install .tar.gz:  tar xzf $OUTPUT_DIR/$TAR_NAME && cd $TAR_DIR_NAME && sudo ./install.sh"
