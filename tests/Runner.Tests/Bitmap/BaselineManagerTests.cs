using System;
using System.IO;
using SdvTestFramework.Runner.Bitmap;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Bitmap;

public class BaselineManagerTests
{
    [Fact]
    public void ResolveBaseline_RelativePath_ResolvesAgainstScenarioDir()
    {
        var scenarioPath = Path.Combine("/tmp", "scenarios", "11-bitmap.test.json");
        var relBaseline = Path.Combine("baselines", "shop.png");

        var resolved = BaselineManager.ResolveBaseline(scenarioPath, relBaseline);

        Assert.Equal(Path.Combine("/tmp", "scenarios", "baselines", "shop.png"), resolved);
    }

    [Fact]
    public void ResolveBaseline_AbsolutePath_ReturnsUnchanged()
    {
        var absPath = Path.Combine("/opt", "baselines", "shop.png");
        var result = BaselineManager.ResolveBaseline("/tmp/scenario.test.json", absPath);
        Assert.Equal(absPath, result);
    }

    [Fact]
    public void WriteBaseline_CreatesParentDir_WritesBytes()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"baseline-{Guid.NewGuid():N}");
        var target = Path.Combine(tmpDir, "sub", "out.png");
        try
        {
            var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };  // PNG magic
            BaselineManager.WriteBaseline(target, bytes);

            Assert.True(File.Exists(target));
            Assert.Equal(bytes, File.ReadAllBytes(target));
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
        }
    }
}
