using Xunit;

namespace SdvTestFramework.Runner.Mcp.Tests;

/// <summary>Integration surface for M3 MCP — exercised via Worked/manual-smoke.sh.</summary>
public class McpIntegrationTests
{
    [Fact(Skip = "Requires live SDV + Xvfb — run Worked/manual-smoke.sh for end-to-end verification.")]
    public void EndToEnd_LaunchesSdvAndRunsOneScenario() { }
}
