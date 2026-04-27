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
}
