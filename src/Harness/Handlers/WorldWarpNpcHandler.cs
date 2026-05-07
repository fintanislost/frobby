using System;
using System.Text.Json;
using System.Reflection;
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>world.warp_npc</c>. Runs on the game thread.</summary>
public static class WorldWarpNpcHandler
{
    public const string Method = "world.warp_npc";

    private static readonly IWorldWarpNpcWorld ProductionWorld = new SdvWorldWarpNpcWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, IWorldWarpNpcWorld world)
    {
        var req = RpcParams.Required<WarpNpcRequest>(paramsElement);
        var name = req.Name?.Trim() ?? string.Empty;
        var location = req.Location?.Trim() ?? string.Empty;
        if (name.Length == 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.name must be non-empty");
        if (location.Length == 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.location must be non-empty");
        if (req.X is null)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.x required");
        if (req.Y is null)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.y required");
        if (req.X < 0 || req.Y < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.x and params.y must be non-negative");

        if (!world.LocationExists(location))
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"no location named: {location}");

        var npc = world.FindNpc(name)
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"no NPC named: {name}");

        world.PrepareNpcForWarp(npc, name);
        world.WarpNpc(npc, name, location, req.X.Value, req.Y.Value);

        return ProtocolJson.ToElement(new MutatorOk { Tick = world.Tick });
    }
}

internal interface IWorldWarpNpcWorld
{
    int Tick { get; }
    bool LocationExists(string name);
    object? FindNpc(string name);
    void PrepareNpcForWarp(object npc, string name);
    void WarpNpc(object npc, string name, string location, int x, int y);
}

internal sealed class SdvWorldWarpNpcWorld : IWorldWarpNpcWorld
{
    public int Tick => Game1.ticks;

    public bool LocationExists(string name)
        => Game1.getLocationFromName(name) is not null;

    public object? FindNpc(string name)
        => Game1.getCharacterFromName(name);

    public void PrepareNpcForWarp(object npc, string name)
    {
        if (npc is not NPC)
            throw new JsonRpcException(JsonRpcErrorCode.InternalError, $"resolved NPC '{name}' was not an NPC");

        NpcWarpPreparation.Prepare(npc);
    }

    public void WarpNpc(object npc, string name, string location, int x, int y)
    {
        if (npc is not NPC character)
            throw new JsonRpcException(JsonRpcErrorCode.InternalError, $"resolved NPC '{name}' was not an NPC");

        Game1.warpCharacter(character, location, new Vector2(x, y));
    }
}

internal static class NpcWarpPreparation
{
    private const BindingFlags InstanceFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Prepare(object npc)
    {
        InvokeNoArgs(npc, "Halt");
        SetMember(npc, "controller", null);
        SetBoolLikeMember(npc, "isSleeping", false);
        SetBoolLikeMember(npc, "isPlayingSleepingAnimation", false);
        SetBoolLikeMember(npc, "doingEndOfRouteAnimation", false);
        SetBoolLikeMember(npc, "isTemporarilyInvisible", false);
        SetBoolLikeMember(npc, "isInvisible", false);
        SetBoolLikeMember(npc, "IsInvisible", false);
        SetBoolLikeMember(npc, "HideShadow", false);
        InvokeNoArgs(npc, "resetCurrentDialogue");
    }

    private static void InvokeNoArgs(object instance, string name)
    {
        try
        {
            instance.GetType().GetMethod(name, InstanceFlags, binder: null, Type.EmptyTypes, modifiers: null)
                ?.Invoke(instance, Array.Empty<object>());
        }
        catch
        {
            // Best-effort cleanup for version-specific NPC state.
        }
    }

    private static void SetBoolLikeMember(object instance, string name, bool value)
        => SetMember(instance, name, value);

    private static void SetMember(object instance, string name, object? value)
    {
        try
        {
            var type = instance.GetType();
            var property = type.GetProperty(name, InstanceFlags);
            if (property is not null && TrySetProperty(property, instance, value))
                return;

            var field = type.GetField(name, InstanceFlags);
            if (field is not null)
                TrySetField(field, instance, value);
        }
        catch
        {
            // Best-effort cleanup for version-specific NPC state.
        }
    }

    private static bool TrySetProperty(PropertyInfo property, object instance, object? value)
    {
        if (property.CanWrite && IsAssignable(property.PropertyType, value))
        {
            property.SetValue(instance, value);
            return true;
        }

        var propertyValue = property.GetValue(instance);
        return TrySetWrappedBool(propertyValue, value);
    }

    private static void TrySetField(FieldInfo field, object instance, object? value)
    {
        if (IsAssignable(field.FieldType, value))
        {
            field.SetValue(instance, value);
            return;
        }

        var fieldValue = field.GetValue(instance);
        TrySetWrappedBool(fieldValue, value);
    }

    private static bool TrySetWrappedBool(object? target, object? value)
    {
        if (value is not bool boolValue || target is null)
            return false;

        var valueProperty = target.GetType().GetProperty("Value", InstanceFlags);
        if (valueProperty is { CanWrite: true } && valueProperty.PropertyType == typeof(bool))
        {
            valueProperty.SetValue(target, boolValue);
            return true;
        }

        return false;
    }

    private static bool IsAssignable(Type targetType, object? value)
        => value is null
            ? !targetType.IsValueType || Nullable.GetUnderlyingType(targetType) is not null
            : targetType.IsInstanceOfType(value);
}
