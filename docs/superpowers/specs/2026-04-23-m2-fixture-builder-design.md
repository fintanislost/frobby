# M2 — Fixture Builder Tool Design

**Milestone:** M2 subproject 1 (per spec §7 Phase 2 decomposition)
**Date:** 2026-04-23
**Author:** fintan + Claude (brainstorming session)
**Status:** Approved — ready for implementation-plan drafting

## Goal

Ship `sdv-test fixture create <name> --from <script.fixture.json>` — a scripted fixture builder that starts from an existing base fixture, applies a list of RPC steps, saves the resulting game state, and lays down a reproducible `tests/fixtures/<name>/` directory with the save + script + auto-generated metadata + README. Unblocks scenario authors from the "every scenario uses `m0spike_436515781`" bottleneck and makes fixture creation a version-controllable artifact per `.claude/rules/fixtures.md` Path B.

## Architecture

Three parts:
1. **Runner — new `fixture` subcommand** that orchestrates the build, reusing the existing scenario-runner step-dispatch pattern.
2. **Harness — new `fixture.save` + `state.mods` RPCs** that trigger `SaveGame.Save()` on the game thread and expose the loaded-mods list for metadata.
3. **Staging layer** that bridges the repo's `tests/fixtures/` directory to SDV's save directory (`Constants.SavesPath`) at both fixture-build time and scenario-run time.

Every fixture builds from an existing base fixture — no new-game / character-creation path in this subproject. The m0spike fixture serves as the initial base; derived fixtures can compose further. The runner owns all filesystem bridging between the repo and SDV's save directory; the harness stays ignorant of `tests/fixtures/`.

## Components

**New files (Runner):**
- `src/Runner/Commands/FixtureCommand.cs` — routes `fixture create` and `fixture list` subcommands.
- `src/Runner/Fixtures/FixtureSpec.cs` — DTO for the `.fixture.json` script (mirrors `ScenarioSpec` minus assertions, plus required `base` field).
- `src/Runner/Fixtures/FixtureLoader.cs` — loads + validates `.fixture.json` against `schemas/fixture.schema.json`.
- `src/Runner/Fixtures/FixtureBuilder.cs` — orchestrates: stage base → load → steps → save → copy → metadata.
- `src/Runner/Fixtures/FixtureStager.cs` — bidirectional copy between `tests/fixtures/<name>/save/` and SDV's `Constants.SavesPath`.
- `src/Runner/Fixtures/FixtureMetadata.cs` — DTO for `<name>.meta.json`, plus a `Generate(FixtureSpec, mods, sdvVersion, smapiVersion, farmer)` builder.
- `src/Runner/Fixtures/FixtureReadme.cs` — generates `<name>.README.md` from metadata + spec.

**New files (Harness):**
- `src/Harness/Handlers/FixtureSaveHandler.cs` — RPC handler for `fixture.save`. Mirrors FreezeBeginHandler's precondition pattern.
- `src/Harness/Handlers/StateModsHandler.cs` — RPC handler for `state.mods`. Returns the list of loaded mod UniqueIDs.

**New files (Protocol):**
- `src/Protocol/Models/FixtureSaveRequest.cs` — `{name: string}`.
- `src/Protocol/Models/FixtureSaveResult.cs` — `{ok, tick, save_path}`.
- `src/Protocol/Models/ModsState.cs` — `{mods: string[]}`.

**New files (schemas / docs):**
- `schemas/fixture.schema.json` — JSON Schema for `.fixture.json`.

**New files (tests):**
- `tests/Runner.Tests/FixtureCommandTests.cs` — arg parsing.
- `tests/Runner.Tests/FixtureStagerTests.cs` — shim-based copy tests.
- `tests/Runner.Tests/FixtureLoaderTests.cs` — schema validation.
- `tests/Runner.Tests/FixtureMetadataTests.cs` — given mock inputs, serializes to expected shape.
- `tests/Harness.Tests/StateModsHandlerTests.cs` — mod list round-trip via `IModRegistry` shim.
- `tests/Harness.Tests/FixtureSaveHandlerTests.cs` — precondition enforcement (skip-marked for the `SaveGame.Save()` integration path).
- `tests/Harness.Tests/FixtureBuilderIntegrationTests.cs` — 3 skip-marked integration tests.

