using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>Ambient static DSL for the <c>shop.*</c> RPC surface.</summary>
public static class Shop
{
    /// <summary>Open a data-backed Stardew shop menu.</summary>
    public static async Task<ShopOpenResult> Open(
        string shopId,
        string? ownerName = null,
        bool forceOpen = false,
        CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new ShopOpenRequest
        {
            ShopId = shopId,
            OwnerName = ownerName,
            ForceOpen = forceOpen,
        }, ProtocolJson.Options);
        var resp = await s.InvokeAsync("shop.open", p, ct);
        return JsonSerializer.Deserialize<ShopOpenResult>(resp, ProtocolJson.Options)
            ?? throw new SdvRpcException("shop.open", Protocol.JsonRpcErrorCode.InternalError,
                "empty shop.open response");
    }

    /// <summary>Purchase an item from the active shop through Frobby's semantic shop path.</summary>
    public static async Task<ShopPurchaseResult> Purchase(
        string itemId,
        int count = 1,
        CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new ShopPurchaseRequest
        {
            ItemId = itemId,
            Count = count,
        }, ProtocolJson.Options);
        var resp = await s.InvokeAsync("shop.purchase", p, ct);
        return JsonSerializer.Deserialize<ShopPurchaseResult>(resp, ProtocolJson.Options)
            ?? throw new SdvRpcException("shop.purchase", Protocol.JsonRpcErrorCode.InternalError,
                "empty shop.purchase response");
    }

    /// <summary>Purchase an item by clicking its visible row in the active shop menu.</summary>
    public static async Task<ShopClickPurchaseResult> ClickPurchase(
        string itemId = "",
        string displayName = "",
        int? itemIndex = null,
        int count = 1,
        int scrollAttempts = 16,
        CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new ShopClickPurchaseRequest
        {
            ItemId = itemId,
            DisplayName = displayName,
            ItemIndex = itemIndex,
            Count = count,
            ScrollAttempts = scrollAttempts,
        }, ProtocolJson.Options);
        var resp = await s.InvokeAsync("shop.click_purchase", p, ct);
        return JsonSerializer.Deserialize<ShopClickPurchaseResult>(resp, ProtocolJson.Options)
            ?? throw new SdvRpcException("shop.click_purchase", Protocol.JsonRpcErrorCode.InternalError,
                "empty shop.click_purchase response");
    }
}
