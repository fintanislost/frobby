using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Commands;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Repo;

[Collection("Console")]
public sealed class RepoCommandTests : IDisposable
{
    private readonly string _repoRoot = CreateTempDirectory();

    [Fact]
    public async Task RepoRun_dry_run_prints_build_command_run_suite_headless_extra_mod_and_report_hub()
    {
        Directory.CreateDirectory(Path.Combine(_repoRoot, "mods", "Frobby"));
        Directory.CreateDirectory(Path.Combine(_repoRoot, "tests", "scenarios"));
        WriteConfig(defaultTarget: "tests/scenarios");

        var output = new StringWriter();
        var previousOut = Console.Out;
        Console.SetOut(output);
        try
        {
            var exit = await RepoCommand.RunAsync(
                new[] { "run", "--repo-root", _repoRoot, "--dry-run" }.AsMemory(),
                CancellationToken.None);

            Assert.Equal(0, exit);
        }
        finally
        {
            Console.SetOut(previousOut);
        }

        var text = output.ToString();
        Assert.Contains("cd " + _repoRoot, text);
        Assert.Contains("dotnet build Frobby.sln", text);
        Assert.Contains("sdv-test run-suite", text);
        Assert.Contains("--headless", text);
        Assert.Contains("--extra-mod", text);
        Assert.Contains(Path.Combine(_repoRoot, "mods", "Frobby"), text);
        Assert.Contains("report hub: ", text);
        Assert.Contains("index.html", text);
    }

    [Fact]
    public async Task RepoRun_non_dry_run_no_build_visible_uses_run_executor_run_suite_and_omits_headless()
    {
        Directory.CreateDirectory(Path.Combine(_repoRoot, "mods", "Frobby"));
        Directory.CreateDirectory(Path.Combine(_repoRoot, "tests", "scenarios"));
        WriteConfig(defaultTarget: "tests/scenarios");
        var calls = new List<string[]>();
        var original = RepoCommand.RunExecutor;
        RepoCommand.RunExecutor = (args, _) =>
        {
            calls.Add(args.ToArray());
            return Task.FromResult(0);
        };
        try
        {
            var exit = await RepoCommand.RunAsync(
                new[] { "run", "--repo-root", _repoRoot, "--no-build", "--visible" }.AsMemory(),
                CancellationToken.None);

            Assert.Equal(0, exit);
        }
        finally
        {
            RepoCommand.RunExecutor = original;
        }

        var call = Assert.Single(calls);
        Assert.Equal("run-suite", call[0]);
        Assert.Contains("--fresh-process-per-scenario", call);
        Assert.DoesNotContain("--headless", call);
    }

    public void Dispose()
    {
        Directory.Delete(_repoRoot, recursive: true);
    }

    private void WriteConfig(string defaultTarget)
    {
        File.WriteAllText(
            Path.Combine(_repoRoot, "sdv-test.config.json"),
            $$"""
            {
              "project": {
                "name": "Frobby",
                "slug": "frobby",
                "version": "1.2.3"
              },
              "build": {
                "command": "dotnet",
                "args": ["build", "Frobby.sln"]
              },
              "defaultTarget": "{{defaultTarget}}",
              "baselineTarget": "tests/scenarios",
              "modSets": [
                {
                  "name": "default",
                  "extraMods": ["mods/Frobby"]
                }
              ]
            }
            """);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "sdv-repo-command-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
