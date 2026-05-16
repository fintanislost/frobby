# MCP Run Scenario Assertion Parity Design

## Summary

`run_scenario` in the MCP server currently runs scenario setup and steps, but only
evaluates a narrow `draw.contains` assertion subset. Richer assertion support lives
in the CLI `ScenarioRunner`, so an agent using MCP can author a valid scenario that
passes under `sdv-test run` but fails under MCP with "assertion type not evaluated".

This slice closes the high-value part of that gap by sharing non-bitmap assertion
evaluation between the CLI runner and MCP `run_scenario`. The first target is
state-oriented scenario authoring: `state`, `content.asset`, and direct RPC-result
assertions should behave the same through MCP as they do through the CLI runner.

## Goals

- Make MCP `run_scenario` evaluate the assertion types agents commonly need while
  iterating on real mod scenarios:
  - `state`
  - `content.asset`
  - `state.fishing_context`
  - `state.fishing_table`
  - `fishing.sample_catch`
  - `draw.contains`
  - `draw.not_contains`
  - `draw.text_contains`
  - `draw.text_not_contains`
- Keep assertion expression behavior consistent between MCP and CLI.
- Avoid copying the state/content expression parser into a second implementation.
- Return useful failure details in MCP results, including assertion type and message
  where available.
- Update docs so agents no longer avoid MCP for state assertions.

## Non-Goals

- Bitmap assertions, baseline updates, diff images, and bitmap failure forensics.
  These stay CLI-only for this slice because they depend on Runner bitmap/reporting
  services.
- Full static HTML report generation from MCP. `run_scenario` may still return a
  report directory placeholder or summary, but `sdv-test run` remains the complete
  report path.
- Streaming MCP progress. This remains the next higher-value MCP follow-up after
  assertions are no longer silently unsupported.
- Moving `ScenarioLoader` back into Protocol. That is a separate roadmap cleanup.
- Adding new scenario assertion syntax. This slice only shares the current syntax.

## Current Shape

`src/Runner.Mcp/Tools/RunScenarioTool.cs`:

- loads `.test.json` through `ScenarioLoader`
- calls `scenario.begin`
- optionally calls `fixture.load`
- forwards ordinary steps directly through `SdvLifecycle.InvokeAsync`
- sleeps for `wait.ms`
- evaluates only `draw.contains`
- marks every other assertion unsupported

`src/Runner/Scenarios/ScenarioRunner.cs`:

- owns richer scenario execution
- evaluates draw, text draw, bitmap, content asset, state, and selected RPC-result
  assertions
- contains private helper methods for expression parsing and JSON path resolution

The key duplication risk is the expression evaluator. The implementation should
extract that behavior instead of recreating it in `RunScenarioTool`.

## Proposed Architecture

Add a shared non-bitmap assertion evaluator in the runner/MCP layer, then have both
callers use it.

The evaluator should be independent of `JsonRpcSession` and `SdvLifecycle`. It should
accept a small RPC abstraction, for example:

```csharp
internal interface IScenarioAssertionRpc
{
    Task<ScenarioAssertionRpcResult> InvokeAsync(
        string method,
        JsonElement? parameters,
        CancellationToken cancellationToken);
}
```

`ScenarioAssertionRpcResult` should carry either a `JsonElement` result or an error
message. This keeps CLI behavior, which receives `JsonRpcResponse` errors, and MCP
behavior, which currently sees `SdvRpcException`, behind one evaluator contract.

Suggested component layout:

- `src/Runner.Mcp/Scenarios/ScenarioAssertionEvaluator.cs`
  - evaluates the shared assertion subset
  - owns state/content/result expression parsing
  - returns `ScenarioAssertionEvaluationResult`
- `src/Runner.Mcp/Scenarios/ScenarioAssertionRpcResult.cs`
  - small success/error result object
- `src/Runner.Mcp/Scenarios/LifecycleScenarioAssertionRpc.cs`
  - adapts `SdvLifecycle`
- `src/Runner/Scenarios/JsonRpcSessionScenarioAssertionRpc.cs`
  - adapts `JsonRpcSession`

The location is intentionally pragmatic: `Runner` already references `Runner.Mcp`.
This avoids adding a new project or creating a dependency cycle. The namespace should
not imply the evaluator is MCP-only; use a neutral namespace such as
`SdvTestFramework.Runner.Mcp.Scenarios` for now and leave physical consolidation to
the existing ScenarioLoader roadmap item.

## Evaluator Behavior

The evaluator should support these assertion types:

1. `draw.contains`
   - calls `draw.assert_contains`
   - passes `filter`, `min_count`, and `message`
2. `draw.not_contains`
   - calls `draw.assert_not_contains`
   - passes `filter` and `message`
3. `draw.text_contains`
   - calls `draw.assert_text_contains`
   - passes `filter`, `min_count`, `max_count`, and `message`
   - preserves current failure detail behavior when the RPC returns diagnostics
4. `draw.text_not_contains`
   - calls `draw.assert_text_not_contains`
   - preserves current failure detail behavior when the RPC returns diagnostics
5. `content.asset`
   - calls `content.asset`
   - checks `exists`
   - evaluates optional `asset.*` expressions exactly as CLI does today
6. `state`
   - calls the state RPC implied by `state.<method>...`
   - passes `ScenarioAssertion.Params`
   - evaluates equality, inequality, indexed paths, and contains expressions as CLI
     does today
