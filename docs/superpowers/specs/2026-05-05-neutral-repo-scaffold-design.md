# Neutral Repo Scaffold - Design

**Date:** 2026-05-05
**Author:** fintan + Codex
**Status:** Draft for user review before implementation planning

## Goal

Make the Starberg repo-local Frobby convention portable to other Stardew Valley mod
repos without copying Starberg-specific names, paths, report directories, or assumptions.
The first acceptance target is the core Stardew Valley Expanded checkout at
`/home/fintan/stardewRepos/StardewValleyExpanded`.

The scaffold should let a mod developer add a small, consistent test surface to a repo:

```text
sdv-test.config.json
scripts/sdv-test
scripts/sdv-repeat
tests/sdv/
tests/sdv/fragments/
tests/sdv/baselines/
tests/scripts/sdv-test-dry-run.sh
tests/scripts/sdv-repeat-dry-run.sh
docs/FROBBY.md
```

Starberg remains a proven consumer, but Frobby owns the convention.

## Non-Goals

- Do not deeply test SVE gameplay in this first slice.
- Do not migrate Starberg to the new scaffold until SVE proves the neutral flow.
- Do not download Content Patcher, Farm Type Manager, or other third-party dependencies.
  The scaffold stages paths the user already has.
- Do not make optional SVE farm packs part of the first acceptance target. Grandpa's
  Farm, Frontier Farm, Immersive Farm 2, and Grampleton Fields are later mod-set tests.

## Why SVE Core

SVE Core is a stronger neutrality test than Starberg because it has:

- A C# SMAPI mod at `Stardew Valley Expanded/StardewValleyExpanded`.
- A Content Patcher pack at `Stardew Valley Expanded/[CP] Stardew Valley Expanded`.
- A Farm Type Manager pack at `Stardew Valley Expanded/[FTM] Stardew Valley Expanded`.
- Paths with spaces and brackets.
- Required external framework mods, especially Content Patcher and Farm Type Manager.

That combination forces the scaffold to handle multi-folder staging, path quoting, and
dependency paths without using Starberg-only assumptions.

## User-Facing Shape

From a mod repo, the developer runs:

```bash
./scripts/sdv-test
./scripts/sdv-test --visible
./scripts/sdv-test --no-build tests/sdv/01-sve-core-loads.test.json
./scripts/sdv-test --dry-run
./scripts/sdv-repeat --count 3 tests/sdv/01-sve-core-loads.test.json
```

The wrapper defaults to headless mode. Visible mode is an explicit debugging choice.
Reports go to `/tmp/<project-slug>-frobby-results-<version>/` by default, with repeat
runs under `/tmp/<project-slug>-frobby-repeat-<version>/run-NN/`.

## Config File

Each repo gets a tracked `sdv-test.config.json`. Frobby parses this file through a new
repo command instead of making bash parse JSON.

```json
{
  "project": {
    "name": "Stardew Valley Expanded",
    "slug": "stardew-valley-expanded",
    "version": "0.1.0"
  },
  "frobbyRoot": "../frobby/sdv-test-framework",
  "build": {
    "command": "dotnet",
    "args": [
      "build",
      "Stardew Valley Expanded/StardewValleyExpanded.sln",
      "--configuration",
      "Release"
    ]
  },
  "defaultTarget": "tests/sdv",
  "baselineTarget": "tests/sdv/01-sve-core-loads.test.json",
  "modSets": [
    {
      "name": "core",
      "extraMods": [
        "${SDV_GAME_MODS}/ContentPatcher",
        "${SDV_GAME_MODS}/FarmTypeManager",
        "Stardew Valley Expanded/StardewValleyExpanded/bin/Release/net6.0",
        "Stardew Valley Expanded/[CP] Stardew Valley Expanded",
        "Stardew Valley Expanded/[FTM] Stardew Valley Expanded"
      ]
    }
  ]
}
```

Path handling rules:

- Relative paths resolve from the repo root.
- Absolute paths are used as-is.
- `~`, `$VAR`, and `${VAR}` expand in paths.
- Missing environment variables or missing paths fail before launching Stardew.
- Paths are passed to Frobby as repeated `--extra-mod` arguments, never shell-concatenated.

`SDV_GAME_MODS` defaults to the discovered Stardew `Mods` directory when the user does
not set it. Users can override it for custom SMAPI installs.

## Frobby CLI Additions

Add a new top-level command family:

```bash
sdv-test repo init [repo-path] [options]
sdv-test repo run [--repo-root <path>] [options] [scenario-or-directory ...]
sdv-test repo repeat [--repo-root <path>] [options] [scenario-or-directory ...]
```

`repo init` writes the neutral scaffold files. It accepts explicit `--project-name`,
`--slug`, `--version`, repeated `--build-arg`, repeated `--extra-mod`, optional
`--baseline-target`, and `--force`. The build command is captured as an argument array
so paths with spaces never require shell parsing.

