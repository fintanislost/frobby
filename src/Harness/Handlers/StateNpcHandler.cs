using System.Text.Json;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for the <c>state.npc</c> RPC method. Runs on the game thread.</summary>
public static class StateNpcHandler
{
    public const string Method = "state.npc";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        if (paramsElement is not { } p || !p.TryGetProperty("name", out var nameEl)
            || nameEl.ValueKind != JsonValueKind.String)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.name (string) is required");

        var name = nameEl.GetString()!;
        var npc = Game1.getCharacterFromName(name);
        if (npc is null)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"no NPC named: {name}");

        int friendshipPoints = 0;
        bool giftGivenToday = false;
        if (Game1.player?.friendshipData is { } data && data.TryGetValue(name, out var friendship))
        {
            friendshipPoints = friendship.Points;
            giftGivenToday = friendship.GiftsToday > 0;
        }

        var state = new NpcState
        {
            Name = npc.Name ?? string.Empty,
            Location = npc.currentLocation?.Name ?? string.Empty,
            Tile = new TilePoint { X = npc.TilePoint.X, Y = npc.TilePoint.Y },
            FriendshipPoints = friendshipPoints,
            Hearts = friendshipPoints / 250,
            GiftGivenToday = giftGivenToday,
            Portrait = NormalizePortraitName(npc.Portrait?.Name) ?? npc.Name ?? string.Empty,
        };
        return ProtocolJson.ToElement(state);
    }

    private static string? NormalizePortraitName(string? rawAssetName)
    {
        if (string.IsNullOrEmpty(rawAssetName)) return null;
        // MonoGame's Texture2D.Name is the full asset path as loaded (e.g. "Portraits/Abigail"
        // or "Portraits\\Abigail"). The DTO doc contract promises the bare base name.
        return System.IO.Path.GetFileNameWithoutExtension(rawAssetName.Replace('\\', '/'));
    }
}
