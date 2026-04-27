using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Commands;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class BuildManifestCommandTests
{
    [Fact]
    public async Task UnknownFlag_ReturnsTwo()
    {
        var code = await BuildManifestCommand.RunAsync(
            new[] { "--nope" }.AsMemory(), CancellationToken.None);
        Assert.Equal(2, code);
    }

    [Fact]
    public void ResolveOutputPath_NoExplicit_UsesCacheDir()
    {
        var path = BuildManifestCommand.ResolveOutputPath(explicitPath: null, sdvVersion: "1.6.15");
        Assert.Contains(".cache/sdv-test-framework/texture-manifests", path);
        Assert.EndsWith("1.6.15.json", path);
    }
}
