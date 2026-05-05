using System;
using System.IO;
using SdvTestFramework.Runner.Repo;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Repo;

public sealed class RepoRunPlannerTests : IDisposable
{
    private readonly string _repoRoot = CreateTempDirectory();

    [Fact]
    public void BuildRunPlan_directory_target_uses_run_suite_headless_extra_mods_default_report_and_target()
    {
        var scenarios = Directory.CreateDirectory(Path.Combine(_repoRoot, "tests", "scenarios")).FullName;
        var extraOne = Directory.CreateDirectory(Path.Combine(_repoRoot, "mods", "One")).FullName;
        var extraTwo = Directory.CreateDirectory(Path.Combine(_repoRoot, "mods", "Two")).FullName;
        var config = Config(defaultTarget: "tests/scenarios", extraMods: ["mods/One", "mods/Two"]);

        var plan = RepoRunPlanner.BuildRunPlan(
            _repoRoot,
            config,
            new RepoRunRequest(
                Visible: false,
                NoBuild: false,
                DryRun: false,
                Baseline: false,
                ModSet: null,
                ReportDir: null,
                Targets: Array.Empty<string>()));

        Assert.Equal(new[] { "dotnet", "build", "Frobby.sln" }, plan.BuildCommand);
        Assert.Equal(Path.Combine(Path.GetTempPath(), "frobby-frobby-results-1.2.3"), plan.ReportDir);
        Assert.Equal(new[] { extraOne, extraTwo }, plan.ExtraMods);
        Assert.Equal("run-suite", plan.FrobbyArgs[0]);
        Assert.Contains("--fresh-process-per-scenario", plan.FrobbyArgs);
        Assert.Contains("--headless", plan.FrobbyArgs);
        Assert.Equal(2, Count(plan.FrobbyArgs, "--extra-mod"));
        Assert.Contains(extraOne, plan.FrobbyArgs);
        Assert.Contains(extraTwo, plan.FrobbyArgs);
        Assert.Contains("--report-dir", plan.FrobbyArgs);
        Assert.Contains(plan.ReportDir, plan.FrobbyArgs);
        Assert.Equal(scenarios, plan.FrobbyArgs[^1]);
    }

    [Fact]
    public void BuildRunPlan_single_scenario_uses_run_and_no_build_yields_null_build_command()
    {
        var scenario = Path.Combine(_repoRoot, "tests", "smoke.test.json");
        Directory.CreateDirectory(Path.GetDirectoryName(scenario)!);
        File.WriteAllText(scenario, """{"name":"smoke","steps":[]}""");
        Directory.CreateDirectory(Path.Combine(_repoRoot, "mods", "One"));

        var plan = RepoRunPlanner.BuildRunPlan(
            _repoRoot,
            Config(defaultTarget: "tests/smoke.test.json", extraMods: ["mods/One"]),
            new RepoRunRequest(false, NoBuild: true, false, false, null, null, Array.Empty<string>()));

        Assert.Null(plan.BuildCommand);
        Assert.Equal("run", plan.FrobbyArgs[0]);
        Assert.DoesNotContain("--fresh-process-per-scenario", plan.FrobbyArgs);
        Assert.Equal(scenario, plan.FrobbyArgs[^1]);
    }

    [Fact]
    public void BuildRunPlan_baseline_uses_configured_baseline_target_update_baselines_and_custom_report_dir()
    {
        var baseline = Path.Combine(_repoRoot, "tests", "baseline.test.json");
        var reports = Path.Combine(_repoRoot, "artifacts", "baseline-report");
        Directory.CreateDirectory(Path.GetDirectoryName(baseline)!);
        Directory.CreateDirectory(Path.Combine(_repoRoot, "mods", "One"));
        File.WriteAllText(baseline, """{"name":"baseline","steps":[]}""");

        var plan = RepoRunPlanner.BuildRunPlan(
            _repoRoot,
            Config(defaultTarget: "tests", baselineTarget: "tests/baseline.test.json", extraMods: ["mods/One"]),
            new RepoRunRequest(false, false, false, Baseline: true, null, reports, Array.Empty<string>()));

        Assert.Equal(reports, plan.ReportDir);
        Assert.Equal("run", plan.FrobbyArgs[0]);
        Assert.Contains("--update-baselines", plan.FrobbyArgs);
        Assert.Equal(baseline, plan.FrobbyArgs[^1]);
    }

    public void Dispose()
    {
        Directory.Delete(_repoRoot, recursive: true);
    }

    private static int Count(System.Collections.Generic.IEnumerable<string> values, string expected)
        => System.Linq.Enumerable.Count(values, value => value == expected);

    private static RepoTestConfig Config(
        string defaultTarget,
        string? baselineTarget = null,
        string[]? extraMods = null)
        => new()
        {
            Project = new RepoProjectConfig { Name = "Frobby", Slug = "frobby", Version = "1.2.3" },
            Build = new RepoBuildConfig { Command = "dotnet", Args = ["build", "Frobby.sln"] },
            DefaultTarget = defaultTarget,
            BaselineTarget = baselineTarget,
            ModSets =
            [
                new RepoModSetConfig
                {
                    Name = "default",
                    ExtraMods = extraMods ?? Array.Empty<string>(),
                },
            ],
        };

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "sdv-repo-run-planner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
