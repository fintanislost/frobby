# SVE Slice 32 Movie Screening Reactions Design

## Overview

Slice 32 adds headless coverage for the full movie-theater payoff after the
existing SVE movie ticket and concession slices. The test should invite Sophia,
enter the theater through the real lobby door action, observe the screening or
reaction sequence, and prove SVE's `Data/MoviesReactions` special-response data
is present and surfaced through normal Stardew runtime UI/event state.

The framework changes must remain mod-neutral. Frobby should not add a
movie-specific shortcut unless the generic event/input route proves impossible.

## Current State

Implemented before this slice:

- `input.click_tile` can use a selected item on an NPC and can discover a nearby
  exact map `action_value`.
- `shop.click_purchase.item_index` can click dynamic visible shop rows such as
  movie concessions.
- `state.event` exposes active event/cutscene status, root dialogue, choices,
  actors, actor dialogue, actor positions, and viewport.
- Runner `wait.event_active` can wait for event id/location/festival/actor
  filters, and actor dialogue filters.
- Runner `wait.menu` and `event.advance` can wait for normal menu/dialogue text
  and advance it.
- SVE scenarios 36, 38, and 39 cover theater NPC tile clicks, Sophia movie
  ticket invites, and concession purchase after invite.

Research/probe finding:

- A throwaway probe found `Theater_Doors` through `state.tile_actions`, but
  `input.click_tile.action_value` clicked from the lobby/concession area and
  reported `handled=false`; `wait.event_active` never observed an event. The
  likely cause is that the resolved action tile was off-screen or outside the
  effective click region from the player/viewpoint. This is a generic Frobby
  diagnostics and targeting problem, not an SVE-specific behavior.

## Goals

1. Prove a player-like path from Sophia invite into the movie screening flow.
2. Capture useful step screenshots around lobby entry, event/reaction state, and
   final reaction/return state.
3. Add generic Frobby diagnostics so action-value clicks report the resolved
   map-action tile and whether the target screen coordinate is inside the
   viewport.
4. Add generic runner filtering for root event dialogue so scenarios can wait on
   dialogue text attached to the active event, not only actor dialogue.
5. Document the new generic event/input guidance and mark Slice 32 complete only
   after live SVE verification passes.

## Non-Goals

- Do not add SVE-specific Frobby production code.
- Do not add a hard-coded `movie.start`, `movie.screening`, or `state.movie`
  primitive in the first implementation.
- Do not depend on exact pixel-perfect movie frames. Screenshots are for human
  review; assertions should use stable state/text/content signals.
- Do not merge the SVE branch into `master` unless the user explicitly approves.

## Proposed Approach

Use the generic event/input hardening route.

Frobby changes:

- Extend `InputClickTileResult` with optional action-resolution details:
  `resolved_action_value`, `resolved_action_layer`, `resolved_action_property`,
  `resolved_action_tile`, and `screen_visible`.
- Keep existing click behavior intact, but make off-screen or ineffective
  action-value clicks obvious in reports. This slice reports
  `screen_visible=false` and does not add a hard guard that would change existing
  behavior.
- Extend runner `wait.event_active` filters with root event dialogue filters:
  `dialogue_text`, `dialogue_text_matches`, and `dialogue_speaker`.
- Include root event dialogue in timeout diagnostics so long cutscene failures
  explain what text was last observed.

SVE test changes:

- Add scenario 40 named `sve_movie_screening_reaction_flow`.
- Reuse the Sophia invite setup from scenario 38.
- Warp to the theater near the entrance doors rather than the concession area,
  or use a closer anchor found during implementation probing.
- Use `state.tile_actions` to assert a `Theater_Doors` action exists near the
  entry anchor.
- Use `input.click_tile.action_value` to click the discovered theater-door
  action, then wait first for active event root dialogue. If live probing proves
  the vanilla flow exposes only menu text for the relevant phase, use
  `wait.menu` in the SVE scenario while still keeping Frobby's root event
  dialogue filter as the generic framework improvement.
- Assert `Data/MoviesReactions` contains Sophia and at least one special response
  for a date-selected movie tag. The live scenario should use a date chosen
  during implementation probing so Sophia's before/during/after reaction text is
  deterministic.

## Components

### Frobby Protocol

`src/Protocol/Models/InputClickTileRequest.cs`

- Add no required fields.
- Do not add `require_screen_visible` in this slice; preserve current click
  behavior and improve diagnostics first.

`src/Protocol/Models/InputClickTileRequest.cs` (`InputClickTileResult`)

- Extend `InputClickTileResult` with nullable action-resolution fields and a
  boolean `screen_visible`.
- Preserve backwards compatibility for existing JSON scenarios.

`src/Protocol/Models/EventState.cs`

- No protocol shape change is required for root dialogue, because
  `EventState.Dialogue.Text`, `Speaker`, and `Choices` already exist.

### Frobby Harness

