# Action-Trace Recording — Design

**Milestone:** Roadmap Tier 1 (final item)
**Date:** 2026-04-24
**Author:** fintan + Claude (brainstorming session, auto-mode)
**Status:** Approved — ready for implementation-plan drafting

## Goal

Ship the **third record-mode flow** (after M2's state-snapshot + RPC-trace): capture
human input during play and translate it to a replayable `.test.json` scenario. Closes
the Playwright-codegen analog gap.

Workflow target: user plays their mod, hits a bug or interesting state, types
`harness_record_actions <name>` in SMAPI console, plays through the repro, types
`harness_record_stop`, and gets a scenario file to drop into Claude Code: "Reproduce
this bug, write a test for it" or "Look at this scenario, why is it failing on the
last assertion?"

This pairs with MCP's `run_scenario` (replays the trace) and `scaffold_scenario`
(extends it with assertions). Together they form the round-trip: play → trace →
edit → run.

## Architecture

**Coarse events, not key-by-key.** Hook SMAPI's high-level events (`Player.Warped`,
`Display.MenuChanged`, `GameLoop.TimeChanged`) instead of raw input events. Output
scenarios are readable — `[warp Farm, advance 30min, warp SeedShop, interact Pierre]`
— rather than `[press W 0.5s, press A 0.3s, ...]`. Coarse events map directly to the
existing RPC surface (`player.warp`, `time.advance`, `world.interact_npc`).

**Why coarse:**
- LLMs reason better about semantic steps than tick-perfect input streams.
- The replay path (the existing `ScenarioRunner`) already executes RPCs, not inputs.
  Recording at the RPC level is symmetric with replay.
- Tick-perfect replay would need a much larger investment (input simulation, frame
  alignment) that's out of scope for the LLM-workflow north-star.

**Translation strategy:**

| Event source | Recorder behavior |
|---|---|
| `Player.Warped` | Buffer `(location, x, y, timestamp)`. Multi-warp coalesce: only emit the latest warp within a 1-second window. |
| `Display.MenuChanged` to `DialogueBox` / `ShopMenu` | Look back ≤2s for the last warp; use that location's NPC list to guess the interaction target. Emit `world.interact_npc(name)`. |
| `GameLoop.TimeChanged` | Track elapsed in-game minutes since the last emitted RPC. On the next non-time event (or on stop), emit `time.advance(minutes)` if delta ≥10 minutes. Drops noise from per-tick time changes. |
| Other menu types (inventory, save, pause) | Ignored — not scenario-meaningful. |
| Stop | Flush any pending time-advance. |

**Scope boundary:** action-trace covers what the existing RPC surface can replay.
- Captured: warp, NPC interaction, time advance.
- Skipped (no replay path yet): inventory pickups (mining/foraging — too noisy and
  no `player.pickup_item` RPC), tool use (fishing/watering — no `player.use_tool`),
  combat. These are M4 polish items that need new RPCs first.

**Console commands** (mirrors the M2-record `harness_record` pattern):
- `harness_record_actions <name>` — start recording. Logs target path + reminder of
  the stop command.
- `harness_record_stop` — write buffer to `~/.cache/sdv-test-framework/records/actions/<name>.test.json`. Logs steps emitted.

**File output** matches the existing scenario-JSON shape:
```json
{
  "name": "<name>",
  "config": { "seed": 42 },
  "steps": [
    { "action": "player.warp", "args": { "location": "Farm", "x": 64, "y": 15 } },
    { "action": "time.advance", "args": { "minutes": 30 } },
    { "action": "player.warp", "args": { "location": "SeedShop", "x": 4, "y": 19 } },
    { "action": "world.interact_npc", "args": { "name": "Pierre" } }
  ],
  "assertions": []
}
```

User adds `fixture` + `assertions` post-hoc. Trace ships steps only — assertions are
the user's job (or Claude's, via `scaffold_scenario` extending an existing trace).

## Components

**New files (Harness):**

- `src/Harness/Recording/ActionTraceRecorder.cs` — the orchestrator.
  - Subscribes to SMAPI events on `Start(name, IModHelper, IMonitor)`.
  - Buffers raw events with timestamps.
  - `Stop() → IReadOnlyList<ScenarioStep>` — translates buffer + clears.
- `src/Harness/Recording/ActionTraceTranslator.cs` — pure function.
  - `Translate(IReadOnlyList<RecordedAction>) → IReadOnlyList<ScenarioStep>`.
  - All the heuristics live here. No SMAPI dependency — pure event-record-in,
    step-out. Fully unit-testable without SDV.
