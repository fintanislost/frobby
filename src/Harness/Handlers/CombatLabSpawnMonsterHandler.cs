using System;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using StardewValley.Monsters;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// Handler for <c>combat_lab.spawn_monster</c>. Spawns a supported vanilla monster in the
/// neutral Combat Lab and assigns a stable run-local identity for later targeting.
/// </summary>
public static class CombatLabSpawnMonsterHandler
{
    public const string Method = "combat_lab.spawn_monster";

    private static readonly ICombatLabSpawnWorld ProductionWorld = new SdvCombatLabSpawnWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, ICombatLabSpawnWorld world)
    {
        var req = RpcParams.Required<CombatLabSpawnMonsterRequest>(paramsElement);
        Validate(req);

        if (!world.IsWorldReady)
            throw new JsonRpcException(
                JsonRpcErrorCode.GameStateInvalid,
                "no active save - combat_lab.spawn_monster requires a loaded world");

        return ProtocolJson.ToElement(world.SpawnMonster(req));
    }

    internal static bool IsSupportedKind(string? kind)
        => kind is "GreenSlime" or "Bat";

    private static void Validate(CombatLabSpawnMonsterRequest req)
    {
        if (!IsSupportedKind(req.Kind))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, $"unsupported monster kind: {req.Kind}");
        if (req.X < 0 || req.Y < 0)
            throw new JsonRpcException(
                JsonRpcErrorCode.InvalidParams,
                "combat_lab.spawn_monster requires non-negative x and y");
        if (req.Health is <= 0)
        {
            throw new JsonRpcException(
                JsonRpcErrorCode.InvalidParams,
                "combat_lab.spawn_monster health must be positive when supplied");
        }
    }
}

internal interface ICombatLabSpawnWorld
{
    bool IsWorldReady { get; }
    CombatLabSpawnMonsterResult SpawnMonster(CombatLabSpawnMonsterRequest request);
}

internal sealed class SdvCombatLabSpawnWorld : ICombatLabSpawnWorld
{
    public bool IsWorldReady => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;

    public CombatLabSpawnMonsterResult SpawnMonster(CombatLabSpawnMonsterRequest request)
    {
        var lab = Game1.getLocationFromName(CombatLabResetHandler.LocationName)
            ?? throw new JsonRpcException(
                JsonRpcErrorCode.GameStateInvalid,
                "combat_lab.spawn_monster requires combat_lab.reset first");
        var mapLayer = lab.Map?.Layers.FirstOrDefault();
        ValidateSpawnTileAgainstMap(request, mapLayer?.LayerWidth, mapLayer?.LayerHeight);

        var monster = CreateMonster(request);
        if (request.Health is { } health)
        {
            monster.Health = health;
            monster.MaxHealth = Math.Max(monster.MaxHealth, health);
        }

        monster.currentLocation = lab;
        lab.characters.Add(monster);

        var identity = CombatLabIdentityRegistry.Assign(monster, request.Label);
        return new CombatLabSpawnMonsterResult
        {
            MonsterId = identity.MonsterId,
            Label = identity.Label,
            Kind = request.Kind,
            Location = CombatLabResetHandler.LocationName,
            Tile = new TilePoint { X = request.X, Y = request.Y },
            Health = monster.Health,
            MaxHealth = monster.MaxHealth,
        };
    }

    internal static void ValidateSpawnTileAgainstMap(CombatLabSpawnMonsterRequest request, int? mapWidth, int? mapHeight)
    {
        if (mapWidth is null || mapHeight is null)
            return;

        if (request.X >= mapWidth || request.Y >= mapHeight)
        {
            throw new JsonRpcException(
                JsonRpcErrorCode.InvalidParams,
                "combat_lab.spawn_monster tile must be inside the combat lab map bounds");
        }
    }

    private static Monster CreateMonster(CombatLabSpawnMonsterRequest request)
    {
        var position = new Vector2(request.X * 64f, request.Y * 64f);
        return request.Kind switch
        {
            "GreenSlime" => new GreenSlime(position, 0),
            "Bat" => new Bat(position, 0),
            _ => throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, $"unsupported monster kind: {request.Kind}"),
        };
    }
}
