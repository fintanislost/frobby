# HTML Run Reports — Design

**Milestone:** Roadmap Tier 1 (promoted from Tier 2 candidate)
**Date:** 2026-04-24
**Author:** fintan + Claude (brainstorming session, auto-mode)
**Status:** Approved — ready for implementation-plan drafting

## Goal

Per-run directory containing an HTML report + screenshots that demonstrates **what
the test did and what it saw**. Both human modders and LLM agents (Claude Code via
MCP) consume the artifacts:

- **Humans** open `index.html` in a browser → green/red dashboard + per-scenario
  drill-downs with embedded screenshots.
- **LLMs** read `summary.json` (machine-equivalent of the HTML) + reference screenshot
  paths. When a test fails, Claude has both the assertion message AND the framebuffer
  the assertion saw, so it can reason about the failure visually.

The Playwright trace-viewer analog. Promoted to Tier 1 because evidence visibility is
core to the LLM-workflow north-star — without it, Claude has only error strings to
work with, no visual context.

## Architecture

**Per-run directory structure:**

```
test-results/
  2026-04-24T15-30-45-abc123/         ← run-id (ISO + short hash)
    index.html                         ← landing page: all scenarios + summary
    summary.json                       ← machine-readable for LLMs
    scenarios/
      11-bitmap-basic/
        report.html                    ← per-scenario detail
        steps.json                     ← step + assertion timing/outcomes
        screenshots/
          step-01-after-freeze.png    ← auto-capture
          step-03-explicit-named.png  ← user's Screenshot.Capture("explicit_named")
          assertion-fail.png          ← only on failure
        diffs/                         ← bitmap-assert diffs (Tier 3 followup populates this)
          bitmap_0.diff.png
      shop_menu_custom/
        report.html
        ...
    assets/
      styles.css                       ← single embedded stylesheet
```

**Run-id format:** `<ISO-timestamp-with-hyphens>-<6char-hash>`. Example:
`2026-04-24T15-30-45-abc123`. Colons replaced by hyphens for cross-platform
filesystem safety. Hash from `Guid.NewGuid().ToString("N")[..6]`.

**Output location:** `./test-results/<run-id>/` (project-relative). Configurable via
`--report-dir <path>` for `sdv-test run`. For `dotnet test` via the DSL, the
collection fixture chooses the location (defaults to `./test-results/<run-id>/`
relative to the current working dir). For MCP `run_scenario`, the tool accepts a
`report_dir` argument; if absent, defaults to `./test-results/<run-id>/`. The
returned tool result includes the absolute path so Claude can navigate to artifacts.

**Screenshot triggers:**

1. **Auto on `freeze.begin` success** — most scenarios enter FREEZE for assertions.
   `ScenarioRunner` (CLI path) and the DSL's session machinery both detect successful
   `freeze.begin` and call `bitmap.capture` + copy the result to the report dir as
   `step-NN-after-freeze.png`. Cheap (existing RPC); no new harness work.

2. **Auto on assertion failure** — `ScenarioRunner.EvaluateAssertionAsync` (tuple
   return: pass + detail) — on `passed: false`, capture before returning. File:
   `assertion-fail-NN.png`.

3. **Explicit user-driven** — new step type `screenshot.capture` with
   `args = {name: string}`. DSL method `Screenshot.Capture(name)` mirrors. Files
   named after the `name` argument.

**Capture mechanism:** Runner-side orchestration only. Harness's `bitmap.capture`
RPC is unchanged. After the RPC returns the path to a temp PNG (already at
`~/.cache/sdv-test-framework/captures/<scenario>/bitmap_N.png`), the runner copies
the bytes into `<report-dir>/scenarios/<scenario>/screenshots/<name>.png`. Source
file may be left in cache or cleaned — design decision: leave for now (Tier 4
followup: capture-cache cleanup already on roadmap).

**HTML rendering:** Pure C# string-templating. No JS framework. Single inline CSS
file (`assets/styles.css`) referenced from each HTML. Pages render fast, work
offline, survive being downloaded as a CI artifact and unzipped anywhere.

