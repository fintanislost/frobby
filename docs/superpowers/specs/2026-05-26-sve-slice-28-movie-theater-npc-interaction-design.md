# SVE Slice 28 Movie Theater NPC Interaction Design

## Goal

Use Stardew Valley Expanded's movie theater worker behavior to harden Frobby's
neutral support for player-like NPC interaction in special indoor locations. A
coding agent should be able to set theater-ready progression, wait for a modded
NPC scheduled into `MovieTheater`, click that NPC through the gameplay tile
input path, and assert the resulting dialogue.

## Context

Earlier SVE slices proved ordinary NPC schedules, direct NPC interaction,
festival actor interaction, custom maps, progression flags, and click-based
world input. The remaining theater-specific gap is that Stardew's
`MovieTheater` has special interaction behavior that can prevent normal NPC
clicks from behaving like ordinary location NPC clicks.

SVE adds a Harmony patch named `HarmonyPatch_MovieTheaterNPCs` which:

- postfixes `MovieTheater.checkAction(Location, Rectangle, Farmer)`;
- allows generic NPC interaction with villager NPCs inside the theater when the
  target tile has no map action and the NPC is not a movie patron;
- transpiles Stardew's character-interaction helper to reduce special
  `MovieTheater` cursor behavior.

SVE also patches schedules for Claire and Martin when the movie theater is
available. Claire works at `MovieTheater` on Monday, Wednesday, Thursday,
Friday, and Sunday after the `ccMovieTheater` flag and event `191393` are set.
Her scheduled theater tile is `(7,5)`, and SVE adds theater-specific dialogue
strings such as "Let me know if you want any concessions."

Frobby already has:

- `player.add_mail` and `player.add_event_seen` for progression setup.
- `time.set` for deterministic date/time.
- `wait.npc_location` and `state.npc` for schedule observability.
- `player.warp` and `wait.location` for test positioning.
- `input.click_tile` for player-like gameplay tile clicks.
- `wait.menu`, `state.menu`, screenshots, and dialogue text projection.
- `world.interact_npc` for direct interaction, but that bypasses the tile-click
  path this slice is meant to validate.

## Options Considered

### Option A: Worker NPC Tile-Click Proof

Set theater progression, wait for Claire or Martin to work inside
`MovieTheater`, warp the player nearby, click the NPC tile using
`input.click_tile`, and assert SVE theater dialogue.

This directly exercises the special-location NPC click path and SVE's Harmony
patch while reusing existing Frobby tools. It is the recommended first slice.

### Option B: Full Movie Ticket And Invite Flow

Seed or buy a ticket, invite an NPC, enter the movie, watch the event sequence,
and assert movie reactions.

This is closer to the full player experience, but it expands into ticket item
state, invitation state, movie patron data, event playback, concessions, and
post-movie reactions. It should be a later slice after the theater click path is
stable.

### Option C: State-Only Theater Schedule Validation

Assert the SVE schedule and dialogue assets exist and that an NPC reaches
`MovieTheater`, without clicking.

This is useful as supporting coverage, but it does not prove the player-visible
interaction path or Frobby's input tooling.

Decision: use Option A.

## Frobby Design

The intended first implementation should avoid new production RPCs unless the
live SVE scenario exposes a real neutral gap.

The scenario should use existing primitives:

1. `player.add_mail` for `ccMovieTheater`.
2. `player.add_event_seen` for `191393`.
3. `time.set` to a Claire theater workday, preferably Thursday at or after
   9:00.
4. `wait.npc_location` for Claire at `MovieTheater` tile `(7,5)`.
5. `player.warp` near Claire inside `MovieTheater`.
6. `input.click_tile` with `button: "right"` on Claire's tile.
7. `wait.menu` / `state.menu` assertions for SVE theater dialogue.
8. A final screenshot.

If the live scenario fails because `input.click_tile` cannot interact with an
NPC in `MovieTheater`, the Frobby fix should stay neutral. Acceptable neutral
hardening includes:

- ensuring right-click tile input primes `Game1.currentCursorTile` and cursor
  screen/world coordinates consistently for special locations;
- improving `input.click_tile` result diagnostics when a click is handled but
  no menu opens;
- adding optional result metadata that describes whether an NPC occupied the
  target tile before the click.

The production code must not special-case SVE, Claire, Martin, or
`MovieTheater`. The only theater-specific details belong in the SVE scenario.

## SVE Scenario Design

Add scenario `tests/sdv/36-sve-movie-theater-npc-click.test.json`.

Preferred flow:

1. Set theater unlock state:
   - mail flag `ccMovieTheater`;
   - event seen `191393`.
2. Set a clear workday and time, such as Spring 4, year 1, Thursday at 9:00.
3. Set sunny weather to avoid schedule variants.
4. Wait for Claire in `MovieTheater` at tile `(7,5)`.
5. Assert `state.npc.location == "MovieTheater"` and tile `(7,5)`.
6. Warp the player to a nearby tile in `MovieTheater`.
7. Capture the theater before interaction.
8. Right-click Claire's tile with `input.click_tile`.
9. Wait for a dialogue menu containing an SVE theater phrase.
10. Assert `state.menu.extra.character == "Claire"` and dialogue text is
    non-empty / contains the expected phrase.
11. Capture the final screenshot.

Claire is the preferred first target because she has several theater workdays
and direct SVE theater dialogue. Martin remains a follow-up target if Claire's
schedule or dialogue is affected by relationship state.

## Testing

Frobby unit coverage should be added only for changed framework behavior:

- If `input.click_tile` gains NPC occupancy diagnostics, add protocol/handler
  tests for the response shape.
- If click priming changes, add handler tests that prove the world receives
  correct screen/world/tile coordinates and the selected button.
- If runner labels or reports change, add runner tests for readable output.

SVE live coverage:

- Run scenario 36 headless under the `core` profile.
- Re-run scenario 05 as an ordinary NPC schedule/dialogue regression.
- Re-run scenario 32 as an active festival actor interaction regression.

## Non-Goals

- Full movie ticket purchase or NPC invitation flow.
- Movie patron reaction assertions.
- Concession stand purchases.
- Direct Frobby APIs named after movie theaters.
- SVE-specific production code.

## Acceptance Criteria

- The SVE scenario proves a modded NPC can be scheduled into `MovieTheater`.
- The scenario interacts through `input.click_tile`, not `world.interact_npc`.
- The interaction opens SVE theater dialogue and captures a report screenshot.
- Any Frobby code changes are neutral and covered by red/green tests.
- Existing ordinary NPC and festival actor interaction scenarios still pass.
