using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Models;
using SdvTestFramework.Protocol.Reports;
using SdvTestFramework.Protocol.Scenarios;
using SdvTestFramework.Runner.Bitmap;
using SixLabors.ImageSharp;

namespace SdvTestFramework.Runner.Commands;

/// <summary>
/// <c>sdv-test baselines</c> dispatcher. Subcommands: <c>list | update | show | delete</c>.
/// </summary>
/// <remarks>
/// <see cref="RunExecutor"/> is a swappable static seam used by the <c>update</c> subcommand
/// so tests can capture the constructed <see cref="RunCommandOptions"/> without launching SDV.
/// Production callers leave it pointed at <see cref="RunCommand.RunFromOptions"/>.
/// </remarks>
public static class BaselinesCommand
{
    /// <summary>
    /// Test seam — <c>update</c> delegates here. Defaults to <see cref="RunCommand.RunFromOptions"/>;
    /// tests substitute a probe to avoid launching SDV.
    /// </summary>
    public static Func<RunCommandOptions, CancellationToken, Task<int>> RunExecutor { get; set; }
        = RunCommand.RunFromOptions;

    /// <summary>
    /// Dispatches <c>baselines &lt;subcommand&gt;</c>; returns the subcommand's exit code or
    /// 64 (EX_USAGE) for missing/unknown subcommands.
    /// </summary>
    public static async Task<int> RunAsync(ReadOnlyMemory<string> args, CancellationToken ct)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: sdv-test baselines <list|update|show|delete> [args...]");
            return 64;
        }

        var sub = args.Span[0];
        var rest = args[1..];
        return sub switch
        {
            "list" => RunList(rest),
            "update" => await RunUpdate(rest, ct),
            "show" => RunShow(rest),
            "delete" => RunDelete(rest),
            _ => Unknown(sub),
        };
    }

    private static int Unknown(string sub)
    {
        Console.Error.WriteLine($"unknown baselines subcommand: {sub}");
        return 64;
    }

    // --- list ---
    private static int RunList(ReadOnlyMemory<string> args)
    {
        string scenariosDir = Directory.GetCurrentDirectory();
        for (int i = 0; i < args.Length; i++)
        {
            if (args.Span[i] == "--scenarios" && i + 1 < args.Length)
                scenariosDir = args.Span[++i];
        }

        if (!Directory.Exists(scenariosDir))
        {
            Console.Error.WriteLine($"[baselines] scenarios dir not found: {scenariosDir}");
            return 1;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int found = 0;
        foreach (var f in Directory.EnumerateFiles(scenariosDir, "*.test.json", SearchOption.AllDirectories))
        {
            ScenarioSpec spec;
            try { spec = ScenarioLoader.Load(f); }
            catch { continue; }
            foreach (var a in spec.Assertions)
            {
                if (a.Type != "bitmap" || string.IsNullOrEmpty(a.Baseline)) continue;
                var resolved = BaselineManager.ResolveBaseline(f, a.Baseline);
                if (!seen.Add(resolved)) continue;
                found++;
                var status = File.Exists(resolved) ? "PRESENT" : "MISSING";
                long size = status == "PRESENT" ? new FileInfo(resolved).Length : 0;
                Console.Out.WriteLine($"[{status}] {resolved} ({size} bytes) — {Path.GetFileName(f)}::{spec.Name}");
            }
        }

        if (found == 0)
        {
            Console.Out.WriteLine("(no bitmap baselines referenced)");
            return 1;
        }
        return 0;
    }

    // --- update ---
    private static async Task<int> RunUpdate(ReadOnlyMemory<string> args, CancellationToken ct)
    {
        var paths = new List<string>();
        string tier = "generic";
        string? modsPath = null;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args.Span[i];
            if (a == "--tier" && i + 1 < args.Length) { tier = args.Span[++i]; continue; }
            if (a == "--mods-path" && i + 1 < args.Length) { modsPath = args.Span[++i]; continue; }
            paths.Add(a);
        }
        if (paths.Count == 0)
        {
            Console.Error.WriteLine("usage: sdv-test baselines update <path-or-glob> [--tier <name>] [--mods-path <p>]");
            return 64;
        }

        var opts = new RunCommandOptions(
            Paths: paths,
            Filter: null,
            ModsPath: modsPath,
            ExtraMods: Array.Empty<string>(),
            ReporterName: "console",
            OutputPath: null,
            Watch: false,
            UpdateBaselines: true,
            ReportDirPath: null,
            NoReport: true,        // baselines update is a regen op; HTML report not useful
            DiffFormat: DiffFormat.Files,
            Tier: tier,
            NoCacheCleanup: false,
            Headless: false,
            ProfileId: null,
            ProfileCacheNamespace: null,
            ConfigOverlays: Array.Empty<ExtraModConfigOverlay>(),
            PreCreatedRunDir: null);

        return await RunExecutor(opts, ct);
    }

    // --- show ---
    private static int RunShow(ReadOnlyMemory<string> args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: sdv-test baselines show <path>");
            return 64;
        }
        var path = args.Span[0];
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"[baselines] file not found: {path}");
            return 1;
        }

        var info = new FileInfo(path);
        try
        {
            var img = Image.Identify(path);
            Console.Out.WriteLine($"path:       {path}");
            Console.Out.WriteLine($"dimensions: {img.Width}x{img.Height}");
            Console.Out.WriteLine($"file size:  {info.Length} bytes");
            Console.Out.WriteLine($"modified:   {info.LastWriteTimeUtc:O}");
            Console.Out.WriteLine($"format:     {img.Metadata.DecodedImageFormat?.Name ?? "unknown"}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[baselines] failed to identify '{path}': {ex.Message}");
            return 1;
        }
    }

    // --- delete ---
    private static int RunDelete(ReadOnlyMemory<string> args)
    {
        bool force = false;
        string? path = null;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args.Span[i];
            if (a == "--force") { force = true; continue; }
            path ??= a;
        }
        if (path is null)
        {
            Console.Error.WriteLine("usage: sdv-test baselines delete <path> [--force]");
            return 64;
        }
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"[baselines] file not found: {path}");
            return 1;
        }

        if (!force)
        {
            Console.Out.Write($"delete {path}? [y/N] ");
            var answer = Console.In.ReadLine()?.Trim();
            if (answer is null or ""
                || (!answer.Equals("y", StringComparison.OrdinalIgnoreCase)
                    && !answer.Equals("yes", StringComparison.OrdinalIgnoreCase)))
            {
                Console.Out.WriteLine("aborted");
                return 0;
            }
        }

        try
        {
            File.Delete(path);
            Console.Out.WriteLine($"deleted: {path}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[baselines] delete failed: {ex.Message}");
            return 1;
        }
    }
}
