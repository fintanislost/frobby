using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using SdvTestFramework.Protocol.Reports;
using SdvTestFramework.Runner.Bitmap;
using SdvTestFramework.Runner.Reports;

namespace SdvTestFramework.Runner.Scenarios;

/// <summary>
/// Drives a <see cref="ScenarioSpec"/> over a connected <see cref="JsonRpcSession"/>: begins
/// scenario, optionally loads fixture, runs steps, runs assertions, ends scenario. Returns
/// a <see cref="ScenarioReport"/> summarizing pass/fail + per-assertion detail.
///
/// When a <see cref="RunDirectory"/> is supplied the runner also:
/// <list type="bullet">
///   <item>Populates <see cref="ScenarioReport.Steps"/> with per-step timing.</item>
///   <item>Auto-captures a screenshot after each successful <c>freeze.begin</c>.</item>
///   <item>Captures a screenshot on every assertion failure.</item>
///   <item>Captures screenshots for explicit <c>screenshot.capture</c> steps.</item>
/// </list>
/// </summary>
public sealed class ScenarioRunner
{
    private readonly JsonRpcSession _session;
    private readonly bool _updateBaselines;
    private readonly RunDirectory? _reportDir;
    private readonly ScreenshotRecorder? _recorder;
    private readonly DiffFormat _runWideDiffFormat;
    private readonly string _runWideTier;

    // Per-run mutable state — set at the start of RunAsync, cleared in finally.
    private string _scenarioPath = string.Empty;
    private ScenarioReport? _currentReport;
    private ScenarioSpec? _currentSpec;
    private int _assertionFailureCount;

    /// <summary>Construct a runner bound to an already-connected session.</summary>
    public ScenarioRunner(JsonRpcSession session) : this(session, updateBaselines: false) { }

    /// <summary>
    /// Construct a runner bound to a session, with bitmap-assertion mode.
    /// When <paramref name="updateBaselines"/> is true, missing/mismatched bitmap baselines
    /// are regenerated from captures instead of failing the assertion.
    /// </summary>
    public ScenarioRunner(JsonRpcSession session, bool updateBaselines)
        : this(session, updateBaselines, reportDir: null) { }

    /// <summary>
    /// 3-arg constructor — defaults <paramref name="reportDir"/>'s diff format to <see cref="DiffFormat.Files"/>.
    /// Kept for backwards compatibility with older call sites.
    /// </summary>
    public ScenarioRunner(JsonRpcSession session, bool updateBaselines, RunDirectory? reportDir)
        : this(session, updateBaselines, reportDir, DiffFormat.Files) { }

    /// <summary>
    /// 4-arg constructor — chains to the 5-arg constructor with the default tier
    /// <c>"generic"</c>. Kept for backwards compatibility.
    /// </summary>
    public ScenarioRunner(
        JsonRpcSession session,
        bool updateBaselines,
        RunDirectory? reportDir,
        DiffFormat runWideDiffFormat)
        : this(session, updateBaselines, reportDir, runWideDiffFormat, "generic") { }

    /// <summary>
    /// Full constructor. Supply a non-null <paramref name="reportDir"/> to enable per-step
    /// timing, auto-screenshot, and assertion-failure screenshot capture.
    /// <paramref name="runWideDiffFormat"/> determines the default diff format applied to
    /// failed bitmap assertions that don't override it via their own <c>diff_format</c> field.
    /// <paramref name="runWideTier"/> is the run-wide tier name resolved per-method via
    /// <see cref="TierTolerance"/> for bitmap assertions that don't override <c>tier</c>.
    /// </summary>
    public ScenarioRunner(
        JsonRpcSession session,
        bool updateBaselines,
        RunDirectory? reportDir,
        DiffFormat runWideDiffFormat,
        string runWideTier)
    {
        _session = session;
        _updateBaselines = updateBaselines;
        _reportDir = reportDir;
        _recorder = reportDir is not null ? new ScreenshotRecorder(session) : null;
        _runWideDiffFormat = runWideDiffFormat;
        _runWideTier = runWideTier;
    }

