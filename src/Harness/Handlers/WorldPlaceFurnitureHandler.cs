using System.Text.Json;
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// Handler for <c>world.place_furniture</c>. Creates furniture through SDV's
/// <see cref="ItemRegistry"/> and adds it to a loaded location's furniture collection.
/// </summary>
public static class WorldPlaceFurnitureHandler
{
    public const string Method = "world.place_furniture";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var req = RpcParams.Required<PlaceFurnitureRequest>(paramsElement);
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

        RpcPreconditions.RequireWorldReady();

        var x = req.X.Value;
        var y = req.Y.Value;
        var location = ResolveLocation(req.Location);
        if (!ItemRegistry.Exists(req.Id))
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"unknown item id: {req.Id}");

        var item = ItemRegistry.Create(req.Id);
        if (item is not StardewValley.Objects.Furniture furniture)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"item is not furniture: {req.Id}");

        var tile = new Vector2(x, y);
        if (req.RemoveExisting)
        {
            for (int i = location.furniture.Count - 1; i >= 0; i--)
            {
                var existing = location.furniture[i];
                if ((int)existing.TileLocation.X == x && (int)existing.TileLocation.Y == y)
                    location.furniture.RemoveAt(i);
            }
        }

        furniture.TileLocation = tile;
        location.furniture.Add(furniture);

        return ProtocolJson.ToElement(new PlaceFurnitureResult
        {
            Tick = Game1.ticks,
            Id = furniture.QualifiedItemId ?? req.Id,
            Location = location.Name ?? string.Empty,
            Tile = new TilePoint { X = x, Y = y },
        });
    }

    private static GameLocation ResolveLocation(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Game1.currentLocation
                ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                    $"{Method} requires a current location");

        return Game1.getLocationFromName(name)
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"no location named: {name}");
    }
}
