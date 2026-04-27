# D1.6 — Determinism Controller (FREEZE/THAW) Design

**Milestone:** M1 D1.6
**Date:** 2026-04-23
**Author:** fintan + Claude (brainstorming session)
**Status:** Approved — ready for implementation-plan drafting

## Goal

Expose explicit `freeze.begin` / `freeze.end` / `freeze.status` RPCs that pin the game clock, per-location RNG, NPC movement, cursor, and ambient effects — producing bit-identical captures across runs and closing the parallax-background residual M0 left on the table.

## Architecture

A new `DeterminismController` static singleton (same pattern as `Recorder` and `TextureAssetRegistry.Shared`) owns the FREEZE state, a saved-state snapshot, and the ordered enter/exit orchestration. One Harmony prefix on `Game1.Update(GameTime)` returns `false` when frozen — which simultaneously freezes `currentGameTime`, animations, and (as a free side effect) the `Game1.background` parallax scroll that M0 flagged. Per-location RNG and NPC halt are done by reflection/iteration at `freeze.begin`, not via patches — they're state mutations that can be snapshotted and restored.

**Separation:** arm = "capture draws," freeze = "stop the world." They're orthogonal: you can arm without freezing (watch draws evolve live) or freeze without arming (take state snapshots deterministically). The current codebase conflates them — `CursorPatches` gates on `Recorder.IsArmed`, and `Recorder.ActivateArm` flips `eventUp`/`displayHUD` — this design untangles them.

## Components

