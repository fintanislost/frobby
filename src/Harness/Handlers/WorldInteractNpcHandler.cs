using System.Linq;
using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// Handler for <c>world.interact_npc</c>. Triggers an NPC interaction by directly invoking
/// <see cref="NPC.checkAction"/> — the same call SDV makes when the player presses action
/// while facing an NPC at conversation distance. The NPC must be present in the player's
/// current location; otherwise the handler returns <c>GameStateInvalid</c> rather than
/// silently warping (test authors should warp explicitly first).
/// </summary>
public static class WorldInteractNpcHandler
{
    public const string Method = "world.interact_npc";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var req = RpcParams.Required<WorldInteractNpcRequest>(paramsElement);
        if (string.IsNullOrEmpty(req.Name))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "name is required");

        RpcPreconditions.RequireWorldReady();

        var location = Game1.currentLocation
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"{Method} requires a current location");

        var npc = location.characters?.FirstOrDefault(c => c?.Name == req.Name)
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"NPC '{req.Name}' not found in current location '{location.Name}'");

        // Return value is intentionally ignored — some interactions return false even
        // when they successfully triggered something (e.g. dialogue that routes through
        // a different code path than checkAction's boolean contract implies).
        npc.checkAction(Game1.player, location);

        return ProtocolJson.ToElement(new MutatorOk { Tick = Game1.ticks });
    }
}
