# MCP Run Scenario Progress Notifications Design

## Summary

`run_scenario` is now capable enough for real scenario iteration through MCP, but it
still returns only one final result. Long mod scenarios leave the agent blind until
the tool call completes, which makes slow failures harder to diagnose and encourages
shorter, less representative tests.

This slice adds protocol-native MCP progress notifications for the existing
`run_scenario` tool. When the client includes a request `_meta.progressToken`, the
server will emit `notifications/progress` updates as the scenario advances through
setup, fixture loading, steps, assertions, and cleanup. The final tool result remains
backward compatible.

## Protocol Reference

MCP progress notifications are the right fit for this work:

- A caller requests progress by sending `_meta.progressToken` with the request.
- The receiver may emit `notifications/progress` notifications while the request is
  active.
- Progress notifications include the original token, a monotonically increasing
  `progress` value, optional `total`, and optional human-readable `message`.
- Notifications must stop after the request completes.

References:

- https://modelcontextprotocol.io/specification/2024-11-05/basic/utilities/progress
- https://modelcontextprotocol.io/specification/2025-11-25/schema

## Goals

- Add progress notifications to `run_scenario` without changing its final JSON result.
- Keep notifications optional: no `_meta.progressToken` means no progress output.
- Emit useful, stable messages at scenario milestones:
  - scenario begin
  - fixture load when present
  - each step
  - each assertion
  - scenario end
- Include a deterministic `total` when the scenario is loaded successfully.
- Keep progress values monotonically increasing for one active request.
- Preserve scenario failure semantics: failed scenarios still return a successful MCP
  tool envelope with `passed: false`.
- Add tests around notification shape, ordering, no-token behavior, and final-result
  compatibility.

## Non-Goals

- A separate `run_scenario_stream` tool.
- A task API with start/poll/cancel semantics.
- Streaming full screenshots, bitmap diffs, HTML fragments, or large structured
  scenario reports through progress messages.
- Changing `run_scenario` assertion behavior.
- Adding concurrent tool execution. The current stdio server processes one request at
  a time.

## Current Shape

`McpServer.DispatchToolCallAsync` parses `tools/call`, finds a tool, awaits
`tool.InvokeAsync`, then writes one JSON-RPC response to stdout.

`RunScenarioTool` currently owns scenario execution:

1. validate arguments
2. create a report directory
3. load the scenario
4. call `scenario.begin`
5. optionally call `fixture.load`
6. execute scenario steps
7. evaluate assertions
8. best-effort `scenario.end`
9. return the final JSON summary

The `ITool` interface only accepts `args`, `SdvLifecycle`, and `CancellationToken`.
Tools cannot currently write MCP notifications.

## Proposed Architecture

Add a small progress abstraction that the MCP server can pass into tools.

Suggested components:

- `src/Runner.Mcp/McpProgressReporter.cs`
  - Holds an optional progress token and a notification writer callback.
  - Exposes `ReportAsync(int progress, int? total, string message, CancellationToken ct)`.
  - No-ops when no token is present.
  - Serializes `notifications/progress` JSON-RPC notifications.

- `src/Runner.Mcp/ToolInvocationContext.cs`
  - Holds `SdvLifecycle?` and `McpProgressReporter`.
  - Lets future tool-level context grow without repeatedly changing `ITool`.

- `src/Runner.Mcp/ITool.cs`
  - Add a context-aware invocation path.
  - Update all tool implementations in one pass. This is small enough in the current
    tool surface and avoids carrying two invocation contracts.

- `src/Runner.Mcp/McpServer.cs`
  - Extract `_meta.progressToken` from `tools/call.params`.
  - Create a per-request `McpProgressReporter`.
  - Pass the context into the tool.
  - Ensure notifications are written through the same `NdJsonWriter` as responses so
    stdout message framing stays valid.

This keeps the protocol-specific write concern in `Runner.Mcp`, while `RunScenarioTool`
only asks for progress updates through a narrow abstraction.

