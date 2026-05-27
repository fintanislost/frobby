namespace SdvTestFramework.Protocol.Models;

/// <summary>
/// Request shape for <c>world.refresh_npc_schedule</c>. Re-applies the named NPC's
/// current schedule against the active save date/time.
/// </summary>
public sealed class RefreshNpcScheduleRequest
{
    public string? Name { get; set; }
    public string? ScheduleKey { get; set; }
}

/// <summary>Result shape for <c>world.refresh_npc_schedule</c>.</summary>
public sealed class RefreshNpcScheduleResult : MutatorOk
{
    public string? Location { get; set; }
    public TilePoint? Tile { get; set; }
}
