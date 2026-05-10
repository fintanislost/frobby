using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
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
///   <item>Auto-captures a best-effort screenshot after successful visible-action steps.</item>
///   <item>Captures the <c>freeze.begin</c> step without unfrozen capture bypass.</item>
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
    private readonly List<SaveFolderRestore> _pendingSaveRestores = new();

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
        _pendingSaveRestores.Clear();
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

                // Poll state.player until the location is populated and SDV is no longer
                // mid-warp. The location field can become non-empty before the save-load
                // transition is fully settled; starting steps during that window can wedge
                // later freeze.begin calls behind Game1.isWarping.
                await WaitForWorldReady(ct);
            }

            // 3. steps
            int stepIndex = 0;
            foreach (var step in spec.Steps)
            {
                var stepSw = Stopwatch.StartNew();
                bool stepPassed = true;
                string? stepDetail = DescribeStep(step);
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
                    else if (step.Action == "wait.location")
                    {
                        await InvokeWaitLocationAsync(step, ct);
                    }
                    else if (step.Action == "wait.player")
                    {
                        await InvokeWaitPlayerAsync(step, ct);
                    }
                    else if (step.Action == "wait.npc_location")
                    {
                        await InvokeWaitNpcLocationAsync(step, ct);
                    }
                    else if (step.Action == "wait.location_content")
                    {
                        await InvokeWaitLocationContentAsync(step, ct);
                    }
                    else if (step.Action == "wait.visual_effects")
                    {
                        await InvokeWaitVisualEffectsAsync(step, ct);
                    }
                    else if (step.Action == "wait.event_active")
                    {
                        await InvokeWaitEventActiveAsync(step, ct);
                    }
                    else if (step.Action == "wait.event_complete")
                    {
                        await InvokeWaitEventCompleteAsync(step, ct);
                    }
                    else if (step.Action == "screenshot.capture")
                    {
                        await CaptureExplicitScreenshotAsync(
                            step,
                            spec.Name,
                            report,
                            ct,
                            ScreenshotCaptureMode.Immediate);
                    }
                    else if (step.Action == "screenshot.capture_next_frame")
                    {
                        await CaptureExplicitScreenshotAsync(
                            step,
                            spec.Name,
                            report,
                            ct,
                            ScreenshotCaptureMode.NextFrame);
                    }
                    else if (step.Action == "ui.wait_text")
                    {
                        await WaitForUiTextAsync(step, ct);
                    }
                    else if (step.Action == "wait.menu")
                    {
                        await InvokeWaitMenuAsync(step, ct);
                    }
                    else if (step.Action == "ui.click_text")
                    {
                        var uiText = await WaitForUiTextAsync(step, ct);
                        var clickParams = ProtocolJson.ToElement(new InputClickTextRequest
                        {
                            Text = uiText.Text,
                            TextEquals = uiText.TextEquals,
                            TextMatches = uiText.TextMatches,
                            Button = uiText.Button,
                            CaseSensitive = uiText.CaseSensitive,
                            Occurrence = uiText.Occurrence,
                            InRect = uiText.InRect,
                            BoundsWithinRect = uiText.BoundsWithinRect,
                            BoundsIntersectsRect = uiText.BoundsIntersectsRect,
                        });
                        var resp = await _session.InvokeAsync("input.click_text", clickParams, ct);
                        if (resp.Error is { } ex)
                            throw new InvalidOperationException($"step '{step.Action}' failed: {ex.Message}");
                    }
                    else if (step.Action == "ui.hover_text")
                    {
                        var uiText = await WaitForUiTextAsync(step, ct);
                        var hoverParams = ProtocolJson.ToElement(new InputHoverTextRequest
                        {
                            Text = uiText.Text,
                            TextEquals = uiText.TextEquals,
                            TextMatches = uiText.TextMatches,
                            CaseSensitive = uiText.CaseSensitive,
                            Occurrence = uiText.Occurrence,
                            InRect = uiText.InRect,
                            BoundsWithinRect = uiText.BoundsWithinRect,
                            BoundsIntersectsRect = uiText.BoundsIntersectsRect,
                        });
                        var resp = await _session.InvokeAsync("input.hover_text", hoverParams, ct);
                        if (resp.Error is { } ex)
                            throw new InvalidOperationException($"step '{step.Action}' failed: {ex.Message}");
                    }
                    else if (step.Action == "ui.acknowledge" || step.Action == "event.advance")
                    {
                        await InvokeMenuAdvanceAsync(step, ct);
                    }
                    else if (step.Action == "time.next_day")
                    {
                        await InvokeTimeNextDayAsync(step, ct);
                    }
                    else if (step.Action == "fixture.save_reload")
                    {
                        await InvokeFixtureSaveReloadAsync(step, spec.Fixture, ct);
                    }
                    else if (step.Action == "freeze.begin")
                    {
                        await InvokeFreezeBeginAsync(step, ct);
                    }
                    else if (step.Action == "state.assert")
                    {
                        await InvokeStateAssertAsync(step, ct);
                    }
                    else if (step.Action == "combat.attack")
                    {
                        await InvokeCombatAttackAsync(step, ct);
                    }
                    else
                    {
                        var resp = await _session.InvokeAsync(step.Action, step.Args, ct);
                        if (resp.Error is { } ex)
                            throw new InvalidOperationException($"step '{step.Action}' failed: {ex.Message}");
                    }

                    if (step.Action != "screenshot.capture" && step.Action != "screenshot.capture_next_frame")
                        await TryCaptureStepScreenshotAsync(report, spec.Name, step, stepIndex, ct);
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
                report.Assertions.Add(new AssertionOutcome(
                    DescribeAssertion(a),
                    passed,
                    passed ? a.Message : detail ?? a.Message));
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

            try
            {
                RestorePendingSaveFolders();
            }
            catch (Exception ex)
            {
                report.Failures.Add($"save folder restore failed: {ex.Message}");
                report.Passed = false;
            }

            report.DurationMs = (int)sw.ElapsedMilliseconds;
            _currentReport = null;
            _currentSpec = null;
        }

        return report;
    }

    /// <summary>
    /// Poll <c>state.player</c> until the farmer's <c>location</c> field is a non-empty string
    /// and <c>freeze.status</c> reports that SDV is not mid-warp.
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
                && !string.IsNullOrEmpty(loc.GetString())
                && await IsWarpSettledAsync(ct))
            {
                return;
            }
            await Task.Delay(100, ct);
        }
        throw new TimeoutException("world never became ready after fixture.load");
    }

    private async Task<bool> IsWarpSettledAsync(CancellationToken ct)
    {
        var resp = await _session.InvokeAsync("freeze.status", params_: null, ct);
        if (resp.Error is not null || resp.Result is not { } status)
            return false;

        if (!status.TryGetProperty("is_warping", out var isWarping)
            || (isWarping.ValueKind != JsonValueKind.True && isWarping.ValueKind != JsonValueKind.False))
        {
            return IsFadeSettled(status);
        }

        return !isWarping.GetBoolean() && IsFadeSettled(status);
    }

    private static bool IsFadeSettled(JsonElement status)
    {
        if (!status.TryGetProperty("is_fading", out var isFading)
            || (isFading.ValueKind != JsonValueKind.True && isFading.ValueKind != JsonValueKind.False))
        {
            return true;
        }

        return !isFading.GetBoolean();
    }

    private async Task InvokeWaitLocationAsync(ScenarioStep step, CancellationToken ct)
    {
        var args = step.Args is { ValueKind: JsonValueKind.Object } obj
            ? JsonSerializer.Deserialize<WaitLocationStepArgs>(obj.GetRawText(), ProtocolJson.Options)
                ?? new WaitLocationStepArgs()
            : new WaitLocationStepArgs();

        if (string.IsNullOrWhiteSpace(args.Location))
            throw new InvalidOperationException("wait.location requires args.location");
        if (args.TimeoutMs < 1)
            throw new InvalidOperationException("wait.location requires args.timeout_ms >= 1");
        if (args.PollMs < 1)
            throw new InvalidOperationException("wait.location requires args.poll_ms >= 1");

        var elapsed = Stopwatch.StartNew();
        PlayerState? lastObserved = null;
        while (elapsed.ElapsedMilliseconds < args.TimeoutMs)
        {
            ct.ThrowIfCancellationRequested();
            var resp = await _session.InvokeAsync("state.player", params_: null, ct);
            if (resp.Error is { } error)
                throw new InvalidOperationException($"wait.location failed during state.player: {error.Message}");
            if (resp.Result is { } result)
                lastObserved = JsonSerializer.Deserialize<PlayerState>(result.GetRawText(), ProtocolJson.Options);

            if (lastObserved is not null
                && string.Equals(lastObserved.Location, args.Location, StringComparison.Ordinal)
                && (args.X is null || args.X == lastObserved.Tile.X)
                && (args.Y is null || args.Y == lastObserved.Tile.Y)
                && await IsWarpSettledAsync(ct))
            {
                return;
            }

            await Task.Delay(args.PollMs, ct);
        }

        var expectedTile = args.X is not null && args.Y is not null
            ? $" at {args.X},{args.Y}"
            : string.Empty;
        var last = lastObserved is null
            ? "nothing"
            : $"{lastObserved.Location} at {lastObserved.Tile.X},{lastObserved.Tile.Y}";
        throw new TimeoutException(
            $"wait.location timed out after {args.TimeoutMs}ms waiting for location {args.Location}{expectedTile}; " +
            $"last observed {last}");
    }

    private async Task InvokeWaitPlayerAsync(ScenarioStep step, CancellationToken ct)
    {
        var args = step.Args is { ValueKind: JsonValueKind.Object } obj
            ? JsonSerializer.Deserialize<WaitPlayerStepArgs>(obj.GetRawText(), ProtocolJson.Options)
                ?? new WaitPlayerStepArgs()
            : new WaitPlayerStepArgs();

        ValidateWaitPlayerArgs(args);

        var elapsed = Stopwatch.StartNew();
        JsonElement? last = null;
        while (elapsed.ElapsedMilliseconds < args.TimeoutMs)
        {
            ct.ThrowIfCancellationRequested();
            var resp = await _session.InvokeAsync("state.player", params_: null, ct);
            if (resp.Error is { } error)
                throw new InvalidOperationException($"wait.player failed during state.player: {error.Message}");

            if (resp.Result is { } root)
            {
                last = root.Clone();
                if (PlayerStateMatches(root, args))
                    return;
            }

            await Task.Delay(args.PollMs, ct);
        }

        throw new TimeoutException(
            $"wait.player timed out after {args.TimeoutMs}ms waiting for player{FormatWaitPlayerFilters(args)}; " +
            $"last observed {FormatObservedPlayer(last)}");
    }

    private static void ValidateWaitPlayerArgs(WaitPlayerStepArgs args)
    {
        if (args.TimeoutMs < 1)
            throw new InvalidOperationException("wait.player requires args.timeout_ms >= 1");
        if (args.PollMs < 1)
            throw new InvalidOperationException("wait.player requires args.poll_ms >= 1");
        if ((args.X is null) != (args.Y is null))
            throw new InvalidOperationException("wait.player requires both args.x and args.y when filtering by tile");
    }

    private static bool PlayerStateMatches(JsonElement root, WaitPlayerStepArgs args)
    {
        return StringFilterMatches(root, "location", args.Location)
            && NumberFilterMatches(root, "health", args.Health, args.HealthLt, args.HealthLte, args.HealthGt, args.HealthGte)
            && TileFilterMatches(root, args.X, args.Y);
    }

    private static string FormatWaitPlayerFilters(WaitPlayerStepArgs args)
    {
        var filters = new List<string>();
        if (args.Location is not null) filters.Add($"location={args.Location}");
        AddNumberFilters(filters, "health", args.Health, args.HealthLt, args.HealthLte, args.HealthGt, args.HealthGte);
        if (args.X is not null && args.Y is not null) filters.Add($"tile={args.X},{args.Y}");
        return filters.Count == 0 ? string.Empty : $" matching {string.Join(", ", filters)}";
    }

    private static string FormatObservedPlayer(JsonElement? root)
    {
        if (root is null || root.Value.ValueKind != JsonValueKind.Object)
            return "nothing";

        var health = root.Value.TryGetProperty("health", out var h) && h.TryGetInt32(out var hv)
            ? hv.ToString(CultureInfo.InvariantCulture)
            : "?";
        var location = root.Value.TryGetProperty("location", out var l) && l.ValueKind == JsonValueKind.String
            ? l.GetString() ?? string.Empty
            : "?";
        var tile = "?";
        if (root.Value.TryGetProperty("tile", out var t)
            && t.ValueKind == JsonValueKind.Object
            && t.TryGetProperty("x", out var x)
            && t.TryGetProperty("y", out var y)
            && x.TryGetInt32(out var xv)
            && y.TryGetInt32(out var yv))
        {
            tile = $"{xv},{yv}";
        }

        return $"health={health} location={location} tile={tile}";
    }

    private async Task InvokeWaitNpcLocationAsync(ScenarioStep step, CancellationToken ct)
    {
        var args = step.Args is { ValueKind: JsonValueKind.Object } obj
            ? JsonSerializer.Deserialize<WaitNpcLocationStepArgs>(obj.GetRawText(), ProtocolJson.Options)
                ?? new WaitNpcLocationStepArgs()
            : new WaitNpcLocationStepArgs();

        if (string.IsNullOrWhiteSpace(args.Name))
            throw new InvalidOperationException("wait.npc_location requires args.name");
        if (string.IsNullOrWhiteSpace(args.Location))
            throw new InvalidOperationException("wait.npc_location requires args.location");
        if (args.TimeoutMs < 1)
            throw new InvalidOperationException("wait.npc_location requires args.timeout_ms >= 1");
        if (args.PollMs < 1)
            throw new InvalidOperationException("wait.npc_location requires args.poll_ms >= 1");

        var npcParams = ProtocolJson.ToElement(new { name = args.Name });
        var elapsed = Stopwatch.StartNew();
        NpcState? lastObserved = null;
        while (elapsed.ElapsedMilliseconds < args.TimeoutMs)
        {
            ct.ThrowIfCancellationRequested();
            var resp = await _session.InvokeAsync("state.npc", npcParams, ct);
            if (resp.Error is { } error)
                throw new InvalidOperationException($"wait.npc_location failed during state.npc: {error.Message}");
            if (resp.Result is { } result)
                lastObserved = JsonSerializer.Deserialize<NpcState>(result.GetRawText(), ProtocolJson.Options);

            if (lastObserved is not null
                && string.Equals(lastObserved.Location, args.Location, StringComparison.Ordinal)
                && (args.X is null || args.X == lastObserved.Tile.X)
                && (args.Y is null || args.Y == lastObserved.Tile.Y)
                && await IsWarpSettledAsync(ct))
            {
                return;
            }

            await Task.Delay(args.PollMs, ct);
        }

        var last = lastObserved is null
            ? "nothing"
            : $"{lastObserved.Location} at {lastObserved.Tile.X},{lastObserved.Tile.Y}";
        throw new TimeoutException(
            $"wait.npc_location timed out after {args.TimeoutMs}ms waiting for {args.Name} in {args.Location}{FormatOptionalTile(args.X, args.Y)}; " +
            $"last observed {last}");
    }

    private async Task InvokeWaitLocationContentAsync(ScenarioStep step, CancellationToken ct)
    {
        var args = step.Args is { ValueKind: JsonValueKind.Object } obj
            ? JsonSerializer.Deserialize<WaitLocationContentStepArgs>(obj.GetRawText(), ProtocolJson.Options)
                ?? new WaitLocationContentStepArgs()
            : new WaitLocationContentStepArgs();

        ValidateWaitLocationContentArgs(args);

        var request = ProtocolJson.ToElement(new { name = args.Location });
        var elapsed = Stopwatch.StartNew();
        int lastMatched = 0;
        int lastTotal = 0;
        while (elapsed.ElapsedMilliseconds < args.TimeoutMs)
        {
            ct.ThrowIfCancellationRequested();
            var resp = await _session.InvokeAsync("state.location", request, ct);
            if (resp.Error is { } error)
                throw new InvalidOperationException($"wait.location_content failed during state.location: {error.Message}");

            if (resp.Result is { } root)
            {
                lastMatched = CountLocationContentMatches(root, args, out lastTotal);
                var withinMin = lastMatched >= args.MinCount;
                var withinMax = args.MaxCount is null || lastMatched <= args.MaxCount.Value;
                if (withinMin && withinMax)
                    return;
            }

            await Task.Delay(args.PollMs, ct);
        }

        throw new TimeoutException(
            $"wait.location_content timed out after {args.TimeoutMs}ms waiting for {FormatExpectedContentCount(args)} " +
            $"{args.Collection} in {args.Location}{FormatLocationContentFilters(args)}; " +
            $"last observed {lastMatched} matched out of {lastTotal} {args.Collection}");
    }

    private static void ValidateWaitLocationContentArgs(WaitLocationContentStepArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.Location))
            throw new InvalidOperationException("wait.location_content requires args.location");
        if (string.IsNullOrWhiteSpace(args.Collection))
            throw new InvalidOperationException("wait.location_content requires args.collection");
        if (!AllowedLocationContentCollections.Contains(args.Collection))
            throw new InvalidOperationException("wait.location_content requires args.collection to be one of objects, resource_clumps, monsters, critters, debris");
        if (args.MinCount < 0)
            throw new InvalidOperationException("wait.location_content requires args.min_count >= 0");
        if (args.MaxCount is not null && args.MaxCount < 0)
            throw new InvalidOperationException("wait.location_content requires args.max_count >= 0");
        if (args.MaxCount is not null && args.MaxCount < args.MinCount)
            throw new InvalidOperationException("wait.location_content requires args.max_count >= args.min_count");
        if (args.TimeoutMs < 1)
            throw new InvalidOperationException("wait.location_content requires args.timeout_ms >= 1");
        if (args.PollMs < 1)
            throw new InvalidOperationException("wait.location_content requires args.poll_ms >= 1");
        if ((args.X is null) != (args.Y is null))
            throw new InvalidOperationException("wait.location_content requires both args.x and args.y when filtering by tile");
    }

    private static int CountLocationContentMatches(JsonElement root, WaitLocationContentStepArgs args, out int totalCount)
    {
        totalCount = 0;
        if (args.Collection is null
            || !root.TryGetProperty(args.Collection, out var array)
            || array.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        var matched = 0;
        foreach (var element in array.EnumerateArray())
        {
            totalCount++;
            if (LocationContentElementMatches(element, args))
                matched++;
        }

        return matched;
    }

    private static bool LocationContentElementMatches(JsonElement element, WaitLocationContentStepArgs args)
    {
        return StringFilterMatches(element, "name", args.Name)
            && StringFilterMatches(element, "type", args.Type)
            && StringFilterMatches(element, "kind", args.Kind)
            && StringFilterMatches(element, "id", args.Id)
            && StringFilterMatches(element, "qualified_id", args.QualifiedId)
            && NumberFilterMatches(element, "health", args.Health, args.HealthLt, args.HealthLte, args.HealthGt, args.HealthGte)
            && NumberFilterMatches(element, "max_health", args.MaxHealth, args.MaxHealthLt, args.MaxHealthLte, args.MaxHealthGt, args.MaxHealthGte)
            && NumberFilterMatches(element, "damage", args.Damage, args.DamageLt, args.DamageLte, args.DamageGt, args.DamageGte)
            && StringFilterMatches(element, "runtime_type", args.RuntimeType)
            && NumberFilterMatches(element, "stack", args.Stack, args.StackLt, args.StackLte, args.StackGt, args.StackGte)
            && NumberFilterMatches(element, "quality", args.Quality, args.QualityLt, args.QualityLte, args.QualityGt, args.QualityGte)
            && NumberFilterMatches(element, "category", args.Category, args.CategoryLt, args.CategoryLte, args.CategoryGt, args.CategoryGte)
            && StringFilterMatches(element, "sprite_texture", args.SpriteTexture)
            && TileFilterMatches(element, args.X, args.Y);
    }

    private static bool StringFilterMatches(JsonElement element, string property, string? expected)
    {
        if (expected is null)
            return true;

        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            && string.Equals(value.GetString(), expected, StringComparison.Ordinal);
    }

    private static bool NumberFilterMatches(
        JsonElement element,
        string property,
        int? expected,
        int? lessThan,
        int? lessThanOrEqual,
        int? greaterThan,
        int? greaterThanOrEqual)
    {
        if (expected is null
            && lessThan is null
            && lessThanOrEqual is null
            && greaterThan is null
            && greaterThanOrEqual is null)
            return true;

        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(property, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var actual))
        {
            return false;
        }

        return (expected is null || actual == expected.Value)
            && (lessThan is null || actual < lessThan.Value)
            && (lessThanOrEqual is null || actual <= lessThanOrEqual.Value)
            && (greaterThan is null || actual > greaterThan.Value)
            && (greaterThanOrEqual is null || actual >= greaterThanOrEqual.Value);
    }

    private static bool TileFilterMatches(JsonElement element, int? x, int? y)
    {
        if (x is null && y is null)
            return true;

        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("tile", out var tile)
            && tile.ValueKind == JsonValueKind.Object
            && tile.TryGetProperty("x", out var tileX)
            && tile.TryGetProperty("y", out var tileY)
            && tileX.TryGetInt32(out var actualX)
            && tileY.TryGetInt32(out var actualY)
            && actualX == x
            && actualY == y;
    }

    private static string FormatExpectedContentCount(WaitLocationContentStepArgs args)
        => args.MaxCount is null
            ? $"at least {args.MinCount}"
            : args.MinCount == args.MaxCount.Value
                ? $"exactly {args.MinCount}"
                : $"between {args.MinCount} and {args.MaxCount.Value}";

    private static string FormatLocationContentFilters(WaitLocationContentStepArgs args)
    {
        var filters = new List<string>();
        if (args.Name is not null) filters.Add($"name={args.Name}");
        if (args.Type is not null) filters.Add($"type={args.Type}");
        if (args.Kind is not null) filters.Add($"kind={args.Kind}");
        if (args.Id is not null) filters.Add($"id={args.Id}");
        if (args.QualifiedId is not null) filters.Add($"qualified_id={args.QualifiedId}");
        AddNumberFilters(filters, "health", args.Health, args.HealthLt, args.HealthLte, args.HealthGt, args.HealthGte);
        AddNumberFilters(filters, "max_health", args.MaxHealth, args.MaxHealthLt, args.MaxHealthLte, args.MaxHealthGt, args.MaxHealthGte);
        AddNumberFilters(filters, "damage", args.Damage, args.DamageLt, args.DamageLte, args.DamageGt, args.DamageGte);
        if (args.RuntimeType is not null) filters.Add($"runtime_type={args.RuntimeType}");
        AddNumberFilters(filters, "stack", args.Stack, args.StackLt, args.StackLte, args.StackGt, args.StackGte);
        AddNumberFilters(filters, "quality", args.Quality, args.QualityLt, args.QualityLte, args.QualityGt, args.QualityGte);
        AddNumberFilters(filters, "category", args.Category, args.CategoryLt, args.CategoryLte, args.CategoryGt, args.CategoryGte);
        if (args.SpriteTexture is not null) filters.Add($"sprite_texture={args.SpriteTexture}");
        if (args.X is not null && args.Y is not null) filters.Add($"tile={args.X},{args.Y}");
        return filters.Count == 0 ? string.Empty : $" matching {string.Join(", ", filters)}";
    }

    private static void AddNumberFilters(
        List<string> filters,
        string name,
        int? expected,
        int? lessThan,
        int? lessThanOrEqual,
        int? greaterThan,
        int? greaterThanOrEqual)
    {
        if (expected is not null) filters.Add($"{name}={expected}");
        if (lessThan is not null) filters.Add($"{name}_lt={lessThan}");
        if (lessThanOrEqual is not null) filters.Add($"{name}_lte={lessThanOrEqual}");
        if (greaterThan is not null) filters.Add($"{name}_gt={greaterThan}");
        if (greaterThanOrEqual is not null) filters.Add($"{name}_gte={greaterThanOrEqual}");
    }

    private async Task InvokeWaitVisualEffectsAsync(ScenarioStep step, CancellationToken ct)
    {
        var args = step.Args is { ValueKind: JsonValueKind.Object } obj
            ? JsonSerializer.Deserialize<WaitVisualEffectsStepArgs>(obj.GetRawText(), ProtocolJson.Options)
                ?? new WaitVisualEffectsStepArgs()
            : new WaitVisualEffectsStepArgs();

        ValidateWaitVisualEffectsArgs(args);

        JsonElement? request = string.IsNullOrWhiteSpace(args.Location)
            ? null
            : ProtocolJson.ToElement(new VisualEffectsRequest { Location = args.Location });

        var elapsed = Stopwatch.StartNew();
        var lastObserved = new VisualEffectsObservedCounts();
        while (elapsed.ElapsedMilliseconds < args.TimeoutMs)
        {
            ct.ThrowIfCancellationRequested();
            var resp = await _session.InvokeAsync("state.visual_effects", request, ct);
            if (resp.Error is { } error)
                throw new InvalidOperationException($"wait.visual_effects failed during state.visual_effects: {error.Message}");

            if (resp.Result is { } result)
            {
                var state = JsonSerializer.Deserialize<VisualEffectsState>(result.GetRawText(), ProtocolJson.Options);
                if (state is not null)
                {
                    lastObserved = CountVisualEffectMatches(state, args);
                    if (VisualEffectCriteriaSatisfied(args, lastObserved))
                        return;
                }
            }

            await Task.Delay(args.PollMs, ct);
        }

        throw new TimeoutException(
            $"wait.visual_effects timed out after {args.TimeoutMs}ms waiting for {FormatVisualEffectsExpectation(args)} " +
            $"in {args.Location ?? "current location"}; last observed {FormatVisualEffectsLastObserved(args, lastObserved)}");
    }

    private static void ValidateWaitVisualEffectsArgs(WaitVisualEffectsStepArgs args)
    {
        if (args.TimeoutMs < 1)
            throw new InvalidOperationException("wait.visual_effects requires args.timeout_ms >= 1");
        if (args.PollMs < 1)
            throw new InvalidOperationException("wait.visual_effects requires args.poll_ms >= 1");
        if (args.TemporarySprites is null
            && args.LightSources is null
            && args.AmbientLight is null
            && args.WeatherDebrisMinCount is null)
            throw new InvalidOperationException("wait.visual_effects requires at least one of args.temporary_sprites, args.light_sources, args.ambient_light, args.weather_debris_min_count");

        if (args.AmbientLight is not null && args.AmbientLight.Length != 4)
            throw new InvalidOperationException("wait.visual_effects requires args.ambient_light to have 4 values");
        if (args.WeatherDebrisMinCount is not null && args.WeatherDebrisMinCount < 1)
            throw new InvalidOperationException("wait.visual_effects requires args.weather_debris_min_count >= 1");

        if (args.TemporarySprites is { } sprites)
        {
            if (sprites.SourceRect is not null && sprites.SourceRect.Length != 4)
                throw new InvalidOperationException("wait.visual_effects requires args.temporary_sprites.source_rect to have 4 values");
            if (sprites.Color is not null && sprites.Color.Length != 4)
                throw new InvalidOperationException("wait.visual_effects requires args.temporary_sprites.color to have 4 values");
            ValidateVisualEffectCount("temporary_sprites", sprites.MinCount, sprites.MaxCount);
        }

        if (args.LightSources is { } lights)
        {
            if (lights.Color is not null && lights.Color.Length != 4)
                throw new InvalidOperationException("wait.visual_effects requires args.light_sources.color to have 4 values");
            ValidateVisualEffectCount("light_sources", lights.MinCount, lights.MaxCount);
        }
    }

    private static void ValidateVisualEffectCount(string criterion, int minCount, int? maxCount)
    {
        if (minCount < 1)
            throw new InvalidOperationException($"wait.visual_effects requires args.{criterion}.min_count >= 1");
        if (maxCount is not null && maxCount < 1)
            throw new InvalidOperationException($"wait.visual_effects requires args.{criterion}.max_count >= 1");
        if (maxCount is not null && maxCount < minCount)
            throw new InvalidOperationException($"wait.visual_effects requires args.{criterion}.max_count >= args.{criterion}.min_count");
    }

    private static VisualEffectsObservedCounts CountVisualEffectMatches(VisualEffectsState state, WaitVisualEffectsStepArgs args)
    {
        var observed = new VisualEffectsObservedCounts
        {
            TemporarySpritesTotal = state.TemporarySprites.Count,
            LightSourcesTotal = state.LightSources.Count,
            WeatherDebrisCount = state.WeatherDebrisCount,
            AmbientLightMatches = args.AmbientLight is null || IntArrayMatches(state.AmbientLight, args.AmbientLight),
        };

        if (args.TemporarySprites is not null)
        {
            foreach (var sprite in state.TemporarySprites)
            {
                if (TemporarySpriteMatches(sprite, args.TemporarySprites))
                    observed.TemporarySpritesMatched++;
            }
        }

        if (args.LightSources is not null)
        {
            foreach (var light in state.LightSources)
            {
                if (LightSourceMatches(light, args.LightSources))
                    observed.LightSourcesMatched++;
            }
        }

        return observed;
    }

    private static bool VisualEffectCriteriaSatisfied(WaitVisualEffectsStepArgs args, VisualEffectsObservedCounts observed)
    {
        return (args.TemporarySprites is null || CountWithinRange(observed.TemporarySpritesMatched, args.TemporarySprites.MinCount, args.TemporarySprites.MaxCount))
            && (args.LightSources is null || CountWithinRange(observed.LightSourcesMatched, args.LightSources.MinCount, args.LightSources.MaxCount))
            && (args.AmbientLight is null || observed.AmbientLightMatches)
            && (args.WeatherDebrisMinCount is null || observed.WeatherDebrisCount >= args.WeatherDebrisMinCount.Value);
    }

    private static bool TemporarySpriteMatches(TemporarySpriteSummary sprite, VisualEffectTemporarySpriteCriteria criteria)
    {
        return StringEquals(sprite.TextureAsset, criteria.TextureAsset)
            && IntArrayMatches(sprite.SourceRect, criteria.SourceRect)
            && IntArrayMatches(sprite.Color, criteria.Color)
            && StringEquals(sprite.RuntimeType, criteria.RuntimeType)
            && (criteria.MinLayerDepth is null || sprite.LayerDepth >= criteria.MinLayerDepth.Value)
            && (criteria.MaxLayerDepth is null || sprite.LayerDepth <= criteria.MaxLayerDepth.Value);
    }

    private static bool LightSourceMatches(LightSourceSummary light, VisualEffectLightSourceCriteria criteria)
    {
        return StringEquals(light.Id, criteria.Id)
            && (criteria.IdContains is null || light.Id.Contains(criteria.IdContains, StringComparison.Ordinal))
            && StringEquals(light.Context, criteria.Context)
            && IntArrayMatches(light.Color, criteria.Color);
    }

    private static bool StringEquals(string? actual, string? expected)
        => expected is null || string.Equals(actual, expected, StringComparison.Ordinal);

    private static bool IntArrayMatches(int[]? actual, int[]? expected)
        => expected is null || (actual is not null && actual.SequenceEqual(expected));

    private static bool CountWithinRange(int actual, int min, int? max)
        => actual >= min && (max is null || actual <= max.Value);

    private static string FormatVisualEffectsExpectation(WaitVisualEffectsStepArgs args)
    {
        var expectations = new List<string>();
        if (args.TemporarySprites is not null)
            expectations.Add($"{FormatExpectedVisualEffectCount(args.TemporarySprites.MinCount, args.TemporarySprites.MaxCount)} temporary_sprites");
        if (args.LightSources is not null)
            expectations.Add($"{FormatExpectedVisualEffectCount(args.LightSources.MinCount, args.LightSources.MaxCount)} light_sources");
        if (args.AmbientLight is not null)
            expectations.Add($"ambient_light [{string.Join(",", args.AmbientLight)}]");
        if (args.WeatherDebrisMinCount is not null)
            expectations.Add($"at least {args.WeatherDebrisMinCount.Value} weather_debris");
        return string.Join(" and ", expectations);
    }

    private static string FormatExpectedVisualEffectCount(int minCount, int? maxCount)
        => maxCount is null
            ? $"at least {minCount}"
            : minCount == maxCount.Value
                ? $"exactly {minCount}"
                : $"between {minCount} and {maxCount.Value}";

    private static string FormatVisualEffectsLastObserved(WaitVisualEffectsStepArgs args, VisualEffectsObservedCounts observed)
    {
        var parts = new List<string>();
        if (args.TemporarySprites is not null)
            parts.Add($"{observed.TemporarySpritesMatched} matched temporary_sprites out of {observed.TemporarySpritesTotal}");
        if (args.LightSources is not null)
            parts.Add($"{observed.LightSourcesMatched} matched light_sources out of {observed.LightSourcesTotal}");
        if (args.AmbientLight is not null)
            parts.Add($"ambient_light {(observed.AmbientLightMatches ? "matched" : "did not match")}");
        if (args.WeatherDebrisMinCount is not null)
            parts.Add($"{observed.WeatherDebrisCount} weather_debris");
        return string.Join("; ", parts);
    }

    private static string FormatOptionalTile(int? x, int? y)
    {
        if (x is null && y is null)
            return string.Empty;

        return $" at {x?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "any"},{y?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "any"}";
    }

    private async Task InvokeWaitEventActiveAsync(ScenarioStep step, CancellationToken ct)
    {
        var args = ParseWaitEventArgs(step);
        var elapsed = Stopwatch.StartNew();
        EventState? lastObserved = null;

        while (elapsed.ElapsedMilliseconds < args.TimeoutMs)
        {
            ct.ThrowIfCancellationRequested();
            lastObserved = await ReadEventStateAsync(step.Action, ct);
            if (lastObserved.Active
                && (string.IsNullOrWhiteSpace(args.Id) || string.Equals(lastObserved.Id, args.Id, StringComparison.Ordinal))
                && (string.IsNullOrWhiteSpace(args.Location) || string.Equals(lastObserved.Location, args.Location, StringComparison.Ordinal)))
            {
                return;
            }

            await Task.Delay(args.PollMs, ct);
        }

        throw new TimeoutException($"{step.Action} timed out after {args.TimeoutMs}ms; last observed {FormatEventState(lastObserved)}");
    }

    private async Task InvokeWaitEventCompleteAsync(ScenarioStep step, CancellationToken ct)
    {
        var args = ParseWaitEventArgs(step);
        var elapsed = Stopwatch.StartNew();
        var sawRequestedId = string.IsNullOrWhiteSpace(args.Id);
        EventState? lastObserved = null;

        while (elapsed.ElapsedMilliseconds < args.TimeoutMs)
        {
            ct.ThrowIfCancellationRequested();
            lastObserved = await ReadEventStateAsync(step.Action, ct);
            if (!sawRequestedId
                && lastObserved.Active
                && string.Equals(lastObserved.Id, args.Id, StringComparison.Ordinal))
            {
                sawRequestedId = true;
            }

            if (sawRequestedId && !lastObserved.Active && !lastObserved.EventUp)
                return;

            await Task.Delay(args.PollMs, ct);
        }

        throw new TimeoutException($"{step.Action} timed out after {args.TimeoutMs}ms; last observed {FormatEventState(lastObserved)}");
    }

    private async Task<EventState> ReadEventStateAsync(string action, CancellationToken ct)
    {
        var resp = await _session.InvokeAsync("state.event", params_: null, ct);
        if (resp.Error is { } error)
            throw new InvalidOperationException($"{action} failed during state.event: {error.Message}");
        if (resp.Result is not { } result)
            return new EventState();
        return JsonSerializer.Deserialize<EventState>(result.GetRawText(), ProtocolJson.Options) ?? new EventState();
    }

    private static WaitEventStepArgs ParseWaitEventArgs(ScenarioStep step)
    {
        var args = step.Args is { ValueKind: JsonValueKind.Object } obj
            ? JsonSerializer.Deserialize<WaitEventStepArgs>(obj.GetRawText(), ProtocolJson.Options) ?? new WaitEventStepArgs()
            : new WaitEventStepArgs();

        if (args.TimeoutMs < 1)
            throw new InvalidOperationException($"{step.Action} requires args.timeout_ms >= 1");
        if (args.PollMs < 1)
            throw new InvalidOperationException($"{step.Action} requires args.poll_ms >= 1");
        return args;
    }

    private static string FormatEventState(EventState? state)
        => state is null
            ? "nothing"
            : $"active={state.Active}, event_up={state.EventUp}, id='{state.Id}', location='{state.Location}'";

    private async Task InvokeFixtureSaveReloadAsync(ScenarioStep step, string? scenarioFixture, CancellationToken ct)
    {
        int settleTimeoutMs = GetIntArg(step.Args, "settle_timeout_ms") ?? 30000;
        int pollMs = GetIntArg(step.Args, "poll_ms") ?? 100;
        if (settleTimeoutMs < 1)
            throw new InvalidOperationException("fixture.save_reload requires args.settle_timeout_ms >= 1");
        if (pollMs < 1)
            throw new InvalidOperationException("fixture.save_reload requires args.poll_ms >= 1");

        var requestedName = GetStringArg(step.Args, "name") ?? scenarioFixture;
        if (string.IsNullOrWhiteSpace(requestedName))
            throw new InvalidOperationException("fixture.save_reload requires args.name or a scenario fixture");

        var saveReq = ProtocolJson.ToElement(new FixtureSaveRequest { Name = requestedName });
        var saveResp = await _session.InvokeAsync("fixture.save", saveReq, ct);
        if (saveResp.Error is { } saveError)
            throw new InvalidOperationException($"step '{step.Action}' failed during fixture.save: {saveError.Message}");

        if (GetBoolArg(step.Args, "restore_original") != false)
            TryRegisterSaveFolderRestore(saveResp.Result);

        var loadName = GetStringArg(step.Args, "load_name")
            ?? ReadSavedFolderName(saveResp.Result)
            ?? requestedName;

        var titleResp = await _session.InvokeAsync("game.return_to_title", params_: null, ct);
        if (titleResp.Error is { } titleError)
            throw new InvalidOperationException($"step '{step.Action}' failed during game.return_to_title: {titleError.Message}");

        await WaitForTitleReady(settleTimeoutMs, pollMs, ct);

        var loadReq = ProtocolJson.ToElement(new FixtureLoadRequest { Name = loadName });
        var loadResp = await _session.InvokeAsync("fixture.load", loadReq, ct);
        if (loadResp.Error is { } loadError)
            throw new InvalidOperationException($"step '{step.Action}' failed during fixture.load: {loadError.Message}");

        await WaitForWorldReady(ct);
    }

    private async Task WaitForTitleReady(int settleTimeoutMs, int pollMs, CancellationToken ct)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.ElapsedMilliseconds < settleTimeoutMs)
        {
            ct.ThrowIfCancellationRequested();
            var resp = await _session.InvokeAsync("state.time", params_: null, ct);
            if (resp.Error is null
                && resp.Result is { } result
                && result.TryGetProperty("in_save", out var inSave)
                && inSave.ValueKind is JsonValueKind.True or JsonValueKind.False
                && !inSave.GetBoolean())
            {
                return;
            }

            await Task.Delay(pollMs, ct);
        }

        throw new TimeoutException("game never returned to title after fixture.save_reload");
    }

    private static string? ReadSavedFolderName(JsonElement? result)
    {
        if (result is not { ValueKind: JsonValueKind.Object } obj
            || !obj.TryGetProperty("save_path", out var savePathElement)
            || savePathElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var savePath = ReadSavedPath(result);
        if (string.IsNullOrWhiteSpace(savePath))
            return null;

        return Path.GetFileName(savePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    private void TryRegisterSaveFolderRestore(JsonElement? result)
    {
        var savePath = ReadSavedPath(result);
        if (string.IsNullOrWhiteSpace(savePath))
            return;

        var normalizedSavePath = savePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var saveName = Path.GetFileName(normalizedSavePath);
        if (string.IsNullOrWhiteSpace(saveName))
            return;

        var mainSave = Path.Combine(normalizedSavePath, saveName);
        var saveGameInfo = Path.Combine(normalizedSavePath, "SaveGameInfo");
        var oldMainSave = mainSave + "_old";
        var oldSaveGameInfo = saveGameInfo + "_old";
        if (!File.Exists(oldMainSave) || !File.Exists(oldSaveGameInfo))
            return;

        var backupDir = Path.Combine(
            Path.GetTempPath(),
            "sdv-test-save-reload-restores",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backupDir);
        var backupMainSave = Path.Combine(backupDir, saveName);
        var backupSaveGameInfo = Path.Combine(backupDir, "SaveGameInfo");
        File.Copy(oldMainSave, backupMainSave, overwrite: true);
        File.Copy(oldSaveGameInfo, backupSaveGameInfo, overwrite: true);

        _pendingSaveRestores.Add(new SaveFolderRestore(
            backupDir,
            backupMainSave,
            mainSave,
            backupSaveGameInfo,
            saveGameInfo));
    }

    private void RestorePendingSaveFolders()
    {
        var failures = new List<Exception>();
        for (var i = _pendingSaveRestores.Count - 1; i >= 0; i--)
        {
            var restore = _pendingSaveRestores[i];
            try
            {
                File.Copy(restore.BackupMainSave, restore.MainSave, overwrite: true);
                File.Copy(restore.BackupSaveGameInfo, restore.SaveGameInfo, overwrite: true);
                Directory.Delete(restore.BackupDir, recursive: true);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }

        _pendingSaveRestores.Clear();
        if (failures.Count > 0)
            throw new IOException($"{failures.Count} restore operation(s) failed", failures[0]);
    }

    private static string? ReadSavedPath(JsonElement? result)
    {
        if (result is not { ValueKind: JsonValueKind.Object } obj
            || !obj.TryGetProperty("save_path", out var savePathElement)
            || savePathElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return savePathElement.GetString();
    }

    private sealed record SaveFolderRestore(
        string BackupDir,
        string BackupMainSave,
        string MainSave,
        string BackupSaveGameInfo,
        string SaveGameInfo);

    private async Task InvokeTimeNextDayAsync(ScenarioStep step, CancellationToken ct)
    {
        int settleTimeoutMs = GetIntArg(step.Args, "settle_timeout_ms") ?? 3000;
        int pollMs = GetIntArg(step.Args, "poll_ms") ?? 100;
        if (settleTimeoutMs < 1)
            throw new InvalidOperationException("time.next_day requires args.settle_timeout_ms >= 1");
        if (pollMs < 1)
            throw new InvalidOperationException("time.next_day requires args.poll_ms >= 1");

        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var resp = await _session.InvokeAsync(step.Action, step.Args, ct);
            if (resp.Error is null)
                return;

            if (!IsTransientTimeNextDayWarp(resp.Error) || elapsed.ElapsedMilliseconds >= settleTimeoutMs)
                throw new InvalidOperationException($"step '{step.Action}' failed: {resp.Error.Message}");

            await Task.Delay(pollMs, ct);
        }
    }

    private static bool IsTransientTimeNextDayWarp(JsonRpcError error)
        => error.Code == JsonRpcErrorCode.GameStateInvalid
            && string.Equals(error.Message, "time.next_day requires no active warp", StringComparison.Ordinal);

    private async Task InvokeFreezeBeginAsync(ScenarioStep step, CancellationToken ct)
    {
        int settleTimeoutMs = GetIntArg(step.Args, "settle_timeout_ms") ?? 5000;
        int pollMs = GetIntArg(step.Args, "poll_ms") ?? 100;
        if (settleTimeoutMs < 1)
            throw new InvalidOperationException("freeze.begin requires args.settle_timeout_ms >= 1");
        if (pollMs < 1)
            throw new InvalidOperationException("freeze.begin requires args.poll_ms >= 1");

        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var resp = await _session.InvokeAsync(step.Action, step.Args, ct);
            if (resp.Error is null)
                return;

            if (!IsTransientFreezeBeginWarp(resp.Error) || elapsed.ElapsedMilliseconds >= settleTimeoutMs)
                throw new InvalidOperationException($"step '{step.Action}' failed: {resp.Error.Message}");

            await Task.Delay(pollMs, ct);
        }
    }

    private static bool IsTransientFreezeBeginWarp(JsonRpcError error)
        => error.Code == JsonRpcErrorCode.GameStateInvalid
            && string.Equals(error.Message, "freeze.begin requires !Game1.isWarping (mid-warp)", StringComparison.Ordinal);

    private async Task InvokeStateAssertAsync(ScenarioStep step, CancellationToken ct)
    {
        var expr = GetStringArg(step.Args, "expr");
        if (string.IsNullOrWhiteSpace(expr))
            throw new InvalidOperationException("state.assert requires args.expr");

        var message = GetStringArg(step.Args, "message");
        JsonElement? assertionParams = null;
        if (step.Args is { ValueKind: JsonValueKind.Object } obj
            && obj.TryGetProperty("params", out var paramsElement))
        {
            assertionParams = paramsElement.Clone();
        }

        var (passed, detail) = await EvaluateAssertionAsync(
            new ScenarioAssertion
            {
                Type = "state",
                Expr = expr,
                Message = message,
                Params = assertionParams,
            },
            assertionIndex: -1,
            ct);

        if (!passed)
            throw new InvalidOperationException($"step 'state.assert' failed: {message ?? detail ?? expr}");
    }

    private async Task InvokeMenuAdvanceAsync(ScenarioStep step, CancellationToken ct)
    {
        if (HasMenuChoiceTarget(step.Args))
        {
            var choiceArgs = ParseWaitMenuArgs(step);
            await WaitForMenuAsync(step.Action, choiceArgs, ct);
            var choiceParams = ProtocolJson.ToElement(new InputClickMenuChoiceRequest
            {
                Key = choiceArgs.ChoiceKey,
                Text = choiceArgs.ChoiceTextContains,
                TextEquals = choiceArgs.ChoiceText,
                TextMatches = choiceArgs.ChoiceTextMatches,
                Button = choiceArgs.Button,
                CaseSensitive = choiceArgs.CaseSensitive,
            });
            var choiceResp = await _session.InvokeAsync("input.click_menu_choice", choiceParams, ct);
            if (choiceResp.Error is { } choiceError)
                throw new InvalidOperationException($"step '{step.Action}' failed during input.click_menu_choice: {choiceError.Message}");
            return;
        }

        if (HasUiTextTarget(step.Args))
        {
            var uiText = await WaitForUiTextAsync(step, ct);
            var clickParams = ProtocolJson.ToElement(new InputClickTextRequest
            {
                Text = uiText.Text,
                TextEquals = uiText.TextEquals,
                TextMatches = uiText.TextMatches,
                Button = uiText.Button,
                CaseSensitive = uiText.CaseSensitive,
                Occurrence = uiText.Occurrence,
                InRect = uiText.InRect,
                BoundsWithinRect = uiText.BoundsWithinRect,
                BoundsIntersectsRect = uiText.BoundsIntersectsRect,
            });
            var resp = await _session.InvokeAsync("input.click_text", clickParams, ct);
            if (resp.Error is { } textClickError)
                throw new InvalidOperationException($"step '{step.Action}' failed: {textClickError.Message}");
            return;
        }

        var args = ParseMenuAdvanceArgs(step);
        var elapsed = Stopwatch.StartNew();
        MenuState? lastObserved = null;
        while (elapsed.ElapsedMilliseconds < args.TimeoutMs)
        {
            ct.ThrowIfCancellationRequested();
            var menuResp = await _session.InvokeAsync("state.menu", params_: null, ct);
            if (menuResp.Error is { } menuError)
                throw new InvalidOperationException($"step '{step.Action}' failed during state.menu: {menuError.Message}");

            if (menuResp.Result is { } menuRoot)
                lastObserved = JsonSerializer.Deserialize<MenuState>(menuRoot.GetRawText(), ProtocolJson.Options);

            if (lastObserved?.Present == true && lastObserved.Bounds is { Width: > 0, Height: > 0 } bounds)
            {
                var clickParams = ProtocolJson.ToElement(new InputClickMenuAdvanceRequest
                {
                    Button = args.Button,
                });
                for (var i = 0; i < args.Repeat; i++)
                {
                    var clickResp = await _session.InvokeAsync("input.click_menu_advance", clickParams, ct);
                    if (clickResp.Error is { } clickError)
                        throw new InvalidOperationException($"step '{step.Action}' failed during input.click_menu_advance: {clickError.Message}");
                    if (i + 1 < args.Repeat)
                        await Task.Delay(args.IntervalMs, ct);
                }
                return;
            }

            await Task.Delay(args.PollMs, ct);
        }

        var last = lastObserved is null
            ? "nothing"
            : $"present={lastObserved.Present}, type='{lastObserved.Type}', bounds={(lastObserved.Bounds is null ? "none" : $"{lastObserved.Bounds.Width}x{lastObserved.Bounds.Height}")}";
        throw new TimeoutException($"{step.Action} timed out after {args.TimeoutMs}ms waiting for an active menu with bounds; last observed {last}");
    }

    private async Task InvokeWaitMenuAsync(ScenarioStep step, CancellationToken ct)
    {
        var args = ParseWaitMenuArgs(step);
        await WaitForMenuAsync(step.Action, args, ct);
    }

    private async Task<MenuState> WaitForMenuAsync(string action, WaitMenuStepArgs args, CancellationToken ct)
    {
        var elapsed = Stopwatch.StartNew();
        MenuState? lastObserved = null;
        while (elapsed.ElapsedMilliseconds < args.TimeoutMs)
        {
            ct.ThrowIfCancellationRequested();
            var menuResp = await _session.InvokeAsync("state.menu", params_: null, ct);
            if (menuResp.Error is { } menuError)
                throw new InvalidOperationException($"{action} failed during state.menu: {menuError.Message}");

            if (menuResp.Result is { } menuRoot)
                lastObserved = JsonSerializer.Deserialize<MenuState>(menuRoot.GetRawText(), ProtocolJson.Options);

            if (lastObserved is not null && MenuMatches(lastObserved, args))
                return lastObserved;

            await Task.Delay(args.PollMs, ct);
        }

        throw new TimeoutException(
            $"{action} timed out after {args.TimeoutMs}ms waiting for {FormatMenuExpectation(args)}; " +
            $"last observed {FormatMenuState(lastObserved)}");
    }

    private static WaitMenuStepArgs ParseWaitMenuArgs(ScenarioStep step)
    {
        var args = step.Args is { ValueKind: JsonValueKind.Object } obj
            ? JsonSerializer.Deserialize<WaitMenuStepArgs>(obj.GetRawText(), ProtocolJson.Options) ?? new WaitMenuStepArgs()
            : new WaitMenuStepArgs();

        if (args.TimeoutMs < 1)
            throw new InvalidOperationException($"{step.Action} requires args.timeout_ms >= 1");
        if (args.PollMs < 1)
            throw new InvalidOperationException($"{step.Action} requires args.poll_ms >= 1");

        var button = string.IsNullOrWhiteSpace(args.Button) ? "left" : args.Button.Trim().ToLowerInvariant();
        if (button != "left" && button != "right")
            throw new InvalidOperationException($"{step.Action} requires args.button to be left or right");
        args.Button = button;
        return args;
    }

    private static bool MenuMatches(MenuState state, WaitMenuStepArgs args)
    {
        var expectedPresent = args.Present ?? true;
        if (state.Present != expectedPresent)
            return false;
        if (!expectedPresent)
            return true;

        if (!string.IsNullOrWhiteSpace(args.Type)
            && !string.Equals(state.Type, args.Type, StringComparison.Ordinal))
        {
            return false;
        }

        if (HasMenuTextTarget(args) && !MenuTextMatches(state, args))
            return false;

        if (args.Ready is not null && !MenuReadyMatches(state, args.Ready.Value))
            return false;

        if (HasMenuChoiceTarget(args) && !MenuChoiceMatches(state, args))
            return false;

        return true;
    }

    private static bool MenuTextMatches(MenuState state, WaitMenuStepArgs args)
    {
        foreach (var value in state.Extra.Values)
        {
            if (TextMatches(value, args.Text, args.TextEquals, args.TextMatches, args.CaseSensitive))
                return true;
        }

        return false;
    }

    private static bool MenuReadyMatches(MenuState state, bool expected)
        => state.Extra.TryGetValue("dialogue_ready", out var value)
            && bool.TryParse(value, out var actual)
            && actual == expected;

    private static bool MenuChoiceMatches(MenuState state, WaitMenuStepArgs args)
    {
        foreach (var choice in state.Choices)
        {
            if (!string.IsNullOrWhiteSpace(args.ChoiceKey)
                && string.Equals(choice.Key, args.ChoiceKey, StringComparison.Ordinal))
            {
                return true;
            }

            if (TextMatches(choice.Text, args.ChoiceTextContains, args.ChoiceText, args.ChoiceTextMatches, args.CaseSensitive))
                return true;
        }

        return false;
    }

    private static bool TextMatches(string actual, string? contains, string? equals, string? matches, bool caseSensitive)
    {
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (!string.IsNullOrWhiteSpace(equals))
            return string.Equals(actual, equals, comparison);
        if (!string.IsNullOrWhiteSpace(contains))
            return actual.Contains(contains, comparison);
        if (!string.IsNullOrWhiteSpace(matches))
            return System.Text.RegularExpressions.Regex.IsMatch(
                actual,
                matches,
                caseSensitive ? System.Text.RegularExpressions.RegexOptions.None : System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return false;
    }

    private static bool HasMenuTextTarget(WaitMenuStepArgs args)
        => !string.IsNullOrWhiteSpace(args.Text)
            || !string.IsNullOrWhiteSpace(args.TextEquals)
            || !string.IsNullOrWhiteSpace(args.TextMatches);

    private static bool HasMenuChoiceTarget(WaitMenuStepArgs args)
        => !string.IsNullOrWhiteSpace(args.ChoiceKey)
            || !string.IsNullOrWhiteSpace(args.ChoiceText)
            || !string.IsNullOrWhiteSpace(args.ChoiceTextContains)
            || !string.IsNullOrWhiteSpace(args.ChoiceTextMatches);

    private static string FormatMenuExpectation(WaitMenuStepArgs args)
    {
        if (args.Present == false)
            return "no active menu";
        if (!string.IsNullOrWhiteSpace(args.ChoiceKey))
            return $"menu choice key \"{args.ChoiceKey}\"";
        var choiceLabel = args.ChoiceText ?? args.ChoiceTextContains ?? args.ChoiceTextMatches;
        if (!string.IsNullOrWhiteSpace(choiceLabel))
            return $"menu choice \"{choiceLabel}\"";
        var textLabel = args.TextEquals ?? args.Text ?? args.TextMatches;
        if (!string.IsNullOrWhiteSpace(textLabel))
            return args.Ready is null
                ? $"menu text \"{textLabel}\""
                : $"menu text \"{textLabel}\" ready={args.Ready.Value.ToString().ToLowerInvariant()}";
        if (args.Ready is not null)
            return $"menu ready={args.Ready.Value.ToString().ToLowerInvariant()}";
        if (!string.IsNullOrWhiteSpace(args.Type))
            return $"menu type \"{args.Type}\"";
        return "an active menu";
    }

    private static string FormatMenuState(MenuState? state)
        => state is null
            ? "nothing"
            : $"present={state.Present}, type='{state.Type}', choices={state.Choices.Count}, extra_keys={state.Extra.Count}, extra={FormatMenuExtra(state.Extra)}";

    private static string FormatMenuExtra(IReadOnlyDictionary<string, string> extra)
        => extra.Count == 0
            ? "{}"
            : "{" + string.Join(", ", extra.Take(8).Select(kvp => $"{kvp.Key}='{Truncate(kvp.Value, 4000)}'")) + "}";

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "...";

    private static MenuAdvanceStepArgs ParseMenuAdvanceArgs(ScenarioStep step)
    {
        var args = step.Args is { ValueKind: JsonValueKind.Object } obj
            ? JsonSerializer.Deserialize<MenuAdvanceStepArgs>(obj.GetRawText(), ProtocolJson.Options) ?? new MenuAdvanceStepArgs()
            : new MenuAdvanceStepArgs();

        if (args.TimeoutMs < 1)
            throw new InvalidOperationException($"{step.Action} requires args.timeout_ms >= 1");
        if (args.PollMs < 1)
            throw new InvalidOperationException($"{step.Action} requires args.poll_ms >= 1");
        if (args.Repeat < 1)
            throw new InvalidOperationException($"{step.Action} requires args.repeat >= 1");
        if (args.IntervalMs < 0)
            throw new InvalidOperationException($"{step.Action} requires args.interval_ms >= 0");

        var button = string.IsNullOrWhiteSpace(args.Button) ? "left" : args.Button.Trim().ToLowerInvariant();
        if (button != "left" && button != "right")
            throw new InvalidOperationException($"{step.Action} requires args.button to be left or right");
        args.Button = button;
        return args;
    }

    private async Task InvokeCombatAttackAsync(ScenarioStep step, CancellationToken ct)
    {
        var args = step.Args is { ValueKind: JsonValueKind.Object } obj
            ? JsonSerializer.Deserialize<CombatAttackRequest>(obj.GetRawText(), ProtocolJson.Options) ?? new CombatAttackRequest()
            : new CombatAttackRequest();

        if (args.Repeat < 1)
            throw new InvalidOperationException($"{step.Action} requires args.repeat >= 1");
        if (args.DelayTicks < 0)
            throw new InvalidOperationException($"{step.Action} requires args.delay_ticks >= 0");

        var singleAttack = new JsonObject();
        if (args.X is { } x)
            singleAttack["x"] = x;
        if (args.Y is { } y)
            singleAttack["y"] = y;
        if (!string.IsNullOrWhiteSpace(args.Direction))
            singleAttack["direction"] = args.Direction;
        if (!string.IsNullOrWhiteSpace(args.QualifiedItemId))
            singleAttack["qualified_item_id"] = args.QualifiedItemId;

        var singleAttackElement = JsonDocument.Parse(singleAttack.ToJsonString()).RootElement.Clone();
        for (int i = 0; i < args.Repeat; i++)
        {
            var resp = await _session.InvokeAsync("combat.attack", singleAttackElement, ct);
            if (resp.Error is { } ex)
                throw new InvalidOperationException($"step '{step.Action}' failed: {ex.Message}");

            if (i + 1 < args.Repeat && args.DelayTicks > 0)
                await Task.Delay(TimeSpan.FromMilliseconds(args.DelayTicks * 17.0), ct);
        }
    }

    private async Task<UiTextStepArgs> WaitForUiTextAsync(ScenarioStep step, CancellationToken ct)
    {
        var args = ParseUiTextArgs(step);
        var requiredCount = System.Math.Max(args.MinCount, args.Occurrence);
        var elapsed = Stopwatch.StartNew();
        var lastCount = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var armParams = ProtocolJson.ToElement(new DrawArmRequest { Ticks = args.CaptureTicks });
            var armResp = await _session.InvokeAsync("draw.arm", armParams, ct);
            if (armResp.Error is { } armError)
                throw new InvalidOperationException($"step '{step.Action}' failed during draw.arm: {armError.Message}");

            await Task.Delay(args.PollMs, ct);

            var findResp = await _session.InvokeAsync("draw.text_find", BuildTextFindParams(args), ct);
            if (findResp.Error is { } findError)
                throw new InvalidOperationException($"step '{step.Action}' failed during draw.text_find: {findError.Message}");

            lastCount = findResp.Result is { } result ? ReadTextFindCount(result) : 0;
            await DisarmDrawCaptureAsync(step.Action, ct);
            if (lastCount >= requiredCount)
                return args;

            if (elapsed.ElapsedMilliseconds >= args.TimeoutMs)
            {
                throw new TimeoutException(
                    $"{step.Action} timed out after {args.TimeoutMs}ms waiting for text \"{args.TextLabel}\" " +
                    $"(matched {lastCount} < {requiredCount})");
            }
        }
    }

    private async Task DisarmDrawCaptureAsync(string action, CancellationToken ct)
    {
        var resp = await _session.InvokeAsync("draw.disarm", params_: null, ct);
        if (resp.Error is { } error)
            throw new InvalidOperationException($"step '{action}' failed during draw.disarm: {error.Message}");
    }

    private static UiTextStepArgs ParseUiTextArgs(ScenarioStep step)
    {
        var args = step.Args is { ValueKind: JsonValueKind.Object } obj
            ? JsonSerializer.Deserialize<UiTextStepArgs>(obj.GetRawText(), ProtocolJson.Options) ?? new UiTextStepArgs()
            : new UiTextStepArgs();

        if (string.IsNullOrWhiteSpace(args.Text)
            && string.IsNullOrWhiteSpace(args.TextEquals)
            && string.IsNullOrWhiteSpace(args.TextMatches))
            throw new InvalidOperationException($"{step.Action} requires args.text, args.text_equals, or args.text_matches");
        if (args.TimeoutMs < 1)
            throw new InvalidOperationException($"{step.Action} requires args.timeout_ms >= 1");
        if (args.PollMs < 1)
            throw new InvalidOperationException($"{step.Action} requires args.poll_ms >= 1");
        if (args.CaptureTicks < 1)
            throw new InvalidOperationException($"{step.Action} requires args.capture_ticks >= 1");
        if (args.MinCount < 1)
            throw new InvalidOperationException($"{step.Action} requires args.min_count >= 1");
        if (args.Occurrence < 1)
            throw new InvalidOperationException($"{step.Action} requires args.occurrence >= 1");

        return args;
    }

    private static JsonElement BuildTextFindParams(UiTextStepArgs args)
        => ProtocolJson.ToElement(new TextDrawFilter
        {
            TextContains = args.Text,
            TextEquals = args.TextEquals,
            TextMatches = args.TextMatches,
            CaseSensitive = args.CaseSensitive,
            InRect = args.InRect,
            BoundsWithinRect = args.BoundsWithinRect,
            BoundsIntersectsRect = args.BoundsIntersectsRect,
        });

    private static int ReadTextFindCount(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object)
            return 0;
        if (result.TryGetProperty("count", out var count) && count.TryGetInt32(out var parsed))
            return parsed;
        if (result.TryGetProperty("events", out var events) && events.ValueKind == JsonValueKind.Array)
            return events.EnumerateArray().Count();
        return 0;
    }

    private static string DescribeStep(ScenarioStep step)
    {
        return step.Action switch
        {
            "wait.ms" => $"Wait {GetIntArg(step.Args, "ms") ?? 0}ms",
            "wait.location" => $"Wait for location {GetStringArg(step.Args, "location") ?? "unknown"}",
            "wait.player" => "Wait for player state",
            "wait.npc_location" => $"Wait for NPC {GetStringArg(step.Args, "name") ?? "unknown"} in {GetStringArg(step.Args, "location") ?? "unknown"}",
            "wait.location_content" => $"Wait for {GetStringArg(step.Args, "collection") ?? "content"} in {GetStringArg(step.Args, "location") ?? "unknown"}",
            "wait.visual_effects" => $"Wait for visual effects in {GetStringArg(step.Args, "location") ?? "current location"}",
            "wait.event_active" => $"Wait for event {GetStringArg(step.Args, "id") ?? "active"}",
            "wait.event_complete" => $"Wait for event {GetStringArg(step.Args, "id") ?? "active"} to complete",
            "player.warp" => $"Warp to {GetStringArg(step.Args, "location") ?? "unknown"} ({GetIntArg(step.Args, "x") ?? 0},{GetIntArg(step.Args, "y") ?? 0})",
            "world.place_furniture" => $"Place {GetStringArg(step.Args, "id") ?? "furniture"} at {GetStringArg(step.Args, "location") ?? "current"} ({GetIntArg(step.Args, "x") ?? 0},{GetIntArg(step.Args, "y") ?? 0})",
            "world.interact_tile" => $"Interact tile ({GetIntArg(step.Args, "x") ?? 0},{GetIntArg(step.Args, "y") ?? 0})",
            "world.interact_tile_action" => $"Run tile {GetStringArg(step.Args, "property") ?? "action"} at ({GetIntArg(step.Args, "x") ?? 0},{GetIntArg(step.Args, "y") ?? 0})",
            "input.key" => $"Key {GetStringArg(step.Args, "key") ?? "unknown"}",
            "input.text" => $"Type \"{GetStringArg(step.Args, "text") ?? string.Empty}\"{(GetBoolArg(step.Args, "submit") == true ? " + submit" : string.Empty)}",
            "input.click" => $"Click {GetStringArg(step.Args, "button") ?? "left"} at ({GetIntArg(step.Args, "x") ?? 0},{GetIntArg(step.Args, "y") ?? 0})",
            "input.click_text" => $"Click {GetStringArg(step.Args, "button") ?? "left"} text \"{GetUiTextLabel(step.Args)}\"",
            "input.click_menu_button" => $"Click {GetStringArg(step.Args, "button") ?? "left"} menu button \"{GetMenuButtonLabel(step.Args)}\"{GetRepeatSuffix(step.Args)}",
            "input.hover" => $"Hover at ({GetIntArg(step.Args, "x") ?? 0},{GetIntArg(step.Args, "y") ?? 0})",
            "input.hover_text" => $"Hover text \"{GetUiTextLabel(step.Args)}\"",
            "ui.wait_text" => $"Wait for text \"{GetUiTextLabel(step.Args)}\"",
            "wait.menu" => $"Wait for menu {GetMenuLabel(step.Args)}",
            "ui.click_text" => $"Wait and click {GetStringArg(step.Args, "button") ?? "left"} text \"{GetUiTextLabel(step.Args)}\"",
            "ui.hover_text" => $"Wait and hover text \"{GetUiTextLabel(step.Args)}\"",
            "ui.acknowledge" => HasMenuChoiceTarget(step.Args)
                ? $"Acknowledge choice \"{GetMenuChoiceLabel(step.Args)}\""
                : HasUiTextTarget(step.Args)
                ? $"Acknowledge text \"{GetUiTextLabel(step.Args)}\""
                : "Acknowledge active menu",
            "event.advance" => HasMenuChoiceTarget(step.Args)
                ? $"Advance event through choice \"{GetMenuChoiceLabel(step.Args)}\""
                : HasUiTextTarget(step.Args)
                ? $"Advance event through text \"{GetUiTextLabel(step.Args)}\""
                : "Advance active event/menu",
            "draw.arm" => $"Capture draw events for {GetIntArg(step.Args, "ticks") ?? 0} ticks",
            "freeze.begin" => "Freeze deterministic frame",
            "freeze.end" => "Resume live frame",
            "state.assert" => $"Assert {GetStringArg(step.Args, "expr") ?? "state"}",
            "fixture.save_reload" => $"Save and reload fixture \"{GetStringArg(step.Args, "name") ?? "current"}\"",
            "combat.attack" => DescribeCombatAttack(step.Args),
            "time.next_day" => "Advance to next day",
            "screenshot.capture" => $"Capture screenshot \"{GetStringArg(step.Args, "name") ?? "explicit"}\"",
            "screenshot.capture_next_frame" => $"Capture next-frame screenshot \"{GetStringArg(step.Args, "name") ?? "explicit"}\"",
            _ => step.Args is null ? step.Action : $"{step.Action} {step.Args.Value.GetRawText()}",
        };
    }

    private async Task CaptureExplicitScreenshotAsync(
        ScenarioStep step,
        string scenarioName,
        ScenarioReport report,
        CancellationToken ct,
        ScreenshotCaptureMode captureMode)
    {
        if (_recorder is null || _reportDir is null)
            return;

        string name = GetStringArg(step.Args, "name") ?? "explicit";
        int timeoutMs = GetIntArg(step.Args, "timeout_ms") ?? 2000;
        var path = await _recorder.CaptureAsync(
            _reportDir,
            scenarioName,
            name,
            ct,
            allowUnfrozen: true,
            captureMode: captureMode,
            timeoutMs: timeoutMs);
        if (path is not null)
            report.Screenshots.Add(MakeRelativePath(_reportDir, path));
    }

    private async Task TryCaptureStepScreenshotAsync(
        ScenarioReport report,
        string scenarioName,
        ScenarioStep step,
        int stepIndex,
        CancellationToken ct)
    {
        if (_recorder is null || _reportDir is null)
            return;
        if (!ShouldAutoCaptureStep(step))
            return;

        var name = step.Action == "freeze.begin"
            ? $"step-{stepIndex:D2}-after-freeze"
            : $"step-{stepIndex:D2}-{SanitizeScreenshotName(step.Action)}";
        var path = await _recorder.CaptureAsync(
            _reportDir,
            scenarioName,
            name,
            ct,
            allowUnfrozen: step.Action != "freeze.begin");
        if (path is not null)
            report.Screenshots.Add(MakeRelativePath(_reportDir, path));
    }

    internal static bool ShouldAutoCaptureStep(ScenarioStep step)
    {
        if (GetBoolArg(step.Args, "auto_screenshot") == false)
            return false;

        return step.Action switch
        {
            "wait.ms" => false,
            "wait.location" => false,
            "wait.player" => false,
            "wait.npc_location" => false,
            "wait.location_content" => false,
            "wait.visual_effects" => false,
            "wait.event_active" => false,
            "wait.event_complete" => false,
            "wait.menu" => false,
            "draw.arm" => false,
            "draw.disarm" => false,
            "state.assert" => false,
            "ui.wait_text" => false,
            _ => true,
        };
    }

    private static string DescribeAssertion(ScenarioAssertion assertion)
    {
        return assertion.Type switch
        {
            "draw.text_contains" => $"draw.text_contains \"{GetTextFilterLabel(assertion)}\"",
            "draw.text_not_contains" => $"draw.text_not_contains \"{GetTextFilterLabel(assertion)}\"",
            "draw.text_all_within" => $"draw.text_all_within \"{GetTextFilterLabel(assertion)}\"",
            "draw.contains" => $"draw.contains {GetFilterString(assertion, "texture_asset") ?? "<draw filter>"}",
            "draw.not_contains" => $"draw.not_contains {GetFilterString(assertion, "texture_asset") ?? "<draw filter>"}",
            "state" => string.IsNullOrWhiteSpace(assertion.Expr) ? "state" : $"state {assertion.Expr}",
            "content.asset" => string.IsNullOrWhiteSpace(assertion.Asset) ? "content.asset" : $"content.asset {assertion.Asset}",
            "bitmap" => string.IsNullOrWhiteSpace(assertion.Baseline) ? "bitmap" : $"bitmap {assertion.Baseline}",
            _ => assertion.Type,
        };
    }

    private static string? GetStringArg(JsonElement? args, string name)
        => args is { ValueKind: JsonValueKind.Object } obj
            && obj.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

    private static string GetUiTextLabel(JsonElement? args)
        => GetStringArg(args, "text_equals")
            ?? GetStringArg(args, "text")
            ?? GetStringArg(args, "text_matches")
            ?? string.Empty;

    private static bool HasUiTextTarget(JsonElement? args)
        => !string.IsNullOrWhiteSpace(GetStringArg(args, "text"))
            || !string.IsNullOrWhiteSpace(GetStringArg(args, "text_equals"))
            || !string.IsNullOrWhiteSpace(GetStringArg(args, "text_matches"));

    private static bool HasMenuChoiceTarget(JsonElement? args)
        => !string.IsNullOrWhiteSpace(GetStringArg(args, "choice_key"))
            || !string.IsNullOrWhiteSpace(GetStringArg(args, "choice_text"))
            || !string.IsNullOrWhiteSpace(GetStringArg(args, "choice_text_contains"))
            || !string.IsNullOrWhiteSpace(GetStringArg(args, "choice_text_matches"));

    private static string GetMenuChoiceLabel(JsonElement? args)
        => GetStringArg(args, "choice_key")
            ?? GetStringArg(args, "choice_text")
            ?? GetStringArg(args, "choice_text_contains")
            ?? GetStringArg(args, "choice_text_matches")
            ?? string.Empty;

    private static string GetMenuLabel(JsonElement? args)
    {
        foreach (var label in new[] { GetMenuChoiceLabel(args), GetUiTextLabel(args), GetStringArg(args, "type") })
        {
            if (!string.IsNullOrWhiteSpace(label))
                return label;
        }

        return "active";
    }

    private static string GetMenuButtonLabel(JsonElement? args)
        => GetStringArg(args, "label") ?? GetStringArg(args, "text_equals") ?? GetStringArg(args, "id") ?? string.Empty;

    private static string DescribeCombatAttack(JsonElement? args)
    {
        var direction = GetStringArg(args, "direction");
        if (!string.IsNullOrWhiteSpace(direction))
            return $"Attack {direction}{GetRepeatSuffix(args)}";

        return $"Attack tile ({GetIntArg(args, "x") ?? 0},{GetIntArg(args, "y") ?? 0}){GetRepeatSuffix(args)}";
    }

    private static string GetRepeatSuffix(JsonElement? args)
    {
        var repeat = GetIntArg(args, "repeat") ?? 1;
        return repeat > 1 ? $" x{repeat}" : string.Empty;
    }

    private static int? GetIntArg(JsonElement? args, string name)
        => args is { ValueKind: JsonValueKind.Object } obj
            && obj.TryGetProperty(name, out var value)
            && value.TryGetInt32(out var parsed)
                ? parsed
                : null;

    private static bool? GetBoolArg(JsonElement? args, string name)
        => args is { ValueKind: JsonValueKind.Object } obj
            && obj.TryGetProperty(name, out var value)
            && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
                ? value.GetBoolean()
                : null;

    private static string? GetFilterString(ScenarioAssertion assertion, string name)
        => assertion.Filter is { ValueKind: JsonValueKind.Object } obj
            && obj.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

    private static string GetTextFilterLabel(ScenarioAssertion assertion)
        => GetFilterString(assertion, "text_contains")
            ?? GetFilterString(assertion, "text_equals")
            ?? GetFilterString(assertion, "text_matches")
            ?? "<text>";

    private static string SanitizeScreenshotName(string value)
    {
        var chars = value.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var compact = new System.Text.StringBuilder(chars.Length);
        var previousDash = false;
        foreach (var c in chars)
        {
            if (c == '-')
            {
                if (!previousDash)
                    compact.Append(c);
                previousDash = true;
                continue;
            }

            compact.Append(c);
            previousDash = false;
        }

        var result = compact.ToString().Trim('-');
        return result.Length == 0 ? "step" : result;
    }

    private sealed class UiTextStepArgs
    {
        public string? Text { get; set; }
        public string? TextEquals { get; set; }
        public string? TextMatches { get; set; }
        public string Button { get; set; } = "left";
        public bool CaseSensitive { get; set; } = true;
        public int Occurrence { get; set; } = 1;
        public int MinCount { get; set; } = 1;
        public int TimeoutMs { get; set; } = 1500;
        public int PollMs { get; set; } = 50;
        public int CaptureTicks { get; set; } = 10;
        public int[]? InRect { get; set; }
        public int[]? BoundsWithinRect { get; set; }
        public int[]? BoundsIntersectsRect { get; set; }

        public string TextLabel
            => TextEquals ?? Text ?? TextMatches ?? string.Empty;
    }

    private sealed class MenuAdvanceStepArgs
    {
        public string Button { get; set; } = "left";
        public int TimeoutMs { get; set; } = 1500;
        public int PollMs { get; set; } = 50;
        public int Repeat { get; set; } = 1;
        public int IntervalMs { get; set; } = 100;
    }

    private sealed class WaitMenuStepArgs
    {
        public bool? Present { get; set; }
        public string? Type { get; set; }
        public string? Text { get; set; }
        public string? TextEquals { get; set; }
        public string? TextMatches { get; set; }
        public string? ChoiceKey { get; set; }
        public string? ChoiceText { get; set; }
        public string? ChoiceTextContains { get; set; }
        public string? ChoiceTextMatches { get; set; }
        public bool? Ready { get; set; }
        public string Button { get; set; } = "left";
        public bool CaseSensitive { get; set; } = true;
        public int TimeoutMs { get; set; } = 1500;
        public int PollMs { get; set; } = 50;
    }

    private sealed class WaitLocationStepArgs
    {
        public string? Location { get; set; }
        public int? X { get; set; }
        public int? Y { get; set; }
        public int TimeoutMs { get; set; } = 5000;
        public int PollMs { get; set; } = 100;
    }

    private sealed class WaitPlayerStepArgs
    {
        public string? Location { get; set; }
        public int? X { get; set; }
        public int? Y { get; set; }
        public int? Health { get; set; }
        public int? HealthLt { get; set; }
        public int? HealthLte { get; set; }
        public int? HealthGt { get; set; }
        public int? HealthGte { get; set; }
        public int TimeoutMs { get; set; } = 10000;
        public int PollMs { get; set; } = 100;
    }

    private sealed class WaitNpcLocationStepArgs
    {
        public string? Name { get; set; }
        public string? Location { get; set; }
        public int? X { get; set; }
        public int? Y { get; set; }
        public int TimeoutMs { get; set; } = 10000;
        public int PollMs { get; set; } = 100;
    }

    private static readonly HashSet<string> AllowedLocationContentCollections = new(StringComparer.Ordinal)
    {
        "objects",
        "resource_clumps",
        "monsters",
        "critters",
        "debris",
    };

    private sealed class WaitLocationContentStepArgs
    {
        public string? Location { get; set; }
        public string? Collection { get; set; }
        public string? Name { get; set; }
        public string? Type { get; set; }
        public string? Kind { get; set; }
        public string? Id { get; set; }
        public string? QualifiedId { get; set; }
        public int? Health { get; set; }
        public int? HealthLt { get; set; }
        public int? HealthLte { get; set; }
        public int? HealthGt { get; set; }
        public int? HealthGte { get; set; }
        public int? MaxHealth { get; set; }
        public int? MaxHealthLt { get; set; }
        public int? MaxHealthLte { get; set; }
        public int? MaxHealthGt { get; set; }
        public int? MaxHealthGte { get; set; }
        public int? Damage { get; set; }
        public int? DamageLt { get; set; }
        public int? DamageLte { get; set; }
        public int? DamageGt { get; set; }
        public int? DamageGte { get; set; }
        public string? RuntimeType { get; set; }
        public int? Stack { get; set; }
        public int? StackLt { get; set; }
        public int? StackLte { get; set; }
        public int? StackGt { get; set; }
        public int? StackGte { get; set; }
        public int? Quality { get; set; }
        public int? QualityLt { get; set; }
        public int? QualityLte { get; set; }
        public int? QualityGt { get; set; }
        public int? QualityGte { get; set; }
        public int? Category { get; set; }
        public int? CategoryLt { get; set; }
        public int? CategoryLte { get; set; }
        public int? CategoryGt { get; set; }
        public int? CategoryGte { get; set; }
        public string? SpriteTexture { get; set; }
        public int? X { get; set; }
        public int? Y { get; set; }
        public int MinCount { get; set; } = 1;
        public int? MaxCount { get; set; }
        public int TimeoutMs { get; set; } = 10000;
        public int PollMs { get; set; } = 100;
    }

    private sealed class WaitVisualEffectsStepArgs
    {
        public string? Location { get; set; }
        public VisualEffectTemporarySpriteCriteria? TemporarySprites { get; set; }
        public VisualEffectLightSourceCriteria? LightSources { get; set; }
        public int[]? AmbientLight { get; set; }
        public int? WeatherDebrisMinCount { get; set; }
        public int TimeoutMs { get; set; } = 10000;
        public int PollMs { get; set; } = 100;
    }

    private sealed class VisualEffectTemporarySpriteCriteria
    {
        public string? TextureAsset { get; set; }
        public int[]? SourceRect { get; set; }
        public int[]? Color { get; set; }
        public string? RuntimeType { get; set; }
        public float? MinLayerDepth { get; set; }
        public float? MaxLayerDepth { get; set; }
        public int MinCount { get; set; } = 1;
        public int? MaxCount { get; set; }
    }

    private sealed class VisualEffectLightSourceCriteria
    {
        public string? Id { get; set; }
        public string? IdContains { get; set; }
        public string? Context { get; set; }
        public int[]? Color { get; set; }
        public int MinCount { get; set; } = 1;
        public int? MaxCount { get; set; }
    }

    private sealed class VisualEffectsObservedCounts
    {
        public int TemporarySpritesMatched { get; set; }
        public int TemporarySpritesTotal { get; set; }
        public int LightSourcesMatched { get; set; }
        public int LightSourcesTotal { get; set; }
        public bool AmbientLightMatches { get; set; }
        public int WeatherDebrisCount { get; set; }
    }

    private sealed class WaitEventStepArgs
    {
        public string? Id { get; set; }
        public string? Location { get; set; }
        public int TimeoutMs { get; set; } = 10000;
        public int PollMs { get; set; } = 100;
    }

    /// <summary>
    /// Evaluate a single assertion. Currently supports:
    /// <list type="bullet">
    ///   <item><c>draw.contains</c> — delegates to <c>draw.assert_contains</c> RPC.</item>
    ///   <item><c>draw.not_contains</c> — delegates to <c>draw.assert_not_contains</c> RPC.</item>
    ///   <item><c>draw.text_contains</c> — delegates to <c>draw.assert_text_contains</c> RPC.</item>
    ///   <item><c>draw.text_not_contains</c> — delegates to <c>draw.assert_text_not_contains</c> RPC.</item>
    ///   <item><c>bitmap</c> — captures a screenshot and compares it to a baseline.</item>
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
            case "draw.text_contains":
            {
                if (a.Filter is null) return (false, null);
                var payload = new
                {
                    filter = a.Filter,
                    min_count = a.MinCount,
                    max_count = a.MaxCount,
                    message = a.Message,
                };
                var req = JsonSerializer.SerializeToElement(payload, ProtocolJson.Options);
                var resp = await _session.InvokeAsync("draw.assert_text_contains", req, ct);
                if (resp.Error is not null)
                {
                    await TryCaptureAssertionFailureAsync(ct);
                    return (false, resp.Error.Message);
                }
                if (resp.Result is not { } r) return (false, null);
                bool passed = r.TryGetProperty("passed", out var p) && p.GetBoolean();
                if (!passed) await TryCaptureAssertionFailureAsync(ct);
                return (passed, passed ? null : TextContainsFailureDetail(r));
            }
            case "draw.text_not_contains":
            {
                if (a.Filter is null) return (false, null);
                var payload = new { filter = a.Filter, message = a.Message };
                var req = JsonSerializer.SerializeToElement(payload, ProtocolJson.Options);
                var resp = await _session.InvokeAsync("draw.assert_text_not_contains", req, ct);
                if (resp.Error is not null)
                {
                    await TryCaptureAssertionFailureAsync(ct);
                    return (false, resp.Error.Message);
                }
                if (resp.Result is not { } r) return (false, null);
                bool passed = r.TryGetProperty("passed", out var p) && p.GetBoolean();
                if (!passed) await TryCaptureAssertionFailureAsync(ct);
                return (passed, passed ? null : TextNotContainsFailureDetail(r));
            }
            case "draw.text_all_within":
            {
                var (passed, detail) = await EvaluateTextAllWithinAsync(a, ct);
                if (!passed) await TryCaptureAssertionFailureAsync(ct);
                return (passed, detail);
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
            case "content.asset":
            {
                var result = await EvaluateContentAssetAssertionAsync(a, ct);
                if (!result.Passed)
                    await TryCaptureAssertionFailureAsync(ct);
                return result;
            }
            case "state":
            {
                // Minimal DSL: "state.<method>.<field>[.<subfield>...] == <literal>"
                //           or "state.<method>.<field>[.<subfield>...] != <literal>"
                //           or "state.<method>.<array> contains [field] '<literal>'.
                // RHS literal: single/double-quoted string, integer, or boolean. Anything
                // more expressive is deferred — scenarios needing richer logic compose
                // multiple assertions.
                if (string.IsNullOrWhiteSpace(a.Expr)) return (false, null);

                var containsMatch = System.Text.RegularExpressions.Regex.Match(
                    a.Expr.Trim(),
                    @"^state\.([A-Za-z_][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)\s+contains(?:\s+([A-Za-z_][A-Za-z0-9_]*))?\s+(['""])(.*?)\4$");
                if (containsMatch.Success)
                {
                    var containsMethod = $"state.{containsMatch.Groups[1].Value}";
                    var arrayProperty = containsMatch.Groups[2].Value;
                    var objectField = containsMatch.Groups[3].Success ? containsMatch.Groups[3].Value : null;
                    var literal = containsMatch.Groups[5].Value;

                    var containsResp = await _session.InvokeAsync(containsMethod, a.Params, ct);
                    if (containsResp.Error is not null || containsResp.Result is not { } containsRoot)
                    {
                        await TryCaptureAssertionFailureAsync(ct);
                        return (false, containsResp.Error?.Message);
                    }

                    if (!containsRoot.TryGetProperty(arrayProperty, out var array) || array.ValueKind != JsonValueKind.Array)
                    {
                        await TryCaptureAssertionFailureAsync(ct);
                        return (false, $"state.{containsMatch.Groups[1].Value}.{arrayProperty} was not an array");
                    }

                    var matched = false;
                    foreach (var element in array.EnumerateArray())
                    {
                        if (objectField is null)
                        {
                            matched = element.ValueKind == JsonValueKind.String
                                && string.Equals(element.GetString(), literal, StringComparison.Ordinal);
                        }
                        else
                        {
                            matched = element.ValueKind == JsonValueKind.Object
                                && element.TryGetProperty(objectField, out var field)
                                && field.ValueKind == JsonValueKind.String
                                && string.Equals(field.GetString(), literal, StringComparison.Ordinal);
                        }

                        if (matched)
                            break;
                    }

                    if (!matched) await TryCaptureAssertionFailureAsync(ct);
                    return (matched, matched ? null : $"expected {arrayProperty} to contain '{literal}'");
                }

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
                var resp = await _session.InvokeAsync(method, a.Params, ct);
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

    private async Task<(bool Passed, string? Detail)> EvaluateContentAssetAssertionAsync(
        ScenarioAssertion assertion,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(assertion.Asset))
            return (false, "content.asset requires asset");

        var request = ProtocolJson.ToElement(new ContentAssetRequest
        {
            Name = assertion.Asset,
            AssetType = assertion.AssetType,
            IncludeKeys = assertion.IncludeKeys ?? false,
            KeysLimit = assertion.KeysLimit,
            EntryKeys = assertion.EntryKeys,
            HashTexture = assertion.HashTexture ?? false,
        });
        var resp = await _session.InvokeAsync("content.asset", request, ct);
        if (resp.Error is not null)
            return (false, resp.Error.Message);
        if (resp.Result is not { ValueKind: JsonValueKind.Object } root)
            return (false, "content.asset returned no result");

        if (!root.TryGetProperty("exists", out var exists)
            || exists.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return (false, $"content.asset returned invalid exists for {assertion.Asset}");
        }

        if (!exists.GetBoolean())
            return (false, $"{assertion.Asset} is missing");

        if (string.IsNullOrWhiteSpace(assertion.Expr))
            return (true, null);

        return EvaluateAssetExpression(root, assertion.Expr);
    }

    private static (bool Passed, string? Detail) EvaluateAssetExpression(JsonElement assetRoot, string expr)
    {
        var trimmed = expr.Trim();
        var containsMatch = System.Text.RegularExpressions.Regex.Match(
            trimmed,
            @"^asset\.([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s+contains(?:\s+([A-Za-z_][A-Za-z0-9_]*))?\s+(['""])(.*?)\3$");
        if (containsMatch.Success)
        {
            var path = "asset." + containsMatch.Groups[1].Value;
            var objectField = containsMatch.Groups[2].Success ? containsMatch.Groups[2].Value : null;
            var literal = containsMatch.Groups[4].Value;

            if (!TryResolveAssetPath(assetRoot, path, out var array))
                return (false, $"{path} was not found");
            if (array.ValueKind != JsonValueKind.Array)
                return (false, $"{path} was not an array");

            foreach (var element in array.EnumerateArray())
            {
                if (objectField is null)
                {
                    if (element.ValueKind == JsonValueKind.String
                        && string.Equals(element.GetString(), literal, StringComparison.Ordinal))
                    {
                        return (true, null);
                    }
                }
                else if (element.ValueKind == JsonValueKind.Object
                    && element.TryGetProperty(objectField, out var field)
                    && field.ValueKind == JsonValueKind.String
                    && string.Equals(field.GetString(), literal, StringComparison.Ordinal))
                {
                    return (true, null);
                }
            }

            return (false, $"expected {path} to contain '{literal}'");
        }

        var neqIdx = trimmed.IndexOf("!=", StringComparison.Ordinal);
        var eqIdx = trimmed.IndexOf("==", StringComparison.Ordinal);
        bool negated;
        string[] parts;
        if (neqIdx >= 0 && (eqIdx < 0 || neqIdx < eqIdx))
        {
            negated = true;
            parts = new[] { trimmed.Substring(0, neqIdx), trimmed.Substring(neqIdx + 2) };
        }
        else if (eqIdx >= 0)
        {
            negated = false;
            parts = new[] { trimmed.Substring(0, eqIdx), trimmed.Substring(eqIdx + 2) };
        }
        else
        {
            return (false, $"unsupported content.asset expression: {expr}");
        }

        var lhs = parts[0].Trim();
        var rhs = parts[1].Trim();
        if (!TryResolveAssetPath(assetRoot, lhs, out var value))
            return (false, $"{lhs} was not found");

        var equal = JsonElementEqualsLiteral(value, rhs);
        if (equal is null)
            return (false, $"unsupported literal in content.asset expression: {rhs}");

        var result = negated ? !equal.Value : equal.Value;
        return (result, result ? null : $"{lhs} did not match {rhs}");
    }

    private static bool TryResolveAssetPath(JsonElement assetRoot, string path, out JsonElement value)
    {
        value = default;
        var tokens = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0 || tokens[0] != "asset")
            return false;
        if (tokens.Length == 1)
        {
            value = assetRoot;
            return true;
        }
        if (assetRoot.ValueKind != JsonValueKind.Object)
            return false;

        var index = 1;
        if (tokens[index] == "summary")
        {
            if (!assetRoot.TryGetProperty("summary", out value))
                return false;
            index++;
        }
        else if (assetRoot.TryGetProperty(tokens[index], out value))
        {
            index++;
        }
        else
        {
            if (!assetRoot.TryGetProperty("summary", out value))
                return false;
        }

        for (; index < tokens.Length; index++)
        {
            if (!TryReadJsonToken(value, tokens[index], out value))
                return false;
        }

        return true;
    }

    private static bool TryReadJsonToken(JsonElement current, string token, out JsonElement value)
    {
        value = default;
        var match = System.Text.RegularExpressions.Regex.Match(token, @"^([A-Za-z_][A-Za-z0-9_]*)(?:\[(\d+)\])?$");
        if (!match.Success)
            return false;

        if (current.ValueKind != JsonValueKind.Object)
            return false;
        if (!current.TryGetProperty(match.Groups[1].Value, out value))
            return false;

        if (!match.Groups[2].Success)
            return true;

        if (value.ValueKind != JsonValueKind.Array)
            return false;
        var index = int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        if (index < 0 || index >= value.GetArrayLength())
            return false;
        value = value[index];
        return true;
    }

    private static bool? JsonElementEqualsLiteral(JsonElement value, string rhs)
    {
        if ((rhs.StartsWith('\'') && rhs.EndsWith('\'')) ||
            (rhs.StartsWith('"') && rhs.EndsWith('"')))
        {
            var literal = rhs.Substring(1, rhs.Length - 2);
            return value.ValueKind == JsonValueKind.String
                && string.Equals(value.GetString(), literal, StringComparison.Ordinal);
        }

        if (long.TryParse(rhs, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var intLiteral))
        {
            return value.ValueKind == JsonValueKind.Number
                && value.TryGetInt64(out var actual)
                && actual == intLiteral;
        }

        if (bool.TryParse(rhs, out var boolLiteral))
        {
            return (value.ValueKind == JsonValueKind.True && boolLiteral)
                || (value.ValueKind == JsonValueKind.False && !boolLiteral);
        }

        return null;
    }

    private async Task<(bool Passed, string? Detail)> EvaluateTextAllWithinAsync(
        ScenarioAssertion assertion,
        CancellationToken ct)
    {
        if (!TryReadRect(assertion.Region, "region", out var region, out var rectError))
            return (false, rectError);

        TextDrawFilter filter;
        try
        {
            filter = assertion.Filter is { } filterJson
                ? JsonSerializer.Deserialize<TextDrawFilter>(filterJson.GetRawText(), ProtocolJson.Options) ?? new TextDrawFilter()
                : new TextDrawFilter();
        }
        catch (JsonException ex)
        {
            return (false, $"invalid text filter: {ex.Message}");
        }

        var resp = await _session.InvokeAsync("draw.text_snapshot", params_: null, ct);
        if (resp.Error is not null)
            return (false, resp.Error.Message);
        if (resp.Result is not { } root)
            return (false, "draw.text_snapshot returned no result");

        TextDrawEventSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<TextDrawEventSnapshot>(root.GetRawText(), ProtocolJson.Options);
        }
        catch (JsonException ex)
        {
            return (false, $"invalid draw.text_snapshot result: {ex.Message}");
        }

        if (snapshot is null)
            return (false, "draw.text_snapshot returned no result");

        var matched = 0;
        foreach (var ev in snapshot.Events)
        {
            if (!TextMatches(ev, filter))
                continue;

            matched++;
            var bounds = new TextRect(ev.X, ev.Y, ev.Width, ev.Height);
            if (!region.Contains(bounds))
            {
                var text = string.IsNullOrEmpty(ev.Text) ? "<empty>" : ev.Text;
                return (false, $"\"{text}\" bounds {bounds} outside {region}");
            }
        }

        return matched >= assertion.MinCount
            ? (true, null)
            : (false, $"matched {matched} < {assertion.MinCount}");
    }

    private static bool TextMatches(TextDrawEventDto ev, TextDrawFilter filter)
    {
        var comparison = filter.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        if (!string.IsNullOrEmpty(filter.TextContains) &&
            (ev.Text ?? string.Empty).IndexOf(filter.TextContains, comparison) < 0)
            return false;

        if (filter.TextEquals is { } equals &&
            !string.Equals(ev.Text ?? string.Empty, equals, comparison))
            return false;

        if (filter.Color is { Length: 4 } color)
        {
            if (ev.Color.Length < 4 ||
                ev.Color[0] != color[0] ||
                ev.Color[1] != color[1] ||
                ev.Color[2] != color[2] ||
                ev.Color[3] != color[3])
                return false;
        }

        if (filter.ColorAny is { Length: > 0 } colorAny && !MatchesAnyColor(ev.Color, colorAny))
            return false;

        var bounds = new TextRect(ev.X, ev.Y, ev.Width, ev.Height);

        if (TryReadRect(filter.InRect, out var inRect) &&
            !inRect.ContainsPoint(ev.X, ev.Y))
            return false;

        if (TryReadRect(filter.BoundsWithinRect, out var within) &&
            !within.Contains(bounds))
            return false;

        if (TryReadRect(filter.BoundsIntersectsRect, out var intersects) &&
            !intersects.Intersects(bounds))
            return false;

        if (filter.LayerDepthRange is { Length: 2 } range &&
            (ev.LayerDepth < range[0] || ev.LayerDepth > range[1]))
            return false;

        return true;
    }

    private static bool MatchesAnyColor(int[] eventColor, int[][] colors)
    {
        foreach (var color in colors)
        {
            if (color.Length == 4 &&
                eventColor.Length >= 4 &&
                eventColor[0] == color[0] &&
                eventColor[1] == color[1] &&
                eventColor[2] == color[2] &&
                eventColor[3] == color[3])
                return true;
        }

        return false;
    }

    private static bool TryReadRect(JsonElement? value, string name, out TextRect rect, out string? error)
    {
        rect = default;
        error = null;
        if (value is not { } element)
        {
            error = $"{name} must be [x, y, w, h] or {{x,y,w,h}}";
            return false;
        }

        int[] values;
        if (element.ValueKind == JsonValueKind.Array)
        {
            if (element.GetArrayLength() != 4)
            {
                error = $"{name} must be [x, y, w, h]";
                return false;
            }

            values = new int[4];
            var i = 0;
            foreach (var item in element.EnumerateArray())
            {
                if (!item.TryGetInt32(out values[i]))
                {
                    error = $"{name} values must be integers";
                    return false;
                }
                i++;
            }
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            if (!TryReadRectProperty(element, "x", out var x) ||
                !TryReadRectProperty(element, "y", out var y) ||
                !TryReadRectProperty(element, "w", out var w) ||
                !TryReadRectProperty(element, "h", out var h))
            {
                error = $"{name} object values must be integers";
                return false;
            }

            values = new[] { x, y, w, h };
        }
        else
        {
            error = $"{name} must be [x, y, w, h] or {{x,y,w,h}}";
            return false;
        }

        if (values[2] < 0 || values[3] < 0)
        {
            error = $"{name} width/height must be >= 0";
            return false;
        }

        rect = new TextRect(values[0], values[1], values[2], values[3]);
        return true;
    }

    private static bool TryReadRectProperty(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        return element.TryGetProperty(propertyName, out var property) &&
            property.TryGetInt32(out value);
    }

    private static bool TryReadRect(int[]? value, out TextRect rect)
    {
        rect = default;
        if (value is not { Length: 4 } || value[2] < 0 || value[3] < 0)
            return false;

        rect = new TextRect(value[0], value[1], value[2], value[3]);
        return true;
    }

    private readonly record struct TextRect(int X, int Y, int Width, int Height)
    {
        public int Right => X + Width;
        public int Bottom => Y + Height;

        public bool Contains(TextRect other)
            => other.X >= X && other.Y >= Y && other.Right <= Right && other.Bottom <= Bottom;

        public bool ContainsPoint(int x, int y)
            => x >= X && y >= Y && x < Right && y < Bottom;

        public bool Intersects(TextRect other)
            => X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y;

        public override string ToString()
            => $"[{X},{Y},{Width},{Height}]";
    }

    private static string? TextContainsFailureDetail(JsonElement result)
    {
        if (TryGetInt(result, "matched_count", out var matched) &&
            TryGetInt(result, "min_count", out var min))
        {
            if (matched < min)
                return $"matched {matched} < {min}";

            if (TryGetInt(result, "max_count", out var max) && matched > max)
                return $"matched {matched} > {max}";
        }
        return null;
    }

    private static string? TextNotContainsFailureDetail(JsonElement result)
    {
        if (TryGetInt(result, "matched_count", out var matched))
            return $"matched {matched}";
        return null;
    }

    private static bool TryGetInt(JsonElement obj, string propertyName, out int value)
    {
        value = 0;
        return obj.ValueKind == JsonValueKind.Object
            && obj.TryGetProperty(propertyName, out var property)
            && property.TryGetInt32(out value);
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
