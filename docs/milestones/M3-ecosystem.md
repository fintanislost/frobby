# Milestone 3 — Ecosystem

**Prerequisite:** M2 passes, framework has external users (even just 1-2).

**Goal:** Framework graduates from "solo project" to "community tool." MCP server makes Claude Code-driven test authoring work.

**Duration:** Ongoing / open-ended.

**Exit criteria:** None — this is where the project lives once stable.

## Deliverables

### D3.1 — MCP server

- Separate repo / subdirectory: `mcp-server/`
- Wraps the runner's RPC interface with MCP-shaped tools
- Curated high-level tools (not full RPC exposure):
  - `author_scenario(description)` — LLM drafts a scenario from natural language
  - `run_scenario(path)` — execute and return structured results
  - `explain_failure(scenario, failure)` — LLM-driven diagnosis
  - `list_draw_calls(filter)` — inspection for iterative authoring
  - `capture_baseline(scenario)` — regenerate bitmap baseline
- Ships as an npm package + Python package (both thin wrappers)

### D3.2 — C# fluent DSL

- NuGet package: `[tool].Testing`
- Attribute-based scenarios (see spec Appendix A)
- Compiles to JSON-RPC calls via source generators (avoid reflection at runtime)
- `dotnet test` integration so scenarios run alongside unit tests

### D3.3 — Documentation site upgrade

- SvelteKit-based (leveraging author's stack)
- API reference, scenario cookbook, troubleshooting
- Hosted interactive examples (WASM-based SDV simulation is unrealistic, but annotated scenario gallery is)

### D3.4 — Community example suites

- Partner with 3-5 popular community mod maintainers
- Ship test suites as examples (with maintainer permission/attribution)
- Use as regression tests for the framework itself

### D3.5 — Plugin marketplace presence

- Submit to `claude-plugins-official` as a dev-workflow plugin
- Skill: `sdv-mod-testing` — auto-triggers when Claude Code detects a SMAPI mod project
- Command: `/test-mod` for one-shot scenario authoring

### D3.6 — Property-based extension (stretch)

- FsCheck integration for property-based scenarios
- Example: "for any valid player inventory, opening the shop menu never crashes"
- Deterministic seed propagation so failures are reproducible

## Risks and open questions

- **MCP scope creep.** Resist exposing every RPC method. Curated high-level tools serve LLMs better than raw protocol access.
- **Maintenance burden.** Community example suites can become liabilities when SDV updates. Policy: examples are best-effort, not guaranteed to stay green across SDV versions.
- **Naming.** Still TBD. Pick before M3.1 ships publicly. Options in spec §10.
- **Sponsorship/sustainability.** Tooling projects without funding churn. Consider: GitHub Sponsors, grant applications, Patreon. Not urgent, but worth thinking about before community grows.
