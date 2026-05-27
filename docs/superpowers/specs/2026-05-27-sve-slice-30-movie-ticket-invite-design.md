# SVE Slice 30: Movie Ticket Invite Flow

## Purpose

Use Stardew Valley Expanded as a real testbed for a player-like movie ticket
invitation flow. Existing SVE scenario 36 proves a worker NPC can be clicked in
`MovieTheater`; Slice 30 should prove a broader theater gameplay path where the
player holds a movie ticket, uses it on a custom NPC, and the scenario can
observe the resulting invite/dialogue state.

The Frobby capability added by this slice must stay mod-neutral. SVE-specific
NPC names, event flags, dialogue strings, and asset keys belong only in the SVE
scenario.

## Current Context

Already available in Frobby:

- `player.give_item` can place a movie ticket `(O)809` in the player's
  inventory.
- `player.select_item` can select an existing inventory item.
- `input.click_tile` can right-click NPC-occupied tiles through Stardew's
  gameplay action path.
- `world.interact_npc` can invoke ordinary NPC interactions directly, but it may
  bypass selected-item invite behavior and should not be the first choice for
  the acceptance scenario.
- `wait.menu`, `state.menu`, `state.player`, `state.npc`, and screenshots can
  observe the visible invite/dialogue outcome.
- `content.asset` can prove SVE's movie-theater data patches, including
  `Data/MoviesReactions`, `Data/ConcessionTastes`, and
  `Strings/Characters` spouse invite strings.

Relevant SVE coverage:

- Scenario 36 seeds theater progression, warps to `MovieTheater`, refreshes
  Claire's schedule, right-clicks Claire's tile, and verifies theater worker
  dialogue.
- SVE patches Claire/Martin theater schedules and worker behavior.
- SVE adds movie reactions and concession tastes for custom NPCs such as Sophia,
  Claire, Martin, Scarlett, Lance, Olivia, Victor, and Apples.

## Acceptance Target

Use **Sophia** as the first movie-ticket invite target.

Reasons:

- Sophia is a custom SVE NPC with movie reaction content.
- She avoids Claire's worker/spouse special cases and Martin's minor/worker
  edge cases.
- She gives the scenario a clean custom-NPC proof of selected-item interaction.

If live research shows Sophia cannot be placed or invited deterministically from
the current fixture, choose another SVE custom NPC with movie reaction content
that can be placed with existing `world.warp_npc` / relationship helpers.

## Scenario Shape

Add SVE scenario 38:

1. Load the existing fixture.
2. Seed theater progression with `ccMovieTheater`, `191393`, and `015305930`,
   matching the existing scenario 36 setup.
3. Seed Sophia friendship to at least 2000 points so the invite path is not
   blocked by low relationship state.
4. Warp Sophia into a stable reachable location and wait for
   `wait.npc_location`. The implementation plan should try a normal town tile
   first and fall back to the farm if the live route has map/NPC collision
   issues.
5. Give the player one movie ticket `(O)809`.
6. Select the ticket through `player.select_item`.
7. Capture a pre-invite screenshot.
8. Use `input.click_tile` on the NPC's tile, preferring right-click/action input
   with the selected ticket.
9. Wait for a visible invite response menu/dialogue or a state change that
   proves the invite path.
10. Assert the selected-ticket outcome through `state.menu`, `state.player`,
    and, if needed, a neutral movie-theater state projector.
11. Capture a post-invite screenshot.
12. Assert SVE content assets for the chosen NPC's movie reactions so the test
    proves the mod's movie data is present.

The scenario should not require playing the full movie screening in this slice.
The invite result is the acceptance boundary.

## Frobby Capability Strategy

Start with the existing player-like tools.

If `player.give_item` + `player.select_item` + `input.click_tile` can trigger
Stardew's native ticket invite behavior, Frobby needs no new RPC. The slice then
only adds SVE scenario coverage and docs/TODO updates.

If the click does not carry the selected ticket into the native NPC interaction
path, harden `input.click_tile` in a mod-neutral way:

- preserve current behavior for ordinary right-clicks;
- ensure selected inventory items remain selected when the action click is sent;
- route NPC-occupied tile clicks through the same Stardew path a player uses for
  gifting/inviting rather than only drawing generic dialogue;
- report selected item and target NPC metadata in the existing click result.

If the visible menu/dialogue is not enough to assert the invite state, add a
neutral `state.movie_theater` projector. It should expose live Stardew
movie-theater state only, such as current invited patrons, player-invited
patrons, current movie, or relevant theater flags when those fields exist. It
must not know about SVE NPCs.

## Out Of Scope

- Buying concessions or proving concession taste effects.
- Running a full movie screening and asserting before/during/after reactions.
- Claire/Martin worker-specific rejection and schedule variants.
- SVE-specific helpers in Frobby production code.
- Parsing SVE content packs inside Frobby. Runtime asset checks should use the
  existing `content.asset` primitive.

## Testing

Frobby tests, only if new framework behavior is needed:

- `InputClickTileHandlerTests` for selected-item NPC tile clicks preserving
  selected item and target NPC metadata.
- `StateMovieTheaterHandlerTests` if `state.movie_theater` is introduced.
- Runner tests only if a new runner-side wait or label is introduced.

SVE tests:

- New scenario 38 for the movie ticket invite flow.
- Rerun scenario 36 as a regression because it shares theater NPC click
  behavior.

Verification:

- Targeted Frobby unit tests for any changed handlers/projectors.
- `dotnet build src/Runner/Runner.csproj --nologo`.
- Headless SVE scenario 38.
- Headless SVE scenario 36 regression.

## Follow-Ups

- Concession purchase and taste validation.
- Full movie screening reaction flow.
- Claire/Martin worker invite/reject edge cases.
- Broader custom-NPC movie reaction matrix.

## Open Risks

- Stardew's movie invite state may live in private `MovieTheater` fields that
  are not visible from generic menu/dialogue projection.
- The current fixture may need careful day/time/friendship setup to make Sophia
  available and inviteable.
- Selected-item NPC actions may currently fall through to generic dialogue. If
  so, the fix belongs in neutral selected-item tile-click handling, not in an SVE
  shortcut.
