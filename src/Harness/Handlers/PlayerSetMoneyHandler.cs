using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// Handler for the <c>player.set_money</c> RPC method. Sets the local farmer's money to an
/// absolute value. Returns the prior value so scenarios can correlate deltas without an
/// extra query. Runs on the game thread.
/// </summary>
public static class PlayerSetMoneyHandler
{
    public const string Method = "player.set_money";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var req = RpcParams.Required<SetMoneyRequest>(paramsElement);
        if (req.Amount < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.amount must be >= 0");

        RpcPreconditions.RequireWorldReady();

        int previous = Game1.player.Money;
        Game1.player.Money = req.Amount;

        return ProtocolJson.ToElement(new SetMoneyResult
        {
            Tick = Game1.ticks,
            Previous = previous,
        });
    }
}
