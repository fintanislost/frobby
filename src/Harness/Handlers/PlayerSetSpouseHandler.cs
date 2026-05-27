using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>player.set_spouse</c>. Runs on the game thread.</summary>
public static class PlayerSetSpouseHandler
{
    public const string Method = "player.set_spouse";

    private static readonly IPlayerSetSpouseWorld ProductionWorld = new SdvPlayerSetSpouseWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, IPlayerSetSpouseWorld world)
    {
        var req = RpcParams.Required<SetSpouseRequest>(paramsElement);
        var npc = req.Npc?.Trim() ?? string.Empty;
        if (npc.Length == 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.npc must be non-empty");

        var points = req.Points ?? 2500;
        if (points is < 0 or > 2500)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.points must be between 0 and 2500");

        var weddingDate = ResolveWeddingDate(req, world.CurrentWeddingDate);

        if (!world.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, "player.set_spouse requires a loaded world");

        var state = world.SetSpouse(npc, points, req.Roommate ?? false, weddingDate);

        return ProtocolJson.ToElement(new SetSpouseResult
        {
            Ok = true,
            Tick = world.Tick,
            Spouse = state.Spouse,
            Points = state.Points,
            Status = state.Status,
        });
    }

    private static WeddingDateSpec ResolveWeddingDate(SetSpouseRequest req, WeddingDateSpec currentDate)
    {
        var hasAnyWeddingDate = req.WeddingYear.HasValue
            || req.WeddingSeason is not null
            || req.WeddingDay.HasValue;

        if (!hasAnyWeddingDate)
            return currentDate;

        if (!req.WeddingYear.HasValue || req.WeddingSeason is null || !req.WeddingDay.HasValue)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "params.wedding_year, params.wedding_season, and params.wedding_day must be provided together");

        if (req.WeddingYear < 1)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.wedding_year must be >= 1");

        var season = req.WeddingSeason.Trim().ToLowerInvariant();
        if (season is not "spring" and not "summer" and not "fall" and not "winter")
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                $"params.wedding_season must be one of (spring, summer, fall, winter) (got '{req.WeddingSeason}')");

        if (req.WeddingDay is < 1 or > 28)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                $"params.wedding_day must be between 1 and 28 (got {req.WeddingDay})");

        return new WeddingDateSpec(req.WeddingYear.Value, season, req.WeddingDay.Value);
    }
}

internal interface IPlayerSetSpouseWorld
{
    bool IsWorldReady { get; }
    int Tick { get; }
    WeddingDateSpec CurrentWeddingDate { get; }
    PlayerSpouseState SetSpouse(string npc, int points, bool roommate, WeddingDateSpec weddingDate);
}

internal sealed record WeddingDateSpec(int Year, string Season, int Day);
internal sealed record PlayerSpouseState(string Spouse, int Points, string Status);

internal sealed class SdvPlayerSetSpouseWorld : IPlayerSetSpouseWorld
{
    public bool IsWorldReady => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;
    public int Tick => Game1.ticks;
    public WeddingDateSpec CurrentWeddingDate => new(Game1.year, Game1.currentSeason, Game1.dayOfMonth);

    public PlayerSpouseState SetSpouse(string npc, int points, bool roommate, WeddingDateSpec weddingDate)
    {
        Apply(Game1.MasterPlayer, npc, points, roommate, weddingDate);
        if (!ReferenceEquals(Game1.player, Game1.MasterPlayer))
            Apply(Game1.player, npc, points, roommate, weddingDate);

        return new PlayerSpouseState(npc, points, "married");
    }

    private static void Apply(Farmer farmer, string npc, int points, bool roommate, WeddingDateSpec weddingDate)
    {
        farmer.spouse = npc;

        if (!farmer.friendshipData.TryGetValue(npc, out var friendship))
        {
            friendship = new Friendship();
            farmer.friendshipData[npc] = friendship;
        }

        friendship.Points = points;
        friendship.Status = FriendshipStatus.Married;
        friendship.RoommateMarriage = roommate;
        friendship.WeddingDate = new WorldDate(weddingDate.Year, weddingDate.Season, weddingDate.Day);
    }
}
