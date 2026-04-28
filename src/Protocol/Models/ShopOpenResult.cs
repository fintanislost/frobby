namespace SdvTestFramework.Protocol.Models;

/// <summary>Response shape of <c>shop.open</c>.</summary>
public sealed class ShopOpenResult : MutatorOk
{
    public string ShopId { get; set; } = string.Empty;
    public string MenuType { get; set; } = string.Empty;
}
