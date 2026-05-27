using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class PlayerAddEventSeenHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => PlayerAddEventSeenHandler.Handle(null));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-number")]
    public void Handle_InvalidId_ThrowsInvalidParams(string id)
    {
        var p = JsonSerializer.SerializeToElement(new { id });

        var ex = Assert.Throws<JsonRpcException>(() => PlayerAddEventSeenHandler.Handle(p));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("numeric", ex.Message);
    }

    [Fact(Skip = "Requires live SDV (Game1.MasterPlayer.eventsSeen read/write).")]
    public void Handle_ValidId_AddsEventSeenFlag() { /* integration */ }

    [Fact]
    public void EventSeenIds_PreservesZeroPaddedModEventId()
    {
        var ids = PlayerAddEventSeenHandler.EventSeenIds("015305930");

        Assert.Equal(new[] { "015305930", "15305930" }, ids);
    }
}
