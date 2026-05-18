using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Models;
using SdvTestFramework.Protocol.Reports;
using SdvTestFramework.Protocol.Scenarios;
using SdvTestFramework.Runner.Bitmap;
using SdvTestFramework.Runner.Fixtures;
using SdvTestFramework.Runner.Reports;
using SdvTestFramework.Runner.Scenarios;

namespace SdvTestFramework.Runner.Commands;

/// <summary>
/// `run` — headline end-to-end command. Loads scenarios, launches SDV, streams RPC,
/// collects reports, prints a Playwright-style summary.
/// </summary>
public static class RunCommand
{
    public static async Task<int> RunAsync(ReadOnlyMemory<string> args, CancellationToken ct)
    {
        // ---- parse args ----
        var paths = new List<string>();
        var extraMods = new List<string>();
        var configOverlays = new List<ExtraModConfigOverlay>();
        string? filter = null;
        string? modsPath = null;
        string? profileId = null;
        string? profileCacheNamespace = null;
        string reporterName = "console";
        string? outputPath = null;
        bool watch = false;
        bool updateBaselines = false;
        string? reportDirPath = null;
        bool noReport = false;
        DiffFormat diffFormat = DiffFormat.Files;
        string runWideTier = "generic";
        bool noCacheCleanup = false;
        bool headless = false;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args.Span[i];
            if (a == "--filter" && i + 1 < args.Length) { filter = args.Span[++i]; continue; }
            if (a == "--mods-path" && i + 1 < args.Length) { modsPath = args.Span[++i]; continue; }
            if (a == "--extra-mod" && i + 1 < args.Length) { extraMods.Add(args.Span[++i]); continue; }
            if (a == "--profile-id" && i + 1 < args.Length) { profileId = args.Span[++i]; continue; }
            if (a == "--profile-cache-namespace" && i + 1 < args.Length) { profileCacheNamespace = args.Span[++i]; continue; }
            if (a == "--config-overlay")
            {
                if (!TryReadRunOptionValue(args, ref i, a, out var source, out var sourceError))
                {
                    Console.Error.WriteLine(sourceError);
                    return 2;
                }
                if (!TryReadRunOptionValue(args, ref i, a, out var targetMod, out var targetModError))
                {
                    Console.Error.WriteLine(targetModError);
                    return 2;
                }
                if (!TryReadRunOptionValue(args, ref i, a, out var targetPath, out var targetPathError))
                {
                    Console.Error.WriteLine(targetPathError);
                    return 2;
                }
                if (LooksLikeScenarioPath(targetPath!))
                {
                    Console.Error.WriteLine(
                        $"[run] --config-overlay target path '{targetPath}' looks like a scenario path; expected <source> <target-mod> <target-path>");
                    return 2;
                }

                configOverlays.Add(new ExtraModConfigOverlay(
                    SourcePath: source!,
                    TargetModUniqueId: targetMod!,
                    TargetRelativePath: targetPath!));
                continue;
            }
            if (a == "--reporter" && i + 1 < args.Length) { reporterName = args.Span[++i]; continue; }
            if (a == "--output" && i + 1 < args.Length) { outputPath = args.Span[++i]; continue; }
            if (a == "--watch") { watch = true; continue; }
            if (a == "--update-baselines") { updateBaselines = true; continue; }
            if (a == "--report-dir" && i + 1 < args.Length) { reportDirPath = args.Span[++i]; continue; }
            if (a == "--no-report") { noReport = true; continue; }
            if (a == "--no-cache-cleanup") { noCacheCleanup = true; continue; }
            if (a == "--headless") { headless = true; continue; }
            if (a == "--diff-format" && i + 1 < args.Length)
            {
                var raw = args.Span[++i];
                if (!Enum.TryParse<DiffFormat>(raw, ignoreCase: true, out diffFormat))
                {
                    Console.Error.WriteLine($"[run] invalid --diff-format '{raw}'; expected files|triptych|all");
                    return 2;
                }
                continue;
            }
            if (a == "--tier" && i + 1 < args.Length)
            {
                var raw = args.Span[++i];
                if (raw is not ("generic" or "ci-ubuntu" or "self-hosted-nvidia"))
                {
                    Console.Error.WriteLine(
                        $"[run] invalid --tier '{raw}'; expected generic | ci-ubuntu | self-hosted-nvidia");
                    return 2;
                }
                runWideTier = raw;
                continue;
            }
            paths.Add(a);
        }
        if (paths.Count == 0) paths.Add(Directory.GetCurrentDirectory());

