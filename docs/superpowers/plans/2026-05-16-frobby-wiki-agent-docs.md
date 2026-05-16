# Frobby Wiki And Agent Documentation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a task-oriented Frobby documentation hub and make `AGENTS.md` the canonical agent entrypoint with an explicit docs-update completion rule.

**Architecture:** Keep `README.md` as the public quickstart, move agent rules into root `AGENTS.md`, convert `CLAUDE.md` into a compatibility pointer, and add wiki pages under `docs/wiki/`. The wiki should link to existing reference docs and sibling-repo scenario examples instead of duplicating long reference material.

**Tech Stack:** Markdown, existing Frobby docs tree, git, ripgrep-based documentation checks.

---

## File Map

- Create: `AGENTS.md` — canonical agent constitution and documentation completion rule.
- Modify: `CLAUDE.md` — compatibility pointer to `AGENTS.md`.
- Create: `docs/wiki/index.md` — searchable task-oriented documentation hub.
- Create: `docs/wiki/examples.md` — curated real scenario/example index.
- Modify: `README.md` — link users and agents to the wiki hub and update the tree.
- Optional verify only: sibling examples under `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/` and `/home/fintan/stardewRepos/stonks/tests/sdv/`.

## Task 1: Create Canonical `AGENTS.md`

**Files:**
- Create: `AGENTS.md`
- Modify: `CLAUDE.md`

- [ ] **Step 1: Inspect current agent constitution**

Run:

```bash
sed -n '1,260p' CLAUDE.md
```

Expected: file contains the current Frobby project constitution, rules, workflow, boundaries, and testing commands.

- [ ] **Step 2: Create `AGENTS.md` from the current constitution**

Create `AGENTS.md` with this content, preserving the current project rules while updating the name and docs workflow:

```markdown
# Frobby Agent Guide

Frobby is a Stardew Valley mod testing framework. Read this file first when an
agent starts work in this repository, then load the referenced rule files and
docs as needed.

## Project Summary

Frobby launches Stardew Valley with a SMAPI harness, drives real game/menu input
through JSON-RPC, records semantic draw/text state through Harmony patches, and
writes static HTML reports with screenshots and machine-readable summaries.

The draw-call interception approach is the load-bearing testing strategy. Do not
replace it with pixel diffing as the primary verification path. Bitmap diffing is
only a fallback for shader, procedural, or full-frame visual behavior.

## Start Here

- `docs/wiki/index.md` is the task-oriented documentation hub for agents and mod
  developers.
- `README.md` is the public quickstart and capability overview.
- `docs/rpc-schema.md` is the authoritative JSON-RPC method reference.
- `docs/roadmap.md` is the prioritized project backlog.
- `docs/milestones/current.md` tracks the current milestone and acceptance
  notes.
- `docs/wiki/examples.md` points to real scenarios in Frobby, SVE, and Starberg
  when those sibling repos are available locally.

## Always-Loaded Rules

- `.claude/rules/tdd.md` — TDD discipline for this codebase.
- `.claude/rules/harmony-patching.md` — safe Harmony patch patterns.
- `.claude/rules/sdv-conventions.md` — SDV/SMAPI-specific gotchas.
- `.claude/rules/commit-style.md` — commit and PR conventions.

## Load-On-Demand Rules

- `.claude/rules/draw-call-recorder.md` — deep detail on draw-call assertions.
- `.claude/rules/determinism.md` — RNG pinning, animation freeze, NPC halt.
- `.claude/rules/fixtures.md` — save fixture management.
- `.claude/rules/ci-integration.md` — GitHub Actions and runner notes.

## Documentation Completion Rule

No slice, feature, or bugfix is complete until documentation has been checked.
For every change, do one of the following before final status:

1. Update the relevant docs in the same slice.
2. State explicitly in the final status why no docs change was needed.

Capability additions should usually update at least one of:

- `docs/wiki/index.md`
- `docs/wiki/examples.md`
- `README.md`
- `docs/rpc-schema.md`
- package-facing docs under `nuget/`
- capability/history notes such as `SVE_FROBBY_CAPABILITY_TODO.md`

When adding a new RPC method, update `docs/rpc-schema.md` in the same commit.
When adding or changing repo-local workflows, update `README.md` and the wiki.
When adding new scenario patterns, add or adjust links in `docs/wiki/examples.md`.

## Workflow

1. Before implementation, identify which milestone, roadmap item, or capability
   slice the work advances.
2. Use design and planning docs under `docs/superpowers/` for multi-file work.
3. Keep Frobby capabilities mod-neutral. Real mod suites such as Starberg and
   SVE should prove capabilities without baking their ids into Frobby source.
4. Spike work lives in `docs/spikes/`; write the spike report before promoting
   spike code to `src/`.
5. Every Harmony patch gets a comment block explaining target method, patch type,
   reason, and rollback plan.
6. Prefer headless test execution unless a visible run is explicitly needed.

## Boundaries

- Do not introduce dependencies beyond what is pinned in the spec without
  explicit approval.
- Do not add YAML-based config; this project is JSON-first for schema tooling.
- Do not couple to SDV internals beyond what SMAPI public APIs and documented
  Harmony patches expose. If private reflection is required, document why.
- Do not invent scenarios or test cases that are not grounded in real mod
  development pain points.

## Running And Testing

See `docs/developer-setup.md` for environment setup.

- Build: `dotnet build sdv-test-framework.slnx`
- Runner unit tests: `dotnet test tests/Runner.Tests/`
- Harness unit tests: `dotnet test tests/Harness.Tests/`
- Harness integration tests: `./scripts/run-integration-tests.sh`
- Full local check: `./scripts/ci.sh`

When testing real mod suites through repo wrappers, prefer `--headless` or set
`SDV_TEST_HEADLESS=1`.

## Style

- C# 12, nullable reference types enabled, warnings as errors.
- Use `var` for obvious types and explicit types when the right-hand side is not
  self-documenting.
- XML doc comments on public APIs.
- Avoid abbreviations except `Rpc`, `Sdv`, `Cp`, and `Io`.

## When Stuck

- Search the wiki and `docs/spikes/` for prior investigation.
- Use the roadmap and capability backlog files to understand why a feature exists.
- For SDV internals questions, document unknowns in `docs/open-questions.md` and
  keep interfaces swappable.
```

