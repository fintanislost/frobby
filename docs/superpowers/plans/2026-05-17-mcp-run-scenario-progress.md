# MCP Run Scenario Progress Notifications Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add protocol-native MCP `notifications/progress` updates to `run_scenario` when clients provide `_meta.progressToken`.

**Architecture:** Introduce a small `McpProgressReporter` and `ToolInvocationContext` in `Runner.Mcp`, pass that context from `McpServer` into tools, and have `RunScenarioTool` emit milestone progress as it executes. Final tool results stay unchanged; no progress token means no notification output.

**Tech Stack:** C# 12, .NET 10, xUnit, `System.Text.Json`, MCP JSON-RPC over existing `NdJsonWriter`.

---

## Spec Reference

Design spec: `docs/superpowers/specs/2026-05-17-mcp-run-scenario-progress-design.md`

Roadmap item: `docs/roadmap.md` Tier 3, "MCP streaming tool results".

## File Structure

- Create: `tests/Runner.Mcp.Tests/McpServerProgressTests.cs`
  - Server-level transport tests for progress notifications and final response compatibility.
- Create: `src/Runner.Mcp/McpProgressReporter.cs`
  - Optional progress reporter that serializes `notifications/progress` JSON-RPC notifications.
- Create: `src/Runner.Mcp/ToolInvocationContext.cs`
  - Tool invocation context containing `SdvLifecycle?` and `McpProgressReporter`.
- Modify: `src/Runner.Mcp/ITool.cs`
  - Replace direct lifecycle invocation with context-aware invocation.
- Modify: `src/Runner.Mcp/McpServer.cs`
  - Extract `_meta.progressToken`, create a reporter, and pass context into tools.
- Modify: all MCP tools under `src/Runner.Mcp/Tools/*.cs`
  - Accept `ToolInvocationContext` and use `context.Lifecycle` where needed.
- Modify: direct tool tests under `tests/Runner.Mcp.Tests/Tools/*.cs`
  - Pass `ToolInvocationContext` into direct tool calls.
- Modify: `tests/Runner.Mcp.Tests/ToolRegistryTests.cs`
  - Update the stub tool to the context-aware `ITool` signature.
- Modify: `src/Runner.Mcp/Tools/RunScenarioTool.cs`
  - Emit progress around begin, fixture load, steps, assertions, and cleanup.
- Modify: `docs/mcp-quickstart.md`
  - Document `_meta.progressToken` support.
- Modify: `docs/roadmap.md`
  - Move the progress item to Completed after implementation verification.

## Task 1: Add Red MCP Progress Tests

**Files:**
- Create: `tests/Runner.Mcp.Tests/McpServerProgressTests.cs`

- [ ] **Step 1: Write server-level failing tests**

