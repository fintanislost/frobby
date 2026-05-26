namespace SdvTestFramework.Protocol.Models;

/// <summary>Response shape for <c>player.set_shop_currency</c>.</summary>
public sealed class SetShopCurrencyResult : MutatorOk
{
    public int Currency { get; set; }
    public string CurrencyName { get; set; } = string.Empty;
    public int Previous { get; set; }
    public int Amount { get; set; }
}
