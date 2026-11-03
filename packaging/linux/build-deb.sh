#!/usr/bin/env bash
# Builds dist/taskitify_<version>_amd64.deb: a self-contained .NET publish under /opt/taskitify,
# a /usr/bin/taskitify symlink, the menu entry, the icon and the GNOME Shell extension.
#
#   packaging/linux/build-deb.sh [version]      # default 1.0.0
set -euo pipefail

VERSION="${1:-1.0.0}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
PKG="$WORK/pkg"
UUID="taskitify-lyrics@carlosvinnifernandes.github.io"

dotnet publish "$ROOT/src/Taskitify.Linux" -c Release -r linux-x64 --self-contained true \
  -p:DebugType=None -p:Version="${VERSION%%-*}" -o "$WORK/publish"

install -d "$PKG/opt/taskitify" "$PKG/usr/bin" "$PKG/usr/share/applications" \
  "$PKG/usr/share/icons/hicolor/512x512/apps" "$PKG/usr/share/gnome-shell/extensions/$UUID" "$PKG/DEBIAN"
cp -r "$WORK/publish/." "$PKG/opt/taskitify/"
chmod 755 "$PKG/opt/taskitify/taskitify"
ln -s /opt/taskitify/taskitify "$PKG/usr/bin/taskitify"
install -m 644 "$ROOT/packaging/linux/taskitify.desktop" "$PKG/usr/share/applications/taskitify.desktop"
install -m 644 "$ROOT/docs/images/logo.png" "$PKG/usr/share/icons/hicolor/512x512/apps/taskitify.png"
install -m 644 "$ROOT"/gnome-extension/"$UUID"/* "$PKG/usr/share/gnome-shell/extensions/$UUID/"

INSTALLED_KB="$(du -sk "$PKG" | cut -f1)"
cat > "$PKG/DEBIAN/control" <<CONTROL
Package: taskitify
Version: $VERSION
Section: sound
Priority: optional
Architecture: amd64
Maintainer: Carlos Fernandes <noreply@users.noreply.github.com>
Installed-Size: $INSTALLED_KB
Depends: libx11-6, libice6, libsm6, libfontconfig1
Recommends: python3, python3-gi, gir1.2-webkit2-4.1
Suggests: spotify-client
Homepage: https://github.com/CarlosVinniFernandes/Taskitify
Description: Synced Spotify lyrics for Linux
 Follows Spotify Desktop through MPRIS and shows the current lyric line in a
 floating bar or, with the bundled GNOME Shell extension, in the top bar next
 to the clock. Lyrics come from LRCLIB; no Spotify login is needed.
CONTROL

mkdir -p "$ROOT/dist"
OUT="$ROOT/dist/taskitify_${VERSION}_amd64.deb"
dpkg-deb --build --root-owner-group "$PKG" "$OUT"
echo "Built $OUT"
