namespace SdvTestFramework.Protocol.Models;

/// <summary>Response for <c>time.advance</c>. Carries the new clock value so scenarios
/// don't need a round-trip to <c>state.time</c> just to confirm the advance.</summary>
public sealed class TimeAdvanceResult : MutatorOk
{
    /// <summary><c>Game1.timeOfDay</c> after the advance (e.g. 630 = 06:30am).</summary>
    public int NewTimeOfDay { get; set; }
}
