using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SdvTestFramework.Runner.Watch;

/// <summary>
/// Resident orchestrator for <c>--watch</c> mode. Installs a <see cref="ScenarioWatcher"/>,
/// blocks on either a watcher trigger or cancellation, invokes the rerun callback on each
/// trigger, and prints <c>[watch]</c> banners to the output writer.
/// </summary>
/// <remarks>
/// The rerun callback is supplied by <c>RunCommand</c>; it typically wraps the
/// <c>RunOnceAsync</c> helper with a closure over the live session + reporter + writer.
/// Exceptions from the rerun callback are caught + logged to stderr — the loop continues
/// watching rather than tearing down SDV.
/// </remarks>
public static class WatchLoop
{
    /// <summary>
    /// Run the watch loop until <paramref name="ct"/> cancels. Prints an initial banner,
    /// installs a watcher over <paramref name="paths"/>, reruns on each trigger.
    /// </summary>
    public static Task RunAsync(
        IReadOnlyList<string> paths,
        Func<CancellationToken, Task> rerun,
        TextWriter output,
        CancellationToken ct)
    {
        return RunAsyncForTests(paths, rerun, output, watcherFactory: null, ct);
    }

    /// <summary>Test seam: inject a custom watcher factory for synthetic triggers.</summary>
    public static async Task RunAsyncForTests(
        IReadOnlyList<string> paths,
        Func<CancellationToken, Task> rerun,
        TextWriter output,
        Func<Action, ScenarioWatcher>? watcherFactory,
        CancellationToken ct)
    {
        using var triggered = new SemaphoreSlim(0, int.MaxValue);

        ScenarioWatcher watcher = watcherFactory != null
            ? watcherFactory(() => triggered.Release())
            : new ScenarioWatcher(paths, () => triggered.Release());

        try
        {
            output.WriteLine($"[watch] waiting for changes in {string.Join(", ", paths)}...");
            output.Flush();

            while (!ct.IsCancellationRequested)
            {
                try { await triggered.WaitAsync(ct); }
                catch (OperationCanceledException) { break; }

                output.WriteLine();
                output.WriteLine("[watch] file(s) changed — rerunning");
                output.Flush();

                try { await rerun(ct); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[watch] rerun failed: {ex.Message}");
                }

                if (ct.IsCancellationRequested) break;
                output.WriteLine($"[watch] waiting for changes in {string.Join(", ", paths)}...");
                output.Flush();
            }
        }
        finally
        {
            watcher.Dispose();
        }
    }
}
