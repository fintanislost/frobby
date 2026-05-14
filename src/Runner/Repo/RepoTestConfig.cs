using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SdvTestFramework.Protocol.Json;

namespace SdvTestFramework.Runner.Repo;

public sealed class RepoTestConfig
{
    public const string FileName = "sdv-test.config.json";

    [JsonPropertyName("project")]
    public RepoProjectConfig Project { get; init; } = new();

    [JsonPropertyName("frobbyRoot")]
    public string? FrobbyRoot { get; init; }

    [JsonPropertyName("build")]
    public RepoBuildConfig Build { get; init; } = new();

    [JsonPropertyName("defaultTarget")]
    public string? DefaultTarget { get; init; }

    [JsonPropertyName("baselineTarget")]
    public string? BaselineTarget { get; init; }

    [JsonPropertyName("modSets")]
    public IReadOnlyList<RepoModSetConfig> ModSets { get; init; } = Array.Empty<RepoModSetConfig>();

    [JsonPropertyName("profiles")]
    public IReadOnlyDictionary<string, RepoProfileConfig> Profiles { get; init; }
        = new Dictionary<string, RepoProfileConfig>(StringComparer.Ordinal);

    public static RepoTestConfig Load(string repoRoot)
    {
        var path = Path.Combine(repoRoot, FileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Missing required {FileName} at {path}.", path);
        }

        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<RepoTestConfig>(json, ProtocolJson.Options)
            ?? throw new InvalidOperationException($"{FileName} did not contain a valid repo test config.");

        config.Validate(path);
        return config;
    }

    private void Validate(string path)
    {
        var project = Project ?? throw Missing(path, "project");
        RequireText(project.Name, path, "project.name");
        RequireText(project.Slug, path, "project.slug");
        RequireText(project.Version, path, "project.version");
        var build = Build ?? throw Missing(path, "build");
        RequireText(build.Command, path, "build.command");
        RequireText(DefaultTarget, path, "defaultTarget");
        ValidateEntries(build.Args, path, "build.args");
        Require(ModSets is { Count: > 0 }, path, "modSets");
        Require(Profiles is not null, path, "profiles");

        for (var i = 0; i < ModSets.Count; i++)
        {
            if (ModSets[i] is not { } modSet)
            {
                throw Missing(path, $"modSets[{i}]");
            }

            RequireText(modSet.Name, path, $"modSets[{i}].name");
            Require(modSet.ExtraMods is { Count: > 0 }, path, $"modSets[{i}].extraMods");
            Require(modSet.Deps is not null, path, $"modSets[{i}].deps");
            ValidateDependencies(modSet.Deps, path, $"modSets[{i}].deps");
            ValidateEntries(modSet.ExtraMods, path, $"modSets[{i}].extraMods");
        }

        ValidateProfiles(Profiles, path);
    }

    private static void ValidateProfiles(
        IReadOnlyDictionary<string, RepoProfileConfig>? profiles,
        string path)
    {
        if (profiles is null)
        {
            return;
        }

        foreach (var (name, profile) in profiles)
        {
            RequireText(name, path, $"profiles.{name}");
            if (profile is null)
            {
                throw Missing(path, $"profiles.{name}");
            }

            if (profile.Inherits is not null)
            {
                RequireText(profile.Inherits, path, $"profiles.{name}.inherits");
            }

            if (profile.CacheNamespace is not null)
            {
                RequireText(profile.CacheNamespace, path, $"profiles.{name}.cacheNamespace");
            }

            Require(profile.Deps is not null, path, $"profiles.{name}.deps");
            Require(profile.ExtraMods is not null, path, $"profiles.{name}.extraMods");
            Require(profile.ConfigOverlays is not null, path, $"profiles.{name}.configOverlays");
            ValidateDependencies(profile.Deps, path, $"profiles.{name}.deps");
            ValidateEntries(profile.ExtraMods, path, $"profiles.{name}.extraMods");
            ValidateConfigOverlays(profile.ConfigOverlays, path, $"profiles.{name}.configOverlays");
        }
    }

