using System;
using System.Collections.Generic;
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
/// Handler for <c>combat_lab.relocate_monster</c>. Moves an existing runtime monster into
/// the neutral Combat Lab and assigns a stable run-local identity for later targeting.
/// </summary>
public static class CombatLabRelocateMonsterHandler
{
    public const string Method = "combat_lab.relocate_monster";

    private static readonly ICombatLabRelocateWorld ProductionWorld = new SdvCombatLabRelocateWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, ICombatLabRelocateWorld world)
    {
        var req = RpcParams.Required<CombatLabRelocateMonsterRequest>(paramsElement);
        Validate(req);

        if (!world.IsWorldReady)
            throw new JsonRpcException(
                JsonRpcErrorCode.GameStateInvalid,
                "no active save - combat_lab.relocate_monster requires a loaded world");

        return ProtocolJson.ToElement(world.RelocateMonster(req));
    }

    private static void Validate(CombatLabRelocateMonsterRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.FromLocation))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "combat_lab.relocate_monster requires from_location");
        if (req.TargetX < 0 || req.TargetY < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "combat_lab.relocate_monster requires non-negative target_x and target_y");
        if (req.Match is null || !CombatLabMonsterMatcher.HasAnyFilter(req.Match))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "combat_lab.relocate_monster requires at least one match filter");
    }
}

internal interface ICombatLabRelocateWorld
{
    bool IsWorldReady { get; }
    CombatLabRelocateMonsterResult RelocateMonster(CombatLabRelocateMonsterRequest request);
}

internal interface ICombatLabRelocateLocation
{
    string Name { get; }
    int? MapWidth { get; }
    int? MapHeight { get; }
    IReadOnlyList<ICombatLabRelocatableMonster> Monsters { get; }
    void Remove(ICombatLabRelocatableMonster monster);
    void Add(ICombatLabRelocatableMonster monster);
}

internal interface ICombatLabRelocatableMonster
{
    object IdentityKey { get; }
    TilePoint Tile { get; }
    MonsterSummary Project();
    void MoveTo(ICombatLabRelocateLocation location, int x, int y);
}

internal sealed class SdvCombatLabRelocateWorld : ICombatLabRelocateWorld
{
    public bool IsWorldReady => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;

    public CombatLabRelocateMonsterResult RelocateMonster(CombatLabRelocateMonsterRequest request)
    {
        var source = Game1.getLocationFromName(request.FromLocation)
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"combat_lab.relocate_monster source location not found: {request.FromLocation}");
        var lab = Game1.getLocationFromName(CombatLabResetHandler.LocationName)
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, "combat_lab.relocate_monster requires combat_lab.reset first");

        return RelocatePreparedMonster(
            request,
            new SdvCombatLabRelocateLocation(source),
            new SdvCombatLabRelocateLocation(lab));
    }

    internal static CombatLabRelocateMonsterResult RelocatePreparedMonster(
        CombatLabRelocateMonsterRequest request,
        ICombatLabRelocateLocation source,
        ICombatLabRelocateLocation lab)
    {
        ValidateTargetTileAgainstMap(request, lab.MapWidth, lab.MapHeight);

        var matches = source.Monsters
            .Select(monster => new { Monster = monster, Summary = monster.Project() })
            .Where(entry => CombatLabMonsterMatcher.Matches(entry.Summary, request.Match))
            .ToList();

        if (matches.Count == 0)
        {
            throw new JsonRpcException(
                JsonRpcErrorCode.GameStateInvalid,
                $"combat_lab.relocate_monster matched no monsters in {request.FromLocation} with {CombatLabMonsterMatcher.Describe(request.Match)}");
        }
        if (matches.Count > 1)
        {
            throw new JsonRpcException(
                JsonRpcErrorCode.GameStateInvalid,
                $"combat_lab.relocate_monster matched {matches.Count} monsters in {request.FromLocation}; use a tighter selector than {CombatLabMonsterMatcher.Describe(request.Match)}");
        }

        var selected = matches[0];
        var sourceTile = selected.Summary.Tile;
        source.Remove(selected.Monster);
        selected.Monster.MoveTo(lab, request.TargetX, request.TargetY);
        lab.Add(selected.Monster);

        var identity = CombatLabIdentityRegistry.Assign(selected.Monster.IdentityKey, request.Label, spawnedByFrobby: false);
        var relocated = selected.Monster.Project();
        return new CombatLabRelocateMonsterResult
        {
            MonsterId = identity.MonsterId,
            Label = identity.Label,
            FromLocation = request.FromLocation,
            SourceTile = sourceTile,
            Location = CombatLabResetHandler.LocationName,
            Tile = relocated.Tile,
            Name = relocated.Name,
            Type = relocated.Type,
            SpriteTexture = relocated.SpriteTexture,
            Health = relocated.Health,
            MaxHealth = relocated.MaxHealth,
        };
    }

    internal static void ValidateTargetTileAgainstMap(CombatLabRelocateMonsterRequest request, int? mapWidth, int? mapHeight)
    {
        if (mapWidth is null || mapHeight is null)
            return;

        if (request.TargetX >= mapWidth || request.TargetY >= mapHeight)
        {
            throw new JsonRpcException(
                JsonRpcErrorCode.InvalidParams,
                "combat_lab.relocate_monster target tile must be inside the combat lab map bounds");
        }
    }
}

internal sealed class SdvCombatLabRelocateLocation : ICombatLabRelocateLocation
{
    private readonly GameLocation location;

    public SdvCombatLabRelocateLocation(GameLocation location)
        => this.location = location;

    public string Name => location.Name;
    public int? MapWidth => location.Map?.Layers.FirstOrDefault()?.LayerWidth;
    public int? MapHeight => location.Map?.Layers.FirstOrDefault()?.LayerHeight;
    public IReadOnlyList<ICombatLabRelocatableMonster> Monsters
        => location.characters.OfType<Monster>().Select(monster => new SdvCombatLabRelocatableMonster(monster)).ToList();

    internal GameLocation Location => location;

    public void Remove(ICombatLabRelocatableMonster monster)
    {
        if (monster is not SdvCombatLabRelocatableMonster sdvMonster)
            throw new InvalidOperationException("combat_lab.relocate_monster received an incompatible monster adapter");

        location.characters.Remove(sdvMonster.Monster);
    }

    public void Add(ICombatLabRelocatableMonster monster)
    {
        if (monster is not SdvCombatLabRelocatableMonster sdvMonster)
            throw new InvalidOperationException("combat_lab.relocate_monster received an incompatible monster adapter");

        location.characters.Add(sdvMonster.Monster);
    }
}

internal sealed class SdvCombatLabRelocatableMonster : ICombatLabRelocatableMonster
{
    public SdvCombatLabRelocatableMonster(Monster monster)
        => Monster = monster;

    public Monster Monster { get; }
    public object IdentityKey => Monster;
    public TilePoint Tile => LocationContentProjector.ProjectMonsterForTests(Monster).Tile;

    public MonsterSummary Project()
        => LocationContentProjector.ProjectMonsterForTests(Monster);

    public void MoveTo(ICombatLabRelocateLocation location, int x, int y)
    {
        if (location is not SdvCombatLabRelocateLocation sdvLocation)
            throw new InvalidOperationException("combat_lab.relocate_monster received an incompatible location adapter");

        Monster.Position = new Vector2(x * 64f, y * 64f);
        Monster.currentLocation = sdvLocation.Location;
    }
}
