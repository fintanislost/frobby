using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>state.tile_actions</c>. Lists map Action/TouchAction candidates around a tile.</summary>
public static class StateTileActionsHandler
{
    public const string Method = "state.tile_actions";
    private const int MaxRadius = 25;

    private static readonly ITileActionsWorld ProductionWorld = new SdvTileActionsWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, ITileActionsWorld world)
    {
        var req = RpcParams.Optional<TileActionsRequest>(paramsElement);
        if (req.X < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.x must be >= 0");
        if (req.Y < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.y must be >= 0");
        if (req.Radius < 0 || req.Radius > MaxRadius)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, $"params.radius must be between 0 and {MaxRadius}");

        if (!world.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "state.tile_actions requires a loaded world");

        if (!string.IsNullOrWhiteSpace(req.Location)
            && !string.Equals(req.Location, world.CurrentLocationName, StringComparison.Ordinal))
        {
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"state.tile_actions location guard expected {req.Location}, current location is {world.CurrentLocationName}");
        }

        var x = req.X ?? world.PlayerTileX;
        var y = req.Y ?? world.PlayerTileY;
        var layers = WorldInteractTileActionHandler.ResolveLayers(req.Layers, world.LayerNames);
        var properties = TileActionPropertyNames.Resolve(req.Properties, "properties");
        var actions = Scan(world, x, y, req.Radius, layers, properties);

        return ProtocolJson.ToElement(new TileActionsState
        {
            Location = world.CurrentLocationName,
            X = x,
            Y = y,
            Radius = req.Radius,
            Actions = actions,
        });
    }

    private static List<TileActionCandidate> Scan(
        ITileActionsWorld world,
        int centerX,
        int centerY,
        int radius,
        IReadOnlyList<string> layers,
        IReadOnlyList<string> properties)
    {
        var actions = new List<TileActionCandidate>();
        for (var y = centerY - radius; y <= centerY + radius; y++)
        {
            if (y < 0)
                continue;

            for (var x = centerX - radius; x <= centerX + radius; x++)
            {
                if (x < 0)
                    continue;

                foreach (var property in properties)
                foreach (var layer in layers)
                {
                    var value = world.GetTileProperty(x, y, layer, property);
                    if (string.IsNullOrWhiteSpace(value))
                        continue;

                    actions.Add(new TileActionCandidate
                    {
                        Tile = new TilePoint { X = x, Y = y },
                        Layer = layer,
                        Property = property,
                        Value = value,
                        Distance = Math.Abs(x - centerX) + Math.Abs(y - centerY),
                    });
                }
            }
        }

        return actions
            .OrderBy(action => action.Distance)
            .ThenBy(action => action.Tile.Y)
            .ThenBy(action => action.Tile.X)
            .ThenBy(action => actions.IndexOf(action))
            .ToList();
    }
}

internal interface ITileActionsWorld
{
    bool IsWorldReady { get; }
    string CurrentLocationName { get; }
    int PlayerTileX { get; }
    int PlayerTileY { get; }
    IReadOnlyList<string> LayerNames { get; }
    string? GetTileProperty(int x, int y, string layer, string property);
}

internal sealed class SdvTileActionsWorld : ITileActionsWorld
{
    public bool IsWorldReady
        => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;

    public string CurrentLocationName => Location.NameOrUniqueName ?? Location.Name ?? string.Empty;
    public int PlayerTileX => Game1.player?.TilePoint.X ?? 0;
    public int PlayerTileY => Game1.player?.TilePoint.Y ?? 0;
    public IReadOnlyList<string> LayerNames
        => Location.Map?.Layers.Select(layer => layer.Id).ToList() ?? new List<string>();

    public string? GetTileProperty(int x, int y, string layer, string property)
        => Location.doesTileHaveProperty(x, y, property, layer, ignoreTileSheetProperties: false);

    private static GameLocation Location
        => Game1.currentLocation
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"{StateTileActionsHandler.Method} requires a current location");
}
