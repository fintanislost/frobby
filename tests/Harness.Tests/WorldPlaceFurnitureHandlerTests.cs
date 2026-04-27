using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class WorldPlaceFurnitureHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => WorldPlaceFurnitureHandler.Handle(null));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_EmptyId_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"id\":\"\",\"x\":1,\"y\":2}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => WorldPlaceFurnitureHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("id", ex.Message);
    }

    [Fact]
    public void Handle_NegativeX_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"id\":\"(F)1308\",\"x\":-1,\"y\":2}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => WorldPlaceFurnitureHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("x", ex.Message);
    }

    [Fact]
    public void Handle_MissingX_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"id\":\"(F)1308\",\"y\":2}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => WorldPlaceFurnitureHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("x", ex.Message);
    }

    [Fact]
    public void Handle_NegativeY_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"id\":\"(F)1308\",\"x\":1,\"y\":-2}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => WorldPlaceFurnitureHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("y", ex.Message);
    }

    [Fact]
    public void Handle_MissingY_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"id\":\"(F)1308\",\"x\":1}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => WorldPlaceFurnitureHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("y", ex.Message);
    }

    [Fact(Skip = "Requires live SDV (ItemRegistry furniture creation + GameLocation.furniture).")]
    public void Handle_ValidFurniture_AddsFurnitureAndReturnsTile() { /* integration */ }

    [Fact(Skip = "Requires live SDV (Context.IsWorldReady — verified by smoke test).")]
    public void Handle_AtTitleScreen_ThrowsGameStateInvalid() { /* integration */ }
}
