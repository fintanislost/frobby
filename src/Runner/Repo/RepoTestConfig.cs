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

        config.Validate();
        return config;
    }

    private void Validate()
    {
        var project = Project ?? throw Missing("project");
        RequireText(project.Name, "project.name");
        RequireText(project.Slug, "project.slug");
        RequireText(project.Version, "project.version");
        var build = Build ?? throw Missing("build");
        RequireText(build.Command, "build.command");
        RequireText(DefaultTarget, "defaultTarget");
        Require(ModSets is { Count: > 0 }, "modSets");

        for (var i = 0; i < ModSets.Count; i++)
        {
            if (ModSets[i] is not { } modSet)
            {
                throw Missing($"modSets[{i}]");
            }

            RequireText(modSet.Name, $"modSets[{i}].name");
            Require(modSet.ExtraMods is { Count: > 0 }, $"modSets[{i}].extraMods");
        }
    }

    private static void RequireText(string? value, string field)
    {
        Require(!string.IsNullOrWhiteSpace(value), field);
    }

    private static void Require(bool condition, string field)
    {
        if (!condition)
        {
            throw Missing(field);
        }
    }

    private static InvalidOperationException Missing(string field)
        => new($"{FileName} requires '{field}'.");
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

    [JsonPropertyName("extraMods")]
    public IReadOnlyList<string> ExtraMods { get; init; } = Array.Empty<string>();
}
