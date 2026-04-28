using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using StardewValley.Objects;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// Handler for <c>world.place_inventory_furniture</c>. Moves a matching furniture item
/// from the player's inventory into a loaded location.
/// </summary>
public static class WorldPlaceInventoryFurnitureHandler
{
    public const string Method = "world.place_inventory_furniture";

    private static readonly IInventoryFurnitureWorld ProductionWorld = new SdvInventoryFurnitureWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, IInventoryFurnitureWorld world)
    {
        var req = RpcParams.Required<PlaceInventoryFurnitureRequest>(paramsElement);
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

        if (!world.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "world.place_inventory_furniture requires a loaded world");

        var item = world.Items.FirstOrDefault(i => string.Equals(i.Id, req.Id, System.StringComparison.Ordinal))
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"inventory item not found: {req.Id}");

        if (!item.IsFurniture)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"inventory item is not furniture: {req.Id}");

        var x = req.X.Value;
        var y = req.Y.Value;
        world.PlaceFurniture(item, req.Location, x, y, req.RemoveExisting);

        return ProtocolJson.ToElement(new PlaceInventoryFurnitureResult
        {
            Tick = world.Tick,
            Id = item.Id,
            Location = req.Location ?? world.CurrentLocation,
            Tile = new TilePoint { X = x, Y = y },
            SourceSlot = item.Slot,
        });
    }
}

internal interface IInventoryFurnitureWorld
{
    bool IsWorldReady { get; }
    int Tick { get; }
    string CurrentLocation { get; }
    IReadOnlyList<IInventoryFurnitureItem> Items { get; }
    void PlaceFurniture(IInventoryFurnitureItem item, string? location, int x, int y, bool removeExisting);
}

internal interface IInventoryFurnitureItem
{
    int Slot { get; }
    string Id { get; }
    string Name { get; }
    bool IsFurniture { get; }
}

internal sealed class SdvInventoryFurnitureWorld : IInventoryFurnitureWorld
{
    public bool IsWorldReady => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;
    public int Tick => Game1.ticks;
    public string CurrentLocation => Game1.currentLocation?.Name ?? string.Empty;

    public IReadOnlyList<IInventoryFurnitureItem> Items
    {
        get
        {
            var items = new List<IInventoryFurnitureItem>();
            for (int slot = 0; slot < Game1.player.Items.Count; slot++)
            {
                if (Game1.player.Items[slot] is Item item)
                    items.Add(new SdvInventoryFurnitureItem(slot, item));
            }

            return items;
        }
    }

    public void PlaceFurniture(IInventoryFurnitureItem item, string? locationName, int x, int y, bool removeExisting)
    {
        if (item is not SdvInventoryFurnitureItem sdvItem)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "world.place_inventory_furniture can only place live inventory items");

        if (sdvItem.Item is not Furniture furniture)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"inventory item is not furniture: {item.Id}");

        var location = ResolveLocation(locationName);
        if (removeExisting)
        {
            for (int i = location.furniture.Count - 1; i >= 0; i--)
            {
                var existing = location.furniture[i];
                if ((int)existing.TileLocation.X == x && (int)existing.TileLocation.Y == y)
                    location.furniture.RemoveAt(i);
            }
        }

        Game1.player.Items[item.Slot] = null;
        furniture.TileLocation = new Vector2(x, y);
        location.furniture.Add(furniture);
    }

    private static GameLocation ResolveLocation(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Game1.currentLocation
                ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                    $"{WorldPlaceInventoryFurnitureHandler.Method} requires a current location");

        return Game1.getLocationFromName(name)
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"no location named: {name}");
    }
}

internal sealed class SdvInventoryFurnitureItem : IInventoryFurnitureItem
{
    public SdvInventoryFurnitureItem(int slot, Item item)
    {
        Slot = slot;
        Item = item;
    }

    public int Slot { get; }
    public Item Item { get; }
    public string Id => Item.QualifiedItemId ?? Item.ItemId ?? string.Empty;
    public string Name => Item.DisplayName ?? Item.Name ?? string.Empty;
    public bool IsFurniture => Item is Furniture;
}
