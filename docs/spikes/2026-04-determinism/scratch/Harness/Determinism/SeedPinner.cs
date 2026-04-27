using System;
using System.Reflection;
using StardewModdingAPI;
using StardewValley;

namespace SdvTestFramework.SpikeHarness.Determinism;

/// <summary>
/// Pins <see cref="Game1.random"/> to a fresh <see cref="Random"/> with a known seed.
/// Spike-level: no per-location RNG handling — see determinism.md §Per-location RNG and the
/// report's <em>What pinning the spike applies</em> section for the deferred work.
/// </summary>
internal static class SeedPinner
{
    public static void Pin(int seed, IMonitor monitor)
    {
        var field = typeof(Game1).GetField("random",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (field is null)
            throw new InvalidOperationException(
                "Game1.random field not found — SDV internals have shifted. Spike must be revised.");

        field.SetValue(null, new Random(seed));

        // Sanity-check: record one value so the report can see that pinning actually bit.
        var r = (Random)field.GetValue(null)!;
        int probe = r.Next();
        monitor.Log($"Game1.random pinned with seed={seed}. Probe Next()={probe}.", LogLevel.Info);

        // Re-seed because the probe consumed a value.
        field.SetValue(null, new Random(seed));
    }
}
