# Frobby — Stardew Valley Mod Testing Framework

Frobby is an automated testing framework for Stardew Valley mods. It launches SDV
with a SMAPI harness, drives real game and menu input, captures semantic draw/text
events, and writes static HTML reports with per-step screenshots.

**Status:** active 0.1.x development. The runner, harness, JSON scenario format,
C# DSL, MCP server, HTML reports, click/hover helpers, text-fit assertions,
runtime content-asset assertions, and headless Linux execution are implemented
and used against real mod suites.

## Quick Start For Mod Developers

Install from NuGet when published:

```bash
dotnet tool install -g SdvTestFramework.Cli
```

When working from the source tree:

```bash
dotnet run --project src/Runner -- --help
```

Run one scenario or a directory of `*.test.json` scenarios:

```bash
sdv-test run --headless tests/sdv
sdv-test run-suite --headless tests/sdv
```

Use `--headless` on Linux by default so SDV renders under `xvfb-run` instead of
using the active desktop display or mouse cursor. Set `SDV_TEST_HEADLESS=1` when
you want all launcher paths to behave headlessly without repeating the CLI flag.

For mod-local workflows, prefer a small repo script that pins the mod build path,
report directory, and Frobby command:

```bash
./scripts/sdv-test --headless
```

For new mod repos, use the repo scaffold flow:

```bash
sdv-test repo init --project-name "Example Mod" --slug example-mod \
  --build-command dotnet --build-arg build --build-arg Example.sln \
  --extra-mod bin/Release/net6.0
./scripts/sdv-test --dry-run
```

The generated scripts read `sdv-test.config.json`, default to headless execution,
stage every configured `extra_mod`, and write a stable
`/tmp/<slug>-frobby-results-<version>/` report hub.

Each configured `extra_mod` must resolve to a SMAPI mod directory that contains
`manifest.json`. If a project writes DLLs to one folder but packages the actual
mod elsewhere during its build, point the scaffold at the packaged mod folder.

### Repo Dependency Cache

For repo-local test suites, keep external dependency mods in Frobby's local cache
instead of pointing at your playable Stardew `Mods` folder:

```bash
sdv-test repo deps import --from "/path/to/ContentPatcher"
sdv-test repo deps import --from "/path/to/FarmTypeManager"
sdv-test repo deps doctor --repo-root .
```

The default cache lives at `sdv-test-framework/.cache/deps/` and is gitignored.
Set `SDV_TEST_MOD_CACHE=/path/to/deps` when a repo needs a shared or CI-provided
cache. Use `modSets[].deps` for external dependency mods keyed by SMAPI
`UniqueID`; keep `modSets[].extraMods` for repo-owned mod folders and content
packs. Normal `sdv-test repo run` stages cached copies into the isolated test
mods directory and does not read the user's live game `Mods` folder unless the
repo config still contains explicit `${SDV_GAME_MODS}` paths.
Before `sdv-test run` stages extra mods, it removes stale mods it previously
staged but that are no longer requested. Custom `--mods-path` directories
preserve unmarked mods; the default Frobby cache can clean unmarked stale mods
because it is test-owned.

### Repo Profiles

Large mods can define `profiles` in `sdv-test.config.json` for alternate packs
or config-gated runs. Scenarios select a profile with top-level `"profile":
"profile-id"`. Repo runs stage each profile into
`.cache/frobby-test-mods/<cacheNamespace>/`; when `cacheNamespace` is omitted it
defaults from the profile id. The user's playable Stardew `Mods` folder stays
untouched.

When a repo needs the same base fixture staged as another farm type, a scenario
can apply a farm override while selecting the relevant profile:

```json
{
  "name": "alternate_farm_profile",
  "profile": "alternate-farm",
  "fixture": "spring_day_1",
  "save_overrides": {
    "farm_type": {
      "which_farm": "mod",
      "mod_farm_id": "ExampleFarm"
    }
  },
  "steps": []
}
```

The CLI writes reports to `./test-results/<run-id>/` by default. Pass
`--report-dir <path>` for stable locations, such as
`/tmp/sdv-test-results-0.1.0/`, when repeated runs should overwrite a known report hub.
When using the MCP server, agents can discover the latest report through
`resources/list` and read `frobby://reports/latest/summary`,
`frobby://reports/latest/index`, or `frobby://reports/latest/scenarios` when the
current server process has run or recorded a report.

## Report Workflow

Each HTML report contains:

- `index.html` — run dashboard with links to scenario reports.
- `summary.json` — machine-readable run data for agents and CI.
- `scenarios/<name>/index.html` — step timeline, assertions, screenshots, and
  failure forensics for one scenario.

Use `screenshot.capture` for immediate captures. Use
`screenshot.capture_next_frame` after click, hover, typed input, or any action where
the next rendered frame is the meaningful visual state. Final assertion screenshots
should normally be captured under `freeze.begin` for deterministic output.

## Authoring Guidance

Frobby tests should exercise the UI like a player whenever possible:

