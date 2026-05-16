# Frobby Wiki And Agent Documentation Design

## Goal

Make Frobby easier for both agents and mod developers to navigate by adding a
searchable documentation hub and making `AGENTS.md` the canonical root agent
constitution. Future feature slices must include a documentation follow-up check
before they are considered complete.

## Problem

Frobby already has useful documentation, but it is split across `README.md`,
quickstarts, RPC reference docs, milestone notes, and capability backlog files. That
works for someone who already knows where to look, but it is weaker for two
important users:

- Agents trying to find examples, rules, and current workflows quickly.
- Mod developers trying to add Frobby to a repo and write their first useful
  scenarios without reading the whole project history.

The root agent file is also still `CLAUDE.md`. The project should use
`AGENTS.md` as the portable, agent-neutral entrypoint, while keeping compatibility
for tools that still read `CLAUDE.md`.

## Design

### `AGENTS.md` Becomes Canonical

Add a root `AGENTS.md` containing the current project constitution, updated for
the current Frobby name and workflow. It should link to the new wiki hub, the
roadmap, RPC schema, and repo scaffold docs.

Keep `CLAUDE.md` as a short compatibility pointer to `AGENTS.md` rather than
duplicating rules in two root files. This avoids drift while preserving older
tooling behavior.

### Wiki Hub

Add `docs/wiki/index.md` as the human-and-agent landing page. It should be
organized around tasks rather than project history:

- Start here.
- Add Frobby to a mod repo.
- Write JSON scenarios.
- Use profiles, dependencies, and fixtures.
- Test UI with clicks, hovers, text bounds, and screenshots.
- Test world/content behavior.
- Read reports and debug failures.
- Use MCP and agent workflows.
- Troubleshoot common issues.

The hub should link to existing docs instead of duplicating long reference text.
Where current docs are missing a topic, the hub should include a short complete
summary and point to the best available source.

### Examples Index

Add `docs/wiki/examples.md` as a curated index of real scenario patterns. It
should link to examples from SVE and Starberg by category when the paths are
available locally:

- Repo profiles and external mod dependencies.
- Alternate farm fixture overrides.
- Click-first UI testing.
- Text-fit and screenshot report coverage.
- Runtime map/content assertions.
- Events, festivals, NPCs, shops, combat, fishing, and save/reload flows.

The examples index should not vendor another repo's scenario contents. It should
describe the pattern and point to paths that agents can inspect when the sibling
repos are present.

### Documentation Completion Rule

Add an explicit rule to `AGENTS.md`:

No slice, feature, or bugfix is complete until docs are checked. The implementer
must either update the relevant docs in the same slice or state why no docs
change was needed in the final status. Capability additions should usually touch
at least one of:

- `docs/wiki/index.md`
- `docs/wiki/examples.md`
- `README.md`
- `docs/rpc-schema.md`
- package-facing docs under `nuget/`
- capability/history notes such as `SVE_FROBBY_CAPABILITY_TODO.md`

### README Role

Keep `README.md` as the public quickstart and high-level overview. Add a clear
pointer from the README to `docs/wiki/index.md` for deeper task-oriented docs.
The README should not become the full wiki.

## Non-Goals

- No generated static site yet.
- No migration of every existing doc into the wiki in this slice.
- No broad rewrite of `docs/rpc-schema.md`.
- No automated documentation linter in this first pass.

## Validation

The slice is complete when:

- `AGENTS.md` is the root canonical agent entrypoint.
- `CLAUDE.md` points to `AGENTS.md` and does not duplicate the constitution.
- `docs/wiki/index.md` exists and links current documentation by task.
- `docs/wiki/examples.md` exists and links real SVE/Starberg/Frobby example
  locations without copying scenario bodies.
- `README.md` points users to the wiki hub.
- The documentation completion rule is explicit in `AGENTS.md`.
- Markdown files do not leave unfinished marker text in the new wiki pages.
