using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class PlayerSetMoneyHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => PlayerSetMoneyHandler.Handle(null));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_NegativeAmount_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"amount\":-1}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => PlayerSetMoneyHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains(">= 0", ex.Message);
    }

    [Fact]
    public void Handle_NonIntegerAmount_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"amount\":\"not-a-number\"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => PlayerSetMoneyHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact(Skip = "Requires live SDV (Game1.player.Money read/write).")]
    public void Handle_ValidAmount_UpdatesMoneyAndReturnsPrevious() { /* integration */ }

    [Fact(Skip = "Requires live SDV (Context.IsWorldReady — verified by smoke test).")]
    public void Handle_AtTitleScreen_ThrowsGameStateInvalid() { /* integration */ }
}
