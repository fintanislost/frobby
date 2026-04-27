using System;
using System.Collections.Generic;
using System.IO;
using SdvTestFramework.Protocol.Reports;
using SdvTestFramework.Runner.Reports;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Reports;

public class HtmlReportGeneratorForensicsTests
{
    private static RunDirectory MakeRunDir(string testName)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"forensics-{testName}-{Guid.NewGuid():N}");
        return RunDirectory.Create(tmp);
    }

    [Fact]
    public void ScenarioWithDiffs_RendersForensicsSection()
    {
        var rd = MakeRunDir("with");
        try
        {
            var diff = new DiffSet(
                Baseline: "scenarios/x/diffs/assertion-03-bitmap/baseline.png",
                Capture:  "scenarios/x/diffs/assertion-03-bitmap/capture.png",
                Diff:     "scenarios/x/diffs/assertion-03-bitmap/diff.png",
                Triptych: null);
            var summary = new RunSummary(
                rd.RunId, "2026-04-25T15:30:00Z", 0,
                Scenarios: new[] { new ScenarioOutcome(
                    "x", null, false, 100,
                    Steps: Array.Empty<StepOutcome>(),
                    Assertions: new[] { new AssertionOutcome("bitmap", false, "SSIM 0.7234 < tolerance 0.9500") },
                    Screenshots: Array.Empty<string>(),
                    Diffs: new[] { diff }) });

            HtmlReportGenerator.Generate(rd, summary);

            var html = File.ReadAllText(Path.Combine(rd.ScenariosDir, "x", "report.html"));
            Assert.Contains("class=\"forensics\"", html);
            Assert.Contains("diffs/assertion-03-bitmap/baseline.png", html);
            Assert.Contains("diffs/assertion-03-bitmap/capture.png", html);
            Assert.Contains("diffs/assertion-03-bitmap/diff.png", html);
        }
        finally { Directory.Delete(rd.Root, recursive: true); }
    }

    [Fact]
    public void ScenarioWithoutDiffs_HasNoForensicsSection()
    {
        var rd = MakeRunDir("without");
        try
        {
            var summary = new RunSummary(
                rd.RunId, "2026-04-25T15:30:00Z", 0,
                Scenarios: new[] { new ScenarioOutcome(
                    "x", null, true, 100,
                    Steps: Array.Empty<StepOutcome>(),
                    Assertions: Array.Empty<AssertionOutcome>(),
                    Screenshots: Array.Empty<string>(),
                    Diffs: Array.Empty<DiffSet>()) });

            HtmlReportGenerator.Generate(rd, summary);

            var html = File.ReadAllText(Path.Combine(rd.ScenariosDir, "x", "report.html"));
            Assert.DoesNotContain("class=\"forensics\"", html);
            Assert.DoesNotContain("Failure forensics", html);
        }
        finally { Directory.Delete(rd.Root, recursive: true); }
    }
}