`repo run` reads `sdv-test.config.json`, optionally runs the build command, resolves the
selected mod set, and delegates to Frobby's existing `run` or `run-suite` command:

- Single `.test.json` target uses `run`.
- Directory or multiple targets use `run-suite --fresh-process-per-scenario`.
- `--headless` is default.
- `--visible` suppresses `--headless`.
- `--no-build` skips the configured build command.
- `--report-dir` overrides the generated report root.
- `--mod-set <name>` selects a configured mod set. Default: the first mod set.
- `--baseline` runs the configured baseline target with `--update-baselines`.
- `--dry-run` prints the resolved build command, Frobby command, staged extra mods,
  and report hub without launching Stardew.

`repo repeat` mirrors Starberg's repeat wrapper behavior: it calls `repo run` multiple
times, writes each iteration under `run-NN`, builds only on the first run unless
`--no-build` is supplied, and exits non-zero if any iteration fails.

The generated `scripts/sdv-test` and `scripts/sdv-repeat` stay tiny. They locate the
repo root and invoke either the source-tree Frobby runner through `FROBBY_ROOT` or the
installed `sdv-test` tool if no source tree is available.

## Neutral Smoke Capability

To make SVE Core verification meaningful without asserting SVE-specific gameplay yet,
add a neutral Frobby state query:

```json
{ "action": "state.mods", "args": {} }
```

The matching state assertion surface must expose loaded SMAPI mod metadata:

- `unique_id`
- `name`
- `version`
- `is_content_pack`
- `content_pack_for`

The first SVE scenario can then assert that these are loaded:

- `Pathoschild.ContentPatcher`
- `Esca.FarmTypeManager`
- `FlashShifter.SVECode`
- `FlashShifter.StardewValleyExpandedCP`
- `FlashShifter.SVE-FTM`

This is a generic capability useful for any mod with framework dependencies.

## SVE Core Scaffold

The SVE checkout gets a local scaffold generated from Frobby, not a handcrafted copy
of Starberg's scripts.

Initial files:

```text
sdv-test.config.json
scripts/sdv-test
scripts/sdv-repeat
tests/sdv/01-sve-core-loads.test.json
docs/FROBBY.md
tests/scripts/sdv-test-dry-run.sh
tests/scripts/sdv-repeat-dry-run.sh
```

The first scenario should:

1. Load the standard Frobby fixture.
2. Wait for the world to settle.
3. Query loaded mods through the new neutral mod-state surface.
4. Assert SVE Core code, CP, FTM, and required framework dependencies are loaded.
5. Freeze and capture a final frame for report sanity.

It should not warp into SVE custom locations yet. That belongs to the later SVE gameplay
coverage pass after the scaffold is proven.

## Starberg Impact

No Starberg migration is required in this design. Starberg can keep its current scripts
until the new repo scaffold passes against SVE Core. After that, Starberg can move to
`sdv-test.config.json` and the generated scripts in a separate cleanup.

The existing Starberg runbook remains a useful comparison for behavior parity:

- default headless execution
- stable `/tmp` report hub
- `--visible`, `--no-build`, `--baseline`, `--dry-run`
- repeat runner for flake checks
- tracked scenario files, fragments, and baselines

## Testing Strategy

Frobby unit tests:

- Config parsing handles relative paths, absolute paths, `~`, `$VAR`, and `${VAR}`.
- Missing env vars and missing paths fail before launch with actionable messages.
- Repo command assembly uses repeated `--extra-mod` arguments for each configured path.
- Single scenario selects `run`; directories and multiple targets select `run-suite`.
- `--visible`, `--no-build`, `--report-dir`, `--mod-set`, `--baseline`, and `--dry-run`
  behave as documented.
- Generated scripts are mod-neutral and contain no Starberg or SVE-specific text except
  values supplied by the scaffold config.

SVE local verification:

- `./scripts/sdv-test --dry-run` prints a correctly quoted build command and Frobby
  command with all Core mod paths.
- `./scripts/sdv-repeat --dry-run --count 2 tests/sdv/01-sve-core-loads.test.json`
  prints two iteration report directories and only one build by default.
- Live headless run of `tests/sdv/01-sve-core-loads.test.json` passes after config
  path validation confirms the required framework dependency paths exist.

Regression verification:

- Full Frobby unit suite passes.
- Current Starberg scripts continue to work because this change adds a new path rather
  than changing their behavior.

## Acceptance Criteria

- Frobby owns a documented, neutral repo scaffold.
- SVE Core has a generated local scaffold that stages code, CP, FTM, Content Patcher,
  and Farm Type Manager through repeated `--extra-mod` paths.
- SVE Core dry-run proves path quoting for spaces and brackets.
- SVE Core live smoke proves the staged mods are loaded through neutral `state.mods`.
- No generated Frobby scaffold template contains Starberg-specific names.
- Starberg remains unchanged during this first slice.
