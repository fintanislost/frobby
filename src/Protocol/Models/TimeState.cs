namespace SdvTestFramework.Protocol.Models;

/// <summary>Snapshot of game time. Response shape of <c>state.time</c>.</summary>
public sealed class TimeState
{
    /// <summary>
    /// <c>true</c> when a save is loaded and the clock fields reflect real world state.
    /// <c>false</c> at the title screen — in that case <see cref="DayOfMonth"/> will be
    /// <c>0</c> (SDV's pre-save default) and callers should disregard the date/clock
    /// values as uninitialized.
    /// </summary>
    public bool InSave { get; set; }

    /// <summary>Lowercase season name (<c>spring</c>, <c>summer</c>, <c>fall</c>, <c>winter</c>).</summary>
    public string Season { get; set; } = string.Empty;

    /// <summary>Day of month (1-28 when in save; 0 at title screen).</summary>
    public int DayOfMonth { get; set; }

    /// <summary>In-game year, starting at 1.</summary>
    public int Year { get; set; }

    /// <summary>Clock time as an SDV-native integer — e.g. 600 is 06:00am, 1530 is 3:30pm.</summary>
    public int TimeOfDay { get; set; }

    /// <summary>Lowercase day-of-week name.</summary>
    public string DayOfWeek { get; set; } = string.Empty;
}
