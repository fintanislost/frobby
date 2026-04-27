using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Reports;
using SdvTestFramework.Runner.Dsl;
using Xunit;

namespace SdvTestFramework.Runner.Dsl.Tests.Facets;

public class ScreenshotTests
{
    private sealed class CapturingInvoker : ISdvTestInvoker
    {
        public List<(string Method, string ParamsJson)> Calls { get; } = new();
        public string CapturePath { get; init; } = "/tmp/fake.png";

        public Task<JsonElement> InvokeAsync(string m, JsonElement? p, CancellationToken ct)
        {
            Calls.Add((m, p?.GetRawText() ?? ""));
            var json = $"{{\"path\":\"{CapturePath.Replace("\\", "\\\\")}\",\"width\":1280,\"height\":720}}";
            return Task.FromResult(JsonDocument.Parse(json).RootElement.Clone());
        }
    }

    [Fact]
    public async Task Capture_WithReportDir_CopiesToScenarioScreenshots()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"sshot-{Guid.NewGuid():N}");
        var rd = RunDirectory.Create(tmp);
        var sourcePng = Path.Combine(tmp, "source.png");
        File.WriteAllBytes(sourcePng, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var inv = new CapturingInvoker { CapturePath = sourcePng };
        SdvTestSession.InitializeForTests(inv);
        var session = SdvTestSession.Current!;
        session.ReportDir = rd;
        session.CurrentScenarioName = "test_scenario";

        try
        {
            await Screenshot.Capture("after_warp");
            Assert.Equal("bitmap.capture", inv.Calls[0].Method);
            var dest = Path.Combine(rd.ScenarioDir("test_scenario"), "screenshots", "after_warp.png");
            Assert.True(File.Exists(dest));
        }
        finally
        {
            SdvTestSession.ResetForTests();
            Directory.Delete(rd.Root, recursive: true);
        }
    }
}
