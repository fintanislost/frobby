using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public sealed class CombatLabResetHandlerTests
{
    [Fact]
    public void Handle_NoLoadedWorld_ThrowsGameStateInvalid()
    {
        var ex = Assert.Throws<JsonRpcException>(() =>
            CombatLabResetHandler.Handle(null, new FakeCombatLabWorld { IsWorldReady = false }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_InvalidDimensions_ThrowsInvalidParams()
    {
        var json = JsonDocument.Parse("""{"width":7,"height":14}""").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            CombatLabResetHandler.Handle(json, new FakeCombatLabWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("width", ex.Message);
    }

    [Fact]
    public void Handle_PlayerTileOutsideBounds_ThrowsInvalidParams()
    {
        var json = JsonDocument.Parse("""{"width":20,"height":14,"player_x":20,"player_y":8}""").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            CombatLabResetHandler.Handle(json, new FakeCombatLabWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("player", ex.Message);
    }

    [Fact]
    public void Handle_Reset_ClearsCombatLabIdentityRegistryAndReturnsResult()
    {
        var monster = new object();
        CombatLabIdentityRegistry.Assign(monster, "target");
        var world = new FakeCombatLabWorld
        {
            Result = new CombatLabResetResult
            {
                Location = CombatLabResetHandler.LocationName,
                PlayerTile = new TilePoint { X = 7, Y = 8 },
                MapWidth = 24,
                MapHeight = 16,
                ClearedMonsters = 2,
                ClearedDebris = 3,
            },
        };
        var json = JsonDocument.Parse("""{"player_x":7,"player_y":8,"width":24,"height":16,"warp_player":false}""").RootElement;

        var result = CombatLabResetHandler.Handle(json, world);

        Assert.True(world.ResetCalled);
        Assert.Equal(7, world.Request!.PlayerX);
        Assert.Equal(8, world.Request.PlayerY);
        Assert.Equal(24, world.Request.Width);
        Assert.Equal(16, world.Request.Height);
        Assert.False(world.Request.WarpPlayer);
        Assert.Equal(CombatLabResetHandler.LocationName, result.GetProperty("location").GetString());
        Assert.Equal(7, result.GetProperty("player_tile").GetProperty("x").GetInt32());
        Assert.Equal(8, result.GetProperty("player_tile").GetProperty("y").GetInt32());
        Assert.Equal(2, result.GetProperty("cleared_monsters").GetInt32());
        Assert.False(CombatLabIdentityRegistry.TryGet(monster, out _));
    }

    private sealed class FakeCombatLabWorld : ICombatLabWorld
    {
        public bool IsWorldReady { get; init; } = true;
        public bool ResetCalled { get; private set; }
        public CombatLabResetRequest? Request { get; private set; }
        public CombatLabResetResult Result { get; init; } = new()
        {
            Location = CombatLabResetHandler.LocationName,
            PlayerTile = new TilePoint { X = 8, Y = 8 },
            MapWidth = 20,
            MapHeight = 14,
        };

        public CombatLabResetResult Reset(CombatLabResetRequest request)
        {
            ResetCalled = true;
            Request = request;
            return Result;
        }
    }
}
