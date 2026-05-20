using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public sealed class CombatLabSpawnMonsterHandlerTests
{
    [Fact]
    public void Handle_NotWorldReady_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("""{"kind":"GreenSlime","x":12,"y":8}""").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            CombatLabSpawnMonsterHandler.Handle(p, new FakeCombatLabSpawnWorld { IsWorldReady = false }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_UnsupportedKind_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("""{"kind":"CustomBoss","x":12,"y":8}""").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            CombatLabSpawnMonsterHandler.Handle(p, new FakeCombatLabSpawnWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("unsupported monster kind", ex.Message);
    }

    [Fact]
    public void Handle_NegativeTile_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("""{"kind":"GreenSlime","x":-1,"y":8}""").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            CombatLabSpawnMonsterHandler.Handle(p, new FakeCombatLabSpawnWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("non-negative", ex.Message);
    }

    [Fact]
    public void Handle_NonPositiveHealth_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("""{"kind":"GreenSlime","x":12,"y":8,"health":0}""").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            CombatLabSpawnMonsterHandler.Handle(p, new FakeCombatLabSpawnWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("health", ex.Message);
    }

    [Fact]
    public void Handle_DelegatesSpawnAndReturnsIdentity()
    {
        var world = new FakeCombatLabSpawnWorld();
        var p = JsonDocument.Parse("""{"kind":"GreenSlime","label":"target","x":12,"y":8,"health":1}""").RootElement;

        var result = CombatLabSpawnMonsterHandler.Handle(p, world);
        var json = result.GetRawText();

        Assert.Equal("GreenSlime", world.Request!.Kind);
        Assert.Equal("target", world.Request.Label);
        Assert.Equal(12, world.Request.X);
        Assert.Equal(8, world.Request.Y);
        Assert.Equal(1, world.Request.Health);
        Assert.Contains("\"monster_id\":\"frobby-monster-1\"", json);
        Assert.Contains("\"label\":\"target\"", json);
        Assert.Contains("\"kind\":\"GreenSlime\"", json);
    }

    [Theory]
    [InlineData("GreenSlime")]
    [InlineData("Bat")]
    public void IsSupportedKind_AllowsInitialVanillaKinds(string kind)
    {
        Assert.True(CombatLabSpawnMonsterHandler.IsSupportedKind(kind));
    }

    private sealed class FakeCombatLabSpawnWorld : ICombatLabSpawnWorld
    {
        public bool IsWorldReady { get; init; } = true;
        public CombatLabSpawnMonsterRequest? Request { get; private set; }

        public CombatLabSpawnMonsterResult SpawnMonster(CombatLabSpawnMonsterRequest request)
        {
            Request = request;
            return new CombatLabSpawnMonsterResult
            {
                MonsterId = "frobby-monster-1",
                Label = request.Label,
                Kind = request.Kind,
                Location = CombatLabResetHandler.LocationName,
                Tile = new TilePoint { X = request.X, Y = request.Y },
                Health = request.Health,
                MaxHealth = 24,
            };
        }
    }
}
