using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SdvTestFramework.Protocol.Reports;
using SdvTestFramework.Runner.Reports;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Reports;

public class HtmlReportGeneratorTests
{
    private static RunDirectory MakeRunDir(string testName)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"htmlrep-{testName}-{Guid.NewGuid():N}");
        return RunDirectory.Create(tmp);
    }

    [Fact]
    public void EmptyRun_ProducesValidHtml()
    {
        var rd = MakeRunDir("empty");
        try
        {
            var summary = new RunSummary(
                rd.RunId, "2026-04-24T15:30:45Z", 0,
                Scenarios: Array.Empty<ScenarioOutcome>());

            HtmlReportGenerator.Generate(rd, summary);

            var indexPath = Path.Combine(rd.Root, "index.html");
            Assert.True(File.Exists(indexPath));
            var html = File.ReadAllText(indexPath);
            Assert.Contains("<!DOCTYPE html>", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(rd.RunId, html);
            Assert.Contains("0 passed", html, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(rd.Root, recursive: true); }
    }

    [Fact]
    public void PassingScenario_RendersGreen()
    {
        var rd = MakeRunDir("passing");
        try
        {
            var summary = new RunSummary(
                rd.RunId, "2026-04-24T15:30:45Z", 1234,
                Scenarios: new[] { new ScenarioOutcome(
                    "shop_menu_test", "tests/samples/shop.test.json", true, 1234,
                    Steps: new[] { new StepOutcome("player.warp", true, 12, null) },
                    Assertions: new[] { new AssertionOutcome("state", true, null) },
                    Screenshots: Array.Empty<string>(),
                    Diffs: Array.Empty<DiffSet>()) });

            HtmlReportGenerator.Generate(rd, summary);

            var html = File.ReadAllText(Path.Combine(rd.Root, "index.html"));
            Assert.Contains("shop_menu_test", html);
            Assert.Contains("class=\"pass\"", html);
            Assert.DoesNotContain("class=\"fail\"", html);
        }
        finally { Directory.Delete(rd.Root, recursive: true); }
    }

    [Fact]
    public void FailedScenario_RendersRedWithAssertionDetail()
    {
        var rd = MakeRunDir("failing");
        try
        {
            var summary = new RunSummary(
                rd.RunId, "2026-04-24T15:30:45Z", 567,
                Scenarios: new[] { new ScenarioOutcome(
                    "broken_test", null, false, 567,
                    Steps: Array.Empty<StepOutcome>(),
                    Assertions: new[] { new AssertionOutcome("state", false, "expected 5000, got 0") },
                    Screenshots: Array.Empty<string>(),
                    Diffs: Array.Empty<DiffSet>()) });

            HtmlReportGenerator.Generate(rd, summary);

            var html = File.ReadAllText(Path.Combine(rd.Root, "index.html"));
            Assert.Contains("broken_test", html);
            Assert.Contains("class=\"fail\"", html);
            var scenHtml = File.ReadAllText(Path.Combine(rd.ScenariosDir, "broken_test", "report.html"));
            Assert.Contains("expected 5000, got 0", scenHtml);
        }
        finally { Directory.Delete(rd.Root, recursive: true); }
    }

    [Fact]
    public void SummaryJson_DeserializesBackToRunSummary()
    {
        var rd = MakeRunDir("rt");
        try
        {
            var summary = new RunSummary(
                rd.RunId, "2026-04-24T15:30:45Z", 0,
                Scenarios: new[] { new ScenarioOutcome(
                    "x", null, true, 100,
                    Steps: Array.Empty<StepOutcome>(),
                    Assertions: Array.Empty<AssertionOutcome>(),
                    Screenshots: Array.Empty<string>(),
                    Diffs: Array.Empty<DiffSet>()) });

            HtmlReportGenerator.Generate(rd, summary);

            var jsonPath = Path.Combine(rd.Root, "summary.json");
            Assert.True(File.Exists(jsonPath));
            var roundTripped = JsonSerializer.Deserialize<RunSummary>(
                File.ReadAllText(jsonPath),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
            Assert.NotNull(roundTripped);
            Assert.Equal("x", roundTripped!.Scenarios[0].Name);
        }
        finally { Directory.Delete(rd.Root, recursive: true); }
    }
}
