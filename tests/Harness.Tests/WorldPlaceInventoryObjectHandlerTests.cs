using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class WorldPlaceInventoryObjectHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceInventoryObjectHandler.Handle(null, new FakeInventoryObjectWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_MissingId_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"x\":9,\"y\":8}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceInventoryObjectHandler.Handle(p, new FakeInventoryObjectWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("id", ex.Message);
    }

    [Fact]
    public void Handle_MissingX_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"id\":\"(O)287\",\"y\":8}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceInventoryObjectHandler.Handle(p, new FakeInventoryObjectWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("x", ex.Message);
    }

    [Fact]
    public void Handle_MissingY_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"id\":\"(O)287\",\"x\":9}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceInventoryObjectHandler.Handle(p, new FakeInventoryObjectWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("y", ex.Message);
    }

    [Theory]
    [InlineData("{\"id\":\"(O)287\",\"x\":-1,\"y\":8}", "x")]
    [InlineData("{\"id\":\"(O)287\",\"x\":9,\"y\":-1}", "y")]
    [InlineData("{\"id\":\"(O)287\",\"x\":9,\"y\":8,\"slot\":-1}", "slot")]
    public void Handle_InvalidNumericParams_ThrowsInvalidParams(string json, string field)
    {
        var p = JsonDocument.Parse(json).RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceInventoryObjectHandler.Handle(p, new FakeInventoryObjectWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains(field, ex.Message);
    }

    [Fact]
    public void Handle_UnknownFacing_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"id\":\"(O)287\",\"x\":9,\"y\":8,\"facing\":\"sideways\"}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceInventoryObjectHandler.Handle(p, new FakeInventoryObjectWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("sideways", ex.Message);
    }

    [Fact]
    public void Handle_NoLoadedWorld_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"id\":\"(O)287\",\"x\":9,\"y\":8}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceInventoryObjectHandler.Handle(p, new FakeInventoryObjectWorld { IsWorldReady = false }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_LocationGuardMismatch_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"id\":\"(O)287\",\"location\":\"Town\",\"x\":9,\"y\":8}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceInventoryObjectHandler.Handle(p, new FakeInventoryObjectWorld { CurrentLocation = "Farm" }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("Town", ex.Message);
        Assert.Contains("Farm", ex.Message);
    }

    [Fact]
    public void Handle_MissingInventoryItem_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"id\":\"(O)287\",\"x\":9,\"y\":8}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceInventoryObjectHandler.Handle(p, new FakeInventoryObjectWorld()));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("(O)287", ex.Message);
    }

    [Fact]
    public void Handle_NonObjectInventoryItem_ThrowsGameStateInvalid()
    {
        var world = new FakeInventoryObjectWorld();
        world.Items.Add(new FakeInventoryObjectItem(3, "(W)5", "5", "Sword", "MeleeWeapon", 1, false));
        var p = JsonDocument.Parse("{\"id\":\"(W)5\",\"x\":9,\"y\":8}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceInventoryObjectHandler.Handle(p, world));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("not an object", ex.Message);
    }

    [Fact]
    public void Handle_SlotOverrideSelectsMatchingSlot()
    {
        var world = new FakeInventoryObjectWorld();
        world.Items.Add(new FakeInventoryObjectItem(2, "(O)287", "287", "Bomb", "Object", 2, true));
        world.Items.Add(new FakeInventoryObjectItem(7, "(O)287", "287", "Bomb", "Object", 5, true));
        var p = JsonDocument.Parse("{\"id\":\"(O)287\",\"x\":9,\"y\":8,\"slot\":7}").RootElement;

        var json = WorldPlaceInventoryObjectHandler.Handle(p, world);
        var result = JsonSerializer.Deserialize<PlaceInventoryObjectResult>(json, ProtocolJson.Options)!;

        Assert.True(result.Ok);
        Assert.Equal(7, result.SourceSlot);
        Assert.Equal(5, result.StackBefore);
        Assert.Equal(4, result.StackAfter);
        Assert.Equal(7, world.PlacedSlot);
    }

    [Fact]
    public void Handle_RawItemIdCanMatchInventoryObject()
    {
        var world = new FakeInventoryObjectWorld();
        world.Items.Add(new FakeInventoryObjectItem(2, "(O)287", "287", "Bomb", "Object", 1, true));
        var p = JsonDocument.Parse("{\"id\":\"287\",\"x\":9,\"y\":8}").RootElement;

        var json = WorldPlaceInventoryObjectHandler.Handle(p, world);
        var result = JsonSerializer.Deserialize<PlaceInventoryObjectResult>(json, ProtocolJson.Options)!;

        Assert.Equal("287", result.Id);
        Assert.Equal("(O)287", result.QualifiedId);
        Assert.Equal(2, result.SourceSlot);
    }

    [Fact]
    public void Handle_NativePlacementFailure_ThrowsGameStateInvalid()
    {
        var world = new FakeInventoryObjectWorld { PlacementSucceeds = false };
        world.Items.Add(new FakeInventoryObjectItem(2, "(O)287", "287", "Bomb", "Object", 1, true));
        var p = JsonDocument.Parse("{\"id\":\"(O)287\",\"x\":9,\"y\":8}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceInventoryObjectHandler.Handle(p, world));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("could not place", ex.Message);
    }

    [Fact]
    public void Handle_PlacesObjectAndReturnsMetadata()
    {
        var world = new FakeInventoryObjectWorld { CurrentLocation = "Frobby_CombatLab" };
        world.Items.Add(new FakeInventoryObjectItem(12, "(O)287", "287", "Bomb", "Object", 2, true));
        var p = JsonDocument.Parse("{\"id\":\"(O)287\",\"location\":\"Frobby_CombatLab\",\"x\":9,\"y\":8,\"facing\":\"right\"}").RootElement;

        var json = WorldPlaceInventoryObjectHandler.Handle(p, world);
        var result = JsonSerializer.Deserialize<PlaceInventoryObjectResult>(json, ProtocolJson.Options)!;

        Assert.True(result.Ok);
        Assert.Equal(1234, result.Tick);
        Assert.Equal("287", result.Id);
        Assert.Equal("(O)287", result.QualifiedId);
        Assert.Equal("Bomb", result.Name);
        Assert.Equal("Frobby_CombatLab", result.Location);
        Assert.Equal(9, result.Tile.X);
        Assert.Equal(8, result.Tile.Y);
        Assert.Equal(12, result.SourceSlot);
        Assert.Equal(2, result.StackBefore);
        Assert.Equal(1, result.StackAfter);
        Assert.Equal("Object", result.RuntimeType);
        Assert.True(result.Placed);
        Assert.Equal("right", world.FacedDirection);
        Assert.Equal(12, world.PlacedSlot);
        Assert.Equal(9, world.PlacedX);
        Assert.Equal(8, world.PlacedY);
    }

    private sealed class FakeInventoryObjectWorld : IInventoryObjectPlacementWorld
    {
        public bool IsWorldReady { get; init; } = true;
        public int Tick => 1234;
        public string CurrentLocation { get; init; } = "Frobby_CombatLab";
        public List<IInventoryObjectItem> Items { get; } = new();
        public bool PlacementSucceeds { get; init; } = true;
        public string? FacedDirection { get; private set; }
        public int? PlacedSlot { get; private set; }
        public int? PlacedX { get; private set; }
        public int? PlacedY { get; private set; }

        IReadOnlyList<IInventoryObjectItem> IInventoryObjectPlacementWorld.Items => Items;

        public void FaceDirection(string direction) => FacedDirection = direction;

        public bool PlaceObject(IInventoryObjectItem item, int x, int y)
        {
            PlacedSlot = item.Slot;
            PlacedX = x;
            PlacedY = y;
            if (!PlacementSucceeds)
                return false;

            if (item is FakeInventoryObjectItem fake)
                fake.Stack = System.Math.Max(0, (fake.Stack ?? 0) - 1);

            return true;
        }
    }

    private sealed class FakeInventoryObjectItem : IInventoryObjectItem
    {
        public FakeInventoryObjectItem(
            int slot,
            string qualifiedId,
            string itemId,
            string name,
            string runtimeType,
            int stack,
            bool isObject)
        {
            Slot = slot;
            QualifiedId = qualifiedId;
            ItemId = itemId;
            Name = name;
            RuntimeType = runtimeType;
            Stack = stack;
            IsObject = isObject;
        }

        public int Slot { get; }
        public string QualifiedId { get; }
        public string ItemId { get; }
        public string Name { get; }
        public string RuntimeType { get; }
        public int? Stack { get; set; }
        public bool IsObject { get; }
    }
}