    /// <summary>Legacy single-arg entry — scenario path is unknown, bitmap assertions without absolute baseline paths will fail.</summary>
    public Task<ScenarioReport> RunAsync(ScenarioSpec spec, CancellationToken ct)
        => RunAsync(spec, scenarioPath: string.Empty, ct);

    /// <summary>
    /// Execute the scenario end-to-end: <c>scenario.begin</c> → optional <c>fixture.load</c> +
    /// wait-for-ready → steps → assertions → <c>scenario.end</c>. Never throws for scenario
    /// failure — errors are captured in the returned <see cref="ScenarioReport"/>.
    /// </summary>
    public async Task<ScenarioReport> RunAsync(ScenarioSpec spec, string scenarioPath, CancellationToken ct)
    {
        _scenarioPath = scenarioPath;
        _assertionFailureCount = 0;
        var report = new ScenarioReport { Name = spec.Name };
        _currentReport = report;
        _currentSpec = spec;
        var sw = Stopwatch.StartNew();

        try
        {
            // 1. scenario.begin
            var beginReq = ProtocolJson.ToElement(new ScenarioBeginRequest
            {
                Name = spec.Name,
                Seed = spec.Config.Seed,
                Fixture = spec.Fixture,
            });
            var beginResp = await _session.InvokeAsync("scenario.begin", beginReq, ct);
            if (beginResp.Error is { } e)
                throw new InvalidOperationException($"scenario.begin failed: {e.Message}");

            // 2. fixture.load (if the spec has one)
            if (!string.IsNullOrEmpty(spec.Fixture))
            {
                var fxReq = ProtocolJson.ToElement(new FixtureLoadRequest { Name = spec.Fixture });
                var fxResp = await _session.InvokeAsync("fixture.load", fxReq, ct);
                if (fxResp.Error is { } fe)
                    throw new InvalidOperationException($"fixture.load failed: {fe.Message}");

                // Poll state.player until the location is populated (proxy for world-ready).
                await WaitForWorldReady(ct);
            }

            // 3. steps
            int stepIndex = 0;
            foreach (var step in spec.Steps)
            {
                var stepSw = Stopwatch.StartNew();
                bool stepPassed = true;
                string? stepDetail = null;
                try
                {
                    // Client-side wait primitive — lets scripts pause between RPCs so async
                    // game-thread work (warps, loading coroutines) can complete before the next
                    // assertion. Game keeps running during the sleep; this is just an RPC gap.
                    if (step.Action == "wait.ms")
                    {
                        int ms = 0;
                        if (step.Args is { ValueKind: JsonValueKind.Object } args
                            && args.TryGetProperty("ms", out var msEl)
                            && msEl.TryGetInt32(out var parsed))
                            ms = parsed;
                        if (ms > 0) await Task.Delay(ms, ct);
                    }
                    else if (step.Action == "screenshot.capture")
                    {
                        // Explicit screenshot step — capture + record in report.
                        if (_recorder is not null && _reportDir is not null)
                        {
                            string name = "explicit";
                            if (step.Args is { ValueKind: JsonValueKind.Object } a
                                && a.TryGetProperty("name", out var nameEl)
                                && nameEl.ValueKind == JsonValueKind.String
                                && nameEl.GetString() is { } s && s.Length > 0)
                            {
                                name = s;
                            }
                            var path = await _recorder.CaptureAsync(_reportDir, spec.Name, name, ct);
                            if (path is not null)
                                report.Screenshots.Add(MakeRelativePath(_reportDir, path));
                        }
                    }
                    else
                    {
                        var resp = await _session.InvokeAsync(step.Action, step.Args, ct);
                        if (resp.Error is { } ex)
                            throw new InvalidOperationException($"step '{step.Action}' failed: {ex.Message}");

                        // Auto-capture after freeze.begin success so there's always a frozen-world
                        // screenshot for debugging.
                        if (step.Action == "freeze.begin" && _recorder is not null && _reportDir is not null)
                        {
                            var path = await _recorder.CaptureAsync(
                                _reportDir, spec.Name, $"step-{stepIndex:D2}-after-freeze", ct);
                            if (path is not null)
                                report.Screenshots.Add(MakeRelativePath(_reportDir, path));
                        }
                    }
                }
                catch (Exception ex)
                {
                    stepPassed = false;
                    stepDetail = ex.Message;
                    stepSw.Stop();
                    if (_reportDir is not null)
                        report.Steps.Add(new StepOutcome(step.Action, stepPassed, (int)stepSw.ElapsedMilliseconds, stepDetail));
                    // Re-throw: the outer catch handles report.Failures.
                    throw;
                }
                stepSw.Stop();
                if (_reportDir is not null)
                    report.Steps.Add(new StepOutcome(step.Action, stepPassed, (int)stepSw.ElapsedMilliseconds, stepDetail));
                stepIndex++;
            }

            // 4. assertions
            int assertionIndex = 0;
            foreach (var a in spec.Assertions)
            {
                report.AssertionsRun++;
                var (passed, detail) = await EvaluateAssertionAsync(a, assertionIndex, ct);
                if (passed) report.AssertionsPassed++;
                else report.Failures.Add($"{a.Type}: {detail ?? a.Message ?? "failed"}");
                assertionIndex++;
            }

            report.Passed = report.Failures.Count == 0;
        }
        catch (Exception ex)
        {
            report.Failures.Add(ex.Message);
            report.Passed = false;
        }
        finally
        {
            // scenario.end must always run (even on step/fixture/assertion failure) so the
            // harness-side scenario state is reset; otherwise the next scenario.begin wedges
            // with "scenario '<prev>' already active". Errors here are swallowed because the
            // report outcome is already decided.
            try
            {
                // Pass accumulated counts so the harness can surface them in its response.
                var endParams = System.Text.Json.JsonSerializer.SerializeToElement(
                    new { assertions_run = report.AssertionsRun, assertions_passed = report.AssertionsPassed },
                    ProtocolJson.Options);
                await _session.InvokeAsync("scenario.end", endParams, ct);
            }
            catch { /* best-effort cleanup */ }

            report.DurationMs = (int)sw.ElapsedMilliseconds;
            _currentReport = null;
            _currentSpec = null;
        }

        return report;
    }

