using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Scenarios;

namespace SdvTestFramework.Runner.Commands;

/// <summary>
/// `list` — scans a directory recursively for <c>*.test.json</c> scenario files and validates
/// each. Prints one line per file (<c>[ok]</c> or <c>[invalid]</c>) plus a summary. Exits 1
/// if any file fails validation.
/// </summary>
public static class ListCommand
{
    public static Task<int> RunAsync(ReadOnlyMemory<string> args, CancellationToken ct)
    {
        string root = args.Length > 0 ? args.Span[0] : Directory.GetCurrentDirectory();
        if (!Directory.Exists(root))
        {
            Console.Error.WriteLine($"not a directory: {root}");
            return Task.FromResult(2);
        }

        int ok = 0, bad = 0;
        foreach (var path in Directory.EnumerateFiles(root, "*.test.json", SearchOption.AllDirectories))
        {
            try
            {
                var spec = ScenarioLoader.Load(path);
                Console.WriteLine($"[ok] {spec.Name} ({path})");
                ok++;
            }
            catch (Exception ex)
            {
                // ScenarioLoadException's message already includes the path; no need to
                // duplicate it here. Other exceptions (e.g., IO) get wrapped identically.
                Console.WriteLine($"[invalid] {path}: {ex.Message}");
                bad++;
            }
        }

        Console.WriteLine($"[list] {ok} ok, {bad} invalid");
        return Task.FromResult(bad == 0 ? 0 : 1);
    }
}
