using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for the <c>state.special_orders</c> RPC method. Projects team special-order state.</summary>
public static class StateSpecialOrdersHandler
{
    public const string Method = "state.special_orders";

    private static readonly ISpecialOrdersWorld ProductionWorld = new SdvSpecialOrdersWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, ISpecialOrdersWorld world)
    {
        var state = new SpecialOrdersState
        {
            Active = world.Active.Select(ProjectOrder).ToList(),
            Available = world.Available.Select(ProjectOrder).ToList(),
            Completed = world.Completed.Select(value => value ?? string.Empty).ToList(),
            AcceptedTypes = world.AcceptedTypes.Select(value => value ?? string.Empty).ToList(),
            ReturnedDonations = world.ReturnedDonations.Select(ProjectItem).ToList(),
        };

        return ProtocolJson.ToElement(state);
    }

    private static SpecialOrderSummary ProjectOrder(ISpecialOrderSource order)
        => new()
        {
            Key = order.Key,
            Name = order.Name,
            Description = order.Description,
            Requester = order.Requester,
            OrderType = order.OrderType,
            SpecialRule = order.SpecialRule,
            Duration = order.Duration,
            DueDate = order.DueDate,
            State = order.State,
            ReadyForRemoval = order.ReadyForRemoval,
            IsTimed = order.IsTimed,
            RuntimeType = order.RuntimeType,
            SelectedRandomElements = ProjectKeyValues(order.SelectedRandomElements),
            PreselectedItems = ProjectKeyValues(order.PreselectedItems),
            Objectives = order.Objectives.Select(ProjectObjective).ToList(),
            Rewards = order.Rewards.Select(ProjectReward).ToList(),
            DonatedItems = order.DonatedItems.Select(ProjectItem).ToList(),
        };

    private static SpecialOrderObjectiveSummary ProjectObjective(ISpecialOrderObjectiveSource objective, int index)
        => new()
        {
            Index = index,
            Type = objective.Type,
            RuntimeType = objective.RuntimeType,
            Description = objective.Description,
            CurrentCount = objective.CurrentCount,
            MaxCount = objective.MaxCount,
            Complete = objective.Complete,
            DropBox = objective.DropBox,
            DropBoxLocation = objective.DropBoxLocation,
            DropBoxTile = objective.DropBoxTile,
            TargetName = objective.TargetName,
            AcceptedContextTags = objective.AcceptedContextTags.Select(value => value ?? string.Empty).ToList(),
            Confirmed = objective.Confirmed,
            MinimumCapacity = objective.MinimumCapacity,
        };

    private static SpecialOrderRewardSummary ProjectReward(ISpecialOrderRewardSource reward, int index)
        => new()
        {
            Index = index,
            Type = reward.Type,
            RuntimeType = reward.RuntimeType,
            Amount = reward.Amount,
            Mail = reward.Mail.Select(value => value ?? string.Empty).ToList(),
        };

    private static SpecialOrderItemSummary ProjectItem(ISpecialOrderItemSource item)
        => new()
        {
            Id = item.Id,
            ItemId = item.ItemId,
            QualifiedId = item.QualifiedId,
            Name = item.Name,
            Stack = item.Stack,
            Quality = item.Quality,
            Category = item.Category,
            RuntimeType = item.RuntimeType,
        };

    private static List<SpecialOrderKeyValueSummary> ProjectKeyValues(IReadOnlyDictionary<string, string> values)
        => values
            .Select(pair => new SpecialOrderKeyValueSummary
            {
                Key = pair.Key ?? string.Empty,
                Value = pair.Value ?? string.Empty,
            })
            .ToList();
}

internal interface ISpecialOrdersWorld
{
    IReadOnlyList<ISpecialOrderSource> Active { get; }
    IReadOnlyList<ISpecialOrderSource> Available { get; }
    IReadOnlyList<string> Completed { get; }
    IReadOnlyList<string> AcceptedTypes { get; }
    IReadOnlyList<ISpecialOrderItemSource> ReturnedDonations { get; }
}

