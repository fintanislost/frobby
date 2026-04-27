using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

/// <summary>
/// End-to-end JSON-RPC session tests. Two sessions connected via an in-memory duplex
/// stream pair; verifies handshake notifications, request/response correlation, and
/// clean shutdown on disconnect.
/// </summary>
public class JsonRpcSessionTests
{
    [Fact]
    public async Task Handshake_NotificationFlowsFromServerToClient()
    {
        var (serverSide, clientSide) = DuplexStreams.CreatePair();
        using var server = new JsonRpcSession(serverSide);
        using var client = new JsonRpcSession(clientSide);

        var cts = new CancellationTokenSource(System.TimeSpan.FromSeconds(5));
        var received = new TaskCompletionSource<JsonRpcNotification>();
        client.NotificationReceived += n => received.TrySetResult(n);

        var clientRun = client.RunAsync(cts.Token);

        await server.SendNotificationAsync("ready",
            JsonDocument.Parse("""{"version":"0.1.0"}""").RootElement, cts.Token);

        var note = await received.Task.WaitAsync(cts.Token);
        Assert.Equal("ready", note.Method);
        Assert.Equal("0.1.0", note.Params!.Value.GetProperty("version").GetString());

        cts.Cancel();
        await Assert.ThrowsAnyAsync<System.OperationCanceledException>(() => clientRun);
    }

    [Fact]
    public async Task RequestResponse_RoundTrips()
    {
        var (serverSide, clientSide) = DuplexStreams.CreatePair();
        using var server = new JsonRpcSession(serverSide);
        using var client = new JsonRpcSession(clientSide);

        server.RequestReceived += async req =>
        {
            // echo-back handler
            var result = JsonDocument.Parse("{\"echoed\":\"" + req.Method + "\"}").RootElement;
            await server.SendResponseAsync(JsonRpcResponse.Ok(req.Id, result), CancellationToken.None);
        };

        using var cts = new CancellationTokenSource(System.TimeSpan.FromSeconds(5));
        _ = server.RunAsync(cts.Token);
        _ = client.RunAsync(cts.Token);

        var resp = await client.InvokeAsync("ping",
            JsonDocument.Parse("""{}""").RootElement, cts.Token);

        Assert.Null(resp.Error);
        Assert.Equal("ping", resp.Result!.Value.GetProperty("echoed").GetString());
    }

    [Fact]
    public async Task Invoke_ErrorResponse_SurfacesAsTypedError()
    {
        var (serverSide, clientSide) = DuplexStreams.CreatePair();
        using var server = new JsonRpcSession(serverSide);
        using var client = new JsonRpcSession(clientSide);

        server.RequestReceived += async req =>
        {
            var err = new JsonRpcError(JsonRpcErrorCode.MethodNotFound, "no such method");
            await server.SendResponseAsync(JsonRpcResponse.Fail(req.Id, err), CancellationToken.None);
        };

        using var cts = new CancellationTokenSource(System.TimeSpan.FromSeconds(5));
        _ = server.RunAsync(cts.Token);
        _ = client.RunAsync(cts.Token);

        var resp = await client.InvokeAsync("unknown_method", params_: null, cts.Token);
        Assert.NotNull(resp.Error);
        Assert.Equal(JsonRpcErrorCode.MethodNotFound, resp.Error!.Code);
    }

    [Fact]
    public async Task RunAsync_CleanExit_OnPeerDisconnect()
    {
        var (serverSide, clientSide) = DuplexStreams.CreatePair();
        using var server = new JsonRpcSession(serverSide);
        var client = new JsonRpcSession(clientSide);

        var serverRun = server.RunAsync(CancellationToken.None);

        // Closing client side should give server a clean EOF.
        client.Dispose();

        await serverRun.WaitAsync(System.TimeSpan.FromSeconds(5));
    }
}