**Modified files:**
- `src/Runner/Program.cs` — route `fixture` to the new command.
- `src/Runner/Commands/RunCommand.cs` — call `FixtureStager.Stage` for every unique `fixture` name in the scenario set before launching SDV.
- `src/Harness/ModEntry.cs` — register the two new handlers.
- `docs/rpc-schema.md` — document `fixture.save` and `state.mods`.
- `docs/milestones/current.md` — M2 subproject-tracker section + completion note on land.
- `tests/fixtures/m0spike_436515781/` — migrate the existing spike save into the repo so the sample suite keeps passing after the staging layer lands (saves currently live only in the user's `~/.config/StardewValley/Saves/`).

## CLI surface

### `sdv-test fixture create <name> --from <script-path> [--mods-path X] [--force]`

- `<name>` — positional, the new fixture's identifier (matches the directory name).
- `--from` — required, path to the `.fixture.json` script.
- `--mods-path` — optional, same semantics as `run` (isolated mods dir override).
- `--force` — optional, overwrite an existing `tests/fixtures/<name>/` directory.

Exit codes:
- 0 — fixture built successfully.
- 2 — script load / validation failure (script missing, schema invalid, missing base).
- 3 — destination collision without `--force`.
- 4 — runtime failure (SDV launch / RPC / save failed).

### `sdv-test fixture list`

Enumerates `tests/fixtures/*/` and prints one line per fixture: `<name> — <description from meta> (created <created_at>)`. Silently prints nothing if the directory is empty or missing.

## Fixture directory layout

`tests/fixtures/<name>/`:
- `save/` — the SDV save directory (files copied verbatim: `SaveGameInfo`, `<farmer-name>`, `<farmer-name>_SaveGameInfo`).
- `<name>.fixture.json` — the authoring script, copied from `--from` at build time so the directory is self-contained.
- `<name>.meta.json` — auto-generated per `.claude/rules/fixtures.md` schema.
- `<name>.README.md` — auto-generated human summary.

## Wire shapes

### `.fixture.json`

```json
{
  "name": "spring_day_5_500g",
  "base": "m0spike_436515781",
  "description": "Spring Day 5 with 500g, all NPCs at default friendship.",
  "steps": [
    { "action": "player.set_money", "args": { "amount": 500 } },
    { "action": "time.advance", "args": { "minutes": 120 } }
  ]
}
```

- `name` (string, required) — must match the containing directory name.
- `base` (string, required) — name of an existing fixture in `tests/fixtures/`.
- `description` (string, required) — human-readable one-liner, flows through to `.meta.json` + README.
- `steps` (array, required; may be empty) — RPC step objects exactly as in `.test.json` scenarios.

### `<name>.meta.json`

```json
{
  "name": "spring_day_5_500g",
  "description": "Spring Day 5 with 500g, all NPCs at default friendship.",
  "sdv_version": "1.6.15",
  "smapi_version": "4.5.2",
  "mods_installed": ["Pathoschild.ContentPatcher", "SdvTestFramework.Harness"],
  "created_at": "2026-04-23T15:30:00Z",
  "created_by": "fixture-builder",
  "base": "m0spike_436515781",
  "regenerate_with": "tests/fixtures/spring_day_5_500g/spring_day_5_500g.fixture.json",
  "farmer": { "name": "Tester", "gender": "female" }
}
```

All fields auto-populated: `sdv_version` via `Game1.version`, `smapi_version` via `Constants.ApiVersion`, `mods_installed` via new `state.mods` RPC, `created_at` via `DateTime.UtcNow.ToString("O")`, `farmer` via existing `state.player`.

### `fixture.save` RPC

**Params:** `{name: string}` — destination save-folder name. Typically matches the fixture name.

**Preconditions (strict):**
- `Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame` — same widened predicate M1 D1.7 T1 shipped for mutators.
- `!Game1.eventUp` — no cutscene.
- `Game1.currentMinigame == null` — no minigame.
- `!Game1.isWarping` — not mid-warp.

**Action:** sets `Game1.player.favoriteThing = "sdv-test-fixture"` (marker for framework-created saves; harmless flavor-text field), then drives SDV's save flow to completion. The exact mechanism is plan-level detail — `SaveGame.Save()` returns an `IEnumerator` that SDV drives across multiple update ticks, so the handler needs to either iterate it to completion on the game thread or yield the RPC response only once `SaveGame.IsProcessing == false`. Plan picks the implementation; hard cap at 30 seconds to catch runaway saves.

**Response:** `{ok: true, tick: T, save_path: "/abs/path/to/SDV/Saves/<name>"}`.

### `state.mods` RPC

**Params:** none.
**Response:** `{mods: ["UniqueID1", "UniqueID2", ...]}` — ordered by load order (as SMAPI's `IModRegistry.GetAll()` returns them).

## Staging logic

`FixtureStager.Stage(name, targetSavesDir)`:
1. Resolve source: `<repo>/tests/fixtures/<name>/save/`. Error if missing.
2. Resolve target: `<targetSavesDir>/<name>/`. Delete if exists.
3. Recursive copy source → target. Preserve file permissions (Linux only — Windows support is Phase 3+ per spec §6).

Called at two moments:
- **`sdv-test run`** — iterate unique `fixture` values across the scenario set; stage each before launching SDV.
- **`sdv-test fixture create`** — stage the `base` before launching SDV.

After `fixture create` completes successfully, the inverse copy happens: `<targetSavesDir>/<newName>/` → `<repo>/tests/fixtures/<newName>/save/`.

## Error handling

- **Script load failure** → exit 2 with the offending field quoted.
- **Missing `base` fixture** → exit 2 with `"base fixture '<X>' not found — did you forget to build it?"`.
- **Destination collision** → exit 3 unless `--force`. Keeps stale metadata from silently overwriting the user's regenerate-with source.
- **Precondition failures on `fixture.save`** → `GameStateInvalid -32003` with failing check named (mirrors FreezeBeginHandler). Build aborts; no partial fixture dir written.
- **`SaveGame.Save()` throws** → `InternalError -32603`. Partial save directory in SDV's saves dir is cleaned up by the runner on exit.
- **SDV subprocess crash** → existing `SdvLauncher` error path surfaces to the runner, which exits 4 and cleans partial fixture dir.
- **Copy failure (disk full, permission)** → exit 4 with the specific path.

Atomicity goal: a `fixture create` invocation either produces a complete `tests/fixtures/<name>/` (save + script + meta + README) or produces nothing. No partial directories land in the repo.

## Testing

**Unit tests (~10 new):**
- `FixtureStager.Stage`: shim source/target dirs, asserts recursive copy + overwrite.
- `FixtureLoader`: valid JSON, missing `base`, missing `name`, missing `description`, extra fields (strict).
- `FixtureMetadata.Generate`: shim inputs → expected JSON shape.
- `FixtureReadme.Generate`: shim inputs → markdown output contains the expected sections.
- `FixtureCommand` arg parsing: `--from`, `--mods-path`, `--force`, missing `--from`, unknown flags.
- `StateModsHandler`: `IModRegistry` shim returning 2 mods → response list matches.

**Skip-marked integration (3):**
- `FixtureCreate_EndToEnd_ProducesValidFixtureDirectory` — exercised by the smoke script.
- `DerivedFixture_LoadsInScenario_RunsToCompletion` — same.
- `FixtureList_EnumeratesCommittedFixtures` — can be unit-tested once the m0spike fixture is migrated into `tests/fixtures/`; covered there.

**Target test count after M2 fixture-builder:** 201+26 → ~211+29.

## Acceptance criteria

1. `./scripts/ci.sh` green with ~10 new unit tests.
2. `sdv-test fixture create <name> --from <script>` succeeds end-to-end against live SDV, producing a well-formed `tests/fixtures/<name>/` directory.
3. A scenario with `"fixture": "<name>"` where the fixture lives in `tests/fixtures/` runs successfully via `sdv-test run` — staging is transparent.
4. `sdv-test fixture list` enumerates everything in `tests/fixtures/` with name + description + created-at.
5. Existing 10 sample scenarios still pass — requires migrating the `m0spike_436515781` fixture from the user's saves dir into `tests/fixtures/m0spike_436515781/` (last task).
6. `docs/rpc-schema.md` documents `fixture.save` and `state.mods`.
7. `docs/milestones/current.md` gets an M2-fixture-builder completion subsection + updated M2 subproject tracker.

## Out of scope for this subproject

TODOs for future work — noted in the design so later contributors know what was explicitly deferred:

- **Interactive path** (`sdv-test fixture create --interactive`) — play by hand, framework captures on save. Deferred to pair with spec §4.7 "record mode" — both capture user actions and should share primitives.
- **New-game base** — building fixtures from character creation instead of from another fixture. Requires new RPCs driving intro menus + farm-type selection + farmer details. Deferred to M3; m0spike covers the Day-1 starting case for M2.
- **Git LFS** — `.claude/rules/fixtures.md` recommends it once the repo has >5 fixtures. Documented here but not configured. Add a one-liner to README once we cross that threshold.
- **`fixture delete` / `fixture validate`** — `rm -rf` handles deletion; schema validation happens at load time. Add if real use emerges.
- **Multi-base / fixture composition** — technically works trivially (a base is just another fixture), but not explicitly tested. Document as "works but unvalidated."
- **Windows support** — all filesystem paths assume Linux. Windows support is spec §6 / Phase 3 material.

## Links

- Spec: `docs/spec.md` §4.8 Fixture Management, §7 Phase 2
- Rule: `.claude/rules/fixtures.md`
- M1 ship note: `docs/milestones/current.md` §D1.7
- Existing runner commands: `src/Runner/Commands/`
- Existing scenario infra: `src/Runner/Scenarios/` (patterns reused by `FixtureBuilder`)
