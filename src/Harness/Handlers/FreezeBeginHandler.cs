using System.Text.Json;
using SdvTestFramework.Harness.Determinism;
using SdvTestFramework.Harness.Scenarios;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewModdingAPI;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>freeze.begin</c>. Enforces strict preconditions, calls into
/// <see cref="DeterminismController.EnterFreeze"/>, returns pinned-count metrics.</summary>
public static class FreezeBeginHandler
{
    public const string Method = "freeze.begin";

    /// <summary>Set by <c>ModEntry</c> at startup so orchestration can log.</summary>
    public static IMonitor? Monitor { get; set; }

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var s = ScenarioState.Current;
        if (!s.IsActive)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "freeze.begin requires an active scenario (call scenario.begin first)");

        if (DeterminismController.Frozen)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "freeze.begin requires Frozen == false (already frozen)");

        // Widened predicate per D1.7 T1 — Context.IsWorldReady stays false under headless
        // Xvfb even after gameMode transitions to playingGameMode.
        if (Game1.gameMode != Game1.playingGameMode || !Game1.hasLoadedGame)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "freeze.begin requires a loaded world (no active save)");

        if (Game1.eventUp)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "freeze.begin requires !Game1.eventUp (event active)");

        if (Game1.currentMinigame != null)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "freeze.begin requires Game1.currentMinigame == null (minigame active)");

        if (Game1.isWarping)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "freeze.begin requires !Game1.isWarping (mid-warp)");

        var result = DeterminismController.EnterFreeze(s.Seed, Monitor);

        return ProtocolJson.ToElement(new FreezeBeginResult
        {
            Ok = true,
            Tick = Game1.ticks,
            LocationsPinned = result.LocationsPinned,
            NpcsHalted = result.NpcsHalted,
        });
    }
}
