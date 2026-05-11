using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for the <c>drop_box.deposit</c> RPC method.</summary>
public static class DropBoxDepositHandler
{
    public const string Method = "drop_box.deposit";

    private static readonly IDropBoxDepositWorld ProductionWorld = new SdvDropBoxDepositWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, IDropBoxDepositWorld world)
    {
        var req = RpcParams.Required<DropBoxDepositRequest>(paramsElement);
        Validate(req);

        var order = world.ActiveOrders.FirstOrDefault(order => string.Equals(order.Key, req.OrderKey, StringComparison.Ordinal))
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"drop_box.deposit found no active order '{req.OrderKey}'");
        var objective = order.Objectives.FirstOrDefault(objective => ObjectiveMatches(objective, req))
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"drop_box.deposit found no matching donation objective for order '{req.OrderKey}'");
        var selected = SelectInventory(world.PlayerInventory, req, objective);
        var before = objective.CurrentCount;

        order.Deposit(objective, selected, req.Count);

        return ProtocolJson.ToElement(new DropBoxDepositResult
        {
            Ok = true,
            OrderKey = order.Key,
            DropBox = objective.DropBox,
            DepositedCount = req.Count,
            ObjectiveIndex = objective.Index,
            BeforeCount = before,
            AfterCount = objective.CurrentCount,
            Item = selected.ToSummary(req.Count),
        });
    }

    private static void Validate(DropBoxDepositRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.OrderKey))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.order_key required");
        if (string.IsNullOrWhiteSpace(req.ItemId) && string.IsNullOrWhiteSpace(req.QualifiedId))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.item_id or params.qualified_id required");
        if (req.Count < 1)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.count must be >= 1");
    }

    private static bool ObjectiveMatches(IDropBoxDepositObjective objective, DropBoxDepositRequest req)
        => string.Equals(objective.Type, "Donate", StringComparison.Ordinal)
            && (string.IsNullOrWhiteSpace(req.DropBox) || string.Equals(objective.DropBox, req.DropBox, StringComparison.Ordinal));

    private static IDropBoxInventoryItem SelectInventory(
        IReadOnlyList<IDropBoxInventoryItem> inventory,
        DropBoxDepositRequest req,
        IDropBoxDepositObjective objective)
    {
        var item = inventory.FirstOrDefault(item =>
            (string.IsNullOrWhiteSpace(req.QualifiedId) || string.Equals(item.QualifiedId, req.QualifiedId, StringComparison.Ordinal))
            && (string.IsNullOrWhiteSpace(req.ItemId) || string.Equals(item.ItemId, req.ItemId, StringComparison.Ordinal))
            && item.Stack >= req.Count
            && ItemMatchesObjective(item, objective));

        return item ?? throw new JsonRpcException(
            JsonRpcErrorCode.GameStateInvalid,
            "drop_box.deposit found not enough matching inventory for objective");
    }

    private static bool ItemMatchesObjective(IDropBoxInventoryItem item, IDropBoxDepositObjective objective)
        => objective.AcceptedContextTags.Count == 0
            || objective.AcceptedContextTags.Any(tag => item.ContextTags.Contains(tag, StringComparer.OrdinalIgnoreCase));
}

internal interface IDropBoxDepositWorld
{
    IReadOnlyList<IDropBoxDepositOrder> ActiveOrders { get; }
    IReadOnlyList<IDropBoxInventoryItem> PlayerInventory { get; }
}

internal interface IDropBoxDepositOrder
{
    string Key { get; }
    IReadOnlyList<IDropBoxDepositObjective> Objectives { get; }
    void Deposit(IDropBoxDepositObjective objective, IDropBoxInventoryItem item, int count);
}

internal interface IDropBoxDepositObjective
{
    int Index { get; }
    string Type { get; }
    string DropBox { get; }
    IReadOnlyList<string> AcceptedContextTags { get; }
    int? CurrentCount { get; }
    int? MaxCount { get; }
}

internal interface IDropBoxInventoryItem
{
    string Id { get; }
    string ItemId { get; }
    string QualifiedId { get; }
    string Name { get; }
    int Stack { get; }
    int? Quality { get; }
    int? Category { get; }
    string RuntimeType { get; }
    IReadOnlyList<string> ContextTags { get; }
    SpecialOrderItemSummary ToSummary(int stack);
}

internal sealed class SdvDropBoxDepositWorld : IDropBoxDepositWorld
{
    private Farmer Player
    {
        get
        {
            RpcPreconditions.RequireWorldReady();
            return Game1.player;
        }
    }

    public IReadOnlyList<IDropBoxDepositOrder> ActiveOrders
        => SpecialOrderReflection.ReadEnumerable(
                SpecialOrderReflection.ReadRaw(Player.team, "specialOrders", "SpecialOrders"))
            .Select(order => new SdvDropBoxDepositOrder(order))
            .ToList();

