using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SdvTestFramework.Runner.Commands;

/// <summary>
/// `doctor` — verifies that the local environment has what's needed to run scenarios:
/// .NET runtime, SDV install, SMAPI binary, Saves directory. Prints one line per check
/// with <c>[ok]</c> or <c>[FAIL]</c>, then a summary line. Exits 0 if all pass, 1 otherwise.
/// </summary>
public static class DoctorCommand
{
    public static Task<int> RunAsync(ReadOnlyMemory<string> args, CancellationToken ct)
    {
        int failed = 0;

        failed += Check("dotnet runtime available",
            () => !string.IsNullOrEmpty(Environment.Version.ToString())) ? 0 : 1;

        var install = ResolveSdvPath();
        failed += Check($"SDV install at {install}",
            () => Directory.Exists(install)) ? 0 : 1;
        failed += Check("SMAPI binary present",
            () => File.Exists(Path.Combine(install, "StardewModdingAPI"))) ? 0 : 1;

        failed += Check("Saves directory found",
            () => SavesDirCandidates().Any(Directory.Exists)) ? 0 : 1;

        Console.WriteLine(failed == 0
            ? "[doctor] all checks passed"
            : $"[doctor] {failed} check(s) failed");
        return Task.FromResult(failed == 0 ? 0 : 1);
    }

    private static string ResolveSdvPath() =>
        Environment.GetEnvironmentVariable("SDV_INSTALL_PATH")
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Stardew Valley");

    private static IEnumerable<string> SavesDirCandidates()
    {
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StardewValley", "Saves");
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".var/app/com.valvesoftware.Steam/.config/StardewValley/Saves");
    }

    private static bool Check(string name, Func<bool> predicate)
    {
        bool ok;
        try { ok = predicate(); }
        catch { ok = false; }
        Console.WriteLine($"  [{(ok ? "ok" : "FAIL")}] {name}");
        return ok;
    }
}
