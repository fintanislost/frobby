using System.IO;
using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewModdingAPI;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// Handler for <c>fixture.load</c>. Initiates an asynchronous SDV save-load by folder
/// name. Returns as soon as the load is queued; the save-load coroutine runs over the next
/// several ticks. Callers must poll <c>state.player</c> (or similar) to detect completion.
/// </summary>
/// <remarks>
/// This is the RPC equivalent of the <c>harness_load</c> console command. Same underlying
/// mechanism: <c>Game1.currentLoader = SaveGame.getLoadEnumerator(name)</c> then flip
/// gameMode to 6 (loadingMode). SMAPI's <c>SaveLoaded</c> event fires when complete.
/// </remarks>
public static class FixtureLoadHandler
{
    public const string Method = "fixture.load";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var req = RpcParams.Required<FixtureLoadRequest>(paramsElement);
        if (string.IsNullOrEmpty(req.Name))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.name required");

        if (Context.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "already in a save — return to title first");

        // Verify the save folder exists before handing it to SDV's lazy enumerator — the
        // enumerator doesn't fail until a later tick tries to read the file, which would
        // leave the runner polling for world-ready until timeout. Folder-only check is
        // sufficient: SDV will error later if the inner file is missing.
        var saveFolder = Path.Combine(Constants.SavesPath, req.Name);
        if (!Directory.Exists(saveFolder))
            throw new JsonRpcException(JsonRpcErrorCode.FixtureLoadFailed,
                $"no save named '{req.Name}' (looked in {saveFolder})");

        Game1.currentLoader = SaveGame.getLoadEnumerator(req.Name);
        Game1.gameMode = 6;

        return ProtocolJson.ToElement(new MutatorOk { Tick = Game1.ticks });
    }
}
