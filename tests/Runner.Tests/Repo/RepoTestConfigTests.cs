using System;
using System.IO;
using System.Text.Json;
using SdvTestFramework.Runner.Repo;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Repo;

public sealed class RepoTestConfigTests : IDisposable
{
    private readonly string _repoRoot = CreateTempDirectory();

    [Fact]
    public void Load_reads_project_build_targets_and_mod_sets()
    {
        WriteConfig(
            """
            {
              "project": {
                "name": "Frobby",
                "slug": "frobby",
                "version": "1.2.3"
              },
              "frobbyRoot": "../frobby",
              "build": {
                "command": "dotnet",
                "args": ["build", "Frobby.sln"]
              },
              "defaultTarget": "smoke",
              "baselineTarget": "baseline",
              "modSets": [
                {
                  "name": "smoke",
                  "extraMods": ["mods/Content Patcher", "$EXTRA_MOD"]
                }
              ]
            }
            """);

        var config = RepoTestConfig.Load(_repoRoot);

        Assert.Equal("Frobby", config.Project.Name);
        Assert.Equal("frobby", config.Project.Slug);
        Assert.Equal("1.2.3", config.Project.Version);
        Assert.Equal("../frobby", config.FrobbyRoot);
        Assert.Equal("dotnet", config.Build.Command);
        Assert.Equal(new[] { "build", "Frobby.sln" }, config.Build.Args);
        Assert.Equal("smoke", config.DefaultTarget);
        Assert.Equal("baseline", config.BaselineTarget);
        var modSet = Assert.Single(config.ModSets);
        Assert.Equal("smoke", modSet.Name);
        Assert.Equal(new[] { "mods/Content Patcher", "$EXTRA_MOD" }, modSet.ExtraMods);
    }

    [Fact]
    public void Load_missing_config_throws_file_not_found_with_config_name()
    {
        var ex = Assert.Throws<FileNotFoundException>(() => RepoTestConfig.Load(_repoRoot));

        Assert.Contains("sdv-test.config.json", ex.Message);
    }

    [Theory]
    [InlineData("""{"project":{"slug":"frobby","version":"1.0.0"},"build":{"command":"dotnet"},"defaultTarget":"smoke","modSets":[{"name":"smoke","extraMods":["mods/a"]}]}""", "project.name")]
    [InlineData("""{"project":{"name":"Frobby","version":"1.0.0"},"build":{"command":"dotnet"},"defaultTarget":"smoke","modSets":[{"name":"smoke","extraMods":["mods/a"]}]}""", "project.slug")]
    [InlineData("""{"project":{"name":"Frobby","slug":"frobby"},"build":{"command":"dotnet"},"defaultTarget":"smoke","modSets":[{"name":"smoke","extraMods":["mods/a"]}]}""", "project.version")]
    [InlineData("""{"project":{"name":"Frobby","slug":"frobby","version":"1.0.0"},"build":{},"defaultTarget":"smoke","modSets":[{"name":"smoke","extraMods":["mods/a"]}]}""", "build.command")]
    [InlineData("""{"project":{"name":"Frobby","slug":"frobby","version":"1.0.0"},"build":{"command":"dotnet"},"modSets":[{"name":"smoke","extraMods":["mods/a"]}]}""", "defaultTarget")]
    [InlineData("""{"project":{"name":"Frobby","slug":"frobby","version":"1.0.0"},"build":{"command":"dotnet"},"defaultTarget":"smoke","modSets":[]}""", "modSets")]
    [InlineData("""{"project":{"name":"Frobby","slug":"frobby","version":"1.0.0"},"build":{"command":"dotnet"},"defaultTarget":"smoke","modSets":[{"extraMods":["mods/a"]}]}""", "modSets[0].name")]
    [InlineData("""{"project":{"name":"Frobby","slug":"frobby","version":"1.0.0"},"build":{"command":"dotnet"},"defaultTarget":"smoke","modSets":[{"name":"smoke","extraMods":[]}]}""", "modSets[0].extraMods")]
    public void Load_validates_required_fields(string json, string field)
    {
        WriteConfig(json);

        var ex = Assert.Throws<InvalidOperationException>(() => RepoTestConfig.Load(_repoRoot));

        Assert.Contains(field, ex.Message);
        Assert.Contains("sdv-test.config.json", ex.Message);
    }

    public void Dispose()
    {
        Directory.Delete(_repoRoot, recursive: true);
    }

    private void WriteConfig(string json)
    {
        using var doc = JsonDocument.Parse(json);
        File.WriteAllText(
            Path.Combine(_repoRoot, RepoTestConfig.FileName),
            JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "sdv-repo-config-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
