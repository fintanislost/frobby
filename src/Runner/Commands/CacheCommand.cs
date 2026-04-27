using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Bitmap;

namespace SdvTestFramework.Runner.Commands;

/// <summary>
/// <c>sdv-test cache</c> dispatcher. Subcommand: <c>clean</c>.
/// </summary>
public static class CacheCommand
{
    public static Task<int> RunAsync(ReadOnlyMemory<string> args, CancellationToken ct)
    {
        if (args.Length == 0 || args.Span[0] != "clean")
        {
            Console.Error.WriteLine("usage: sdv-test cache clean [--max-age <days>] [--keep-runs <n>] [--dry-run]");
            return Task.FromResult(64);
        }

        int maxAgeDays = 7;
        int keepRuns = 5;
        bool dryRun = false;
        for (int i = 1; i < args.Length; i++)
        {
            var a = args.Span[i];
            if (a == "--max-age" && i + 1 < args.Length && int.TryParse(args.Span[++i], out var d)) { maxAgeDays = d; continue; }
            if (a == "--keep-runs" && i + 1 < args.Length && int.TryParse(args.Span[++i], out var k)) { keepRuns = k; continue; }
            if (a == "--dry-run") { dryRun = true; continue; }
        }

        var cacheDir = Environment.GetEnvironmentVariable("SDV_CACHE_DIR")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache", "sdv-test-framework", "captures");

        var prefix = dryRun ? "[cache] would delete" : "[cache] deleted";
        var count = CaptureCacheCleaner.CleanCache(cacheDir, maxAgeDays, keepRuns, dryRun);
        Console.Out.WriteLine($"{prefix} {count} file(s) from {cacheDir}");
        return Task.FromResult(0);
    }
}