    public IReadOnlyList<IDropBoxInventoryItem> PlayerInventory
    {
        get
        {
            var items = new List<IDropBoxInventoryItem>();
            for (var slot = 0; slot < Player.Items.Count; slot++)
            {
                if (Player.Items[slot] is Item item)
                    items.Add(new SdvDropBoxInventoryItem(item, slot));
            }

            return items;
        }
    }
}

internal sealed class SdvDropBoxDepositOrder : IDropBoxDepositOrder
{
    private readonly object _order;

    public SdvDropBoxDepositOrder(object order)
    {
        _order = order;
    }

    public string Key => SpecialOrderReflection.ReadString(_order, "questKey", "QuestKey", "key", "Key");

    public IReadOnlyList<IDropBoxDepositObjective> Objectives
        => SpecialOrderReflection.ReadEnumerable(SpecialOrderReflection.ReadRaw(_order, "objectives", "Objectives"))
            .Select((objective, index) => new SdvDropBoxDepositObjective(objective, index))
            .ToList();

    public void Deposit(IDropBoxDepositObjective objective, IDropBoxInventoryItem item, int count)
    {
        if (objective is not SdvDropBoxDepositObjective sdvObjective || item is not SdvDropBoxInventoryItem sdvItem)
            throw new JsonRpcException(JsonRpcErrorCode.InternalError, "drop_box.deposit received incompatible runtime wrappers");

        var donated = sdvItem.CloneForDeposit(count);
        var donatedItems = DropBoxDepositReflection.ReadMemberRaw(_order, "donatedItems", "DonatedItems");
        if (donatedItems is null || !DropBoxDepositReflection.TryAdd(donatedItems, donated))
            throw new JsonRpcException(
                JsonRpcErrorCode.InternalError,
                "drop_box.deposit could not append donated item to special order");

        var before = sdvObjective.CurrentCount.GetValueOrDefault();
        var after = before + count;
        if (sdvObjective.MaxCount is { } max)
            after = Math.Min(after, max);
        sdvObjective.SetCurrentCount(after);
        sdvObjective.SetConfirmed(true);
        sdvItem.RemoveStack(count);
    }
}

internal sealed class SdvDropBoxDepositObjective : IDropBoxDepositObjective
{
    private readonly object _objective;

    public SdvDropBoxDepositObjective(object objective, int index)
    {
        _objective = objective;
        Index = index;
    }

    public int Index { get; }

    public string Type
    {
        get
        {
            var explicitType = SpecialOrderReflection.ReadString(_objective, "type", "Type", "objectiveType", "ObjectiveType");
            return explicitType.Length > 0 ? explicitType : SpecialOrderReflection.TrimSuffix(RuntimeType, "Objective");
        }
    }

    private string RuntimeType => _objective.GetType().Name;
    public string DropBox => SpecialOrderReflection.ReadString(_objective, "dropBox", "DropBox");

    public IReadOnlyList<string> AcceptedContextTags
        => SpecialOrderReflection.ToStringList(
            SpecialOrderReflection.ReadRaw(
                _objective,
                "acceptedContextTags",
                "AcceptedContextTags",
                "acceptableContextTagSets",
                "AcceptableContextTagSets"));

    public int? CurrentCount => SpecialOrderReflection.ReadInt(_objective, "currentCount", "CurrentCount");
    public int? MaxCount => SpecialOrderReflection.ReadInt(_objective, "maxCount", "MaxCount", "requiredCount", "RequiredCount");

    public void SetCurrentCount(int value)
        => DropBoxDepositReflection.TrySetMember(_objective, value, "currentCount", "CurrentCount");

    public void SetConfirmed(bool value)
        => DropBoxDepositReflection.TrySetMember(_objective, value, "confirmed", "Confirmed");
}

internal sealed class SdvDropBoxInventoryItem : IDropBoxInventoryItem
{
    private readonly Item _item;
    private readonly int _slot;

    public SdvDropBoxInventoryItem(Item item, int slot)
    {
        _item = item;
        _slot = slot;
    }

    public string Id => QualifiedId;
    public string ItemId => _item.ItemId ?? SpecialOrderReflection.StripQualifiedPrefix(QualifiedId);
    public string QualifiedId => _item.QualifiedItemId ?? _item.ItemId ?? string.Empty;
    public string Name => _item.DisplayName ?? _item.Name ?? string.Empty;
    public int Stack => _item.Stack;
    public int? Quality => _item.Quality;
    public int? Category => _item.Category;
    public string RuntimeType => _item.GetType().Name;
    public IReadOnlyList<string> ContextTags => DropBoxDepositReflection.ReadContextTags(_item);

