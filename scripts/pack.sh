#!/usr/bin/env bash
# Build NuGet packages: SdvTestFramework.Protocol, .Runner.Dsl, .Cli.
#
# Output: ./nupkg/*.0.1.0.nupkg
# Used by the local-install smoke and (eventually) a CI publish step.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

OUT="$REPO_ROOT/nupkg"
mkdir -p "$OUT"
rm -f "$OUT"/*.nupkg

echo "==> Build solution (so embedded harness resources are fresh)"
build_args=(sdv-test-framework.slnx -c Release)
if [ -n "${FROBBY_GAME_PATH:-}" ]; then
  build_args+=("/p:GamePath=$FROBBY_GAME_PATH")
fi
dotnet build "${build_args[@]}"

echo "==> Pack Protocol"
dotnet pack src/Protocol/Protocol.csproj -c Release -o "$OUT" --no-build

echo "==> Pack Runner.Dsl"
dotnet pack src/Runner.Dsl/Runner.Dsl.csproj -c Release -o "$OUT" --no-build

echo "==> Pack Cli"
dotnet pack src/Runner/Runner.csproj -c Release -o "$OUT" --no-build

echo "==> Produced packages:"
ls "$OUT"

echo "==> pack.sh PASSED"
