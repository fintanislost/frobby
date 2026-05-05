namespace SdvTestFramework.Protocol.Models;

/// <summary>
/// Request shape of <c>player.add_mail</c>. Adds a mail flag to the master farmer's
/// received-mail set so scenarios can exercise save-state gates exposed by mods.
/// </summary>
public sealed class AddMailRequest
{
    /// <summary>Mail flag id to add. Must be non-empty.</summary>
    public string Id { get; set; } = string.Empty;
}
