using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>state.npcs</c>. Runs on the game thread.</summary>
public static class StateNpcsHandler
{
    public const string Method = "state.npcs";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var req = RpcParams.Optional<NpcsStateRequest>(paramsElement);
        if (req.Limit < 1 || req.Limit > 1000)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.limit must be between 1 and 1000");

        var npcs = req.IncludeOffscreen ? AllLoadedLocationNpcs() : CurrentLocationNpcs();
        return ProtocolJson.ToElement(new NpcsState
        {
            Npcs = NpcStateProjector.ProjectMany(npcs, Game1.player, req.Limit),
        });
    }

    private static IEnumerable<NPC> AllLoadedLocationNpcs()
        => Game1.locations is null
            ? CurrentLocationNpcs()
            : Game1.locations
                .Where(location => location?.characters is not null)
                .SelectMany(location => location.characters);

    private static IEnumerable<NPC> CurrentLocationNpcs()
        => Game1.currentLocation?.characters ?? Enumerable.Empty<NPC>();
}
