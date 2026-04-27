using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SdvTestFramework.Protocol;

/// <summary>
/// One bidirectional JSON-RPC 2.0 session over a full-duplex <see cref="Stream"/>. Symmetric
/// — both peers can send requests, responses, and notifications. No coupling between "server"
/// and "client" at the session layer; which peer is the server is just a convention layered
/// on top (which methods it exposes, who initiates shutdown, etc.).
/// </summary>
public sealed class JsonRpcSession : IDisposable
{
    private readonly Stream _stream;
    private readonly NdJsonReader _reader;
    private readonly NdJsonWriter _writer;
    private readonly SemaphoreSlim _writeLock = new(initialCount: 1, maxCount: 1);

    private long _nextId;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonRpcResponse>> _pending = new();

    /// <summary>Fires when a request (id + method) is received. Peer must send a response.</summary>
    public event Action<JsonRpcRequest>? RequestReceived;

    /// <summary>Fires on an incoming notification (method, no id).</summary>
    public event Action<JsonRpcNotification>? NotificationReceived;

    public JsonRpcSession(Stream stream)
    {
        _stream = stream;
        _reader = new NdJsonReader(stream);
        _writer = new NdJsonWriter(stream);
    }

    /// <summary>
    /// Drive the read loop. Returns when the peer disconnects or the token is cancelled.
    /// Throw if a malformed message arrives — callers can swallow or decide policy.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            string? line;
            try
            {
                line = await _reader.ReadAsync(ct).ConfigureAwait(false);
            }
            catch (IOException) { break; }  // peer closed mid-read
            catch (ObjectDisposedException) { break; }

            if (line is null) break;  // EOF
            DispatchLine(line);
        }

        // Fail any pending calls — peer won't be replying.
        foreach (var tcs in _pending.Values)
            tcs.TrySetException(new IOException("peer disconnected before response"));
        _pending.Clear();
    }

    public async Task SendNotificationAsync(string method, JsonElement? params_, CancellationToken ct)
    {
        var note = new JsonRpcNotification { Method = method, Params = params_ };
        await WriteLineAsync(JsonRpcCodec.Serialize(note), ct).ConfigureAwait(false);
    }

    public async Task SendResponseAsync(JsonRpcResponse resp, CancellationToken ct)
    {
        await WriteLineAsync(JsonRpcCodec.Serialize(resp), ct).ConfigureAwait(false);
    }

    /// <summary>Send a request and await its response. Correlates by monotonic id.</summary>
    public async Task<JsonRpcResponse> InvokeAsync(string method, JsonElement? params_, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonRpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        try
        {
            var req = new JsonRpcRequest { Id = id, Method = method, Params = params_ };
            await WriteLineAsync(JsonRpcCodec.Serialize(req), ct).ConfigureAwait(false);
            await using var _ = ct.Register(() => tcs.TrySetCanceled(ct));
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    // ---- internal ----

    private async Task WriteLineAsync(string line, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try { await _writer.WriteAsync(line, ct).ConfigureAwait(false); }
        finally { _writeLock.Release(); }
    }

    private void DispatchLine(string line)
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        bool hasMethod = root.TryGetProperty("method", out _);
        bool hasId = root.TryGetProperty("id", out _);

        if (hasMethod && hasId)
        {
            RequestReceived?.Invoke(JsonRpcCodec.ParseRequest(line));
        }
        else if (hasMethod)
        {
            NotificationReceived?.Invoke(JsonRpcCodec.ParseNotification(line));
        }
        else if (hasId)
        {
            var resp = JsonRpcCodec.ParseResponse(line);
            if (_pending.TryRemove(resp.Id, out var tcs))
                tcs.TrySetResult(resp);
            // else: response to a request we don't know about — drop silently. Alternative:
            // surface a "stray response" event for diagnostics. Start silent; add if needed.
        }
    }

    public void Dispose()
    {
        try { _stream.Dispose(); } catch { /* best effort */ }
        _writeLock.Dispose();
    }
}
