using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public sealed class CombatLabMonsterMatcherTests
{
    [Fact]
    public void Matches_AllSuppliedFilters()
    {
        var summary = new MonsterSummary
        {
            Tile = new TilePoint { X = 20, Y = 144 },
            MonsterId = "frobby-monster-7",
            Label = "source",
            Name = "Mummy",
            Type = "Mummy",
            SpriteTexture = "Characters/Monsters/CorruptMummy",
            Health = 2000,
            MaxHealth = 2000,
            Damage = 100,
        };
        var match = new CombatLabMonsterMatchCriteria
        {
            X = 20,
            Y = 144,
            MonsterId = "frobby-monster-7",
            Label = "source",
            Name = "Mummy",
            Type = "Mummy",
            SpriteTexture = "Characters/Monsters/CorruptMummy",
            Health = 2000,
            MaxHealth = 2000,
            Damage = 100,
        };

        Assert.True(CombatLabMonsterMatcher.Matches(summary, match));
    }

    [Theory]
    [InlineData("type")]
    [InlineData("sprite")]
    [InlineData("health")]
    [InlineData("tile")]
    public void Matches_ReturnsFalseForMismatchedFilters(string mismatch)
    {
        var summary = new MonsterSummary
        {
            Tile = new TilePoint { X = 20, Y = 144 },
            Type = "Mummy",
            SpriteTexture = "Characters/Monsters/CorruptMummy",
            Health = 2000,
        };
        var match = new CombatLabMonsterMatchCriteria
        {
            X = mismatch == "tile" ? 21 : 20,
            Y = 144,
            Type = mismatch == "type" ? "ShadowBrute" : "Mummy",
            SpriteTexture = mismatch == "sprite" ? "Other/Sprite" : "Characters/Monsters/CorruptMummy",
            Health = mismatch == "health" ? 1999 : 2000,
        };

        Assert.False(CombatLabMonsterMatcher.Matches(summary, match));
    }

    [Fact]
    public void HasAnyFilter_ReturnsTrueOnlyWhenAFilterIsSet()
    {
        Assert.False(CombatLabMonsterMatcher.HasAnyFilter(new CombatLabMonsterMatchCriteria()));
        Assert.True(CombatLabMonsterMatcher.HasAnyFilter(new CombatLabMonsterMatchCriteria { SpriteTexture = "Characters/Monsters/CorruptMummy" }));
    }

    [Fact]
    public void Describe_IncludesSuppliedFilters()
    {
        var text = CombatLabMonsterMatcher.Describe(new CombatLabMonsterMatchCriteria
        {
            X = 20,
            Y = 144,
            SpriteTexture = "Characters/Monsters/CorruptMummy",
            Health = 2000,
        });

        Assert.Contains("x=20", text);
        Assert.Contains("y=144", text);
        Assert.Contains("sprite_texture=Characters/Monsters/CorruptMummy", text);
        Assert.Contains("health=2000", text);
    }
}
