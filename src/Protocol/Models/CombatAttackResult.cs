namespace SdvTestFramework.Protocol.Models;

/// <summary>Response shape of <c>combat.attack</c>.</summary>
public sealed class CombatAttackResult
{
    public bool Ok { get; set; } = true;
    public int Tick { get; set; }
    public TilePoint Tile { get; set; } = new();
    public string Direction { get; set; } = string.Empty;
    public string? SelectedItemId { get; set; }
    public string? SelectedItemQualifiedId { get; set; }
    public string? SelectedItemName { get; set; }
    public string? SelectedItemRuntimeType { get; set; }
}
