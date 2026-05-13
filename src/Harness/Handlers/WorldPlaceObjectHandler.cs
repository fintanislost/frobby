using System.Text.Json;
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using SObject = StardewValley.Object;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// Handler for <c>world.place_object</c>. Creates Stardew objects through
/// <see cref="ItemRegistry"/> and places them into a loaded location's object table.
/// </summary>
public static class WorldPlaceObjectHandler
{
    public const string Method = "world.place_object";

    private static readonly IObjectPlacementWorld ProductionWorld = new SdvObjectPlacementWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, IObjectPlacementWorld world)
    {
        var req = RpcParams.Required<PlaceObjectRequest>(paramsElement);
        if (string.IsNullOrWhiteSpace(req.Id))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.id required");
        if (req.X is null)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.x required");
        if (req.X < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.x must be >= 0");
        if (req.Y is null)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.y required");
        if (req.Y < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.y must be >= 0");
        if (req.Stack is not null && req.Stack < 1)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.stack must be >= 1");
        if (req.Quality is not null && req.Quality < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.quality must be >= 0");

        if (!world.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "world.place_object requires a loaded world");

        if (!world.ItemExists(req.Id))
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"unknown item id: {req.Id}");

        var obj = world.CreateObject(req.Id)
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"item is not an object: {req.Id}");

        if (req.Stack is not null)
            obj.Stack = req.Stack.Value;
        if (req.Quality is not null)
            obj.Quality = req.Quality.Value;

        var x = req.X.Value;
        var y = req.Y.Value;
        var location = world.PlaceObject(obj, req.Location, x, y, req.RemoveExisting);

        return ProtocolJson.ToElement(new PlaceObjectResult
        {
            Tick = world.Tick,
            Id = obj.Id,
            QualifiedId = obj.QualifiedId,
            Name = obj.Name,
            Location = location,
            Tile = new TilePoint { X = x, Y = y },
            BigCraftable = obj.BigCraftable,
            RuntimeType = obj.RuntimeType,
        });
    }
}

internal interface IObjectPlacementWorld
{
    bool IsWorldReady { get; }
    int Tick { get; }
    string CurrentLocation { get; }
    bool ItemExists(string id);
    IPlaceableObject? CreateObject(string id);
    string PlaceObject(IPlaceableObject obj, string? location, int x, int y, bool removeExisting);
}

internal interface IPlaceableObject
{
    string Id { get; }
    string QualifiedId { get; }
    string Name { get; }
    int Stack { get; set; }
    int Quality { get; set; }
    bool BigCraftable { get; }
    string RuntimeType { get; }
}

internal sealed class SdvObjectPlacementWorld : IObjectPlacementWorld
{
    public bool IsWorldReady => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;
    public int Tick => Game1.ticks;
    public string CurrentLocation => Game1.currentLocation?.Name ?? string.Empty;

    public bool ItemExists(string id) => ItemRegistry.Exists(id);

    public IPlaceableObject? CreateObject(string id)
    {
        var item = ItemRegistry.Create(id);
        return item is SObject obj ? new SdvPlaceableObject(obj) : null;
    }

    public string PlaceObject(IPlaceableObject obj, string? locationName, int x, int y, bool removeExisting)
    {
        if (obj is not SdvPlaceableObject sdvObject)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "world.place_object can only place live Stardew objects");

        var location = ResolveLocation(locationName);
        var tile = new Vector2(x, y);
        if (removeExisting)
        {
            location.Objects.Remove(tile);
        }
        else if (location.Objects.ContainsKey(tile))
        {
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"object already exists at tile {x},{y}; pass remove_existing=true to replace it");
        }

        sdvObject.Object.Location = location;
        sdvObject.Object.TileLocation = tile;
        location.Objects[tile] = sdvObject.Object;
        return location.Name ?? string.Empty;
    }

    private static GameLocation ResolveLocation(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Game1.currentLocation
                ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                    $"{WorldPlaceObjectHandler.Method} requires a current location");

        return Game1.getLocationFromName(name)
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"no location named: {name}");
    }
}

internal sealed class SdvPlaceableObject : IPlaceableObject
{
    public SdvPlaceableObject(SObject obj)
    {
        Object = obj;
    }

    public SObject Object { get; }
    public string Id => Object.ItemId ?? string.Empty;
    public string QualifiedId => Object.QualifiedItemId ?? string.Empty;
    public string Name => Object.Name ?? Object.DisplayName ?? Object.GetType().Name;
    public int Stack { get => Object.Stack; set => Object.Stack = value; }
    public int Quality { get => Object.Quality; set => Object.Quality = value; }
    public bool BigCraftable => Object.bigCraftable.Value;
    public string RuntimeType => Object.GetType().Name;
}
