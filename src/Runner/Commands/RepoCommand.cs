using System;
using System.Collections;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Models;
using SdvTestFramework.Protocol.Scenarios;
using SdvTestFramework.Runner.Repo;

namespace SdvTestFramework.Runner.Commands;

public static class RepoCommand
{
    public static Func<IReadOnlyList<string>, CancellationToken, Task<int>> RunExecutor { get; set; }
        = RunPlannedFrobbyAsync;

    public static async Task<int> RunAsync(ReadOnlyMemory<string> args, CancellationToken ct)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: sdv-test repo <run|repeat|init|deps> [args...]");
            return 64;
        }

        var subcommand = args.Span[0];
        var rest = args[1..];
        try
        {
            return subcommand switch
            {
                "run" => await RunRepoRunAsync(rest, ct),
                "repeat" => await RunRepoRepeatAsync(rest, ct),
                "init" => RepoScaffoldGenerator.RunInit(rest),
                "deps" => RunRepoDeps(rest),
                _ => Unknown(subcommand),
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or JsonException or Win32Exception)
        {
            Console.Error.WriteLine($"[repo] {ex.Message}");
            return 2;
        }
    }

    private static async Task<int> RunRepoRunAsync(ReadOnlyMemory<string> args, CancellationToken ct)
    {
        var options = ParseRunOptions(args);
        var config = RepoTestConfig.Load(options.RepoRoot);
        var environment = BuildRepoEnvironment();
        var profiledScenarioPlans = BuildProfiledScenarioPlans(options, config, environment);
        if (profiledScenarioPlans.Count > 0)
        {
            var buildPlan = RepoRunPlanner.BuildRunPlan(options.RepoRoot, config, options.ToRequest(), environment);
            if (options.DryRun)
            {
                PrintDryRunBuild(buildPlan);
                foreach (var scenarioPlan in profiledScenarioPlans)
                {
                    PrintDryRunRun(scenarioPlan);
                }

                return 0;
            }

            {
                var profiledBuildExit = await RunBuildIfNeededAsync(buildPlan, ct);
                if (profiledBuildExit != 0)
                {
                    return profiledBuildExit;
                }
            }

            var worstExit = 0;
            foreach (var scenarioPlan in profiledScenarioPlans)
            {
                var exit = await RunExecutor(scenarioPlan.FrobbyArgs, ct);
                worstExit = Math.Max(worstExit, exit);
            }

            return worstExit;
        }

        var plan = RepoRunPlanner.BuildRunPlan(options.RepoRoot, config, options.ToRequest(), environment);

        if (options.DryRun)
        {
            PrintDryRun(plan);
            return 0;
        }

        var buildExit = await RunBuildIfNeededAsync(plan, ct);
        if (buildExit != 0)
        {
            return buildExit;
        }

        return await RunExecutor(plan.FrobbyArgs, ct);
    }

    private static List<RepoRunPlan> BuildProfiledScenarioPlans(
        RunOptions options,
        RepoTestConfig config,
        IReadOnlyDictionary<string, string?> environment)
    {
        if (options.Baseline)
        {
            return new List<RepoRunPlan>();
        }

        var rawTargets = options.Targets.Count > 0
            ? options.Targets
            : new[] { RequireText(config.DefaultTarget, "defaultTarget") };
        var scenarios = DiscoverRepoScenarios(options.RepoRoot, rawTargets, environment);
        if (!string.IsNullOrWhiteSpace(options.Filter))
        {
            scenarios = scenarios
                .Where(item => item.Spec.Name.Contains(options.Filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (scenarios.Count == 0 || scenarios.All(item => string.IsNullOrWhiteSpace(item.Spec.Profile)))
        {
            return new List<RepoRunPlan>();
        }

        var plans = new List<RepoRunPlan>(scenarios.Count);
        foreach (var (path, spec) in scenarios)
        {
            var profileName = string.IsNullOrWhiteSpace(spec.Profile) ? options.ModSet : spec.Profile;
            var request = options with
            {
                NoBuild = options.NoBuild,
                ModSet = profileName,
                Targets = [path],
            };
            plans.Add(RepoRunPlanner.BuildRunPlan(options.RepoRoot, config, request.ToRequest(), environment));
        }

        return plans;
    }

    private static List<(string Path, ScenarioSpec Spec)> DiscoverRepoScenarios(
        string repoRoot,
        IReadOnlyList<string> rawTargets,
        IReadOnlyDictionary<string, string?> environment)
    {
        var scenarios = new List<(string Path, ScenarioSpec Spec)>();
        foreach (var target in rawTargets)
        {
            var resolved = RepoPathResolver.Resolve(repoRoot, target, environment, requireExists: true);
            if (File.Exists(resolved))
            {
                scenarios.Add((resolved, ScenarioLoader.Load(resolved)));
                continue;
            }

            foreach (var file in Directory
                .EnumerateFiles(resolved, "*.test.json", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal))
            {
                scenarios.Add((file, ScenarioLoader.Load(file)));
            }
        }

        return scenarios;
    }

    private static async Task<int> RunRepoRepeatAsync(ReadOnlyMemory<string> args, CancellationToken ct)
    {
        var repeat = ParseRepeatOptions(args);
        var config = RepoTestConfig.Load(repeat.Run.RepoRoot);
        var environment = BuildRepoEnvironment();
        var worstExit = 0;

        for (var i = 1; i <= repeat.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var runOptions = repeat.Run with
            {
                NoBuild = repeat.Run.NoBuild || i > 1,
                ReportDir = RepeatReportDir(config, repeat.Run.ReportDir, i),
            };
            var plan = RepoRunPlanner.BuildRunPlan(runOptions.RepoRoot, config, runOptions.ToRequest(), environment);

            if (runOptions.DryRun)
            {
                PrintDryRun(plan);
                continue;
            }

            var buildExit = await RunBuildIfNeededAsync(plan, ct);
            if (buildExit != 0)
            {
                return buildExit;
            }

            var exit = await RunExecutor(plan.FrobbyArgs, ct);
            worstExit = Math.Max(worstExit, exit);
        }

        return worstExit;
    }

    private static int Unknown(string subcommand)
    {
        Console.Error.WriteLine($"unknown repo subcommand: {subcommand}");
        return 64;
    }

    private static int RunRepoDeps(ReadOnlyMemory<string> args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: sdv-test repo deps <import|doctor> [args...]");
            return 64;
        }

        return args.Span[0] switch
        {
            "import" => RunRepoDepsImport(args[1..]),
            "doctor" => RunRepoDepsDoctor(args[1..]),
            _ => Unknown("deps " + args.Span[0]),
        };
    }

    private static int RunRepoDepsImport(ReadOnlyMemory<string> args)
    {
        string? source = null;
        for (var i = 0; i < args.Length; i++)
        {
            var value = args.Span[i];
            if (value == "--from")
            {
                source = ReadRequiredValue(args, ref i, value);
                continue;
            }

            throw new InvalidOperationException($"unknown repo deps import option: {value}");
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new InvalidOperationException("repo deps import requires --from <path>.");
        }

        var environment = BuildRepoEnvironment();
        var manifest = RepoDependencyCache.Import(source, environment);
        var cacheRoot = RepoDependencyCache.ResolveCacheRoot(environment);
        Console.Out.WriteLine($"[repo deps] imported {manifest.UniqueId} {manifest.Version ?? "<unknown>"}");
        Console.Out.WriteLine($"[repo deps] from {Path.GetFullPath(source)}");
        Console.Out.WriteLine($"[repo deps] to {Path.Combine(cacheRoot, manifest.UniqueId)}");
        return 0;
    }

    private static int RunRepoDepsDoctor(ReadOnlyMemory<string> args)
    {
        var repoRoot = Directory.GetCurrentDirectory();
        string? modSetName = null;
        for (var i = 0; i < args.Length; i++)
        {
            var value = args.Span[i];
            switch (value)
            {
                case "--repo-root":
                    repoRoot = ReadRequiredValue(args, ref i, value);
                    continue;
                case "--mod-set":
                    modSetName = ReadRequiredValue(args, ref i, value);
                    continue;
                default:
                    throw new InvalidOperationException($"unknown repo deps doctor option: {value}");
            }
        }

        var config = RepoTestConfig.Load(repoRoot);
        var modSet = SelectModSetForCommand(config, modSetName);
        var environment = BuildRepoEnvironment();
        var hadFailures = false;
        foreach (var dependency in modSet.Deps)
        {
            var check = RepoDependencyCache.Check(dependency, environment);
            if (check.Status == RepoDependencyStatus.Ok)
            {
                Console.Out.WriteLine(check.Message);
            }
            else
            {
                hadFailures = true;
                Console.Error.WriteLine(check.Message);
            }
        }

        foreach (var extraMod in modSet.ExtraMods.Where(value => value.Contains("SDV_GAME_MODS", StringComparison.Ordinal)))
        {
            Console.Error.WriteLine($"[repo deps] warning: extraMods entry '{extraMod}' still reads from SDV_GAME_MODS; move external dependencies to deps.");
        }

        if (modSet.Deps.Count == 0)
        {
            Console.Out.WriteLine($"[repo deps] mod set '{modSet.Name}' declares no deps.");
        }

        return hadFailures ? 1 : 0;
    }

    private static RepoModSetConfig SelectModSetForCommand(RepoTestConfig config, string? requestedName)
    {
        if (config.ModSets.Count == 0)
        {
            throw new InvalidOperationException("sdv-test config must define at least one mod set.");
        }

        if (string.IsNullOrWhiteSpace(requestedName))
        {
            return config.ModSets[0];
        }

        return config.ModSets.FirstOrDefault(modSet => modSet.Name == requestedName)
            ?? throw new InvalidOperationException($"Unknown mod set '{requestedName}'.");
    }

    private static async Task<int> RunBuildIfNeededAsync(RepoRunPlan plan, CancellationToken ct)
    {
        if (plan.BuildCommand is null)
        {
            return 0;
        }

        Console.Error.WriteLine($"[repo] build: {FormatCommand(plan.BuildCommand)}");
        var startInfo = new ProcessStartInfo
        {
            FileName = plan.BuildCommand[0],
            WorkingDirectory = plan.RepoRoot,
            UseShellExecute = false,
        };
        foreach (var arg in plan.BuildCommand.Skip(1))
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)
            ?? throw new IOException($"failed to start build command '{plan.BuildCommand[0]}'.");
        await process.WaitForExitAsync(ct);
        return process.ExitCode;
    }

    private static Task<int> RunPlannedFrobbyAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        if (args.Count == 0)
        {
            throw new InvalidOperationException("planned frobby command is empty.");
        }

        var rest = args.Skip(1).ToArray().AsMemory();
        return args[0] switch
        {
            "run" => RunCommand.RunAsync(rest, ct),
            "run-suite" => RunSuiteCommand.RunAsync(rest, ct),
            _ => throw new InvalidOperationException($"unsupported planned frobby command '{args[0]}'."),
        };
    }

    private static void PrintDryRun(RepoRunPlan plan)
    {
        Console.Out.WriteLine("cd " + plan.RepoRoot);
        PrintDryRunBuild(plan);
        PrintDryRunRun(plan);
    }

    private static void PrintDryRunBuild(RepoRunPlan plan)
    {
        if (plan.BuildCommand is not null)
        {
            Console.Out.WriteLine(FormatCommand(plan.BuildCommand));
        }
    }

    private static void PrintDryRunRun(RepoRunPlan plan)
    {
        Console.Out.WriteLine("sdv-test " + FormatCommand(plan.FrobbyArgs));
        Console.Out.WriteLine("report hub: " + Path.Combine(plan.ReportDir, "index.html"));
    }

    private static RunOptions ParseRunOptions(ReadOnlyMemory<string> args)
    {
        var repoRoot = Directory.GetCurrentDirectory();
        var visible = false;
        var noBuild = false;
        var dryRun = false;
        var baseline = false;
        string? filter = null;
        string? modSet = null;
        string? reportDir = null;
        var targets = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var value = args.Span[i];
            switch (value)
            {
                case "--repo-root":
                    repoRoot = ReadRequiredValue(args, ref i, value);
                    continue;
                case "--visible":
                    visible = true;
                    continue;
                case "--headless":
                    visible = false;
                    continue;
                case "--no-build":
                    noBuild = true;
                    continue;
                case "--dry-run":
                    dryRun = true;
                    continue;
                case "--baseline":
                    baseline = true;
                    continue;
                case "--filter":
                    filter = ReadRequiredValue(args, ref i, value);
                    continue;
                case "--mod-set":
                    modSet = ReadRequiredValue(args, ref i, value);
                    continue;
                case "--report-dir":
                    reportDir = ReadRequiredValue(args, ref i, value);
                    continue;
            }

            if (value.StartsWith("-", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"unknown repo run option: {value}");
            }

            targets.Add(value);
        }

        return new RunOptions(
            Path.GetFullPath(repoRoot),
            visible,
            noBuild,
            dryRun,
            baseline,
            filter,
            modSet,
            reportDir,
            targets);
    }

    private static RepeatOptions ParseRepeatOptions(ReadOnlyMemory<string> args)
    {
        var values = new List<string>();
        var count = 2;

        for (var i = 0; i < args.Length; i++)
        {
            var value = args.Span[i];
            if (value is "--count" or "-n")
            {
                var raw = ReadRequiredValue(args, ref i, value);
                if (!int.TryParse(raw, out count) || count < 1)
                {
                    throw new InvalidOperationException($"{value} requires a positive integer.");
                }
                continue;
            }

            values.Add(value);
        }

        return new RepeatOptions(count, ParseRunOptions(values.ToArray().AsMemory()));
    }

    private static string ReadRequiredValue(ReadOnlyMemory<string> args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new InvalidOperationException($"{option} requires a value.");
        }

        return args.Span[++index];
    }

    private static string RequireText(string? value, string field)
        => !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"sdv-test config requires '{field}'.");

    private static string RepeatReportDir(RepoTestConfig config, string? requestedReportDir, int runNumber)
    {
        var reportBase = requestedReportDir;
        if (string.IsNullOrWhiteSpace(reportBase))
        {
            var slug = config.Project.Slug;
            var version = config.Project.Version;
            if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(version))
            {
                throw new InvalidOperationException("sdv-test config requires project.slug and project.version.");
            }

            reportBase = Path.Combine(Path.GetTempPath(), $"{slug}-frobby-repeat-{version}");
        }

        return Path.Combine(reportBase, $"run-{runNumber:00}");
    }

    private static IReadOnlyDictionary<string, string?> BuildRepoEnvironment()
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key)
            {
                environment[key] = entry.Value?.ToString();
            }
        }

        if (!environment.TryGetValue("SDV_GAME_MODS", out var gameMods)
            || string.IsNullOrWhiteSpace(gameMods))
        {
            var discoveredMods = DiscoverSdvGameMods(environment);
            if (discoveredMods is not null)
            {
                environment["SDV_GAME_MODS"] = discoveredMods;
            }
        }

        return environment;
    }

    private static string? DiscoverSdvGameMods(IReadOnlyDictionary<string, string?> environment)
    {
        var installPath = environment.TryGetValue("SDV_INSTALL_PATH", out var configuredInstallPath)
            ? configuredInstallPath
            : null;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            installPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Stardew Valley");
        }

        var modsPath = Path.Combine(installPath, "Mods");
        return Directory.Exists(modsPath)
            ? Path.GetFullPath(modsPath)
            : null;
    }

    private static string FormatCommand(IReadOnlyList<string> command)
        => string.Join(" ", command.Select(QuoteIfNeeded));

    private static string QuoteIfNeeded(string value)
    {
        if (value.Length == 0)
        {
            return "\"\"";
        }

        return value.Any(char.IsWhiteSpace)
            ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : value;
    }

    private sealed record RunOptions(
        string RepoRoot,
        bool Visible,
        bool NoBuild,
        bool DryRun,
        bool Baseline,
        string? Filter,
        string? ModSet,
        string? ReportDir,
        IReadOnlyList<string> Targets)
    {
        public RepoRunRequest ToRequest()
            => new(Visible, NoBuild, DryRun, Baseline, ModSet, ReportDir, Targets, Filter);
    }

    private sealed record RepeatOptions(int Count, RunOptions Run);
}
