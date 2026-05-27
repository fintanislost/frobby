using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class FestivalContinueHandlerTests
{
    [Fact]
    public void Handle_NoActiveEvent_ThrowsGameStateInvalid()
    {
        var ex = Assert.Throws<JsonRpcException>(() =>
            FestivalContinueHandler.Handle(null, new FakeFestivalContinueWorld { ActiveEvent = null }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_NonFestivalEvent_ThrowsGameStateInvalid()
    {
        var ex = Assert.Throws<JsonRpcException>(() =>
            FestivalContinueHandler.Handle(null, new FakeFestivalContinueWorld { IsFestival = false }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_ContinuesActiveFestival()
    {
        var world = new FakeFestivalContinueWorld();

        var json = FestivalContinueHandler.Handle(null, world);
        var result = JsonSerializer.Deserialize<FestivalContinueResult>(json, ProtocolJson.Options)!;

        Assert.True(result.Ok);
        Assert.Equal(1234, result.Tick);
        Assert.Equal("festival_fall16", result.Id);
        Assert.True(result.IsFestival);
        Assert.True(world.Continued);
    }

    private sealed class FakeFestivalContinueWorld : IFestivalContinueWorld
    {
        public object? ActiveEvent { get; init; } = new();
        public int Tick => 1234;
        public bool IsFestival { get; init; } = true;
        public bool Continued { get; private set; }

        public string ReadEventId(object ev) => "festival_fall16";
        public void ContinueFestival(object ev) => Continued = true;
    }
}
