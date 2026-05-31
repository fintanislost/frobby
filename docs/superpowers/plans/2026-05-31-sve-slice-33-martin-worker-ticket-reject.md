# SVE Slice 33 Martin Worker Ticket Reject Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Track each task by changing its checkbox from `- [ ]` to `- [x]` as it completes.

**Goal:** Add headless SVE coverage proving Martin is interactable as a movie-theater worker and visibly rejects a selected movie ticket without consuming it, while keeping Frobby changes generic and mod-neutral.

**Architecture:** This slice should primarily add an SVE repository scenario that exercises existing Frobby harness actions. Frobby production code changes are only allowed if the live run exposes a real generic capability gap that would apply to other mods too.

**Tech Stack:** .NET 10, Stardew Valley/Frobby SMAPI harness, Frobby JSON scenario DSL, SVE Content Patcher content, headless `repo run` reports.

---

## Task 1: Confirm Branches And Baseline

**Files:** None.

- [ ] Run all `dotnet` commands from the Frobby Slice 33 worktree, not from `/home/fintan/stardewRepos/stonks`. The Starberg checkout has a `global.json` that selects SDK 6, which cannot build Frobby's `net10.0` runner.

- [ ] Verify Frobby is on the Slice 33 worktree branch:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-33-martin-worker-ticket-reject status --short --branch
```

Expected:

```text
## feature/sve-slice-33-martin-worker-ticket-reject
```

- [ ] Verify SVE is on a feature branch and do not merge it to `master`:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
```

Expected branch at the start of this slice:

```text
## feature/frobby-sve-slice-32-movie-screening
```

- [ ] Build the Frobby runner in the Slice 33 worktree so scenario runs use current code:

```bash
dotnet build /home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-33-martin-worker-ticket-reject/src/Runner/Runner.csproj --nologo
```

