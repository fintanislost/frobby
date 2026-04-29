using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Reports;
using SdvTestFramework.Runner.Reports;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Reports;

public class ScreenshotRecorderTests
{
    private sealed class FakeBitmapInvoker : ScreenshotRecorder.IBitmapInvoker
    {
        public string CapturePath { get; init; } = "/tmp/fake-capture.png";
        public bool ShouldFail { get; init; }
        public bool? LastAllowUnfrozen { get; private set; }
        public ScreenshotCaptureMode? LastMode { get; private set; }
        public int? LastTimeoutMs { get; private set; }
        public Task<string?> CaptureAsync(
            bool allowUnfrozen,
            ScreenshotCaptureMode mode,
            int timeoutMs,
            CancellationToken ct)
        {
            LastAllowUnfrozen = allowUnfrozen;
            LastMode = mode;
            LastTimeoutMs = timeoutMs;
            return ShouldFail
                ? Task.FromResult<string?>(null)
                : Task.FromResult<string?>(CapturePath);
        }
    }

    [Fact]
    public async Task CaptureAsync_CopiesBitmapToScenarioScreenshots()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"ssrt-{Guid.NewGuid():N}");
        var rd = RunDirectory.Create(tmp);
        // Pre-create a fake source PNG so the copy succeeds.
        var src = Path.Combine(tmp, "source.png");
        File.WriteAllBytes(src, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        try
        {
            var inv = new FakeBitmapInvoker { CapturePath = src };
            var rec = new ScreenshotRecorder(inv);
            var dest = await rec.CaptureAsync(rd, "my_scenario", "after-warp", CancellationToken.None);

            Assert.NotNull(dest);
            Assert.True(File.Exists(dest));
            Assert.EndsWith(Path.Combine("my_scenario", "screenshots", "after-warp.png"), dest);
        }
        finally { Directory.Delete(rd.Root, recursive: true); }
    }

    [Fact]
    public async Task CaptureAsync_RpcFailure_ReturnsNullWithoutThrowing()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"ssrt-{Guid.NewGuid():N}");
        var rd = RunDirectory.Create(tmp);
        try
        {
            var inv = new FakeBitmapInvoker { ShouldFail = true };
            var rec = new ScreenshotRecorder(inv);
            var dest = await rec.CaptureAsync(rd, "my_scenario", "x", CancellationToken.None);
            Assert.Null(dest);
        }
        finally { Directory.Delete(rd.Root, recursive: true); }
    }

    [Fact]
    public async Task CaptureAsync_ThreadsAllowUnfrozenToInvoker()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"ssrt-{Guid.NewGuid():N}");
        var rd = RunDirectory.Create(tmp);
        var src = Path.Combine(tmp, "source.png");
        File.WriteAllBytes(src, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        try
        {
            var inv = new FakeBitmapInvoker { CapturePath = src };
            var rec = new ScreenshotRecorder(inv);
            await rec.CaptureAsync(rd, "my_scenario", "after-warp", CancellationToken.None, allowUnfrozen: true);

            Assert.True(inv.LastAllowUnfrozen);
        }
        finally { Directory.Delete(rd.Root, recursive: true); }
    }

    [Fact]
    public async Task CaptureAsync_CanUseNextFrameInvoker()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"ssrt-{Guid.NewGuid():N}");
        var rd = RunDirectory.Create(tmp);
        var src = Path.Combine(tmp, "source.png");
        File.WriteAllBytes(src, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        try
        {
            var inv = new FakeBitmapInvoker { CapturePath = src };
            var rec = new ScreenshotRecorder(inv);

            await rec.CaptureAsync(
                rd,
                "my_scenario",
                "next",
                CancellationToken.None,
                captureMode: ScreenshotCaptureMode.NextFrame,
                timeoutMs: 3000);

            Assert.Equal(ScreenshotCaptureMode.NextFrame, inv.LastMode);
            Assert.Equal(3000, inv.LastTimeoutMs);
        }
        finally { Directory.Delete(rd.Root, recursive: true); }
    }
}
