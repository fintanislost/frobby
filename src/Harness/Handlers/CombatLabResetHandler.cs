using System.Linq;
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
        if (req.PlayerX < 0 || req.PlayerY < 0)
        {
            throw new JsonRpcException(
                JsonRpcErrorCode.InvalidParams,
                "params.player_x/player_y must be non-negative");
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
    private const string MapAsset = "Maps/Mines/1";

    public bool IsWorldReady => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;

    public CombatLabResetResult Reset(CombatLabResetRequest request)
    {
        return ResetPreparedLab(request, GetOrCreateLab());
    }

    internal static CombatLabResetResult ResetPreparedLab(CombatLabResetRequest request, ICombatLabLocation lab)
    {
        var mapWidth = lab.MapWidth ?? request.Width;
        var mapHeight = lab.MapHeight ?? request.Height;
        ValidatePlayerTileAgainstMap(request, mapWidth, mapHeight);

        var clearedMonsters = lab.MonsterCount;
        var clearedDebris = lab.DebrisCount;
        lab.Clear();

        if (!lab.IsInWorld)
            lab.AddToWorld();

        if (request.WarpPlayer)
            lab.WarpPlayer(request.PlayerX, request.PlayerY);

        return BuildResetResult(
            request,
            mapWidth,
            mapHeight,
            clearedMonsters,
            clearedDebris);
    }

    internal static void ValidatePlayerTileAgainstMap(CombatLabResetRequest request, int mapWidth, int mapHeight)
    {
        if (request.PlayerX < 0 || request.PlayerY < 0 || request.PlayerX >= mapWidth || request.PlayerY >= mapHeight)
        {
            throw new JsonRpcException(
                JsonRpcErrorCode.InvalidParams,
                "params.player_x/player_y must be inside the combat lab map bounds");
        }
    }

    internal static CombatLabResetResult BuildResetResult(
        CombatLabResetRequest request,
        int? mapWidth,
        int? mapHeight,
        int clearedMonsters,
        int clearedDebris)
    {
        return new CombatLabResetResult
        {
            Location = CombatLabResetHandler.LocationName,
            PlayerTile = new TilePoint { X = request.PlayerX, Y = request.PlayerY },
            MapWidth = mapWidth ?? request.Width,
            MapHeight = mapHeight ?? request.Height,
            ClearedMonsters = clearedMonsters,
            ClearedDebris = clearedDebris,
        };
    }

    private static ICombatLabLocation GetOrCreateLab()
    {
        if (Game1.getLocationFromName(CombatLabResetHandler.LocationName) is { } existing)
            return new SdvCombatLabLocation(existing, isInWorld: true);

        var lab = new GameLocation(MapAsset, CombatLabResetHandler.LocationName);
        return new SdvCombatLabLocation(lab, isInWorld: false);
    }
}

internal interface ICombatLabLocation
{
    bool IsInWorld { get; }
    int? MapWidth { get; }
    int? MapHeight { get; }
    int MonsterCount { get; }
    int DebrisCount { get; }
    void Clear();
    void AddToWorld();
    void WarpPlayer(int x, int y);
}

internal sealed class SdvCombatLabLocation : ICombatLabLocation
{
    private readonly GameLocation location;

    public SdvCombatLabLocation(GameLocation location, bool isInWorld)
    {
        this.location = location;
        IsInWorld = isInWorld;
    }

    public bool IsInWorld { get; private set; }
    public int? MapWidth => location.Map?.Layers.FirstOrDefault()?.LayerWidth;
    public int? MapHeight => location.Map?.Layers.FirstOrDefault()?.LayerHeight;
    public int MonsterCount => location.characters.Count;
    public int DebrisCount => location.debris.Count;

    public void Clear()
    {
        location.characters.Clear();
        location.debris.Clear();
        location.objects.Clear();
    }

    public void AddToWorld()
    {
        if (IsInWorld)
            return;

        Game1.locations.Add(location);
        IsInWorld = true;
    }

    public void WarpPlayer(int x, int y)
    {
        Game1.warpFarmer(CombatLabResetHandler.LocationName, x, y, flip: false);
    }
}
