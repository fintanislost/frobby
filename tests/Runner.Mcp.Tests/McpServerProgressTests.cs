using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Runner.Mcp;
using SdvTestFramework.Runner.Mcp.Tools;
using Xunit;

namespace SdvTestFramework.Runner.Mcp.Tests;

public class McpServerProgressTests
{
    private sealed class ProgressReportingTool : ITool
    {
        public string Name => "progress_probe";
        public string Description => "progress probe";
        public JsonElement InputSchema => JsonDocument.Parse("{\"type\":\"object\"}").RootElement;

        public async Task<McpToolResult> InvokeAsync(
            JsonElement args,
            ToolInvocationContext context,
            CancellationToken ct)
        {
            await context.Progress.ReportAsync(1, 1, "probe", ct);
            return McpToolResult.Success(JsonDocument.Parse("{\"ok\":true}").RootElement);
        }
    }

    private sealed class RecordingLifecycle : SdvLifecycle
    {
        public List<(string Method, string ParamsJson)> Calls { get; } = new();
        public Dictionary<string, string> Responses { get; } = new();
        public HashSet<string> FailMethods { get; } = new();

        public override Task<JsonElement> InvokeAsync(string method, JsonElement? p, CancellationToken ct)
        {
            Calls.Add((method, p?.GetRawText() ?? ""));
            if (FailMethods.Contains(method))
                throw new SdvRpcException(method, JsonRpcErrorCode.InternalError, "forced failure");

            var response = Responses.TryGetValue(method, out var json) ? json : "{}";
            return Task.FromResult(JsonDocument.Parse(response).RootElement.Clone());
        }
    }

    private sealed class FailFirstWriteStream : MemoryStream
    {
        private bool _failed;

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (!_failed)
            {
                _failed = true;
                throw new IOException("forced progress transport failure");
            }

