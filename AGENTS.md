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
