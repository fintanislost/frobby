namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape for <c>world.place_inventory_object</c>.</summary>
public sealed class PlaceInventoryObjectRequest
{
    /// <summary>Inventory object id to place. Qualified ids such as <c>(O)287</c> are preferred.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Optional current-location guard. Null means no guard.</summary>
    public string? Location { get; set; }

    /// <summary>Tile X coordinate.</summary>
    public int? X { get; set; }

    /// <summary>Tile Y coordinate.</summary>
    public int? Y { get; set; }

    /// <summary>Optional inventory slot override for ambiguous item ids.</summary>
    public int? Slot { get; set; }

    /// <summary>Optional player facing direction before placement.</summary>
    public string? Facing { get; set; }
}

/// <summary>Response shape for <c>world.place_inventory_object</c>.</summary>
public sealed class PlaceInventoryObjectResult : MutatorOk
{
    public string Id { get; set; } = string.Empty;
    public string QualifiedId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public TilePoint Tile { get; set; } = new();
    public int SourceSlot { get; set; }
    public int? StackBefore { get; set; }
    public int? StackAfter { get; set; }
    public string RuntimeType { get; set; } = string.Empty;
    public bool Placed { get; set; }
}
