using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SdvTestFramework.Harness.Determinism;
using StardewModdingAPI;
using StardewValley;

namespace SdvTestFramework.Harness.Patches;

/// <summary>Force the mouse cursor to (0,0) while frozen, so hover-driven UI doesn't jitter.</summary>
// Patch: Game1.getMouseX() / Game1.getMouseY()
// Type: Postfix (rewrites return value)
// Why: Cursor-sensitive draws (hover tooltips, button highlights) are a nondeterminism
//      source. Force-zero during FREEZE per .claude/rules/determinism.md §Cursor.
//      Gated on DeterminismController.Frozen — pre-D1.6 gated on Recorder.IsArmed,
//      but freeze and capture are orthogonal concerns (can freeze without armed).
// Rollback: Remove Apply() call from ModEntry; cursor returns to OS-reported position.
// Tested in: tests/Harness.IntegrationTests/CursorPatchTests.cs (future M1 work)
// Depends on: Harmony 2.x, SMAPI >= 4.1.10, SDV 1.6.x
internal static class CursorPatches
{
    public static void Apply(Harmony harmony, IMonitor monitor)
    {
        // SDV 1.6 has both bool-taking and no-arg variants depending on version.
        PatchOne(harmony, monitor, "getMouseX", typeof(bool));
        PatchOne(harmony, monitor, "getMouseY", typeof(bool));
        PatchOne(harmony, monitor, "getMouseX");
        PatchOne(harmony, monitor, "getMouseY");
    }

    private static void PatchOne(Harmony harmony, IMonitor monitor, string name, params Type[] sig)
    {
        var target = sig.Length > 0
            ? AccessTools.Method(typeof(Game1), name, sig)
            : AccessTools.Method(typeof(Game1), name, Array.Empty<Type>());
        if (target is null)
        {
            monitor.Log(
                $"Game1.{name}({string.Join(",", sig.Select(t => t.Name))}) not found — skipping cursor patch.",
                LogLevel.Trace);
            return;
        }
        var postfix = new HarmonyMethod(typeof(CursorPatches).GetMethod(
            nameof(ReturnZero), BindingFlags.Static | BindingFlags.NonPublic));
        harmony.Patch(target, postfix: postfix);
    }

    private static void ReturnZero(ref int __result)
    {
        if (DeterminismController.Frozen) __result = 0;
    }
}
