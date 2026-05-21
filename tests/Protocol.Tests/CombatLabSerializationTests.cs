using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class CombatLabSerializationTests
{
    [Fact]
    public void ResetRequest_SerializesSnakeCaseFields()
    {
        var json = JsonSerializer.Serialize(new CombatLabResetRequest
        {
            PlayerX = 8,
            PlayerY = 9,
            Width = 20,
            Height = 14,
            WarpPlayer = true,
        }, ProtocolJson.Options);

        Assert.Contains("\"player_x\":8", json);
        Assert.Contains("\"player_y\":9", json);
        Assert.Contains("\"width\":20", json);
        Assert.Contains("\"height\":14", json);
        Assert.Contains("\"warp_player\":true", json);
    }

    [Fact]
    public void SpawnMonsterRequest_SerializesSnakeCaseFields()
    {
        var json = JsonSerializer.Serialize(new CombatLabSpawnMonsterRequest
        {
            Kind = "GreenSlime",
            Label = "target",
            X = 12,
            Y = 8,
            Health = 1,
        }, ProtocolJson.Options);

        Assert.Contains("\"kind\":\"GreenSlime\"", json);
        Assert.Contains("\"label\":\"target\"", json);
        Assert.Contains("\"x\":12", json);
        Assert.Contains("\"y\":8", json);
        Assert.Contains("\"health\":1", json);
    }

    [Fact]
    public void SpawnMonsterResult_SerializesIdentityFields()
    {
        var json = JsonSerializer.Serialize(new CombatLabSpawnMonsterResult
        {
            Ok = true,
            MonsterId = "frobby-monster-1",
            Label = "target",
            Kind = "GreenSlime",
            Location = "Frobby_CombatLab",
            Tile = new TilePoint { X = 12, Y = 8 },
            Health = 1,
            MaxHealth = 24,
        }, ProtocolJson.Options);

        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"monster_id\":\"frobby-monster-1\"", json);
        Assert.Contains("\"label\":\"target\"", json);
        Assert.Contains("\"kind\":\"GreenSlime\"", json);
        Assert.Contains("\"location\":\"Frobby_CombatLab\"", json);
        Assert.Contains("\"tile\":{\"x\":12,\"y\":8}", json);
        Assert.Contains("\"health\":1", json);
        Assert.Contains("\"max_health\":24", json);
    }

    [Fact]
    public void RelocateMonsterRequest_SerializesSnakeCaseFields()
    {
        var json = JsonSerializer.Serialize(new CombatLabRelocateMonsterRequest
        {
            FromLocation = "Custom_CrimsonBadlands",
            Label = "corrupt-mummy",
            TargetX = 9,
            TargetY = 8,
            Match = new CombatLabMonsterMatchCriteria
            {
                X = 20,
                Y = 144,
                SpriteTexture = "Characters/Monsters/CorruptMummy",
                Health = 2000,
                MaxHealth = 2000,
                Damage = 100,
            },
        }, ProtocolJson.Options);

        Assert.Contains("\"from_location\":\"Custom_CrimsonBadlands\"", json);
        Assert.Contains("\"label\":\"corrupt-mummy\"", json);
        Assert.Contains("\"target_x\":9", json);
        Assert.Contains("\"target_y\":8", json);
        Assert.Contains("\"match\":", json);
        Assert.Contains("\"sprite_texture\":\"Characters/Monsters/CorruptMummy\"", json);
        Assert.Contains("\"max_health\":2000", json);
    }

    [Fact]
    public void RelocateMonsterResult_SerializesIdentityAndSourceFields()
    {
        var json = JsonSerializer.Serialize(new CombatLabRelocateMonsterResult
        {
            Ok = true,
            MonsterId = "frobby-monster-1",
            Label = "corrupt-mummy",
            FromLocation = "Custom_CrimsonBadlands",
            SourceTile = new TilePoint { X = 20, Y = 144 },
            Location = "Frobby_CombatLab",
            Tile = new TilePoint { X = 9, Y = 8 },
            Name = "Mummy",
            Type = "Mummy",
            SpriteTexture = "Characters/Monsters/CorruptMummy",
            Health = 2000,
            MaxHealth = 2000,
        }, ProtocolJson.Options);

        Assert.Contains("\"monster_id\":\"frobby-monster-1\"", json);
        Assert.Contains("\"label\":\"corrupt-mummy\"", json);
        Assert.Contains("\"from_location\":\"Custom_CrimsonBadlands\"", json);
        Assert.Contains("\"source_tile\":{\"x\":20,\"y\":144}", json);
        Assert.Contains("\"location\":\"Frobby_CombatLab\"", json);
        Assert.Contains("\"tile\":{\"x\":9,\"y\":8}", json);
        Assert.Contains("\"sprite_texture\":\"Characters/Monsters/CorruptMummy\"", json);
    }
}
