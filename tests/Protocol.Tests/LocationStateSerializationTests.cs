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
            UniqueName = "Farm",
            IsOutdoors = true,
            MapWidth = 120,
            MapHeight = 80,
            Warps = new()
            {
                new WarpSummary
                {
                    Source = new TilePoint { X = 64, Y = 15 },
                    TargetLocation = "FarmHouse",
                    Target = new TilePoint { X = 8, Y = 10 },
                },
            },
            Npcs = new() { new NpcSummary { Name = "Pierre", Tile = new TilePoint { X = 4, Y = 17 } } },
            Objects = new()
            {
                new ObjectSummary
                {
                    Tile = new TilePoint { X = 10, Y = 10 },
                    Name = "Weeds",
                    Id = "O771",
                    QualifiedId = "(O)771",
                    Category = -999,
                    Stack = 1,
                    Quality = 0,
                },
            },
            Furniture = new() { new FurnitureSummary { Tile = new TilePoint { X = 7, Y = 8 }, Id = "(F)1302", Name = "Oak Chair" } },
            Terrain = new() { new TerrainSummary { Tile = new TilePoint { X = 12, Y = 12 }, Kind = "HoeDirt" } },
            ResourceClumps = new()
            {
                new ResourceClumpSummary
                {
                    Tile = new TilePoint { X = 21, Y = 17 },
                    Kind = "ResourceClump",
                    Id = "602",
                    Name = "Log",
                    Width = 2,
                    Height = 2,
                    Health = 10,
                },
            },
            Monsters = new()
            {
                new MonsterSummary
                {
                    Tile = new TilePoint { X = 44, Y = 31 },
                    Name = "Mummy",
                    Type = "Mummy",
                    Health = 2000,
                    MaxHealth = 2000,
                    Damage = 100,
                    SpriteTexture = "Characters/Monsters/CorruptMummy",
                },
            },
        };

        var json = JsonSerializer.Serialize(loc, ProtocolJson.Options);
        Assert.Contains("\"name\":\"Farm\"", json);
        Assert.Contains("\"unique_name\":\"Farm\"", json);
        Assert.Contains("\"is_outdoors\":true", json);
        Assert.Contains("\"map_width\":120", json);
        Assert.Contains("\"map_height\":80", json);
        Assert.Contains("\"warps\":[{\"source\":{\"x\":64,\"y\":15},\"target_location\":\"FarmHouse\",\"target\":{\"x\":8,\"y\":10}}]", json);
        Assert.Contains("\"npcs\":[{\"name\":\"Pierre\"", json);
        Assert.Contains("\"objects\":[{\"tile\":{\"x\":10,\"y\":10},\"name\":\"Weeds\",\"id\":\"O771\",\"qualified_id\":\"(O)771\",\"category\":-999,\"stack\":1,\"quality\":0}]", json);
        Assert.Contains("\"furniture\":[{\"tile\":{\"x\":7,\"y\":8},\"id\":\"(F)1302\",\"name\":\"Oak Chair\"}]", json);
        Assert.Contains("\"terrain\":[{\"tile\":{\"x\":12,\"y\":12},\"kind\":\"HoeDirt\"}]", json);
        Assert.Contains("\"resource_clumps\":[{\"tile\":{\"x\":21,\"y\":17},\"kind\":\"ResourceClump\",\"id\":\"602\",\"name\":\"Log\",\"width\":2,\"height\":2,\"health\":10}]", json);
        Assert.Contains("\"monsters\":[{\"tile\":{\"x\":44,\"y\":31},\"name\":\"Mummy\",\"type\":\"Mummy\",\"health\":2000,\"max_health\":2000,\"damage\":100,\"sprite_texture\":\"Characters/Monsters/CorruptMummy\"}]", json);
    }
}
