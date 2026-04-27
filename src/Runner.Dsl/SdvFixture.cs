using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Reports;
using Xunit;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>
/// xUnit collection fixture that launches SDV + harness once per test assembly, connects
/// over Unix socket, initializes <see cref="SdvTestSession.Current"/>. Tears down on dispose.
/// </summary>
/// <remarks>
/// Users opt in via <c>[CollectionDefinition("SDV")]</c> + <c>[Collection("SDV")]</c>.
/// See <c>docs/dsl-quickstart.md</c>.
/// Environment knobs: <c>SDV_MODS_PATH</c> (defaults to <c>~/.cache/sdv-test-framework/mods/</c>),
/// <c>DSL_SKIP_SDV_LAUNCH</c> (set to any value in CI when no live SDV is available — the
/// fixture becomes a no-op and any <c>[Scenario]</c> test in the assembly will fail with
/// "SdvTestSession.Current is not initialized"; exists so CI doesn't hang on missing SDV).
/// </remarks>
public sealed class SdvFixture : IAsyncLifetime
{
    private Process? _sdv;
    private JsonRpcSession? _session;
    private CancellationTokenSource? _lifetimeCts;
    private RunDirectory? _reportDir;
    private Stopwatch? _runStopwatch;
    private DateTime _runStartedAt;

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("DSL_SKIP_SDV_LAUNCH") is { Length: > 0 })
            return;

        _lifetimeCts = new CancellationTokenSource();
        var ct = _lifetimeCts.Token;

        var socket = Path.Combine(Path.GetTempPath(), $"sdv-dsl-{Guid.NewGuid():N}.sock");

        var modsPath = Environment.GetEnvironmentVariable("SDV_MODS_PATH");
        if (string.IsNullOrEmpty(modsPath))
        {
            modsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache", "sdv-test-framework", "mods");
        }
        Directory.CreateDirectory(modsPath);
        SdvTestFramework.Protocol.HarnessDeployer.Deploy(modsPath);
        ExtraModDeployer.DeployMany(
            modsPath,
            ExtraModDeployer.ParseEnvList(Environment.GetEnvironmentVariable("SDV_EXTRA_MODS")));

        _sdv = SdvTestFramework.Protocol.SdvLauncher.Launch(socket, installPath: null, modsPath: modsPath);

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(TimeSpan.FromSeconds(60));

        for (int i = 0; i < 120 && !File.Exists(socket); i++)
            await Task.Delay(500, connectCts.Token);
        if (!File.Exists(socket))
            throw new TimeoutException("SDV never opened the DSL test socket");

        _session = await UnixSocketRpc.ConnectAsync(socket, connectCts.Token);
        var readyTcs = new TaskCompletionSource<JsonRpcNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
        _session.NotificationReceived += n => { if (n.Method == "ready") readyTcs.TrySetResult(n); };
        _ = _session.RunAsync(ct);
        await readyTcs.Task.WaitAsync(TimeSpan.FromSeconds(60), ct);

        var session = SdvTestSession.Initialize(_session);

        // Create a per-assembly report directory. Override via $SDV_REPORT_DIR (e.g. to
        // colocate with CI artifacts); default is ./test-results under CWD. Runs are
        // cumulative: we never delete prior runs on dispose — each gets a unique subdir
        // via RunDirectory.Create.
        var baseDir = Environment.GetEnvironmentVariable("SDV_REPORT_DIR");
        if (string.IsNullOrEmpty(baseDir))
            baseDir = Path.Combine(Directory.GetCurrentDirectory(), "test-results");
        _reportDir = RunDirectory.Create(baseDir);
        session.ReportDir = _reportDir;
        _runStartedAt = DateTime.UtcNow;
        _runStopwatch = Stopwatch.StartNew();
        Console.Error.WriteLine($"[sdv-fixture] report dir: {_reportDir.Root}");
    }

    public async Task DisposeAsync()
    {
        // Write a minimal summary.json so the fixture path has discoverable run artifacts
        // (per-scenario screenshot dirs are already on disk). We don't call
        // HtmlReportGenerator — it lives in Runner which Runner.Dsl doesn't reference;
        // pulling Runner in just for one method would drag the entire CLI runtime into
        // mod test projects. Richer reports remain a Tier 2 followup (xUnit observer).
        // DisposeAsync must not throw — xUnit masks test failures with dispose exceptions.
        try
        {
            if (_reportDir is not null && _runStopwatch is not null)
            {
                _runStopwatch.Stop();
                var summary = new RunSummary(
                    RunId: _reportDir.RunId,
                    Started: _runStartedAt.ToString("o"),
                    DurationMs: (int)_runStopwatch.ElapsedMilliseconds,
                    Scenarios: Array.Empty<ScenarioOutcome>());
                var json = JsonSerializer.Serialize(summary, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                });
                File.WriteAllText(Path.Combine(_reportDir.Root, "summary.json"), json);
            }
        }
        catch (Exception ex)
        {
            // Best-effort: surface a warning but never throw from DisposeAsync.
            try { Console.Error.WriteLine($"[sdv-fixture] summary.json write failed: {ex.Message}"); }
            catch { }
        }

        SdvTestSession.ResetForTests();
        try { _session?.Dispose(); } catch { }
        try
        {
            if (_sdv is { HasExited: false })
            {
                _sdv.Kill();
                _sdv.WaitForExit(5000);
            }
        } catch { }
        _lifetimeCts?.Cancel();
        _lifetimeCts?.Dispose();
        await Task.CompletedTask;
    }
}
