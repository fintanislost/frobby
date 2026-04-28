using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Models;
using SdvTestFramework.Protocol.Scenarios;

namespace SdvTestFramework.Runner.Commands;

/// <summary>
/// Runs a directory of scenarios by invoking <see cref="RunCommand"/> once per scenario, so
/// each scenario gets a fresh Stardew/SMAPI process while sharing one report hub.
/// </summary>
public static class RunSuiteCommand
{
    /// <summary>Test seam; production delegates to <see cref="RunCommand.RunAsync"/>.</summary>
    public static Func<ReadOnlyMemory<string>, CancellationToken, Task<int>> RunExecutor { get; set; }
        = RunCommand.RunAsync;

    public static async Task<int> RunAsync(ReadOnlyMemory<string> args, CancellationToken ct)
    {
        var parse = ParseArgs(args);
        if (!parse.Ok)
        {
            Console.Error.WriteLine(parse.Error);
            return 2;
        }

        var opts = parse.Options!;
        var scenarios = LoadScenarios(opts.Paths);
        if (scenarios.Error is not null)
        {
            Console.Error.WriteLine(scenarios.Error);
            return 2;
        }

        var selected = scenarios.Items!;
        if (!string.IsNullOrWhiteSpace(opts.Filter))
            selected = selected
                .Where(s => s.Spec.Name.Contains(opts.Filter, StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (selected.Count == 0)
        {
            Console.WriteLine("no scenarios matched");
            return 0;
        }

        var childArgsPrefix = opts.PassThroughArgs.ToList();
        if (!opts.NoReport)
        {
            var reportBase = opts.ReportDirPath ?? DefaultSuiteReportBase();
            childArgsPrefix.Add("--report-dir");
            childArgsPrefix.Add(reportBase);
            Console.Error.WriteLine($"[suite] report dir: {reportBase}");
        }

        var passed = 0;
        var worstExit = 0;
        for (var i = 0; i < selected.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (path, spec) = selected[i];
            Console.Error.WriteLine($"[suite] {i + 1}/{selected.Count} {spec.Name}");

            var childArgs = childArgsPrefix.Concat(new[] { path }).ToArray();
            var exit = await RunExecutor(childArgs.AsMemory(), ct);
            if (exit == 0)
            {
                passed++;
                continue;
            }

            worstExit = Math.Max(worstExit, exit);
        }

        Console.WriteLine($"[suite] {passed}/{selected.Count} passed");
        if (!opts.NoReport)
        {
            var reportBase = childArgsPrefix[childArgsPrefix.LastIndexOf("--report-dir") + 1];
            Console.WriteLine($"[suite] report hub: {Path.Combine(reportBase, "index.html")}");
        }

        return worstExit == 0 ? 0 : NormalizeExit(worstExit);
    }

    private static int NormalizeExit(int exit)
    {
        if (exit >= 3) return 3;
        if (exit == 2) return 2;
        return 1;
    }

    private static string DefaultSuiteReportBase()
        => Path.Combine(
            Directory.GetCurrentDirectory(),
            "test-results",
            "suite-" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH-mm-ss"));

    private static LoadResult LoadScenarios(IReadOnlyList<string> roots)
    {
        var items = new List<(string Path, ScenarioSpec Spec)>();
        foreach (var root in roots)
        {
            if (File.Exists(root))
            {
                var loaded = LoadOne(root);
                if (loaded.Error is not null) return LoadResult.Fail(loaded.Error);
                items.Add(loaded.Item!.Value);
                continue;
            }

            if (Directory.Exists(root))
            {
                foreach (var file in Directory
                    .EnumerateFiles(root, "*.test.json", SearchOption.AllDirectories)
                    .OrderBy(p => p, StringComparer.Ordinal))
                {
                    var loaded = LoadOne(file);
                    if (loaded.Error is not null) return LoadResult.Fail(loaded.Error);
                    items.Add(loaded.Item!.Value);
                }
                continue;
            }

            return LoadResult.Fail($"[suite] path not found: {root}");
        }

        return LoadResult.Success(items);
    }

    private static LoadOneResult LoadOne(string path)
    {
        try { return LoadOneResult.Success((path, ScenarioLoader.Load(path))); }
        catch (Exception ex) { return LoadOneResult.Fail($"[suite load-error] {path}: {ex.Message}"); }
    }

    private static ParseResult ParseArgs(ReadOnlyMemory<string> args)
    {
        var paths = new List<string>();
        var passThrough = new List<string>();
        string? filter = null;
        string? reportDirPath = null;
        bool noReport = false;

        for (var i = 0; i < args.Length; i++)
        {
            var value = args.Span[i];
            switch (value)
            {
                case "--fresh-process-per-scenario":
                    continue;

                case "--filter":
                    if (!TryReadValue(args, ref i, value, out filter, out var filterError))
                        return ParseResult.Fail(filterError);
                    continue;

                case "--report-dir":
                    if (!TryReadValue(args, ref i, value, out reportDirPath, out var reportError))
                        return ParseResult.Fail(reportError);
                    continue;

                case "--no-report":
                    noReport = true;
                    passThrough.Add(value);
                    continue;

                case "--mods-path":
                case "--extra-mod":
                case "--tier":
                case "--diff-format":
                    if (!TryReadValue(args, ref i, value, out var argValue, out var error))
                        return ParseResult.Fail(error);
                    passThrough.Add(value);
                    passThrough.Add(argValue!);
                    continue;

                case "--update-baselines":
                case "--no-cache-cleanup":
                    passThrough.Add(value);
                    continue;

                case "--watch":
                    return ParseResult.Fail("[suite] --watch is not supported; run-suite already starts a fresh process per scenario.");

                case "--output":
                    return ParseResult.Fail("[suite] --output is not supported because run-suite executes one child run per scenario.");
            }

            if (value.StartsWith("-", StringComparison.Ordinal))
                return ParseResult.Fail($"[suite] unknown option: {value}");
            paths.Add(value);
        }

        if (paths.Count == 0)
            paths.Add(Directory.GetCurrentDirectory());

        return ParseResult.Success(new SuiteOptions(paths, passThrough, filter, reportDirPath, noReport));
    }

    private static bool TryReadValue(
        ReadOnlyMemory<string> args,
        ref int index,
        string option,
        out string? value,
        out string error)
    {
        value = null;
        error = string.Empty;
        if (index + 1 >= args.Length)
        {
            error = $"[suite] {option} requires a value";
            return false;
        }

        value = args.Span[++index];
        return true;
    }

    private sealed record SuiteOptions(
        IReadOnlyList<string> Paths,
        IReadOnlyList<string> PassThroughArgs,
        string? Filter,
        string? ReportDirPath,
        bool NoReport);

    private sealed record ParseResult(bool Ok, SuiteOptions? Options, string Error)
    {
        public static ParseResult Success(SuiteOptions options) => new(true, options, string.Empty);
        public static ParseResult Fail(string error) => new(false, null, error);
    }

    private sealed record LoadResult(List<(string Path, ScenarioSpec Spec)>? Items, string? Error)
    {
        public static LoadResult Success(List<(string Path, ScenarioSpec Spec)> items) => new(items, null);
        public static LoadResult Fail(string error) => new(null, error);
    }

    private sealed record LoadOneResult((string Path, ScenarioSpec Spec)? Item, string? Error)
    {
        public static LoadOneResult Success((string Path, ScenarioSpec Spec) item) => new(item, null);
        public static LoadOneResult Fail(string error) => new(null, error);
    }
}
