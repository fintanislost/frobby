using System;
using System.IO;
using SdvTestFramework.Protocol.Reports;
using SdvTestFramework.Runner.Bitmap;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Bitmap;

public class DiffImageRendererMethodTests
{
    private static byte[] SolidPng(byte r, byte g, byte b)
    {
        using var img = new Image<Rgba32>(64, 64);
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
            img[x, y] = new Rgba32(r, g, b, 255);
        using var ms = new MemoryStream();
        img.SaveAsPng(ms);
        return ms.ToArray();
    }

    [Fact]
    public void PixelExactMethod_RendersPerPixelHeatmap()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"diff-pe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            // Baseline solid 100,100,100; capture solid 200,100,100 → per-channel R delta = 100.
            var baseline = SolidPng(100, 100, 100);
            var capture = SolidPng(200, 100, 100);

            var set = DiffImageRenderer.Render(
                baseline, capture,
                ssim: null,
                tolerance: 5,
                method: BitmapMethod.PixelExact,
                format: DiffFormat.Files,
                outputDir: tmp);

            Assert.True(File.Exists(set.Diff));
            using var diffImg = Image.Load<Rgba32>(set.Diff);
            // Sample center pixel: should be visibly red-shifted (R dominates G/B).
            var p = diffImg[32, 32];
            Assert.True(p.R > p.G + 20, $"expected red dominance at pixel-exact-failing pixel, got R={p.R} G={p.G} B={p.B}");
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public void DHashMethod_SkipsDiffPng()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"diff-dh-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var baseline = SolidPng(100, 100, 100);
            var capture = SolidPng(200, 100, 100);

            var set = DiffImageRenderer.Render(
                baseline, capture,
                ssim: null,
                tolerance: 5,
                method: BitmapMethod.DHash,
                format: DiffFormat.Files,
                outputDir: tmp);

            Assert.True(File.Exists(set.Baseline));
            Assert.True(File.Exists(set.Capture));
            // diff.png must NOT have been written; DiffSet.Diff is empty string.
            Assert.Equal(string.Empty, set.Diff);
            Assert.False(File.Exists(Path.Combine(tmp, "diff.png")));
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }
}
