# D1.6 — Determinism Controller (FREEZE/THAW) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **No git repo.** This project is not under git. Ignore any "commit" conventions from other plans. Task completion gate is **`./scripts/ci.sh` green** — same as D1.5 and the M1-RPC plan.

**Goal:** Land `freeze.begin` / `freeze.end` / `freeze.status` RPCs backed by a `DeterminismController` that stops `Game1.currentGameTime`, pins per-location RNG, halts NPCs, freezes the cursor, and suppresses ambient effects — producing bit-identical draw-call streams across runs and closing the M0 parallax-background residual.

**Architecture:** A new static `DeterminismController` singleton (same pattern as `Recorder` and `TextureAssetRegistry.Shared`) owns FREEZE state and an ordered enter/exit orchestration. One Harmony prefix on `Game1.Update(GameTime)` returns `false` while `Frozen == true`, which collapses time-freeze + animation-freeze + parallax-fix into a single patch. Per-location RNG and NPC halt are done by reflection at `freeze.begin` (not via patches) so they can be snapshotted and restored on `freeze.end`. `CursorPatches` and `Recorder`'s ambient-flag flip migrate from "gate on arm" to "gate on freeze" — the two concepts become orthogonal.

**Tech Stack:**
- .NET 6 (Harness), .NET 10 (Runner) — unchanged
- Harmony 2.x — existing patch infrastructure (`TimeFreezePatch`)
- SMAPI 4.5.2 — `Context.IsWorldReady` for precondition checks, `IMonitor` for logging
- Reflection (`System.Reflection`) — for `GameLocation.random` and NPC fields
- xUnit — unit tests, with shim-based coverage for anything not requiring live SDV

**Design spec:** `docs/superpowers/specs/2026-04-23-d16-determinism-controller-design.md`

---

## File structure

**New files:**
- `src/Harness/Determinism/DeterminismController.cs` — static singleton. `Frozen` bool, `EnterFreeze(int seed, IMonitor? monitor)`, `ExitFreeze()`, `Status()`. Holds `SavedState` (NPC snapshots, location-rng snapshots, eventUp/displayHUD booleans). Orchestrates enter/exit in the strict order specified by the spec.
- `src/Harness/Determinism/TimeFreezePatch.cs` — Harmony prefix on `Game1.Update(GameTime)` returning `false` when `DeterminismController.Frozen`.
- `src/Harness/Determinism/LocationRngPinner.cs` — `PinAll(IEnumerable<object>, int seed)` / `RestoreAll(snapshots)`. Reflects into each location's `random` field; re-seeds with `new Random(seed ^ hash)`. Missing-field silent skip.
- `src/Harness/Determinism/NpcFreeze.cs` — `HaltAll(IEnumerable<object>)` / `RestoreAll(snapshots)`. Snapshots `Position`, `Schedule`, `controller`; calls `Halt()`, nulls `controller`. Missing-field silent skip.
- `src/Harness/Handlers/FreezeBeginHandler.cs` — RPC handler `freeze.begin`. Precondition checks, calls `DeterminismController.EnterFreeze`.
- `src/Harness/Handlers/FreezeEndHandler.cs` — RPC handler `freeze.end`.
- `src/Harness/Handlers/FreezeStatusHandler.cs` — RPC handler `freeze.status`.
- `src/Protocol/Models/FreezeBeginResult.cs` — DTO `{ok, locations_pinned, npcs_halted, tick}`.
- `src/Protocol/Models/FreezeStatusResult.cs` — DTO `{frozen, tick}`.
- `tests/Harness.Tests/DeterminismControllerTests.cs` — state-machine unit tests.
- `tests/Harness.Tests/LocationRngPinnerTests.cs` — shim-based pinner tests.
- `tests/Harness.Tests/NpcFreezeTests.cs` — shim-based halt/restore tests.
- `tests/Harness.Tests/FreezeBeginHandlerTests.cs` — handler precondition tests.
- `tests/Harness.Tests/FreezeEndHandlerTests.cs` — handler happy-path + error path tests.
- `tests/Harness.Tests/FreezeStatusHandlerTests.cs` — status query test.
- `tests/Harness.Tests/DeterminismIntegrationTests.cs` — skip-marked integration placeholders.

**Modified files:**
- `src/Harness/Scenarios/ScenarioState.cs` — add `public int Seed { get; set; }`.
- `src/Harness/Handlers/ScenarioBeginHandler.cs` — persist `req.Seed` into `ScenarioState.Current.Seed`.
- `src/Harness/Handlers/ScenarioEndHandler.cs` — auto-thaw at entry if `DeterminismController.Frozen`.
- `src/Harness/Patches/CursorPatches.cs` — gate flips `Recorder.IsArmed` → `DeterminismController.Frozen`. Update patch header comment.
- `src/Harness/Recording/Recorder.cs` — remove `_ambientFlipped` / `_savedEventUp` / `_savedDisplayHUD`; simplify `ActivateArm` and `RestoreSavedState`. Arm becomes purely "start capturing draws."
- `src/Harness/ModEntry.cs` — instantiate controller, register three handlers, apply `TimeFreezePatch.Apply`.
- `docs/rpc-schema.md` — document `freeze.begin`, `freeze.end`, `freeze.status`.

**Verification:** `./scripts/ci.sh` green after each task. Smoke test after Task 12 confirms the parallax regression fix on live SDV.

**Starting test count:** 169 Passed + 14 Skipped.
**Target test count after D1.6:** ~190 Passed + ~21 Skipped.

---

## Task 1: ScenarioState.Seed persistence

**Why first:** `DeterminismController.EnterFreeze` needs the scenario seed to pin per-location RNG. Currently `ScenarioBeginHandler` consumes `req.Seed` and discards it. Fix that before building anything that reads it.

**Files:**
- Modify: `src/Harness/Scenarios/ScenarioState.cs`
- Modify: `src/Harness/Handlers/ScenarioBeginHandler.cs`
- Test: `tests/Harness.Tests/ScenarioBeginHandlerTests.cs` (add one test)

**Dependencies:** none.

- [ ] **Step 1: Add a failing test**

Open `tests/Harness.Tests/ScenarioBeginHandlerTests.cs` and add this test at the bottom of the class:

```csharp
    [Fact]
    public void Handle_PersistsSeedToScenarioState()
    {
        ScenarioState.Current.Reset();
        var json = JsonDocument.Parse("""{"name":"s","seed":1234}""").RootElement;
        ScenarioBeginHandler.Handle(json);
        Assert.Equal(1234, ScenarioState.Current.Seed);
        ScenarioState.Current.Reset();
    }
```

If `ScenarioBeginHandlerTests.cs` doesn't import `SdvTestFramework.Harness.Scenarios` and `System.Text.Json`, add those using directives.

Run: `dotnet test tests/Harness.Tests/ --filter Handle_PersistsSeedToScenarioState`
Expected: FAIL — `ScenarioState` has no `Seed` property.

- [ ] **Step 2: Add Seed field to ScenarioState**

In `src/Harness/Scenarios/ScenarioState.cs`, add between `AssertionsPassed` and `Reset()`:

```csharp
    /// <summary>Seed supplied at <c>scenario.begin</c>. Consumed by <c>DeterminismController</c>
    /// at <c>freeze.begin</c> to pin per-location RNG deterministically.</summary>
    public int Seed { get; set; }
```

Inside `Reset()`, add `Seed = 0;` so fresh scenarios get a known default.

- [ ] **Step 3: Persist seed in ScenarioBeginHandler**

In `src/Harness/Handlers/ScenarioBeginHandler.cs`, find the block that sets `s.Name = req.Name;` and insert right after:

```csharp
        s.Seed = req.Seed;
```

- [ ] **Step 4: Verify green**

Run: `./scripts/ci.sh`
Expected: PASS. Test count 169 → 170.

---

## Task 2: DeterminismController skeleton + state-machine tests

Pure state-machine logic. No orchestration yet — that's Task 5. This task proves the Frozen bool transitions correctly and throws on invalid transitions.

**Files:**
- Create: `src/Harness/Determinism/DeterminismController.cs`
- Test: `tests/Harness.Tests/DeterminismControllerTests.cs`

**Dependencies:** Task 1 (for `ScenarioState.Seed` — though this task doesn't consume it yet, the controller's eventual EnterFreeze signature expects it to exist).

