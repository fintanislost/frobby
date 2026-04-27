using Xunit;

namespace SdvTestFramework.Runner.Tests.Reports;

/// <summary>Integration surface for HTML run reports — verified manually via T7 smoke.</summary>
public class RunReportIntegrationTests
{
    [Fact(Skip = "Requires live SDV — run-samples.sh produces a real run dir; verify by inspecting test-results/.")]
    public void RunReports_PopulatedAfterRunSamples() { }
}
