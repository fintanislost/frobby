# Milestone 1 — Core Framework

**Prerequisite:** M0 passes.

**Goal:** End-to-end scenario execution with state + draw-call assertions against a real mod.

**Duration:** 3-4 weeks.

**Exit criteria:** 10 sample scenarios against a small existing Content Patcher mod, all passing reproducibly in <2 minutes total on the author's workstation.

## Deliverables

### D1.1 — RPC transport

- Unix socket server in harness mod
- JSON-RPC 2.0 protocol (no custom framing; stick to the spec)
- Command loop on game thread, socket reader on background thread, queue between
- Schema documented in `docs/rpc-schema.md` — every method has request/response examples

### D1.2 — Harness command surface

Minimum viable API per spec §4.3 & §4.4:

**State queries:** `state.player`, `state.location`, `state.npc`, `state.time`, `state.menu`
**Manipulators:** `player.warp`, `player.give_item`, `player.set_money`, `time.advance`, `world.set_weather`
**Draw:** `draw.arm`, `draw.snapshot`, `draw.find`, `draw.assert_contains`
**Lifecycle:** `scenario.begin`, `scenario.end`, `fixture.load`

### D1.3 — Runner CLI

- .NET 8 console app at `src/Runner/`
- `run`, `doctor`, `list` commands
- Launches SDV subprocess, manages lifecycle, streams RPC
- Console reporter (Playwright-style) with pass/fail/skip, duration, first failure detail
- `--filter` flag for name-glob filtering

### D1.4 — Scenario format + parser

- JSON schema in `schemas/scenario.schema.json`
- Parser with validation errors that point to line/column
- Loader that resolves `fixture` and `mods` paths relative to scenario file

### D1.5 — Texture → asset path resolution (Tier 1)

- SMAPI content pipeline hook populating the weak-ref map
- Tier 2 (hash fallback) stubbed but not required for M1
- Documented: what fraction of real draws resolve via Tier 1 on a test fixture

### D1.6 — Determinism controller

- FREEZE/THAW implementation per `@.claude/rules/determinism.md`
- Integrated into scenario lifecycle (auto-FREEZE before assertions)
- Determinism regression test that runs in CI

### D1.7 — Sample suite

- Pick a small, stable community CP mod (get maintainer buy-in or use our own example mod)
- 10 scenarios covering: menu content, tile decoration, NPC portrait replacement, seasonal variation, conditional patches
- All pass twice in a row
- Documented in `examples/cp-mod-tests/README.md`

## Task order

1. RPC transport + socket lifecycle (simplest thing that handshakes)
2. One state query (`state.player`) end-to-end, runner → harness → response
3. One manipulator (`player.warp`), prove state changes persist
4. Arm/snapshot draw recorder over RPC
5. Determinism controller wired into scenario lifecycle
6. Scenario format + runner execution
7. Texture path resolution Tier 1
8. `draw.find` + `draw.assert_contains`
9. Sample suite authoring

Each of these gets its own PR, its own test, its own commit trail.

## Non-goals for M1

- Bitmap fallback (M2)
- Record mode (M2)
- Watch mode (M2)
- TAP/JUnit reporters (M2)
- C# fluent DSL (M3)
- MCP server (M3)

## Risks

- **SMAPI API drift during development.** Pin the minor version in `manifest.json`; upgrade deliberately at milestone boundaries.
- **RPC performance.** Target: <10ms round-trip for simple queries on localhost. If we're slower, investigate before widening the API.
- **Fixture save compatibility.** Pin the SDV version; regenerate fixtures if SDV updates mid-milestone.