- Prefer `ui.click_text`, `input.click_text`, `ui.hover_text`, `input.hover_text`,
  `input.click`, and `input.hover` for menu flows. For Stardew dialogue
  choices, prefer `wait.menu` with `choice_text`/`choice_key` and
  `event.advance` with the same choice target so the runner uses
  `input.click_menu_choice` against reflected menu response bounds.
- Keep keyboard input for scenarios that explicitly validate keyboard behavior.
- Use semantic text assertions for Stardew UI: `draw.text_contains`,
  `draw.text_not_contains`, `text_equals`, `text_matches`, bounds filters,
  `min_count`, `max_count`, and `color_any`.
- Use `draw.text_all_within` as the standard guardrail for fixed panes, tables,
  terminal bodies, button bars, and any UI where text overflow is a regression.
- Use player/world setup helpers such as `player.set_money`, `player.give_item`,
  `player.add_mail`, `player.add_event_seen`, `player.add_secret_note_seen`,
  `time.set`, `time.advance`, `time.next_day`, and fixture loading to create
  deterministic test state before exercising the mod. Use `wait.player` for
  progression flags such as received mail, pending mail, seen events, and seen
  secret notes.
- Use `state.locations`, expanded `state.location`, `state.map_tile`,
  `state.tile_actions`, `world.interact_tile_action`, and runner-side
  `wait.location` when testing custom mod locations, maps, direct warp flows, and
  map-defined `Action` / `TouchAction` behavior. These waits also hold through
  Stardew warp/fade transitions, so scenarios do not assert against a black
  transition frame. These are neutral map introspection and interaction tools and
  should be preferred over hard-coded mod-specific helper scripts.
- Use `world.use_tool` for player-like map interactions that depend on Stardew
  tool behavior, such as hoe digs. Keep the tile coordinates and expected item or
  flag ids in the mod repo scenario, then prove the side effect with runtime
  state such as `wait.player`, `state.location`, or inventory assertions.
- Use `state.npcs`, parameterized `state.npc` assertions, `player.set_friendship`,
  `world.warp_npc`, and runner-side `wait.npc_location` for custom NPC
  relationship, schedule, and dialogue flows. These helpers are mod-neutral and
  work for vanilla or Content Patcher-added NPCs.
- Use `shop.open`, `shop.purchase`, `state.shop`, and `state.player.items` for
  custom shop and inventory flows. Prefer `qualified_id` when asserting Stardew
  1.6 custom items, and use raw `item_id` only when the scenario intentionally
  works from unqualified mod data.
- Use `state.location.resource_clumps`, `state.location.monsters`,
  `state.location.debris`, and
  runner-side `wait.location_content` when testing spawned world content such as
  logs, boulders, forage-like objects, ore, monsters, dropped combat loot, or
  transient item debris. Monster summaries can expose runtime `health`,
  `max_health`, `damage`, and `sprite_texture`, and debris summaries can expose
  runtime type, item identity, stack, quality, and category when Stardew provides
  those fields. These helpers observe runtime Stardew state and stay independent
  from specific spawn frameworks.
- Use `combat.attack` with `state.location.monsters` and
  `wait.location_content` numeric filters for player-like combat checks. Prefer
  baseline-then-attack waits, such as first proving `health` and `max_health`
  and then waiting for `health_lt`, over fixed sleeps. Keep mod-specific monster
  coordinates in repo scenarios rather than in Frobby code. For moving monsters,
  runner scenarios may pass a `target` selector so each repeated attack retargets
  from current `state.location.monsters` metadata.
- Use `wait.player` when a scenario needs to wait for player health changes after
  contact damage, hazards, or defensive effects. Prefer health comparison waits
  such as `health_lt` or `health_gte` over fixed sleeps.
- Use `state.visual_effects` and runner-side `wait.visual_effects` when testing
  temporary sprites, light sources, ambient light, or weather debris. For example:
  `{ "action": "wait.visual_effects", "args": { "location": "Example.VisualLocation", "temporary_sprites": { "texture_asset": "ExampleMod/Visuals/Effects", "source_rect": [0, 32, 16, 16], "min_count": 1 }, "timeout_ms": 10000 } }`.
  This is runner-side polling over `state.visual_effects`; final rendering should
  still use draw, screenshot, or bitmap tools.
- Use `content.asset` assertions when the test needs runtime truth for a named
  Stardew asset, such as a Content Patcher-added map, a nested `Data/*` entry, or
  a texture that should exist before it is rendered.
- Use `event.start`, `event.skip`, `state.event`, `wait.event_active`, and
  `wait.event_complete` for cutscenes or other Stardew events. Use
  `wait.menu` plus `event.advance` for click-based dialogue acknowledgement and
  question choices inside events. Active-event screenshots should use
  `screenshot.capture_next_frame`; `freeze.begin` still rejects cutscenes.
