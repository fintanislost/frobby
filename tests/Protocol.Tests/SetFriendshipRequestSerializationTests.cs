using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class SetFriendshipRequestSerializationTests
{
    [Fact]
    public void Request_DeserializesSnakeCase()
    {
        var req = JsonSerializer.Deserialize<SetFriendshipRequest>(
            "{\"npc\":\"Sophia\",\"points\":500,\"talked_to_today\":true,\"gifts_today\":1,\"gifts_this_week\":2}",
            ProtocolJson.Options)!;

        Assert.Equal("Sophia", req.Npc);
        Assert.Equal(500, req.Points);
        Assert.True(req.TalkedToToday);
        Assert.Equal(1, req.GiftsToday);
        Assert.Equal(2, req.GiftsThisWeek);
    }
}
