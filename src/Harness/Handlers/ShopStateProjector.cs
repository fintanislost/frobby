using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using StardewValley.Menus;

namespace SdvTestFramework.Harness.Handlers;

internal static class ShopStateProjector
{
    public static ShopState Project(IShopMenuState? shop, IShopCurrencyBalances? balances = null)
    {
        if (shop is null)
            return new ShopState();

        var currencyBalance = balances is not null && ShopCurrency.IsSupported(shop.Currency)
            ? ShopCurrency.GetBalance(shop.Currency, balances)
            : (int?)null;

        return new ShopState
        {
            Present = true,
            MenuType = shop.MenuType,
            ShopId = shop.ShopId,
            Currency = shop.Currency,
            CurrencyName = ShopCurrency.Name(shop.Currency),
            CurrencyBalance = currencyBalance,
            Items = shop.Items
                .Select(item => new ShopItemSummary
                {
                    ItemId = item.ItemId,
                    QualifiedId = item.QualifiedId,
                    DisplayName = item.DisplayName,
                    Price = item.UnitPrice,
                    Stock = item.Stock,
                    Category = item.Category,
                    Quality = item.Quality,
                    RuntimeType = item.RuntimeType,
                })
                .ToList(),
        };
    }

    public static bool MatchesRequestedItem(IShopItem item, string requestedItemId)
        => string.Equals(item.ItemId, requestedItemId, StringComparison.Ordinal)
            || string.Equals(item.QualifiedId, requestedItemId, StringComparison.Ordinal);

    internal static int? ReadStock(object stockInfo)
    {
        foreach (var name in new[] { "Stock", "Quantity", "AvailableStock" })
        {
            var property = stockInfo.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property?.GetValue(stockInfo) is int value)
                return value;
        }

        return null;
    }

    internal static string StripQualifiedPrefix(string qualifiedId)
    {
        if (qualifiedId.Length > 0 && qualifiedId[0] == '(')
        {
            var close = qualifiedId.IndexOf(')', StringComparison.Ordinal);
            if (close >= 0 && close + 1 < qualifiedId.Length)
                return qualifiedId[(close + 1)..];
        }

        return qualifiedId;
    }
}

internal interface IShopMenuState
{
    string MenuType { get; }
    string ShopId { get; }
    int Currency { get; }
    IReadOnlyList<IShopItem> Items { get; }
}

internal interface IShopItem
{
    string ItemId { get; }
    string QualifiedId { get; }
    string DisplayName { get; }
    int UnitPrice { get; }
    int? Stock { get; }
    int? Category { get; }
    int? Quality { get; }
    string RuntimeType { get; }
}

internal sealed record ShopItem(
    string ItemId,
    string QualifiedId,
    string DisplayName,
    int UnitPrice,
    int? Stock,
    int? Category,
    int? Quality,
    string RuntimeType) : IShopItem;

internal sealed class SdvShopMenuState : IShopMenuState
{
    private readonly ShopMenu _shop;

    public SdvShopMenuState(ShopMenu shop)
    {
        _shop = shop;
    }

    public string MenuType => _shop.GetType().Name;
    public string ShopId => _shop.ShopId ?? string.Empty;
    public int Currency => _shop.currency;

    public IReadOnlyList<IShopItem> Items => _shop.forSale
        .Select(item =>
        {
            var hasStockInfo = _shop.itemPriceAndStock.TryGetValue(item, out var stockInfo);
            var price = hasStockInfo && stockInfo is not null
                ? stockInfo.Price
                : item.salePrice();
            var stockCount = hasStockInfo && stockInfo is not null
                ? ShopStateProjector.ReadStock(stockInfo)
                : null;
            return new SdvShopItem(_shop, item, price, stockCount);
        })
        .ToList();
}

internal sealed class SdvShopItem : IShopItem
{
    private bool _instanceResolved;
    private Item? _instance;

    public SdvShopItem(ShopMenu shop, ISalable salable, int unitPrice, int? stock)
    {
        Shop = shop;
        Salable = salable;
        UnitPrice = unitPrice;
        Stock = stock;
    }

    public ShopMenu Shop { get; }
    public ISalable Salable { get; }
    public int UnitPrice { get; }
    public int? Stock { get; }
    public string QualifiedId => Instance?.QualifiedItemId ?? Salable.QualifiedItemId ?? string.Empty;
    public string ItemId => Instance?.ItemId ?? ShopStateProjector.StripQualifiedPrefix(QualifiedId);
    public string DisplayName => Instance?.DisplayName ?? Salable.DisplayName ?? string.Empty;
    public int? Category => Instance?.Category;
    public int? Quality => Instance?.Quality;
    public string RuntimeType => Instance?.GetType().Name ?? Salable.GetType().Name;

    private Item? Instance
    {
        get
        {
            if (_instanceResolved)
                return _instance;

            _instanceResolved = true;
            try
            {
                _instance = Salable.GetSalableInstance() as Item;
            }
            catch
            {
                _instance = null;
            }

            return _instance;
        }
    }
}
