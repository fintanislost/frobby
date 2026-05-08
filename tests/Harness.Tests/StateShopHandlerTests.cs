using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class StateShopHandlerTests
{
    [Fact]
    public void Handle_NoActiveShop_ReturnsAbsentShopState()
    {
        var result = StateShopHandler.Handle(null, new FakeShopStateWorld { ActiveShop = null });
        var state = JsonSerializer.Deserialize<ShopState>(result, ProtocolJson.Options)!;

        Assert.False(state.Present);
        Assert.Equal("", state.MenuType);
        Assert.Equal("", state.ShopId);
        Assert.Equal(0, state.Currency);
        Assert.Empty(state.Items);
    }

    [Fact]
    public void Handle_ActiveShop_ReturnsProjectedShopState()
    {
        var result = StateShopHandler.Handle(null, new FakeShopStateWorld());
        var state = JsonSerializer.Deserialize<ShopState>(result, ProtocolJson.Options)!;

        Assert.True(state.Present);
        Assert.Equal("ShopMenu", state.MenuType);
        Assert.Equal("ExampleMod.CustomVendor", state.ShopId);
        Assert.Equal(0, state.Currency);
        Assert.Collection(state.Items,
            item =>
            {
                Assert.Equal("ExampleMod.CustomDrink", item.ItemId);
                Assert.Equal("(O)ExampleMod.CustomDrink", item.QualifiedId);
                Assert.Equal("Custom Drink", item.DisplayName);
                Assert.Equal(4000, item.Price);
                Assert.Equal(5, item.Stock);
                Assert.Equal(0, item.Category);
                Assert.Equal(0, item.Quality);
                Assert.Equal("Object", item.RuntimeType);
            });
    }

    private sealed class FakeShopStateWorld : IShopStateWorld
    {
        public IShopMenuState? ActiveShop { get; init; } = new FakeShop();
    }

    private sealed class FakeShop : IShopMenuState
    {
        public string MenuType => "ShopMenu";
        public string ShopId => "ExampleMod.CustomVendor";
        public int Currency => 0;
        public IReadOnlyList<IShopItem> Items { get; } = new[]
        {
            new ShopItem(
                "ExampleMod.CustomDrink",
                "(O)ExampleMod.CustomDrink",
                "Custom Drink",
                4000,
                5,
                0,
                0,
                "Object"),
        };
    }
}
