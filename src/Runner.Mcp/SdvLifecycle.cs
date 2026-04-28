using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;

namespace SdvTestFramework.Runner.Mcp;

/// <summary>
/// Lazy SDV launcher for the MCP server. First tool call that needs a session triggers
/// launch; subsequent calls reuse. Thread-safe via a semaphore (stdio dispatch is serial
/// today but MCP clients may pipeline).
/// </summary>
/// <remarks>
/// Tests subclass this and override <see cref="InvokeAsync"/> to short-circuit the launch
/// path and return canned responses.
/// </remarks>
public class SdvLifecycle : IAsyncDisposable
{
    private readonly SemaphoreSlim _launchLock = new(1, 1);
    private Process? _sdv;
    private JsonRpcSession? _session;

    /// <summary>Ensure SDV is running + a session is connected; return the session.</summary>
    public virtual async Task<JsonRpcSession> EnsureRunningAsync(CancellationToken ct)
    {
        if (_session is not null) return _session;

        await _launchLock.WaitAsync(ct);
        try
        {
            if (_session is not null) return _session;

            var socket = Path.Combine(Path.GetTempPath(), $"sdv-mcp-{Guid.NewGuid():N}.sock");

            var modsPath = Environment.GetEnvironmentVariable("SDV_MODS_PATH");
            if (string.IsNullOrEmpty(modsPath))
            {
                modsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".cache", "sdv-test-framework", "mods");
            }
            Directory.CreateDirectory(modsPath);
            HarnessDeployer.Deploy(modsPath);
            ExtraModDeployer.DeployMany(
                modsPath,
                ExtraModDeployer.ParseEnvList(Environment.GetEnvironmentVariable("SDV_EXTRA_MODS")));

            _sdv = SdvLauncher.Launch(socket, installPath: null, modsPath: modsPath);

            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(TimeSpan.FromSeconds(60));

            for (int i = 0; i < 120 && !File.Exists(socket); i++)
                await Task.Delay(500, connectCts.Token);
            if (!File.Exists(socket))
                throw new TimeoutException("SDV never opened the MCP test socket");

            _session = await UnixSocketRpc.ConnectAsync(socket, connectCts.Token);
            var readyTcs = new TaskCompletionSource<JsonRpcNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
            _session.NotificationReceived += n => { if (n.Method == "ready") readyTcs.TrySetResult(n); };
            _ = _session.RunAsync(ct);
            await readyTcs.Task.WaitAsync(TimeSpan.FromSeconds(60), ct);

            return _session;
        }
        finally { _launchLock.Release(); }
    }

    /// <summary>
    /// Invoke an RPC method. Launches SDV on first call; subsequent calls reuse the session.
    /// Tests override this to short-circuit the launch path. On RPC error, throws <see cref="SdvRpcException"/>.
    /// </summary>
    public virtual async Task<JsonElement> InvokeAsync(string method, JsonElement? p, CancellationToken ct)
    {
        var session = await EnsureRunningAsync(ct);
        var resp = await session.InvokeAsync(method, p, ct);
        if (resp.Error is { } e) throw SdvRpcException.Create(method, e);
        return resp.Result ?? JsonDocument.Parse("{}").RootElement.Clone();
    }

    public async ValueTask DisposeAsync()
    {
        try { _session?.Dispose(); } catch { }
        try
        {
            if (_sdv is not null)
                SdvLauncher.Terminate(_sdv);
        } catch { }
        _launchLock.Dispose();
        await Task.CompletedTask;
    }
}

/// <summary>
/// Typed exception for RPC errors surfaced through the MCP server. Mirrors
/// <c>SdvTestFramework.Runner.Dsl.SdvRpcException</c> — we don't take a dependency on
/// Runner.Dsl since the MCP server is orthogonal.
/// </summary>
public sealed class SdvRpcException : Exception
{
    public string Method { get; }
    public JsonRpcErrorCode Code { get; }

    public SdvRpcException(string method, JsonRpcErrorCode code, string message)
        : base($"RPC '{method}' failed ({code}): {message}")
    {
        Method = method;
        Code = code;
    }

    public static SdvRpcException Create(string method, JsonRpcError error)
        => new(method, error.Code, error.Message);
}
