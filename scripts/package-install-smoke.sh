#!/usr/bin/env bash
# Smoke-test the packaged CLI from a clean repo-local dotnet tool install.
#
# This intentionally avoids the source-checkout wrapper path used by Frobby
# developers. It proves a mod repo can depend on an installed SdvTestFramework.Cli
# package without a sibling frobby/sdv-test-framework checkout.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "[package-smoke] dotnet is required." >&2
  exit 127
fi

PACKAGE_VERSION="${FROBBY_PACKAGE_VERSION:-$(sed -n 's:.*<SdvTestFrameworkVersion>\(.*\)</SdvTestFrameworkVersion>.*:\1:p' Directory.Build.props)}"
PACKAGE_SOURCE="${FROBBY_PACKAGE_SOURCE:-"$REPO_ROOT/nupkg"}"
PACKAGE_PATH="$PACKAGE_SOURCE/SdvTestFramework.Cli.$PACKAGE_VERSION.nupkg"
WORK_ROOT="${FROBBY_PACKAGE_SMOKE_ROOT:-$(mktemp -d "${TMPDIR:-/tmp}/frobby-package-smoke.XXXXXX")}"
KEEP_WORK_ROOT="${FROBBY_PACKAGE_SMOKE_KEEP:-0}"

cleanup() {
  if [ "$KEEP_WORK_ROOT" != "1" ] && [ -d "$WORK_ROOT" ]; then
    rm -rf "$WORK_ROOT"
  fi
}
trap cleanup EXIT

if [ "${FROBBY_PACKAGE_SMOKE_SKIP_PACK:-0}" != "1" ]; then
  "$REPO_ROOT/scripts/pack.sh"
fi

if [ ! -f "$PACKAGE_PATH" ]; then
  echo "[package-smoke] CLI package not found: $PACKAGE_PATH" >&2
  exit 2
fi

export DOTNET_CLI_HOME="$WORK_ROOT/dotnet-home"
export DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export NUGET_PACKAGES="$WORK_ROOT/nuget-packages"
mkdir -p "$DOTNET_CLI_HOME" "$NUGET_PACKAGES"

SMOKE_REPO="$WORK_ROOT/clean-mod-repo"
mkdir -p "$SMOKE_REPO"

echo "[package-smoke] work root: $WORK_ROOT"
echo "[package-smoke] package: $PACKAGE_PATH"

cd "$SMOKE_REPO"
dotnet new tool-manifest >/dev/null
cat > NuGet.config <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="frobby-local" value="$PACKAGE_SOURCE" />
  </packageSources>
</configuration>
EOF
dotnet tool install SdvTestFramework.Cli \
  --version "$PACKAGE_VERSION" \
  --no-http-cache >/dev/null
dotnet tool restore >/dev/null

dotnet tool run sdv-test -- repo init \
  --project-name "Package Smoke Mod" \
  --slug package-smoke \
  --version "$PACKAGE_VERSION" \
  --build-command dotnet \
  --build-arg build \
  --extra-mod bin/Release/net6.0

mkdir -p bin/Release/net6.0
cat > bin/Release/net6.0/manifest.json <<EOF
{
  "Name": "Package Smoke Mod",
  "Author": "Frobby",
  "Version": "$PACKAGE_VERSION",
  "Description": "Package install smoke placeholder mod.",
  "UniqueID": "Frobby.PackageSmoke",
  "EntryDll": "PackageSmoke.dll",
  "MinimumApiVersion": "4.0.0"
}
EOF

test -f sdv-test.config.json
test -x scripts/sdv-test
test -x scripts/sdv-repeat
test -x scripts/sdv-preflight
test -f tests/sdv/01-example-core-loads.test.json
test -f docs/FROBBY.md

if grep -R -E "Starberg|Stardew Valley Expanded|SVE" \
  sdv-test.config.json scripts tests/sdv docs/FROBBY.md >/dev/null; then
  echo "[package-smoke] generated scaffold contains project-specific text." >&2
  exit 3
fi

dotnet tool run sdv-test -- list tests/sdv
./scripts/sdv-preflight tests/sdv/01-example-core-loads.test.json
./scripts/sdv-test --dry-run tests/sdv/01-example-core-loads.test.json
./scripts/sdv-repeat --dry-run --count 2 tests/sdv/01-example-core-loads.test.json

echo "PASS package install smoke"