- `src/Harness/Recording/RecordedAction.cs` — small record:
  - `(DateTime At, ActionKind Kind, string? Location, int? X, int? Y, string? NpcName, int? MinutesElapsed)`.
  - `enum ActionKind { Warp, NpcInteract, TimeAdvance }`.
- Modify: `src/Harness/ModEntry.cs` — register two new console commands
  (`harness_record_actions` + `harness_record_stop`); wire to a singleton
  `ActionTraceRecorder.Current`.

**New tests:**

- `tests/Harness.Tests/ActionTraceTranslatorTests.cs` — translation unit tests.
  - `OnlyWarp_EmitsWarpStep`
  - `WarpThenNpcInteract_EmitsBothSteps`
  - `MultipleWarpsWithinOneSecond_CoalescesToLatest`
  - `LongIdleBeforeMenu_EmitsTimeAdvance`
  - `MenuChange_NoNearbyWarp_SkipsNpcInteract`
  - `TimeAdvanceBelowThreshold_NotEmitted` (e.g. 5min idle, no other events → drop)
  - `EmptyBuffer_ReturnsEmptyList`
- `tests/Harness.Tests/ActionTraceRecorderTests.cs` — recorder lifecycle.
  - `Start_ThenStop_FlushesBuffer` — feed synthetic actions via internal seam.
  - `DoubleStart_LogsWarning_KeepsFirstSession`
  - `StopBeforeStart_LogsWarning_NoFile`
- `tests/Harness.Tests/ActionTraceIntegrationTests.cs` — 1 skipped placeholder for
  live-SDV verification.

**Target test count:** 337+43 → ~349+44 (+12 passed, +1 skipped).

## Wire / file shapes

### `harness_record_actions <name>` console command

Output (info-level log):
```
[harness_record_actions] recording session 'spring_day_5_repro' — type harness_record_stop to finalize. Output: /home/user/.cache/sdv-test-framework/records/actions/spring_day_5_repro.test.json
```

Errors (error-level log, no file):
- Name missing or invalid (regex `^[A-Za-z0-9_-]+$`) → `[harness_record_actions] name must match [A-Za-z0-9_-]+`.
- Already recording → `[harness_record_actions] session 'X' already in progress; type harness_record_stop first`.

### `harness_record_stop` console command

Output:
```
[harness_record_stop] wrote 7 steps to /home/.../records/actions/spring_day_5_repro.test.json
```

Errors:
- No active session → `[harness_record_stop] no active recording session`.

### `RecordedAction` shape

```csharp
internal sealed record RecordedAction(
    DateTime At,
    ActionKind Kind,
    string? Location = null,   // for Warp
    int? X = null,             // for Warp
    int? Y = null,             // for Warp
    string? NpcName = null,    // for NpcInteract
    int? MinutesElapsed = null // for TimeAdvance
);

internal enum ActionKind { Warp, NpcInteract, TimeAdvance }
```

### Translation rules

```
Input: list of RecordedActions ordered by At ascending.
Output: list of ScenarioSteps.

State:
  pendingMinutes = 0
  lastWarpAt = null
  lastWarpStep = null

For each action:
  case Warp:
    if pendingMinutes >= 10: emit time.advance(pendingMinutes); pendingMinutes = 0
    if lastWarpStep is not null and (action.At - lastWarpAt) < 1s:
      replace lastWarpStep with new warp step (coalesce)
    else:
      emit player.warp(action.Location, action.X, action.Y)
      lastWarpStep = the emitted step; lastWarpAt = action.At
  case NpcInteract:
    if pendingMinutes >= 10: emit time.advance(pendingMinutes); pendingMinutes = 0
    emit world.interact_npc(action.NpcName)
    lastWarpStep = null (subsequent warp coalesce window resets)
  case TimeAdvance:
    pendingMinutes += action.MinutesElapsed

End-of-buffer flush:
  if pendingMinutes >= 10: emit time.advance(pendingMinutes)
```

The "≥10 minutes" threshold prevents spam from the per-tick `TimeChanged` event
(SDV's clock advances 1 in-game minute every 700ms by default, ~1.4 changes per real
second — emitting per-tick would produce thousands of `time.advance(1)` steps).

## Error handling

- **Subscription failure during `Start`** — log a warning + leave the recorder in
  no-op mode. `Stop` will still write an empty-steps file with the right shape.
- **NPC name lookup fails** (menu opened, no NPC nearby) — skip the NpcInteract event;
  don't emit a step. Logs at trace level (silent in default config).
