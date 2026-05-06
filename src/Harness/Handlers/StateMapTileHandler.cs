using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using xTile.Layers;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for the <c>state.map_tile</c> RPC method. Runs on the game thread.</summary>
public static class StateMapTileHandler
{
    public const string Method = "state.map_tile";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var req = RpcParams.Optional<MapTileRequest>(paramsElement);
        if (req.X < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.x must be >= 0");
        if (req.Y < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.y must be >= 0");

        var location = ResolveLocation(req.Location);
        var point = ResolvePoint(req);
        var layers = ResolveLayers(location, req.Layers);

        foreach (var layer in layers)
        {
            if (point.X >= layer.LayerWidth || point.Y >= layer.LayerHeight)
            {
                throw new JsonRpcException(
                    JsonRpcErrorCode.InvalidParams,
                    $"tile {point.X},{point.Y} outside layer {layer.Id} in {location.Name}");
            }
        }

        return ProtocolJson.ToElement(new MapTileState
        {
            Location = location.NameOrUniqueName ?? location.Name ?? string.Empty,
            X = point.X,
            Y = point.Y,
            Layers = layers.Select(layer => SnapshotLayer(layer, point)).ToList(),
        });
    }

    private static GameLocation ResolveLocation(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Game1.currentLocation
                ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                    $"{Method} requires a current location");
        }

        return Game1.getLocationFromName(name)
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"no location named: {name}");
    }

    private static TilePoint ResolvePoint(MapTileRequest req)
    {
        var playerTile = Game1.player?.TilePoint
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"{Method} requires a player when x or y is omitted");

        return new TilePoint
        {
            X = req.X ?? playerTile.X,
            Y = req.Y ?? playerTile.Y,
        };
    }

    private static List<Layer> ResolveLayers(GameLocation location, List<string>? requested)
    {
        var map = location.Map
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"location {location.Name} has no map");

        if (requested is null || requested.Count == 0)
            return map.Layers.ToList();

        var layers = new List<Layer>();
        foreach (var name in requested)
        {
            var layer = map.GetLayer(name);
            if (layer is null)
                throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, $"layer {name} not found in {location.Name}");
            layers.Add(layer);
        }

        return layers;
    }

    private static MapTileLayerState SnapshotLayer(Layer layer, TilePoint point)
    {
        var tile = layer.Tiles[point.X, point.Y];
        if (tile is null)
            return new MapTileLayerState { Name = layer.Id };

        var properties = new Dictionary<string, string>();
        foreach (var key in tile.Properties.Keys)
            properties[key] = tile.Properties[key]?.ToString() ?? string.Empty;

        return new MapTileLayerState
        {
            Name = layer.Id,
            TileIndex = tile.TileIndex,
            TileSheet = tile.TileSheet?.Id ?? string.Empty,
            Properties = properties,
        };
    }
}
