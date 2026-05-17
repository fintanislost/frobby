using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Mcp;
using SdvTestFramework.Runner.Mcp.Tools;
using Xunit;

namespace SdvTestFramework.Runner.Mcp.Tests.Tools;

public class RunScenarioAssertionParityTests
{
    private sealed class RecordingLifecycle : SdvLifecycle
    {
        public List<(string Method, string ParamsJson)> Calls { get; } = new();
        public Dictionary<string, string> Responses { get; } = new();

        public override Task<JsonElement> InvokeAsync(string method, JsonElement? p, CancellationToken ct)
        {
            Calls.Add((method, p?.GetRawText() ?? ""));
            var response = Responses.TryGetValue(method, out var json) ? json : "{}";
            return Task.FromResult(JsonDocument.Parse(response).RootElement.Clone());
        }
    }

    [Fact]
    public async Task RunScenario_EvaluatesPassingStateAssertion()
    {
        var scenario = """
        {
          "name": "mcp_state_pass",
          "config": { "seed": 42 },
          "steps": [],
          "assertions": [
            { "type": "state", "expr": "state.player.money == 500", "message": "money seeded" }
          ]
        }
        """;

        var (result, life) = await RunScenarioAsync(scenario, new Dictionary<string, string>
        {
            ["state.player"] = "{\"name\":\"Tester\",\"money\":500,\"location\":\"Farm\",\"tile\":{\"x\":0,\"y\":0},\"items\":[]}",
        });

        Assert.False(result.IsError);
        var json = ParseResult(result);
        AssertScenarioResult(json, passed: true, assertionsRun: 1, assertionsPassed: 1);
        Assert.Contains(life.Calls, call => call.Method == "state.player");
    }

    [Fact]
    public async Task RunScenario_ReturnsFailureForFailingStateAssertion()
    {
        var scenario = """
        {
          "name": "mcp_state_fail",
          "config": { "seed": 42 },
          "steps": [],
          "assertions": [
            { "type": "state", "expr": "state.player.money == 500", "message": "money seeded" }
          ]
        }
        """;

        var (result, _) = await RunScenarioAsync(scenario, new Dictionary<string, string>
        {
            ["state.player"] = "{\"name\":\"Tester\",\"money\":499,\"location\":\"Farm\",\"tile\":{\"x\":0,\"y\":0},\"items\":[]}",
        });

        Assert.False(result.IsError);
        var json = ParseResult(result);
        AssertScenarioResult(json, passed: false, assertionsRun: 1, assertionsPassed: 0);
        var failures = GetFailures(json);
        var failure = Assert.Single(failures);
        Assert.Contains("assertion 1 state", failure);
        Assert.Contains("money seeded", failure);
        Assert.Contains("state.player.money", failure);
    }

    [Fact]
    public async Task RunScenario_PassesStateAssertionParams()
    {
        var scenario = """
        {
          "name": "mcp_state_params",
          "config": { "seed": 42 },
          "steps": [],
          "assertions": [
            {
              "type": "state",
              "expr": "state.npc.hearts == 4",
              "params": { "name": "Sophia" },
              "message": "Sophia hearts"
            }
          ]
        }
        """;

        var (result, life) = await RunScenarioAsync(scenario, new Dictionary<string, string>
        {
            ["state.npc"] = "{\"name\":\"Sophia\",\"hearts\":4,\"location\":\"Town\"}",
        });

        Assert.False(result.IsError);
        var json = ParseResult(result);
        AssertScenarioResult(json, passed: true, assertionsRun: 1, assertionsPassed: 1);
        Assert.Contains(life.Calls, call =>
            call.Method == "state.npc"
            && CallParamStringEquals(call, "name", "Sophia"));
    }

    [Fact]
    public async Task RunScenario_EvaluatesContentAssetAssertion()
    {
        var scenario = """
        {
          "name": "mcp_content_asset",
          "config": { "seed": 42 },
          "steps": [],
          "assertions": [
            {
              "type": "content.asset",
              "asset": "Maps/Custom_TownEast",
              "asset_type": "map",
              "expr": "asset.layers contains name 'Back'",
              "message": "map has Back layer"
            }
          ]
        }
        """;

        var (result, life) = await RunScenarioAsync(scenario, new Dictionary<string, string>
        {
            ["content.asset"] = """
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
            """,
        });

        Assert.False(result.IsError);
        var json = ParseResult(result);
        AssertScenarioResult(json, passed: true, assertionsRun: 1, assertionsPassed: 1);
        Assert.Contains(life.Calls, call =>
            call.Method == "content.asset"
            && CallParamStringEquals(call, "name", "Maps/Custom_TownEast")
            && CallParamStringEquals(call, "asset_type", "map"));
    }

