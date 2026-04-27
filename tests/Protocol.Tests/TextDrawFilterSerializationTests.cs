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
            CaseSensitive = false,
            InRect = new[] { 0, 0, 320, 180 },
            Color = new[] { 255, 176, 0, 255 },
            LayerDepthRange = new[] { 0.9f, 1.0f },
        };

        var json = JsonSerializer.Serialize(filter, ProtocolJson.Options);

        Assert.Contains("\"text_contains\":\"CASH\"", json);
        Assert.Contains("\"text_equals\":\"STARBERG TERMINAL v0.1.0\"", json);
        Assert.Contains("\"case_sensitive\":false", json);
        Assert.Contains("\"in_rect\":[0,0,320,180]", json);
        Assert.Contains("\"color\":[255,176,0,255]", json);
        Assert.Contains("\"layer_depth_range\":[0.9,1]", json);
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
}
