using System.Collections.Generic;
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
            Name = "Crystal Bat",
            Health = 180,
            MaxHealth = 180,
            DamageToFarmer = 32,
            Sprite = new FakeAnimatedSprite { textureName = "ExampleMod\\Monsters\\CrystalBat" },
        };

        var summary = LocationContentProjector.ProjectMonsterForTests(monster);

        Assert.Equal(44, summary.Tile.X);
        Assert.Equal(31, summary.Tile.Y);
        Assert.Equal("Crystal Bat", summary.Name);
        Assert.Equal("GreenSlime", summary.Type);
        Assert.Equal(180, summary.Health);
        Assert.Equal(180, summary.MaxHealth);
        Assert.Equal(32, summary.Damage);
        Assert.Equal("ExampleMod/Monsters/CrystalBat", summary.SpriteTexture);
    }

    [Fact]
    public void ProjectDebris_ReadsItemDebrisFields()
    {
        var debris = new FakeDebris
        {
            position = new Vector2(960, 1024),
            item = new FakeDebrisItem
            {
                ItemId = "769",
                QualifiedItemId = "(O)769",
                DisplayName = "Void Essence",
                Stack = 2,
                Quality = 0,
                Category = -2,
            },
        };

        var summary = LocationContentProjector.ProjectDebrisForTests(debris);

        Assert.Equal(15, summary.Tile.X);
        Assert.Equal(16, summary.Tile.Y);
        Assert.NotNull(summary.Pixel);
        Assert.Equal(960, summary.Pixel!.X);
        Assert.Equal(1024, summary.Pixel.Y);
        Assert.Equal("ItemDebris", summary.Kind);
        Assert.Equal("769", summary.Id);
        Assert.Equal("(O)769", summary.QualifiedId);
        Assert.Equal("Void Essence", summary.Name);
        Assert.Equal(2, summary.Stack);
        Assert.Equal(0, summary.Quality);
        Assert.Equal(-2, summary.Category);
        Assert.Equal("FakeDebris", summary.RuntimeType);
    }

    [Fact]
    public void ProjectDebris_UnwrapsValueWrappedItemFields()
    {
        var debris = new FakeWrappedDebris
        {
            position = new Vector2(128, 192),
            item = new FakeValueWrapper<FakeDebrisItem>
            {
                Value = new FakeDebrisItem
                {
                    ItemId = "766",
                    QualifiedItemId = "(O)766",
                    DisplayName = "Slime",
                    Stack = 3,
                    Quality = 1,
                    Category = -2,
                },
            },
        };

        var summary = LocationContentProjector.ProjectDebrisForTests(debris);

        Assert.Equal(2, summary.Tile.X);
        Assert.Equal(3, summary.Tile.Y);
        Assert.Equal("ItemDebris", summary.Kind);
        Assert.Equal("766", summary.Id);
        Assert.Equal("(O)766", summary.QualifiedId);
        Assert.Equal("Slime", summary.Name);
        Assert.Equal(3, summary.Stack);
        Assert.Equal(1, summary.Quality);
        Assert.Equal(-2, summary.Category);
    }

    [Fact]
    public void ProjectDebris_ToleratesNonItemDebris()
    {
        var debris = new FakeVisualDebris
        {
            position = new Vector2(64, 128),
            debrisType = "spark",
        };

        var summary = LocationContentProjector.ProjectDebrisForTests(debris);

        Assert.Equal(1, summary.Tile.X);
        Assert.Equal(2, summary.Tile.Y);
        Assert.Equal("VisualDebris", summary.Kind);
        Assert.Equal("spark", summary.Name);
        Assert.Equal(string.Empty, summary.Id);
        Assert.Equal(string.Empty, summary.QualifiedId);
        Assert.Equal("FakeVisualDebris", summary.RuntimeType);
    }

    [Fact]
    public void ProjectObject_ReadsObjectMetadata()
    {
        var obj = new FakeLocationObject
        {
            Name = "Golden Piggy Bank",
            ItemId = "Example_Golden_Piggy_Bank",
            QualifiedItemId = "(BC)Example_Golden_Piggy_Bank",
            Category = -9,
            Stack = 1,
            Quality = 0,
            bigCraftable = new FakeValueWrapper<bool> { Value = true },
            readyForHarvest = new FakeValueWrapper<bool> { Value = false },
        };

        var summary = LocationContentProjector.ProjectObjectForTests(new Vector2(8, 9), obj);

        Assert.Equal(8, summary.Tile.X);
        Assert.Equal(9, summary.Tile.Y);
        Assert.Equal("Golden Piggy Bank", summary.Name);
        Assert.Equal("Example_Golden_Piggy_Bank", summary.Id);
        Assert.Equal("(BC)Example_Golden_Piggy_Bank", summary.QualifiedId);
        Assert.Equal(-9, summary.Category);
        Assert.Equal(1, summary.Stack);
        Assert.Equal(0, summary.Quality);
        Assert.Equal("FakeLocationObject", summary.RuntimeType);
        Assert.True(summary.BigCraftable);
        Assert.False(summary.ReadyForHarvest);
    }

    [Fact]
    public void ProjectObject_ReadsHeldObjectMetadata()
    {
        var obj = new FakeLocationObject
        {
            Name = "Example Machine",
            ItemId = "Example_Machine",
            QualifiedItemId = "(BC)Example_Machine",
            bigCraftable = new FakeValueWrapper<bool> { Value = true },
            heldObject = new FakeValueWrapper<FakeHeldObject>
            {
                Value = new FakeHeldObject
                {
                    Name = "Honey",
                    ItemId = "340",
                    QualifiedItemId = "(O)340",
                },
            },
        };

        var summary = LocationContentProjector.ProjectObjectForTests(new Vector2(4, 5), obj);

        Assert.Equal("340", summary.HeldObjectId);
        Assert.Equal("(O)340", summary.HeldObjectQualifiedId);
        Assert.Equal("Honey", summary.HeldObjectName);
    }

    [Fact]
    public void ProjectObject_ReadsChestContainedItems()
    {
        var chest = new FakeChest
        {
            Name = "Treasure Chest",
            ItemId = "130",
            QualifiedItemId = "(BC)130",
            Items =
            {
                new FakeHeldObject
                {
                    Name = "Golden Pumpkin",
                    ItemId = "373",
                    QualifiedItemId = "(O)373",
                    Stack = 1,
                    Quality = 0,
                    Category = -79,
                },
            },
        };

        var summary = LocationContentProjector.ProjectObjectForTests(new Vector2(63, 16), chest);

        Assert.True(summary.IsChest);
        Assert.Equal(1, summary.ItemCount);
        Assert.False(summary.ItemsTruncated);
        var item = Assert.Single(summary.Items);
        Assert.Equal(0, item.Slot);
        Assert.Equal("373", item.Id);
        Assert.Equal("373", item.ItemId);
        Assert.Equal("(O)373", item.QualifiedId);
        Assert.Equal("Golden Pumpkin", item.Name);
        Assert.Equal(1, item.Stack);
        Assert.Equal(0, item.Quality);
        Assert.Equal(-79, item.Category);
        Assert.Equal("FakeHeldObject", item.RuntimeType);
    }

    [Fact]
    public void ProjectObject_LeavesNonChestItemListEmpty()
    {
        var obj = new FakeLocationObject
        {
            Name = "Golden Piggy Bank",
            ItemId = "Example_Golden_Piggy_Bank",
            QualifiedItemId = "(BC)Example_Golden_Piggy_Bank",
        };

        var summary = LocationContentProjector.ProjectObjectForTests(new Vector2(8, 9), obj);

        Assert.False(summary.IsChest);
        Assert.Null(summary.ItemCount);
        Assert.Null(summary.ItemsTruncated);
        Assert.Empty(summary.Items);
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

    private sealed class FakeDebris
    {
        public Vector2 position;
        public FakeDebrisItem? item;
    }

    private sealed class FakeVisualDebris
    {
        public Vector2 position;
        public string debrisType = string.Empty;
    }

    private sealed class FakeWrappedDebris
    {
        public Vector2 position;
        public FakeValueWrapper<FakeDebrisItem>? item;
    }

    private sealed class FakeDebrisItem
    {
        public string ItemId = string.Empty;
        public string QualifiedItemId = string.Empty;
        public string DisplayName = string.Empty;
        public int Stack;
        public int Quality;
        public int Category;
    }

    private sealed class FakeLocationObject
    {
        public string Name = string.Empty;
        public string ItemId = string.Empty;
        public string QualifiedItemId = string.Empty;
        public int Category;
        public int Stack;
        public int Quality;
        public FakeValueWrapper<bool>? bigCraftable;
        public FakeValueWrapper<bool>? readyForHarvest;
        public FakeValueWrapper<FakeHeldObject>? heldObject;
    }

    private sealed class FakeHeldObject
    {
        public string Name = string.Empty;
        public string ItemId = string.Empty;
        public string QualifiedItemId = string.Empty;
        public int Stack;
        public int Quality;
        public int Category;
    }

    private sealed class FakeChest
    {
        public string Name = string.Empty;
        public string ItemId = string.Empty;
        public string QualifiedItemId = string.Empty;
        public List<FakeHeldObject> Items { get; } = new();
    }

    private sealed class FakeValueWrapper<T>
    {
        public T? Value { get; set; }
    }
}