**Why no JS framework:** keeps the runner dependency-free, makes the report
portable (just static files), and lets browsers + LLM HTML parsers read the
content immediately. If interactivity becomes important (timeline scrubbing,
filter-by-status), a Tier 4 followup adds vanilla JS or a library.

## Components

**New files (Runner):**

- `src/Runner/Reports/RunDirectory.cs` — pure type. `Create(string baseDir, string? runId = null) → RunDirectory` (creates subdirs, returns wrapper). Properties: `Root`, `RunId`, `ScenariosDir`, `AssetsDir`. Method: `ScenarioDir(name)` → per-scenario subdir.
- `src/Runner/Reports/ScreenshotRecorder.cs` — `CaptureAsync(JsonRpcSession, string scenarioName, string filename) → Task<string>`. Calls `bitmap.capture` over RPC, copies to scenario's `screenshots/`, returns relative path.
- `src/Runner/Reports/HtmlReportGenerator.cs` — pure-function generator. Takes a `RunSummary` POCO + the run-dir path, writes `index.html`, `summary.json`, per-scenario `report.html` + `steps.json`. Embedded CSS template constant at the top of the file.
- `src/Runner/Reports/RunSummary.cs` — DTO record. `(string RunId, DateTime Started, TimeSpan Duration, IReadOnlyList<ScenarioOutcome> Scenarios)` + nested `ScenarioOutcome(string Name, string Path, bool Passed, IReadOnlyList<StepOutcome> Steps, IReadOnlyList<AssertionOutcome> Assertions, IReadOnlyList<string> Screenshots, int DurationMs)` + `StepOutcome` + `AssertionOutcome`.

**Modified files (Runner):**

- `src/Runner/Commands/RunCommand.cs` — parse `--report-dir`. Pre-create `RunDirectory`. Pass to `ScenarioRunner`. After all scenarios, call `HtmlReportGenerator.Generate(...)`.
- `src/Runner/Scenarios/ScenarioRunner.cs` — accept `RunDirectory?` via constructor (null = no reporting, backward-compat). On `freeze.begin` success in step loop, call `ScreenshotRecorder.CaptureAsync(...)`. On assertion failure, capture before returning. New step case `screenshot.capture` → call recorder.
- `src/Runner/Scenarios/ScenarioReport.cs` — extend with `Screenshots: List<string>` + `Steps: List<StepOutcome>`.

**Modified files (Protocol):**

