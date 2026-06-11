using System;
using System.IO;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public sealed class ReleaseDryRunScriptTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void Release_dry_run_script_validates_packages_and_never_publishes()
    {
        var scriptPath = Path.Combine(RepoRoot, "scripts", "release-dry-run.sh");
        Assert.True(File.Exists(scriptPath), scriptPath);

        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(scriptPath);
            Assert.True(
                mode.HasFlag(UnixFileMode.UserExecute),
                "scripts/release-dry-run.sh should be executable by the owner.");
        }

        var script = File.ReadAllText(scriptPath);
        Assert.Contains("set -euo pipefail", script);
        Assert.Contains("scripts/package-install-smoke.sh", script);
        Assert.Contains("SdvTestFramework.Protocol", script);
        Assert.Contains("SdvTestFramework.Runner.Dsl", script);
        Assert.Contains("SdvTestFramework.Cli", script);
        Assert.Contains("release-dry-run.json", script);
        Assert.Contains("Directory.Build.props", script);
        Assert.DoesNotContain("dotnet nuget push", script);
        Assert.DoesNotContain("NUGET_API_KEY", script);
    }

    [Fact]
    public void Release_dry_run_workflow_uploads_artifacts_without_publish_secret()
    {
        var workflowPath = Path.Combine(RepoRoot, ".github", "workflows", "release-dry-run.yml");
        Assert.True(File.Exists(workflowPath), workflowPath);

        var workflow = File.ReadAllText(workflowPath);
        Assert.Contains("workflow_dispatch:", workflow);
        Assert.Contains("branches: [ main ]", workflow);
        Assert.Contains("tags: [ 'v*' ]", workflow);
        Assert.Contains("actions/checkout@v6", workflow);
        Assert.Contains("actions/setup-dotnet@v5", workflow);
        Assert.Contains("actions/upload-artifact@v7", workflow);
        Assert.Contains("./scripts/release-dry-run.sh", workflow);
        Assert.Contains("nupkg/*.nupkg", workflow);
        Assert.Contains("nupkg/release-dry-run.json", workflow);
        Assert.DoesNotContain("dotnet nuget push", workflow);
        Assert.DoesNotContain("NUGET_API_KEY", workflow);
        Assert.DoesNotContain("NUGET_TOKEN", workflow);
    }

    [Fact]
    public void Release_dry_run_artifacts_are_gitignored()
    {
        var gitignorePath = Path.Combine(RepoRoot, ".gitignore");
        var gitignore = File.ReadAllText(gitignorePath);

        Assert.Contains("nupkg/", gitignore);
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