        // ---- create run directory eagerly (before SDV launches) ----
        // This lets report path appear in stderr before the 60s SDV boot wait, and ensures we
        // don't discover an unwritable output dir after a slow spin-up.
        RunDirectory? runDir = null;
        if (!noReport)
        {
            var baseDir = reportDirPath ?? Path.Combine(Directory.GetCurrentDirectory(), "test-results");
            try
            {
                var explicitRunId = reportDirPath is null
                    ? null
                    : ReportRunId.ForExplicitReportBase(paths, filter);
                runDir = RunDirectory.Create(
                    baseDir,
                    explicitRunId,
                    replaceExisting: reportDirPath is not null);
                Console.Error.WriteLine($"[run] report dir: {runDir.Root}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[run] failed to create report dir: {ex.Message}");
                return 3;
            }
        }

        var opts = new RunCommandOptions(
            Paths: paths,
            Filter: filter,
            ModsPath: modsPath,
            ExtraMods: extraMods,
            ReporterName: reporterName,
            OutputPath: outputPath,
            Watch: watch,
            UpdateBaselines: updateBaselines,
            ReportDirPath: reportDirPath,
            NoReport: noReport,
            DiffFormat: diffFormat,
            Tier: runWideTier,
            NoCacheCleanup: noCacheCleanup,
            Headless: headless,
            ProfileId: profileId,
            ProfileCacheNamespace: profileCacheNamespace,
            ConfigOverlays: configOverlays,
            PreCreatedRunDir: runDir);

