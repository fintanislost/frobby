using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class WorldInteractTileHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => WorldInteractTileHandler.Handle(null));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_MissingX_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"y\":2}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => WorldInteractTileHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("x", ex.Message);
    }

    [Fact]
    public void Handle_NegativeX_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"x\":-1,\"y\":2}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => WorldInteractTileHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("x", ex.Message);
    }

    [Fact]
    public void Handle_MissingY_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"x\":1}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => WorldInteractTileHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("y", ex.Message);
    }

    [Fact]
    public void Handle_NegativeY_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"x\":1,\"y\":-2}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => WorldInteractTileHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("y", ex.Message);
    }

    [Fact(Skip = "Requires live SDV (current location furniture/object interaction).")]
    public void Handle_FurnitureAtTile_InvokesCheckForAction() { /* integration */ }

    [Fact(Skip = "Requires live SDV (Context.IsWorldReady).")]
    public void Handle_AtTitleScreen_ThrowsGameStateInvalid() { /* integration */ }
}
