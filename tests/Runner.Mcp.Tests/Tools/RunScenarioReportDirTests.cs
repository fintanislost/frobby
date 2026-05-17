using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Mcp;
using SdvTestFramework.Runner.Mcp.Tools;
using Xunit;

namespace SdvTestFramework.Runner.Mcp.Tests.Tools;

public class RunScenarioReportDirTests
{
    private sealed class RecordingLifecycle : SdvLifecycle
    {
        public List<(string Method, string ParamsJson)> Calls { get; } = new();
        public Dictionary<string, string> Responses { get; } = new();

        public override Task<JsonElement> InvokeAsync(string method, JsonElement? p, CancellationToken ct)
        {
            Calls.Add((method, p?.GetRawText() ?? ""));
            var resp = Responses.TryGetValue(method, out var r) ? r : "{}";
            return Task.FromResult(JsonDocument.Parse(resp).RootElement.Clone());
        }
    }

    [Fact]
    public async Task RunScenario_ResultIncludesReportDirAndIndex()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mcp-run-rdir-{System.Guid.NewGuid():N}.test.json");
        File.WriteAllText(tmp, "{\"name\":\"n\",\"config\":{\"seed\":42},\"steps\":[],\"assertions\":[]}");
        var lifeBaseDir = Path.Combine(Path.GetTempPath(), $"mcp-rdir-base-{System.Guid.NewGuid():N}");

        try
        {
            var life = new RecordingLifecycle();
            life.Responses["scenario.begin"] = "{\"session_id\":\"x\",\"tick\":0}";
            life.Responses["scenario.end"]   = "{\"duration_ms\":1,\"assertions_run\":0,\"assertions_passed\":0}";

            var tool = new RunScenarioTool();
            var argsJson = $"{{\"path\":{JsonSerializer.Serialize(tmp)},\"report_dir\":{JsonSerializer.Serialize(lifeBaseDir)}}}";
            var args = JsonDocument.Parse(argsJson).RootElement;
            var result = await tool.InvokeAsync(
                args,
                new ToolInvocationContext(life, McpProgressReporter.None),
                CancellationToken.None);

            Assert.False(result.IsError);
            Assert.Contains("\"report_dir\"", result.Text);
            Assert.Contains("\"report_index\"", result.Text);
            // The auto-id subdir is appended to the user-supplied base dir; on Linux the JSON
            // encoding of the path is unchanged (no backslashes). The replace is a no-op there
            // and a JSON-escape on Windows.
            Assert.Contains(lifeBaseDir.Replace("\\", "\\\\"), result.Text);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
            if (Directory.Exists(lifeBaseDir)) Directory.Delete(lifeBaseDir, recursive: true);
        }
    }
}
