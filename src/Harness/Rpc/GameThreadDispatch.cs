using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace SdvTestFramework.Harness.Rpc;

/// <summary>
/// Marshals callbacks from the RPC worker thread back onto the game thread.
/// SMAPI callbacks and any direct <c>Game1.*</c> access must happen on the game thread
/// (per <c>.claude/rules/sdv-conventions.md §Statics everywhere</c>); this queue + tick
/// drain is the supported path.
/// </summary>
public sealed class GameThreadDispatch
{
    private readonly ConcurrentQueue<Action> _queue = new();

    /// <summary>
    /// Run <paramref name="fn"/> on the game thread and return its result. The returned
    /// task completes asynchronously — once the next tick drains the queue and runs
    /// the action.
    /// </summary>
    public Task<T> RunAsync<T>(Func<T> fn, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        CancellationTokenRegistration reg = default;
        if (ct.CanBeCanceled)
            reg = ct.Register(() => tcs.TrySetCanceled(ct));

        _queue.Enqueue(() =>
        {
            reg.Dispose();
            if (ct.IsCancellationRequested) { tcs.TrySetCanceled(ct); return; }
            try { tcs.TrySetResult(fn()); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        });

        return tcs.Task;
    }

    /// <summary>Run <paramref name="fn"/> on the game thread; return a task that completes when it finishes.</summary>
    public Task RunAsync(Action fn, CancellationToken ct = default)
        => RunAsync<object?>(() => { fn(); return null; }, ct);

    /// <summary>
    /// Drain the queue. Call from an <c>UpdateTicked</c> handler. Exceptions inside user
    /// actions are propagated via the returned <see cref="Task"/>; we swallow here so one
    /// bad action doesn't stall subsequent ones.
    /// </summary>
    public void Drain()
    {
        while (_queue.TryDequeue(out var action))
        {
            action();
        }
    }

    /// <summary>Number of pending actions. Primarily for diagnostics.</summary>
    public int PendingCount => _queue.Count;
}
