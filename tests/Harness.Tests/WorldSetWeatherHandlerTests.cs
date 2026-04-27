using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class WorldSetWeatherHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => WorldSetWeatherHandler.Handle(null));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_EmptyType_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"type\":\"\"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => WorldSetWeatherHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_UnknownType_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"type\":\"hurricane\"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => WorldSetWeatherHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("unknown weather type", ex.Message);
    }

    [Fact(Skip = "Requires live SDV (Game1.netWorldState + Game1.updateWeather).")]
    public void Handle_ValidType_SetsWeatherAndReturnsTick() { /* integration */ }

    [Fact(Skip = "Requires live SDV (Context.IsWorldReady — verified by smoke test).")]
    public void Handle_AtTitleScreen_ThrowsGameStateInvalid() { /* integration */ }
}