Create `tests/Runner.Mcp.Tests/McpServerProgressTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Runner.Mcp;
using SdvTestFramework.Runner.Mcp.Tools;
using Xunit;

namespace SdvTestFramework.Runner.Mcp.Tests;

public class McpServerProgressTests
{
    private sealed class RecordingLifecycle : SdvLifecycle
    {
        public List<(string Method, string ParamsJson)> Calls { get; } = new();
        public Dictionary<string, string> Responses { get; } = new();
        public HashSet<string> FailMethods { get; } = new();

        public override Task<JsonElement> InvokeAsync(string method, JsonElement? p, CancellationToken ct)
        {
            Calls.Add((method, p?.GetRawText() ?? ""));
            if (FailMethods.Contains(method))
                throw new SdvRpcException(method, JsonRpcErrorCode.InternalError, "forced failure");

            var response = Responses.TryGetValue(method, out var json) ? json : "{}";
            return Task.FromResult(JsonDocument.Parse(response).RootElement.Clone());
        }
    }

    [Fact]
    public async Task RunScenario_WithProgressToken_EmitsProgressNotificationsBeforeFinalResponse()
    {
        var scenario = """
        {
          "name": "progress_ok",
          "config": { "seed": 42 },
          "steps": [
            { "action": "player.warp", "args": { "location": "Farm", "x": 1, "y": 2 } }
          ],
          "assertions": [
            { "type": "state", "expr": "state.player.money == 500", "message": "money seeded" }
          ]
        }
        """;

        var life = CreateLifecycle();
        life.Responses["state.player"] = """
        {"name":"Tester","money":500,"location":"Farm","tile":{"x":1,"y":2},"items":[]}
        """;

        var lines = await RunScenarioThroughServerAsync(
            scenario,
            life,
            progressTokenJson: "\"scenario-01\"");

        Assert.Equal(5, lines.Length);

        var notifications = ParseNotifications(lines);
        Assert.Equal(4, notifications.Count);
        AssertProgress(notifications[0], "scenario-01", progress: 1, total: 4, message: "scenario.begin");
        AssertProgress(notifications[1], "scenario-01", progress: 2, total: 4, message: "step 1/1: player.warp");
        AssertProgress(notifications[2], "scenario-01", progress: 3, total: 4, message: "assertion 1/1: state");
        AssertProgress(notifications[3], "scenario-01", progress: 4, total: 4, message: "scenario.end");

        var final = JsonDocument.Parse(lines[^1]).RootElement;
        Assert.Equal(9, final.GetProperty("id").GetInt32());
        var toolText = final.GetProperty("result")
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString()!;
        using var toolDoc = JsonDocument.Parse(toolText);
        Assert.True(toolDoc.RootElement.GetProperty("passed").GetBoolean());
    }

    [Fact]
    public async Task RunScenario_WithoutProgressToken_EmitsOnlyFinalResponse()
    {
        var scenario = """
        {
          "name": "progress_none",
          "config": { "seed": 42 },
          "steps": [],
          "assertions": []
        }
        """;

        var lines = await RunScenarioThroughServerAsync(
            scenario,
            CreateLifecycle(),
            progressTokenJson: null);

        Assert.Single(lines);
        var final = JsonDocument.Parse(lines[0]).RootElement;
        Assert.Equal(9, final.GetProperty("id").GetInt32());
        Assert.False(final.TryGetProperty("method", out _));
    }

    [Fact]
    public async Task RunScenario_WithStepFailure_EmitsFailedStepProgressAndPassedFalse()
    {
        var scenario = """
        {
          "name": "progress_step_fail",
          "config": { "seed": 42 },
          "steps": [
            { "action": "player.warp", "args": { "location": "Farm", "x": 1, "y": 2 } }
          ],
          "assertions": []
        }
        """;

        var life = CreateLifecycle();
        life.FailMethods.Add("player.warp");

        var lines = await RunScenarioThroughServerAsync(
            scenario,
            life,
            progressTokenJson: "\"scenario-fail\"");

        var notifications = ParseNotifications(lines);
        Assert.Equal(3, notifications.Count);
        AssertProgress(notifications[0], "scenario-fail", progress: 1, total: 3, message: "scenario.begin");
        AssertProgress(notifications[1], "scenario-fail", progress: 2, total: 3, message: "step 1/1 failed: player.warp");
        AssertProgress(notifications[2], "scenario-fail", progress: 3, total: 3, message: "scenario.end");

        var final = JsonDocument.Parse(lines[^1]).RootElement;
        var toolText = final.GetProperty("result")
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString()!;
        using var toolDoc = JsonDocument.Parse(toolText);
        Assert.False(toolDoc.RootElement.GetProperty("passed").GetBoolean());
        Assert.Contains("player.warp", toolText);
    }

    private static RecordingLifecycle CreateLifecycle()
    {
        var life = new RecordingLifecycle();
        life.Responses["scenario.begin"] = "{\"session_id\":\"x\",\"tick\":0}";
        life.Responses["scenario.end"] = "{\"duration_ms\":1,\"assertions_run\":0,\"assertions_passed\":0}";
        return life;
    }

    private static async Task<string[]> RunScenarioThroughServerAsync(
        string scenarioJson,
        RecordingLifecycle life,
        string? progressTokenJson)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mcp-progress-{Guid.NewGuid():N}.test.json");
        var reportBase = Path.Combine(Path.GetTempPath(), $"mcp-progress-report-{Guid.NewGuid():N}");
        Directory.CreateDirectory(reportBase);
        await File.WriteAllTextAsync(path, scenarioJson);

        try
        {
            var meta = progressTokenJson is null
                ? ""
                : $$","_meta":{"progressToken":{{progressTokenJson}}}""";
            var request = $$"""
            {"jsonrpc":"2.0","id":9,"method":"tools/call","params":{"name":"run_scenario","arguments":{"path":{{JsonSerializer.Serialize(path)}},"report_dir":{{JsonSerializer.Serialize(reportBase)}}}{{meta}}}}
            """;
            var input = Encoding.UTF8.GetBytes(request + "\n");
            using var stdin = new MemoryStream(input);
            using var stdout = new MemoryStream();

            var registry = new ToolRegistry();
            registry.Register(new RunScenarioTool());
            var server = new McpServer(registry, life);
            await server.RunAsync(stdin, stdout, CancellationToken.None);

            var output = Encoding.UTF8.GetString(stdout.ToArray());
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
            if (Directory.Exists(reportBase))
                Directory.Delete(reportBase, recursive: true);
        }
    }

    private static List<JsonElement> ParseNotifications(string[] lines)
    {
        var notifications = new List<JsonElement>();
        foreach (var line in lines)
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.TryGetProperty("method", out var method) &&
                method.GetString() == "notifications/progress")
            {
                notifications.Add(root.Clone());
            }
        }

        return notifications;
    }

    private static void AssertProgress(
        JsonElement notification,
        string token,
        int progress,
        int total,
        string message)
    {
        Assert.Equal("2.0", notification.GetProperty("jsonrpc").GetString());
        Assert.False(notification.TryGetProperty("id", out _));
        Assert.Equal("notifications/progress", notification.GetProperty("method").GetString());

        var parameters = notification.GetProperty("params");
        Assert.Equal(token, parameters.GetProperty("progressToken").GetString());
        Assert.Equal(progress, parameters.GetProperty("progress").GetInt32());
        Assert.Equal(total, parameters.GetProperty("total").GetInt32());
        Assert.Equal(message, parameters.GetProperty("message").GetString());
    }
}
```

