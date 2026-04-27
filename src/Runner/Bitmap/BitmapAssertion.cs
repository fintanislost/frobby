using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using SdvTestFramework.Protocol.Reports;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SdvTestFramework.Runner.Bitmap;

/// <summary>Tiny abstraction over "call bitmap.capture, get path + dims" — lets tests shim the RPC.</summary>
public interface IBitmapRpcClient
{
    Task<BitmapCaptureResult> BitmapCaptureAsync(JsonElement? region, CancellationToken ct);
}

/// <summary>Production adapter wrapping a <see cref="JsonRpcSession"/>.</summary>
public sealed class SessionBitmapRpcClient : IBitmapRpcClient
{
    private readonly JsonRpcSession _session;
    public SessionBitmapRpcClient(JsonRpcSession session) => _session = session;

    public async Task<BitmapCaptureResult> BitmapCaptureAsync(JsonElement? region, CancellationToken ct)
    {
        // Build params via the standard ProtocolJson pattern; no hand-rolled MemoryStream.
        JsonElement? reqParams = region is { } r
            ? JsonSerializer.SerializeToElement(new { region = r }, ProtocolJson.Options)
            : null;

        var resp = await _session.InvokeAsync("bitmap.capture", reqParams, ct);
        if (resp.Error is { } e)
            throw new InvalidOperationException($"bitmap.capture failed: {e.Message}");
        if (resp.Result is not { } result)
            throw new InvalidOperationException("bitmap.capture returned no result");

        return JsonSerializer.Deserialize<BitmapCaptureResult>(result, ProtocolJson.Options)
            ?? throw new InvalidOperationException("bitmap.capture: null result deserialization");
    }
}

/// <summary>Pass/fail outcome from <see cref="BitmapAssertion.EvaluateAsync"/>.</summary>
/// <param name="Passed">True iff the assertion passed.</param>
/// <param name="FailureMessage">Human-readable failure message, null when passed.</param>
/// <param name="Diffs">Forensics PNG paths produced on failure (null otherwise — including update-baselines mode and other failure modes like missing-capture-path).</param>
public sealed record BitmapAssertionResult(bool Passed, string? FailureMessage, DiffSet? Diffs = null);

/// <summary>
/// Evaluator for the <c>bitmap</c> assertion type. Calls <c>bitmap.capture</c>, loads both
/// the capture PNG and the baseline PNG, runs the per-method diff (SSIM / pixel-exact /
/// dHash), and produces a result. Honors <paramref name="updateBaselines"/>: in that mode,
/// the capture bytes overwrite the baseline path (or create it if missing), and the
/// assertion short-circuits to pass.
/// </summary>
public static class BitmapAssertion
{
    public static async Task<BitmapAssertionResult> EvaluateAsync(
        IBitmapRpcClient rpc,
        ScenarioAssertion a,
        string scenarioPath,
        bool updateBaselines,
        string? diffOutputDir,
        DiffFormat runWideDiffFormat,
        string runWideTier,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(a.Baseline))
            return new BitmapAssertionResult(false, "bitmap assertion missing 'baseline' field");

        // Parse method up front. Unknown methods fail fast with a diagnostic.
        BitmapMethod method;
        try { method = BitmapMethodExtensions.ParseMethod(a.Method); }
        catch (ArgumentException ex) { return new BitmapAssertionResult(false, ex.Message); }

        var baselinePath = BaselineManager.ResolveBaseline(scenarioPath, a.Baseline);

        // 1. Capture via RPC.
        BitmapCaptureResult captureResp;
        try { captureResp = await rpc.BitmapCaptureAsync(a.Region, ct); }
        catch (Exception ex) { return new BitmapAssertionResult(false, $"bitmap.capture failed: {ex.Message}"); }

        var capturePath = captureResp.Path;
        if (string.IsNullOrEmpty(capturePath))
            return new BitmapAssertionResult(false, "bitmap.capture response missing 'path' field");
        if (!File.Exists(capturePath))
            return new BitmapAssertionResult(false, $"bitmap.capture path does not exist: {capturePath}");

