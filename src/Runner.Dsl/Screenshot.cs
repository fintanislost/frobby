using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Reports;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>Ambient static DSL for capturing screenshots into the per-run report directory.</summary>
public static class Screenshot
{
    /// <summary>
    /// Capture the current framebuffer + save to
    /// <c>&lt;report-dir&gt;/scenarios/&lt;current-scenario&gt;/screenshots/&lt;name&gt;.png</c>.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>No-op (with stderr warning) when no report dir is configured (e.g. unit tests).</item>
    ///   <item>No-op (with stderr warning) if the harness response is malformed or the captured file is missing on disk.</item>
    ///   <item>Throws <see cref="System.InvalidOperationException"/> if no scenario is active or the file copy fails.</item>
    ///   <item>Throws <see cref="SdvRpcException"/> if the underlying <c>bitmap.capture</c> RPC fails (e.g. not in FREEZE phase).</item>
    /// </list>
    /// </remarks>
    public static async Task Capture(string name, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        if (s.ReportDir is null)
        {
            Console.Error.WriteLine($"[screenshot] Capture('{name}') called but no report dir is configured");
            return;
        }
        if (s.CurrentScenarioName is null)
            throw new InvalidOperationException("Screenshot.Capture requires an active [Scenario] (no scenario name set)");

        // Call bitmap.capture via the session; response envelope is the result directly
        // (ISdvTestInvoker.InvokeAsync returns the unwrapped JsonElement per its contract).
        var resp = await s.InvokeAsync("bitmap.capture", null, ct);
        if (!resp.TryGetProperty("path", out var pathEl) || pathEl.ValueKind != JsonValueKind.String)
        {
            Console.Error.WriteLine($"[screenshot] bitmap.capture returned no path");
            return;
        }
        var sourcePath = pathEl.GetString()!;
        if (!File.Exists(sourcePath))
        {
            Console.Error.WriteLine($"[screenshot] capture path missing: {sourcePath}");
            return;
        }

        var scenDir = s.ReportDir.ScenarioDir(s.CurrentScenarioName);
        var dest = Path.Combine(scenDir, "screenshots", $"{name}.png");
        try
        {
            File.Copy(sourcePath, dest, overwrite: true);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Screenshot.Capture failed to copy '{sourcePath}' to '{dest}': {ex.Message}", ex);
        }
    }
}
