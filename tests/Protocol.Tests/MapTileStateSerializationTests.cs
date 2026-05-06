using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class MapTileStateSerializationTests
{
    [Fact]
    public void Request_SerializesNullableFieldsAndLayerFilters()
    {
        var req = new MapTileRequest
        {
            Location = "Custom_TownEast",
            X = 10,
            Y = 20,
            Layers = new() { "Back", "Buildings" },
        };

        var json = JsonSerializer.Serialize(req, ProtocolJson.Options);
        var roundTrip = JsonSerializer.Deserialize<MapTileRequest>(json, ProtocolJson.Options);

        Assert.Contains("\"location\":\"Custom_TownEast\"", json);
        Assert.Contains("\"x\":10", json);
        Assert.Contains("\"y\":20", json);
        Assert.Contains("\"layers\":[\"Back\",\"Buildings\"]", json);
        Assert.NotNull(roundTrip);
        Assert.Equal("Custom_TownEast", roundTrip!.Location);
        Assert.Equal(10, roundTrip.X);
        Assert.Equal(20, roundTrip.Y);
        Assert.Equal(new[] { "Back", "Buildings" }, roundTrip.Layers);
    }

    [Fact]
    public void State_SerializesLayerProperties()
    {
        var state = new MapTileState
        {
            Location = "Custom_TownEast",
            X = 10,
            Y = 20,
            Layers = new()
            {
                new MapTileLayerState
                {
                    Name = "Back",
                    TileIndex = 471,
                    TileSheet = "outdoors",
                    Properties = new()
                    {
                        ["TouchAction"] = "MagicWarp Custom_EnchantedGrove",
                        ["Passable"] = "F",
                    },
                },
            },
        };

        var json = JsonSerializer.Serialize(state, ProtocolJson.Options);

        Assert.Contains("\"location\":\"Custom_TownEast\"", json);
        Assert.Contains("\"x\":10", json);
        Assert.Contains("\"y\":20", json);
        Assert.Contains("\"layers\":[{\"name\":\"Back\",\"tile_index\":471,\"tile_sheet\":\"outdoors\"", json);
        Assert.Contains("\"properties\":{\"TouchAction\":\"MagicWarp Custom_EnchantedGrove\",\"Passable\":\"F\"}", json);
    }
}
