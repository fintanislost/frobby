# SVE Slice 33 Martin Worker Ticket Reject Design

## Overview

Slice 33 adds headless coverage for the remaining narrow movie-theater worker
edge case: Martin should be interactable as an SVE theater worker, but should
reject a movie-ticket invite while he is working. The scenario should stay
player-like by using tile clicks and a selected `(O)809` movie ticket rather
than semantic shortcuts.

The framework posture is conservative. Use the existing neutral Frobby surface
first, and only add new Frobby behavior if the live Martin flow reveals a
generic missing capability that would help other mods.

## Current State

Implemented before this slice:

- SVE scenario 36 verifies Claire can be scheduled into `MovieTheater` and
  opened through `input.click_tile`.
- SVE scenario 38 verifies a selected movie ticket can invite Sophia and consumes
  the ticket on acceptance.
- SVE scenario 39 verifies concessions can be reached through
  `input.click_tile.action_value` and purchased through visible shop UI.
- SVE scenario 40 verifies a full Sophia invite, theater-door click, and visible
  movie reaction flow.
- Frobby already exposes `player.add_mail`, `player.add_event_seen`,
  `player.set_friendship`, `player.give_item`, `player.select_item`,
  `world.refresh_npc_schedule`, `wait.npc_location`, `input.click_tile`,
  `wait.menu`, `state.player.items`, and `content.asset`.

Research findings:

- Martin's movie-theater schedule is patched when `ccMovieTheater` is received
  and event `191393` has been seen. Without Claire marriage, Martin works at
  `MovieTheater` tile `(7,5)` on Tuesday and Saturday.
- SVE patches `Data/MoviesReactions` so Martin's response is `reject` on
  Tuesday/Saturday workdays when event `502261` has not been seen and the player
  is not married to Claire.
- Martin also has content entries for `Data/ConcessionTastes` and normal movie
  reactions, so the reject override is a runtime content condition worth proving.
- SVE's `HarmonyPatch_MovieTheaterNPCs` allows generic interactions with theater
  NPCs only when the NPC is not already a vanilla theater patron. This is exactly
  the kind of worker edge case Frobby should validate through live clicks.

## Goals

1. Prove Martin can be scheduled into the movie theater on a workday and clicked
   through the same player-like path used for Claire.
2. Prove using a selected movie ticket on working Martin opens the rejection path
   rather than accepting an invite.
3. Prove the rejected ticket remains in the player's inventory.
4. Assert SVE's runtime content includes Martin theater schedule data and Martin
   movie reaction data.
5. Keep the slice mod-neutral on the Frobby side. If new capability is required,
   it must be expressed as a generic testing primitive or projection.

## Non-Goals

- Do not implement the broader Claire/Martin schedule matrix in this slice.
- Do not test Claire spouse-Friday/Wednesday Martin schedule replacement here.
- Do not add a hard-coded `movie.reject`, `movie.worker`, or SVE-specific Frobby
  helper.
- Do not merge SVE back to `master`.
- Do not require pixel-perfect dialogue screenshots. Screenshots are review
  artifacts; assertions should use stable text/state/content.

## Proposed Approach

Use a single SVE scenario named `sve_martin_movie_worker_ticket_reject`.

Scenario setup:

- Set a Tuesday date and movie-theater progression state:
  `ccMovieTheater`, seen event `191393`, and seen event `015305930`.
- Give Martin enough friendship for social interaction but do not mark event
  `502261` seen.
- Warp to `MovieTheater`, refresh Martin's Tuesday schedule, and wait for Martin
  at tile `(7,5)`.
- Capture a pre-click screenshot.

Worker click path:

- Right-click Martin's tile with `input.click_tile`.
- Wait for Martin's theater-worker dialogue through `wait.menu`.
- Assert the menu character is Martin and dialogue text is non-empty.
- Acknowledge the dialogue so the ticket test starts from a clean menu state.

Ticket rejection path:

- Give the player `(O)809`, select it with `player.select_item`, and right-click
  Martin at `(7,5)` again.
- Wait for rejection dialogue. The text should be matched broadly against terms
  from the reject/workday behavior, such as `working`, `showing`, `another time`,
  `movie`, `ticket`, or `Martin`, because vanilla/SVE localization and response
  formatting can differ.
- Assert the selected ticket remains in `state.player.items`.
- Capture the rejection prompt or final state.

Content assertions:

- Assert `Characters/schedules/Martin` has a Tuesday entry containing
  `MovieTheater 7 5`.
- Assert `Data/MoviesReactions` includes a Martin entry.
- If the current content projection can expose nested reaction counts reliably,
  assert `asset.entries.Martin.value.reactions.count != 0`. If live probing
  shows that the reject override's details need deeper list item inspection,
  add a generic Frobby content projection enhancement via TDD before using it in
  the SVE assertion.

## Stress-Test Decisions

- Tuesday is the primary test date. Saturday is only a fallback if live probing
  shows Tuesday conflicts with fixture state.
- Ticket preservation is the hard behavioral assertion for rejection. Visible
  rejection text is still required, but should be matched broadly because the
  final string may come from SVE localization, vanilla movie reaction handling,
  or a dialogue wrapper.
- If Martin has queued introduction or unrelated fixture dialogue, clear it with
  real `input.click_tile`, `wait.menu`, and `ui.acknowledge` steps before the
  ticket rejection proof. Do not add a framework shortcut for clearing dialogue.