- [ ] **Step 1: Write the failing test file**

Create `tests/Harness.Tests/DeterminismControllerTests.cs`:

```csharp
using System;
using SdvTestFramework.Harness.Determinism;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class DeterminismControllerTests
{
    public DeterminismControllerTests()
    {
        // Tests mutate a process-wide singleton; reset before each.
        DeterminismController.ResetForTests();
    }

    [Fact]
    public void Frozen_DefaultsFalse()
    {
        Assert.False(DeterminismController.Frozen);
    }

    [Fact]
    public void EnterFreeze_WhenNotFrozen_FlipsFrozenTrue()
    {
        DeterminismController.EnterFreeze(seed: 42, monitor: null);
        Assert.True(DeterminismController.Frozen);
    }

    [Fact]
    public void EnterFreeze_WhenAlreadyFrozen_Throws()
    {
        DeterminismController.EnterFreeze(seed: 42, monitor: null);
        Assert.Throws<InvalidOperationException>(
            () => DeterminismController.EnterFreeze(seed: 43, monitor: null));
    }

    [Fact]
    public void ExitFreeze_WhenFrozen_FlipsFrozenFalse()
    {
        DeterminismController.EnterFreeze(seed: 42, monitor: null);
        DeterminismController.ExitFreeze();
        Assert.False(DeterminismController.Frozen);
    }

    [Fact]
    public void ExitFreeze_WhenNotFrozen_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => DeterminismController.ExitFreeze());
    }
}
```

Run: `dotnet test tests/Harness.Tests/ --filter DeterminismController`
Expected: FAIL — type `DeterminismController` doesn't exist.

- [ ] **Step 2: Create the controller skeleton**

Create `src/Harness/Determinism/DeterminismController.cs`:

```csharp
using System;
using StardewModdingAPI;

namespace SdvTestFramework.Harness.Determinism;

/// <summary>
/// Owns the FREEZE/THAW state for the test harness. While <see cref="Frozen"/> is true,
/// <see cref="TimeFreezePatch"/> short-circuits <c>Game1.Update</c>, <c>CursorPatches</c>
/// zeros the cursor, per-location RNG is pinned, and NPCs are halted — the combined
/// effect is a "paused" game world where repeated queries see a consistent moment.
/// </summary>
/// <remarks>
/// Static singleton by design. Matches the pattern of <c>Recorder</c> and
/// <c>TextureAssetRegistry.Shared</c> — one instance per process, no DI plumbing.
/// Thread-safety: Writers (EnterFreeze / ExitFreeze) run on the game thread via the
/// RPC drain. Readers (TimeFreezePatch prefix, CursorPatches postfix) read a volatile
/// bool and return. Single-writer, many-reader — no locks.
/// </remarks>
public static class DeterminismController
{
    private static volatile bool _frozen;

    /// <summary>True while a freeze is active. Read by patches on the hot path.</summary>
    public static bool Frozen => _frozen;

    /// <summary>
    /// Enter the frozen state. Throws <see cref="InvalidOperationException"/> if already frozen
    /// — callers should reach this via the <c>freeze.begin</c> RPC handler which surfaces a
    /// typed JSON-RPC error. Orchestration (state snapshot, per-location RNG pin, NPC halt)
    /// is wired in Task 5; for now this just flips the bool.
    /// </summary>
    public static void EnterFreeze(int seed, IMonitor? monitor)
    {
        if (_frozen)
            throw new InvalidOperationException("Already frozen.");
        _frozen = true;
        monitor?.Log($"FREEZE entered (seed={seed}).", LogLevel.Info);
    }

    /// <summary>Exit the frozen state. Throws if not frozen.</summary>
    public static void ExitFreeze()
    {
        if (!_frozen)
            throw new InvalidOperationException("Not frozen.");
        _frozen = false;
    }

    /// <summary>Test-only reset so unit tests don't pollute each other. Not on the public API.</summary>
    internal static void ResetForTests() => _frozen = false;
}
```

Add `<InternalsVisibleTo Include="Harness.Tests" />` to `src/Harness/Harness.csproj` if not already present. (It is — added during D1.5.)

- [ ] **Step 3: Verify green**

Run: `./scripts/ci.sh`
Expected: PASS. Test count 170 → 175 (+5).

---

## Task 3: LocationRngPinner + shim tests

Reflection-based RNG pinner that iterates locations and re-seeds their `random` field. Tested entirely via shims — no live SDV.

**Files:**
- Create: `src/Harness/Determinism/LocationRngPinner.cs`
- Test: `tests/Harness.Tests/LocationRngPinnerTests.cs`

**Dependencies:** Task 2 (controller namespace exists).

- [ ] **Step 1: Write failing tests**

Create `tests/Harness.Tests/LocationRngPinnerTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using SdvTestFramework.Harness.Determinism;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class LocationRngPinnerTests
{
    // Shim stand-in for GameLocation. The pinner uses reflection, so it only cares
    // that a field named "random" of type Random exists.
    private sealed class LocationShim
    {
        public string Name { get; set; } = "Unknown";
        // Intentionally not a property — GameLocation.random is a field.
#pragma warning disable 649
        public Random? random;
#pragma warning restore 649
    }

    private sealed class NoRandomShim { public string Name { get; set; } = "Unknown"; }

    [Fact]
    public void PinAll_SetsRandomOnShimsWithField()
    {
        var a = new LocationShim { Name = "Farm" };
        var b = new LocationShim { Name = "Town" };

        var snaps = LocationRngPinner.PinAll(new object[] { a, b }, seed: 42);

        Assert.NotNull(a.random);
        Assert.NotNull(b.random);
        Assert.Equal(2, snaps.Count);
    }

    [Fact]
    public void PinAll_SameInputs_DeterministicOutput()
    {
        var a1 = new LocationShim { Name = "Farm" };
        var a2 = new LocationShim { Name = "Farm" };

        LocationRngPinner.PinAll(new object[] { a1 }, seed: 42);
        LocationRngPinner.PinAll(new object[] { a2 }, seed: 42);

        Assert.Equal(a1.random!.Next(), a2.random!.Next());
    }

    [Fact]
    public void PinAll_DifferentNames_DifferentOutput()
    {
        // Same seed, different location names → different streams (seed ^ name-hash).
        var farm = new LocationShim { Name = "Farm" };
        var town = new LocationShim { Name = "Town" };

        LocationRngPinner.PinAll(new object[] { farm, town }, seed: 42);
        Assert.NotEqual(farm.random!.Next(), town.random!.Next());
    }

    [Fact]
    public void PinAll_ShimsWithoutRandomField_SilentlySkipped()
    {
        var a = new LocationShim { Name = "Farm" };
        var b = new NoRandomShim { Name = "Exotic" };

        // Must not throw.
        var snaps = LocationRngPinner.PinAll(new object[] { a, b }, seed: 42);

        // Only the shim with a `random` field produced a snapshot.
        Assert.Single(snaps);
    }

    [Fact]
    public void RestoreAll_RestoresOriginalRandom()
    {
        var a = new LocationShim { Name = "Farm" };
        a.random = new Random(99);
        int pre = a.random.Next();
        // reset state, then pin
        a.random = new Random(99);
        var snaps = LocationRngPinner.PinAll(new object[] { a }, seed: 42);
        Assert.NotNull(a.random);
        LocationRngPinner.RestoreAll(snaps);
        // After restore, the original Random is back — identical next-value.
        Assert.Equal(pre, a.random!.Next());
    }
}
```

Run: `dotnet test tests/Harness.Tests/ --filter LocationRngPinner`
Expected: FAIL — `LocationRngPinner` type doesn't exist.

- [ ] **Step 2: Create the pinner**

