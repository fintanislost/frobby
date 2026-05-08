using System.Runtime.Serialization;
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Handlers;
using StardewValley;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class LocationContentProjectorTests
{
    [Theory]
    [InlineData("602", "Log")]
    [InlineData("600", "Stump")]
    [InlineData("622", "Meteorite")]
    [InlineData("672", "Boulder")]
    [InlineData("9999", "ResourceClump 9999")]
    [InlineData("", "ResourceClump")]
    public void ResourceClumpName_MapsKnownIds(string id, string expected)
    {
        Assert.Equal(expected, LocationContentProjector.ResourceClumpNameForTests(id));
    }

    [Fact]
    public void ProjectResourceClump_ReadsPlainFieldsAndProperties()
    {
        var clump = new ResourceClump
        {
            tile = new Vector2(21, 17),
            parentSheetIndex = 602,
            width = 2,
            height = 2,
            health = 10,
        };

        var summary = LocationContentProjector.ProjectResourceClumpForTests(clump);

        Assert.Equal(21, summary.Tile.X);
        Assert.Equal(17, summary.Tile.Y);
        Assert.Equal("ResourceClump", summary.Kind);
        Assert.Equal("602", summary.Id);
        Assert.Equal("Log", summary.Name);
        Assert.Equal(2, summary.Width);
        Assert.Equal(2, summary.Height);
        Assert.Equal(10, summary.Health);
    }

    [Fact]
    public void ProjectMonster_ReadsRuntimeMonsterFields()
    {
        var monster = new GreenSlime
        {
            tile = new Vector2(44, 31),
            Name = "Mummy",
            Health = 2000,
            MaxHealth = 2000,
            DamageToFarmer = 100,
            Sprite = new FakeAnimatedSprite { textureName = "Characters\\Monsters\\CorruptMummy" },
        };

        var summary = LocationContentProjector.ProjectMonsterForTests(monster);

        Assert.Equal(44, summary.Tile.X);
        Assert.Equal(31, summary.Tile.Y);
        Assert.Equal("Mummy", summary.Name);
        Assert.Equal("GreenSlime", summary.Type);
        Assert.Equal(2000, summary.Health);
        Assert.Equal(2000, summary.MaxHealth);
        Assert.Equal(100, summary.Damage);
        Assert.Equal("Characters/Monsters/CorruptMummy", summary.SpriteTexture);
    }

    [Fact]
    public void IsMonster_ReturnsFalseForSocialNpc()
    {
        var npc = (NPC)FormatterServices.GetUninitializedObject(typeof(NPC));

        Assert.False(LocationContentProjector.IsMonster(npc));
    }

    [Fact]
    public void IsMonster_ReturnsTrueForRuntimeMonster()
    {
        var monster = (NPC)FormatterServices.GetUninitializedObject(typeof(StardewValley.Monsters.GreenSlime));

        Assert.True(LocationContentProjector.IsMonster(monster));
    }

    private sealed class ResourceClump
    {
        public Vector2 tile;
        public int parentSheetIndex;
        public int width;
        public int height;
        public int health;
    }

    private sealed class GreenSlime
    {
        public Vector2 tile;
        public string Name = string.Empty;
        public int Health;
        public int MaxHealth;
        public int DamageToFarmer;
        public FakeAnimatedSprite? Sprite;
    }

    private sealed class FakeAnimatedSprite
    {
        public string textureName = string.Empty;
    }
}
