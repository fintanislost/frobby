using System.Threading.Tasks;
using SdvTestFramework.Harness.Rpc;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class GameThreadRpcPumpTests
{
    [Fact]
    public async Task OnRendering_DrainsQueuedRpcWork_BeforeFrameDraws()
    {
        var dispatch = new GameThreadDispatch();
        var pump = new GameThreadRpcPump(dispatch);

        var task = dispatch.RunAsync(() => 42);

        Assert.False(task.IsCompleted);

        pump.OnRendering();

        Assert.Equal(42, await task);
        Assert.Equal(0, dispatch.PendingCount);
    }

    [Fact]
    public async Task OnRendered_DrainsQueuedRpcWork_WhenUpdateTicksArePaused()
    {
        var dispatch = new GameThreadDispatch();
        var pump = new GameThreadRpcPump(dispatch);

        var task = dispatch.RunAsync(() => 42);

        Assert.False(task.IsCompleted);

        pump.OnRendered();

        Assert.Equal(42, await task);
        Assert.Equal(0, dispatch.PendingCount);
    }
}
