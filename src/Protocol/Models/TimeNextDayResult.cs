namespace SdvTestFramework.Protocol.Models;

/// <summary>Response shape for <c>time.next_day</c>.</summary>
public sealed class TimeNextDayResult : MutatorOk
{
    public int Year { get; set; }
    public string Season { get; set; } = string.Empty;
    public int DayOfMonth { get; set; }
    public int TimeOfDay { get; set; }
}
