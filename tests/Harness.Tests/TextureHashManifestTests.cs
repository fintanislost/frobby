using System;
using System.IO;
using SdvTestFramework.Harness.Assets;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class TextureHashManifestTests
{
    [Fact]
    public void Load_MissingFile_ReturnsEmptyManifest()
    {
        var m = TextureHashManifest.Load("/tmp/definitely-does-not-exist.json");
        Assert.Equal(0, m.Count);
        Assert.Null(m.TryResolve("a1b2c3d4e5f6a789"));
    }

    [Fact]
    public void Load_ValidJson_ResolvesHashToPath()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mf-{Guid.NewGuid():N}.json");
        File.WriteAllText(tmp,
            "{\"sdv_version\":\"1.6.15\",\"texture_count\":2," +
             "\"manifest\":{\"a1b2c3d4e5f6a789\":\"Characters/Abigail\",\"deadbeefcafef00d\":\"LooseSprites/Cursors\"}}");
        try
        {
            var m = TextureHashManifest.Load(tmp);
            Assert.Equal(2, m.Count);
            Assert.Equal("Characters/Abigail", m.TryResolve("a1b2c3d4e5f6a789"));
            Assert.Equal("LooseSprites/Cursors", m.TryResolve("deadbeefcafef00d"));
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void TryResolve_UnknownHash_ReturnsNull()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mf-{Guid.NewGuid():N}.json");
        File.WriteAllText(tmp, "{\"sdv_version\":\"1.6.15\",\"texture_count\":0,\"manifest\":{}}");
        try
        {
            var m = TextureHashManifest.Load(tmp);
            Assert.Null(m.TryResolve("0000000000000000"));
        }
        finally { File.Delete(tmp); }
    }
}
