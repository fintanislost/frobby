using System;
using System.IO;
using Xunit;

namespace SdvTestFramework.Repository.Tests;

public sealed class ReleaseWorkflowTests
{
    private static readonly string RepoRoot = RepositoryTestPaths.FindRepoRoot();

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
        Assert.Contains("scripts/release-env-preflight.sh", script);
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
    public void Release_dry_run_workflow_is_game_backed_and_not_a_main_push_check()
    {
        var workflowPath = Path.Combine(RepoRoot, ".github", "workflows", "release-dry-run.yml");
        Assert.True(File.Exists(workflowPath), workflowPath);

        var workflow = File.ReadAllText(workflowPath);
        Assert.Contains("workflow_dispatch:", workflow);
        Assert.Contains("tags: [ 'v*' ]", workflow);
        Assert.DoesNotContain("branches: [ main ]", workflow);
        Assert.Contains("runs-on: ${{ vars.FROBBY_RELEASE_RUNNER || 'ubuntu-latest' }}", workflow);
        Assert.Contains("FROBBY_RELEASE_RUNNER", workflow);
        Assert.Contains("FROBBY_GAME_PATH: ${{ vars.FROBBY_GAME_PATH }}", workflow);
        Assert.Contains("./scripts/release-env-preflight.sh", workflow);
        Assert.Contains("./scripts/release-dry-run.sh", workflow);
        Assert.Contains("actions/checkout@v6", workflow);
        Assert.Contains("actions/setup-dotnet@v5", workflow);
        Assert.Contains("actions/upload-artifact@v7", workflow);
        Assert.Contains("nupkg/*.nupkg", workflow);
        Assert.Contains("nupkg/release-dry-run.json", workflow);
        Assert.DoesNotContain("dotnet nuget push", workflow);
        Assert.DoesNotContain("NUGET_API_KEY", workflow);
        Assert.DoesNotContain("NUGET_TOKEN", workflow);
    }

    [Fact]
    public void Release_environment_preflight_explains_hosted_runner_game_path_requirement()
    {
        var scriptPath = Path.Combine(RepoRoot, "scripts", "release-env-preflight.sh");
        Assert.True(File.Exists(scriptPath), scriptPath);

        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(scriptPath);
            Assert.True(
                mode.HasFlag(UnixFileMode.UserExecute),
                "scripts/release-env-preflight.sh should be executable by the owner.");
        }

        var script = File.ReadAllText(scriptPath);
        Assert.Contains("set -euo pipefail", script);
        Assert.Contains("GITHUB_ACTIONS", script);
        Assert.Contains("FROBBY_GAME_PATH", script);
        Assert.Contains("Stardew Valley.dll", script);
        Assert.Contains("StardewModdingAPI.dll", script);
        Assert.Contains("Pathoschild.Stardew.ModBuildConfig", script);
        Assert.Contains("public hosted runners do not include Stardew Valley", script);
    }

    [Fact]
    public void Pack_script_forwards_explicit_game_path_to_solution_build()
    {
        var scriptPath = Path.Combine(RepoRoot, "scripts", "pack.sh");
        Assert.True(File.Exists(scriptPath), scriptPath);

        var script = File.ReadAllText(scriptPath);
        Assert.Contains("FROBBY_GAME_PATH", script);
        Assert.Contains("/p:GamePath=$FROBBY_GAME_PATH", script);
        Assert.Contains("dotnet build", script);
    }
}