- [ ] **Step 2: Run tests and verify RED**

Run:

```bash
dotnet test tests/Runner.Mcp.Tests/Runner.Mcp.Tests.csproj --filter "FullyQualifiedName~McpServerProgressTests" -v minimal
```

Expected: compile succeeds and at least `RunScenario_WithProgressToken_EmitsProgressNotificationsBeforeFinalResponse` fails because only the final tool response is written.

- [ ] **Step 3: Commit red tests**

```bash
git add tests/Runner.Mcp.Tests/McpServerProgressTests.cs
git commit -m "test: cover MCP run_scenario progress notifications"
```

## Task 2: Add Progress Context And Reporter

**Files:**
- Create: `src/Runner.Mcp/McpProgressReporter.cs`
- Create: `src/Runner.Mcp/ToolInvocationContext.cs`
- Modify: `src/Runner.Mcp/ITool.cs`
- Modify: `src/Runner.Mcp/Tools/CaptureStateTool.cs`
- Modify: `src/Runner.Mcp/Tools/ListFixturesTool.cs`
- Modify: `src/Runner.Mcp/Tools/ListScenariosTool.cs`
- Modify: `src/Runner.Mcp/Tools/RpcCallTool.cs`
- Modify: `src/Runner.Mcp/Tools/RunScenarioTool.cs`
- Modify: `src/Runner.Mcp/Tools/ScaffoldScenarioTool.cs`
- Modify: `src/Runner.Mcp/Tools/WarpAndAssertDrawTool.cs`
- Modify: `src/Runner.Mcp/McpServer.cs`

- [ ] **Step 1: Add the no-op-capable progress reporter**

Create `src/Runner.Mcp/McpProgressReporter.cs`:

