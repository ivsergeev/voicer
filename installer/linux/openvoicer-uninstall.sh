#!/bin/bash
# Uninstall OpenVoicer
set -e

if [ "$EUID" -ne 0 ]; then
    echo "Please run with sudo: sudo /opt/openvoicer/uninstall.sh"
    exit 1
fi

echo "Uninstalling OpenVoicer..."

rm -f /usr/bin/openvoicer
rm -f /usr/share/applications/openvoicer.desktop
rm -f /usr/share/icons/hicolor/256x256/apps/openvoicer.png
rm -rf /opt/openvoicer

update-desktop-database /usr/share/applications/ 2>/dev/null || true
gtk-update-icon-cache /usr/share/icons/hicolor/ 2>/dev/null || true

echo "OpenVoicer uninstalled."
