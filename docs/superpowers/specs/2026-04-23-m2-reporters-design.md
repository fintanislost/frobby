# M2 — TAP + JUnit Reporters Design

**Milestone:** M2 subproject 2 (per spec §7 Phase 2 decomposition)
**Date:** 2026-04-23
**Author:** fintan + Claude (brainstorming session)
**Status:** Approved — ready for implementation-plan drafting

## Goal

Add `sdv-test run --reporter <console|tap|junit> [--output PATH]` so the framework's scenario results can be consumed by real CI systems (GitHub Actions, GitLab, Jenkins). The existing Playwright-style stdout summary becomes one of three reporters; TAP 13 and Jenkins-compatible JUnit XML are the two new formats.

## Architecture

Refactor the inline console output currently in `RunCommand.cs` into an `IReporter` interface with three implementations. `RunCommand` parses the new flags, collects the existing `List<ScenarioReport>`, and dispatches to the chosen reporter at the end of the run. No scenario-execution logic changes — reporters are pure output adapters.

Scenario = testcase is the granularity across all reporters. Each `ScenarioReport` maps to one TAP `ok`/`not ok` line or one JUnit `<testcase>`. Per-assertion nesting (e.g. every `Failures` entry becoming its own testcase) would require preserving scenario hierarchy in XML and renders awkwardly in CI UIs; matches Playwright/Jest conventions.

## Components

**New files (`src/Runner/Reporters/`):**
- `IReporter.cs` — `void Report(IReadOnlyList<ScenarioReport> reports, TextWriter output)`. One method, synchronous.
- `ConsoleReporter.cs` — Moves the existing stdout formatting out of `RunCommand`. Matches the current output shape byte-for-byte so the default behavior is unchanged.
- `TapReporter.cs` — Emits TAP 13: `TAP version 13`, `1..N` plan, then `ok|not ok <n> - <name>` per scenario. Failures get a YAML diagnostic block with `message`, `failures` (list), `duration_ms`.
- `JunitReporter.cs` — Emits Jenkins-compatible JUnit XML: one `<testsuites>` containing one `<testsuite>` with all scenarios as `<testcase>` elements. Failures render as `<failure type="assertion" message="<first-failure>">` with the full joined `Failures` list in the body.
- `ReporterFactory.cs` — `IReporter Create(string name)` — maps `"console"` / `"tap"` / `"junit"` to instances; throws `ArgumentException` on unknown.

**Modified files:**
- `src/Runner/Commands/RunCommand.cs` — parse `--reporter` + `--output`; after the scenario loop completes, dispatch the collected reports to the selected reporter. Delete the inline console loop (now in `ConsoleReporter`).
- `src/Runner/Program.cs` — update `PrintHelp()` to document the new flags.

**New tests (`tests/Runner.Tests/`):**
- `ConsoleReporterTests.cs` — verifies the refactored output matches the existing shape.
- `TapReporterTests.cs` — TAP 13 format: all-pass, mixed, empty, with-failures-yaml.
- `JunitReporterTests.cs` — XML schema (testsuites/testsuite/testcase), failure body, time attribute, empty run.
- `RunCommandReporterFlagTests.cs` — `--reporter <x>` + `--output <y>` parsing, unknown reporter error, stdout vs file routing.

## CLI surface

```
sdv-test run [--filter <pattern>] [--mods-path <path>] [--reporter console|tap|junit] [--output <path>] [paths...]
```

- `--reporter` — optional; defaults to `console`. Accepts `console`, `tap`, or `junit` (case-insensitive).
- `--output` — optional; defaults to stdout. When set, the reporter writes to the file path instead. The user is responsible for the path being writable.

Exit codes unchanged: 0 = all scenarios passed, 1 = at least one failed, 2 = argument error (including unknown reporter name), 3 = launch/runtime fatal.

## Wire shapes

### TAP 13 output (5 scenarios, 1 fails)

```
TAP version 13
1..5
ok 1 - state_time_after_load
ok 2 - state_player_inventory_index
not ok 3 - draw_contains_patched_cursor
  ---
  message: "the specific cursor tile patched by the sample mod should render"
  duration_ms: 454
  failures:
    - "draw.contains: the specific cursor tile patched by the sample mod should render"
  ...
ok 4 - draw_not_contains_unused_asset
ok 5 - player_warp_updates_location
```

### JUnit XML output (Jenkins-compatible, same scenarios)

