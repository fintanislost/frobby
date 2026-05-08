using System;
using System.Collections.Generic;
using System.IO;
using SdvTestFramework.Runner.Repo;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Repo;

public sealed class RepoDependencyCacheTests : IDisposable
{
    private readonly string _root = CreateTempDirectory();

    [Fact]
    public void Import_copies_mod_folder_to_cache_folder_named_by_unique_id()
    {
        var cacheRoot = Path.Combine(_root, "deps");
        var source = CreateMod("SourceContentPatcher", "Pathoschild.ContentPatcher", "2.7.0");
        File.WriteAllText(Path.Combine(source, "assets.txt"), "copied");

        var manifest = RepoDependencyCache.Import(source, Env(cacheRoot));

        Assert.Equal("Pathoschild.ContentPatcher", manifest.UniqueId);
        Assert.Equal("2.7.0", manifest.Version);
        var cached = Path.Combine(cacheRoot, "Pathoschild.ContentPatcher");
        Assert.True(File.Exists(Path.Combine(cached, "manifest.json")));
        Assert.Equal("copied", File.ReadAllText(Path.Combine(cached, "assets.txt")));
    }

    [Fact]
    public void Check_returns_missing_when_configured_dependency_is_not_cached()
    {
        var cacheRoot = Path.Combine(_root, "deps");

        var check = RepoDependencyCache.Check(
            new RepoModDependencyConfig { Id = "Pathoschild.ContentPatcher" },
            Env(cacheRoot));

        Assert.Equal(RepoDependencyStatus.Missing, check.Status);
        Assert.Contains("Pathoschild.ContentPatcher", check.Message);
        Assert.Contains("repo deps import --from", check.Message);
    }

    [Fact]
    public void Check_detects_unique_id_mismatch()
    {
        var cacheRoot = Path.Combine(_root, "deps");
        var cached = Path.Combine(cacheRoot, "Pathoschild.ContentPatcher");
        Directory.CreateDirectory(cached);
        WriteManifest(cached, "Other.Mod", "1.0.0");

        var check = RepoDependencyCache.Check(
            new RepoModDependencyConfig { Id = "Pathoschild.ContentPatcher" },
            Env(cacheRoot));

        Assert.Equal(RepoDependencyStatus.UniqueIdMismatch, check.Status);
        Assert.Contains("expected Pathoschild.ContentPatcher", check.Message);
        Assert.Contains("found Other.Mod", check.Message);
    }

    [Fact]
    public void Check_detects_version_mismatch()
    {
        var cacheRoot = Path.Combine(_root, "deps");
        var cached = Path.Combine(cacheRoot, "Pathoschild.ContentPatcher");
        Directory.CreateDirectory(cached);
        WriteManifest(cached, "Pathoschild.ContentPatcher", "2.6.0");

        var check = RepoDependencyCache.Check(
            new RepoModDependencyConfig { Id = "Pathoschild.ContentPatcher", Version = "2.7.0" },
            Env(cacheRoot));

        Assert.Equal(RepoDependencyStatus.VersionMismatch, check.Status);
        Assert.Contains("expected 2.7.0", check.Message);
        Assert.Contains("found 2.6.0", check.Message);
    }

    [Fact]
    public void Check_detects_bad_manifest()
    {
        var cacheRoot = Path.Combine(_root, "deps");
        var cached = Path.Combine(cacheRoot, "Pathoschild.ContentPatcher");
        Directory.CreateDirectory(cached);
        File.WriteAllText(Path.Combine(cached, "manifest.json"), """{"Name":"Bad"}""");

        var check = RepoDependencyCache.Check(
            new RepoModDependencyConfig { Id = "Pathoschild.ContentPatcher" },
            Env(cacheRoot));

        Assert.Equal(RepoDependencyStatus.BadManifest, check.Status);
        Assert.Contains("manifest", check.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UniqueID", check.Message);
    }

    [Fact]
    public void Resolve_required_dependency_returns_cached_path_when_manifest_matches()
    {
        var cacheRoot = Path.Combine(_root, "deps");
        var cached = Path.Combine(cacheRoot, "Esca.FarmTypeManager");
        Directory.CreateDirectory(cached);
        WriteManifest(cached, "Esca.FarmTypeManager", "1.23.0");

        var path = RepoDependencyCache.ResolveRequired(
            new RepoModDependencyConfig { Id = "Esca.FarmTypeManager", Version = "1.23.0" },
            Env(cacheRoot));

        Assert.Equal(cached, path);
    }

    [Fact]
    public void Resolve_cache_root_uses_environment_override()
    {
        var cacheRoot = Path.Combine(_root, "custom-cache");

        var resolved = RepoDependencyCache.ResolveCacheRoot(Env(cacheRoot));

        Assert.Equal(cacheRoot, resolved);
    }

    [Fact]
    public void Resolve_cache_root_defaults_to_source_tree_cache()
    {
        var resolved = RepoDependencyCache.ResolveCacheRoot(new Dictionary<string, string?>());

        Assert.EndsWith(Path.Combine(".cache", "deps"), resolved);
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(resolved)!)!, "sdv-test-framework.slnx")));
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    private string CreateMod(string folderName, string uniqueId, string version)
    {
        var path = Path.Combine(_root, folderName);
        Directory.CreateDirectory(path);
        WriteManifest(path, uniqueId, version);
        return path;
    }

    private static void WriteManifest(string directory, string uniqueId, string version)
        => File.WriteAllText(
            Path.Combine(directory, "manifest.json"),
            $$"""{"Name":"Test","UniqueID":"{{uniqueId}}","Version":"{{version}}","EntryDll":"Test.dll"}""");

    private static IReadOnlyDictionary<string, string?> Env(string cacheRoot)
        => new Dictionary<string, string?> { [RepoDependencyCache.CacheEnvironmentVariable] = cacheRoot };

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "sdv-repo-deps-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
