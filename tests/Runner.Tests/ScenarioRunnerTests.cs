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
    [InlineData("ui.wait_text", false)]
    [InlineData("ui.click_text", true)]
    [InlineData("input.click_text", true)]
    [InlineData("freeze.begin", true)]
    public void ShouldAutoCaptureStep_SkipsTimingAndInstrumentationSteps(string action, bool expected)
    {
        var step = new ScenarioStep { Action = action };

        Assert.Equal(expected, ScenarioRunner.ShouldAutoCaptureStep(step));
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
                        "draw.text_find" => JsonDocument.Parse("{\"events\":[{}],\"count\":1}").RootElement,
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
                    Args = JsonDocument.Parse("{\"text\":\"SUBMIT ORDER\",\"case_sensitive\":false,\"timeout_ms\":1000,\"poll_ms\":1,\"capture_ticks\":3,\"bounds_intersects_rect\":[700,500,120,40]}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);
        Assert.Equal(2, findCalls);
        Assert.Contains("draw.arm", calls);
        Assert.DoesNotContain("input.click_text", calls);
        Assert.Equal(3, armParams.GetProperty("ticks").GetInt32());
        Assert.Equal("SUBMIT ORDER", findParams.GetProperty("text_contains").GetString());
        Assert.False(findParams.GetProperty("case_sensitive").GetBoolean());
        Assert.Equal(700, findParams.GetProperty("bounds_intersects_rect")[0].GetInt32());

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task UiClickText_WaitsThenClicksMatchedText()
    {
        var socket = SocketPath();
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
        Assert.Equal(new[] { "scenario.begin", "draw.arm", "draw.text_find", "draw.disarm", "input.click_text", "scenario.end" }, calls);
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
                        "draw.text_find" => JsonDocument.Parse("{\"events\":[{}],\"count\":1}").RootElement,
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
        Assert.Equal("Error text should be absent", textNotContainsParams.GetProperty("message").GetString());
        Assert.Equal("draw.text_contains \"CASH & WIRES\"", report.Assertions[0].Type);
        Assert.Equal("Cash panel should be visible", report.Assertions[0].Detail);
        Assert.Equal("draw.text_not_contains \"ERROR\"", report.Assertions[1].Type);
        Assert.Equal("draw.text_contains \"0.00 SBD\"", report.Assertions[2].Type);

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
