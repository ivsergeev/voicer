#!/bin/bash
# Install Voicer from tarball
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
INSTALL_DIR="/opt/voicer"

# Check for root
if [ "$EUID" -ne 0 ]; then
    echo "Please run with sudo: sudo ./install.sh"
    exit 1
fi

echo "Installing Voicer to $INSTALL_DIR..."

# Create installation directory
mkdir -p "$INSTALL_DIR"

# Copy all files
cp -R "$SCRIPT_DIR/"* "$INSTALL_DIR/"

# Remove install/uninstall scripts from installation directory
rm -f "$INSTALL_DIR/install.sh"

# Make executable
chmod +x "$INSTALL_DIR/Voicer"
find "$INSTALL_DIR" -name "*.so" -exec chmod +x {} \;

# Create writable settings file
touch "$INSTALL_DIR/settings.json"
chmod 666 "$INSTALL_DIR/settings.json"

# Create CLI symlink
ln -sf "$INSTALL_DIR/Voicer" /usr/bin/voicer

# Install desktop entry
mkdir -p /usr/share/applications
if [ -f "$INSTALL_DIR/voicer.desktop" ]; then
    cp "$INSTALL_DIR/voicer.desktop" /usr/share/applications/
fi

# Install icon
if [ -f "$INSTALL_DIR/voicer.png" ]; then
    mkdir -p /usr/share/icons/hicolor/256x256/apps
    cp "$INSTALL_DIR/voicer.png" /usr/share/icons/hicolor/256x256/apps/
    gtk-update-icon-cache /usr/share/icons/hicolor/ 2>/dev/null || true
fi

# Update desktop database
update-desktop-database /usr/share/applications/ 2>/dev/null || true

echo ""
echo "Voicer installed successfully!"
echo "Run 'voicer' or find it in your application menu."
echo "To uninstall: sudo /opt/voicer/uninstall.sh"
