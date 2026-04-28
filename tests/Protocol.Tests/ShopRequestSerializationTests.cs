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
        var json = "{\"item_id\":\"(F)stonks_starberg_terminal_v1\"}";
        var req = JsonSerializer.Deserialize<ShopPurchaseRequest>(json, ProtocolJson.Options)!;

        Assert.Equal("(F)stonks_starberg_terminal_v1", req.ItemId);
        Assert.Equal(1, req.Count);
    }

    [Fact]
    public void ShopPurchaseResult_SerializesPurchaseDetails()
    {
        var result = new ShopPurchaseResult
        {
            Tick = 44,
            ShopId = "Carpenter",
            ItemId = "(F)stonks_starberg_terminal_v1",
            DisplayName = "Starberg Terminal - Model 4201",
            Count = 1,
            UnitPrice = 25000,
            PreviousMoney = 30000,
            Money = 5000,
        };

        var json = JsonSerializer.Serialize(result, ProtocolJson.Options);

        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"shop_id\":\"Carpenter\"", json);
        Assert.Contains("\"item_id\":\"(F)stonks_starberg_terminal_v1\"", json);
        Assert.Contains("\"unit_price\":25000", json);
        Assert.Contains("\"previous_money\":30000", json);
        Assert.Contains("\"money\":5000", json);
    }
}
