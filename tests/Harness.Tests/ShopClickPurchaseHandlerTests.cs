using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class ShopClickPurchaseHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => ShopClickPurchaseHandler.Handle(null, new FakeWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_MissingItemIdAndDisplayName_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => ShopClickPurchaseHandler.Handle(p, new FakeWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("item_id", ex.Message);
    }

    [Fact]
    public void Handle_InvalidCount_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"item_id\":\"(F)terminal\",\"count\":2}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => ShopClickPurchaseHandler.Handle(p, new FakeWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("count", ex.Message);
    }

    [Fact]
    public void Handle_NegativeScrollAttempts_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"item_id\":\"(F)terminal\",\"scroll_attempts\":-1}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => ShopClickPurchaseHandler.Handle(p, new FakeWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("scroll_attempts", ex.Message);
    }

    [Fact]
    public void Handle_NoActiveShop_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"item_id\":\"(F)terminal\"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            ShopClickPurchaseHandler.Handle(p, new FakeWorld { Shop = null }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_MissingItem_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"item_id\":\"(F)missing\"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => ShopClickPurchaseHandler.Handle(p, new FakeWorld()));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("(F)missing", ex.Message);
    }

    [Fact]
    public void Handle_UnsupportedCurrency_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"item_id\":\"(F)terminal\"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            ShopClickPurchaseHandler.Handle(p, new FakeWorld
            {
                Shop = new FakeShop(currency: 99),
            }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("99", ex.Message);
    }

    [Fact]
    public void Handle_TargetNotVisibleAfterReveal_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"item_id\":\"(F)terminal\",\"scroll_attempts\":1}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            ShopClickPurchaseHandler.Handle(p, new FakeWorld
            {
                Shop = new FakeShop { RevealSucceeds = false },
            }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("could not reveal", ex.Message);
    }

    [Fact]
    public void Handle_PaidClickWithoutCurrencyDelta_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"item_id\":\"(F)terminal\"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            ShopClickPurchaseHandler.Handle(p, new FakeWorld
            {
                Shop = new FakeShop { DebitOnClick = false },
            }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("did not change currency balance", ex.Message);
    }

    [Fact]
    public void Handle_ClicksItemIndexTarget()
    {
        var world = new FakeWorld();
        var p = JsonDocument.Parse("{\"item_index\":1,\"count\":1}").RootElement;

        var result = ShopClickPurchaseHandler.Handle(p, world);
        var purchase = JsonSerializer.Deserialize<ShopClickPurchaseResult>(result, ProtocolJson.Options)!;

        Assert.Equal("(O)388", purchase.ItemId);
        Assert.Equal("Wood", purchase.DisplayName);
        Assert.Equal(1, purchase.ItemIndex);
        Assert.Equal("(O)388", world.Shop!.RevealedItemId);
    }

    [Fact]
    public void Handle_NegativeItemIndex_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"item_index\":-1}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            ShopClickPurchaseHandler.Handle(p, new FakeWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("item_index", ex.Message);
    }

    [Fact]
    public void Handle_ItemIndexOutOfRange_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"item_index\":99}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            ShopClickPurchaseHandler.Handle(p, new FakeWorld()));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("item_index 99", ex.Message);
    }

    [Fact]
    public void Handle_ClicksMatchingItemAndReturnsCurrencyAndBounds()
    {
        var world = new FakeWorld();
        var p = JsonDocument.Parse("{\"item_id\":\"(F)terminal\",\"count\":1}").RootElement;

        var result = ShopClickPurchaseHandler.Handle(p, world);
        var purchase = JsonSerializer.Deserialize<ShopClickPurchaseResult>(result, ProtocolJson.Options)!;

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
        Assert.Equal(860, purchase.Screen.X);
        Assert.Equal(420, purchase.Screen.Y);
        Assert.Equal(500, purchase.Bounds.X);
        Assert.Equal(380, purchase.Bounds.Y);
        Assert.Equal(720, purchase.Bounds.Width);
        Assert.Equal(80, purchase.Bounds.Height);
        Assert.Equal(1, purchase.VisibleIndex);
        Assert.Equal(2, purchase.ItemIndex);
        Assert.True(purchase.Scrolled);
        Assert.True(purchase.HeldItemDeposited);
        Assert.Equal((860, 420), world.Shop!.LastClick);
        Assert.Equal("(F)terminal", world.Shop.RevealedItemId);
        Assert.Equal(16, world.Shop.RevealScrollAttempts);
    }

    [Fact]
    public void Handle_ClicksDisplayNameTarget()
    {
        var world = new FakeWorld();
        var p = JsonDocument.Parse("{\"display_name\":\"Wood\"}").RootElement;

        var result = ShopClickPurchaseHandler.Handle(p, world);
        var purchase = JsonSerializer.Deserialize<ShopClickPurchaseResult>(result, ProtocolJson.Options)!;

        Assert.Equal("(O)388", purchase.ItemId);
        Assert.Equal("Wood", purchase.DisplayName);
        Assert.Equal("(O)388", world.Shop!.RevealedItemId);
    }

    private sealed class FakeWorld : IShopClickPurchaseWorld
    {
        public bool IsWorldReady { get; init; } = true;
        public int Tick => 1234;
        public int Money { get; set; } = 30000;
        public int FestivalScore { get; set; }
        public FakeShop? Shop { get; init; } = new();
        public IShopClickMenuState? ActiveShop => Shop;
    }

    private sealed class FakeShop : IShopClickMenuState
    {
        private readonly int _currency;

        public FakeShop(int currency = 0)
        {
            _currency = currency;
        }

        public string MenuType => "ShopMenu";
        public string ShopId => "Carpenter";
        public int Currency => _currency;
        public bool RevealSucceeds { get; init; } = true;
        public string? RevealedItemId { get; private set; }
        public int? RevealScrollAttempts { get; private set; }
        public (int X, int Y)? LastClick { get; private set; }
        public bool DebitOnClick { get; init; } = true;
        public IReadOnlyList<IShopItem> Items { get; } = new[]
        {
            new ShopItem("starter", "(O)starter", "Starter", 1, null, -16, 0, "Object"),
            new ShopItem("388", "(O)388", "Wood", 10, null, -16, 0, "Object"),
            new ShopItem("terminal", "(F)terminal", "Terminal", 25000, 1, -9, 0, "Furniture"),
        };

        public ShopClickTarget? RevealItem(IShopItem item, int scrollAttempts)
        {
            RevealedItemId = item.QualifiedId;
            RevealScrollAttempts = scrollAttempts;
            if (!RevealSucceeds)
                return null;

            return new ShopClickTarget
            {
                Screen = new PixelPoint { X = 860, Y = 420 },
                Bounds = new MenuBounds { X = 500, Y = 380, Width = 720, Height = 80 },
                VisibleIndex = 1,
                ItemIndex = IndexOf(item),
                Scrolled = true,
            };
        }

        public ShopClickCompletion Click(ShopClickTarget target, IShopCurrencyBalances balances)
        {
            LastClick = (target.Screen.X, target.Screen.Y);
            if (!DebitOnClick)
                return new ShopClickCompletion();

            var current = ShopCurrency.GetBalance(_currency, balances);
            ShopCurrency.SetBalance(_currency, balances, current - 25000);
            return new ShopClickCompletion { HeldItemDeposited = true };
        }

        private int IndexOf(IShopItem item)
        {
            for (var i = 0; i < Items.Count; i++)
            {
                if (ReferenceEquals(Items[i], item))
                    return i;
            }

            return -1;
        }
    }
}
