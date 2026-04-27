using System.Reflection;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;

namespace SdvTestFramework.SpikeHarness.Patches;

/// <summary>Force the mouse cursor to (0,0) while armed, so hover-driven UI doesn't jitter.</summary>
// Patch: Game1.getMouseX() / Game1.getMouseY()
// Type: Postfix (rewrites return value)
// Why: Cursor-sensitive draws (hover tooltips, button highlights) are a nondeterminism
//      source. Forcing cursor to (0,0) during capture is the blunt-but-effective treatment
//      per .claude/rules/determinism.md §Cursor.
// Rollback: Remove the Apply() call from ModEntry. Cursor returns to OS-reported position.
// Tested in: docs/spikes/2026-04-determinism/scratch/run.sh (integration only; no xUnit for spike)
// Depends on: Harmony 2.x, SMAPI >= 4.1.10, SDV 1.6.x (method names confirmed in that range)
internal static class CursorPatches
{
    public static void Apply(Harmony harmony, IMonitor monitor)
    {
        PatchOne(harmony, monitor, "getMouseX", typeof(bool));
        PatchOne(harmony, monitor, "getMouseY", typeof(bool));
        PatchOne(harmony, monitor, "getMouseX"); // zero-arg variant on some SDV versions
        PatchOne(harmony, monitor, "getMouseY");
    }

    private static void PatchOne(Harmony harmony, IMonitor monitor, string name, params System.Type[] sig)
    {
        var target = sig.Length > 0
            ? AccessTools.Method(typeof(Game1), name, sig)
            : AccessTools.Method(typeof(Game1), name, System.Array.Empty<System.Type>());
        if (target == null)
        {
            monitor.Log($"Game1.{name}({string.Join(",", System.Linq.Enumerable.Select(sig, t => t.Name))}) not found — skipping cursor patch.", LogLevel.Trace);
            return;
        }
        var postfix = new HarmonyMethod(typeof(CursorPatches).GetMethod(
            nameof(ReturnZero), BindingFlags.Static | BindingFlags.NonPublic));
        harmony.Patch(target, postfix: postfix);
    }

    private static void ReturnZero(ref int __result)
    {
        if (Recording.Recorder.IsArmed) __result = 0;
    }
}