            return base.WriteAsync(buffer, cancellationToken);
        }
    }

    [Fact]
    public async Task ToolCall_WithProgressToken_EmitsProgressNotificationBeforeFinalResponse()
    {
        var lines = await RunProbeToolThroughServerAsync(new MemoryStream());

        Assert.Equal(2, lines.Length);
        AssertProgress(ParseNotification(lines[0]), "probe-token", progress: 1, total: 1, message: "probe");

        using var finalDoc = JsonDocument.Parse(lines[1]);
        var final = finalDoc.RootElement;
        Assert.Equal(9, final.GetProperty("id").GetInt32());
        var toolText = final.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("\"ok\":true", toolText);
    }

    [Fact]
    public async Task ToolCall_WhenProgressWriteFails_PropagatesTransportFailure()
    {
        var stdout = new FailFirstWriteStream();

        await Assert.ThrowsAnyAsync<IOException>(() => RunProbeToolThroughServerAsync(stdout));
    }

    [Fact]
    public async Task RunScenario_WithProgressToken_EmitsProgressNotificationsBeforeFinalResponse()
    {
        var scenario = """
        {
          "name": "progress_ok",
          "config": { "seed": 42 },
          "steps": [
            { "action": "player.warp", "args": { "location": "Farm", "x": 1, "y": 2 } }
          ],
          "assertions": [
            { "type": "state", "expr": "state.player.money == 500", "message": "money seeded" }
          ]
        }
        """;

        var life = CreateLifecycle();
        life.Responses["state.player"] = """
        {"name":"Tester","money":500,"location":"Farm","tile":{"x":1,"y":2},"items":[]}
        """;

        var lines = await RunScenarioThroughServerAsync(
            scenario,
            life,
            progressTokenJson: "\"scenario-01\"");

        Assert.Equal(5, lines.Length);

        var notifications = ParseNotifications(lines);
        Assert.Equal(4, notifications.Count);
        AssertProgress(notifications[0], "scenario-01", progress: 1, total: 4, message: "scenario.begin");
        AssertProgress(notifications[1], "scenario-01", progress: 2, total: 4, message: "step 1/1: player.warp");
        AssertProgress(notifications[2], "scenario-01", progress: 3, total: 4, message: "assertion 1/1: state");
        AssertProgress(notifications[3], "scenario-01", progress: 4, total: 4, message: "scenario.end");

        using var toolDoc = AssertFinalToolResult(lines[^1], id: 9, expectedPassed: true);
    }

    [Fact]
    public async Task RunScenario_WithoutProgressToken_EmitsOnlyFinalResponse()
    {
        var scenario = """
        {
          "name": "progress_none",
          "config": { "seed": 42 },
          "steps": [],
          "assertions": []
        }
        """;

        var lines = await RunScenarioThroughServerAsync(
            scenario,
            CreateLifecycle(),
            progressTokenJson: null);

        Assert.Single(lines);
        using var toolDoc = AssertFinalToolResult(lines[0], id: 9, expectedPassed: true);
        using var finalDoc = JsonDocument.Parse(lines[0]);
        var final = finalDoc.RootElement;
        Assert.False(final.TryGetProperty("method", out _));
    }

    [Fact]
    public async Task RunScenario_WithFixture_ReportsFixtureProgress()
    {
        var scenario = """
        {
          "name": "progress_fixture",
          "fixture": "seeded-farm",
          "config": { "seed": 42 },
          "steps": [],
          "assertions": []
        }
        """;

        var lines = await RunScenarioThroughServerAsync(
            scenario,
            CreateLifecycle(),
            progressTokenJson: "\"scenario-fixture\"");

        Assert.Equal(4, lines.Length);
        AssertProgress(ParseNotification(lines[0]), "scenario-fixture", progress: 1, total: 3, message: "scenario.begin");
        AssertProgress(ParseNotification(lines[1]), "scenario-fixture", progress: 2, total: 3, message: "fixture.load: seeded-farm");
        AssertProgress(ParseNotification(lines[2]), "scenario-fixture", progress: 3, total: 3, message: "scenario.end");
        using var toolDoc = AssertFinalToolResult(lines[3], id: 9, expectedPassed: true);
    }

    [Fact]
    public async Task RunScenario_WithStepFailure_EmitsFailedStepProgressAndPassedFalse()
    {
        var scenario = """
        {
          "name": "progress_step_fail",
          "config": { "seed": 42 },
          "steps": [
            { "action": "player.warp", "args": { "location": "Farm", "x": 1, "y": 2 } }
          ],
          "assertions": []
        }
        """;

        var life = CreateLifecycle();
        life.FailMethods.Add("player.warp");

        var lines = await RunScenarioThroughServerAsync(
            scenario,
            life,
            progressTokenJson: "\"scenario-fail\"");

        Assert.Equal(4, lines.Length);
        AssertProgress(ParseNotification(lines[0]), "scenario-fail", progress: 1, total: 3, message: "scenario.begin");
        AssertProgress(ParseNotification(lines[1]), "scenario-fail", progress: 2, total: 3, message: "step 1/1 failed: player.warp");
        AssertProgress(ParseNotification(lines[2]), "scenario-fail", progress: 3, total: 3, message: "scenario.end");
        using var toolDoc = AssertFinalToolResult(lines[3], id: 9, expectedPassed: false);

        var failures = toolDoc.RootElement.GetProperty("failures");
        Assert.Equal(JsonValueKind.Array, failures.ValueKind);
        var failure = Assert.Single(failures.EnumerateArray());
        Assert.Contains("player.warp", failure.GetRawText());
    }

    [Fact]
    public async Task RunScenario_WithEarlyStepFailure_CompletesProgressTotal()
    {
        var scenario = """
        {
          "name": "progress_early_step_fail",
          "config": { "seed": 42 },
          "steps": [
            { "action": "player.warp", "args": { "location": "Farm", "x": 1, "y": 2 } },
            { "action": "time.advance", "args": { "minutes": 10 } }
          ],
          "assertions": [
            { "type": "state", "expr": "state.player.money == 500", "message": "money seeded" }
          ]
        }
        """;

        var life = CreateLifecycle();
        life.FailMethods.Add("player.warp");

        var lines = await RunScenarioThroughServerAsync(
            scenario,
            life,
            progressTokenJson: "\"scenario-early-fail\"");

        Assert.Equal(4, lines.Length);
        AssertProgress(
            ParseNotification(lines[0]),
            "scenario-early-fail",
            progress: 1,
            total: 5,
            message: "scenario.begin");
        AssertProgress(
            ParseNotification(lines[1]),
            "scenario-early-fail",
            progress: 2,
            total: 5,
            message: "step 1/2 failed: player.warp");
        AssertProgress(
            ParseNotification(lines[2]),
            "scenario-early-fail",
            progress: 5,
            total: 5,
            message: "scenario.end");
        using var toolDoc = AssertFinalToolResult(lines[3], id: 9, expectedPassed: false);
    }

    private static RecordingLifecycle CreateLifecycle()
    {
        var life = new RecordingLifecycle();
        life.Responses["scenario.begin"] = "{\"session_id\":\"x\",\"tick\":0}";
        life.Responses["scenario.end"] = "{\"duration_ms\":1,\"assertions_run\":0,\"assertions_passed\":0}";
        return life;
    }

    private static async Task<string[]> RunScenarioThroughServerAsync(
        string scenarioJson,
        RecordingLifecycle life,
        string? progressTokenJson)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mcp-progress-{Guid.NewGuid():N}.test.json");
        var reportBase = Path.Combine(Path.GetTempPath(), $"mcp-progress-report-{Guid.NewGuid():N}");
        Directory.CreateDirectory(reportBase);
        await File.WriteAllTextAsync(path, scenarioJson);

        try
        {
            var meta = progressTokenJson is null
                ? ""
                : $",\"_meta\":{{\"progressToken\":{progressTokenJson}}}";
            var request =
                "{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"tools/call\",\"params\":{\"name\":\"run_scenario\",\"arguments\":{\"path\":" +
                JsonSerializer.Serialize(path) +
                ",\"report_dir\":" +
                JsonSerializer.Serialize(reportBase) +
                "}" +
                meta +
                "}}";
            var input = Encoding.UTF8.GetBytes(request + "\n");
            using var stdin = new MemoryStream(input);
            using var stdout = new MemoryStream();

            var registry = new ToolRegistry();
            registry.Register(new RunScenarioTool());
            var server = new McpServer(registry, life);
            await server.RunAsync(stdin, stdout, CancellationToken.None);

            var output = Encoding.UTF8.GetString(stdout.ToArray());
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
            if (Directory.Exists(reportBase))
                Directory.Delete(reportBase, recursive: true);
        }
    }

    private static async Task<string[]> RunProbeToolThroughServerAsync(Stream stdout)
    {
        var request =
            "{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"tools/call\",\"params\":{\"name\":\"progress_probe\",\"arguments\":{},\"_meta\":{\"progressToken\":\"probe-token\"}}}";
        var input = Encoding.UTF8.GetBytes(request + "\n");
        using var stdin = new MemoryStream(input);

        var registry = new ToolRegistry();
        registry.Register(new ProgressReportingTool());
        var server = new McpServer(registry, lifecycle: null);
        await server.RunAsync(stdin, stdout, CancellationToken.None);

        if (stdout is MemoryStream memory)
        {
            var output = Encoding.UTF8.GetString(memory.ToArray());
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }

        return [];
    }

    private static List<JsonElement> ParseNotifications(string[] lines)
    {
        var notifications = new List<JsonElement>();
        foreach (var line in lines)
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.TryGetProperty("method", out var method) &&
                method.GetString() == "notifications/progress")
            {
                notifications.Add(root.Clone());
            }
        }

        return notifications;
    }

    private static JsonElement ParseNotification(string line)
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        Assert.Equal("notifications/progress", root.GetProperty("method").GetString());
        return root.Clone();
    }

    private static JsonDocument AssertFinalToolResult(string line, int id, bool expectedPassed)
    {
        using var finalDoc = JsonDocument.Parse(line);
        var final = finalDoc.RootElement;
        Assert.Equal("2.0", final.GetProperty("jsonrpc").GetString());
        Assert.Equal(id, final.GetProperty("id").GetInt32());
        Assert.False(final.TryGetProperty("error", out _));

        var content = final.GetProperty("result").GetProperty("content")[0];
        Assert.Equal("text", content.GetProperty("type").GetString());
        Assert.False(final.GetProperty("result").TryGetProperty("isError", out _));

        var toolText = content.GetProperty("text").GetString()!;
        var toolDoc = JsonDocument.Parse(toolText);
        Assert.Equal(expectedPassed, toolDoc.RootElement.GetProperty("passed").GetBoolean());
        return toolDoc;
    }

    private static void AssertProgress(
        JsonElement notification,
        string token,
        int progress,
        int total,
        string message)
    {
        Assert.Equal("2.0", notification.GetProperty("jsonrpc").GetString());
        Assert.False(notification.TryGetProperty("id", out _));
        Assert.Equal("notifications/progress", notification.GetProperty("method").GetString());

        var parameters = notification.GetProperty("params");
        Assert.Equal(token, parameters.GetProperty("progressToken").GetString());
        Assert.Equal(progress, parameters.GetProperty("progress").GetInt32());
        Assert.Equal(total, parameters.GetProperty("total").GetInt32());
        Assert.Equal(message, parameters.GetProperty("message").GetString());
    }
}
