using System;
using System.IO;
using Xunit;

namespace SdvTestFramework.Repository.Tests;

public sealed class PublicCiWorkflowTests
{
    private static readonly string RepoRoot = RepositoryTestPaths.FindRepoRoot();

    [Fact]
    public void Public_ci_workflow_runs_hosted_validation_without_game_backed_release_packaging()
    {
        var workflowPath = Path.Combine(RepoRoot, ".github", "workflows", "ci.yml");
        Assert.True(File.Exists(workflowPath), workflowPath);

        var workflow = File.ReadAllText(workflowPath);
        Assert.Contains("name: Public CI", workflow);
        Assert.Contains("branches: [ main ]", workflow);
        Assert.Contains("pull_request:", workflow);
        Assert.Contains("actions/checkout@v6", workflow);
        Assert.Contains("actions/setup-dotnet@v5", workflow);
        Assert.Contains("dotnet-version: |", workflow);
        Assert.Contains("6.0.x", workflow);
        Assert.Contains("10.0.x", workflow);
        Assert.Contains("./scripts/ci-public.sh", workflow);
        Assert.DoesNotContain("./scripts/release-dry-run.sh", workflow);
        Assert.DoesNotContain("dotnet nuget push", workflow);
        Assert.DoesNotContain("NUGET_API_KEY", workflow);
    }

    [Fact]
    public void Public_ci_script_documents_and_runs_only_non_game_backed_checks()
    {
        var scriptPath = Path.Combine(RepoRoot, "scripts", "ci-public.sh");
        Assert.True(File.Exists(scriptPath), scriptPath);

        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(scriptPath);
            Assert.True(
                mode.HasFlag(UnixFileMode.UserExecute),
                "scripts/ci-public.sh should be executable by the owner.");
        }

        var script = File.ReadAllText(scriptPath);
        Assert.Contains("set -euo pipefail", script);
        Assert.Contains("tests/Repository.Tests/Repository.Tests.csproj", script);
        Assert.Contains("tests/Protocol.Tests/Protocol.Tests.csproj", script);
        Assert.Contains("tests/Runner.Mcp.Tests/Runner.Mcp.Tests.csproj", script);
        Assert.Contains("tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj", script);
        Assert.DoesNotContain("sdv-test-framework.slnx", script);
        Assert.DoesNotContain("tests/Harness.Tests/Harness.Tests.csproj", script);
        Assert.DoesNotContain("tests/Runner.Tests/Runner.Tests.csproj", script);
        Assert.DoesNotContain("scripts/release-dry-run.sh", script);
        Assert.DoesNotContain("scripts/pack.sh", script);
    }
}
