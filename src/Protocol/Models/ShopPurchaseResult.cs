namespace SdvTestFramework.Protocol.Models;

/// <summary>Response shape of <c>shop.purchase</c>.</summary>
public sealed class ShopPurchaseResult : MutatorOk
{
    public string ShopId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int Count { get; set; }
    public int UnitPrice { get; set; }
    public int PreviousMoney { get; set; }
    public int Money { get; set; }
}
