# M2 — Watch Mode Design

**Milestone:** M2 subproject 3 (per spec §7 Phase 2 decomposition)
**Date:** 2026-04-23
**Author:** fintan + Claude (brainstorming session)
**Status:** Approved — ready for implementation-plan drafting

## Goal

Add `sdv-test run --watch` — stays resident after the initial scenario run, watches for `*.test.json` changes in the run's paths, and reruns all scenarios on change. Reuses the running SDV subprocess across reruns so the cold-boot cost (~15s) is paid once per session rather than once per edit.

## Architecture

A flag on the existing `run` command (not a separate `watch` command) so all the existing `--filter` / `--mods-path` / `--reporter` / `--output` parsing is inherited with zero duplication. After the initial run + reporter call, a new `WatchLoop` takes over: it holds the `JsonRpcSession` open, installs a `FileSystemWatcher` on the run's paths, and blocks on the first file-change event. On each change: re-discover scenarios (handles added/removed files), re-run them against the same SDV subprocess via the existing `ScenarioRunner`, re-invoke the reporter. Ctrl-C breaks the loop and falls through to the existing teardown `finally` that kills SDV.

Scenarios already use `scenario.begin` / `scenario.end` for isolation (harness-side `ScenarioState` resets between each), so reusing the process is safe. Fixtures stage once at startup.

## Components

**New files (`src/Runner/Watch/`):**
- `ScenarioWatcher.cs` — wraps `FileSystemWatcher` with a 300ms debounce timer. Public API: `ScenarioWatcher(IReadOnlyList<string> paths, Action onTriggered)` constructor + `IDisposable` for teardown. Internally: one `FileSystemWatcher` per path, filter `*.test.json`, `IncludeSubdirectories = true`. On any Created/Changed/Deleted/Renamed event, reset the debounce timer; when it fires, invoke `onTriggered` exactly once per coalesced burst. Test-seamable: expose a `TriggerForTests()` method so unit tests don't need real files/timing.
- `WatchLoop.cs` — resident orchestrator. `RunAsync(WatchContext ctx, CancellationToken ct)` where `WatchContext` bundles the live `JsonRpcSession`, the paths, the reporter, and a factory for fresh scenario lists. After each rerun, prints `[watch] waiting for changes in <paths>...`. Exits cleanly on `ct.IsCancellationRequested`.

**Modified files:**
- `src/Runner/Commands/RunCommand.cs` — parse `--watch` flag. Factor the "discover + stage + run scenarios + reporter" block into a helper `RunOnceAsync(...)` so `WatchLoop` can call it for each rerun without duplicating logic. When `--watch` is set: after the initial `RunOnceAsync` returns, call `WatchLoop.RunAsync(...)` instead of falling through to teardown. Ctrl-C reaches the outer `finally` which still kills SDV.
- `src/Runner/Program.cs` — `PrintHelp()` mentions `--watch`.

**New tests (`tests/Runner.Tests/`):**
- `ScenarioWatcherTests.cs` — 3 tests: file-write triggers callback after debounce; burst of writes within 100ms coalesces to one callback; `Dispose()` stops the watcher cleanly.
- `RunCommandWatchFlagTests.cs` — 1 test: `--watch` flag parsed; unknown `--watch=X` rejected. (No live SDV — just the parse path.)

**Skip-marked integration (1 new):**
- `WatchModeIntegrationTests.WatchMode_FileChange_TriggersRerun` — documents the live behavior; exercised manually during T5 smoke.

**CLI:** `sdv-test run --watch [existing flags] [paths...]`. `--watch` is a bare boolean flag; default is false (existing run-once behavior).

## Control flow under `--watch`

1. Parse args (existing) → resolve reporter + output writer (existing).
2. Launch SDV (existing) → wait for `ready` notification (existing).
3. Stage fixtures + initial `RunOnceAsync`: discover `*.test.json` under paths → run scenarios → invoke reporter.
4. If `!watch`: return exit code (existing).
5. If `watch`: print `[watch] waiting for changes in <paths>...` → install `ScenarioWatcher` → `await` either watcher triggers or Ctrl-C:
   - On watcher trigger: print `[watch] file(s) changed — rerunning` → call `RunOnceAsync` again → print `[watch] waiting for changes in <paths>...`.
   - On Ctrl-C: exit loop.
6. Outer `finally` block (existing) kills the SDV subprocess + disposes the file writer.

Exit code under watch: returns the last run's exit code (0 if all scenarios passed, 1 if any failed). If Ctrl-C happens before any rerun completes, the initial run's code is returned.

## Wire shapes

### Console output under `--watch`

