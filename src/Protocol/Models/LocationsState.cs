using System.Collections.Generic;

namespace SdvTestFramework.Protocol.Models;

/// <summary>Snapshot of loaded Stardew locations. Response shape of <c>state.locations</c>.</summary>
public sealed class LocationsState
{
    public List<LocationSummary> Locations { get; set; } = new();
}

/// <summary>Compact descriptor for one loaded <c>GameLocation</c>.</summary>
public sealed class LocationSummary
{
    public string Name { get; set; } = string.Empty;
    public string UniqueName { get; set; } = string.Empty;
    public bool IsOutdoors { get; set; }
    public int MapWidth { get; set; }
    public int MapHeight { get; set; }
    public int WarpCount { get; set; }
}