internal interface ISpecialOrderSource
{
    string Key { get; }
    string Name { get; }
    string Description { get; }
    string Requester { get; }
    string OrderType { get; }
    string SpecialRule { get; }
    string Duration { get; }
    int? DueDate { get; }
    string State { get; }
    bool? ReadyForRemoval { get; }
    bool? IsTimed { get; }
    string RuntimeType { get; }
    IReadOnlyDictionary<string, string> SelectedRandomElements { get; }
    IReadOnlyDictionary<string, string> PreselectedItems { get; }
    IReadOnlyList<ISpecialOrderObjectiveSource> Objectives { get; }
    IReadOnlyList<ISpecialOrderRewardSource> Rewards { get; }
    IReadOnlyList<ISpecialOrderItemSource> DonatedItems { get; }
}

internal interface ISpecialOrderObjectiveSource
{
    string Type { get; }
    string RuntimeType { get; }
    string Description { get; }
    int? CurrentCount { get; }
    int? MaxCount { get; }
    bool? Complete { get; }
    string DropBox { get; }
    string DropBoxLocation { get; }
    TilePoint? DropBoxTile { get; }
    string TargetName { get; }
    IReadOnlyList<string> AcceptedContextTags { get; }
    bool? Confirmed { get; }
    int? MinimumCapacity { get; }
}

internal interface ISpecialOrderRewardSource
{
    string Type { get; }
    string RuntimeType { get; }
    int? Amount { get; }
    IReadOnlyList<string> Mail { get; }
}

internal interface ISpecialOrderItemSource
{
    string Id { get; }
    string ItemId { get; }
    string QualifiedId { get; }
    string Name { get; }
    int Stack { get; }
    int? Quality { get; }
    int? Category { get; }
    string RuntimeType { get; }
}

internal sealed class SdvSpecialOrdersWorld : ISpecialOrdersWorld
{
    private object Team
    {
        get
        {
            RpcPreconditions.RequireWorldReady();
            return Game1.player.team;
        }
    }

    public IReadOnlyList<ISpecialOrderSource> Active
        => SpecialOrderReflection.ReadEnumerable(SpecialOrderReflection.ReadRaw(Team, "specialOrders", "SpecialOrders"))
            .Select(order => new SdvSpecialOrderSource(order))
            .ToList();

    public IReadOnlyList<ISpecialOrderSource> Available
        => SpecialOrderReflection.ReadEnumerable(SpecialOrderReflection.ReadRaw(Team, "availableSpecialOrders", "AvailableSpecialOrders"))
            .Select(order => new SdvSpecialOrderSource(order))
            .ToList();

    public IReadOnlyList<string> Completed
        => SpecialOrderReflection.ToStringList(SpecialOrderReflection.ReadRaw(Team, "completedSpecialOrders", "CompletedSpecialOrders"));

    public IReadOnlyList<string> AcceptedTypes
        => SpecialOrderReflection.ToStringList(SpecialOrderReflection.ReadRaw(Team, "acceptedSpecialOrderTypes", "AcceptedSpecialOrderTypes"));

    public IReadOnlyList<ISpecialOrderItemSource> ReturnedDonations
        => SpecialOrderReflection.ReadEnumerable(SpecialOrderReflection.ReadRaw(Team, "returnedDonations", "ReturnedDonations"))
            .OfType<Item>()
            .Select(item => new SdvSpecialOrderItemSource(item))
            .ToList();
}

internal sealed class SdvSpecialOrderSource : ISpecialOrderSource
{
    private readonly object _order;

    public SdvSpecialOrderSource(object order)
    {
        _order = order;
    }

