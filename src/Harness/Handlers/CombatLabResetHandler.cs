using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// Handler for <c>combat_lab.reset</c>. Creates or clears a neutral harness-owned combat
/// location so scenarios can spawn and target monsters without depending on a mod map.
/// </summary>
public static class CombatLabResetHandler
{
    public const string Method = "combat_lab.reset";
    public const string LocationName = "Frobby_CombatLab";

    private static readonly ICombatLabWorld ProductionWorld = new SdvCombatLabWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, ICombatLabWorld world)
    {
        var req = RpcParams.Optional<CombatLabResetRequest>(paramsElement);
        Validate(req);

        if (!world.IsWorldReady)
            throw new JsonRpcException(
                JsonRpcErrorCode.GameStateInvalid,
                "no active save - combat_lab.reset requires a loaded world");

        CombatLabIdentityRegistry.Clear();
        return ProtocolJson.ToElement(world.Reset(req));
    }

    private static void Validate(CombatLabResetRequest req)
    {
        if (req.Width < 8)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.width must be >= 8");
        if (req.Height < 8)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.height must be >= 8");
        if (req.PlayerX < 0 || req.PlayerY < 0 || req.PlayerX >= req.Width || req.PlayerY >= req.Height)
        {
            throw new JsonRpcException(
                JsonRpcErrorCode.InvalidParams,
                "params.player_x/player_y must be inside the combat lab bounds");
        }
    }
}

internal interface ICombatLabWorld
{
    bool IsWorldReady { get; }
    CombatLabResetResult Reset(CombatLabResetRequest request);
}

internal sealed class SdvCombatLabWorld : ICombatLabWorld
{
    public bool IsWorldReady => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;

    public CombatLabResetResult Reset(CombatLabResetRequest request)
    {
        var lab = GetOrCreateLab();
        var clearedMonsters = lab.characters.Count;
        var clearedDebris = lab.debris.Count;

        lab.characters.Clear();
        lab.debris.Clear();
        lab.objects.Clear();

        if (request.WarpPlayer)
            Game1.warpFarmer(CombatLabResetHandler.LocationName, request.PlayerX, request.PlayerY, flip: false);

        return new CombatLabResetResult
        {
            Location = CombatLabResetHandler.LocationName,
            PlayerTile = new TilePoint { X = request.PlayerX, Y = request.PlayerY },
            MapWidth = request.Width,
            MapHeight = request.Height,
            ClearedMonsters = clearedMonsters,
            ClearedDebris = clearedDebris,
        };
    }

    private static GameLocation GetOrCreateLab()
    {
        if (Game1.getLocationFromName(CombatLabResetHandler.LocationName) is { } existing)
            return existing;

        var lab = new GameLocation("Maps/Mines/1", CombatLabResetHandler.LocationName);
        Game1.locations.Add(lab);
        return lab;
    }
}
