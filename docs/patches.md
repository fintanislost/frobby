# Active Harmony Patches

Registry of all Harmony patches applied by the harness mod. Updated with every patch addition per @.claude/rules/commit-style.md and @.claude/rules/harmony-patching.md.

## Format

Each patch entry:

```
### <Class>.<Method>(<signature>)

- **Type:** Prefix | Postfix | Transpiler
- **Purpose:** one-line reason
- **Added:** YYYY-MM-DD, commit <sha>
- **SDV versions supported:** <range>
- **Depends on:** <other patches, SMAPI version, etc.>
- **Tested in:** <test file path>
- **Rollback:** <what breaks if this patch is removed>
```

## Patches

> **Note:** The patches below are applied by the M0 **spike** harness at
> `docs/spikes/2026-04-determinism/scratch/Harness/`. Spike code is throwaway
> per `.claude/rules/tdd.md §Exceptions`; these entries will be revisited (and
> the real versions added here with `src/` paths and integration-test
> references) when promoted to production for M1.

### SpriteBatch.Draw(...) — all 7 overloads

- **Type:** Prefix (observation-only, returns void)
- **Purpose:** Capture every draw call to a ring buffer for determinism / assertion tests. Spec §4.2.
- **Added:** 2026-04-21 (M0 spike, not yet committed)
- **SDV versions supported:** Verified against 1.6.15.24356 + SMAPI 4.5.2.0; overload set enumerated at runtime so drift inside 1.6.x is detected at load time.
- **Depends on:** Harmony 2.x (bundled with SMAPI ≥ 4.1.10), `Microsoft.Xna.Framework.Graphics.SpriteBatch`.
- **Tested in:** `docs/spikes/2026-04-determinism/scratch/run.sh` (integration only — the spike is exempt from TDD).
- **Rollback:** Remove the `SpriteBatchDrawPatches.Apply()` call from `ModEntry`. Recorder returns to inert state (zero draw overhead via `Recorder.IsArmed` fast-path).

### Game1.getMouseX() / Game1.getMouseY()

- **Type:** Postfix (rewrites `__result` to 0 while `Recorder.IsArmed`)
- **Purpose:** Eliminate cursor-position nondeterminism during capture. See `.claude/rules/determinism.md §Cursor`.
- **Added:** 2026-04-21 (M0 spike)
- **SDV versions supported:** 1.6.x (method names confirmed via `AccessTools.Method`; any missing overload is logged and skipped, not fatal).
- **Depends on:** Harmony 2.x, SMAPI ≥ 4.1.10.
- **Tested in:** `docs/spikes/2026-04-determinism/scratch/run.sh`.
- **Rollback:** Remove `CursorPatches.Apply()` from `ModEntry`. Cursor reports OS-reported position again.

## Patches explicitly considered and rejected

_(nothing here yet; document rejected approaches here so we don't revisit them)_
