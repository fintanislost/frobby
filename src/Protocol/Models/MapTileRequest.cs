using System.Collections.Generic;

namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape for <c>state.map_tile</c>.</summary>
public sealed class MapTileRequest
{
    public string? Location { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public List<string>? Layers { get; set; }
}