        // 2. Update-mode short-circuit (unchanged).
        if (updateBaselines)
        {
            var bytes = await File.ReadAllBytesAsync(capturePath, ct);
            var existedBefore = File.Exists(baselinePath);
            BaselineManager.WriteBaseline(baselinePath, bytes);
            var action = existedBefore ? "updated" : "wrote";
            Console.WriteLine($"[bitmap] {action} baseline: {baselinePath}");
            return new BitmapAssertionResult(true, null);
        }

        // 3. Baseline existence check.
        if (!File.Exists(baselinePath))
            return new BitmapAssertionResult(false,
                $"baseline not found: {baselinePath} (re-run with --update-baselines to create it)");

        // 4. Effective tolerance (per-assertion > tier > method default). Per-method validation
        // happens inside Resolve and surfaces as ArgumentException with method name in the message.
        var perAssertionTier = a.Tier;
        var effectiveTier = !string.IsNullOrEmpty(perAssertionTier) ? perAssertionTier : runWideTier;
        double tolerance;
        try { tolerance = TierTolerance.Resolve(effectiveTier, method, a.Tolerance); }
        catch (ArgumentException ex) { return new BitmapAssertionResult(false, ex.Message); }

        // 5. Load + diff. Read raw bytes first so they're available for the diff renderer
        // on failure.
        byte[] baselineBytes, captureBytes;
        try
        {
            baselineBytes = await File.ReadAllBytesAsync(baselinePath, ct);
            captureBytes = await File.ReadAllBytesAsync(capturePath, ct);
        }
        catch (Exception ex)
        {
            return new BitmapAssertionResult(false, $"file read failed: {ex.Message}");
        }

        SsimResult? ssim = null;
        bool passed;
        string failureDetail;
        try
        {
            using var baseline = Image.Load<Rgba32>(baselineBytes);
            using var capture = Image.Load<Rgba32>(captureBytes);
            switch (method)
            {
                case BitmapMethod.Ssim:
                {
                    var s = SsimDiff.Compute(baseline, capture);
                    ssim = s;
                    // epsilon guards against float→double promotion rounding at the exact boundary;
                    // 1e-9 is well below float precision so real failures are not masked.
                    passed = s.Score + 1e-9 >= tolerance;
                    failureDetail = $"SSIM {s.Score:F4} < tolerance {tolerance:F4}";
                    break;
                }
                case BitmapMethod.PixelExact:
                {
                    int delta = PixelExactDiff.MaxChannelDelta(baseline, capture);
                    passed = delta <= tolerance;
                    failureDetail = $"pixel-exact max delta {delta} > tolerance {tolerance:F0}";
                    break;
                }
                case BitmapMethod.DHash:
                {
                    int dist = DHashDiff.HammingDistance(baseline, capture);
                    passed = dist <= tolerance;
                    failureDetail = $"dhash distance {dist} > tolerance {tolerance:F0}";
                    break;
                }
                default:
                    return new BitmapAssertionResult(false, $"unknown bitmap method enum: {method}");
            }
        }
        catch (ArgumentException ex)
        {
            return new BitmapAssertionResult(false, ex.Message + " — regenerate baseline with --update-baselines");
        }
        catch (Exception ex)
        {
            return new BitmapAssertionResult(false, $"bitmap diff failed: {ex.Message}");
        }

        if (passed)
            return new BitmapAssertionResult(true, null);

        // 6. Failure → render diffs if a target dir is provided.
        DiffSet? diffs = null;
        if (!string.IsNullOrEmpty(diffOutputDir))
        {
            var format = a.DiffFormat ?? runWideDiffFormat;
            try
            {
                Directory.CreateDirectory(diffOutputDir);
                diffs = DiffImageRenderer.Render(
                    baselineBytes, captureBytes, ssim, tolerance, method, format, diffOutputDir);
            }
            catch (Exception ex)
            {
                // Don't compound a bitmap failure with a diff-render failure. Log + continue.
                Console.Error.WriteLine($"[bitmap] diff render failed: {ex.Message}");
            }
        }

        return new BitmapAssertionResult(false, $"{failureDetail}; capture: {capturePath}", diffs);
    }
}
