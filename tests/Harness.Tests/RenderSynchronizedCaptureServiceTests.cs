using System;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Harness.Capture;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class RenderSynchronizedCaptureServiceTests
{
    [Fact]
    public async Task RequestAsync_CompletesOnRendered()
    {
        var service = new RenderSynchronizedCaptureService();
        var task = service.RequestAsync(
            () => new BitmapCaptureResult { Path = "/tmp/capture.png", Width = 1280, Height = 720 },
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.False(task.IsCompleted);

        service.OnRendered();

        var result = await task;
        Assert.Equal("/tmp/capture.png", result.Path);
    }

    [Fact]
    public async Task RequestAsync_TimesOutWithoutRendered()
    {
        var service = new RenderSynchronizedCaptureService();

        var task = service.RequestAsync(
            () => new BitmapCaptureResult(),
            TimeSpan.FromMilliseconds(10),
            CancellationToken.None);

        await Assert.ThrowsAsync<TimeoutException>(async () => await task);
    }

    [Fact]
    public async Task RequestAsync_PropagatesCaptureFailure()
    {
        var service = new RenderSynchronizedCaptureService();
        var task = service.RequestAsync(
            () => throw new InvalidOperationException("capture failed"),
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        service.OnRendered();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await task);
        Assert.Equal("capture failed", ex.Message);
    }

    [Fact]
    public async Task RequestAsync_DoesNotCaptureAfterTimeout()
    {
        var service = new RenderSynchronizedCaptureService();
        int captures = 0;

        var task = service.RequestAsync(
            () =>
            {
                captures++;
                return new BitmapCaptureResult();
            },
            TimeSpan.FromMilliseconds(10),
            CancellationToken.None);

        await Assert.ThrowsAsync<TimeoutException>(async () => await task);

        service.OnRendered();

        Assert.Equal(0, captures);
    }
}
