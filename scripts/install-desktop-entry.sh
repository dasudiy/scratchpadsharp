#!/usr/bin/env bash
# Install ScratchpadSharp desktop entry and icons for GNOME / freedesktop.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ICON_SRC="$ROOT/src/ScratchpadSharp/Assets/app-icon.png"
APPS_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
ICONS_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor"
APP_ID="scratchpad-sharp"

if [[ ! -f "$ICON_SRC" ]]; then
  echo "Icon not found: $ICON_SRC" >&2
  exit 1
fi

# Resolve binary path: arg > Release > Debug
if [[ $# -ge 1 ]]; then
  BINARY="$(realpath "$1")"
elif [[ -x "$ROOT/src/ScratchpadSharp/bin/Release/net8.0/ScratchpadSharp" ]]; then
  BINARY="$ROOT/src/ScratchpadSharp/bin/Release/net8.0/ScratchpadSharp"
elif [[ -x "$ROOT/src/ScratchpadSharp/bin/Debug/net8.0/ScratchpadSharp" ]]; then
  BINARY="$ROOT/src/ScratchpadSharp/bin/Debug/net8.0/ScratchpadSharp"
else
  echo "Build the app first: dotnet build -c Release" >&2
  echo "Or pass the binary path: $0 /path/to/ScratchpadSharp" >&2
  exit 1
fi

mkdir -p "$APPS_DIR"

install_icon() {
  local size="$1"
  local dest="$ICONS_DIR/${size}x${size}/apps"
  mkdir -p "$dest"
  if command -v convert >/dev/null 2>&1; then
    convert "$ICON_SRC" -resize "${size}x${size}" "$dest/${APP_ID}.png"
  elif command -v magick >/dev/null 2>&1; then
    magick "$ICON_SRC" -resize "${size}x${size}" "$dest/${APP_ID}.png"
  else
    cp "$ICON_SRC" "$dest/${APP_ID}.png"
  fi
}

for size in 48 64 128 256 512; do
  install_icon "$size"
done

DESKTOP_FILE="$APPS_DIR/${APP_ID}.desktop"
cat > "$DESKTOP_FILE" <<EOF
[Desktop Entry]
Type=Application
Version=1.0
Name=ScratchpadSharp
GenericName=C# Scratchpad
Comment=Lightweight C# script runner with Roslyn IntelliSense
Exec="${BINARY}" %F
Icon=${APP_ID}
Terminal=false
StartupNotify=true
StartupWMClass=ScratchpadSharp
Categories=Development;IDE;TextEditor;
Keywords=csharp;script;roslyn;dotnet;
MimeType=text/x-csharp;
EOF

chmod +x "$BINARY" 2>/dev/null || true

if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database "$APPS_DIR" || true
fi

if command -v gtk-update-icon-cache >/dev/null 2>&1; then
  gtk-update-icon-cache -f -t "$ICONS_DIR" 2>/dev/null || true
fi

echo "Installed:"
echo "  Desktop entry: $DESKTOP_FILE"
echo "  Icons:         $ICONS_DIR/*/apps/${APP_ID}.png"
echo "  Binary:        $BINARY"
echo ""
echo "Launch from GNOME app grid as 'ScratchpadSharp', or run: gtk-launch ${APP_ID}"
