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
