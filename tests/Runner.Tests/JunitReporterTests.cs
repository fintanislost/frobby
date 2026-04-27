using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using SdvTestFramework.Runner.Reporters;
using SdvTestFramework.Runner.Scenarios;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class JunitReporterTests
{
    private static XDocument RenderTo(IReadOnlyList<ScenarioReport> reports)
    {
        var sw = new StringWriter();
        new JunitReporter().Report(reports, sw);
        return XDocument.Parse(sw.ToString());
    }

    [Fact]
    public void Report_AllPass_ProducesWellFormedSuite()
    {
        var reports = new List<ScenarioReport>
        {
            new ScenarioReport { Name = "alpha", Path = "tests/samples/alpha.test.json", Passed = true, DurationMs = 10 },
            new ScenarioReport { Name = "beta", Path = "tests/samples/beta.test.json", Passed = true, DurationMs = 20 },
        };
        var doc = RenderTo(reports);
        var root = doc.Root!;
        Assert.Equal("testsuites", root.Name.LocalName);
        Assert.Equal("2", root.Attribute("tests")!.Value);
        Assert.Equal("0", root.Attribute("failures")!.Value);
        Assert.Equal("0.030", root.Attribute("time")!.Value);

        var suite = root.Element("testsuite")!;
        Assert.Equal("sdv-test", suite.Attribute("name")!.Value);
        Assert.Equal("2", suite.Attribute("tests")!.Value);
        Assert.Equal("0", suite.Attribute("failures")!.Value);

        var cases = suite.Elements("testcase").ToList();
        Assert.Equal(2, cases.Count);
        Assert.Equal("alpha", cases[0].Attribute("name")!.Value);
        Assert.Equal("tests/samples/alpha.test.json", cases[0].Attribute("classname")!.Value);
        Assert.Equal("0.010", cases[0].Attribute("time")!.Value);
        Assert.Null(cases[0].Element("failure"));
    }

    [Fact]
    public void Report_WithFailure_EmitsFailureElement()
    {
        var reports = new List<ScenarioReport>
        {
            new ScenarioReport
            {
                Name = "broken",
                Path = "tests/samples/b.test.json",
                Passed = false,
                DurationMs = 123,
                Failures = new List<string>
                {
                    "step 'player.warp' failed: no location named: Nowhere",
                    "assertion: state.player.location == 'Farm'",
                },
            },
        };
        var doc = RenderTo(reports);
        var root = doc.Root!;
        Assert.Equal("1", root.Attribute("tests")!.Value);
        Assert.Equal("1", root.Attribute("failures")!.Value);

        var c = root.Element("testsuite")!.Element("testcase")!;
        Assert.Equal("0.123", c.Attribute("time")!.Value);
        var failure = c.Element("failure")!;
        Assert.Equal("assertion", failure.Attribute("type")!.Value);
        // message attr = first failure line (most CI UIs display only this).
        Assert.Equal("step 'player.warp' failed: no location named: Nowhere", failure.Attribute("message")!.Value);
        // Body = full list joined by \n.
        Assert.Contains("step 'player.warp' failed", failure.Value);
        Assert.Contains("assertion: state.player.location", failure.Value);
    }

    [Fact]
    public void Report_Empty_ProducesEmptySuite()
    {
        var doc = RenderTo(new List<ScenarioReport>());
        var root = doc.Root!;
        Assert.Equal("testsuites", root.Name.LocalName);
        Assert.Equal("0", root.Attribute("tests")!.Value);
    }
}
