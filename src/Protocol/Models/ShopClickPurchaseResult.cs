namespace SdvTestFramework.Protocol.Models;

/// <summary>Response shape of <c>shop.click_purchase</c>.</summary>
public sealed class ShopClickPurchaseResult : MutatorOk
{
    public string ShopId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int Count { get; set; }
    public int UnitPrice { get; set; }
    public int Currency { get; set; }
    public int PreviousCurrencyBalance { get; set; }
    public int CurrencyBalance { get; set; }
    public int PreviousMoney { get; set; }
    public int Money { get; set; }
    public PixelPoint Screen { get; set; } = new();
    public MenuBounds Bounds { get; set; } = new();
    public int VisibleIndex { get; set; }
    public int ItemIndex { get; set; }
    public bool Scrolled { get; set; }
}
