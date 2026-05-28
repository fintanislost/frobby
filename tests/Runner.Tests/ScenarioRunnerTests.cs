using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Models;
using SdvTestFramework.Protocol.Reports;
using SdvTestFramework.Runner.Scenarios;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

/// <summary>
/// Exercises the <see cref="ScenarioRunner"/> against an in-proc Unix socket server so no
/// live SDV is required. Mirrors the pattern used in <c>ProbeCommandTests</c>.
/// </summary>
public class ScenarioRunnerTests
{
    private static string SocketPath() => Path.Combine(Path.GetTempPath(), $"sdv-test-{Guid.NewGuid():N}.sock");

    private static async Task<ScenarioReport> RunSingleAssertionAsync(
        ScenarioAssertion assertion,
        Func<JsonRpcRequest, JsonRpcResponse?> respond,
        CancellationTokenSource cts)
    {
        var ct = cts.Token;
        var socket = SocketPath();
        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    var custom = respond(req);
                    if (custom is not null)
                    {
                        await session.SendResponseAsync(custom, tok);
                        return;
                    }

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":1,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, ct);
        }, ct);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, ct);

        using var client = await UnixSocketRpc.ConnectAsync(socket, ct);
        _ = client.RunAsync(ct);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "single_assertion",
            Assertions = new() { assertion },
        }, ct);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
        return report;
    }

    [Fact]
    public async Task EmptyScenario_Passes()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec { Name = "t" }, cts.Token);

        Assert.True(report.Passed);
        Assert.Empty(report.Failures);
        Assert.Equal(0, report.AssertionsRun);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task CombatAttack_RepeatsAndStripsRunnerOnlyDelay()
    {
        var socket = SocketPath();
        var attackParams = new List<JsonElement>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    if (req.Method == "combat.attack" && req.Params is { } p)
                        attackParams.Add(p.Clone());

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "combat.attack" => JsonDocument.Parse("{\"ok\":true,\"tick\":1}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "combat_attack_repeat",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "combat.attack",
                    Args = JsonDocument.Parse(
                        "{\"x\":20,\"y\":144,\"repeat\":2,\"delay_ticks\":1,\"qualified_item_id\":\"(W)4\"}")
                        .RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);
        Assert.Equal(2, attackParams.Count);
        foreach (var p in attackParams)
        {
            Assert.Equal(20, p.GetProperty("x").GetInt32());
            Assert.Equal(144, p.GetProperty("y").GetInt32());
            Assert.Equal("(W)4", p.GetProperty("qualified_item_id").GetString());
            Assert.False(p.TryGetProperty("repeat", out _));
            Assert.False(p.TryGetProperty("delay_ticks", out _));
        }

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task CombatAttack_TargetSelectorRetargetsNearestMonsterEachRepeat()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var locationPolls = 0;
        var attackTargets = new List<string>();

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    if (req.Method == "combat.attack" && req.Params is { } p)
                    {
                        attackTargets.Add(p.GetRawText());
                    }

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.location" => JsonDocument.Parse(locationPolls++ == 0
                            ? "{\"name\":\"ExampleDeepCave\",\"monsters\":[{\"tile\":{\"x\":12,\"y\":8},\"type\":\"Serpent\",\"health\":245,\"max_health\":245,\"sprite_texture\":\"ExampleMod/Serpent\"}]}"
                            : "{\"name\":\"ExampleDeepCave\",\"monsters\":[{\"tile\":{\"x\":11,\"y\":8},\"type\":\"Serpent\",\"health\":100,\"max_health\":245,\"sprite_texture\":\"ExampleMod/Serpent\"}]}").RootElement,
                        "combat.attack" => JsonDocument.Parse("{\"ok\":true,\"tick\":1}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "combat_attack_retargeting",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "combat.attack",
                    Args = JsonDocument.Parse("{\"qualified_item_id\":\"(W)4\",\"repeat\":2,\"delay_ticks\":0,\"target\":{\"location\":\"ExampleDeepCave\",\"type\":\"Serpent\",\"sprite_texture\":\"ExampleMod/Serpent\"}}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);
        Assert.Equal(2, attackTargets.Count);
        Assert.Contains("\"x\":12", attackTargets[0]);
        Assert.Contains("\"y\":8", attackTargets[0]);
        Assert.Contains("\"x\":11", attackTargets[1]);
        Assert.Contains("\"y\":8", attackTargets[1]);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task CombatAttack_TargetSelectorMatchesMonsterLabelEachRepeat()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var locationPolls = 0;
        var attackTargets = new List<string>();

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    if (req.Method == "combat.attack" && req.Params is { } p)
                    {
                        attackTargets.Add(p.GetRawText());
                    }

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.location" => JsonDocument.Parse(locationPolls++ == 0
                            ? "{\"name\":\"Frobby_CombatLab\",\"monsters\":[{\"monster_id\":\"frobby-monster-2\",\"label\":\"decoy\",\"tile\":{\"x\":99,\"y\":8},\"type\":\"GreenSlime\",\"health\":1},{\"monster_id\":\"frobby-monster-1\",\"label\":\"target\",\"tile\":{\"x\":12,\"y\":8},\"type\":\"GreenSlime\",\"health\":1}]}"
                            : "{\"name\":\"Frobby_CombatLab\",\"monsters\":[{\"monster_id\":\"frobby-monster-2\",\"label\":\"decoy\",\"tile\":{\"x\":98,\"y\":8},\"type\":\"GreenSlime\",\"health\":1},{\"monster_id\":\"frobby-monster-1\",\"label\":\"target\",\"tile\":{\"x\":11,\"y\":8},\"type\":\"GreenSlime\",\"health\":1}]}").RootElement,
                        "combat.attack" => JsonDocument.Parse("{\"ok\":true,\"tick\":1}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "combat_lab_label_retargeting",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "combat.attack",
                    Args = JsonDocument.Parse("{\"qualified_item_id\":\"(W)4\",\"repeat\":2,\"delay_ticks\":0,\"target\":{\"location\":\"Frobby_CombatLab\",\"label\":\"target\"}}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed, string.Join("\n", report.Failures));
        Assert.Equal(2, attackTargets.Count);
        Assert.Contains("\"x\":12", attackTargets[0]);
        Assert.Contains("\"y\":8", attackTargets[0]);
        Assert.Contains("\"x\":11", attackTargets[1]);
        Assert.Contains("\"y\":8", attackTargets[1]);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task CombatAttack_TargetSelectorMatchesMonsterIdEachRepeat()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var locationPolls = 0;
        var attackTargets = new List<string>();

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    if (req.Method == "combat.attack" && req.Params is { } p)
                    {
                        attackTargets.Add(p.GetRawText());
                    }

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.location" => JsonDocument.Parse(locationPolls++ == 0
                            ? "{\"name\":\"Frobby_CombatLab\",\"monsters\":[{\"monster_id\":\"frobby-monster-2\",\"label\":\"target\",\"tile\":{\"x\":99,\"y\":8},\"type\":\"GreenSlime\",\"health\":1},{\"monster_id\":\"frobby-monster-1\",\"label\":\"other\",\"tile\":{\"x\":12,\"y\":8},\"type\":\"GreenSlime\",\"health\":1}]}"
                            : "{\"name\":\"Frobby_CombatLab\",\"monsters\":[{\"monster_id\":\"frobby-monster-2\",\"label\":\"target\",\"tile\":{\"x\":98,\"y\":8},\"type\":\"GreenSlime\",\"health\":1},{\"monster_id\":\"frobby-monster-1\",\"label\":\"other\",\"tile\":{\"x\":11,\"y\":8},\"type\":\"GreenSlime\",\"health\":1}]}").RootElement,
                        "combat.attack" => JsonDocument.Parse("{\"ok\":true,\"tick\":1}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "combat_lab_monster_id_retargeting",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "combat.attack",
                    Args = JsonDocument.Parse("{\"qualified_item_id\":\"(W)4\",\"repeat\":2,\"delay_ticks\":0,\"target\":{\"location\":\"Frobby_CombatLab\",\"monster_id\":\"frobby-monster-1\"}}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed, string.Join("\n", report.Failures));
        Assert.Equal(2, attackTargets.Count);
        Assert.Contains("\"x\":12", attackTargets[0]);
        Assert.Contains("\"y\":8", attackTargets[0]);
        Assert.Contains("\"x\":11", attackTargets[1]);
        Assert.Contains("\"y\":8", attackTargets[1]);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task CombatLabRelocateMonster_PassesThroughAndReportsReadableStep()
    {
        var socket = SocketPath();
        var tmp = Path.Combine(Path.GetTempPath(), $"combat-lab-relocate-report-{Guid.NewGuid():N}");
        var rd = RunDirectory.Create(tmp);
        var calls = new List<string>();
        var relocateParams = default(JsonElement);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    calls.Add(req.Method);
                    if (req.Method == "combat_lab.relocate_monster")
                        relocateParams = req.Params!.Value.Clone();

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "combat_lab.relocate_monster" => JsonDocument.Parse("{\"ok\":true,\"monster_id\":\"frobby-monster-1\",\"label\":\"corrupt-mummy\",\"from_location\":\"Custom_CrimsonBadlands\",\"source_tile\":{\"x\":20,\"y\":144},\"location\":\"Frobby_CombatLab\",\"tile\":{\"x\":9,\"y\":8},\"name\":\"Mummy\",\"type\":\"Mummy\",\"sprite_texture\":\"Characters/Monsters/CorruptMummy\",\"health\":2000,\"max_health\":2000}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        try
        {
            for (int i = 0; i < 40 && !File.Exists(socket); i++)
                await Task.Delay(50, cts.Token);

            using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
            _ = client.RunAsync(cts.Token);

            var runner = new ScenarioRunner(client, updateBaselines: false, reportDir: rd);
            var report = await runner.RunAsync(new ScenarioSpec
            {
                Name = "combat_lab_relocate_report",
                Steps = new()
                {
                    new ScenarioStep
                    {
                        Action = "combat_lab.relocate_monster",
                        Args = JsonDocument.Parse("{\"from_location\":\"Custom_CrimsonBadlands\",\"label\":\"corrupt-mummy\",\"target_x\":9,\"target_y\":8,\"match\":{\"sprite_texture\":\"Characters/Monsters/CorruptMummy\"}}").RootElement,
                    },
                },
            }, cts.Token);

            Assert.True(report.Passed, string.Join("\n", report.Failures));
            Assert.Contains("combat_lab.relocate_monster", calls);
            Assert.Equal("Custom_CrimsonBadlands", relocateParams.GetProperty("from_location").GetString());
            Assert.Equal("corrupt-mummy", relocateParams.GetProperty("label").GetString());
            Assert.Equal("Relocate monster from Custom_CrimsonBadlands to Combat Lab", report.Steps[0].Detail);
        }
        finally
        {
            cts.Cancel();
            try { await serverTask; } catch (OperationCanceledException) { }
            Directory.Delete(rd.Root, recursive: true);
        }
    }

    [Fact]
    public async Task WorldExplodeTile_PassesThroughAndReportsReadableStep()
    {
        var socket = SocketPath();
        var tmp = Path.Combine(Path.GetTempPath(), $"explode-tile-report-{Guid.NewGuid():N}");
        var rd = RunDirectory.Create(tmp);
        var calls = new List<string>();
        var explodeParams = default(JsonElement);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    calls.Add(req.Method);
                    if (req.Method == "world.explode_tile")
                        explodeParams = req.Params!.Value.Clone();

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "world.explode_tile" => JsonDocument.Parse("{\"ok\":true,\"tick\":123,\"location\":\"Frobby_CombatLab\",\"tile\":{\"x\":9,\"y\":8},\"radius\":2,\"damage_player\":false,\"monsters_before\":1,\"monsters_after\":0,\"debris_before\":0,\"debris_after\":1,\"invoked\":true}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        try
        {
            for (int i = 0; i < 40 && !File.Exists(socket); i++)
                await Task.Delay(50, cts.Token);

            using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
            _ = client.RunAsync(cts.Token);

            var runner = new ScenarioRunner(client, updateBaselines: false, reportDir: rd);
            var report = await runner.RunAsync(new ScenarioSpec
            {
                Name = "explode_tile_report",
                Steps = new()
                {
                    new ScenarioStep
                    {
                        Action = "world.explode_tile",
                        Args = JsonDocument.Parse("{\"location\":\"Frobby_CombatLab\",\"x\":9,\"y\":8,\"radius\":2,\"damage_player\":false}").RootElement,
                    },
                },
            }, cts.Token);

            Assert.True(report.Passed, string.Join("\n", report.Failures));
            Assert.Contains("world.explode_tile", calls);
            Assert.Equal("Frobby_CombatLab", explodeParams.GetProperty("location").GetString());
            Assert.Equal(9, explodeParams.GetProperty("x").GetInt32());
            Assert.Equal(8, explodeParams.GetProperty("y").GetInt32());
            Assert.Equal("Explode tile Frobby_CombatLab (9,8) radius 2", report.Steps[0].Detail);
        }
        finally
        {
            cts.Cancel();
            try { await serverTask; } catch (OperationCanceledException) { }
            Directory.Delete(rd.Root, recursive: true);
        }
    }

    [Fact]
    public async Task WorldPlaceInventoryObject_PassesThroughAndReportsReadableStep()
    {
        var socket = SocketPath();
        var tmp = Path.Combine(Path.GetTempPath(), $"place-inventory-object-report-{Guid.NewGuid():N}");
        var rd = RunDirectory.Create(tmp);
        var calls = new List<string>();
        var placeParams = default(JsonElement);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    calls.Add(req.Method);
                    if (req.Method == "world.place_inventory_object")
                        placeParams = req.Params!.Value.Clone();

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "world.place_inventory_object" => JsonDocument.Parse("{\"ok\":true,\"tick\":123,\"id\":\"287\",\"qualified_id\":\"(O)287\",\"name\":\"Bomb\",\"location\":\"Frobby_CombatLab\",\"tile\":{\"x\":9,\"y\":8},\"source_slot\":12,\"stack_before\":2,\"stack_after\":1,\"runtime_type\":\"Object\",\"placed\":true}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        try
        {
            for (int i = 0; i < 40 && !File.Exists(socket); i++)
                await Task.Delay(50, cts.Token);

            using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
            _ = client.RunAsync(cts.Token);

            var runner = new ScenarioRunner(client, updateBaselines: false, reportDir: rd);
            var report = await runner.RunAsync(new ScenarioSpec
            {
                Name = "place_inventory_object_report",
                Steps = new()
                {
                    new ScenarioStep
                    {
                        Action = "world.place_inventory_object",
                        Args = JsonDocument.Parse("{\"id\":\"(O)287\",\"location\":\"Frobby_CombatLab\",\"x\":9,\"y\":8}").RootElement,
                    },
                },
            }, cts.Token);

            Assert.True(report.Passed, string.Join("\n", report.Failures));
            Assert.Contains("world.place_inventory_object", calls);
            Assert.Equal("(O)287", placeParams.GetProperty("id").GetString());
            Assert.Equal("Frobby_CombatLab", placeParams.GetProperty("location").GetString());
            Assert.Equal(9, placeParams.GetProperty("x").GetInt32());
            Assert.Equal(8, placeParams.GetProperty("y").GetInt32());
            Assert.Equal("Place inventory object (O)287 at Frobby_CombatLab (9,8)", report.Steps[0].Detail);
        }
        finally
        {
            cts.Cancel();
            try { await serverTask; } catch (OperationCanceledException) { }
            Directory.Delete(rd.Root, recursive: true);
        }
    }

    [Fact]
    public async Task InputClickTile_PassesThroughAndReportsReadableStep()
    {
        var socket = SocketPath();
        var tmp = Path.Combine(Path.GetTempPath(), $"click-tile-report-{Guid.NewGuid():N}");
        var rd = RunDirectory.Create(tmp);
        var calls = new List<string>();
        var clickParams = default(JsonElement);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    calls.Add(req.Method);
                    if (req.Method == "input.click_tile")
                        clickParams = req.Params!.Value.Clone();

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "input.click_tile" => JsonDocument.Parse("{\"ok\":true,\"tick\":123,\"location\":\"Frobby_CombatLab\",\"tile\":{\"x\":9,\"y\":9},\"screen\":{\"x\":576,\"y\":576},\"world\":{\"x\":608,\"y\":608},\"selected_item\":{\"slot\":1,\"id\":\"(O)287\",\"item_id\":\"287\",\"qualified_id\":\"(O)287\",\"name\":\"Bomb\",\"stack\":1,\"runtime_type\":\"Object\"},\"handled\":true,\"target_npc_name\":\"Claire\",\"npc_fallback_used\":true}").RootElement,
                        "bitmap.capture" => JsonDocument.Parse("{\"path\":\"/tmp/click-tile.png\",\"width\":1280,\"height\":720}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        try
        {
            for (int i = 0; i < 40 && !File.Exists(socket); i++)
                await Task.Delay(50, cts.Token);

            using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
            _ = client.RunAsync(cts.Token);

            var runner = new ScenarioRunner(client, updateBaselines: false, reportDir: rd);
            var report = await runner.RunAsync(new ScenarioSpec
            {
                Name = "click_tile_report",
                Steps = new()
                {
                    new ScenarioStep
                    {
                        Action = "input.click_tile",
                        Args = JsonDocument.Parse("{\"location\":\"Frobby_CombatLab\",\"x\":9,\"y\":9}").RootElement,
                    },
                },
            }, cts.Token);

            Assert.True(report.Passed, string.Join("\n", report.Failures));
            Assert.Contains("input.click_tile", calls);
            Assert.Equal("Frobby_CombatLab", clickParams.GetProperty("location").GetString());
            Assert.Equal(9, clickParams.GetProperty("x").GetInt32());
            Assert.Equal(9, clickParams.GetProperty("y").GetInt32());
            Assert.Equal("Click left tile Frobby_CombatLab (9,9) handled=true target_npc=Claire npc_fallback=true selected_item=(O)287 Bomb", report.Steps[0].Detail);
            Assert.Contains("bitmap.capture", calls);
        }
        finally
        {
            cts.Cancel();
            try { await serverTask; } catch (OperationCanceledException) { }
            Directory.Delete(rd.Root, recursive: true);
        }
    }

    [Fact]
    public async Task InputClickTile_ReportsResolvedActionDiagnostics()
    {
        var socket = SocketPath();
        var tmp = Path.Combine(Path.GetTempPath(), $"click-action-details-{Guid.NewGuid():N}");
        var rd = RunDirectory.Create(tmp);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "input.click_tile" => JsonDocument.Parse("{\"ok\":true,\"tick\":5,\"location\":\"MovieTheater\",\"tile\":{\"x\":4,\"y\":13},\"screen\":{\"x\":288,\"y\":544},\"world\":{\"x\":288,\"y\":864},\"handled\":false,\"resolved_action_value\":\"Theater_Doors\",\"resolved_action_layer\":\"Buildings\",\"resolved_action_property\":\"Action\",\"resolved_action_tile\":{\"x\":4,\"y\":13},\"screen_visible\":false}").RootElement,
                        "bitmap.capture" => JsonDocument.Parse("{\"path\":\"/tmp/click-action-details.png\",\"width\":1280,\"height\":720}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        try
        {
            for (int i = 0; i < 40 && !File.Exists(socket); i++)
                await Task.Delay(50, cts.Token);

            using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
            _ = client.RunAsync(cts.Token);

            var runner = new ScenarioRunner(client, updateBaselines: false, reportDir: rd);
            var report = await runner.RunAsync(new ScenarioSpec
            {
                Name = "click_action_details",
                Steps = new()
                {
                    new ScenarioStep
                    {
                        Action = "input.click_tile",
                        Args = JsonDocument.Parse("{\"location\":\"MovieTheater\",\"x\":5,\"y\":14,\"button\":\"right\",\"action_value\":\"Theater_Doors\",\"radius\":8}").RootElement,
                    },
                },
            }, cts.Token);

            Assert.True(report.Passed, string.Join("\n", report.Failures));
            Assert.Contains("resolved_action=Theater_Doors@4,13 Buildings/Action", report.Steps[0].Detail);
            Assert.Contains("screen_visible=false", report.Steps[0].Detail);
        }
        finally
        {
            cts.Cancel();
            try { await serverTask; } catch (OperationCanceledException) { }
            Directory.Delete(rd.Root, recursive: true);
        }
    }

    [Fact]
    public async Task InputClickEventActor_ResolvesActorTileThenClicksTile()
    {
        var socket = SocketPath();
        var tmp = Path.Combine(Path.GetTempPath(), $"click-event-actor-report-{Guid.NewGuid():N}");
        var rd = RunDirectory.Create(tmp);
        var calls = new List<string>();
        var clickParams = default(JsonElement);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    calls.Add(req.Method);
                    if (req.Method == "input.click_tile")
                        clickParams = req.Params!.Value.Clone();

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.event" => JsonDocument.Parse("{\"active\":true,\"event_up\":true,\"location\":\"Town\",\"id\":\"fall16\",\"is_festival\":true,\"actors\":[{\"name\":\"Lewis\",\"tile\":{\"x\":42,\"y\":57},\"pixel\":{\"x\":2688,\"y\":3648},\"facing_direction\":2,\"current_frame\":0}],\"dialogue\":null,\"viewport\":{\"x\":0,\"y\":0,\"width\":1280,\"height\":720}}").RootElement,
                        "input.click_tile" => JsonDocument.Parse("{\"ok\":true,\"tick\":123,\"location\":\"Town\",\"tile\":{\"x\":42,\"y\":57},\"screen\":{\"x\":2720,\"y\":3680},\"world\":{\"x\":2720,\"y\":3680},\"handled\":true}").RootElement,
                        "bitmap.capture" => JsonDocument.Parse("{\"path\":\"/tmp/click-event-actor.png\",\"width\":1280,\"height\":720}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        try
        {
            for (int i = 0; i < 40 && !File.Exists(socket); i++)
                await Task.Delay(50, cts.Token);

            using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
            _ = client.RunAsync(cts.Token);

            var runner = new ScenarioRunner(client, updateBaselines: false, reportDir: rd);
            var report = await runner.RunAsync(new ScenarioSpec
            {
                Name = "click_event_actor_report",
                Steps = new()
                {
                    new ScenarioStep
                    {
                        Action = "input.click_event_actor",
                        Args = JsonDocument.Parse("{\"actor_name\":\"lewis\",\"location\":\"   \"}").RootElement,
                    },
                },
            }, cts.Token);

            Assert.True(report.Passed, string.Join("\n", report.Failures));
            Assert.Contains("state.event", calls);
            Assert.Contains("input.click_tile", calls);
            Assert.Equal("Town", clickParams.GetProperty("location").GetString());
            Assert.Equal(42, clickParams.GetProperty("x").GetInt32());
            Assert.Equal(57, clickParams.GetProperty("y").GetInt32());
            Assert.Equal("left", clickParams.GetProperty("button").GetString());
            Assert.True(clickParams.GetProperty("allow_event_input").GetBoolean());
            Assert.Equal(32, clickParams.GetProperty("screen_offset_x").GetInt32());
            Assert.Equal(32, clickParams.GetProperty("screen_offset_y").GetInt32());
            Assert.Equal("Click event actor Lewis", report.Steps[0].Detail);
            Assert.Contains("bitmap.capture", calls);
        }
        finally
        {
            cts.Cancel();
            try { await serverTask; } catch (OperationCanceledException) { }
            Directory.Delete(rd.Root, recursive: true);
        }
    }

    [Fact]
    public async Task InputClickEventActor_WaitsForFadeBeforeClickingTile()
    {
        var socket = SocketPath();
        var tmp = Path.Combine(Path.GetTempPath(), $"click-event-actor-fade-report-{Guid.NewGuid():N}");
        var rd = RunDirectory.Create(tmp);
        var calls = new List<string>();
        var statusCalls = 0;
        var clickParams = default(JsonElement);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    calls.Add(req.Method);
                    if (req.Method == "input.click_tile")
                        clickParams = req.Params!.Value.Clone();

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.event" => JsonDocument.Parse("{\"active\":true,\"event_up\":true,\"location\":\"Temp\",\"id\":\"festival_fall16\",\"is_festival\":true,\"actors\":[{\"name\":\"Lewis\",\"tile\":{\"x\":25,\"y\":53},\"pixel\":{\"x\":1600,\"y\":3392},\"facing_direction\":2,\"current_frame\":0}],\"dialogue\":null,\"viewport\":{\"x\":0,\"y\":0,\"width\":1280,\"height\":720}}").RootElement,
                        "freeze.status" => JsonDocument.Parse(++statusCalls == 1
                            ? "{\"frozen\":false,\"is_warping\":false,\"is_fading\":true,\"tick\":1}"
                            : "{\"frozen\":false,\"is_warping\":false,\"is_fading\":false,\"tick\":2}").RootElement,
                        "input.click_tile" => JsonDocument.Parse("{\"ok\":true,\"tick\":123,\"location\":\"Temp\",\"tile\":{\"x\":25,\"y\":53},\"screen\":{\"x\":1632,\"y\":3424},\"world\":{\"x\":1632,\"y\":3424},\"handled\":true}").RootElement,
                        "bitmap.capture" => JsonDocument.Parse("{\"path\":\"/tmp/click-event-actor-fade.png\",\"width\":1280,\"height\":720}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        try
        {
            for (int i = 0; i < 40 && !File.Exists(socket); i++)
                await Task.Delay(50, cts.Token);

            using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
            _ = client.RunAsync(cts.Token);

            var runner = new ScenarioRunner(client, updateBaselines: false, reportDir: rd);
            var report = await runner.RunAsync(new ScenarioSpec
            {
                Name = "click_event_actor_fade",
                Steps = new()
                {
                    new ScenarioStep
                    {
                        Action = "input.click_event_actor",
                        Args = JsonDocument.Parse("{\"actor_name\":\"Lewis\",\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
                    },
                },
            }, cts.Token);

            Assert.True(report.Passed, string.Join("\n", report.Failures));
            Assert.Equal(2, statusCalls);
            Assert.Contains("input.click_tile", calls);
            Assert.Equal("Temp", clickParams.GetProperty("location").GetString());
            Assert.Equal(25, clickParams.GetProperty("x").GetInt32());
            Assert.Equal(53, clickParams.GetProperty("y").GetInt32());
        }
        finally
        {
            cts.Cancel();
            try { await serverTask; } catch (OperationCanceledException) { }
            Directory.Delete(rd.Root, recursive: true);
        }
    }

    [Fact]
    public async Task ShopClickPurchase_PassesThroughAndReportsReadableStep()
    {
        var socket = SocketPath();
        var tmp = Path.Combine(Path.GetTempPath(), $"shop-click-purchase-report-{Guid.NewGuid():N}");
        var rd = RunDirectory.Create(tmp);
        var calls = new List<string>();
        var clickParams = new List<JsonElement>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    calls.Add(req.Method);
                    if (req.Method == "shop.click_purchase")
                        clickParams.Add(req.Params!.Value.Clone());

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "shop.click_purchase" => JsonDocument.Parse("{\"ok\":true,\"tick\":45,\"shop_id\":\"Carpenter\",\"item_id\":\"(F)terminal\",\"display_name\":\"Terminal\",\"count\":1,\"unit_price\":25000,\"currency\":0,\"previous_currency_balance\":30000,\"currency_balance\":5000,\"previous_money\":30000,\"money\":5000,\"screen\":{\"x\":860,\"y\":420},\"bounds\":{\"x\":500,\"y\":380,\"width\":720,\"height\":80},\"visible_index\":1,\"item_index\":2,\"scrolled\":true,\"held_item_deposited\":true}").RootElement,
                        "bitmap.capture" => JsonDocument.Parse("{\"path\":\"/tmp/shop-click-purchase.png\",\"width\":1280,\"height\":720}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        try
        {
            for (int i = 0; i < 40 && !File.Exists(socket); i++)
                await Task.Delay(50, cts.Token);

            using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
            _ = client.RunAsync(cts.Token);

            var runner = new ScenarioRunner(client, updateBaselines: false, reportDir: rd);
            var report = await runner.RunAsync(new ScenarioSpec
            {
                Name = "shop_click_purchase_report",
                Steps = new()
                {
                    new ScenarioStep
                    {
                        Action = "shop.click_purchase",
                        Args = JsonDocument.Parse("{\"item_id\":\"(F)terminal\"}").RootElement,
                    },
                    new ScenarioStep
                    {
                        Action = "shop.click_purchase",
                        Args = JsonDocument.Parse("{\"item_index\":0}").RootElement,
                    },
                },
            }, cts.Token);

            Assert.True(report.Passed, string.Join("\n", report.Failures));
            Assert.Contains("shop.click_purchase", calls);
            Assert.Equal(2, clickParams.Count);
            Assert.Equal("(F)terminal", clickParams[0].GetProperty("item_id").GetString());
            Assert.Equal(0, clickParams[1].GetProperty("item_index").GetInt32());
            Assert.Equal("Click purchase shop item \"(F)terminal\"", report.Steps[0].Detail);
            Assert.Equal("Click purchase shop item \"item_index 0\"", report.Steps[1].Detail);
            Assert.Contains("bitmap.capture", calls);
        }
        finally
        {
            cts.Cancel();
            try { await serverTask; } catch (OperationCanceledException) { }
            Directory.Delete(rd.Root, recursive: true);
        }
    }

    [Fact]
    public async Task FixtureLoad_WaitsForWarpToSettleBeforeFirstStep()
    {
        var socket = SocketPath();
        var calls = new List<string>();
        var statusCalls = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    calls.Add(req.Method);
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "fixture.load" => JsonDocument.Parse("{\"ok\":true,\"tick\":1}").RootElement,
                        "state.player" => JsonDocument.Parse("{\"name\":\"Tester\",\"location\":\"FarmHouse\"}").RootElement,
                        "freeze.status" when ++statusCalls < 3 => JsonDocument.Parse("{\"frozen\":false,\"tick\":2,\"is_warping\":true}").RootElement,
                        "freeze.status" => JsonDocument.Parse("{\"frozen\":false,\"tick\":4,\"is_warping\":false}").RootElement,
                        "player.set_money" => JsonDocument.Parse("{\"ok\":true,\"tick\":5}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "fixture_waits_for_warp",
            Fixture = "m0spike_436515781",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "player.set_money",
                    Args = JsonDocument.Parse("{\"amount\":1000}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);
        Assert.True(statusCalls >= 3);
        Assert.True(calls.IndexOf("freeze.status") < calls.IndexOf("player.set_money"));

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Theory]
    [InlineData("wait.ms", false)]
    [InlineData("draw.arm", false)]
    [InlineData("state.assert", false)]
    [InlineData("ui.wait_text", false)]
    [InlineData("wait.location_content", false)]
    [InlineData("wait.visual_effects", false)]
    [InlineData("ui.click_text", true)]
    [InlineData("ui.hover_text", true)]
    [InlineData("input.click_text", true)]
    [InlineData("input.click_tile", true)]
    [InlineData("input.click_event_actor", true)]
    [InlineData("input.hover_text", true)]
    [InlineData("freeze.begin", true)]
    public void ShouldAutoCaptureStep_SkipsTimingAndInstrumentationSteps(string action, bool expected)
    {
        var step = new ScenarioStep { Action = action };

        Assert.Equal(expected, ScenarioRunner.ShouldAutoCaptureStep(step));
    }

    [Fact]
    public void ShouldAutoCaptureStep_CanBeDisabledPerStep()
    {
        var step = new ScenarioStep
        {
            Action = "input.click",
            Args = JsonDocument.Parse("{\"x\":1156,\"y\":143,\"auto_screenshot\":false}").RootElement,
        };

        Assert.False(ScenarioRunner.ShouldAutoCaptureStep(step));
    }

    [Fact]
    public async Task WaitSpecialOrder_PollsUntilActiveOrderObjectiveMatches()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var polls = 0;

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.special_orders" => JsonDocument.Parse(polls++ == 0
                            ? "{\"active\":[],\"available\":[],\"completed\":[],\"accepted_types\":[],\"returned_donations\":[]}"
                            : "{\"active\":[{\"key\":\"Andy\",\"requester\":\"Andy\",\"order_type\":\"StardewValleyExpanded\",\"state\":\"InProgress\",\"objectives\":[{\"index\":0,\"type\":\"Donate\",\"runtime_type\":\"DonateObjective\",\"drop_box\":\"AndyChest\",\"drop_box_location\":\"Custom_AndyHouse\",\"accepted_context_tags\":[\"item_wood\"],\"current_count\":25,\"max_count\":500,\"complete\":false}],\"rewards\":[],\"donated_items\":[]}],\"available\":[],\"completed\":[],\"accepted_types\":[],\"returned_donations\":[]}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_special_order",
            Steps =
            {
                new ScenarioStep
                {
                    Action = "wait.special_order",
                    Args = JsonDocument.Parse("{\"collection\":\"active\",\"key\":\"Andy\",\"requester\":\"Andy\",\"objective_type\":\"Donate\",\"drop_box\":\"AndyChest\",\"accepted_context_tag\":\"item_wood\",\"current_count_gte\":25,\"timeout_ms\":1000,\"poll_ms\":10}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);
        Assert.True(polls >= 2);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitSpecialOrder_TimeoutReportsObservedKeys()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.special_orders" => JsonDocument.Parse("{\"active\":[{\"key\":\"MarlonFay2\",\"objectives\":[],\"rewards\":[],\"donated_items\":[]}],\"available\":[{\"key\":\"Andy\",\"objectives\":[],\"rewards\":[],\"donated_items\":[]}],\"completed\":[\"Emily\"],\"accepted_types\":[],\"returned_donations\":[]}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_special_order_timeout",
            Steps =
            {
                new ScenarioStep
                {
                    Action = "wait.special_order",
                    Args = JsonDocument.Parse("{\"collection\":\"active\",\"key\":\"Missing\",\"timeout_ms\":50,\"poll_ms\":10}").RootElement,
                },
            },
        }, cts.Token);

        Assert.False(report.Passed);
        Assert.Contains("active=[MarlonFay2]", report.Failures[0]);
        Assert.Contains("available=[Andy]", report.Failures[0]);
        Assert.Contains("completed=[Emily]", report.Failures[0]);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task UiWaitText_PollsTextCaptureUntilMatch()
    {
        var socket = SocketPath();
        var calls = new List<string>();
        var findCalls = 0;
        var armParams = default(JsonElement);
        var findParams = default(JsonElement);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    calls.Add(req.Method);
                    if (req.Method == "draw.arm" && req.Params is { } arm)
                        armParams = arm.Clone();
                    if (req.Method == "draw.text_find" && req.Params is { } find)
                    {
                        findParams = find.Clone();
                        findCalls++;
                    }

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "draw.arm" => JsonDocument.Parse("{\"ok\":true,\"tick\":1}").RootElement,
                        "draw.text_find" when findCalls < 2 => JsonDocument.Parse("{\"events\":[],\"count\":0}").RootElement,
                        "draw.text_find" => JsonDocument.Parse("{\"events\":[{},{}],\"count\":2}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "ui_wait_text",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "ui.wait_text",
                    Args = JsonDocument.Parse("{\"text_matches\":\"^SUBMIT [A-Z]+$\",\"case_sensitive\":false,\"timeout_ms\":1000,\"poll_ms\":1,\"capture_ticks\":3,\"bounds_intersects_rect\":[700,500,120,40]}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);
        Assert.Equal(2, findCalls);
        Assert.Contains("draw.arm", calls);
        Assert.DoesNotContain("input.click_text", calls);
        Assert.Equal(3, armParams.GetProperty("ticks").GetInt32());
        Assert.Equal("^SUBMIT [A-Z]+$", findParams.GetProperty("text_matches").GetString());
        Assert.False(findParams.GetProperty("case_sensitive").GetBoolean());
        Assert.Equal(700, findParams.GetProperty("bounds_intersects_rect")[0].GetInt32());

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitLocation_PollsStatePlayerUntilLocationMatches()
    {
        var socket = SocketPath();
        var calls = new List<string>();
        var playerPolls = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    calls.Add(req.Method);
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.player" => JsonDocument.Parse(playerPolls++ == 0
                            ? "{\"name\":\"Tester\",\"money\":500,\"stamina\":270,\"max_stamina\":270,\"health\":100,\"location\":\"Farm\",\"tile\":{\"x\":64,\"y\":15},\"items\":[]}"
                            : "{\"name\":\"Tester\",\"money\":500,\"stamina\":270,\"max_stamina\":270,\"health\":100,\"location\":\"Custom_TownEast\",\"tile\":{\"x\":10,\"y\":20},\"items\":[]}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_location",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.location",
                    Args = JsonDocument.Parse("{\"location\":\"Custom_TownEast\",\"x\":10,\"y\":20,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);
        Assert.Equal(2, playerPolls);
        Assert.DoesNotContain("wait.location", calls);
        Assert.Contains("state.player", calls);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitPlayer_PollsStatePlayerUntilHealthMatches()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var playerPolls = 0;

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.player" => JsonDocument.Parse(playerPolls++ == 0
                            ? "{\"name\":\"Tester\",\"health\":100,\"location\":\"ExampleDeepCave\",\"tile\":{\"x\":10,\"y\":20}}"
                            : "{\"name\":\"Tester\",\"health\":82,\"location\":\"ExampleDeepCave\",\"tile\":{\"x\":10,\"y\":20}}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_player_health",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.player",
                    Args = JsonDocument.Parse("{\"location\":\"ExampleDeepCave\",\"x\":10,\"y\":20,\"health_lt\":100,\"health_gte\":80,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);
        Assert.True(playerPolls >= 2);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitPlayer_PollsStatePlayerUntilMovementStateMatches()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var playerPolls = 0;

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.player" => JsonDocument.Parse(playerPolls++ == 0
                            ? "{\"name\":\"Tester\",\"health\":100,\"location\":\"ExampleDeepCave\",\"tile\":{\"x\":10,\"y\":20},\"can_move\":false,\"is_busy\":true}"
                            : "{\"name\":\"Tester\",\"health\":100,\"location\":\"ExampleDeepCave\",\"tile\":{\"x\":10,\"y\":20},\"can_move\":true,\"is_busy\":false}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_player_movement",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.player",
                    Args = JsonDocument.Parse("{\"can_move\":true,\"is_busy\":false,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);
        Assert.True(playerPolls >= 2);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitPlayer_TimeoutIncludesLastObservedHealth()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.player" => JsonDocument.Parse("{\"name\":\"Tester\",\"health\":100,\"location\":\"Farm\",\"tile\":{\"x\":64,\"y\":15}}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_player_timeout",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.player",
                    Args = JsonDocument.Parse("{\"health_lt\":90,\"timeout_ms\":20,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.False(report.Passed);
        var failure = Assert.Single(report.Failures);
        Assert.Contains("wait.player timed out after 20ms waiting for player matching health_lt=90", failure);
        Assert.Contains("last observed health=100 location=Farm tile=64,15", failure);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitPlayer_MatchesTransientStateAndBuffEffects()
    {
        var socket = SocketPath();
        var playerPolls = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.player" => JsonDocument.Parse(playerPolls++ == 0
                            ? "{\"name\":\"Tester\",\"health\":100,\"location\":\"Custom_SpriteSpring2\",\"tile\":{\"x\":10,\"y\":20},\"swimming\":true,\"bathing_clothes\":false,\"buffs\":[]}"
                            : "{\"name\":\"Tester\",\"health\":100,\"location\":\"Custom_SpriteSpring2\",\"tile\":{\"x\":10,\"y\":20},\"swimming\":true,\"bathing_clothes\":false,\"buffs\":[{\"id\":\"1\",\"display_name\":\"Fishing\",\"effects\":{\"fishing_level\":3},\"milliseconds_duration\":720000}]}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_player_buff",
            Steps =
            {
                new ScenarioStep
                {
                    Action = "wait.player",
                    Args = JsonDocument.Parse("{\"location\":\"Custom_SpriteSpring2\",\"swimming\":true,\"buff_count_gte\":1,\"buff_any_effect_gte\":{\"effects\":[\"fishing_level\",\"farming_level\",\"mining_level\",\"foraging_level\",\"attack\"],\"value\":3},\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);
        Assert.True(playerPolls >= 2);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitPlayer_MatchesProgressionLists()
    {
        var socket = SocketPath();
        var playerPolls = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.player" => JsonDocument.Parse(playerPolls++ == 0
                            ? "{\"name\":\"Tester\",\"health\":100,\"location\":\"Farm\",\"tile\":{\"x\":64,\"y\":15},\"mail_received\":[],\"mail_for_tomorrow\":[],\"events_seen\":[]}"
                            : "{\"name\":\"Tester\",\"health\":100,\"location\":\"Farm\",\"tile\":{\"x\":64,\"y\":15},\"mail_received\":[\"ShedRepaired\"],\"mail_for_tomorrow\":[\"HenchmanMarshTonics\"],\"events_seen\":[\"1000035\"]}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_player_progression",
            Steps =
            {
                new ScenarioStep
                {
                    Action = "wait.player",
                    Args = JsonDocument.Parse("{\"mail_received\":\"ShedRepaired\",\"mail_for_tomorrow\":\"HenchmanMarshTonics\",\"event_seen\":\"1000035\",\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);
        Assert.True(playerPolls >= 2);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitPlayer_MatchesSecretNoteSeen()
    {
        var socket = SocketPath();
        var playerPolls = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.player" => JsonDocument.Parse(playerPolls++ == 0
                            ? "{\"name\":\"Tester\",\"health\":100,\"location\":\"Farm\",\"tile\":{\"x\":64,\"y\":15},\"secret_notes_seen\":[]}"
                            : "{\"name\":\"Tester\",\"health\":100,\"location\":\"Farm\",\"tile\":{\"x\":64,\"y\":15},\"secret_notes_seen\":[18]}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_player_secret_note",
            Steps =
            {
                new ScenarioStep
                {
                    Action = "wait.player",
                    Args = JsonDocument.Parse("{\"secret_note_seen\":18,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);
        Assert.True(playerPolls >= 2);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitPlayer_TimeoutIncludesTransientAndBuffSummary()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.player" => JsonDocument.Parse("{\"name\":\"Tester\",\"health\":100,\"location\":\"Farm\",\"tile\":{\"x\":64,\"y\":15},\"swimming\":false,\"bathing_clothes\":false,\"buffs\":[]}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_player_buff_timeout",
            Steps =
            {
                new ScenarioStep
                {
                    Action = "wait.player",
                    Args = JsonDocument.Parse("{\"swimming\":true,\"buff_count_gte\":1,\"timeout_ms\":20,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.False(report.Passed);
        var failure = Assert.Single(report.Failures);
        Assert.Contains("swimming=true", failure);
        Assert.Contains("last observed health=100 location=Farm tile=64,15 swimming=false bathing_clothes=false can_move=? is_busy=? buffs=0", failure);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitPlayer_TimeoutIncludesSecretNoteSummary()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.player" => JsonDocument.Parse("{\"name\":\"Tester\",\"health\":100,\"location\":\"Farm\",\"tile\":{\"x\":64,\"y\":15},\"mail_received\":[],\"mail_for_tomorrow\":[],\"events_seen\":[],\"secret_notes_seen\":[12]}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_player_secret_note_timeout",
            Steps =
            {
                new ScenarioStep
                {
                    Action = "wait.player",
                    Args = JsonDocument.Parse("{\"secret_note_seen\":18,\"timeout_ms\":20,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.False(report.Passed);
        var failure = Assert.Single(report.Failures);
        Assert.Contains("secret_notes_seen contains 18", failure);
        Assert.Contains("secret_notes_seen=1", failure);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitPlayer_TimeoutIncludesProgressionSummary()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.player" => JsonDocument.Parse("{\"name\":\"Tester\",\"health\":100,\"location\":\"Farm\",\"tile\":{\"x\":64,\"y\":15},\"mail_received\":[\"ShedRepaired\"],\"mail_for_tomorrow\":[],\"events_seen\":[\"418172\"]}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_player_progression_timeout",
            Steps =
            {
                new ScenarioStep
                {
                    Action = "wait.player",
                    Args = JsonDocument.Parse("{\"mail_for_tomorrow\":\"HenchmanMarshTonics\",\"timeout_ms\":20,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.False(report.Passed);
        var failure = Assert.Single(report.Failures);
        Assert.Contains("mail_for_tomorrow contains HenchmanMarshTonics", failure);
        Assert.Contains("mail_received=1 mail_for_tomorrow=0 events_seen=1", failure);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitLocation_WaitsForWarpToSettleAfterLocationMatches()
    {
        var socket = SocketPath();
        var playerPolls = 0;
        var warpPolls = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.player" => JsonDocument.Parse("{\"name\":\"Tester\",\"money\":500,\"stamina\":270,\"max_stamina\":270,\"health\":100,\"location\":\"Custom_TownEast\",\"tile\":{\"x\":10,\"y\":20},\"items\":[]}").RootElement,
                        "freeze.status" => JsonDocument.Parse(warpPolls++ == 0
                            ? "{\"frozen\":false,\"is_warping\":true,\"tick\":1}"
                            : "{\"frozen\":false,\"is_warping\":false,\"tick\":2}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };

                    if (req.Method == "state.player")
                        playerPolls++;

                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_location_warp",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.location",
                    Args = JsonDocument.Parse("{\"location\":\"Custom_TownEast\",\"x\":10,\"y\":20,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);
        Assert.Equal(2, playerPolls);
        Assert.Equal(2, warpPolls);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitLocation_WaitsForFadeToClearAfterLocationMatches()
    {
        var socket = SocketPath();
        var playerPolls = 0;
        var statusPolls = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.player" => JsonDocument.Parse("{\"name\":\"Tester\",\"money\":500,\"stamina\":270,\"max_stamina\":270,\"health\":100,\"location\":\"Custom_TownEast\",\"tile\":{\"x\":10,\"y\":20},\"items\":[]}").RootElement,
                        "freeze.status" => JsonDocument.Parse(statusPolls++ == 0
                            ? "{\"frozen\":false,\"is_warping\":false,\"is_fading\":true,\"tick\":1}"
                            : "{\"frozen\":false,\"is_warping\":false,\"is_fading\":false,\"tick\":2}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };

                    if (req.Method == "state.player")
                        playerPolls++;

                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_location_fade",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.location",
                    Args = JsonDocument.Parse("{\"location\":\"Custom_TownEast\",\"x\":10,\"y\":20,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);
        Assert.Equal(2, playerPolls);
        Assert.Equal(2, statusPolls);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitLocation_TimeoutIncludesLastObservedLocation()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.player" => JsonDocument.Parse("{\"name\":\"Tester\",\"money\":500,\"stamina\":270,\"max_stamina\":270,\"health\":100,\"location\":\"Farm\",\"tile\":{\"x\":64,\"y\":15},\"items\":[]}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_location_timeout",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.location",
                    Args = JsonDocument.Parse("{\"location\":\"Custom_TownEast\",\"timeout_ms\":20,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.False(report.Passed);
        var failure = Assert.Single(report.Failures);
        Assert.Contains("wait.location timed out after 20ms waiting for location Custom_TownEast", failure);
        Assert.Contains("last observed Farm at 64,15", failure);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitNpcLocation_PollsStateNpcUntilLocationMatches()
    {
        var socket = SocketPath();
        var calls = new List<string>();
        var npcPolls = 0;
        JsonElement? lastNpcParams = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    calls.Add(req.Method);
                    if (req.Method == "state.npc")
                        lastNpcParams = req.Params;

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.npc" => JsonDocument.Parse(npcPolls++ == 0
                            ? "{\"name\":\"Sophia\",\"location\":\"Custom_SophiaHouse\",\"tile\":{\"x\":23,\"y\":6},\"friendship_points\":0,\"hearts\":0,\"gift_given_today\":false,\"portrait\":\"Sophia\"}"
                            : "{\"name\":\"Sophia\",\"location\":\"Custom_BlueMoonVineyard\",\"tile\":{\"x\":20,\"y\":32},\"friendship_points\":0,\"hearts\":0,\"gift_given_today\":false,\"portrait\":\"Sophia\"}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_npc_location",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.npc_location",
                    Args = JsonDocument.Parse("{\"name\":\"Sophia\",\"location\":\"Custom_BlueMoonVineyard\",\"x\":20,\"y\":32,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);
        Assert.Equal(2, npcPolls);
        Assert.DoesNotContain("wait.npc_location", calls);
        Assert.Contains("state.npc", calls);
        Assert.Equal("Sophia", lastNpcParams!.Value.GetProperty("name").GetString());

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitNpcLocation_WaitsForWarpToSettleAfterLocationMatches()
    {
        var socket = SocketPath();
        var npcPolls = 0;
        var warpPolls = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.npc" => JsonDocument.Parse("{\"name\":\"Sophia\",\"location\":\"Custom_BlueMoonVineyard\",\"tile\":{\"x\":20,\"y\":32},\"friendship_points\":0,\"hearts\":0,\"gift_given_today\":false,\"portrait\":\"Sophia\"}").RootElement,
                        "freeze.status" => JsonDocument.Parse(warpPolls++ == 0
                            ? "{\"frozen\":false,\"is_warping\":true,\"tick\":1}"
                            : "{\"frozen\":false,\"is_warping\":false,\"tick\":2}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };

                    if (req.Method == "state.npc")
                        npcPolls++;

                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_npc_location_warp",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.npc_location",
                    Args = JsonDocument.Parse("{\"name\":\"Sophia\",\"location\":\"Custom_BlueMoonVineyard\",\"x\":20,\"y\":32,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);
        Assert.Equal(2, npcPolls);
        Assert.Equal(2, warpPolls);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitNpcLocation_WaitsForFadeToClearAfterLocationMatches()
    {
        var socket = SocketPath();
        var npcPolls = 0;
        var statusPolls = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.npc" => JsonDocument.Parse("{\"name\":\"Sophia\",\"location\":\"Custom_BlueMoonVineyard\",\"tile\":{\"x\":20,\"y\":32},\"friendship_points\":0,\"hearts\":0,\"gift_given_today\":false,\"portrait\":\"Sophia\"}").RootElement,
                        "freeze.status" => JsonDocument.Parse(statusPolls++ == 0
                            ? "{\"frozen\":false,\"is_warping\":false,\"is_fading\":true,\"tick\":1}"
                            : "{\"frozen\":false,\"is_warping\":false,\"is_fading\":false,\"tick\":2}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };

                    if (req.Method == "state.npc")
                        npcPolls++;

                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_npc_location_fade",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.npc_location",
                    Args = JsonDocument.Parse("{\"name\":\"Sophia\",\"location\":\"Custom_BlueMoonVineyard\",\"x\":20,\"y\":32,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);
        Assert.Equal(2, npcPolls);
        Assert.Equal(2, statusPolls);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitNpcLocation_TimeoutIncludesLastObservedNpcLocation()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.npc" => JsonDocument.Parse("{\"name\":\"Sophia\",\"location\":\"Custom_SophiaHouse\",\"tile\":{\"x\":23,\"y\":6},\"friendship_points\":0,\"hearts\":0,\"gift_given_today\":false,\"portrait\":\"Sophia\"}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_npc_location_timeout",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.npc_location",
                    Args = JsonDocument.Parse("{\"name\":\"Sophia\",\"location\":\"Custom_BlueMoonVineyard\",\"timeout_ms\":20,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.False(report.Passed);
        var failure = Assert.Single(report.Failures);
        Assert.Contains("wait.npc_location timed out after 20ms waiting for Sophia in Custom_BlueMoonVineyard", failure);
        Assert.Contains("last observed Custom_SophiaHouse at 23,6", failure);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitLocationContent_PollsStateLocationUntilFilteredCountMatches()
    {
        var socket = SocketPath();
        var calls = new List<string>();
        var locationPolls = 0;
        JsonElement? lastLocationParams = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    calls.Add(req.Method);
                    if (req.Method == "state.location")
                        lastLocationParams = req.Params;

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.location" => JsonDocument.Parse(locationPolls++ == 0
                            ? "{\"name\":\"Custom_GrandpasShedOutside\",\"resource_clumps\":[],\"monsters\":[],\"objects\":[]}"
                            : "{\"name\":\"Custom_GrandpasShedOutside\",\"resource_clumps\":[{\"tile\":{\"x\":21,\"y\":17},\"kind\":\"ResourceClump\",\"id\":\"602\",\"name\":\"Log\"},{\"tile\":{\"x\":23,\"y\":17},\"kind\":\"ResourceClump\",\"id\":\"602\",\"name\":\"Log\"}],\"monsters\":[],\"objects\":[]}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_location_content",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.location_content",
                    Args = JsonDocument.Parse("{\"location\":\"Custom_GrandpasShedOutside\",\"collection\":\"resource_clumps\",\"name\":\"Log\",\"min_count\":2,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);
        Assert.Equal(2, locationPolls);
        Assert.DoesNotContain("wait.location_content", calls);
        Assert.Contains("state.location", calls);
        Assert.Equal("Custom_GrandpasShedOutside", lastLocationParams!.Value.GetProperty("name").GetString());

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitLocationContent_FiltersByTileAndMaxCount()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.location" => JsonDocument.Parse("{\"name\":\"Custom_GrandpasShedOutside\",\"resource_clumps\":[{\"tile\":{\"x\":21,\"y\":17},\"kind\":\"ResourceClump\",\"id\":\"602\",\"name\":\"Log\"},{\"tile\":{\"x\":23,\"y\":17},\"kind\":\"ResourceClump\",\"id\":\"602\",\"name\":\"Log\"}],\"monsters\":[],\"objects\":[]}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_location_content_tile",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.location_content",
                    Args = JsonDocument.Parse("{\"location\":\"Custom_GrandpasShedOutside\",\"collection\":\"resource_clumps\",\"name\":\"Log\",\"x\":21,\"y\":17,\"min_count\":1,\"max_count\":1,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitLocationContent_FiltersByMonsterNumericAndSpriteFields()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.location" => JsonDocument.Parse("{\"name\":\"ExampleDeepCave\",\"resource_clumps\":[],\"objects\":[],\"monsters\":[{\"tile\":{\"x\":12,\"y\":8},\"name\":\"Crystal Bat\",\"type\":\"Bat\",\"health\":180,\"max_health\":180,\"damage\":32,\"sprite_texture\":\"ExampleMod/Monsters/CrystalBat\"},{\"tile\":{\"x\":13,\"y\":8},\"name\":\"Cave Moth\",\"type\":\"Bat\",\"health\":90,\"max_health\":90,\"damage\":18,\"sprite_texture\":\"ExampleMod/Monsters/CaveMoth\"}]}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_location_content_monster_metadata",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.location_content",
                    Args = JsonDocument.Parse("{\"location\":\"ExampleDeepCave\",\"collection\":\"monsters\",\"name\":\"Crystal Bat\",\"type\":\"Bat\",\"health\":180,\"max_health\":180,\"damage\":32,\"sprite_texture\":\"ExampleMod/Monsters/CrystalBat\",\"min_count\":1,\"max_count\":1,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitLocationContent_FiltersMonstersByIdentityAndLabel()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.location" => JsonDocument.Parse("{\"name\":\"Frobby_CombatLab\",\"resource_clumps\":[],\"objects\":[],\"monsters\":[{\"monster_id\":\"frobby-monster-1\",\"label\":\"wrong-label\",\"tile\":{\"x\":13,\"y\":8},\"type\":\"GreenSlime\",\"health\":1},{\"monster_id\":\"frobby-monster-2\",\"label\":\"target\",\"tile\":{\"x\":14,\"y\":8},\"type\":\"GreenSlime\",\"health\":1},{\"monster_id\":\"frobby-monster-1\",\"label\":\"target\",\"tile\":{\"x\":12,\"y\":8},\"type\":\"GreenSlime\",\"health\":1}]}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_lab_monster_identity",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.location_content",
                    Args = JsonDocument.Parse("{\"location\":\"Frobby_CombatLab\",\"collection\":\"monsters\",\"monster_id\":\"frobby-monster-1\",\"label\":\"target\",\"min_count\":1,\"max_count\":1,\"timeout_ms\":20,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed, string.Join("\n", report.Failures));

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitLocationContent_RejectsMonsterIdentityFiltersForNonMonsterCollections()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_object_with_monster_identity_filter",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.location_content",
                    Args = JsonDocument.Parse("{\"location\":\"FarmHouse\",\"collection\":\"objects\",\"monster_id\":\"frobby-monster-1\",\"min_count\":1,\"timeout_ms\":20,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.False(report.Passed);
        Assert.Contains("monster_id and label filters require collection=monsters", Assert.Single(report.Failures));

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitLocationContent_FiltersDebrisByIdentityTileAndStack()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.location" => JsonDocument.Parse("{\"name\":\"ExampleDeepCave\",\"resource_clumps\":[],\"objects\":[],\"monsters\":[],\"critters\":[],\"debris\":[{\"tile\":{\"x\":15,\"y\":16},\"pixel\":{\"x\":960,\"y\":1024},\"kind\":\"ItemDebris\",\"id\":\"769\",\"qualified_id\":\"(O)769\",\"name\":\"Void Essence\",\"stack\":2,\"quality\":0,\"category\":-2,\"runtime_type\":\"Debris\"}]}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_location_content_debris",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.location_content",
                    Args = JsonDocument.Parse("{\"location\":\"ExampleDeepCave\",\"collection\":\"debris\",\"qualified_id\":\"(O)769\",\"runtime_type\":\"Debris\",\"x\":15,\"y\":16,\"stack_gte\":2,\"quality\":0,\"category\":-2,\"min_count\":1,\"max_count\":1,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitLocationContent_FiltersObjectsByInteractionMetadata()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.location" => JsonDocument.Parse("{\"name\":\"FarmHouse\",\"objects\":[{\"tile\":{\"x\":8,\"y\":9},\"name\":\"Golden Piggy Bank\",\"id\":\"FlashShifter.StardewValleyExpandedCP_Golden_Piggy_Bank\",\"qualified_id\":\"(BC)FlashShifter.StardewValleyExpandedCP_Golden_Piggy_Bank\",\"runtime_type\":\"Object\",\"big_craftable\":false,\"held_object_id\":\"388\",\"held_object_qualified_id\":\"(O)388\"},{\"tile\":{\"x\":8,\"y\":9},\"name\":\"Golden Piggy Bank\",\"id\":\"FlashShifter.StardewValleyExpandedCP_Golden_Piggy_Bank\",\"qualified_id\":\"(BC)FlashShifter.StardewValleyExpandedCP_Golden_Piggy_Bank\",\"runtime_type\":\"Object\",\"big_craftable\":true,\"held_object_id\":\"340\",\"held_object_qualified_id\":\"(O)340\"}],\"resource_clumps\":[],\"monsters\":[],\"debris\":[]}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_location_content_object_metadata",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.location_content",
                    Args = JsonDocument.Parse("{\"location\":\"FarmHouse\",\"collection\":\"objects\",\"qualified_id\":\"(BC)FlashShifter.StardewValleyExpandedCP_Golden_Piggy_Bank\",\"runtime_type\":\"Object\",\"big_craftable\":true,\"held_object_id\":\"340\",\"held_object_qualified_id\":\"(O)340\",\"x\":8,\"y\":9,\"min_count\":1,\"max_count\":1,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitLocationContent_FiltersObjectsByContainedItem()
    {
        var socket = SocketPath();
        var locationPolls = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.location" when locationPolls++ == 0 => JsonDocument.Parse("{\"name\":\"Town\",\"objects\":[{\"tile\":{\"x\":63,\"y\":16},\"name\":\"Treasure Chest\",\"runtime_type\":\"Chest\",\"is_chest\":true,\"item_count\":1,\"items_truncated\":false,\"items\":[{\"slot\":0,\"id\":\"74\",\"item_id\":\"74\",\"qualified_id\":\"(O)74\",\"name\":\"Prismatic Shard\",\"stack\":1,\"quality\":0,\"category\":-12,\"runtime_type\":\"Object\"}]}],\"resource_clumps\":[],\"monsters\":[],\"debris\":[]}").RootElement,
                        "state.location" => JsonDocument.Parse("{\"name\":\"Town\",\"objects\":[{\"tile\":{\"x\":63,\"y\":16},\"name\":\"Treasure Chest\",\"runtime_type\":\"Chest\",\"is_chest\":true,\"item_count\":1,\"items_truncated\":false,\"items\":[{\"slot\":0,\"id\":\"373\",\"item_id\":\"373\",\"qualified_id\":\"(O)373\",\"name\":\"Golden Pumpkin\",\"stack\":1,\"quality\":0,\"category\":-79,\"runtime_type\":\"Object\"}]}],\"resource_clumps\":[],\"monsters\":[],\"debris\":[]}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_location_content_chest_item",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.location_content",
                    Args = JsonDocument.Parse("{\"location\":\"Town\",\"collection\":\"objects\",\"runtime_type\":\"Chest\",\"x\":63,\"y\":16,\"contains_item_qualified_id\":\"(O)373\",\"contains_item_stack\":1,\"contains_item_quality\":0,\"contains_item_category\":-79,\"min_count\":1,\"max_count\":1,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed, string.Join("\n", report.Failures));
        Assert.True(locationPolls >= 2);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitLocationContent_MatchesMonsterNumericComparisons()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.location" => JsonDocument.Parse("{\"name\":\"ExampleDeepCave\",\"resource_clumps\":[],\"objects\":[],\"monsters\":[{\"tile\":{\"x\":12,\"y\":8},\"name\":\"Crystal Bat\",\"type\":\"Bat\",\"health\":180,\"max_health\":220,\"damage\":32,\"revive_timer\":1200,\"sprite_texture\":\"ExampleMod/Monsters/CrystalBat\"},{\"tile\":{\"x\":13,\"y\":8},\"name\":\"Cave Moth\",\"type\":\"Bat\",\"health\":180,\"max_health\":220,\"damage\":32,\"revive_timer\":0,\"sprite_texture\":\"ExampleMod/Monsters/CaveMoth\"}]}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_location_content_monster_numeric_comparisons",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.location_content",
                    Args = JsonDocument.Parse("{\"location\":\"ExampleDeepCave\",\"collection\":\"monsters\",\"type\":\"Bat\",\"health_gt\":100,\"health_lte\":180,\"max_health_gte\":200,\"damage_lt\":40,\"revive_timer_gt\":0,\"min_count\":1,\"max_count\":1,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitLocationContent_MatchesObjectMinutesUntilReadyComparisons()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.location" => JsonDocument.Parse("{\"name\":\"Frobby_CombatLab\",\"resource_clumps\":[],\"objects\":[{\"tile\":{\"x\":9,\"y\":8},\"name\":\"Bomb\",\"id\":\"287\",\"qualified_id\":\"(O)287\",\"runtime_type\":\"Object\",\"big_craftable\":false,\"minutes_until_ready\":2},{\"tile\":{\"x\":10,\"y\":8},\"name\":\"Bomb\",\"id\":\"287\",\"qualified_id\":\"(O)287\",\"runtime_type\":\"Object\",\"big_craftable\":false,\"minutes_until_ready\":0}],\"monsters\":[],\"debris\":[]}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_location_content_object_minutes_until_ready",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.location_content",
                    Args = JsonDocument.Parse("{\"location\":\"Frobby_CombatLab\",\"collection\":\"objects\",\"qualified_id\":\"(O)287\",\"minutes_until_ready_gt\":0,\"min_count\":1,\"max_count\":1,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed, string.Join("\n", report.Failures));

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitLocationContent_AllowsZeroMatchingContent()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.location" => JsonDocument.Parse("{\"name\":\"ExampleDeepCave\",\"resource_clumps\":[],\"objects\":[],\"monsters\":[{\"tile\":{\"x\":12,\"y\":8},\"name\":\"Crystal Bat\",\"type\":\"Bat\",\"health\":180,\"max_health\":180,\"damage\":32,\"sprite_texture\":\"ExampleMod/Monsters/CrystalBat\"}]}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_location_content_zero_matches",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.location_content",
                    Args = JsonDocument.Parse("{\"location\":\"ExampleDeepCave\",\"collection\":\"monsters\",\"name\":\"Missing Bat\",\"min_count\":0,\"max_count\":0,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitLocationContent_TimeoutIncludesMonsterMetadataFilters()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.location" => JsonDocument.Parse("{\"name\":\"ExampleDeepCave\",\"resource_clumps\":[],\"objects\":[],\"monsters\":[{\"tile\":{\"x\":13,\"y\":8},\"name\":\"Cave Moth\",\"type\":\"Bat\",\"health\":90,\"max_health\":90,\"damage\":18,\"sprite_texture\":\"ExampleMod/Monsters/CaveMoth\"}]}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_location_content_monster_timeout",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.location_content",
                    Args = JsonDocument.Parse("{\"location\":\"ExampleDeepCave\",\"collection\":\"monsters\",\"name\":\"Crystal Bat\",\"type\":\"Bat\",\"health\":180,\"max_health\":180,\"damage\":32,\"health_gt\":150,\"max_health_lte\":200,\"damage_lt\":40,\"sprite_texture\":\"ExampleMod/Monsters/CrystalBat\",\"min_count\":1,\"timeout_ms\":20,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.False(report.Passed);
        var failure = Assert.Single(report.Failures);
        Assert.Contains("matching name=Crystal Bat, type=Bat, health=180, health_gt=150, max_health=180, max_health_lte=200, damage=32, damage_lt=40, sprite_texture=ExampleMod/Monsters/CrystalBat", failure);
        Assert.Contains("last observed 0 matched out of 1 monsters", failure);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitLocationContent_TimeoutIncludesMonsterIdentityFilters()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.location" => JsonDocument.Parse("{\"name\":\"Frobby_CombatLab\",\"resource_clumps\":[],\"objects\":[],\"monsters\":[{\"monster_id\":\"frobby-monster-2\",\"label\":\"decoy\",\"tile\":{\"x\":13,\"y\":8},\"type\":\"GreenSlime\",\"health\":1}]}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_lab_monster_identity_timeout",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.location_content",
                    Args = JsonDocument.Parse("{\"location\":\"Frobby_CombatLab\",\"collection\":\"monsters\",\"monster_id\":\"frobby-monster-1\",\"label\":\"target\",\"min_count\":1,\"timeout_ms\":20,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.False(report.Passed);
        var failure = Assert.Single(report.Failures);
        Assert.Contains("matching monster_id=frobby-monster-1, label=target", failure);
        Assert.Contains("last observed 0 matched out of 1 monsters", failure);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitLocationContent_TimeoutIncludesObjectMetadataFilters()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.location" => JsonDocument.Parse("{\"name\":\"FarmHouse\",\"objects\":[{\"tile\":{\"x\":8,\"y\":9},\"name\":\"Plain Chest\",\"id\":\"130\",\"qualified_id\":\"(BC)130\",\"runtime_type\":\"Chest\",\"big_craftable\":true}],\"resource_clumps\":[],\"monsters\":[],\"debris\":[]}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_location_content_object_timeout",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.location_content",
                    Args = JsonDocument.Parse("{\"location\":\"FarmHouse\",\"collection\":\"objects\",\"name\":\"Golden Piggy Bank\",\"runtime_type\":\"Object\",\"big_craftable\":true,\"held_object_id\":\"340\",\"timeout_ms\":20,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.False(report.Passed);
        var failure = Assert.Single(report.Failures);
        Assert.Contains("matching name=Golden Piggy Bank, runtime_type=Object, big_craftable=True, held_object_id=340", failure);
        Assert.Contains("last observed 0 matched out of 1 objects", failure);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitLocationContent_NonNumberMonsterMetadataIsNonMatch()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.location" => JsonDocument.Parse("{\"name\":\"ExampleDeepCave\",\"resource_clumps\":[],\"objects\":[],\"monsters\":[{\"tile\":{\"x\":12,\"y\":8},\"name\":\"Crystal Bat\",\"type\":\"Bat\",\"health\":\"unknown\",\"max_health\":180,\"damage\":32,\"sprite_texture\":\"ExampleMod/Monsters/CrystalBat\"}]}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_location_content_monster_non_number_metadata",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.location_content",
                    Args = JsonDocument.Parse("{\"location\":\"ExampleDeepCave\",\"collection\":\"monsters\",\"name\":\"Crystal Bat\",\"type\":\"Bat\",\"health\":180,\"timeout_ms\":20,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.False(report.Passed);
        var failure = Assert.Single(report.Failures);
        Assert.Contains("wait.location_content timed out after 20ms waiting for at least 1 monsters in ExampleDeepCave matching name=Crystal Bat, type=Bat, health=180", failure);
        Assert.Contains("last observed 0 matched out of 1 monsters", failure);
        Assert.DoesNotContain("InvalidOperationException", failure);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitLocationContent_TimeoutIncludesLastObservedCounts()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.location" => JsonDocument.Parse("{\"name\":\"Custom_GrandpasShedOutside\",\"resource_clumps\":[{\"tile\":{\"x\":21,\"y\":17},\"kind\":\"ResourceClump\",\"id\":\"602\",\"name\":\"Log\"}],\"monsters\":[],\"objects\":[]}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_location_content_timeout",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.location_content",
                    Args = JsonDocument.Parse("{\"location\":\"Custom_GrandpasShedOutside\",\"collection\":\"resource_clumps\",\"name\":\"Log\",\"min_count\":2,\"timeout_ms\":20,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.False(report.Passed);
        var failure = Assert.Single(report.Failures);
        Assert.Contains("wait.location_content timed out after 20ms waiting for at least 2 resource_clumps in Custom_GrandpasShedOutside", failure);
        Assert.Contains("last observed 1 matched out of 1 resource_clumps", failure);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitLocationContent_RejectsUnsupportedCollection()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_location_content_bad_collection",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.location_content",
                    Args = JsonDocument.Parse("{\"location\":\"Farm\",\"collection\":\"mailboxes\",\"timeout_ms\":20,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.False(report.Passed);
        Assert.Contains("wait.location_content requires args.collection to be one of objects, resource_clumps, monsters, critters, debris", Assert.Single(report.Failures));

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitVisualEffects_PollsStateUntilTemporarySpriteMatches()
    {
        var socket = SocketPath();
        var calls = new List<string>();
        var visualEffectsPolls = 0;
        JsonElement? lastVisualEffectsParams = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    calls.Add(req.Method);
                    if (req.Method == "state.visual_effects")
                        lastVisualEffectsParams = req.Params;

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.visual_effects" => JsonDocument.Parse(visualEffectsPolls++ == 0
                            ? "{\"location\":\"Custom_GrandpasGrove\",\"ambient_light\":[255,255,255,255],\"temporary_sprites\":[],\"light_sources\":[],\"weather_debris_count\":0}"
                            : "{\"location\":\"Custom_GrandpasGrove\",\"ambient_light\":[255,255,255,255],\"temporary_sprites\":[{\"texture_asset\":\"LooseSprites/Cursors\",\"source_rect\":[372,1956,10,10],\"color\":[255,255,255,255],\"runtime_type\":\"TemporaryAnimatedSprite\",\"layer_depth\":0.5}],\"light_sources\":[],\"weather_debris_count\":0}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_visual_effects_sprite",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.visual_effects",
                    Args = JsonDocument.Parse("{\"location\":\"Custom_GrandpasGrove\",\"temporary_sprites\":{\"texture_asset\":\"LooseSprites/Cursors\",\"source_rect\":[372,1956,10,10],\"min_count\":1},\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);
        Assert.Equal(2, visualEffectsPolls);
        Assert.DoesNotContain("wait.visual_effects", calls);
        Assert.Contains("state.visual_effects", calls);
        Assert.Equal("Custom_GrandpasGrove", lastVisualEffectsParams!.Value.GetProperty("location").GetString());

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitVisualEffects_PollsStateUntilLightAndAmbientMatch()
    {
        var socket = SocketPath();
        var visualEffectsPolls = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.visual_effects" => JsonDocument.Parse(visualEffectsPolls++ == 0
                            ? "{\"location\":\"Custom_GrandpasGrove\",\"ambient_light\":[1,2,3,255],\"temporary_sprites\":[],\"light_sources\":[],\"weather_debris_count\":0}"
                            : "{\"location\":\"Custom_GrandpasGrove\",\"ambient_light\":[8,9,10,255],\"temporary_sprites\":[],\"light_sources\":[{\"id\":\"SVE_FH_Lantern\",\"color\":[255,240,220,255],\"context\":\"GameLocation\"}],\"weather_debris_count\":0}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_visual_effects_light_ambient",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.visual_effects",
                    Args = JsonDocument.Parse("{\"location\":\"Custom_GrandpasGrove\",\"ambient_light\":[8,9,10,255],\"light_sources\":{\"id_contains\":\"SVE_FH\",\"min_count\":1},\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);
        Assert.Equal(2, visualEffectsPolls);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitVisualEffects_TimeoutIncludesLastObservedCounts()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.visual_effects" => JsonDocument.Parse("{\"location\":\"Custom_GrandpasGrove\",\"ambient_light\":[255,255,255,255],\"temporary_sprites\":[{\"texture_asset\":\"LooseSprites/Cursors\",\"source_rect\":[372,1956,10,10],\"color\":[255,255,255,255],\"runtime_type\":\"TemporaryAnimatedSprite\",\"layer_depth\":0.5}],\"light_sources\":[],\"weather_debris_count\":0}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_visual_effects_timeout",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.visual_effects",
                    Args = JsonDocument.Parse("{\"location\":\"Custom_GrandpasGrove\",\"temporary_sprites\":{\"texture_asset\":\"Maps/SandstormEffect\",\"min_count\":1},\"timeout_ms\":20,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.False(report.Passed);
        var failure = Assert.Single(report.Failures);
        Assert.Contains("wait.visual_effects timed out after 20ms waiting for at least 1 temporary_sprites in Custom_GrandpasGrove", failure);
        Assert.Contains("last observed 0 matched temporary_sprites out of 1", failure);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitVisualEffects_RejectsInvalidSourceRect()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_visual_effects_invalid_source_rect",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.visual_effects",
                    Args = JsonDocument.Parse("{\"location\":\"Custom_GrandpasGrove\",\"temporary_sprites\":{\"source_rect\":[1,2,3],\"min_count\":1},\"timeout_ms\":20,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.False(report.Passed);
        Assert.Contains("wait.visual_effects requires args.temporary_sprites.source_rect to have 4 values", Assert.Single(report.Failures));

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitEventActive_PollsStateEventUntilActive()
    {
        var socket = SocketPath();
        var eventPolls = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.event" when eventPolls++ == 0 => JsonDocument.Parse("{\"active\":false,\"event_up\":false,\"location\":\"\",\"id\":\"\",\"actors\":[],\"dialogue\":null,\"viewport\":null}").RootElement,
                        "state.event" => JsonDocument.Parse("{\"active\":true,\"event_up\":true,\"location\":\"BusStop\",\"id\":\"520702\",\"actors\":[],\"dialogue\":null,\"viewport\":{\"x\":0,\"y\":0,\"width\":1280,\"height\":720}}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_event_active",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.event_active",
                    Args = JsonDocument.Parse("{\"id\":\"520702\",\"location\":\"BusStop\",\"timeout_ms\":1000,\"poll_ms\":10}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed, string.Join("\n", report.Failures));
        Assert.True(eventPolls >= 2);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitEventActive_FiltersByFestivalState()
    {
        var socket = SocketPath();
        var eventPolls = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.event" when eventPolls++ == 0 => JsonDocument.Parse("{\"active\":true,\"event_up\":true,\"location\":\"Town\",\"id\":\"fall27\",\"is_festival\":false,\"actors\":[],\"dialogue\":null,\"viewport\":{\"x\":0,\"y\":0,\"width\":1280,\"height\":720}}").RootElement,
                        "state.event" => JsonDocument.Parse("{\"active\":true,\"event_up\":true,\"location\":\"Town\",\"id\":\"fall27\",\"is_festival\":true,\"actors\":[],\"dialogue\":null,\"viewport\":{\"x\":0,\"y\":0,\"width\":1280,\"height\":720}}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_event_active_festival",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.event_active",
                    Args = JsonDocument.Parse("{\"id\":\"fall27\",\"location\":\"Town\",\"is_festival\":true,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed, string.Join("\n", report.Failures));
        Assert.True(eventPolls >= 2);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitEventActive_FiltersByActorNameAndTile()
    {
        var socket = SocketPath();
        var eventPolls = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.event" when eventPolls++ == 0 => JsonDocument.Parse("{\"active\":true,\"event_up\":true,\"location\":\"Town\",\"id\":\"fall16\",\"is_festival\":true,\"actors\":[{\"name\":\"Andy\",\"tile\":{\"x\":18,\"y\":77},\"pixel\":{\"x\":1152,\"y\":4928},\"facing_direction\":1,\"current_frame\":0}],\"dialogue\":null,\"viewport\":{\"x\":0,\"y\":0,\"width\":1280,\"height\":720}}").RootElement,
                        "state.event" => JsonDocument.Parse("{\"active\":true,\"event_up\":true,\"location\":\"Town\",\"id\":\"fall16\",\"is_festival\":true,\"actors\":[{\"name\":\"Sophia\",\"tile\":{\"x\":19,\"y\":77},\"pixel\":{\"x\":1216,\"y\":4928},\"facing_direction\":1,\"current_frame\":0}],\"dialogue\":null,\"viewport\":{\"x\":0,\"y\":0,\"width\":1280,\"height\":720}}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_event_active_actor",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.event_active",
                    Args = JsonDocument.Parse("{\"id\":\"fall16\",\"location\":\"Town\",\"is_festival\":true,\"actor_name\":\"Sophia\",\"actor_x\":19,\"actor_y\":77,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed, string.Join("\n", report.Failures));
        Assert.True(eventPolls >= 2);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitEventActive_FiltersByActorDialogueText()
    {
        var socket = SocketPath();
        var eventPolls = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.event" when eventPolls++ == 0 => JsonDocument.Parse("{\"active\":true,\"event_up\":true,\"location\":\"Town\",\"id\":\"fall16\",\"is_festival\":true,\"actors\":[{\"name\":\"Lewis\",\"tile\":{\"x\":42,\"y\":57},\"pixel\":{\"x\":2688,\"y\":3648},\"facing_direction\":2,\"current_frame\":0,\"dialogue_key\":\"Strings\\\\Events:TownFair_Lewis_Greeting\",\"dialogue_text\":\"Welcome to the Stardew Valley Fair!\"}],\"dialogue\":null,\"viewport\":{\"x\":0,\"y\":0,\"width\":1280,\"height\":720}}").RootElement,
                        "state.event" => JsonDocument.Parse("{\"active\":true,\"event_up\":true,\"location\":\"Town\",\"id\":\"fall16\",\"is_festival\":true,\"actors\":[{\"name\":\"Lewis\",\"tile\":{\"x\":42,\"y\":57},\"pixel\":{\"x\":2688,\"y\":3648},\"facing_direction\":2,\"current_frame\":0,\"dialogue_key\":\"Strings\\\\Events:TownFair_Lewis_Judging\",\"dialogue_text\":\"Now judging the grange displays.\"}],\"dialogue\":null,\"viewport\":{\"x\":0,\"y\":0,\"width\":1280,\"height\":720}}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_event_active_actor_dialogue",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.event_active",
                    Args = JsonDocument.Parse("{\"id\":\"fall16\",\"actor_name\":\"Lewis\",\"actor_dialogue_text\":\"GRANGE DISPLAYS\",\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed, string.Join("\n", report.Failures));
        Assert.True(eventPolls >= 2);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitEventActive_FiltersByActorDialogueTextMatches()
    {
        var socket = SocketPath();
        var eventPolls = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.event" when eventPolls++ == 0 => JsonDocument.Parse("{\"active\":true,\"event_up\":true,\"location\":\"Town\",\"id\":\"fall16\",\"is_festival\":true,\"actors\":[{\"name\":\"Lewis\",\"tile\":{\"x\":42,\"y\":57},\"pixel\":{\"x\":2688,\"y\":3648},\"facing_direction\":2,\"current_frame\":0,\"dialogue_text\":\"Welcome to the Stardew Valley Fair!\"}],\"dialogue\":null,\"viewport\":{\"x\":0,\"y\":0,\"width\":1280,\"height\":720}}").RootElement,
                        "state.event" => JsonDocument.Parse("{\"active\":true,\"event_up\":true,\"location\":\"Town\",\"id\":\"fall16\",\"is_festival\":true,\"actors\":[{\"name\":\"Lewis\",\"tile\":{\"x\":42,\"y\":57},\"pixel\":{\"x\":2688,\"y\":3648},\"facing_direction\":2,\"current_frame\":0,\"dialogue_text\":\"Now judging the grange displays.\"}],\"dialogue\":null,\"viewport\":{\"x\":0,\"y\":0,\"width\":1280,\"height\":720}}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_event_active_actor_dialogue_matches",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.event_active",
                    Args = JsonDocument.Parse("{\"id\":\"fall16\",\"actor_name\":\"Lewis\",\"actor_dialogue_text_matches\":\"GRANGE\\\\s+DISPLAYS\",\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed, string.Join("\n", report.Failures));
        Assert.True(eventPolls >= 2);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitEventActive_FiltersByActorDialogueKey()
    {
        var socket = SocketPath();
        var eventPolls = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.event" when eventPolls++ == 0 => JsonDocument.Parse("{\"active\":true,\"event_up\":true,\"location\":\"Town\",\"id\":\"fall16\",\"is_festival\":true,\"actors\":[{\"name\":\"Lewis\",\"tile\":{\"x\":42,\"y\":57},\"pixel\":{\"x\":2688,\"y\":3648},\"facing_direction\":2,\"current_frame\":0,\"dialogue_key\":\"Strings\\\\Events:TownFair_Lewis_Greeting\",\"dialogue_text\":\"Welcome to the Stardew Valley Fair!\"}],\"dialogue\":null,\"viewport\":{\"x\":0,\"y\":0,\"width\":1280,\"height\":720}}").RootElement,
                        "state.event" => JsonDocument.Parse("{\"active\":true,\"event_up\":true,\"location\":\"Town\",\"id\":\"fall16\",\"is_festival\":true,\"actors\":[{\"name\":\"Lewis\",\"tile\":{\"x\":42,\"y\":57},\"pixel\":{\"x\":2688,\"y\":3648},\"facing_direction\":2,\"current_frame\":0,\"dialogue_key\":\"Strings\\\\Events:TownFair_Lewis_Judging\",\"dialogue_text\":\"Now judging the grange displays.\"}],\"dialogue\":null,\"viewport\":{\"x\":0,\"y\":0,\"width\":1280,\"height\":720}}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_event_active_actor_dialogue_key",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.event_active",
                    Args = JsonDocument.Parse("{\"id\":\"fall16\",\"actor_name\":\"Lewis\",\"actor_dialogue_key\":\"Strings\\\\Events:TownFair_Lewis_Judging\",\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed, string.Join("\n", report.Failures));
        Assert.True(eventPolls >= 2);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitEventActive_ActorTimeoutIncludesObservedActorNames()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.event" => JsonDocument.Parse("{\"active\":true,\"event_up\":true,\"location\":\"Town\",\"id\":\"fall16\",\"is_festival\":true,\"actors\":[{\"name\":\"Andy\",\"tile\":{\"x\":18,\"y\":77},\"pixel\":{\"x\":1152,\"y\":4928},\"facing_direction\":1,\"current_frame\":0,\"dialogue_key\":\"Strings\\\\Events:TownFair_Andy_Display\",\"dialogue_text\":\"My display is ready.\"}],\"dialogue\":null,\"viewport\":{\"x\":0,\"y\":0,\"width\":1280,\"height\":720}}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_event_active_missing_actor",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.event_active",
                    Args = JsonDocument.Parse("{\"id\":\"fall16\",\"actor_name\":\"Sophia\",\"timeout_ms\":20,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.False(report.Passed);
        var failure = Assert.Single(report.Failures);
        Assert.Contains("actor_name=Sophia", failure);
        Assert.Contains("Andy@18,77", failure);
        Assert.Contains("dialogue_key=Strings\\Events:TownFair_Andy_Display", failure);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitEventComplete_WithId_WaitsForTargetEventBeforeCompletion()
    {
        var socket = SocketPath();
        var eventPolls = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.event" when eventPolls++ == 0 => JsonDocument.Parse("{\"active\":false,\"event_up\":false,\"location\":\"\",\"id\":\"\",\"actors\":[],\"dialogue\":null,\"viewport\":null}").RootElement,
                        "state.event" when eventPolls == 2 => JsonDocument.Parse("{\"active\":true,\"event_up\":true,\"location\":\"BusStop\",\"id\":\"520702\",\"actors\":[],\"dialogue\":null,\"viewport\":{\"x\":0,\"y\":0,\"width\":1280,\"height\":720}}").RootElement,
                        "state.event" => JsonDocument.Parse("{\"active\":false,\"event_up\":false,\"location\":\"BusStop\",\"id\":\"\",\"actors\":[],\"dialogue\":null,\"viewport\":null}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_event_complete",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.event_complete",
                    Args = JsonDocument.Parse("{\"id\":\"520702\",\"timeout_ms\":1000,\"poll_ms\":10}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed, string.Join("\n", report.Failures));
        Assert.True(eventPolls >= 3);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task ScreenshotCaptureNextFrame_UsesNextFrameBitmapRpc()
    {
        var socket = SocketPath();
        var tmp = Path.Combine(Path.GetTempPath(), $"scenario-next-frame-{Guid.NewGuid():N}");
        var rd = RunDirectory.Create(tmp);
        var source = Path.Combine(tmp, "source.png");
        File.WriteAllBytes(source, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        var calls = new List<string>();
        var captureParams = default(JsonElement);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    calls.Add(req.Method);
                    JsonElement r;
                    if (req.Method == "bitmap.capture_next_frame")
                    {
                        captureParams = req.Params!.Value.Clone();
                        r = JsonSerializer.SerializeToElement(new
                        {
                            path = source,
                            width = 1,
                            height = 1,
                        });
                    }
                    else
                    {
                        r = req.Method switch
                        {
                            "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                            "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                            _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                        };
                    }
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        try
        {
            for (int i = 0; i < 40 && !File.Exists(socket); i++)
                await Task.Delay(50, cts.Token);

            using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
            _ = client.RunAsync(cts.Token);

            var runner = new ScenarioRunner(client, updateBaselines: false, reportDir: rd);
            var report = await runner.RunAsync(new ScenarioSpec
            {
                Name = "next_frame_screenshot",
                Steps = new()
                {
                    new ScenarioStep
                    {
                        Action = "screenshot.capture_next_frame",
                        Args = JsonDocument.Parse("{\"name\":\"chart-1m\",\"timeout_ms\":3000}").RootElement,
                    },
                },
            }, cts.Token);

            Assert.True(report.Passed);
            Assert.Contains("bitmap.capture_next_frame", calls);
            Assert.DoesNotContain("bitmap.capture", calls);
            Assert.Equal(3000, captureParams.GetProperty("timeout_ms").GetInt32());
            Assert.True(captureParams.GetProperty("allow_unfrozen").GetBoolean());
            Assert.Contains("scenarios/next_frame_screenshot/screenshots/chart-1m.png", report.Screenshots);
            Assert.Equal("Capture next-frame screenshot \"chart-1m\"", report.Steps[0].Detail);
        }
        finally
        {
            cts.Cancel();
            try { await serverTask; } catch (OperationCanceledException) { }
            Directory.Delete(rd.Root, recursive: true);
        }
    }

    [Fact]
    public async Task UiClickText_WaitsThenClicksMatchedText()
    {
        var socket = SocketPath();
        var calls = new List<string>();
        var clickParams = default(JsonElement);
        var findParams = default(JsonElement);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    calls.Add(req.Method);
                    if (req.Method == "draw.text_find" && req.Params is { } find)
                        findParams = find.Clone();
                    if (req.Method == "input.click_text" && req.Params is { } click)
                        clickParams = click.Clone();

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "draw.arm" => JsonDocument.Parse("{\"ok\":true,\"tick\":1}").RootElement,
                        "draw.text_find" => JsonDocument.Parse("{\"events\":[{},{}],\"count\":2}").RootElement,
                        "input.click_text" => JsonDocument.Parse("{\"ok\":true,\"tick\":2}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "ui_click_text",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "ui.click_text",
                    Args = JsonDocument.Parse("{\"text\":\"WIRE IN 1,000g\",\"button\":\"right\",\"occurrence\":2,\"timeout_ms\":1000,\"poll_ms\":1,\"capture_ticks\":3,\"in_rect\":[0,0,640,360]}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);
        Assert.Equal(new[] { "scenario.begin", "draw.arm", "draw.text_find", "input.click_text", "scenario.end" }, calls);
        Assert.True(findParams.GetProperty("disarm_after_snapshot").GetBoolean());
        Assert.Equal("WIRE IN 1,000g", clickParams.GetProperty("text").GetString());
        Assert.Equal("right", clickParams.GetProperty("button").GetString());
        Assert.Equal(2, clickParams.GetProperty("occurrence").GetInt32());
        Assert.Equal(640, clickParams.GetProperty("in_rect")[2].GetInt32());

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task UiClickText_ForwardsExactTextFilter()
    {
        var socket = SocketPath();
        var findParams = default(JsonElement);
        var clickParams = default(JsonElement);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    if (req.Method == "draw.text_find" && req.Params is { } find)
                        findParams = find.Clone();
                    if (req.Method == "input.click_text" && req.Params is { } click)
                        clickParams = click.Clone();

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "draw.arm" => JsonDocument.Parse("{\"ok\":true,\"tick\":1}").RootElement,
                        "draw.text_find" => JsonDocument.Parse("{\"events\":[{},{}],\"count\":2}").RootElement,
                        "input.click_text" => JsonDocument.Parse("{\"ok\":true,\"tick\":2}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "ui_click_exact_text",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "ui.click_text",
                    Args = JsonDocument.Parse("{\"text_equals\":\"CONTINUE\",\"timeout_ms\":1000,\"poll_ms\":1,\"capture_ticks\":3}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);
        Assert.Equal("CONTINUE", findParams.GetProperty("text_equals").GetString());
        Assert.False(findParams.TryGetProperty("text_contains", out _));
        Assert.Equal("CONTINUE", clickParams.GetProperty("text_equals").GetString());
        Assert.False(clickParams.TryGetProperty("text", out _));

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task UiHoverText_WaitsThenInvokesInputHoverText()
    {
        var socket = SocketPath();
        var calls = new List<string>();
        var hoverParams = default(JsonElement);
        var findParams = default(JsonElement);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    calls.Add(req.Method);
                    if (req.Method == "draw.text_find" && req.Params is { } find)
                        findParams = find.Clone();
                    if (req.Method == "input.hover_text" && req.Params is { } hover)
                        hoverParams = hover.Clone();

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "draw.arm" => JsonDocument.Parse("{\"ok\":true,\"tick\":1}").RootElement,
                        "draw.text_find" => JsonDocument.Parse("{\"events\":[{},{}],\"count\":2}").RootElement,
                        "input.hover_text" => JsonDocument.Parse("{\"ok\":true,\"tick\":2}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "ui_hover_text",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "ui.hover_text",
                    Args = JsonDocument.Parse("{\"text_equals\":\"2.15B g\",\"occurrence\":2,\"timeout_ms\":1000,\"poll_ms\":1,\"capture_ticks\":3,\"bounds_within_rect\":[560,238,308,74]}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);
        Assert.Equal(new[] { "scenario.begin", "draw.arm", "draw.text_find", "input.hover_text", "scenario.end" }, calls);
        Assert.True(findParams.GetProperty("disarm_after_snapshot").GetBoolean());
        Assert.Equal("2.15B g", hoverParams.GetProperty("text_equals").GetString());
        Assert.Equal(2, hoverParams.GetProperty("occurrence").GetInt32());
        Assert.Equal(560, hoverParams.GetProperty("bounds_within_rect")[0].GetInt32());

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task UiWaitText_TimeoutFailsScenario()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "draw.arm" => JsonDocument.Parse("{\"ok\":true,\"tick\":1}").RootElement,
                        "draw.text_find" => JsonDocument.Parse("{\"events\":[],\"count\":0}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "ui_wait_text_timeout",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "ui.wait_text",
                    Args = JsonDocument.Parse("{\"text\":\"NEVER\",\"timeout_ms\":20,\"poll_ms\":1,\"capture_ticks\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.False(report.Passed);
        Assert.Contains("ui.wait_text timed out after 20ms waiting for text \"NEVER\"", string.Join(";", report.Failures));

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task UiWaitText_StalledTextFindFailsOnStepTimeout()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    if (req.Method == "draw.text_find")
                        return;

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "draw.arm" => JsonDocument.Parse("{\"ok\":true,\"tick\":1}").RootElement,
                        "draw.disarm" => JsonDocument.Parse("{\"ok\":true}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var runTask = runner.RunAsync(new ScenarioSpec
        {
            Name = "ui_wait_text_stalled_text_find",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "ui.wait_text",
                    Args = JsonDocument.Parse("{\"text\":\"NEVER\",\"timeout_ms\":40,\"poll_ms\":1,\"capture_ticks\":1}").RootElement,
                },
            },
        }, cts.Token);

        var completed = await Task.WhenAny(runTask, Task.Delay(1000, cts.Token));
        Assert.Same(runTask, completed);
        var report = await runTask;

        Assert.False(report.Passed);
        Assert.Contains("ui.wait_text timed out after 40ms waiting for text \"NEVER\"", string.Join(";", report.Failures));

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task UiWaitText_StalledTextFindDoesNotHangWhenScenarioEndStalls()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    if (req.Method == "draw.text_find" || req.Method == "scenario.end")
                        return;

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "draw.arm" => JsonDocument.Parse("{\"ok\":true,\"tick\":1}").RootElement,
                        "draw.disarm" => JsonDocument.Parse("{\"ok\":true}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var runTask = runner.RunAsync(new ScenarioSpec
        {
            Name = "ui_wait_text_stalled_cleanup",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "ui.wait_text",
                    Args = JsonDocument.Parse("{\"text\":\"NEVER\",\"timeout_ms\":40,\"poll_ms\":1,\"capture_ticks\":1}").RootElement,
                },
            },
        }, cts.Token);

        var completed = await Task.WhenAny(runTask, Task.Delay(1500, cts.Token));
        Assert.Same(runTask, completed);
        var report = await runTask;

        Assert.False(report.Passed);
        Assert.Contains("ui.wait_text timed out after 40ms waiting for text \"NEVER\"", string.Join(";", report.Failures));

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task UiWaitText_MatchDoesNotFailWhenDisarmExceedsStepTimeout()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    if (req.Method == "draw.disarm")
                        return;

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "draw.arm" => JsonDocument.Parse("{\"ok\":true,\"tick\":1}").RootElement,
                        "draw.text_find" => JsonDocument.Parse("{\"events\":[{\"text\":\"READY\"}],\"count\":1}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var runTask = runner.RunAsync(new ScenarioSpec
        {
            Name = "ui_wait_text_match_stalled_disarm",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "ui.wait_text",
                    Args = JsonDocument.Parse("{\"text\":\"READY\",\"timeout_ms\":40,\"poll_ms\":1,\"capture_ticks\":1}").RootElement,
                },
            },
        }, cts.Token);

        var completed = await Task.WhenAny(runTask, Task.Delay(1000, cts.Token));
        Assert.Same(runTask, completed);
        var report = await runTask;

        Assert.True(report.Passed, string.Join(";", report.Failures));

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task UiWaitText_RequestsAtomicTextFindDisarm()
    {
        var socket = SocketPath();
        var calls = new List<string>();
        var findParams = default(JsonElement);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    calls.Add(req.Method);
                    if (req.Method == "draw.text_find" && req.Params is { } find)
                        findParams = find.Clone();

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "draw.arm" => JsonDocument.Parse("{\"ok\":true,\"tick\":1}").RootElement,
                        "draw.text_find" => JsonDocument.Parse("{\"events\":[{\"text\":\"READY\"}],\"count\":1}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "ui_wait_text_atomic_disarm",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "ui.wait_text",
                    Args = JsonDocument.Parse("{\"text\":\"READY\",\"timeout_ms\":1000,\"poll_ms\":1,\"capture_ticks\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed, string.Join(";", report.Failures));
        Assert.True(findParams.GetProperty("disarm_after_snapshot").GetBoolean());
        Assert.DoesNotContain("draw.disarm", calls);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task GenericStep_StalledRpcFailsOnStepTimeout()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    if (req.Method == "input.click")
                        return;

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var runTask = runner.RunAsync(new ScenarioSpec
        {
            Name = "generic_stalled_rpc",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "input.click",
                    Args = JsonDocument.Parse("{\"x\":117,\"y\":347,\"timeout_ms\":40}").RootElement,
                },
            },
        }, cts.Token);

        var completed = await Task.WhenAny(runTask, Task.Delay(1000, cts.Token));
        Assert.Same(runTask, completed);
        var report = await runTask;

        Assert.False(report.Passed);
        Assert.Contains("step 'input.click' timed out after 40ms", string.Join(";", report.Failures));

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task UiClickText_StalledClickFailsOnStepTimeout()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    if (req.Method == "input.click_text")
                        return;

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "draw.arm" => JsonDocument.Parse("{\"ok\":true,\"tick\":1}").RootElement,
                        "draw.text_find" => JsonDocument.Parse("{\"events\":[{}],\"count\":1}").RootElement,
                        "draw.disarm" => JsonDocument.Parse("{\"ok\":true}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var runTask = runner.RunAsync(new ScenarioSpec
        {
            Name = "ui_click_text_stalled_click",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "ui.click_text",
                    Args = JsonDocument.Parse("{\"text_equals\":\"SELL\",\"timeout_ms\":40,\"poll_ms\":1,\"capture_ticks\":1}").RootElement,
                },
            },
        }, cts.Token);

        var completed = await Task.WhenAny(runTask, Task.Delay(1000, cts.Token));
        Assert.Same(runTask, completed);
        var report = await runTask;

        Assert.False(report.Passed);
        Assert.Contains("step 'ui.click_text' timed out after 40ms while invoking input.click_text", string.Join(";", report.Failures));

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task StateAssertion_EvaluatesEqualityDsl()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.menu" => JsonDocument.Parse("{\"type\":\"ShopMenu\",\"present\":true,\"extra\":{}}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":1,\"assertions_passed\":1}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var spec = new ScenarioSpec
        {
            Name = "menu_check",
            Assertions = new()
            {
                new ScenarioAssertion { Type = "state", Expr = "state.menu.type == 'ShopMenu'" },
                new ScenarioAssertion { Type = "state", Expr = "state.menu.type == 'WrongMenu'" },
            },
        };
        var report = await runner.RunAsync(spec, cts.Token);

        Assert.Equal(2, report.AssertionsRun);
        Assert.Equal(1, report.AssertionsPassed);
        Assert.False(report.Passed);
        Assert.Single(report.Failures);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task StateAssertStep_EvaluatesStateDslDuringSteps()
    {
        var socket = SocketPath();
        var calls = new List<string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    calls.Add(req.Method);
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.menu" => JsonDocument.Parse("{\"type\":\"ShopMenu\",\"present\":true,\"extra\":{}}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "state_assert_step",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "state.assert",
                    Args = JsonDocument.Parse("{\"expr\":\"state.menu.type == 'ShopMenu'\"}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);
        Assert.Contains("state.menu", calls);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task StateAssertStep_StalledStateRpcFailsOnStepTimeout()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    if (req.Method == "state.menu")
                        return;

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var runTask = runner.RunAsync(new ScenarioSpec
        {
            Name = "state_assert_stalled_rpc",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "state.assert",
                    Args = JsonDocument.Parse("{\"expr\":\"state.menu.type == 'ShopMenu'\",\"timeout_ms\":40}").RootElement,
                },
            },
        }, cts.Token);

        var completed = await Task.WhenAny(runTask, Task.Delay(1000, cts.Token));
        Assert.Same(runTask, completed);
        var report = await runTask;

        Assert.False(report.Passed);
        Assert.Contains("step 'state.assert' timed out after 40ms", string.Join(";", report.Failures));

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task StateAssertStep_FailedAssertionDoesNotWaitOnStalledFailureScreenshot()
    {
        var socket = SocketPath();
        var tmp = Path.Combine(Path.GetTempPath(), $"state-assert-failure-capture-{Guid.NewGuid():N}");
        var rd = RunDirectory.Create(tmp);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    if (req.Method == "bitmap.capture")
                        return;

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.menu" => JsonDocument.Parse("{\"type\":\"TerminalMenu\",\"present\":true,\"extra\":{\"current_panel_type\":\"QuotePanel\"}}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        try
        {
            for (int i = 0; i < 40 && !File.Exists(socket); i++)
                await Task.Delay(50, cts.Token);

            using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
            _ = client.RunAsync(cts.Token);

            var runner = new ScenarioRunner(client, updateBaselines: false, reportDir: rd);
            var runTask = runner.RunAsync(new ScenarioSpec
            {
                Name = "state_assert_failed_capture_stall",
                Steps = new()
                {
                    new ScenarioStep
                    {
                        Action = "state.assert",
                        Args = JsonDocument.Parse("{\"expr\":\"state.menu.extra.current_panel_type == 'NewsPanel'\",\"message\":\"menu should be NewsPanel\"}").RootElement,
                    },
                },
            }, cts.Token);

            var completed = await Task.WhenAny(runTask, Task.Delay(1500, cts.Token));
            Assert.Same(runTask, completed);
            var report = await runTask;

            Assert.False(report.Passed);
            var failures = string.Join(";", report.Failures);
            Assert.Contains("step 'state.assert' failed: menu should be NewsPanel", failures);
            Assert.DoesNotContain("timed out", failures);
        }
        finally
        {
            cts.Cancel();
            try { await serverTask; } catch (OperationCanceledException) { }
            Directory.Delete(rd.Root, recursive: true);
        }
    }

    [Fact]
    public async Task StateAssert_ForwardsParamsToStateMethod()
    {
        var socket = SocketPath();
        JsonElement? stateNpcParams = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    if (req.Method == "state.npc")
                        stateNpcParams = req.Params;

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.npc" => JsonDocument.Parse("{\"name\":\"Sophia\",\"location\":\"Custom_BlueMoonVineyard\",\"tile\":{\"x\":20,\"y\":32},\"friendship_points\":500,\"hearts\":2,\"gift_given_today\":false,\"portrait\":\"Sophia\"}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "state_assert_params",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "state.assert",
                    Args = JsonDocument.Parse("{\"expr\":\"state.npc.hearts == 2\",\"params\":{\"name\":\"Sophia\"}}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);
        Assert.Equal("Sophia", stateNpcParams!.Value.GetProperty("name").GetString());

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task DrawContainsAssertion_CallsAssertContains()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "draw.assert_contains" => JsonDocument.Parse("{\"passed\":true,\"matched_count\":3,\"min_count\":1}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":1,\"assertions_passed\":1}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var spec = new ScenarioSpec
        {
            Name = "draw_check",
            Assertions = new()
            {
                new ScenarioAssertion
                {
                    Type = "draw.contains",
                    Filter = JsonDocument.Parse("{\"texture_asset\":\"Mods/X\"}").RootElement,
                    MinCount = 1,
                },
            },
        };
        var report = await runner.RunAsync(spec, cts.Token);

        Assert.True(report.Passed);
        Assert.Equal(1, report.AssertionsRun);
        Assert.Equal(1, report.AssertionsPassed);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task DrawTextAssertions_CallTextAssertRpcs()
    {
        var socket = SocketPath();
        var calls = new List<string>();
        var textContainsParams = new List<JsonElement>();
        var textNotContainsParams = default(JsonElement);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    calls.Add(req.Method);
                    if (req.Method == "draw.assert_text_contains" && req.Params is { } contains)
                        textContainsParams.Add(contains.Clone());
                    if (req.Method == "draw.assert_text_not_contains" && req.Params is { } notContains)
                        textNotContainsParams = notContains.Clone();

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "draw.assert_text_contains" => JsonDocument.Parse("{\"passed\":true,\"matched_count\":2,\"min_count\":1}").RootElement,
                        "draw.assert_text_not_contains" => JsonDocument.Parse("{\"passed\":true,\"matched_count\":0,\"min_count\":0}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":2,\"assertions_passed\":2}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var spec = new ScenarioSpec
        {
            Name = "text_draw_check",
            Assertions = new()
            {
                new ScenarioAssertion
                {
                    Type = "draw.text_contains",
                    Filter = JsonDocument.Parse("{\"text_contains\":\"CASH & WIRES\",\"case_sensitive\":true}").RootElement,
                    MinCount = 1,
                    Message = "Cash panel should be visible",
                },
                new ScenarioAssertion
                {
                    Type = "draw.text_not_contains",
                    Filter = JsonDocument.Parse("{\"text_contains\":\"ERROR\"}").RootElement,
                    Message = "Error text should be absent",
                },
                new ScenarioAssertion
                {
                    Type = "draw.text_contains",
                    Filter = JsonDocument.Parse("{\"text_equals\":\"0.00 SBD\",\"bounds_intersects_rect\":[560,190,310,40]}").RootElement,
                    MinCount = 1,
                    MaxCount = 1,
                    Message = "Unsettled cell should show zero",
                },
            },
        };

        var report = await runner.RunAsync(spec, cts.Token);

        Assert.True(report.Passed);
        Assert.Contains("draw.assert_text_contains", calls);
        Assert.Contains("draw.assert_text_not_contains", calls);
        Assert.Equal(2, textContainsParams.Count);
        Assert.Equal(1, textContainsParams[0].GetProperty("min_count").GetInt32());
        Assert.Equal("Cash panel should be visible", textContainsParams[0].GetProperty("message").GetString());
        Assert.Equal("CASH & WIRES", textContainsParams[0].GetProperty("filter").GetProperty("text_contains").GetString());
        Assert.Equal("0.00 SBD", textContainsParams[1].GetProperty("filter").GetProperty("text_equals").GetString());
        Assert.Equal(1, textContainsParams[1].GetProperty("max_count").GetInt32());
        Assert.Equal("Error text should be absent", textNotContainsParams.GetProperty("message").GetString());
        Assert.Equal("draw.text_contains \"CASH & WIRES\"", report.Assertions[0].Type);
        Assert.Equal("Cash panel should be visible", report.Assertions[0].Detail);
        Assert.Equal("draw.text_not_contains \"ERROR\"", report.Assertions[1].Type);
        Assert.Equal("draw.text_contains \"0.00 SBD\"", report.Assertions[2].Type);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task TextAllWithinAssertion_PassesWhenMatchingTextBoundsFitRegion()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var report = await RunSingleAssertionAsync(
            new ScenarioAssertion
            {
                Type = "draw.text_all_within",
                Filter = JsonDocument.Parse("{\"text_contains\":\"COMPLIANCE\",\"case_sensitive\":true}").RootElement,
                Region = JsonDocument.Parse("{\"x\":100,\"y\":100,\"w\":200,\"h\":80}").RootElement,
                Message = "Compliance text should fit the document pane",
            },
            req => req.Method switch
            {
                "draw.text_snapshot" => JsonRpcResponse.Ok(req.Id, JsonDocument.Parse(
                    "{\"events\":[{\"text\":\"COMPLIANCE WORKFLOW\",\"x\":120,\"y\":110,\"width\":170,\"height\":24,\"color\":[255,176,0,255],\"layer_depth\":0.9}],\"meta\":{\"ticks\":1,\"events\":1,\"dropped\":0}}").RootElement),
                _ => null,
            },
            cts);

        Assert.True(report.Passed);
        Assert.Single(report.Assertions);
        Assert.Equal("draw.text_all_within \"COMPLIANCE\"", report.Assertions[0].Type);
    }

    [Fact]
    public async Task TextAllWithinAssertion_FailsWhenMatchingTextBoundsOverflowRegion()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var report = await RunSingleAssertionAsync(
            new ScenarioAssertion
            {
                Type = "draw.text_all_within",
                Filter = JsonDocument.Parse("{\"text_contains\":\"CUSTOMER AGREEMENT\",\"case_sensitive\":true}").RootElement,
                Region = JsonDocument.Parse("[100,100,200,80]").RootElement,
                Message = "Agreement body text should fit the document pane",
            },
            req => req.Method switch
            {
                "draw.text_snapshot" => JsonRpcResponse.Ok(req.Id, JsonDocument.Parse(
                    "{\"events\":[{\"text\":\"CUSTOMER AGREEMENT BODY OVERFLOW\",\"x\":120,\"y\":110,\"width\":260,\"height\":24,\"color\":[255,255,255,255],\"layer_depth\":0.9}],\"meta\":{\"ticks\":1,\"events\":1,\"dropped\":0}}").RootElement),
                _ => null,
            },
            cts);

        Assert.False(report.Passed);
        var failure = Assert.Single(report.Failures);
        Assert.Contains("CUSTOMER AGREEMENT BODY OVERFLOW", failure);
        Assert.Contains("bounds [120,110,260,24] outside [100,100,200,80]", failure);
    }

    [Fact]
    public async Task TextAllWithinAssertion_ColorAnyIgnoresNonMatchingOverflow()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var report = await RunSingleAssertionAsync(
            new ScenarioAssertion
            {
                Type = "draw.text_all_within",
                Filter = JsonDocument.Parse("{\"text_contains\":\"PANE\",\"color_any\":[[236,229,206,255]]}").RootElement,
                Region = JsonDocument.Parse("{\"x\":100,\"y\":100,\"w\":200,\"h\":80}").RootElement,
                Message = "Only terminal palette text should be checked",
            },
            req => req.Method switch
            {
                "draw.text_snapshot" => JsonRpcResponse.Ok(req.Id, JsonDocument.Parse(
                    "{\"events\":[" +
                    "{\"text\":\"PANE OK\",\"x\":120,\"y\":110,\"width\":80,\"height\":24,\"color\":[236,229,206,255],\"layer_depth\":0.9}," +
                    "{\"text\":\"PANE HUD OVERFLOW\",\"x\":120,\"y\":110,\"width\":260,\"height\":24,\"color\":[120,80,40,255],\"layer_depth\":0.9}" +
                    "],\"meta\":{\"ticks\":1,\"events\":2,\"dropped\":0}}").RootElement),
                _ => null,
            },
            cts);

        Assert.True(report.Passed);
    }

    [Fact]
    public async Task DrawTextContainsFailure_IncludesMatchedAndMinCountDetail()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var report = await RunSingleAssertionAsync(
            new ScenarioAssertion
            {
                Type = "draw.text_contains",
                Filter = JsonDocument.Parse("{\"text_contains\":\"CASH\"}").RootElement,
                MinCount = 1,
            },
            req => req.Method == "draw.assert_text_contains"
                ? JsonRpcResponse.Ok(req.Id, JsonDocument.Parse("{\"passed\":false,\"matched_count\":0,\"min_count\":1}").RootElement)
                : null,
            cts);

        Assert.False(report.Passed);
        var failure = Assert.Single(report.Failures);
        Assert.Contains("draw.text_contains", failure);
        Assert.Contains("matched 0 < 1", failure);
    }

    [Fact]
    public async Task DrawTextContainsFailure_IncludesMatchedAndMaxCountDetail()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var report = await RunSingleAssertionAsync(
            new ScenarioAssertion
            {
                Type = "draw.text_contains",
                Filter = JsonDocument.Parse("{\"text_contains\":\"TERMINAL CLOSE archived\"}").RootElement,
                MinCount = 1,
                MaxCount = 1,
            },
            req => req.Method == "draw.assert_text_contains"
                ? JsonRpcResponse.Ok(req.Id, JsonDocument.Parse("{\"passed\":false,\"matched_count\":2,\"min_count\":1,\"max_count\":1}").RootElement)
                : null,
            cts);

        Assert.False(report.Passed);
        var failure = Assert.Single(report.Failures);
        Assert.Contains("draw.text_contains", failure);
        Assert.Contains("matched 2 > 1", failure);
    }

    [Fact]
    public async Task DrawTextNotContainsFailure_IncludesMatchedCountDetail()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var report = await RunSingleAssertionAsync(
            new ScenarioAssertion
            {
                Type = "draw.text_not_contains",
                Filter = JsonDocument.Parse("{\"text_contains\":\"ERROR\"}").RootElement,
            },
            req => req.Method == "draw.assert_text_not_contains"
                ? JsonRpcResponse.Ok(req.Id, JsonDocument.Parse("{\"passed\":false,\"matched_count\":3,\"min_count\":0}").RootElement)
                : null,
            cts);

        Assert.False(report.Passed);
        var failure = Assert.Single(report.Failures);
        Assert.Contains("draw.text_not_contains", failure);
        Assert.Contains("matched 3", failure);
    }

    [Fact]
    public async Task DrawTextAssertionRpcError_IncludesRpcErrorMessage()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var report = await RunSingleAssertionAsync(
            new ScenarioAssertion
            {
                Type = "draw.text_contains",
                Filter = JsonDocument.Parse("{\"text_contains\":\"CASH\"}").RootElement,
                MinCount = 1,
            },
            req => req.Method == "draw.assert_text_contains"
                ? JsonRpcResponse.Fail(req.Id, new JsonRpcError(JsonRpcErrorCode.InvalidParams, "bad text filter"))
                : null,
            cts);

        Assert.False(report.Passed);
        var failure = Assert.Single(report.Failures);
        Assert.Contains("draw.text_contains", failure);
        Assert.Contains("bad text filter", failure);
    }

    [Fact]
    public async Task TimeNextDay_RetriesTransientActiveWarp()
    {
        var socket = SocketPath();
        var nextDayCalls = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    if (req.Method == "time.next_day")
                    {
                        nextDayCalls++;
                        if (nextDayCalls == 1)
                        {
                            await session.SendResponseAsync(
                                JsonRpcResponse.Fail(req.Id, new JsonRpcError(JsonRpcErrorCode.GameStateInvalid, "time.next_day requires no active warp")),
                                tok);
                            return;
                        }
                    }

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var spec = new ScenarioSpec
        {
            Name = "next_day_after_close",
            Steps = new()
            {
                new ScenarioStep { Action = "time.next_day" },
            },
        };
        var report = await runner.RunAsync(spec, cts.Token);

        Assert.True(report.Passed);
        Assert.Equal(2, nextDayCalls);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task TimeNextDay_StalledRpcFailsOnSettleTimeout()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    if (req.Method == "time.next_day")
                        return;

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var runTask = runner.RunAsync(new ScenarioSpec
        {
            Name = "next_day_stalled_rpc",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "time.next_day",
                    Args = JsonDocument.Parse("{\"settle_timeout_ms\":40,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        var completed = await Task.WhenAny(runTask, Task.Delay(1000, cts.Token));
        Assert.Same(runTask, completed);
        var report = await runTask;

        Assert.False(report.Passed);
        Assert.Contains("step 'time.next_day' timed out after 40ms", string.Join(";", report.Failures));

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task FixtureSaveReload_SavesReturnsToTitleLoadsAndWaitsForWorldReady()
    {
        var socket = SocketPath();
        var calls = new List<string>();
        var timePolls = 0;
        var playerPolls = 0;
        var saveRoot = Path.Combine(Path.GetTempPath(), $"sdv-test-save-reload-{Guid.NewGuid():N}");
        var saveDir = Path.Combine(saveRoot, "test_save");
        Directory.CreateDirectory(saveDir);
        File.WriteAllText(Path.Combine(saveDir, "test_save"), "new-main");
        File.WriteAllText(Path.Combine(saveDir, "SaveGameInfo"), "new-info");
        File.WriteAllText(Path.Combine(saveDir, "test_save_old"), "old-main");
        File.WriteAllText(Path.Combine(saveDir, "SaveGameInfo_old"), "old-info");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    calls.Add(req.Method);

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "fixture.save" => JsonDocument.Parse(
                            "{\"ok\":true,\"tick\":10,\"save_path\":"
                            + JsonSerializer.Serialize(saveDir)
                            + "}").RootElement,
                        "game.return_to_title" => JsonDocument.Parse("{\"ok\":true,\"tick\":11}").RootElement,
                        "state.time" => JsonDocument.Parse(timePolls++ == 0
                            ? "{\"in_save\":true,\"season\":\"spring\",\"day_of_month\":2,\"year\":1,\"time_of_day\":900,\"day_of_week\":\"tuesday\"}"
                            : "{\"in_save\":false,\"season\":\"spring\",\"day_of_month\":0,\"year\":0,\"time_of_day\":0,\"day_of_week\":\"monday\"}").RootElement,
                        "fixture.load" => JsonDocument.Parse("{\"ok\":true,\"tick\":12}").RootElement,
                        "state.player" => JsonDocument.Parse(playerPolls++ == 0
                            ? "{\"name\":\"Tester\",\"money\":500,\"stamina\":270,\"max_stamina\":270,\"health\":100,\"location\":\"\",\"tile\":{\"x\":0,\"y\":0},\"items\":[]}"
                            : "{\"name\":\"Tester\",\"money\":500,\"stamina\":270,\"max_stamina\":270,\"health\":100,\"location\":\"FarmHouse\",\"tile\":{\"x\":8,\"y\":10},\"items\":[]}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var spec = new ScenarioSpec
        {
            Name = "save_reload",
            Fixture = "test_save",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "fixture.save_reload",
                    Args = JsonDocument.Parse("{\"settle_timeout_ms\":500,\"poll_ms\":1}").RootElement,
                },
            },
        };
        var report = await runner.RunAsync(spec, cts.Token);

        Assert.True(report.Passed);
        Assert.Contains(calls, c => c == "fixture.save");
        Assert.Contains(calls, c => c == "game.return_to_title");
        Assert.Contains(calls, c => c == "fixture.load");
        Assert.True(calls.IndexOf("fixture.save") < calls.IndexOf("game.return_to_title"));
        Assert.True(calls.IndexOf("game.return_to_title") < calls.LastIndexOf("fixture.load"));
        Assert.True(timePolls >= 2);
        Assert.True(playerPolls >= 2);
        Assert.Equal("old-main", File.ReadAllText(Path.Combine(saveDir, "test_save")));
        Assert.Equal("old-info", File.ReadAllText(Path.Combine(saveDir, "SaveGameInfo")));

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
        Directory.Delete(saveRoot, recursive: true);
    }

    [Fact]
    public async Task FreezeBegin_RetriesTransientMidWarp()
    {
        var socket = SocketPath();
        var freezeCalls = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    if (req.Method == "freeze.begin")
                    {
                        freezeCalls++;
                        if (freezeCalls == 1)
                        {
                            await session.SendResponseAsync(
                                JsonRpcResponse.Fail(req.Id, new JsonRpcError(JsonRpcErrorCode.GameStateInvalid, "freeze.begin requires !Game1.isWarping (mid-warp)")),
                                tok);
                            return;
                        }
                    }

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var spec = new ScenarioSpec
        {
            Name = "freeze_after_warp",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "freeze.begin",
                    Args = JsonDocument.Parse("{\"settle_timeout_ms\":100,\"poll_ms\":1}").RootElement,
                },
            },
        };
        var report = await runner.RunAsync(spec, cts.Token);

        Assert.True(report.Passed);
        Assert.Equal(2, freezeCalls);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task StepFailure_RecordedInFailures()
    {
        var socket = SocketPath();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    if (req.Method == "player.warp")
                    {
                        await session.SendResponseAsync(
                            JsonRpcResponse.Fail(req.Id, new JsonRpcError(JsonRpcErrorCode.GameStateInvalid, "no such location")),
                            tok);
                        return;
                    }
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var spec = new ScenarioSpec
        {
            Name = "warp_fail",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "player.warp",
                    Args = JsonDocument.Parse("{\"location\":\"Nowhere\",\"x\":0,\"y\":0}").RootElement,
                },
            },
        };
        var report = await runner.RunAsync(spec, cts.Token);

        Assert.False(report.Passed);
        Assert.Contains("player.warp", string.Join(";", report.Failures));

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }
}
