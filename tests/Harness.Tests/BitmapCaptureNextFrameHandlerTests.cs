using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using SdvTestFramework.Harness.Capture;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class BitmapCaptureNextFrameHandlerTests
{
    public BitmapCaptureNextFrameHandlerTests()
    {
        BitmapCaptureNextFrameHandler.CaptureService = new RenderSynchronizedCaptureService();
        BitmapCaptureNextFrameHandler.CaptureNow = _ => new BitmapCaptureResult();
    }

    [Fact]
    public async Task HandleAsync_RequiresPositiveTimeout()
    {
        var p = JsonDocument.Parse("{\"timeout_ms\":0}").RootElement;

        var ex = await Assert.ThrowsAsync<JsonRpcException>(
            async () => await BitmapCaptureNextFrameHandler.HandleAsync(p, CancellationToken.None));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public async Task HandleAsync_ReturnsNextFrameCaptureResult()
    {
        var service = new RenderSynchronizedCaptureService();
        BitmapCaptureNextFrameHandler.CaptureService = service;
        BitmapCaptureNextFrameHandler.CaptureNow = _ => new BitmapCaptureResult
        {
            Path = "/tmp/next.png",
            Width = 1280,
            Height = 720,
        };

        var task = BitmapCaptureNextFrameHandler.HandleAsync(null, CancellationToken.None);

        Assert.False(task.IsCompleted);

        service.OnRendered();
        Assert.False(task.IsCompleted);

        service.OnUpdateTicked();

        var result = await task;
        Assert.Equal("/tmp/next.png", result!.Value.GetProperty("path").GetString());
        Assert.Equal(1280, result.Value.GetProperty("width").GetInt32());
    }
}
