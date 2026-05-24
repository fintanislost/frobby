using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SdvTestFramework.Harness.Determinism;
using StardewModdingAPI;
using StardewValley;

namespace SdvTestFramework.Harness.Patches;

/// <summary>Force the mouse cursor to (0,0) while frozen, so hover-driven UI doesn't jitter.</summary>
// Patch: Game1.getMouseX() / Game1.getMouseY() / Game1.getOldMouseX() / Game1.getOldMouseY()
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
        PatchOne(harmony, monitor, "getMouseX", nameof(ReturnX), typeof(bool));
        PatchOne(harmony, monitor, "getMouseY", nameof(ReturnY), typeof(bool));
        PatchOne(harmony, monitor, "getMouseX", nameof(ReturnX));
        PatchOne(harmony, monitor, "getMouseY", nameof(ReturnY));
        PatchOne(harmony, monitor, "getOldMouseX", nameof(ReturnOldX), typeof(bool));
        PatchOne(harmony, monitor, "getOldMouseY", nameof(ReturnOldY), typeof(bool));
        PatchOne(harmony, monitor, "getOldMouseX", nameof(ReturnOldX));
        PatchOne(harmony, monitor, "getOldMouseY", nameof(ReturnOldY));
    }

    private static void PatchOne(Harmony harmony, IMonitor monitor, string name, string postfixName, params Type[] sig)
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
            postfixName, BindingFlags.Static | BindingFlags.NonPublic));
        harmony.Patch(target, postfix: postfix);
    }

    internal static int ResolveX(int current)
    {
        return ControlledCursor.TryGet(out var x, out _)
            ? x
            : DeterminismController.Frozen
                ? 0
                : current;
    }

    internal static int ResolveY(int current)
    {
        return ControlledCursor.TryGet(out _, out var y)
            ? y
            : DeterminismController.Frozen
                ? 0
                : current;
    }

    internal static int ResolveOldX(int current) => ResolveX(current);

    internal static int ResolveOldY(int current) => ResolveY(current);

    private static void ReturnX(ref int __result) => __result = ResolveX(__result);

    private static void ReturnY(ref int __result) => __result = ResolveY(__result);

    private static void ReturnOldX(ref int __result) => __result = ResolveOldX(__result);

    private static void ReturnOldY(ref int __result) => __result = ResolveOldY(__result);
}
