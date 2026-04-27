using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Models;
using SdvTestFramework.Protocol.Reports;
using SdvTestFramework.Runner.Bitmap;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Bitmap;

public class BitmapAssertionTests
{
    // Minimal shim of the IBitmapRpcClient contract — just enough to return a canned
    // bitmap.capture response pointing at a pre-written PNG on disk.
    private sealed class FakeRpcClient : IBitmapRpcClient
    {
        public string CapturePath { get; init; } = string.Empty;
        public int CaptureWidth { get; init; } = 64;
        public int CaptureHeight { get; init; } = 64;

        public Task<BitmapCaptureResult> BitmapCaptureAsync(JsonElement? region, CancellationToken ct)
            => Task.FromResult(new BitmapCaptureResult
            {
                Path = CapturePath,
                Width = CaptureWidth,
                Height = CaptureHeight,
            });
    }

    private static string WriteGradientPng(string path, int w = 64, int h = 64)
    {
        using var img = new Image<Rgba32>(w, h);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            img[x, y] = new Rgba32((byte)((x * 4) % 256), (byte)((y * 4) % 256), (byte)(((x + y) * 2) % 256), 255);
        img.SaveAsPng(path);
        return path;
    }

    [Fact]
    public async Task MatchingCapture_Passes()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"ba-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        var capture = WriteGradientPng(Path.Combine(tmp, "capture.png"));
        var baseline = WriteGradientPng(Path.Combine(tmp, "baseline.png"));

        try
        {
            var a = new ScenarioAssertion
            {
                Type = "bitmap",
                Baseline = baseline,   // absolute path → no resolution needed
                Tolerance = 0.95,
            };
            var rpc = new FakeRpcClient { CapturePath = capture };
            var result = await BitmapAssertion.EvaluateAsync(
                rpc, a, scenarioPath: Path.Combine(tmp, "s.test.json"),
                updateBaselines: false,
                diffOutputDir: null,
                runWideDiffFormat: DiffFormat.Files,
                runWideTier: "generic",
                ct: CancellationToken.None);

            Assert.True(result.Passed);
            Assert.Null(result.FailureMessage);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public async Task MissingBaseline_WithoutUpdateFlag_Fails()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"ba-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        var capture = WriteGradientPng(Path.Combine(tmp, "capture.png"));
        var missing = Path.Combine(tmp, "does_not_exist.png");

        try
        {
            var a = new ScenarioAssertion
            {
                Type = "bitmap",
                Baseline = missing,
                Tolerance = 0.95,
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
            Assert.Contains("baseline not found", result.FailureMessage);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public async Task MissingBaseline_WithUpdateFlag_WritesAndPasses()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"ba-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        var capture = WriteGradientPng(Path.Combine(tmp, "capture.png"));
        var baseline = Path.Combine(tmp, "baselines", "new.png");   // parent dir not yet created

        try
        {
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
                diffOutputDir: null,
                runWideDiffFormat: DiffFormat.Files,
                runWideTier: "generic",
                ct: CancellationToken.None);

            Assert.True(result.Passed);
            Assert.Null(result.FailureMessage);
            Assert.True(File.Exists(baseline));
            Assert.Equal(File.ReadAllBytes(capture), File.ReadAllBytes(baseline));
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public async Task ExistingBaseline_WithUpdateFlag_OverwritesAndPasses()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"ba-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        var capture = WriteGradientPng(Path.Combine(tmp, "capture.png"));
        // Pre-populate the baseline with OLD content.
        var baseline = Path.Combine(tmp, "baseline.png");
        File.WriteAllBytes(baseline, new byte[] { 0x00, 0x01, 0x02 });   // stale content

        try
        {
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
                diffOutputDir: null,
                runWideDiffFormat: DiffFormat.Files,
                runWideTier: "generic",
                ct: CancellationToken.None);

            Assert.True(result.Passed);
            Assert.Null(result.FailureMessage);
            // Baseline should now match the capture, not the stale 3-byte content.
            Assert.Equal(File.ReadAllBytes(capture), File.ReadAllBytes(baseline));
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }
}
