# Frobby Documentation Hub

This hub is for mod developers and agents building Stardew Valley mod tests with
Frobby. It links task-oriented docs, reference material, and real scenario
examples.

## Start Here

- New to Frobby: read `README.md`, then `docs/developer-setup.md`.
- Adding Frobby to a mod repo: read the repo scaffold and dependency sections in
  `README.md`.
- Writing JSON scenarios: read the authoring guidance in `README.md` and method
  shapes in `docs/rpc-schema.md`.
- Writing C# DSL tests: read `docs/dsl-quickstart.md`.
- Using Frobby from an agent: read `docs/mcp-quickstart.md` and `AGENTS.md`.
- Looking for examples: read `docs/wiki/examples.md`.

## Add Frobby To A Mod Repo

Use `sdv-test repo init` to scaffold repo-local config and scripts. The generated
scripts read `sdv-test.config.json`, stage configured mod folders into an
isolated test Mods directory, run headlessly by default, and write reports under
`/tmp/<slug>-frobby-results-<version>/`.
They resolve Frobby from `FROBBY_ROOT`, a repo-local dotnet tool manifest, or a
global `sdv-test` command, in that order. Run `scripts/package-install-smoke.sh`
from the Frobby repo when you need to validate the local package/install path
before testing against another mod repo.

Useful docs:

- `README.md` - quickstart, repo scaffold, dependency cache, profiles.
- `docs/developer-setup.md` - local prerequisites and headless setup.
- `docs/wiki/examples.md` - SVE profile and dependency examples.

## Write JSON Scenarios

JSON scenarios are the most portable way to test a mod. A scenario usually:

1. Loads a fixture.
2. Sets deterministic player/world state.
3. Performs player-like input or state transitions.
4. Captures screenshots or draw/text state.
5. Asserts semantic state, text bounds, content assets, or visual output.

Useful docs:

- `README.md` - authoring guidance by capability.
- `docs/rpc-schema.md` - JSON-RPC methods and scenario action shapes.
- `docs/wiki/examples.md` - real scenario files grouped by pattern.

## Profiles, Dependencies, And Fixtures

Use repo dependency caches for external SMAPI mods. Use repo profiles for
alternate packs or config-gated runs. Use `save_overrides.farm_type` when the
same base fixture must be staged as an additional or modded farm type.

Useful docs:

- `README.md` - repo dependency cache, profiles, and farm-type overrides.
- `docs/rpc-schema.md` - scenario schema and action reference.
- `SVE_FROBBY_CAPABILITY_TODO.md` - capability history proven against SVE.

## UI Testing

Prefer click-first and hover-first flows for UI behavior. Use keyboard input only
when the test is specifically about keyboard behavior. For fixed UI panes and
tables, use text bounds assertions to catch overflow.

Useful tools:

- `input.click`, `input.click_text`, `ui.click_text`
- `input.hover`, `input.hover_text`, `ui.hover_text`
- `draw.text_contains`, `draw.text_not_contains`
- `draw.text_all_within`
- `screenshot.capture_next_frame`
- `freeze.begin` for deterministic final screenshots outside active cutscenes

Useful docs:

- `README.md` - authoring guidance.
- `docs/dsl-quickstart.md` - text-fit assertions and report behavior.
- `docs/wiki/examples.md` - Starberg UI scenario examples.

## World And Content Testing

Use runtime state and content assertions for maps, locations, NPCs, shops,
special orders, combat, fishing, visual effects, Stardew tool interactions, and
Content Patcher assets. Keep mod-specific ids and coordinates in repo scenarios,
not in Frobby source.
Combat coverage includes player-like melee attacks, monster/debris state, and
the test-only Combat Lab for isolated monster identity/removal checks.

Useful docs:

- `README.md` - capability guidance.
- `docs/rpc-schema.md` - method reference.
- `docs/wiki/examples.md` - SVE world/content scenario examples.

## Reports And Debugging

Frobby writes static HTML reports with per-step screenshots, final screenshots,
assertion details, and `summary.json`. Use stable `--report-dir` paths when you
want repeated runs to overwrite a known report hub.
MCP agents can also read the current server process' latest report summary via
`frobby://reports/latest/summary` and static report artifacts via the latest report
resource URIs documented in the MCP quickstart.

Useful docs:

- `README.md` - report workflow.
- `docs/dsl-quickstart.md` - HTML reports, bitmap baselines, cache cleanup.
- `docs/mcp-quickstart.md` - MCP latest-report resources.

## MCP And Agent Workflow

The MCP server lets agents list scenarios, run scenarios, capture state, scaffold
tests, and issue raw RPC calls. Agents should use the wiki and examples index
before inventing new scenario shapes.

Useful docs:

- `AGENTS.md` - root agent rules.
- `docs/mcp-quickstart.md` - MCP setup and tool surface.
- `docs/wiki/examples.md` - scenario patterns to reuse.

## Troubleshooting

- If SDV steals the mouse or display, run headless with `--headless` or
  `SDV_TEST_HEADLESS=1`.
- If a mod dependency is missing, use `sdv-test repo deps doctor --repo-root .`.
- If a scenario asserts during a warp/fade, add a runner wait such as
  `wait.location`.
- If a screenshot captures stale UI after input, prefer
  `screenshot.capture_next_frame`.
- If text leaks outside a panel, add or restore `draw.text_all_within` coverage.
- If a content assertion fails for a date-gated asset, cross a real day boundary
  with `time.next_day` so SMAPI/Content Patcher invalidation paths run.
- If a note-gated dig or other tool-driven action does nothing, first prove the
  setup with `state.player.secret_notes_seen` or `wait.player.secret_note_seen`,
  then use `world.use_tool` and assert the runtime side effect separately.
