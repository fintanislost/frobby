using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class NpcStateSerializationTests
{
    [Fact]
    public void Serialize_SnakeCase()
    {
        var npc = new NpcState
        {
            Name = "Abigail",
            Location = "Town",
            Tile = new TilePoint { X = 4, Y = 23 },
            FriendshipPoints = 500,
            Hearts = 2,
            GiftGivenToday = false,
            Portrait = "Abigail",
        };
        var json = JsonSerializer.Serialize(npc, ProtocolJson.Options);
        Assert.Contains("\"friendship_points\":500", json);
        Assert.Contains("\"gift_given_today\":false", json);
    }
}
