# M2 — Record Mode Design

**Milestone:** M2 subproject 4 (per spec §7 Phase 2 decomposition)
**Date:** 2026-04-24
**Author:** fintan + Claude (brainstorming session)
**Status:** Approved — ready for implementation-plan drafting

## Goal

Two complementary record flows land together as one subproject, covering the dev-loop workflows spec §4.7 calls "play-to-record":

1. **State-snapshot record** — user plays in-game, types `harness_record <name>` in the SMAPI console. Framework captures the current `state.*` as a scenario with state assertions but no steps. User gets a "reproduce-this-state" scenario to promote to their suite.

2. **RPC-trace record** — `sdv-test record <name>` subcommand. Launches SDV + harness, tracing decorator on the RPC dispatcher logs every mutating call (skips reads + lifecycle). On Ctrl-C, emits a scenario whose `steps` are those calls. User drives the game via external RPC (Python probes, MCP tools) and captures their scripted session as replayable steps.

Action-trace recording (input-event → RPC translation, the Playwright-codegen analog) is explicitly **deferred to M3** as its own subproject.

## Architecture

Two distinct flows, no shared code path — they live in different layers (harness console command vs Runner CLI) and can't share DTOs cleanly since Harness is .NET 6 and Runner is .NET 10. The duplicated JSON-emission code (~20 lines each side) is accepted as the cost of that boundary.

**Flow A (state-snapshot):** a `harness_record <name>` SMAPI console command runs on the game thread, reads `Game1.player`/`Game1.currentLocation`/`Game1.Date` directly, emits a `.test.json` file with a curated set of state assertions. No fixture auto-creation; user adds `fixture` field manually if desired.

**Flow B (RPC-trace):** a new `sdv-test record` subcommand reuses `RunCommand`'s SDV-launch boilerplate. Instead of running scenarios, it installs an `RpcTraceRecorder` that subscribes to the `JsonRpcSession`'s incoming-request stream, filters out reads + lifecycle calls, buffers mutators. On Ctrl-C, serializes the buffer to a `.test.json`.

## Components

**New files (Harness):**
- `src/Harness/Handlers/HarnessRecordConsole.cs` — SMAPI console command handler. Test-seamable via an `IFileSink` interface so unit tests don't write real files.

**New files (Runner):**
- `src/Runner/Commands/RecordCommand.cs` — CLI entry point, mirrors `RunCommand`'s SDV-launch flow.
- `src/Runner/Recording/RpcTraceRecorder.cs` — buffers filtered RPC calls, emits a scenario on demand.

**New tests:**
- `tests/Harness.Tests/HarnessRecordConsoleTests.cs` — 2 tests (valid name emits well-formed JSON; invalid name rejects + logs).
- `tests/Runner.Tests/RpcTraceRecorderTests.cs` — 3 tests (records mutator; skips `state.*` + `scenario.begin/end`; emits valid scenario JSON that `ScenarioLoader.Load` accepts).
- `tests/Runner.Tests/RecordCommandTests.cs` — 2 tests (missing name → exit 2; existing output without `--force` → exit 3).
- `tests/Runner.Tests/RecordModeIntegrationTests.cs` — 1 skipped integration placeholder.

**Modified:**
- `src/Harness/ModEntry.cs` — register the `harness_record` console command in `Entry`.
- `src/Runner/Program.cs` — dispatch `record` subcommand; `PrintHelp()` mentions it.
- `docs/rpc-schema.md` — note that `sdv-test record` exists and filters which methods it captures.
- `docs/milestones/current.md` — M2-record completion subsection.

If `JsonRpcSession.RequestReceived` event (or equivalent hook) isn't already present from M1 D1.2, a small addition goes in `src/Harness/Rpc/JsonRpcSession.cs` or `src/Protocol/` — plan-level detail. The tracing recorder needs read-only access to incoming requests; it does NOT modify dispatch.

