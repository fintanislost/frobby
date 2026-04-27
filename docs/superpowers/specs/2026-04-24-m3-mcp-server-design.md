# M3 — MCP Server Design

**Milestone:** M3 subproject 2 (per spec §7 Phase 3 + §8 "MCP server scope" open question)
**Date:** 2026-04-24
**Author:** fintan + Claude (brainstorming session, auto-mode)
**Status:** Approved — ready for implementation-plan drafting

## Goal

Ship a stdio MCP server that lets an LLM (Claude Code, future Anthropic API integrations) author and run sdv-test scenarios via typed tool calls. Delivered as a new `sdv-test mcp` subcommand that speaks Anthropic's Model Context Protocol over stdin/stdout.

Users configure Claude Code via `.mcp.json`:
```json
{
  "mcpServers": {
    "sdv-test": {
      "command": "sdv-test",
      "args": ["mcp"]
    }
  }
}
```

LLMs get a curated tool surface optimized for test-authoring workflows (run, scaffold, assert) plus a raw RPC passthrough escape hatch. Self-launching SDV lifecycle — one subprocess per MCP session, lazy-launched on first tool call, torn down on stdio close.

## Architecture

**New project `src/Runner.Mcp/` (net10)** — references `src/Protocol/` (transport + DTOs) and `src/Runner/` (reuses `SdvLauncher` + `HarnessDeployer`, same pattern as `src/Runner.Dsl/`).

**Protocol mechanics**: MCP is JSON-RPC 2.0 over stdio with NDJSON framing — exactly what `src/Protocol/` already handles for the harness socket. Reuse `NdjsonCodec` + `JsonRpcRequest`/`JsonRpcResponse`/`JsonRpcError` types. The MCP server is a different JSON-RPC endpoint, not a different transport.

**Session lifecycle**:
1. Server starts, reads stdin, writes stdout. stderr for logs.
2. Client sends `initialize` → server responds with server info + `tools` capability.
3. Client sends `notifications/initialized` → no response needed.
4. Client sends `tools/list` → server returns the 7 tool definitions.
5. Client sends `tools/call` for any tool → server dispatches. Tools that need SDV trigger lazy launch via `SdvLifecycle.EnsureRunningAsync()`.
6. stdin EOF → server shuts down SDV (if running), exits 0.

**Lazy SDV launch**: the `SdvLifecycle` singleton holds the SDV subprocess + `JsonRpcSession` handle. First tool call that needs it triggers launch; subsequent calls reuse. Teardown on stdio close. Tools that don't need SDV (`list_scenarios`, `list_fixtures`, `scaffold_scenario`) never trigger launch.

**Tool surface (7 tools)**:

*Curated (6):*
- `run_scenario(path)` — load + execute a `.test.json`, return `{passed, assertions_run, assertions_passed, failures, duration_ms}`.
- `list_scenarios(dir?)` — enumerate `*.test.json` in `dir` (default cwd), return `[{path, name, fixture?}]`.
- `list_fixtures()` — enumerate `tests/fixtures/*/`, return `[{name, sdv_version, description}]` from each fixture's `.meta.json`.
- `warp_and_assert_draw(location, x, y, texture_asset, min_count?)` — atomic: `player.warp` → `draw.arm` → `wait` → `freeze.begin` → `draw.assert_contains` → `freeze.end`. Returns `{passed, matched, message?}`.
- `capture_state()` — parallel reads of `state.player` + `state.location` + `state.time` + `state.menu`, return flattened `{player, location, time, menu}`.
- `scaffold_scenario(name, fixture?, template?)` — generate a starter `.test.json` at `tests/samples/<name>.test.json` with the minimum shape + optional template steps (`"shop"`, `"menu"`, `"warp"`). Returns the written path. Pure file operation — no SDV needed.

*Passthrough (1):*
- `rpc_call(method, params?)` — send any JSON-RPC method to the harness. Returns the raw result (JSON) or raises a tool error with the RPC error details. Catch-all for workflows the curated tools don't cover.

**CLI entry**: `src/Runner/Commands/McpCommand.cs` is a thin dispatch that instantiates `McpServer` from `src/Runner.Mcp/` and calls `.RunAsync(stdin, stdout, cancellationToken)`. Program.cs adds `"mcp" => McpCommand.RunAsync(...)` to its switch.

