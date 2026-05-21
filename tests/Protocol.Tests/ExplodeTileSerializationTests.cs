using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class ExplodeTileSerializationTests
{
    [Fact]
    public void ExplodeTileRequest_DeserializesSnakeCaseFields()
    {
        var req = JsonSerializer.Deserialize<ExplodeTileRequest>(
            "{\"location\":\"Frobby_CombatLab\",\"x\":9,\"y\":8,\"radius\":2,\"damage_player\":false}",
            ProtocolJson.Options)!;

        Assert.Equal("Frobby_CombatLab", req.Location);
        Assert.Equal(9, req.X);
        Assert.Equal(8, req.Y);
        Assert.Equal(2, req.Radius);
        Assert.False(req.DamagePlayer);
    }

    [Fact]
    public void ExplodeTileResult_SerializesDiagnosticsAsSnakeCase()
    {
        var result = new ExplodeTileResult
        {
            Ok = true,
            Tick = 123,
            Location = "Frobby_CombatLab",
            Tile = new TilePoint { X = 9, Y = 8 },
            Radius = 2,
            DamagePlayer = false,
            MonstersBefore = 1,
            MonstersAfter = 0,
            DebrisBefore = 0,
            DebrisAfter = 1,
            Invoked = true,
        };

        var json = JsonSerializer.Serialize(result, ProtocolJson.Options);

        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"tick\":123", json);
        Assert.Contains("\"location\":\"Frobby_CombatLab\"", json);
        Assert.Contains("\"tile\":{\"x\":9,\"y\":8}", json);
        Assert.Contains("\"radius\":2", json);
        Assert.Contains("\"damage_player\":false", json);
        Assert.Contains("\"monsters_before\":1", json);
        Assert.Contains("\"monsters_after\":0", json);
        Assert.Contains("\"debris_before\":0", json);
        Assert.Contains("\"debris_after\":1", json);
        Assert.Contains("\"invoked\":true", json);
        Assert.DoesNotContain("DamagePlayer", json);
    }
}
