using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using SdvTestFramework.Protocol.Reports;
using SdvTestFramework.Protocol.Scenarios;
using SdvTestFramework.Runner.Mcp.Scenarios;

namespace SdvTestFramework.Runner.Mcp.Tools;

/// <summary>
/// Load a <c>.test.json</c> scenario via <see cref="ScenarioLoader"/> and drive it through
/// the harness step-by-step. Returns a summary similar to <c>ScenarioReport</c>.
/// </summary>
/// <remarks>
/// This executor shares the non-bitmap RPC assertion evaluator with the CLI runner. Bitmap
/// assertions and static HTML report generation stay in the CLI path.
/// </remarks>
public sealed class RunScenarioTool : ITool
{
    public string Name => "run_scenario";
    public string Description =>
        "Execute a .test.json scenario. Returns {passed, assertions_run, assertions_passed, failures, duration_ms}.";

    public JsonElement InputSchema { get; } = JsonDocument.Parse("""
        {"type":"object","properties":{
           "path":{"type":"string"},
           "report_dir":{"type":"string","description":"Optional output directory for the HTML run report. Default: ./test-results/<auto-id>/"},
           "diff_format":{"type":"string","enum":["files","triptych","all"],"description":"Diff artifacts produced on bitmap-assertion failure. Default: files (3 separate PNGs)."}
         },"required":["path"]}
        """).RootElement;

