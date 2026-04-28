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
            Assert.Contains("<a href=\"../index.html\">All reports</a>", html);
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

    [Fact]
    public void RunMetadata_RendersInSummaryJsonAndIndex()
    {
        var rd = MakeRunDir("metadata");
        try
        {
            var summary = new RunSummary(
                rd.RunId, "2026-04-24T15:30:45Z", 0,
                Scenarios: Array.Empty<ScenarioOutcome>())
            {
                Metadata = new RunMetadata(
                    Command: "sdv-test run --headless tests/sdv",
                    WorkingDirectory: "/home/fintan/stardewRepos/frobby/sdv-test-framework",
                    LaunchMode: "headless",
                    Headless: true,
                    Launcher: "xvfb-run",
                    Repositories: new[]
                    {
                        new RunRepositoryMetadata(
                            Label: "runner:sdv-test-framework",
                            Path: "/home/fintan/stardewRepos/frobby/sdv-test-framework",
                            Commit: "0ad28e7",
                            Dirty: false),
                        new RunRepositoryMetadata(
                            Label: "extra-mod:stonks",
                            Path: "/home/fintan/stardewRepos/stonks",
                            Commit: "4e62c75",
                            Dirty: true),
                    }),
            };

            HtmlReportGenerator.Generate(rd, summary);

            var json = File.ReadAllText(Path.Combine(rd.Root, "summary.json"));
            Assert.Contains("\"launch_mode\": \"headless\"", json);
            Assert.Contains("\"launcher\": \"xvfb-run\"", json);
            Assert.Contains("\"label\": \"extra-mod:stonks\"", json);
            Assert.Contains("\"commit\": \"4e62c75\"", json);

            var html = File.ReadAllText(Path.Combine(rd.Root, "index.html"));
            Assert.Contains("headless", html);
            Assert.Contains("xvfb-run", html);
            Assert.Contains("runner:sdv-test-framework", html);
            Assert.Contains("0ad28e7", html);
            Assert.Contains("extra-mod:stonks", html);
            Assert.Contains("4e62c75", html);
            Assert.Contains("dirty", html);
        }
        finally { Directory.Delete(rd.Root, recursive: true); }
    }

    [Fact]
    public void ScenarioReport_RendersStepScreenshotsNearMatchingStep()
    {
        var rd = MakeRunDir("stepshots");
        try
        {
            var summary = new RunSummary(
                rd.RunId, "2026-04-24T15:30:45Z", 100,
                Scenarios: new[] { new ScenarioOutcome(
                    "visual_path", null, true, 100,
                    Steps: new[]
                    {
                        new StepOutcome("player.warp", true, 12, "Warp to FarmHouse (8,10)"),
                        new StepOutcome("freeze.begin", true, 20, "Freeze deterministic frame"),
                    },
                    Assertions: new[] { new AssertionOutcome("draw.text_contains \"OE TICKET\"", true, "Order ticket tab visible") },
                    Screenshots: new[]
                    {
                        "scenarios/visual_path/screenshots/step-00-player-warp.png",
                        "scenarios/visual_path/screenshots/step-01-after-freeze.png",
                    },
                    Diffs: Array.Empty<DiffSet>()) });

            HtmlReportGenerator.Generate(rd, summary);

            var html = File.ReadAllText(Path.Combine(rd.ScenariosDir, "visual_path", "report.html"));
            Assert.Contains("Warp to FarmHouse (8,10)", html);
            Assert.Contains("<a href=\"../../index.html\">back to run</a>", html);
            Assert.Contains("<a href=\"../../../index.html\">All reports</a>", html);
            Assert.Contains("class=\"step-screenshots\"", html);
            Assert.Contains("screenshots/step-00-player-warp.png", html);
            Assert.Contains("draw.text_contains &quot;OE TICKET&quot;", html);
        }
        finally { Directory.Delete(rd.Root, recursive: true); }
    }

    [Fact]
    public void ScenarioReport_RendersClickableImageModal()
    {
        var rd = MakeRunDir("imagemodal");
        try
        {
            var diff = new DiffSet(
                Baseline: "scenarios/visual_path/diffs/assertion-03-bitmap/baseline.png",
                Capture: "scenarios/visual_path/diffs/assertion-03-bitmap/capture.png",
                Diff: "scenarios/visual_path/diffs/assertion-03-bitmap/diff.png",
                Triptych: null);
            var summary = new RunSummary(
                rd.RunId, "2026-04-24T15:30:45Z", 100,
                Scenarios: new[] { new ScenarioOutcome(
                    "visual_path", null, false, 100,
                    Steps: new[] { new StepOutcome("player.warp", true, 12, "Warp to FarmHouse (8,10)") },
                    Assertions: new[] { new AssertionOutcome("bitmap", false, "SSIM mismatch") },
                    Screenshots: new[]
                    {
                        "scenarios/visual_path/screenshots/step-00-player-warp.png",
                        "scenarios/visual_path/screenshots/final.png",
                    },
                    Diffs: new[] { diff }) });

            HtmlReportGenerator.Generate(rd, summary);

            var html = File.ReadAllText(Path.Combine(rd.ScenariosDir, "visual_path", "report.html"));
            Assert.Contains("<dialog id=\"image-modal\"", html);
            Assert.Contains("data-full-image-src=\"screenshots/step-00-player-warp.png\"", html);
            Assert.Contains("data-full-image-src=\"screenshots/final.png\"", html);
            Assert.Contains("data-full-image-src=\"diffs/assertion-03-bitmap/diff.png\"", html);
            Assert.Contains("id=\"image-modal-img\"", html);
            Assert.Contains("showModal", html);
        }
        finally { Directory.Delete(rd.Root, recursive: true); }
    }

    [Fact]
    public void GenerateHub_LinksRunIndexesFromReportBase()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"htmlhub-{Guid.NewGuid():N}");
        try
        {
            var first = RunDirectory.Create(baseDir, explicitRunId: "20-starberg-ui-quote-shell");
            var second = RunDirectory.Create(baseDir, explicitRunId: "23-starberg-ui-order-ticket");
            File.WriteAllText(Path.Combine(first.Root, "index.html"), "<!doctype html>");
            File.WriteAllText(Path.Combine(second.Root, "index.html"), "<!doctype html>");
            File.WriteAllText(Path.Combine(first.Root, "summary.json"), "{\"run_id\":\"20-starberg-ui-quote-shell\",\"started\":\"2026-04-24T15:30:45Z\",\"duration_ms\":12,\"scenarios\":[]}");
            File.WriteAllText(Path.Combine(second.Root, "summary.json"), "{\"run_id\":\"23-starberg-ui-order-ticket\",\"started\":\"2026-04-24T15:31:45Z\",\"duration_ms\":34,\"scenarios\":[]}");

            HtmlReportGenerator.GenerateHub(baseDir);

            var html = File.ReadAllText(Path.Combine(baseDir, "index.html"));
            Assert.Contains("20-starberg-ui-quote-shell/index.html", html);
            Assert.Contains("23-starberg-ui-order-ticket/index.html", html);
            Assert.Contains("Frobby Reports", html);
            Assert.Contains("<dialog id=\"report-modal\"", html);
            Assert.Contains("data-report-src=\"20-starberg-ui-quote-shell/index.html\"", html);
            Assert.Contains("<iframe id=\"report-frame\"", html);
        }
        finally
        {
            if (Directory.Exists(baseDir))
                Directory.Delete(baseDir, recursive: true);
        }
    }
}
