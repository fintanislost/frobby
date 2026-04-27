# Determinism Controller

Nondeterminism is the #1 threat to this framework. Anything that changes between runs on the same inputs must be controlled.

## Sources and treatments

### Game1.random (primary RNG)

Pinned via reflection on scenario start:
```csharp
var field = typeof(Game1).GetField("random", BindingFlags.NonPublic | BindingFlags.Static);
field.SetValue(null, new Random(scenario.Seed));
```

Verify after pinning: call `Game1.random.Next()` once, compare to expected value from the scenario fixture. If mismatch, SDV has changed; fail loud.

### Per-location RNG

Some `GameLocation` subclasses have their own `random` field. Enumerate all loaded locations at scenario start and re-seed each with a deterministic function of `scenario.Seed + location.Name.GetHashCode()`.

### currentGameTime

Controlled via the FREEZE phase. Harmony-patch `Game1.Update` prefix that returns false when `DeterminismController.Frozen == true`. Draw continues; logic does not.

**Critical:** freezing during transitions (e.g., mid-warp, mid-save) deadlocks. Only freeze after confirming stable state via `Game1.eventUp == false && Game1.currentMinigame == null && !Game1.isWarping`.

### NPC movement

All NPCs get `Halt()` + their `Schedule` is nulled during FREEZE. Restored on THAW. Store original schedule in a sidecar dict, not in reflection-read state each time.

### Particles, critters, grass

Blunt instrument: set `Game1.eventUp = true` during FREEZE. This suppresses ambient effects. Side effect: hides HUD. Our capture happens before HUD is relevant for most assertions; for HUD-specific scenarios, use targeted particle suppression instead.

### Cursor

Force `Game1.getMouseX()` / `Game1.getMouseY()` to return (0, 0) during FREEZE via patch. Original values restored on THAW.

### Animation frame counters

Many sprites animate via `Game1.currentGameTime.TotalGameTime`. Since time is frozen, animation pose is deterministic as long as we enter FREEZE at a consistent phase. Capture tick-aligned: always the Nth tick after load.

## FREEZE lifecycle invariants

```
ENTER FREEZE:
  assert !frozen
  snapshot RNG states (for THAW restore)
  snapshot NPC schedules
  snapshot Game1.eventUp
  set frozen = true

ASSERT:
  // all reads/queries happen here
  // no state mutation allowed

EXIT FREEZE (THAW):
  assert frozen
  restore snapshots in reverse order
  set frozen = false
```

Any assertion that mutates state (rare but possible via reflection) is forbidden. Read-only during FREEZE.

## Verification

A dedicated test in `tests/Integration/DeterminismTests.cs`:

1. Load fixture
2. Execute identical scenario twice
3. Capture full draw-call stream for both runs
4. Assert byte-for-byte equality

If this test flakes, stop and fix before any other work. A non-deterministic framework is worse than no framework.