    public async Task<McpToolResult> InvokeAsync(JsonElement args, ToolInvocationContext context, CancellationToken ct)
    {
        var life = context.Lifecycle;
        if (life is null) return McpToolResult.Error("lifecycle unavailable");
        if (!args.TryGetProperty("path", out var p) || p.ValueKind != JsonValueKind.String)
            return McpToolResult.Error("'path' is required");
        var path = p.GetString()!;

        string? userReportDir = null;
        if (args.TryGetProperty("report_dir", out var rdEl) && rdEl.ValueKind == JsonValueKind.String)
            userReportDir = rdEl.GetString();

        // diff_format is parsed for forward-compat; the MCP run_scenario path doesn't currently
        // evaluate bitmap assertions itself (see class XML doc) — full DSL eval is a Tier 3 followup.
        if (args.TryGetProperty("diff_format", out _)) { /* no-op for now */ }

        var baseDir = !string.IsNullOrEmpty(userReportDir)
            ? userReportDir
            : Path.Combine(Directory.GetCurrentDirectory(), "test-results");
        RunDirectory reportDir;
        try { reportDir = RunDirectory.Create(baseDir); }
        catch (System.Exception ex) { return McpToolResult.Error($"failed to create report dir: {ex.Message}"); }

        ScenarioSpec spec;
        try { spec = ScenarioLoader.Load(path); }
        catch (System.Exception ex) { return McpToolResult.Error($"load failed: {ex.Message}"); }

        var totalProgress = 1 + spec.Steps.Count + spec.Assertions.Count + 1;
        if (!string.IsNullOrEmpty(spec.Fixture))
            totalProgress++;
        var progress = 0;

        var failures = new List<string>();
        var started = Stopwatch.StartNew();
        int run = 0, passed = 0;
        var assertionEvaluator = new ScenarioAssertionEvaluator(new LifecycleScenarioAssertionRpc(life));

        try
        {
            // 1. scenario.begin
            var beginParams = JsonSerializer.SerializeToElement(new ScenarioBeginRequest
            {
                Name = spec.Name, Seed = spec.Config.Seed, Fixture = spec.Fixture,
            }, ProtocolJson.Options);
            await life.InvokeAsync("scenario.begin", beginParams, ct);
            progress++;
            await context.Progress.ReportAsync(progress, totalProgress, "scenario.begin", ct);

            // 2. fixture.load (if any)
            if (!string.IsNullOrEmpty(spec.Fixture))
            {
                var fxParams = JsonSerializer.SerializeToElement(
                    new FixtureLoadRequest { Name = spec.Fixture }, ProtocolJson.Options);
                await life.InvokeAsync("fixture.load", fxParams, ct);
                progress++;
                await context.Progress.ReportAsync(progress, totalProgress, $"fixture.load: {spec.Fixture}", ct);
            }

            // 3. steps
            for (var i = 0; i < spec.Steps.Count; i++)
            {
                var step = spec.Steps[i];
                if (step.Action == "wait.ms")
                {
                    int ms = 0;
                    if (step.Args is { ValueKind: JsonValueKind.Object } a
                        && a.TryGetProperty("ms", out var mel) && mel.TryGetInt32(out var parsed))
                        ms = parsed;
                    if (ms > 0) await Task.Delay(ms, ct);
                    progress++;
                    await context.Progress.ReportAsync(
                        progress,
                        totalProgress,
                        $"step {i + 1}/{spec.Steps.Count}: {step.Action}",
                        ct);
                    continue;
                }
                try { await life.InvokeAsync(step.Action, step.Args, ct); }
                catch (SdvRpcException ex)
                {
                    failures.Add($"step {step.Action}: {ex.Message}");
                    progress++;
                    await context.Progress.ReportAsync(
                        progress,
                        totalProgress,
                        $"step {i + 1}/{spec.Steps.Count} failed: {step.Action}",
                        ct);
                    goto done;
                }
                progress++;
                await context.Progress.ReportAsync(
                    progress,
                    totalProgress,
                    $"step {i + 1}/{spec.Steps.Count}: {step.Action}",
                    ct);
            }

            // 4. assertions
            for (var i = 0; i < spec.Assertions.Count; i++)
            {
                var assertion = spec.Assertions[i];
                run++;
                var evaluation = await assertionEvaluator.EvaluateAsync(assertion, ct);
                if (evaluation.Passed) passed++;
                else failures.Add($"assertion {run} {assertion.Type}: {FormatAssertionFailure(assertion, evaluation.Detail)}");
                progress++;
                await context.Progress.ReportAsync(
                    progress,
                    totalProgress,
                    evaluation.Passed
                        ? $"assertion {i + 1}/{spec.Assertions.Count}: {assertion.Type}"
                        : $"assertion {i + 1}/{spec.Assertions.Count} failed: {assertion.Type}",
                    ct);
            }

            done:
            // 5. scenario.end
            try
            {
                await life.InvokeAsync("scenario.end", null, ct);
                progress = totalProgress;
                await context.Progress.ReportAsync(progress, totalProgress, "scenario.end", ct);
            }
            catch (System.OperationCanceledException) { throw; }
            catch { }
        }
        catch (SdvRpcException ex) { failures.Add(ex.Message); }

        started.Stop();
        // report_index is a path-promise: the HTML generator lives in Runner (not referenced
        // by Runner.Mcp), so this tool does not write index.html itself. It points at where
        // the runner CLI would emit it once the user promotes the scenario to a CLI run.
        var report = new JsonObject
        {
            ["passed"] = failures.Count == 0,
            ["assertions_run"] = run,
            ["assertions_passed"] = passed,
            ["failures"] = new JsonArray(failures.Select(f => (JsonNode)f).ToArray()),
            ["duration_ms"] = (int)started.ElapsedMilliseconds,
            ["report_dir"] = reportDir.Root,
            ["report_index"] = Path.Combine(reportDir.Root, "index.html"),
        };
        return McpToolResult.Success(JsonDocument.Parse(report.ToJsonString()).RootElement);
    }

    private static string FormatAssertionFailure(ScenarioAssertion assertion, string? detail)
    {
        if (!string.IsNullOrWhiteSpace(assertion.Message) && !string.IsNullOrWhiteSpace(detail))
        {
            return string.Equals(assertion.Message, detail, System.StringComparison.Ordinal)
                ? detail
                : $"{assertion.Message}: {detail}";
        }

        return detail ?? assertion.Message ?? "failed";
    }
}
