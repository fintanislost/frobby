using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// Handler for the <c>time.advance</c> RPC method. Advances SDV's in-game clock by a
/// multiple of 10 minutes (the smallest game-time step). Runs on the game thread.
/// </summary>
/// <remarks>
/// Calls <c>Game1.performTenMinuteClockUpdate</c> once per 10-minute step; this triggers
/// any scheduled NPC pathing / event updates that key off clock advance, so callers should
/// consider time-advance side effects (weather tick, shop restock boundaries, etc.).
/// </remarks>
public static class TimeAdvanceHandler
{
    public const string Method = "time.advance";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var req = RpcParams.Required<TimeAdvanceRequest>(paramsElement);
        if (req.Minutes < 10 || req.Minutes > 120 || req.Minutes % 10 != 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "params.minutes must be a multiple of 10, between 10 and 120");

        RpcPreconditions.RequireWorldReady();

        int steps = req.Minutes / 10;
        for (int i = 0; i < steps; i++)
            Game1.performTenMinuteClockUpdate();

        return ProtocolJson.ToElement(new TimeAdvanceResult
        {
            Tick = Game1.ticks,
            NewTimeOfDay = Game1.timeOfDay,
        });
    }
}
