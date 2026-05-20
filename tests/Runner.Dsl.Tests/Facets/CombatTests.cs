using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Dsl;
using Xunit;

namespace SdvTestFramework.Runner.Dsl.Tests.Facets;

public class CombatTests
{
    private sealed class CapturingInvoker : ISdvTestInvoker
    {
        public List<(string Method, string ParamsJson)> Calls { get; } = new();

        public Task<JsonElement> InvokeAsync(string method, JsonElement? @params, CancellationToken ct)
        {
            Calls.Add((method, @params?.GetRawText() ?? ""));
            return Task.FromResult(JsonDocument.Parse("{\"ok\":true,\"tick\":42}").RootElement.Clone());
        }
    }

    [Fact]
    public async Task AttackTile_InvokesCombatAttackWithTileAndQualifiedItem()
    {
        SdvTestSession.ResetForTests();
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try { await Combat.AttackTile(20, 144, qualifiedItemId: "(W)4"); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Single(inv.Calls);
        Assert.Equal("combat.attack", inv.Calls[0].Method);
        Assert.Contains("\"x\":20", inv.Calls[0].ParamsJson);
        Assert.Contains("\"y\":144", inv.Calls[0].ParamsJson);
        Assert.Contains("\"qualified_item_id\":\"(W)4\"", inv.Calls[0].ParamsJson);
    }

    [Fact]
    public async Task AttackDirection_InvokesCombatAttackWithDirectionRepeatAndDelayTicks()
    {
        SdvTestSession.ResetForTests();
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try { await Combat.AttackDirection("up", repeat: 2, delayTicks: 1); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Single(inv.Calls);
        Assert.Equal("combat.attack", inv.Calls[0].Method);
        Assert.Contains("\"direction\":\"up\"", inv.Calls[0].ParamsJson);
        Assert.Contains("\"repeat\":2", inv.Calls[0].ParamsJson);
        Assert.Contains("\"delay_ticks\":1", inv.Calls[0].ParamsJson);
    }

    [Fact]
    public async Task AttackTarget_InvokesCombatAttackWithLabel()
    {
        SdvTestSession.ResetForTests();
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try { await Combat.AttackTarget(label: "target", location: "Frobby_CombatLab", qualifiedItemId: "(W)4", repeat: 3, delayTicks: 1); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Single(inv.Calls);
        Assert.Equal("combat.attack", inv.Calls[0].Method);
        Assert.Contains("\"label\":\"target\"", inv.Calls[0].ParamsJson);
        Assert.Contains("\"location\":\"Frobby_CombatLab\"", inv.Calls[0].ParamsJson);
        Assert.Contains("\"qualified_item_id\":\"(W)4\"", inv.Calls[0].ParamsJson);
        Assert.Contains("\"repeat\":3", inv.Calls[0].ParamsJson);
    }
}
