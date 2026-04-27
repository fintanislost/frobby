using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class TimeSetHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => TimeSetHandler.Handle(null));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_NoFields_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => TimeSetHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("at least one", ex.Message);
    }

    [Fact]
    public void Handle_InvalidSeason_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"season\":\"autumn\"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => TimeSetHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("season", ex.Message);
    }

    [Theory]
    [InlineData(2570)]   // minutes >= 60 (25h70m)
    [InlineData(500)]    // H < 6
    [InlineData(2600)]   // H == 26, at upper bound (must be < 2600)
    [InlineData(1560)]   // minutes == 60
    public void Handle_InvalidTime_ThrowsInvalidParams(int time)
    {
        var p = JsonDocument.Parse("{\"time\":" + time + "}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => TimeSetHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Theory]
    [InlineData(0)]    // below range
    [InlineData(29)]   // above range
    [InlineData(-1)]   // negative
    public void Handle_InvalidDay_ThrowsInvalidParams(int day)
    {
        var p = JsonDocument.Parse("{\"day\":" + day + "}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => TimeSetHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Theory]
    [InlineData(0)]    // zero
    [InlineData(-1)]   // negative
    public void Handle_InvalidYear_ThrowsInvalidParams(int year)
    {
        var p = JsonDocument.Parse("{\"year\":" + year + "}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => TimeSetHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact(Skip = "Requires live SDV (Game1.timeOfDay/dayOfMonth/season/year mutation).")]
    public void Handle_ValidTime_SetsTimeOfDayAndReturnsTick() { /* integration */ }

    [Fact(Skip = "Requires live SDV (Context.IsWorldReady — verified by smoke test).")]
    public void Handle_AtTitleScreen_WithValidInput_ThrowsGameStateInvalid() { /* integration */ }
}
