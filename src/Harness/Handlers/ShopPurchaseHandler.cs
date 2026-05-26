using System.Linq;
using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using StardewValley.Menus;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>shop.purchase</c>. Buys an item from the active <see cref="ShopMenu"/>.</summary>
public static class ShopPurchaseHandler
{
    public const string Method = "shop.purchase";

    private static readonly IShopPurchaseWorld ProductionWorld = new SdvShopPurchaseWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, IShopPurchaseWorld world)
    {
        var req = RpcParams.Required<ShopPurchaseRequest>(paramsElement);
        if (string.IsNullOrWhiteSpace(req.ItemId))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.item_id required");
        if (req.Count < 1)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.count must be >= 1");

        if (!world.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "shop.purchase requires a loaded world");

        var shop = world.ActiveShop
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "shop.purchase requires an active ShopMenu");

        var item = shop.Items.FirstOrDefault(i => ShopStateProjector.MatchesRequestedItem(i, req.ItemId))
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"shop.purchase item not found: {req.ItemId}");

        ShopCurrency.RequireSupported(shop.Currency, Method);
        var previousCurrencyBalance = ShopCurrency.GetBalance(shop.Currency, world);
        var previousMoney = world.Money;
        if (!world.Purchase(item, req.Count))
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"shop.purchase failed for item: {req.ItemId}");

        return ProtocolJson.ToElement(new ShopPurchaseResult
        {
            Tick = world.Tick,
            ShopId = shop.ShopId,
            ItemId = item.QualifiedId,
            DisplayName = item.DisplayName,
            Count = req.Count,
            UnitPrice = item.UnitPrice,
            Currency = shop.Currency,
            PreviousCurrencyBalance = previousCurrencyBalance,
            CurrencyBalance = ShopCurrency.GetBalance(shop.Currency, world),
            PreviousMoney = previousMoney,
            Money = world.Money,
        });
    }

    internal static int CheckedTotalPrice(int unitPrice, int count)
    {
        try
        {
            return checked(unitPrice * count);
        }
        catch (System.OverflowException ex)
        {
            throw new JsonRpcException(
                JsonRpcErrorCode.InvalidParams,
                "params.count is too large for shop.purchase total price",
                ex);
        }
    }
}

internal interface IShopPurchaseWorld
    : IShopCurrencyBalances
{
    bool IsWorldReady { get; }
    int Tick { get; }
    IShopMenuState? ActiveShop { get; }
    bool Purchase(IShopItem item, int count);
}

internal sealed class SdvShopPurchaseWorld : IShopPurchaseWorld
{
    private readonly SdvShopCurrencyBalances _balances = new();

    public bool IsWorldReady => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;
    public int Tick => Game1.ticks;
    public int Money { get => _balances.Money; set => _balances.Money = value; }
    public int FestivalScore { get => _balances.FestivalScore; set => _balances.FestivalScore = value; }
    public IShopMenuState? ActiveShop => Game1.activeClickableMenu is ShopMenu shop
        ? new SdvShopMenuState(shop)
        : null;

    public bool Purchase(IShopItem item, int count)
    {
        if (item is not SdvShopItem sdvItem)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "shop.purchase can only buy items from the active SDV shop");

        var totalPrice = ShopPurchaseHandler.CheckedTotalPrice(sdvItem.UnitPrice, count);
        ShopCurrency.RequireSupported(sdvItem.Shop.currency, ShopPurchaseHandler.Method);
        var balance = ShopCurrency.GetBalance(sdvItem.Shop.currency, this);
        if (balance < totalPrice)
            return false;

        if (sdvItem.Salable.GetSalableInstance() is not Item purchased)
            return false;

        purchased.Stack = count;
        ShopCurrency.SetBalance(sdvItem.Shop.currency, this, balance - totalPrice);
        Game1.player.addItemByMenuIfNecessary(purchased);
        sdvItem.Salable.actionWhenPurchased(sdvItem.Shop.ShopId);
        return true;
    }
}