- [ ] Run a focused Frobby baseline. If this fails, diagnose before adding SVE scenario coverage:

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-33-martin-worker-ticket-reject/tests/Runner.Tests/Runner.Tests.csproj --no-restore --nologo
```

---

## Task 2: Add The SVE Martin Ticket Rejection Scenario

**Files:**

- `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/41-sve-martin-movie-worker-ticket-reject.test.json`

- [ ] Create the scenario file with this initial test:

```json
{
  "name": "sve_martin_movie_worker_ticket_reject",
  "fixture": "m0spike_436515781",
  "config": {
    "seed": 42
  },
  "steps": [
    {
      "label": "Set Tuesday movie-theater test conditions",
      "action": "time.set",
      "args": {
        "time": 900,
        "day": 2,
        "season": "spring",
        "year": 1
      }
    },
    {
      "label": "Use sunny weather for deterministic routing",
      "action": "world.set_weather",
      "args": {
        "type": "sun"
      }
    },
    {
      "label": "Unlock the movie theater",
      "action": "player.add_mail",
      "args": {
        "id": "ccMovieTheater"
      }
    },
    {
      "label": "Mark movie theater opening event seen",
      "action": "player.add_event_seen",
      "args": {
        "id": "191393"
      }
    },
    {
      "label": "Mark movie theater access event seen",
      "action": "player.add_event_seen",
      "args": {
        "id": "015305930"
      }
    },
    {
      "label": "Make Martin socially available without consuming his intro path",
      "action": "player.set_friendship",
      "args": {
        "npc": "Martin",
        "points": 1000,
        "talked_to_today": false,
        "gifts_today": 0,
        "gifts_this_week": 0
      }
    },
    {
      "label": "Confirm the player has theater progression state",
      "action": "wait.player",
      "args": {
        "mail_received": "ccMovieTheater",
        "event_seen": "191393",
        "timeout_ms": 10000,
        "poll_ms": 100
      }
    },
    {
      "label": "Warp to the movie theater lobby",
      "action": "player.warp",
      "args": {
        "location": "MovieTheater",
        "x": 7,
        "y": 7
      }
    },
    {
      "label": "Wait for theater lobby load",
      "action": "wait.location",
      "args": {
        "location": "MovieTheater",
        "x": 7,
        "y": 7,
        "timeout_ms": 10000,
        "poll_ms": 100
      }
    },
    {
      "label": "Let theater NPCs initialize",
      "action": "wait.ms",
      "args": {
        "ms": 10000
      }
    },
    {
      "label": "Place Martin on his Tuesday theater worker schedule",
      "action": "world.refresh_npc_schedule",
      "args": {
        "name": "Martin",
        "schedule_key": "Tue"
      }
    },
    {
      "label": "Wait for Martin at the theater counter",
      "action": "wait.npc_location",
      "args": {
        "name": "Martin",
        "location": "MovieTheater",
        "x": 7,
        "y": 5,
        "timeout_ms": 30000,
        "poll_ms": 100
      }
    },
    {
      "label": "Assert Martin is stationed as the worker",
      "action": "state.assert",
      "args": {
        "expr": "state.npcs.Martin.location == 'MovieTheater' && state.npcs.Martin.tile.x == 7 && state.npcs.Martin.tile.y == 5",
        "message": "Martin should be positioned at the movie theater worker counter."
      }
    },
    {
      "label": "Assert Martin can be clicked for social interaction",
      "action": "state.assert",
      "args": {
        "expr": "state.npcs.Martin.can_socialize == true",
        "message": "Martin should be socially clickable while working."
      }
    },
    {
      "label": "Capture Martin before worker interaction",
      "action": "screenshot",
      "args": {
        "name": "martin-worker-before-click"
      }
    },
    {
      "label": "Right-click Martin at the counter",
      "action": "input.click_tile",
      "args": {
        "location": "MovieTheater",
        "x": 7,
        "y": 5,
        "button": "right",
        "allow_event_input": true
      }
    },
    {
      "label": "Wait for Martin worker dialogue",
      "action": "wait.menu",
      "args": {
        "ready": true,
        "text_matches": "Hello|movie|Movie|theater|Theater|work|working|popcorn|Martin|Joja|welcome",
        "timeout_ms": 30000,
        "poll_ms": 100
      }
    },
    {
      "label": "Assert Martin owns the worker dialogue",
      "action": "state.assert",
      "args": {
        "expr": "state.menu.extra.character == 'Martin'",
        "message": "Worker interaction should open Martin dialogue."
      }
    },
    {
      "label": "Assert worker dialogue has text",
      "action": "state.assert",
      "args": {
        "expr": "state.menu.extra.dialogue_text != ''",
        "message": "Martin worker dialogue should contain visible text."
      }
    },
    {
      "label": "Capture Martin worker dialogue",
      "action": "screenshot",
      "args": {
        "name": "martin-worker-dialogue"
      }
    },
    {
      "label": "Close Martin worker dialogue",
      "action": "ui.acknowledge",
      "args": {
        "until_closed": true,
        "max_clicks": 8,
        "timeout_ms": 10000,
        "poll_ms": 100
      }
    },
    {
      "label": "Let the lobby recover after worker dialogue",
      "action": "wait.ms",
      "args": {
        "ms": 500
      }
    },
    {
      "label": "Give the player one movie ticket",
      "action": "player.give_item",
      "args": {
        "id": "(O)809",
        "count": 1
      }
    },
    {
      "label": "Assert the movie ticket is in inventory before rejection",
      "action": "state.assert",
      "args": {
        "expr": "state.player.items contains qualified_id '(O)809'",
        "message": "The player should have a movie ticket before clicking Martin."
      }
    },
    {
      "label": "Select the movie ticket",
      "action": "player.select_item",
      "args": {
        "id": "(O)809",
        "prefer_hotbar": true
      }
    },
    {
      "label": "Let held ticket selection settle",
      "action": "wait.ms",
      "args": {
        "ms": 500
      }
    },
    {
      "label": "Capture the selected ticket before rejection",
      "action": "screenshot",
      "args": {
        "name": "martin-ticket-before-click"
      }
    },
    {
      "label": "Right-click Martin with the selected ticket",
      "action": "input.click_tile",
      "args": {
        "location": "MovieTheater",
        "x": 7,
        "y": 5,
        "button": "right",
        "allow_event_input": true
      }
    },
    {
      "label": "Wait for visible ticket rejection feedback",
      "action": "wait.menu",
      "args": {
        "ready": true,
        "text_matches": "movie|Movie|ticket|Ticket|busy|work|working|showing|another time|can't|cannot|reject|Martin",
        "timeout_ms": 30000,
        "poll_ms": 100
      }
    },
    {
      "label": "Assert the rejection has visible dialogue",
      "action": "state.assert",
      "args": {
        "expr": "state.menu.extra.dialogue_text != ''",
        "message": "Martin ticket rejection should produce visible feedback."
      }
    },
    {
      "label": "Assert the rejected movie ticket remains in inventory",
      "action": "state.assert",
      "args": {
        "expr": "state.player.items contains qualified_id '(O)809'",
        "message": "Martin should reject the selected movie ticket without consuming it."
      }
    },
    {
      "label": "Capture Martin ticket rejection dialogue",
      "action": "screenshot",
      "args": {
        "name": "martin-ticket-rejection"
      }
    }
  ],
  "assertions": [
    {
      "label": "Martin Tuesday schedule has the theater worker stop",
      "kind": "content.asset",
      "asset_name": "Characters/schedules/Martin",
      "entry_keys": [
        "Tue"
      ],
      "expr": "asset.entries.Tue.value contains 'MovieTheater 7 5'",
      "message": "SVE should schedule Martin at the movie theater counter on Tuesday after theater unlock."
    },
    {
      "label": "Martin has movie reaction data",
      "kind": "content.asset",
      "asset_name": "Data/MoviesReactions",
      "entry_keys": [
        "Martin"
      ],
      "expr": "asset.entries.Martin.exists == true",
      "message": "SVE should define Martin movie reaction data."
    },
    {
      "label": "Martin movie reaction data is populated",
      "kind": "content.asset",
      "asset_name": "Data/MoviesReactions",
      "entry_keys": [
        "Martin"
      ],
      "expr": "asset.entries.Martin.value.reactions.count != 0",
      "message": "Martin should have one or more movie reactions."
    }
  ]
}
```

- [ ] If live execution shows an unrelated queued Martin intro or fixture dialogue before the worker dialogue, adjust only the scenario path:
  - Keep the initial click real: `input.click_tile`.
  - Clear queued dialogue with `wait.menu` and `ui.acknowledge`.
  - Re-click Martin for the worker assertion.
  - Do not add a Frobby shortcut that mutates social/dialogue state.

- [ ] If Tuesday conflicts with the fixture in live execution, switch the setup to Saturday:
  - Change `day` from `2` to `6`.
  - Change `schedule_key` from `Tue` to `Sat`.
  - Change the schedule content assertion key from `Tue` to `Sat`.
  - Keep Tuesday as the first attempted implementation unless the live report proves the conflict.

---

## Task 3: Run The New Scenario Headless And Diagnose

**Files:**

- `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/41-sve-martin-movie-worker-ticket-reject.test.json`
- Reports under `/tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-33-*`

- [ ] Run the new scenario with a fresh report:

```bash
env SDV_TEST_MOD_CACHE=/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-33-martin-worker-ticket-reject/.cache/deps dotnet /home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-33-martin-worker-ticket-reject/src/Runner/bin/Debug/net10.0/sdv-test.dll repo run --repo-root /home/fintan/stardewRepos/StardewValleyExpanded --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-33-probe-41 /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/41-sve-martin-movie-worker-ticket-reject.test.json
```

- [ ] Inspect the generated HTML report and step screenshots if the run fails:

```bash
test -f /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-33-probe-41/main.html
```

- [ ] Apply the smallest scenario-only adjustment for ordinary SVE fixture realities:
  - Increase waits if the NPC location is still settling.
  - Clear a queued dialogue with real input.
  - Broaden the rejection text matcher if the report shows equivalent visible rejection text.
  - Do not loosen the inventory preservation assertion.

- [ ] Treat a silent ticket click as a real failure:
  - `state.player.items contains qualified_id '(O)809'` passing is not enough.
  - `wait.menu` with visible dialogue must pass too.

---

## Task 4: Add Generic Frobby Capability Only If The Live Run Proves A Gap

**Files:** Only if needed.

- [ ] If scenario 41 passes with existing Frobby tools, skip this task. Do not add speculative Frobby code.

- [ ] If the scenario is blocked by a generic Frobby gap, stop and write a targeted TDD addendum before production edits. Acceptable examples:
  - Content projection cannot expose a nested collection count that the DSL already claims to support generically.
  - Click diagnostics cannot distinguish a real no-op from a successful menu-open interaction across mods.
  - A generic wait predicate needed by multiple mods is missing.

- [ ] If production Frobby code is required, follow TDD:
  - Add a failing unit/integration test first in the relevant Frobby test project.
  - Implement the smallest generic harness or runner change.
  - Re-run the new test, the owning test project, and scenario 41.
  - Update `docs/wiki/` and any relevant schema/reference docs in the same change.

Do not add an action or assertion that is hard-coded to SVE, Martin, MovieTheater, or movie tickets.

---

## Task 5: Update Documentation And TODO State

**Files:**

- `/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-33-martin-worker-ticket-reject/SVE_FROBBY_CAPABILITY_TODO.md`
- `/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-33-martin-worker-ticket-reject/docs/wiki/examples.md`
- `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`

- [ ] In Frobby `SVE_FROBBY_CAPABILITY_TODO.md`, add Slice 33 as completed after verification:

```markdown
- [x] Done: Slice 33, Martin movie worker ticket rejection.
  - SVE pressure: Martin is a movie-theater worker on some days and should reject a selected movie ticket while working instead of silently accepting or consuming it.
  - Frobby goal: prove existing click, wait, inventory, screenshot, and content assertions can cover another movie worker edge case without mod-specific helpers.
  - Design spec: `docs/superpowers/specs/2026-05-31-sve-slice-33-martin-worker-ticket-reject-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-31-sve-slice-33-martin-worker-ticket-reject.md`.
  - Scenario: `tests/sdv/41-sve-martin-movie-worker-ticket-reject.test.json` in the SVE testbed.
  - Verification: focused headless scenario 41 plus adjacent movie scenarios 36, 38, 39, and 40.
