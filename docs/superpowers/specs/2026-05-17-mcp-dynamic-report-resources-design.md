# MCP Dynamic Report Resources Design

## Problem

The MCP server can run scenarios and exposes static docs/resources, but an agent still has
to scrape tool-call text to find the latest report information. That makes the debug loop
fragile after context compaction and prevents clients from discovering report artifacts
through the same MCP resource surface they use for docs.

## Goals

- Advertise stable, mod-neutral latest-report resource URIs.
- Let `run_scenario` record the latest MCP run summary inside the server process.
- Read static CLI report artifacts when the latest report directory contains them.
- Return clear MCP errors for no-report-yet, missing files, or malformed JSON.
- Keep this generic: no Starberg, SVE, or repo-specific assumptions.

## Resource URIs

- `frobby://reports/latest/summary`
  - `application/json`
  - Returns the latest in-memory MCP `run_scenario` summary when present.
  - Falls back to `<latest-report-dir>/summary.json` for CLI report directories.
- `frobby://reports/latest/index`
  - `text/html`
  - Returns `<latest-report-dir>/index.html` when present.
- `frobby://reports/latest/scenarios`
  - `text/markdown`
  - Summarizes scenarios from `summary.json` when available and links to
    `scenarios/<name>/report.html` when those pages exist.
  - Falls back to scanning `<latest-report-dir>/scenarios/*/report.html`.

## Non-goals

- Resource subscriptions/templates.
- Cross-process latest-report persistence.
- Generating CLI-quality HTML from MCP `run_scenario`; the MCP run path remains a
  lightweight scenario executor with a JSON summary.

## Acceptance

- `resources/list` includes all three report resource descriptors.
- Reading latest report resources before any report is recorded returns `InvalidParams`.
- A successful `run_scenario` records enough state for
  `frobby://reports/latest/summary` to work in the same MCP process.
- File-backed report reads validate JSON/paths and fail with meaningful messages.
- Runner.Mcp tests, solution build, and diff whitespace checks pass.
