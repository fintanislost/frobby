#!/usr/bin/env bash
# Build the spike harness and drop it into SMAPI's Mods/ directory.
# Idempotent. Safe to re-run.

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HARNESS_DIR="$HERE/Harness"

# Auto-detect Flatpak Steam install. Override by setting SDV_INSTALL_PATH explicitly.
: "${SDV_INSTALL_PATH:=$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Stardew Valley}"
if [[ ! -d "$SDV_INSTALL_PATH" ]]; then
  echo "ERROR: SDV install not found at $SDV_INSTALL_PATH. Set SDV_INSTALL_PATH explicitly." >&2
  exit 2
fi
# Isolated mods dir — avoids loading the ~95 mods in the user's default Mods/.
: "${SMAPI_MODS_PATH:=$HERE/mods-isolated}"
mkdir -p "$SMAPI_MODS_PATH"
TARGET="$SMAPI_MODS_PATH/SdvTestFramework.SpikeHarness"
echo "==> SDV_INSTALL_PATH:  $SDV_INSTALL_PATH"
echo "==> Isolated Mods dir: $SMAPI_MODS_PATH"

echo "==> Building harness"
dotnet build "$HARNESS_DIR/Harness.csproj" -c Release

echo "==> Deploying to $TARGET"
mkdir -p "$TARGET"

BUILT_DIR="$HARNESS_DIR/bin/Release/net6.0"
cp "$BUILT_DIR/Harness.dll" "$TARGET/"
cp "$BUILT_DIR/Harness.pdb" "$TARGET/" 2>/dev/null || true
cp "$HARNESS_DIR/manifest.json" "$TARGET/"

echo "==> Installed: $(ls "$TARGET")"
