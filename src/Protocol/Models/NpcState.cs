namespace SdvTestFramework.Protocol.Models;

/// <summary>Snapshot of a named NPC. Response shape of <c>state.npc</c>.</summary>
public sealed class NpcState
{
    /// <summary>NPC name, e.g. <c>Abigail</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Current location name, e.g. <c>Town</c>.</summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>Current tile position.</summary>
    public TilePoint Tile { get; set; } = new();

    /// <summary>Raw friendship points with the local farmer (250 per heart).</summary>
    public int FriendshipPoints { get; set; }

    /// <summary>Friendship hearts (<c>FriendshipPoints / 250</c>).</summary>
    public int Hearts { get; set; }

    /// <summary>True if the farmer has given this NPC a gift today.</summary>
    public bool GiftGivenToday { get; set; }

    /// <summary>Portrait asset base name (e.g. <c>Abigail</c>). Falls back to the NPC's name when the portrait texture hasn't been loaded yet.</summary>
    public string Portrait { get; set; } = string.Empty;
}
