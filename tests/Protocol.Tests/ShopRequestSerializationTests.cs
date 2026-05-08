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
            ShopId = "Carpenter",
            ItemId = "(F)example_terminal",
            DisplayName = "Example Terminal",
            Count = 1,
            UnitPrice = 25000,
            PreviousMoney = 30000,
            Money = 5000,
        };

        var json = JsonSerializer.Serialize(result, ProtocolJson.Options);

        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"shop_id\":\"Carpenter\"", json);
        Assert.Contains("\"item_id\":\"(F)example_terminal\"", json);
        Assert.Contains("\"unit_price\":25000", json);
        Assert.Contains("\"previous_money\":30000", json);
        Assert.Contains("\"money\":5000", json);
    }

    [Fact]
    public void ShopState_SerializesLiveShopInventory()
    {
        var state = new ShopState
        {
            Present = true,
            MenuType = "ShopMenu",
            ShopId = "FlashShifter.StardewValleyExpandedCP_CamillaVendor",
            Currency = 0,
            Items =
            {
                new ShopItemSummary
                {
                    ItemId = "FlashShifter.StardewValleyExpandedCP_Gravity_Elixir",
                    QualifiedId = "(O)FlashShifter.StardewValleyExpandedCP_Gravity_Elixir",
                    DisplayName = "Gravity Elixir",
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
        Assert.Contains("\"shop_id\":\"FlashShifter.StardewValleyExpandedCP_CamillaVendor\"", json);
        Assert.Contains("\"currency\":0", json);
        Assert.Contains("\"item_id\":\"FlashShifter.StardewValleyExpandedCP_Gravity_Elixir\"", json);
        Assert.Contains("\"qualified_id\":\"(O)FlashShifter.StardewValleyExpandedCP_Gravity_Elixir\"", json);
        Assert.Contains("\"display_name\":\"Gravity Elixir\"", json);
        Assert.Contains("\"price\":4000", json);
        Assert.Contains("\"stock\":5", json);
        Assert.Contains("\"runtime_type\":\"Object\"", json);
    }
}
