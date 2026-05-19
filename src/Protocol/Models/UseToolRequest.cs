namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape for <c>world.use_tool</c>.</summary>
public sealed class UseToolRequest
{
    public string? Tool { get; set; }
    public string? Location { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public string? Facing { get; set; }
    public int Power { get; set; }
}

/// <summary>Response shape for <c>world.use_tool</c>.</summary>
public sealed class UseToolResult : MutatorOk
{
    public string Tool { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public TilePoint Tile { get; set; } = new();
    public string? SelectedItemId { get; set; }
    public string? SelectedItemQualifiedId { get; set; }
    public string? SelectedItemName { get; set; }
    public string? SelectedItemRuntimeType { get; set; }
    public int? SelectedToolIndex { get; set; }
    public bool Invoked { get; set; }
}
