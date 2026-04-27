using SdvTestFramework.Harness.Assets;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class TextureHasherTests
{
    [Fact]
    public void ComputeHashFromBytes_SameData_ReturnsSameHash()
    {
        var data = new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80 };
        var h1 = TextureHasher.ComputeHashHexPrefix(data);
        var h2 = TextureHasher.ComputeHashHexPrefix(data);
        Assert.Equal(h1, h2);
        Assert.Equal(16, h1.Length);
    }

    [Fact]
    public void ComputeHashFromBytes_DifferentData_ReturnsDifferentHash()
    {
        var a = new byte[] { 0x10, 0x20, 0x30, 0x40 };
        var b = new byte[] { 0x10, 0x20, 0x30, 0x41 };
        Assert.NotEqual(
            TextureHasher.ComputeHashHexPrefix(a),
            TextureHasher.ComputeHashHexPrefix(b));
    }
}