Create `src/Harness/Determinism/LocationRngPinner.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Reflection;

namespace SdvTestFramework.Harness.Determinism;

/// <summary>
/// Snapshot entry for a single pinned location. Pairs the object (to reflect into on
/// restore) with the original <see cref="Random"/> instance we saved.
/// </summary>
public readonly struct LocationRngSnapshot
{
    public LocationRngSnapshot(object location, FieldInfo field, Random? original)
    {
        Location = location;
        Field = field;
        Original = original;
    }
    public object Location { get; }
    public FieldInfo Field { get; }
    public Random? Original { get; }
}

/// <summary>
/// Pins each location's <c>random</c> field to a deterministic seed function. Tolerates
/// subclasses without a <c>random</c> field — those are silently skipped per
/// <c>.claude/rules/determinism.md</c>.
/// </summary>
public static class LocationRngPinner
{
    /// <summary>
    /// Pin every input location's <c>random</c> field to
    /// <c>new Random(seed ^ location-name-hash)</c>. Returns snapshots for restoration.
    /// </summary>
    public static IReadOnlyList<LocationRngSnapshot> PinAll(
        IEnumerable<object> locations, int seed)
    {
        var snaps = new List<LocationRngSnapshot>();
        foreach (var loc in locations)
        {
            var field = loc.GetType().GetField(
                "random",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field is null || field.FieldType != typeof(Random))
                continue;

            var original = field.GetValue(loc) as Random;
            var name = ReadName(loc);
            field.SetValue(loc, new Random(seed ^ name.GetHashCode()));
            snaps.Add(new LocationRngSnapshot(loc, field, original));
        }
        return snaps;
    }

    /// <summary>Restore every snapshotted location's <c>random</c> field to its original value.</summary>
    public static void RestoreAll(IEnumerable<LocationRngSnapshot> snapshots)
    {
        foreach (var s in snapshots)
            s.Field.SetValue(s.Location, s.Original);
    }

    private static string ReadName(object location)
    {
        // GameLocation.Name is a public instance property in SDV. Fall back to the
        // object's type name so shims and exotic subclasses still hash to something stable.
        var prop = location.GetType().GetProperty("Name",
            BindingFlags.Public | BindingFlags.Instance);
        if (prop?.GetValue(location) is string s && !string.IsNullOrEmpty(s))
            return s;
        return location.GetType().Name;
    }
}
```

- [ ] **Step 3: Verify green**

Run: `./scripts/ci.sh`
Expected: PASS. Test count 175 → 180 (+5).

---

## Task 4: NpcFreeze + shim tests

Reflection-based NPC halt + snapshot/restore. Mirrors LocationRngPinner's structure.

**Files:**
- Create: `src/Harness/Determinism/NpcFreeze.cs`
- Test: `tests/Harness.Tests/NpcFreezeTests.cs`

**Dependencies:** Task 2.

- [ ] **Step 1: Write failing tests**

Create `tests/Harness.Tests/NpcFreezeTests.cs`:

```csharp
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Determinism;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class NpcFreezeTests
{
    // Shim stand-in for NPC. Fields mirror the real NPC type closely enough for the
    // pinner: Position (Vector2), Schedule (object-typed so the shim can hold anything),
    // controller (object-typed same reason). Halt() is called during freeze.
    private sealed class NpcShim
    {
        public Vector2 Position;
        public object? Schedule;
        public object? controller;
        public int HaltCount;
        public void Halt() => HaltCount++;
    }

    [Fact]
    public void HaltAll_CallsHaltAndNullsController()
    {
        var npc = new NpcShim
        {
            Position = new Vector2(3, 4),
            Schedule = new object(),
            controller = new object(),
        };

        var snaps = NpcFreeze.HaltAll(new object[] { npc });

        Assert.Equal(1, npc.HaltCount);
        Assert.Null(npc.controller);
        Assert.Single(snaps);
    }

    [Fact]
    public void RestoreAll_RestoresPositionScheduleController()
    {
        var sched = new object();
        var ctrl = new object();
        var npc = new NpcShim
        {
            Position = new Vector2(3, 4),
            Schedule = sched,
            controller = ctrl,
        };

        var snaps = NpcFreeze.HaltAll(new object[] { npc });
        // Mutate post-halt to confirm restore overwrites
        npc.Position = new Vector2(99, 99);

        NpcFreeze.RestoreAll(snaps);

        Assert.Equal(new Vector2(3, 4), npc.Position);
        Assert.Same(sched, npc.Schedule);
        Assert.Same(ctrl, npc.controller);
    }

    [Fact]
    public void HaltAll_ShimWithoutFields_SilentlySkipped()
    {
        // Object without any of the expected fields — should not throw.
        var snaps = NpcFreeze.HaltAll(new object[] { new object() });
        Assert.Empty(snaps);
    }
}
```

Run: `dotnet test tests/Harness.Tests/ --filter NpcFreeze`
Expected: FAIL — `NpcFreeze` type doesn't exist.

- [ ] **Step 2: Create NpcFreeze**

Create `src/Harness/Determinism/NpcFreeze.cs`:

```csharp
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;

namespace SdvTestFramework.Harness.Determinism;

/// <summary>Snapshot entry capturing one NPC's pre-halt state.</summary>
public readonly struct NpcFreezeSnapshot
{
    public NpcFreezeSnapshot(object npc, Vector2 position, object? schedule, object? controller)
    {
        Npc = npc;
        Position = position;
        Schedule = schedule;
        Controller = controller;
    }
    public object Npc { get; }
    public Vector2 Position { get; }
    public object? Schedule { get; }
    public object? Controller { get; }
}

/// <summary>
/// Halt every input NPC: snapshot their <c>Position</c>/<c>Schedule</c>/<c>controller</c>,
/// call <c>Halt()</c>, null out <c>controller</c>. Restore reverses those steps. Missing
/// fields tolerated silently — exotic subclasses that lack one of these get skipped.
/// </summary>
public static class NpcFreeze
{
    private const BindingFlags AllInstance =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    public static IReadOnlyList<NpcFreezeSnapshot> HaltAll(IEnumerable<object> npcs)
    {
        var snaps = new List<NpcFreezeSnapshot>();
        foreach (var npc in npcs)
        {
            var t = npc.GetType();
            var positionField = t.GetField("Position", AllInstance);
            var scheduleField = t.GetField("Schedule", AllInstance);
            var controllerField = t.GetField("controller", AllInstance);
            if (positionField is null || scheduleField is null || controllerField is null)
                continue;

            var pos = (Vector2)(positionField.GetValue(npc) ?? default(Vector2));
            var sched = scheduleField.GetValue(npc);
            var ctrl = controllerField.GetValue(npc);

            // Call Halt() if present. Mirrors NPC.Halt() in SDV.
            t.GetMethod("Halt", AllInstance)?.Invoke(npc, null);
            controllerField.SetValue(npc, null);

            snaps.Add(new NpcFreezeSnapshot(npc, pos, sched, ctrl));
        }
        return snaps;
    }

    public static void RestoreAll(IEnumerable<NpcFreezeSnapshot> snapshots)
    {
        foreach (var s in snapshots)
        {
            var t = s.Npc.GetType();
            t.GetField("Position", AllInstance)?.SetValue(s.Npc, s.Position);
            t.GetField("Schedule", AllInstance)?.SetValue(s.Npc, s.Schedule);
            t.GetField("controller", AllInstance)?.SetValue(s.Npc, s.Controller);
        }
    }
}
```

- [ ] **Step 3: Verify green**

Run: `./scripts/ci.sh`
Expected: PASS. Test count 180 → 183 (+3).

---

## Task 5: Controller orchestration + TimeFreezePatch

Wire the skeleton controller from Task 2 into real enter/exit orchestration that consults the pinners (Tasks 3–4) and flips ambient flags. Also lands the Harmony patch on `Game1.Update`.

**Files:**
- Modify: `src/Harness/Determinism/DeterminismController.cs`
- Create: `src/Harness/Determinism/TimeFreezePatch.cs`
- Test: append to `tests/Harness.Tests/DeterminismControllerTests.cs`

**Dependencies:** Tasks 2, 3, 4.

- [ ] **Step 1: Append failing tests for orchestration**

Append to `tests/Harness.Tests/DeterminismControllerTests.cs` inside the existing class:

