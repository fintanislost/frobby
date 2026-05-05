using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class PlayerAddMailHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => PlayerAddMailHandler.Handle(null));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Handle_BlankId_ThrowsInvalidParams(string id)
    {
        var p = JsonSerializer.SerializeToElement(new { id });
        var ex = Assert.Throws<JsonRpcException>(() => PlayerAddMailHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("non-empty", ex.Message);
    }

    [Fact(Skip = "Requires live SDV (Game1.MasterPlayer.mailReceived read/write).")]
    public void Handle_ValidId_AddsMailFlag() { /* integration */ }
}
