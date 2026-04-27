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
