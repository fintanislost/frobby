using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class WorldPlaceObjectHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceObjectHandler.Handle(null, new FakeObjectPlacementWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_MissingId_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"x\":8,\"y\":9}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceObjectHandler.Handle(p, new FakeObjectPlacementWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("id", ex.Message);
    }

    [Fact]
    public void Handle_MissingX_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"id\":\"(O)388\",\"y\":9}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceObjectHandler.Handle(p, new FakeObjectPlacementWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("x", ex.Message);
    }

    [Fact]
    public void Handle_MissingY_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"id\":\"(O)388\",\"x\":8}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceObjectHandler.Handle(p, new FakeObjectPlacementWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("y", ex.Message);
    }

    [Theory]
    [InlineData("{\"id\":\"(O)388\",\"x\":-1,\"y\":9}", "x")]
    [InlineData("{\"id\":\"(O)388\",\"x\":8,\"y\":-1}", "y")]
    [InlineData("{\"id\":\"(O)388\",\"x\":8,\"y\":9,\"stack\":0}", "stack")]
    [InlineData("{\"id\":\"(O)388\",\"x\":8,\"y\":9,\"quality\":-1}", "quality")]
    public void Handle_InvalidNumericParams_ThrowsInvalidParams(string json, string field)
    {
        var p = JsonDocument.Parse(json).RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceObjectHandler.Handle(p, new FakeObjectPlacementWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains(field, ex.Message);
    }

    [Fact]
    public void Handle_NoLoadedWorld_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"id\":\"(O)388\",\"x\":8,\"y\":9}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceObjectHandler.Handle(p, new FakeObjectPlacementWorld { IsWorldReady = false }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_UnknownItem_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"id\":\"(O)missing\",\"x\":8,\"y\":9}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceObjectHandler.Handle(p, new FakeObjectPlacementWorld()));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("(O)missing", ex.Message);
    }

    [Fact]
    public void Handle_NonObjectItem_ThrowsGameStateInvalid()
    {
        var world = new FakeObjectPlacementWorld();
        world.Items["(F)1302"] = null;
        var p = JsonDocument.Parse("{\"id\":\"(F)1302\",\"x\":8,\"y\":9}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceObjectHandler.Handle(p, world));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("not an object", ex.Message);
    }

    [Fact]
    public void Handle_PlacesObjectAndReturnsMetadata()
    {
        var world = new FakeObjectPlacementWorld();
        world.Items["(BC)Example_Golden_Piggy_Bank"] = new FakePlaceableObject
        {
            Id = "Example_Golden_Piggy_Bank",
            QualifiedId = "(BC)Example_Golden_Piggy_Bank",
            Name = "Golden Piggy Bank",
            Stack = 1,
            Quality = 0,
            BigCraftable = true,
            RuntimeType = "Object",
        };
        var p = JsonDocument.Parse("{\"id\":\"(BC)Example_Golden_Piggy_Bank\",\"location\":\"FarmHouse\",\"x\":8,\"y\":9,\"stack\":2,\"quality\":1,\"remove_existing\":true}").RootElement;

        var json = WorldPlaceObjectHandler.Handle(p, world);
        var result = JsonSerializer.Deserialize<PlaceObjectResult>(json, ProtocolJson.Options)!;

        Assert.True(result.Ok);
        Assert.Equal(1234, result.Tick);
        Assert.Equal("Example_Golden_Piggy_Bank", result.Id);
        Assert.Equal("(BC)Example_Golden_Piggy_Bank", result.QualifiedId);
        Assert.Equal("Golden Piggy Bank", result.Name);
        Assert.Equal("FarmHouse", result.Location);
        Assert.Equal(8, result.Tile.X);
        Assert.Equal(9, result.Tile.Y);
        Assert.True(result.BigCraftable);
        Assert.Equal("Object", result.RuntimeType);
        Assert.Equal("FarmHouse", world.PlacedLocation);
        Assert.Equal(8, world.PlacedX);
        Assert.Equal(9, world.PlacedY);
        Assert.True(world.LastRemoveExisting);
        Assert.Equal(2, world.PlacedObject!.Stack);
        Assert.Equal(1, world.PlacedObject.Quality);
        var fakeObject = Assert.IsType<FakePlaceableObject>(world.PlacedObject);
        Assert.Equal("FarmHouse", fakeObject.PlacementLocation);
        Assert.Equal(8, fakeObject.PlacementX);
        Assert.Equal(9, fakeObject.PlacementY);
    }

    [Fact]
    public void Handle_WhitespaceLocation_ReturnsResolvedCurrentLocation()
    {
        var world = new FakeObjectPlacementWorld();
        world.Items["(O)388"] = new FakePlaceableObject
        {
            Id = "388",
            QualifiedId = "(O)388",
            Name = "Wood",
            Stack = 1,
            Quality = 0,
            RuntimeType = "Object",
        };
        var p = JsonDocument.Parse("{\"id\":\"(O)388\",\"location\":\" \",\"x\":2,\"y\":3}").RootElement;

        var json = WorldPlaceObjectHandler.Handle(p, world);
        var result = JsonSerializer.Deserialize<PlaceObjectResult>(json, ProtocolJson.Options)!;

        Assert.Equal("Farm", result.Location);
        Assert.Equal("Farm", world.PlacedLocation);
        var fakeObject = Assert.IsType<FakePlaceableObject>(world.PlacedObject);
        Assert.Equal("Farm", fakeObject.PlacementLocation);
        Assert.Equal(2, fakeObject.PlacementX);
        Assert.Equal(3, fakeObject.PlacementY);
    }

    private sealed class FakeObjectPlacementWorld : IObjectPlacementWorld
    {
        public bool IsWorldReady { get; init; } = true;
        public int Tick => 1234;
        public string CurrentLocation => "Farm";
        public Dictionary<string, IPlaceableObject?> Items { get; } = new();
        public string? PlacedLocation { get; private set; }
        public int? PlacedX { get; private set; }
        public int? PlacedY { get; private set; }
        public bool LastRemoveExisting { get; private set; }
        public IPlaceableObject? PlacedObject { get; private set; }

        public bool ItemExists(string id) => Items.ContainsKey(id);
        public IPlaceableObject? CreateObject(string id) => Items[id];

        public string PlaceObject(IPlaceableObject obj, string? location, int x, int y, bool removeExisting)
        {
            var resolvedLocation = string.IsNullOrWhiteSpace(location) ? CurrentLocation : location!;
            PlacedObject = obj;
            PlacedLocation = resolvedLocation;
            PlacedX = x;
            PlacedY = y;
            LastRemoveExisting = removeExisting;
            if (obj is FakePlaceableObject fakeObject)
                fakeObject.SetPlacement(resolvedLocation, x, y);

            return resolvedLocation;
        }
    }

    private sealed class FakePlaceableObject : IPlaceableObject
    {
        public string Id { get; init; } = string.Empty;
        public string QualifiedId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public int Stack { get; set; }
        public int Quality { get; set; }
        public bool BigCraftable { get; init; }
        public string RuntimeType { get; init; } = string.Empty;
        public string? PlacementLocation { get; private set; }
        public int? PlacementX { get; private set; }
        public int? PlacementY { get; private set; }

        public void SetPlacement(string location, int x, int y)
        {
            PlacementLocation = location;
            PlacementX = x;
            PlacementY = y;
        }
    }
}
