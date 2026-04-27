using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Models;
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
        var textContainsParams = default(JsonElement);
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
                        textContainsParams = contains.Clone();
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
            },
        };

        var report = await runner.RunAsync(spec, cts.Token);

        Assert.True(report.Passed);
        Assert.Contains("draw.assert_text_contains", calls);
        Assert.Contains("draw.assert_text_not_contains", calls);
        Assert.Equal(1, textContainsParams.GetProperty("min_count").GetInt32());
        Assert.Equal("Cash panel should be visible", textContainsParams.GetProperty("message").GetString());
        Assert.Equal("CASH & WIRES", textContainsParams.GetProperty("filter").GetProperty("text_contains").GetString());
        Assert.Equal("Error text should be absent", textNotContainsParams.GetProperty("message").GetString());

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
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
