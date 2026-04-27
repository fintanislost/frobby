using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class WorldInteractNpcHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => WorldInteractNpcHandler.Handle(null));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_EmptyName_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"name\":\"\"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => WorldInteractNpcHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("name", ex.Message);
    }

    [Fact(Skip = "Requires live SDV (Game1.currentLocation.characters + NPC.checkAction).")]
    public void Handle_NpcPresentInLocation_InvokesCheckActionAndReturnsTick() { /* integration */ }

    [Fact(Skip = "Requires live SDV (Context.IsWorldReady — verified by smoke test).")]
    public void Handle_AtTitleScreen_ThrowsGameStateInvalid() { /* integration */ }

    [Fact(Skip = "Requires live SDV (NPC not found returns GameStateInvalid -32003).")]
    public void Handle_NpcNotInCurrentLocation_ThrowsGameStateInvalid() { /* integration */ }
}
