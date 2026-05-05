using System;
using System.Collections.Generic;
using System.IO;
using SdvTestFramework.Runner.Repo;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Repo;

public sealed class RepoPathResolverTests : IDisposable
{
    private readonly string _repoRoot = CreateTempDirectory();

    [Fact]
    public void Resolve_handles_repo_relative_paths_and_preserves_spaces()
    {
        var path = Path.Combine(_repoRoot, "mods", "Content Patcher");
        Directory.CreateDirectory(path);

        var resolved = RepoPathResolver.Resolve(_repoRoot, "mods/Content Patcher");

        Assert.Equal(Path.GetFullPath(path), resolved);
    }

    [Fact]
    public void Resolve_handles_absolute_paths()
    {
        var path = Path.Combine(_repoRoot, "absolute-file.txt");
        File.WriteAllText(path, "");

        var resolved = RepoPathResolver.Resolve(_repoRoot, path);

        Assert.Equal(Path.GetFullPath(path), resolved);
    }

    [Fact]
    public void Resolve_expands_home_from_supplied_environment()
    {
        var home = Path.Combine(_repoRoot, "home dir");
        var path = Path.Combine(home, "mods", "extra");
        Directory.CreateDirectory(path);
        var environment = new Dictionary<string, string?> { ["HOME"] = home };

        var resolved = RepoPathResolver.Resolve(_repoRoot, "~/mods/extra", environment);

        Assert.Equal(Path.GetFullPath(path), resolved);
    }

    [Theory]
    [InlineData("$MOD_ROOT/Extra Mod")]
    [InlineData("${MOD_ROOT}/Extra Mod")]
    public void Resolve_expands_environment_variables(string rawPath)
    {
        var modRoot = Path.Combine(_repoRoot, "env root");
        var path = Path.Combine(modRoot, "Extra Mod");
        Directory.CreateDirectory(path);
        var environment = new Dictionary<string, string?> { ["MOD_ROOT"] = modRoot };

        var resolved = RepoPathResolver.Resolve(_repoRoot, rawPath, environment);

        Assert.Equal(Path.GetFullPath(path), resolved);
    }

    [Fact]
    public void Resolve_missing_environment_variable_throws_actionable_error()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => RepoPathResolver.Resolve(_repoRoot, "$MISSING_MOD/Extra", new Dictionary<string, string?>()));

        Assert.Contains("MISSING_MOD", ex.Message);
        Assert.Contains("$MISSING_MOD/Extra", ex.Message);
    }

    [Fact]
    public void Resolve_required_missing_path_throws_directory_not_found()
    {
        var ex = Assert.Throws<DirectoryNotFoundException>(
            () => RepoPathResolver.Resolve(_repoRoot, "missing/path"));

        Assert.Contains("missing/path", ex.Message);
    }

    [Fact]
    public void Resolve_can_skip_existence_check()
    {
        var resolved = RepoPathResolver.Resolve(_repoRoot, "missing/path", requireExists: false);

        Assert.Equal(Path.GetFullPath(Path.Combine(_repoRoot, "missing/path")), resolved);
    }

    public void Dispose()
    {
        Directory.Delete(_repoRoot, recursive: true);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "sdv-repo-path-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