7. `state.fishing_context`, `state.fishing_table`, `fishing.sample_catch`
   - calls the assertion type as the RPC method
   - evaluates optional `result.*` expressions exactly as CLI does today

Unsupported assertions should return a failed evaluation with a clear detail:

`assertion type '<type>' is not supported by MCP run_scenario; use sdv-test run for bitmap/report-only assertions.`

For this slice, that mainly applies to `bitmap` and `draw.text_all_within`.

## MCP Run Flow

`RunScenarioTool` keeps its current top-level lifecycle:

1. load scenario
2. create report directory
3. call `scenario.begin`
4. optionally call `fixture.load`
5. execute steps
6. evaluate assertions
7. call `scenario.end` in best-effort cleanup
8. return JSON summary

Only step 6 changes materially. It should instantiate the shared evaluator with a
`LifecycleScenarioAssertionRpc`, then evaluate every assertion in order.

The returned MCP JSON should keep the existing shape:

```json
{
  "passed": true,
  "assertions_run": 2,
  "assertions_passed": 2,
  "failures": [],
  "duration_ms": 123,
  "report_dir": "...",
  "report_index": "..."
}
```

Failure strings should include enough context for an agent to fix the scenario:

- assertion index
- assertion type
- scenario-authored `message` when present
- evaluator detail or RPC error message

Example:

`assertion 2 state: player money should be seeded: state.player.money did not match 500`

## CLI Runner Integration

`ScenarioRunner` should delegate supported shared assertion cases to the new evaluator
instead of keeping a private duplicate parser. CLI-only assertion types stay local:

- `bitmap`
- `draw.text_all_within`
- any assertion that needs report-specific side effects not available in the shared
  evaluator

When a shared assertion fails under the CLI runner, `ScenarioRunner` should keep its
existing failure screenshot behavior by calling `TryCaptureAssertionFailureAsync`
after receiving a failed evaluation.

## Error Handling

- RPC errors should not crash scenario execution. They should become failed assertion
  details.
- Invalid assertion expressions should fail with a specific detail where possible,
  not a null/empty failure.
- Missing filters or required fields should fail with direct messages such as
  `draw.contains requires filter` or `content.asset requires asset`.
- `scenario.end` should remain best-effort and run even after a step or assertion
  failure.
- MCP should keep returning an MCP tool success envelope for scenario failures. The
  scenario result itself carries `passed: false`.

## Documentation Updates

Update:

- `docs/mcp-quickstart.md`
  - remove the statement that MCP `run_scenario` lacks state/rich assertions
  - document that bitmap assertions and full static reports remain CLI-only
- `docs/roadmap.md`
  - move "Full DSL assertion eval in MCP `run_scenario`" from Tier 3 to Completed
    after implementation and verification

No `docs/rpc-schema.md` change is required unless the MCP tool input or output schema
changes. The current tool still accepts `path`, `report_dir`, and `diff_format`.

## Test Plan

Use TDD.

Start with failing MCP tests:

1. `RunScenario_EvaluatesPassingStateAssertion`
   - scenario assertion: `state.player.money == 500`
   - fake lifecycle returns `{"money":500}` for `state.player`
   - result passes and counts the assertion
2. `RunScenario_ReturnsFailureForFailingStateAssertion`
   - fake lifecycle returns `{"money":499}`
   - result has `passed:false` and a useful failure string
3. `RunScenario_PassesStateAssertionParams`
   - scenario assertion uses `params`
   - fake lifecycle records the `state.npc` call params
4. `RunScenario_EvaluatesContentAssetAssertion`
   - fake lifecycle returns a content asset result with `exists:true`
   - optional `asset.*` expression passes
5. `RunScenario_EvaluatesRpcResultAssertion`
   - use `state.fishing_table` or `fishing.sample_catch`
   - fake lifecycle result satisfies a `result.*` expression
6. `ScenarioRunner_UsesSharedStateEvaluator`
   - CLI unit coverage should prove the shared evaluator still supports existing
     state assertion syntax

Targeted verification:

- `dotnet test tests/Runner.Mcp.Tests/Runner.Mcp.Tests.csproj --filter "FullyQualifiedName~RunScenario"`
- `dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~ScenarioRunner"`
- `dotnet test tests/Runner.Mcp.Tests/Runner.Mcp.Tests.csproj`
- `dotnet test tests/Runner.Tests/Runner.Tests.csproj`

Live SDV verification is not required for the first pass because this is evaluator
and tool wiring work. A later smoke can run a real Starberg or SVE state-heavy
scenario through MCP if needed.

## Acceptance Criteria

- MCP `run_scenario` evaluates `state` assertions with the same syntax as CLI.
- MCP `run_scenario` evaluates `content.asset` assertions with the same syntax as CLI.
- MCP `run_scenario` evaluates `state.fishing_context`, `state.fishing_table`, and
  `fishing.sample_catch` result expressions with the same syntax as CLI.
- MCP `run_scenario` evaluates the basic draw assertion set listed in this spec.
- Unsupported assertion types return explicit failure details instead of the old
  generic rich-assertion limitation.
- CLI state/content/result assertion tests still pass after extraction.
- MCP docs accurately describe the new support and remaining CLI-only paths.
- Roadmap marks the Tier 3 item complete only after tests pass.
