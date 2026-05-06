namespace SdvTestFramework.Protocol.Models;

/// <summary>Snapshot of a named NPC. Response shape of <c>state.npc</c>.</summary>
public sealed class NpcState
{
    /// <summary>NPC name, e.g. <c>Abigail</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Localized/display name when it differs from the internal NPC name.</summary>
    public string? DisplayName { get; set; }

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

    /// <summary>True if the farmer has talked to this NPC today.</summary>
    public bool? TalkedToToday { get; set; }

    /// <summary>Portrait asset base name (e.g. <c>Abigail</c>). Falls back to the NPC's name when the portrait texture hasn't been loaded yet.</summary>
    public string Portrait { get; set; } = string.Empty;

    /// <summary>Current schedule key, when available.</summary>
    public string? CurrentScheduleKey { get; set; }

    /// <summary>Current schedule time, when available.</summary>
    public int? CurrentScheduleTime { get; set; }

    /// <summary>Current schedule location, when available.</summary>
    public string? CurrentScheduleLocation { get; set; }

    /// <summary>Current schedule tile, when available.</summary>
    public TilePoint? CurrentScheduleTile { get; set; }

    /// <summary>Current schedule facing direction, when available.</summary>
    public int? CurrentScheduleDirection { get; set; }

    /// <summary>Current schedule animation, when available.</summary>
    public string? CurrentScheduleAnimation { get; set; }

    /// <summary>True when the NPC is a villager.</summary>
    public bool? IsVillager { get; set; }

    /// <summary>True when the NPC can participate in social interactions.</summary>
    public bool? CanSocialize { get; set; }
}