    [Fact]
    public async Task RunScenario_EvaluatesRpcResultAssertion()
    {
        var scenario = """
        {
          "name": "mcp_fishing_table",
          "config": { "seed": 42 },
          "steps": [],
          "assertions": [
            {
              "type": "state.fishing_table",
              "params": { "location": "Desert", "x": 28, "y": 6 },
              "expr": "result.candidates contains item_id '164'",
              "message": "Sandfish candidate visible"
            }
          ]
        }
        """;

        var (result, life) = await RunScenarioAsync(scenario, new Dictionary<string, string>
        {
            ["state.fishing_table"] = """
            {
              "location": "Desert",
              "is_fishable": true,
              "candidates": [
                { "item_id": "2334", "qualified_id": "(F)2334", "display_name": "Pyramid Decal" },
                { "item_id": "164", "qualified_id": "(O)164", "display_name": "Sandfish" }
              ]
            }
            """,
        });

        Assert.False(result.IsError);
        var json = ParseResult(result);
        AssertScenarioResult(json, passed: true, assertionsRun: 1, assertionsPassed: 1);
        Assert.Contains(life.Calls, call =>
            call.Method == "state.fishing_table"
            && CallParamStringEquals(call, "location", "Desert"));
    }

    [Fact]
    public async Task RunScenario_EvaluatesDrawNotContainsAssertion()
    {
        var scenario = """
        {
          "name": "mcp_draw_not_contains",
          "config": { "seed": 42 },
          "steps": [],
          "assertions": [
            {
              "type": "draw.not_contains",
              "filter": { "texture_asset": "LooseSprites/Cursors" },
              "message": "cursor should be absent"
            }
          ]
        }
        """;

        var (result, life) = await RunScenarioAsync(scenario, new Dictionary<string, string>
        {
            ["draw.assert_not_contains"] = "{\"passed\":true,\"matched_count\":0}",
        });

        Assert.False(result.IsError);
        var json = ParseResult(result);
        AssertScenarioResult(json, passed: true, assertionsRun: 1, assertionsPassed: 1);
        Assert.Contains(life.Calls, call => call.Method == "draw.assert_not_contains");
    }

    private static JsonElement ParseResult(McpToolResult result)
    {
        using var doc = JsonDocument.Parse(result.Text);
        return doc.RootElement.Clone();
    }

    private static void AssertScenarioResult(
        JsonElement json,
        bool passed,
        int assertionsRun,
        int assertionsPassed)
    {
        Assert.Equal(passed, json.GetProperty("passed").GetBoolean());
        Assert.Equal(assertionsRun, json.GetProperty("assertions_run").GetInt32());
        Assert.Equal(assertionsPassed, json.GetProperty("assertions_passed").GetInt32());
    }

    private static List<string> GetFailures(JsonElement json)
    {
        var failures = new List<string>();
        foreach (var failure in json.GetProperty("failures").EnumerateArray())
            failures.Add(failure.GetString() ?? "");

        return failures;
    }

    private static bool CallParamStringEquals((string Method, string ParamsJson) call, string propertyName, string expected)
    {
        if (string.IsNullOrWhiteSpace(call.ParamsJson))
            return false;

        using var doc = JsonDocument.Parse(call.ParamsJson);
        return doc.RootElement.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
            && value.GetString() == expected;
    }

    private static async Task<(McpToolResult Result, RecordingLifecycle Life)> RunScenarioAsync(
        string scenarioJson,
        Dictionary<string, string> methodResponses)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mcp-parity-{Guid.NewGuid():N}.test.json");
        var reportBase = Path.Combine(Path.GetTempPath(), $"mcp-parity-report-{Guid.NewGuid():N}");
        Directory.CreateDirectory(reportBase);
        await File.WriteAllTextAsync(path, scenarioJson);

        try
        {
            var life = new RecordingLifecycle();
            life.Responses["scenario.begin"] = "{\"session_id\":\"x\",\"tick\":0}";
            life.Responses["scenario.end"] = "{\"duration_ms\":1,\"assertions_run\":0,\"assertions_passed\":0}";
            foreach (var entry in methodResponses)
                life.Responses[entry.Key] = entry.Value;

            var tool = new RunScenarioTool();
            var args = JsonDocument.Parse($$"""
            {
              "path": {{JsonSerializer.Serialize(path)}},
              "report_dir": {{JsonSerializer.Serialize(reportBase)}}
            }
            """).RootElement;

            return (await tool.InvokeAsync(
                args,
                new ToolInvocationContext(life, McpProgressReporter.None),
                CancellationToken.None), life);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
            if (Directory.Exists(reportBase))
                Directory.Delete(reportBase, recursive: true);
        }
    }
}
