namespace SdvTestFramework.Protocol.Models;

/// <summary>
/// Request shape of <c>time.set</c>. All fields optional — at least one must be provided.
/// <c>Time</c> is in HHMM format (e.g. 1530 = 3:30pm). <c>Season</c> is one of
/// "spring", "summer", "fall", "winter".
/// </summary>
public sealed class TimeSetRequest
{
    /// <summary>Time of day in HHMM format (600-2600). Optional.</summary>
    public int? Time { get; set; }

    /// <summary>Day of month, 1-28. Optional.</summary>
    public int? Day { get; set; }

    /// <summary>Season name (spring|summer|fall|winter). Optional. Case-insensitive.</summary>
    public string? Season { get; set; }

    /// <summary>Year (>= 1). Optional.</summary>
    public int? Year { get; set; }
}
