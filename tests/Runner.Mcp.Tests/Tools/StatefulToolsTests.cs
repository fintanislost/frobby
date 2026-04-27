using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Mcp;
using SdvTestFramework.Runner.Mcp.Tools;
using Xunit;

namespace SdvTestFramework.Runner.Mcp.Tests.Tools;

public class StatefulToolsTests
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
    public async Task WarpAndAssertDraw_ProducesAtomicSequence()
    {
        var life = new RecordingLifecycle();
        life.Responses["draw.assert_contains"] = "{\"passed\":true,\"matched\":2}";

        var tool = new WarpAndAssertDrawTool();
        var args = JsonDocument.Parse("{\"location\":\"SeedShop\",\"x\":4,\"y\":19,\"texture_asset\":\"LooseSprites/Cursors\"}").RootElement;
        var result = await tool.InvokeAsync(args, life, CancellationToken.None);

        Assert.False(result.IsError);
        var methods = life.Calls.ConvertAll(c => c.Method);
        Assert.Contains("player.warp", methods);
        Assert.Contains("draw.arm", methods);
        Assert.Contains("freeze.begin", methods);
        Assert.Contains("draw.assert_contains", methods);
        Assert.Contains("freeze.end", methods);
        Assert.Contains("\"passed\":true", result.Text);
    }

    [Fact]
    public async Task RunScenario_LoadsAndReturnsReport()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mcp-run-{System.Guid.NewGuid():N}.test.json");
        File.WriteAllText(tmp, "{\"name\":\"n\",\"config\":{\"seed\":42},\"steps\":[],\"assertions\":[]}");

        try
        {
            var life = new RecordingLifecycle();
            life.Responses["scenario.begin"] = "{\"session_id\":\"x\",\"tick\":0}";
            life.Responses["scenario.end"]   = "{\"duration_ms\":1,\"assertions_run\":0,\"assertions_passed\":0}";

            var tool = new RunScenarioTool();
            var args = JsonDocument.Parse($"{{\"path\":{JsonSerializer.Serialize(tmp)}}}").RootElement;
            var result = await tool.InvokeAsync(args, life, CancellationToken.None);

            Assert.False(result.IsError);
            Assert.Contains("\"passed\":true", result.Text);
            Assert.Contains("scenario.begin", life.Calls.ConvertAll(c => c.Method));
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
}