```csharp
using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;

namespace SdvTestFramework.Runner.Mcp;

public sealed class McpProgressReporter
{
    private readonly JsonElement? _progressToken;
    private readonly Func<JsonRpcNotification, CancellationToken, Task> _writeNotificationAsync;

    public static McpProgressReporter None { get; } = new(null, static (_, _) => Task.CompletedTask);

    public McpProgressReporter(
        JsonElement? progressToken,
        Func<JsonRpcNotification, CancellationToken, Task> writeNotificationAsync)
    {
        _progressToken = progressToken is { } token ? token.Clone() : null;
        _writeNotificationAsync = writeNotificationAsync;
    }

    public bool Enabled => _progressToken.HasValue;

    public Task ReportAsync(int progress, int? total, string message, CancellationToken cancellationToken)
    {
        if (_progressToken is not { } token)
            return Task.CompletedTask;

        var parameters = new JsonObject
        {
            ["progressToken"] = JsonNode.Parse(token.GetRawText())!,
            ["progress"] = progress,
            ["message"] = message,
        };
        if (total is { } totalValue)
            parameters["total"] = totalValue;

        using var doc = JsonDocument.Parse(parameters.ToJsonString());
        var notification = new JsonRpcNotification
        {
            Method = "notifications/progress",
            Params = doc.RootElement.Clone(),
        };
        return _writeNotificationAsync(notification, cancellationToken);
    }
}
```

- [ ] **Step 2: Add the tool invocation context**

Create `src/Runner.Mcp/ToolInvocationContext.cs`:

```csharp
namespace SdvTestFramework.Runner.Mcp;

public sealed class ToolInvocationContext
{
    public ToolInvocationContext(SdvLifecycle? lifecycle, McpProgressReporter progress)
    {
        Lifecycle = lifecycle;
        Progress = progress;
    }

    public SdvLifecycle? Lifecycle { get; }
    public McpProgressReporter Progress { get; }
}
```

- [ ] **Step 3: Update the tool interface**

Replace the `InvokeAsync` signature in `src/Runner.Mcp/ITool.cs` with:

```csharp
/// <summary>Invoke the tool. Context carries lifecycle plus optional request-scoped MCP utilities.</summary>
Task<McpToolResult> InvokeAsync(JsonElement args, ToolInvocationContext context, CancellationToken ct);
```

- [ ] **Step 4: Update stateless tools to accept context**

In `src/Runner.Mcp/Tools/ListScenariosTool.cs`, `src/Runner.Mcp/Tools/ListFixturesTool.cs`, and `src/Runner.Mcp/Tools/ScaffoldScenarioTool.cs`, replace:

```csharp
public Task<McpToolResult> InvokeAsync(JsonElement args, SdvLifecycle? lifecycle, CancellationToken ct)
```

with:

```csharp
public Task<McpToolResult> InvokeAsync(JsonElement args, ToolInvocationContext context, CancellationToken ct)
```

These tools do not need to read `context`.

- [ ] **Step 5: Update lifecycle-backed tools to read context**

For `CaptureStateTool`, `RpcCallTool`, `RunScenarioTool`, and `WarpAndAssertDrawTool`, replace the method signature with:

```csharp
public async Task<McpToolResult> InvokeAsync(JsonElement args, ToolInvocationContext context, CancellationToken ct)
```

At the top of each method, add:

```csharp
var life = context.Lifecycle;
```

Keep the existing null-lifecycle checks unchanged after that assignment.

- [ ] **Step 6: Update the server to pass a no-op context**

In `src/Runner.Mcp/McpServer.cs`, replace:

```csharp
try { result = await tool.InvokeAsync(args, _lifecycle, ct); }
catch (Exception ex) { result = McpToolResult.Error($"tool '{name}' threw: {ex.Message}"); }
```

with:

```csharp
var context = new ToolInvocationContext(_lifecycle, McpProgressReporter.None);

try { result = await tool.InvokeAsync(args, context, ct); }
catch (Exception ex) { result = McpToolResult.Error($"tool '{name}' threw: {ex.Message}"); }
```

- [ ] **Step 7: Build and verify compile errors are resolved in production**

