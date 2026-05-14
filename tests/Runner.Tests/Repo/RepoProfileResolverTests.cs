using System;
using System.Collections.Generic;
using System.IO;
using SdvTestFramework.Runner.Repo;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Repo;

public sealed class RepoProfileResolverTests : IDisposable
{
    private readonly string _repoRoot = CreateTempDirectory();

    [Fact]
    public void Resolve_uses_legacy_mod_set_when_profile_name_matches_mod_set()
    {
        Directory.CreateDirectory(Path.Combine(_repoRoot, "mods", "Core"));
        var config = Config(modSets: [ModSet("core", "mods/Core")]);

        var profile = RepoProfileResolver.Resolve(
            _repoRoot,
            config,
            requestedName: "core",
            environment: new Dictionary<string, string?>(),
            requireRepoExtraMods: true);

        Assert.Equal("core", profile.Id);
        Assert.Equal("core", profile.CacheNamespace);
        Assert.Equal([Path.Combine(_repoRoot, "mods", "Core")], profile.ExtraMods);
        Assert.Empty(profile.ConfigOverlays);
    }

    [Fact]
    public void Resolve_explicit_name_prefers_profile_when_profile_and_mod_set_names_match()
    {
        Directory.CreateDirectory(Path.Combine(_repoRoot, "mods", "Core"));
        var profileMod = Directory.CreateDirectory(Path.Combine(_repoRoot, "mods", "ProfileCore")).FullName;
        var config = Config(
            modSets: [ModSet("core", "mods/Core")],
            profiles: new Dictionary<string, RepoProfileConfig>
            {
                ["core"] = new() { ExtraMods = ["mods/ProfileCore"] },
            });

        var profile = RepoProfileResolver.Resolve(
            _repoRoot,
            config,
            requestedName: "core",
            environment: new Dictionary<string, string?>(),
            requireRepoExtraMods: true);

        Assert.Equal([profileMod], profile.ExtraMods);
    }

    [Fact]
    public void Resolve_blank_request_uses_first_legacy_mod_set_even_when_profile_name_matches()
    {
        var coreMod = Directory.CreateDirectory(Path.Combine(_repoRoot, "mods", "Core")).FullName;
        var profileMod = Directory.CreateDirectory(Path.Combine(_repoRoot, "mods", "ProfileCore")).FullName;
        var config = Config(
            modSets: [ModSet("core", "mods/Core")],
            profiles: new Dictionary<string, RepoProfileConfig>
            {
                ["core"] = new() { ExtraMods = ["mods/ProfileCore"] },
            });

        var profile = RepoProfileResolver.Resolve(
            _repoRoot,
            config,
            requestedName: null,
            environment: new Dictionary<string, string?>(),
            requireRepoExtraMods: true);

        Assert.Equal("core", profile.Id);
        Assert.Equal([coreMod], profile.ExtraMods);
        Assert.DoesNotContain(profileMod, profile.ExtraMods);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    public void Resolve_rejects_path_control_cache_namespaces(string cacheNamespace)
    {
        var config = Config(
            profiles: new Dictionary<string, RepoProfileConfig>
            {
                ["bad"] = new() { CacheNamespace = cacheNamespace },
            });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            RepoProfileResolver.Resolve(_repoRoot, config, "bad", new Dictionary<string, string?>(), true));

        Assert.Contains("cache namespace", ex.Message);
    }

