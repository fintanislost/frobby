namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape of <c>player.warp</c>. Field names deserialize from snake_case via <see cref="Json.ProtocolJson.Options"/>.</summary>
public sealed class WarpRequest
{
    /// <summary>Target location name, e.g. <c>SeedShop</c>. Resolved via <c>Game1.getLocationFromName</c>.</summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>Destination tile X coordinate.</summary>
    public int X { get; set; }

    /// <summary>Destination tile Y coordinate.</summary>
    public int Y { get; set; }
}
