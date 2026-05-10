using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Microsoft.Xna.Framework;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using StardewValley.Monsters;

namespace SdvTestFramework.Harness.Handlers;

internal static class LocationContentProjector
{
    public static IEnumerable<ResourceClumpSummary> ProjectResourceClumps(GameLocation loc)
    {
        if (ReadMemberRaw(loc, "resourceClumps", "ResourceClumps") is not IEnumerable clumps)
            yield break;

        foreach (var clump in clumps)
        {
            if (clump is null)
                continue;

            yield return ProjectResourceClump(clump);
        }
    }

    public static IEnumerable<MonsterSummary> ProjectMonsters(GameLocation loc)
    {
        foreach (var character in loc.characters)
        {
            if (character is Monster monster)
                yield return ProjectMonster(monster);
        }
    }

    public static IEnumerable<DebrisSummary> ProjectDebris(GameLocation loc)
    {
        if (ReadMemberRaw(loc, "debris", "Debris") is not IEnumerable debris)
            yield break;

        foreach (var entry in debris)
        {
            if (entry is null)
                continue;

            yield return ProjectDebris(entry);
        }
    }

    public static bool IsMonster(NPC npc) => npc is Monster;

    internal static ResourceClumpSummary ProjectResourceClumpForTests(object clump)
        => ProjectResourceClump(clump);

    internal static MonsterSummary ProjectMonsterForTests(object monster)
        => ProjectMonster(monster);

    internal static DebrisSummary ProjectDebrisForTests(object debris)
        => ProjectDebris(debris);

    internal static string ResourceClumpNameForTests(string id)
        => ResourceClumpName(id);

    private static ResourceClumpSummary ProjectResourceClump(object clump)
    {
        var tile = ReadVector2(clump, "tile", "Tile") ?? Vector2.Zero;
        var id = ReadInt(clump, "parentSheetIndex", "ParentSheetIndex")?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        return new ResourceClumpSummary
        {
            Tile = new TilePoint { X = (int)tile.X, Y = (int)tile.Y },
            Kind = clump.GetType().Name,
            Id = id,
            Name = ResourceClumpName(id),
            Width = ReadInt(clump, "width", "Width"),
            Height = ReadInt(clump, "height", "Height"),
            Health = ReadInt(clump, "health", "Health"),
        };
    }

    private static MonsterSummary ProjectMonster(object monster)
    {
        var tile = ReadTilePoint(monster);
        return new MonsterSummary
        {
            Tile = tile,
            Name = ReadString(monster, "Name", "name", "DisplayName", "displayName") ?? monster.GetType().Name,
            Type = monster.GetType().Name,
            Health = ReadInt(monster, "Health", "health"),
            MaxHealth = ReadInt(monster, "MaxHealth", "maxHealth"),
            Damage = ReadInt(monster, "DamageToFarmer", "damageToFarmer", "damage"),
            SpriteTexture = NormalizeAssetName(ReadSpriteTexture(monster)),
        };
    }

    private static DebrisSummary ProjectDebris(object debris)
    {
        var pixel = ReadVector2(debris, "position", "Position", "debrisOrigin", "DebrisOrigin")
            ?? ReadFirstNestedVector2(debris, "chunks", "Chunks");
        var item = ReadValueProperty(ReadMemberRaw(debris, "item", "Item", "debrisItem", "DebrisItem"))
            ?? ReadMemberRaw(debris, "item", "Item", "debrisItem", "DebrisItem");
        var qualifiedId = ReadString(item, "QualifiedItemId", "qualifiedItemId")
            ?? ReadString(debris, "QualifiedItemId", "qualifiedItemId")
            ?? string.Empty;
        var id = ReadString(item, "ItemId", "itemId")
            ?? ReadString(debris, "itemId", "ItemId")
            ?? StripQualifiedPrefix(qualifiedId);
        var name = ReadString(item, "DisplayName", "displayName", "Name", "name")
            ?? ReadString(debris, "debrisType", "DebrisType", "Name", "name")
            ?? string.Empty;

        return new DebrisSummary
        {
            Tile = pixel is null
                ? new TilePoint()
                : new TilePoint { X = (int)(pixel.Value.X / 64), Y = (int)(pixel.Value.Y / 64) },
            Pixel = pixel is null
                ? null
                : new PixelPoint { X = (int)pixel.Value.X, Y = (int)pixel.Value.Y },
            Kind = item is null ? "VisualDebris" : "ItemDebris",
            Id = id,
            QualifiedId = qualifiedId,
            Name = name,
            Stack = ReadInt(item, "Stack", "stack") ?? ReadInt(debris, "stack", "Stack"),
            Quality = ReadInt(item, "Quality", "quality") ?? ReadInt(debris, "quality", "Quality", "itemQuality", "ItemQuality"),
            Category = ReadInt(item, "Category", "category") ?? ReadInt(debris, "category", "Category"),
            RuntimeType = debris.GetType().Name,
        };
    }

