using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SdvTestFramework.Runner.Bitmap;

/// <summary>
/// Structural Similarity Index (SSIM) over 8×8 non-overlapping grayscale blocks.
/// Returns a float in [0, 1] where 1 = identical. Constants follow the standard
/// 8-bit formulation: C1 = (0.01·L)² and C2 = (0.03·L)² with L = 255.
/// </summary>
/// <remarks>
/// Luminance conversion uses Rec. 601: <c>Y = 0.299·R + 0.587·G + 0.114·B</c>.
/// Alpha is ignored. Edge pixels outside a full 8×8 tile are skipped (the score
/// averages across full blocks only). Hand-rolled so we don't pull in a heavy
/// SSIM-specific NuGet on top of ImageSharp.
/// Variance and covariance use the population estimator (divides by n, not n-1),
/// matching the standard block-SSIM formulation. Scikit-image defaults to sample
/// covariance — expect small numeric differences against that reference.
/// </remarks>
public static class SsimDiff
{
    private const int Block = 8;
    private const double L = 255.0;
    private const double C1 = (0.01 * L) * (0.01 * L);   // 6.5025
    private const double C2 = (0.03 * L) * (0.03 * L);   // 58.5225

    /// <summary>
    /// Compute SSIM between two images. Both must be same dimensions; otherwise throws
    /// <see cref="ArgumentException"/> with the mismatch shape in the message.
    /// </summary>
    public static SsimResult Compute(Image<Rgba32> a, Image<Rgba32> b)
    {
        if (a.Width != b.Width || a.Height != b.Height)
            throw new ArgumentException(
                $"SSIM dim mismatch: {a.Width}×{a.Height} vs {b.Width}×{b.Height}");

        int w = a.Width, h = a.Height;
        int blocksX = w / Block;
        int blocksY = h / Block;
        if (blocksX == 0 || blocksY == 0)
            throw new ArgumentException(
                $"SSIM requires at least one 8×8 block (got {w}×{h})");

        var grid = new float[blocksY, blocksX];
        double sum = 0;
        for (int by = 0; by < blocksY; by++)
        for (int bx = 0; bx < blocksX; bx++)
        {
            var s = (float)BlockSsim(a, b, bx * Block, by * Block);
            grid[by, bx] = s;
            sum += s;
        }

        return new SsimResult(
            Score: (float)(sum / (blocksX * blocksY)),
            BlockScores: grid,
            BlocksX: blocksX,
            BlocksY: blocksY);
    }

    private static double BlockSsim(Image<Rgba32> a, Image<Rgba32> b, int x0, int y0)
    {
        // First pass: means.
        double sumX = 0, sumY = 0;
        for (int y = 0; y < Block; y++)
        for (int x = 0; x < Block; x++)
        {
            sumX += Luma(a[x0 + x, y0 + y]);
            sumY += Luma(b[x0 + x, y0 + y]);
        }
        const int n = Block * Block;
        double muX = sumX / n;
        double muY = sumY / n;

        // Second pass: variances + covariance.
        double varX = 0, varY = 0, covXY = 0;
        for (int y = 0; y < Block; y++)
        for (int x = 0; x < Block; x++)
        {
            double lx = Luma(a[x0 + x, y0 + y]) - muX;
            double ly = Luma(b[x0 + x, y0 + y]) - muY;
            varX += lx * lx;
            varY += ly * ly;
            covXY += lx * ly;
        }
        varX /= n;
        varY /= n;
        covXY /= n;

        double num = (2 * muX * muY + C1) * (2 * covXY + C2);
        double den = (muX * muX + muY * muY + C1) * (varX + varY + C2);
        return num / den;
    }

    private static double Luma(Rgba32 p) =>
        0.299 * p.R + 0.587 * p.G + 0.114 * p.B;
}
