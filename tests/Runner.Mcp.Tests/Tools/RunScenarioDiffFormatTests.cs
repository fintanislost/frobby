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

public class RunScenarioDiffFormatTests
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
    public async Task RunScenario_DiffFormatArg_AcceptedWithoutError()
    {
        // The MCP tool's run_scenario doesn't currently evaluate bitmap assertions
        // (it delegates to the CLI runner). The minimum contract this test enforces:
        // passing a diff_format arg doesn't error — schema accepts it, tool routes it.
        var tmp = Path.Combine(Path.GetTempPath(), $"mcp-df-{Guid.NewGuid():N}.test.json");
        File.WriteAllText(tmp, "{\"name\":\"n\",\"config\":{\"seed\":42},\"steps\":[],\"assertions\":[]}");
        var lifeBaseDir = Path.Combine(Path.GetTempPath(), $"mcp-df-base-{Guid.NewGuid():N}");
        Directory.CreateDirectory(lifeBaseDir);

        try
        {
            var life = new RecordingLifecycle();
            life.Responses["scenario.begin"] = "{\"session_id\":\"x\",\"tick\":0}";
            life.Responses["scenario.end"]   = "{\"duration_ms\":1,\"assertions_run\":0,\"assertions_passed\":0}";

            var tool = new RunScenarioTool();
            var argsJson = $"{{\"path\":{JsonSerializer.Serialize(tmp)},\"report_dir\":{JsonSerializer.Serialize(lifeBaseDir)},\"diff_format\":\"triptych\"}}";
            var args = JsonDocument.Parse(argsJson).RootElement;
            var result = await tool.InvokeAsync(args, life, CancellationToken.None);

            Assert.False(result.IsError);
            // Tool's input schema must declare diff_format; verify by inspecting InputSchema.
            var schemaText = tool.InputSchema.GetRawText();
            Assert.Contains("diff_format", schemaText);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
            if (Directory.Exists(lifeBaseDir)) Directory.Delete(lifeBaseDir, recursive: true);
        }
    }
}