**Target test count:** 246+32 → ~253 Passed + 33 Skipped (+7 passed, +1 skipped).

## CLI surface

### `sdv-test record <name> [--mods-path <path>] [--output <path>] [--force]`

- `<name>` — positional, required. The scenario name (drives output file name if `--output` omitted).
- `--mods-path` — same as `run`. Defaults to `~/.cache/sdv-test-framework/mods`.
- `--output` — optional path for the generated `.test.json`. Defaults to `tests/samples/<name>.test.json`.
- `--force` — overwrite an existing output file. Without it, exit 3 on collision.

Exit codes: 0 success; 2 argument error (missing name, unknown flag); 3 output collision without `--force`; 4 SDV launch / runtime fatal.

### `harness_record <name>` (SMAPI console)

- `<name>` — required. Must match `[A-Za-z0-9_-]+`. Invalid → log error, no file written.
- Output: `~/.cache/sdv-test-framework/records/<name>.test.json`. Directory auto-created. Existing file silently overwritten (warning logged).
- Usage: user plays to desired state, types command at SMAPI prompt, sees `[harness_record] wrote <abspath> (6 assertions)`.

## Wire shapes

### State-snapshot output (`harness_record spring_day_5_500g`)

```json
{
  "name": "spring_day_5_500g",
  "config": { "seed": 42 },
  "steps": [],
  "assertions": [
    { "type": "state", "expr": "state.time.in_save == true" },
    { "type": "state", "expr": "state.time.season == 'spring'" },
    { "type": "state", "expr": "state.time.day_of_month == 5" },
    { "type": "state", "expr": "state.time.year == 1" },
    { "type": "state", "expr": "state.location.name == 'FarmHouse'" },
    { "type": "state", "expr": "state.player.money == 500" }
  ]
}
```

- 6 assertions, fixed curated list.
- `config.seed` captured from `ScenarioState.Current.Seed` if a scenario is active; else defaults to `42` (matches sample suite convention).
- Empty `steps` array — user adds `fixture` field + steps by hand when promoting from `~/.cache/.../records/` to `tests/samples/`.

### RPC-trace output (`sdv-test record my_trace` + user makes 3 RPC calls)

```json
{
  "name": "my_trace",
  "config": { "seed": 42 },
  "steps": [
    { "action": "player.warp", "args": { "location": "Farm", "x": 64, "y": 15 } },
    { "action": "player.set_money", "args": { "amount": 500 } },
    { "action": "time.advance", "args": { "minutes": 120 } }
  ],
  "assertions": []
}
```

- `steps` in dispatch order.
- Empty `assertions` — user adds after (or mixes in a state-snapshot's assertions manually).
- `config.seed` defaults to `42` unless the user's RPC sequence included `scenario.begin` with a seed (which we filter, so seed doesn't flow through — user sets manually).

### Filter list (RPC-trace)

**Skipped (not captured as steps):**
- Any method starting with `state.` — reads have no replay value.
- `scenario.begin`, `scenario.end` — the recorded scenario has its own lifecycle.

**Captured:**
- All other RPC calls: `player.*`, `time.*`, `world.*`, `fixture.load`, `draw.arm`, `draw.disarm`, `draw.snapshot`, `draw.find`, `draw.assert_contains`, `draw.assert_not_contains`, `freeze.*`.

## Error handling

- **`harness_record` invalid name** — regex-check against `^[A-Za-z0-9_-]+$`. Non-matching → log `[harness_record] name must match [A-Za-z0-9_-]+`, no file written. Blocks directory traversal.
- **`harness_record` overwrite** — silently overwrites; logs `overwrote existing file` alongside the success line.
- **`harness_record` write failure** (permission, disk) — logs `[harness_record] write failed: <msg>`. Never throws; game continues.
- **`sdv-test record` output collision** — exit 3 with `error: <path> exists; pass --force to overwrite`.
- **`sdv-test record` no calls captured** — still writes a valid scenario (empty `steps`). Log `[record] no calls captured — wrote <path>`.
- **SDV crash mid-record** — outer try/finally catches; flushes buffer to output path before exiting with code 4. Preserves partial work.
- **SIGINT limitation** — same TTY/pipe quirk as watch mode. Works interactively, fails for background `dotnet run`. Documented in help text + spec Out-of-scope.

