using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
            Console.Error.WriteLine("usage: sdv-test repo <run|repeat|init> [args...]");
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
                "init" => RunRepoInit(),
                _ => Unknown(subcommand),
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            Console.Error.WriteLine($"[repo] {ex.Message}");
            return 2;
        }
    }

    private static async Task<int> RunRepoRunAsync(ReadOnlyMemory<string> args, CancellationToken ct)
    {
        var options = ParseRunOptions(args);
        var config = RepoTestConfig.Load(options.RepoRoot);
        var plan = RepoRunPlanner.BuildRunPlan(options.RepoRoot, config, options.ToRequest());

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

    private static async Task<int> RunRepoRepeatAsync(ReadOnlyMemory<string> args, CancellationToken ct)
    {
        var repeat = ParseRepeatOptions(args);
        var config = RepoTestConfig.Load(repeat.Run.RepoRoot);
        var worstExit = 0;

        for (var i = 1; i <= repeat.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var runOptions = repeat.Run with
            {
                NoBuild = repeat.Run.NoBuild || i > 1,
                ReportDir = repeat.Run.ReportDir ?? DefaultRepeatReportDir(config, i),
            };
            var plan = RepoRunPlanner.BuildRunPlan(runOptions.RepoRoot, config, runOptions.ToRequest());

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

    private static int RunRepoInit()
    {
        Console.Error.WriteLine("[repo] init is registered by the scaffold generator task; Task 3 will replace this placeholder.");
        return 2;
    }

    private static int Unknown(string subcommand)
    {
        Console.Error.WriteLine($"unknown repo subcommand: {subcommand}");
        return 64;
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
        if (plan.BuildCommand is not null)
        {
            Console.Out.WriteLine(FormatCommand(plan.BuildCommand));
        }

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

    private static string DefaultRepeatReportDir(RepoTestConfig config, int runNumber)
    {
        var slug = config.Project.Slug;
        var version = config.Project.Version;
        if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(version))
        {
            throw new InvalidOperationException("sdv-test config requires project.slug and project.version.");
        }

        return Path.Combine(Path.GetTempPath(), $"{slug}-frobby-results-{version}", $"repeat-{runNumber:000}");
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
        string? ModSet,
        string? ReportDir,
        IReadOnlyList<string> Targets)
    {
        public RepoRunRequest ToRequest()
            => new(Visible, NoBuild, DryRun, Baseline, ModSet, ReportDir, Targets);
    }

    private sealed record RepeatOptions(int Count, RunOptions Run);
}
