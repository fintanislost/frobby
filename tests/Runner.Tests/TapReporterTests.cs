using System.Collections.Generic;
using System.IO;
using SdvTestFramework.Runner.Reporters;
using SdvTestFramework.Runner.Scenarios;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class TapReporterTests
{
    private static string RenderTo(IReadOnlyList<ScenarioReport> reports)
    {
        var sw = new StringWriter();
        new TapReporter().Report(reports, sw);
        return sw.ToString().Replace("\r\n", "\n");
    }

    [Fact]
    public void Report_AllPass_EmitsOkLinesWithPlan()
    {
        var reports = new List<ScenarioReport>
        {
            new ScenarioReport { Name = "alpha", Path = "/a.test.json", Passed = true, DurationMs = 10 },
            new ScenarioReport { Name = "beta", Path = "/b.test.json", Passed = true, DurationMs = 20 },
        };
        var expected =
            "TAP version 13\n" +
            "1..2\n" +
            "ok 1 - alpha\n" +
            "ok 2 - beta\n";
        Assert.Equal(expected, RenderTo(reports));
    }

    [Fact]
    public void Report_FailureWithYamlBlock_IncludesDiagnostics()
    {
        var reports = new List<ScenarioReport>
        {
            new ScenarioReport
            {
                Name = "broken",
                Path = "/b.test.json",
                Passed = false,
                DurationMs = 125,
                Failures = new List<string> { "step 'player.warp' failed: no location named: Nowhere" },
            },
        };
        var output = RenderTo(reports);
        Assert.Contains("TAP version 13\n", output);
        Assert.Contains("1..1\n", output);
        Assert.Contains("not ok 1 - broken\n", output);
        Assert.Contains("  ---\n", output);
        Assert.Contains("  duration_ms: 125\n", output);
        Assert.Contains("  failures:\n", output);
        Assert.Contains("    - \"step 'player.warp' failed: no location named: Nowhere\"\n", output);
        Assert.Contains("  ...\n", output);
    }

    [Fact]
    public void Report_EmptyReports_EmitsZeroPlan()
    {
        var expected =
            "TAP version 13\n" +
            "1..0\n";
        Assert.Equal(expected, RenderTo(new List<ScenarioReport>()));
    }
}