    public string Key => SpecialOrderReflection.ReadString(_order, "questKey", "QuestKey", "key", "Key");
    public string Name => SpecialOrderReflection.ReadString(_order, "questName", "QuestName", "name", "Name");
    public string Description => SpecialOrderReflection.ReadString(_order, "questDescription", "QuestDescription", "description", "Description");
    public string Requester => SpecialOrderReflection.ReadString(_order, "requester", "Requester");
    public string OrderType => SpecialOrderReflection.ReadString(_order, "orderType", "OrderType");
    public string SpecialRule => SpecialOrderReflection.ReadString(_order, "specialRule", "SpecialRule");
    public string Duration => SpecialOrderReflection.ReadString(_order, "duration", "Duration");
    public int? DueDate => SpecialOrderReflection.ReadInt(_order, "dueDate", "DueDate");
    public string State => SpecialOrderReflection.ReadString(_order, "questState", "QuestState", "state", "State");
    public bool? ReadyForRemoval => SpecialOrderReflection.ReadBool(_order, "readyForRemoval", "ReadyForRemoval");
    public bool? IsTimed => SpecialOrderReflection.InvokeBool(_order, "IsTimedQuest", "isTimedQuest");
    public string RuntimeType => _order.GetType().Name;

    public IReadOnlyDictionary<string, string> SelectedRandomElements
        => SpecialOrderReflection.ToStringDictionary(
            SpecialOrderReflection.ReadRaw(_order, "selectedRandomElements", "SelectedRandomElements"));

    public IReadOnlyDictionary<string, string> PreselectedItems
        => SpecialOrderReflection.ToStringDictionary(
            SpecialOrderReflection.ReadRaw(_order, "preSelectedItems", "PreSelectedItems", "preselectedItems", "PreselectedItems"));

    public IReadOnlyList<ISpecialOrderObjectiveSource> Objectives
        => SpecialOrderReflection.ReadEnumerable(SpecialOrderReflection.ReadRaw(_order, "objectives", "Objectives"))
            .Select(objective => new SdvSpecialOrderObjectiveSource(objective))
            .ToList();

    public IReadOnlyList<ISpecialOrderRewardSource> Rewards
        => SpecialOrderReflection.ReadEnumerable(SpecialOrderReflection.ReadRaw(_order, "rewards", "Rewards"))
            .Select(reward => new SdvSpecialOrderRewardSource(reward))
            .ToList();

    public IReadOnlyList<ISpecialOrderItemSource> DonatedItems
        => SpecialOrderReflection.ReadEnumerable(SpecialOrderReflection.ReadRaw(_order, "donatedItems", "DonatedItems"))
            .OfType<Item>()
            .Select(item => new SdvSpecialOrderItemSource(item))
            .ToList();
}

internal sealed class SdvSpecialOrderObjectiveSource : ISpecialOrderObjectiveSource
{
    private readonly object _objective;

    public SdvSpecialOrderObjectiveSource(object objective)
    {
        _objective = objective;
    }

    public string Type
    {
        get
        {
            var explicitType = SpecialOrderReflection.ReadString(_objective, "type", "Type", "objectiveType", "ObjectiveType");
            return explicitType.Length > 0 ? explicitType : SpecialOrderReflection.TrimSuffix(RuntimeType, "Objective");
        }
    }

    public string RuntimeType => _objective.GetType().Name;
    public string Description => SpecialOrderReflection.ReadString(_objective, "description", "Description", "_description");
    public int? CurrentCount => SpecialOrderReflection.ReadInt(_objective, "currentCount", "CurrentCount");
    public int? MaxCount => SpecialOrderReflection.ReadInt(_objective, "maxCount", "MaxCount", "requiredCount", "RequiredCount");

    public bool? Complete
    {
        get
        {
            var explicitValue = SpecialOrderReflection.ReadBool(_objective, "complete", "Complete", "completed", "Completed");
            if (explicitValue.HasValue)
                return explicitValue.Value;

            var current = CurrentCount;
            var max = MaxCount;
            return current.HasValue && max.HasValue ? current.Value >= max.Value : null;
        }
    }

    public string DropBox => SpecialOrderReflection.ReadString(_objective, "dropBox", "DropBox");
    public string DropBoxLocation => SpecialOrderReflection.ReadString(_objective, "dropBoxGameLocation", "DropBoxGameLocation", "dropBoxLocation", "DropBoxLocation");

    public TilePoint? DropBoxTile
        => SpecialOrderReflection.ToTilePoint(
            SpecialOrderReflection.ReadRaw(_objective, "dropBoxTileLocation", "DropBoxTileLocation", "dropBoxTile", "DropBoxTile"));

