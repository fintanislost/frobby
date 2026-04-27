using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Reports;

namespace SdvTestFramework.Runner.Reports;

/// <summary>
/// Runner-side orchestrator for screenshot capture. Calls <c>bitmap.capture</c> via
/// the RPC session, then copies the resulting PNG into the per-scenario report dir.
/// </summary>
public sealed class ScreenshotRecorder
{
    /// <summary>Test seam — production implementation calls <see cref="JsonRpcSession"/>.</summary>
    public interface IBitmapInvoker
    {
        Task<string?> CaptureAsync(bool allowUnfrozen, CancellationToken ct);
    }

    private readonly IBitmapInvoker _invoker;

    public ScreenshotRecorder(IBitmapInvoker invoker) => _invoker = invoker;

    /// <summary>Convenience constructor wrapping a real <see cref="JsonRpcSession"/>.</summary>
    public ScreenshotRecorder(JsonRpcSession session) : this(new SessionInvoker(session)) { }

    /// <summary>
    /// Capture the current framebuffer + copy to
    /// <c>&lt;run-dir&gt;/scenarios/&lt;scenario&gt;/screenshots/&lt;name&gt;.png</c>.
    /// Returns the absolute destination path, or null on capture failure (logs but
    /// doesn't throw — auto-captures shouldn't fail tests).
    /// </summary>
    public async Task<string?> CaptureAsync(
        RunDirectory runDir,
        string scenarioName,
        string fileNameWithoutExt,
        CancellationToken ct,
        bool allowUnfrozen = false)
    {
        string? source;
        try
        {
            source = await _invoker.CaptureAsync(allowUnfrozen, ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[screenshot] capture failed: {ex.Message}");
            return null;
        }
        if (source is null || !File.Exists(source))
            return null;

        var scenDir = runDir.ScenarioDir(scenarioName);
        var dest = Path.Combine(scenDir, "screenshots", $"{fileNameWithoutExt}.png");
        try
        {
            File.Copy(source, dest, overwrite: true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[screenshot] copy failed: {ex.Message}");
            return null;
        }
        return dest;
    }

    private sealed class SessionInvoker : IBitmapInvoker
    {
        private readonly JsonRpcSession _session;
        public SessionInvoker(JsonRpcSession session) => _session = session;

        public async Task<string?> CaptureAsync(bool allowUnfrozen, CancellationToken ct)
        {
            JsonElement? args = null;
            if (allowUnfrozen)
            {
                args = JsonSerializer.SerializeToElement(new { allow_unfrozen = true });
            }

            var resp = await _session.InvokeAsync("bitmap.capture", args, ct);
            if (resp.Error is not null) return null;
            if (resp.Result is not { } r) return null;
            if (!r.TryGetProperty("path", out var pathEl) || pathEl.ValueKind != JsonValueKind.String)
                return null;
            return pathEl.GetString();
        }
    }
}
