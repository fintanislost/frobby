# Developer Setup

Tested on Arch Linux (primary) and Windows 11 (secondary). macOS unverified.

## Prerequisites

- .NET 8 SDK
- Git with LFS (`git-lfs install`)
- A legitimate Stardew Valley install (Steam or GOG)
- SMAPI 4.1+ installed and working
- Node.js 20+ (for docs site and the MCP server wrapper, M3)

### Arch Linux specifics

```bash
sudo pacman -S dotnet-sdk git-lfs xorg-server-xvfb mesa
# Steam install of SDV is fine; SMAPI installs as usual
```

For headless testing:
```bash
sudo pacman -S xorg-server-xvfb
```

Frobby's CLI can launch Stardew through `xvfb-run` so test runs do not use the
active desktop display or mouse cursor:

```bash
dotnet run --project src/Runner -- run --headless tests/samples
dotnet run --project src/Runner -- run-suite --headless tests/samples
```

Set `SDV_TEST_HEADLESS=1` to make all `SdvLauncher` callers use the same Xvfb
wrapper without adding the CLI flag.

### Windows specifics

```powershell
winget install Microsoft.DotNet.SDK.8
winget install Git.Git
# Install SDV via Steam, then SMAPI via its installer
```

## First-time setup

```bash
git clone <repo-url>
cd <repo>
git lfs pull
dotnet restore
dotnet build
./scripts/doctor.sh  # verifies SDV, SMAPI, fixtures, .NET all work
```

## Environment variables

- `SDV_INSTALL_PATH` — path to Stardew Valley install root (required if not auto-detected)
- `SMAPI_PATH` — path to SMAPI binary (required if not in PATH)
- `SDV_TEST_SOCKET` — set automatically by runner; don't set manually
- `SDV_TEST_LOG_LEVEL` — `trace|debug|info|warn|error`, default `info`

Put persistent values in `.env` (gitignored).

## Running tests

Unit tests (fast, no SDV launch):
```bash
dotnet test tests/Runner.Tests/
```

Integration tests (launches SDV):
```bash
./scripts/run-integration-tests.sh
```

Full CI-equivalent run:
```bash
./scripts/ci.sh
```

Public hosted CI subset:
```bash
./scripts/ci-public.sh
```

`scripts/ci-public.sh` is the path used by public hosted GitHub runners. It runs
repo-neutral tests and avoids the game-backed Harness/package build because
`Pathoschild.Stardew.ModBuildConfig` needs a real Stardew Valley + SMAPI install
and public hosted runners do not include Stardew Valley. Use `scripts/ci.sh` or
the release commands below on a local workstation or self-hosted runner with
`FROBBY_GAME_PATH` set when autodetect cannot find the real Stardew install.

Package/install smoke before release or local mod testing:
```bash
./scripts/package-install-smoke.sh
```

This packs Frobby locally, installs `SdvTestFramework.Cli` into a clean temporary
mod repo as a local dotnet tool, scaffolds repo scripts, and verifies list,
preflight, repo-run dry-run, and repeat dry-run paths without launching SDV.

Release dry-run before tagging or opening a release PR:
```bash
./scripts/release-dry-run.sh
```

The release dry-run wraps the package/install smoke, verifies all expected
`.nupkg` files for `SdvTestFrameworkVersion`, and writes
`nupkg/release-dry-run.json`. The matching GitHub Actions workflow uploads those
packages and the manifest as artifacts, but does not publish to NuGet. This is a
game-backed path, so GitHub release workflows must run where a real Stardew
install is available through ModBuildConfig autodetect or repository variable
`FROBBY_GAME_PATH`.

Guarded NuGet publish:

1. Add `NUGET_API_KEY` as a repository secret.
2. Configure `FROBBY_RELEASE_RUNNER` as the label for a game-backed self-hosted
   runner when release jobs should not use `ubuntu-latest`.
3. Configure `FROBBY_GAME_PATH` as a repository variable on that runner if
   ModBuildConfig autodetect is not available.
4. Run the publish workflow manually if you only want another game-backed dry-run.
5. Push a `v*` tag when you intend to publish.

The publish workflow runs restore, build, tests, and `scripts/release-dry-run.sh`
before `dotnet nuget push --skip-duplicate`. The push step is gated to tag-push
events, so manual dispatch does not publish packages.

## Debugging the harness mod

The harness mod is a normal SMAPI mod. Debug it like any other:
1. Open `src/Harness/` in your IDE
2. Attach debugger to the `StardewModdingAPI` process after launch
3. Set breakpoints in harness code

The RPC loop runs on a background thread; game-thread work runs on `UpdateTicked`. Both are debuggable, but watch for thread IDs in the debugger to orient yourself.

## Common issues

**"SMAPI not found"** — set `SMAPI_PATH` or install to a standard location.

**"Fixture load failed: save corrupt"** — SDV version mismatch. Check `tests/fixtures/<name>.meta.json` against installed SDV.

**"Harmony patch failed to apply"** — SDV version changed. Run `./scripts/doctor.sh` which will list which patches resolved and which didn't.

**Xvfb tests hang** — use `sdv-test run --headless` or set `SDV_TEST_HEADLESS=1`
so the runner starts Stardew through `xvfb-run` before SDV launches.
