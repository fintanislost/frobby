using SdvTestFramework.Protocol;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

internal interface IShopCurrencyBalances
{
    int Money { get; set; }
    int FestivalScore { get; set; }
}

internal static class ShopCurrency
{
    public const int Gold = 0;
    public const int StarTokens = 1;

    public static string Name(int currency)
        => currency switch
        {
            Gold => "gold",
            StarTokens => "star_tokens",
            _ => $"currency_{currency}",
        };

    public static bool IsSupported(int currency)
        => currency is Gold or StarTokens;

    public static void RequireSupported(int currency, string method)
    {
        if (!IsSupported(currency))
            throw new JsonRpcException(
                JsonRpcErrorCode.GameStateInvalid,
                $"{method} does not support shop currency {currency}");
    }

    public static int GetBalance(int currency, IShopCurrencyBalances balances)
        => currency switch
        {
            Gold => balances.Money,
            StarTokens => balances.FestivalScore,
            _ => throw new JsonRpcException(
                JsonRpcErrorCode.GameStateInvalid,
                $"unsupported shop currency {currency}"),
        };

    public static void SetBalance(int currency, IShopCurrencyBalances balances, int amount)
    {
        switch (currency)
        {
            case Gold:
                balances.Money = amount;
                break;
            case StarTokens:
                balances.FestivalScore = amount;
                break;
            default:
                throw new JsonRpcException(
                    JsonRpcErrorCode.GameStateInvalid,
                    $"unsupported shop currency {currency}");
        }
    }
}

internal sealed class SdvShopCurrencyBalances : IShopCurrencyBalances
{
    public int Money
    {
        get => Game1.player.Money;
        set => Game1.player.Money = value;
    }

    public int FestivalScore
    {
        get => Game1.player.festivalScore;
        set => Game1.player.festivalScore = value;
    }
}
