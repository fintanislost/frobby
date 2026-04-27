using System.Collections.Generic;
using System.IO;
using SdvTestFramework.Runner.Reporters;
using SdvTestFramework.Runner.Scenarios;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class ConsoleReporterTests
{
    [Fact]
    public void Report_MixedPassFail_MatchesExistingOutputShape()
    {
        var reports = new List<ScenarioReport>
        {
            new ScenarioReport { Name = "alpha", Path = "/abs/alpha.test.json", Passed = true, DurationMs = 443 },
            new ScenarioReport
            {
                Name = "beta",
                Path = "/abs/beta.test.json",
                Passed = false,
                DurationMs = 454,
                Failures = new List<string> { "draw.contains: thing not found" },
            },
        };
        var writer = new StringWriter();
        new ConsoleReporter().Report(reports, writer);

        var expected =
            "  PASS alpha (443ms) — /abs/alpha.test.json\n" +
            "  FAIL beta (454ms) — /abs/beta.test.json\n" +
            "        draw.contains: thing not found\n" +
            "\n" +
            "[run] 1/2 passed\n";
        Assert.Equal(expected, writer.ToString().Replace("\r\n", "\n"));
    }

    [Fact]
    public void Report_AllPass_PrintsAllPassedLine()
    {
        var reports = new List<ScenarioReport>
        {
            new ScenarioReport { Name = "x", Path = "/x.test.json", Passed = true, DurationMs = 10 },
        };
        var writer = new StringWriter();
        new ConsoleReporter().Report(reports, writer);
        Assert.Contains("[run] 1/1 passed", writer.ToString());
    }
}
