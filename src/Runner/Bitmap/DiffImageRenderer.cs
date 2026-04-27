using System;
using System.IO;
using SdvTestFramework.Protocol.Reports;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SdvTestFramework.Runner.Bitmap;

/// <summary>
/// Pure-function renderer producing forensics PNGs for a failed bitmap assertion.
/// Outputs (always): <c>baseline.png</c>, <c>capture.png</c>. The diff PNG is
/// method-specific:
/// <list type="bullet">
///   <item><see cref="BitmapMethod.Ssim"/> — bilinear-smoothed per-block redness heatmap.</item>
///   <item><see cref="BitmapMethod.PixelExact"/> — per-pixel redness based on max channel delta.</item>
///   <item><see cref="BitmapMethod.DHash"/> — diff PNG skipped (perceptual hash doesn't localize); <see cref="DiffSet.Diff"/> returned as empty string.</item>
/// </list>
/// Optional composite output: <c>triptych.png</c> (3-wide horizontal stitch).
/// </summary>
public static class DiffImageRenderer
{
    private const int Block = 8;

    /// <summary>
    /// Render the diff set into <paramref name="outputDir"/>. Caller is responsible for
    /// pre-creating the directory. Returns absolute paths to written files.
    /// </summary>
    /// <param name="ssim">SSIM result; must be non-null when <paramref name="method"/> is <see cref="BitmapMethod.Ssim"/>; ignored otherwise.</param>
    /// <param name="tolerance">Method-specific: SSIM 0-1 score; pixel-exact max channel delta; dHash ignored.</param>
    public static DiffSet Render(
        byte[] baselineBytes,
        byte[] captureBytes,
        SsimResult? ssim,
        double tolerance,
        BitmapMethod method,
        DiffFormat format,
        string outputDir)
    {
        // 1. Byte-for-byte copies of inputs — no re-encoding (preserves source fidelity).
        var baselinePath = Path.Combine(outputDir, "baseline.png");
        var capturePath = Path.Combine(outputDir, "capture.png");
        File.WriteAllBytes(baselinePath, baselineBytes);
        File.WriteAllBytes(capturePath, captureBytes);

        // 2. dHash skips diff entirely — perceptual hash doesn't localize per-pixel.
        if (method == BitmapMethod.DHash)
            return new DiffSet(baselinePath, capturePath, Diff: string.Empty, Triptych: null);

        // 3. SSIM + pixel-exact both produce a diff.png with red heatmap overlay.
        var diffPath = Path.Combine(outputDir, "diff.png");
        using var baseline = Image.Load<Rgba32>(baselineBytes);
        using var capture = Image.Load<Rgba32>(captureBytes);
        var pixelRedness = method switch
        {
            BitmapMethod.Ssim => BuildSsimRedness(
                ssim ?? throw new ArgumentException("Ssim method requires non-null ssim"),
                tolerance, baseline.Width, baseline.Height),
            BitmapMethod.PixelExact => BuildPixelExactRedness(baseline, capture, tolerance),
            _ => throw new ArgumentOutOfRangeException(nameof(method)),
        };

        using (var diff = baseline.Clone())
        {
            ApplyHeatmap(diff, pixelRedness);
            diff.SaveAsPng(diffPath);
        }

        // 4. Composite output if requested.
        string? triptychPath = null;
        if (format is DiffFormat.Triptych or DiffFormat.All)
        {
            triptychPath = Path.Combine(outputDir, "triptych.png");
            BuildTriptych(baselineBytes, captureBytes, diffPath, triptychPath);
        }

        return new DiffSet(baselinePath, capturePath, diffPath, triptychPath);
    }

