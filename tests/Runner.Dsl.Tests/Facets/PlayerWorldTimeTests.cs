using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Dsl;
using Xunit;

namespace SdvTestFramework.Runner.Dsl.Tests.Facets;

public class PlayerWorldTimeTests
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
    public async Task Warp_InvokesPlayerWarpWithLocationXY()
    {
        SdvTestSession.ResetForTests();  // Clear any prior state
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try { await Player.Warp("SeedShop", 4, 19); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Single(inv.Calls);
        Assert.Equal("player.warp", inv.Calls[0].Method);
        Assert.Contains("\"location\":\"SeedShop\"", inv.Calls[0].ParamsJson);
        Assert.Contains("\"x\":4", inv.Calls[0].ParamsJson);
        Assert.Contains("\"y\":19", inv.Calls[0].ParamsJson);
    }

    [Fact]
    public async Task SetMoney_InvokesPlayerSetMoneyWithAmount()
    {
        SdvTestSession.ResetForTests();  // Clear any prior state
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try { await Player.SetMoney(5000); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Equal("player.set_money", inv.Calls[0].Method);
        Assert.Contains("\"amount\":5000", inv.Calls[0].ParamsJson);
    }

    [Fact]
    public async Task Advance_InvokesTimeAdvanceWithMinutes()
    {
        SdvTestSession.ResetForTests();  // Clear any prior state
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try { await Time.Advance(60); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Equal("time.advance", inv.Calls[0].Method);
        Assert.Contains("\"minutes\":60", inv.Calls[0].ParamsJson);
    }

    [Fact]
    public async Task SetWeather_InvokesWorldSetWeatherWithType()
    {
        SdvTestSession.ResetForTests();  // Clear any prior state
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try { await World.SetWeather("rain"); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Equal("world.set_weather", inv.Calls[0].Method);
        Assert.Contains("\"type\":\"rain\"", inv.Calls[0].ParamsJson);
    }

    [Fact]
    public async Task InteractNpc_InvokesWorldInteractNpcWithName()
    {
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try { await World.InteractNpc("Pierre"); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Equal("world.interact_npc", inv.Calls[0].Method);
        Assert.Contains("\"name\":\"Pierre\"", inv.Calls[0].ParamsJson);
    }

    [Fact]
    public async Task TimeSet_InvokesTimeSetWithFields()
    {
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try { await Time.Set(time: 1530, day: 5, season: "spring", year: 1); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Equal("time.set", inv.Calls[0].Method);
        Assert.Contains("\"time\":1530", inv.Calls[0].ParamsJson);
        Assert.Contains("\"day\":5", inv.Calls[0].ParamsJson);
        Assert.Contains("\"season\":\"spring\"", inv.Calls[0].ParamsJson);
        Assert.Contains("\"year\":1", inv.Calls[0].ParamsJson);
    }
}
