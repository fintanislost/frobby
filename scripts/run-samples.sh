#!/usr/bin/env bash
# Sample-suite smoke runner. Stages Content Patcher + sample-cp-mod + harness into an
# isolated mods dir, launches Xvfb + SDV via the Runner, runs tests/samples/*.test.json.
# Returns non-zero if any scenario fails.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SDV_ROOT="$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Stardew Valley"
SAMPLES_MODS="$HOME/.cache/sdv-test-framework-samples/mods"

# 0. sanity-check Content Patcher is installed in the user's SDV mods dir
if [ ! -d "$SDV_ROOT/Mods/ContentPatcher" ]; then
    echo "error: Content Patcher not found at '$SDV_ROOT/Mods/ContentPatcher'" >&2
    echo "install it from https://www.nexusmods.com/stardewvalley/mods/1915" >&2
    exit 2
fi

# 1. build Release (also auto-stages the harness payload to the default mods cache)
cd "$REPO"
dotnet build -c Release >/dev/null

# 2. rebuild isolated mods dir
rm -rf "$SAMPLES_MODS"
mkdir -p "$SAMPLES_MODS"
cp -r ~/.cache/sdv-test-framework/mods/SdvTestFramework.Harness "$SAMPLES_MODS/"
cp -r "$SDV_ROOT/Mods/ContentPatcher" "$SAMPLES_MODS/"
cp -r "$REPO/tests/sample-cp-mod" "$SAMPLES_MODS/SdvTestFramework.SampleCpMod"

# 3. Xvfb + run the scenarios
pkill -9 -f StardewModdingAPI 2>/dev/null || true
pkill Xvfb 2>/dev/null || true
sleep 1
Xvfb :99 -screen 0 1280x720x24 >/dev/null 2>&1 &
XVFB_PID=$!
trap "pkill -9 -f StardewModdingAPI 2>/dev/null; kill $XVFB_PID 2>/dev/null; exit" EXIT

DISPLAY=:99 LIBGL_ALWAYS_SOFTWARE=1 dotnet run --project src/Runner -c Release --no-build -- \
    run "$REPO/tests/samples/" --mods-path "$SAMPLES_MODS" "$@"