- `src/Protocol/Models/ScenarioStep.cs` — already has `Action` + `Args`; no change needed (new `screenshot.capture` action handled by ScenarioRunner's switch).
- `schemas/scenario.schema.json` — add `screenshot.capture` to recognized step actions if the schema enumerates them (probably not — it accepts any string).

**New files (Runner.Dsl):**

- `src/Runner.Dsl/Screenshot.cs` — `Screenshot.Capture(name, ct?)`. Calls `bitmap.capture` via session, copies bytes into `SdvTestSession.Current.ReportDir`.

**Modified files (Runner.Dsl):**

- `src/Runner.Dsl/SdvTestSession.cs` — add `ReportDir: RunDirectory?` property (settable by `SdvFixture`).
- `src/Runner.Dsl/SdvFixture.cs` — at `InitializeAsync`, create a run-dir under `./test-results/<run-id>/` and assign to `SdvTestSession.Current.ReportDir`. At `DisposeAsync`, write the report HTML.

**Modified files (Runner.Mcp):**

- `src/Runner.Mcp/Tools/RunScenarioTool.cs` — accept optional `report_dir` argument. Default to `./test-results/<run-id>/` if absent. Return `report_dir` (absolute path) in the tool result so Claude can navigate the artifacts.

**New tests:**

- `tests/Runner.Tests/Reports/RunDirectoryTests.cs` — 2 tests (creates expected subdirs; run-id format).
- `tests/Runner.Tests/Reports/HtmlReportGeneratorTests.cs` — 4 tests (empty run produces valid HTML; passing scenarios show green; failed scenarios show red + assertion message; summary.json round-trips RunSummary shape).
- `tests/Runner.Tests/Reports/ScreenshotRecorderTests.cs` — 2 tests (CaptureAsync forwards to bitmap.capture + copies to expected path; missing capture returns error string instead of throwing).
- `tests/Runner.Dsl.Tests/Facets/ScreenshotTests.cs` — 1 test (Screenshot.Capture invokes bitmap.capture with right shape).
- `tests/Runner.Mcp.Tests/Tools/RunScenarioReportDirTests.cs` — 1 test (report_dir argument plumbed through; result contains report_dir path).
- `tests/Runner.Tests/Reports/RunReportIntegrationTests.cs` — 1 skipped placeholder.

**Target test count:** 347+45 → ~358+46 (+11 passing, +1 skipped).

## Wire / file shapes

### `RunSummary` (in summary.json)

```json
{
  "run_id": "2026-04-24T15-30-45-abc123",
  "started": "2026-04-24T15:30:45Z",
  "duration_ms": 14523,
  "scenarios": [
    {
      "name": "bitmap_shop_menu_basic",
      "path": "tests/samples/11-bitmap-basic.test.json",
      "passed": true,
      "duration_ms": 1854,
      "steps": [
        { "action": "draw.arm", "passed": true, "duration_ms": 12 },
        { "action": "wait.ms", "passed": true, "duration_ms": 502 },
        { "action": "freeze.begin", "passed": true, "duration_ms": 38 }
      ],
      "assertions": [
        { "type": "bitmap", "passed": true, "detail": null }
      ],
      "screenshots": [
        "scenarios/bitmap_shop_menu_basic/screenshots/step-03-after-freeze.png"
      ]
    }
  ]
}
```

### Tool result for MCP `run_scenario`

```json
{
  "passed": true,
  "assertions_run": 3,
  "assertions_passed": 3,
  "duration_ms": 142,
  "report_dir": "/abs/path/to/test-results/2026-04-24T15-30-45-abc123/",
  "report_index": "/abs/path/to/test-results/.../index.html"
}
```

LLM can then call `rpc_call` with `method: "scenarios.read_summary"` (M4 — for now,
Claude reads `summary.json` directly via filesystem if MCP is in the same process)
or simply reference the report dir in its narration to the user.

### CLI flag

```
sdv-test run [--report-dir <path>] [--no-report] [paths...]
```

`--no-report` disables HTML generation (back-compat for users who don't want the
overhead). `--report-dir` defaults to `./test-results/<run-id>/`. Run-id is auto-
generated unless explicit `<run-id>` is the directory name (treated as resuming a
named run — use case: regression diff tooling).

### DSL method

```csharp
await Screenshot.Capture("after_warp_to_seedshop");
```

Captures the current framebuffer + saves as
`<report-dir>/scenarios/<scenario-name>/screenshots/after_warp_to_seedshop.png`.
No-ops with a one-line warning if `SdvTestSession.Current.ReportDir` is null
(running without a report dir, e.g. unit tests with the shim).

### Step type (JSON scenarios)

```json
{ "action": "screenshot.capture", "args": { "name": "after_warp_to_seedshop" } }
```

## Error handling

- **Run dir already exists** (rare; user passed an explicit `--report-dir` that
  collides) — error out with a clear message before launching SDV. User can append a
  suffix.
- **Disk full on screenshot write** — log warning, continue scenario. Report HTML
  still generates with a "screenshot write failed" annotation.
- **`bitmap.capture` RPC fails** (no scenario active, not in FREEZE) — silent for
  auto-captures (don't break the test); log warning. Explicit
  `screenshot.capture` step propagates the error to the user as a step failure.
- **HTML generation throws** (template bug) — log error path; the run still
  succeeds + the artifacts directory has the partial JSON. User can re-run with
  `--no-report` if HTML generation breaks.
- **`Screenshot.Capture` called outside a scenario** (no active session) — throws
  `InvalidOperationException` with a clear message ("Screenshot.Capture requires
  an active scenario").
- **Permission failure on test-results/ creation** — error out before SDV launch.

## Testing

**Unit tests (~11 passing, 1 skipped):**

- `RunDirectoryTests` — `Create_ProducesExpectedSubdirs`, `RunId_FormatMatchesRegex`.
- `HtmlReportGeneratorTests` — `EmptyRun_ProducesValidHtml`,
  `PassingScenario_RendersGreen`, `FailedScenario_RendersRedWithAssertionDetail`,
  `SummaryJson_DeserializesBackToRunSummary`.
- `ScreenshotRecorderTests` — `CaptureAsync_CopiesBitmapToScenarioScreenshots`,
  `CaptureAsync_RpcFailure_ReturnsErrorWithoutThrowing`.
- `Runner.Dsl.Tests/Facets/ScreenshotTests.cs` — `Capture_InvokesBitmapCaptureWithName`.
- `Runner.Mcp.Tests/Tools/RunScenarioReportDirTests` —
  `RunScenario_ReturnsReportDirPath` (uses shim SdvLifecycle that tracks the
  report-dir argument flowing through).
- `RunReportIntegrationTests` — 1 skipped (verifies via T6 manual smoke).

**Manual smoke (T6):**

1. `./scripts/run-samples.sh` — pre-existing 11 scenarios run end-to-end.
2. Inspect `./test-results/<latest>/`:
   - `index.html` opens in browser, shows 11/11 green.
   - Each scenario has its own `report.html`.
   - At least the bitmap scenario has a `screenshots/step-NN-after-freeze.png`.
   - `summary.json` parses cleanly.
3. Tamper a scenario to fail (edit assertion to be impossible). Re-run.
   - HTML shows the failed scenario in red.
   - `assertion-fail.png` exists in screenshots/.
   - Failure message visible in HTML + summary.json.

## Acceptance criteria

1. `./scripts/ci.sh` green at ~358 Passed + 46 Skipped.
2. `sdv-test run` produces a `./test-results/<run-id>/` directory by default.
3. `index.html` opens in a browser, shows scenario outcomes with screenshots inline
   (or as visible thumbnail/link).
4. `summary.json` is valid JSON matching the documented shape.
5. Auto-screenshots fire at `freeze.begin` and on assertion failure (verified by
   T6 smoke against the existing 11-scenario sample suite).
6. `dotnet test` against a project using the DSL produces a report directory via
   `SdvFixture`.
7. `Screenshot.Capture(name)` writes a named screenshot via the DSL.
8. MCP `run_scenario` tool returns `report_dir` + `report_index` in its result so
   Claude can navigate to artifacts.
9. `./scripts/run-samples.sh` still 11/11 PASS — no regression from the
   ScenarioRunner changes.
10. `--no-report` flag disables HTML generation (backward-compat).
11. `docs/roadmap.md`: HTML run reports moved from Tier 1 to Completed.
12. `docs/milestones/current.md` gains a completion subsection with screenshot
    evidence.

## Out of scope (Tier 3/4 followups)

- **Diff-image-on-failure** for bitmap assertions (already on Tier 3 — pairs
  naturally with this work; the report-dir gives diffs a natural home).
- **Interactive HTML** (timeline scrubbing, filter-by-status, JS framework). Tier 4.
- **Run pruning** — `sdv-test runs prune --older-than 7d`. Tier 4.
- **Diff between two runs** — "what changed since yesterday's run." Tier 4.
- **Inline thumbnails** vs full-resolution screenshots — render thumbnails inline,
  full-size on click. Tier 4 polish.
- **Compress screenshots to JPEG/WebP** for storage efficiency. Tier 4.
- **Server-side viewer** — `sdv-test serve <run-dir>` opens a localhost server with
  a richer UI. Tier 4.
- **Near-miss data on failed `draw.contains`** ("you asked for X, closest matches
  were Y") — distinct feature. Tier 3.

## Links

- Roadmap: `docs/roadmap.md` (was Tier 2 candidate, promoted to Tier 1 during
  this brainstorm).
- Inspired by Playwright's trace viewer.
