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

    private static readonly IFixtureLoadWorld ProductionWorld = new SdvFixtureLoadWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, IFixtureLoadWorld world)
    {
        var req = RpcParams.Required<FixtureLoadRequest>(paramsElement);
        if (string.IsNullOrEmpty(req.Name))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.name required");

        if (world.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "already in a save — return to title first");

        // Verify the save folder exists before handing it to SDV's lazy enumerator — the
        // enumerator doesn't fail until a later tick tries to read the file, which would
        // leave the runner polling for world-ready until timeout. Folder-only check is
        // sufficient: SDV will error later if the inner file is missing.
        var saveFolder = world.SavePath(req.Name);
        if (!world.SaveExists(req.Name))
            throw new JsonRpcException(JsonRpcErrorCode.FixtureLoadFailed,
                $"no save named '{req.Name}' (looked in {saveFolder})");

        world.ClearActiveMenu();
        world.QueueLoad(req.Name);

        return ProtocolJson.ToElement(new MutatorOk { Tick = world.Tick });
    }
}

internal interface IFixtureLoadWorld
{
    bool IsWorldReady { get; }
    int Tick { get; }
    bool SaveExists(string name);
    string SavePath(string name);
    void ClearActiveMenu();
    void QueueLoad(string name);
}

internal sealed class SdvFixtureLoadWorld : IFixtureLoadWorld
{
    public bool IsWorldReady => Context.IsWorldReady;
    public int Tick => Game1.ticks;
    public bool SaveExists(string name) => Directory.Exists(SavePath(name));
    public string SavePath(string name) => Path.Combine(Constants.SavesPath, name);

    public void ClearActiveMenu()
    {
        Game1.activeClickableMenu = null;
    }

    public void QueueLoad(string name)
    {
        Game1.currentLoader = SaveGame.getLoadEnumerator(name);
        Game1.gameMode = 6;
    }
}
