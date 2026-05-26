namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape of <c>player.set_shop_currency</c>.</summary>
public sealed class SetShopCurrencyRequest
{
    /// <summary>Stardew shop currency code. Supported initially: 0 = gold, 1 = star tokens.</summary>
    public int Currency { get; set; }

    /// <summary>Absolute balance to set. Must be <c>&gt;= 0</c>.</summary>
    public int Amount { get; set; }
}
