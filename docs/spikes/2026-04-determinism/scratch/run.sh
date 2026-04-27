#!/usr/bin/env bash
# M0 determinism experiment driver.
#
# Runs the harness twice with the same save + seed, captures draw events from each pass,
# normalizes per-run texture-reference IDs and diffs. Exit 0 iff streams match.
#
# Inputs (override with env):
#   SDV_INSTALL_PATH  — auto-detected under Flatpak Steam
#   SMAPI_MODS_PATH   — defaults to scratch/mods-isolated (harness only, no other mods)
#   FIXTURE           — SDV save folder name (e.g. m0spike_436510938). Defaults to the
#                       single save dir inside scratch/fixtures/; errors if zero or >1.
#   SEED              — RNG seed (default 42)
#   TICKS             — captured tick budget per pass (default 120)
#
# Prerequisites:
#   - ./deploy-harness.sh has been run (harness staged in SMAPI_MODS_PATH)
#   - ./stage-fixture.sh has been run (fixture staged in scratch/fixtures/<save-name>)

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RUNS_DIR="$HERE/logs/$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$RUNS_DIR"

: "${SDV_INSTALL_PATH:=$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Stardew Valley}"
: "${SEED:=42}"
: "${TICKS:=30}"  # 30 ticks ≈ 0.5 sec; with the 100k ring buffer, no drops expected.

SMAPI_BIN="$SDV_INSTALL_PATH/StardewModdingAPI"
[[ -x "$SMAPI_BIN" ]] || { echo "ERROR: SMAPI binary not at $SMAPI_BIN" >&2; exit 2; }

: "${SMAPI_MODS_PATH:=$HERE/mods-isolated}"
[[ -d "$SMAPI_MODS_PATH/SdvTestFramework.SpikeHarness" ]] || {
  echo "ERROR: harness not deployed to $SMAPI_MODS_PATH — run ./deploy-harness.sh" >&2; exit 3; }

# Determine fixture: explicit FIXTURE env var wins; otherwise pick the lone fixture dir.
if [[ -z "${FIXTURE:-}" ]]; then
  mapfile -t fxs < <(find "$HERE/fixtures" -mindepth 1 -maxdepth 1 -type d -printf '%f\n' 2>/dev/null || true)
  if [[ ${#fxs[@]} -eq 0 ]]; then
    echo "ERROR: no fixture dirs under $HERE/fixtures — run ./stage-fixture.sh first" >&2
    exit 4
  elif [[ ${#fxs[@]} -gt 1 ]]; then
    echo "ERROR: multiple fixture dirs; set FIXTURE explicitly. Found: ${fxs[*]}" >&2
    exit 4
  fi
  FIXTURE="${fxs[0]}"
fi
FIXTURE_SRC="$HERE/fixtures/$FIXTURE"
[[ -f "$FIXTURE_SRC/$FIXTURE" ]] || {
  echo "ERROR: fixture $FIXTURE_SRC/$FIXTURE missing" >&2; exit 5; }
echo "==> Fixture: $FIXTURE"

# When StardewModdingAPI is launched directly (our ./run.sh path, not through Flatpak
# Steam), SDV reads and writes saves under standard XDG_CONFIG_HOME. We target the same
# path so harness_load finds what we stage.
SAVES_CANDIDATES=(
  "${XDG_CONFIG_HOME:-$HOME/.config}/StardewValley/Saves"
  "$HOME/.var/app/com.valvesoftware.Steam/.config/StardewValley/Saves"
)
SAVES_DIR=""
for d in "${SAVES_CANDIDATES[@]}"; do
  [[ -d "$d" ]] && { SAVES_DIR="$d"; break; }
done
[[ -n "$SAVES_DIR" ]] || { echo "ERROR: SDV saves dir not found" >&2; exit 6; }
echo "==> SDV saves dir: $SAVES_DIR"

# Copy fixture back into SDV's saves dir (restores a clean starting state each run).
stage_save() {
  local dest="$SAVES_DIR/$FIXTURE"
  if [[ -d "$dest" ]]; then
    rm -rf "$dest"
  fi
  cp -r "$FIXTURE_SRC" "$dest"
}

# Headless display if nothing set.
XVFB_PID=""
if [[ -z "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ]]; then
  Xvfb :99 -screen 0 1280x720x24 >/dev/null 2>&1 &
  XVFB_PID=$!
  export DISPLAY=:99
  export LIBGL_ALWAYS_SOFTWARE=1
  trap 'kill "$XVFB_PID" 2>/dev/null || true' EXIT
fi

run_once() {
  local label="$1" out_jsonl="$2"

  stage_save

  local stdin_file="$RUNS_DIR/${label}.commands"
  cat > "$stdin_file" <<EOF
harness_load $FIXTURE
harness_pin_seed $SEED
harness_arm $TICKS $out_jsonl
EOF

  echo "[run $label] Launching SDV (fixture=$FIXTURE, seed=$SEED, ticks=$TICKS)"
  local stdout="$RUNS_DIR/${label}.stdout"
  local stderr="$RUNS_DIR/${label}.stderr"
  (
    cd "$SDV_INSTALL_PATH"
    "$SMAPI_BIN" --mods-path "$SMAPI_MODS_PATH" \
        < "$stdin_file" > "$stdout" 2> "$stderr" &
    SDV_PID=$!

    # Poll for jsonl file. Shorter timeout (60s) so diagnostics are fast; 120 ticks
    # should write in < 5s normally, and if it hasn't after a minute we want the log
    # back to debug.
    tries=0
    while (( tries < 60 )); do
      if [[ -f "$out_jsonl" ]]; then
        s1=$(stat -c%s "$out_jsonl"); sleep 2
        s2=$(stat -c%s "$out_jsonl")
        if [[ "$s1" == "$s2" && "$s1" != "0" ]]; then break; fi
      fi
      sleep 1
      tries=$((tries+1))
    done

    # Graceful shutdown: SIGINT first (SMAPI installs a handler that flushes logs cleanly),
    # short grace, then escalate.
    kill -INT "$SDV_PID" 2>/dev/null || true
    for _ in 1 2 3 4 5; do
      kill -0 "$SDV_PID" 2>/dev/null || break
      sleep 1
    done
    kill -TERM "$SDV_PID" 2>/dev/null || true
    wait "$SDV_PID" 2>/dev/null || true
  )

  [[ -s "$out_jsonl" ]] || { echo "ERROR: no draw events captured — see $stderr"; return 7; }
  local events
  events=$(grep -c '"type":"draw"' "$out_jsonl" || true)
  echo "[run $label] Captured $events draw events → $out_jsonl"
}

OUT1="$RUNS_DIR/run1.jsonl"
OUT2="$RUNS_DIR/run2.jsonl"

run_once 1 "$OUT1"
run_once 2 "$OUT2"

echo
echo "==> Normalizing + diffing"
if python3 "$HERE/analyze.py" "$OUT1" "$OUT2" > "$RUNS_DIR/diff.txt"; then
  echo "✓ Deterministic: identical streams ($(wc -l < "$OUT1") lines)."
  echo "  Run artefacts: $RUNS_DIR"
  exit 0
else
  rc=$?
  echo "✗ Divergence detected. See $RUNS_DIR/diff.txt" >&2
  head -50 "$RUNS_DIR/diff.txt" >&2
  exit $rc
fi