- [ ] **Step 3: Replace `CLAUDE.md` with a compatibility pointer**

Replace `CLAUDE.md` with:

```markdown
# Compatibility Pointer

`AGENTS.md` is the canonical Frobby agent guide.

Read `AGENTS.md` first, then load the referenced rule files and docs as needed.
This file remains only for tools that still look for `CLAUDE.md`.
```

- [ ] **Step 4: Verify root agent docs**

Run:

```bash
test -f AGENTS.md
rg -n "Documentation Completion Rule|docs/wiki/index.md|docs/wiki/examples.md" AGENTS.md
rg -n "AGENTS.md is the canonical" CLAUDE.md
```

Expected: all commands exit 0 and show the canonical guide plus compatibility pointer.

- [ ] **Step 5: Commit Task 1**

```bash
git add AGENTS.md CLAUDE.md
git commit -m "docs: add canonical agent guide"
```

## Task 2: Add Wiki Hub

**Files:**
- Create: `docs/wiki/index.md`
- Modify: `README.md`

- [ ] **Step 1: Create wiki directory**

Run:

```bash
mkdir -p docs/wiki
```

- [ ] **Step 2: Create `docs/wiki/index.md`**

Create `docs/wiki/index.md` with:

```markdown
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

Useful docs:

- `README.md` — quickstart, repo scaffold, dependency cache, profiles.
- `docs/developer-setup.md` — local prerequisites and headless setup.
- `docs/wiki/examples.md` — SVE profile and dependency examples.

## Write JSON Scenarios

JSON scenarios are the most portable way to test a mod. A scenario usually:

1. Loads a fixture.
2. Sets deterministic player/world state.
3. Performs player-like input or state transitions.
4. Captures screenshots or draw/text state.
5. Asserts semantic state, text bounds, content assets, or visual output.

Useful docs:

- `README.md` — authoring guidance by capability.
- `docs/rpc-schema.md` — JSON-RPC methods and scenario action shapes.
- `docs/wiki/examples.md` — real scenario files grouped by pattern.

## Profiles, Dependencies, And Fixtures

Use repo dependency caches for external SMAPI mods. Use repo profiles for
alternate packs or config-gated runs. Use `save_overrides.farm_type` when the
same base fixture must be staged as an additional or modded farm type.

Useful docs:

- `README.md` — repo dependency cache, profiles, and farm-type overrides.
- `docs/rpc-schema.md` — scenario schema and action reference.
- `SVE_FROBBY_CAPABILITY_TODO.md` — capability history proven against SVE.

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

- `README.md` — authoring guidance.
- `docs/dsl-quickstart.md` — text-fit assertions and report behavior.
- `docs/wiki/examples.md` — Starberg UI scenario examples.

## World And Content Testing

Use runtime state and content assertions for maps, locations, NPCs, shops,
special orders, combat, fishing, visual effects, and Content Patcher assets.
Keep mod-specific ids and coordinates in repo scenarios, not in Frobby source.

Useful docs:

- `README.md` — capability guidance.
- `docs/rpc-schema.md` — method reference.
- `docs/wiki/examples.md` — SVE world/content scenario examples.

## Reports And Debugging

Frobby writes static HTML reports with per-step screenshots, final screenshots,
assertion details, and `summary.json`. Use stable `--report-dir` paths when you
want repeated runs to overwrite a known report hub.

Useful docs:

- `README.md` — report workflow.
- `docs/dsl-quickstart.md` — HTML reports, bitmap baselines, cache cleanup.

## MCP And Agent Workflow

The MCP server lets agents list scenarios, run scenarios, capture state, scaffold
tests, and issue raw RPC calls. Agents should use the wiki and examples index
before inventing new scenario shapes.

Useful docs:

- `AGENTS.md` — root agent rules.
- `docs/mcp-quickstart.md` — MCP setup and tool surface.
- `docs/wiki/examples.md` — scenario patterns to reuse.

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
```

