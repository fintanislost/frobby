namespace SdvTestFramework.Protocol.Models;

/// <summary>Snapshot of the local farmer. Response shape of <c>state.player</c>.</summary>
public sealed class PlayerState
{
    public string Name { get; set; } = string.Empty;
    public int Money { get; set; }
    public int Stamina { get; set; }
    public int MaxStamina { get; set; }
    public int Health { get; set; }
    public string Location { get; set; } = string.Empty;
    public TilePoint Tile { get; set; } = new();
}

public sealed class TilePoint
{
    public int X { get; set; }
    public int Y { get; set; }
}