## Components

**New files (`src/Runner.Mcp/`):**
- `Runner.Mcp.csproj` — net10, references Protocol + Runner.
- `McpServer.cs` — main loop: parse line → dispatch → serialize + write.
- `McpCapabilities.cs` — static class with the `initialize` response shape + tool schemas.
- `ITool.cs` — `string Name`, `string Description`, `JsonElement InputSchema`, `Task<JsonElement> InvokeAsync(JsonElement args, CancellationToken)`.
- `ToolRegistry.cs` — map<string, ITool>; `Get(name) → ITool?`; `List() → ITool[]`.
- `SdvLifecycle.cs` — singleton-ish; holds the `JsonRpcSession` after lazy launch; `EnsureRunningAsync()` returns the session (launching if needed); `ShutdownAsync()` kills the subprocess.
- `McpError.cs` / `McpException.cs` — typed error shapes matching MCP's `-32xxx` codes.
- `Tools/RunScenarioTool.cs`
- `Tools/ListScenariosTool.cs`
- `Tools/ListFixturesTool.cs`
- `Tools/WarpAndAssertDrawTool.cs`
- `Tools/CaptureStateTool.cs`
- `Tools/ScaffoldScenarioTool.cs`
- `Tools/RpcCallTool.cs`

**New files (`src/Runner/Commands/`):**
- `McpCommand.cs` — static `RunAsync(ReadOnlyMemory<string>, CancellationToken)` that constructs `McpServer` + pumps stdin/stdout.

**Modified files:**
- `src/Runner/Program.cs` — dispatch `"mcp"` + help text.
- `docs/milestones/current.md` — M3-MCP completion subsection.

**New tests (`tests/Runner.Mcp.Tests/`):**
- `Runner.Mcp.Tests.csproj`
- `McpServerTests.cs` — 3 tests:
  - `Initialize_ReturnsServerInfo_AndToolsCapability`
  - `ToolsList_ReturnsSevenTools`
  - `ToolsCall_UnknownTool_ReturnsMethodNotFound`
- `ToolRegistryTests.cs` — 2 tests: `Get_ExistingName_ReturnsTool`, `Get_UnknownName_ReturnsNull`.
- `Tools/IntrospectionToolTests.cs` — 2 tests: `ListScenarios_GlobsDirectory`, `ListFixtures_ReadsMetaJson`. (No SDV needed — pure filesystem ops.)
- `Tools/ScaffoldScenarioToolTests.cs` — 1 test: `Scaffold_WritesStarterJson_ReturnsPath`.
- `Tools/RpcCallToolTests.cs` — 2 tests: `Dispatch_ForwardsToSession`, `Error_MapsToMcpError`. (Shim `SdvLifecycle`.)
- `Tools/StatefulToolsTests.cs` — 2 tests via shim: `RunScenario_LoadsAndReturnsReport`, `WarpAndAssertDraw_ProducesAtomicSequence`.
- `McpIntegrationTests.cs` — 1 `[Fact(Skip=...)]` placeholder.

**Target test count:** 286+36 → ~298+37 (+12 passed, +1 skipped).

## MCP wire shapes

### Handshake — `initialize`

**Request:**
```json
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{
  "protocolVersion":"2024-11-05",
  "clientInfo":{"name":"claude-code","version":"..."},
  "capabilities":{}
}}
```

**Response:**
```json
{"jsonrpc":"2.0","id":1,"result":{
  "protocolVersion":"2024-11-05",
  "serverInfo":{"name":"sdv-test-mcp","version":"0.1.0"},
  "capabilities":{"tools":{}}
}}
```

The server advertises `tools` capability only. No `resources`, `prompts`, `logging` in MVP.

### `tools/list`

**Request:** `{"jsonrpc":"2.0","id":2,"method":"tools/list"}`

**Response:**
```json
{"jsonrpc":"2.0","id":2,"result":{"tools":[
  {
    "name":"run_scenario",
    "description":"Execute a .test.json scenario against a live SDV instance. Returns pass/fail + failure details.",
    "inputSchema":{
      "type":"object",
      "properties":{"path":{"type":"string","description":"Absolute or workspace-relative path to the .test.json"}},
      "required":["path"]
    }
  },
  ...
]}}
```

