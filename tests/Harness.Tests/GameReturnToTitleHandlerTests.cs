using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class GameReturnToTitleHandlerTests
{
    [Fact]
    public void Handle_NoLoadedWorld_ThrowsGameStateInvalid()
    {
        var ex = Assert.Throws<JsonRpcException>(() =>
            GameReturnToTitleHandler.Handle(null, new FakeReturnToTitleWorld { IsWorldReady = false }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("loaded world", ex.Message);
    }

    [Fact]
    public void Handle_BlockedGameMode_ThrowsGameStateInvalid()
    {
        var ex = Assert.Throws<JsonRpcException>(() =>
            GameReturnToTitleHandler.Handle(null, new FakeReturnToTitleWorld { IsEventUp = true }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("event active", ex.Message);
    }

    [Fact]
    public void Handle_LoadedWorld_ReturnsToTitleAndReportsTick()
    {
        var world = new FakeReturnToTitleWorld { Tick = 4321 };

        var result = GameReturnToTitleHandler.Handle(null, world);
        var ok = JsonSerializer.Deserialize<MutatorOk>(result, ProtocolJson.Options)!;

        Assert.True(ok.Ok);
        Assert.Equal(4321, ok.Tick);
        Assert.True(world.ReturnedToTitle);
    }

    private sealed class FakeReturnToTitleWorld : IReturnToTitleWorld
    {
        public bool IsWorldReady { get; init; } = true;
        public bool IsEventUp { get; init; }
        public bool IsMinigameActive { get; init; }
        public bool IsWarping { get; init; }
        public int Tick { get; init; }
        public bool ReturnedToTitle { get; private set; }

        public void ReturnToTitle() => ReturnedToTitle = true;
    }
}
