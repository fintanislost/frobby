#!/usr/bin/env bash
# Launch SMAPI + SDV pointed at the spike's isolated Mods directory.
# Used for both:
#   (a) fixture creation — play the new game, run `harness_save`, quit.
#   (b) interactive harness debugging.
#
# Not used by run.sh (that calls SMAPI directly with stdin-scripted commands).

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

: "${SDV_INSTALL_PATH:=$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Stardew Valley}"
if [[ ! -x "$SDV_INSTALL_PATH/StardewModdingAPI" ]]; then
  echo "ERROR: StardewModdingAPI not found at $SDV_INSTALL_PATH." >&2
  echo "       Set SDV_INSTALL_PATH explicitly if the autodetection is wrong." >&2
  exit 2
fi

export SMAPI_MODS_PATH="${SMAPI_MODS_PATH:-$HERE/mods-isolated}"
if [[ ! -d "$SMAPI_MODS_PATH/SdvTestFramework.SpikeHarness" ]]; then
  echo "ERROR: harness not staged. Run ./deploy-harness.sh first." >&2
  exit 3
fi

# Belt-and-braces: pass --mods-path as a CLI argument too. Empirically the env var is
# not reliably honoured in all launch environments (Flatpak sandboxing, Steam wrappers).
SMAPI_ARGS=(--mods-path "$SMAPI_MODS_PATH")

# Pass through the user's display by default. Use DISPLAY=headless to force Xvfb.
if [[ "${DISPLAY:-}" == "headless" || ( -z "${DISPLAY:-}" && -z "${WAYLAND_DISPLAY:-}" ) ]]; then
  echo "==> No display detected; starting Xvfb :99"
  Xvfb :99 -screen 0 1280x720x24 >/dev/null 2>&1 &
  XVFB_PID=$!
  trap 'kill "$XVFB_PID" 2>/dev/null || true' EXIT
  export DISPLAY=:99
  export LIBGL_ALWAYS_SOFTWARE=1
fi

echo "==> SDV_INSTALL_PATH: $SDV_INSTALL_PATH"
echo "==> SMAPI_MODS_PATH:  $SMAPI_MODS_PATH"
echo "==> DISPLAY:          $DISPLAY"
echo "==> Launching SMAPI (Ctrl+C or quit in-game to exit)"
echo

cd "$SDV_INSTALL_PATH"
echo "==> Invoking: ./StardewModdingAPI ${SMAPI_ARGS[*]} $*"
exec ./StardewModdingAPI "${SMAPI_ARGS[@]}" "$@"
