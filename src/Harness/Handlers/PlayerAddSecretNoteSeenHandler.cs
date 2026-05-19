using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// Handler for <c>player.add_secret_note_seen</c>. Adds a secret-note id to the farmer's
/// seen-note set so scenarios can exercise note-gated mod content without custom hooks.
/// </summary>
public static class PlayerAddSecretNoteSeenHandler
{
    public const string Method = "player.add_secret_note_seen";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var req = RpcParams.Required<AddSecretNoteSeenRequest>(paramsElement);
        if (req.Id <= 0)
        {
            throw new JsonRpcException(
                JsonRpcErrorCode.InvalidParams,
                "params.id must be a positive secret note id");
        }

        RpcPreconditions.RequireWorldReady();

        Game1.MasterPlayer.secretNotesSeen.Add(req.Id);
        if (!ReferenceEquals(Game1.player, Game1.MasterPlayer))
            Game1.player.secretNotesSeen.Add(req.Id);

        return ProtocolJson.ToElement(new MutatorOk
        {
            Tick = Game1.ticks,
        });
    }
}
