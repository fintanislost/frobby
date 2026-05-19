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
        => Handle(paramsElement, new SdvSecretNoteSeenWorld());

    internal static JsonElement Handle(JsonElement? paramsElement, ISecretNoteSeenWorld world)
    {
        var req = RpcParams.Required<AddSecretNoteSeenRequest>(paramsElement);
        if (req.Id <= 0)
        {
            throw new JsonRpcException(
                JsonRpcErrorCode.InvalidParams,
                "params.id must be a positive secret note id");
        }

        world.RequireWorldReady();

        if (!world.MasterHasSecretNoteSeen(req.Id))
            world.AddMasterSecretNoteSeen(req.Id);

        if (!world.LocalPlayerIsMaster && !world.LocalHasSecretNoteSeen(req.Id))
            world.AddLocalSecretNoteSeen(req.Id);

        return ProtocolJson.ToElement(new MutatorOk
        {
            Tick = world.Tick,
        });
    }
}

internal interface ISecretNoteSeenWorld
{
    int Tick { get; }
    bool LocalPlayerIsMaster { get; }
    void RequireWorldReady();
    bool MasterHasSecretNoteSeen(int id);
    void AddMasterSecretNoteSeen(int id);
    bool LocalHasSecretNoteSeen(int id);
    void AddLocalSecretNoteSeen(int id);
}

internal sealed class SdvSecretNoteSeenWorld : ISecretNoteSeenWorld
{
    public int Tick => Game1.ticks;

    public bool LocalPlayerIsMaster => ReferenceEquals(Game1.player, Game1.MasterPlayer);

    public void RequireWorldReady() => RpcPreconditions.RequireWorldReady();

    public bool MasterHasSecretNoteSeen(int id) => Game1.MasterPlayer.secretNotesSeen.Contains(id);

    public void AddMasterSecretNoteSeen(int id) => Game1.MasterPlayer.secretNotesSeen.Add(id);

    public bool LocalHasSecretNoteSeen(int id) => Game1.player.secretNotesSeen.Contains(id);

    public void AddLocalSecretNoteSeen(int id) => Game1.player.secretNotesSeen.Add(id);
}
