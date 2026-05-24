# SVE Slice 24: Festival Actor Interaction Design

## Context

Frobby can already start active festivals, observe active event state, inspect festival map content, and interact with ordinary NPCs in the current location. Stardew Valley Expanded still has untested festival behavior where actors are part of the active event rather than ordinary location NPCs. The Stardew Valley Fair is a useful next proof because SVE adds festival actors and dialogue, and those actors are exposed through the active event lifecycle.

This slice should harden a neutral Frobby gap: test scenarios need to wait for an active event/festival actor and interact with that actor through the same broad "talk to this named character" primitive used outside events.

## Goals

- Extend `wait.event_active` with actor filters so scenarios can wait until a named event/festival actor is present before interacting.
- Extend `world.interact_npc` so it can fall back to active event actors when no ordinary location NPC with that name is present.
- Keep Frobby framework code neutral. SVE actor names, dates, and dialogue snippets belong only in SVE scenario JSON and SVE docs.
- Prove the behavior with one SVE scenario against the Fall 16 Stardew Valley Fair by talking to Sophia as an SVE-added festival actor.
- Update Frobby docs, SVE docs, and the capability TODO so future mod authors can discover the pattern.

## Non-Goals

- Do not implement movie theater-specific setup in this slice.
- Do not implement grange judging command progression in this slice.
- Do not add pathfinding to walk to festival actors.
- Do not add SVE-specific code or hard-coded actor names to Frobby.
- Do not change existing `world.interact_npc` behavior for ordinary current-location NPCs.

## Approaches Considered

### Direct Event Actor Interaction Fallback

When `world.interact_npc` cannot find an ordinary location NPC, it looks for a named actor in the active event or current location event. If found, it calls the same `NPC.checkAction(player, location)` and dialogue fallback used by ordinary NPC interactions. This is the preferred approach because it is small, preserves the existing RPC surface, and maps cleanly to how scenario authors think about "talk to Sophia."

### New `event.interact_actor` RPC

A dedicated event actor RPC would make the target explicit, but it would duplicate most of `world.interact_npc` and force scenario authors to know whether an NPC is a location character or event actor. That is unnecessary for this slice.

### Coordinate-Based Clicks Only

Using `input.click_tile` against the actor tile would be player-like, but it is harder to make robust during active events and does not add the neutral observability needed to avoid racing festival setup. This remains useful for later UI/input hardening, but it is not the foundation for this slice.

## Recommended Design

### Event Actor Filters

`wait.event_active` gains optional actor filters:

- `actor_name`: pass when `state.event.actors` contains an actor with the exact name.
- `actor_x` and `actor_y`: optional tile filters applied to the matching actor. These must be provided together.

The wait keeps existing `id`, `location`, and `is_festival` filters. Timeout diagnostics should mention actor filters and include the last observed event actor names so failures explain whether the event was active but missing the target actor.

Example:

```json
{
  "action": "wait.event_active",
  "args": {
    "location": "Temp",
    "is_festival": true,
    "actor_name": "Sophia",
    "timeout_ms": 15000,
    "poll_ms": 100
  }
}
```

### `world.interact_npc` Event Fallback

The existing RPC remains:

```json
{ "action": "world.interact_npc", "args": { "name": "Sophia" } }
```

Resolution order:

1. Search ordinary NPCs in `Game1.currentLocation.characters`.
2. If none match, search active event actors from `Game1.CurrentEvent` and `Game1.currentLocation.currentEvent`.
3. Interact with the resolved NPC through the current location's `NPC.checkAction` path.
4. If the interaction does not leave a readable menu open and the NPC can talk, use the existing dialogue fallback.

If neither source has a matching NPC, return `GameStateInvalid` with a message that includes the current location and active event actor names when known.

### SVE Proof Scenario

Add scenario 32:

1. Load the existing clean fixture.
2. Set time/date to Fall 16 Year 1 during the Fair entry window.
3. Start the festival with `festival.start`.
4. Wait for an active festival event and actor `Sophia`.
5. Call `world.interact_npc` for `Sophia`.
6. Wait for a menu/dialogue containing a stable SVE Fair text fragment, such as `Blue Moon Vineyard` or `aged wine`.
7. Assert `state.menu.extra.character == 'Sophia'` and `state.menu.extra.dialogue_text` contains the selected text fragment.
8. Capture a final screenshot.

The scenario may use the event actor wait instead of coordinates unless implementation testing shows the actor needs a tile assertion for stability.

## Test Strategy

Use TDD:

- Runner test: `wait.event_active` polls until the named actor appears.
- Runner test: timeout diagnostics for missing actor include the target filter and last observed actor names.
- Harness test: `world.interact_npc` resolves ordinary current-location NPCs before event actors.
- Harness test: `world.interact_npc` falls back to an active event actor and uses the existing interaction/dialogue path.
- Harness test: missing NPC errors include event actor names when available.
- Live SVE headless scenario 32 proves the festival actor dialogue path.
- Run adjacent festival scenario 19 to ensure the existing Spirit's Eve festival support still works.

## Risks

- Festival actor dialogue may be rendered through event dialogue rather than ordinary NPC dialogue. If that happens, the handler should still use the existing `NPC.checkAction` path first and only adjust the fallback in a neutral way.
- The active festival location reports as `Temp` after `festival.start`, while the festival metadata starts from `Town`. The SVE scenario should assert against observed runtime state rather than assuming map internals.
- Fair setup includes long `advancedMove` commands. The event actor wait should prevent fixed sleeps, but the live scenario may need a realistic timeout.

## Completion Criteria

- Frobby can wait for a named active event/festival actor.
- Frobby can interact with an event actor through `world.interact_npc` without affecting ordinary NPC interactions.
- SVE scenario 32 passes headless and captures the actor dialogue.
- Existing Frobby unit tests pass.
- Existing SVE festival scenario 19 and new scenario 32 pass headless.
- Frobby docs, SVE docs, and `SVE_FROBBY_CAPABILITY_TODO.md` document the new pattern and keep movie theater/grange follow-ups pending.
