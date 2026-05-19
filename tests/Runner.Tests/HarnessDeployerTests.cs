using System;
using System.IO;
using System.Linq;
using SdvTestFramework.Protocol;
using SdvTestFramework.Runner.Commands;
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

    [Fact]
    public void Deploy_CopiesHarnessRuntimeDependenciesWithoutRunnerOnlySchemaPackages()
    {
        var mods = Path.Combine(Path.GetTempPath(), $"deploy-deps-{Guid.NewGuid():N}");
        Directory.CreateDirectory(mods);
        try
        {
            HarnessDeployer.Deploy(mods);
            var target = Path.Combine(mods, "SdvTestFramework.Harness");

            Assert.True(
                File.Exists(Path.Combine(target, "SixLabors.ImageSharp.dll")),
                "expected deployed runtime dependency SixLabors.ImageSharp.dll");

            foreach (var file in new[]
            {
                "Humanizer.dll",
                "Json.More.dll",
                "JsonPointer.Net.dll",
                "JsonSchema.Net.dll",
            })
            {
                Assert.False(File.Exists(Path.Combine(target, file)), $"did not expect runner-only schema package {file}");
            }
        }
        finally { Directory.Delete(mods, recursive: true); }
    }

    [Fact]
    public void Deploy_RemovesStaleFilesNotInCurrentPayload()
    {
        var mods = Path.Combine(Path.GetTempPath(), $"deploy-stale-{Guid.NewGuid():N}");
        var target = Path.Combine(mods, "SdvTestFramework.Harness");
        Directory.CreateDirectory(target);
        try
        {
            File.WriteAllText(Path.Combine(target, "JsonSchema.Net.dll"), "stale");

            HarnessDeployer.Deploy(mods);

            Assert.False(
                File.Exists(Path.Combine(target, "JsonSchema.Net.dll")),
                "expected stale schema package from a prior payload to be removed");
        }
        finally { Directory.Delete(mods, recursive: true); }
    }

    [Fact]
    public void RunnerAssembly_EmbedsHarnessRuntimeDependenciesWithoutRunnerOnlySchemaPackages()
    {
        var resources = typeof(RunCommand)
            .Assembly
            .GetManifestResourceNames()
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("harness/SixLabors.ImageSharp.dll", resources);

        foreach (var resource in new[]
        {
            "harness/Humanizer.dll",
            "harness/Json.More.dll",
            "harness/JsonPointer.Net.dll",
            "harness/JsonSchema.Net.dll",
        })
        {
            Assert.DoesNotContain(resource, resources);
        }
    }
}
