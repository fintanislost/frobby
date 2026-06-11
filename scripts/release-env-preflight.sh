#!/usr/bin/env bash
# Validate the environment needed by game-backed package/release builds.

set -euo pipefail

if [ -n "${FROBBY_GAME_PATH:-}" ]; then
  if [ ! -d "$FROBBY_GAME_PATH" ]; then
    echo "[release-env-preflight] FROBBY_GAME_PATH does not exist: $FROBBY_GAME_PATH" >&2
    exit 2
  fi

  for file in "Stardew Valley.dll" "StardewModdingAPI.dll"; do
    if [ ! -f "$FROBBY_GAME_PATH/$file" ]; then
      echo "[release-env-preflight] FROBBY_GAME_PATH is missing $file: $FROBBY_GAME_PATH" >&2
      exit 2
    fi
  done

  echo "[release-env-preflight] using FROBBY_GAME_PATH=$FROBBY_GAME_PATH"
  exit 0
fi

if [ "${GITHUB_ACTIONS:-}" = "true" ]; then
  cat >&2 <<'EOF'
[release-env-preflight] FROBBY_GAME_PATH is required for GitHub Actions release/package builds.
Pathoschild.Stardew.ModBuildConfig compiles the Harness against Stardew Valley and SMAPI assemblies,
and public hosted runners do not include Stardew Valley.
Set FROBBY_RELEASE_RUNNER to a game-backed self-hosted runner label and set repository variable
FROBBY_GAME_PATH to a real Stardew install, or use ./scripts/ci-public.sh for hosted validation
that does not package the Harness.
EOF
  exit 2
fi

echo "[release-env-preflight] FROBBY_GAME_PATH not set; relying on Pathoschild.Stardew.ModBuildConfig autodetect."
