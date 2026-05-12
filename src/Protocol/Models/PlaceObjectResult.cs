namespace SdvTestFramework.Protocol.Models;

/// <summary>Response shape for <c>world.place_object</c>.</summary>
public sealed class PlaceObjectResult : MutatorOk
{
    public string Id { get; set; } = string.Empty;
    public string QualifiedId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public TilePoint Tile { get; set; } = new();
    public bool BigCraftable { get; set; }
    public string RuntimeType { get; set; } = string.Empty;
}
