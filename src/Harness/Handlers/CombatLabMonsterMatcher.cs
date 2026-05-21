using System;
using System.Collections.Generic;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Handlers;

internal static class CombatLabMonsterMatcher
{
    public static bool HasAnyFilter(CombatLabMonsterMatchCriteria match)
    {
        ArgumentNullException.ThrowIfNull(match);

        return match.X is not null
            || match.Y is not null
            || match.MonsterId is not null
            || match.Label is not null
            || match.Name is not null
            || match.Type is not null
            || match.SpriteTexture is not null
            || match.Health is not null
            || match.MaxHealth is not null
            || match.Damage is not null;
    }

    public static bool Matches(MonsterSummary summary, CombatLabMonsterMatchCriteria match)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(match);

        return NumberMatches(summary.Tile.X, match.X)
            && NumberMatches(summary.Tile.Y, match.Y)
            && StringMatches(summary.MonsterId, match.MonsterId)
            && StringMatches(summary.Label, match.Label)
            && StringMatches(summary.Name, match.Name)
            && StringMatches(summary.Type, match.Type)
            && StringMatches(summary.SpriteTexture, match.SpriteTexture)
            && NumberMatches(summary.Health, match.Health)
            && NumberMatches(summary.MaxHealth, match.MaxHealth)
            && NumberMatches(summary.Damage, match.Damage);
    }

    public static string Describe(CombatLabMonsterMatchCriteria match)
    {
        ArgumentNullException.ThrowIfNull(match);

        var parts = new List<string>();
        Add(parts, "x", match.X);
        Add(parts, "y", match.Y);
        Add(parts, "monster_id", match.MonsterId);
        Add(parts, "label", match.Label);
        Add(parts, "name", match.Name);
        Add(parts, "type", match.Type);
        Add(parts, "sprite_texture", match.SpriteTexture);
        Add(parts, "health", match.Health);
        Add(parts, "max_health", match.MaxHealth);
        Add(parts, "damage", match.Damage);
        return parts.Count == 0 ? "(no filters)" : string.Join(", ", parts);
    }

    private static bool StringMatches(string? actual, string? expected)
        => expected is null || string.Equals(actual, expected, StringComparison.Ordinal);

    private static bool NumberMatches(int? actual, int? expected)
        => expected is null || actual == expected;

    private static void Add(List<string> parts, string name, object? value)
    {
        if (value is not null)
            parts.Add($"{name}={value}");
    }
}
