using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using SdvTestFramework.Harness.Determinism;
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
        var currencyBalance = ShopCurrency.GetBalance(shop.Currency, world);
        if (item.UnitPrice > 0 && currencyBalance >= previousCurrencyBalance)
        {
            throw new JsonRpcException(
                JsonRpcErrorCode.GameStateInvalid,
                "shop.click_purchase click did not change currency balance; the menu may not have accepted the click");
        }

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
            CurrencyBalance = currencyBalance,
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
    public IShopClickMenuState? ActiveShop => Game1.activeClickableMenu is ShopMenu shop
        ? new SdvShopClickMenuState(shop)
        : null;
}

internal sealed class SdvShopClickMenuState : IShopClickMenuState
{
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly ShopMenu _shop;

    public SdvShopClickMenuState(ShopMenu shop)
    {
        _shop = shop;
    }

    public string MenuType => _shop.GetType().Name;
    public string ShopId => _shop.ShopId ?? string.Empty;
    public int Currency => _shop.currency;
    public IReadOnlyList<IShopItem> Items => new SdvShopMenuState(_shop).Items;

    public ShopClickTarget? RevealItem(IShopItem item, int scrollAttempts)
    {
        var itemIndex = FindItemIndex(item);
        if (itemIndex < 0)
            return null;

        var buttons = ReadForSaleButtons();
        if (buttons.Count == 0)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "shop.click_purchase could not read visible shop row buttons");

        var currentIndex = ReadCurrentItemIndex();
        var visibleIndex = itemIndex - currentIndex;
        var scrolled = false;
        if (visibleIndex < 0 || visibleIndex >= buttons.Count)
        {
            if (scrollAttempts == 0)
                return null;

            var maxStart = Math.Max(0, _shop.forSale.Count - buttons.Count);
            var nextIndex = Math.Clamp(itemIndex, 0, maxStart);
            WriteCurrentItemIndex(nextIndex);
            currentIndex = nextIndex;
            visibleIndex = itemIndex - currentIndex;
            scrolled = true;
        }

        if (visibleIndex < 0 || visibleIndex >= buttons.Count)
            return null;
        if (buttons[visibleIndex] is not ClickableComponent button)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "shop.click_purchase could not read visible shop row bounds");

        var bounds = button.bounds;
        return new ShopClickTarget
        {
            Screen = new PixelPoint
            {
                X = bounds.X + bounds.Width / 2,
                Y = bounds.Y + bounds.Height / 2,
            },
            Bounds = new MenuBounds
            {
                X = bounds.X,
                Y = bounds.Y,
                Width = bounds.Width,
                Height = bounds.Height,
            },
            VisibleIndex = visibleIndex,
            ItemIndex = itemIndex,
            Scrolled = scrolled,
        };
    }

    public void Click(ShopClickTarget target, IShopCurrencyBalances balances)
    {
        ControlledCursor.Set(target.Screen.X, target.Screen.Y);
        _shop.performHoverAction(target.Screen.X, target.Screen.Y);
        _shop.receiveLeftClick(target.Screen.X, target.Screen.Y);
    }

    private int FindItemIndex(IShopItem target)
    {
        if (target is SdvShopItem sdvItem && ReferenceEquals(sdvItem.Shop, _shop))
            return _shop.forSale.IndexOf(sdvItem.Salable);

        for (var i = 0; i < _shop.forSale.Count; i++)
        {
            var item = new SdvShopItem(
                _shop,
                _shop.forSale[i],
                ReadUnitPrice(_shop.forSale[i]),
                stock: null);
            if (ShopStateProjector.MatchesRequestedItem(item, target.QualifiedId))
                return i;
        }

        return -1;
    }

    private int ReadUnitPrice(ISalable salable)
    {
        if (_shop.itemPriceAndStock.TryGetValue(salable, out var stockInfo) && stockInfo is not null)
            return stockInfo.Price;
        return salable.salePrice();
    }

    private IList ReadForSaleButtons()
    {
        var field = FindField(_shop.GetType(), "forSaleButtons")
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "shop.click_purchase could not find ShopMenu.forSaleButtons");
        if (field.GetValue(_shop) is IList buttons)
            return buttons;
        throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
            "shop.click_purchase could not read ShopMenu.forSaleButtons");
    }

    private int ReadCurrentItemIndex()
    {
        var field = CurrentItemIndexField();
        if (field.GetValue(_shop) is int value)
            return value;
        throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
            "shop.click_purchase could not read ShopMenu.currentItemIndex");
    }

    private void WriteCurrentItemIndex(int value)
    {
        CurrentItemIndexField().SetValue(_shop, value);
    }

    private FieldInfo CurrentItemIndexField()
        => FindField(_shop.GetType(), "currentItemIndex")
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "shop.click_purchase could not find ShopMenu.currentItemIndex");

    private static FieldInfo? FindField(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(name, InstanceFlags);
            if (field is not null)
                return field;
        }

        return null;
    }
}