```

- [ ] If the older Slice 30 follow-up still mentions `Claire/Martin worker invite edge cases`, update that line to show Martin ticket rejection is covered and only Claire-specific follow-up remains if applicable.

- [ ] In Frobby `docs/wiki/examples.md`, add scenario 41 to the SVE examples section:

```markdown
- `tests/sdv/41-sve-martin-movie-worker-ticket-reject.test.json`: right-clicks Martin while he is working at the theater counter, selects a movie ticket, verifies visible rejection feedback, and asserts the ticket remains in inventory.
```

- [ ] In SVE `docs/FROBBY.md`, add scenario 41 after the existing movie screening coverage:

```markdown
Scenario `tests/sdv/41-sve-martin-movie-worker-ticket-reject.test.json` covers Martin's movie-theater worker edge case. It places Martin on his Tuesday theater schedule, verifies the worker click opens Martin dialogue, selects a movie ticket, then proves the worker rejection is visible and the ticket remains in inventory.
```

- [ ] If no Frobby production code changed, explicitly note in the TODO entry that no new neutral capability was needed.

---

## Task 6: Final Verification

**Files:** Reports only.

- [ ] Rebuild the Frobby runner:

```bash
dotnet build /home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-33-martin-worker-ticket-reject/src/Runner/Runner.csproj --nologo
```

- [ ] Run the focused SVE scenario:

```bash
env SDV_TEST_MOD_CACHE=/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-33-martin-worker-ticket-reject/.cache/deps dotnet /home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-33-martin-worker-ticket-reject/src/Runner/bin/Debug/net10.0/sdv-test.dll repo run --repo-root /home/fintan/stardewRepos/StardewValleyExpanded --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-33-final-41 /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/41-sve-martin-movie-worker-ticket-reject.test.json
```

- [ ] Run adjacent SVE movie scenarios to catch regressions:

```bash
env SDV_TEST_MOD_CACHE=/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-33-martin-worker-ticket-reject/.cache/deps dotnet /home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-33-martin-worker-ticket-reject/src/Runner/bin/Debug/net10.0/sdv-test.dll repo run --repo-root /home/fintan/stardewRepos/StardewValleyExpanded --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-33-final-movie-adjacent /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/36-sve-movie-theater-npc-click.test.json /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/38-sve-movie-ticket-invite-flow.test.json /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/39-sve-movie-concession-purchase-flow.test.json /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/40-sve-movie-screening-reaction-flow.test.json /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/41-sve-martin-movie-worker-ticket-reject.test.json
```

- [ ] Run Frobby tests:

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-33-martin-worker-ticket-reject/sdv-test-framework.sln --no-restore --nologo
```

