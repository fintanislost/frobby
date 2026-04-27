using System;
using System.Reflection;
using StardewModdingAPI;
using StardewValley;

namespace SdvTestFramework.Harness.Determinism;

/// <summary>
/// Pins <see cref="Game1.random"/> to a fresh <see cref="Random"/> with a known seed. Per-location
/// RNG handling lives elsewhere (M1 FREEZE controller); this class only touches <c>Game1.random</c>.
/// </summary>
public static class SeedPinner
{
    public static void Pin(int seed, IMonitor monitor)
    {
        var field = typeof(Game1).GetField("random",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (field is null)
            throw new InvalidOperationException(
                "Game1.random field not found — SDV internals have shifted.");

        field.SetValue(null, new Random(seed));

        // Sanity probe + re-seed to cancel out the probe call's consumption.
        var r = (Random)field.GetValue(null)!;
        int probe = r.Next();
        monitor.Log($"Game1.random pinned with seed={seed}. Probe Next()={probe}.", LogLevel.Info);
        field.SetValue(null, new Random(seed));
    }
}
