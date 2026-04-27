using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class PlayerWarpHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => PlayerWarpHandler.Handle(null));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_EmptyLocation_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"location\":\"\",\"x\":0,\"y\":0}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => PlayerWarpHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_MalformedJson_ThrowsInvalidParams()
    {
        // X is supposed to be int — passing a string should surface as InvalidParams via
        // the JsonException catch.
        var p = JsonDocument.Parse("{\"location\":\"Farm\",\"x\":\"not-a-number\",\"y\":0}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => PlayerWarpHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact(Skip = "Requires live SDV (Game1.getLocationFromName + Game1.warpFarmer). Lives in integration tests once those exist.")]
    public void Handle_ValidLocation_WarpsAndReturnsTick() { /* integration */ }

    [Fact(Skip = "Requires live SDV (Game1.getLocationFromName).")]
    public void Handle_UnknownLocation_ThrowsGameStateInvalid() { /* integration */ }
}
