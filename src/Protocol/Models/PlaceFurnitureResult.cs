namespace SdvTestFramework.Protocol.Models;

/// <summary>Response shape for <c>world.place_furniture</c>.</summary>
public sealed class PlaceFurnitureResult : MutatorOk
{
    public string Id { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public TilePoint Tile { get; set; } = new();
}
