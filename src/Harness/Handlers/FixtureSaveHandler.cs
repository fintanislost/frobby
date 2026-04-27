using System;
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
/// Handler for <c>fixture.save</c>. Drives SDV's <see cref="SaveGame.Save"/> synchronously
/// on the game thread (the handler already runs there via GameThreadDispatch), then returns
/// the absolute save path. Preconditions mirror <c>FreezeBeginHandler</c>.
/// </summary>
public static class FixtureSaveHandler
{
    public const string Method = "fixture.save";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var req = RpcParams.Required<FixtureSaveRequest>(paramsElement);
        if (string.IsNullOrEmpty(req.Name))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.name required");

        // Preconditions — same predicate as FreezeBeginHandler (D1.7 widened).
        if (Game1.gameMode != Game1.playingGameMode || !Game1.hasLoadedGame)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "fixture.save requires a loaded world (no active save)");
        if (Game1.eventUp)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "fixture.save requires !Game1.eventUp (event active)");
        if (Game1.currentMinigame != null)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "fixture.save requires Game1.currentMinigame == null (minigame active)");
        if (Game1.isWarping)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "fixture.save requires !Game1.isWarping (mid-warp)");

        // Marker so framework-created saves can be identified later. Harmless flavor field.
        Game1.player.favoriteThing.Value = "sdv-test-fixture";

        // Drive the save to completion. SDV 1.6's SaveGame.Save() is a synchronous void
        // call that drives its internal coroutine to completion on the calling thread.
        // The handler runs on the game thread (via GameThreadDispatch), so this is safe.
        DriveSaveToCompletion();

        // SDV writes saves to Constants.SavesPath/<farmName>_<uniqueID>. The Runner can
        // copy the output elsewhere post-save; the handler just reports where SDV wrote.
        var savePath = Path.Combine(
            Constants.SavesPath,
            Game1.player.farmName.Value + "_" + Game1.uniqueIDForThisGame);

        return ProtocolJson.ToElement(new FixtureSaveResult
        {
            Ok = true,
            Tick = Game1.ticks,
            SavePath = savePath,
        });
    }

    /// <summary>
    /// Drive <see cref="SaveGame.Save"/> to completion. In SDV 1.6 this is a synchronous
    /// void call that internally runs its save coroutine to completion. The handler runs on
    /// the game thread (via GameThreadDispatch), so blocking here is safe — SDV saves
    /// typically complete in under one second.
    /// </summary>
    private static void DriveSaveToCompletion()
    {
        // SDV 1.6: SaveGame.Save() is synchronous — confirmed in docs/spikes/2026-04-determinism.
        // The spike's harness_save command used the same direct call pattern successfully.
        SaveGame.Save();
    }
}
