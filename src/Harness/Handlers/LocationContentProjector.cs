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

    public static bool IsMonster(NPC npc) => npc is Monster;

    internal static ResourceClumpSummary ProjectResourceClumpForTests(object clump)
        => ProjectResourceClump(clump);

    internal static MonsterSummary ProjectMonsterForTests(object monster)
        => ProjectMonster(monster);

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

    private static Vector2? ReadVector2(object instance, params string[] names)
    {
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

    private static int? ReadInt(object instance, params string[] names)
    {
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

    private static string? ReadString(object instance, params string[] names)
    {
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

    private static object? ReadMemberRaw(object instance, params string[] names)
    {
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