Run:

```bash
dotnet build src/Runner.Mcp/Runner.Mcp.csproj
```

Expected: production build passes, but tests that directly call tools may still need context updates in the next task.

- [ ] **Step 8: Commit context and reporter**

```bash
git add src/Runner.Mcp/McpProgressReporter.cs src/Runner.Mcp/ToolInvocationContext.cs src/Runner.Mcp/ITool.cs src/Runner.Mcp/Tools src/Runner.Mcp/McpServer.cs
git commit -m "feat: add MCP tool progress context"
```

## Task 3: Wire Server Progress Tokens

**Files:**
- Modify: `src/Runner.Mcp/McpServer.cs`
- Modify: direct tool tests under `tests/Runner.Mcp.Tests/Tools/*.cs`
- Modify: `tests/Runner.Mcp.Tests/ToolRegistryTests.cs`

- [ ] **Step 1: Pass context from `McpServer` into tools**

In `src/Runner.Mcp/McpServer.cs`, replace the no-op progress context:

```csharp
var context = new ToolInvocationContext(_lifecycle, McpProgressReporter.None);

try { result = await tool.InvokeAsync(args, context, ct); }
catch (Exception ex) { result = McpToolResult.Error($"tool '{name}' threw: {ex.Message}"); }
```

with:

```csharp
var progress = new McpProgressReporter(
    TryGetProgressToken(p),
    (notification, token) => writer.WriteAsync(JsonRpcCodec.Serialize(notification), token));
var context = new ToolInvocationContext(_lifecycle, progress);

try { result = await tool.InvokeAsync(args, context, ct); }
catch (Exception ex) { result = McpToolResult.Error($"tool '{name}' threw: {ex.Message}"); }
```

- [ ] **Step 2: Add progress token extraction helper**

Add this helper to `McpServer`:

```csharp
private static JsonElement? TryGetProgressToken(JsonElement toolParams)
{
    if (!toolParams.TryGetProperty("_meta", out var meta) ||
        meta.ValueKind != JsonValueKind.Object)
    {
        return null;
    }

    if (!meta.TryGetProperty("progressToken", out var token))
        return null;

    return token.ValueKind is JsonValueKind.String or JsonValueKind.Number
        ? token.Clone()
        : null;
}
```

- [ ] **Step 3: Update direct tool tests to pass context**

In tests that directly call `tool.InvokeAsync(args, life, CancellationToken.None)`, replace calls with:

```csharp
var context = new ToolInvocationContext(life, McpProgressReporter.None);
var result = await tool.InvokeAsync(args, context, CancellationToken.None);
```

In tests that pass `lifecycle: null`, use:

```csharp
var context = new ToolInvocationContext(lifecycle: null, McpProgressReporter.None);
var result = await tool.InvokeAsync(args, context, CancellationToken.None);
```

Run this search to confirm no old direct calls remain:

```bash
rg "InvokeAsync\\(args, (life|lifecycle|null)" tests/Runner.Mcp.Tests src/Runner.Mcp
```

Expected: no matches.

- [ ] **Step 4: Update the tool registry stub**

In `tests/Runner.Mcp.Tests/ToolRegistryTests.cs`, replace the stub tool method with:

```csharp
public Task<McpToolResult> InvokeAsync(JsonElement args, ToolInvocationContext context, CancellationToken ct)
    => Task.FromResult(McpToolResult.Success(JsonDocument.Parse("{}").RootElement));
```

- [ ] **Step 5: Run progress tests and confirm still RED behaviorally**

Run:

```bash
dotnet test tests/Runner.Mcp.Tests/Runner.Mcp.Tests.csproj --filter "FullyQualifiedName~McpServerProgressTests" -v minimal
```

Expected: tests compile, no-token test may pass, progress-token tests still fail because `RunScenarioTool` has not emitted reporter events yet.

- [ ] **Step 6: Commit server token plumbing**

```bash
git add src/Runner.Mcp/McpServer.cs tests/Runner.Mcp.Tests
git commit -m "feat: pass MCP progress context to tools"
```

