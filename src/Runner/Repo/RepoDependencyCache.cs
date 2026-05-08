using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SdvTestFramework.Protocol;

namespace SdvTestFramework.Runner.Repo;

public enum RepoDependencyStatus
{
    Ok,
    Missing,
    BadManifest,
    UniqueIdMismatch,
    VersionMismatch,
}

public sealed record RepoDependencyManifest(string UniqueId, string? Version);

public sealed record RepoDependencyCheck(
    RepoDependencyStatus Status,
    string DependencyId,
    string ExpectedPath,
    RepoDependencyManifest? Manifest,
    string Message);

public static class RepoDependencyCache
{
    public const string CacheEnvironmentVariable = "SDV_TEST_MOD_CACHE";

    public static RepoDependencyManifest Import(
        string sourcePath,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        var manifest = ReadManifest(sourcePath);
        var cacheRoot = ResolveCacheRoot(environment);
        Directory.CreateDirectory(cacheRoot);
        ExtraModDeployer.Deploy(cacheRoot, sourcePath);
        return manifest;
    }

    public static string ResolveRequired(
        RepoModDependencyConfig dependency,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        var check = Check(dependency, environment);
        if (check.Status != RepoDependencyStatus.Ok)
        {
            throw new InvalidOperationException(check.Message);
        }

        return check.ExpectedPath;
    }

    public static RepoDependencyCheck Check(
        RepoModDependencyConfig dependency,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        var id = RequireDependencyId(dependency);
        var path = Path.Combine(ResolveCacheRoot(environment), SanitizeFolderName(id));
        if (!Directory.Exists(path))
        {
            return new RepoDependencyCheck(
                RepoDependencyStatus.Missing,
                id,
                path,
                null,
                $"[repo deps] missing {id} in {Path.GetDirectoryName(path)}. Import it with: sdv-test repo deps import --from <path-to-{id}>");
        }

        RepoDependencyManifest manifest;
        try
        {
            manifest = ReadManifest(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or JsonException)
        {
            return new RepoDependencyCheck(
                RepoDependencyStatus.BadManifest,
                id,
                path,
                null,
                $"[repo deps] {Path.Combine(path, "manifest.json")} is invalid: {ex.Message}");
        }

        if (!string.Equals(manifest.UniqueId, id, StringComparison.Ordinal))
        {
            return new RepoDependencyCheck(
                RepoDependencyStatus.UniqueIdMismatch,
                id,
                path,
                manifest,
                $"[repo deps] {id} UniqueID mismatch: expected {id}, found {manifest.UniqueId}.");
        }

        if (!string.IsNullOrWhiteSpace(dependency.Version)
            && !string.Equals(manifest.Version, dependency.Version, StringComparison.Ordinal))
        {
            return new RepoDependencyCheck(
                RepoDependencyStatus.VersionMismatch,
                id,
                path,
                manifest,
                $"[repo deps] {id} version mismatch: expected {dependency.Version}, found {manifest.Version ?? "<missing>"}.");
        }

        return new RepoDependencyCheck(
            RepoDependencyStatus.Ok,
            id,
            path,
            manifest,
            $"[repo deps] {id} {manifest.Version ?? "<unknown>"} ok at {path}");
    }

    public static string ResolveCacheRoot(IReadOnlyDictionary<string, string?>? environment = null)
    {
        var configured = GetEnvironmentValue(CacheEnvironmentVariable, environment);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var frameworkRoot = FindFrameworkRoot(Directory.GetCurrentDirectory())
            ?? FindFrameworkRoot(AppContext.BaseDirectory)
            ?? throw new InvalidOperationException(
                $"Unable to locate sdv-test-framework.slnx. Set {CacheEnvironmentVariable} to a dependency cache directory.");
        return Path.Combine(frameworkRoot, ".cache", "deps");
    }

    private static RepoDependencyManifest ReadManifest(string modPath)
    {
        if (string.IsNullOrWhiteSpace(modPath))
        {
            throw new InvalidOperationException("dependency mod path is required.");
        }

        var manifestPath = Path.Combine(Path.GetFullPath(modPath), "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"dependency manifest not found: {manifestPath}", manifestPath);
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        if (!doc.RootElement.TryGetProperty("UniqueID", out var idElement)
            || idElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(idElement.GetString()))
        {
            throw new InvalidOperationException($"manifest missing non-empty UniqueID: {manifestPath}");
        }

        string? version = null;
        if (doc.RootElement.TryGetProperty("Version", out var versionElement)
            && versionElement.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(versionElement.GetString()))
        {
            version = versionElement.GetString();
        }

        return new RepoDependencyManifest(idElement.GetString()!, version);
    }

    private static string? FindFrameworkRoot(string start)
    {
        if (string.IsNullOrWhiteSpace(start))
        {
            return null;
        }

        var fullStart = Path.GetFullPath(start);
        var directory = File.Exists(fullStart)
            ? new FileInfo(fullStart).Directory
            : new DirectoryInfo(fullStart);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "sdv-test-framework.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string RequireDependencyId(RepoModDependencyConfig dependency)
        => !string.IsNullOrWhiteSpace(dependency.Id)
            ? dependency.Id
            : throw new InvalidOperationException("repo dependency id is required.");

    private static string? GetEnvironmentValue(
        string name,
        IReadOnlyDictionary<string, string?>? environment)
        => environment is not null
            ? environment.TryGetValue(name, out var value) ? value : null
            : Environment.GetEnvironmentVariable(name);

    private static string SanitizeFolderName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '_');
        }

        return value;
    }
}