```
  PASS state_time_after_load (443ms) — tests/samples/01-state-time-after-load.test.json
  ... 9 more lines ...

[run] 10/10 passed
[watch] waiting for changes in tests/samples/...

[watch] 01-state-time-after-load.test.json changed — rerunning

  PASS state_time_after_load (442ms) — tests/samples/01-state-time-after-load.test.json
  ... 9 more lines ...

[run] 10/10 passed
[watch] waiting for changes in tests/samples/...
```

### `--output x.xml` under `--watch`

Each rerun opens the output file with `FileMode.Create` (overwrites). The file always reflects the most recent run. Rationale: CI-ish consumers expect "latest = truth"; preserving history would require rotation logic that's overkill for a dev-loop feature.

Console reporter prints interleaved across reruns with `[watch]` banner separators.

## Debounce behavior

`ScenarioWatcher` uses a 300ms debounce: each watcher event resets the timer; the callback fires only when 300ms of quiet elapses. Covers:
- **VS Code saves** — write-rename pattern, typically settles within 50ms.
- **vim `:w`** — single write, no special handling needed.
- **Atomic writes** (tmp + rename) — coalesces the two events.

Debounce is a field on `ScenarioWatcher`; constructor takes an optional `TimeSpan debounce = default` so tests can pass `TimeSpan.FromMilliseconds(5)` for fast assertions.

## Error handling

- **Scenario load fails mid-rerun** (e.g. user saved half-written JSON) → print `[load-error] <file>: <msg>` to stderr, skip that scenario, continue watching. The rerun still runs the other scenarios. On next save, the file is re-tried. SDV subprocess stays up.
- **Scenario execution throws** (already handled inside `ScenarioRunner.RunAsync` as a failed report) → counted in reporter output; watch continues.
- **RPC session dies** (SDV crashed) → tear down + exit non-zero. Auto-reconnect is out of scope; user Ctrl-Cs and restarts.
- **`FileSystemWatcher` fails to initialize** (inotify limit exceeded on Linux, etc.) → print `[watch] can't install watcher: <msg> — falling back to run-once` and exit 0 after the initial run.
- **Output file can't be reopened on rerun** (path became a dir, permission revoked, etc.) → print error to stderr, reuse the old writer if possible; worst case, fall through to stdout for that rerun.

## Testing

**~4 new passing tests:**
- `ScenarioWatcherTests.Triggers_AfterDebounce` — write a `*.test.json` file, assert callback fires within `debounce + 100ms`.
- `ScenarioWatcherTests.CoalescesBurst_OneCallback` — 5 writes within 100ms, assert callback fires exactly once.
- `ScenarioWatcherTests.Dispose_StopsWatcher` — dispose mid-debounce, subsequent writes don't trigger.
- `RunCommandWatchFlagTests.WatchFlagParsed` — `--watch` is recognized and doesn't collide with existing flags.

**1 new skipped integration:**
- `WatchModeIntegrationTests.WatchMode_FileChange_TriggersRerun` — live-SDV exercise, skipped in CI, run manually during T5.

**Target test count:** 240+31 → ~244+32 (+4 passed, +1 skipped).

## Acceptance criteria

1. `./scripts/ci.sh` green with ~4 new passing tests + 1 new skipped integration.
2. `sdv-test run --watch tests/samples/` runs all 10 scenarios once (~15s), prints `[watch] waiting...`, stays resident.
3. `touch tests/samples/01-*.test.json` triggers a rerun within ~500ms; **no SDV relaunch** (process ID unchanged before/after).
4. Ctrl-C exits cleanly with the SDV subprocess killed.
5. `sdv-test run --watch --reporter junit --output /tmp/x.xml tests/samples/` overwrites `/tmp/x.xml` on each rerun.
6. `docs/milestones/current.md` gets an M2-watch completion subsection.

## Out of scope (TODO for later M2/M3)

- **Keyboard shortcuts** (Playwright-style `r`/`q`/`a`) — needs ANSI raw-mode input + redraw logic. Value is modest; Ctrl-C + save-file is enough for M2.
- **Granular rerun** (only the changed file's scenarios) — defer; `--filter` + rerun-all works for narrow cases and avoids subtle state-leak bugs.
- **Watching non-scenario files** (fixtures, mods, source code) — each requires SDV teardown/restart. Ctrl-C + re-invoke is the right UX. Documented in the watch-help output.
- **Auto-reconnect on SDV crash** — complex; not a dev-loop concern since SDV crashes rarely mid-session.
- **Scenario dependency graph** (e.g. "fixture X is used by scenarios [1,3,5], rerun only those on X's change") — requires metadata tracking; defer.
- **Clearing the terminal between reruns** — nice UX but platform-specific; keep output chronologically interleaved instead.

## Links

- Spec: `docs/spec.md` §4.7 Test Runner CLI
- M2 tracker: `docs/milestones/current.md` §M2 — Production polish
- Prior M2 subprojects: fixture builder (2026-04-23-m2-fixture-builder.md), reporters (2026-04-23-m2-reporters.md)