- [ ] **Step 3: Update README documentation map**

In `README.md`, update the `## Documentation Map` section to include:

```markdown
- `docs/wiki/index.md` — task-oriented documentation hub for agents and mod
  developers.
- `docs/wiki/examples.md` — curated index of real scenario examples.
```

Also update the tree under `## What's in here` so `docs/wiki/` appears below
`docs/`.

- [ ] **Step 4: Verify wiki hub links**

Run:

```bash
test -f docs/wiki/index.md
rg -n "docs/wiki/examples.md|docs/rpc-schema.md|docs/mcp-quickstart.md|AGENTS.md" docs/wiki/index.md README.md
```

Expected: all referenced docs appear in the hub or README.

- [ ] **Step 5: Commit Task 2**

```bash
git add docs/wiki/index.md README.md
git commit -m "docs: add Frobby wiki hub"
```

## Task 3: Add Examples Index

**Files:**
- Create: `docs/wiki/examples.md`

- [ ] **Step 1: Confirm example paths exist locally**

Run:

```bash
test -f /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/24-sve-frontier-farm-profile.test.json
test -f /home/fintan/stardewRepos/stonks/tests/sdv/38-starberg-chart-panel-live.test.json
test -f /home/fintan/stardewRepos/stonks/tests/sdv/70-starberg-save-reload-persistence.test.json
```

Expected: commands exit 0 if sibling repos are present. If one sibling repo is
missing in another environment, keep the wiki text as optional sibling examples.

- [ ] **Step 2: Create `docs/wiki/examples.md`**

Create `docs/wiki/examples.md` with:

```markdown
# Frobby Scenario Examples

This page points to real scenario files that demonstrate Frobby patterns. The
examples live in sibling mod repos when those repos are available locally. Do not
copy scenario bodies into this page; inspect the source files so examples stay
current.

## Repo Profiles And Dependencies

- SVE core smoke:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/01-sve-core-loads.test.json`
- SVE Grandpa's Farm profile:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/20-sve-grandpas-farm-profile.test.json`
- SVE Frontier Farm profile:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/24-sve-frontier-farm-profile.test.json`

Use these when adding profile coverage, external dependency cache coverage, or
alternate content-pack runs.

## Alternate Farm Fixtures

- Frontier Farm fixture override:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/24-sve-frontier-farm-profile.test.json`
- Frontier Farm config-gated shortcut coverage:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/25-sve-frontier-farm-instant-unlocks.test.json`

Use these when a test needs `save_overrides.farm_type` to stage the same source
fixture as a modded/additional farm.

## Click-First UI Testing

- Starberg click navigation:
  `/home/fintan/stardewRepos/stonks/tests/sdv/10-starberg-panel-click-navigation.test.json`
- Starberg order entry click flow:
  `/home/fintan/stardewRepos/stonks/tests/sdv/27-starberg-click-text-buy-order.test.json`
- Starberg activity panel click flow:
  `/home/fintan/stardewRepos/stonks/tests/sdv/34-starberg-click-text-activity-panel.test.json`

Use these when testing menu panels through player-like clicks instead of command
shortcuts.

## Text Bounds, Screenshots, And Reports

- Starberg visual baseline:
  `/home/fintan/stardewRepos/stonks/tests/sdv/26-starberg-ui-visual-baseline.test.json`
- Starberg chart panel:
  `/home/fintan/stardewRepos/stonks/tests/sdv/38-starberg-chart-panel-live.test.json`
- Starberg news/intel document flow:
  `/home/fintan/stardewRepos/stonks/tests/sdv/77-starberg-news-intel-depth.test.json`

Use these when adding `draw.text_all_within`, step screenshots,
`screenshot.capture_next_frame`, or final frozen screenshot coverage.

## Runtime Map And Content Assertions

- SVE content asset runtime checks:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/04-sve-content-assets-runtime.test.json`
- SVE custom location and tile-action warp:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/06-sve-tile-action-warp.test.json`
- SVE Frontier Farm runtime map checks:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/24-sve-frontier-farm-profile.test.json`

