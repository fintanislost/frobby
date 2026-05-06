using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class PlayerSetFriendshipHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => PlayerSetFriendshipHandler.Handle(null));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Handle_BlankNpc_ThrowsInvalidParams(string npc)
    {
        var p = JsonSerializer.SerializeToElement(new { npc, points = 500 });

        var ex = Assert.Throws<JsonRpcException>(() => PlayerSetFriendshipHandler.Handle(p));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("npc", ex.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2501)]
    public void Handle_OutOfRangePoints_ThrowsInvalidParams(int points)
    {
        var p = JsonSerializer.SerializeToElement(new { npc = "Sophia", points });

        var ex = Assert.Throws<JsonRpcException>(() => PlayerSetFriendshipHandler.Handle(p));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("points", ex.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void Handle_OutOfRangeGiftCounts_ThrowsInvalidParams(int gifts)
    {
        var p = JsonSerializer.SerializeToElement(new
        {
            npc = "Sophia",
            points = 500,
            gifts_today = gifts,
        });

        var ex = Assert.Throws<JsonRpcException>(() => PlayerSetFriendshipHandler.Handle(p));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("gifts_today", ex.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void Handle_OutOfRangeWeeklyGiftCounts_ThrowsInvalidParams(int gifts)
    {
        var p = JsonSerializer.SerializeToElement(new
        {
            npc = "Sophia",
            points = 500,
            gifts_this_week = gifts,
        });

        var ex = Assert.Throws<JsonRpcException>(() => PlayerSetFriendshipHandler.Handle(p));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("gifts_this_week", ex.Message);
    }

    [Fact(Skip = "Requires live SDV (Game1.MasterPlayer.friendshipData read/write).")]
    public void Handle_ValidRequest_SetsFriendshipEntry() { }
}
