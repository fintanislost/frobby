using System;
using System.Collections.Generic;
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
        Assert.Equal("default", plan.ProfileId);
        Assert.Equal("default", plan.ProfileCacheNamespace);
        Assert.Equal(Path.Combine(_repoRoot, ".cache", "frobby-test-mods", "default"), plan.ModsPath);
        Assert.Equal("run-suite", plan.FrobbyArgs[0]);
        Assert.Contains("--fresh-process-per-scenario", plan.FrobbyArgs);
        Assert.Contains("--headless", plan.FrobbyArgs);
        Assert.Contains("--mods-path", plan.FrobbyArgs);
        Assert.Contains(plan.ModsPath, plan.FrobbyArgs);
        Assert.Contains("--profile-id", plan.FrobbyArgs);
        Assert.Contains("default", plan.FrobbyArgs);
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
        Assert.Equal("default", plan.ProfileId);
        Assert.Equal("default", plan.ProfileCacheNamespace);
        Assert.Equal(Path.Combine(_repoRoot, ".cache", "frobby-test-mods", "default"), plan.ModsPath);
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

    [Fact]
    public void BuildRunPlan_mod_set_selects_requested_non_first_mod_set_exactly()
    {
        Directory.CreateDirectory(Path.Combine(_repoRoot, "tests", "scenarios"));
        var defaultMod = Directory.CreateDirectory(Path.Combine(_repoRoot, "mods", "Default")).FullName;
        var selectedOne = Directory.CreateDirectory(Path.Combine(_repoRoot, "mods", "SelectedOne")).FullName;
        var selectedTwo = Directory.CreateDirectory(Path.Combine(_repoRoot, "mods", "SelectedTwo")).FullName;
        var config = Config(
            defaultTarget: "tests/scenarios",
            modSets:
            [
                ModSet("default", "mods/Default"),
                ModSet("alternate", "mods/SelectedOne", "mods/SelectedTwo"),
            ]);

        var plan = RepoRunPlanner.BuildRunPlan(
            _repoRoot,
            config,
            new RepoRunRequest(false, false, false, false, "alternate", null, Array.Empty<string>()));

        Assert.Equal(new[] { selectedOne, selectedTwo }, plan.ExtraMods);
        Assert.Equal("alternate", plan.ProfileId);
        Assert.Equal("alternate", plan.ProfileCacheNamespace);
        Assert.Equal(Path.Combine(_repoRoot, ".cache", "frobby-test-mods", "alternate"), plan.ModsPath);
        Assert.DoesNotContain(defaultMod, plan.ExtraMods);
        Assert.Contains(selectedOne, plan.FrobbyArgs);
        Assert.Contains(selectedTwo, plan.FrobbyArgs);
        Assert.DoesNotContain(defaultMod, plan.FrobbyArgs);
    }

    [Fact]
    public void BuildRunPlan_extra_mods_use_supplied_environment_for_path_expansion()
    {
        Directory.CreateDirectory(Path.Combine(_repoRoot, "tests", "scenarios"));
        var suppliedRoot = Directory.CreateDirectory(Path.Combine(_repoRoot, "outside-env-mods")).FullName;
        var envMod = Directory.CreateDirectory(Path.Combine(suppliedRoot, "EnvMod")).FullName;
        var config = Config(defaultTarget: "tests/scenarios", extraMods: ["$MOD_ROOT/EnvMod"]);
        var environment = new System.Collections.Generic.Dictionary<string, string?>
        {
            ["MOD_ROOT"] = suppliedRoot,
        };

        var plan = RepoRunPlanner.BuildRunPlan(
            _repoRoot,
            config,
            new RepoRunRequest(false, false, false, false, null, null, Array.Empty<string>()),
            environment);

        Assert.Equal(new[] { envMod }, plan.ExtraMods);
        Assert.Contains(envMod, plan.FrobbyArgs);
    }

    [Fact]
    public void BuildRunPlan_filter_is_passed_through_to_frobby_args()
    {
        Directory.CreateDirectory(Path.Combine(_repoRoot, "tests", "scenarios"));
        Directory.CreateDirectory(Path.Combine(_repoRoot, "mods", "One"));
        var config = Config(defaultTarget: "tests/scenarios", extraMods: ["mods/One"]);

        var plan = RepoRunPlanner.BuildRunPlan(
            _repoRoot,
            config,
            new RepoRunRequest(false, false, false, false, null, null, Array.Empty<string>(), Filter: "grandpa"));

        Assert.Contains("--filter", plan.FrobbyArgs);
        Assert.Contains("grandpa", plan.FrobbyArgs);
    }

    [Fact]
    public void BuildRunPlan_prepends_cached_deps_before_repo_extra_mods()
    {
        Directory.CreateDirectory(Path.Combine(_repoRoot, "tests", "scenarios"));
        var cacheRoot = Path.Combine(_repoRoot, ".cache", "deps");
        var contentPatcher = CreateCachedMod(cacheRoot, "Pathoschild.ContentPatcher", "2.7.0");
        var frobbyMod = Directory.CreateDirectory(Path.Combine(_repoRoot, "mods", "Frobby")).FullName;
        var config = Config(
            defaultTarget: "tests/scenarios",
            modSets:
            [
                new RepoModSetConfig
                {
                    Name = "core",
                    Deps =
                    [
                        new RepoModDependencyConfig { Id = "Pathoschild.ContentPatcher", Version = "2.7.0" },
                    ],
                    ExtraMods = ["mods/Frobby"],
                },
            ]);
        var environment = new Dictionary<string, string?>
        {
            [RepoDependencyCache.CacheEnvironmentVariable] = cacheRoot,
        };

        var plan = RepoRunPlanner.BuildRunPlan(
            _repoRoot,
            config,
            new RepoRunRequest(false, false, false, false, "core", null, Array.Empty<string>()),
            environment);

        Assert.Equal(new[] { contentPatcher, frobbyMod }, plan.ExtraMods);
        var firstExtraFlag = plan.FrobbyArgs.IndexOf("--extra-mod");
        Assert.Equal(contentPatcher, plan.FrobbyArgs[firstExtraFlag + 1]);
        var secondExtraFlag = plan.FrobbyArgs.IndexOf("--extra-mod", firstExtraFlag + 1);
        Assert.Equal(frobbyMod, plan.FrobbyArgs[secondExtraFlag + 1]);
    }

    [Fact]
    public void BuildRunPlan_profile_adds_profile_mods_metadata_overlays_and_isolated_mods_path()
    {
        Directory.CreateDirectory(Path.Combine(_repoRoot, "tests", "scenarios"));
        var core = Directory.CreateDirectory(Path.Combine(_repoRoot, "mods", "Core")).FullName;
        var farm = Directory.CreateDirectory(Path.Combine(_repoRoot, "mods", "GrandpasFarm")).FullName;
        var overlay = Path.Combine(_repoRoot, "tests", "config", "content-patcher.json");
        Directory.CreateDirectory(Path.GetDirectoryName(overlay)!);
        File.WriteAllText(overlay, "{}");
        var config = Config(
            defaultTarget: "tests/scenarios",
            profiles: new Dictionary<string, RepoProfileConfig>
            {
                ["sve-core"] = new() { ExtraMods = ["mods/Core"] },
                ["sve-grandpas-farm"] = new()
                {
                    Inherits = "sve-core",
                    ExtraMods = ["mods/GrandpasFarm"],
                    CacheNamespace = "sve-grandpas-farm",
                    ConfigOverlays =
                    [
                        new RepoConfigOverlayConfig
                        {
                            Source = "tests/config/content-patcher.json",
                            TargetMod = "Pathoschild.ContentPatcher",
                            TargetPath = "config.json",
                        },
                    ],
                },
            });

        var plan = RepoRunPlanner.BuildRunPlan(
            _repoRoot,
            config,
            new RepoRunRequest(false, false, false, false, "sve-grandpas-farm", null, Array.Empty<string>()),
            new Dictionary<string, string?>());

        Assert.Equal("sve-grandpas-farm", plan.ProfileId);
        Assert.Equal("sve-grandpas-farm", plan.ProfileCacheNamespace);
        Assert.Equal(Path.Combine(_repoRoot, ".cache", "frobby-test-mods", "sve-grandpas-farm"), plan.ModsPath);
        Assert.Equal([core, farm], plan.ExtraMods);
        Assert.Contains("--mods-path", plan.FrobbyArgs);
        Assert.Contains(plan.ModsPath, plan.FrobbyArgs);
        Assert.Contains("--profile-id", plan.FrobbyArgs);
        Assert.Contains("sve-grandpas-farm", plan.FrobbyArgs);
        Assert.Contains("--profile-cache-namespace", plan.FrobbyArgs);
        Assert.Contains("--config-overlay", plan.FrobbyArgs);
        Assert.Contains(overlay, plan.FrobbyArgs);
        Assert.Contains("Pathoschild.ContentPatcher", plan.FrobbyArgs);
        Assert.Contains("config.json", plan.FrobbyArgs);
    }

    [Fact]
    public void BuildRunPlan_allows_missing_repo_extra_mods_when_build_will_run()
    {
        Directory.CreateDirectory(Path.Combine(_repoRoot, "tests", "scenarios"));
        var missingBuiltMod = Path.Combine(_repoRoot, ".cache", "frobby-game-mods", "ExampleMod");
        var config = Config(defaultTarget: "tests/scenarios", extraMods: [".cache/frobby-game-mods/ExampleMod"]);

        var plan = RepoRunPlanner.BuildRunPlan(
            _repoRoot,
            config,
            new RepoRunRequest(false, NoBuild: false, false, false, null, null, Array.Empty<string>()));

        Assert.Equal(new[] { missingBuiltMod }, plan.ExtraMods);
        Assert.NotNull(plan.BuildCommand);
    }

    [Fact]
    public void BuildRunPlan_requires_repo_extra_mods_when_no_build_is_set()
    {
        Directory.CreateDirectory(Path.Combine(_repoRoot, "tests", "scenarios"));
        var config = Config(defaultTarget: "tests/scenarios", extraMods: [".cache/frobby-game-mods/ExampleMod"]);

        var ex = Assert.Throws<DirectoryNotFoundException>(() =>
            RepoRunPlanner.BuildRunPlan(
                _repoRoot,
                config,
                new RepoRunRequest(false, NoBuild: true, false, false, null, null, Array.Empty<string>())));

        Assert.Contains(".cache/frobby-game-mods/ExampleMod", ex.Message);
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
        string[]? extraMods = null,
        RepoModSetConfig[]? modSets = null,
        IReadOnlyDictionary<string, RepoProfileConfig>? profiles = null)
        => new()
        {
            Project = new RepoProjectConfig { Name = "Frobby", Slug = "frobby", Version = "1.2.3" },
            Build = new RepoBuildConfig { Command = "dotnet", Args = ["build", "Frobby.sln"] },
            DefaultTarget = defaultTarget,
            BaselineTarget = baselineTarget,
            ModSets = modSets ?? [ModSet("default", extraMods ?? Array.Empty<string>())],
            Profiles = profiles ?? new Dictionary<string, RepoProfileConfig>(),
        };

    private static RepoModSetConfig ModSet(string name, params string[] extraMods)
        => new()
        {
            Name = name,
            ExtraMods = extraMods,
        };

    private static string CreateCachedMod(string cacheRoot, string uniqueId, string version)
    {
        var path = Path.Combine(cacheRoot, uniqueId);
        Directory.CreateDirectory(path);
        File.WriteAllText(
            Path.Combine(path, "manifest.json"),
            $$"""{"Name":"Test","UniqueID":"{{uniqueId}}","Version":"{{version}}","EntryDll":"Test.dll"}""");
        return path;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "sdv-repo-run-planner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
