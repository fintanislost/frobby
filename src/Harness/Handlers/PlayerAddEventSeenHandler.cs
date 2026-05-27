using System;
using System.Globalization;
using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// Handler for <c>player.add_event_seen</c>. Adds a numeric event id to the farmer's
/// seen-event set so scenarios can exercise event-gated mod content without custom hooks.
/// </summary>
public static class PlayerAddEventSeenHandler
{
    public const string Method = "player.add_event_seen";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var req = RpcParams.Required<AddEventSeenRequest>(paramsElement);
        if (!int.TryParse(req.Id?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var eventId))
        {
            throw new JsonRpcException(
                JsonRpcErrorCode.InvalidParams,
                "params.id must be a numeric event id");
        }

        RpcPreconditions.RequireWorldReady();

        foreach (var eventSeenId in EventSeenIds(req.Id.Trim()))
        {
            Game1.MasterPlayer.eventsSeen.Add(eventSeenId);
            if (!ReferenceEquals(Game1.player, Game1.MasterPlayer))
                Game1.player.eventsSeen.Add(eventSeenId);
        }

        return ProtocolJson.ToElement(new MutatorOk
        {
            Tick = Game1.ticks,
        });
    }

    internal static string[] EventSeenIds(string id)
    {
        var normalizedId = int.Parse(id, NumberStyles.Integer, CultureInfo.InvariantCulture)
            .ToString(CultureInfo.InvariantCulture);
        return string.Equals(id, normalizedId, StringComparison.Ordinal)
            ? new[] { normalizedId }
            : new[] { id, normalizedId };
    }
}
