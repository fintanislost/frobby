namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape for <c>world.explode_tile</c>.</summary>
public sealed class ExplodeTileRequest
{
    public string? Location { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public int Radius { get; set; } = 2;
    public bool DamagePlayer { get; set; }
}

/// <summary>Response shape for <c>world.explode_tile</c>.</summary>
public sealed class ExplodeTileResult : MutatorOk
{
    public string Location { get; set; } = string.Empty;
    public TilePoint Tile { get; set; } = new();
    public int Radius { get; set; }
    public bool DamagePlayer { get; set; }
    public int? MonstersBefore { get; set; }
    public int? MonstersAfter { get; set; }
    public int? DebrisBefore { get; set; }
    public int? DebrisAfter { get; set; }
    public bool Invoked { get; set; }
}