    /// <summary>
    /// Compute per-pixel redness in [0, 1] via bilinear interpolation of per-block SSIM scores.
    /// Block centers are at <c>(bx*8 + 4, by*8 + 4)</c>. Edge pixels clamp to boundary
    /// blocks so the 4-neighbour interpolation always has 4 valid samples.
    /// </summary>
    private static float[,] BuildSsimRedness(SsimResult ssim, double tolerance, int width, int height)
    {
        var blocks = ssim.BlockScores;
        int bx = ssim.BlocksX;
        int by = ssim.BlocksY;
        float tol = (float)tolerance;

        // Per-block redness: 0 if score >= tolerance, else proportional severity.
        var blockRedness = new float[by, bx];
        for (int j = 0; j < by; j++)
            for (int i = 0; i < bx; i++)
            {
                float s = blocks[j, i];
                blockRedness[j, i] = s >= tol ? 0f : Math.Clamp((tol - s) / tol, 0f, 1f);
            }

        var pixelRedness = new float[height, width];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            // Find the block whose center is just to the upper-left of (x, y).
            // Block center for (bi, bj) is at ((bi*8 + 4), (bj*8 + 4)).
            float fx = (x - 4) / (float)Block;   // can go negative for x < 4
            float fy = (y - 4) / (float)Block;
            int bi = (int)Math.Floor(fx);
            int bj = (int)Math.Floor(fy);
            float u = fx - bi;
            float v = fy - bj;

            // Clamp the 4 neighbour indices to valid block range.
            int bi0 = Math.Clamp(bi, 0, bx - 1);
            int bi1 = Math.Clamp(bi + 1, 0, bx - 1);
            int bj0 = Math.Clamp(bj, 0, by - 1);
            int bj1 = Math.Clamp(bj + 1, 0, by - 1);

            // Bilinear: weighted sum of 4 corners.
            float r00 = blockRedness[bj0, bi0];
            float r10 = blockRedness[bj0, bi1];
            float r01 = blockRedness[bj1, bi0];
            float r11 = blockRedness[bj1, bi1];
            float top = r00 * (1 - u) + r10 * u;
            float bot = r01 * (1 - u) + r11 * u;
            pixelRedness[y, x] = top * (1 - v) + bot * v;
        }
        return pixelRedness;
    }

    /// <summary>
    /// Per-pixel redness for pixel-exact mode. Pixels with max-channel-delta ≤ tolerance
    /// get redness=0 (clean); failing pixels scale to delta/255. No bilinear smoothing —
    /// the block grid concept doesn't apply.
    /// </summary>
    private static float[,] BuildPixelExactRedness(Image<Rgba32> baseline, Image<Rgba32> capture, double tolerance)
    {
        int w = baseline.Width, h = baseline.Height;
        var redness = new float[h, w];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var pa = baseline[x, y];
            var pb = capture[x, y];
            int dr = Math.Abs(pa.R - pb.R);
            int dg = Math.Abs(pa.G - pb.G);
            int db = Math.Abs(pa.B - pb.B);
            int delta = Math.Max(dr, Math.Max(dg, db));
            if (delta > tolerance)
                redness[y, x] = (float)(delta / 255.0);
        }
        return redness;
    }

    /// <summary>
    /// Apply the heatmap to the image in place. Per pixel:
    /// <c>R' = lerp(R, 255, redness*0.6); G' = G * (1 - redness*0.4); B' = B * (1 - redness*0.4)</c>.
    /// Keeps underlying detail visible while making hot regions obvious.
    /// </summary>
    private static void ApplyHeatmap(Image<Rgba32> img, float[,] pixelRedness)
    {
        int w = img.Width, h = img.Height;
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < w; x++)
                {
                    float r = pixelRedness[y, x];
                    if (r <= 0) continue;
                    var p = row[x];
                    int newR = (int)(p.R + (255 - p.R) * (r * 0.6f));
                    int newG = (int)(p.G * (1f - r * 0.4f));
                    int newB = (int)(p.B * (1f - r * 0.4f));
                    row[x] = new Rgba32(
                        (byte)Math.Clamp(newR, 0, 255),
                        (byte)Math.Clamp(newG, 0, 255),
                        (byte)Math.Clamp(newB, 0, 255),
                        p.A);
                }
            }
        });
    }

    /// <summary>
    /// Build a 3-wide horizontal triptych from baseline | capture | diff. All three are
    /// expected to share dimensions — the renderer guarantees this by construction.
    /// </summary>
    private static void BuildTriptych(byte[] baselineBytes, byte[] captureBytes, string diffPath, string outputPath)
    {
        using var baseline = Image.Load<Rgba32>(baselineBytes);
        using var capture = Image.Load<Rgba32>(captureBytes);
        using var diff = Image.Load<Rgba32>(diffPath);
        int w = baseline.Width, h = baseline.Height;
        using var composite = new Image<Rgba32>(w * 3, h);
        composite.Mutate(ctx =>
        {
            ctx.DrawImage(baseline, new Point(0, 0), 1f);
            ctx.DrawImage(capture, new Point(w, 0), 1f);
            ctx.DrawImage(diff, new Point(w * 2, 0), 1f);
        });
        composite.SaveAsPng(outputPath);
    }
}
