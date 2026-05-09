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

public class ScenarioRunnerMenuChoiceTests
{
    private static string SocketPath() => Path.Combine(Path.GetTempPath(), $"sdv-test-{Guid.NewGuid():N}.sock");

    [Fact]
    public async Task WaitMenu_PollsUntilChoiceTextMatches()
    {
        var (report, calls, _) = await RunMenuScenarioAsync(new ScenarioStep
        {
            Action = "wait.menu",
            Args = JsonDocument.Parse("{\"choice_text\":\"Pet Dusty\",\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
        });

        Assert.True(report.Passed);
        Assert.Equal(2, calls.FindAll(c => c == "state.menu").Count);
    }

    [Fact]
    public async Task EventAdvance_WithChoiceText_WaitsAndClicksChoice()
    {
        var (report, calls, clickParams) = await RunMenuScenarioAsync(new ScenarioStep
        {
            Action = "event.advance",
            Args = JsonDocument.Parse("{\"choice_text\":\"Pet Dusty\",\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
        });

        Assert.True(report.Passed);
        Assert.Contains("state.menu", calls);
        Assert.Contains("input.click_menu_choice", calls);
        Assert.Equal("Pet Dusty", clickParams?.GetProperty("text_equals").GetString());
    }

    [Fact]
    public async Task WaitMenu_WithReady_PollsUntilDialogueReady()
    {
        var (report, calls, _) = await RunMenuScenarioAsync(new ScenarioStep
        {
            Action = "wait.menu",
            Args = JsonDocument.Parse("{\"text\":\"!!!\",\"ready\":true,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
        }, readyScenario: true);

        Assert.True(report.Passed);
        Assert.Equal(2, calls.FindAll(c => c == "state.menu").Count);
    }

    private static async Task<(ScenarioReport Report, List<string> Calls, JsonElement? ClickParams)> RunMenuScenarioAsync(
        ScenarioStep step,
        bool readyScenario = false)
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
                    if (req.Method == "input.click_menu_choice")
                        clickParams = req.Params?.Clone();

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.menu" when readyScenario && menuPolls++ == 0 => JsonDocument.Parse(
                            "{\"type\":\"DialogueBox\",\"present\":true,\"bounds\":{\"x\":100,\"y\":200,\"width\":640,\"height\":240},\"extra\":{\"dialogue_text\":\"!!!\",\"dialogue_ready\":\"false\"},\"choices\":[]}").RootElement,
                        "state.menu" when readyScenario => JsonDocument.Parse(
                            "{\"type\":\"DialogueBox\",\"present\":true,\"bounds\":{\"x\":100,\"y\":200,\"width\":640,\"height\":240},\"extra\":{\"dialogue_text\":\"!!!\",\"dialogue_ready\":\"true\"},\"choices\":[]}").RootElement,
                        "state.menu" when menuPolls++ == 0 => JsonDocument.Parse(
                            "{\"type\":\"\",\"present\":false,\"extra\":{},\"choices\":[]}").RootElement,
                        "state.menu" => JsonDocument.Parse(
                            "{\"type\":\"DialogueBox\",\"present\":true,\"bounds\":{\"x\":100,\"y\":200,\"width\":640,\"height\":240},\"extra\":{\"question_text\":\"What should I do?\"},\"choices\":[{\"key\":\"pet\",\"text\":\"Pet Dusty\"}]}").RootElement,
                        "input.click_menu_choice" => JsonDocument.Parse("{\"ok\":true,\"tick\":5}").RootElement,
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
            Name = "menu_choice",
            Steps = new() { step },
        }, cts.Token);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
        return (report, calls, clickParams);
    }
}
