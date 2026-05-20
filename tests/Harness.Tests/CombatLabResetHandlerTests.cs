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
    public void Handle_NegativePlayerTile_ThrowsInvalidParams()
    {
        var json = JsonDocument.Parse("""{"width":20,"height":14,"player_x":-1,"player_y":8}""").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            CombatLabResetHandler.Handle(json, new FakeCombatLabWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("player", ex.Message);
    }

    [Fact]
    public void Handle_PlayerTileOutsideRequestedSize_DelegatesToWorldForActualMapBounds()
    {
        var world = new FakeCombatLabWorld
        {
            Result = new CombatLabResetResult
            {
                Location = CombatLabResetHandler.LocationName,
                PlayerTile = new TilePoint { X = 9, Y = 7 },
                MapWidth = 120,
                MapHeight = 60,
            },
        };
        var json = JsonDocument.Parse("""{"width":8,"height":8,"player_x":9,"player_y":7}""").RootElement;

        CombatLabResetHandler.Handle(json, world);

        Assert.True(world.ResetCalled);
        Assert.Equal(9, world.Request!.PlayerX);
        Assert.Equal(7, world.Request.PlayerY);
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

    [Fact]
    public void BuildResetResult_PrefersActualMapDimensionsOverRequestBounds()
    {
        var req = new CombatLabResetRequest
        {
            PlayerX = 7,
            PlayerY = 8,
            Width = 24,
            Height = 16,
        };

        var result = SdvCombatLabWorld.BuildResetResult(
            req,
            mapWidth: 120,
            mapHeight: 60,
            clearedMonsters: 2,
            clearedDebris: 3);

        Assert.Equal(120, result.MapWidth);
        Assert.Equal(60, result.MapHeight);
        Assert.Equal(7, result.PlayerTile.X);
        Assert.Equal(8, result.PlayerTile.Y);
        Assert.Equal(2, result.ClearedMonsters);
        Assert.Equal(3, result.ClearedDebris);
    }

    [Fact]
    public void ValidatePlayerTileAgainstMap_OutsideActualMap_ThrowsInvalidParams()
    {
        var req = new CombatLabResetRequest
        {
            PlayerX = 120,
            PlayerY = 7,
            Width = 200,
            Height = 200,
        };

        var ex = Assert.Throws<JsonRpcException>(() =>
            SdvCombatLabWorld.ValidatePlayerTileAgainstMap(req, mapWidth: 120, mapHeight: 60));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("player", ex.Message);
    }

    [Fact]
    public void ResetPreparedLab_InvalidMapBounds_DoesNotMutateLab()
    {
        var req = new CombatLabResetRequest
        {
            PlayerX = 120,
            PlayerY = 7,
            Width = 200,
            Height = 200,
        };
        var lab = new FakeCombatLabLocation
        {
            MapWidth = 120,
            MapHeight = 60,
            MonsterCount = 2,
            DebrisCount = 3,
        };

        Assert.Throws<JsonRpcException>(() => SdvCombatLabWorld.ResetPreparedLab(req, lab));

        Assert.False(lab.ClearCalled);
        Assert.False(lab.AddToWorldCalled);
        Assert.False(lab.WarpPlayerCalled);
    }

    [Fact]
    public void ResetPreparedLab_ValidNewLab_ClearsAddsWarpsAndReturnsCounts()
    {
        var req = new CombatLabResetRequest
        {
            PlayerX = 8,
            PlayerY = 7,
            Width = 20,
            Height = 14,
            WarpPlayer = true,
        };
        var lab = new FakeCombatLabLocation
        {
            MapWidth = 120,
            MapHeight = 60,
            MonsterCount = 2,
            DebrisCount = 3,
        };

        var result = SdvCombatLabWorld.ResetPreparedLab(req, lab);

        Assert.True(lab.ClearCalled);
        Assert.True(lab.AddToWorldCalled);
        Assert.True(lab.WarpPlayerCalled);
        Assert.Equal(120, result.MapWidth);
        Assert.Equal(60, result.MapHeight);
        Assert.Equal(2, result.ClearedMonsters);
        Assert.Equal(3, result.ClearedDebris);
    }

    [Fact]
    public void CombatLabLifecycle_Clear_RemovesLocationAndClearsIdentities()
    {
        var monster = new object();
        CombatLabIdentityRegistry.Assign(monster, "target");
        var world = new FakeCombatLabCleanupWorld();

        CombatLabLifecycle.Clear(world);

        Assert.True(world.RemoveCalled);
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

    private sealed class FakeCombatLabCleanupWorld : ICombatLabCleanupWorld
    {
        public bool RemoveCalled { get; private set; }

        public void RemoveCombatLabLocation()
            => RemoveCalled = true;
    }

    private sealed class FakeCombatLabLocation : ICombatLabLocation
    {
        public bool IsInWorld { get; set; }
        public int? MapWidth { get; init; }
        public int? MapHeight { get; init; }
        public int MonsterCount { get; init; }
        public int DebrisCount { get; init; }
        public bool ClearCalled { get; private set; }
        public bool AddToWorldCalled { get; private set; }
        public bool WarpPlayerCalled { get; private set; }

        public void Clear()
            => ClearCalled = true;

        public void AddToWorld()
        {
            AddToWorldCalled = true;
            IsInWorld = true;
        }

        public void WarpPlayer(int x, int y)
            => WarpPlayerCalled = true;
    }
}
