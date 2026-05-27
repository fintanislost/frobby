using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Models;
using SdvTestFramework.Runner.Scenarios;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class ScenarioRunnerUiAcknowledgeTests
{
    private static string SocketPath() => Path.Combine(Path.GetTempPath(), $"sdv-test-{Guid.NewGuid():N}.sock");

    [Theory]
    [InlineData("ui.acknowledge")]
    [InlineData("event.advance")]
    public async Task MenuAdvanceAction_ClicksActiveMenuAdvanceControl(string action)
    {
        var socket = SocketPath();
        var calls = new List<string>();
        var menuPolls = 0;
        JsonElement? clickParams = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    calls.Add(req.Method);
                    if (req.Method == "input.click_menu_advance")
                        clickParams = req.Params?.Clone();

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.menu" when menuPolls++ == 0 => JsonDocument.Parse(
                            "{\"type\":\"\",\"present\":false,\"extra\":{},\"choices\":[]}").RootElement,
                        "state.menu" => JsonDocument.Parse(
                            "{\"type\":\"DialogueBox\",\"present\":true,\"bounds\":{\"x\":100,\"y\":200,\"width\":640,\"height\":240},\"extra\":{},\"choices\":[]}").RootElement,
                        "input.click_menu_advance" => JsonDocument.Parse("{\"ok\":true,\"tick\":5}").RootElement,
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
            Name = "menu_advance",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = action,
                    Args = JsonDocument.Parse("{\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);
        Assert.Contains("state.menu", calls);
        Assert.Contains("input.click_menu_advance", calls);
        Assert.Equal("left", clickParams?.GetProperty("button").GetString());

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task MenuAdvanceAction_WithRepeat_ClicksActiveMenuMultipleTimes()
    {
        var socket = SocketPath();
        var calls = new List<string>();
        var clickCount = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    calls.Add(req.Method);
                    if (req.Method == "input.click_menu_advance")
                        clickCount++;

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.menu" => JsonDocument.Parse(
                            "{\"type\":\"DialogueBox\",\"present\":true,\"bounds\":{\"x\":100,\"y\":200,\"width\":640,\"height\":240},\"extra\":{},\"choices\":[]}").RootElement,
                        "input.click_menu_advance" => JsonDocument.Parse("{\"ok\":true,\"tick\":5}").RootElement,
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
            Name = "menu_advance_repeat",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "event.advance",
                    Args = JsonDocument.Parse("{\"timeout_ms\":1000,\"poll_ms\":1,\"repeat\":2,\"interval_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);
        Assert.Equal(2, clickCount);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task MenuAdvanceAction_UntilClosed_WaitsForDialogueReadyBetweenClicks()
    {
        var socket = SocketPath();
        var statePolls = 0;
        var clickCount = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    if (req.Method == "input.click_menu_advance")
                        clickCount++;

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.menu" => NextMenuState(ref statePolls),
                        "input.click_menu_advance" => JsonDocument.Parse("{\"ok\":true,\"tick\":5}").RootElement,
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
            Name = "menu_advance_until_closed",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "event.advance",
                    Args = JsonDocument.Parse("{\"timeout_ms\":1000,\"poll_ms\":1,\"until_closed\":true,\"max_clicks\":5,\"interval_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);
        Assert.Equal(2, clickCount);
        Assert.True(statePolls >= 5);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }

        static JsonElement NextMenuState(ref int statePolls)
        {
            var json = statePolls++ switch
            {
                0 => "{\"type\":\"DialogueBox\",\"present\":true,\"bounds\":{\"x\":100,\"y\":200,\"width\":640,\"height\":240},\"extra\":{\"dialogue_ready\":\"false\"},\"choices\":[]}",
                1 => "{\"type\":\"DialogueBox\",\"present\":true,\"bounds\":{\"x\":100,\"y\":200,\"width\":640,\"height\":240},\"extra\":{\"dialogue_ready\":\"true\"},\"choices\":[]}",
                2 => "{\"type\":\"DialogueBox\",\"present\":true,\"bounds\":{\"x\":100,\"y\":200,\"width\":640,\"height\":240},\"extra\":{\"dialogue_ready\":\"false\"},\"choices\":[]}",
                3 => "{\"type\":\"DialogueBox\",\"present\":true,\"bounds\":{\"x\":100,\"y\":200,\"width\":640,\"height\":240},\"extra\":{\"dialogue_ready\":\"true\"},\"choices\":[]}",
                _ => "{\"type\":\"\",\"present\":false,\"extra\":{},\"choices\":[]}",
            };
            return JsonDocument.Parse(json).RootElement;
        }
    }
}