- Deeper `Data/MoviesReactions` list-item projection is not required unless the
  live run needs it for diagnosis. The player-visible rejection plus inventory
  preservation is the main proof.
- `world.refresh_npc_schedule` is acceptable here because this slice validates
  worker interaction and ticket rejection, not natural day-start scheduling.
- A silent no-op ticket click is a failure even if the ticket remains in
  inventory. The test must prove the player sees a rejection/dialogue state.

## Components

### SVE Scenario

Create:

- `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/41-sve-martin-movie-worker-ticket-reject.test.json`

The scenario owns SVE-specific ids, dates, NPC names, and tile coordinates.
Frobby code must not encode those details.

### SVE Docs

Modify:

- `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`

Document scenario 41 near the existing movie-theater scenarios. The note should
explain that this covers Martin's worker-day ticket rejection and proves the
ticket remains after rejection.

### Frobby Capability Tracking

Modify:

- `SVE_FROBBY_CAPABILITY_TODO.md`

Add Slice 33 as active during implementation and done after verification. If no
Frobby production change is needed, record that Slice 33 reused existing neutral
click, schedule, inventory, menu, and content-asset tools.

### Optional Frobby Content Projection

Only if required by the SVE proof:

- `src/Harness/Assets/ContentAssetProjector.cs`
- `tests/Harness.Tests/ContentAssetProjectorTests.cs`

The most likely generic enhancement would be bounded summaries for selected
items inside list-like nested data, such as movie reaction ids/responses/tags.
This must be test-driven and documented if added.

## Data Flow

1. The scenario seeds theater progression and a Tuesday workday.
2. Martin's schedule is refreshed and observed at `MovieTheater` tile `(7,5)`.
3. A normal tile click proves SVE's worker interaction path still opens Martin
   dialogue in the special theater location.
4. The player receives and selects `(O)809`.
5. A second tile click routes through Stardew's selected-item NPC interaction.
6. SVE's workday movie reaction override rejects the invite.
7. The scenario asserts the rejection text appears and the ticket remains in
   inventory.
8. Content assertions prove Martin's schedule/reaction data is present at
   runtime.

## Error Handling

- If Martin is not found at `(7,5)`, timeout diagnostics from
  `wait.npc_location` and `state.npc` should show whether schedule refresh or
  progression seeding failed.
- If a normal tile click opens non-Martin theater behavior, the report should
  show `input.click_tile` details such as `target_npc`, `npc_fallback`, and
  selected item.
- If the ticket click consumes the ticket, the scenario should fail explicitly
  with the inventory assertion because that would contradict the worker reject
  behavior.
- If rejection text appears in a menu/message rather than NPC dialogue extras,
  use `wait.menu` text matching as the visible user-facing assertion path.
- If the only missing signal is deeper `Data/MoviesReactions` introspection,
  add the narrow generic content projection enhancement instead of weakening the
  live behavior assertions.

## Testing

Frobby tests, only if Frobby changes are required:

- Add a failing `ContentAssetProjectorTests` case before production changes.
- Verify red, implement the minimal generic projection, then verify green.
- Run the relevant focused tests and full `dotnet test --no-restore --nologo`.

SVE live tests:

- New scenario 41 must pass headlessly under the `core` mod set.
- Adjacent movie scenarios 36, 38, 39, and 40 should pass headlessly with
  scenario 41 in the same final suite.

Baseline commands:

```bash
dotnet test --no-restore --nologo
env SDV_TEST_MOD_CACHE=/home/fintan/stardewRepos/frobby/sdv-test-framework/.cache/deps \
  dotnet src/Runner/bin/Debug/net10.0/sdv-test.dll repo run \
  --repo-root /home/fintan/stardewRepos/StardewValleyExpanded \
  --headless --mod-set core \
  --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-33-final \
  /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/36-sve-movie-theater-npc-click.test.json \
  /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/38-sve-movie-ticket-invite-flow.test.json \
  /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/39-sve-movie-concession-purchase-flow.test.json \
  /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/40-sve-movie-screening-reaction-flow.test.json \
  /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/41-sve-martin-movie-worker-ticket-reject.test.json
```

## Open Risks

- Martin may have queued day-one introduction dialogue like Sophia did. If so,
  the scenario should clear it with real NPC clicks before the ticket rejection
  step, not by adding an SVE-specific helper.
- The exact rejection text may come from vanilla movie reaction handling rather
  than the SVE i18n string. Use broad text matching and inventory state as the
  stable behavior proof.
- If Tuesday date selection conflicts with another fixture condition or event,
  Saturday is the fallback because SVE patches Martin to work both days without
  Claire marriage.
- A selected ticket click may route differently if Martin is treated as a worker
  instead of a normal NPC. If the path fails, diagnose with click result details
  before adding framework behavior.

## Acceptance Criteria

- SVE scenario 41 proves Martin's workday theater interaction and selected
  ticket rejection path headlessly.
- The selected movie ticket remains in inventory after rejection.
- Adjacent movie scenarios 36, 38, 39, and 40 still pass headlessly.
- Any Frobby additions are generic, test-driven, documented, and not
  SVE-specific.
- `SVE_FROBBY_CAPABILITY_TODO.md` and SVE `docs/FROBBY.md` reflect Slice 33.
