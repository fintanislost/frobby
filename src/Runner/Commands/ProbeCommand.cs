using System;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;

namespace SdvTestFramework.Runner.Commands;

/// <summary>
/// `probe` — smoke test for the RPC pipe: connect, await `ready`, invoke `state.player`,
/// print result. Fails fast with a non-zero exit code on any step failing or on a 10-second
/// timeout per step.
/// </summary>
public static class ProbeCommand
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan InvokeTimeout = TimeSpan.FromSeconds(10);

    public static async Task<int> RunAsync(ReadOnlyMemory<string> args, CancellationToken ct)
    {
        var socket = args.Length > 0
            ? args.Span[0]
            : Environment.GetEnvironmentVariable("SDV_TEST_SOCKET");
        if (string.IsNullOrEmpty(socket))
        {
            Console.Error.WriteLine("no socket path: pass as arg or set $SDV_TEST_SOCKET");
            return 2;
        }

        try
        {
            return await ExecuteAsync(socket, ct);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("probe cancelled");
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"probe failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> ExecuteAsync(string socket, CancellationToken ct)
    {
        Console.WriteLine($"[probe] connecting to {socket}");
        using var session = await UnixSocketRpc.ConnectAsync(socket, ct);

        var readyTcs = new TaskCompletionSource<JsonRpcNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.NotificationReceived += note =>
        {
            if (note.Method == "ready")
                readyTcs.TrySetResult(note);
        };

        var runLoop = session.RunAsync(ct);

        // Handshake.
        var ready = await readyTcs.Task.WaitAsync(ReadyTimeout, ct);
        var p = ready.Params;
        Console.WriteLine(
            $"[probe] ready   version={p?.GetProperty("version").GetString()} "
            + $"sdv={p?.GetProperty("sdv").GetString()} "
            + $"smapi={p?.GetProperty("smapi").GetString()}");

        // First real method invocation.
        using var invokeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        invokeCts.CancelAfter(InvokeTimeout);
        var resp = await session.InvokeAsync("state.player", params_: null, invokeCts.Token);

        if (resp.Error is { } err)
        {
            Console.Error.WriteLine($"[probe] state.player error: {(int)err.Code} {err.Message}");
            return 3;
        }

        Console.WriteLine($"[probe] state.player: {resp.Result}");
        return 0;
    }
}
