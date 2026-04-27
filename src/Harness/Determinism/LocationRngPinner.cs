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
            // string.GetHashCode() is randomized per-process in .NET 5+. Within a single SDV
            // run this stays deterministic, which is what M1 scenarios assert on. If a future
            // scenario asserts on a specific per-location RNG draw across process boundaries
            // (e.g. "fish pond contains X fish on day N"), replace with a stable hash.
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
