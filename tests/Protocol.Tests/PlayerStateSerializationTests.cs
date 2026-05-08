using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class PlayerStateSerializationTests
{
    [Fact]
    public void Serialize_ProducesSnakeCaseFields()
    {
        var p = new PlayerState
        {
            Name = "Tester",
            Money = 1000,
            Stamina = 270,
            MaxStamina = 270,
            Health = 100,
            Location = "Farm",
            Tile = new TilePoint { X = 64, Y = 15 },
        };
        p.Items.Add(new PlayerItemSummary
        {
            Slot = 12,
            Id = "(O)ExampleMod.CustomDrink",
            ItemId = "ExampleMod.CustomDrink",
            QualifiedId = "(O)ExampleMod.CustomDrink",
            Name = "Custom Drink",
            Stack = 1,
            Category = 0,
            Quality = 0,
            RuntimeType = "Object",
        });

        var json = JsonSerializer.Serialize(p, ProtocolJson.Options);

        Assert.Contains("\"name\":\"Tester\"", json);
        Assert.Contains("\"max_stamina\":270", json);
        Assert.Contains("\"tile\":{\"x\":64,\"y\":15}", json);
        Assert.Contains("\"item_id\":\"ExampleMod.CustomDrink\"", json);
        Assert.Contains("\"qualified_id\":\"(O)ExampleMod.CustomDrink\"", json);
        Assert.Contains("\"runtime_type\":\"Object\"", json);
        Assert.DoesNotContain("MaxStamina", json);
        Assert.DoesNotContain("QualifiedId", json);
    }

    [Fact]
    public void Serialize_OmitsUnsetAdditiveInventoryMetadata()
    {
        var p = new PlayerState
        {
            Name = "Tester",
            Location = "Farm",
            Tile = new TilePoint { X = 1, Y = 2 },
        };
        p.Items.Add(new PlayerItemSummary
        {
            Slot = 0,
            Id = "(O)388",
            Name = "Wood",
            Stack = 5,
        });

        var json = JsonSerializer.Serialize(p, ProtocolJson.Options);

        Assert.Contains("\"id\":\"(O)388\"", json);
        Assert.DoesNotContain("\"item_id\"", json);
        Assert.DoesNotContain("\"qualified_id\"", json);
        Assert.DoesNotContain("\"runtime_type\"", json);
    }

    [Fact]
    public void Deserialize_RoundTrips()
    {
        var original = new PlayerState
        {
            Name = "Alice",
            Money = 500,
            Stamina = 170,
            MaxStamina = 270,
            Health = 80,
            Location = "BusStop",
            Tile = new TilePoint { X = 10, Y = 20 },
        };

        var json = JsonSerializer.Serialize(original, ProtocolJson.Options);
        var round = JsonSerializer.Deserialize<PlayerState>(json, ProtocolJson.Options)!;

        Assert.Equal(original.Name, round.Name);
        Assert.Equal(original.MaxStamina, round.MaxStamina);
        Assert.Equal(original.Tile.X, round.Tile.X);
        Assert.Equal(original.Tile.Y, round.Tile.Y);
    }
}
