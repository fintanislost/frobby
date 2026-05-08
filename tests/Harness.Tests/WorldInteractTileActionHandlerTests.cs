using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class WorldInteractTileActionHandlerTests
{
    [Fact]
    public void Handle_NegativeX_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"x\":-1,\"y\":2}").RootElement;
        var world = new FakeTileActionWorld();

        var ex = Assert.Throws<JsonRpcException>(() => WorldInteractTileActionHandler.Handle(p, world));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("x", ex.Message);
    }

    [Fact]
    public void Handle_UnsupportedProperty_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"x\":1,\"y\":2,\"property\":\"Message\"}").RootElement;
        var world = new FakeTileActionWorld();

        var ex = Assert.Throws<JsonRpcException>(() => WorldInteractTileActionHandler.Handle(p, world));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("property", ex.Message);
    }

    [Fact]
    public void Handle_LocationGuardMismatch_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"location\":\"Town\",\"x\":1,\"y\":2}").RootElement;
        var world = new FakeTileActionWorld { CurrentLocationName = "Forest" };

        var ex = Assert.Throws<JsonRpcException>(() => WorldInteractTileActionHandler.Handle(p, world));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("Town", ex.Message);
        Assert.Contains("Forest", ex.Message);
    }

    [Fact]
    public void Handle_ActionProperty_InvokesPerformAction()
    {
        var p = JsonDocument.Parse("{\"x\":8,\"y\":9,\"property\":\"Action\"}").RootElement;
        var world = new FakeTileActionWorld();
        world.Set(8, 9, "Buildings", "Action", "Warp 5 10 Custom_HenchmanHouse");

        var json = WorldInteractTileActionHandler.Handle(p, world);

        Assert.Equal(new[] { "action:Warp 5 10 Custom_HenchmanHouse:8:9:False" }, world.Calls);
        Assert.True(json.GetProperty("handled").GetBoolean());
        Assert.Equal("Action", json.GetProperty("action_type").GetString());
        Assert.Equal("Warp 5 10 Custom_HenchmanHouse", json.GetProperty("action").GetString());
    }

    [Fact]
    public void Handle_TouchActionProperty_InvokesPerformTouchAction()
    {
        var p = JsonDocument.Parse("{\"x\":56,\"y\":48,\"property\":\"TouchAction\",\"layers\":[\"Back\"]}").RootElement;
        var world = new FakeTileActionWorld();
        world.Set(56, 48, "Back", "TouchAction", "LoadMap Town 50 114 0");

        var json = WorldInteractTileActionHandler.Handle(p, world);

        Assert.Equal(new[] { "move:56:48", "touch:LoadMap Town 50 114 0:56:48" }, world.Calls);
        Assert.True(json.GetProperty("handled").GetBoolean());
        Assert.Equal("TouchAction", json.GetProperty("action_type").GetString());
        Assert.Equal("LoadMap Town 50 114 0", json.GetProperty("action").GetString());
    }

    [Fact]
    public void Handle_NoProperty_PrefersActionBeforeTouchAction()
    {
        var p = JsonDocument.Parse("{\"x\":4,\"y\":5}").RootElement;
        var world = new FakeTileActionWorld();
        world.Set(4, 5, "Back", "TouchAction", "LoadMap Town 1 2");
        world.Set(4, 5, "Buildings", "Action", "Message \"hello\"");

        WorldInteractTileActionHandler.Handle(p, world);

        Assert.Equal(new[] { "action:Message \"hello\":4:5:False" }, world.Calls);
    }

    [Fact]
    public void Handle_NoActionProperty_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"x\":4,\"y\":5}").RootElement;
        var world = new FakeTileActionWorld();

        var ex = Assert.Throws<JsonRpcException>(() => WorldInteractTileActionHandler.Handle(p, world));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("4,5", ex.Message);
    }

    private sealed class FakeTileActionWorld : IWorldInteractTileActionWorld
    {
        private readonly Dictionary<(int X, int Y, string Layer, string Property), string> _properties = new();

        public bool IsWorldReady => true;
        public string CurrentLocationName { get; init; } = "Custom_BlueMoonVineyard";
        public int Tick => 123;
        public int PlayerTileX => 10;
        public int PlayerTileY => 20;
        public IReadOnlyList<string> LayerNames { get; } = new[] { "Back", "Buildings" };
        public List<string> Calls { get; } = new();

        public void Set(int x, int y, string layer, string property, string value)
            => _properties[(x, y, layer, property)] = value;

        public string? GetTileProperty(int x, int y, string layer, string property)
            => _properties.TryGetValue((x, y, layer, property), out var value) ? value : null;

        public bool PerformAction(string action, int x, int y, bool justCheckingForActivity)
        {
            Calls.Add($"action:{action}:{x}:{y}:{justCheckingForActivity}");
            return true;
        }

        public void MovePlayerToTile(int x, int y)
            => Calls.Add($"move:{x}:{y}");

        public void PerformTouchAction(string action, int x, int y)
            => Calls.Add($"touch:{action}:{x}:{y}");
    }
}
