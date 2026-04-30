using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Models;
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
    public async Task NextDay_InvokesTimeNextDayAndReturnsNewDate()
    {
        SdvTestSession.ResetForTests();  // Clear any prior state
        var inv = new CapturingInvoker
        {
            NextResponse = JsonDocument.Parse(
                "{\"ok\":true,\"tick\":90123,\"year\":1,\"season\":\"spring\",\"day_of_month\":2,\"time_of_day\":600}")
                .RootElement,
        };
        SdvTestSession.InitializeForTests(inv);
        TimeNextDayResult result;
        try { result = await Time.NextDay(); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Equal("time.next_day", inv.Calls[0].Method);
        Assert.Equal("", inv.Calls[0].ParamsJson);
        Assert.Equal(90123, result.Tick);
        Assert.Equal(1, result.Year);
        Assert.Equal("spring", result.Season);
        Assert.Equal(2, result.DayOfMonth);
        Assert.Equal(600, result.TimeOfDay);
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
    public async Task InteractTile_InvokesWorldInteractTile()
    {
        var inv = new CapturingInvoker();
        inv.NextResponse = JsonDocument.Parse(
            "{\"ok\":true,\"tick\":42,\"handled\":false,\"target_type\":\"Furniture\",\"tile\":{\"x\":8,\"y\":9}}")
            .RootElement;
        SdvTestSession.InitializeForTests(inv);
        InteractTileResult result;
        try { result = await World.InteractTile(8, 9, justCheckingForActivity: true); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Equal("world.interact_tile", inv.Calls[0].Method);
        Assert.Contains("\"x\":8", inv.Calls[0].ParamsJson);
        Assert.Contains("\"y\":9", inv.Calls[0].ParamsJson);
        Assert.Contains("\"just_checking_for_activity\":true", inv.Calls[0].ParamsJson);
        Assert.False(result.Handled);
        Assert.Equal("Furniture", result.TargetType);
        Assert.Equal(8, result.Tile.X);
        Assert.Equal(9, result.Tile.Y);
    }

    [Fact]
    public async Task InputKey_InvokesInputKey()
    {
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try { await Input.Key("Enter"); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Equal("input.key", inv.Calls[0].Method);
        Assert.Contains("\"key\":\"Enter\"", inv.Calls[0].ParamsJson);
    }

    [Fact]
    public async Task InputText_InvokesInputText()
    {
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try { await Input.Text("OE", submit: true); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Equal("input.text", inv.Calls[0].Method);
        Assert.Contains("\"text\":\"OE\"", inv.Calls[0].ParamsJson);
        Assert.Contains("\"submit\":true", inv.Calls[0].ParamsJson);
    }

    [Fact]
    public async Task InputClick_InvokesInputClick()
    {
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try { await Input.Click(144, 134); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Equal("input.click", inv.Calls[0].Method);
        Assert.Contains("\"x\":144", inv.Calls[0].ParamsJson);
        Assert.Contains("\"y\":134", inv.Calls[0].ParamsJson);
        Assert.Contains("\"button\":\"left\"", inv.Calls[0].ParamsJson);
    }

    [Fact]
    public async Task InputHover_InvokesInputHover()
    {
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try { await Input.Hover(144, 134); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Equal("input.hover", inv.Calls[0].Method);
        Assert.Contains("\"x\":144", inv.Calls[0].ParamsJson);
        Assert.Contains("\"y\":134", inv.Calls[0].ParamsJson);
    }

    [Fact]
    public async Task InputClickText_InvokesInputClickText()
    {
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try { await Input.ClickText("CONTINUE"); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Equal("input.click_text", inv.Calls[0].Method);
        Assert.Contains("\"text\":\"CONTINUE\"", inv.Calls[0].ParamsJson);
        Assert.Contains("\"button\":\"left\"", inv.Calls[0].ParamsJson);
    }

    [Fact]
    public async Task InputHoverText_InvokesInputHoverText()
    {
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try { await Input.HoverText("CONTINUE"); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Equal("input.hover_text", inv.Calls[0].Method);
        Assert.Contains("\"text\":\"CONTINUE\"", inv.Calls[0].ParamsJson);
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
