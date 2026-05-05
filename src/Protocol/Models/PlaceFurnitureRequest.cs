namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape for <c>world.place_furniture</c>.</summary>
public sealed class PlaceFurnitureRequest
{
    /// <summary>Qualified SDV furniture item id, e.g. <c>"(F)example_terminal"</c>.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Optional location name. Null means current location.</summary>
    public string? Location { get; set; }

    /// <summary>Tile X coordinate.</summary>
    public int? X { get; set; }

    /// <summary>Tile Y coordinate.</summary>
    public int? Y { get; set; }

    /// <summary>When true, remove existing furniture with the same top-left tile before adding.</summary>
    public bool RemoveExisting { get; set; }
}
