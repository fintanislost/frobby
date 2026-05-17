using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Runner.Mcp;
using SdvTestFramework.Runner.Mcp.Tools;
using Xunit;

namespace SdvTestFramework.Runner.Mcp.Tests;

public class McpReportResourceTests
{
    private sealed class RecordingLifecycle : SdvLifecycle
    {
        public Dictionary<string, string> Responses { get; } = new();

        public override Task<JsonElement> InvokeAsync(string method, JsonElement? p, CancellationToken ct)
        {
            var response = Responses.TryGetValue(method, out var json) ? json : "{}";
            return Task.FromResult(JsonDocument.Parse(response).RootElement.Clone());
        }
    }

    [Fact]
    public async Task ResourcesList_IncludesLatestReportResources()
    {
        var lines = await RunServerWithAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"resources/list\"}\n");

        using var doc = JsonDocument.Parse(lines[0]);
        var uris = doc.RootElement.GetProperty("result").GetProperty("resources")
            .EnumerateArray()
            .Select(r => r.GetProperty("uri").GetString())
            .ToArray();

        Assert.Contains("frobby://reports/latest/summary", uris);
        Assert.Contains("frobby://reports/latest/index", uris);
        Assert.Contains("frobby://reports/latest/scenarios", uris);
    }

    [Fact]
    public async Task ResourcesRead_LatestSummaryWithoutReport_ReturnsInvalidParams()
    {
        var lines = await RunServerWithAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"resources/read\",\"params\":{\"uri\":\"frobby://reports/latest/summary\"}}\n");

        using var doc = JsonDocument.Parse(lines[0]);
        var error = doc.RootElement.GetProperty("error");
        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Contains("no latest report", error.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResourcesRead_LatestFileBackedReport_ReturnsSummaryIndexAndScenarios()
    {
        var reportDir = Path.Combine(Path.GetTempPath(), $"mcp-report-resource-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(reportDir, "scenarios", "ui_path"));
        await File.WriteAllTextAsync(
            Path.Combine(reportDir, "summary.json"),
            """
            {
              "run_id": "test-run",
              "started": "2026-05-17T12:00:00Z",
              "duration_ms": 42,
              "scenarios": [
                {
                  "name": "ui_path",
                  "path": "tests/sdv/ui_path.test.json",
                  "passed": true,
                  "duration_ms": 40,
                  "steps": [],
                  "assertions": [],
                  "screenshots": [],
                  "diffs": []
                }
              ]
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(reportDir, "index.html"), "<!doctype html><title>test run</title>");
        await File.WriteAllTextAsync(Path.Combine(reportDir, "scenarios", "ui_path", "report.html"), "<!doctype html>");

        try
        {
            var registry = new McpReportRegistry();
            registry.RecordLatestReport(reportDir);
            var input = """
            {"jsonrpc":"2.0","id":3,"method":"resources/read","params":{"uri":"frobby://reports/latest/summary"}}
            {"jsonrpc":"2.0","id":4,"method":"resources/read","params":{"uri":"frobby://reports/latest/index"}}
            {"jsonrpc":"2.0","id":5,"method":"resources/read","params":{"uri":"frobby://reports/latest/scenarios"}}
            """;

            var lines = await RunServerWithAsync(input, reportRegistry: registry);

            AssertResourceContent(lines[0], "frobby://reports/latest/summary", "application/json", "\"run_id\"");
            AssertResourceContent(lines[1], "frobby://reports/latest/index", "text/html", "<title>test run</title>");
            AssertResourceContent(lines[2], "frobby://reports/latest/scenarios", "text/markdown", "ui_path");
            AssertResourceContent(lines[2], "frobby://reports/latest/scenarios", "text/markdown", "scenarios/ui_path/report.html");
        }
        finally
        {
            if (Directory.Exists(reportDir))
                Directory.Delete(reportDir, recursive: true);
        }
    }

    [Fact]
    public async Task ResourcesRead_LatestSummaryWithMalformedJson_ReturnsInvalidParams()
    {
        var reportDir = Path.Combine(Path.GetTempPath(), $"mcp-report-bad-summary-{Guid.NewGuid():N}");
        Directory.CreateDirectory(reportDir);
        await File.WriteAllTextAsync(Path.Combine(reportDir, "summary.json"), "{not-json");

        try
        {
            var registry = new McpReportRegistry();
            registry.RecordLatestReport(reportDir);
            var lines = await RunServerWithAsync(
                "{\"jsonrpc\":\"2.0\",\"id\":6,\"method\":\"resources/read\",\"params\":{\"uri\":\"frobby://reports/latest/summary\"}}\n",
                reportRegistry: registry);

            using var doc = JsonDocument.Parse(lines[0]);
            var error = doc.RootElement.GetProperty("error");
            Assert.Equal(-32602, error.GetProperty("code").GetInt32());
            Assert.Contains("valid JSON", error.GetProperty("message").GetString());
        }
        finally
        {
            if (Directory.Exists(reportDir))
                Directory.Delete(reportDir, recursive: true);
        }
    }

    [Fact]
    public async Task RunScenario_RecordsLatestSummaryResource()
    {
        var scenarioPath = Path.Combine(Path.GetTempPath(), $"mcp-report-run-{Guid.NewGuid():N}.test.json");
        var reportBase = Path.Combine(Path.GetTempPath(), $"mcp-report-run-base-{Guid.NewGuid():N}");
        Directory.CreateDirectory(reportBase);
        await File.WriteAllTextAsync(
            scenarioPath,
            "{\"name\":\"report_probe\",\"config\":{\"seed\":42},\"steps\":[],\"assertions\":[]}");

        try
        {
            var lifecycle = new RecordingLifecycle();
            lifecycle.Responses["scenario.begin"] = "{\"session_id\":\"x\",\"tick\":0}";
            lifecycle.Responses["scenario.end"] = "{\"duration_ms\":1,\"assertions_run\":0,\"assertions_passed\":0}";

            var registry = new ToolRegistry();
            registry.Register(new RunScenarioTool());
            var input =
                "{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"tools/call\",\"params\":{\"name\":\"run_scenario\",\"arguments\":{\"path\":" +
                JsonSerializer.Serialize(scenarioPath) +
                ",\"report_dir\":" +
                JsonSerializer.Serialize(reportBase) +
                "}}}\n" +
                "{\"jsonrpc\":\"2.0\",\"id\":8,\"method\":\"resources/read\",\"params\":{\"uri\":\"frobby://reports/latest/summary\"}}\n";

            var lines = await RunServerWithAsync(input, registry, lifecycle);

            Assert.Equal(2, lines.Length);
            AssertResourceContent(lines[1], "frobby://reports/latest/summary", "application/json", "\"report_probe\"");
            AssertResourceContent(lines[1], "frobby://reports/latest/summary", "application/json", "\"report_dir\"");
        }
        finally
        {
            if (File.Exists(scenarioPath))
                File.Delete(scenarioPath);
            if (Directory.Exists(reportBase))
                Directory.Delete(reportBase, recursive: true);
        }
    }

    private static async Task<string[]> RunServerWithAsync(
        string input,
        ToolRegistry? toolRegistry = null,
        SdvLifecycle? lifecycle = null,
        McpReportRegistry? reportRegistry = null)
    {
        var inBytes = Encoding.UTF8.GetBytes(input);
        using var stdin = new MemoryStream(inBytes);
        using var stdout = new MemoryStream();
        var server = new McpServer(toolRegistry ?? new ToolRegistry(), lifecycle, reportRegistry);
        await server.RunAsync(stdin, stdout, CancellationToken.None);
        var output = Encoding.UTF8.GetString(stdout.ToArray());
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private static void AssertResourceContent(
        string line,
        string uri,
        string mimeType,
        string expectedText)
    {
        using var doc = JsonDocument.Parse(line);
        var content = doc.RootElement.GetProperty("result").GetProperty("contents")[0];
        Assert.Equal(uri, content.GetProperty("uri").GetString());
        Assert.Equal(mimeType, content.GetProperty("mimeType").GetString());
        Assert.Contains(expectedText, content.GetProperty("text").GetString());
    }
}
