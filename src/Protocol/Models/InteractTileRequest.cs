namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape for <c>world.interact_tile</c>.</summary>
public sealed class InteractTileRequest
{
    /// <summary>Tile X coordinate.</summary>
    public int? X { get; set; }

    /// <summary>Tile Y coordinate.</summary>
    public int? Y { get; set; }

    /// <summary>Forwards SDV's activity-probe flag to furniture/object action handlers.</summary>
    public bool JustCheckingForActivity { get; set; }
}
