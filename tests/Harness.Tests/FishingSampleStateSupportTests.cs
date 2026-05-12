using System;
using SdvTestFramework.Harness.Handlers;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class FishingSampleStateSupportTests
{
    [Fact]
    public void RandomOverride_WithoutSeed_UsesDisposableRandomAndRestoresUnadvancedOriginal()
    {
        var original = new Random(42);
        var expectedNext = new Random(42).Next();
        Random? current = original;

        var scope = FishingRandomOverride.CaptureAndApply(
            seed: null,
            restoreState: true,
            get: () => current,
            set: value => current = value);

        Assert.NotSame(original, current);
        _ = current!.Next();

        scope.Restore();

        Assert.Same(original, current);
        Assert.Equal(expectedNext, current!.Next());
    }

    [Fact]
    public void RandomOverride_WithSeed_UsesSeededRandomAndRestoresOriginal()
    {
        var original = new Random(42);
        Random? current = original;

        var scope = FishingRandomOverride.CaptureAndApply(
            seed: 1234,
            restoreState: true,
            get: () => current,
            set: value => current = value);

        Assert.Equal(new Random(1234).Next(), current!.Next());

        scope.Restore();

        Assert.Same(original, current);
    }

    [Fact]
    public void AttachmentSnapshots_RestoreOriginalAndSelectedAttachmentCollections()
    {
        var original = new FakeAttachments("original-bait", "original-tackle");
        var selected = new FakeAttachments("selected-bait", "selected-tackle");
        var snapshots = new FishingAttachmentSnapshots();

        snapshots.Capture(original);
        snapshots.Capture(selected);
        selected[0] = "mutated-bait";
        selected[1] = "mutated-tackle";

        snapshots.Restore();

        Assert.Equal("original-bait", original[0]);
        Assert.Equal("original-tackle", original[1]);
        Assert.Equal("selected-bait", selected[0]);
        Assert.Equal("selected-tackle", selected[1]);
    }

    private sealed class FakeAttachments
    {
        private readonly object?[] _items;

        public FakeAttachments(object? bait, object? tackle)
        {
            _items = [bait, tackle];
        }

        public object? this[int index]
        {
            get => _items[index];
            set => _items[index] = value;
        }
    }
}
