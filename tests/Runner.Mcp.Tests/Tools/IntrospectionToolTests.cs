using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Mcp.Tools;
using Xunit;

namespace SdvTestFramework.Runner.Mcp.Tests.Tools;

public class IntrospectionToolTests
{
    [Fact]
    public async Task ListScenarios_GlobsDirectory()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mcp-list-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            File.WriteAllText(Path.Combine(tmp, "a.test.json"), "{\"name\":\"A\",\"steps\":[],\"fixture\":\"f1\"}");
            File.WriteAllText(Path.Combine(tmp, "b.test.json"), "{\"name\":\"B\",\"steps\":[]}");
            File.WriteAllText(Path.Combine(tmp, "not-a-test.txt"), "ignore");

            var tool = new ListScenariosTool();
            var args = JsonDocument.Parse($"{{\"dir\":{JsonSerializer.Serialize(tmp)}}}").RootElement;
            var result = await tool.InvokeAsync(args, lifecycle: null, CancellationToken.None);

            Assert.False(result.IsError);
            Assert.Contains("\"name\":\"A\"", result.Text);
            Assert.Contains("\"name\":\"B\"", result.Text);
            Assert.Contains("\"fixture\":\"f1\"", result.Text);
            Assert.DoesNotContain("not-a-test", result.Text);
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public async Task ListFixtures_ReadsMetaJson()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mcp-fx-{Guid.NewGuid():N}");
        var fxDir = Path.Combine(tmp, "tests", "fixtures", "myfixture");
        Directory.CreateDirectory(fxDir);
        try
        {
            File.WriteAllText(Path.Combine(fxDir, ".meta.json"),
                "{\"name\":\"myfixture\",\"sdv_version\":\"1.6.15\",\"description\":\"test fixture\"}");

            var tool = new ListFixturesTool();
            var args = JsonDocument.Parse($"{{\"root\":{JsonSerializer.Serialize(Path.Combine(tmp, "tests", "fixtures"))}}}").RootElement;
            var result = await tool.InvokeAsync(args, lifecycle: null, CancellationToken.None);

            Assert.False(result.IsError);
            Assert.Contains("\"name\":\"myfixture\"", result.Text);
            Assert.Contains("test fixture", result.Text);
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }
}
