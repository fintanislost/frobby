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

    [Fact]
    public async Task RepoRun_dry_run_parses_headless_baseline_mod_set_report_dir_and_trailing_target()
    {
        Directory.CreateDirectory(Path.Combine(_repoRoot, "mods", "Default"));
        var alternateMod = Directory.CreateDirectory(Path.Combine(_repoRoot, "mods", "Alternate")).FullName;
        var target = Path.Combine(_repoRoot, "tests", "custom.test.json");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, """{"name":"custom","steps":[]}""");
        var reportDir = Path.Combine(_repoRoot, "artifacts", "custom-report");
        WriteConfig(
            defaultTarget: "tests/custom.test.json",
            modSetsJson:
                """
                [
                  {
                    "name": "default",
                    "extraMods": ["mods/Default"]
                  },
                  {
                    "name": "alternate",
                    "extraMods": ["mods/Alternate"]
                  }
                ]
                """);

        var output = new StringWriter();
        var previousOut = Console.Out;
        Console.SetOut(output);
        try
        {
            var exit = await RepoCommand.RunAsync(
                new[]
                {
                    "run",
                    "--repo-root",
                    _repoRoot,
                    "--dry-run",
                    "--headless",
                    "--baseline",
                    "--mod-set",
                    "alternate",
                    "--report-dir",
                    reportDir,
                    "tests/custom.test.json",
                }.AsMemory(),
                CancellationToken.None);

            Assert.Equal(0, exit);
        }
        finally
        {
            Console.SetOut(previousOut);
        }

        var text = output.ToString();
        Assert.Contains("sdv-test run ", text);
        Assert.Contains("--headless", text);
        Assert.Contains("--update-baselines", text);
        Assert.Contains("--report-dir " + reportDir, text);
        Assert.Contains("--extra-mod " + alternateMod, text);
        Assert.DoesNotContain(Path.Combine(_repoRoot, "mods", "Default"), text);
        Assert.Contains(target, text);
    }

    [Fact]
    public async Task RepoRun_bad_config_path_returns_exit_2_and_writes_repo_prefix()
    {
        Directory.CreateDirectory(Path.Combine(_repoRoot, "mods", "Frobby"));
        WriteConfig(defaultTarget: "tests/missing");
        var error = new StringWriter();
        var previousError = Console.Error;
        Console.SetError(error);
        try
        {
            var exit = await RepoCommand.RunAsync(
                new[] { "run", "--repo-root", _repoRoot, "--dry-run" }.AsMemory(),
                CancellationToken.None);

            Assert.Equal(2, exit);
        }
        finally
        {
            Console.SetError(previousError);
        }

        Assert.Contains("[repo]", error.ToString());
    }

    [Fact]
    public async Task RepoRun_malformed_config_returns_exit_2_and_writes_repo_prefix()
    {
        File.WriteAllText(Path.Combine(_repoRoot, "sdv-test.config.json"), """{"project":""");
        var error = new StringWriter();
        var previousError = Console.Error;
        Console.SetError(error);
        try
        {
            var exit = await RepoCommand.RunAsync(
                new[] { "run", "--repo-root", _repoRoot, "--dry-run" }.AsMemory(),
                CancellationToken.None);

            Assert.Equal(2, exit);
        }
        finally
        {
            Console.SetError(previousError);
        }

        Assert.Contains("[repo]", error.ToString());
    }

    [Fact]
    public async Task RepoRun_missing_build_executable_returns_nonzero_repo_error_instead_of_throwing()
    {
        Directory.CreateDirectory(Path.Combine(_repoRoot, "mods", "Frobby"));
        Directory.CreateDirectory(Path.Combine(_repoRoot, "tests", "scenarios"));
        WriteConfig(
            defaultTarget: "tests/scenarios",
            buildCommand: "sdv-test-clearly-missing-build-command");

        var calls = new List<string[]>();
        var original = RepoCommand.RunExecutor;
        RepoCommand.RunExecutor = (args, _) =>
        {
            calls.Add(args.ToArray());
            return Task.FromResult(0);
        };
        var error = new StringWriter();
        var previousError = Console.Error;
        Console.SetError(error);
        try
        {
            var exit = await RepoCommand.RunAsync(
                new[] { "run", "--repo-root", _repoRoot }.AsMemory(),
                CancellationToken.None);

            Assert.NotEqual(0, exit);
        }
        finally
        {
            Console.SetError(previousError);
            RepoCommand.RunExecutor = original;
        }

        Assert.Empty(calls);
        Assert.Contains("[repo]", error.ToString());
        Assert.Contains("sdv-test-clearly-missing-build-command", error.ToString());
    }

    [Fact]
    public async Task RepoRun_dry_run_defaults_sdv_game_mods_from_sdv_install_path_mods_dir()
    {
        Directory.CreateDirectory(Path.Combine(_repoRoot, "tests", "scenarios"));
        var installPath = Directory.CreateDirectory(Path.Combine(_repoRoot, "fake-sdv")).FullName;
        var contentPatcher = Directory.CreateDirectory(Path.Combine(installPath, "Mods", "ContentPatcher")).FullName;
        WriteConfig(
            defaultTarget: "tests/scenarios",
            modSetsJson:
                """
                [
                  {
                    "name": "default",
                    "extraMods": ["${SDV_GAME_MODS}/ContentPatcher"]
                  }
                ]
                """);

        var previousInstallPath = Environment.GetEnvironmentVariable("SDV_INSTALL_PATH");
        var previousGameMods = Environment.GetEnvironmentVariable("SDV_GAME_MODS");
        Environment.SetEnvironmentVariable("SDV_INSTALL_PATH", installPath);
        Environment.SetEnvironmentVariable("SDV_GAME_MODS", null);
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
            Environment.SetEnvironmentVariable("SDV_INSTALL_PATH", previousInstallPath);
            Environment.SetEnvironmentVariable("SDV_GAME_MODS", previousGameMods);
        }

        Assert.Contains(contentPatcher, output.ToString());
    }

    [Fact]
    public async Task RepoRepeat_short_count_invokes_run_executor_requested_times_and_uses_default_run_report_dirs()
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
                new[] { "repeat", "--repo-root", _repoRoot, "--no-build", "-n", "2" }.AsMemory(),
                CancellationToken.None);

            Assert.Equal(0, exit);
        }
        finally
        {
            RepoCommand.RunExecutor = original;
        }

        Assert.Equal(2, calls.Count);
        Assert.Equal(
            Path.Combine(Path.GetTempPath(), "frobby-frobby-repeat-1.2.3", "run-01"),
            ReportDir(calls[0]));
        Assert.Equal(
            Path.Combine(Path.GetTempPath(), "frobby-frobby-repeat-1.2.3", "run-02"),
            ReportDir(calls[1]));
    }

    [Fact]
    public async Task RepoRepeat_report_dir_is_used_as_repeat_base_with_per_run_subdirectories()
    {
        Directory.CreateDirectory(Path.Combine(_repoRoot, "mods", "Frobby"));
        Directory.CreateDirectory(Path.Combine(_repoRoot, "tests", "scenarios"));
        WriteConfig(defaultTarget: "tests/scenarios");
        var reportBase = Path.Combine(_repoRoot, "reports", "repeat");
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
                new[] { "repeat", "--repo-root", _repoRoot, "--no-build", "--count", "2", "--report-dir", reportBase }.AsMemory(),
                CancellationToken.None);

            Assert.Equal(0, exit);
        }
        finally
        {
            RepoCommand.RunExecutor = original;
        }

        Assert.Equal(2, calls.Count);
        Assert.Equal(Path.Combine(reportBase, "run-01"), ReportDir(calls[0]));
        Assert.Equal(Path.Combine(reportBase, "run-02"), ReportDir(calls[1]));
    }

    [Fact]
    public async Task RepoRepeat_dry_run_prints_build_only_for_first_run()
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
                new[] { "repeat", "--repo-root", _repoRoot, "--dry-run", "--count", "2" }.AsMemory(),
                CancellationToken.None);

            Assert.Equal(0, exit);
        }
        finally
        {
            Console.SetOut(previousOut);
        }

        Assert.Equal(1, Count(output.ToString(), "dotnet build Frobby.sln"));
    }

    [Fact]
    public async Task RepoInit_creates_scaffold()
    {
        var output = new StringWriter();
        var previousOut = Console.Out;
        Console.SetOut(output);
        try
        {
            var exit = await RepoCommand.RunAsync(
                new[] { "init", "--repo-root", _repoRoot }.AsMemory(),
                CancellationToken.None);

            Assert.Equal(0, exit);
        }
        finally
        {
            Console.SetOut(previousOut);
        }

        Assert.True(File.Exists(Path.Combine(_repoRoot, "sdv-test.config.json")));
        Assert.Contains(_repoRoot, output.ToString());
    }

    public void Dispose()
    {
        Directory.Delete(_repoRoot, recursive: true);
    }

    private static string ReportDir(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (args[i] == "--report-dir")
            {
                return args[i + 1];
            }
        }

        throw new Xunit.Sdk.XunitException("Expected --report-dir followed by a value.");
    }

    private static int Count(string value, string expected)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(expected, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += expected.Length;
        }

        return count;
    }

    private void WriteConfig(string defaultTarget, string? modSetsJson = null, string buildCommand = "dotnet")
    {
        modSetsJson ??=
            """
            [
              {
                "name": "default",
                "extraMods": ["mods/Frobby"]
              }
            ]
            """;
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
                "command": "{{buildCommand}}",
                "args": ["build", "Frobby.sln"]
              },
              "defaultTarget": "{{defaultTarget}}",
              "baselineTarget": "tests/scenarios",
              "modSets": {{modSetsJson}}
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
