namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape of <c>shop.purchase</c>.</summary>
public sealed class ShopPurchaseRequest
{
    /// <summary>Qualified item ID to purchase from the active shop.</summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>Stack count to buy. Defaults to one.</summary>
    public int Count { get; set; } = 1;
}
