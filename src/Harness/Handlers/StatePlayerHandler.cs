using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
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
            MailReceived = world.MailReceived.ToList(),
            MailForTomorrow = world.MailForTomorrow.ToList(),
            EventsSeen = world.EventsSeen.ToList(),
            SecretNotesSeen = world.SecretNotesSeen.ToList(),
            Swimming = world.Swimming,
            BathingClothes = world.BathingClothes,
            IsBusy = world.IsBusy,
            CanMove = world.CanMove,
            Buffs = world.Buffs
                .Select(b => new PlayerBuffSummary
                {
                    Id = b.Id,
                    DisplayName = b.DisplayName,
                    Source = b.Source,
                    MillisecondsDuration = b.MillisecondsDuration,
                    TotalMillisecondsDuration = b.TotalMillisecondsDuration,
                    Effects = b.Effects,
                    RuntimeType = b.RuntimeType,
                })
                .ToList(),
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
    IReadOnlyList<string> MailReceived { get; }
    IReadOnlyList<string> MailForTomorrow { get; }
    IReadOnlyList<string> EventsSeen { get; }
    IReadOnlyList<int> SecretNotesSeen { get; }
    bool Swimming { get; }
    bool BathingClothes { get; }
    bool IsBusy { get; }
    bool CanMove { get; }
    IReadOnlyList<IPlayerBuffSummary> Buffs { get; }
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

