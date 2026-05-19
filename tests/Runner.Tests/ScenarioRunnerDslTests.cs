using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Models;
using SdvTestFramework.Runner.Scenarios;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

/// <summary>
/// Exercises the state-assertion DSL's integer + boolean literal support added after T15
/// review. Lives in a separate file so the main ScenarioRunnerTests stays focused on
/// control-flow cases.
/// </summary>
public class ScenarioRunnerDslTests
{
    private static string SocketPath() => Path.Combine(Path.GetTempPath(), $"sdv-test-{Guid.NewGuid():N}.sock");

    private static async Task<(CancellationTokenSource Cts, Task Server, JsonRpcSession Client)> StartFakeHarness(string socket)
        => await StartFakeHarnessWithPlayerJson(socket,
            "{\"name\":\"Tester\",\"money\":1000,\"stamina\":270,\"max_stamina\":270,\"health\":100,\"location\":\"Farm\",\"tile\":{\"x\":0,\"y\":0}}");

    private static async Task<(CancellationTokenSource Cts, Task Server, JsonRpcSession Client)> StartFakeHarnessWithPlayerJson(
        string socket, string playerJson)
        => await StartFakeHarnessWithStateJson(socket, playerJson, null);

    private static async Task<(CancellationTokenSource Cts, Task Server, JsonRpcSession Client)> StartFakeHarnessWithStateJson(
        string socket, string playerJson, string? modsJson)
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.player" => JsonDocument.Parse(playerJson).RootElement,
                        "state.mods" => JsonDocument.Parse(modsJson ?? "{\"unique_ids\":[],\"mods\":[]}").RootElement,
                        "state.menu" => JsonDocument.Parse(
                            "{\"type\":\"\",\"present\":false,\"extra\":{}}").RootElement,
                        "scenario.end" => JsonDocument.Parse(
                            "{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);
        for (int i = 0; i < 40 && !File.Exists(socket); i++) await Task.Delay(50, cts.Token);
        var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);
        return (cts, serverTask, client);
    }

    [Fact]
    public async Task StateAssertion_IntegerLiteral_Matches()
    {
        var (cts, server, client) = await StartFakeHarness(SocketPath());
        using var _ = cts; using var __ = client;

        var runner = new ScenarioRunner(client);
        var spec = new ScenarioSpec
        {
            Name = "int_literal",
            Assertions = new()
            {
                new ScenarioAssertion { Type = "state", Expr = "state.player.money == 1000" },
                new ScenarioAssertion { Type = "state", Expr = "state.player.money == 500" },
            },
        };
        var report = await runner.RunAsync(spec, cts.Token);

        Assert.Equal(2, report.AssertionsRun);
        Assert.Equal(1, report.AssertionsPassed);
        cts.Cancel();
        try { await server; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task StateAssertion_FailingComparison_ReportsExpressionDetail()
    {
        var socket = SocketPath();
        var (cts, server, client) = await StartFakeHarnessWithPlayerJson(socket,
            "{\"name\":\"Tester\",\"money\":499,\"stamina\":0,\"max_stamina\":0,\"health\":0,\"location\":\"Farm\",\"tile\":{\"x\":0,\"y\":0}}");
        using var _ = cts;
        using var __ = client;

        var runner = new ScenarioRunner(client);
        var spec = new ScenarioSpec
        {
            Name = "state_failure_detail",
            Assertions = new()
            {
                new ScenarioAssertion
                {
                    Type = "state",
                    Expr = "state.player.money == 500",
                    Message = "money seeded",
                },
            },
        };

        var report = await runner.RunAsync(spec, cts.Token);

        Assert.False(report.Passed);
        Assert.Contains(report.Failures, failure =>
            failure.Contains("money seeded", StringComparison.Ordinal)
            && failure.Contains("state.player.money", StringComparison.Ordinal));
        cts.Cancel();
        try { await server; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task StateAssertion_BooleanLiteral_Matches()
    {
        var (cts, server, client) = await StartFakeHarness(SocketPath());
        using var _ = cts; using var __ = client;

        var runner = new ScenarioRunner(client);
        var spec = new ScenarioSpec
        {
            Name = "bool_literal",
            Assertions = new()
            {
                new ScenarioAssertion { Type = "state", Expr = "state.menu.present == false" },
                new ScenarioAssertion { Type = "state", Expr = "state.menu.present == true" },
            },
        };
        var report = await runner.RunAsync(spec, cts.Token);

        Assert.Equal(2, report.AssertionsRun);
        Assert.Equal(1, report.AssertionsPassed);
        cts.Cancel();
        try { await server; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task StateAssertion_NotEquals_MismatchedValues_Passes()
    {
        // "state.player.name != 'Tester'" where the mock state returns name=="Wrong" → passes.
        var socket = SocketPath();
        var (cts, server, client) = await StartFakeHarnessWithPlayerJson(socket,
            "{\"name\":\"Wrong\",\"money\":0,\"stamina\":0,\"max_stamina\":0,\"health\":0,\"location\":\"Farm\",\"tile\":{\"x\":0,\"y\":0}}");
        using var _ = cts; using var __ = client;

        var runner = new ScenarioRunner(client);
        var spec = new ScenarioSpec
        {
            Name = "neq_mismatched",
            Assertions = new()
            {
                new ScenarioAssertion { Type = "state", Expr = "state.player.name != 'Tester'" },
            },
        };
        var report = await runner.RunAsync(spec, cts.Token);

        Assert.Equal(1, report.AssertionsRun);
        Assert.Equal(1, report.AssertionsPassed);
        cts.Cancel();
        try { await server; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task StateAssertion_NotEquals_EqualValues_Fails()
    {
        // "state.player.name != 'Tester'" where the mock state returns name=="Tester" → fails.
        var socket = SocketPath();
        var (cts, server, client) = await StartFakeHarnessWithPlayerJson(socket,
            "{\"name\":\"Tester\",\"money\":0,\"stamina\":0,\"max_stamina\":0,\"health\":0,\"location\":\"Farm\",\"tile\":{\"x\":0,\"y\":0}}");
        using var _ = cts; using var __ = client;

        var runner = new ScenarioRunner(client);
        var spec = new ScenarioSpec
        {
            Name = "neq_equal",
            Assertions = new()
            {
                new ScenarioAssertion { Type = "state", Expr = "state.player.name != 'Tester'" },
            },
        };
        var report = await runner.RunAsync(spec, cts.Token);

        Assert.Equal(1, report.AssertionsRun);
        Assert.Equal(0, report.AssertionsPassed);
        cts.Cancel();
        try { await server; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task StateAssertion_ArrayIndex_ValidIndex_ResolvesElement()
    {
        // state.player.items[0].id == 'O388' — element 0's id field matches.
        var socket = SocketPath();
        var (cts, server, client) = await StartFakeHarnessWithPlayerJson(socket,
            "{\"name\":\"x\",\"money\":0,\"stamina\":0,\"max_stamina\":0,\"health\":0,\"location\":\"Farm\",\"tile\":{\"x\":0,\"y\":0},\"items\":[{\"id\":\"O388\",\"count\":3},{\"id\":\"O390\",\"count\":1}]}");
        using var _ = cts; using var __ = client;

        var runner = new ScenarioRunner(client);
        var spec = new ScenarioSpec
        {
            Name = "array_index_valid",
            Assertions = new()
            {
                new ScenarioAssertion { Type = "state", Expr = "state.player.items[0].id == 'O388'" },
            },
        };
        var report = await runner.RunAsync(spec, cts.Token);

        Assert.Equal(1, report.AssertionsRun);
        Assert.Equal(1, report.AssertionsPassed);
        cts.Cancel();
        try { await server; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task StateAssertion_ArrayIndex_OutOfRange_Fails()
    {
        // state.player.items[5].id == 'O388' — items has only 1 element; index 5 is out of range.
        var socket = SocketPath();
        var (cts, server, client) = await StartFakeHarnessWithPlayerJson(socket,
            "{\"name\":\"x\",\"money\":0,\"stamina\":0,\"max_stamina\":0,\"health\":0,\"location\":\"Farm\",\"tile\":{\"x\":0,\"y\":0},\"items\":[{\"id\":\"O388\"}]}");
        using var _ = cts; using var __ = client;

        var runner = new ScenarioRunner(client);
        var spec = new ScenarioSpec
        {
            Name = "array_index_oob",
            Assertions = new()
            {
                new ScenarioAssertion { Type = "state", Expr = "state.player.items[5].id == 'O388'" },
            },
        };
        var report = await runner.RunAsync(spec, cts.Token);

        Assert.Equal(1, report.AssertionsRun);
        Assert.Equal(0, report.AssertionsPassed);
        cts.Cancel();
        try { await server; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task StateAssertion_StringArrayContains_Matches()
    {
        var socket = SocketPath();
        var (cts, server, client) = await StartFakeHarnessWithStateJson(
            socket,
            "{\"name\":\"x\",\"money\":0,\"stamina\":0,\"max_stamina\":0,\"health\":0,\"location\":\"Farm\",\"tile\":{\"x\":0,\"y\":0}}",
            "{\"unique_ids\":[\"FlashShifter.SVECode\",\"Pathoschild.ContentPatcher\"],\"mods\":[]}");
        using var _ = cts; using var __ = client;

        var runner = new ScenarioRunner(client);
        var spec = new ScenarioSpec
        {
            Name = "string_array_contains",
            Assertions = new()
            {
                new ScenarioAssertion { Type = "state", Expr = "state.mods.unique_ids contains 'FlashShifter.SVECode'" },
                new ScenarioAssertion { Type = "state", Expr = "state.mods.unique_ids contains 'Missing.Mod'" },
            },
        };
        var report = await runner.RunAsync(spec, cts.Token);

        Assert.Equal(2, report.AssertionsRun);
        Assert.Equal(1, report.AssertionsPassed);
        Assert.Single(report.Failures);
        cts.Cancel();
        try { await server; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task StateAssertion_NumberArrayContains_Matches()
    {
        var socket = SocketPath();
        var (cts, server, client) = await StartFakeHarnessWithStateJson(
            socket,
            "{\"name\":\"x\",\"money\":0,\"stamina\":0,\"max_stamina\":0,\"health\":0,\"location\":\"Farm\",\"tile\":{\"x\":0,\"y\":0},\"secret_notes_seen\":[18]}",
            "{\"unique_ids\":[],\"mods\":[]}");
        using var _ = cts; using var __ = client;

        var runner = new ScenarioRunner(client);
        var spec = new ScenarioSpec
        {
            Name = "number_array_contains",
            Assertions = new()
            {
                new ScenarioAssertion { Type = "state", Expr = "state.player.secret_notes_seen contains 18" },
                new ScenarioAssertion { Type = "state", Expr = "state.player.secret_notes_seen contains 12" },
            },
        };
        var report = await runner.RunAsync(spec, cts.Token);

        Assert.Equal(2, report.AssertionsRun);
        Assert.Equal(1, report.AssertionsPassed);
        Assert.Single(report.Failures);
        cts.Cancel();
        try { await server; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task StateAssertion_StringArrayNotContains_MatchesAbsence()
    {
        var socket = SocketPath();
        var (cts, server, client) = await StartFakeHarnessWithStateJson(
            socket,
            "{\"name\":\"x\",\"money\":0,\"stamina\":0,\"max_stamina\":0,\"health\":0,\"location\":\"Farm\",\"tile\":{\"x\":0,\"y\":0}}",
            "{\"unique_ids\":[\"FlashShifter.SVECode\",\"Pathoschild.ContentPatcher\"],\"mods\":[]}");
        using var _ = cts; using var __ = client;

        var runner = new ScenarioRunner(client);
        var spec = new ScenarioSpec
        {
            Name = "string_array_not_contains",
            Assertions = new()
            {
                new ScenarioAssertion { Type = "state", Expr = "state.mods.unique_ids not contains 'Missing.Mod'" },
                new ScenarioAssertion { Type = "state", Expr = "state.mods.unique_ids not contains 'FlashShifter.SVECode'" },
            },
        };
        var report = await runner.RunAsync(spec, cts.Token);

        Assert.Equal(2, report.AssertionsRun);
        Assert.Equal(1, report.AssertionsPassed);
        Assert.Single(report.Failures);
        cts.Cancel();
        try { await server; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task StateAssertion_ObjectArrayContains_MatchesField()
    {
        var socket = SocketPath();
        var (cts, server, client) = await StartFakeHarnessWithStateJson(
            socket,
            "{\"name\":\"x\",\"money\":0,\"stamina\":0,\"max_stamina\":0,\"health\":0,\"location\":\"Farm\",\"tile\":{\"x\":0,\"y\":0}}",
            "{\"unique_ids\":[],\"mods\":[{\"unique_id\":\"FlashShifter.SVECode\",\"name\":\"SVE\",\"version\":\"1.0.0\"},{\"unique_id\":\"Pathoschild.ContentPatcher\",\"name\":\"CP\",\"version\":\"2.0.0\"}]}");
        using var _ = cts; using var __ = client;

        var runner = new ScenarioRunner(client);
        var spec = new ScenarioSpec
        {
            Name = "object_array_contains",
            Assertions = new()
            {
                new ScenarioAssertion { Type = "state", Expr = "state.mods.mods contains unique_id 'FlashShifter.SVECode'" },
                new ScenarioAssertion { Type = "state", Expr = "state.mods.mods contains unique_id 'Missing.Mod'" },
            },
        };
        var report = await runner.RunAsync(spec, cts.Token);

        Assert.Equal(2, report.AssertionsRun);
        Assert.Equal(1, report.AssertionsPassed);
        Assert.Single(report.Failures);
        cts.Cancel();
        try { await server; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task StateAssertion_ObjectArrayNotContains_MatchesFieldAbsence()
    {
        var socket = SocketPath();
        var (cts, server, client) = await StartFakeHarnessWithStateJson(
            socket,
            "{\"name\":\"x\",\"money\":0,\"stamina\":0,\"max_stamina\":0,\"health\":0,\"location\":\"Farm\",\"tile\":{\"x\":0,\"y\":0}}",
            "{\"unique_ids\":[],\"mods\":[{\"unique_id\":\"FlashShifter.SVECode\",\"name\":\"SVE\",\"version\":\"1.0.0\"},{\"unique_id\":\"Pathoschild.ContentPatcher\",\"name\":\"CP\",\"version\":\"2.0.0\"}]}");
        using var _ = cts; using var __ = client;

        var runner = new ScenarioRunner(client);
        var spec = new ScenarioSpec
        {
            Name = "object_array_not_contains",
            Assertions = new()
            {
                new ScenarioAssertion { Type = "state", Expr = "state.mods.mods not contains unique_id 'Missing.Mod'" },
                new ScenarioAssertion { Type = "state", Expr = "state.mods.mods not contains unique_id 'FlashShifter.SVECode'" },
            },
        };
        var report = await runner.RunAsync(spec, cts.Token);

        Assert.Equal(2, report.AssertionsRun);
        Assert.Equal(1, report.AssertionsPassed);
        Assert.Single(report.Failures);
        cts.Cancel();
        try { await server; } catch (OperationCanceledException) { }
    }
}