**New (`src/Harness/Determinism/`):**
- `DeterminismController.cs` — static singleton. `Frozen` (bool), `EnterFreeze(int seed)`, `ExitFreeze()`, `Status()`. Holds `SavedState` (NPC snapshots, location-rng snapshots, eventUp/displayHUD booleans).
- `TimeFreezePatch.cs` — Harmony prefix on `Game1.Update(GameTime)` returning `false` when `DeterminismController.Frozen`.
- `LocationRngPinner.cs` — `PinAll(int seed)` / `RestoreAll(snapshots)`. Iterates `Game1.locations`; reflects into each `GameLocation.random` field where present; re-seeds with `new Random(seed ^ location.NameOrUniqueName.GetHashCode())`. Missing-field tolerated silently (some subclasses don't have one).
- `NpcFreeze.cs` — `HaltAll()` / `RestoreAll(snapshots)`. Iterates NPCs in every loaded location; snapshots `Position`, `Schedule`, `controller`; calls `Halt()`, nulls `controller`. Missing-field tolerated silently.

**New (`src/Harness/Handlers/`):**
- `FreezeBeginHandler.cs` — RPC method `freeze.begin`.
- `FreezeEndHandler.cs` — RPC method `freeze.end`.
- `FreezeStatusHandler.cs` — RPC method `freeze.status`.

**Modified:**
- `src/Harness/Patches/CursorPatches.cs` — gate flips from `Recorder.IsArmed` → `DeterminismController.Frozen`. Semantic: cursor freeze belongs to FREEZE, not capture.
- `src/Harness/Recording/Recorder.cs` — `ActivateArm` / `RestoreSavedState` stop flipping `eventUp`/`displayHUD`. Arm becomes purely "start draw capture." The `_ambientFlipped` sentinel and the title-screen vs in-world branch are deleted.
- `src/Harness/ModEntry.cs` — instantiate the controller, register three handlers, apply `TimeFreezePatch` to the Harmony instance.
- `src/Harness/Handlers/ScenarioEndHandler.cs` — at entry, check `DeterminismController.Frozen`; if true, call `ExitFreeze()` before the existing end logic (safety valve, mirrors S4's scenario-end-in-finally fix).

**Unchanged (deliberately):**
- `SeedPinner` — stays the way it is; it handles `Game1.random` globally, not per-location. FREEZE calls it on enter to re-pin before per-location pinning.

## RPC surface

### `freeze.begin`

**Params:** none. Inherits seed from `ScenarioState.Current.Seed`. If no scenario is active (scenario.begin hasn't run) → `GameStateInvalid (-32003)` — "freeze.begin requires an active scenario (call scenario.begin first)". (Same code as the other precondition failures; simpler contract than having callers distinguish param-shape errors from state errors.)

**Preconditions (strict, all must hold):**
- `Context.IsWorldReady` — not at title screen, save is loaded
- `!Game1.eventUp` — no cutscene active
- `Game1.currentMinigame == null` — no minigame active
- `!Game1.isWarping` — not mid-warp
- `!DeterminismController.Frozen` — not already frozen

Any violation → `GameStateInvalid (-32003)` with the failing check named: `"freeze.begin requires !Game1.eventUp (event active)"`, etc.

**Action (ordered):**
1. Snapshot `(eventUp, displayHUD)` pair.
2. Snapshot NPC states (per-NPC: `Position`, `Schedule`, `controller`).
3. Snapshot per-location RNG state (per-location: the current `Random` instance via reflection).
4. Set `Frozen = true` (flips the `TimeFreezePatch` gate).
5. Apply `Game1.eventUp = true`, `Game1.displayHUD = false`.
6. Pin per-location RNGs (`new Random(seed ^ hash)` for each).
7. Call `Halt()` on each NPC, null `controller`.

**Response:** `{"ok": true, "locations_pinned": N, "npcs_halted": M, "tick": T}` — metrics aid debugging (did pinning actually find locations? how many NPCs halted?).

### `freeze.end`

**Preconditions:** `DeterminismController.Frozen == true`. Else `GameStateInvalid` — `"freeze.end requires Frozen == true (no active freeze)"`.

**Action (reverse order):**
1. Restore per-location RNGs from snapshot.
2. Restore NPC `Position`/`Schedule`/`controller` from snapshot.
3. Restore `eventUp`/`displayHUD` from snapshot.
4. Set `Frozen = false`.

**Response:** `{"ok": true, "tick": T}`.

### `freeze.status`

**Preconditions:** none. Pure query.
**Response:** `{"frozen": bool, "tick": T}`.

## Lifecycle semantics

While `Frozen`, the `Game1.Update` prefix returns `false` — the original body is skipped. SMAPI's `SGame.Update` continues past the base call (it's a prefix, not a replacement), so `UpdateTicked` events still fire and the RPC drain keeps working. That means **multiple queries within a single FREEZE window see a consistent moment**: `Game1.currentGameTime` doesn't advance, `Game1.ticks` stays pinned, animations freeze on their current frame, `Game1.background` parallax doesn't drift.

Consequence: draws captured while frozen (if armed) all share a tick number. That's the correct "single moment" semantic — exactly what the spec §4.4 FREEZE phase calls for.

**Re-entrancy:** strict — `freeze.begin` while already frozen throws. Simple contract; scenarios can always end + begin again for multi-phase work.

## Error handling

- **Atomic enter:** `EnterFreeze` runs all steps in a `try`. If any step throws, run the already-completed portion of `ExitFreeze` in reverse to unwind, set `Frozen = false`, and rethrow. No half-frozen state escapes to the caller.
- **Reflection-miss tolerance:** `GameLocation` subclasses without a `random` field are silently skipped (logged at `Trace`). Same for NPCs without `Schedule`. The controller's goal is best-effort freeze across all locations — a handful of exotic subclasses missing fields shouldn't crash the scenario.
- **Scenario-end safety valve:** `ScenarioEndHandler` checks `DeterminismController.Frozen` at entry. If true, call `ExitFreeze()` first, log at `Info` ("scenario ended while frozen — auto-thawed"), then proceed with normal end logic. Prevents a scenario-level exception during the assertion phase from wedging the harness in a frozen state (mirrors the S4 fix pattern).
- **Error codes:** `GameStateInvalid (-32003)` for all precondition violations (existing code), including "no active scenario." Avoids forcing callers to distinguish state errors from param-shape errors when both are "you called this at the wrong time."

## Testing

**Unit tests (no SDV):** follow the established shim pattern (like `TextureAssetRegistry.RegisterShim`).

- `DeterminismController` state machine:
  - `EnterFreeze` when already frozen → throws
  - `ExitFreeze` when not frozen → throws
  - `EnterFreeze` success → `Frozen == true`
  - `ExitFreeze` success → `Frozen == false`
  - `EnterFreeze` with a throwing shim step → state rolls back, `Frozen == false`, exception rethrown
- `LocationRngPinner.PinOne(shim)` / `RestoreOne(shim)`:
  - shim has a `Random random` field reachable by reflection
  - pin with `(seed=42, name="Farm")` → deterministic `Next()` output
  - same `(seed, name)` pair on two shims → identical output (proves determinism across runs)
  - shim without `random` field → PinOne returns silently
- `NpcFreeze.HaltOne(shim)` / `RestoreOne(shim)`:
  - shim with `Position` / `Schedule` / `controller` fields
  - halt snapshots and clears; restore brings back exact pre-halt values
- `FreezeBeginHandler` / `FreezeEndHandler` / `FreezeStatusHandler`:
  - missing scenario → InvalidParams
  - unknown params → (already covered by `RpcParams.Required` convention)
  - happy-path response shape

**Skip-marked integration tests (require live SDV — exercised via D1.7 smoke):**
- `freeze.begin` at title screen → `GameStateInvalid`
- `freeze.begin` mid-warp → `GameStateInvalid`
- `freeze.begin` in save (happy path) → ok; `freeze.status` reports frozen
- Two `draw.snapshot` calls 2s apart while frozen → same tick number
- `freeze.end` without prior begin → `GameStateInvalid`
- `scenario.end` while frozen → auto-thaws; subsequent `freeze.begin` in a new scenario succeeds
- Full round-trip: `eventUp`/`displayHUD`/`Game1.locations[0].random` all equal pre-freeze values after `freeze.end`
- Parallax regression: `Game1.background.position` captured before + after a 60-tick freeze window → identical values (confirms M0 residual fixed)

**Target test count after D1.6:** ~185 Passed + ~22 Skipped (from 169+14).

## Out of scope for D1.6

- **ScenarioRunner auto-wrapping assertions with freeze/thaw** — this is a DSL concern, wrapped up with the D1.7 improvements (`!=`, array indexing, `draw.assert_not_contains`, counter wiring). D1.6 only exposes the RPCs.
- **Fully deterministic minigames** — `currentMinigame != null` is a freeze.begin precondition violation for M1; minigame-internal RNG control is M2+ work.
- **Multiplayer determinism** — single-player only per spec §9.
- **`Game1.eventUp` fine-grained suppression** — blunt `= true` is sufficient per determinism.md §Particles/critters/grass; targeted particle suppression is deferred.

## Acceptance criteria

1. `freeze.begin` / `freeze.end` / `freeze.status` land as documented RPCs in `docs/rpc-schema.md`.
2. All precondition violations throw `GameStateInvalid` with a message naming the failing check.
3. `./scripts/ci.sh` green; unit-test count hits ~185 passed.
4. Skip-marked integration tests exist for every documented behavior (exercise deferred to D1.7 smoke).
5. Parallax regression check passes: two `draw.snapshot` calls 2s apart during a freeze produce byte-identical event streams (modulo tick/callIndex which share the frozen tick).
6. `CursorPatches` + `Recorder.ActivateArm` migration complete — arm no longer mutates ambient flags; cursor freeze driven by FREEZE state.
7. `ScenarioEndHandler` auto-thaws if entered while frozen; logs at Info.

## Links

- Spec: `docs/spec.md` §4.4 Determinism Controller
- Rule: `.claude/rules/determinism.md` §FREEZE lifecycle invariants
- M0 residual: `docs/spikes/2026-04-determinism/REPORT.md` — parallax background scroll
- D1.5 completion: `docs/milestones/current.md` §D1.5 (Tier 1 at 90.8% resolution rate)
