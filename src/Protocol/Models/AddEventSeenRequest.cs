namespace SdvTestFramework.Protocol.Models;

/// <summary>
/// Request shape of <c>player.add_event_seen</c>. Adds a numeric event id to the
/// farmer's seen-event set so scenarios can exercise event-gated mod content.
/// </summary>
public sealed class AddEventSeenRequest
{
    /// <summary>Numeric event id to add. Accepted as a string for consistency with event.start ids.</summary>
    public string Id { get; set; } = string.Empty;
}
