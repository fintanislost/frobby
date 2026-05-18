using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class TextDrawFilterSerializationTests
{
    [Fact]
    public void Serialize_UsesSnakeCaseNames()
    {
        var filter = new TextDrawFilter
        {
            TextContains = "CASH",
            TextEquals = "STARBERG TERMINAL v0.1.0",
            TextMatches = "^[0-9][0-9,]*$",
            CaseSensitive = false,
            InRect = new[] { 0, 0, 320, 180 },
            BoundsWithinRect = new[] { 10, 10, 300, 160 },
            BoundsIntersectsRect = new[] { 20, 20, 120, 60 },
            Color = new[] { 255, 176, 0, 255 },
            ColorAny = new[] { new[] { 255, 214, 128, 255 }, new[] { 236, 229, 206, 255 } },
            LayerDepthRange = new[] { 0.9f, 1.0f },
            DisarmAfterSnapshot = true,
        };

        var json = JsonSerializer.Serialize(filter, ProtocolJson.Options);

        Assert.Contains("\"text_contains\":\"CASH\"", json);
        Assert.Contains("\"text_equals\":\"STARBERG TERMINAL v0.1.0\"", json);
        Assert.Contains("\"text_matches\":\"^[0-9][0-9,]*$\"", json);
        Assert.Contains("\"case_sensitive\":false", json);
        Assert.Contains("\"in_rect\":[0,0,320,180]", json);
        Assert.Contains("\"bounds_within_rect\":[10,10,300,160]", json);
        Assert.Contains("\"bounds_intersects_rect\":[20,20,120,60]", json);
        Assert.Contains("\"color\":[255,176,0,255]", json);
        Assert.Contains("\"color_any\":[[255,214,128,255],[236,229,206,255]]", json);
        Assert.Contains("\"layer_depth_range\":[0.9,1]", json);
        Assert.Contains("\"disarm_after_snapshot\":true", json);
    }

    [Fact]
    public void Deserialize_DefaultsCaseSensitiveToTrue()
    {
        var filter = JsonSerializer.Deserialize<TextDrawFilter>(
            "{\"text_contains\":\"cash\"}",
            ProtocolJson.Options)!;

        Assert.True(filter.CaseSensitive);
        Assert.Equal("cash", filter.TextContains);
    }

    [Fact]
    public void Deserialize_AcceptsBoundsWithinRect()
    {
        var filter = JsonSerializer.Deserialize<TextDrawFilter>(
            "{\"bounds_within_rect\":[100,50,200,80]}",
            ProtocolJson.Options)!;

        Assert.Equal(new[] { 100, 50, 200, 80 }, filter.BoundsWithinRect);
    }

    [Fact]
    public void Deserialize_AcceptsBoundsIntersectsRect()
    {
        var filter = JsonSerializer.Deserialize<TextDrawFilter>(
            "{\"bounds_intersects_rect\":[100,50,200,80]}",
            ProtocolJson.Options)!;

        Assert.Equal(new[] { 100, 50, 200, 80 }, filter.BoundsIntersectsRect);
    }

    [Fact]
    public void Deserialize_AcceptsColorAny()
    {
        var filter = JsonSerializer.Deserialize<TextDrawFilter>(
            "{\"color_any\":[[255,214,128,255],[236,229,206,255]]}",
            ProtocolJson.Options)!;

        Assert.NotNull(filter.ColorAny);
        Assert.Equal(new[] { 255, 214, 128, 255 }, filter.ColorAny![0]);
        Assert.Equal(new[] { 236, 229, 206, 255 }, filter.ColorAny![1]);
    }

    [Fact]
    public void Deserialize_AcceptsDisarmAfterSnapshot()
    {
        var filter = JsonSerializer.Deserialize<TextDrawFilter>(
            "{\"disarm_after_snapshot\":true}",
            ProtocolJson.Options)!;

        Assert.True(filter.DisarmAfterSnapshot);
    }
}