```xml
<?xml version="1.0" encoding="utf-8"?>
<testsuites tests="5" failures="1" errors="0" time="5.872">
  <testsuite name="sdv-test" tests="5" failures="1" errors="0" time="5.872" timestamp="2026-04-23T12:00:00Z">
    <testcase classname="tests/samples/01-state-time-after-load.test.json" name="state_time_after_load" time="0.443"/>
    <testcase classname="tests/samples/02-state-player-inventory-index.test.json" name="state_player_inventory_index" time="0.368"/>
    <testcase classname="tests/samples/04-draw-contains-patched-cursor.test.json" name="draw_contains_patched_cursor" time="0.454">
      <failure type="assertion" message="draw.contains: the specific cursor tile patched by the sample mod should render">draw.contains: the specific cursor tile patched by the sample mod should render</failure>
    </testcase>
    <testcase classname="tests/samples/05-draw-not-contains-unused-asset.test.json" name="draw_not_contains_unused_asset" time="2.696"/>
    <testcase classname="tests/samples/07-player-warp-updates-location.test.json" name="player_warp_updates_location" time="0.512"/>
  </testsuite>
</testsuites>
```

- `time` is total seconds (rounded to milliseconds), not milliseconds.
- `classname` is the scenario file's repo-relative path — matches Jenkins' convention of "classname = file path".
- `timestamp` is ISO 8601 UTC at run start.
- Multiple failures in a scenario's `Failures` list are joined with `\n` in the `<failure>` body; `message` takes the first entry (most CI UIs display only `message`).

### Console output (unchanged)

Preserved byte-for-byte from current `RunCommand`:

```
  PASS state_time_after_load (443ms) — tests/samples/01-state-time-after-load.test.json
  FAIL draw_contains_patched_cursor (454ms) — tests/samples/04-draw-contains-patched-cursor.test.json
        draw.contains: the specific cursor tile patched by the sample mod should render

[run] 4/5 passed
```

## Error handling

- **Unknown reporter** → exit 2 with `unknown reporter: <x> (known: console, tap, junit)`.
- **`--output` path unwritable** (directory missing, permission denied, etc.) → exit 3 with the IO exception message.
- **Empty scenario set** — reporter still runs; TAP emits `1..0`, JUnit emits `<testsuites tests="0">`, console prints nothing (matches today's behavior).
- **Scenario execution failures** — already land in `ScenarioReport.Failures`; reporters render them. No reporter-side retry or redaction.

## Testing

**~8 new unit tests (all passing, no skipped):**
- `ConsoleReporterTests`: 1 test (output matches current shape byte-for-byte).
- `TapReporterTests`: 3 tests (all-pass, mixed with failure, empty).
- `JunitReporterTests`: 3 tests (all-pass, mixed with failure, empty).
- `RunCommandReporterFlagTests`: 3 tests (default is console, `--reporter tap` dispatches correctly, unknown reporter → exit 2).

**Target test count after this subproject:** ~237 Passed + 31 Skipped (was 229+31).

## Acceptance criteria

1. `./scripts/ci.sh` green with ~8 new unit tests.
2. `sdv-test run --reporter tap tests/samples/` → valid TAP 13 on stdout; all 10 scenarios emitted as `ok 1..10`.
3. `sdv-test run --reporter junit --output /tmp/x.xml tests/samples/` → `/tmp/x.xml` validates against Jenkins JUnit schema (testsuites root; tests/failures/errors/time attrs; one testsuite; one testcase per scenario with classname+name+time).
4. `sdv-test run tests/samples/` (no reporter flag) — output byte-for-byte identical to the pre-refactor behavior.
5. `./scripts/run-samples.sh` → 10/10 passed (the script uses default reporter).
6. `docs/milestones/current.md` gets an M2-reporters completion subsection.

## Out of scope for this subproject

TODOs for later work:

- **GitLab's native `test-results` format** — similar XML but different schema. Defer; GitLab also accepts JUnit.
- **Coloured console output** — current plain text matches existing style; defer to a later polish pass.
- **Incremental/streaming output** — reporter emits per-scenario as they run rather than all-at-end. Adds state-management complexity for little gain at M2 scale.
- **Multiple reporters at once** — `--reporter console --reporter junit`. Users can run twice with different flags if they really need both.
- **HTML reporter** (browser-viewable report) — spec §4.7 mentions "console (default, Playwright-style), TAP, JUnit XML"; HTML isn't listed. Could land in M3+.
- **Filename sanitization for `--output`** — user is responsible for writable/sane paths; don't auto-create parent directories silently.

## Links

- Spec: `docs/spec.md` §4.7 Test Runner CLI, §6 CI integration
- TAP 13 spec: https://testanything.org/tap-version-13-specification.html
- Jenkins JUnit schema: https://llg.cubic.org/docs/junit/
- M2 tracker: `docs/milestones/current.md` §M2 — Production polish