## Task 4: Emit Run Scenario Progress

**Files:**
- Modify: `src/Runner.Mcp/Tools/RunScenarioTool.cs`

- [ ] **Step 1: Add progress counters after scenario load**

After `ScenarioSpec spec` is loaded and before the `try` block, add:

```csharp
var totalProgress = 1 + spec.Steps.Count + spec.Assertions.Count + 1;
if (!string.IsNullOrEmpty(spec.Fixture))
    totalProgress++;
var progress = 0;
```

- [ ] **Step 2: Report scenario begin**

Immediately after successful `scenario.begin`, add:

```csharp
progress++;
await context.Progress.ReportAsync(progress, totalProgress, "scenario.begin", ct);
```

- [ ] **Step 3: Report fixture load when present**

Immediately after successful `fixture.load`, add:

```csharp
progress++;
await context.Progress.ReportAsync(progress, totalProgress, $"fixture.load: {spec.Fixture}", ct);
```

- [ ] **Step 4: Report step progress and failed step progress**

Change the steps loop to track indexes:

```csharp
for (var stepIndex = 0; stepIndex < spec.Steps.Count; stepIndex++)
{
    var step = spec.Steps[stepIndex];
    var stepNumber = stepIndex + 1;
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
            $"step {stepNumber}/{spec.Steps.Count}: {step.Action}",
            ct);
        continue;
    }

    try
    {
        await life.InvokeAsync(step.Action, step.Args, ct);
        progress++;
        await context.Progress.ReportAsync(
            progress,
            totalProgress,
            $"step {stepNumber}/{spec.Steps.Count}: {step.Action}",
            ct);
    }
    catch (SdvRpcException ex)
    {
        failures.Add($"step {step.Action}: {ex.Message}");
        progress++;
        await context.Progress.ReportAsync(
            progress,
            totalProgress,
            $"step {stepNumber}/{spec.Steps.Count} failed: {step.Action}",
            ct);
        goto done;
    }
}
```

- [ ] **Step 5: Report assertion progress and failed assertion progress**

Change the assertion loop to track indexes:

```csharp
for (var assertionIndex = 0; assertionIndex < spec.Assertions.Count; assertionIndex++)
{
    var assertion = spec.Assertions[assertionIndex];
    run++;
    var evaluation = await assertionEvaluator.EvaluateAsync(assertion, ct);
    if (evaluation.Passed)
    {
        passed++;
        progress++;
        await context.Progress.ReportAsync(
            progress,
            totalProgress,
            $"assertion {assertionIndex + 1}/{spec.Assertions.Count}: {assertion.Type}",
            ct);
    }
    else
    {
        failures.Add($"assertion {run} {assertion.Type}: {FormatAssertionFailure(assertion, evaluation.Detail)}");
        progress++;
        await context.Progress.ReportAsync(
            progress,
            totalProgress,
            $"assertion {assertionIndex + 1}/{spec.Assertions.Count} failed: {assertion.Type}",
            ct);
    }
}
```

- [ ] **Step 6: Report scenario end only after successful cleanup RPC**

Replace the current best-effort cleanup:

```csharp
try { await life.InvokeAsync("scenario.end", null, ct); } catch { }
```

with:

```csharp
try
{
    await life.InvokeAsync("scenario.end", null, ct);
    progress++;
    await context.Progress.ReportAsync(progress, totalProgress, "scenario.end", ct);
}
catch { }
```

- [ ] **Step 7: Run progress tests and verify GREEN**

Run:

```bash
dotnet test tests/Runner.Mcp.Tests/Runner.Mcp.Tests.csproj --filter "FullyQualifiedName~McpServerProgressTests" -v minimal
```

Expected: all progress tests pass.

- [ ] **Step 8: Run full MCP tests**

Run:

```bash
dotnet test tests/Runner.Mcp.Tests/Runner.Mcp.Tests.csproj -v minimal
```

Expected: all non-live MCP tests pass; the live SDV integration test remains skipped unless explicitly enabled.

- [ ] **Step 9: Commit run_scenario progress emission**

