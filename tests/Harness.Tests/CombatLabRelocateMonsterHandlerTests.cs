using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public sealed class CombatLabRelocateMonsterHandlerTests
{
    [Fact]
    public void Handle_NotWorldReady_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("""{"from_location":"Custom_CrimsonBadlands","target_x":9,"target_y":8,"match":{"type":"Mummy"}}""").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            CombatLabRelocateMonsterHandler.Handle(p, new FakeRelocateWorld { IsWorldReady = false }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Theory]
    [InlineData("""{"target_x":9,"target_y":8,"match":{"type":"Mummy"}}""", "from_location")]
    [InlineData("""{"from_location":"Custom_CrimsonBadlands","target_x":-1,"target_y":8,"match":{"type":"Mummy"}}""", "target")]
    [InlineData("""{"from_location":"Custom_CrimsonBadlands","target_x":9,"target_y":8,"match":{}}""", "match")]
    public void Handle_InvalidParams_ThrowsInvalidParams(string json, string messagePart)
    {
        var p = JsonDocument.Parse(json).RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            CombatLabRelocateMonsterHandler.Handle(p, new FakeRelocateWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains(messagePart, ex.Message);
    }

    [Fact]
    public void Handle_DelegatesRelocationAndReturnsIdentity()
    {
        var world = new FakeRelocateWorld();
        var p = JsonDocument.Parse("""{"from_location":"Custom_CrimsonBadlands","label":"corrupt-mummy","target_x":9,"target_y":8,"match":{"x":20,"y":144,"sprite_texture":"Characters/Monsters/CorruptMummy"}}""").RootElement;

        var result = CombatLabRelocateMonsterHandler.Handle(p, world);
        var json = result.GetRawText();

        Assert.Equal("Custom_CrimsonBadlands", world.Request!.FromLocation);
        Assert.Equal("corrupt-mummy", world.Request.Label);
        Assert.Equal(9, world.Request.TargetX);
        Assert.Equal(8, world.Request.TargetY);
        Assert.Equal("Characters/Monsters/CorruptMummy", world.Request.Match.SpriteTexture);
        Assert.Contains("\"monster_id\":\"frobby-monster-1\"", json);
        Assert.Contains("\"label\":\"corrupt-mummy\"", json);
        Assert.Contains("\"from_location\":\"Custom_CrimsonBadlands\"", json);
    }

    [Fact]
    public void ValidateTargetTileAgainstMap_OutsideActualMap_ThrowsInvalidParams()
    {
        var req = new CombatLabRelocateMonsterRequest
        {
            FromLocation = "Custom_CrimsonBadlands",
            TargetX = 120,
            TargetY = 8,
            Match = new CombatLabMonsterMatchCriteria { Type = "Mummy" },
        };

        var ex = Assert.Throws<JsonRpcException>(() =>
            SdvCombatLabRelocateWorld.ValidateTargetTileAgainstMap(req, mapWidth: 120, mapHeight: 60));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("map bounds", ex.Message);
    }

    [Fact]
    public void RelocatePreparedMonster_NoMatches_ThrowsGameStateInvalidWithoutMutating()
    {
        var source = new FakeRelocateLocation("Custom_CrimsonBadlands");
        var lab = new FakeRelocateLocation(CombatLabResetHandler.LocationName) { MapWidth = 20, MapHeight = 14 };
        var req = new CombatLabRelocateMonsterRequest
        {
            FromLocation = source.Name,
            TargetX = 9,
            TargetY = 8,
            Match = new CombatLabMonsterMatchCriteria { Type = "Mummy" },
        };

        var ex = Assert.Throws<JsonRpcException>(() =>
            SdvCombatLabRelocateWorld.RelocatePreparedMonster(req, source, lab));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("matched no monsters", ex.Message);
        Assert.Empty(lab.Monsters);
    }

    [Fact]
    public void RelocatePreparedMonster_MultipleMatches_ThrowsGameStateInvalidWithoutMutating()
    {
        var first = new FakeRelocatableMonster(new MonsterSummary { Tile = new TilePoint { X = 20, Y = 144 }, Type = "Mummy" });
        var second = new FakeRelocatableMonster(new MonsterSummary { Tile = new TilePoint { X = 21, Y = 144 }, Type = "Mummy" });
        var source = new FakeRelocateLocation("Custom_CrimsonBadlands", first, second);
        var lab = new FakeRelocateLocation(CombatLabResetHandler.LocationName) { MapWidth = 20, MapHeight = 14 };
        var req = new CombatLabRelocateMonsterRequest
        {
            FromLocation = source.Name,
            TargetX = 9,
            TargetY = 8,
            Match = new CombatLabMonsterMatchCriteria { Type = "Mummy" },
        };

        var ex = Assert.Throws<JsonRpcException>(() =>
            SdvCombatLabRelocateWorld.RelocatePreparedMonster(req, source, lab));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("matched 2 monsters", ex.Message);
        Assert.Equal(2, source.Monsters.Count);
        Assert.Empty(lab.Monsters);
    }

    [Fact]
    public void RelocatePreparedMonster_ExactMatch_MovesMonsterAndAssignsRelocatedIdentity()
    {
        CombatLabIdentityRegistry.Clear();
        var target = new FakeRelocatableMonster(new MonsterSummary
        {
            Tile = new TilePoint { X = 20, Y = 144 },
            Name = "Mummy",
            Type = "Mummy",
            SpriteTexture = "Characters/Monsters/CorruptMummy",
            Health = 2000,
            MaxHealth = 2000,
        });
        var decoy = new FakeRelocatableMonster(new MonsterSummary
        {
            Tile = new TilePoint { X = 21, Y = 144 },
            Type = "Mummy",
        });
        var source = new FakeRelocateLocation("Custom_CrimsonBadlands", target, decoy);
        var lab = new FakeRelocateLocation(CombatLabResetHandler.LocationName) { MapWidth = 20, MapHeight = 14 };
        var req = new CombatLabRelocateMonsterRequest
        {
            FromLocation = source.Name,
            Label = "corrupt-mummy",
            TargetX = 9,
            TargetY = 8,
            Match = new CombatLabMonsterMatchCriteria
            {
                X = 20,
                Y = 144,
                SpriteTexture = "Characters/Monsters/CorruptMummy",
            },
        };

        var result = SdvCombatLabRelocateWorld.RelocatePreparedMonster(req, source, lab);

        Assert.DoesNotContain(target, source.Monsters);
        Assert.Contains(decoy, source.Monsters);
        Assert.Single(lab.Monsters);
        Assert.Same(target, lab.Monsters[0]);
        Assert.Equal(9, target.Tile.X);
        Assert.Equal(8, target.Tile.Y);
        Assert.Equal(CombatLabResetHandler.LocationName, target.CurrentLocationName);
        Assert.Equal("frobby-monster-1", result.MonsterId);
        Assert.Equal("corrupt-mummy", result.Label);
        Assert.Equal(source.Name, result.FromLocation);
        Assert.Equal(20, result.SourceTile.X);
        Assert.Equal(144, result.SourceTile.Y);
        Assert.Equal("Mummy", result.Type);
        Assert.Equal("Characters/Monsters/CorruptMummy", result.SpriteTexture);
        Assert.True(CombatLabIdentityRegistry.TryGet(target.IdentityKey, out var identity));
        Assert.False(identity.SpawnedByFrobby);
    }

    private sealed class FakeRelocateWorld : ICombatLabRelocateWorld
    {
        public bool IsWorldReady { get; init; } = true;
        public CombatLabRelocateMonsterRequest? Request { get; private set; }

        public CombatLabRelocateMonsterResult RelocateMonster(CombatLabRelocateMonsterRequest request)
        {
            Request = request;
            return new CombatLabRelocateMonsterResult
            {
                MonsterId = "frobby-monster-1",
                Label = request.Label,
                FromLocation = request.FromLocation,
                SourceTile = new TilePoint { X = request.Match.X ?? 0, Y = request.Match.Y ?? 0 },
                Location = CombatLabResetHandler.LocationName,
                Tile = new TilePoint { X = request.TargetX, Y = request.TargetY },
                Name = "Mummy",
                Type = "Mummy",
                SpriteTexture = request.Match.SpriteTexture,
                Health = request.Match.Health,
                MaxHealth = request.Match.MaxHealth,
            };
        }
    }

    private sealed class FakeRelocateLocation : ICombatLabRelocateLocation
    {
        public FakeRelocateLocation(string name, params ICombatLabRelocatableMonster[] monsters)
        {
            Name = name;
            Monsters.AddRange(monsters);
        }

        public string Name { get; }
        public int? MapWidth { get; init; }
        public int? MapHeight { get; init; }
        public List<ICombatLabRelocatableMonster> Monsters { get; } = new();

        IReadOnlyList<ICombatLabRelocatableMonster> ICombatLabRelocateLocation.Monsters => Monsters;

        public void Remove(ICombatLabRelocatableMonster monster)
            => Monsters.Remove(monster);

        public void Add(ICombatLabRelocatableMonster monster)
            => Monsters.Add(monster);
    }

    private sealed class FakeRelocatableMonster : ICombatLabRelocatableMonster
    {
        private MonsterSummary summary;

        public FakeRelocatableMonster(MonsterSummary summary)
        {
            this.summary = summary;
        }

        public object IdentityKey => this;
        public TilePoint Tile => summary.Tile;
        public string? CurrentLocationName { get; private set; }

        public MonsterSummary Project()
            => summary;

        public void MoveTo(ICombatLabRelocateLocation location, int x, int y)
        {
            CurrentLocationName = location.Name;
            summary = new MonsterSummary
            {
                Tile = new TilePoint { X = x, Y = y },
                MonsterId = summary.MonsterId,
                Label = summary.Label,
                SpawnedByFrobby = summary.SpawnedByFrobby,
                Name = summary.Name,
                Type = summary.Type,
                Health = summary.Health,
                MaxHealth = summary.MaxHealth,
                Damage = summary.Damage,
                SpriteTexture = summary.SpriteTexture,
            };
        }
    }
}
