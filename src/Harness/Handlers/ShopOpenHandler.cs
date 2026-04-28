using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>shop.open</c>. Opens a data-backed Stardew shop by ID.</summary>
public static class ShopOpenHandler
{
    public const string Method = "shop.open";

    private static readonly IShopOpenWorld ProductionWorld = new SdvShopOpenWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, IShopOpenWorld world)
    {
        var req = RpcParams.Required<ShopOpenRequest>(paramsElement);
        if (string.IsNullOrWhiteSpace(req.ShopId))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.shop_id required");

        if (!world.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "shop.open requires a loaded world");

        if (!world.OpenShop(req.ShopId, req.OwnerName, req.ForceOpen))
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"shop.open could not open shop '{req.ShopId}'");

        return ProtocolJson.ToElement(new ShopOpenResult
        {
            Tick = world.Tick,
            ShopId = req.ShopId,
            MenuType = world.ActiveMenuType ?? string.Empty,
        });
    }
}

internal interface IShopOpenWorld
{
    bool IsWorldReady { get; }
    int Tick { get; }
    string? ActiveMenuType { get; }
    bool OpenShop(string shopId, string? ownerName, bool forceOpen);
}

internal sealed class SdvShopOpenWorld : IShopOpenWorld
{
    public bool IsWorldReady => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;
    public int Tick => Game1.ticks;
    public string? ActiveMenuType => Game1.activeClickableMenu?.GetType().Name;

    public bool OpenShop(string shopId, string? ownerName, bool forceOpen)
    {
        if (!forceOpen)
            return Utility.TryOpenShopMenu(shopId, ownerName ?? string.Empty, playOpenSound: false);

        var location = Game1.currentLocation
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "shop.open requires a current location");

        return Utility.TryOpenShopMenu(
            shopId,
            location,
            ownerArea: null,
            maxOwnerY: null,
            forceOpen: true,
            playOpenSound: false,
            showClosedMessage: null);
    }
}
