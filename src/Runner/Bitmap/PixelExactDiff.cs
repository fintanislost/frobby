using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SdvTestFramework.Runner.Bitmap;

/// <summary>
/// Pixel-exact diff. Returns the maximum per-channel RGB delta across all pixels.
/// Alpha is ignored (consistent with <see cref="SsimDiff"/>). 0 = bit-identical RGB.
/// </summary>
public static class PixelExactDiff
{
    /// <summary>
    /// Compute max per-channel delta. Both images must share dimensions; otherwise throws
    /// <see cref="ArgumentException"/> with the mismatch shape in the message (matches
    /// <see cref="SsimDiff"/>).
    /// </summary>
    public static int MaxChannelDelta(Image<Rgba32> a, Image<Rgba32> b)
    {
        if (a.Width != b.Width || a.Height != b.Height)
            throw new ArgumentException(
                $"pixel-exact dim mismatch: {a.Width}×{a.Height} vs {b.Width}×{b.Height}");

        int max = 0;
        int w = a.Width, h = a.Height;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var pa = a[x, y];
            var pb = b[x, y];
            int dr = Math.Abs(pa.R - pb.R);
            int dg = Math.Abs(pa.G - pb.G);
            int db = Math.Abs(pa.B - pb.B);
            int local = Math.Max(dr, Math.Max(dg, db));
            if (local > max) max = local;
        }
        return max;
    }
}
