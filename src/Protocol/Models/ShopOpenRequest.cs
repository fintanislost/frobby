namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape of <c>shop.open</c>.</summary>
public sealed class ShopOpenRequest
{
    /// <summary>SDV shop data ID, e.g. <c>Carpenter</c>.</summary>
    public string ShopId { get; set; } = string.Empty;

    /// <summary>Optional owner NPC name, e.g. <c>Robin</c>.</summary>
    public string? OwnerName { get; set; }

    /// <summary>When true, bypasses schedule/open-hours checks and opens the data-backed shop directly.</summary>
    public bool ForceOpen { get; set; } = true;
}
