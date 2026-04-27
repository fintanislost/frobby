using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SdvTestFramework.Protocol;

/// <summary>
/// Copies already-built SMAPI mod folders into an isolated mods directory before launching SDV.
/// </summary>
public static class ExtraModDeployer
{
    public static IReadOnlyList<string> DeployMany(string modsPath, IEnumerable<string> modPaths)
    {
        var deployed = new List<string>();
        foreach (var modPath in modPaths)
        {
            if (string.IsNullOrWhiteSpace(modPath))
                continue;

            deployed.Add(Deploy(modsPath, modPath));
        }

        return deployed;
    }

    public static string Deploy(string modsPath, string modPath)
    {
        if (string.IsNullOrWhiteSpace(modsPath))
            throw new ArgumentException("modsPath required", nameof(modsPath));
        if (string.IsNullOrWhiteSpace(modPath))
            throw new ArgumentException("modPath required", nameof(modPath));

        var sourceDir = NormalizeDirectoryPath(modPath);
        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException($"extra mod directory not found: {modPath}");

        var manifestPath = Path.Combine(sourceDir, "manifest.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"extra mod manifest not found: {manifestPath}", manifestPath);

        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        if (!doc.RootElement.TryGetProperty("UniqueID", out var idElement)
            || idElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(idElement.GetString()))
        {
            throw new InvalidOperationException($"manifest missing non-empty UniqueID: {manifestPath}");
        }

        var targetDir = NormalizeDirectoryPath(Path.Combine(modsPath, SanitizeFolderName(idElement.GetString()!)));
        if (PathsEqual(sourceDir, targetDir))
            return targetDir;
        if (IsSubPathOf(sourceDir, targetDir))
            throw new InvalidOperationException($"extra mod source is inside deployment target: {sourceDir}");
        if (IsSubPathOf(targetDir, sourceDir))
            throw new InvalidOperationException($"extra mod target is inside source directory: {targetDir}");

        if (Directory.Exists(targetDir))
            Directory.Delete(targetDir, recursive: true);

        Directory.CreateDirectory(targetDir);
        CopyDirectory(sourceDir, targetDir);
        return targetDir;
    }

    public static IReadOnlyList<string> ParseEnvList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<string>();

        return value.Split(
            Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string SanitizeFolderName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');

        return value;
    }

    private static string NormalizeDirectoryPath(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool PathsEqual(string left, string right)
        => string.Equals(left, right, PathStringComparison);

    private static bool IsSubPathOf(string candidate, string parent)
    {
        if (!candidate.StartsWith(parent, PathStringComparison))
            return false;
        return candidate.Length > parent.Length && IsDirectorySeparator(candidate[parent.Length]);
    }

    private static bool IsDirectorySeparator(char c)
        => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;

    private static StringComparison PathStringComparison
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, dir);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
            File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(file));
        }
    }
}
