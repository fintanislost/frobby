# MCP Server Quickstart

The sdv-test MCP server exposes scenario-authoring + debugging tools to LLMs via
Anthropic's Model Context Protocol. Configure Claude Code to launch it, and your LLM
gets typed tools for running scenarios, capturing state, and scaffolding new tests.

## 1. Install

```bash
dotnet tool install -g SdvTestFramework.Cli
```

## 2. Configure Claude Code

Add to `.mcp.json` in your workspace:

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

## 3. Tool surface

Six curated helpers + one raw passthrough:

- **`run_scenario(path)`** — execute a `.test.json`, return pass/fail + failures.
- **`list_scenarios(dir?)`** — enumerate `*.test.json` files.
- **`list_fixtures()`** — enumerate `tests/fixtures/`.
- **`warp_and_assert_draw(location, x, y, texture_asset, min_count?)`** — atomic warp + freeze + draw assertion. Returns `{passed, matched}`.
- **`capture_state()`** — snapshot `{player, location, time, menu}`.
- **`scaffold_scenario(name, fixture?, template?)`** — write a starter `.test.json`. Templates: `shop`, `menu`, `warp`, `npc_interaction`, `shop_purchase`, `tool_use`, `inventory_check`, `furniture_menu`.
- **`rpc_call(method, params?)`** — raw JSON-RPC passthrough. Escape hatch for everything else.

The `furniture_menu` scaffold template places custom furniture, interacts with it,
arms draw capture, and includes starter assertions for the expected menu type and
visible text.

`run_scenario` evaluates scenario steps plus the shared non-bitmap RPC assertion set:
`state`, `content.asset`, `state.fishing_context`, `state.fishing_table`,
`fishing.sample_catch`, `draw.contains`, `draw.not_contains`, `draw.text_contains`,
and `draw.text_not_contains`. Bitmap assertions, `draw.text_all_within`, and
complete static HTML reports remain CLI-only via `sdv-test run` or
`sdv-test run-suite`.

MCP clients that support progress can send `_meta.progressToken` on `tools/call`
requests for `run_scenario`. Frobby then emits `notifications/progress` after
scenario setup, each step, each assertion, and cleanup. Progress notifications are
optional status updates; delivery failures are treated as MCP transport failures,
and successful final tool results keep the same JSON summary shape.

## 4. Environment knobs

- `SDV_MODS_PATH` — override the mods dir the harness is deployed to (default `~/.cache/sdv-test-framework/mods`).
- `SDV_EXTRA_MODS` — platform-path-separator-delimited list of built SMAPI mod folders to copy into the isolated mods dir before launch. Example on Linux: `SDV_EXTRA_MODS=/path/to/Example.Mod/bin/Release/net6.0`.
- For repo-local workflows, prefer `sdv-test repo init` and the generated
  `scripts/sdv-test` wrapper over hand-written mod-specific shell scripts. The
  generated wrapper keeps headless defaults, extra mod staging, report paths, and
  repeat runs consistent across projects.
- `SDV_TEST_HEADLESS=1` — launch SDV through `xvfb-run` on Linux so MCP-driven tests do not use the active desktop display or mouse cursor.
- The MCP server lazy-launches SDV on first tool call that needs it. Tools that don't need SDV (`list_*`, `scaffold_scenario`) never trigger launch.
- Stdio EOF tears down SDV cleanly.

## 5. What's deferred

See the M3-MCP design spec (`docs/superpowers/specs/2026-04-24-m3-mcp-server-design.md`) for M4 follow-ups: HTTP transport, MCP resources/prompts, streaming tool results, richer scaffold templates.
