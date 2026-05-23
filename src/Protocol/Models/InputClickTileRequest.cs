namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape for <c>input.click_tile</c>.</summary>
public sealed class InputClickTileRequest
{
    /// <summary>Optional current-location guard. Null means use the current location.</summary>
    public string? Location { get; set; }

    /// <summary>Tile X coordinate.</summary>
    public int? X { get; set; }

    /// <summary>Tile Y coordinate.</summary>
    public int? Y { get; set; }

    /// <summary>Mouse button to send. Slice 23 only supports <c>left</c>.</summary>
    public string Button { get; set; } = "left";

    /// <summary>Reject when <see cref="Location"/> is supplied and the current location differs.</summary>
    public bool RequireCurrentLocation { get; set; } = true;

    /// <summary>Pixel offset within the tile. Defaults to the tile center.</summary>
    public int ScreenOffsetX { get; set; } = 32;

    /// <summary>Pixel offset within the tile. Defaults to the tile center.</summary>
    public int ScreenOffsetY { get; set; } = 32;
}

/// <summary>Response shape for <c>input.click_tile</c>.</summary>
public sealed class InputClickTileResult : MutatorOk
{
    public string Location { get; set; } = string.Empty;
    public TilePoint Tile { get; set; } = new();
    public PixelPoint Screen { get; set; } = new();
    public PixelPoint World { get; set; } = new();
    public PlayerItemSummary? SelectedItem { get; set; }
    public bool Handled { get; set; }
}
