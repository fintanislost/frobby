#!/usr/bin/env bash
# Mirrors CI exactly. Per CLAUDE.md §Running & testing.
#
# What this does:
#   1. `dotnet restore` the solution
#   2. `dotnet build` the solution with warnings-as-errors
#   3. `dotnet test` the solution (runs every test project listed in the .slnx)
#
# Integration tests (which need a live SDV) are NOT run here — they live in
# scripts/run-integration-tests.sh so they can be opted out of on CI runners
# without SDV installed.
#
# Adding a new project: `dotnet sln sdv-test-framework.slnx add <path-to-csproj>`
# — then it's picked up automatically by this script.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

SLN=sdv-test-framework.slnx

echo "==> dotnet --version"
dotnet --version

echo "==> Restore $SLN"
dotnet restore "$SLN"

echo "==> Build $SLN (warnings-as-errors via Directory.Build.props)"
dotnet build "$SLN" --no-restore --configuration Release

echo "==> Unit tests"
dotnet test "$SLN" --no-build --configuration Release --logger "console;verbosity=normal"

echo "==> ci.sh PASSED"
