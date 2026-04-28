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
        Assert.Equal(30000, purchase.PreviousMoney);
        Assert.Equal(5000, purchase.Money);
        Assert.Equal("(F)terminal", world.PurchasedItemId);
        Assert.Equal(1, world.PurchasedCount);
    }

    private sealed class FakeShopPurchaseWorld : IShopPurchaseWorld
    {
        public bool IsWorldReady { get; init; } = true;
        public int Tick => 1234;
        public int Money { get; private set; } = 30000;
        public bool PurchaseSucceeds { get; init; } = true;
        public string? PurchasedItemId { get; private set; }
        public int PurchasedCount { get; private set; }
        public IShopMenuState? ActiveShop { get; init; } = new FakeShop();

        public bool Purchase(IShopItem item, int count)
        {
            PurchasedItemId = item.ItemId;
            PurchasedCount = count;
            if (!PurchaseSucceeds)
                return false;

            Money -= item.UnitPrice * count;
            return true;
        }
    }

    private sealed class FakeShop : IShopMenuState
    {
        public string ShopId => "Carpenter";
        public IReadOnlyList<IShopItem> Items { get; } = new[]
        {
            new ShopItem("(F)terminal", "Terminal", 25000),
            new ShopItem("(O)388", "Wood", 10),
        };
    }
}
