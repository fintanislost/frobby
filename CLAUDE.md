# [TBD] — Stardew Valley Mod Testing Framework

Project constitution. Read this first, then load rules as needed.

See @docs/spec.md for the full design spec.
See @docs/milestones/ for current phase and acceptance criteria.

## What this is

An automated testing framework for Stardew Valley mods. Two-process architecture: external CLI runner talks over Unix socket (JSON-RPC) to an in-game SMAPI harness mod that Harmony-patches `SpriteBatch.Draw` to enable semantic assertions against draw calls rather than pixel-diffed framebuffers.

The **draw-call interception approach** is the load-bearing insight. Do not suggest pixel diffing as the primary strategy. Bitmap diff exists only as a fallback for shader/procedural content (~5% of cases).

## Current phase

See @docs/milestones/current.md for the active milestone. Always check this before starting work.

## Roadmap

See @docs/roadmap.md for the prioritized backlog. **Check it at every session start** — skim
the near-term tier (Tier 1) to see what's ready to pick up, and note what's in-flight.

**Maintain the roadmap as you go:**

1. **When an item ships**: move it from its tier into the `## Completed` section with the
   date + a one-line summary of what landed (test count delta, key files, plan link).
   Don't leave checked-off items in their original tier — they make future skims harder.
2. **When a new TODO surfaces** (code-review fallout, smoke-test bug, spec gap, user
   feedback): add it to the appropriate tier with a source attribution. Every new entry
   should be specific and actionable — no "improve X" or "refactor Y" without justification.
3. **North-star goal alignment**: the roadmap's north-star is "LLM-driven mod testing."
   When deciding tier placement, weight items that unblock Claude-as-tester workflows
   higher than pure-polish items.

Don't wait for an end-of-session commit to maintain the roadmap — update it alongside the
code that closes or surfaces each item.

## Tech stack

- **.NET 8** for harness mod and CLI runner
- **SMAPI 4.x** as the mod loader (assume latest stable unless pinned in milestone)
- **Harmony 2.x** (bundled with SMAPI) for runtime patching
- **System.Text.Json** for serialization (not Newtonsoft; no extra deps)
- **Unix sockets** on Linux/macOS, **named pipes** on Windows
- **xUnit** for unit tests of the runner itself

## Rules — always loaded

- @.claude/rules/tdd.md — TDD discipline for this codebase
- @.claude/rules/harmony-patching.md — safe Harmony patch patterns
- @.claude/rules/sdv-conventions.md — SDV/SMAPI-specific gotchas
- @.claude/rules/commit-style.md — commit and PR conventions

## Rules — load on demand

- @.claude/rules/draw-call-recorder.md — deep detail on §4.2 of spec
- @.claude/rules/determinism.md — RNG pinning, animation freeze, NPC halt
- @.claude/rules/fixtures.md — save fixture management
- @.claude/rules/ci-integration.md — GitHub Actions + Proxmox runner notes

## Workflow

1. Before any implementation task, confirm which milestone it advances. Link it.
2. Use Superpowers `/brainstorm` for ambiguous design decisions, `/write-plan` for anything >1 file, `/execute-plan` for execution.
3. Spike work lives in `docs/spikes/` — write the spike report **before** promoting code to `src/`.
4. Every Harmony patch gets a comment block explaining: target method, patch type, why, rollback plan.
5. Every new RPC method gets a schema entry in `docs/rpc-schema.md` in the same commit.

## Boundaries

- **Do not** introduce dependencies beyond what is pinned in the spec without explicit approval.
- **Do not** add YAML-based config; this project is JSON-first for schema tooling.
- **Do not** couple to SDV internals beyond what SMAPI's public APIs + Harmony expose. If a patch requires reflecting into private fields, document why in the patch comment.
- **Do not** invent scenarios or test cases that aren't grounded in real mod development pain points. Ask first.

## Running & testing

See @docs/developer-setup.md for environment setup (Arch Linux primary, Windows secondary).

- Build: `dotnet build sdv-test-framework.slnx`
- Runner unit tests: `dotnet test tests/Runner.Tests/`
- Harness integration tests: `./scripts/run-integration-tests.sh` (launches SDV headlessly via Xvfb)
- Full test: `./scripts/ci.sh` (mirrors CI exactly; uses the solution)

**Adding a new project:** `dotnet sln sdv-test-framework.slnx add <path-to-csproj>`. The
solution is the source of truth for what `ci.sh` builds and tests — without this step a
new project's tests will silently not run.

## Style

- C# 12, nullable reference types enabled, `TreatWarningsAsErrors=true`
- `var` for obvious types, explicit type when the RHS isn't self-documenting
- XML doc comments on all public APIs
- No abbreviations in identifier names except for `Rpc`, `Sdv`, `Cp` (Content Patcher), `Io`
- Formatters handle whitespace — don't write rules about spacing

## When stuck

- Check @docs/spikes/ for prior investigation on similar problems
- Run `/superpowers:brainstorm` to structure the problem
- If a design question, propose 2-3 alternatives with tradeoffs before implementing
- If blocked on SDV internals: document the unknown in `docs/open-questions.md` and proceed with a best-guess interface that can be swapped later
