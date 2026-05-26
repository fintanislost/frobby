namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape of <c>shop.click_purchase</c>.</summary>
public sealed class ShopClickPurchaseRequest
{
    /// <summary>Raw or qualified item id to click in the active shop.</summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>Exact display-name target used when <see cref="ItemId"/> is empty.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Stack count to buy. Slice 27 supports one visible click.</summary>
    public int Count { get; set; } = 1;

    /// <summary>Maximum reveal attempts before failing. Defaults to enough to cover ordinary shop lists.</summary>
    public int ScrollAttempts { get; set; } = 16;
}
