#!/usr/bin/env bash
# Validate the release-shaped package flow without publishing to NuGet.
#
# This script is safe for local development and CI: it builds packages, installs
# the CLI from the local package source into a clean temporary mod repo, validates
# expected package artifacts, and writes an artifact manifest for review.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

PACKAGE_VERSION="${FROBBY_PACKAGE_VERSION:-$(sed -n 's:.*<SdvTestFrameworkVersion>\(.*\)</SdvTestFrameworkVersion>.*:\1:p' Directory.Build.props)}"
PACKAGE_SOURCE="${FROBBY_PACKAGE_SOURCE:-"$REPO_ROOT/nupkg"}"
MANIFEST="$PACKAGE_SOURCE/release-dry-run.json"

EXPECTED_PACKAGE_IDS=(
  "SdvTestFramework.Protocol"
  "SdvTestFramework.Runner.Dsl"
  "SdvTestFramework.Cli"
)

if [ -z "$PACKAGE_VERSION" ]; then
  echo "[release-dry-run] Could not read SdvTestFrameworkVersion from Directory.Build.props." >&2
  exit 2
fi

"$REPO_ROOT/scripts/release-env-preflight.sh"
"$REPO_ROOT/scripts/package-install-smoke.sh"

missing=0
for package_id in "${EXPECTED_PACKAGE_IDS[@]}"; do
  package_path="$PACKAGE_SOURCE/$package_id.$PACKAGE_VERSION.nupkg"
  if [ ! -f "$package_path" ]; then
    echo "[release-dry-run] Missing expected package: $package_path" >&2
    missing=1
  fi
done

if [ "$missing" != "0" ]; then
  exit 3
fi

mkdir -p "$PACKAGE_SOURCE"
generated_at="$(date -u +"%Y-%m-%dT%H:%M:%SZ")"

{
  printf '{\n'
  printf '  "version": "%s",\n' "$PACKAGE_VERSION"
  printf '  "generatedAtUtc": "%s",\n' "$generated_at"
  printf '  "packages": [\n'
  for index in "${!EXPECTED_PACKAGE_IDS[@]}"; do
    package_id="${EXPECTED_PACKAGE_IDS[$index]}"
    package_path="nupkg/$package_id.$PACKAGE_VERSION.nupkg"
    comma=","
    if [ "$index" -eq "$((${#EXPECTED_PACKAGE_IDS[@]} - 1))" ]; then
      comma=""
    fi
    printf '    { "id": "%s", "path": "%s" }%s\n' "$package_id" "$package_path" "$comma"
  done
  printf '  ]\n'
  printf '}\n'
} > "$MANIFEST"

echo "[release-dry-run] wrote $MANIFEST"
echo "PASS release dry-run"
