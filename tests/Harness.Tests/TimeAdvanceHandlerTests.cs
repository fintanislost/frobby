using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class TimeAdvanceHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => TimeAdvanceHandler.Handle(null));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Theory]
    [InlineData(0)]        // below range
    [InlineData(5)]        // not multiple of 10 and below range
    [InlineData(15)]       // not multiple of 10, in range
    [InlineData(130)]      // above range
    [InlineData(-30)]      // negative
    public void Handle_InvalidMinutes_ThrowsInvalidParams(int minutes)
    {
        var p = JsonDocument.Parse("{\"minutes\":" + minutes + "}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => TimeAdvanceHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_NonIntegerMinutes_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"minutes\":\"abc\"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => TimeAdvanceHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact(Skip = "Requires live SDV (Game1.performTenMinuteClockUpdate + Game1.timeOfDay).")]
    public void Handle_Valid30Minutes_AdvancesClockAndReturns() { /* integration */ }

    [Fact(Skip = "Requires live SDV (Context.IsWorldReady — verified by smoke test).")]
    public void Handle_AtTitleScreen_ThrowsGameStateInvalid() { /* integration */ }
}
