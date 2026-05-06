using System.Text.Json;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
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

        var state = NpcStateProjector.Project(npc, Game1.player);
        return ProtocolJson.ToElement(state);
    }
}
