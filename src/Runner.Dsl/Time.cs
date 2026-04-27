using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>Ambient static DSL for the <c>time.*</c> RPC surface.</summary>
public static class Time
{
    /// <summary>Advance in-game time by <paramref name="minutes"/>.</summary>
    public static async Task Advance(int minutes, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new TimeAdvanceRequest { Minutes = minutes }, ProtocolJson.Options);
        await s.InvokeAsync("time.advance", p, ct);
    }

    /// <summary>
    /// Advance through Frobby's deterministic testing day transition and return the new
    /// in-game date. This does not run SDV's sleep, save, or end-of-night UI.
    /// </summary>
    public static async Task<TimeNextDayResult> NextDay(CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var json = await s.InvokeAsync("time.next_day", null, ct);
        return json.Deserialize<TimeNextDayResult>(ProtocolJson.Options)!;
    }

    /// <summary>
    /// Set the in-game clock and/or date directly. All parameters optional — at least one
    /// must be provided. <paramref name="time"/> is HHMM format (e.g. 1530 = 3:30pm).
    /// <paramref name="season"/> is one of "spring", "summer", "fall", "winter" (case-insensitive).
    /// </summary>
    public static async Task Set(int? time = null, int? day = null, string? season = null, int? year = null, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new TimeSetRequest
        {
            Time = time,
            Day = day,
            Season = season,
            Year = year,
        }, ProtocolJson.Options);
        await s.InvokeAsync("time.set", p, ct);
    }
}
