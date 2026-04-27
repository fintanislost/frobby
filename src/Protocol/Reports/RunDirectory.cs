using System;
using System.IO;

namespace SdvTestFramework.Protocol.Reports;

/// <summary>
/// Filesystem wrapper for a single test-run's output directory. Owns the run-id, the
/// root path, and the standard subdir layout (scenarios/, assets/). Per-scenario
/// subdirs are created on demand via <see cref="ScenarioDir"/>.
/// </summary>
public sealed class RunDirectory
{
    public string Root { get; }
    public string RunId { get; }
    public string ScenariosDir => Path.Combine(Root, "scenarios");
    public string AssetsDir => Path.Combine(Root, "assets");

    private RunDirectory(string root, string runId)
    {
        Root = root;
        RunId = runId;
    }

    /// <summary>
    /// Create a new run directory under <paramref name="baseDir"/>. If
    /// <paramref name="explicitRunId"/> is null, generate one as
    /// <c>YYYY-MM-DDTHH-mm-ss-&lt;hash&gt;</c>. Subdirs (scenarios/, assets/) are
    /// pre-created. Throws if the resulting directory already exists.
    /// </summary>
    public static RunDirectory Create(string baseDir, string? explicitRunId = null)
    {
        var runId = explicitRunId ?? GenerateRunId();
        var root = Path.Combine(baseDir, runId);
        if (Directory.Exists(root))
            throw new IOException($"run directory already exists: {root}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "scenarios"));
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        return new RunDirectory(root, runId);
    }

    /// <summary>Path to the per-scenario subdir; creates the subdir + screenshots/ if absent.</summary>
    public string ScenarioDir(string scenarioName)
    {
        var safe = SanitizeName(scenarioName);
        var dir = Path.Combine(ScenariosDir, safe);
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "screenshots"));
        return dir;
    }

    private static string GenerateRunId()
    {
        var ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH-mm-ss");
        var hash = Guid.NewGuid().ToString("N").Substring(0, 6);
        return $"{ts}-{hash}";
    }

    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString();
    }
}
