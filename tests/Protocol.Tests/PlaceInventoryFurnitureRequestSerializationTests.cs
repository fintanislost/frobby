using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class PlaceInventoryFurnitureRequestSerializationTests
{
    [Fact]
    public void PlaceInventoryFurnitureRequest_DeserializesFromSnakeCase()
    {
        var json = "{\"id\":\"(F)example_terminal\",\"location\":\"FarmHouse\",\"x\":8,\"y\":9,\"remove_existing\":true}";
        var req = JsonSerializer.Deserialize<PlaceInventoryFurnitureRequest>(json, ProtocolJson.Options)!;

        Assert.Equal("(F)example_terminal", req.Id);
        Assert.Equal("FarmHouse", req.Location);
        Assert.Equal(8, req.X);
        Assert.Equal(9, req.Y);
        Assert.True(req.RemoveExisting);
    }

    [Fact]
    public void PlaceInventoryFurnitureResult_SerializesSourceSlot()
    {
        var result = new PlaceInventoryFurnitureResult
        {
            Tick = 42,
            Id = "(F)example_terminal",
            Location = "FarmHouse",
            Tile = new TilePoint { X = 8, Y = 9 },
            SourceSlot = 5,
        };

        var json = JsonSerializer.Serialize(result, ProtocolJson.Options);

        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"id\":\"(F)example_terminal\"", json);
        Assert.Contains("\"location\":\"FarmHouse\"", json);
        Assert.Contains("\"source_slot\":5", json);
    }
}
