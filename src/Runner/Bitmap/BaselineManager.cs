using System.IO;

namespace SdvTestFramework.Runner.Bitmap;

/// <summary>
/// Helpers for locating + writing bitmap baselines. Thin wrapper over <see cref="Path"/>
/// and <see cref="File"/>; split out so the assertion evaluator stays focused on diff logic.
/// </summary>
public static class BaselineManager
{
    /// <summary>
    /// Resolve a baseline reference to an absolute path. Absolute <paramref name="baselineRef"/>
    /// is returned unchanged; relative paths resolve against the scenario file's directory.
    /// </summary>
    public static string ResolveBaseline(string scenarioAbsPath, string baselineRef)
    {
        if (Path.IsPathRooted(baselineRef)) return baselineRef;
        var scenarioDir = Path.GetDirectoryName(scenarioAbsPath) ?? string.Empty;
        return Path.GetFullPath(Path.Combine(scenarioDir, baselineRef));
    }

    /// <summary>
    /// Write <paramref name="bytes"/> to <paramref name="absPath"/>, creating parent
    /// directories as needed. Overwrites any existing file.
    /// </summary>
    public static void WriteBaseline(string absPath, byte[] bytes)
    {
        var dir = Path.GetDirectoryName(absPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllBytes(absPath, bytes);
    }
}
