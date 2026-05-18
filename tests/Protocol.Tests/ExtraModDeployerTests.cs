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
    public void DeployMany_CleanUnlisted_RemovesPreviouslyManagedMod()
    {
        var root = Path.Combine(Path.GetTempPath(), $"extra-mod-{Guid.NewGuid():N}");
        var mods = Path.Combine(root, "mods");
        var first = Path.Combine(root, "first");
        var second = Path.Combine(root, "second");
        Directory.CreateDirectory(mods);
        WriteMod(first, "Example.First");
        WriteMod(second, "Example.Second");
        try
        {
            ExtraModDeployer.DeployMany(mods, new[] { first });

            ExtraModDeployer.DeployMany(mods, new[] { second }, cleanUnlisted: true);

            Assert.False(Directory.Exists(Path.Combine(mods, "Example.First")));
            Assert.True(Directory.Exists(Path.Combine(mods, "Example.Second")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DeployMany_CleanUnlisted_AggressiveRemovesUnmarkedStaleMod()
    {
        var root = Path.Combine(Path.GetTempPath(), $"extra-mod-{Guid.NewGuid():N}");
        var mods = Path.Combine(root, "mods");
        var current = Path.Combine(root, "current");
        var stale = Path.Combine(mods, "Example.Stale");
        Directory.CreateDirectory(mods);
        WriteMod(current, "Example.Current");
        WriteMod(stale, "Example.Stale");
        try
        {
            ExtraModDeployer.DeployMany(
                mods,
                new[] { current },
                cleanUnlisted: true,
                cleanUnmarked: true);

            Assert.False(Directory.Exists(stale));
            Assert.True(Directory.Exists(Path.Combine(mods, "Example.Current")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DeployMany_CleanUnlisted_NonAggressiveKeepsUnmarkedMod()
    {
        var root = Path.Combine(Path.GetTempPath(), $"extra-mod-{Guid.NewGuid():N}");
        var mods = Path.Combine(root, "mods");
        var current = Path.Combine(root, "current");
        var unmanaged = Path.Combine(mods, "Example.Unmanaged");
        Directory.CreateDirectory(mods);
        WriteMod(current, "Example.Current");
        WriteMod(unmanaged, "Example.Unmanaged");
        try
        {
            ExtraModDeployer.DeployMany(mods, new[] { current }, cleanUnlisted: true);

            Assert.True(Directory.Exists(unmanaged));
            Assert.True(Directory.Exists(Path.Combine(mods, "Example.Current")));
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

    [Fact]
    public void ApplyConfigOverlays_CopiesSourceIntoDeployedMod()
    {
        var root = Path.Combine(Path.GetTempPath(), $"extra-mod-overlay-{Guid.NewGuid():N}");
        var mods = Path.Combine(root, "mods");
        var targetMod = Path.Combine(mods, "Example.Mod");
        var source = Path.Combine(root, "overlay.json");
        Directory.CreateDirectory(targetMod);
        try
        {
            File.WriteAllText(source, "{\"enabled\":true}");
            var lastWrite = new DateTime(2026, 5, 14, 12, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(source, lastWrite);

            ExtraModDeployer.ApplyConfigOverlays(
                mods,
                new[]
                {
                    new ExtraModConfigOverlay(source, "Example.Mod", "config/settings.json"),
                });

            var target = Path.Combine(targetMod, "config", "settings.json");
            Assert.True(File.Exists(target));
            Assert.Equal("{\"enabled\":true}", File.ReadAllText(target));
            Assert.Equal(lastWrite, File.GetLastWriteTimeUtc(target));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("../escape.json")]
    [InlineData("..\\escape.json")]
    [InlineData("/escape.json")]
    [InlineData("\\escape.json")]
    [InlineData("C:\\escape.json")]
    [InlineData("config/./escape.json")]
    [InlineData("config\\..\\escape.json")]
    public void ApplyConfigOverlays_RejectsTargetPathsOutsideMod(string targetPath)
    {
        var root = Path.Combine(Path.GetTempPath(), $"extra-mod-overlay-bad-{Guid.NewGuid():N}");
        var mods = Path.Combine(root, "mods");
        var targetMod = Path.Combine(mods, "Example.Mod");
        var source = Path.Combine(root, "overlay.json");
        Directory.CreateDirectory(targetMod);
        try
        {
            File.WriteAllText(source, "{}");

            var ex = Assert.Throws<InvalidOperationException>(() => ExtraModDeployer.ApplyConfigOverlays(
                mods,
                new[]
                {
                    new ExtraModConfigOverlay(source, "Example.Mod", targetPath),
                }));

            Assert.Contains("overlay target must stay inside deployed mod", ex.Message);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../Example.Mod")]
    [InlineData("C:\\Example.Mod")]
    public void ApplyConfigOverlays_RejectsUnsafeTargetModIds(string targetModId)
    {
        var root = Path.Combine(Path.GetTempPath(), $"extra-mod-overlay-bad-id-{Guid.NewGuid():N}");
        var mods = Path.Combine(root, "mods");
        var source = Path.Combine(root, "overlay.json");
        Directory.CreateDirectory(mods);
        try
        {
            File.WriteAllText(source, "{}");

            var ex = Assert.Throws<InvalidOperationException>(() => ExtraModDeployer.ApplyConfigOverlays(
                mods,
                new[]
                {
                    new ExtraModConfigOverlay(source, targetModId, "config.json"),
                }));

            Assert.Contains("overlay target mod id is not valid", ex.Message);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplyConfigOverlays_MissingTargetModThrowsClearError()
    {
        var root = Path.Combine(Path.GetTempPath(), $"extra-mod-overlay-missing-{Guid.NewGuid():N}");
        var mods = Path.Combine(root, "mods");
        var source = Path.Combine(root, "overlay.json");
        Directory.CreateDirectory(mods);
        try
        {
            File.WriteAllText(source, "{}");

            var ex = Assert.Throws<DirectoryNotFoundException>(() => ExtraModDeployer.ApplyConfigOverlays(
                mods,
                new[]
                {
                    new ExtraModConfigOverlay(source, "Example.Missing", "config.json"),
                }));

            Assert.Contains("Example.Missing", ex.Message);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteMod(string path, string uniqueId)
    {
        Directory.CreateDirectory(path);
        File.WriteAllText(
            Path.Combine(path, "manifest.json"),
            $$"""{"Name":"{{uniqueId}}","UniqueID":"{{uniqueId}}","EntryDll":"{{uniqueId}}.dll"}""");
        File.WriteAllText(Path.Combine(path, uniqueId + ".dll"), "not a real dll");
    }
}
