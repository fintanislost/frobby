using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class ShopRequestSerializationTests
{
    [Fact]
    public void ShopOpenRequest_DeserializesFromSnakeCase()
    {
        var json = "{\"shop_id\":\"Carpenter\",\"owner_name\":\"Robin\",\"force_open\":true}";
        var req = JsonSerializer.Deserialize<ShopOpenRequest>(json, ProtocolJson.Options)!;

        Assert.Equal("Carpenter", req.ShopId);
        Assert.Equal("Robin", req.OwnerName);
        Assert.True(req.ForceOpen);
    }

    [Fact]
    public void ShopOpenResult_SerializesOutcome()
    {
        var result = new ShopOpenResult
        {
            Tick = 42,
            ShopId = "Carpenter",
            MenuType = "ShopMenu",
        };

        var json = JsonSerializer.Serialize(result, ProtocolJson.Options);

        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"tick\":42", json);
        Assert.Contains("\"shop_id\":\"Carpenter\"", json);
        Assert.Contains("\"menu_type\":\"ShopMenu\"", json);
    }

    [Fact]
    public void ShopPurchaseRequest_DefaultsCountToOne()
    {
        var json = "{\"item_id\":\"(F)example_terminal\"}";
        var req = JsonSerializer.Deserialize<ShopPurchaseRequest>(json, ProtocolJson.Options)!;

        Assert.Equal("(F)example_terminal", req.ItemId);
        Assert.Equal(1, req.Count);
    }

    [Fact]
    public void ShopPurchaseResult_SerializesPurchaseDetails()
    {
        var result = new ShopPurchaseResult
        {
            Tick = 44,
            ShopId = "Festival_StardewValleyFair_StarTokens",
            ItemId = "(F)FlashShifter.StardewValleyExpandedCP_Furniture_Catalogue_2",
            DisplayName = "Furniture Catalogue 2",
            Count = 1,
            UnitPrice = 9999,
            Currency = 1,
            PreviousCurrencyBalance = 10000,
            CurrencyBalance = 1,
            PreviousMoney = 5000,
            Money = 5000,
        };

        var json = JsonSerializer.Serialize(result, ProtocolJson.Options);

        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"shop_id\":\"Festival_StardewValleyFair_StarTokens\"", json);
        Assert.Contains("\"item_id\":\"(F)FlashShifter.StardewValleyExpandedCP_Furniture_Catalogue_2\"", json);
        Assert.Contains("\"unit_price\":9999", json);
        Assert.Contains("\"currency\":1", json);
        Assert.Contains("\"previous_currency_balance\":10000", json);
        Assert.Contains("\"currency_balance\":1", json);
        Assert.Contains("\"previous_money\":5000", json);
        Assert.Contains("\"money\":5000", json);
    }

    [Fact]
    public void ShopClickPurchaseRequest_DefaultsCountAndScrollAttempts()
    {
        var json = "{\"item_id\":\"(F)example_terminal\"}";
        var req = JsonSerializer.Deserialize<ShopClickPurchaseRequest>(json, ProtocolJson.Options)!;

        Assert.Equal("(F)example_terminal", req.ItemId);
        Assert.Equal(string.Empty, req.DisplayName);
        Assert.Equal(1, req.Count);
        Assert.Equal(16, req.ScrollAttempts);
    }

    [Fact]
    public void ShopClickPurchaseRequest_DeserializesDisplayNameTarget()
    {
        var json = "{\"display_name\":\"Decorative Tulips\",\"count\":1,\"scroll_attempts\":4}";
        var req = JsonSerializer.Deserialize<ShopClickPurchaseRequest>(json, ProtocolJson.Options)!;

        Assert.Equal(string.Empty, req.ItemId);
        Assert.Equal("Decorative Tulips", req.DisplayName);
        Assert.Equal(1, req.Count);
        Assert.Equal(4, req.ScrollAttempts);
    }

    [Fact]
    public void ShopClickPurchaseResult_SerializesClickMetadata()
    {
        var result = new ShopClickPurchaseResult
        {
            Tick = 45,
            ShopId = "Festival_FlowerDance_Pierre",
            ItemId = "(F)FlashShifter.StardewValleyExpandedCP_Decorative_Tulips",
            DisplayName = "Decorative Tulips",
            Count = 1,
            UnitPrice = 400,
            Currency = 0,
            PreviousCurrencyBalance = 1000,
            CurrencyBalance = 600,
            PreviousMoney = 1000,
            Money = 600,
            Screen = new PixelPoint { X = 880, Y = 420 },
            Bounds = new MenuBounds { X = 500, Y = 380, Width = 760, Height = 80 },
            VisibleIndex = 1,
            ItemIndex = 3,
            Scrolled = true,
            HeldItemDeposited = true,
        };

        var json = JsonSerializer.Serialize(result, ProtocolJson.Options);

        Assert.Contains("\"shop_id\":\"Festival_FlowerDance_Pierre\"", json);
        Assert.Contains("\"screen\":{\"x\":880,\"y\":420}", json);
        Assert.Contains("\"bounds\":{\"x\":500,\"y\":380,\"width\":760,\"height\":80}", json);
        Assert.Contains("\"visible_index\":1", json);
        Assert.Contains("\"item_index\":3", json);
        Assert.Contains("\"scrolled\":true", json);
        Assert.Contains("\"held_item_deposited\":true", json);
    }

    [Fact]
    public void ShopState_SerializesLiveShopInventory()
    {
        var state = new ShopState
        {
            Present = true,
            MenuType = "ShopMenu",
            ShopId = "ExampleMod.CustomVendor",
            Currency = 1,
            CurrencyName = "star_tokens",
            CurrencyBalance = 10000,
            Items =
            {
                new ShopItemSummary
                {
                    ItemId = "ExampleMod.CustomDrink",
                    QualifiedId = "(O)ExampleMod.CustomDrink",
                    DisplayName = "Custom Drink",
                    Price = 4000,
                    Stock = 5,
                    Category = 0,
                    Quality = 0,
                    RuntimeType = "Object",
                },
            },
        };

        var json = JsonSerializer.Serialize(state, ProtocolJson.Options);

        Assert.Contains("\"present\":true", json);
        Assert.Contains("\"menu_type\":\"ShopMenu\"", json);
        Assert.Contains("\"shop_id\":\"ExampleMod.CustomVendor\"", json);
        Assert.Contains("\"currency\":1", json);
        Assert.Contains("\"currency_name\":\"star_tokens\"", json);
        Assert.Contains("\"currency_balance\":10000", json);
        Assert.Contains("\"item_id\":\"ExampleMod.CustomDrink\"", json);
        Assert.Contains("\"qualified_id\":\"(O)ExampleMod.CustomDrink\"", json);
        Assert.Contains("\"display_name\":\"Custom Drink\"", json);
        Assert.Contains("\"price\":4000", json);
        Assert.Contains("\"stock\":5", json);
        Assert.Contains("\"runtime_type\":\"Object\"", json);
    }
}