internal interface IPlayerBuffSummary
{
    string? Id { get; }
    string? DisplayName { get; }
    string? Source { get; }
    int? MillisecondsDuration { get; }
    int? TotalMillisecondsDuration { get; }
    PlayerBuffEffects Effects { get; }
    string? RuntimeType { get; }
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

internal sealed record PlayerBuffProjection(
    string? Id,
    string? DisplayName,
    string? Source,
    int? MillisecondsDuration,
    int? TotalMillisecondsDuration,
    PlayerBuffEffects Effects,
    string? RuntimeType) : IPlayerBuffSummary;

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
    public IReadOnlyList<string> MailReceived => Player.mailReceived.Select(m => m ?? string.Empty).ToList();
    public IReadOnlyList<string> MailForTomorrow
        => ReflectionValue.ReadStringList(
            ReflectionValue.ReadRaw(Player, "mailForTomorrow", "MailForTomorrow"))
            .ToList();
    public IReadOnlyList<string> EventsSeen => Player.eventsSeen.Select(e => e.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();
    public IReadOnlyList<int> SecretNotesSeen => Player.secretNotesSeen.ToList();
    public bool Swimming => ReadBoolValue(Player, "swimming", "Swimming") ?? false;
    public bool BathingClothes => ReadBoolValue(Player, "bathingClothes", "BathingClothes") ?? false;
    public bool IsBusy => ReflectionValue.TryInvokeBool(Player, "isBusy", [], out var busy) && busy;
    public bool CanMove => ReadBoolValue(Player, "CanMove", "canMove") ?? false;
    public IReadOnlyList<IPlayerBuffSummary> Buffs => ProjectBuffs(ReflectionValue.ReadRaw(Player, "buffs", "Buffs")).ToList();

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

    private static IEnumerable<IPlayerBuffSummary> ProjectBuffs(object? rawBuffs)
    {
        foreach (var buff in EnumerateActiveBuffs(rawBuffs))
        {
            var projection = ProjectBuff(buff);
            if (projection is not null)
            {
                yield return projection;
            }
        }
    }

    private static IEnumerable<object> EnumerateActiveBuffs(object? rawBuffs)
    {
        var unwrapped = Unwrap(rawBuffs);
        if (unwrapped is null)
        {
            yield break;
        }

        var memberValues = new List<object>();
        foreach (var memberName in new[] { "AppliedBuffs", "appliedBuffs", "Buffs", "buffs" })
        {
            foreach (var buff in EnumerateValues(ReflectionValue.ReadRaw(unwrapped, memberName)))
            {
                memberValues.Add(buff);
            }
        }

        var values = memberValues.Count > 0
            ? memberValues
            : EnumerateValues(unwrapped).ToList();

        foreach (var value in values)
        {
            yield return value;
        }
    }

    private static IEnumerable<object> EnumerateValues(object? raw)
    {
        var unwrapped = Unwrap(raw);
        if (unwrapped is null || unwrapped is string)
        {
            yield break;
        }

        var dictionaryValues = ReflectionValue.ReadDictionary(unwrapped)
            .Select(pair => Unwrap(pair.Value))
            .Where(value => value is not null)
            .Cast<object>()
            .ToList();
        if (dictionaryValues.Count > 0)
        {
            foreach (var value in dictionaryValues)
            {
                yield return value;
            }

            yield break;
        }

        var enumerableValues = ReflectionValue.ReadEnumerable(unwrapped)
            .Select(Unwrap)
            .Where(value => value is not null)
            .Cast<object>()
            .ToList();
        if (enumerableValues.Count > 0)
        {
            foreach (var value in enumerableValues)
            {
                yield return value;
            }

            yield break;
        }

        yield return unwrapped;
    }

    private static PlayerBuffProjection? ProjectBuff(object buff)
    {
        var effects = ProjectBuffEffects(ReadRawValue(buff, "effects", "Effects"));
        var id = ReadStringValue(buff, "Id", "ID", "id");
        var displayName = ReadStringValue(buff, "DisplayName", "displayName", "Name", "name");
        var source = ReadStringValue(buff, "Source", "source");
        var millisecondsDuration = ReadIntValue(buff, "MillisecondsDuration", "millisecondsDuration", "Duration", "duration");
        var totalMillisecondsDuration = ReadIntValue(
            buff,
            "TotalMillisecondsDuration",
            "totalMillisecondsDuration",
            "TotalDuration",
            "totalDuration");

        if (id is null
            && displayName is null
            && source is null
            && millisecondsDuration is null
            && totalMillisecondsDuration is null
            && !HasAnyEffect(effects))
        {
            return null;
        }

        return new PlayerBuffProjection(
            id,
            displayName,
            source,
            millisecondsDuration,
            totalMillisecondsDuration,
            effects,
            buff.GetType().Name);
    }

    private static PlayerBuffEffects ProjectBuffEffects(object? effects)
    {
        return new PlayerBuffEffects
        {
            FarmingLevel = ReadIntValue(effects, "FarmingLevel", "farmingLevel", "Farming", "farming") ?? 0,
            FishingLevel = ReadIntValue(effects, "FishingLevel", "fishingLevel", "Fishing", "fishing") ?? 0,
            MiningLevel = ReadIntValue(effects, "MiningLevel", "miningLevel", "Mining", "mining") ?? 0,
            ForagingLevel = ReadIntValue(effects, "ForagingLevel", "foragingLevel", "Foraging", "foraging") ?? 0,
            LuckLevel = ReadIntValue(effects, "LuckLevel", "luckLevel", "Luck", "luck") ?? 0,
            Attack = ReadIntValue(effects, "Attack", "attack") ?? 0,
            Defense = ReadIntValue(effects, "Defense", "defense") ?? 0,
            Speed = ReadIntValue(effects, "Speed", "speed") ?? 0,
            MagnetRadius = ReadIntValue(effects, "MagnetRadius", "magnetRadius") ?? 0,
        };
    }

    private static bool HasAnyEffect(PlayerBuffEffects effects)
    {
        return effects.FarmingLevel != 0
            || effects.FishingLevel != 0
            || effects.MiningLevel != 0
            || effects.ForagingLevel != 0
            || effects.LuckLevel != 0
            || effects.Attack != 0
            || effects.Defense != 0
            || effects.Speed != 0
            || effects.MagnetRadius != 0;
    }

    private static object? ReadRawValue(object? source, params string[] names)
        => Unwrap(ReflectionValue.ReadRaw(source, names));

    private static string? ReadStringValue(object? source, params string[] names)
    {
        var raw = ReadRawValue(source, names);
        var text = raw?.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static int? ReadIntValue(object? source, params string[] names)
    {
        var raw = ReadRawValue(source, names);
        if (raw is null)
        {
            return null;
        }

        try
        {
            return Convert.ToInt32(raw, CultureInfo.InvariantCulture);
        }
        catch
        {
            return int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }
    }

    private static bool? ReadBoolValue(object? source, params string[] names)
    {
        var raw = ReadRawValue(source, names);
        if (raw is null)
        {
            return null;
        }

        if (raw is bool boolValue)
        {
            return boolValue;
        }

        return bool.TryParse(raw.ToString(), out var parsed) ? parsed : null;
    }

    private static object? Unwrap(object? raw)
    {
        if (raw is null || raw is string)
        {
            return raw;
        }

        var value = ReflectionValue.ReadRaw(raw, "Value", "value");
        return value is null || ReferenceEquals(value, raw) ? raw : value;
    }
}
