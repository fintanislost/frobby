using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>player.set_shop_currency</c>. Sets a supported shop currency balance.</summary>
public static class PlayerSetShopCurrencyHandler
{
    public const string Method = "player.set_shop_currency";

    private static readonly IPlayerSetShopCurrencyWorld ProductionWorld = new SdvPlayerSetShopCurrencyWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, IPlayerSetShopCurrencyWorld world)
    {
        var req = RpcParams.Required<SetShopCurrencyRequest>(paramsElement);
        if (req.Amount < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.amount must be >= 0");

        if (!world.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "player.set_shop_currency requires a loaded world");

        ShopCurrency.RequireSupported(req.Currency, Method);
        var previous = ShopCurrency.GetBalance(req.Currency, world);
        ShopCurrency.SetBalance(req.Currency, world, req.Amount);
        var current = ShopCurrency.GetBalance(req.Currency, world);

        return ProtocolJson.ToElement(new SetShopCurrencyResult
        {
            Tick = world.Tick,
            Currency = req.Currency,
            CurrencyName = ShopCurrency.Name(req.Currency),
            Previous = previous,
            Amount = current,
        });
    }
}

internal interface IPlayerSetShopCurrencyWorld : IShopCurrencyBalances
{
    bool IsWorldReady { get; }
    int Tick { get; }
}

internal sealed class SdvPlayerSetShopCurrencyWorld : IPlayerSetShopCurrencyWorld
{
    private readonly SdvShopCurrencyBalances _balances = new();

    public bool IsWorldReady => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;
    public int Tick => Game1.ticks;
    public int Money { get => _balances.Money; set => _balances.Money = value; }
    public int FestivalScore { get => _balances.FestivalScore; set => _balances.FestivalScore = value; }
}
