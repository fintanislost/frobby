namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape of <c>combat_lab.reset</c>.</summary>
public sealed class CombatLabResetRequest
{
    public int PlayerX { get; set; } = 8;
    public int PlayerY { get; set; } = 8;
    public int Width { get; set; } = 20;
    public int Height { get; set; } = 14;
    public bool WarpPlayer { get; set; } = true;
}

/// <summary>Response shape of <c>combat_lab.reset</c>.</summary>
public sealed class CombatLabResetResult
{
    public bool Ok { get; set; } = true;
    public string Location { get; set; } = string.Empty;
    public TilePoint PlayerTile { get; set; } = new();
    public int MapWidth { get; set; }
    public int MapHeight { get; set; }
    public int ClearedMonsters { get; set; }
    public int ClearedDebris { get; set; }
}

/// <summary>Request shape of <c>combat_lab.spawn_monster</c>.</summary>
public sealed class CombatLabSpawnMonsterRequest
{
    public string Kind { get; set; } = string.Empty;
    public string? Label { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int? Health { get; set; }
}

/// <summary>Response shape of <c>combat_lab.spawn_monster</c>.</summary>
public sealed class CombatLabSpawnMonsterResult
{
    public bool Ok { get; set; } = true;
    public string MonsterId { get; set; } = string.Empty;
    public string? Label { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public TilePoint Tile { get; set; } = new();
    public int? Health { get; set; }
    public int? MaxHealth { get; set; }
}
