# Release Dry-Run Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a NuGet release dry-run path that validates package artifacts and CI upload behavior without publishing.

**Architecture:** Keep publishing out of scope. A shell script validates the same local package/install flow used by developers and emits a manifest; a GitHub Actions workflow runs the script and uploads artifacts.

**Tech Stack:** Bash, GitHub Actions YAML, .NET pack/tool install, xUnit file-contract tests.

---

### Task 1: Script and Workflow Contract Tests

**Files:**
- Create: `tests/Runner.Tests/ReleaseDryRunScriptTests.cs`

- [ ] Add xUnit tests that assert `scripts/release-dry-run.sh` exists, is executable, invokes `scripts/package-install-smoke.sh`, validates all three package IDs, writes `nupkg/release-dry-run.json`, and never invokes `dotnet nuget push`.
- [ ] Add workflow contract assertions for `.github/workflows/release-dry-run.yml`: `workflow_dispatch`, `branches: [ main ]`, `tags: [ 'v*' ]`, `actions/setup-dotnet@v5`, `actions/upload-artifact@v4`, and no NuGet push/API key.
- [ ] Run `dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter ReleaseDryRunScriptTests` and confirm the tests fail because files are missing.

### Task 2: Release Dry-Run Script

**Files:**
- Create: `scripts/release-dry-run.sh`

- [ ] Implement a POSIX-safe Bash script with `set -euo pipefail`.
- [ ] Resolve repo root, package version from `Directory.Build.props`, and expected package names:
  - `SdvTestFramework.Protocol`
  - `SdvTestFramework.Runner.Dsl`
  - `SdvTestFramework.Cli`
- [ ] Run `scripts/package-install-smoke.sh`.
- [ ] Validate each expected `.nupkg` exists in `nupkg/`.
- [ ] Write `nupkg/release-dry-run.json` with version, UTC timestamp, and package paths.
- [ ] Run the targeted tests and fix only the script contract failures.

### Task 3: GitHub Actions Dry-Run Workflow

**Files:**
- Create: `.github/workflows/release-dry-run.yml`

- [ ] Add workflow triggers for manual dispatch, `main`, and `v*` tags.
- [ ] Use `actions/checkout@v6` and `actions/setup-dotnet@v5`.
- [ ] Run `dotnet restore`, `dotnet build --configuration Release --no-restore`, `dotnet test --configuration Release --no-build --logger "console;verbosity=normal"`, and `./scripts/release-dry-run.sh`.
- [ ] Upload `nupkg/*.nupkg` and `nupkg/release-dry-run.json` with `actions/upload-artifact@v4`.
- [ ] Run targeted tests again.

### Task 4: Docs and Roadmap

**Files:**
- Modify: `README.md`
- Modify: `docs/developer-setup.md`
- Modify: `docs/wiki/index.md`
- Modify: `docs/roadmap.md`

- [ ] Document the dry-run command and what it validates.
- [ ] Clarify that real NuGet publishing is still pending and requires a later guarded publish workflow.
- [ ] Keep docs neutral and user-facing; do not mention Starberg or SVE.

### Task 5: Verification and Commit

- [ ] Run `dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter ReleaseDryRunScriptTests`.
- [ ] Run `./scripts/release-dry-run.sh`.
- [ ] Run `dotnet test sdv-test-framework.slnx --no-restore --nologo`.
- [ ] Check `git status --short --branch`.
- [ ] Commit the feature branch with a concise message.
