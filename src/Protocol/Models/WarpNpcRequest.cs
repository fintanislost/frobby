namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape for <c>world.warp_npc</c>.</summary>
public sealed class WarpNpcRequest
{
    /// <summary>NPC name, e.g. <c>"Sophia"</c> or <c>"Abigail"</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Target location name, e.g. <c>Town</c> or a custom location.</summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>Destination tile X coordinate.</summary>
    public int? X { get; set; }

    /// <summary>Destination tile Y coordinate.</summary>
    public int? Y { get; set; }
}
