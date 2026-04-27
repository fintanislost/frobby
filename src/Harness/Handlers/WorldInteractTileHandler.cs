using System.Text.Json;
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// Handler for <c>world.interact_tile</c>. Interacts with furniture or a placed object
/// at a tile in the current location.
/// </summary>
public static class WorldInteractTileHandler
{
    public const string Method = "world.interact_tile";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var req = RpcParams.Required<InteractTileRequest>(paramsElement);
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
        var location = Game1.currentLocation
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"{Method} requires a current location");

        var tile = new Vector2(x, y);
        foreach (var furniture in location.furniture)
        {
            if ((int)furniture.TileLocation.X != x || (int)furniture.TileLocation.Y != y)
                continue;

            var handled = furniture.checkForAction(Game1.player, req.JustCheckingForActivity);
            return ProtocolJson.ToElement(new InteractTileResult
            {
                Tick = Game1.ticks,
                Handled = handled,
                TargetType = furniture.GetType().Name,
                Tile = new TilePoint { X = x, Y = y },
            });
        }

        if (location.Objects.TryGetValue(tile, out var obj))
        {
            var handled = obj.checkForAction(Game1.player, req.JustCheckingForActivity);
            return ProtocolJson.ToElement(new InteractTileResult
            {
                Tick = Game1.ticks,
                Handled = handled,
                TargetType = obj.GetType().Name,
                Tile = new TilePoint { X = x, Y = y },
            });
        }

        throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
            $"no furniture or object at tile {x},{y} in {location.Name}");
    }
}
