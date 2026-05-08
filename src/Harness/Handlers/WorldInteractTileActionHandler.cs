using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using xTile.Dimensions;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>world.interact_tile_action</c>. Runs map tile Action/TouchAction properties.</summary>
public static class WorldInteractTileActionHandler
{
    public const string Method = "world.interact_tile_action";

    private static readonly IWorldInteractTileActionWorld ProductionWorld = new SdvWorldInteractTileActionWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, IWorldInteractTileActionWorld world)
    {
        var req = RpcParams.Optional<InteractTileActionRequest>(paramsElement);
        if (req.X < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.x must be >= 0");
        if (req.Y < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.y must be >= 0");

        if (!world.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "no active save — mutation requires a loaded world");

        if (!string.IsNullOrWhiteSpace(req.Location)
            && !string.Equals(req.Location, world.CurrentLocationName, StringComparison.Ordinal))
        {
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"world.interact_tile_action location guard expected {req.Location}, current location is {world.CurrentLocationName}");
        }

        var x = req.X ?? world.PlayerTileX;
        var y = req.Y ?? world.PlayerTileY;
        var layers = ResolveLayers(req.Layers, world.LayerNames);
        var properties = string.IsNullOrWhiteSpace(req.Property)
            ? TileActionPropertyNames.DefaultOrder.ToList()
            : new List<string> { TileActionPropertyNames.Normalize(req.Property, "property") };

        var candidate = FindFirstAction(world, x, y, layers, properties)
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"no Action or TouchAction at tile {x},{y} in {world.CurrentLocationName}");

        var handled = candidate.Property == TileActionPropertyNames.Action
            ? world.PerformAction(candidate.Value, x, y, req.JustCheckingForActivity)
            : PerformTouchAction(world, candidate.Value, x, y);

        return ProtocolJson.ToElement(new InteractTileResult
        {
            Tick = world.Tick,
            Handled = handled,
            TargetType = "MapTileAction",
            ActionType = candidate.Property,
            Action = candidate.Value,
            Tile = new TilePoint { X = x, Y = y },
        });
    }

    private static bool PerformTouchAction(IWorldInteractTileActionWorld world, string action, int x, int y)
    {
        world.MovePlayerToTile(x, y);
        world.PerformTouchAction(action, x, y);
        return true;
    }

    private static TileActionCandidate? FindFirstAction(
        IWorldInteractTileActionWorld world,
        int x,
        int y,
        IReadOnlyList<string> layers,
        IReadOnlyList<string> properties)
    {
        foreach (var property in properties)
        {
            foreach (var layer in layers)
            {
                var value = world.GetTileProperty(x, y, layer, property);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return new TileActionCandidate
                    {
                        Tile = new TilePoint { X = x, Y = y },
                        Layer = layer,
                        Property = property,
                        Value = value,
                    };
                }
            }
        }

        return null;
    }

    internal static List<string> ResolveLayers(List<string>? requested, IReadOnlyList<string> worldLayers)
    {
        if (requested is null || requested.Count == 0)
            return worldLayers.ToList();

        return requested
            .Where(layer => !string.IsNullOrWhiteSpace(layer))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}

internal interface IWorldInteractTileActionWorld
{
    bool IsWorldReady { get; }
    string CurrentLocationName { get; }
    int Tick { get; }
    int PlayerTileX { get; }
    int PlayerTileY { get; }
    IReadOnlyList<string> LayerNames { get; }
    string? GetTileProperty(int x, int y, string layer, string property);
    bool PerformAction(string action, int x, int y, bool justCheckingForActivity);
    void MovePlayerToTile(int x, int y);
    void PerformTouchAction(string action, int x, int y);
}

internal sealed class SdvWorldInteractTileActionWorld : IWorldInteractTileActionWorld
{
    public bool IsWorldReady
        => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;

    public string CurrentLocationName => Location.NameOrUniqueName ?? Location.Name ?? string.Empty;
    public int Tick => Game1.ticks;
    public int PlayerTileX => Game1.player?.TilePoint.X ?? 0;
    public int PlayerTileY => Game1.player?.TilePoint.Y ?? 0;

    public IReadOnlyList<string> LayerNames
        => Location.Map?.Layers.Select(layer => layer.Id).ToList() ?? new List<string>();

    public string? GetTileProperty(int x, int y, string layer, string property)
        => Location.doesTileHaveProperty(x, y, property, layer, ignoreTileSheetProperties: false);

    public bool PerformAction(string action, int x, int y, bool justCheckingForActivity)
        => Location.performAction(action, Game1.player, new Location(x, y));

    public void MovePlayerToTile(int x, int y)
        => Game1.player.setTileLocation(new Vector2(x, y));

    public void PerformTouchAction(string action, int x, int y)
        => Location.performTouchAction(action, new Vector2(x, y));

    private static GameLocation Location
        => Game1.currentLocation
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"{WorldInteractTileActionHandler.Method} requires a current location");
}
