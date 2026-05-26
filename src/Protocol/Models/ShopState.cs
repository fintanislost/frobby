using System.Collections.Generic;

namespace SdvTestFramework.Protocol.Models;

/// <summary>Snapshot of the active shop menu. Response shape of <c>state.shop</c>.</summary>
public sealed class ShopState
{
    public bool Present { get; set; }
    public string MenuType { get; set; } = string.Empty;
    public string ShopId { get; set; } = string.Empty;
    public int Currency { get; set; }
    public string CurrencyName { get; set; } = string.Empty;
    public int? CurrencyBalance { get; set; }
    public List<ShopItemSummary> Items { get; set; } = new();
}

/// <summary>Live shop item descriptor for a shop snapshot.</summary>
public sealed class ShopItemSummary
{
    public string ItemId { get; set; } = string.Empty;
    public string QualifiedId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int Price { get; set; }
    public int? Stock { get; set; }
    public int? Category { get; set; }
    public int? Quality { get; set; }
    public string RuntimeType { get; set; } = string.Empty;
}
