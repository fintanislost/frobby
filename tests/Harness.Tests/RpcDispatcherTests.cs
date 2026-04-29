using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class RpcDispatcherTests
{
    private static readonly JsonElement TrueElement = JsonDocument.Parse("true").RootElement;

    [Fact]
    public async Task Dispatch_KnownMethod_ReturnsOk()
    {
        var gameThread = new GameThreadDispatch();
        var disp = new RpcDispatcher(gameThread);
        disp.Register("ping", _ => TrueElement);

        var task = disp.DispatchAsync(
            new JsonRpcRequest { Id = 1, Method = "ping" },
            CancellationToken.None);

        gameThread.Drain();
        var resp = await task;

        Assert.Null(resp.Error);
        Assert.Equal(1L, resp.Id);
        Assert.True(resp.Result!.Value.GetBoolean());
    }

    [Fact]
    public async Task Dispatch_UnknownMethod_ReturnsMethodNotFound()
    {
        var disp = new RpcDispatcher(new GameThreadDispatch());
        var resp = await disp.DispatchAsync(
            new JsonRpcRequest { Id = 1, Method = "nope" }, CancellationToken.None);

        Assert.NotNull(resp.Error);
        Assert.Equal(JsonRpcErrorCode.MethodNotFound, resp.Error!.Code);
        Assert.Contains("nope", resp.Error.Message);
    }

    [Fact]
    public async Task Dispatch_HandlerThrows_ReturnsInternalError()
    {
        var gameThread = new GameThreadDispatch();
        var disp = new RpcDispatcher(gameThread);
        disp.Register("kaboom", _ => throw new Exception("handler failure"));

        var task = disp.DispatchAsync(
            new JsonRpcRequest { Id = 7, Method = "kaboom" }, CancellationToken.None);
        gameThread.Drain();
        var resp = await task;

        Assert.Equal(JsonRpcErrorCode.InternalError, resp.Error!.Code);
        Assert.Equal("handler failure", resp.Error.Message);
    }

    [Fact]
    public async Task Dispatch_HandlerThrowsRpcException_PreservesCode()
    {
        var gameThread = new GameThreadDispatch();
        var disp = new RpcDispatcher(gameThread);
        disp.Register("typed_err",
            _ => throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "nope"));

        var task = disp.DispatchAsync(
            new JsonRpcRequest { Id = 1, Method = "typed_err" }, CancellationToken.None);
        gameThread.Drain();
        var resp = await task;

        Assert.Equal(JsonRpcErrorCode.InvalidParams, resp.Error!.Code);
    }

    [Fact]
    public void Register_Duplicate_Throws()
    {
        var disp = new RpcDispatcher(new GameThreadDispatch());
        disp.Register("ping", _ => TrueElement);
        Assert.Throws<InvalidOperationException>(() => disp.Register("ping", _ => TrueElement));
    }

    [Fact]
    public async Task Dispatch_HandlerRunsOnGameThread()
    {
        var gameThread = new GameThreadDispatch();
        var disp = new RpcDispatcher(gameThread);
        bool ran = false;

        disp.Register("probe", _ => { ran = true; return TrueElement; });

        var task = disp.DispatchAsync(
            new JsonRpcRequest { Id = 1, Method = "probe" }, CancellationToken.None);

        // Before drain, handler has not executed.
        Assert.False(ran);
        Assert.False(task.IsCompleted);

        gameThread.Drain();
        await task;

        Assert.True(ran);
    }

    [Fact]
    public async Task Dispatch_AsyncHandler_CompletesAfterHandlerTask()
    {
        var gameThread = new GameThreadDispatch();
        var disp = new RpcDispatcher(gameThread);
        var inner = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        disp.RegisterAsync("wait", _ => inner.Task);

        var task = disp.DispatchAsync(
            new JsonRpcRequest { Id = 5, Method = "wait" },
            CancellationToken.None);

        gameThread.Drain();
        Assert.False(task.IsCompleted);

        inner.SetResult(JsonDocument.Parse("{\"ok\":true}").RootElement);
        var resp = await task;

        Assert.Null(resp.Error);
        Assert.True(resp.Result!.Value.GetProperty("ok").GetBoolean());
    }
}
