using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for the <c>state.time</c> RPC method. Runs on the game thread.</summary>
public static class StateTimeHandler
{
    public const string Method = "state.time";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var date = Game1.Date;
        // Use the widened predicate (see RpcPreconditions.RequireWorldReady in D1.7 T1) so
        // headless-Xvfb scenarios see in_save=true once gameMode transitions to playing,
        // matching what state-mutating handlers accept.
        var state = new TimeState
        {
            InSave = Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame,
            Season = date.Season.ToString().ToLowerInvariant(),
            DayOfMonth = date.DayOfMonth,
            Year = date.Year,
            TimeOfDay = Game1.timeOfDay,
            DayOfWeek = date.DayOfWeek.ToString().ToLowerInvariant(),
        };
        return ProtocolJson.ToElement(state);
    }
}
