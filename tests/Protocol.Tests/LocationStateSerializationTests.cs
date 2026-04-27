using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class LocationStateSerializationTests
{
    [Fact]
    public void Serialize_SnakeCaseFields()
    {
        var loc = new LocationState
        {
            Name = "Farm",
            IsOutdoors = true,
            Npcs = new() { new NpcSummary { Name = "Pierre", Tile = new TilePoint { X = 4, Y = 17 } } },
            Objects = new() { new ObjectSummary { Tile = new TilePoint { X = 10, Y = 10 }, Name = "Weeds" } },
            Terrain = new() { new TerrainSummary { Tile = new TilePoint { X = 12, Y = 12 }, Kind = "HoeDirt" } },
        };

        var json = JsonSerializer.Serialize(loc, ProtocolJson.Options);
        Assert.Contains("\"name\":\"Farm\"", json);
        Assert.Contains("\"is_outdoors\":true", json);
        Assert.Contains("\"npcs\":[{\"name\":\"Pierre\"", json);
        Assert.Contains("\"terrain\":[{\"tile\":{\"x\":12,\"y\":12},\"kind\":\"HoeDirt\"}]", json);
    }
}