`src/Harness/Handlers/InputClickTileHandler.cs`

- Return details about the map action selected by `action_value` resolution.
- Compute `screen_visible` from the final screen coordinate and the viewport
  size. Use the same click point that is sent to Stardew.
- Keep selection deterministic: nearest by Manhattan distance, then Y, then X.

### Frobby Runner

`src/Runner/Scenarios/ScenarioRunner.cs`

- Parse root dialogue filters on `wait.event_active`.
- Match filters against `state.event.dialogue.text` and
  `state.event.dialogue.speaker`.
- Add root dialogue text/speaker to `FormatEventState` timeout diagnostics.
- Improve the action label/detail for `input.click_tile.action_value` so reports
  show the resolved action tile and `screen_visible` status.

### SVE Scenario

`tests/sdv/40-sve-movie-screening-reaction-flow.test.json`

- Start from the stable scenario 38 invite setup.
- Enter the theater through the real `Theater_Doors` map action.
- Capture screenshots before door click, after event/screening starts, and at
  the final observed reaction/return point.
- Validate SVE movie reaction data through `content.asset`.

`docs/FROBBY.md`

- Document scenario 40 and the movie-screening capabilities it proves.

### Frobby Docs/TODO

- Update `README.md`, `docs/rpc-schema.md`, and `docs/wiki/examples.md` for:
  - action-value click diagnostics,
  - root event dialogue wait filters,
  - movie-screening example coverage.
- Update `SVE_FROBBY_CAPABILITY_TODO.md` with Slice 32 as active/done.

## Data Flow

1. Scenario prepares world state: time/weather, theater mail/events, Sophia
   friendship, and movie ticket.
2. Scenario selects the ticket and right-clicks Sophia through `input.click_tile`.
3. Scenario acknowledges the invite dialogue and verifies the ticket is consumed.
4. Scenario warps near the movie-theater doors and asserts a `Theater_Doors` map
   action exists nearby.
5. `input.click_tile.action_value` resolves the closest matching action tile,
   returns diagnostics, and sends the player-like click.
6. `wait.event_active` observes root event/dialogue state or actor state from the
   screening sequence.
7. Scenario asserts SVE reaction data exists and that runtime text/state matches
   the expected reaction path.

## Error Handling

- If `action_value` resolution succeeds but the final click point is outside the
  viewport, the report must show `screen_visible=false`.
- If no action tile is found, preserve the existing `GameStateInvalid` error.
- If root dialogue text never appears, `wait.event_active` timeout diagnostics
  should include the last observed event id, location, actors, and root dialogue.
- If live probing reveals the movie flow opens a menu rather than an event,
  scenario 40 may use `wait.menu` for that phase, but Frobby root event dialogue
  filtering should still be implemented because it is a generic cutscene
  observability gap.

## Testing

Frobby unit tests:

- `InputClickTileHandlerTests` should verify action-value clicks include resolved
  action details and `screen_visible`.
- `InputClickTileHandlerTests` should verify off-screen action-value targets are
  reported as `screen_visible=false` without changing existing behavior.
- `ScenarioRunnerTests` should verify `wait.event_active.dialogue_text` and
  `dialogue_text_matches` match root event dialogue.
- `ScenarioRunnerTests` should verify timeout formatting includes root dialogue.
- Protocol serialization tests should cover the new result fields if existing
  test patterns require explicit coverage.

SVE live tests:

- New scenario 40 must pass headlessly.
- Adjacent movie scenarios 39, 38, and 36 should pass headlessly in the same
  final suite.

Baseline commands:

```bash
dotnet test --nologo
./scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-32-final tests/sdv/40-sve-movie-screening-reaction-flow.test.json tests/sdv/39-sve-movie-concession-purchase-flow.test.json tests/sdv/38-sve-movie-ticket-invite-flow.test.json tests/sdv/36-sve-movie-theater-npc-click.test.json
```

## Open Risks

- Vanilla movie-theater flow may not expose a long-lived `Game1.currentEvent`
  when entered through `Theater_Doors`. If so, the scenario should assert the
  generic runtime signal that actually appears, such as a menu dialogue, location
  transition, or player mail/invitation state.
- The correct entry anchor for `Theater_Doors` needs a short live probe. The
  final scenario should use the probed stable nearby anchor plus action
  discovery.
- Current-season movie choice affects which Sophia reaction text appears. The
  implementation plan should choose a date that maps to a known Sophia special
  response and assert that response path.

## Acceptance Criteria

- Frobby reports action-value click resolution details in a mod-neutral way.
- Runner scenarios can wait for active event root dialogue text.
- SVE scenario 40 exercises a real ticket invite into the screening/reaction
  flow and passes headlessly.
- Adjacent movie scenarios 39, 38, and 36 still pass headlessly.
- Docs and `SVE_FROBBY_CAPABILITY_TODO.md` are updated.