## Testing

**Unit tests (~7 new passing):**

- `HarnessRecordConsoleTests.ValidName_EmitsWellFormedJson` — shim `IFileSink` captures bytes; deserialize + validate against `schemas/scenario.schema.json` via `ScenarioLoader`.
- `HarnessRecordConsoleTests.InvalidName_LogsErrorAndWritesNothing` — `../bad` triggers the regex failure; shim sink's `Write` never called.
- `RpcTraceRecorderTests.RecordsMutator` — feed 3 synthetic `JsonRpcRequest`s; 2 captured (1 skipped for `state.player`).
- `RpcTraceRecorderTests.SkipsLifecycle` — `scenario.begin` + `scenario.end` both skipped.
- `RpcTraceRecorderTests.EmitsValidScenarioJson` — recorded list → JSON → `ScenarioLoader.Load` returns a valid `ScenarioSpec`.
- `RecordCommandTests.MissingName_ReturnsTwo` — `await RecordCommand.RunAsync(new[]{}.AsMemory(), ct)` → exit 2.
- `RecordCommandTests.ExistingOutputWithoutForce_ReturnsThree` — pre-create target, run without `--force`, exit 3.

**Skipped integration (1):**
- `RecordModeIntegrationTests.RecordMode_LiveSession_EmitsReplayableScenario` — exercised in T5 smoke: launch `sdv-test record` + drive via Python probe + Ctrl-C + replay via `sdv-test run`.

## Acceptance criteria

1. `./scripts/ci.sh` green with ~7 new unit tests + 1 skipped integration.
2. In-game: `harness_record my_state` produces `~/.cache/sdv-test-framework/records/my_state.test.json` that loads cleanly via `ScenarioLoader`.
3. `sdv-test record my_trace --mods-path <samples>` + Python probe making RPC calls + Ctrl-C produces `tests/samples/my_trace.test.json` with those calls as `steps`.
4. The recorded `my_trace.test.json` replays cleanly via `sdv-test run tests/samples/my_trace.test.json` — steps execute, 0 failures.
5. `./scripts/run-samples.sh` still 10/10 (no regression).
6. `docs/milestones/current.md` gets an M2-record subsection.
7. `src/Runner/Program.cs` PrintHelp documents both `record` (CLI subcommand) and `harness_record` (SMAPI console command).

## Out of scope (TODO for M3+)

- **Action-trace record** — input-event-to-RPC translation. Its own subproject in M3.
- **`--force` on `harness_record`** — silent overwrite + log warning sufficient for M2.
- **Recording draw-assertion synthesis** — RPC trace captures `draw.arm`/`draw.snapshot` but doesn't auto-generate the corresponding `draw.contains` assertion. User adds by hand.
- **Merged snapshot+trace in one session** — needs in-game signaling (special RPC or key bind) to trigger snapshot mid-record. Defer.
- **Auto-promote recorded scenario to `tests/samples/`** — user copies by hand from the records cache.
- **Recording `--output` for `harness_record`** — currently writes to a fixed cache dir; custom path support would need new arg parsing in the SMAPI console command.
- **Auto-fixture-creation on `harness_record`** — user runs `harness_save` / `fixture.save` separately if they want a fixture too.

## Links

- Spec: `docs/spec.md` §4.7 Test Runner CLI ("Record mode")
- M2 tracker: `docs/milestones/current.md` §M2 — Production polish
- Prior M2: fixture builder (2026-04-23-m2-fixture-builder.md), reporters (2026-04-23-m2-reporters.md), watch mode (2026-04-23-m2-watch-mode.md)