### `tools/call`

**Request:**
```json
{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{
  "name":"run_scenario",
  "arguments":{"path":"tests/samples/03-shop-sprite.test.json"}
}}
```

**Response (success):**
```json
{"jsonrpc":"2.0","id":3,"result":{"content":[{
  "type":"text",
  "text":"{\"passed\":true,\"assertions_run\":3,\"assertions_passed\":3,\"duration_ms\":142}"
}]}}
```

Every tool response is wrapped as `{content: [{type: "text", text: "..."}]}` per MCP convention. The `text` is the tool's JSON result serialized as a string. LLMs can parse it back.

**Response (tool-level error — valid response shape with `isError: true`):**
```json
{"jsonrpc":"2.0","id":3,"result":{"content":[{
  "type":"text",
  "text":"scenario file not found: tests/samples/bad.test.json"
}],"isError":true}}
```

**Response (JSON-RPC error — protocol-level failure):**
```json
{"jsonrpc":"2.0","id":3,"error":{"code":-32602,"message":"invalid arguments for run_scenario: 'path' is required"}}
```

## Tool details

### `run_scenario`

Params: `{path: string}`. Loads via `ScenarioLoader.Load`, drives through a freshly-assembled `ScenarioRunner` bound to the ambient `SdvLifecycle` session. Returns `{passed, assertions_run, assertions_passed, failures: string[], duration_ms}` serialized as JSON string inside the `text` content.

### `list_scenarios`

Params: `{dir?: string}` (default: cwd). Enumerate `*.test.json` recursively, parse just the `name` + `fixture` fields (skip full validation for speed). Return `[{path, name, fixture?}]`.

### `list_fixtures`

Params: none. Enumerate `tests/fixtures/*/.meta.json`. Return `[{name, sdv_version?, description?}]`. No SDV needed.

### `warp_and_assert_draw`

Params: `{location, x, y, texture_asset, min_count?}`. Executes the atomic sequence:
1. `player.warp`
2. `draw.arm`
3. client-side 500ms wait
4. `freeze.begin`
5. `draw.assert_contains` with `{texture_asset, min_count ?? 1}`
6. `freeze.end` (finally block — even on step-5 failure)

Returns `{passed, matched, message?}` — the assertion result. Typical use case: an LLM wants to probe "does Pierre's shop UI contain the shop-menu texture?"

### `capture_state`

Params: none (optionally `{location?}` to target a specific location). Parallel reads of the 4 state endpoints. Returns flat object `{player: PlayerState, location: LocationState, time: TimeState, menu: MenuState}`.

### `scaffold_scenario`

Params: `{name: string, fixture?: string, template?: "shop" | "menu" | "warp"}`. Writes a starter `.test.json` at `tests/samples/<name>.test.json`. Templates:
- `shop`: warp to SeedShop, interact with Pierre, wait for menu, assert draw.
- `menu`: fixture load, wait, draw-arm + draw.assert_contains on a menu texture.
- `warp`: minimal warp-only scenario (for exploring).
- (no template): skeleton with `config.seed = 42`, empty steps + assertions.

Returns `{path: "tests/samples/<name>.test.json"}`. No SDV needed. Pure file op.

### `rpc_call`

Params: `{method: string, params?: object}`. Forwards to the ambient session's `InvokeAsync(method, params, ct)`. On success: returns the raw JSON result. On RPC error: returns `isError: true` content with the error message + code.

This is the escape hatch. Everything else the CLI can do, `rpc_call` can do.

## Error handling

- **Protocol-level errors** (malformed JSON, unknown method) → JSON-RPC error response (`-32600 Invalid Request`, `-32601 Method not found`, `-32602 Invalid params`, `-32603 Internal error`). MCP uses the same codes.
- **Tool-argument validation** (missing required field, wrong type) → JSON-RPC `-32602 Invalid params` with the specific field.
- **Tool execution errors** (RPC call failed, file not found) → MCP-convention `{content, isError: true}` — not a JSON-RPC error. Lets the LLM see the failure reason in the tool result stream instead of a hard protocol-level reject.
- **SDV launch failure** → `SdvLifecycle.EnsureRunningAsync()` throws; surfaces as `-32603 Internal error` with the launch failure message.
- **Stdin EOF** → clean shutdown: kill SDV if running, exit 0. No error.
- **SIGTERM** (background shutdown) → same as EOF. Relies on the SIGTERM handler that landed in M3 subproject 0.

