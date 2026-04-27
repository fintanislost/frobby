using System;
using SdvTestFramework.Runner.Bitmap;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Bitmap;

public class SsimDiffTests
{
    // Build a 64×64 gradient as a deterministic reference image. Pixel (x, y) gets
    // R=x*4 mod 256, G=y*4 mod 256, B=(x+y)*2 mod 256.
    private static Image<Rgba32> Gradient(int w = 64, int h = 64)
    {
        var img = new Image<Rgba32>(w, h);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            img[x, y] = new Rgba32((byte)((x * 4) % 256), (byte)((y * 4) % 256), (byte)(((x + y) * 2) % 256), 255);
        return img;
    }

    // Same gradient with +/- 2 LSB noise from a deterministic seed — roughly 1% perturbation.
    private static Image<Rgba32> GradientWithNoise(int seed = 123)
    {
        var img = Gradient();
        var rng = new Random(seed);
        for (int y = 0; y < img.Height; y++)
        for (int x = 0; x < img.Width; x++)
        {
            var p = img[x, y];
            byte Clamp(int v) => (byte)Math.Clamp(v, 0, 255);
            img[x, y] = new Rgba32(
                Clamp(p.R + rng.Next(-2, 3)),
                Clamp(p.G + rng.Next(-2, 3)),
                Clamp(p.B + rng.Next(-2, 3)),
                255);
        }
        return img;
    }

    [Fact]
    public void IdenticalImages_ReturnsOne()
    {
        using var a = Gradient();
        using var b = Gradient();
        var result = SsimDiff.Compute(a, b);
        // Identical inputs → exactly 1.0 up to float precision.
        Assert.InRange(result.Score, 0.999, 1.0 + 1e-6);
    }

    [Fact]
    public void SlightlyPerturbedImages_ReturnsHighScore()
    {
        using var a = Gradient();
        using var b = GradientWithNoise();
        var result = SsimDiff.Compute(a, b);
        // Small perturbations (±2 LSB) should land well above the 0.95 tolerance floor.
        Assert.InRange(result.Score, 0.95, 1.0);
    }

    [Fact]
    public void DifferentDimensions_Throws()
    {
        using var a = Gradient(64, 64);
        using var b = Gradient(32, 32);
        var ex = Assert.Throws<ArgumentException>(() => SsimDiff.Compute(a, b));
        Assert.Contains("64", ex.Message);
        Assert.Contains("32", ex.Message);
        Assert.Contains("mismatch", ex.Message);
    }
}
