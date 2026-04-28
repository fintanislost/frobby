using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Runner.Recording;

namespace SdvTestFramework.Runner.Commands;

/// <summary>
/// <c>sdv-test record &lt;name&gt; [--mods-path X] [--output path] [--force]</c> — launches
/// SDV, installs an <see cref="RpcTraceRecorder"/> on the session, blocks until cancellation,
/// then writes the recorded steps as a scenario JSON at the configured output path.
/// </summary>
public static class RecordCommand
{
    public static async Task<int> RunAsync(ReadOnlyMemory<string> args, CancellationToken ct)
    {
        // ---- parse args ----
        string? name = null;
        string? modsPath = null;
        string? outputPath = null;
        bool force = false;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args.Span[i];
            if (a == "--mods-path" && i + 1 < args.Length) { modsPath = args.Span[++i]; continue; }
            if (a == "--output" && i + 1 < args.Length) { outputPath = args.Span[++i]; continue; }
            if (a == "--force") { force = true; continue; }
            if (a.StartsWith("--")) { Console.Error.WriteLine($"record: unknown flag '{a}'"); return 2; }
            if (name is null) { name = a; continue; }
            Console.Error.WriteLine($"record: unexpected positional argument '{a}'");
            return 2;
        }

        if (string.IsNullOrEmpty(name))
        {
            Console.Error.WriteLine("usage: sdv-test record <name> [--mods-path X] [--output path] [--force]");
            return 2;
        }

        // Default output: tests/samples/<name>.test.json (relative to cwd).
        outputPath ??= Path.Combine(Directory.GetCurrentDirectory(), "tests", "samples", $"{name}.test.json");

        // ---- output-collision check (pre-launch) ----
        if (File.Exists(outputPath) && !force)
        {
            Console.Error.WriteLine($"error: {outputPath} exists; pass --force to overwrite");
            return 3;
        }

        // ---- resolve mods path (same logic as RunCommand) ----
        modsPath ??= Environment.GetEnvironmentVariable("SDV_MODS_PATH");
        if (string.IsNullOrEmpty(modsPath))
        {
            modsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache", "sdv-test-framework", "mods");
        }
        Directory.CreateDirectory(modsPath);
        HarnessDeployer.Deploy(modsPath);

        // ---- launch SDV + connect ----
        var socket = Path.Combine(Path.GetTempPath(), $"sdv-test-record-{Guid.NewGuid():N}.sock");
        using var sdv = SdvLauncher.Launch(socket, installPath: null, modsPath: modsPath);
        var recorder = new RpcTraceRecorder();
        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(TimeSpan.FromSeconds(60));

            for (int i = 0; i < 120 && !File.Exists(socket); i++)
                await Task.Delay(500, connectCts.Token);
            if (!File.Exists(socket))
                throw new TimeoutException("SDV never opened the test socket");

            using var session = await UnixSocketRpc.ConnectAsync(socket, connectCts.Token);
            var readyTcs = new TaskCompletionSource<JsonRpcNotification>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            session.NotificationReceived += n => { if (n.Method == "ready") readyTcs.TrySetResult(n); };
            _ = session.RunAsync(ct);
            await readyTcs.Task.WaitAsync(TimeSpan.FromSeconds(60), ct);

            // Install recorder — subscribes to RequestReceived until unsubscribe fires.
            var unsubscribe = recorder.Subscribe(session);

            Console.WriteLine($"[record] capturing RPC calls — drive the game externally; Ctrl-C to save to {outputPath}");

            // Block until cancellation. Task.Delay(-1, ct) throws OperationCanceledException
            // on cancel, which we catch + exit cleanly.
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { /* expected */ }
            finally { unsubscribe(); }

            // Flush buffer to disk before teardown runs.
            recorder.WriteToFile(outputPath, name!, seed: 42);
            Console.WriteLine($"[record] wrote {outputPath} ({recorder.Count} steps)");
            if (recorder.Count == 0)
                Console.WriteLine("[record] no calls captured");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[record] fatal: {ex.Message}");
            // Best-effort: flush what we have, then error out.
            try { recorder.WriteToFile(outputPath, name!, seed: 42); Console.Error.WriteLine($"[record] partial file: {outputPath}"); }
            catch { /* swallow */ }
            return 4;
        }
        finally
        {
            SdvLauncher.Terminate(sdv);
        }
    }
}
