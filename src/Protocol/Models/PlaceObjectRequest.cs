namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape for <c>world.place_object</c>.</summary>
public sealed class PlaceObjectRequest
{
    /// <summary>Qualified SDV object item id, e.g. <c>"(O)340"</c>.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Optional location name. Null means current location.</summary>
    public string? Location { get; set; }

    /// <summary>Tile X coordinate.</summary>
    public int? X { get; set; }

    /// <summary>Tile Y coordinate.</summary>
    public int? Y { get; set; }

    /// <summary>Item stack size to place.</summary>
    public int Stack { get; set; } = 1;

    /// <summary>Item quality value to place.</summary>
    public int Quality { get; set; }

    /// <summary>When true, remove an existing object at the same tile before adding.</summary>
    public bool RemoveExisting { get; set; }
}
