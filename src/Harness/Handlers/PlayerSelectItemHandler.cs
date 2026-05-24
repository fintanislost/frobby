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

/// <summary>Handler for <c>player.select_item</c>. Selects an existing farmer inventory slot.</summary>
public static class PlayerSelectItemHandler
{
    public const string Method = "player.select_item";

    private static readonly IPlayerInventorySelectionWorld ProductionWorld = new SdvPlayerInventorySelectionWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, IPlayerInventorySelectionWorld world)
    {
        var req = RpcParams.Required<PlayerSelectItemRequest>(paramsElement);
        ValidateRequest(req);

        if (!world.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "player.select_item requires a loaded world");

        var selected = req.Slot is { } slot
            ? SelectBySlot(world, slot)
            : SelectById(world, req.Id!.Trim(), req.PreferHotbar);

        world.SelectSlot(selected.Slot);

        return ProtocolJson.ToElement(new PlayerSelectItemResult
        {
            Ok = true,
            Tick = world.Tick,
            Slot = selected.Slot,
            Item = ToSummary(selected),
        });
    }

    private static void ValidateRequest(PlayerSelectItemRequest req)
    {
        var hasId = !string.IsNullOrWhiteSpace(req.Id);
        var hasSlot = req.Slot is not null;
        if (hasId == hasSlot)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "player.select_item requires exactly one of params.id or params.slot");
        if (req.Slot is < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.slot must be >= 0");
    }

    private static ISelectableInventoryItem SelectBySlot(IPlayerInventorySelectionWorld world, int slot)
    {
        if (slot >= world.InventoryCount)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                $"params.slot {slot} is out of range for inventory size {world.InventoryCount}");

        return world.Items.FirstOrDefault(i => i.Slot == slot)
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"inventory slot {slot} is empty");
    }

    private static ISelectableInventoryItem SelectById(
        IPlayerInventorySelectionWorld world,
        string id,
        bool preferHotbar)
    {
        var matches = world.Items
            .Where(i =>
                string.Equals(i.QualifiedId, id, StringComparison.Ordinal)
                || string.Equals(i.ItemId, id, StringComparison.Ordinal)
                || string.Equals(i.Id, id, StringComparison.Ordinal))
            .ToList();

        if (matches.Count == 0)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"inventory item not found: {id}");

        return preferHotbar
            ? matches.OrderBy(i => IsHotbarSlot(i.Slot) ? 0 : 1).ThenBy(i => i.Slot).First()
            : matches.First();
    }

    internal static PlayerItemSummary ToSummary(ISelectableInventoryItem item)
        => new()
        {
            Slot = item.Slot,
            Id = item.Id,
            ItemId = item.ItemId,
            QualifiedId = item.QualifiedId,
            Name = item.Name,
            Stack = item.Stack,
            Category = item.Category,
            Quality = item.Quality,
            RuntimeType = item.RuntimeType,
        };

    private static bool IsHotbarSlot(int slot) => slot is >= 0 and <= 11;
}

internal interface IPlayerInventorySelectionWorld
{
    bool IsWorldReady { get; }
    int Tick { get; }
    int InventoryCount { get; }
    IReadOnlyList<ISelectableInventoryItem> Items { get; }
    void SelectSlot(int slot);
}

internal interface ISelectableInventoryItem
{
    int Slot { get; }
    string Id { get; }
    string ItemId { get; }
    string QualifiedId { get; }
    string Name { get; }
    int Stack { get; }
    int? Category { get; }
    int? Quality { get; }
    string RuntimeType { get; }
}

internal sealed record SelectableInventoryItem(
    int Slot,
    string QualifiedId,
    string ItemId,
    string Name,
    int Stack,
    int? Category,
    int? Quality,
    string RuntimeType) : ISelectableInventoryItem
{
    public string Id => QualifiedId;
}

internal sealed class SdvPlayerInventorySelectionWorld : IPlayerInventorySelectionWorld
{
    public bool IsWorldReady => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;
    public int Tick => Game1.ticks;
    public int InventoryCount => Game1.player?.Items.Count ?? 0;

    public IReadOnlyList<ISelectableInventoryItem> Items
    {
        get
        {
            var items = new List<ISelectableInventoryItem>();
            if (Game1.player is null)
                return items;

            for (var slot = 0; slot < Game1.player.Items.Count; slot++)
            {
                if (Game1.player.Items[slot] is not Item item)
                    continue;

                var qualifiedId = item.QualifiedItemId ?? item.ItemId ?? string.Empty;
                var itemId = item.ItemId ?? SdvPlayerStateWorld.StripQualifiedPrefix(qualifiedId);
                items.Add(new SelectableInventoryItem(
                    slot,
                    qualifiedId,
                    itemId,
                    item.DisplayName ?? item.Name ?? string.Empty,
                    item.Stack,
                    item.Category,
                    item.Quality,
                    item.GetType().Name));
            }

            return items;
        }
    }

    public void SelectSlot(int slot)
    {
        Game1.player.CurrentToolIndex = slot;
    }
}
