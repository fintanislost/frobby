using System;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Harness.Rpc;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class GameThreadDispatchTests
{
    [Fact]
    public async Task RunAsync_ReturnsResult_AfterDrain()
    {
        var d = new GameThreadDispatch();
        var task = d.RunAsync(() => 42);

        Assert.False(task.IsCompleted);
        Assert.Equal(1, d.PendingCount);

        d.Drain();
        Assert.Equal(42, await task);
        Assert.Equal(0, d.PendingCount);
    }

    [Fact]
    public async Task RunAsync_PropagatesExceptions()
    {
        var d = new GameThreadDispatch();
        var task = d.RunAsync<int>(() => throw new InvalidOperationException("boom"));

        d.Drain();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await task);
        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public async Task RunAsync_BadAction_DoesNotStallSubsequent()
    {
        var d = new GameThreadDispatch();
        var bad = d.RunAsync<int>(() => throw new Exception("first"));
        var good = d.RunAsync(() => 99);

        d.Drain();
        await Assert.ThrowsAsync<Exception>(async () => await bad);
        Assert.Equal(99, await good);
    }

    [Fact]
    public async Task RunAsync_CancelledBeforeDrain_CompletesCancelled()
    {
        var d = new GameThreadDispatch();
        using var cts = new CancellationTokenSource();
        var task = d.RunAsync(() => 1, cts.Token);

        cts.Cancel();
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await task);
    }

    [Fact]
    public async Task RunAsync_MultipleActions_ProcessedInFifoOrder()
    {
        var d = new GameThreadDispatch();
        var log = new System.Collections.Generic.List<int>();

        var t1 = d.RunAsync(() => { log.Add(1); return 1; });
        var t2 = d.RunAsync(() => { log.Add(2); return 2; });
        var t3 = d.RunAsync(() => { log.Add(3); return 3; });

        d.Drain();
        await Task.WhenAll(t1, t2, t3);

        Assert.Equal(new[] { 1, 2, 3 }, log);
    }
}
