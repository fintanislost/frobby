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

public class BitmapMethodDispatchTests
{
    private sealed class FakeRpcClient : IBitmapRpcClient
    {
        public string CapturePath { get; init; } = string.Empty;
        public Task<BitmapCaptureResult> BitmapCaptureAsync(JsonElement? region, CancellationToken ct)
            => Task.FromResult(new BitmapCaptureResult { Path = CapturePath, Width = 64, Height = 64 });
    }

    private static string WriteSolid(string path, byte r, byte g, byte b)
    {
        using var img = new Image<Rgba32>(64, 64);
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
            img[x, y] = new Rgba32(r, g, b, 255);
        img.SaveAsPng(path);
        return path;
    }

    private static string WriteGradient(string path, int seed = 0)
    {
        using var img = new Image<Rgba32>(64, 64);
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
            img[x, y] = new Rgba32(
                (byte)((x * 4 + seed) % 256),
                (byte)((y * 4 + seed) % 256),
                (byte)(((x + y) * 2 + seed) % 256),
                255);
        img.SaveAsPng(path);
        return path;
    }

    private static string WriteInvertedGradient(string path)
    {
        using var img = new Image<Rgba32>(64, 64);
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
        {
            var r = (byte)((x * 4) % 256);
            var g = (byte)((y * 4) % 256);
            var b = (byte)(((x + y) * 2) % 256);
            img[x, y] = new Rgba32((byte)(255 - r), (byte)(255 - g), (byte)(255 - b), 255);
        }
        img.SaveAsPng(path);
        return path;
    }

    [Fact]
    public async Task MethodPixelExact_DispatchesToPixelExactDiff()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"bmd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            // Baseline gray=100, capture gray=120 → pixel-exact max delta = 20.
            var baseline = WriteSolid(Path.Combine(tmp, "baseline.png"), 100, 100, 100);
            var capture  = WriteSolid(Path.Combine(tmp, "capture.png"),  120, 120, 120);

            var a = new ScenarioAssertion
            {
                Type = "bitmap",
                Baseline = baseline,
                Method = "pixel-exact",
                Tolerance = 5,   // delta 20 > tolerance 5 → fail
            };
            var rpc = new FakeRpcClient { CapturePath = capture };
            var result = await BitmapAssertion.EvaluateAsync(
                rpc, a, scenarioPath: Path.Combine(tmp, "s.test.json"),
                updateBaselines: false,
                diffOutputDir: null,
                runWideDiffFormat: DiffFormat.Files,
                runWideTier: "generic",
                ct: CancellationToken.None);

            Assert.False(result.Passed);
            Assert.Contains("pixel-exact", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("20", result.FailureMessage);
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public async Task MethodDHash_DispatchesToDHashDiff()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"bmd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            // Gradient vs inverted gradient → high Hamming distance.
            // Tolerance = 0 → any non-zero distance fails the assertion via the dispatch path.
            var baseline = WriteGradient(Path.Combine(tmp, "baseline.png"));
            var capture  = WriteInvertedGradient(Path.Combine(tmp, "capture.png"));

            var a = new ScenarioAssertion
            {
                Type = "bitmap",
                Baseline = baseline,
                Method = "dhash",
                Tolerance = 0,
            };
            var rpc = new FakeRpcClient { CapturePath = capture };
            var result = await BitmapAssertion.EvaluateAsync(
                rpc, a, scenarioPath: Path.Combine(tmp, "s.test.json"),
                updateBaselines: false,
                diffOutputDir: null,
                runWideDiffFormat: DiffFormat.Files,
                runWideTier: "generic",
                ct: CancellationToken.None);

            Assert.False(result.Passed);
            // Failure message must come from the dispatch path's format string,
            // proving DHashDiff.HammingDistance was actually invoked.
            Assert.Contains("dhash distance", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("tolerance 0", result.FailureMessage);
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public async Task UnknownMethod_FailsWithDiagnostic()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"bmd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var baseline = WriteSolid(Path.Combine(tmp, "baseline.png"), 0, 0, 0);
            var capture  = WriteSolid(Path.Combine(tmp, "capture.png"), 0, 0, 0);

            var a = new ScenarioAssertion
            {
                Type = "bitmap",
                Baseline = baseline,
                Method = "garbage",
            };
            var rpc = new FakeRpcClient { CapturePath = capture };
            var result = await BitmapAssertion.EvaluateAsync(
                rpc, a, scenarioPath: Path.Combine(tmp, "s.test.json"),
                updateBaselines: false,
                diffOutputDir: null,
                runWideDiffFormat: DiffFormat.Files,
                runWideTier: "generic",
                ct: CancellationToken.None);

            Assert.False(result.Passed);
            Assert.Contains("garbage", result.FailureMessage);
            Assert.Contains("unknown", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }
}
