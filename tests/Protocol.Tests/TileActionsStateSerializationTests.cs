using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class TileActionsStateSerializationTests
{
    [Fact]
    public void Request_SerializesSnakeCase()
    {
        var req = new TileActionsRequest
        {
            Location = "Custom_BlueMoonVineyard",
            X = 56,
            Y = 48,
            Radius = 2,
            Layers = new() { "Back", "Buildings" },
            Properties = new() { "TouchAction" },
        };

        var json = JsonSerializer.Serialize(req, ProtocolJson.Options);

        Assert.Contains("\"location\":\"Custom_BlueMoonVineyard\"", json);
        Assert.Contains("\"radius\":2", json);
        Assert.Contains("\"layers\":[\"Back\",\"Buildings\"]", json);
        Assert.Contains("\"properties\":[\"TouchAction\"]", json);
    }

    [Fact]
    public void State_SerializesCandidates()
    {
        var state = new TileActionsState
        {
            Location = "Custom_BlueMoonVineyard",
            X = 56,
            Y = 48,
            Radius = 1,
            Actions =
            {
                new TileActionCandidate
                {
                    Tile = new TilePoint { X = 56, Y = 48 },
                    Layer = "Back",
                    Property = "TouchAction",
                    Value = "LoadMap Town 50 114 0",
                    Distance = 0,
                },
            },
        };

        var json = JsonSerializer.Serialize(state, ProtocolJson.Options);

        Assert.Contains("\"location\":\"Custom_BlueMoonVineyard\"", json);
        Assert.Contains("\"actions\":[", json);
        Assert.Contains("\"property\":\"TouchAction\"", json);
        Assert.Contains("\"value\":\"LoadMap Town 50 114 0\"", json);
        Assert.Contains("\"distance\":0", json);
    }
}
