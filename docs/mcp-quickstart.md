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
- **`scaffold_scenario(name, fixture?, template?)`** — write a starter `.test.json`. Templates: `shop`, `menu`, `warp`, `npc_interaction`, `shop_purchase`, `tool_use`, `inventory_check`, `starberg_terminal`.
- **`rpc_call(method, params?)`** — raw JSON-RPC passthrough. Escape hatch for everything else.

The `starberg_terminal` scaffold template arms draw capture, opens the terminal, and includes `draw.text_contains` assertions for visible terminal/cash text.

`run_scenario` is intentionally lighter than the CLI runner: it is useful for quick
agent probes, but full scenario evaluation, rich assertions, bitmap forensics, and
complete static HTML reports should be run through `sdv-test run` or
`sdv-test run-suite`.

## 4. Environment knobs

- `SDV_MODS_PATH` — override the mods dir the harness is deployed to (default `~/.cache/sdv-test-framework/mods`).
- `SDV_EXTRA_MODS` — platform-path-separator-delimited list of built SMAPI mod folders to copy into the isolated mods dir before launch. Example on Linux: `SDV_EXTRA_MODS=/home/fintan/stardewRepos/stonks/src/Starberg.Mod/bin/Release/net6.0`.
- `SDV_TEST_HEADLESS=1` — launch SDV through `xvfb-run` on Linux so MCP-driven tests do not use the active desktop display or mouse cursor.
- The MCP server lazy-launches SDV on first tool call that needs it. Tools that don't need SDV (`list_*`, `scaffold_scenario`) never trigger launch.
- Stdio EOF tears down SDV cleanly.

## 5. What's deferred

See the M3-MCP design spec (`docs/superpowers/specs/2026-04-24-m3-mcp-server-design.md`) for M4 follow-ups: HTTP transport, MCP resources/prompts, streaming tool results, richer scaffold templates.
