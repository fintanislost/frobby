using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class UseToolSerializationTests
{
    [Fact]
    public void UseToolRequest_DeserializesSnakeCase()
    {
        var req = JsonSerializer.Deserialize<UseToolRequest>(
            "{\"tool\":\"Hoe\",\"location\":\"Desert\",\"x\":9,\"y\":43,\"facing\":\"down\",\"power\":0}",
            ProtocolJson.Options)!;

        Assert.Equal("Hoe", req.Tool);
        Assert.Equal("Desert", req.Location);
        Assert.Equal(9, req.X);
        Assert.Equal(43, req.Y);
        Assert.Equal("down", req.Facing);
        Assert.Equal(0, req.Power);
    }

    [Fact]
    public void UseToolResult_SerializesDiagnosticsAsSnakeCase()
    {
        var result = new UseToolResult
        {
            Tick = 123,
            Tool = "Hoe",
            Location = "Desert",
            Tile = new TilePoint { X = 9, Y = 43 },
            SelectedItemId = "Hoe",
            SelectedItemQualifiedId = "(T)Hoe",
            SelectedItemName = "Hoe",
            SelectedItemRuntimeType = "Hoe",
            SelectedToolIndex = 1,
            Invoked = true,
        };

        var json = JsonSerializer.Serialize(result, ProtocolJson.Options);

        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"tick\":123", json);
        Assert.Contains("\"tool\":\"Hoe\"", json);
        Assert.Contains("\"location\":\"Desert\"", json);
        Assert.Contains("\"tile\":{\"x\":9,\"y\":43}", json);
        Assert.Contains("\"selected_tool_index\":1", json);
        Assert.Contains("\"selected_item_runtime_type\":\"Hoe\"", json);
        Assert.Contains("\"invoked\":true", json);
        Assert.DoesNotContain("SelectedToolIndex", json);
    }
}
