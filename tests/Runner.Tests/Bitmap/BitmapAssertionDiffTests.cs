using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Models;
using SdvTestFramework.Protocol.Reports;
using SdvTestFramework.Runner.Bitmap;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Bitmap;

public class BitmapAssertionDiffTests
{
    private sealed class FakeRpcClient : IBitmapRpcClient
    {
        public string CapturePath { get; init; } = string.Empty;
        public Task<BitmapCaptureResult> BitmapCaptureAsync(JsonElement? region, CancellationToken ct)
            => Task.FromResult(new BitmapCaptureResult { Path = CapturePath, Width = 64, Height = 64 });
    }

    private static string WriteGradientPng(string path, int seed = 0)
    {
        using var img = new Image<Rgba32>(64, 64);
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
            img[x, y] = new Rgba32((byte)((x * 4 + seed) % 256), (byte)((y * 4 + seed) % 256), (byte)(((x + y) * 2 + seed) % 256), 255);
        img.SaveAsPng(path);
        return path;
    }

    private static string MakeBlackPng(string path)
    {
        using var img = new Image<Rgba32>(64, 64);
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
            img[x, y] = new Rgba32(0, 0, 0, 255);
        img.SaveAsPng(path);
        return path;
    }

    [Fact]
    public async Task FailingAssertion_WritesThreeDiffPngs()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"bad-{Guid.NewGuid():N}");
        var outDir = Path.Combine(tmp, "out");
        Directory.CreateDirectory(tmp);
        try
        {
            var capture = WriteGradientPng(Path.Combine(tmp, "capture.png"));
            // Baseline is solid black — wildly different from the gradient capture.
            var baseline = MakeBlackPng(Path.Combine(tmp, "baseline.png"));

            var a = new ScenarioAssertion
            {
                Type = "bitmap",
                Baseline = baseline,
                Tolerance = 0.95,
            };
            var rpc = new FakeRpcClient { CapturePath = capture };
            var result = await BitmapAssertion.EvaluateAsync(
                rpc, a, scenarioPath: Path.Combine(tmp, "s.test.json"),
                updateBaselines: false,
                diffOutputDir: outDir,
                runWideDiffFormat: DiffFormat.Files,
                runWideTier: "generic",
                ct: CancellationToken.None);

            Assert.False(result.Passed);
            Assert.NotNull(result.Diffs);
            Assert.True(File.Exists(result.Diffs!.Baseline));
            Assert.True(File.Exists(result.Diffs.Capture));
            Assert.True(File.Exists(result.Diffs.Diff));
            Assert.Null(result.Diffs.Triptych);
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public async Task PassingAssertion_WritesNoDiffPngs()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"bad-{Guid.NewGuid():N}");
        var outDir = Path.Combine(tmp, "out");
        Directory.CreateDirectory(tmp);
        try
        {
            var capture = WriteGradientPng(Path.Combine(tmp, "capture.png"));
            var baseline = WriteGradientPng(Path.Combine(tmp, "baseline.png"));

            var a = new ScenarioAssertion
            {
                Type = "bitmap",
                Baseline = baseline,
                Tolerance = 0.95,
            };
            var rpc = new FakeRpcClient { CapturePath = capture };
            var result = await BitmapAssertion.EvaluateAsync(
                rpc, a, scenarioPath: Path.Combine(tmp, "s.test.json"),
                updateBaselines: false,
                diffOutputDir: outDir,
                runWideDiffFormat: DiffFormat.Files,
                runWideTier: "generic",
                ct: CancellationToken.None);

            Assert.True(result.Passed);
            Assert.Null(result.Diffs);
            Assert.False(Directory.Exists(outDir), "diff dir should not be created on pass");
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public async Task UpdateBaselinesMode_FailingAssertion_WritesNoDiffPngs()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"bad-{Guid.NewGuid():N}");
        var outDir = Path.Combine(tmp, "out");
        Directory.CreateDirectory(tmp);
        try
        {
            var capture = WriteGradientPng(Path.Combine(tmp, "capture.png"));
            // Baseline starts solid black — would fail SSIM, but update mode should
            // overwrite it with the capture, not generate diffs.
            var baseline = MakeBlackPng(Path.Combine(tmp, "baseline.png"));

            var a = new ScenarioAssertion
            {
                Type = "bitmap",
                Baseline = baseline,
                Tolerance = 0.95,
            };
            var rpc = new FakeRpcClient { CapturePath = capture };
            var result = await BitmapAssertion.EvaluateAsync(
                rpc, a, scenarioPath: Path.Combine(tmp, "s.test.json"),
                updateBaselines: true,
                diffOutputDir: outDir,
                runWideDiffFormat: DiffFormat.Files,
                runWideTier: "generic",
                ct: CancellationToken.None);

            Assert.True(result.Passed);
            Assert.Null(result.Diffs);
            Assert.False(Directory.Exists(outDir), "diff dir should not be created in update-mode");
            // Baseline now matches the capture (existing behaviour).
            Assert.Equal(File.ReadAllBytes(capture), File.ReadAllBytes(baseline));
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }
}
