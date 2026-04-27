using System;
using System.IO;
using SdvTestFramework.Protocol.Reports;
using SdvTestFramework.Runner.Bitmap;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Bitmap;

public class DiffImageRendererTests
{
    // 64×64 deterministic gradient. Same shape as SsimDiffTests for consistency.
    private static byte[] GradientPng(int seed = 0)
    {
        using var img = new Image<Rgba32>(64, 64);
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
        {
            byte r = (byte)((x * 4 + seed) % 256);
            byte g = (byte)((y * 4 + seed) % 256);
            byte b = (byte)(((x + y) * 2 + seed) % 256);
            img[x, y] = new Rgba32(r, g, b, 255);
        }
        using var ms = new MemoryStream();
        img.SaveAsPng(ms);
        return ms.ToArray();
    }

    private static SsimResult MakeSsim(float[,] blockScores)
    {
        int by = blockScores.GetLength(0);
        int bx = blockScores.GetLength(1);
        double sum = 0;
        for (int j = 0; j < by; j++)
            for (int i = 0; i < bx; i++)
                sum += blockScores[j, i];
        return new SsimResult((float)(sum / (by * bx)), blockScores, bx, by);
    }

    [Fact]
    public void IdenticalImages_DiffPngHasNoRedTint()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"diff-id-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var bytes = GradientPng();
            // 8×8 block grid = 64 blocks. All blocks score 1.0.
            var grid = new float[8, 8];
            for (int j = 0; j < 8; j++)
                for (int i = 0; i < 8; i++)
                    grid[j, i] = 1.0f;
            var ssim = MakeSsim(grid);

            var set = DiffImageRenderer.Render(bytes, bytes, ssim, tolerance: 0.95, method: BitmapMethod.Ssim, DiffFormat.Files, tmp);

            Assert.True(File.Exists(set.Baseline));
            Assert.True(File.Exists(set.Capture));
            Assert.True(File.Exists(set.Diff));
            Assert.Null(set.Triptych);

            // Diff PNG should be visually identical to baseline (no red tint applied
            // because all blocks scored above tolerance). Sample a few pixels.
            using var diff = Image.Load<Rgba32>(set.Diff);
            using var baseline = Image.Load<Rgba32>(bytes);
            for (int i = 0; i < 10; i++)
            {
                int x = (i * 7) % 64;
                int y = (i * 11) % 64;
                var dp = diff[x, y];
                var bp = baseline[x, y];
                Assert.InRange((int)dp.R, bp.R - 2, bp.R + 2);
                Assert.InRange((int)dp.G, bp.G - 2, bp.G + 2);
                Assert.InRange((int)dp.B, bp.B - 2, bp.B + 2);
            }
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public void DifferingImages_DiffPngHasRedRegionsAtFailingBlocks()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"diff-fail-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var baselineBytes = GradientPng();
            var captureBytes = GradientPng(seed: 50);
            // Mark the top-left 3×3 region of blocks as failing (score 0.5),
            // rest pass (1.0). Tolerance 0.95 → only the top-left corner gets red tint.
            var grid = new float[8, 8];
            for (int j = 0; j < 8; j++)
                for (int i = 0; i < 8; i++)
                    grid[j, i] = (j < 3 && i < 3) ? 0.5f : 1.0f;
            var ssim = MakeSsim(grid);

            var set = DiffImageRenderer.Render(baselineBytes, captureBytes, ssim, tolerance: 0.95, method: BitmapMethod.Ssim, DiffFormat.Files, tmp);

            using var diff = Image.Load<Rgba32>(set.Diff);
            // Sample center of failing block (4, 4): should be visibly red-shifted.
            // R should be elevated relative to G/B.
            var pp = diff[4, 4];
            Assert.True(pp.R > pp.G + 20, $"expected red dominance at failing block, got R={pp.R} G={pp.G} B={pp.B}");
            Assert.True(pp.R > pp.B + 20, $"expected red dominance at failing block, got R={pp.R} G={pp.G} B={pp.B}");

            // Sample a passing block (e.g., (40, 40)): should NOT have heavy red tint.
            var pq = diff[40, 40];
            Assert.True(pq.R - System.Math.Max(pq.G, pq.B) < 30, $"unexpected red tint at passing block: R={pq.R} G={pq.G} B={pq.B}");
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public void Triptych_ProducesFourthFile_3xWidth()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"diff-tri-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var bytes = GradientPng();
            var grid = new float[8, 8];
            for (int j = 0; j < 8; j++)
                for (int i = 0; i < 8; i++)
                    grid[j, i] = 1.0f;
            var ssim = MakeSsim(grid);

            var set = DiffImageRenderer.Render(bytes, bytes, ssim, tolerance: 0.95, method: BitmapMethod.Ssim, DiffFormat.Triptych, tmp);

            Assert.NotNull(set.Triptych);
            Assert.True(File.Exists(set.Triptych));
            using var img = Image.Load<Rgba32>(set.Triptych!);
            Assert.Equal(64 * 3, img.Width);
            Assert.Equal(64, img.Height);
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public void BilinearSmoothing_NoHardBlockBoundaries()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"diff-smooth-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var bytes = GradientPng();
            // Sharp gradient: leftmost block fails (0.0), all others pass (1.0).
            var grid = new float[8, 8];
            for (int j = 0; j < 8; j++)
                for (int i = 0; i < 8; i++)
                    grid[j, i] = i == 0 ? 0.0f : 1.0f;
            var ssim = MakeSsim(grid);

            var set = DiffImageRenderer.Render(bytes, bytes, ssim, tolerance: 0.95, method: BitmapMethod.Ssim, DiffFormat.Files, tmp);
            using var diff = Image.Load<Rgba32>(set.Diff);
            using var baseline = Image.Load<Rgba32>(bytes);

            // Pixel just inside the failing block (x=7, mid-row): heavily red.
            // Pixel just outside (x=8): with bilinear smoothing, redness should taper.
            // Pixel further out (x=12, midway to the next block center at x=12): less red.
            int redAt7 = diff[7, 32].R - baseline[7, 32].R;
            int redAt9 = diff[9, 32].R - baseline[9, 32].R;
            int redAt15 = diff[15, 32].R - baseline[15, 32].R;
            // Strict block-tinting (no bilinear) would make redAt7 high and redAt9 ≈ 0.
            // With bilinear smoothing, redAt9 is between redAt7 and redAt15 (continuous gradient).
            Assert.True(redAt7 > redAt9, $"expected redAt7({redAt7}) > redAt9({redAt9})");
            Assert.True(redAt9 > redAt15, $"expected redAt9({redAt9}) > redAt15({redAt15})");
            // And redAt9 must be > 0 — block-strict tinting would zero this out at the boundary.
            Assert.True(redAt9 > 5, $"expected smoothed tint at block boundary, got redAt9={redAt9}");
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }
}
