using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class TextDrawEventSnapshotSerializationTests
{
    [Fact]
    public void Serialize_UsesSnakeCaseShape()
    {
        var snap = new TextDrawEventSnapshot
        {
            Events = new()
            {
                new TextDrawEventDto
                {
                    Tick = 101,
                    Call = 7,
                    Text = "STARBERG TERMINAL v0.1.0",
                    X = 64,
                    Y = 48,
                    Width = 180,
                    Height = 24,
                    Color = new[] { 255, 176, 0, 255 },
                    LayerDepth = 0.91f,
                },
            },
            Meta = new TextDrawSnapshotMetadata { Ticks = 30, Events = 1, Dropped = 0 },
        };

        var json = JsonSerializer.Serialize(snap, ProtocolJson.Options);

        Assert.Equal(
            "{\"events\":[{\"tick\":101,\"call\":7,\"text\":\"STARBERG TERMINAL v0.1.0\",\"x\":64,\"y\":48,\"width\":180,\"height\":24,\"color\":[255,176,0,255],\"layer_depth\":0.91}],\"meta\":{\"ticks\":30,\"events\":1,\"dropped\":0}}",
            json);
    }

    [Fact]
    public void Serialize_TextEventDto_EmitsTextSize()
    {
        var dto = new TextDrawEventDto
        {
            Text = "JOJA US Equity",
            X = 100,
            Y = 50,
            Width = 132,
            Height = 24,
            Color = new[] { 255, 255, 255, 255 },
        };

        var json = JsonSerializer.Serialize(dto, ProtocolJson.Options);

        Assert.Equal(
            "{\"tick\":0,\"call\":0,\"text\":\"JOJA US Equity\",\"x\":100,\"y\":50,\"width\":132,\"height\":24,\"color\":[255,255,255,255],\"layer_depth\":0}",
            json);
    }

    [Fact]
    public void Serialize_EmptySnapshot_EmitsEmptyEventsAndDefaultMeta()
    {
        var json = JsonSerializer.Serialize(new TextDrawEventSnapshot(), ProtocolJson.Options);

        Assert.Equal(
            "{\"events\":[],\"meta\":{\"ticks\":0,\"events\":0,\"dropped\":0}}",
            json);
    }
}
