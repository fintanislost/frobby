# MCP Run Scenario Assertion Parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make MCP `run_scenario` evaluate the shared non-bitmap assertion set that already works in the CLI runner.

**Architecture:** Extract CLI assertion evaluation into a shared evaluator in the Runner.Mcp assembly, adapt it to both `SdvLifecycle` and `JsonRpcSession`, and let `RunScenarioTool` and `ScenarioRunner` call that evaluator. Bitmap and report-specific assertion behavior remains in `ScenarioRunner`.

**Tech Stack:** C# 12, .NET 10 runner/MCP projects, xUnit, `System.Text.Json`, existing Frobby JSON-RPC protocol models.

---

## Spec Reference

Design spec: `docs/superpowers/specs/2026-05-16-mcp-run-scenario-assertion-parity-design.md`

Roadmap item: `docs/roadmap.md` Tier 3, "Full DSL assertion eval in MCP `run_scenario`".

## File Structure

- Create: `src/Runner.Mcp/Scenarios/IScenarioAssertionRpc.cs`
  - RPC abstraction used by the shared evaluator.
- Create: `src/Runner.Mcp/Scenarios/ScenarioAssertionRpcResult.cs`
  - Success/error wrapper for assertion RPC calls.
- Create: `src/Runner.Mcp/Scenarios/ScenarioAssertionEvaluationResult.cs`
  - Result returned for one evaluated assertion.
- Create: `src/Runner.Mcp/Scenarios/ScenarioAssertionEvaluator.cs`
  - Shared non-bitmap assertion evaluator.
- Create: `src/Runner.Mcp/Scenarios/LifecycleScenarioAssertionRpc.cs`
  - Adapter from MCP `SdvLifecycle` to `IScenarioAssertionRpc`.
- Create: `src/Runner/Scenarios/JsonRpcSessionScenarioAssertionRpc.cs`
  - Adapter from CLI `JsonRpcSession` to `IScenarioAssertionRpc`.