## Progress Token Handling

For a `tools/call` request, MCP metadata is expected at:

```json
{
  "jsonrpc": "2.0",
  "id": 10,
  "method": "tools/call",
  "params": {
    "name": "run_scenario",
    "arguments": { "path": "tests/sdv/01.test.json" },
    "_meta": { "progressToken": "scenario-01" }
  }
}
```

The implementation should accept string or integer tokens. Other token shapes should
be ignored rather than failing the tool call, because progress is optional and should
not make otherwise valid tool requests brittle.

Notifications should use the same token value and write JSON-RPC notifications with no
`id`:

```json
{
  "jsonrpc": "2.0",
  "method": "notifications/progress",
  "params": {
    "progressToken": "scenario-01",
    "progress": 3,
    "total": 12,
    "message": "step 2/8: player.warp"
  }
}
```

## Run Scenario Progress Model

After the scenario file loads, compute total units as:

- `1` for `scenario.begin`
- `1` for `fixture.load` when `spec.Fixture` is present
- `spec.Steps.Count`
- `spec.Assertions.Count`
- `1` for `scenario.end`

Progress starts at `0` conceptually but does not need an initial notification. Emit
after each completed unit. For example:

1. `scenario.begin`
2. `fixture.load`
3. `step 1/8: player.warp`
4. `step 2/8: freeze.begin`
5. `assertion 1/3: draw.text_contains`
6. `scenario.end`

If scenario loading fails before `total` is known, the tool should return its current
error result without progress notifications. If a step fails, emit progress for the
failed step with a failure message before breaking to cleanup. Then emit
`scenario.end` if cleanup succeeds.

Progress values should be numeric and monotonically increasing. Integer units are
sufficient for this slice.

## Message Style

Messages should be stable and concise because agents may key off them:

- `scenario.begin`
- `fixture.load: <fixture>`
- `step <index>/<totalSteps>: <action>`
- `step <index>/<totalSteps> failed: <action>`
- `assertion <index>/<totalAssertions>: <type>`
- `assertion <index>/<totalAssertions> failed: <type>`
- `scenario.end`

Do not include large serialized arguments in progress messages. The final result and
run reports remain the detailed diagnostic surfaces.

## Error Handling

- Progress notification write failures should propagate like response write failures.
  If stdout cannot accept a notification, the server transport is already unhealthy,
  and attempting to hide that would make final response reliability misleading.
- Scenario failures should remain scenario failures, not MCP transport errors.
- If the client supplies no valid progress token, all reporter calls should no-op.
- `scenario.end` stays best-effort. If cleanup throws, do not emit a misleading
  success message for cleanup.

## Testing

Add MCP server-level tests because progress is a transport behavior:

- `tools/call` with `_meta.progressToken` and a small `run_scenario` emits
  `notifications/progress` lines before the final response.
- Notification progress values increase and include the same token.
- Notification `total` equals begin + optional fixture + steps + assertions + end.
- Final response remains the normal MCP tool result envelope.
- No `_meta.progressToken` produces no progress notifications.
- Step failure emits a failed step progress message and still returns `passed: false`.

Use a fake lifecycle, temp scenario files, and in-memory stdin/stdout streams so tests
stay fast and do not launch Stardew Valley.

## Documentation Updates

Update:

- `docs/mcp-quickstart.md`
  - document that `run_scenario` supports MCP progress notifications when clients send
    `_meta.progressToken`
  - clarify that progress is optional status and final results stay unchanged
- `docs/roadmap.md`
  - move the Tier 3 progress item to Completed once implementation and tests pass

## Implementation Decisions

- Use integer progress units internally. JSON numbers can represent them directly,
  and scenario work is naturally counted in whole milestones.
- Use `scenario.end` as the final progress notification. When cleanup succeeds it
  naturally reaches `progress == total`; no separate "complete" notification is needed.
