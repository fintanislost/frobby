using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class TextDrawFilterTests
{
    private static TextDrawEvent Event(string text = "STARBERG TERMINAL", int x = 64, int y = 48, Color? color = null, float z = 0.5f)
        => new()
        {
            Text = text,
            Position = new Vector2(x, y),
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
