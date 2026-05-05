using System;
using System.IO;
using SdvTestFramework.Runner.Reports;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Reports;

public class ReportRunIdTests
{
    [Fact]
    public void ForExplicitReportBase_UsesSingleScenarioFileStem()
    {
        var path = Path.Combine("tests", "sdv", "23-example-ui-order-ticket.test.json");

        var runId = ReportRunId.ForExplicitReportBase(new[] { path }, filter: null);

        Assert.Equal("23-example-ui-order-ticket", runId);
    }

    [Fact]
    public void ForExplicitReportBase_UsesNumericRangeForMultipleScenarioFiles()
    {
        var runId = ReportRunId.ForExplicitReportBase(new[]
        {
            Path.Combine("tests", "sdv", "20-example-ui-quote-shell.test.json"),
            Path.Combine("tests", "sdv", "26-example-ui-visual-baseline.test.json"),
        }, filter: null);

        Assert.Equal("20-26", runId);
    }

    [Fact]
    public void ForExplicitReportBase_UsesFilterWhenPresent()
    {
        var runId = ReportRunId.ForExplicitReportBase(new[] { "tests/sdv" }, filter: "ui quote");

        Assert.Equal("filter-ui-quote", runId);
    }
}