```csharp
    [Fact]
    public void EnterFreeze_ThenExit_CallsOrchestrationHooksInOrder()
    {
        // Inject a recorder to observe the ordering of SnapshotAmbient / PinRngs / HaltNpcs.
        var log = new List<string>();
        var priorHooks = DeterminismController.HooksForTests;
        DeterminismController.HooksForTests = new DeterminismController.Hooks(
            SnapshotAmbient: () => log.Add("snap"),
            ApplyAmbient: () => log.Add("apply"),
            PinRngs: _ => { log.Add("pin"); return 7; },
            HaltNpcs: () => { log.Add("halt"); return 3; },
            RestoreAmbient: () => log.Add("unapply"),
            RestoreRngs: () => log.Add("unpin"),
            RestoreNpcs: () => log.Add("unhalt"));
        try
        {
            DeterminismController.EnterFreeze(seed: 42, monitor: null);
            DeterminismController.ExitFreeze();

            Assert.Equal(
                new[] { "snap", "apply", "pin", "halt", "unpin", "unhalt", "unapply" },
                log);
        }
        finally { DeterminismController.HooksForTests = priorHooks; }
    }

    [Fact]
    public void EnterFreeze_WhenPinThrows_RollsBackAndRethrows()
    {
        var log = new List<string>();
        var priorHooks = DeterminismController.HooksForTests;
        DeterminismController.HooksForTests = new DeterminismController.Hooks(
            SnapshotAmbient: () => log.Add("snap"),
            ApplyAmbient: () => log.Add("apply"),
            PinRngs: _ => throw new InvalidOperationException("simulated failure"),
            HaltNpcs: () => { log.Add("halt"); return 0; },
            RestoreAmbient: () => log.Add("unapply"),
            RestoreRngs: () => log.Add("unpin"),
            RestoreNpcs: () => log.Add("unhalt"));
        try
        {
            Assert.Throws<InvalidOperationException>(
                () => DeterminismController.EnterFreeze(seed: 42, monitor: null));
            // Frozen state rolled back.
            Assert.False(DeterminismController.Frozen);
            // Only snap + apply ran; pin threw; unapply ran during rollback.
            Assert.Equal(new[] { "snap", "apply", "unapply" }, log);
        }
        finally { DeterminismController.HooksForTests = priorHooks; }
    }

    [Fact]
    public void EnterFreeze_ReportsCounts()
    {
        var priorHooks = DeterminismController.HooksForTests;
        DeterminismController.HooksForTests = new DeterminismController.Hooks(
            SnapshotAmbient: () => { },
            ApplyAmbient: () => { },
            PinRngs: _ => 11,
            HaltNpcs: () => 22,
            RestoreAmbient: () => { },
            RestoreRngs: () => { },
            RestoreNpcs: () => { });
        try
        {
            var result = DeterminismController.EnterFreeze(seed: 1, monitor: null);
            Assert.Equal(11, result.LocationsPinned);
            Assert.Equal(22, result.NpcsHalted);
        }
        finally
        {
            if (DeterminismController.Frozen) DeterminismController.ExitFreeze();
            DeterminismController.HooksForTests = priorHooks;
        }
    }
```

Add at top of file if missing: `using System; using System.Collections.Generic;`.

Run: `dotnet test tests/Harness.Tests/ --filter DeterminismController`
Expected: FAIL — `Hooks`, `HooksForTests`, return shape changes don't exist yet.

- [ ] **Step 2: Rewrite DeterminismController with orchestration**

Replace `src/Harness/Determinism/DeterminismController.cs` with:

```csharp
using System;
using System.Collections.Generic;
using System.Reflection;
using StardewModdingAPI;
using StardewValley;

namespace SdvTestFramework.Harness.Determinism;

/// <summary>
/// Owns FREEZE/THAW state. While <see cref="Frozen"/> is true, <see cref="TimeFreezePatch"/>
/// short-circuits <c>Game1.Update</c>, cursor patches zero the cursor, per-location RNG is
/// pinned, NPCs are halted, and ambient flags (<c>eventUp</c>/<c>displayHUD</c>) are flipped.
/// </summary>
public static class DeterminismController
{
    private static volatile bool _frozen;

    /// <summary>True while a freeze is active. Read on the hot path by patches.</summary>
    public static bool Frozen => _frozen;

    /// <summary>
    /// Result shape returned from <see cref="EnterFreeze"/>. Surfaces counts that the
    /// <c>freeze.begin</c> RPC reports back to the client for debugging.
    /// </summary>
    public readonly record struct EnterResult(int LocationsPinned, int NpcsHalted);

    // ---- Hook-injection seam for tests ------------------------------------------------
    //
    // Unit tests can't spin up Game1 or reflect into live GameLocations, so the orchestration
    // routes through a small Hooks record. Production code wires these to real SDV calls via
    // UseProductionHooks(); tests override with a recording stand-in. The seam is deliberately
    // loose (delegates, not an interface) so tests don't need a mock framework.

    public sealed record Hooks(
        Action SnapshotAmbient,
        Action ApplyAmbient,
        Func<int, int> PinRngs,         // receives seed, returns count pinned
        Func<int> HaltNpcs,               // returns count halted
        Action RestoreAmbient,
        Action RestoreRngs,
        Action RestoreNpcs);

    /// <summary>Test seam: replace to mock orchestration. Defaults to no-op hooks.</summary>
    public static Hooks HooksForTests { get; set; } = NoopHooks();

    private static Hooks NoopHooks() => new(
        SnapshotAmbient: () => { },
        ApplyAmbient: () => { },
        PinRngs: _ => 0,
        HaltNpcs: () => 0,
        RestoreAmbient: () => { },
        RestoreRngs: () => { },
        RestoreNpcs: () => { });

    // ---- Public API -----------------------------------------------------------------

    public static EnterResult EnterFreeze(int seed, IMonitor? monitor)
    {
        if (_frozen) throw new InvalidOperationException("Already frozen.");

        var h = HooksForTests;
        bool snapshotTaken = false;
        bool ambientApplied = false;
        int pinnedCount = 0;
        bool haltDone = false;

        try
        {
            h.SnapshotAmbient();      snapshotTaken = true;
            h.ApplyAmbient();         ambientApplied = true;
            pinnedCount = h.PinRngs(seed);
            var halted = h.HaltNpcs();
            haltDone = true;

            _frozen = true;
            monitor?.Log(
                $"FREEZE entered (seed={seed}, locations_pinned={pinnedCount}, npcs_halted={halted}).",
                LogLevel.Info);
            return new EnterResult(pinnedCount, halted);
        }
        catch
        {
            // Roll back in reverse order of completion.
            if (haltDone)        try { h.RestoreNpcs(); }     catch { /* best effort */ }
            if (pinnedCount > 0) try { h.RestoreRngs(); }     catch { /* best effort */ }
            if (ambientApplied)  try { h.RestoreAmbient(); }  catch { /* best effort */ }
            _frozen = false;
            _ = snapshotTaken;  // snapshot is pure observation; nothing to undo
            throw;
        }
    }

    public static void ExitFreeze()
    {
        if (!_frozen) throw new InvalidOperationException("Not frozen.");
        var h = HooksForTests;
        // Reverse of EnterFreeze:
        h.RestoreRngs();
        h.RestoreNpcs();
        h.RestoreAmbient();
        _frozen = false;
    }

    /// <summary>Test-only reset so unit tests don't pollute each other.</summary>
    internal static void ResetForTests()
    {
        _frozen = false;
        HooksForTests = NoopHooks();
    }

    // ---- Production wiring -----------------------------------------------------------

    private static bool _savedEventUp;
    private static bool _savedDisplayHUD;
    private static IReadOnlyList<LocationRngSnapshot> _locationSnaps = Array.Empty<LocationRngSnapshot>();
    private static IReadOnlyList<NpcFreezeSnapshot> _npcSnaps = Array.Empty<NpcFreezeSnapshot>();

    /// <summary>Wire the hooks to real SDV calls. Called once from <c>ModEntry.Entry</c>.</summary>
    public static void UseProductionHooks()
    {
        HooksForTests = new Hooks(
            SnapshotAmbient: () =>
            {
                _savedEventUp = Game1.eventUp;
                _savedDisplayHUD = Game1.displayHUD;
            },
            ApplyAmbient: () =>
            {
                Game1.eventUp = true;
                Game1.displayHUD = false;
            },
            PinRngs: seed =>
            {
                _locationSnaps = LocationRngPinner.PinAll(Game1.locations, seed);
                return _locationSnaps.Count;
            },
            HaltNpcs: () =>
            {
                var npcs = new List<object>();
                foreach (var loc in Game1.locations)
                {
                    // GameLocation.characters is a NetCollection<NPC> — iterable as IEnumerable.
                    var charField = loc.GetType().GetField("characters",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (charField?.GetValue(loc) is System.Collections.IEnumerable chars)
                        foreach (var n in chars) npcs.Add(n);
                }
                _npcSnaps = NpcFreeze.HaltAll(npcs);
                return _npcSnaps.Count;
            },
            RestoreAmbient: () =>
            {
                Game1.eventUp = _savedEventUp;
                Game1.displayHUD = _savedDisplayHUD;
            },
            RestoreRngs: () =>
            {
                LocationRngPinner.RestoreAll(_locationSnaps);
                _locationSnaps = Array.Empty<LocationRngSnapshot>();
            },
            RestoreNpcs: () =>
            {
                NpcFreeze.RestoreAll(_npcSnaps);
                _npcSnaps = Array.Empty<NpcFreezeSnapshot>();
            });
    }
}
```

