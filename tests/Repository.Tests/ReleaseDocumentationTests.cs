using System.IO;
using Xunit;

namespace SdvTestFramework.Repository.Tests;

public sealed class ReleaseDocumentationTests
{
    private static readonly string RepoRoot = RepositoryTestPaths.FindRepoRoot();

    [Theory]
    [InlineData("README.md")]
    [InlineData("docs/developer-setup.md")]
    [InlineData("docs/wiki/index.md")]
    public void Release_docs_distinguish_public_ci_from_game_backed_release_packaging(string relativePath)
    {
        var path = Path.Combine(RepoRoot, relativePath);
        Assert.True(File.Exists(path), path);

        var doc = File.ReadAllText(path);
        Assert.Contains("scripts/ci-public.sh", doc);
        Assert.Contains("FROBBY_GAME_PATH", doc);
        Assert.Contains("Pathoschild.Stardew.ModBuildConfig", doc);
        Assert.Contains("public hosted", doc);
        Assert.Contains("real Stardew", doc);
    }
}
