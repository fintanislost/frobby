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
        // Inverse actions in exit order: rngs → npcs → ambient. Not the strict reverse
        // of entry — the two data restores are commutative (disjoint state) and landing
        // ambient last keeps the "frozen" flags applied until the very last step.
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