Use these when proving Content Patcher maps, data assets, and runtime location
metadata.

## NPCs, Dialogue, Events, And Festivals

- SVE NPC relationship/dialogue:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/05-sve-npc-schedules-dialogue-relationships.test.json`
- SVE event dialogue choice:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/11-sve-event-dialogue-choice.test.json`
- SVE Spirit's Eve festival chest:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/19-sve-spirit-eve-chest.test.json`

Use these when testing events, dialogue choice menus, relationship state, or
festival maps.

## Shops, Inventory, Combat, Fishing, And World Content

- SVE custom shop and inventory:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/08-sve-custom-shop-inventory-items.test.json`
- SVE combat damage:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/12-sve-combat-monster-damage.test.json`
- SVE fishing table and catch sampling:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/16-sve-fishing-core.test.json`
- SVE world object interaction:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/18-sve-object-piggy-bank-interaction.test.json`

Use these when testing runtime state rather than parsing a mod's content files.

## Save, Reload, And Long-Running State

- Starberg save/reload smoke:
  `/home/fintan/stardewRepos/stonks/tests/sdv/70-starberg-save-reload-persistence.test.json`
- Starberg pending settlement persistence:
  `/home/fintan/stardewRepos/stonks/tests/sdv/83-starberg-sell-settlement-save-reload.test.json`
- Starberg depleted book side persistence:
  `/home/fintan/stardewRepos/stonks/tests/sdv/84-starberg-empty-book-side-save-reload.test.json`

Use these when testing Frobby's neutral save/reload flow and mod state that must
survive title-screen reloads.
```

- [ ] **Step 3: Verify referenced examples**

Run:

```bash
rg -o "/home/fintan/stardewRepos/[^` ]+\\.test\\.json" docs/wiki/examples.md | sort -u
```

For each listed path, run `test -f <path>`. If a path is wrong in this checkout,
fix the path or remove that example before committing.

- [ ] **Step 4: Commit Task 3**

```bash
git add docs/wiki/examples.md
git commit -m "docs: add scenario examples index"
```

## Task 4: Final Documentation Verification

**Files:**
- Verify only unless issues are found.

- [ ] **Step 1: Check status and recent commits**

Run:

```bash
git status --short --branch
git log --oneline -5
```

Expected: on `main`, clean or only expected docs edits before final commit.

- [ ] **Step 2: Run documentation search checks**

Run:

```bash
rg -n "AGENTS.md|docs/wiki/index.md|docs/wiki/examples.md|Documentation Completion Rule" AGENTS.md CLAUDE.md README.md docs/wiki
rg -n "PLACEHOLDER|UNFINISHED_MARKER|IMPLEMENT_LATER" AGENTS.md CLAUDE.md docs/wiki
```

Expected: first command finds the new docs and rule. Second command exits 1.

- [ ] **Step 3: Run lightweight Markdown file checks**

Run:

```bash
test -f AGENTS.md
test -f docs/wiki/index.md
test -f docs/wiki/examples.md
test -f docs/superpowers/specs/2026-05-16-frobby-wiki-agent-docs-design.md
test -f docs/superpowers/plans/2026-05-16-frobby-wiki-agent-docs.md
```

Expected: all files exist.

- [ ] **Step 4: Commit plan if not already committed**

If this plan file is not committed yet, run:

```bash
git add docs/superpowers/plans/2026-05-16-frobby-wiki-agent-docs.md
git commit -m "docs: plan Frobby wiki and agent docs"
```

- [ ] **Step 5: Final status**

Run:

```bash
git status --short --branch
```

Expected: clean `main`.

## Self-Review

Spec coverage:

- `AGENTS.md` canonical entrypoint: Task 1.
- `CLAUDE.md` compatibility pointer: Task 1.
- Wiki hub: Task 2.
- Examples index: Task 3.
- README pointer: Task 2.
- Documentation completion rule: Task 1.
- Search and file verification: Task 4.

The slice is docs-only and does not require C# unit tests. Existing Frobby tests
were already run after merging Slice 17 to `main`; this plan's verification
focuses on Markdown presence, references, and unfinished-marker checks.
