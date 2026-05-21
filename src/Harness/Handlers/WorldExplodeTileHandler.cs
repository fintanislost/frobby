using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using StardewValley.Monsters;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>world.explode_tile</c>. Triggers native SDV explosion behavior at a tile.</summary>
public static class WorldExplodeTileHandler
{
    public const string Method = "world.explode_tile";
    internal const int MaxRadius = 10;

    private static readonly IExplodeTileWorld ProductionWorld = new SdvExplodeTileWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, IExplodeTileWorld world)
    {
        var req = RpcParams.Required<ExplodeTileRequest>(paramsElement);
        ValidateRequest(req);

        if (!world.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "no active save - world.explode_tile requires a loaded world");

        var location = world.ResolveLocation(req.Location);
        if (location is null)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"world.explode_tile location not found: {req.Location}");

        var x = req.X!.Value;
        var y = req.Y!.Value;
        var radius = req.Radius;
        ValidateTileBounds(location, x, y);

        var before = world.CountContent(location);
        world.Explode(location, x, y, radius, req.DamagePlayer, req.DamageAmount);
        var after = world.CountContent(location);

        return ProtocolJson.ToElement(new ExplodeTileResult
        {
            Tick = world.Tick,
            Location = location.Name,
            Tile = new TilePoint { X = x, Y = y },
            Radius = radius,
            DamagePlayer = req.DamagePlayer,
            DamageAmount = req.DamageAmount,
            MonstersBefore = before.MonsterCount,
            MonstersAfter = after.MonsterCount,
            DebrisBefore = before.DebrisCount,
            DebrisAfter = after.DebrisCount,
            Invoked = true,
        });
    }

    private static void ValidateRequest(ExplodeTileRequest req)
    {
        if ((req.X is null) != (req.Y is null))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "world.explode_tile requires both x and y");
        if (req.X is null || req.Y is null)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "world.explode_tile requires target tile x and y");
        if (req.X < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.x must be >= 0");
        if (req.Y < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.y must be >= 0");
        if (req.Radius < 1 || req.Radius > MaxRadius)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                $"params.radius must be between 1 and {MaxRadius}");
        if (req.DamageAmount is < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.damage_amount must be >= 0");
    }

    private static void ValidateTileBounds(ExplodeTileLocation location, int x, int y)
    {
        if (location.MapWidth is null || location.MapHeight is null)
            return;

        if (x >= location.MapWidth || y >= location.MapHeight)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "world.explode_tile target tile must be inside the resolved map bounds");
    }
}

internal interface IExplodeTileWorld
{
    bool IsWorldReady { get; }
    int Tick { get; }
    string CurrentLocationName { get; }
    ExplodeTileLocation? ResolveLocation(string? location);
    ExplodeTileCounts CountContent(ExplodeTileLocation location);
    void Explode(ExplodeTileLocation location, int x, int y, int radius, bool damagePlayer, int? damageAmount);
}

internal sealed record ExplodeTileLocation(string Name, int? MapWidth, int? MapHeight, object? NativeLocation = null);

internal sealed record ExplodeTileCounts(int MonsterCount, int DebrisCount);

internal sealed class SdvExplodeTileWorld : IExplodeTileWorld
{
    private const BindingFlags InstanceMemberFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public bool IsWorldReady => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;
    public int Tick => Game1.ticks;
    public string CurrentLocationName => CurrentLocation.NameOrUniqueName ?? CurrentLocation.Name ?? string.Empty;

    public ExplodeTileLocation? ResolveLocation(string? location)
    {
        var native = string.IsNullOrWhiteSpace(location)
            ? CurrentLocation
            : Game1.getLocationFromName(location);
        if (native is null)
            return null;

        return new ExplodeTileLocation(
            native.NameOrUniqueName ?? native.Name ?? string.Empty,
            native.Map?.Layers.FirstOrDefault()?.LayerWidth,
            native.Map?.Layers.FirstOrDefault()?.LayerHeight,
            native);
    }

    public ExplodeTileCounts CountContent(ExplodeTileLocation location)
    {
        var native = RequireNativeLocation(location);
        return new ExplodeTileCounts(
            native.characters.OfType<Monster>().Count(),
            native.debris?.Count ?? 0);
    }

    public void Explode(ExplodeTileLocation location, int x, int y, int radius, bool damagePlayer, int? damageAmount)
    {
        var native = RequireNativeLocation(location);
        InvokeNativeExplosion(native, x, y, radius, damagePlayer, damageAmount);
    }

    private static GameLocation RequireNativeLocation(ExplodeTileLocation location)
        => location.NativeLocation as GameLocation
            ?? throw new JsonRpcException(JsonRpcErrorCode.InternalError,
                "world.explode_tile received a non-Stardew location adapter");

    private static void InvokeNativeExplosion(GameLocation location, int x, int y, int radius, bool damagePlayer, int? damageAmount)
    {
        var tile = new Vector2(x, y);
        var farmer = Game1.player;
        var methods = typeof(GameLocation)
            .GetMethods(InstanceMemberFlags)
            .Where(m => m.Name == "explode")
            .OrderByDescending(m => m.GetParameters().Length)
            .ToList();

        foreach (var method in methods)
        {
            var args = TryBuildExplosionArgs(method, tile, radius, farmer, damagePlayer, damageAmount);
            if (args is null)
                continue;

            method.Invoke(location, args);
            return;
        }

        throw new JsonRpcException(JsonRpcErrorCode.InternalError,
            "world.explode_tile could not find a compatible GameLocation.explode overload");
    }

    private static object?[]? TryBuildExplosionArgs(
        MethodInfo method,
        Vector2 tile,
        int radius,
        Farmer farmer,
        bool damagePlayer,
        int? damageAmount)
    {
        var parameters = method.GetParameters();
        var args = new object?[parameters.Length];
        var assignedVector = false;
        var assignedRadius = false;
        var assignedFarmer = false;

        for (var i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            if (p.ParameterType == typeof(Vector2) && !assignedVector)
            {
                args[i] = tile;
                assignedVector = true;
            }
            else if (p.ParameterType == typeof(int) && !assignedRadius)
            {
                args[i] = radius;
                assignedRadius = true;
            }
            else if (p.ParameterType == typeof(int)
                && damageAmount is not null
                && p.Name is not null
                && p.Name.Contains("damage", StringComparison.OrdinalIgnoreCase))
            {
                args[i] = damageAmount.Value;
            }
            else if (p.ParameterType == typeof(Farmer) && !assignedFarmer)
            {
                args[i] = farmer;
                assignedFarmer = true;
            }
            else if (p.ParameterType == typeof(bool))
            {
                args[i] = p.Name is not null
                    && (p.Name.Contains("farmer", StringComparison.OrdinalIgnoreCase)
                        || p.Name.Contains("player", StringComparison.OrdinalIgnoreCase))
                    ? damagePlayer
                    : p.HasDefaultValue
                        ? p.DefaultValue
                        : false;
            }
            else if (p.HasDefaultValue)
            {
                args[i] = p.DefaultValue;
            }
            else
            {
                return null;
            }
        }

        return assignedVector && assignedRadius ? args : null;
    }

    private static GameLocation CurrentLocation
        => Game1.currentLocation
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"{WorldExplodeTileHandler.Method} requires a current location");
}
