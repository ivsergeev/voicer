#!/bin/bash
# Install OpenVoicer from tarball
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
INSTALL_DIR="/opt/openvoicer"

# Check for root
if [ "$EUID" -ne 0 ]; then
    echo "Please run with sudo: sudo ./install.sh"
    exit 1
fi

echo "Installing OpenVoicer to $INSTALL_DIR..."

# Create installation directory
mkdir -p "$INSTALL_DIR"

# Copy all files
cp -R "$SCRIPT_DIR/"* "$INSTALL_DIR/"

# Remove install/uninstall scripts from installation directory
rm -f "$INSTALL_DIR/install.sh"

# Make executable
chmod +x "$INSTALL_DIR/OpenVoicer"
find "$INSTALL_DIR" -name "*.so" -exec chmod +x {} \;

# Create writable settings file
touch "$INSTALL_DIR/settings.json"
chmod 666 "$INSTALL_DIR/settings.json"

# Create CLI symlink
ln -sf "$INSTALL_DIR/OpenVoicer" /usr/bin/openvoicer

# Install desktop entry
mkdir -p /usr/share/applications
if [ -f "$INSTALL_DIR/openvoicer.desktop" ]; then
    cp "$INSTALL_DIR/openvoicer.desktop" /usr/share/applications/
fi

# Install icon
if [ -f "$INSTALL_DIR/openvoicer.png" ]; then
    mkdir -p /usr/share/icons/hicolor/256x256/apps
    cp "$INSTALL_DIR/openvoicer.png" /usr/share/icons/hicolor/256x256/apps/
    gtk-update-icon-cache /usr/share/icons/hicolor/ 2>/dev/null || true
fi

# Update desktop database
update-desktop-database /usr/share/applications/ 2>/dev/null || true

echo ""
echo "OpenVoicer installed successfully!"
echo "Run 'openvoicer' or find it in your application menu."
echo "To uninstall: sudo /opt/openvoicer/uninstall.sh"
