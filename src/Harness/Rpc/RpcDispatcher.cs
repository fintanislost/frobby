using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;

namespace SdvTestFramework.Harness.Rpc;

/// <summary>
/// Maps RPC method names to handler delegates and produces a <see cref="JsonRpcResponse"/>
/// for each incoming <see cref="JsonRpcRequest"/>. Handlers registered via
/// <see cref="Register"/> run on the game thread via the supplied
/// <see cref="GameThreadDispatch"/>; callers shouldn't schedule their own dispatch.
/// </summary>
public sealed class RpcDispatcher
{
    private readonly Dictionary<string, Func<JsonElement?, Task<JsonElement?>>> _handlers =
        new(StringComparer.Ordinal);

    private readonly GameThreadDispatch _gameThread;

    public RpcDispatcher(GameThreadDispatch gameThread)
    {
        _gameThread = gameThread;
    }

    public void Register(string method, Func<JsonElement?, JsonElement?> handler)
        => RegisterAsync(method, p => Task.FromResult(handler(p)));

    public void RegisterAsync(string method, Func<JsonElement?, Task<JsonElement?>> handler)
    {
        if (_handlers.ContainsKey(method))
            throw new InvalidOperationException($"duplicate method registration: {method}");
        _handlers[method] = handler;
    }

    public async Task<JsonRpcResponse> DispatchAsync(JsonRpcRequest request, CancellationToken ct)
    {
        if (!_handlers.TryGetValue(request.Method, out var handler))
        {
            return JsonRpcResponse.Fail(request.Id,
                new JsonRpcError(JsonRpcErrorCode.MethodNotFound, $"method not found: {request.Method}"));
        }

        try
        {
            var result = await _gameThread.RunTaskAsync(() => handler(request.Params), ct).ConfigureAwait(false);
            return JsonRpcResponse.Ok(request.Id, result ?? NullElement);
        }
        catch (OperationCanceledException)
        {
            return JsonRpcResponse.Fail(request.Id,
                new JsonRpcError(JsonRpcErrorCode.InternalError, "cancelled"));
        }
        catch (JsonRpcException rpcEx)
        {
            return JsonRpcResponse.Fail(request.Id, new JsonRpcError(rpcEx.Code, rpcEx.Message));
        }
        catch (Exception ex)
        {
            // Last-resort error. Handlers that need richer error codes should throw
            // JsonRpcException explicitly.
            return JsonRpcResponse.Fail(request.Id,
                new JsonRpcError(JsonRpcErrorCode.InternalError, ex.Message));
        }
    }

    private static readonly JsonElement NullElement = JsonDocument.Parse("null").RootElement;
}
