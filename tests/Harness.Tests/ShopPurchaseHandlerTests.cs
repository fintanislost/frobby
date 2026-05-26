using System;
using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class ShopPurchaseHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => ShopPurchaseHandler.Handle(null, new FakeShopPurchaseWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_MissingItemId_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => ShopPurchaseHandler.Handle(p, new FakeShopPurchaseWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("item_id", ex.Message);
    }

    [Fact]
    public void Handle_InvalidCount_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"item_id\":\"(F)x\",\"count\":0}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => ShopPurchaseHandler.Handle(p, new FakeShopPurchaseWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("count", ex.Message);
    }

    [Fact]
    public void Handle_NoActiveShop_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"item_id\":\"(F)x\"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            ShopPurchaseHandler.Handle(p, new FakeShopPurchaseWorld { ActiveShop = null }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_MissingItem_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"item_id\":\"(F)missing\"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            ShopPurchaseHandler.Handle(p, new FakeShopPurchaseWorld()));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("(F)missing", ex.Message);
    }

    [Fact]
    public void Handle_PurchaseFailure_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"item_id\":\"(F)terminal\"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            ShopPurchaseHandler.Handle(p, new FakeShopPurchaseWorld { PurchaseSucceeds = false }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_PurchasePriceOverflow_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"item_id\":\"(F)terminal\",\"count\":2147483647}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => ShopPurchaseHandler.Handle(p, new FakeShopPurchaseWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("count", ex.Message);
    }

    [Fact]
    public void Handle_PurchaseInternalOverflow_DoesNotReportInvalidParams()
    {
        var p = JsonDocument.Parse("{\"item_id\":\"(F)terminal\",\"count\":1}").RootElement;

        Assert.Throws<OverflowException>(() =>
            ShopPurchaseHandler.Handle(p, new FakeShopPurchaseWorld { PurchaseThrowsOverflow = true }));
    }

    [Fact]
    public void Handle_PurchasesMatchingItemAndReturnsMoneyDelta()
    {
        var world = new FakeShopPurchaseWorld();
        var p = JsonDocument.Parse("{\"item_id\":\"(F)terminal\",\"count\":1}").RootElement;

        var result = ShopPurchaseHandler.Handle(p, world);
        var purchase = JsonSerializer.Deserialize<ShopPurchaseResult>(result, ProtocolJson.Options)!;

        Assert.True(purchase.Ok);
        Assert.Equal(1234, purchase.Tick);
        Assert.Equal("Carpenter", purchase.ShopId);
        Assert.Equal("(F)terminal", purchase.ItemId);
        Assert.Equal("Terminal", purchase.DisplayName);
        Assert.Equal(1, purchase.Count);
        Assert.Equal(25000, purchase.UnitPrice);
        Assert.Equal(0, purchase.Currency);
        Assert.Equal(30000, purchase.PreviousCurrencyBalance);
        Assert.Equal(5000, purchase.CurrencyBalance);
        Assert.Equal(30000, purchase.PreviousMoney);
        Assert.Equal(5000, purchase.Money);
        Assert.Equal("(F)terminal", world.PurchasedItemId);
        Assert.Equal("(F)terminal", world.PurchasedQualifiedId);
        Assert.Equal(1, world.PurchasedCount);
    }

    [Fact]
    public void Handle_PurchasesMatchingRawItemId()
    {
        var world = new FakeShopPurchaseWorld();
        var p = JsonDocument.Parse("{\"item_id\":\"terminal\",\"count\":1}").RootElement;

        var result = ShopPurchaseHandler.Handle(p, world);
        var purchase = JsonSerializer.Deserialize<ShopPurchaseResult>(result, ProtocolJson.Options)!;

        Assert.True(purchase.Ok);
        Assert.Equal("(F)terminal", purchase.ItemId);
        Assert.Equal("terminal", world.PurchasedRawItemId);
        Assert.Equal("(F)terminal", world.PurchasedQualifiedId);
        Assert.Equal(1, world.PurchasedCount);
    }

    [Fact]
    public void Handle_StarTokenShop_DebitsFestivalScoreAndPreservesMoney()
    {
        var world = new FakeShopPurchaseWorld
        {
            ActiveShop = new FakeShop(currency: 1),
            FestivalScore = 30000,
        };
        var p = JsonDocument.Parse("{\"item_id\":\"(F)terminal\",\"count\":1}").RootElement;

        var result = ShopPurchaseHandler.Handle(p, world);
        var purchase = JsonSerializer.Deserialize<ShopPurchaseResult>(result, ProtocolJson.Options)!;

        Assert.True(purchase.Ok);
        Assert.Equal(1, purchase.Currency);
        Assert.Equal(30000, purchase.PreviousCurrencyBalance);
        Assert.Equal(5000, purchase.CurrencyBalance);
        Assert.Equal(30000, purchase.PreviousMoney);
        Assert.Equal(30000, purchase.Money);
        Assert.Equal(5000, world.FestivalScore);
    }

    [Fact]
    public void Handle_UnsupportedCurrency_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"item_id\":\"(F)terminal\",\"count\":1}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            ShopPurchaseHandler.Handle(p, new FakeShopPurchaseWorld
            {
                ActiveShop = new FakeShop(currency: 99),
            }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("99", ex.Message);
    }

    private sealed class FakeShopPurchaseWorld : IShopPurchaseWorld
    {
        public bool IsWorldReady { get; init; } = true;
        public int Tick => 1234;
        public int Money { get; set; } = 30000;
        public int FestivalScore { get; set; }
        public bool PurchaseSucceeds { get; init; } = true;
        public bool PurchaseThrowsOverflow { get; init; }
        public string? PurchasedItemId { get; private set; }
        public string? PurchasedRawItemId { get; private set; }
        public string? PurchasedQualifiedId { get; private set; }
        public int PurchasedCount { get; private set; }
        public IShopMenuState? ActiveShop { get; init; } = new FakeShop();

        public bool Purchase(IShopItem item, int count)
        {
            PurchasedItemId = item.QualifiedId;
            PurchasedRawItemId = item.ItemId;
            PurchasedQualifiedId = item.QualifiedId;
            PurchasedCount = count;
            if (!PurchaseSucceeds)
                return false;
            if (PurchaseThrowsOverflow)
                throw new OverflowException("simulated price overflow");

            var total = ShopPurchaseHandler.CheckedTotalPrice(item.UnitPrice, count);
            var balance = ShopCurrency.GetBalance(ActiveShop!.Currency, this);
            if (balance < total)
                return false;

            ShopCurrency.SetBalance(ActiveShop.Currency, this, balance - total);
            return true;
        }
    }

    private sealed class FakeShop : IShopMenuState
    {
        private readonly int _currency;

        public FakeShop(int currency = 0)
        {
            _currency = currency;
        }

        public string MenuType => "ShopMenu";
        public string ShopId => "Carpenter";
        public int Currency => _currency;
        public IReadOnlyList<IShopItem> Items { get; } = new[]
        {
            new ShopItem("terminal", "(F)terminal", "Terminal", 25000, 1, -9, 0, "Furniture"),
            new ShopItem("388", "(O)388", "Wood", 10, null, -16, 0, "Object"),
        };
    }
}
