using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class PlayerGiveItemHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => PlayerGiveItemHandler.Handle(null));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_EmptyId_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"id\":\"\",\"count\":1}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => PlayerGiveItemHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("id", ex.Message);
    }

    [Fact]
    public void Handle_ZeroCount_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"id\":\"(O)388\",\"count\":0}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => PlayerGiveItemHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact(Skip = "Requires live SDV (ItemRegistry.Create + Game1.player).")]
    public void Handle_ValidItem_AddsAndReturnsTick() { /* integration */ }

    [Fact(Skip = "Requires live SDV (ItemRegistry.Exists + Game1.player).")]
    public void Handle_UnknownItemId_ThrowsGameStateInvalid() { /* integration */ }

    [Fact(Skip = "Requires live SDV (Context.IsWorldReady — verified by smoke test).")]
    public void Handle_AtTitleScreen_ThrowsGameStateInvalid() { /* integration */ }
}
