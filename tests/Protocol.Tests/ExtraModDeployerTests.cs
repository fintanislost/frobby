using System;
using System.IO;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class ExtraModDeployerTests
{
    [Fact]
    public void Deploy_CopyDirectoryWithManifest_UsesUniqueIdAsFolderName()
    {
        var root = Path.Combine(Path.GetTempPath(), $"extra-mod-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "source");
        var mods = Path.Combine(root, "mods");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(mods);
        try
        {
            File.WriteAllText(
                Path.Combine(source, "manifest.json"),
                "{\"Name\":\"Probe\",\"UniqueID\":\"Example.Probe\",\"EntryDll\":\"Probe.dll\"}");
            File.WriteAllText(Path.Combine(source, "Probe.dll"), "not a real dll");
            Directory.CreateDirectory(Path.Combine(source, "assets"));
            File.WriteAllText(Path.Combine(source, "assets", "marker.txt"), "ok");

            var deployed = ExtraModDeployer.Deploy(mods, source);

            Assert.Equal(Path.Combine(mods, "Example.Probe"), deployed);
            Assert.True(File.Exists(Path.Combine(deployed, "manifest.json")));
            Assert.True(File.Exists(Path.Combine(deployed, "Probe.dll")));
            Assert.True(File.Exists(Path.Combine(deployed, "assets", "marker.txt")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Deploy_ExistingTarget_ReplacesOldContent()
    {
        var root = Path.Combine(Path.GetTempPath(), $"extra-mod-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "source");
        var mods = Path.Combine(root, "mods");
        var target = Path.Combine(mods, "Example.Probe");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        try
        {
            File.WriteAllText(
                Path.Combine(source, "manifest.json"),
                "{\"Name\":\"Probe\",\"UniqueID\":\"Example.Probe\",\"EntryDll\":\"Probe.dll\"}");
            File.WriteAllText(Path.Combine(source, "Probe.dll"), "new");
            File.WriteAllText(Path.Combine(target, "stale.txt"), "old");

            var deployed = ExtraModDeployer.Deploy(mods, source);

            Assert.Equal(target, deployed);
            Assert.False(File.Exists(Path.Combine(target, "stale.txt")));
            Assert.Equal("new", File.ReadAllText(Path.Combine(target, "Probe.dll")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Deploy_SourceAlreadyAtTarget_DoesNotDeleteMod()
    {
        var root = Path.Combine(Path.GetTempPath(), $"extra-mod-{Guid.NewGuid():N}");
        var mods = Path.Combine(root, "mods");
        var source = Path.Combine(mods, "Example.Probe");
        Directory.CreateDirectory(source);
        try
        {
            File.WriteAllText(
                Path.Combine(source, "manifest.json"),
                "{\"Name\":\"Probe\",\"UniqueID\":\"Example.Probe\",\"EntryDll\":\"Probe.dll\"}");
            File.WriteAllText(Path.Combine(source, "Probe.dll"), "keep");

            var deployed = ExtraModDeployer.Deploy(mods, source);

            Assert.Equal(source, deployed);
            Assert.Equal("keep", File.ReadAllText(Path.Combine(source, "Probe.dll")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ParseEnvList_SplitsOnPathSeparatorAndTrims()
    {
        var value = $" /one {Path.PathSeparator}{Path.PathSeparator}/two ";

        var paths = ExtraModDeployer.ParseEnvList(value);

        Assert.Equal(new[] { "/one", "/two" }, paths);
    }

    [Fact]
    public void Deploy_MissingManifest_ThrowsFileNotFound()
    {
        var root = Path.Combine(Path.GetTempPath(), $"extra-mod-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "source");
        var mods = Path.Combine(root, "mods");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(mods);
        try
        {
            Assert.Throws<FileNotFoundException>(() => ExtraModDeployer.Deploy(mods, source));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
