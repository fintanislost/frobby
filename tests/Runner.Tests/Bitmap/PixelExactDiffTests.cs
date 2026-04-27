using SdvTestFramework.Runner.Bitmap;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Bitmap;

public class PixelExactDiffTests
{
    private static Image<Rgba32> Solid(int w, int h, byte r, byte g, byte b)
    {
        var img = new Image<Rgba32>(w, h);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            img[x, y] = new Rgba32(r, g, b, 255);
        return img;
    }

    [Fact]
    public void IdenticalImages_ReturnsZero()
    {
        using var a = Solid(8, 8, 100, 100, 100);
        using var b = Solid(8, 8, 100, 100, 100);
        Assert.Equal(0, PixelExactDiff.MaxChannelDelta(a, b));
    }

    [Fact]
    public void OffByOneChannel_ReturnsOne()
    {
        using var a = Solid(8, 8, 100, 100, 100);
        using var b = Solid(8, 8, 101, 100, 100);
        Assert.Equal(1, PixelExactDiff.MaxChannelDelta(a, b));
    }

    [Fact]
    public void MaxChannelDeltaAcrossPixels_ReturnsLargestSingleDelta()
    {
        using var a = Solid(8, 8, 100, 100, 100);
        using var b = Solid(8, 8, 100, 100, 100);
        // Spike one pixel: R goes from 100 -> 200, G unchanged, B unchanged.
        b[3, 4] = new Rgba32(200, 100, 100, 255);
        Assert.Equal(100, PixelExactDiff.MaxChannelDelta(a, b));
    }
}
