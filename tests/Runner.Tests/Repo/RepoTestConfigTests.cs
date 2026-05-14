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
    public void Load_reads_mod_set_deps()
    {
        WriteConfig(
            """
            {
              "project": { "name": "Frobby", "slug": "frobby", "version": "1.2.3" },
              "build": { "command": "dotnet" },
              "defaultTarget": "smoke",
              "modSets": [
                {
                  "name": "core",
                  "deps": [
                    { "id": "Pathoschild.ContentPatcher", "version": "2.7.0" },
                    { "id": "Esca.FarmTypeManager" }
                  ],
                  "extraMods": ["mods/Frobby"]
                }
              ]
            }
            """);

        var config = RepoTestConfig.Load(_repoRoot);

        var modSet = Assert.Single(config.ModSets);
        Assert.Equal("core", modSet.Name);
        Assert.Equal(2, modSet.Deps.Count);
        Assert.Equal("Pathoschild.ContentPatcher", modSet.Deps[0].Id);
        Assert.Equal("2.7.0", modSet.Deps[0].Version);
        Assert.Equal("Esca.FarmTypeManager", modSet.Deps[1].Id);
        Assert.Null(modSet.Deps[1].Version);
    }

    [Fact]
    public void Load_reads_profiles_with_inheritance_deps_extra_mods_cache_namespace_and_overlays()
    {
        WriteConfig(
            """
            {
              "project": { "name": "Frobby", "slug": "frobby", "version": "1.2.3" },
              "build": { "command": "dotnet" },
              "defaultTarget": "smoke",
              "modSets": [
                { "name": "core", "extraMods": ["mods/Core"] }
              ],
              "profiles": {
                "sve-core": {
                  "deps": [{ "id": "Pathoschild.ContentPatcher" }],
                  "extraMods": ["mods/SVE"]
                },
                "sve-grandpas-farm": {
                  "inherits": "sve-core",
                  "extraMods": ["Grandpa's Farm/[CP] Grandpa's Farm"],
                  "cacheNamespace": "sve-grandpas-farm",
                  "configOverlays": [
                    {
                      "source": "tests/config/grandpas-farm/content-patcher.json",
                      "targetMod": "Pathoschild.ContentPatcher",
                      "targetPath": "config.json"
                    }
                  ]
                }
              }
            }
            """);

        var config = RepoTestConfig.Load(_repoRoot);

        Assert.True(config.Profiles.ContainsKey("sve-core"));
        Assert.True(config.Profiles.ContainsKey("sve-grandpas-farm"));
        Assert.Equal("sve-core", config.Profiles["sve-grandpas-farm"].Inherits);
        Assert.Equal("sve-grandpas-farm", config.Profiles["sve-grandpas-farm"].CacheNamespace);
        var overlay = Assert.Single(config.Profiles["sve-grandpas-farm"].ConfigOverlays);
        Assert.Equal("tests/config/grandpas-farm/content-patcher.json", overlay.Source);
        Assert.Equal("Pathoschild.ContentPatcher", overlay.TargetMod);
        Assert.Equal("config.json", overlay.TargetPath);
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
    [InlineData("""{"project":{"name":"Frobby","slug":"frobby","version":"1.0.0"},"build":{"command":"dotnet"},"defaultTarget":"smoke","modSets":[{"name":"smoke","deps":[{}],"extraMods":["mods/a"]}]}""", "modSets[0].deps[0].id")]
    [InlineData("""{"project":{"name":"Frobby","slug":"frobby","version":"1.0.0"},"build":{"command":"dotnet"},"defaultTarget":"smoke","modSets":[{"name":"smoke","deps":[{"id":" "}],"extraMods":["mods/a"]}]}""", "modSets[0].deps[0].id")]
    [InlineData("""{"project":{"name":"Frobby","slug":"frobby","version":"1.0.0"},"build":{"command":"dotnet"},"defaultTarget":"smoke","modSets":[{"name":"smoke","extraMods":["mods/a"]}],"profiles":{"bad":{"extraMods":[" "]}}}""", "profiles.bad.extraMods[0]")]
    [InlineData("""{"project":{"name":"Frobby","slug":"frobby","version":"1.0.0"},"build":{"command":"dotnet"},"defaultTarget":"smoke","modSets":[{"name":"smoke","extraMods":["mods/a"]}],"profiles":{"bad":{"configOverlays":[{"source":" ","targetMod":"Mod","targetPath":"config.json"}]}}}""", "profiles.bad.configOverlays[0].source")]
    [InlineData("""{"project":{"name":"Frobby","slug":"frobby","version":"1.0.0"},"build":{"command":"dotnet"},"defaultTarget":"smoke","modSets":[{"name":"smoke","extraMods":["mods/a"]}],"profiles":{"bad":{"configOverlays":[{"source":"a.json","targetMod":" ","targetPath":"config.json"}]}}}""", "profiles.bad.configOverlays[0].targetMod")]
    [InlineData("""{"project":{"name":"Frobby","slug":"frobby","version":"1.0.0"},"build":{"command":"dotnet"},"defaultTarget":"smoke","modSets":[{"name":"smoke","extraMods":["mods/a"]}],"profiles":{"bad":{"configOverlays":[{"source":"a.json","targetMod":"Mod","targetPath":" "}]}}}""", "profiles.bad.configOverlays[0].targetPath")]
    public void Load_validates_required_fields(string json, string field)
    {
        WriteConfig(json);

        var ex = Assert.Throws<InvalidOperationException>(() => RepoTestConfig.Load(_repoRoot));

        Assert.Contains(field, ex.Message);
        Assert.Contains("sdv-test.config.json", ex.Message);
    }

    [Fact]
    public void Load_validates_dep_version_entry_when_present()
    {
        WriteConfig(
            """
            {
              "project": { "name": "Frobby", "slug": "frobby", "version": "1.0.0" },
              "build": { "command": "dotnet" },
              "defaultTarget": "smoke",
              "modSets": [
                {
                  "name": "smoke",
                  "deps": [{ "id": "Pathoschild.ContentPatcher", "version": " " }],
                  "extraMods": ["mods/a"]
                }
              ]
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => RepoTestConfig.Load(_repoRoot));

        Assert.Contains("modSets[0].deps[0].version", ex.Message);
        Assert.Contains("sdv-test.config.json", ex.Message);
    }

    [Theory]
    [InlineData("""{"project":{"name":"Frobby","slug":"frobby","version":"1.0.0"},"build":{"command":"dotnet","args":["build"," "]},"defaultTarget":"smoke","modSets":[{"name":"smoke","extraMods":["mods/a"]}]}""", "build.args[1]")]
    [InlineData("""{"project":{"name":"Frobby","slug":"frobby","version":"1.0.0"},"build":{"command":"dotnet","args":["build",null]},"defaultTarget":"smoke","modSets":[{"name":"smoke","extraMods":["mods/a"]}]}""", "build.args[1]")]
    public void Load_validates_build_args_entries(string json, string field)
    {
        WriteConfig(json);

        var ex = Assert.Throws<InvalidOperationException>(() => RepoTestConfig.Load(_repoRoot));

        Assert.Contains(field, ex.Message);
        Assert.Contains("sdv-test.config.json", ex.Message);
    }

    [Fact]
    public void Load_validates_extra_mods_entries()
    {
        WriteConfig(
            """
            {
              "project": { "name": "Frobby", "slug": "frobby", "version": "1.0.0" },
              "build": { "command": "dotnet" },
              "defaultTarget": "smoke",
              "modSets": [
                { "name": "smoke", "extraMods": ["mods/a", "  "] }
              ]
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => RepoTestConfig.Load(_repoRoot));

        Assert.Contains("modSets[0].extraMods[1]", ex.Message);
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
