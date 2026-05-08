using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using StardewValley;
using StardewValley.Menus;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for the <c>state.shop</c> RPC method. Projects the active shop menu, if any.</summary>
public static class StateShopHandler
{
    public const string Method = "state.shop";

    private static readonly IShopStateWorld ProductionWorld = new SdvShopStateWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, IShopStateWorld world)
        => ProtocolJson.ToElement(ShopStateProjector.Project(world.ActiveShop));
}

internal interface IShopStateWorld
{
    IShopMenuState? ActiveShop { get; }
}

internal sealed class SdvShopStateWorld : IShopStateWorld
{
    public IShopMenuState? ActiveShop => Game1.activeClickableMenu is ShopMenu shop
        ? new SdvShopMenuState(shop)
        : null;
}
