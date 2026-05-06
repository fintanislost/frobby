using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>player.set_friendship</c>. Runs on the game thread.</summary>
public static class PlayerSetFriendshipHandler
{
    public const string Method = "player.set_friendship";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var req = RpcParams.Required<SetFriendshipRequest>(paramsElement);
        var npc = req.Npc?.Trim() ?? string.Empty;
        if (npc.Length == 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.npc must be non-empty");
        if (req.Points is null || req.Points < 0 || req.Points > 2500)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.points must be between 0 and 2500");
        ValidateGiftCount(req.GiftsToday, "gifts_today");
        ValidateGiftCount(req.GiftsThisWeek, "gifts_this_week");

        RpcPreconditions.RequireWorldReady();

        if (!Game1.MasterPlayer.friendshipData.TryGetValue(npc, out var friendship))
        {
            friendship = new Friendship();
            Game1.MasterPlayer.friendshipData[npc] = friendship;
        }

        friendship.Points = req.Points.Value;
        if (req.TalkedToToday.HasValue) friendship.TalkedToToday = req.TalkedToToday.Value;
        if (req.GiftsToday.HasValue) friendship.GiftsToday = req.GiftsToday.Value;
        if (req.GiftsThisWeek.HasValue) friendship.GiftsThisWeek = req.GiftsThisWeek.Value;

        return ProtocolJson.ToElement(new MutatorOk { Tick = Game1.ticks });
    }

    private static void ValidateGiftCount(int? value, string field)
    {
        if (value is < 0 or > 2)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, $"params.{field} must be between 0 and 2");
    }
}
