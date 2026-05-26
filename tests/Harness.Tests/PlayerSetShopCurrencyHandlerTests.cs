using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class PlayerSetShopCurrencyHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() =>
            PlayerSetShopCurrencyHandler.Handle(null, new FakeWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_NegativeAmount_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"currency\":1,\"amount\":-1}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            PlayerSetShopCurrencyHandler.Handle(p, new FakeWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("amount", ex.Message);
    }

    [Fact]
    public void Handle_NotWorldReady_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"currency\":1,\"amount\":10000}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            PlayerSetShopCurrencyHandler.Handle(p, new FakeWorld { IsWorldReady = false }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_UnsupportedCurrency_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"currency\":99,\"amount\":10000}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            PlayerSetShopCurrencyHandler.Handle(p, new FakeWorld()));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("99", ex.Message);
    }

    [Fact]
    public void Handle_SetsGoldBalance()
    {
        var p = JsonDocument.Parse("{\"currency\":0,\"amount\":5000}").RootElement;
        var world = new FakeWorld { Money = 100 };

        var result = PlayerSetShopCurrencyHandler.Handle(p, world);
        var set = JsonSerializer.Deserialize<SetShopCurrencyResult>(result, ProtocolJson.Options)!;

        Assert.True(set.Ok);
        Assert.Equal(1234, set.Tick);
        Assert.Equal(0, set.Currency);
        Assert.Equal("gold", set.CurrencyName);
        Assert.Equal(100, set.Previous);
        Assert.Equal(5000, set.Amount);
        Assert.Equal(5000, world.Money);
    }

    [Fact]
    public void Handle_SetsStarTokenBalance()
    {
        var p = JsonDocument.Parse("{\"currency\":1,\"amount\":10000}").RootElement;
        var world = new FakeWorld { FestivalScore = 75 };

        var result = PlayerSetShopCurrencyHandler.Handle(p, world);
        var set = JsonSerializer.Deserialize<SetShopCurrencyResult>(result, ProtocolJson.Options)!;

        Assert.True(set.Ok);
        Assert.Equal(1, set.Currency);
        Assert.Equal("star_tokens", set.CurrencyName);
        Assert.Equal(75, set.Previous);
        Assert.Equal(10000, set.Amount);
        Assert.Equal(10000, world.FestivalScore);
    }

    private sealed class FakeWorld : IPlayerSetShopCurrencyWorld
    {
        public bool IsWorldReady { get; init; } = true;
        public int Tick => 1234;
        public int Money { get; set; } = 30000;
        public int FestivalScore { get; set; }
    }
}