    public SpecialOrderItemSummary ToSummary(int stack)
        => new()
        {
            Id = Id,
            ItemId = ItemId,
            QualifiedId = QualifiedId,
            Name = Name,
            Stack = stack,
            Quality = Quality,
            Category = Category,
            RuntimeType = RuntimeType,
        };

    public Item CloneForDeposit(int count)
    {
        var clone = _item.getOne();
        clone.Stack = count;
        return clone;
    }

    public void RemoveStack(int count)
    {
        _item.Stack -= count;
        if (_item.Stack <= 0)
            Game1.player.Items[_slot] = null;
    }
}

internal static class DropBoxDepositReflection
{
    private static readonly BindingFlags MemberFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    public static object? ReadMemberRaw(object source, params string[] names)
    {
        for (var type = source.GetType(); type is not null; type = type.BaseType)
        {
            foreach (var name in names)
            {
                var property = type.GetProperties(MemberFlags)
                    .FirstOrDefault(candidate =>
                        candidate.GetIndexParameters().Length == 0
                        && string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
                if (property is not null)
                    return property.GetValue(source);

                var field = type.GetFields(MemberFlags)
                    .FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
                if (field is not null)
                    return field.GetValue(source);
            }
        }

        return null;
    }

    public static bool TrySetMember(object source, object value, params string[] names)
    {
        for (var type = source.GetType(); type is not null; type = type.BaseType)
        {
            foreach (var name in names)
            {
                var property = type.GetProperties(MemberFlags)
                    .FirstOrDefault(candidate =>
                        candidate.GetIndexParameters().Length == 0
                        && string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
                if (property is not null)
                    return TrySetProperty(source, property, value);

                var field = type.GetFields(MemberFlags)
                    .FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
                if (field is not null)
                    return TrySetField(source, field, value);
            }
        }

        return false;
    }

    public static bool TryAdd(object collection, object item)
    {
        collection = UnwrapValue(collection) ?? collection;
        if (collection is IList list)
        {
            list.Add(item);
            return true;
        }

        var add = collection.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(method =>
            {
                if (!string.Equals(method.Name, "Add", StringComparison.Ordinal))
                    return false;
                var parameters = method.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(item);
            });
        if (add is null)
            return false;

        add.Invoke(collection, new[] { item });
        return true;
    }

    public static IReadOnlyList<string> ReadContextTags(Item item)
    {
        var method = item.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(candidate =>
                candidate.GetParameters().Length == 0
                && string.Equals(candidate.Name, "GetContextTags", StringComparison.OrdinalIgnoreCase));
        if (method is null)
            return new List<string>();

        var result = method.Invoke(item, Array.Empty<object>());
        if (UnwrapValue(result) is not IEnumerable tags || result is string)
            return new List<string>();

        var values = new List<string>();
        foreach (var tag in tags)
        {
            var text = UnwrapValue(tag)?.ToString();
            if (!string.IsNullOrWhiteSpace(text))
                values.Add(text);
        }

        return values;
    }

    private static bool TrySetProperty(object source, PropertyInfo property, object value)
    {
        if (property.CanWrite)
        {
            property.SetValue(source, ConvertValue(value, property.PropertyType));
            return true;
        }

        var current = property.GetValue(source);
        return TrySetWrappedValue(current, value);
    }

    private static bool TrySetField(object source, FieldInfo field, object value)
    {
        if (!field.IsInitOnly && field.FieldType.IsAssignableFrom(value.GetType()))
        {
            field.SetValue(source, value);
            return true;
        }

        var current = field.GetValue(source);
        return TrySetWrappedValue(current, value);
    }

    private static bool TrySetWrappedValue(object? wrapper, object value)
    {
        if (wrapper is null)
            return false;

        var type = wrapper.GetType();
        var valueProperty = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(candidate =>
                candidate.CanWrite
                && candidate.GetIndexParameters().Length == 0
                && string.Equals(candidate.Name, "Value", StringComparison.OrdinalIgnoreCase));
        if (valueProperty is not null)
        {
            valueProperty.SetValue(wrapper, ConvertValue(value, valueProperty.PropertyType));
            return true;
        }

        var valueField = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(candidate => string.Equals(candidate.Name, "Value", StringComparison.OrdinalIgnoreCase));
        if (valueField is not null && !valueField.IsInitOnly)
        {
            valueField.SetValue(wrapper, ConvertValue(value, valueField.FieldType));
            return true;
        }

        return false;
    }

    private static object ConvertValue(object value, Type targetType)
        => targetType.IsInstanceOfType(value) ? value : Convert.ChangeType(value, targetType);

    private static object? UnwrapValue(object? value)
    {
        var current = value;
        for (var i = 0; i < 4; i++)
        {
            if (current is null || current is string || current is IEnumerable)
                return current;

            var type = current.GetType();
            if (type.IsPrimitive || type.IsEnum || current is decimal)
                return current;

            var property = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
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
}