    public string TargetName => SpecialOrderReflection.ReadString(_objective, "targetName", "TargetName");

    public IReadOnlyList<string> AcceptedContextTags
        => SpecialOrderReflection.ToStringList(
            SpecialOrderReflection.ReadRaw(
                _objective,
                "acceptedContextTags",
                "AcceptedContextTags",
                "acceptableContextTagSets",
                "AcceptableContextTagSets"));

    public bool? Confirmed => SpecialOrderReflection.ReadBool(_objective, "confirmed", "Confirmed");
    public int? MinimumCapacity => SpecialOrderReflection.ReadInt(_objective, "minimumCapacity", "MinimumCapacity");
}

internal sealed class SdvSpecialOrderRewardSource : ISpecialOrderRewardSource
{
    private readonly object _reward;

    public SdvSpecialOrderRewardSource(object reward)
    {
        _reward = reward;
    }

    public string Type
    {
        get
        {
            var explicitType = SpecialOrderReflection.ReadString(_reward, "type", "Type", "rewardType", "RewardType");
            return explicitType.Length > 0 ? explicitType : RuntimeType;
        }
    }

    public string RuntimeType => _reward.GetType().Name;
    public int? Amount => SpecialOrderReflection.ReadInt(_reward, "amount", "Amount", "money", "Money");

    public IReadOnlyList<string> Mail
        => SpecialOrderReflection.ToStringList(
            SpecialOrderReflection.ReadRaw(_reward, "mail", "Mail", "grantedMail", "GrantedMail", "grantedMails", "GrantedMails"));
}

internal sealed class SdvSpecialOrderItemSource : ISpecialOrderItemSource
{
    private readonly Item _item;

    public SdvSpecialOrderItemSource(Item item)
    {
        _item = item;
    }

    public string Id => QualifiedId;
    public string ItemId => _item.ItemId ?? SpecialOrderReflection.StripQualifiedPrefix(QualifiedId);
    public string QualifiedId => _item.QualifiedItemId ?? _item.ItemId ?? string.Empty;
    public string Name => _item.DisplayName ?? _item.Name ?? string.Empty;
    public int Stack => _item.Stack;
    public int? Quality => _item.Quality;
    public int? Category => _item.Category;
    public string RuntimeType => _item.GetType().Name;
}

