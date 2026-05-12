using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using SdvTestFramework.Runner.Scenarios;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class ScenarioRunnerFishingTests
{
    private static string SocketPath() => Path.Combine(Path.GetTempPath(), $"sdv-test-{Guid.NewGuid():N}.sock");

    [Fact]
    public async Task FishingTableAssertion_EvaluatesContainsExpression()
    {
        var (cts, server, client, calls) = await StartFakeHarness(SocketPath());
        using var _ = cts;
        using var __ = client;

        var runner = new ScenarioRunner(client);
        var spec = new ScenarioSpec
        {
            Name = "fishing_table_assertion",
            Assertions = new()
            {
                new ScenarioAssertion
                {
                    Type = "state.fishing_table",
                    Params = ProtocolJson.ToElement(new FishingTableRequest
                    {
                        Location = "Desert",
                        X = 28,
                        Y = 6,
                    }),
                    Expr = "result.candidates contains item_id '164'",
                },
            },
        };

        var report = await runner.RunAsync(spec, cts.Token);

        Assert.True(report.Passed);
        Assert.Equal(1, report.AssertionsPassed);
        Assert.Contains("state.fishing_table", calls);
        cts.Cancel();
        try { await server; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task FishingSampleAssertion_EvaluatesResultExpression()
    {
        var (cts, server, client, calls) = await StartFakeHarness(SocketPath());
        using var _ = cts;
        using var __ = client;

        var runner = new ScenarioRunner(client);
        var spec = new ScenarioSpec
        {
            Name = "fishing_sample_assertion",
            Assertions = new()
            {
                new ScenarioAssertion
                {
                    Type = "fishing.sample_catch",
                    Params = ProtocolJson.ToElement(new FishingSampleCatchRequest
                    {
                        Location = "Desert",
                        X = 28,
                        Y = 6,
                        Attempts = 2,
                        Seed = 1234,
                    }),
                    Expr = "result.results contains display_name 'Sandfish'",
                },
                new ScenarioAssertion
                {
                    Type = "fishing.sample_catch",
                    Params = ProtocolJson.ToElement(new FishingSampleCatchRequest
                    {
                        Location = "Desert",
                        X = 28,
                        Y = 6,
                        Attempts = 2,
                        Seed = 1234,
                    }),
                    Expr = "result.state_restored == true",
                },
            },
        };

        var report = await runner.RunAsync(spec, cts.Token);

        Assert.True(report.Passed);
        Assert.Equal(2, report.AssertionsPassed);
        Assert.Equal(2, calls.FindAll(call => call == "fishing.sample_catch").Count);
        cts.Cancel();
        try { await server; } catch (OperationCanceledException) { }
    }

    private static async Task<(CancellationTokenSource Cts, Task Server, JsonRpcSession Client, List<string> Calls)> StartFakeHarness(
        string socket)
    {
        var calls = new List<string>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
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
                        "state.fishing_table" => JsonDocument.Parse("""
                        {
                          "location": "Desert",
                          "location_name": "Desert",
                          "is_fishable": true,
                          "candidates": [
                            { "item_id": "2334", "qualified_id": "(F)2334", "display_name": "Pyramid Decal", "type": "furniture" },
                            { "item_id": "164", "qualified_id": "(O)164", "display_name": "Sandfish", "type": "fish" }
                          ],
                          "raw": {}
                        }
                        """).RootElement,
                        "fishing.sample_catch" => JsonDocument.Parse("""
                        {
                          "location": "Desert",
                          "tile": { "x": 28, "y": 6 },
                          "attempts": 2,
                          "state_restored": true,
                          "results": [
                            { "attempt": 1, "item_id": "2334", "qualified_id": "(F)2334", "display_name": "Pyramid Decal", "type": "furniture" },
                            { "attempt": 2, "item_id": "164", "qualified_id": "(O)164", "display_name": "Sandfish", "type": "fish" }
                          ]
                        }
                        """).RootElement,
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

        for (var i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);
        return (cts, serverTask, client, calls);
    }
}
