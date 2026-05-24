using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class PlaceInventoryObjectSerializationTests
{
    [Fact]
    public void Request_DeserializesFromSnakeCase()
    {
        var json = "{\"id\":\"(O)287\",\"location\":\"Frobby_CombatLab\",\"x\":9,\"y\":8,\"slot\":12,\"facing\":\"right\"}";

        var req = JsonSerializer.Deserialize<PlaceInventoryObjectRequest>(json, ProtocolJson.Options)!;

        Assert.Equal("(O)287", req.Id);
        Assert.Equal("Frobby_CombatLab", req.Location);
        Assert.Equal(9, req.X);
        Assert.Equal(8, req.Y);
        Assert.Equal(12, req.Slot);
        Assert.Equal("right", req.Facing);
    }

    [Fact]
    public void Request_OptionalFieldsRemainNullWhenOmitted()
    {
        var json = "{\"id\":\"(O)287\",\"x\":9,\"y\":8}";

        var req = JsonSerializer.Deserialize<PlaceInventoryObjectRequest>(json, ProtocolJson.Options)!;

        Assert.Null(req.Location);
        Assert.Null(req.Slot);
        Assert.Null(req.Facing);
    }

    [Fact]
    public void Result_SerializesToSnakeCase()
    {
        var result = new PlaceInventoryObjectResult
        {
            Ok = true,
            Tick = 42,
            Id = "287",
            QualifiedId = "(O)287",
            Name = "Bomb",
            Location = "Frobby_CombatLab",
            Tile = new TilePoint { X = 9, Y = 8 },
            SourceSlot = 12,
            StackBefore = 2,
            StackAfter = 1,
            RuntimeType = "Object",
            Placed = true,
        };

        var json = JsonSerializer.Serialize(result, ProtocolJson.Options);

        Assert.Equal("{\"id\":\"287\",\"qualified_id\":\"(O)287\",\"name\":\"Bomb\",\"location\":\"Frobby_CombatLab\",\"tile\":{\"x\":9,\"y\":8},\"source_slot\":12,\"stack_before\":2,\"stack_after\":1,\"runtime_type\":\"Object\",\"placed\":true,\"ok\":true,\"tick\":42}", json);
    }
}
