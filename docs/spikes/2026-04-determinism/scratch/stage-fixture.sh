#!/usr/bin/env bash
# Copy a SDV save directory (the whole tree: main save file, SaveGameInfo, etc.) into
# scratch/fixtures/ so run.sh can restore it to SDV's Saves/ directory between test runs.
#
# Usage:
#   stage-fixture.sh                     auto-detect most recently modified save
#   stage-fixture.sh <save_folder_name>  stage that save dir specifically

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
FIXTURES_DIR="$HERE/fixtures"
mkdir -p "$FIXTURES_DIR"

# Direct `./StardewModdingAPI` invocation uses standard XDG paths (~/.config/StardewValley).
# Flatpak/Steam-launched SDV uses the Flatpak-redirected XDG_CONFIG_HOME. We use the same
# path SDV writes to — when saves come from our launch-smapi.sh direct invocation they go
# under ~/.config, so that's our first-choice candidate.
SAVES_CANDIDATES=(
  "${XDG_CONFIG_HOME:-$HOME/.config}/StardewValley/Saves"
  "$HOME/.var/app/com.valvesoftware.Steam/.config/StardewValley/Saves"
)

SAVES_DIR=""
for d in "${SAVES_CANDIDATES[@]}"; do
  if [[ -d "$d" ]]; then SAVES_DIR="$d"; break; fi
done
[[ -n "$SAVES_DIR" ]] || { echo "ERROR: no SDV saves dir found" >&2; exit 4; }
echo "==> Saves dir: $SAVES_DIR"

SAVE_NAME="${1:-}"
if [[ -z "$SAVE_NAME" ]]; then
  newest="$(find "$SAVES_DIR" -mindepth 1 -maxdepth 1 -type d -printf '%T@\t%f\n' | sort -rn)"
  SAVE_NAME="$(printf '%s\n' "$newest" | awk -F '\t' 'NR==1 {print $2}')"
  [[ -n "$SAVE_NAME" ]] || { echo "ERROR: no save subdirs under $SAVES_DIR" >&2; exit 5; }
  echo "==> Auto-detected most recent save: $SAVE_NAME"
fi

SRC_DIR="$SAVES_DIR/$SAVE_NAME"
[[ -f "$SRC_DIR/$SAVE_NAME" ]] || { echo "ERROR: $SRC_DIR/$SAVE_NAME missing" >&2; exit 6; }

DEST_DIR="$FIXTURES_DIR/$SAVE_NAME"
if [[ -d "$DEST_DIR" ]]; then
  BACKUP="$DEST_DIR.bak-$(date -u +%Y%m%dT%H%M%SZ)"
  mv "$DEST_DIR" "$BACKUP"
  echo "==> Backed up previous fixture to $BACKUP"
fi

cp -r "$SRC_DIR" "$DEST_DIR"
echo "==> Staged full save tree: $SRC_DIR -> $DEST_DIR"
echo "    $(find "$DEST_DIR" -type f | wc -l) files, $(du -sh "$DEST_DIR" | cut -f1) total"

META="$FIXTURES_DIR/$SAVE_NAME.meta.json"
cat > "$META" <<EOF
{
  "save_name": "${SAVE_NAME}",
  "logical_name": "spring_day_1_clean",
  "source_path": "${SRC_DIR}",
  "created_at": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "spike": "2026-04-determinism",
  "sdv_version": "1.6.15.24356",
  "smapi_version": "4.5.2"
}
EOF
echo "==> Wrote $META"
echo
echo "==> Use this in run.sh: FIXTURE=$SAVE_NAME ./run.sh"
