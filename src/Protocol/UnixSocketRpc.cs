using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SdvTestFramework.Protocol;

/// <summary>
/// Unix-domain-socket transport for <see cref="JsonRpcSession"/>. Linux + macOS only — on
/// Windows we'll add a Named Pipes variant when that platform becomes a target. Current
/// spec (M1) is Linux-primary, so this is the supported path.
/// </summary>
public static class UnixSocketRpc
{
    /// <summary>Connect to an existing socket and wrap the connection in a session.</summary>
    public static async Task<JsonRpcSession> ConnectAsync(string path, CancellationToken ct)
    {
        var sock = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await sock.ConnectAsync(new UnixDomainSocketEndPoint(path), ct).ConfigureAwait(false);
        return new JsonRpcSession(new NetworkStream(sock, ownsSocket: true));
    }

    /// <summary>
    /// Bind a listener at <paramref name="path"/>, accept connections, and invoke
    /// <paramref name="onConnect"/> for each. The socket file is removed on shutdown.
    /// Only one in-flight session at a time — we serialize accepts.
    /// </summary>
    public static async Task RunServerAsync(
        string path,
        Func<JsonRpcSession, CancellationToken, Task> onConnect,
        CancellationToken ct)
    {
        if (File.Exists(path)) File.Delete(path);

        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(path));
        listener.Listen(backlog: 1);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                Socket sock;
                try
                {
                    sock = await listener.AcceptAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                using var stream = new NetworkStream(sock, ownsSocket: true);
                using var session = new JsonRpcSession(stream);
                await onConnect(session, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            try { File.Delete(path); } catch { /* best-effort */ }
        }
    }
}
