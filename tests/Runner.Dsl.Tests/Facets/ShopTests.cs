using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Models;
using SdvTestFramework.Runner.Dsl;
using Xunit;

namespace SdvTestFramework.Runner.Dsl.Tests.Facets;

public class ShopTests
{
    private sealed class CapturingInvoker : ISdvTestInvoker
    {
        public List<(string Method, string ParamsJson)> Calls { get; } = new();
        public JsonElement NextResponse { get; set; } = JsonDocument.Parse("{\"ok\":true,\"tick\":42}").RootElement;

        public Task<JsonElement> InvokeAsync(string method, JsonElement? p, CancellationToken ct)
        {
            Calls.Add((method, p?.GetRawText() ?? ""));
            return Task.FromResult(NextResponse);
        }
    }

    [Fact]
    public async Task Open_InvokesShopOpen()
    {
        SdvTestSession.ResetForTests();
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try { await Shop.Open("Festival_FlowerDance_Pierre", forceOpen: true); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Equal("shop.open", inv.Calls[0].Method);
        Assert.Contains("\"shop_id\":\"Festival_FlowerDance_Pierre\"", inv.Calls[0].ParamsJson);
        Assert.Contains("\"force_open\":true", inv.Calls[0].ParamsJson);
    }

    [Fact]
    public async Task Purchase_InvokesShopPurchaseAndDeserializesResult()
    {
        SdvTestSession.ResetForTests();
        var inv = new CapturingInvoker
        {
            NextResponse = JsonDocument.Parse(
                "{\"ok\":true,\"tick\":44,\"shop_id\":\"Carpenter\",\"item_id\":\"(F)terminal\",\"display_name\":\"Terminal\",\"count\":1,\"unit_price\":25000,\"currency\":0,\"previous_currency_balance\":30000,\"currency_balance\":5000,\"previous_money\":30000,\"money\":5000}")
                .RootElement,
        };
        SdvTestSession.InitializeForTests(inv);

        ShopPurchaseResult result;
        try { result = await Shop.Purchase("(F)terminal"); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Equal("shop.purchase", inv.Calls[0].Method);
        Assert.Contains("\"item_id\":\"(F)terminal\"", inv.Calls[0].ParamsJson);
        Assert.Equal("(F)terminal", result.ItemId);
        Assert.Equal(5000, result.Money);
    }

    [Fact]
    public async Task ClickPurchase_InvokesShopClickPurchaseAndDeserializesResult()
    {
        SdvTestSession.ResetForTests();
        var inv = new CapturingInvoker
        {
            NextResponse = JsonDocument.Parse(
                "{\"ok\":true,\"tick\":45,\"shop_id\":\"Festival_FlowerDance_Pierre\",\"item_id\":\"(F)terminal\",\"display_name\":\"Terminal\",\"count\":1,\"unit_price\":25000,\"currency\":0,\"previous_currency_balance\":30000,\"currency_balance\":5000,\"previous_money\":30000,\"money\":5000,\"screen\":{\"x\":860,\"y\":420},\"bounds\":{\"x\":500,\"y\":380,\"width\":720,\"height\":80},\"visible_index\":1,\"item_index\":2,\"scrolled\":true}")
                .RootElement,
        };
        SdvTestSession.InitializeForTests(inv);

        ShopClickPurchaseResult result;
        try { result = await Shop.ClickPurchase("(F)terminal", scrollAttempts: 4); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Equal("shop.click_purchase", inv.Calls[0].Method);
        Assert.Contains("\"item_id\":\"(F)terminal\"", inv.Calls[0].ParamsJson);
        Assert.Contains("\"scroll_attempts\":4", inv.Calls[0].ParamsJson);
        Assert.Equal("(F)terminal", result.ItemId);
        Assert.Equal(860, result.Screen.X);
        Assert.True(result.Scrolled);
    }
}
