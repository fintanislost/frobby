# D1.7 — Sample Suite + DSL Extensions Design

**Milestone:** M1 D1.7 (M1's ship criterion per spec §7)
**Date:** 2026-04-23
**Author:** fintan + Claude (brainstorming session)
**Status:** Approved — ready for implementation-plan drafting

## Goal

Author 10 reproducibly-passing end-to-end scenarios against a bundled sample Content Patcher mod, landing the DSL/RPC extensions needed first (`!=`, array indexing, `draw.assert_not_contains`, assertion counter wiring, and a relaxed `RequireWorldReady` predicate to unblock save-dependent scenarios under headless Xvfb). Satisfies spec §7 Phase 1 success criterion: "author 10 sample scenarios covering one real mod, all pass reproducibly."

## Architecture

Three coupled parts. Part A lands first (DSL/RPC extensions are the green floor for authoring scenarios against). Part B adds the bundled sample CP mod + scenario files. Part C is the end-to-end smoke.

**Part A — DSL/RPC extensions** (5 code changes):
- Relax `RpcPreconditions.RequireWorldReady` from `Context.IsWorldReady` to `Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame`. Fixes the D1.5/D1.6 smoke limitation where `Context.IsWorldReady` stays false under headless Xvfb even after `Game1.gameMode` transitions to `playingGameMode`.
- Add `!=` operator to the `ScenarioRunner` state-assertion DSL alongside existing `==`.
- Add array indexing (`state.player.items[0].id`) to DSL path resolution.
- Create `DrawAssertNotContainsHandler` RPC handler + symmetric response DTO.
- Wire `ScenarioRunner` to increment `ScenarioState.AssertionsRun` / `AssertionsPassed` per-assertion so `scenario.end` reports truthful counts.

**Part B — Sample content** (new repo directories):
- `tests/sample-cp-mod/` — minimal Content Patcher mod: `manifest.json` + `content.json` + 1–2 PNG assets. One `Load` patch (new asset `Mods/SdvTestSample/TestMarker`) + one `EditImage` patch (on a visible vanilla tile in `LooseSprites/Cursors`).
- `tests/samples/` — 10 `*.test.json` scenario files covering 4 categories (see §The 10 scenarios below).
- `scripts/run-samples.sh` — wrapper that stages both the sample mod and the harness into the test mods dir, then invokes `sdv-test run tests/samples/`.

**Part C — End-to-end smoke:**
- Single final run: `./scripts/run-samples.sh` → 10/10 pass. Blocks the milestone closure if anything fails.

## The 10 scenarios

All named `<nn>-<area>-<behavior>.test.json` so alpha-sorted runs are predictable. Fixture `m0spike_436515781` unless noted.

**State-only (2):**

1. `01-state-time-after-load.test.json` — exercises the IsWorldReady fix (Part A task 1). Fixture load → `state.time` → assert `in_save == true`, `season == "spring"`, `year >= 1`.
2. `02-state-player-inventory-index.test.json` — exercises DSL additions together. Fixture load → `state.player` → assert `items[0].id != null`. One assertion validates both array indexing and `!=`.

**Draw assertions — positive (2):**

3. `03-draw-contains-sample-marker.test.json` — the sample mod's `Load` asset must render when its holder renders. Fixture load → `draw.arm(60)` → advance time → `draw.assert_contains {texture_asset: "Mods/SdvTestSample/TestMarker", min_count: 1}`. The sample mod's `EditImage` patch references `Mods/SdvTestSample/TestMarker` inside `LooseSprites/Cursors` (via `FromFile` compositing) — so the marker pixels render anywhere `LooseSprites/Cursors` renders. Plan specifies the exact patch parameters.
4. `04-draw-contains-patched-cursor.test.json` — the sample mod's `EditImage` patch is observable via Tier 1 resolution. Fixture load → `draw.arm(60)` → `draw.assert_contains {texture_asset: "LooseSprites/Cursors", source_rect: {<patched-tile>}, color_exact: <patched-color>, min_count: 1}`.

**Draw assertions — negative (2):**

5. `05-draw-not-contains-unused-asset.test.json` — `draw.assert_not_contains` succeeds when nothing matches. Fixture load → arm → `draw.assert_not_contains {texture_asset: "Mods/SdvTestSample/NonExistentAsset"}`.
6. `06-draw-not-contains-after-warp.test.json` — location-specific assets don't render outside their location. Fixture load → `player.warp("Farm", 10, 10)` → arm → `draw.assert_not_contains {texture_asset: "Maps/TownInterior"}`.

**Manipulators (2):**

7. `07-player-warp-updates-location.test.json` — fixture load → `player.warp("Farm", 10, 10)` → `state.location` → assert `name == "Farm"`.
8. `08-player-set-money-roundtrip.test.json` — fixture load → `player.set_money(5000)` → `state.player` → assert `money == 5000`.

**Determinism — closes D1.6 end-to-end proof (2):**

9. `09-freeze-tick-stable.test.json` — `Game1.Update` short-circuit works. Fixture load → `freeze.begin` → `freeze.status` (capture tick T1) → `time.advance(1000)` → `freeze.status` (capture tick T2) → assert `T1 == T2`. This also proves `time.advance` itself is frozen (no ticks advance under freeze — the time.advance RPC becomes a no-op, which is the intended FREEZE semantic).
10. `10-freeze-parallax-regression.test.json` — M0's residual is closed. Fixture load → `player.warp("Beach", 20, 30)` (Beach uses `Game1.background` parallax) → `freeze.begin` → `draw.arm(60)` → snapshot (capture event hash H1) → wait 2s → snapshot (capture H2) → assert `H1 == H2` (modulo tick/call_index meta). The warp is required because `m0spike` fixture starts in the farmhouse (interior, no parallax); Beach is known from M0 spike to exercise the parallax scroll path.

Scenarios 9–10 may compose primitives rather than requiring new RPCs — plan decides final shape. "Capture tick" in 9 is a scenario-level value-capture capability that may or may not exist in the DSL today; plan fleshes out.

## Acceptance criteria

1. `./scripts/ci.sh` green after each code task + after final smoke.
2. `tests/sample-cp-mod/` is valid and loads under SMAPI (no "invalid manifest" / "invalid content.json" errors at launch).
3. `tests/samples/*.test.json` — 10 files, all validate against `schemas/scenario.schema.json`.
4. **End-to-end smoke: `./scripts/run-samples.sh` → 10/10 PASS.** Ship criterion for M1.
5. `scenario.end` response reports accurate `assertions_run` / `assertions_passed` (not the current always-0).
6. `docs/rpc-schema.md` documents `draw.assert_not_contains` with request/response shape and error codes.
7. `docs/milestones/current.md` marks D1.7 `[x]` with a completion subsection including the smoke result; M1 marked shippable overall.

## Error handling

- **`RequireWorldReady` relaxation:** if neither `gameMode == playingGameMode` nor `hasLoadedGame` is true, still throw `GameStateInvalid (-32003)` with message "no active save — mutation requires a loaded world" (unchanged from S3). The predicate widens but the contract stays: mutators only fire during playable gameplay.
- **DSL errors:** `!=` parse failure or bad array index surface the offending expression verbatim in the `ScenarioReport.Failures` list. Matches existing `DslParseException`-equivalent pattern from D1.4.
- **`draw.assert_not_contains` match-found:** returns `{ok: false, found: N, sample: <event_dto>}` mirroring `draw.assert_contains`'s shape. `ScenarioRunner` treats `ok: false` as an assertion failure.
- **Scenario suite failures:** if any of the 10 scenarios fails during the final smoke, the milestone is not complete — root-cause and fix, don't skip-mark. Unit-test gaps during development are expected; end-to-end failures are ship-blockers.

## Testing

**New unit tests (~10):**
- DSL `!=`: 2 tests (equal-to rejected, not-equal accepted)
- Array indexing: 2 tests (valid index returns element, out-of-range → false/error)
- `DrawAssertNotContainsHandler`: 3 tests (no-match ok, match-found returns ok:false, bad filter → `InvalidParams`)
- Counter wiring: 2 tests (`AssertionsRun` increments, `AssertionsPassed` matches passed count)
- `RequireWorldReady` relaxation: 1 test (title-screen rejection still works via new predicate)

**Skip-marked integration (3):**
- `SampleCpMod_Loads_UnderSmapi`
- `SampleSuite_AllTenScenariosPass`
- `FreezeParallaxRegression_HashesMatch`

**Target test count after D1.7:** 193 + 21 → ~203 Passed + ~24 Skipped.

## Out of scope for D1.7 (Phase 2+ work)

- Watch mode (spec §4.7)
- Record mode (spec §4.7)
- Bitmap fallback / SSIM diffing (spec §4.5)
- Fixture builder tool (spec §4.8)
- TAP / JUnit reporters (spec §4.7)
- DSL operators beyond `!=`: `<`, `>`, `in`, regex match — defer to M2
- Content Patcher mod testing helpers (`cp.assert_patched`, `cp.reload`, `cp.list_active_patches` per spec §5) — defer to M2
- Real third-party CP mod validation (scope-limited to bundled sample mod)
- Multiplayer scenarios (spec §9 defers this indefinitely)

## Links

- Spec: `docs/spec.md` §7 "Phase 1 — Core framework"
- Rule: `.claude/rules/draw-call-recorder.md`, `.claude/rules/fixtures.md`
- D1.5 / D1.6 completion: `docs/milestones/current.md`
- Current scenario format: `schemas/scenario.schema.json`
- Current ScenarioRunner: `src/Runner/Scenarios/ScenarioRunner.cs`
