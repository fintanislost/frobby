using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class WorldWarpNpcHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var world = new FakeWorldWarpNpcWorld();

        var ex = Assert.Throws<JsonRpcException>(() => WorldWarpNpcHandler.Handle(null, world));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Theory]
    [InlineData("{\"location\":\"Town\",\"x\":1,\"y\":2}")]
    [InlineData("{\"name\":\"\",\"location\":\"Town\",\"x\":1,\"y\":2}")]
    [InlineData("{\"name\":\"Sophia\",\"x\":1,\"y\":2}")]
    [InlineData("{\"name\":\"Sophia\",\"location\":\"\",\"x\":1,\"y\":2}")]
    [InlineData("{\"name\":\"Sophia\",\"location\":\"Town\",\"y\":2}")]
    [InlineData("{\"name\":\"Sophia\",\"location\":\"Town\",\"x\":1}")]
    [InlineData("{\"name\":\"Sophia\",\"location\":\"Town\",\"x\":-1,\"y\":2}")]
    [InlineData("{\"name\":\"Sophia\",\"location\":\"Town\",\"x\":1,\"y\":-2}")]
    public void Handle_InvalidParams_ThrowsInvalidParams(string json)
    {
        var p = JsonDocument.Parse(json).RootElement;
        var world = new FakeWorldWarpNpcWorld();

        var ex = Assert.Throws<JsonRpcException>(() => WorldWarpNpcHandler.Handle(p, world));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_UnknownLocation_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"name\":\"Sophia\",\"location\":\"Missing\",\"x\":1,\"y\":2}").RootElement;
        var world = new FakeWorldWarpNpcWorld { Npc = new object() };

        var ex = Assert.Throws<JsonRpcException>(() => WorldWarpNpcHandler.Handle(p, world));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("no location named", ex.Message);
    }

    [Fact]
    public void Handle_UnknownNpc_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"name\":\"Missing\",\"location\":\"Town\",\"x\":1,\"y\":2}").RootElement;
        var world = new FakeWorldWarpNpcWorld();
        world.Locations.Add("Town");

        var ex = Assert.Throws<JsonRpcException>(() => WorldWarpNpcHandler.Handle(p, world));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("no NPC named", ex.Message);
    }

    [Fact]
    public void Handle_ValidRequest_WarpsNpc()
    {
        var p = JsonDocument.Parse("{\"name\":\"Sophia\",\"location\":\"Custom_BlueMoonVineyard\",\"x\":20,\"y\":32}").RootElement;
        var npc = new object();
        var world = new FakeWorldWarpNpcWorld { Npc = npc };
        world.Locations.Add("Custom_BlueMoonVineyard");

        WorldWarpNpcHandler.Handle(p, world);

        Assert.Equal(new[] { "prepare:Sophia", "warp:Sophia:Custom_BlueMoonVineyard:20:32" }, world.Calls);
    }

    [Fact]
    public void PrepareNpcForWarp_ClearsTransientSleepingAndRouteState()
    {
        var npc = new FakeSleepingNpc();

        NpcWarpPreparation.Prepare(npc);

        Assert.True(npc.HaltCalled);
        Assert.Null(npc.controller);
        Assert.False(npc.isSleeping);
        Assert.False(npc.isPlayingSleepingAnimation);
        Assert.False(npc.doingEndOfRouteAnimation);
        Assert.False(npc.HideShadow);
        Assert.False(npc.isTemporarilyInvisible.Value);
        Assert.False(npc.IsInvisible);
    }

    private sealed class FakeWorldWarpNpcWorld : IWorldWarpNpcWorld
    {
        public HashSet<string> Locations { get; } = new();
        public List<string> Calls { get; } = new();
        public object? Npc { get; set; }
        public int Tick => 123;

        public bool LocationExists(string name) => Locations.Contains(name);
        public object? FindNpc(string name) => Npc;
        public void PrepareNpcForWarp(object npc, string name)
            => Calls.Add($"prepare:{name}");
        public void WarpNpc(object npc, string name, string location, int x, int y)
            => Calls.Add($"warp:{name}:{location}:{x}:{y}");
    }

    private sealed class FakeSleepingNpc
    {
        public object? controller = new();
        public bool isSleeping = true;
        public bool isPlayingSleepingAnimation = true;
        public bool doingEndOfRouteAnimation = true;
        public FakeBool isTemporarilyInvisible = new(true);
        public bool HideShadow { get; set; } = true;
        public bool IsInvisible { get; set; } = true;
        public bool HaltCalled { get; private set; }

        public void Halt() => HaltCalled = true;
    }

    private sealed class FakeBool
    {
        public FakeBool(bool value) => Value = value;
        public bool Value { get; set; }
    }
}
