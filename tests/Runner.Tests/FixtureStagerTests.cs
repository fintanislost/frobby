using System;
using System.IO;
using SdvTestFramework.Protocol.Models;
using SdvTestFramework.Runner.Fixtures;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class FixtureStagerTests
{
    private static string MakeTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"stager-{System.Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Stage_CopiesSaveDirRecursively()
    {
        var fixturesRoot = MakeTempDir();
        var sdvSaves = MakeTempDir();
        try
        {
            // Seed: fixturesRoot/myfix/save/ with two files
            var src = Path.Combine(fixturesRoot, "myfix", "save");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "SaveGameInfo"), "<info/>");
            File.WriteAllText(Path.Combine(src, "myfix"), "savedata");

            FixtureStager.Stage("myfix", fixturesRoot, sdvSaves);

            var dst = Path.Combine(sdvSaves, "myfix");
            Assert.True(Directory.Exists(dst));
            Assert.Equal("<info/>", File.ReadAllText(Path.Combine(dst, "SaveGameInfo")));
            Assert.Equal("savedata", File.ReadAllText(Path.Combine(dst, "myfix")));
        }
        finally
        {
            Directory.Delete(fixturesRoot, recursive: true);
            Directory.Delete(sdvSaves, recursive: true);
        }
    }

    [Fact]
    public void Stage_OverwritesExistingTarget()
    {
        var fixturesRoot = MakeTempDir();
        var sdvSaves = MakeTempDir();
        try
        {
            var src = Path.Combine(fixturesRoot, "myfix", "save");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "SaveGameInfo"), "new");

            // Pre-existing target with stale content + extra file
            var dst = Path.Combine(sdvSaves, "myfix");
            Directory.CreateDirectory(dst);
            File.WriteAllText(Path.Combine(dst, "SaveGameInfo"), "stale");
            File.WriteAllText(Path.Combine(dst, "orphan"), "x");

            FixtureStager.Stage("myfix", fixturesRoot, sdvSaves);

            Assert.Equal("new", File.ReadAllText(Path.Combine(dst, "SaveGameInfo")));
            // orphan file should be gone — stager does delete-and-replace
            Assert.False(File.Exists(Path.Combine(dst, "orphan")));
        }
        finally
        {
            Directory.Delete(fixturesRoot, recursive: true);
            Directory.Delete(sdvSaves, recursive: true);
        }
    }

    [Fact]
    public void Stage_MissingSource_Throws()
    {
        var fixturesRoot = MakeTempDir();
        var sdvSaves = MakeTempDir();
        try
        {
            Assert.Throws<DirectoryNotFoundException>(
                () => FixtureStager.Stage("nope", fixturesRoot, sdvSaves));
        }
        finally
        {
            Directory.Delete(fixturesRoot, recursive: true);
            Directory.Delete(sdvSaves, recursive: true);
        }
    }

    [Fact]
    public void Stage_WithFarmTypeOverride_MutatesOnlyStagedCopy()
    {
        var fixturesRoot = MakeTempDir();
        var sdvSaves = MakeTempDir();
        try
        {
            var src = Path.Combine(fixturesRoot, "myfix", "save");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "SaveGameInfo"), "<info/>");
            File.WriteAllText(Path.Combine(src, "myfix"), "<SaveGame><whichFarm>0</whichFarm></SaveGame>");

            var stagedName = FixtureStager.Stage(
                "myfix",
                fixturesRoot,
                sdvSaves,
                FrontierOverride(),
                stagedName: "myfix__frontier");

            Assert.Equal("myfix__frontier", stagedName);
            Assert.Equal(
                "<SaveGame><whichFarm>0</whichFarm></SaveGame>",
                File.ReadAllText(Path.Combine(src, "myfix")));

            var dst = Path.Combine(sdvSaves, "myfix__frontier");
            Assert.Equal("<info/>", File.ReadAllText(Path.Combine(dst, "SaveGameInfo")));
            Assert.False(File.Exists(Path.Combine(dst, "myfix")));
            Assert.Contains(
                "<whichFarm>FrontierFarm</whichFarm>",
                File.ReadAllText(Path.Combine(dst, "myfix__frontier")));
        }
        finally
        {
            Directory.Delete(fixturesRoot, recursive: true);
            Directory.Delete(sdvSaves, recursive: true);
        }
    }

    [Fact]
    public void Stage_WithFarmTypeOverrideMissingWhichFarm_ThrowsClearError()
    {
        var fixturesRoot = MakeTempDir();
        var sdvSaves = MakeTempDir();
        try
        {
            var src = Path.Combine(fixturesRoot, "myfix", "save");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "SaveGameInfo"), "<info/>");
            File.WriteAllText(Path.Combine(src, "myfix"), "<SaveGame></SaveGame>");

            var ex = Assert.Throws<InvalidOperationException>(
                () => FixtureStager.Stage("myfix", fixturesRoot, sdvSaves, FrontierOverride()));

            Assert.Contains("whichFarm", ex.Message);
        }
        finally
        {
            Directory.Delete(fixturesRoot, recursive: true);
            Directory.Delete(sdvSaves, recursive: true);
        }
    }

    [Fact]
    public void ScenarioFixtureStageName_DerivesStableNameForOverrides()
    {
        var first = ScenarioFixtureStageName.For("m0spike_436515781", FrontierOverride());
        var second = ScenarioFixtureStageName.For("m0spike_436515781", FrontierOverride());

        Assert.Equal(first, second);
        Assert.StartsWith("m0spike_436515781__frobby_", first);
        Assert.NotEqual("m0spike_436515781", first);
    }

    [Fact]
    public void ScenarioFixtureStageName_UsesOriginalNameWithoutOverrides()
    {
        Assert.Equal("myfix", ScenarioFixtureStageName.For("myfix", null));
        Assert.Equal("myfix", ScenarioFixtureStageName.For("myfix", new ScenarioSaveOverrides()));
    }

    [Fact]
    public void Capture_CopiesFromSdvSavesToFixturesRoot()
    {
        // Inverse of Stage — used by FixtureBuilder after fixture.save succeeds.
        var fixturesRoot = MakeTempDir();
        var sdvSaves = MakeTempDir();
        try
        {
            var src = Path.Combine(sdvSaves, "newfix");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "SaveGameInfo"), "captured");
            File.WriteAllText(Path.Combine(src, "newfix"), "data");

            FixtureStager.Capture("newfix", sdvSaves, fixturesRoot);

            var dst = Path.Combine(fixturesRoot, "newfix", "save");
            Assert.True(Directory.Exists(dst));
            Assert.Equal("captured", File.ReadAllText(Path.Combine(dst, "SaveGameInfo")));
        }
        finally
        {
            Directory.Delete(fixturesRoot, recursive: true);
            Directory.Delete(sdvSaves, recursive: true);
        }
    }

    private static ScenarioSaveOverrides FrontierOverride()
    {
        return new ScenarioSaveOverrides
        {
            FarmType = new ScenarioFarmTypeSaveOverride
            {
                WhichFarm = "mod",
                ModFarmId = "FrontierFarm",
            },
        };
    }
}
