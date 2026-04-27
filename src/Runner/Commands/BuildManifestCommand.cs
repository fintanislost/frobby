using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;

namespace SdvTestFramework.Runner.Commands;

/// <summary>
/// <c>sdv-test build-manifest</c> — drive the harness's <c>diagnostic.build_texture_manifest</c>
/// RPC, write the result to <c>~/.cache/sdv-test-framework/texture-manifests/&lt;version&gt;.json</c>.
/// </summary>
/// <remarks>
/// Blocks SDV for ~30-60 seconds during manifest build. Run once per SDV version.
/// </remarks>
public static class BuildManifestCommand
{
    public static async Task<int> RunAsync(ReadOnlyMemory<string> args, CancellationToken ct)
    {
        // ---- parse args ----
        string? explicitOutput = null;
        string? modsPath = null;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args.Span[i];
            if (a == "--output" && i + 1 < args.Length) { explicitOutput = args.Span[++i]; continue; }
            if (a == "--mods-path" && i + 1 < args.Length) { modsPath = args.Span[++i]; continue; }
            Console.Error.WriteLine($"build-manifest: unknown argument '{a}'");
            return 2;
        }

        // ---- resolve mods path ----
        modsPath ??= Environment.GetEnvironmentVariable("SDV_MODS_PATH");
        if (string.IsNullOrEmpty(modsPath))
            modsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache", "sdv-test-framework", "mods");
        Directory.CreateDirectory(modsPath);
        HarnessDeployer.Deploy(modsPath);

        // ---- launch SDV ----
        var socket = Path.Combine(Path.GetTempPath(), $"sdv-manifest-{Guid.NewGuid():N}.sock");
        using var sdv = SdvLauncher.Launch(socket, installPath: null, modsPath: modsPath);

        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(TimeSpan.FromSeconds(120));

            for (int i = 0; i < 240 && !File.Exists(socket); i++)
                await Task.Delay(500, connectCts.Token);
            if (!File.Exists(socket))
                throw new TimeoutException("SDV never opened the manifest socket");

            using var session = await UnixSocketRpc.ConnectAsync(socket, connectCts.Token);
            var readyTcs = new TaskCompletionSource<JsonRpcNotification>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            session.NotificationReceived += n => { if (n.Method == "ready") readyTcs.TrySetResult(n); };
            _ = session.RunAsync(ct);
            await readyTcs.Task.WaitAsync(TimeSpan.FromSeconds(60), ct);

            Console.Error.WriteLine("[build-manifest] harness ready, iterating content...");
            var sw = Stopwatch.StartNew();
            var resp = await session.InvokeAsync("diagnostic.build_texture_manifest", params_: null, ct);
            sw.Stop();
            if (resp.Error is { } err)
            {
                Console.Error.WriteLine($"[build-manifest] RPC failed: {err.Message}");
                return 4;
            }
            if (resp.Result is not { } result)
            {
                Console.Error.WriteLine("[build-manifest] RPC returned no result");
                return 4;
            }

            var sdvVersion = result.GetProperty("sdv_version").GetString()!;
            var count = result.GetProperty("texture_count").GetInt32();
            var outputPath = ResolveOutputPath(explicitOutput, sdvVersion);

            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir))
                Directory.CreateDirectory(outputDir);
            await File.WriteAllTextAsync(outputPath, result.GetRawText(), ct);

            var size = new FileInfo(outputPath).Length;
            Console.Error.WriteLine(
                $"[build-manifest] hashed {count} textures in {sw.Elapsed.TotalSeconds:F1}s");
            Console.Error.WriteLine(
                $"[build-manifest] wrote {outputPath} ({size / 1024} KB)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[build-manifest] fatal: {ex.Message}");
            return 4;
        }
        finally
        {
            try { if (!sdv.HasExited) { sdv.Kill(); sdv.WaitForExit(5000); } } catch { }
        }
    }

    /// <summary>Resolve the output path. Pure — unit-testable without SDV.</summary>
    public static string ResolveOutputPath(string? explicitPath, string sdvVersion)
    {
        if (!string.IsNullOrEmpty(explicitPath)) return explicitPath;
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache", "sdv-test-framework", "texture-manifests",
            $"{sdvVersion}.json");
    }
}