- Modify: `src/Runner.Mcp/Tools/RunScenarioTool.cs`
  - Replace local `draw.contains` only logic with shared evaluator.
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`
  - Delegate shared assertion cases to `ScenarioAssertionEvaluator`.
  - Keep `bitmap` and `draw.text_all_within` local.
- Create: `tests/Runner.Mcp.Tests/Tools/RunScenarioAssertionParityTests.cs`
  - MCP TDD tests proving state, params, content asset, RPC-result, and draw assertion support.
- Modify: `tests/Runner.Tests/ScenarioRunnerDslTests.cs`
  - Add one regression that proves the CLI path still returns useful detail after extraction.
- Modify: `docs/mcp-quickstart.md`
  - Document new MCP assertion support and remaining CLI-only items.
- Modify: `docs/roadmap.md`
  - Move the Tier 3 roadmap item to Completed after tests pass.

## Task 1: Add Failing MCP Parity Tests

**Files:**
- Create: `tests/Runner.Mcp.Tests/Tools/RunScenarioAssertionParityTests.cs`

- [ ] **Step 1: Add the red MCP tests**

Create `tests/Runner.Mcp.Tests/Tools/RunScenarioAssertionParityTests.cs` with:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Mcp;
using SdvTestFramework.Runner.Mcp.Tools;
using Xunit;

namespace SdvTestFramework.Runner.Mcp.Tests.Tools;

public class RunScenarioAssertionParityTests
{
    private sealed class RecordingLifecycle : SdvLifecycle
    {
        public List<(string Method, string ParamsJson)> Calls { get; } = new();
        public Dictionary<string, string> Responses { get; } = new();

        public override Task<JsonElement> InvokeAsync(string method, JsonElement? p, CancellationToken ct)
        {
            Calls.Add((method, p?.GetRawText() ?? ""));
            var response = Responses.TryGetValue(method, out var json) ? json : "{}";
            return Task.FromResult(JsonDocument.Parse(response).RootElement.Clone());
        }
    }

    [Fact]
    public async Task RunScenario_EvaluatesPassingStateAssertion()
    {
        var scenario = """
        {
          "name": "mcp_state_pass",
          "config": { "seed": 42 },
          "steps": [],
          "assertions": [
            { "type": "state", "expr": "state.player.money == 500", "message": "money seeded" }
          ]
        }
        """;

        var (result, life) = await RunScenarioAsync(scenario, new Dictionary<string, string>
        {
            ["state.player"] = "{\"name\":\"Tester\",\"money\":500,\"location\":\"Farm\",\"tile\":{\"x\":0,\"y\":0},\"items\":[]}",
        });

        Assert.False(result.IsError);
        Assert.Contains("\"passed\":true", result.Text);
        Assert.Contains("\"assertions_run\":1", result.Text);
        Assert.Contains("\"assertions_passed\":1", result.Text);
        Assert.Contains(life.Calls, call => call.Method == "state.player");
    }

    [Fact]
    public async Task RunScenario_ReturnsFailureForFailingStateAssertion()
    {
        var scenario = """
        {
          "name": "mcp_state_fail",
          "config": { "seed": 42 },
          "steps": [],
          "assertions": [
            { "type": "state", "expr": "state.player.money == 500", "message": "money seeded" }
          ]
        }
        """;

        var (result, _) = await RunScenarioAsync(scenario, new Dictionary<string, string>
        {
            ["state.player"] = "{\"name\":\"Tester\",\"money\":499,\"location\":\"Farm\",\"tile\":{\"x\":0,\"y\":0},\"items\":[]}",
        });

        Assert.False(result.IsError);
        Assert.Contains("\"passed\":false", result.Text);
        Assert.Contains("assertion 1 state", result.Text);
        Assert.Contains("money seeded", result.Text);
        Assert.Contains("state.player.money", result.Text);
    }

    [Fact]
    public async Task RunScenario_PassesStateAssertionParams()
    {
        var scenario = """
        {
          "name": "mcp_state_params",
          "config": { "seed": 42 },
          "steps": [],
          "assertions": [
            {
              "type": "state",
              "expr": "state.npc.hearts == 4",
              "params": { "name": "Sophia" },
              "message": "Sophia hearts"
            }
          ]
        }
        """;

        var (result, life) = await RunScenarioAsync(scenario, new Dictionary<string, string>
        {
            ["state.npc"] = "{\"name\":\"Sophia\",\"hearts\":4,\"location\":\"Town\"}",
        });

        Assert.False(result.IsError);
        Assert.Contains("\"passed\":true", result.Text);
        Assert.Contains(life.Calls, call =>
            call.Method == "state.npc"
            && call.ParamsJson.Contains("\"name\":\"Sophia\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunScenario_EvaluatesContentAssetAssertion()
    {
        var scenario = """
        {
          "name": "mcp_content_asset",
          "config": { "seed": 42 },
          "steps": [],
          "assertions": [
            {
              "type": "content.asset",
              "asset": "Maps/Custom_TownEast",
              "asset_type": "map",
              "expr": "asset.layers contains name 'Back'",
              "message": "map has Back layer"
            }
          ]
        }
        """;

        var (result, life) = await RunScenarioAsync(scenario, new Dictionary<string, string>
        {
            ["content.asset"] = """
            {
              "name": "Maps/Custom_TownEast",
              "exists": true,
              "kind": "map",
              "runtime_type": "xTile.Map",
              "summary": {
                "width": 90,
                "height": 64,
                "layers": [ { "name": "Back" }, { "name": "Buildings" } ]
              }
            }
            """,
        });

        Assert.False(result.IsError);
        Assert.Contains("\"passed\":true", result.Text);
        Assert.Contains(life.Calls, call =>
            call.Method == "content.asset"
            && call.ParamsJson.Contains("\"name\":\"Maps/Custom_TownEast\"", StringComparison.Ordinal)
            && call.ParamsJson.Contains("\"asset_type\":\"map\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunScenario_EvaluatesRpcResultAssertion()
    {
        var scenario = """
        {
          "name": "mcp_fishing_table",
          "config": { "seed": 42 },
          "steps": [],
          "assertions": [
            {
              "type": "state.fishing_table",
              "params": { "location": "Desert", "x": 28, "y": 6 },
              "expr": "result.candidates contains item_id '164'",
              "message": "Sandfish candidate visible"
            }
          ]
        }
        """;

        var (result, life) = await RunScenarioAsync(scenario, new Dictionary<string, string>
        {
            ["state.fishing_table"] = """
            {
              "location": "Desert",
              "is_fishable": true,
              "candidates": [
                { "item_id": "2334", "qualified_id": "(F)2334", "display_name": "Pyramid Decal" },
                { "item_id": "164", "qualified_id": "(O)164", "display_name": "Sandfish" }
              ]
            }
            """,
        });

        Assert.False(result.IsError);
        Assert.Contains("\"passed\":true", result.Text);
        Assert.Contains(life.Calls, call =>
            call.Method == "state.fishing_table"
            && call.ParamsJson.Contains("\"location\":\"Desert\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunScenario_EvaluatesDrawNotContainsAssertion()
    {
        var scenario = """
        {
          "name": "mcp_draw_not_contains",
          "config": { "seed": 42 },
          "steps": [],
          "assertions": [
            {
              "type": "draw.not_contains",
              "filter": { "texture_asset": "LooseSprites/Cursors" },
              "message": "cursor should be absent"
            }
          ]
        }
        """;

        var (result, life) = await RunScenarioAsync(scenario, new Dictionary<string, string>
        {
            ["draw.assert_not_contains"] = "{\"passed\":true,\"matched_count\":0}",
        });

        Assert.False(result.IsError);
        Assert.Contains("\"passed\":true", result.Text);
        Assert.Contains(life.Calls, call => call.Method == "draw.assert_not_contains");
    }

    private static async Task<(McpToolResult Result, RecordingLifecycle Life)> RunScenarioAsync(
        string scenarioJson,
        Dictionary<string, string> methodResponses)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mcp-parity-{Guid.NewGuid():N}.test.json");
        var reportBase = Path.Combine(Path.GetTempPath(), $"mcp-parity-report-{Guid.NewGuid():N}");
        Directory.CreateDirectory(reportBase);
        await File.WriteAllTextAsync(path, scenarioJson);

        try
        {
            var life = new RecordingLifecycle();
            life.Responses["scenario.begin"] = "{\"session_id\":\"x\",\"tick\":0}";
            life.Responses["scenario.end"] = "{\"duration_ms\":1,\"assertions_run\":0,\"assertions_passed\":0}";
            foreach (var entry in methodResponses)
                life.Responses[entry.Key] = entry.Value;

            var tool = new RunScenarioTool();
            var args = JsonDocument.Parse($$"""
            {
              "path": {{JsonSerializer.Serialize(path)}},
              "report_dir": {{JsonSerializer.Serialize(reportBase)}}
            }
            """).RootElement;

            return (await tool.InvokeAsync(args, life, CancellationToken.None), life);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
            if (Directory.Exists(reportBase))
                Directory.Delete(reportBase, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Run the MCP tests and confirm they fail**

Run:

```bash
dotnet test tests/Runner.Mcp.Tests/Runner.Mcp.Tests.csproj --filter "FullyQualifiedName~RunScenarioAssertionParityTests" -v minimal
```

Expected: tests fail because `run_scenario` still reports unsupported assertion types other than `draw.contains`.

- [ ] **Step 3: Commit the red tests**

```bash
git add tests/Runner.Mcp.Tests/Tools/RunScenarioAssertionParityTests.cs
git commit -m "test: cover MCP scenario assertion parity"
```

## Task 2: Add Shared Assertion Evaluator Types

**Files:**
- Create: `src/Runner.Mcp/Scenarios/IScenarioAssertionRpc.cs`
- Create: `src/Runner.Mcp/Scenarios/ScenarioAssertionRpcResult.cs`
- Create: `src/Runner.Mcp/Scenarios/ScenarioAssertionEvaluationResult.cs`
- Create: `src/Runner.Mcp/Scenarios/LifecycleScenarioAssertionRpc.cs`
- Create: `src/Runner/Scenarios/JsonRpcSessionScenarioAssertionRpc.cs`

- [ ] **Step 1: Add the shared RPC abstraction**

Create `src/Runner.Mcp/Scenarios/IScenarioAssertionRpc.cs`:

```csharp
using System.Text.Json;

