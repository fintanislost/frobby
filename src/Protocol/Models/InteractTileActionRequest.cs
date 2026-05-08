using System.Collections.Generic;

namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape for <c>world.interact_tile_action</c>.</summary>
public sealed class InteractTileActionRequest
{
    /// <summary>Optional current-location guard. If provided, it must match the active location.</summary>
    public string? Location { get; set; }

    /// <summary>Tile X coordinate. Defaults to the farmer's current tile when omitted.</summary>
    public int? X { get; set; }

    /// <summary>Tile Y coordinate. Defaults to the farmer's current tile when omitted.</summary>
    public int? Y { get; set; }

    /// <summary>Optional action property to execute: <c>Action</c> or <c>TouchAction</c>.</summary>
    public string? Property { get; set; }

    /// <summary>Optional layer search order for finding the tile action property.</summary>
    public List<string>? Layers { get; set; }

    /// <summary>Forwarded to Stardew's <c>Action</c> execution path.</summary>
    public bool JustCheckingForActivity { get; set; }
}
