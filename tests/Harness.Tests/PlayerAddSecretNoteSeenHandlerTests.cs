using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class PlayerAddSecretNoteSeenHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => PlayerAddSecretNoteSeenHandler.Handle(null));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Handle_InvalidId_ThrowsInvalidParams(int id)
    {
        var p = JsonSerializer.SerializeToElement(new { id });

        var ex = Assert.Throws<JsonRpcException>(() => PlayerAddSecretNoteSeenHandler.Handle(p));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("positive", ex.Message);
    }

    [Fact(Skip = "Requires live SDV (Game1.MasterPlayer.secretNotesSeen read/write).")]
    public void Handle_ValidId_AddsSecretNoteSeen() { /* integration */ }
}
