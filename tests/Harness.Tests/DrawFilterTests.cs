using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class DrawFilterTests
{
    private static DrawEvent Event(Rectangle dest, Color? col = null, float z = 0f, Rectangle? src = null)
        => new()
        {
            DestRect = dest,
            Color = col ?? Color.White,
            LayerDepth = z,
            SourceRect = src,
        };

    [Fact]
    public void EmptyFilter_MatchesEverything()
    {
        Assert.True(DrawFilterMatcher.Matches(Event(new Rectangle(0, 0, 10, 10)), new DrawFilter()));
    }

    [Fact]
    public void ColorMismatch_Rejects()
    {
        var f = new DrawFilter { Color = new[] { 255, 0, 0, 255 } };
        Assert.False(DrawFilterMatcher.Matches(Event(default, Color.White), f));
    }

    [Fact]
    public void ColorExact_Accepts()
    {
        var f = new DrawFilter { Color = new[] { 255, 255, 255, 255 } };
        Assert.True(DrawFilterMatcher.Matches(Event(default, Color.White), f));
    }

    [Fact]
    public void InRect_ContainmentChecked()
    {
        var f = new DrawFilter { InRect = new[] { 0, 0, 100, 100 } };
        Assert.True(DrawFilterMatcher.Matches(Event(new Rectangle(10, 10, 50, 50)), f));
        Assert.False(DrawFilterMatcher.Matches(Event(new Rectangle(90, 90, 50, 50)), f));
    }

    [Fact]
    public void LayerDepthRange_Inclusive()
    {
        var f = new DrawFilter { LayerDepthRange = new[] { 0.5f, 1.0f } };
        Assert.False(DrawFilterMatcher.Matches(Event(default, z: 0.4f), f));
        Assert.True(DrawFilterMatcher.Matches(Event(default, z: 0.5f), f));
        Assert.True(DrawFilterMatcher.Matches(Event(default, z: 1.0f), f));
    }

    [Fact]
    public void SourceRect_ExactMatch()
    {
        var f = new DrawFilter { SourceRect = new[] { 0, 0, 16, 16 } };
        Assert.True(DrawFilterMatcher.Matches(Event(default, src: new Rectangle(0, 0, 16, 16)), f));
        Assert.False(DrawFilterMatcher.Matches(Event(default, src: new Rectangle(0, 0, 17, 16)), f));
    }

    [Fact]
    public void SourceRect_FilterSet_EventNull_Rejects()
    {
        var f = new DrawFilter { SourceRect = new[] { 0, 0, 16, 16 } };
        Assert.False(DrawFilterMatcher.Matches(Event(default, src: null), f));
    }

    [Fact]
    public void MultipleFilters_AndTogether()
    {
        var f = new DrawFilter
        {
            Color = new[] { 255, 255, 255, 255 },
            LayerDepthRange = new[] { 0f, 1f },
        };
        Assert.True(DrawFilterMatcher.Matches(Event(default, Color.White, z: 0.5f), f));
        Assert.False(DrawFilterMatcher.Matches(Event(default, Color.Red, z: 0.5f), f));   // color fails
        Assert.False(DrawFilterMatcher.Matches(Event(default, Color.White, z: 2f), f));   // depth fails
    }

    [Fact]
    public void TextureAsset_NullRegistry_NoMatch()
    {
        // When Shared is null (e.g. test with no ModEntry), the event's Texture can't be
        // resolved; a filter requiring a texture_asset path must reject.
        var prior = SdvTestFramework.Harness.Assets.TextureAssetRegistry.Shared;
        SdvTestFramework.Harness.Assets.TextureAssetRegistry.Shared = null;
        try
        {
            var e = new DrawEvent { Texture = null };
            var f = new DrawFilter { TextureAsset = "Characters/Abigail" };
            Assert.False(DrawFilterMatcher.Matches(in e, f));
        }
        finally
        {
            SdvTestFramework.Harness.Assets.TextureAssetRegistry.Shared = prior;
        }
    }

    [Fact]
    public void TextureAsset_NullEventTexture_NoMatch()
    {
        // Registry is populated but event has no Texture ref — still no match.
        var reg = new SdvTestFramework.Harness.Assets.TextureAssetRegistry();
        var prior = SdvTestFramework.Harness.Assets.TextureAssetRegistry.Shared;
        SdvTestFramework.Harness.Assets.TextureAssetRegistry.Shared = reg;
        try
        {
            var e = new DrawEvent { Texture = null };
            var f = new DrawFilter { TextureAsset = "Characters/Abigail" };
            Assert.False(DrawFilterMatcher.Matches(in e, f));
        }
        finally
        {
            SdvTestFramework.Harness.Assets.TextureAssetRegistry.Shared = prior;
        }
    }

    [Fact]
    public void TextureAsset_NonIntegerPath_NoLongerThrows()
    {
        // Pre-D1.5 the matcher threw InvalidParams for non-integer values. Post-D1.5, real
        // asset paths are accepted (and simply don't match when nothing resolves).
        var prior = SdvTestFramework.Harness.Assets.TextureAssetRegistry.Shared;
        SdvTestFramework.Harness.Assets.TextureAssetRegistry.Shared = null;
        try
        {
            var e = new DrawEvent { Texture = null };
            var f = new DrawFilter { TextureAsset = "Mods/MyMod/sprites" };
            // Must NOT throw. Return false (no resolved path → no match).
            Assert.False(DrawFilterMatcher.Matches(in e, f));
        }
        finally
        {
            SdvTestFramework.Harness.Assets.TextureAssetRegistry.Shared = prior;
        }
    }

    [Fact]
    public void ContentHash_PrefixMatches_ReturnsTrue()
    {
        var evt = new DrawEvent { ContentHash = "a1b2c3d4e5f6a789" };
        var filter = new DrawFilter { ContentHash = "a1b2c3d4" };
        Assert.True(DrawFilterMatcher.Matches(in evt, filter));
    }

    [Fact]
    public void ContentHash_PrefixMismatch_ReturnsFalse()
    {
        var evt = new DrawEvent { ContentHash = "a1b2c3d4e5f6a789" };
        var filter = new DrawFilter { ContentHash = "f0e0d0c0" };
        Assert.False(DrawFilterMatcher.Matches(in evt, filter));
    }

    [Fact]
    public void TextureSize_ExactMatch_ReturnsTrue()
    {
        var evt = new DrawEvent { TextureSize = new[] { 512, 1002 } };
        var filter = new DrawFilter { TextureSize = new[] { 512, 1002 } };
        Assert.True(DrawFilterMatcher.Matches(in evt, filter));
    }
}