        return await RunFromOptions(opts, ct);
    }

    private static bool TryReadRunOptionValue(
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
            error = $"[run] {option} requires a value";
            return false;
        }

        var next = args.Span[++index];
        if (next.StartsWith("-", StringComparison.Ordinal))
        {
            error = $"[run] {option} requires a value";
            return false;
        }

        value = next;
        return true;
    }

    private static bool LooksLikeScenarioPath(string value)
        => Directory.Exists(value)
            || (File.Exists(value)
                && value.EndsWith(".test.json", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Post-argv-parse half of the run flow: resolve reporter + output sink + mods path,
    /// discover scenarios, stage fixtures, launch SDV, connect, run-once, and (optionally)
    /// enter watch mode. Public so other commands (e.g. <c>BaselinesCommand.update</c>) can
    /// build a <see cref="RunCommandOptions"/> directly and reuse this entry point without
    /// going through CLI argv parsing.
    /// </summary>
    public static async Task<int> RunFromOptions(RunCommandOptions opts, CancellationToken ct)
    {
        // ---- resolve reporter eagerly so bad flag values exit 2 before SDV launches ----
        SdvTestFramework.Runner.Reporters.IReporter reporter;
        try { reporter = SdvTestFramework.Runner.Reporters.ReporterFactory.Create(opts.ReporterName); }
        catch (ArgumentException ex) { Console.Error.WriteLine($"[reporter] {ex.Message}"); return 2; }

        // Resolve output sink. Stream writer is opened here (before SDV launches) so an
        // unwritable path fails fast with exit 3 — no waiting through a 60s SDV boot
        // only to discover the destination is bad.
        TextWriter? fileWriter = null;
        if (!string.IsNullOrEmpty(opts.OutputPath))
        {
            try { fileWriter = new StreamWriter(opts.OutputPath); }
            catch (Exception ex) { Console.Error.WriteLine($"[reporter] can't open --output '{opts.OutputPath}': {ex.Message}"); return 3; }
        }

        // ---- resolve mods path ----
        // SMAPI loads from its default Mods dir unless --mods-path is set; on a dev workstation
        // that's likely the user's full mod collection, which breaks the harness socket path.
        // Precedence: CLI flag → $SDV_MODS_PATH env var → default per-user cache dir.
        var defaultModsPath = DefaultModsPath();
        var modsPath = opts.ModsPath ?? Environment.GetEnvironmentVariable("SDV_MODS_PATH");
        if (string.IsNullOrEmpty(modsPath))
        {
            modsPath = defaultModsPath;
        }
        Directory.CreateDirectory(modsPath);
        HarnessDeployer.Deploy(modsPath);
        try
        {
            ExtraModDeployer.DeployMany(
                modsPath,
                opts.ExtraMods
                    .Concat(ExtraModDeployer.ParseEnvList(Environment.GetEnvironmentVariable("SDV_EXTRA_MODS")))
                    .Distinct(StringComparer.Ordinal),
                cleanUnlisted: true,
                cleanUnmarked: PathsEqual(modsPath, defaultModsPath));
            ExtraModDeployer.ApplyConfigOverlays(modsPath, opts.ConfigOverlays);
        }
        catch (Exception ex) when (ex is ArgumentException
            or DirectoryNotFoundException
            or FileNotFoundException
            or InvalidOperationException
            or JsonException)
        {
            Console.Error.WriteLine($"[extra-mod] {ex.Message}");
            return 2;
        }

        // ---- discover + load scenarios ----
        var scenarios = new List<(string Path, ScenarioSpec Spec)>();
        foreach (var root in opts.Paths)
        {
            if (File.Exists(root))
            {
                try { scenarios.Add((root, ScenarioLoader.Load(root))); }
                catch (Exception ex) { Console.Error.WriteLine($"[load-error] {root}: {ex.Message}"); return 2; }
            }
            else if (Directory.Exists(root))
            {
                foreach (var f in Directory.EnumerateFiles(root, "*.test.json", SearchOption.AllDirectories))
                {
                    try { scenarios.Add((f, ScenarioLoader.Load(f))); }
                    catch (Exception ex) { Console.Error.WriteLine($"[load-error] {f}: {ex.Message}"); return 2; }
                }
            }
            else
            {
                Console.Error.WriteLine($"[error] path not found: {root}");
                return 2;
            }
        }
        if (!string.IsNullOrEmpty(opts.Filter))
            scenarios = scenarios
                .Where(s => s.Spec.Name.Contains(opts.Filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        if (scenarios.Count == 0)
        {
            Console.WriteLine("no scenarios matched");
            return 0;
        }

        // Stage every unique fixture variant referenced by the scenario set into SDV's
        // saves dir before launch. Fixtures live in tests/fixtures/<name>/save/ in the
        // repo; SDV expects them in Constants.SavesPath (resolved client-side here to
        // HOME/.config/StardewValley/Saves).
        var sdvSavesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "StardewValley", "Saves");
        var stageExit = ScenarioFixtureVariantStager.StageAll(
            Directory.GetCurrentDirectory(),
            sdvSavesDir,
            scenarios,
            Console.Error);
        if (stageExit != 0)
            return stageExit;

        // ---- launch SDV + connect ----
        var socket = Path.Combine(Path.GetTempPath(), $"sdv-test-{Guid.NewGuid():N}.sock");
        var effectiveHeadless = SdvLauncher.IsHeadlessRequested(opts.Headless);
        var launcher = effectiveHeadless ? "xvfb-run" : "StardewModdingAPI";
        using var sdv = SdvLauncher.Launch(socket, installPath: null, modsPath: modsPath, headless: effectiveHeadless);
        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(TimeSpan.FromSeconds(60));

            // Wait for listener to appear.
            for (int i = 0; i < 120 && !File.Exists(socket); i++)
                await Task.Delay(500, connectCts.Token);
            if (!File.Exists(socket))
                throw new TimeoutException("SDV never opened the test socket");

            using var session = await UnixSocketRpc.ConnectAsync(socket, connectCts.Token);
            var readyTcs = new TaskCompletionSource<JsonRpcNotification>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            session.NotificationReceived += n =>
            {
                if (n.Method == "ready") readyTcs.TrySetResult(n);
            };
            _ = session.RunAsync(ct);

            await readyTcs.Task.WaitAsync(TimeSpan.FromSeconds(60), ct);

            var writer = fileWriter ?? Console.Out;
            int failed = await RunOnceAsync(session, opts, reporter, writer, effectiveHeadless, launcher, ct);

            if (opts.Watch)
            {
                // Stay resident; rerun on *.test.json file changes. Session + reporter + writer
                // are closed over in the callback so each rerun uses the same SDV subprocess.
                // Fixture variants are staged before launch, and effective fixture names are
                // recomputed per run from freshly loaded scenarios. Note: watch mode still does
                // not restage changed fixture files. The run-dir is reused across reruns —
                // screenshots from later reruns accumulate in the same directory.
                await SdvTestFramework.Runner.Watch.WatchLoop.RunAsync(
                    opts.Paths,
                    rerun: async innerCt =>
                    {
                        await RunOnceAsync(session, opts, reporter, writer, effectiveHeadless, launcher, innerCt);
                    },
                    writer,
                    ct);
            }

            // Best-effort capture-cache sweep at end of successful run. Runs once on
            // completion (after watch-mode loop exits), not per-rerun. Defaults match
            // the manual `cache clean` command. Wrapped in try/catch — never throws
            // from the cleanup path.
            if (!opts.NoCacheCleanup)
            {
                try
                {
                    var cacheDir = Environment.GetEnvironmentVariable("SDV_CACHE_DIR")
                        ?? Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                            ".cache", "sdv-test-framework", "captures");
                    var deleted = CaptureCacheCleaner.CleanCache(cacheDir, maxAgeDays: 7, keepRuns: 5, dryRun: false);
                    if (deleted > 0)
                        Console.Error.WriteLine($"[cache] swept {deleted} stale capture file(s)");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[cache] cleanup failed: {ex.Message}");
                }
            }

            return failed == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[run] fatal: {ex.Message}");
            return 3;
        }
        finally
        {
            SdvLauncher.Terminate(sdv);
            fileWriter?.Dispose();
        }
    }

    /// <summary>
    /// Single "discover + run + report" cycle. Called once in non-watch mode, and once per
    /// watcher trigger in <c>--watch</c> mode. Returns the number of failed scenarios; the
    /// outer caller uses 0 → exit 0, &gt;0 → exit 1.
    /// </summary>
    /// <remarks>
    /// Re-discovers <c>*.test.json</c> on every call so watch mode picks up new/deleted files.
    /// Scenario-load errors are tolerant: bad files are skipped with a stderr log + excluded
    /// from the run. Fixture staging is NOT re-run — variants are staged once before SDV
    /// launches, while effective fixture names are recomputed from the loaded scenarios each
    /// run. Changed fixture files still require a new watch session.
    /// </remarks>
    private static async Task<int> RunOnceAsync(
        JsonRpcSession session,
        RunCommandOptions opts,
        SdvTestFramework.Runner.Reporters.IReporter reporter,
        TextWriter reporterOutput,
        bool effectiveHeadless,
        string launcher,
        CancellationToken ct)
    {
        var runStarted = DateTime.UtcNow;
        var runDir = opts.PreCreatedRunDir;

        // 1. Discover scenarios (fresh each call).
        var scenarios = new List<(string Path, ScenarioSpec Spec)>();
        foreach (var root in opts.Paths)
        {
            if (File.Exists(root))
            {
                try { scenarios.Add((root, ScenarioLoader.Load(root))); }
                catch (Exception ex) { Console.Error.WriteLine($"[load-error] {root}: {ex.Message}"); continue; }
            }
            else if (Directory.Exists(root))
            {
                foreach (var f in Directory.EnumerateFiles(root, "*.test.json", SearchOption.AllDirectories))
                {
                    try { scenarios.Add((f, ScenarioLoader.Load(f))); }
                    catch (Exception ex) { Console.Error.WriteLine($"[load-error] {f}: {ex.Message}"); continue; }
                }
            }
        }
        if (!string.IsNullOrEmpty(opts.Filter))
            scenarios = scenarios
                .Where(s => s.Spec.Name.Contains(opts.Filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        ScenarioFixtureVariantStager.ApplyEffectiveFixtureNames(scenarios);
        if (scenarios.Count == 0)
        {
            Console.WriteLine("no scenarios matched");
            return 0;
        }

        // 2. Run scenarios + collect reports.
        var runner = new ScenarioRunner(session, opts.UpdateBaselines, runDir, opts.DiffFormat, opts.Tier);
        var collected = new List<ScenarioReport>(scenarios.Count);
        foreach (var (path, spec) in scenarios)
        {
            var report = await runner.RunAsync(spec, scenarioPath: path, ct);
            report.Path = path;
            collected.Add(report);
        }

        // 3. Report (existing console/TAP/JUnit reporters).
        reporter.Report(collected, reporterOutput);
        reporterOutput.Flush();

        // 4. Generate HTML run report if a run directory was created.
        if (runDir is not null)
        {
            var summary = BuildRunSummary(runDir, runStarted, collected, opts, effectiveHeadless, launcher);
            HtmlReportGenerator.Generate(runDir, summary);
            HtmlReportGenerator.GenerateHub(Directory.GetParent(runDir.Root)?.FullName ?? runDir.Root);
            Console.Out.WriteLine($"[run] report: {Path.Combine(runDir.Root, "index.html")}");
        }

        // 5. Return failed count.
        int failed = 0;
        foreach (var r in collected) if (!r.Passed) failed++;
        return failed;
    }

    /// <summary>
    /// Assemble a <see cref="RunSummary"/> from the collected scenario reports.
    /// </summary>
    private static RunSummary BuildRunSummary(
        RunDirectory rd,
        DateTime started,
        IReadOnlyList<ScenarioReport> reports,
        RunCommandOptions opts,
        bool effectiveHeadless,
        string launcher)
    {
        var scenarioOutcomes = new List<ScenarioOutcome>(reports.Count);
        int totalDuration = 0;
        foreach (var report in reports)
        {
            var assertions = report.Assertions.Count > 0
                ? report.Assertions
                : BuildFallbackAssertions(report);

            scenarioOutcomes.Add(new ScenarioOutcome(
                Name: report.Name,
                Path: string.IsNullOrEmpty(report.Path) ? null : report.Path,
                Passed: report.Passed,
                DurationMs: report.DurationMs,
                Steps: report.Steps,
                Assertions: assertions,
                Screenshots: report.Screenshots,
                Diffs: ConvertDiffs(rd, report.Diffs)));
            totalDuration += report.DurationMs;
        }
        return new RunSummary(rd.RunId, started.ToString("o"), totalDuration, scenarioOutcomes)
        {
            Metadata = RunMetadataBuilder.Build(opts, effectiveHeadless, launcher),
        };
    }

    private static List<AssertionOutcome> BuildFallbackAssertions(ScenarioReport report)
    {
        var assertions = new List<AssertionOutcome>(report.AssertionsRun);
        for (int i = 0; i < report.AssertionsPassed; i++)
            assertions.Add(new AssertionOutcome("assertion", true, null));
        foreach (var failure in report.Failures)
            assertions.Add(new AssertionOutcome("assertion", false, failure));
        return assertions;
    }

    /// <summary>
    /// Convert <see cref="DiffSet"/>s with absolute filesystem paths (as produced by
    /// <c>ScenarioRunner</c> / <c>BitmapAssertion</c>) into run-dir-relative paths with
    /// forward-slash separators. This makes <c>summary.json</c> legible across machines
    /// and lets the HTML renderer construct stable URLs.
    /// </summary>
    private static List<DiffSet> ConvertDiffs(RunDirectory rd, IReadOnlyList<DiffSet> abs)
    {
        var result = new List<DiffSet>(abs.Count);
        foreach (var d in abs)
        {
            result.Add(new DiffSet(
                Baseline: MakeRel(rd, d.Baseline),
                Capture:  MakeRel(rd, d.Capture),
                Diff:     MakeRel(rd, d.Diff),
                Triptych: d.Triptych is { } t ? MakeRel(rd, t) : null));
        }
        return result;
    }

    private static string MakeRel(RunDirectory rd, string abs)
        => Path.GetRelativePath(rd.Root, abs).Replace('\\', '/');

    private static string DefaultModsPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache", "sdv-test-framework", "mods");

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
