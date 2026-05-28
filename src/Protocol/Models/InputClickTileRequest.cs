using System.Collections.Generic;

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

    /// <summary>Mouse button to send. Supported values are <c>left</c> and <c>right</c>.</summary>
    public string Button { get; set; } = "left";

    /// <summary>Reject when <see cref="Location"/> is supplied and the current location differs.</summary>
    public bool RequireCurrentLocation { get; set; } = true;

    /// <summary>Allow gameplay click delivery during active events or festivals. Defaults to false.</summary>
    public bool AllowEventInput { get; set; }

    /// <summary>
    /// Optional exact map action value to discover near <see cref="X"/> and <see cref="Y"/>
    /// before clicking. This keeps scenarios away from brittle coordinates when a
    /// stable Stardew map action is available.
    /// </summary>
    public string? ActionValue { get; set; }

    /// <summary>Search radius used with <see cref="ActionValue"/>. Defaults to the supplied tile only.</summary>
    public int Radius { get; set; }

    /// <summary>Optional map layers to scan when <see cref="ActionValue"/> is set.</summary>
    public List<string>? Layers { get; set; }

    /// <summary>Optional tile properties to scan when <see cref="ActionValue"/> is set.</summary>
    public List<string>? Properties { get; set; }

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
    public string? TargetNpcName { get; set; }
    public bool NpcFallbackUsed { get; set; }
    public string? ResolvedActionValue { get; set; }
    public string? ResolvedActionLayer { get; set; }
    public string? ResolvedActionProperty { get; set; }
    public TilePoint? ResolvedActionTile { get; set; }
    public bool ScreenVisible { get; set; }
}
