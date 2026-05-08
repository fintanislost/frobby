using System.Collections.Generic;

namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape for <c>state.tile_actions</c>.</summary>
public sealed class TileActionsRequest
{
    public string? Location { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public int Radius { get; set; } = 0;
    public List<string>? Layers { get; set; }
    public List<string>? Properties { get; set; }
}

/// <summary>Response shape for <c>state.tile_actions</c>.</summary>
public sealed class TileActionsState
{
    public string Location { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Radius { get; set; }
    public List<TileActionCandidate> Actions { get; set; } = new();
}

/// <summary>One map tile action candidate discovered on a tile/layer.</summary>
public sealed class TileActionCandidate
{
    public TilePoint Tile { get; set; } = new();
    public string Layer { get; set; } = string.Empty;
    public string Property { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public int Distance { get; set; }
}
