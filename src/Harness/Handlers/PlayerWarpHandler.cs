using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// Handler for the <c>player.warp</c> RPC method. Queues a warp of the local farmer to
/// (<c>x</c>, <c>y</c>) in the named location. Runs on the game thread.
/// </summary>
/// <remarks>
/// <c>Game1.warpFarmer</c> is queued internally by SDV — the warp completes a few ticks
/// later. Scenarios should use the response's <see cref="MutatorOk.Tick"/> as the temporal
/// anchor and poll <c>state.player</c> until <c>location</c> matches the target.
/// </remarks>
public static class PlayerWarpHandler
{
    public const string Method = "player.warp";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var req = RpcParams.Required<WarpRequest>(paramsElement);
        if (string.IsNullOrEmpty(req.Location))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.location required");

        if (Game1.getLocationFromName(req.Location) is null)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"no location named: {req.Location}");

        // flip=false: no facing-direction override; SDV derives facing from the warp source.
        Game1.warpFarmer(req.Location, req.X, req.Y, flip: false);

        return ProtocolJson.ToElement(new MutatorOk { Tick = Game1.ticks });
    }
}
