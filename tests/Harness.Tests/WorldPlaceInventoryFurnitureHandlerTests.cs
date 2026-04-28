using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class WorldPlaceInventoryFurnitureHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceInventoryFurnitureHandler.Handle(null, new FakeInventoryFurnitureWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_MissingId_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"x\":8,\"y\":9}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceInventoryFurnitureHandler.Handle(p, new FakeInventoryFurnitureWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("id", ex.Message);
    }

    [Fact]
    public void Handle_MissingX_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"id\":\"(F)terminal\",\"y\":9}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceInventoryFurnitureHandler.Handle(p, new FakeInventoryFurnitureWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("x", ex.Message);
    }

    [Fact]
    public void Handle_MissingY_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"id\":\"(F)terminal\",\"x\":8}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceInventoryFurnitureHandler.Handle(p, new FakeInventoryFurnitureWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("y", ex.Message);
    }

    [Fact]
    public void Handle_NoLoadedWorld_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"id\":\"(F)terminal\",\"x\":8,\"y\":9}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceInventoryFurnitureHandler.Handle(p, new FakeInventoryFurnitureWorld { IsWorldReady = false }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_MissingInventoryItem_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"id\":\"(F)missing\",\"x\":8,\"y\":9}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceInventoryFurnitureHandler.Handle(p, new FakeInventoryFurnitureWorld()));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("(F)missing", ex.Message);
    }

    [Fact]
    public void Handle_NonFurnitureInventoryItem_ThrowsGameStateInvalid()
    {
        var world = new FakeInventoryFurnitureWorld();
        world.Items.Add(new FakeInventoryFurnitureItem(6, "(O)388", "Wood", false));
        var p = JsonDocument.Parse("{\"id\":\"(O)388\",\"x\":8,\"y\":9}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceInventoryFurnitureHandler.Handle(p, world));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("not furniture", ex.Message);
    }

    [Fact]
    public void Handle_PlacesFurnitureFromInventoryAndReturnsSourceSlot()
    {
        var world = new FakeInventoryFurnitureWorld();
        world.Items.Add(new FakeInventoryFurnitureItem(5, "(F)stonks_starberg_terminal_v1", "Starberg Terminal - Model 4201", true));
        var p = JsonDocument.Parse("{\"id\":\"(F)stonks_starberg_terminal_v1\",\"location\":\"FarmHouse\",\"x\":8,\"y\":9,\"remove_existing\":true}").RootElement;

        var result = WorldPlaceInventoryFurnitureHandler.Handle(p, world);
        var placed = JsonSerializer.Deserialize<PlaceInventoryFurnitureResult>(result, ProtocolJson.Options)!;

        Assert.True(placed.Ok);
        Assert.Equal(1234, placed.Tick);
        Assert.Equal("(F)stonks_starberg_terminal_v1", placed.Id);
        Assert.Equal("FarmHouse", placed.Location);
        Assert.Equal(8, placed.Tile.X);
        Assert.Equal(9, placed.Tile.Y);
        Assert.Equal(5, placed.SourceSlot);
        Assert.Equal(5, world.RemovedSlot);
        Assert.Equal("(F)stonks_starberg_terminal_v1", world.PlacedItemId);
        Assert.Equal("FarmHouse", world.PlacedLocation);
        Assert.True(world.LastRemoveExisting);
        Assert.Empty(world.Items);
    }

    private sealed class FakeInventoryFurnitureWorld : IInventoryFurnitureWorld
    {
        public bool IsWorldReady { get; init; } = true;
        public int Tick => 1234;
        public string CurrentLocation => "FarmHouse";
        public List<IInventoryFurnitureItem> Items { get; } = new();
        public int? RemovedSlot { get; private set; }
        public string? PlacedItemId { get; private set; }
        public string? PlacedLocation { get; private set; }
        public bool LastRemoveExisting { get; private set; }

        IReadOnlyList<IInventoryFurnitureItem> IInventoryFurnitureWorld.Items => Items;

        public void PlaceFurniture(IInventoryFurnitureItem item, string? location, int x, int y, bool removeExisting)
        {
            RemovedSlot = item.Slot;
            PlacedItemId = item.Id;
            PlacedLocation = location;
            LastRemoveExisting = removeExisting;
            Items.Remove(Items.Single(i => i.Slot == item.Slot));
        }
    }

    private sealed record FakeInventoryFurnitureItem(
        int Slot,
        string Id,
        string Name,
        bool IsFurniture) : IInventoryFurnitureItem;
}
