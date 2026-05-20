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
}
