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
    [InlineData("ui.click_text", true)]
    [InlineData("ui.hover_text", true)]
    [InlineData("input.click_text", true)]
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
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    calls.Add(req.Method);
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
        Assert.Equal(new[] { "scenario.begin", "draw.arm", "draw.text_find", "draw.disarm", "input.hover_text", "scenario.end" }, calls);
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
