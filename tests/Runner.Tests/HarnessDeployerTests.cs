using System;
using System.IO;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class HarnessDeployerTests
{
    [Fact]
    public void Deploy_CreatesTargetDirAndCopiesFiles()
    {
        var mods = Path.Combine(Path.GetTempPath(), $"deploy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(mods);
        try
        {
            HarnessDeployer.Deploy(mods);
            var target = Path.Combine(mods, "SdvTestFramework.Harness");
            Assert.True(Directory.Exists(target));
            Assert.True(File.Exists(Path.Combine(target, "Harness.dll")));
        }
        finally { Directory.Delete(mods, recursive: true); }
    }
}
