namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape of <c>world.interact_npc</c>.</summary>
public sealed class WorldInteractNpcRequest
{
    /// <summary>NPC name, e.g. <c>"Pierre"</c>, <c>"Abigail"</c>. Must match an NPC in the player's current location.</summary>
    public string Name { get; set; } = string.Empty;
}
