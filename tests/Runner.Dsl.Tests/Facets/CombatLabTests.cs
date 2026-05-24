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
            return Task.FromResult(JsonDocument.Parse(method switch
            {
                "combat_lab.spawn_monster" => "{\"ok\":true,\"monster_id\":\"frobby-monster-1\",\"label\":\"target\",\"kind\":\"GreenSlime\",\"location\":\"Frobby_CombatLab\",\"tile\":{\"x\":12,\"y\":8},\"health\":1,\"max_health\":24}",
                "combat_lab.relocate_monster" => "{\"ok\":true,\"monster_id\":\"frobby-monster-2\",\"label\":\"corrupt-mummy\",\"from_location\":\"Custom_CrimsonBadlands\",\"source_tile\":{\"x\":20,\"y\":144},\"location\":\"Frobby_CombatLab\",\"tile\":{\"x\":9,\"y\":8},\"name\":\"Mummy\",\"type\":\"Mummy\",\"sprite_texture\":\"Characters/Monsters/CorruptMummy\",\"health\":2000,\"max_health\":2000}",
                _ => "{\"ok\":true,\"location\":\"Frobby_CombatLab\",\"player_tile\":{\"x\":8,\"y\":8},\"map_width\":20,\"map_height\":14,\"cleared_monsters\":0,\"cleared_debris\":0}",
            }).RootElement.Clone());
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

    [Fact]
    public async Task RelocateMonster_InvokesCombatLabRelocateMonsterAndReturnsResult()
    {
        SdvTestSession.ResetForTests();
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        CombatLabRelocateMonsterResult result;
        try
        {
            result = await CombatLab.RelocateMonster(
                fromLocation: "Custom_CrimsonBadlands",
                label: "corrupt-mummy",
                targetX: 9,
                targetY: 8,
                match: new CombatLabMonsterMatchCriteria
                {
                    X = 20,
                    Y = 144,
                    SpriteTexture = "Characters/Monsters/CorruptMummy",
                    Health = 2000,
                    MaxHealth = 2000,
                });
        }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Single(inv.Calls);
        Assert.Equal("combat_lab.relocate_monster", inv.Calls[0].Method);
        Assert.Contains("\"from_location\":\"Custom_CrimsonBadlands\"", inv.Calls[0].ParamsJson);
        Assert.Contains("\"target_x\":9", inv.Calls[0].ParamsJson);
        Assert.Contains("\"sprite_texture\":\"Characters/Monsters/CorruptMummy\"", inv.Calls[0].ParamsJson);
        Assert.Equal("frobby-monster-2", result.MonsterId);
        Assert.Equal("corrupt-mummy", result.Label);
    }
}