    [Fact]
    public void Resolve_profile_inherits_parent_deps_extra_mods_and_overlays_in_order()
    {
        var cacheRoot = Path.Combine(_repoRoot, "dep-cache");
        var contentPatcher = CreateCachedMod(cacheRoot, "Pathoschild.ContentPatcher", "2.7.0");
        var coreMod = Directory.CreateDirectory(Path.Combine(_repoRoot, "mods", "Core")).FullName;
        var farmMod = Directory.CreateDirectory(Path.Combine(_repoRoot, "mods", "GrandpasFarm")).FullName;
        var overlaySource = Path.Combine(_repoRoot, "tests", "config", "gf.json");
        Directory.CreateDirectory(Path.GetDirectoryName(overlaySource)!);
        File.WriteAllText(overlaySource, "{}");
        var config = Config(
            profiles: new Dictionary<string, RepoProfileConfig>
            {
                ["sve-core"] = new()
                {
                    Deps = [new RepoModDependencyConfig { Id = "Pathoschild.ContentPatcher", Version = "2.7.0" }],
                    ExtraMods = ["mods/Core"],
                },
                ["sve-grandpas-farm"] = new()
                {
                    Inherits = "sve-core",
                    ExtraMods = ["mods/Core", "mods/GrandpasFarm"],
                    CacheNamespace = "grandpas-farm-cache",
                    ConfigOverlays =
                    [
                        new RepoConfigOverlayConfig
                        {
                            Source = "tests/config/gf.json",
                            TargetMod = "Pathoschild.ContentPatcher",
                            TargetPath = "config.json",
                        },
                    ],
                },
            });
        var env = new Dictionary<string, string?>
        {
            [RepoDependencyCache.CacheEnvironmentVariable] = cacheRoot,
        };

        var profile = RepoProfileResolver.Resolve(_repoRoot, config, "sve-grandpas-farm", env, requireRepoExtraMods: true);

        Assert.Equal("sve-grandpas-farm", profile.Id);
        Assert.Equal("grandpas-farm-cache", profile.CacheNamespace);
        Assert.Equal([contentPatcher, coreMod, farmMod], profile.ExtraMods);
        var overlay = Assert.Single(profile.ConfigOverlays);
        Assert.Equal(overlaySource, overlay.SourcePath);
        Assert.Equal("Pathoschild.ContentPatcher", overlay.TargetModUniqueId);
        Assert.Equal("config.json", overlay.TargetRelativePath);
    }

    [Fact]
    public void Resolve_unknown_profile_throws_clear_error()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            RepoProfileResolver.Resolve(_repoRoot, Config(), "missing", new Dictionary<string, string?>(), true));

        Assert.Contains("Unknown profile 'missing'", ex.Message);
    }

    [Fact]
    public void Resolve_profile_cycle_throws_clear_error()
    {
        var config = Config(
            profiles: new Dictionary<string, RepoProfileConfig>
            {
                ["a"] = new() { Inherits = "b", ExtraMods = ["mods/A"] },
                ["b"] = new() { Inherits = "a", ExtraMods = ["mods/B"] },
            });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            RepoProfileResolver.Resolve(_repoRoot, config, "a", new Dictionary<string, string?>(), false));

        Assert.Contains("profile inheritance cycle", ex.Message);
        Assert.Contains("a -> b -> a", ex.Message);
    }

    [Fact]
    public void Resolve_missing_overlay_source_throws_before_launch()
    {
        var config = Config(
            profiles: new Dictionary<string, RepoProfileConfig>
            {
                ["broken"] = new()
                {
                    ExtraMods = ["mods/Broken"],
                    ConfigOverlays =
                    [
                        new RepoConfigOverlayConfig
                        {
                            Source = "tests/config/missing.json",
                            TargetMod = "Example.Mod",
                            TargetPath = "config.json",
                        },
                    ],
                },
            });

        var ex = Assert.Throws<FileNotFoundException>(() =>
            RepoProfileResolver.Resolve(_repoRoot, config, "broken", new Dictionary<string, string?>(), false));

        Assert.Contains("tests/config/missing.json", ex.Message);
    }

    public void Dispose()
    {
        Directory.Delete(_repoRoot, recursive: true);
    }

    private static RepoTestConfig Config(
        RepoModSetConfig[]? modSets = null,
        IReadOnlyDictionary<string, RepoProfileConfig>? profiles = null)
        => new()
        {
            Project = new RepoProjectConfig { Name = "Frobby", Slug = "frobby", Version = "1.2.3" },
            Build = new RepoBuildConfig { Command = "dotnet", Args = ["build"] },
            DefaultTarget = "tests/sdv",
            ModSets = modSets ?? [ModSet("core", "mods/Core")],
            Profiles = profiles ?? new Dictionary<string, RepoProfileConfig>(),
        };

    private static RepoModSetConfig ModSet(string name, params string[] extraMods)
        => new() { Name = name, ExtraMods = extraMods };

    private static string CreateCachedMod(string cacheRoot, string uniqueId, string version)
    {
        var path = Path.Combine(cacheRoot, uniqueId);
        Directory.CreateDirectory(path);
        File.WriteAllText(
            Path.Combine(path, "manifest.json"),
            $$"""{"Name":"Test","UniqueID":"{{uniqueId}}","Version":"{{version}}","EntryDll":"Test.dll"}""");
        return path;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "repo-profile-resolver-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
