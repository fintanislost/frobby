using System;
using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SdvTestFramework.Runner.Bitmap;

/// <summary>
/// Difference-hash perceptual hash. Resizes to 9×8 grayscale, packs 64 bits where each
/// bit indicates whether the left pixel of an adjacent horizontal pair is darker than
/// the right. Hamming distance between two hashes ≈ how perceptually different the images
/// are. Range [0, 64]; ≤5 is "looks the same", &gt;10 is "clearly different".
/// </summary>
/// <remarks>
/// Luminance via Rec. 601: <c>Y = 0.299·R + 0.587·G + 0.114·B</c>. Alpha ignored.
/// Resize uses bicubic to smooth high-frequency noise. Standard difference-hash
/// algorithm — independent of image dimensions, so no dim-mismatch precondition.
/// </remarks>
public static class DHashDiff
{
    private const int W = 9;
    private const int H = 8;

    /// <summary>Compute the 64-bit dHash for an image.</summary>
    public static ulong Compute(Image<Rgba32> img)
    {
        // Clone + resize to 9×8 (9 cols, 8 rows).
        using var small = img.Clone(ctx => ctx.Resize(W, H, KnownResamplers.Bicubic));

        // Build per-pixel grayscale grid in row-major order.
        var luma = new double[H, W];
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            var p = small[x, y];
            luma[y, x] = 0.299 * p.R + 0.587 * p.G + 0.114 * p.B;
        }

        // Compare adjacent pairs per row. 8 pairs × 8 rows = 64 bits.
        ulong hash = 0;
        int bit = 0;
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W - 1; x++)
        {
            if (luma[y, x] < luma[y, x + 1])
                hash |= 1UL << bit;
            bit++;
        }
        return hash;
    }

    /// <summary>Hamming distance between two image hashes (popcount of XOR).</summary>
    public static int HammingDistance(Image<Rgba32> a, Image<Rgba32> b)
    {
        var ha = Compute(a);
        var hb = Compute(b);
        return BitOperations.PopCount(ha ^ hb);
    }
}
