namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape for <c>player.set_friendship</c>.</summary>
public sealed class SetFriendshipRequest
{
    public string Npc { get; set; } = string.Empty;
    public int? Points { get; set; }
    public bool? TalkedToToday { get; set; }
    public int? GiftsToday { get; set; }
    public int? GiftsThisWeek { get; set; }
}
