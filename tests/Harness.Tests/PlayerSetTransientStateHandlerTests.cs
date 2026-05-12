using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class PlayerSetTransientStateHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => PlayerSetTransientStateHandler.Handle(null, new FakeTransientStateWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_EmptyRequest_ThrowsInvalidParams()
    {
        var req = ProtocolJson.ToElement(new SetTransientStateRequest());

        var ex = Assert.Throws<JsonRpcException>(() => PlayerSetTransientStateHandler.Handle(req, new FakeTransientStateWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("swimming", ex.Message);
    }

    [Fact]
    public void Handle_UpdatesOnlyProvidedFields()
    {
        var world = new FakeTransientStateWorld { Swimming = false, BathingClothes = true };
        var req = ProtocolJson.ToElement(new SetTransientStateRequest { Swimming = true });

        var result = PlayerSetTransientStateHandler.Handle(req, world);
        var state = JsonSerializer.Deserialize<SetTransientStateResult>(result, ProtocolJson.Options)!;

        Assert.False(state.PreviousSwimming);
        Assert.True(state.PreviousBathingClothes);
        Assert.True(state.Swimming);
        Assert.True(state.BathingClothes);
        Assert.True(world.Swimming);
        Assert.True(world.BathingClothes);
    }

    private sealed class FakeTransientStateWorld : ITransientPlayerStateWorld
    {
        public bool Swimming { get; set; }
        public bool BathingClothes { get; set; }
        public int Tick => 42;
        public void RequireWorldReady() { }
    }
}