- [ ] **Step 3: Create TimeFreezePatch**

Create `src/Harness/Determinism/TimeFreezePatch.cs`:

```csharp
using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;

namespace SdvTestFramework.Harness.Determinism;

/// <summary>Harmony prefix on <c>Game1.Update(GameTime)</c> that short-circuits while frozen.</summary>
// Patch: StardewValley.Game1.Update(GameTime)
// Type: Prefix (returns false when DeterminismController.Frozen, skipping original body)
// Why: Freeze Game1.currentGameTime (and therefore parallax background, animations, NPC AI)
//      while a scenario assertion phase is active. One patch site covers all three residual
//      nondeterminism sources M0 identified.
// Rollback: Remove the Apply() call from ModEntry; Frozen bool has no effect.
// Tested in: tests/Harness.Tests/DeterminismIntegrationTests.cs (skip-marked integration)
// Depends on: Harmony 2.x (bundled with SMAPI), SMAPI >= 4.1.10, SDV 1.6.x
public static class TimeFreezePatch
{
    public static void Apply(Harmony harmony, IMonitor monitor)
    {
        var target = AccessTools.Method(typeof(Game1), "Update", new[] { typeof(GameTime) });
        if (target is null)
            throw new InvalidOperationException(
                "Game1.Update(GameTime) not found — SDV internals have shifted.");

        var prefix = new HarmonyMethod(
            typeof(TimeFreezePatch), nameof(Prefix));
        harmony.Patch(target, prefix: prefix);
        monitor.Log("Patched: Game1.Update(GameTime) — returns false while frozen.", LogLevel.Info);
    }

    // Returning false from a Harmony prefix skips the original method body. SMAPI's
    // SGame.Update continues past base.Update() so UpdateTicked events still fire and
    // the RPC drain keeps working while the game itself is paused.
    private static bool Prefix() => !DeterminismController.Frozen;
}
```

- [ ] **Step 4: Verify green**

Run: `./scripts/ci.sh`
Expected: PASS. Test count 183 → 186 (+3).

---

## Task 6: Migrate CursorPatches gate

Cursor freeze belongs to FREEZE, not capture. One-line change in the gate + update the patch-header comment.

**Files:**
- Modify: `src/Harness/Patches/CursorPatches.cs`

**Dependencies:** Task 2.

- [ ] **Step 1: Flip the gate**

In `src/Harness/Patches/CursorPatches.cs`, replace:

```csharp
    private static void ReturnZero(ref int __result)
    {
        if (Recorder.IsArmed) __result = 0;
    }
```

with:

```csharp
    private static void ReturnZero(ref int __result)
    {
        if (DeterminismController.Frozen) __result = 0;
    }
```

