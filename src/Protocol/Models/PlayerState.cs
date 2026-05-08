using System.Collections.Generic;

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
    public List<PlayerItemSummary> Items { get; set; } = new();
}

/// <summary>Minimal inventory item descriptor for a player snapshot.</summary>
public sealed class PlayerItemSummary
{
    public int Slot { get; set; }
    public string Id { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string QualifiedId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Stack { get; set; }
    public int? Category { get; set; }
    public int? Quality { get; set; }
    public string RuntimeType { get; set; } = string.Empty;
}

public sealed class TilePoint
{
    public int X { get; set; }
    public int Y { get; set; }
}
