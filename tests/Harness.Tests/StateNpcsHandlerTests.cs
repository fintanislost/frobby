using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class StateNpcsHandlerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public void Handle_InvalidLimit_ThrowsInvalidParams(int limit)
    {
        var p = JsonSerializer.SerializeToElement(new { limit });

        var ex = Assert.Throws<JsonRpcException>(() => StateNpcsHandler.Handle(p));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("limit", ex.Message);
    }

    [Fact(Skip = "Requires live SDV (Game1.locations/currentLocation NPC collections).")]
    public void Handle_DefaultParams_ReturnsRuntimeNpcs() { }
}
