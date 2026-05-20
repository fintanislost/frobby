using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Models;
using SdvTestFramework.Runner.Dsl;
using Xunit;

namespace SdvTestFramework.Runner.Dsl.Tests.Facets;

public sealed class CombatLabTests
{
    private sealed class CapturingInvoker : ISdvTestInvoker
    {
        public List<(string Method, string ParamsJson)> Calls { get; } = new();

        public Task<JsonElement> InvokeAsync(string method, JsonElement? @params, CancellationToken ct)
        {
            Calls.Add((method, @params?.GetRawText() ?? ""));
            return Task.FromResult(JsonDocument.Parse(
                method == "combat_lab.spawn_monster"
                    ? "{\"ok\":true,\"monster_id\":\"frobby-monster-1\",\"label\":\"target\",\"kind\":\"GreenSlime\",\"location\":\"Frobby_CombatLab\",\"tile\":{\"x\":12,\"y\":8},\"health\":1,\"max_health\":24}"
                    : "{\"ok\":true,\"location\":\"Frobby_CombatLab\",\"player_tile\":{\"x\":8,\"y\":8},\"map_width\":20,\"map_height\":14,\"cleared_monsters\":0,\"cleared_debris\":0}").RootElement.Clone());
        }
    }

    [Fact]
    public async Task Reset_InvokesCombatLabReset()
    {
        SdvTestSession.ResetForTests();
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try { await CombatLab.Reset(playerX: 8, playerY: 8); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Single(inv.Calls);
        Assert.Equal("combat_lab.reset", inv.Calls[0].Method);
        Assert.Contains("\"player_x\":8", inv.Calls[0].ParamsJson);
    }

    [Fact]
    public async Task SpawnMonster_InvokesCombatLabSpawnMonsterAndReturnsResult()
    {
        SdvTestSession.ResetForTests();
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        CombatLabSpawnMonsterResult result;
        try { result = await CombatLab.SpawnMonster("GreenSlime", "target", 12, 8, health: 1); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Single(inv.Calls);
        Assert.Equal("combat_lab.spawn_monster", inv.Calls[0].Method);
        Assert.Contains("\"kind\":\"GreenSlime\"", inv.Calls[0].ParamsJson);
        Assert.Equal("frobby-monster-1", result.MonsterId);
    }
}