internal static class SpecialOrderReflection
{
    private static readonly BindingFlags MemberFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    public static object? ReadRaw(object? source, params string[] names)
    {
        source = UnwrapValue(source);
        if (source is null)
            return null;

        for (var type = source.GetType(); type is not null; type = type.BaseType)
        {
            foreach (var name in names)
            {
                var property = type.GetProperties(MemberFlags)
                    .FirstOrDefault(candidate =>
                        candidate.GetIndexParameters().Length == 0
                        && string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
                if (property is not null)
                    return UnwrapValue(property.GetValue(source));

                var field = type.GetFields(MemberFlags)
                    .FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
                if (field is not null)
                    return UnwrapValue(field.GetValue(source));
            }
        }

        return null;
    }

    public static IEnumerable<object> ReadEnumerable(object? source)
    {
        source = UnwrapValue(source);
        if (source is null || source is string)
            yield break;

        if (source is IEnumerable enumerable)
        {
            foreach (var value in enumerable)
            {
                var unwrapped = UnwrapValue(value);
                if (unwrapped is not null)
                    yield return unwrapped;
            }
        }
    }

    public static IReadOnlyDictionary<string, string> ToStringDictionary(object? source)
    {
        source = UnwrapValue(source);
        var values = new Dictionary<string, string>();
        if (source is null)
            return values;

        if (source is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
                values[ToStringValue(entry.Key)] = ToStringValue(entry.Value);

            return values;
        }

        if (source is IEnumerable enumerable && source is not string)
        {
            foreach (var entry in enumerable)
            {
                var key = ReadRaw(entry, "Key");
                var value = ReadRaw(entry, "Value");
                var keyText = ToStringValue(key);
                if (keyText.Length > 0)
                    values[keyText] = ToStringValue(value);
            }
        }

        return values;
    }

    public static IReadOnlyList<string> ToStringList(object? source)
    {
        source = UnwrapValue(source);
        if (source is null)
            return new List<string>();

        if (source is string text)
            return SplitTextList(text);

        if (source is IEnumerable enumerable)
        {
            var values = new List<string>();
            foreach (var value in enumerable)
                values.AddRange(SplitTextList(ToStringValue(value)));

            return values;
        }

        return SplitTextList(ToStringValue(source));
    }

    public static string ReadString(object? source, params string[] names)
        => ToStringValue(ReadRaw(source, names));

    public static int? ReadInt(object? source, params string[] names)
    {
        var value = UnwrapValue(ReadRaw(source, names));
        if (value is null)
            return null;

        if (value is int intValue)
            return intValue;

        if (value is IConvertible convertible
            && int.TryParse(convertible.ToString(CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var converted))
            return converted;

        return int.TryParse(ToStringValue(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    public static bool? ReadBool(object? source, params string[] names)
    {
        var value = UnwrapValue(ReadRaw(source, names));
        if (value is null)
            return null;

        if (value is bool boolValue)
            return boolValue;

        return bool.TryParse(ToStringValue(value), out var parsed) ? parsed : null;
    }

    public static bool? InvokeBool(object source, params string[] names)
    {
        foreach (var name in names)
        {
            for (var type = source.GetType(); type is not null; type = type.BaseType)
            {
                var method = type.GetMethods(MemberFlags)
                    .FirstOrDefault(candidate =>
                        candidate.GetParameters().Length == 0
                        && string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
                if (method is null)
                    continue;

                var result = UnwrapValue(method.Invoke(source, Array.Empty<object>()));
                if (result is bool boolValue)
                    return boolValue;

                if (bool.TryParse(ToStringValue(result), out var parsed))
                    return parsed;
            }
        }

        return null;
    }

    public static TilePoint? ToTilePoint(object? source)
    {
        source = UnwrapValue(source);
        if (source is null)
            return null;

        if (source is Vector2 vector)
            return new TilePoint { X = (int)vector.X, Y = (int)vector.Y };

        if (source is Point point)
            return new TilePoint { X = point.X, Y = point.Y };

        var x = ReadInt(source, "X", "x");
        var y = ReadInt(source, "Y", "y");
        return x.HasValue && y.HasValue ? new TilePoint { X = x.Value, Y = y.Value } : null;
    }

    public static string TrimSuffix(string value, string suffix)
        => value.EndsWith(suffix, StringComparison.Ordinal) ? value[..^suffix.Length] : value;

    public static string StripQualifiedPrefix(string value)
    {
        if (value.Length > 0 && value[0] == '(')
        {
            var close = value.IndexOf(')', StringComparison.Ordinal);
            if (close >= 0 && close + 1 < value.Length)
                return value[(close + 1)..];
        }

        return value;
    }

    private static object? UnwrapValue(object? value)
    {
        var current = value;
        for (var i = 0; i < 6; i++)
        {
            if (current is null || current is string || current is IEnumerable || current.GetType().IsEnum)
                return current;

            var type = current.GetType();
            if (type.IsValueType
                && type.FullName is string fullName
                && fullName.StartsWith("System.Collections.Generic.KeyValuePair", StringComparison.Ordinal))
                return current;

            if (type.IsPrimitive || current is decimal)
                return current;

            var property = type.GetProperties(MemberFlags)
                .FirstOrDefault(candidate =>
                    candidate.GetIndexParameters().Length == 0
                    && string.Equals(candidate.Name, "Value", StringComparison.OrdinalIgnoreCase));
            if (property is null)
                return current;

            var next = property.GetValue(current);
            if (ReferenceEquals(next, current))
                return current;

            current = next;
        }

        return current;
    }

    private static string ToStringValue(object? value)
    {
        value = UnwrapValue(value);
        return value switch
        {
            null => string.Empty,
            string text => text,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
    }

    private static List<string> SplitTextList(string text)
        => text
            .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .ToList();
}
