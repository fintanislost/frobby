using System;
using System.IO;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public sealed class NuGetPublishWorkflowTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void Publish_workflow_is_tag_guarded_and_runs_dry_run_before_push()
    {
        var workflowPath = Path.Combine(RepoRoot, ".github", "workflows", "publish-nuget.yml");
        Assert.True(File.Exists(workflowPath), workflowPath);

        var workflow = File.ReadAllText(workflowPath);
        Assert.Contains("workflow_dispatch:", workflow);
        Assert.Contains("tags: [ 'v*' ]", workflow);
        Assert.Contains("permissions:", workflow);
        Assert.Contains("contents: read", workflow);
        Assert.Contains("uses: actions/checkout@v6", workflow);
        Assert.Contains("uses: actions/setup-dotnet@v5", workflow);
        Assert.Contains("uses: actions/upload-artifact@v7", workflow);
        Assert.Contains("./scripts/release-dry-run.sh", workflow);
        Assert.Contains("nupkg/*.nupkg", workflow);
        Assert.Contains("nupkg/release-dry-run.json", workflow);
        Assert.Contains("github.event_name == 'push'", workflow);
        Assert.Contains("startsWith(github.ref, 'refs/tags/v')", workflow);
        Assert.Contains("NUGET_API_KEY", workflow);
        Assert.Contains("dotnet nuget push", workflow);
        Assert.Contains("--skip-duplicate", workflow);
        Assert.Contains("--source https://api.nuget.org/v3/index.json", workflow);
        Assert.True(
            workflow.IndexOf("./scripts/release-dry-run.sh", StringComparison.Ordinal)
            < workflow.IndexOf("dotnet nuget push", StringComparison.Ordinal),
            "The publish step must run after the release dry-run step.");
    }

    [Fact]
    public void Publish_workflow_refuses_to_push_without_secret()
    {
        var workflowPath = Path.Combine(RepoRoot, ".github", "workflows", "publish-nuget.yml");
        Assert.True(File.Exists(workflowPath), workflowPath);

        var workflow = File.ReadAllText(workflowPath);
        Assert.Contains("if [ -z \"$NUGET_API_KEY\" ]; then", workflow);
        Assert.Contains("NUGET_API_KEY secret is required", workflow);
        Assert.Contains("exit 1", workflow);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "sdv-test-framework.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate Frobby repository root.");
    }
}
