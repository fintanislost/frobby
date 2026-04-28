using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class ShopOpenHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => ShopOpenHandler.Handle(null, new FakeShopOpenWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_MissingShopId_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => ShopOpenHandler.Handle(p, new FakeShopOpenWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("shop_id", ex.Message);
    }

    [Fact]
    public void Handle_NoLoadedWorld_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"shop_id\":\"Carpenter\"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            ShopOpenHandler.Handle(p, new FakeShopOpenWorld { IsWorldReady = false }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_OpenFailure_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"shop_id\":\"Carpenter\"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            ShopOpenHandler.Handle(p, new FakeShopOpenWorld { Opens = false }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("Carpenter", ex.Message);
    }

    [Fact]
    public void Handle_OpensShopAndReturnsMenuType()
    {
        var world = new FakeShopOpenWorld();
        var p = JsonDocument.Parse("{\"shop_id\":\"Carpenter\",\"owner_name\":\"Robin\",\"force_open\":false}").RootElement;

        var result = ShopOpenHandler.Handle(p, world);
        var open = JsonSerializer.Deserialize<ShopOpenResult>(result, ProtocolJson.Options)!;

        Assert.True(open.Ok);
        Assert.Equal(1234, open.Tick);
        Assert.Equal("Carpenter", open.ShopId);
        Assert.Equal("ShopMenu", open.MenuType);
        Assert.Equal("Carpenter", world.LastShopId);
        Assert.Equal("Robin", world.LastOwnerName);
        Assert.False(world.LastForceOpen);
    }

    private sealed class FakeShopOpenWorld : IShopOpenWorld
    {
        public bool IsWorldReady { get; init; } = true;
        public int Tick => 1234;
        public bool Opens { get; init; } = true;
        public string? LastShopId { get; private set; }
        public string? LastOwnerName { get; private set; }
        public bool LastForceOpen { get; private set; }
        public string? ActiveMenuType => Opens ? "ShopMenu" : null;

        public bool OpenShop(string shopId, string? ownerName, bool forceOpen)
        {
            LastShopId = shopId;
            LastOwnerName = ownerName;
            LastForceOpen = forceOpen;
            return Opens;
        }
    }
}
