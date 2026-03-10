#!/bin/bash
# Uninstall Voicer
set -e

if [ "$EUID" -ne 0 ]; then
    echo "Please run with sudo: sudo /opt/voicer/uninstall.sh"
    exit 1
fi

echo "Uninstalling Voicer..."

rm -f /usr/bin/voicer
rm -f /usr/share/applications/voicer.desktop
rm -f /usr/share/icons/hicolor/256x256/apps/voicer.png
rm -rf /opt/voicer

update-desktop-database /usr/share/applications/ 2>/dev/null || true
gtk-update-icon-cache /usr/share/icons/hicolor/ 2>/dev/null || true

echo "Voicer uninstalled."
