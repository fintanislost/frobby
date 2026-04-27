using System;
using SdvTestFramework.Runner.Bitmap;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Bitmap;

public class DHashDiffTests
{
    // Same gradient pattern used by SsimDiffTests — deterministic 64×64 RGB.
    private static Image<Rgba32> Gradient(int seed = 0)
    {
        var img = new Image<Rgba32>(64, 64);
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
            img[x, y] = new Rgba32(
                (byte)((x * 4 + seed) % 256),
                (byte)((y * 4 + seed) % 256),
                (byte)(((x + y) * 2 + seed) % 256),
                255);
        return img;
    }

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

    private static Image<Rgba32> Inverted()
    {
        var img = Gradient();
        for (int y = 0; y < img.Height; y++)
        for (int x = 0; x < img.Width; x++)
        {
            var p = img[x, y];
            img[x, y] = new Rgba32((byte)(255 - p.R), (byte)(255 - p.G), (byte)(255 - p.B), 255);
        }
        return img;
    }

    [Fact]
    public void IdenticalImages_HammingDistanceZero()
    {
        using var a = Gradient();
        using var b = Gradient();
        Assert.Equal(0, DHashDiff.HammingDistance(a, b));
    }

    [Fact]
    public void MinorNoise_HammingDistanceLowSingleDigit()
    {
        using var a = Gradient();
        using var b = GradientWithNoise(seed: 123);
        var d = DHashDiff.HammingDistance(a, b);
        // ±2 LSB noise in RGB → grayscale conversion + 9×8 resize smooths it out;
        // expect very few bit flips. Threshold 5 matches the dHash defaults.
        Assert.InRange(d, 0, 5);
    }

    [Fact]
    public void Inverted_HammingDistanceHigh()
    {
        using var a = Gradient();
        using var b = Inverted();
        var d = DHashDiff.HammingDistance(a, b);
        // Inversion flips luminance → most adjacent-pair comparisons reverse direction
        // → many bit flips. Expect distance well above the "vaguely similar" threshold.
        Assert.True(d >= 30, $"expected >=30 bit flips for inverted gradient, got {d}");
    }
}
