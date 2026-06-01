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

public class ScenarioRunnerContentAssetTests
{
    private static string SocketPath() => Path.Combine(Path.GetTempPath(), $"sdv-test-{Guid.NewGuid():N}.sock");

    [Fact]
    public async Task ContentAssetAssertion_EvaluatesContainsExpression()
    {
        var (cts, server, client, calls) = await StartFakeHarness(SocketPath(), """
        {
          "name": "Maps/Custom_TownEast",
          "exists": true,
          "kind": "map",
          "runtime_type": "xTile.Map",
          "summary": {
            "width": 90,
            "height": 64,
            "layers": [ { "name": "Back" }, { "name": "Buildings" } ]
          }
        }
        """);
        using var _ = cts;
        using var __ = client;

        var runner = new ScenarioRunner(client);
        var spec = new ScenarioSpec
        {
            Name = "content_asset_contains",
            Assertions = new()
            {
                new ScenarioAssertion
                {
                    Type = "content.asset",
                    Asset = "Maps/Custom_TownEast",
                    AssetType = "map",
                    Expr = "asset.layers contains name 'Back'",
                },
            },
        };

        var report = await runner.RunAsync(spec, cts.Token);

        Assert.True(report.Passed, string.Join(Environment.NewLine, report.Failures));
        Assert.Equal(1, report.AssertionsPassed);
        Assert.Contains("content.asset", calls);
        cts.Cancel();
        try { await server; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task ContentAssetAssertion_EvaluatesBracketedEntryKeyExpression()
    {
        var (cts, server, client, _) = await StartFakeHarness(SocketPath(), """
        {
          "name": "Data/Festivals/fall16",
          "exists": true,
          "kind": "data",
          "runtime_type": "Dictionary\u00602",
          "summary": {
            "entries": {
              "Set-Up_additionalCharacters": {
                "exists": true,
                "value": "Sophia 47 60 down/Andy 49 70 down"
              }
            }
          }
        }
        """);
        using var _ = cts;
        using var __ = client;

        var runner = new ScenarioRunner(client);
        var spec = new ScenarioSpec
        {
            Name = "content_asset_bracketed_entry_key",
            Assertions = new()
            {
                new ScenarioAssertion
                {
                    Type = "content.asset",
                    Asset = "Data/Festivals/fall16",
                    AssetType = "data",
                    EntryKeys = new[] { "Set-Up_additionalCharacters" },
                    Expr = "asset.entries['Set-Up_additionalCharacters'].value contains 'Sophia'",
                },
            },
        };

        var report = await runner.RunAsync(spec, cts.Token);

        Assert.True(report.Passed);
        Assert.Equal(1, report.AssertionsPassed);
        cts.Cancel();
        try { await server; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task ContentAssetAssertion_EvaluatesNestedCollectionItemExpression()
    {
        var (cts, server, client, calls) = await StartFakeHarness(SocketPath(), req =>
        {
            if (req.Method == "content.asset")
            {
                Assert.NotNull(req.Params);
                Assert.True(req.Params.Value.TryGetProperty("nested_items_limit", out var limit));
                Assert.Equal(10, limit.GetInt32());
            }

            return """
            {
              "name": "Data/MoviesReactions",
              "exists": true,
              "kind": "data",
              "runtime_type": "Dictionary\u00602",
              "summary": {
                "entries": {
                  "Martin": {
                    "exists": true,
                    "value": {
                      "npc_name": "Martin",
                      "reactions": {
                        "runtime_type": "System.Collections.Generic.List\u00601",
                        "count": 1,
                        "items_limit": 10,
                        "items_truncated": false,
                        "items": [
                          { "tag": "*", "response": "reject", "i_d": "reaction_0" }
                        ]
                      }
                    }
                  }
                }
              }
            }
            """;
        });
        using var _ = cts;
        using var __ = client;

        var runner = new ScenarioRunner(client);
        var spec = new ScenarioSpec
        {
            Name = "content_asset_nested_collection_item",
            Assertions = new()
            {
                new ScenarioAssertion
                {
                    Type = "content.asset",
                    Asset = "Data/MoviesReactions",
                    AssetType = "data",
                    EntryKeys = new[] { "Martin" },
                    NestedItemsLimit = 10,
                    Expr = "asset.entries.Martin.value.reactions.items contains response 'reject'",
                },
            },
        };

        var report = await runner.RunAsync(spec, cts.Token);

        Assert.True(report.Passed, string.Join(Environment.NewLine, report.Failures));
        Assert.Equal(1, report.AssertionsPassed);
        Assert.Contains("content.asset", calls);
        cts.Cancel();
        try { await server; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task ContentAssetAssertion_MissingAsset_FailsWithAssetName()
    {
        var (cts, server, client, _) = await StartFakeHarness(SocketPath(), """
        {
          "name": "Maps/Missing",
          "exists": false,
          "kind": "missing",
          "runtime_type": "",
          "summary": {}
        }
        """);
        using var _ = cts;
        using var __ = client;

        var runner = new ScenarioRunner(client);
        var spec = new ScenarioSpec
        {
            Name = "content_asset_missing",
            Assertions = new()
            {
                new ScenarioAssertion
                {
                    Type = "content.asset",
                    Asset = "Maps/Missing",
                    AssetType = "map",
                    Expr = "asset.width != 0",
                },
            },
        };

        var report = await runner.RunAsync(spec, cts.Token);

        Assert.False(report.Passed);
        Assert.Contains(report.Failures, failure => failure.Contains("Maps/Missing", StringComparison.Ordinal));
        cts.Cancel();
        try { await server; } catch (OperationCanceledException) { }
    }

    private static Task<(CancellationTokenSource Cts, Task Server, JsonRpcSession Client, List<string> Calls)> StartFakeHarness(
        string socket,
        string contentAssetJson)
        => StartFakeHarness(socket, _ => contentAssetJson);

    private static Task<(CancellationTokenSource Cts, Task Server, JsonRpcSession Client, List<string> Calls)> StartFakeHarness(
        string socket,
        Func<JsonRpcRequest, string> contentAssetJsonFactory)
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
                        "content.asset" => JsonDocument.Parse(contentAssetJsonFactory(req)).RootElement,
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

        return ConnectFakeHarnessAsync(socket, cts, serverTask, calls);
    }

    private static async Task<(CancellationTokenSource Cts, Task Server, JsonRpcSession Client, List<string> Calls)> ConnectFakeHarnessAsync(
        string socket,
        CancellationTokenSource cts,
        Task serverTask,
        List<string> calls)
    {
        for (var i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);
        return (cts, serverTask, client, calls);
    }
}