    private static string ResourceClumpName(string id)
        => id switch
        {
            "" => "ResourceClump",
            "600" => "Stump",
            "602" => "Log",
            "622" => "Meteorite",
            "672" => "Boulder",
            "668" or "670" or "845" or "846" or "847" => "Mine Rock",
            _ => $"ResourceClump {id}",
        };

    private static string? ReadSpriteTexture(object monster)
    {
        var direct = ReadString(monster, "spriteTexture", "SpriteTexture", "textureName", "TextureName");
        if (!string.IsNullOrWhiteSpace(direct))
            return direct;

        var sprite = ReadMemberRaw(monster, "Sprite", "sprite", "AnimatedSprite", "animatedSprite");
        return sprite is null
            ? null
            : ReadString(sprite, "textureName", "TextureName");
    }

    private static string? NormalizeAssetName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Replace('\\', '/');
    }

    private static Vector2? ReadFirstNestedVector2(object instance, params string[] collectionNames)
    {
        if (ReadMemberRaw(instance, collectionNames) is not IEnumerable entries)
            return null;

        foreach (var entry in entries)
        {
            var nested = ReadValueProperty(entry) ?? entry;
            var vector = ReadVector2(nested, "position", "Position", "currentPosition", "CurrentPosition");
            if (vector is not null)
                return vector;
        }

        return null;
    }

    private static string StripQualifiedPrefix(string value)
    {
        if (value.Length > 0 && value[0] == '(')
        {
            var close = value.IndexOf(')', StringComparison.Ordinal);
            if (close >= 0 && close + 1 < value.Length)
                return value[(close + 1)..];
        }

        return value;
    }

    private static Vector2? ReadVector2(object? instance, params string[] names)
    {
        if (instance is null)
            return null;

        var value = ReadMemberRaw(instance, names);
        if (value is Vector2 vector)
            return vector;

        var nested = ReadValueProperty(value);
        return nested is Vector2 nestedVector ? nestedVector : null;
    }

    private static TilePoint ReadTilePoint(object instance)
    {
        var value = ReadMemberRaw(instance, "TilePoint", "tilePoint", "tile", "Tile");
        value = ReadValueProperty(value) ?? value;

        if (value is Point point)
            return new TilePoint { X = point.X, Y = point.Y };

        if (value is Vector2 tile)
            return new TilePoint { X = (int)tile.X, Y = (int)tile.Y };

        var position = ReadVector2(instance, "Position", "position", "NetPosition", "netPosition");
        return position is null
            ? new TilePoint()
            : new TilePoint { X = (int)(position.Value.X / 64), Y = (int)(position.Value.Y / 64) };
    }

    private static int? ReadInt(object? instance, params string[] names)
    {
        if (instance is null)
            return null;

        var value = ReadMemberRaw(instance, names);
        value = ReadValueProperty(value) ?? value;

        return value switch
        {
            int i => i,
            long l when l >= int.MinValue && l <= int.MaxValue => (int)l,
            short s => s,
            byte b => b,
            _ => null,
        };
    }

    private static string? ReadString(object? instance, params string[] names)
    {
        if (instance is null)
            return null;

        var value = ReadMemberRaw(instance, names);
        value = ReadValueProperty(value) ?? value;
        return value as string;
    }

    private static object? ReadValueProperty(object? value)
    {
        if (value is null)
            return null;

        var prop = value.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return prop?.GetValue(value);
    }

    private static object? ReadMemberRaw(object? instance, params string[] names)
    {
        if (instance is null)
            return null;

        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = instance.GetType();
        foreach (var name in names)
        {
            var property = type.GetProperty(name, flags);
            if (property is not null)
            {
                try
                {
                    return property.GetValue(instance);
                }
                catch (TargetInvocationException)
                {
                    continue;
                }
            }

            var field = type.GetField(name, flags);
            if (field is not null)
                return field.GetValue(instance);
        }

        return null;
    }
}
