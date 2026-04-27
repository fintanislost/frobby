using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class TextDrawFilterTests
{
    private static TextDrawEvent Event(
        string text = "STARBERG TERMINAL",
        int x = 64,
        int y = 48,
        int width = 120,
        int height = 24,
        Color? color = null,
        float z = 0.5f)
        => new()
        {
            Text = text,
            Position = new Vector2(x, y),
            Size = new Vector2(width, height),
            Color = color ?? Color.White,
            LayerDepth = z,
        };

    [Fact]
    public void EmptyFilter_MatchesEverything()
    {
        Assert.True(TextDrawFilterMatcher.Matches(Event(), new TextDrawFilter()));
    }

    [Fact]
    public void TextContains_UsesCaseSensitiveDefault()
    {
        Assert.True(TextDrawFilterMatcher.Matches(
            Event("CASH & WIRES"),
            new TextDrawFilter { TextContains = "CASH" }));

        Assert.False(TextDrawFilterMatcher.Matches(
            Event("CASH & WIRES"),
            new TextDrawFilter { TextContains = "cash" }));
    }

    [Fact]
    public void TextContains_CanIgnoreCase()
    {
        Assert.True(TextDrawFilterMatcher.Matches(
            Event("CASH & WIRES"),
            new TextDrawFilter { TextContains = "cash", CaseSensitive = false }));
    }

    [Fact]
    public void TextEquals_MatchesWholeText()
    {
        var filter = new TextDrawFilter { TextEquals = "STARBERG TERMINAL" };

        Assert.True(TextDrawFilterMatcher.Matches(Event("STARBERG TERMINAL"), filter));
        Assert.False(TextDrawFilterMatcher.Matches(Event("STARBERG"), filter));
    }

    [Fact]
    public void PositionMustBeInsideInRect()
    {
        var filter = new TextDrawFilter { InRect = new[] { 60, 40, 20, 20 } };

        Assert.True(TextDrawFilterMatcher.Matches(Event(x: 64, y: 48), filter));
        Assert.False(TextDrawFilterMatcher.Matches(Event(x: 90, y: 48), filter));
    }

    [Fact]
    public void BoundsWithinRect_RequiresFullTextBoundsInsideRect()
    {
        var filter = new TextDrawFilter { BoundsWithinRect = new[] { 60, 40, 140, 40 } };

        Assert.True(TextDrawFilterMatcher.Matches(Event(x: 64, y: 48, width: 120, height: 24), filter));
        Assert.False(TextDrawFilterMatcher.Matches(Event(x: 64, y: 48, width: 160, height: 24), filter));
    }

    [Fact]
    public void BoundsWithinRect_TreatsNegativeEventSizeAsPositiveBounds()
    {
        var filter = new TextDrawFilter { BoundsWithinRect = new[] { 60, 40, 140, 40 } };

        Assert.True(TextDrawFilterMatcher.Matches(Event(x: 64, y: 48, width: -120, height: -24), filter));
    }

    [Fact]
    public void BoundsIntersectsRect_MatchesAnyOverlap()
    {
        var filter = new TextDrawFilter { BoundsIntersectsRect = new[] { 180, 50, 40, 20 } };

        Assert.True(TextDrawFilterMatcher.Matches(Event(x: 64, y: 48, width: 120, height: 24), filter));
        Assert.False(TextDrawFilterMatcher.Matches(Event(x: 64, y: 48, width: 100, height: 24), filter));
    }

    [Theory]
    [InlineData(new int[] { 0, 0, 10 }, "filter.bounds_within_rect must be [x, y, w, h]")]
    [InlineData(new int[] { 0, 0, 10, -1 }, "filter.bounds_within_rect width/height must be >= 0")]
    public void Validate_BoundsWithinRectRejectsInvalidRect(int[] rect, string message)
    {
        var ex = Assert.Throws<SdvTestFramework.Protocol.JsonRpcException>(() =>
            TextDrawFilterMatcher.Validate(new TextDrawFilter { BoundsWithinRect = rect }));

        Assert.Contains(message, ex.Message);
    }

    [Theory]
    [InlineData(new int[] { 0, 0, 10 }, "filter.bounds_intersects_rect must be [x, y, w, h]")]
    [InlineData(new int[] { 0, 0, -1, 10 }, "filter.bounds_intersects_rect width/height must be >= 0")]
    public void Validate_BoundsIntersectsRectRejectsInvalidRect(int[] rect, string message)
    {
        var ex = Assert.Throws<SdvTestFramework.Protocol.JsonRpcException>(() =>
            TextDrawFilterMatcher.Validate(new TextDrawFilter { BoundsIntersectsRect = rect }));

        Assert.Contains(message, ex.Message);
    }

    [Fact]
    public void ColorAndLayerDepthMustMatch()
    {
        var filter = new TextDrawFilter
        {
            Color = new[] { 255, 176, 0, 255 },
            LayerDepthRange = new[] { 0.9f, 1.0f },
        };

        Assert.True(TextDrawFilterMatcher.Matches(Event(color: new Color(255, 176, 0, 255), z: 0.91f), filter));
        Assert.False(TextDrawFilterMatcher.Matches(Event(color: Color.White, z: 0.91f), filter));
        Assert.False(TextDrawFilterMatcher.Matches(Event(color: new Color(255, 176, 0, 255), z: 0.5f), filter));
    }
}