    private static void ValidateConfigOverlays(
        IReadOnlyList<RepoConfigOverlayConfig>? overlays,
        string path,
        string field)
    {
        if (overlays is null)
        {
            return;
        }

        for (var i = 0; i < overlays.Count; i++)
        {
            if (overlays[i] is not { } overlay)
            {
                throw Missing(path, $"{field}[{i}]");
            }

            RequireText(overlay.Source, path, $"{field}[{i}].source");
            RequireText(overlay.TargetMod, path, $"{field}[{i}].targetMod");
            RequireText(overlay.TargetPath, path, $"{field}[{i}].targetPath");
            Require(IsSafeOverlayTargetPath(overlay.TargetPath!), path, $"{field}[{i}].targetPath");
        }
    }

    private static bool IsSafeOverlayTargetPath(string targetPath)
    {
        if (Path.IsPathRooted(targetPath)
            || targetPath.StartsWith('\\')
            || targetPath.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        var parts = targetPath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return Array.TrueForAll(parts, part => part is not "." and not "..");
    }

    private static void ValidateDependencies(
        IReadOnlyList<RepoModDependencyConfig>? dependencies,
        string path,
        string field)
    {
        if (dependencies is null)
        {
            return;
        }

        for (var i = 0; i < dependencies.Count; i++)
        {
            if (dependencies[i] is not { } dependency)
            {
                throw Missing(path, $"{field}[{i}]");
            }

            RequireText(dependency.Id, path, $"{field}[{i}].id");
            if (dependency.Version is not null)
            {
                RequireText(dependency.Version, path, $"{field}[{i}].version");
            }
        }
    }

    private static void ValidateEntries(IReadOnlyList<string>? values, string path, string field)
    {
        if (values is null)
        {
            return;
        }

        for (var i = 0; i < values.Count; i++)
        {
            RequireText(values[i], path, $"{field}[{i}]");
        }
    }

    private static void RequireText(string? value, string path, string field)
    {
        Require(!string.IsNullOrWhiteSpace(value), path, field);
    }

    private static void Require(bool condition, string path, string field)
    {
        if (!condition)
        {
            throw Missing(path, field);
        }
    }

    private static InvalidOperationException Missing(string path, string field)
        => new($"{path}: {FileName} requires '{field}'.");
}

public sealed class RepoProjectConfig
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("slug")]
    public string? Slug { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }
}

public sealed class RepoBuildConfig
{
    [JsonPropertyName("command")]
    public string? Command { get; init; }

    [JsonPropertyName("args")]
    public IReadOnlyList<string> Args { get; init; } = Array.Empty<string>();
}

public sealed class RepoModSetConfig
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("deps")]
    public IReadOnlyList<RepoModDependencyConfig> Deps { get; init; } = Array.Empty<RepoModDependencyConfig>();

    [JsonPropertyName("extraMods")]
    public IReadOnlyList<string> ExtraMods { get; init; } = Array.Empty<string>();
}

public sealed class RepoProfileConfig
{
    [JsonPropertyName("inherits")]
    public string? Inherits { get; init; }

    [JsonPropertyName("deps")]
    public IReadOnlyList<RepoModDependencyConfig> Deps { get; init; } = Array.Empty<RepoModDependencyConfig>();

    [JsonPropertyName("extraMods")]
    public IReadOnlyList<string> ExtraMods { get; init; } = Array.Empty<string>();

    [JsonPropertyName("configOverlays")]
    public IReadOnlyList<RepoConfigOverlayConfig> ConfigOverlays { get; init; } = Array.Empty<RepoConfigOverlayConfig>();

    [JsonPropertyName("cacheNamespace")]
    public string? CacheNamespace { get; init; }
}

public sealed class RepoConfigOverlayConfig
{
    [JsonPropertyName("source")]
    public string? Source { get; init; }

    [JsonPropertyName("targetMod")]
    public string? TargetMod { get; init; }

    [JsonPropertyName("targetPath")]
    public string? TargetPath { get; init; }
}

public sealed class RepoModDependencyConfig
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }
}