- [ ] If Frobby production code changed, run the Starberg smoke set with the same Frobby branch before committing:

```bash
env SDV_TEST_MOD_CACHE=/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-33-martin-worker-ticket-reject/.cache/deps dotnet /home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-33-martin-worker-ticket-reject/src/Runner/bin/Debug/net10.0/sdv-test.dll repo run --repo-root /home/fintan/stardewRepos/stonks --headless --mod-set core --report-dir /tmp/starberg-frobby-results-0.1.0/slice-33-frobby-smoke /home/fintan/stardewRepos/stonks/tests/sdv/01-terminal-opens.test.json /home/fintan/stardewRepos/stonks/tests/sdv/38-chart-panel-live-spacing.test.json /home/fintan/stardewRepos/stonks/tests/sdv/67-news-document-view.test.json
```

---

## Task 7: Commit And Report

**Files:** All changed files from prior tasks.

- [ ] Commit the Frobby implementation-plan-only change before execution:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-33-martin-worker-ticket-reject add docs/superpowers/plans/2026-05-31-sve-slice-33-martin-worker-ticket-reject.md
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-33-martin-worker-ticket-reject commit -m "docs: plan SVE Martin ticket rejection slice"
```

- [ ] After implementation and verification, commit SVE scenario/docs on the SVE feature branch:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded add tests/sdv/41-sve-martin-movie-worker-ticket-reject.test.json docs/FROBBY.md
git -C /home/fintan/stardewRepos/StardewValleyExpanded commit -m "test: cover Martin movie worker rejection"
```

- [ ] Commit Frobby TODO/docs updates on the Frobby Slice 33 branch:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-33-martin-worker-ticket-reject add SVE_FROBBY_CAPABILITY_TODO.md docs/wiki/examples.md
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-33-martin-worker-ticket-reject commit -m "docs: record SVE Martin ticket rejection coverage"
```

- [ ] If Frobby production code changed, include those files in the second Frobby commit and use a non-docs message such as:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-33-martin-worker-ticket-reject commit -m "test: harden generic movie worker interaction coverage"
```

- [ ] Final response should include:
  - Frobby commit hash(es).
  - SVE commit hash.
  - Whether Frobby production code changed.
  - Exact verification commands and pass/fail status.
  - Report directory paths.
  - Any caveats, especially if Saturday fallback was required.

Do not merge SVE into `master`. Frobby may be merged to `main` only after user approval.
