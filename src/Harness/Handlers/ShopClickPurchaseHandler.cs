using System;
using System.Linq;
using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using StardewValley.Menus;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>shop.click_purchase</c>. Clicks a visible item row in the active <see cref="ShopMenu"/>.</summary>
public static class ShopClickPurchaseHandler
{
    public const string Method = "shop.click_purchase";

    private static readonly IShopClickPurchaseWorld ProductionWorld = new SdvShopClickPurchaseWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, IShopClickPurchaseWorld world)
    {
        var req = RpcParams.Required<ShopClickPurchaseRequest>(paramsElement);
        if (string.IsNullOrWhiteSpace(req.ItemId) && string.IsNullOrWhiteSpace(req.DisplayName))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.item_id or params.display_name required");
        if (req.Count != 1)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.count must be 1 for shop.click_purchase");
        if (req.ScrollAttempts < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.scroll_attempts must be >= 0");

        if (!world.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "shop.click_purchase requires a loaded world");

        var shop = world.ActiveShop
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "shop.click_purchase requires an active ShopMenu");

        ShopCurrency.RequireSupported(shop.Currency, Method);

        var item = FindTarget(shop, req)
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"shop.click_purchase item not found: {TargetLabel(req)}");

        var target = shop.RevealItem(item, req.ScrollAttempts)
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"shop.click_purchase could not reveal item: {TargetLabel(req)}");

        var previousCurrencyBalance = ShopCurrency.GetBalance(shop.Currency, world);
        var previousMoney = world.Money;
        shop.Click(target, world);

        return ProtocolJson.ToElement(new ShopClickPurchaseResult
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
            Screen = target.Screen,
            Bounds = target.Bounds,
            VisibleIndex = target.VisibleIndex,
            ItemIndex = target.ItemIndex,
            Scrolled = target.Scrolled,
        });
    }

    private static IShopItem? FindTarget(IShopMenuState shop, ShopClickPurchaseRequest req)
    {
        if (!string.IsNullOrWhiteSpace(req.ItemId))
            return shop.Items.FirstOrDefault(i => ShopStateProjector.MatchesRequestedItem(i, req.ItemId));

        return shop.Items.FirstOrDefault(i =>
            string.Equals(i.DisplayName, req.DisplayName, StringComparison.Ordinal));
    }

    private static string TargetLabel(ShopClickPurchaseRequest req)
        => string.IsNullOrWhiteSpace(req.ItemId) ? req.DisplayName : req.ItemId;
}

internal interface IShopClickPurchaseWorld
    : IShopCurrencyBalances
{
    bool IsWorldReady { get; }
    int Tick { get; }
    IShopClickMenuState? ActiveShop { get; }
}

internal interface IShopClickMenuState
    : IShopMenuState
{
    ShopClickTarget? RevealItem(IShopItem item, int scrollAttempts);
    void Click(ShopClickTarget target, IShopCurrencyBalances balances);
}

internal sealed class ShopClickTarget
{
    public PixelPoint Screen { get; init; } = new();
    public MenuBounds Bounds { get; init; } = new();
    public int VisibleIndex { get; init; }
    public int ItemIndex { get; init; }
    public bool Scrolled { get; init; }
}

internal sealed class SdvShopClickPurchaseWorld : IShopClickPurchaseWorld
{
    private readonly SdvShopCurrencyBalances _balances = new();

    public bool IsWorldReady => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;
    public int Tick => Game1.ticks;
    public int Money { get => _balances.Money; set => _balances.Money = value; }
    public int FestivalScore { get => _balances.FestivalScore; set => _balances.FestivalScore = value; }
    public IShopClickMenuState? ActiveShop => Game1.activeClickableMenu is ShopMenu
        ? null
        : null;
}
