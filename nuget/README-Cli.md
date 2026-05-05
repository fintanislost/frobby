# SdvTestFramework.Cli — `sdv-test`

Stardew Valley test-framework CLI: launch SDV, run scenarios, scaffold templates,
build texture manifests, run as MCP server for Claude Code.

## Install

```bash
dotnet tool install -g SdvTestFramework.Cli
```

After install, `sdv-test --help` lists all subcommands:

- `sdv-test run <path>` — execute one scenario file or a directory of scenarios in one SDV process.
- `sdv-test run-suite <path>` — execute each scenario in a fresh SDV process.
- `sdv-test list <path>` — enumerate `*.test.json` files.
- `sdv-test record <name>` — RPC-trace recorder.
- `sdv-test fixture ...` — build/load fixture save state.
- `sdv-test baselines ...` — inspect, update, show, and delete bitmap baselines.
- `sdv-test cache clean` — clean cached captures.
- `sdv-test build-manifest` — generate texture-hash manifest.
- `sdv-test mcp` — run as MCP stdio server (for Claude Code).
- ... and more.

## Running mod scenarios

Use `--headless` on Linux unless you specifically need to watch the game:

```bash
sdv-test run --headless tests/sdv
sdv-test run-suite --headless tests/sdv
```

`--headless` launches SDV through `xvfb-run`, keeping the active desktop and mouse
cursor free while tests run. `SDV_TEST_HEADLESS=1` applies the same behavior to all
launcher callers.

Useful runner options:

- `--mods-path <path>` — use a specific isolated mods directory.
- `--extra-mod <path>` — copy a built mod folder into the isolated mods directory.
- `--report-dir <path>` — write the HTML report to a stable directory.
- `--no-report` — skip HTML/JSON report generation.
- `--filter <text>` — run only scenarios whose path or name matches.
- `--update-baselines` — refresh bitmap baselines from the current render.
- `--tier <generic|ci-ubuntu|self-hosted-nvidia>` — choose bitmap tolerance defaults.

Every reported run writes an `index.html` hub, `summary.json`, and per-scenario pages
with step timelines, assertions, screenshots, and failure forensics. Use
`screenshot.capture_next_frame` after click or hover actions so the report captures
the UI after the next rendered frame.

## Scenario authoring

Prefer click-first UI flows:

```json
{ "action": "ui.click_text", "args": { "text_equals": "BUY" } }
{ "action": "ui.hover_text", "args": { "text_matches": "^CASH [0-9,]+g$" } }
{ "action": "screenshot.capture_next_frame", "args": { "name": "after_buy" } }
```

Use `draw.text_all_within` for text overflow guardrails in fixed UI bodies, tables,
button rows, and terminal panes. Use `player.set_money`, `player.give_item`,
`player.add_mail`, `time.set`, `time.advance`, and fixtures to create deterministic
setup before exercising the mod through real UI.

Full action and assertion shapes are documented in `docs/rpc-schema.md`; the C# DSL
and report workflow are documented in `docs/dsl-quickstart.md`.

## Claude Code via MCP

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

Quickstart: https://github.com/fintan/sdv-test-framework/blob/main/docs/mcp-quickstart.md
