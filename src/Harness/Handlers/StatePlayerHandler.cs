using System;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for the <c>state.player</c> RPC method. Runs on the game thread.</summary>
public static class StatePlayerHandler
{
    public const string Method = "state.player";

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, new SdvPlayerStateWorld());

    internal static JsonElement Handle(JsonElement? paramsElement, IPlayerStateWorld world)
    {
        var state = new PlayerState
        {
            Name = world.Name,
            Money = world.Money,
            Stamina = world.Stamina,
            MaxStamina = world.MaxStamina,
            Health = world.Health,
            Location = world.Location,
            Tile = world.Tile,
            Items = world.Items
                .Select(i => new PlayerItemSummary
                {
                    Slot = i.Slot,
                    Id = i.Id,
                    ItemId = i.ItemId,
                    QualifiedId = i.QualifiedId,
                    Name = i.Name,
                    Stack = i.Stack,
                    Category = i.Category,
                    Quality = i.Quality,
                    RuntimeType = i.RuntimeType,
                })
                .ToList(),
        };
        return ProtocolJson.ToElement(state);
    }
}

internal interface IPlayerStateWorld
{
    string Name { get; }
    int Money { get; }
    int Stamina { get; }
    int MaxStamina { get; }
    int Health { get; }
    string Location { get; }
    TilePoint Tile { get; }
    IReadOnlyList<IPlayerInventoryItem> Items { get; }
}

internal interface IPlayerInventoryItem
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

internal sealed record PlayerInventoryItem(
    int Slot,
    string Id,
    string ItemId,
    string QualifiedId,
    string Name,
    int Stack,
    int? Category,
    int? Quality,
    string RuntimeType) : IPlayerInventoryItem;

internal sealed class SdvPlayerStateWorld : IPlayerStateWorld
{
    private Farmer Player => Game1.player;

    public string Name => Player.Name ?? string.Empty;
    public int Money => Player.Money;
    public int Stamina => (int)Player.Stamina;
    public int MaxStamina => Player.MaxStamina;
    public int Health => Player.health;
    public string Location => Game1.currentLocation?.Name ?? string.Empty;
    public TilePoint Tile => new() { X = Player.TilePoint.X, Y = Player.TilePoint.Y };

    public IReadOnlyList<IPlayerInventoryItem> Items
    {
        get
        {
            var items = new List<IPlayerInventoryItem>();
            for (int slot = 0; slot < Player.Items.Count; slot++)
            {
                if (Player.Items[slot] is not Item item)
                    continue;

                var qualifiedId = item.QualifiedItemId ?? item.ItemId ?? string.Empty;
                var itemId = item.ItemId ?? StripQualifiedPrefix(qualifiedId);

                items.Add(new PlayerInventoryItem(
                    slot,
                    qualifiedId,
                    itemId,
                    qualifiedId,
                    item.DisplayName ?? item.Name ?? string.Empty,
                    item.Stack,
                    item.Category,
                    item.Quality,
                    item.GetType().Name));
            }

            return items;
        }
    }

    internal static string StripQualifiedPrefix(string value)
    {
        if (value.Length > 0 && value[0] == '(')
        {
            var close = value.IndexOf(')', StringComparison.Ordinal);
            if (close >= 0 && close + 1 < value.Length)
                return value[(close + 1)..];
        }

        return value;
    }
}
