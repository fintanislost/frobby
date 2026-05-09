using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class CombatAttackSerializationTests
{
    [Fact]
    public void CombatAttackRequest_DeserializesFromSnakeCase()
    {
        var json = "{\"x\":12,\"y\":34,\"direction\":\"right\",\"repeat\":3,\"delay_ticks\":2,\"qualified_item_id\":\"(W)4\"}";

        var req = JsonSerializer.Deserialize<CombatAttackRequest>(json, ProtocolJson.Options)!;

        Assert.Equal(12, req.X);
        Assert.Equal(34, req.Y);
        Assert.Equal("right", req.Direction);
        Assert.Equal(3, req.Repeat);
        Assert.Equal(2, req.DelayTicks);
        Assert.Equal("(W)4", req.QualifiedItemId);
    }

    [Fact]
    public void CombatAttackResult_SerializesToSnakeCase()
    {
        var result = new CombatAttackResult
        {
            Tick = 123,
            Tile = new TilePoint { X = 12, Y = 34 },
            Direction = "right",
            SelectedItemId = "4",
            SelectedItemQualifiedId = "(W)4",
            SelectedItemName = "Rusty Sword",
            SelectedItemRuntimeType = "MeleeWeapon",
        };

        var json = JsonSerializer.Serialize(result, ProtocolJson.Options);

        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"tick\":123", json);
        Assert.Contains("\"tile\":", json);
        Assert.Contains("\"x\":12", json);
        Assert.Contains("\"y\":34", json);
        Assert.Contains("\"direction\":\"right\"", json);
        Assert.Contains("\"selected_item_id\":\"4\"", json);
        Assert.Contains("\"selected_item_qualified_id\":\"(W)4\"", json);
        Assert.Contains("\"selected_item_name\":\"Rusty Sword\"", json);
        Assert.Contains("\"selected_item_runtime_type\":\"MeleeWeapon\"", json);
    }
}
