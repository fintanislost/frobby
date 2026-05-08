using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class StateTileActionsHandlerTests
{
    [Fact]
    public void Handle_NegativeRadius_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"radius\":-1}").RootElement;
        var world = new FakeTileActionsWorld();

        var ex = Assert.Throws<JsonRpcException>(() => StateTileActionsHandler.Handle(p, world));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("radius", ex.Message);
    }

    [Fact]
    public void Handle_LocationGuardMismatch_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"location\":\"Town\"}").RootElement;
        var world = new FakeTileActionsWorld { CurrentLocationName = "Forest" };

        var ex = Assert.Throws<JsonRpcException>(() => StateTileActionsHandler.Handle(p, world));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("Town", ex.Message);
    }

    [Fact]
    public void Handle_FindsActionsWithinRadius()
    {
        var p = JsonDocument.Parse("{\"x\":10,\"y\":20,\"radius\":1}").RootElement;
        var world = new FakeTileActionsWorld();
        world.Set(10, 20, "Back", "TouchAction", "LoadMap Town 50 114 0");
        world.Set(11, 20, "Buildings", "Action", "Warp 5 10 Custom_HenchmanHouse");
        world.Set(12, 20, "Back", "TouchAction", "TooFar");

        var json = StateTileActionsHandler.Handle(p, world);
        var actions = json.GetProperty("actions").EnumerateArray().ToList();

        Assert.Equal(2, actions.Count);
        Assert.Equal("TouchAction", actions[0].GetProperty("property").GetString());
        Assert.Equal(0, actions[0].GetProperty("distance").GetInt32());
        Assert.Equal("Action", actions[1].GetProperty("property").GetString());
        Assert.Equal(1, actions[1].GetProperty("distance").GetInt32());
    }

    [Fact]
    public void Handle_PreservesRequestedLayerAndPropertyFilters()
    {
        var p = JsonDocument.Parse("{\"x\":10,\"y\":20,\"radius\":0,\"layers\":[\"Buildings\"],\"properties\":[\"Action\"]}").RootElement;
        var world = new FakeTileActionsWorld();
        world.Set(10, 20, "Back", "TouchAction", "LoadMap Town 50 114 0");
        world.Set(10, 20, "Buildings", "Action", "Message \"hello\"");

        var json = StateTileActionsHandler.Handle(p, world);
        var action = Assert.Single(json.GetProperty("actions").EnumerateArray());

        Assert.Equal("Buildings", action.GetProperty("layer").GetString());
        Assert.Equal("Action", action.GetProperty("property").GetString());
    }

    private sealed class FakeTileActionsWorld : ITileActionsWorld
    {
        private readonly Dictionary<(int X, int Y, string Layer, string Property), string> _properties = new();

        public bool IsWorldReady => true;
        public string CurrentLocationName { get; init; } = "Custom_BlueMoonVineyard";
        public int PlayerTileX => 10;
        public int PlayerTileY => 20;
        public IReadOnlyList<string> LayerNames { get; } = new[] { "Back", "Buildings" };

        public void Set(int x, int y, string layer, string property, string value)
            => _properties[(x, y, layer, property)] = value;

        public string? GetTileProperty(int x, int y, string layer, string property)
            => _properties.TryGetValue((x, y, layer, property), out var value) ? value : null;
    }
}
