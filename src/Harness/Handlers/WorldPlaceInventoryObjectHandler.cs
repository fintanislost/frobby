using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using SObject = StardewValley.Object;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>world.place_inventory_object</c>. Places an existing inventory object through Stardew's native object placement path.</summary>
public static class WorldPlaceInventoryObjectHandler
{
    public const string Method = "world.place_inventory_object";

    private static readonly IInventoryObjectPlacementWorld ProductionWorld = new SdvInventoryObjectPlacementWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, IInventoryObjectPlacementWorld world)
    {
        var req = RpcParams.Required<PlaceInventoryObjectRequest>(paramsElement);
        ValidateRequest(req);

        if (!world.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "world.place_inventory_object requires a loaded world");

        if (!string.IsNullOrWhiteSpace(req.Location)
            && !string.Equals(req.Location.Trim(), world.CurrentLocation, StringComparison.Ordinal))
        {
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"world.place_inventory_object location guard expected {req.Location}, current location is {world.CurrentLocation}");
        }

        var item = SelectInventoryObject(world.Items, req);
        if (!item.IsObject)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"inventory item is not an object: {req.Id}");

        if (!string.IsNullOrWhiteSpace(req.Facing))
            world.FaceDirection(NormalizeDirection(req.Facing));

        var stackBefore = item.Stack;
        var x = req.X!.Value;
        var y = req.Y!.Value;
        if (!world.PlaceObject(item, x, y))
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"world.place_inventory_object could not place {req.Id} at tile {x},{y}");

        return ProtocolJson.ToElement(new PlaceInventoryObjectResult
        {
            Ok = true,
            Tick = world.Tick,
            Id = item.ItemId,
            QualifiedId = item.QualifiedId,
            Name = item.Name,
            Location = world.CurrentLocation,
            Tile = new TilePoint { X = x, Y = y },
            SourceSlot = item.Slot,
            StackBefore = stackBefore,
            StackAfter = item.Stack,
            RuntimeType = item.RuntimeType,
            Placed = true,
        });
    }

    private static void ValidateRequest(PlaceInventoryObjectRequest req)
    {
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
        if (req.Slot is < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.slot must be >= 0");
        if (!string.IsNullOrWhiteSpace(req.Facing) && !IsKnownDirection(req.Facing))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, $"unknown direction: {req.Facing}");
    }

    private static IInventoryObjectItem SelectInventoryObject(IReadOnlyList<IInventoryObjectItem> items, PlaceInventoryObjectRequest req)
    {
        var id = req.Id.Trim();
        var matches = items.Where(item =>
            string.Equals(item.QualifiedId, id, StringComparison.Ordinal)
            || string.Equals(item.ItemId, id, StringComparison.Ordinal));

        if (req.Slot is { } slot)
            matches = matches.Where(item => item.Slot == slot);

        return matches.FirstOrDefault()
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                req.Slot is { } requestedSlot
                    ? $"inventory item not found: {id} in slot {requestedSlot}"
                    : $"inventory item not found: {id}");
    }

    private static bool IsKnownDirection(string direction)
        => NormalizeDirection(direction) is "up" or "right" or "down" or "left";

    private static string NormalizeDirection(string direction)
        => direction.Trim().ToLowerInvariant();
}

internal interface IInventoryObjectPlacementWorld
{
    bool IsWorldReady { get; }
    int Tick { get; }
    string CurrentLocation { get; }
    IReadOnlyList<IInventoryObjectItem> Items { get; }
    void FaceDirection(string direction);
    bool PlaceObject(IInventoryObjectItem item, int x, int y);
}

internal interface IInventoryObjectItem
{
    int Slot { get; }
    string QualifiedId { get; }
    string ItemId { get; }
    string Name { get; }
    string RuntimeType { get; }
    int? Stack { get; }
    bool IsObject { get; }
}

internal sealed class SdvInventoryObjectPlacementWorld : IInventoryObjectPlacementWorld
{
    public bool IsWorldReady => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;
    public int Tick => Game1.ticks;
    public string CurrentLocation => CurrentLocationObject.NameOrUniqueName ?? CurrentLocationObject.Name ?? string.Empty;

    public IReadOnlyList<IInventoryObjectItem> Items
    {
        get
        {
            var items = new List<IInventoryObjectItem>();
            for (var slot = 0; slot < Game1.player.Items.Count; slot++)
            {
                if (Game1.player.Items[slot] is Item item)
                    items.Add(new SdvInventoryObjectItem(slot, item));
            }

            return items;
        }
    }

    public void FaceDirection(string direction)
    {
        Game1.player.faceDirection(DirectionToStardew(direction));
    }

    public bool PlaceObject(IInventoryObjectItem item, int x, int y)
    {
        if (item is not SdvInventoryObjectItem sdvItem)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "world.place_inventory_object can only place live inventory items");
        if (sdvItem.Item is not SObject obj)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"inventory item is not an object: {item.QualifiedId}");

        Game1.player.CurrentToolIndex = item.Slot;
        return obj.placementAction(CurrentLocationObject, x * Game1.tileSize, y * Game1.tileSize, Game1.player);
    }

    private static int DirectionToStardew(string direction)
        => direction switch
        {
            "up" => 0,
            "right" => 1,
            "down" => 2,
            "left" => 3,
            _ => throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, $"unknown direction: {direction}"),
        };

    private static GameLocation CurrentLocationObject
        => Game1.currentLocation
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"{WorldPlaceInventoryObjectHandler.Method} requires a current location");
}

internal sealed class SdvInventoryObjectItem : IInventoryObjectItem
{
    public SdvInventoryObjectItem(int slot, Item item)
    {
        Slot = slot;
        Item = item;
    }

    public int Slot { get; }
    public Item Item { get; }
    public string QualifiedId => Item.QualifiedItemId ?? string.Empty;
    public string ItemId => Item.ItemId ?? string.Empty;
    public string Name => Item.DisplayName ?? Item.Name ?? string.Empty;
    public string RuntimeType => Item.GetType().Name;
    public int? Stack => Item.Stack;
    public bool IsObject => Item is SObject;
}