```bash
git add src/Runner.Mcp/Tools/RunScenarioTool.cs tests/Runner.Mcp.Tests
git commit -m "feat: emit MCP run_scenario progress"
```

## Task 5: Update Docs And Roadmap

**Files:**
- Modify: `docs/mcp-quickstart.md`
- Modify: `docs/roadmap.md`

- [ ] **Step 1: Update MCP quickstart**

In `docs/mcp-quickstart.md`, after the `run_scenario` assertion support paragraph, add:

```markdown
MCP clients that support progress can send `_meta.progressToken` on `tools/call`
requests for `run_scenario`. Frobby then emits `notifications/progress` after
scenario setup, each step, each assertion, and cleanup. Progress messages are
best-effort status updates; the final tool result keeps the same JSON summary shape.
```

- [ ] **Step 2: Move roadmap item to completed**

In `docs/roadmap.md`, remove this Tier 3 item:

```markdown
- [ ] **MCP streaming tool results** (~2 days) — incremental updates for long-running
  scenarios via MCP. Today `run_scenario` is synchronous; LLM sees nothing until
  completion. Streaming lets Claude watch step-by-step. Source: M3-MCP out-of-scope.
```

Add this under `## Completed` for `2026-05-17`:

```markdown
- **MCP `run_scenario` progress notifications**. Added request-scoped
  `_meta.progressToken` support for `run_scenario`, emitting protocol-native
  `notifications/progress` for scenario begin, optional fixture load, each step,
  each assertion, and scenario cleanup while preserving the final tool result shape.
```

- [ ] **Step 3: Run docs consistency checks**

Run:

```bash
rg -n "MCP streaming tool results|run_scenario.*synchronous|progressToken|notifications/progress" docs/mcp-quickstart.md docs/roadmap.md docs/superpowers/specs/2026-05-17-mcp-run-scenario-progress-design.md
```

Expected:
- no pending Tier 3 `MCP streaming tool results` item remains
- `progressToken` and `notifications/progress` appear in quickstart and spec
- roadmap completed section mentions progress notifications

- [ ] **Step 4: Commit docs**

```bash
git add docs/mcp-quickstart.md docs/roadmap.md
git commit -m "docs: document MCP run_scenario progress"
```

## Task 6: Final Verification

**Files:**
- Verify all modified code and docs.

- [ ] **Step 1: Run focused progress tests**

```bash
dotnet test tests/Runner.Mcp.Tests/Runner.Mcp.Tests.csproj --filter "FullyQualifiedName~McpServerProgressTests" -v minimal
```

Expected: all progress tests pass.

- [ ] **Step 2: Run full MCP tests**

```bash
dotnet test tests/Runner.Mcp.Tests/Runner.Mcp.Tests.csproj -v minimal
```

Expected: all non-live MCP tests pass, with the existing live SDV integration skipped.

- [ ] **Step 3: Run full Runner tests for shared-project safety**

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj -v minimal
```

Expected: all non-live Runner tests pass, with existing live integration skips unchanged.

- [ ] **Step 4: Run solution build**

```bash
dotnet build sdv-test-framework.slnx
```

Expected: build passes with 0 warnings and 0 errors.

- [ ] **Step 5: Check whitespace and git state**

```bash
git diff --check
git status --short --branch
```

Expected:
- `git diff --check` prints nothing and exits 0
- only intentional committed changes remain; working tree is clean after commits

- [ ] **Step 6: Final review**

Run:

```bash
git log --oneline --decorate -8
git diff origin/main..HEAD --stat
```

Expected:
- branch contains the design-plan commit plus implementation commits
- diff stat includes MCP progress code, tests, and docs only

## Execution Notes

- Do not run full `dotnet test` commands in parallel in the same worktree. The project
  writes shared `obj/bin` outputs and parallel `dotnet` invocations can lock each
  other's files.
- Keep `run_scenario` final JSON shape backward compatible.
- Progress notifications are transport messages, not scenario result content.
- Do not launch Stardew Valley for these tests; use fake lifecycle instances.
