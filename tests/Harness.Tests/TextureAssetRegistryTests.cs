using Microsoft.Xna.Framework.Graphics;
using SdvTestFramework.Harness.Assets;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class TextureAssetRegistryTests
{
    [Fact]
    public void TryResolve_UnregisteredTexture_ReturnsNull()
    {
        var reg = new TextureAssetRegistry();
        Texture2D? tex = null;
        Assert.Null(reg.TryResolve(tex));
    }

    [Fact]
    public void Register_ThenResolve_ReturnsAssetName()
    {
        var reg = new TextureAssetRegistry();
        var shim = new object();
        reg.RegisterShim(shim, "Characters/Abigail");
        Assert.Equal("Characters/Abigail", reg.TryResolveShim(shim));
    }

    [Fact]
    public void Register_NullTexture_NoOp()
    {
        var reg = new TextureAssetRegistry();
        reg.Register(null, "whatever");  // must not throw
        Assert.Null(reg.TryResolve(null));
    }

    [Fact]
    public void Register_Twice_OverwritesAssetName()
    {
        var reg = new TextureAssetRegistry();
        var shim = new object();
        reg.RegisterShim(shim, "first");
        reg.RegisterShim(shim, "second");
        Assert.Equal("second", reg.TryResolveShim(shim));
    }
}
