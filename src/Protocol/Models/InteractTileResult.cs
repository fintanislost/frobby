namespace SdvTestFramework.Protocol.Models;

/// <summary>Response shape for <c>world.interact_tile</c>.</summary>
public sealed class InteractTileResult : MutatorOk
{
    public bool Handled { get; set; }
    public string TargetType { get; set; } = string.Empty;
    public TilePoint Tile { get; set; } = new();
}