- Use `festival.start`, `wait.event_active` with `is_festival`, and
  `wait.location_content` contained-item filters when testing active festival
  maps, festival chests, or other runtime containers. Keep festival-specific
  item ids and tile coordinates in the mod repo scenarios.
- Use `state.special_orders` and runner-side `wait.special_order` for special
  order registration, event-gated order activation, objective progress, donated
  item state, completion keys, and returned donations. Keep mod-specific order
  keys and event prerequisites in repo scenarios, not in Frobby.
- Use `drop_box.deposit` for neutral donation-objective tests after proving the
  active order and drop box through `state.special_orders`. The action works from
  Stardew runtime special-order state and should not parse a mod's content packs.
- Use scenario `save_overrides.farm_type` when a repo needs the same base fixture
  staged as an additional or modded farm type. The override mutates only the
  staged save copy and writes a derived save folder name, so standard and modded
  variants of the same fixture can run in the same suite. Keep mod farm ids such
  as `FrontierFarm` in repo scenarios rather than in Frobby code.

See `docs/dsl-quickstart.md` for C# DSL usage, report behavior, text-fit
assertions, bitmap baselines, and cache cleanup. See `docs/rpc-schema.md` for the
JSON-RPC method reference and scenario action shapes.

## What's in here

```
.
├── AGENTS.md                    # Canonical agent guide and documentation rules
├── CLAUDE.md                    # Compatibility pointer to AGENTS.md
├── .claude/
│   ├── rules/                   # Modular convention docs, loaded on demand
│   ├── agents/                  # Specialized subagents (spike-runner, reviewer, sdv-expert)
│   └── commands/                # Custom slash commands (/spike, /harmony-patch, etc.)
├── .mcp.json                    # Project-level MCP server config
├── docs/
│   ├── wiki/                    # Task-oriented docs hub and scenario examples
│   ├── spec.md                  # Framework design and scenario model
│   ├── milestones/              # Completed/current implementation notes
│   ├── rpc-schema.md            # JSON-RPC protocol reference
│   ├── patches.md               # Active Harmony patches registry
│   ├── dsl-quickstart.md        # C# DSL + HTML report workflow
│   ├── mcp-quickstart.md        # MCP server setup and tool surface
│   └── spikes/                  # Time-boxed investigation reports
└── install.sh                   # One-time setup helper
```

## Design premise in one paragraph

Stardew Valley renders through `SpriteBatch.Draw` calls with structured arguments (texture, source rect, dest rect, color, layer depth). By Harmony-patching these calls, we can capture rendering as a queryable event stream and assert semantically ("Abigail's happy portrait was drawn at tile X with tint Y") instead of diffing framebuffers. This dodges GPU nondeterminism, animation timing issues, and resolution coupling. Combined with direct state manipulation via SMAPI APIs and RNG/time pinning, scenarios become deterministic and reproducible. Pixel diffing survives as a 5% fallback for shader and procedural content.

## Core Capabilities

- Deterministic scenario sessions with fixture loading and freeze controls.
- Semantic draw-call and text capture through SMAPI/Harmony instrumentation.
- Runtime content-asset inspection for maps, textures, strings, and bounded
  `Data/*` dictionaries, including selected nested data objects, after Content
  Patcher and game conditions apply.
- Fishing diagnostics for fishable tile context, effective fishing candidates,
  and bounded runtime catch sampling through `state.fishing_context`,
  `state.fishing_table`, and `fishing.sample_catch`.
- Click-first and hover-first menu automation, including text-targeted helpers.
- Player/world state mutators for money, inventory, mail, time, weather, shops,
  furniture, interactions, and title-screen reload flows.
- Structured runtime shop and inventory snapshots with qualified item ids for
  custom item, reward, and purchase assertions.
- Static HTML reports with report hub, scenario pages, step screenshots, failure
  screenshots, bitmap diff artifacts, and JSON summaries.
- Bitmap fallback assertions with SSIM, pixel-exact, dHash, tolerance tiers, and
  baseline management.
- MCP server tools for agent-driven scenario listing, scaffolding, state capture,
  raw RPC calls, and lightweight scenario execution.

## Documentation Map

- `docs/wiki/index.md` — task-oriented documentation hub for agents and mod
  developers.
- `docs/wiki/examples.md` — curated index of real scenario examples.
- `docs/developer-setup.md` — local setup, environment variables, and headless notes.
- `docs/dsl-quickstart.md` — C# DSL, HTML reports, text-fit assertions, screenshots,
  bitmap diffing, baselines, and cache cleanup.
- `docs/rpc-schema.md` — authoritative JSON-RPC method reference.
- `docs/mcp-quickstart.md` — MCP server configuration and tool limitations.
- `nuget/README-Cli.md` — installed `sdv-test` CLI quick reference.
- `nuget/README-Dsl.md` and `nuget/README-Protocol.md` — package-facing docs.

## Milestones

Historical milestone notes live under `docs/milestones/`. Treat those as project
history, not the current quickstart.

## License

TBD (MIT or Apache 2.0 — decide before M1 public release).
