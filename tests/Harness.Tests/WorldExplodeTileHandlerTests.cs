using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using StardewValley;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class WorldExplodeTileHandlerTests
{
    [Fact]
    public void Handle_MissingX_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"location\":\"Frobby_CombatLab\",\"y\":8,\"radius\":2}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => WorldExplodeTileHandler.Handle(p, new FakeExplodeTileWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("x and y", ex.Message);
    }

    [Fact]
    public void Handle_MissingY_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"location\":\"Frobby_CombatLab\",\"x\":9,\"radius\":2}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => WorldExplodeTileHandler.Handle(p, new FakeExplodeTileWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("x and y", ex.Message);
    }

    [Fact]
    public void Handle_NegativeTile_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"x\":-1,\"y\":8,\"radius\":2}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => WorldExplodeTileHandler.Handle(p, new FakeExplodeTileWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains(">= 0", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Handle_InvalidRadius_ThrowsInvalidParams(int radius)
    {
        var p = JsonDocument.Parse($"{{\"x\":9,\"y\":8,\"radius\":{radius}}}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => WorldExplodeTileHandler.Handle(p, new FakeExplodeTileWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("radius", ex.Message);
    }

    [Fact]
    public void Handle_NotWorldReady_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"x\":9,\"y\":8,\"radius\":2}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldExplodeTileHandler.Handle(p, new FakeExplodeTileWorld { IsWorldReady = false }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("loaded world", ex.Message);
    }

    [Fact]
    public void Handle_UnknownLocation_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"location\":\"Missing\",\"x\":9,\"y\":8,\"radius\":2}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldExplodeTileHandler.Handle(p, new FakeExplodeTileWorld { LocationExists = false }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("location not found", ex.Message);
    }

    [Fact]
    public void Handle_OutOfBoundsTile_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"location\":\"Frobby_CombatLab\",\"x\":20,\"y\":8,\"radius\":2}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldExplodeTileHandler.Handle(p, new FakeExplodeTileWorld { MapWidth = 20, MapHeight = 14 }));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("map bounds", ex.Message);
    }

    [Fact]
    public void Handle_ValidRequest_InvokesExplosionAndReturnsDiagnostics()
    {
        var world = new FakeExplodeTileWorld
        {
            CurrentLocationName = "Farm",
            ResolvedLocationName = "Frobby_CombatLab",
            MapWidth = 20,
            MapHeight = 14,
            Tick = 456,
            MonstersBefore = 1,
            MonstersAfter = 0,
            DebrisBefore = 0,
            DebrisAfter = 1,
        };
        var p = JsonDocument.Parse("{\"location\":\"Frobby_CombatLab\",\"x\":9,\"y\":8,\"radius\":2,\"damage_player\":false}").RootElement;

        var result = WorldExplodeTileHandler.Handle(p, world);
        var json = result.GetRawText();

        Assert.Equal("Frobby_CombatLab", world.InvokedLocation);
        Assert.Equal(9, world.InvokedX);
        Assert.Equal(8, world.InvokedY);
        Assert.Equal(2, world.InvokedRadius);
        Assert.False(world.InvokedDamagePlayer);
        Assert.Contains("\"location\":\"Frobby_CombatLab\"", json);
        Assert.Contains("\"tile\":{\"x\":9,\"y\":8}", json);
        Assert.Contains("\"monsters_before\":1", json);
        Assert.Contains("\"monsters_after\":0", json);
        Assert.Contains("\"invoked\":true", json);
    }

    [Fact]
    public void Handle_OmittedLocation_UsesCurrentLocation()
    {
        var world = new FakeExplodeTileWorld
        {
            CurrentLocationName = "Farm",
            ResolvedLocationName = "Farm",
            MapWidth = 80,
            MapHeight = 65,
        };
        var p = JsonDocument.Parse("{\"x\":9,\"y\":8}").RootElement;

        var result = WorldExplodeTileHandler.Handle(p, world);

        Assert.Equal("Farm", world.InvokedLocation);
        Assert.Equal(2, world.InvokedRadius);
        Assert.Contains("\"radius\":2", result.GetRawText());
    }

    [Fact]
    public void NativeExplosionArgs_PreserveOptionalDefaultsExceptDamageFarmers()
    {
        var nativeExplode = typeof(GameLocation)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(m =>
            {
                if (m.Name != "explode")
                    return false;
                var p = m.GetParameters();
                return p.Length == 6
                    && p[0].ParameterType == typeof(Vector2)
                    && p[1].ParameterType == typeof(int)
                    && p[2].ParameterType == typeof(Farmer)
                    && p[3].ParameterType == typeof(bool)
                    && p[4].ParameterType == typeof(int)
                    && p[5].ParameterType == typeof(bool);
            });
        var builder = typeof(SdvExplodeTileWorld).GetMethod(
            "TryBuildExplosionArgs",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        var args = (object?[]?)builder.Invoke(null, new object?[]
        {
            nativeExplode,
            new Vector2(9, 8),
            2,
            null,
            false,
        });

        Assert.NotNull(args);
        Assert.Equal(new Vector2(9, 8), args[0]);
        Assert.Equal(2, args[1]);
        Assert.Null(args[2]);
        Assert.False((bool)args[3]!);
        Assert.Equal(-1, args[4]);
        Assert.True((bool)args[5]!);
    }

    private sealed class FakeExplodeTileWorld : IExplodeTileWorld
    {
        public bool IsWorldReady { get; set; } = true;
        public string CurrentLocationName { get; set; } = "Frobby_CombatLab";
        public string ResolvedLocationName { get; set; } = "Frobby_CombatLab";
        public bool LocationExists { get; set; } = true;
        public int? MapWidth { get; set; } = 20;
        public int? MapHeight { get; set; } = 14;
        public int Tick { get; set; } = 123;
        public int MonstersBefore { get; set; }
        public int MonstersAfter { get; set; }
        public int DebrisBefore { get; set; }
        public int DebrisAfter { get; set; }
        public string? InvokedLocation { get; private set; }
        public int? InvokedX { get; private set; }
        public int? InvokedY { get; private set; }
        public int? InvokedRadius { get; private set; }
        public bool? InvokedDamagePlayer { get; private set; }

        public ExplodeTileLocation? ResolveLocation(string? location)
        {
            if (!LocationExists)
                return null;

            return new ExplodeTileLocation(
                string.IsNullOrWhiteSpace(location) ? CurrentLocationName : ResolvedLocationName,
                MapWidth,
                MapHeight);
        }

        public ExplodeTileCounts CountContent(ExplodeTileLocation location)
            => InvokedLocation is null
                ? new ExplodeTileCounts(MonstersBefore, DebrisBefore)
                : new ExplodeTileCounts(MonstersAfter, DebrisAfter);

        public void Explode(ExplodeTileLocation location, int x, int y, int radius, bool damagePlayer)
        {
            InvokedLocation = location.Name;
            InvokedX = x;
            InvokedY = y;
            InvokedRadius = radius;
            InvokedDamagePlayer = damagePlayer;
        }
    }
}
