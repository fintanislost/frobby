using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// Handler for the <c>player.add_mail</c> RPC method. Adds a received-mail flag to
/// the master farmer so scenarios can exercise real save-state conditions without
/// mod-specific hooks.
/// </summary>
public static class PlayerAddMailHandler
{
    public const string Method = "player.add_mail";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var req = RpcParams.Required<AddMailRequest>(paramsElement);
        if (string.IsNullOrWhiteSpace(req.Id))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.id must be non-empty");

        RpcPreconditions.RequireWorldReady();

        Game1.MasterPlayer.mailReceived.Add(req.Id.Trim());
        return ProtocolJson.ToElement(new MutatorOk
        {
            Tick = Game1.ticks,
        });
    }
}