- **File-write failure on Stop** — log the error path; recorder buffer is cleared
  regardless to avoid stuck state on retry.
- **`harness_record_stop` with no active session** — error-level log, no-op.
- **Recorder is alive when SDV exits** (player closes game without calling stop) —
  best-effort flush via `IGameLoopEvents.GameSaving` or similar exit hook. If that
  fails, the trace is lost; document as a known limitation. (M4 followup:
  auto-flush on exit.)

## Testing

**Unit tests (~12 passing):**

- `ActionTraceTranslatorTests` — 7 tests covering the translation rules:
  - `OnlyWarp_EmitsWarpStep`
  - `WarpThenNpcInteract_EmitsBothSteps`
  - `MultipleWarpsWithinOneSecond_CoalescesToLatest` — verifies the 1-second window.
  - `LongIdleBeforeMenu_EmitsTimeAdvance` — 30 minutes pending → emit before next.
  - `MenuChange_NoNearbyWarp_SkipsNpcInteract` — guards against false positives.
  - `TimeAdvanceBelowThreshold_NotEmitted` — 5min idle, no other events → drop.
  - `EmptyBuffer_ReturnsEmptyList`.

- `ActionTraceRecorderTests` — 3 tests for lifecycle:
  - `Start_ThenStop_FlushesBuffer` (uses internal-seam to inject synthetic actions).
  - `DoubleStart_LogsWarning_KeepsFirstSession`
  - `StopBeforeStart_LogsWarning_NoFile`.

- `IFileSink` shim from M2-record gets reused for the recorder's write path. Match
  that pattern.

**Skipped integration (1):**

- `ActionTraceIntegrationTests.RecordsRealPlaySession` — `[Fact(Skip="live SDV")]`,
  exercised by the manual smoke (Phase C).

**Manual smoke** (Phase C):

1. Launch SDV via `./scripts/run-samples.sh` setup (not the runner — needs interactive play).
2. Type `harness_record_actions smoke_walk` in SMAPI console.
3. Walk player around farm → into FarmHouse → out to BusStop. Wait an in-game hour.
4. Type `harness_record_stop`.
5. Inspect `~/.cache/sdv-test-framework/records/actions/smoke_walk.test.json`. Expect
   3 warp steps + at least one time.advance step.
6. Run the recorded scenario via `dotnet run --project src/Runner -- run <path>`.
   Should execute cleanly (no assertions in trace; just step replay).

## Acceptance criteria

1. `./scripts/ci.sh` green at ~349 Passed + 44 Skipped.
2. `harness_record_actions <name>` + `harness_record_stop` console commands work
   end-to-end (manual smoke, Phase C).
3. Generated `.test.json` is accepted by `ScenarioLoader.Load` (verified by translator
   tests' final round-trip + smoke step 6).
4. Coarse-event translation produces readable output: warp + npc-interact + time-advance
   steps with no per-tick noise.
5. NPC-interaction inference works for the common case (warp to shop, open ShopMenu →
   `world.interact_npc(<shopkeeper>)`).
6. `docs/roadmap.md`: action-trace moved from Tier 1 to Completed. Tier 1 now empty
   (the LLM-workflow-enabler bucket is closed pending Tier 2 + 3 followups).
7. `docs/milestones/current.md` gains an action-trace completion subsection.
8. `./scripts/run-samples.sh` still 11/11 PASS.

## Out of scope (M4 followups)

- **Tick-perfect replay** — capturing key-by-key input + simulating it during replay.
  Out-of-scope; the coarse-event approach covers the LLM-workflow target.
- **Tool-use / pickup / combat capture** — needs new RPCs (`player.use_tool`,
  `player.pickup_item`, etc.) before the trace has anywhere to go.
- **Auto-flush on game exit** — best-effort `GameSaving` hook. M4 polish.
- **Recording over multiple in-game days** — works in principle but not specifically
  validated. Add to smoke if priorities shift.
- **Filter UI** — letting the user say "only record warps in this location" or
  similar. Not needed for MVP.
- **MCP wrapper for action-trace** — could expose `start_action_trace` / `stop_action_trace`
  as MCP tools so Claude can drive the recorder, but the use case ("user plays") is
  inherently human-driven; no LLM caller. Skip.

## Links

- M2 record mode: `docs/superpowers/specs/2026-04-24-m2-record-mode-design.md` (the
  state-snapshot + RPC-trace flows). Action-trace is the third flow.
- Roadmap: `docs/roadmap.md` Tier 1 (this item).
- M3-DSL Appendix A: `docs/spec.md` references the codegen-style workflow.