namespace SdvTestFramework.Runner.Mcp.Scenarios;

internal interface IScenarioAssertionRpc
{
    Task<ScenarioAssertionRpcResult> InvokeAsync(
        string method,
        JsonElement? parameters,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Add the shared RPC result**

Create `src/Runner.Mcp/Scenarios/ScenarioAssertionRpcResult.cs`:

```csharp
using System.Text.Json;

namespace SdvTestFramework.Runner.Mcp.Scenarios;

internal readonly record struct ScenarioAssertionRpcResult(JsonElement? Result, string? Error)
{
    public bool Succeeded => Error is null;

    public static ScenarioAssertionRpcResult Success(JsonElement result)
        => new(result.Clone(), Error: null);

    public static ScenarioAssertionRpcResult Failure(string error)
        => new(Result: null, Error: error);
}
```

- [ ] **Step 3: Add the shared evaluation result**

Create `src/Runner.Mcp/Scenarios/ScenarioAssertionEvaluationResult.cs`:

```csharp
namespace SdvTestFramework.Runner.Mcp.Scenarios;

internal readonly record struct ScenarioAssertionEvaluationResult(bool Passed, string? Detail)
{
    public static ScenarioAssertionEvaluationResult Pass()
        => new(Passed: true, Detail: null);

    public static ScenarioAssertionEvaluationResult Fail(string? detail)
        => new(Passed: false, Detail: detail);
}
```

- [ ] **Step 4: Add the MCP lifecycle adapter**

Create `src/Runner.Mcp/Scenarios/LifecycleScenarioAssertionRpc.cs`:

```csharp
using System.Text.Json;

namespace SdvTestFramework.Runner.Mcp.Scenarios;

internal sealed class LifecycleScenarioAssertionRpc : IScenarioAssertionRpc
{
    private readonly SdvLifecycle _lifecycle;

    public LifecycleScenarioAssertionRpc(SdvLifecycle lifecycle)
    {
        _lifecycle = lifecycle;
    }

    public async Task<ScenarioAssertionRpcResult> InvokeAsync(
        string method,
        JsonElement? parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _lifecycle.InvokeAsync(method, parameters, cancellationToken);
            return ScenarioAssertionRpcResult.Success(result);
        }
        catch (SdvRpcException ex)
        {
            return ScenarioAssertionRpcResult.Failure(ex.Message);
        }
    }
}
```

- [ ] **Step 5: Add the CLI JsonRpcSession adapter**

Create `src/Runner/Scenarios/JsonRpcSessionScenarioAssertionRpc.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Protocol;
using SdvTestFramework.Runner.Mcp.Scenarios;

namespace SdvTestFramework.Runner.Scenarios;

internal sealed class JsonRpcSessionScenarioAssertionRpc : IScenarioAssertionRpc
{
    private readonly JsonRpcSession _session;

    public JsonRpcSessionScenarioAssertionRpc(JsonRpcSession session)
    {
        _session = session;
    }

    public async Task<ScenarioAssertionRpcResult> InvokeAsync(
        string method,
        JsonElement? parameters,
        CancellationToken cancellationToken)
    {
        var response = await _session.InvokeAsync(method, parameters, cancellationToken);
        if (response.Error is { } error)
            return ScenarioAssertionRpcResult.Failure(error.Message);

        return ScenarioAssertionRpcResult.Success(response.Result ?? JsonDocument.Parse("{}").RootElement);
    }
}
```

- [ ] **Step 6: Build to verify the new files compile**

Run:

```bash
dotnet build sdv-test-framework.slnx
```

Expected: build passes.

- [ ] **Step 7: Commit the shared type scaffolding**

```bash
git add src/Runner.Mcp/Scenarios src/Runner/Scenarios/JsonRpcSessionScenarioAssertionRpc.cs
git commit -m "feat: add shared scenario assertion RPC adapters"
```

## Task 3: Extract Non-Bitmap Assertion Evaluation

**Files:**
- Create: `src/Runner.Mcp/Scenarios/ScenarioAssertionEvaluator.cs`
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`

- [ ] **Step 1: Create the evaluator class shell**

Create `src/Runner.Mcp/Scenarios/ScenarioAssertionEvaluator.cs`:

```csharp
using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Mcp.Scenarios;

internal sealed class ScenarioAssertionEvaluator
{
    private readonly IScenarioAssertionRpc _rpc;

    public ScenarioAssertionEvaluator(IScenarioAssertionRpc rpc)
    {
        _rpc = rpc;
    }

    public async Task<ScenarioAssertionEvaluationResult> EvaluateAsync(
        ScenarioAssertion assertion,
        CancellationToken cancellationToken)
    {
        switch (assertion.Type)
        {
            case "draw.contains":
                return await EvaluateDrawContainsAsync(assertion, cancellationToken);
            case "draw.not_contains":
                return await EvaluateDrawNotContainsAsync(assertion, cancellationToken);
            case "draw.text_contains":
                return await EvaluateDrawTextContainsAsync(assertion, cancellationToken);
            case "draw.text_not_contains":
                return await EvaluateDrawTextNotContainsAsync(assertion, cancellationToken);
            case "content.asset":
                return await EvaluateContentAssetAssertionAsync(assertion, cancellationToken);
            case "state.fishing_context":
            case "state.fishing_table":
            case "fishing.sample_catch":
                return await EvaluateRpcResultAssertionAsync(assertion.Type, assertion, cancellationToken);
            case "state":
                return await EvaluateStateAssertionAsync(assertion, cancellationToken);
            default:
                return ScenarioAssertionEvaluationResult.Fail(
                    $"assertion type '{assertion.Type}' is not supported by MCP run_scenario; use sdv-test run for bitmap/report-only assertions.");
        }
    }

    private async Task<ScenarioAssertionEvaluationResult> EvaluateDrawContainsAsync(
        ScenarioAssertion assertion,
        CancellationToken cancellationToken)
    {
        if (assertion.Filter is null)
            return ScenarioAssertionEvaluationResult.Fail("draw.contains requires filter");

        var payload = new
        {
            filter = assertion.Filter,
            min_count = assertion.MinCount,
            message = assertion.Message,
        };
        var rpc = await _rpc.InvokeAsync(
            "draw.assert_contains",
            JsonSerializer.SerializeToElement(payload, ProtocolJson.Options),
            cancellationToken);
        if (!rpc.Succeeded)
            return ScenarioAssertionEvaluationResult.Fail(rpc.Error);
        if (rpc.Result is not { } result)
            return ScenarioAssertionEvaluationResult.Fail("draw.assert_contains returned no result");

        var passed = result.TryGetProperty("passed", out var passedElement)
            && passedElement.ValueKind is JsonValueKind.True or JsonValueKind.False
            && passedElement.GetBoolean();
        return passed
            ? ScenarioAssertionEvaluationResult.Pass()
            : ScenarioAssertionEvaluationResult.Fail("draw.contains did not match");
    }

    private async Task<ScenarioAssertionEvaluationResult> EvaluateDrawNotContainsAsync(
        ScenarioAssertion assertion,
        CancellationToken cancellationToken)
    {
        if (assertion.Filter is null)
            return ScenarioAssertionEvaluationResult.Fail("draw.not_contains requires filter");

        var payload = new { filter = assertion.Filter, message = assertion.Message };
        var rpc = await _rpc.InvokeAsync(
            "draw.assert_not_contains",
            JsonSerializer.SerializeToElement(payload, ProtocolJson.Options),
            cancellationToken);
        if (!rpc.Succeeded)
            return ScenarioAssertionEvaluationResult.Fail(rpc.Error);
        if (rpc.Result is not { } result)
            return ScenarioAssertionEvaluationResult.Fail("draw.assert_not_contains returned no result");

        var passed = result.TryGetProperty("passed", out var passedElement)
            && passedElement.ValueKind is JsonValueKind.True or JsonValueKind.False
            && passedElement.GetBoolean();
        return passed
            ? ScenarioAssertionEvaluationResult.Pass()
            : ScenarioAssertionEvaluationResult.Fail(TextNotContainsFailureDetail(result) ?? "draw.not_contains matched");
    }

    private async Task<ScenarioAssertionEvaluationResult> EvaluateDrawTextContainsAsync(
        ScenarioAssertion assertion,
        CancellationToken cancellationToken)
    {
        if (assertion.Filter is null)
            return ScenarioAssertionEvaluationResult.Fail("draw.text_contains requires filter");

        var payload = new
        {
            filter = assertion.Filter,
            min_count = assertion.MinCount,
            max_count = assertion.MaxCount,
            message = assertion.Message,
        };
        var rpc = await _rpc.InvokeAsync(
            "draw.assert_text_contains",
            JsonSerializer.SerializeToElement(payload, ProtocolJson.Options),
            cancellationToken);
        if (!rpc.Succeeded)
            return ScenarioAssertionEvaluationResult.Fail(rpc.Error);
        if (rpc.Result is not { } result)
            return ScenarioAssertionEvaluationResult.Fail("draw.assert_text_contains returned no result");

        var passed = result.TryGetProperty("passed", out var passedElement)
            && passedElement.ValueKind is JsonValueKind.True or JsonValueKind.False
            && passedElement.GetBoolean();
        return passed
            ? ScenarioAssertionEvaluationResult.Pass()
            : ScenarioAssertionEvaluationResult.Fail(TextContainsFailureDetail(result));
    }

    private async Task<ScenarioAssertionEvaluationResult> EvaluateDrawTextNotContainsAsync(
        ScenarioAssertion assertion,
        CancellationToken cancellationToken)
    {
        if (assertion.Filter is null)
            return ScenarioAssertionEvaluationResult.Fail("draw.text_not_contains requires filter");

        var payload = new { filter = assertion.Filter, message = assertion.Message };
        var rpc = await _rpc.InvokeAsync(
            "draw.assert_text_not_contains",
            JsonSerializer.SerializeToElement(payload, ProtocolJson.Options),
            cancellationToken);
        if (!rpc.Succeeded)
            return ScenarioAssertionEvaluationResult.Fail(rpc.Error);
        if (rpc.Result is not { } result)
            return ScenarioAssertionEvaluationResult.Fail("draw.assert_text_not_contains returned no result");

        var passed = result.TryGetProperty("passed", out var passedElement)
            && passedElement.ValueKind is JsonValueKind.True or JsonValueKind.False
            && passedElement.GetBoolean();
        return passed
            ? ScenarioAssertionEvaluationResult.Pass()
            : ScenarioAssertionEvaluationResult.Fail(TextNotContainsFailureDetail(result));
    }
}
```

- [ ] **Step 2: Move state/content/result expression helpers**

Move these existing methods from `src/Runner/Scenarios/ScenarioRunner.cs` into `ScenarioAssertionEvaluator`:

- `EvaluateRpcResultAssertionAsync`
- `EvaluateResultExpression`
- `TryResolveResultPath`
- `EvaluateContentAssetAssertionAsync`
- `EvaluateAssetExpression`
- `TryResolveAssetPath`
- `TryReadJsonToken`
- `JsonElementEqualsLiteral`
- `TextContainsFailureDetail`
- `TextNotContainsFailureDetail`

While moving them:

- Change return types from `(bool Passed, string? Detail)` to `ScenarioAssertionEvaluationResult`.
- Replace `_session.InvokeAsync(...)` calls with `_rpc.InvokeAsync(...)`.
- Replace success returns like `(true, null)` with `ScenarioAssertionEvaluationResult.Pass()`.
- Replace failure returns like `(false, "message")` with `ScenarioAssertionEvaluationResult.Fail("message")`.
- Preserve expression grammar exactly:
  - `state.<method>.<path> == <literal>`
  - `state.<method>.<path> != <literal>`
  - `state.<method>.<array> contains [field] '<literal>'`
  - `state.<method>.<array> not contains [field] '<literal>'`
  - `asset.<path> ...`
  - `result.<path> ...`

- [ ] **Step 3: Add state assertion evaluation to the new evaluator**

In `ScenarioAssertionEvaluator`, implement `EvaluateStateAssertionAsync` by moving the current `case "state"` body from `ScenarioRunner.EvaluateAssertionAsync`.

Use this method signature:

```csharp
private async Task<ScenarioAssertionEvaluationResult> EvaluateStateAssertionAsync(
    ScenarioAssertion assertion,
    CancellationToken cancellationToken)
```

Required behavior:

- Empty `Expr` returns `ScenarioAssertionEvaluationResult.Fail("state assertion requires expr")`.
- Unsupported expression returns `ScenarioAssertionEvaluationResult.Fail($"unsupported state expression: {assertion.Expr}")`.
- RPC error returns `ScenarioAssertionEvaluationResult.Fail(rpc.Error)`.
- Missing path or bad index returns a detail containing the unresolved left-hand side.
- Existing successful and failed comparisons keep the same pass/fail semantics.

- [ ] **Step 4: Remove now-duplicated helper methods from ScenarioRunner**

In `src/Runner/Scenarios/ScenarioRunner.cs`, delete the moved helper methods listed in Step 2 and the old `case "state"` implementation from `EvaluateAssertionAsync`.

Keep these local in `ScenarioRunner`:

- `EvaluateTextAllWithinAsync`
- bitmap assertion code
- screenshot failure capture
- reporting/diff handling

- [ ] **Step 5: Run build and targeted CLI tests**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~ScenarioRunnerDslTests|FullyQualifiedName~ScenarioRunnerContentAssetTests|FullyQualifiedName~ScenarioRunnerFishingTests" -v minimal
```

Expected: compile may fail until Task 4 wires `ScenarioRunner` to the evaluator. If it compiles, these tests should still pass.

- [ ] **Step 6: Commit the evaluator extraction**

Commit only if the code compiles. If it cannot compile until the next wiring task, skip this commit and commit after Task 4.

```bash
git add src/Runner.Mcp/Scenarios/ScenarioAssertionEvaluator.cs src/Runner/Scenarios/ScenarioRunner.cs
git commit -m "feat: extract shared scenario assertion evaluator"
```

## Task 4: Wire ScenarioRunner To The Shared Evaluator

**Files:**
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`
- Modify: `tests/Runner.Tests/ScenarioRunnerDslTests.cs`

- [ ] **Step 1: Add the evaluator using**

At the top of `src/Runner/Scenarios/ScenarioRunner.cs`, add:

```csharp
using SdvTestFramework.Runner.Mcp.Scenarios;
```

- [ ] **Step 2: Delegate shared cases from EvaluateAssertionAsync**

In `ScenarioRunner.EvaluateAssertionAsync`, keep local cases for:

- `bitmap`
- `draw.text_all_within`

Replace the old local cases for:

- `draw.contains`
- `draw.not_contains`
- `draw.text_contains`
- `draw.text_not_contains`
- `content.asset`
- `state.fishing_context`
- `state.fishing_table`
- `fishing.sample_catch`
- `state`

with:

```csharp
            case "draw.contains":
            case "draw.not_contains":
            case "draw.text_contains":
            case "draw.text_not_contains":
            case "content.asset":
            case "state.fishing_context":
            case "state.fishing_table":
            case "fishing.sample_catch":
            case "state":
            {
                var evaluator = new ScenarioAssertionEvaluator(
                    new JsonRpcSessionScenarioAssertionRpc(_session));
                var result = await evaluator.EvaluateAsync(a, ct);
                if (!result.Passed)
                    await TryCaptureAssertionFailureAsync(ct);
                return (result.Passed, result.Detail);
            }
```

- [ ] **Step 3: Add a CLI regression for clearer failure details**

In `tests/Runner.Tests/ScenarioRunnerDslTests.cs`, add:

```csharp
    [Fact]
    public async Task StateAssertion_FailingComparison_ReportsExpressionDetail()
    {
        var socket = SocketPath();
        var (cts, server, client) = await StartFakeHarnessWithPlayerJson(socket,
            "{\"name\":\"Tester\",\"money\":499,\"stamina\":0,\"max_stamina\":0,\"health\":0,\"location\":\"Farm\",\"tile\":{\"x\":0,\"y\":0}}");
        using var _ = cts;
        using var __ = client;

        var runner = new ScenarioRunner(client);
        var spec = new ScenarioSpec
        {
            Name = "state_failure_detail",
            Assertions = new()
            {
                new ScenarioAssertion
                {
                    Type = "state",
                    Expr = "state.player.money == 500",
                    Message = "money seeded",
                },
            },
        };

        var report = await runner.RunAsync(spec, cts.Token);

        Assert.False(report.Passed);
        Assert.Contains(report.Failures, failure =>
            failure.Contains("money seeded", StringComparison.Ordinal)
            && failure.Contains("state.player.money", StringComparison.Ordinal));
        cts.Cancel();
        try { await server; } catch (OperationCanceledException) { }
    }
```

- [ ] **Step 4: Run CLI assertion tests**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~ScenarioRunnerDslTests|FullyQualifiedName~ScenarioRunnerContentAssetTests|FullyQualifiedName~ScenarioRunnerFishingTests" -v minimal
```

Expected: all selected tests pass.

- [ ] **Step 5: Commit ScenarioRunner wiring**

```bash
git add src/Runner/Scenarios/ScenarioRunner.cs src/Runner/Scenarios/JsonRpcSessionScenarioAssertionRpc.cs tests/Runner.Tests/ScenarioRunnerDslTests.cs
git commit -m "feat: share scenario assertion evaluation with CLI runner"
```

## Task 5: Wire RunScenarioTool To The Shared Evaluator

**Files:**
- Modify: `src/Runner.Mcp/Tools/RunScenarioTool.cs`
- Modify: `tests/Runner.Mcp.Tests/Tools/RunScenarioDiffFormatTests.cs`

- [ ] **Step 1: Add the evaluator using**

At the top of `src/Runner.Mcp/Tools/RunScenarioTool.cs`, add:

```csharp
using SdvTestFramework.Runner.Mcp.Scenarios;
```

- [ ] **Step 2: Replace the MCP assertion loop**

In `RunScenarioTool.InvokeAsync`, replace the current assertion loop:

```csharp
            // 4. assertions (minimal — MCP run_scenario supports draw.contains only)
            foreach (var assertion in spec.Assertions)
            {
                run++;
                if (assertion.Type == "draw.contains" && assertion.Filter is { } fx)
                {
                    try
                    {
                        var resp = await life.InvokeAsync("draw.assert_contains",
                            JsonSerializer.SerializeToElement(new { filter = fx, min_count = assertion.MinCount }, ProtocolJson.Options), ct);
                        if (resp.TryGetProperty("passed", out var pel) && pel.GetBoolean()) passed++;
                        else failures.Add($"draw.contains: {assertion.Message ?? "failed"}");
                    }
                    catch (SdvRpcException ex) { failures.Add($"draw.contains: {ex.Message}"); }
                }
                else
                {
                    failures.Add($"assertion type '{assertion.Type}' not evaluated by MCP run_scenario — use the CLI 'sdv-test run' for full DSL support.");
                }
            }
```

with:

```csharp
            // 4. assertions
            var evaluator = new ScenarioAssertionEvaluator(new LifecycleScenarioAssertionRpc(life));
            var assertionIndex = 1;
            foreach (var assertion in spec.Assertions)
            {
                run++;
                var evaluation = await evaluator.EvaluateAsync(assertion, ct);
                if (evaluation.Passed)
                {
                    passed++;
                }
                else
                {
                    failures.Add(FormatAssertionFailure(assertionIndex, assertion, evaluation.Detail));
                }

                assertionIndex++;
            }
```

- [ ] **Step 3: Add a private failure formatter**

In `RunScenarioTool`, add this private method before the closing class brace:

```csharp
    private static string FormatAssertionFailure(int index, ScenarioAssertion assertion, string? detail)
    {
        var label = string.IsNullOrWhiteSpace(assertion.Message)
            ? string.Empty
            : $"{assertion.Message}: ";
        var fallback = string.IsNullOrWhiteSpace(detail) ? "failed" : detail;
        return $"assertion {index} {assertion.Type}: {label}{fallback}";
    }
```

- [ ] **Step 4: Remove stale comments and unused usings**

In `RunScenarioTool`:

- Remove the XML doc remark saying richer state DSL is not evaluated.
- Keep the `diff_format` comment, but change it to:

```csharp
        // diff_format is accepted for CLI parity. MCP run_scenario still does not evaluate
        // bitmap assertions, so the value only matters when a scenario is promoted to CLI.
```

- Remove unused `SdvTestFramework.Protocol.Json` if no code in the file uses it after the loop replacement.

- [ ] **Step 5: Update the diff_format test comment**

In `tests/Runner.Mcp.Tests/Tools/RunScenarioDiffFormatTests.cs`, replace the old comment:

```csharp
        // The MCP tool's run_scenario doesn't currently evaluate bitmap assertions
        // (it delegates to the CLI runner). The minimum contract this test enforces:
        // passing a diff_format arg doesn't error — schema accepts it, tool routes it.
```

with:

```csharp
        // MCP run_scenario accepts diff_format for CLI parity even though bitmap assertions
        // remain CLI-only. The minimum contract: the arg is schema-valid and non-fatal.
```

- [ ] **Step 6: Run the MCP parity tests**

Run:

```bash
dotnet test tests/Runner.Mcp.Tests/Runner.Mcp.Tests.csproj --filter "FullyQualifiedName~RunScenarioAssertionParityTests|FullyQualifiedName~RunScenarioDiffFormatTests|FullyQualifiedName~StatefulToolsTests" -v minimal
```

Expected: all selected tests pass.

- [ ] **Step 7: Commit MCP wiring**

```bash
git add src/Runner.Mcp/Tools/RunScenarioTool.cs tests/Runner.Mcp.Tests/Tools/RunScenarioDiffFormatTests.cs
git commit -m "feat: evaluate shared assertions in MCP run_scenario"
```

## Task 6: Complete Docs And Roadmap Updates

**Files:**
- Modify: `docs/mcp-quickstart.md`
- Modify: `docs/roadmap.md`

- [ ] **Step 1: Update MCP quickstart support text**

In `docs/mcp-quickstart.md`, replace:

```markdown
`run_scenario` is intentionally lighter than the CLI runner: it is useful for quick
agent probes, but full scenario evaluation, rich assertions, bitmap forensics, and
complete static HTML reports should be run through `sdv-test run` or
`sdv-test run-suite`.
```

with:

```markdown
`run_scenario` evaluates the same non-bitmap assertion families used by normal
agent-authored scenarios: `state`, `content.asset`, selected direct RPC-result
assertions like `state.fishing_table`, and draw/text draw assertions. Bitmap
assertions, baseline updates, diff-image forensics, and complete static HTML
reports remain CLI-only; use `sdv-test run` or `sdv-test run-suite` for those.
```

- [ ] **Step 2: Move the roadmap item to Completed**

In `docs/roadmap.md`, remove this Tier 3 item:

```markdown
- [ ] **Full DSL assertion eval in MCP `run_scenario`** (~1 day) — today handles
  steps + `draw.contains` assertions; delegates state-DSL to the CLI runner. Extend to
  evaluate `state` assertions (reuse `ScenarioRunner`'s logic or refactor into a shared
  evaluator). Source: M3-MCP out-of-scope list.
```

Add this entry at the top of `## Completed`:

```markdown
### 2026-05-16

- **MCP `run_scenario` assertion parity.** Shared non-bitmap assertion evaluation
  between CLI `ScenarioRunner` and MCP `run_scenario`, covering `state`,
  `content.asset`, selected direct RPC-result assertions, and draw/text draw
  assertions. Bitmap baselines and full HTML report generation remain CLI-only.
```

If `## Completed` already begins with a `### 2026-05-16` heading by implementation time,
merge this bullet under that heading instead of adding a duplicate heading.

- [ ] **Step 3: Run docs consistency checks**

Run:

```bash
rg -n "not evaluated by MCP run_scenario|lacks state|full scenario evaluation, rich assertions" docs/mcp-quickstart.md docs/roadmap.md src/Runner.Mcp/Tools/RunScenarioTool.cs
```

Expected: no matches.

Run:

```bash
rg -n "bitmap assertions|CLI-only|run_scenario" docs/mcp-quickstart.md docs/roadmap.md
```

Expected: matches show the new support and the remaining CLI-only bitmap/report boundary.

- [ ] **Step 4: Commit docs**

```bash
git add docs/mcp-quickstart.md docs/roadmap.md
git commit -m "docs: document MCP scenario assertion parity"
```

## Task 7: Final Verification

**Files:**
- No planned code edits.

- [ ] **Step 1: Run targeted MCP tests**

```bash
dotnet test tests/Runner.Mcp.Tests/Runner.Mcp.Tests.csproj --filter "FullyQualifiedName~RunScenario" -v minimal
```

Expected: all selected tests pass.

- [ ] **Step 2: Run targeted CLI runner tests**

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~ScenarioRunnerDslTests|FullyQualifiedName~ScenarioRunnerContentAssetTests|FullyQualifiedName~ScenarioRunnerFishingTests" -v minimal
```

Expected: all selected tests pass.

- [ ] **Step 3: Run full MCP tests**

```bash
dotnet test tests/Runner.Mcp.Tests/Runner.Mcp.Tests.csproj -v minimal
```

Expected: all tests pass except the existing skipped live SDV integration test.

- [ ] **Step 4: Run full runner tests**

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj -v minimal
```

Expected: all tests pass.

- [ ] **Step 5: Run solution build**

```bash
dotnet build sdv-test-framework.slnx
```

Expected: build passes with warnings as errors.

- [ ] **Step 6: Check git diff cleanliness**

```bash
git diff --check HEAD
git status --short --branch
```

Expected: `git diff --check` exits 0. Status shows only committed changes or a clean branch.

- [ ] **Step 7: Final review**

Review the final diff against the design spec:

```bash
git log --oneline --decorate -8
git diff --stat HEAD~4..HEAD
git diff HEAD~4..HEAD -- src/Runner.Mcp src/Runner tests/Runner.Mcp.Tests tests/Runner.Tests docs/mcp-quickstart.md docs/roadmap.md
```

Expected:

- no bitmap support added to MCP
- no new dependency cycle
- no duplicated state/content expression parser remains in `ScenarioRunner`
- docs accurately describe the new MCP support

Commit any review fixes with a focused message, then rerun the affected tests.

## Self-Review

Spec coverage:

- MCP evaluates `state`: Task 1 tests, Task 3 evaluator, Task 5 wiring.
- MCP evaluates `content.asset`: Task 1 tests, Task 3 evaluator, Task 5 wiring.
- MCP evaluates `state.fishing_context`, `state.fishing_table`, `fishing.sample_catch`: Task 1 covers `state.fishing_table`; Task 3 includes all three in the evaluator switch.
- MCP evaluates basic draw assertion set: Task 3 evaluator supports draw contains/not and text contains/not; Task 5 wires it.
- Shared parser avoids duplication: Task 3 moves parser helpers; Task 4 deletes local duplicate logic.
- CLI behavior remains covered: Task 4 adds regression and runs existing CLI assertion tests.
- Docs and roadmap update: Task 6.
- Bitmap/report non-goals preserved: Task 3 leaves bitmap and text-all-within local; Task 6 documents CLI-only boundary.

Placeholder scan:

- No open-ended implementation notes remain. Every task names files, concrete code blocks, commands, and expected results.

Type consistency:

- Shared namespace is `SdvTestFramework.Runner.Mcp.Scenarios` throughout.
- Adapter type names are consistent with the design spec.
- `ScenarioAssertionEvaluationResult` uses `Passed` and `Detail`; all task snippets use those names.
