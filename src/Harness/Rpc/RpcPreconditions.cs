using SdvTestFramework.Protocol;
using StardewModdingAPI;
using StardewValley;

namespace SdvTestFramework.Harness.Rpc;

/// <summary>
/// Preconditions that handlers can invoke to short-circuit with a typed
/// <see cref="JsonRpcErrorCode"/> rather than NRE-ing into <c>InternalError</c>.
/// </summary>
public static class RpcPreconditions
{
    /// <summary>
    /// Throws <see cref="JsonRpcErrorCode.GameStateInvalid"/> unless the world is loaded and
    /// interactable. Historically gated on <c>Context.IsWorldReady</c>; that predicate stays
    /// <c>false</c> under headless Xvfb even after <c>Game1.gameMode</c> transitions to
    /// <c>playingGameMode</c>, blocking every mutator in scripted scenarios. D1.7 widens the
    /// gate to <c>(gameMode == playingGameMode &amp;&amp; hasLoadedGame)</c>, which is what
    /// mutators actually need — the save has finished loading and the game is in its normal
    /// gameplay state.
    /// </summary>
    public static void RequireWorldReady()
    {
        if (Game1.gameMode != Game1.playingGameMode || !Game1.hasLoadedGame)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "no active save — mutation requires a loaded world");
    }
}