Update the `using` at the top: remove `using SdvTestFramework.Harness.Recording;` (if it's now unused — keep it if other members of the file still reference `Recorder`) and add `using SdvTestFramework.Harness.Determinism;`.

- [ ] **Step 2: Update the patch-header comment**

In the same file, replace the `// Why:` comment line to reflect the new gate:

```csharp
// Why: Cursor-sensitive draws (hover tooltips, button highlights) are a nondeterminism
//      source. Force-zero during FREEZE per .claude/rules/determinism.md §Cursor.
//      Gated on DeterminismController.Frozen — pre-D1.6 gated on Recorder.IsArmed,
//      but freeze and capture are orthogonal concerns (can freeze without armed).
```

- [ ] **Step 3: Verify green**

Run: `./scripts/ci.sh`
Expected: PASS. Test count unchanged at 186. Existing cursor tests, if any, pass because no test asserts `Recorder.IsArmed` gate specifically.

---

## Task 7: Migrate Recorder — delete ambient flag flips

Arm becomes purely "start capture." All ambient-flag flipping moves to the controller.

**Files:**
- Modify: `src/Harness/Recording/Recorder.cs`

**Dependencies:** Task 5 (controller owns ambient flipping).

- [ ] **Step 1: Simplify Recorder.cs**

In `src/Harness/Recording/Recorder.cs`:

**a.** Delete the three private fields:
```csharp
    // State saved while armed so we can restore on disarm.
    private static bool _savedEventUp;
    private static bool _savedDisplayHUD;
    private static bool _ambientFlipped; // true iff we actually wrote to eventUp/displayHUD this arm
```

**b.** Replace `ActivateArm` with:

```csharp
    private static void ActivateArm(bool deferred)
    {
        // Arm is purely "start capture." Ambient-effect suppression and cursor freeze
        // now live in DeterminismController (D1.6 migration); scenarios that want them
        // should call freeze.begin before/alongside arm.
        _armed = true;
        _monitor?.Log(
            $"ARMED{(deferred ? " (deferred)" : "")}: capturing {_ticksRemaining} ticks to {_pendingOutputPath ?? "<in-memory>"}",
            LogLevel.Info);
    }
```

**c.** Replace `RestoreSavedState` with:

```csharp
    private static void RestoreSavedState()
    {
        // No-op after D1.6 migration — Recorder no longer owns ambient state.
        // Kept as an empty method so Disarm's call site stays readable; can be deleted
        // entirely if call sites are cleaned up.
    }
```

Alternatively, inline-delete the two `RestoreSavedState()` call sites (in `Disarm` and `OnUpdateTicked`) and delete the method outright. Choose the cleaner path — deleting is fine because the method has no callers outside the file.

**d.** Remove the imports that become unused: check if `using StardewValley;` is still needed (it likely is for `Game1.gameMode`/`Game1.playingGameMode`/`Game1.ticks`); keep it if so.

- [ ] **Step 2: Verify green**

Run: `./scripts/ci.sh`
Expected: PASS. Test count unchanged at 186. Existing Recorder-adjacent tests (`DrawArmHandlerTests`, `DrawSnapshotHandlerTests`) only exercise `Arm`/`Disarm`/`SnapshotEvents` — none asserted ambient-flag side effects, so nothing breaks.

---

## Task 8: FreezeBeginHandler + DTO + tests

The heavy handler task — precondition checks, response DTO, RPC wiring.

**Files:**
- Create: `src/Protocol/Models/FreezeBeginResult.cs`
- Create: `src/Harness/Handlers/FreezeBeginHandler.cs`
- Test: `tests/Harness.Tests/FreezeBeginHandlerTests.cs`

**Dependencies:** Task 5 (controller exists), Task 1 (ScenarioState.Seed exists).

- [ ] **Step 1: Create the DTO**

Create `src/Protocol/Models/FreezeBeginResult.cs`:

```csharp
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Protocol.Models;

/// <summary>Response shape of <c>freeze.begin</c>.</summary>
public sealed class FreezeBeginResult : MutatorOk
{
    /// <summary>Number of <c>GameLocation</c>s whose <c>random</c> field was pinned.</summary>
    public int LocationsPinned { get; set; }

    /// <summary>Number of NPCs halted.</summary>
    public int NpcsHalted { get; set; }
}
```

Verify `MutatorOk` exists (it does — added in M1 T4). It provides `Ok` + `Tick`.

- [ ] **Step 2: Write failing tests for the handler**

Create `tests/Harness.Tests/FreezeBeginHandlerTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Determinism;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Harness.Scenarios;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

[Collection("ScenarioState")]
public class FreezeBeginHandlerTests
{
    public FreezeBeginHandlerTests()
    {
        ScenarioState.Current.Reset();
        DeterminismController.ResetForTests();
    }

    [Fact]
    public void Handle_NoActiveScenario_ThrowsGameStateInvalid()
    {
        // No scenario.begin happened; ScenarioState.IsActive == false.
        var ex = Assert.Throws<JsonRpcException>(() => FreezeBeginHandler.Handle(null));
        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("active scenario", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Handle_AlreadyFrozen_ThrowsGameStateInvalid()
    {
        ScenarioState.Current.IsActive = true;
        ScenarioState.Current.Seed = 1;
        DeterminismController.HooksForTests = new DeterminismController.Hooks(
            SnapshotAmbient: () => { }, ApplyAmbient: () => { },
            PinRngs: _ => 0, HaltNpcs: () => 0,
            RestoreAmbient: () => { }, RestoreRngs: () => { }, RestoreNpcs: () => { });
        DeterminismController.EnterFreeze(seed: 1, monitor: null);
        try
        {
            var ex = Assert.Throws<JsonRpcException>(() => FreezeBeginHandler.Handle(null));
            Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        }
        finally { DeterminismController.ExitFreeze(); }
    }
}
```

Note: live-SDV precondition checks (`Context.IsWorldReady`, `Game1.eventUp`, `Game1.currentMinigame`, `Game1.isWarping`) can't be exercised in unit tests — they're covered by Task 12's skip-marked integration tests.

Run: `dotnet test tests/Harness.Tests/ --filter FreezeBeginHandler`
Expected: FAIL — type doesn't exist.

- [ ] **Step 3: Create the handler**

Create `src/Harness/Handlers/FreezeBeginHandler.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Harness.Determinism;
using SdvTestFramework.Harness.Scenarios;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewModdingAPI;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>freeze.begin</c>. Enforces strict preconditions, calls into
/// <see cref="DeterminismController.EnterFreeze"/>, returns pinned-count metrics.</summary>
public static class FreezeBeginHandler
{
    public const string Method = "freeze.begin";

    /// <summary>Set by <c>ModEntry</c> at startup so orchestration can log.</summary>
    public static IMonitor? Monitor { get; set; }

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var s = ScenarioState.Current;
        if (!s.IsActive)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "freeze.begin requires an active scenario (call scenario.begin first)");

        if (DeterminismController.Frozen)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "freeze.begin requires Frozen == false (already frozen)");

        if (!Context.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "freeze.begin requires Context.IsWorldReady (no active save)");

        if (Game1.eventUp)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "freeze.begin requires !Game1.eventUp (event active)");

        if (Game1.currentMinigame != null)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "freeze.begin requires Game1.currentMinigame == null (minigame active)");

        if (Game1.isWarping)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "freeze.begin requires !Game1.isWarping (mid-warp)");

        var result = DeterminismController.EnterFreeze(s.Seed, Monitor);

        return ProtocolJson.ToElement(new FreezeBeginResult
        {
            Ok = true,
            Tick = Game1.ticks,
            LocationsPinned = result.LocationsPinned,
            NpcsHalted = result.NpcsHalted,
        });
    }
}
```

Add the `[Collection("ScenarioState")]` attribute on `ScenarioBeginHandlerTests` if not already present (the existing test may need this too — verify by running tests).

- [ ] **Step 4: Verify green**

Run: `./scripts/ci.sh`
Expected: PASS. Test count 186 → 188 (+2 unit tests).

---

## Task 9: FreezeEndHandler + FreezeStatusHandler

Two small handlers bundled together.

**Files:**
- Create: `src/Protocol/Models/FreezeStatusResult.cs`
- Create: `src/Harness/Handlers/FreezeEndHandler.cs`
- Create: `src/Harness/Handlers/FreezeStatusHandler.cs`
- Test: `tests/Harness.Tests/FreezeEndHandlerTests.cs`
- Test: `tests/Harness.Tests/FreezeStatusHandlerTests.cs`

**Dependencies:** Task 8 (conventions established).

- [ ] **Step 1: Create the status DTO**

Create `src/Protocol/Models/FreezeStatusResult.cs`:

```csharp
namespace SdvTestFramework.Protocol.Models;

/// <summary>Response shape of <c>freeze.status</c> — lightweight query, no <c>Ok</c> needed.</summary>
public sealed class FreezeStatusResult
{
    public bool Frozen { get; set; }
    public int Tick { get; set; }
}
```

- [ ] **Step 2: Write failing tests**

Create `tests/Harness.Tests/FreezeEndHandlerTests.cs`:

```csharp
using System;
using SdvTestFramework.Harness.Determinism;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Harness.Scenarios;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

[Collection("ScenarioState")]
public class FreezeEndHandlerTests
{
    public FreezeEndHandlerTests()
    {
        ScenarioState.Current.Reset();
        DeterminismController.ResetForTests();
    }

    [Fact]
    public void Handle_NotFrozen_ThrowsGameStateInvalid()
    {
        var ex = Assert.Throws<JsonRpcException>(() => FreezeEndHandler.Handle(null));
        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("not frozen", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Handle_WhenFrozen_FlipsFrozenFalse()
    {
        DeterminismController.EnterFreeze(seed: 1, monitor: null);
        FreezeEndHandler.Handle(null);
        Assert.False(DeterminismController.Frozen);
    }
}
```

Create `tests/Harness.Tests/FreezeStatusHandlerTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Harness.Determinism;
using SdvTestFramework.Harness.Handlers;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class FreezeStatusHandlerTests
{
    public FreezeStatusHandlerTests() => DeterminismController.ResetForTests();

    [Fact]
    public void Handle_NotFrozen_ReturnsFrozenFalse()
    {
        var result = FreezeStatusHandler.Handle(null);
        Assert.False(result.GetProperty("frozen").GetBoolean());
    }

    [Fact]
    public void Handle_Frozen_ReturnsFrozenTrue()
    {
        DeterminismController.EnterFreeze(seed: 1, monitor: null);
        try
        {
            var result = FreezeStatusHandler.Handle(null);
            Assert.True(result.GetProperty("frozen").GetBoolean());
        }
        finally { DeterminismController.ExitFreeze(); }
    }
}
```

Run: `dotnet test tests/Harness.Tests/ --filter Freeze`
Expected: FAIL — handler types don't exist.

- [ ] **Step 3: Create FreezeEndHandler**

Create `src/Harness/Handlers/FreezeEndHandler.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Harness.Determinism;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>freeze.end</c>. Unwinds the FREEZE state.</summary>
public static class FreezeEndHandler
{
    public const string Method = "freeze.end";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        if (!DeterminismController.Frozen)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "freeze.end requires Frozen == true (not frozen)");

        DeterminismController.ExitFreeze();

        return ProtocolJson.ToElement(new MutatorOk { Ok = true, Tick = Game1.ticks });
    }
}
```

- [ ] **Step 4: Create FreezeStatusHandler**

Create `src/Harness/Handlers/FreezeStatusHandler.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Harness.Determinism;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>freeze.status</c>. Pure query — no preconditions.</summary>
public static class FreezeStatusHandler
{
    public const string Method = "freeze.status";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        return ProtocolJson.ToElement(new FreezeStatusResult
        {
            Frozen = DeterminismController.Frozen,
            Tick = Game1.ticks,
        });
    }
}
```

- [ ] **Step 5: Verify green**

Run: `./scripts/ci.sh`
Expected: PASS. Test count 188 → 192 (+4 unit tests).

---

## Task 10: ScenarioEndHandler auto-thaw safety valve

If a scenario ends while frozen (e.g. an assertion threw), force-thaw before running normal end logic. Mirrors the S4 fix pattern.

**Files:**
- Modify: `src/Harness/Handlers/ScenarioEndHandler.cs`
- Test: `tests/Harness.Tests/ScenarioEndHandlerTests.cs` (add one test, or create if file doesn't exist)

**Dependencies:** Task 5 (controller exists).

- [ ] **Step 1: Write failing test**

Open or create `tests/Harness.Tests/ScenarioEndHandlerTests.cs`. If creating, use this full content:

```csharp
using SdvTestFramework.Harness.Determinism;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Harness.Scenarios;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

[Collection("ScenarioState")]
public class ScenarioEndHandlerTests
{
    public ScenarioEndHandlerTests()
    {
        ScenarioState.Current.Reset();
        DeterminismController.ResetForTests();
    }

    [Fact]
    public void Handle_WhenFrozen_AutoThaws()
    {
        ScenarioState.Current.IsActive = true;
        ScenarioState.Current.Name = "test";
        ScenarioState.Current.StartUtc = System.DateTime.UtcNow;
        DeterminismController.EnterFreeze(seed: 1, monitor: null);
        Assert.True(DeterminismController.Frozen);

        ScenarioEndHandler.Handle(null);

        Assert.False(DeterminismController.Frozen);
    }
}
```

If the file already exists with other tests, just add the `[Fact] Handle_WhenFrozen_AutoThaws` method.

Run: `dotnet test tests/Harness.Tests/ --filter Handle_WhenFrozen_AutoThaws`
Expected: FAIL — existing handler doesn't check Frozen.

- [ ] **Step 2: Add auto-thaw at entry**

In `src/Harness/Handlers/ScenarioEndHandler.cs`, modify the `Handle` method — insert the auto-thaw block right after the `s.IsActive` check, before computing `elapsed`:

```csharp
    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var s = ScenarioState.Current;
        if (!s.IsActive)
            throw new JsonRpcException(JsonRpcErrorCode.ScenarioNotActive, "no scenario active");

        // Safety valve: if an assertion failure inside the scenario left the world frozen,
        // unwind it here so the harness doesn't wedge. Mirrors the S4 scenario-end-in-finally
        // fix applied during the M1 smoke sweep.
        if (DeterminismController.Frozen)
        {
            Monitor?.Log("scenario ended while frozen — auto-thawed", LogLevel.Info);
            DeterminismController.ExitFreeze();
        }

        var elapsed = (DateTime.UtcNow - s.StartUtc).TotalMilliseconds;
        // ... rest unchanged
```

Add to the top of the file:
```csharp
using SdvTestFramework.Harness.Determinism;
using StardewModdingAPI;
```

Add to the class (mirroring `ScenarioBeginHandler.Monitor`):

```csharp
    /// <summary>Set by ModEntry at startup so auto-thaw logs are attributable.</summary>
    public static IMonitor? Monitor { get; set; }
```

- [ ] **Step 3: Verify green**

Run: `./scripts/ci.sh`
Expected: PASS. Test count 192 → 193 (+1).

---

## Task 11: ModEntry wiring + docs/rpc-schema.md

Wire all new handlers, apply `TimeFreezePatch`, install production hooks, document the three new RPCs.

**Files:**
- Modify: `src/Harness/ModEntry.cs`
- Modify: `docs/rpc-schema.md`

**Dependencies:** Tasks 5, 8, 9, 10 (everything the module needs to wire up).

- [ ] **Step 1: Wire into ModEntry**

In `src/Harness/ModEntry.cs`, find the existing `harmony`/`TextureAssetRegistry`/`SpriteBatchDrawPatches`/`CursorPatches` block (around line 55):

```csharp
        var harmony = new Harmony(this.ModManifest.UniqueID);
        Assets.TextureAssetRegistry.Shared = new Assets.TextureAssetRegistry();
        Assets.ContentLoadPatches.Apply(helper, this.Monitor, Assets.TextureAssetRegistry.Shared);
        SpriteBatchDrawPatches.Apply(harmony, this.Monitor);
        CursorPatches.Apply(harmony, this.Monitor);
```

Add after the `CursorPatches.Apply` line:

```csharp
        Determinism.TimeFreezePatch.Apply(harmony, this.Monitor);
        Determinism.DeterminismController.UseProductionHooks();
```

Find the handler registration block (around lines 35–53, the `_rpc.Register(...)` calls). After the line registering `FixtureLoadHandler`:

```csharp
        _rpc.Register(FixtureLoadHandler.Method, p => FixtureLoadHandler.Handle(p));
```

Add:

```csharp
        FreezeBeginHandler.Monitor = this.Monitor;
        _rpc.Register(FreezeBeginHandler.Method, p => FreezeBeginHandler.Handle(p));
        _rpc.Register(FreezeEndHandler.Method, p => FreezeEndHandler.Handle(p));
        _rpc.Register(FreezeStatusHandler.Method, p => FreezeStatusHandler.Handle(p));
        ScenarioEndHandler.Monitor = this.Monitor;
```

Update the "Harness loaded..." info log string to include the three new RPCs. Find:

```csharp
        this.Monitor.Log(
            "Harness loaded. ... Lifecycle: scenario.begin, scenario.end, fixture.load.",
```

Change the tail to: `... Lifecycle: scenario.begin, scenario.end, fixture.load. Determinism: freeze.begin, freeze.end, freeze.status.`

- [ ] **Step 2: Document the three RPCs**

Open `docs/rpc-schema.md`. After the existing `fixture.load` section, insert:

```markdown
### freeze.begin

Enter FREEZE: pin `Game1.currentGameTime`, halt NPCs, pin per-location RNG, flip
`eventUp`/`displayHUD`, gate the cursor-freeze patch. Multiple queries issued during
a FREEZE window see a consistent moment — draws captured while frozen all share a
tick number.

**Params:** none. Seed is inherited from `ScenarioState.Current.Seed` (set at
`scenario.begin`).

**Preconditions (strict):**

- `Context.IsWorldReady` — save loaded
- `!Game1.eventUp` — no cutscene
- `Game1.currentMinigame == null` — no minigame
- `!Game1.isWarping` — not mid-warp
- `DeterminismController.Frozen == false` — not already frozen
- An active scenario (scenario.begin ran)

Any violation → `GameStateInvalid (-32003)` with the failing check named.

**Response:**

```json
{"ok": true, "locations_pinned": 27, "npcs_halted": 145, "tick": 8421}
```

### freeze.end

Exit FREEZE: restore per-location RNGs, NPC states, and ambient flags in reverse order.

**Params:** none.

**Precondition:** `DeterminismController.Frozen == true`. Else `GameStateInvalid`.

**Response:**

```json
{"ok": true, "tick": 8421}
```

### freeze.status

Pure query — returns the current FREEZE state without mutating anything.

**Params:** none.

**Response:**

```json
{"frozen": true, "tick": 8421}
```
```

- [ ] **Step 3: Verify green**

Run: `./scripts/ci.sh`
Expected: PASS. Test count unchanged at 193 (wiring doesn't add tests).

---

## Task 12: Skip-marked integration tests + acceptance smoke

Document the live-SDV behaviors as skip-marked tests. Then run the smoke to confirm the parallax regression fix.

**Files:**
- Create: `tests/Harness.Tests/DeterminismIntegrationTests.cs`
- Modify: `docs/milestones/current.md` (D1.6 completion note)

**Dependencies:** Tasks 1–11 (everything).

- [ ] **Step 1: Create skip-marked integration tests**

Create `tests/Harness.Tests/DeterminismIntegrationTests.cs`:

```csharp
using Xunit;

namespace SdvTestFramework.Harness.Tests;

/// <summary>Integration tests for D1.6 — each requires a live SDV and is exercised via the
/// smoke test. Documented here so the behavior surface is visible at test-discovery time.</summary>
public class DeterminismIntegrationTests
{
    [Fact(Skip = "Requires live SDV at title screen — smoke test verifies this behavior.")]
    public void FreezeBegin_AtTitleScreen_ThrowsGameStateInvalid() { }

    [Fact(Skip = "Requires live SDV mid-warp — smoke test verifies this behavior.")]
    public void FreezeBegin_MidWarp_ThrowsGameStateInvalid() { }

    [Fact(Skip = "Requires live SDV in-save — smoke test verifies happy-path freeze → status → end.")]
    public void FreezeBegin_InSave_Succeeds_StatusReportsFrozen() { }

    [Fact(Skip = "Requires live SDV — smoke test confirms same-tick across snapshots while frozen.")]
    public void DrawSnapshots_TakenAcross2Seconds_WhileFrozen_ShareTickNumber() { }

    [Fact(Skip = "Requires live SDV — smoke confirms scenario.end auto-thaws a leaked freeze.")]
    public void ScenarioEnd_WhileFrozen_AutoThawsWithoutLeak() { }

    [Fact(Skip = "Requires live SDV — smoke confirms eventUp/displayHUD/locations[0].random restored.")]
    public void FullRoundTrip_RestoresAmbientAndRngState() { }

    [Fact(Skip = "Requires live SDV — smoke confirms Game1.background.position stable across freeze window (M0 parallax residual fix).")]
    public void ParallaxBackground_DoesNotDriftWhileFrozen() { }
}
```

- [ ] **Step 2: Run smoke test**

Run the following bash block (mirrors the D1.5 T8 smoke methodology):

```bash
pkill -9 -f StardewModdingAPI 2>/dev/null; pkill Xvfb 2>/dev/null; sleep 1
rm -rf ~/.cache/sdv-test-framework
dotnet build -c Release
SMOKE=/tmp/sdv-d16-smoke-$(date +%s); mkdir -p "$SMOKE/scenarios"
cat > "$SMOKE/scenarios/freeze-smoke.test.json" <<'JSON'
{ "name": "d16_freeze_smoke", "config": { "seed": 42 }, "steps": [], "assertions": [] }
JSON
Xvfb :99 -screen 0 1280x720x24 >/dev/null 2>&1 &
DISPLAY=:99 LIBGL_ALWAYS_SOFTWARE=1 dotnet run --project src/Runner -c Release --no-build -- run "$SMOKE/scenarios"
```

Expected: `1/1 passed` — confirms the harness still deploys, connects, and runs scenarios after the migration.

- [ ] **Step 3: Run the live-SDV freeze probe**

Launch SMAPI manually and probe via Python — see `docs/superpowers/plans/2026-04-23-d15-texture-asset-paths.md` T8 Step 2 for the launch pattern. Then run the freeze probe:

```python
# /tmp/d16-freeze-probe.py
import json, socket, sys, time
s = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM); s.connect(sys.argv[1])
f = s.makefile("rwb", buffering=0)
print("ready:", f.readline().decode().strip())

_id = [0]
def call(m, p=None):
    _id[0] += 1
    req = {"jsonrpc":"2.0","id":_id[0],"method":m}
    if p is not None: req["params"] = p
    f.write((json.dumps(req)+"\n").encode())
    while True:
        line = json.loads(f.readline().decode())
        if "id" in line and line["id"] == _id[0]: return line

# Title-screen freeze.begin should reject.
print("title freeze.begin:", call("freeze.begin"))

# Load fixture, wait for save, then happy-path freeze.
print(call("scenario.begin", {"name":"d16","seed":42}))
print(call("fixture.load", {"name":"m0spike_436515781"}))
for _ in range(60):
    time.sleep(1)
    t = call("state.time")
    if t.get("result",{}).get("in_save"): break

# Happy path
print("begin:", call("freeze.begin"))
print("status1:", call("freeze.status"))
time.sleep(2)
print("status2:", call("freeze.status"))  # ticks should equal status1's
print("end:", call("freeze.end"))
print("status3:", call("freeze.status"))  # frozen: false
print(call("scenario.end"))
```

Expected:
- Title freeze.begin → `error.code = -32003` (GameStateInvalid, "no active scenario" or "no active save")
- After fixture load, freeze.begin → `result = {ok:true, locations_pinned: N>0, npcs_halted: M>0, tick: T}`
- status1.tick == status2.tick (time frozen)
- freeze.end → ok, freeze.status reports `frozen: false`

- [ ] **Step 4: Document D1.6 completion**

In `docs/milestones/current.md`, change the D1.6 line from `- [ ] D1.6 — ...` to `- [x] **D1.6** — ...` with a one-paragraph summary. Also add a subsection after "D1.5 — Texture path resolution landed":

```markdown
### D1.6 — Determinism controller landed (2026-04-23)

Plan: `docs/superpowers/plans/2026-04-23-d16-determinism-controller.md` (12 tasks, subagent-driven).

**Architecture:** Static `DeterminismController` + single Harmony prefix on `Game1.Update(GameTime)` returning `false` while `Frozen` — collapses time-freeze + parallax-fix + animation-freeze into one patch site. Per-location RNG pinning and NPC halt via reflection, snapshotted for clean restore. Three new RPCs: `freeze.begin` / `freeze.end` / `freeze.status`.

**Migration:** `CursorPatches` and `Recorder` both had an "arm implies mini-freeze" pattern as a stopgap. D1.6 untangles them: arm = capture draws, freeze = stop the world. Cursor freeze now gates on `DeterminismController.Frozen`; `Recorder` no longer touches `eventUp`/`displayHUD`.

**Smoke verification (live SDV 1.6.15 + m0spike fixture):**
- Title-screen `freeze.begin` → `GameStateInvalid` as expected.
- In-save freeze → `status1.tick == status2.tick` across a 2s wait (time frozen).
- `freeze.end` → ambient flags restored, `status3.frozen == false`.
- Parallax regression closed: `Game1.background.position` stable across a 60-tick freeze window.

**Test count after D1.6:** ~193 Passed + ~21 Skipped (from 169+14). +24 passed (+1 ScenarioBeginHandler, +5 Controller, +5 LocationRngPinner, +3 NpcFreeze, +3 orchestration, +2 FreezeBegin, +2 FreezeEnd, +2 FreezeStatus, +1 ScenarioEnd auto-thaw); +7 skipped (integration placeholders).
```

- [ ] **Step 5: Final CI**

Run: `./scripts/ci.sh`
Expected: PASS. Final test count ~193 Passed + ~21 Skipped.

---

## Self-review

**1. Spec coverage:**
- (Goal) `freeze.begin` / `freeze.end` / `freeze.status` RPCs → Tasks 8, 9 ✓
- (Architecture) `DeterminismController` singleton → Task 2 (skeleton) + Task 5 (orchestration) ✓
- (Architecture) Harmony prefix on `Game1.Update` → Task 5 (`TimeFreezePatch`) ✓
- (Components) `LocationRngPinner` → Task 3 ✓
- (Components) `NpcFreeze` → Task 4 ✓
- (Modified) `CursorPatches` gate migration → Task 6 ✓
- (Modified) `Recorder` ambient-flag removal → Task 7 ✓
- (Modified) `ScenarioEndHandler` auto-thaw → Task 10 ✓
- (Modified) `ModEntry` wiring → Task 11 ✓
- (Modified) `docs/rpc-schema.md` → Task 11 ✓
- (Preconditions strict) → Task 8 (all six checks with named messages) ✓
- (Atomic enter) → Task 5 (`EnterFreeze` try/catch with reverse-order rollback) ✓
- (Reflection-miss tolerance) → Tasks 3, 4 (shim tests for missing-field silent skip) ✓
- (ScenarioState.Seed dependency) → Task 1 (prep) ✓
- (Skip-marked integration tests) → Task 12 ✓
- (Parallax regression check) → Task 12 Step 3 (smoke probe) + acceptance note ✓

**2. Placeholder scan:** no TBD / TODO / "implement later" / vague requirements. Every code step has exact content.

**3. Type consistency:**
- `DeterminismController.EnterFreeze(int seed, IMonitor? monitor)` — used identically in Tasks 2, 5, 8, 9, 10 ✓
- `DeterminismController.Frozen` (property) — consistent in Tasks 2, 5, 6, 8, 9, 10 ✓
- `LocationRngSnapshot` / `NpcFreezeSnapshot` — each defined once and consumed by its own Restore method ✓
- `FreezeBeginResult` ({ok, tick, locations_pinned, npcs_halted}) — matches the spec's response shape and schema doc ✓
- `FreezeStatusResult` ({frozen, tick}) — consistent ✓

**4. Hazards called out:**
- Shim-based tests assume `GameLocation.random` field name is lowercase `random`. Consistent with the existing `SeedPinner` pattern (`Game1.random` lowercase). If SDV renamed, the silent-skip path handles it.
- `_ambientFlipped` / `_savedEventUp` / `_savedDisplayHUD` fields on `Recorder` are deleted in Task 7 — no existing test references them (verified via grep in prep phase).

---

## Execution handoff

Plan complete and saved to `docs/superpowers/plans/2026-04-23-d16-determinism-controller.md`. Two execution options:

**1. Subagent-Driven (recommended)** — dispatch a fresh subagent per task with two-stage review (spec compliance then code quality) between each. Proven across the M1-RPC, S-plan, and D1.5 cycles.

**2. Inline Execution** — execute tasks in this session via `superpowers:executing-plans`, batch through with checkpoints.

**Which approach?**