## Testing

**Unit tests (~12 passing):**

- `McpServerTests`:
  - `Initialize_ReturnsServerInfo_AndToolsCapability` — send initialize, verify response shape.
  - `ToolsList_ReturnsSevenTools` — send tools/list, verify tool count + names.
  - `ToolsCall_UnknownTool_ReturnsMethodNotFound` — call nonexistent tool, verify `-32601`.
- `ToolRegistryTests` — 2 tests.
- `IntrospectionToolTests` — `list_scenarios` globs a temp dir with 2 synthetic `.test.json` files; `list_fixtures` reads a temp fixtures dir with 1 synthetic `.meta.json`.
- `ScaffoldScenarioToolTests` — call with `name: "x"`, verify file created at `tests/samples/x.test.json` with valid JSON.
- `RpcCallToolTests` — shim `SdvLifecycle` returns canned RPC results; verify forwarding + error mapping.
- `StatefulToolsTests` — 2 tests via shim: one for `run_scenario` (shimmed session + loader), one for `warp_and_assert_draw` (shimmed session captures the 6-call sequence).

**Skipped integration (1):**
- `McpIntegrationTests.EndToEnd_LaunchesSdvAndRunsOneScenario` — full stdio round-trip against a live SDV. Covered manually via a worked bash script (below).

**Worked smoke (manual, committed but not in CI):**
- `tests/Runner.Mcp.Tests/Worked/manual-smoke.sh` — bash script that pipes a series of JSON-RPC requests into `dotnet run -- mcp` and asserts the responses. Runnable by a dev with live SDV + Xvfb. Tests the full handshake → tools/list → run_scenario flow.

## Acceptance criteria

1. `./scripts/ci.sh` green at ~298 Passed + 37 Skipped.
2. New `src/Runner.Mcp/` + `tests/Runner.Mcp.Tests/` projects build clean under `TreatWarningsAsErrors=true`.
3. `sdv-test mcp` subcommand launches without error and responds to `initialize` over stdio (manually verifiable via `echo '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}' | sdv-test mcp`).
4. `tools/list` response includes all 7 tools with complete input schemas.
5. `rpc_call` passthrough can invoke any harness RPC (verified manually via the worked smoke).
6. `scaffold_scenario` produces a `.test.json` that `ScenarioLoader.Load` accepts (unit-verified).
7. `docs/mcp-quickstart.md` exists describing the `.mcp.json` setup + tool surface.
8. `docs/milestones/current.md` gains an M3-MCP subsection.
9. Sample suite (`./scripts/run-samples.sh`) still 11/11 PASS (no regression).

## Out of scope (TODO for M4)

- **HTTP transport** — stdio only for MVP. Cloud-hosted MCP lands when we have users who aren't running Claude Code locally.
- **MCP resources** — exposing test results, fixtures, scenario files as MCP `resources` instead of / in addition to tools. Arguably better UX for LLM consumption but double the implementation work.
- **MCP prompts** — pre-built scenario-scaffolding prompts as MCP `prompts`. Nice polish, skipped for MVP.
- **Streaming tool results** — MCP supports streaming responses for long-running tools. `run_scenario` would benefit (streaming per-step progress), but synchronous JSON responses are the easier MVP.
- **Tool-level auth / per-user session isolation** — the MCP server is a per-user subprocess; multi-tenant considerations don't apply.
- **Multi-fixture parallelism** — one SDV per MCP session. Running multiple scenarios in parallel across parallel SDVs is a future optimization.
- **Richer `scaffold_scenario` templates** — 3-4 templates for MVP. LLMs can add more by just writing `.test.json` files directly after seeing one example.
- **`.mcp.json` generation** — user writes it themselves per Claude Code conventions. We document the snippet.

## Links

- Spec: `docs/spec.md` §7 Phase 3 + §8 ("MCP server scope")
- Brainstorm: 2026-04-24 auto-mode session (this doc)
- Prior M3: SIGTERM handler (subproject 0), C# fluent DSL (subproject 1)
- MCP reference: https://modelcontextprotocol.io/