    /// <summary>
    /// Poll <c>state.player</c> until the farmer's <c>location</c> field is a non-empty string.
    /// Used as a proxy for "world finished loading" post-<c>fixture.load</c>.
    /// Times out at 30s.
    /// </summary>
    private async Task WaitForWorldReady(CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var resp = await _session.InvokeAsync("state.player", params_: null, ct);
            if (resp.Result is { } r
                && r.TryGetProperty("location", out var loc)
                && loc.ValueKind == JsonValueKind.String
                && !string.IsNullOrEmpty(loc.GetString()))
            {
                return;
            }
            await Task.Delay(500, ct);
        }
        throw new TimeoutException("world never became ready after fixture.load");
    }

    /// <summary>
    /// Evaluate a single assertion. Currently supports:
    /// <list type="bullet">
    ///   <item><c>draw.contains</c> — delegates to <c>draw.assert_contains</c> RPC.</item>
    ///   <item><c>state</c> — minimal DSL: <c>state.&lt;method&gt;.&lt;path&gt; == '&lt;literal&gt;'</c>.</item>
    /// </list>
    /// </summary>
    private async Task<(bool Passed, string? Detail)> EvaluateAssertionAsync(
        ScenarioAssertion a, int assertionIndex, CancellationToken ct)
    {
        switch (a.Type)
        {
            case "draw.contains":
            {
                if (a.Filter is null) return (false, null);
                // Build the assert_contains params. Using raw JSON construction to keep the
                // filter's shape (arbitrary JsonElement) passing through unchanged.
                var payload = new
                {
                    filter = a.Filter,
                    min_count = a.MinCount,
                    message = a.Message,
                };
                var req = JsonSerializer.SerializeToElement(payload, ProtocolJson.Options);
                var resp = await _session.InvokeAsync("draw.assert_contains", req, ct);
                if (resp.Error is not null)
                {
                    await TryCaptureAssertionFailureAsync(ct);
                    return (false, null);
                }
                if (resp.Result is not { } r) return (false, null);
                bool passed = r.TryGetProperty("passed", out var p) && p.GetBoolean();
                if (!passed) await TryCaptureAssertionFailureAsync(ct);
                return (passed, null);
            }
            case "draw.not_contains":
            {
                if (a.Filter is null) return (false, null);
                var payload = new { filter = a.Filter, message = a.Message };
                var req = JsonSerializer.SerializeToElement(payload, ProtocolJson.Options);
                var resp = await _session.InvokeAsync("draw.assert_not_contains", req, ct);
                if (resp.Error is not null)
                {
                    await TryCaptureAssertionFailureAsync(ct);
                    return (false, null);
                }
                if (resp.Result is not { } r) return (false, null);
                bool passed = r.TryGetProperty("passed", out var p) && p.GetBoolean();
                if (!passed) await TryCaptureAssertionFailureAsync(ct);
                return (passed, null);
            }
            case "bitmap":
            {
                var rpc = new SessionBitmapRpcClient(_session);
                string? diffOutputDir = null;
                if (_reportDir is not null && _currentSpec is not null)
                {
                    diffOutputDir = Path.Combine(
                        _reportDir.ScenarioDir(_currentSpec.Name),
                        "diffs",
                        $"assertion-{assertionIndex:D2}-bitmap");
                }
                var result = await BitmapAssertion.EvaluateAsync(
                    rpc, a, _scenarioPath, _updateBaselines,
                    diffOutputDir, _runWideDiffFormat, _runWideTier, ct);
                if (result.Diffs is { } diffSet && _currentReport is not null)
                    _currentReport.Diffs.Add(diffSet);
                if (!result.Passed) await TryCaptureAssertionFailureAsync(ct);
                return (result.Passed, result.FailureMessage);
            }
            case "state":
            {
                // Minimal DSL: "state.<method>.<field>[.<subfield>...] == <literal>"
                //           or "state.<method>.<field>[.<subfield>...] != <literal>".
                // RHS literal: single/double-quoted string, integer, or boolean. Anything
                // more expressive is deferred — scenarios needing richer logic compose
                // multiple assertions.
                if (string.IsNullOrWhiteSpace(a.Expr)) return (false, null);

                // Split on the first occurrence of "!=" or "==" — "!=" checked first so that
                // "a != b" doesn't get parsed as "a !" "= b".
                bool negated;
                string[] parts;
                int neqIdx = a.Expr.IndexOf("!=", StringComparison.Ordinal);
                int eqIdx = a.Expr.IndexOf("==", StringComparison.Ordinal);
                if (neqIdx >= 0 && (eqIdx < 0 || neqIdx < eqIdx))
                {
                    negated = true;
                    parts = new[] { a.Expr.Substring(0, neqIdx), a.Expr.Substring(neqIdx + 2) };
                }
                else if (eqIdx >= 0)
                {
                    negated = false;
                    parts = new[] { a.Expr.Substring(0, eqIdx), a.Expr.Substring(eqIdx + 2) };
                }
                else
                {
                    return (false, null);
                }
                if (parts.Length != 2) return (false, null);

                var pathTokens = parts[0].Trim().Split('.');
                var rhs = parts[1].Trim();
                if (pathTokens.Length < 3 || pathTokens[0] != "state") return (false, null);

                var method = $"state.{pathTokens[1]}";
                var resp = await _session.InvokeAsync(method, params_: null, ct);
                if (resp.Error is not null || resp.Result is not { } root)
                {
                    await TryCaptureAssertionFailureAsync(ct);
                    return (false, null);
                }

                JsonElement cur = root;
                for (int i = 2; i < pathTokens.Length; i++)
                {
                    var token = pathTokens[i];
                    // Match "field[N]" → {field, index}. Regex is intentionally tight; no nested
                    // indexes, no slicing — scenarios can compose multiple assertions instead.
                    var m = System.Text.RegularExpressions.Regex.Match(token, @"^([A-Za-z_][A-Za-z0-9_]*)\[(\d+)\]$");
                    if (m.Success)
                    {
                        var fieldName = m.Groups[1].Value;
                        var index = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                        if (cur.ValueKind != JsonValueKind.Object) return (false, null);
                        if (!cur.TryGetProperty(fieldName, out var arr)) return (false, null);
                        if (arr.ValueKind != JsonValueKind.Array) return (false, null);
                        if (index < 0 || index >= arr.GetArrayLength()) return (false, null);
                        cur = arr[index];
                    }
                    else
                    {
                        if (cur.ValueKind != JsonValueKind.Object) return (false, null);
                        if (!cur.TryGetProperty(token, out var nested)) return (false, null);
                        cur = nested;
                    }
                }

                // Quoted string literal
                if ((rhs.StartsWith('\'') && rhs.EndsWith('\'')) ||
                    (rhs.StartsWith('"') && rhs.EndsWith('"')))
                {
                    var literal = rhs.Substring(1, rhs.Length - 2);
                    bool eq = cur.ValueKind == JsonValueKind.String && cur.GetString() == literal;
                    bool result = negated ? !eq : eq;
                    if (!result) await TryCaptureAssertionFailureAsync(ct);
                    return (result, null);
                }
                // Integer literal
                if (long.TryParse(rhs, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out var intLit))
                {
                    bool eq = cur.ValueKind == JsonValueKind.Number
                        && cur.TryGetInt64(out var cv) && cv == intLit;
                    bool result = negated ? !eq : eq;
                    if (!result) await TryCaptureAssertionFailureAsync(ct);
                    return (result, null);
                }
                // Boolean literal
                if (bool.TryParse(rhs, out var boolLit))
                {
                    bool eq = (cur.ValueKind == JsonValueKind.True && boolLit)
                        || (cur.ValueKind == JsonValueKind.False && !boolLit);
                    bool result = negated ? !eq : eq;
                    if (!result) await TryCaptureAssertionFailureAsync(ct);
                    return (result, null);
                }
                return (false, null);
            }
            default:
                return (false, null);
        }
    }

    /// <summary>
    /// Best-effort screenshot on assertion failure. Swallows all exceptions — a capture
    /// failure must never cause the test itself to fail or throw.
    /// </summary>
    private async Task TryCaptureAssertionFailureAsync(CancellationToken ct)
    {
        if (_recorder is null || _reportDir is null || _currentReport is null || _currentSpec is null)
            return;
        try
        {
            var name = $"assertion-fail-{_assertionFailureCount:D2}";
            _assertionFailureCount++;
            var path = await _recorder.CaptureAsync(_reportDir, _currentSpec.Name, name, ct);
            if (path is not null)
                _currentReport.Screenshots.Add(MakeRelativePath(_reportDir, path));
        }
        catch
        {
            // Best-effort: do not let screenshot failure affect test outcome.
        }
    }

    /// <summary>Return a forward-slash path relative to the run directory root.</summary>
    private static string MakeRelativePath(RunDirectory rd, string absPath)
        => Path.GetRelativePath(rd.Root, absPath).Replace('\\', '/');
}
