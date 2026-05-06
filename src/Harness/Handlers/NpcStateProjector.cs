using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Projects SDV NPC runtime objects into neutral protocol snapshots.</summary>
public static class NpcStateProjector
{
    public static NpcState Project(NPC npc, Farmer? farmer)
    {
        var name = npc.Name ?? string.Empty;
        var tile = new TilePoint { X = npc.TilePoint.X, Y = npc.TilePoint.Y };
        var isVillager = ReadBool(npc, "IsVillager", "isVillager");
        var state = new NpcState
        {
            Name = name,
            DisplayName = ReadString(npc, "displayName", "DisplayName") ?? name,
            Location = npc.currentLocation?.Name ?? string.Empty,
            Tile = tile,
            Portrait = NormalizePortraitName(npc.Portrait?.Name) ?? name,
            CurrentScheduleKey = ReadString(npc, "currentScheduleKey", "CurrentScheduleKey", "scheduleKey", "ScheduleKey"),
            CurrentScheduleTime = Game1.timeOfDay > 0 ? Game1.timeOfDay : null,
            CurrentScheduleLocation = npc.currentLocation?.Name,
            CurrentScheduleTile = tile,
            CurrentScheduleDirection = npc.FacingDirection,
            CurrentScheduleAnimation = ReadString(npc, "currentScheduleAnimation", "CurrentScheduleAnimation", "endOfRouteBehaviorName", "EndOfRouteBehaviorName"),
            IsVillager = isVillager,
            CanSocialize = ReadBool(npc, "CanSocialize", "canSocialize") ?? isVillager,
        };

        if (farmer?.friendshipData is { } data && data.TryGetValue(name, out var friendship))
            ApplyFriendship(state, friendship);

        return state;
    }

    public static List<NpcState> ProjectMany(IEnumerable<NPC> npcs, Farmer? farmer, int limit)
        => NpcsDistinct(npcs)
            .Take(limit)
            .Select(npc => Project(npc, farmer))
            .ToList();

    public static void ApplyFriendship(NpcState state, Friendship friendship)
    {
        state.FriendshipPoints = friendship.Points;
        state.Hearts = friendship.Points / 250;
        state.GiftGivenToday = friendship.GiftsToday > 0;
        state.TalkedToToday = friendship.TalkedToToday;
    }

    public static string? NormalizePortraitName(string? rawAssetName)
    {
        if (string.IsNullOrEmpty(rawAssetName)) return null;
        return System.IO.Path.GetFileNameWithoutExtension(rawAssetName.Replace('\\', '/'));
    }

    private static IEnumerable<NPC> NpcsDistinct(IEnumerable<NPC> npcs)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var npc in npcs)
        {
            if (npc is null) continue;
            var name = npc.Name ?? string.Empty;
            if (name.Length == 0 || !seen.Add(name)) continue;
            yield return npc;
        }
    }

    private static string? ReadString(object instance, params string[] names)
        => ReadMember<string>(instance, names);

    private static bool? ReadBool(object instance, params string[] names)
        => ReadMember<bool>(instance, names);

    private static T? ReadMember<T>(object instance, params string[] names)
    {
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = instance.GetType();
        foreach (var name in names)
        {
            var property = type.GetProperty(name, flags);
            if (property is not null && property.GetValue(instance) is T propertyValue)
                return propertyValue;

            var field = type.GetField(name, flags);
            if (field is not null && field.GetValue(instance) is T fieldValue)
                return fieldValue;

            var method = type.GetMethod(name, flags, binder: null, Type.EmptyTypes, modifiers: null);
            if (method is not null && method.Invoke(instance, Array.Empty<object>()) is T methodValue)
                return methodValue;
        }

        return default;
    }
}
