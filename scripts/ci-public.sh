#!/usr/bin/env bash
# Hosted-safe validation for public CI runners.
#
# This intentionally avoids the game-backed Harness and Runner package path.
# Those projects compile through Pathoschild.Stardew.ModBuildConfig and require
# real Stardew Valley / SMAPI assemblies from a local game install.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

TEST_PROJECTS=(
  "tests/Repository.Tests/Repository.Tests.csproj"
  "tests/Protocol.Tests/Protocol.Tests.csproj"
  "tests/Runner.Mcp.Tests/Runner.Mcp.Tests.csproj"
  "tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj"
)

for project in "${TEST_PROJECTS[@]}"; do
  echo "==> Public CI test: $project"
  dotnet test "$project" --configuration Release --logger "console;verbosity=normal"
done

echo "PASS public CI"
