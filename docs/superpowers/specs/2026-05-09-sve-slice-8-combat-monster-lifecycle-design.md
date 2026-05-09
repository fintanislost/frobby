# SVE Slice 8: Combat, Monster Lifecycle, Drops, And Hazards Design

## Context

Stardew Valley Expanded is a useful pressure test for Frobby combat coverage because
it includes deterministic Farm Type Manager monster spawns in custom locations. The
current SVE monster scenario proves that Frobby can observe spawned monster state,
but it does not yet prove that a test can interact with combat, detect damage, wait
for lifecycle changes, or reason about combat-adjacent drops and hazards.

The first Slice 8 pass should add neutral Frobby combat primitives and prove them
against one low-flake SVE target. It should not bake SVE content-pack knowledge into
Frobby, and it should avoid direct monster mutation unless a later fixture-only tool
needs it.

## Goals

- Add a player-like combat action that can attack toward a tile or monster using
  Stardew runtime behavior instead of directly changing monster health.
- Extend monster wait/assertion support so scenarios can wait for health changes and
  eventual removal without relying on fixed sleeps.
- Add one SVE scenario that damages the deterministic Crimson Badlands corrupt mummy
  guard and proves the health delta through neutral Frobby state.
- Keep room for follow-up coverage of death/removal, dropped objects, player damage,
  and map hazards after the attack path is stable.

## Non-Goals

- Do not parse SVE's Farm Type Manager content pack inside Frobby.
- Do not add SVE-specific monster names, tiles, or loot rules to Frobby code.
- Do not make the first scenario depend on killing a mummy. Stardew mummy death can
  involve additional mechanics, so first-pass coverage should prove real damage
  before expanding to final removal.
- Do not add broad combat AI simulation or adversarial movement controls in this
  slice.

## Approaches Considered

### Player-Like Combat Primitive

Add a neutral `combat.attack` RPC that equips or uses the farmer's currently selected
weapon/tool and performs a normal attack toward a direction or target tile. Scenarios
then wait for monster state to change. This is the recommended path because it tests
the same gameplay surface a player uses while keeping assertions state-based and
deterministic.

### Direct Monster Mutation

Add a `monster.damage` or `monster.kill` helper that finds a monster and changes its
health directly. This would be easier to make deterministic, but it would prove less
about the mod's real gameplay behavior. It may be useful later as a fixture setup
helper, but it should not be the Slice 8 proof.

### Observation-Only Extension

Only add richer monster state and wait filters. This would help reporting, but SVE
scenario 10 already proves observation. Slice 8 needs at least one player-like action
to move beyond passive state inspection.

## Recommended Design

### Frobby Combat Action

Add `combat.attack` as a harness RPC and runner action.

Inputs:
- `x` and `y`: optional target tile in the player's current location.
- `direction`: optional direction string such as `up`, `down`, `left`, or `right`.
- `repeat`: optional positive count for repeated attack inputs.
- `delay_ticks`: optional delay between repeated attacks.

Behavior:
- If a target tile is provided, face the farmer toward that tile.
- If a direction is provided, face the farmer in that direction.
- Trigger Stardew's normal weapon/tool use path rather than setting monster health.
- Return a small result with `ok`, `tick`, current player tile, facing direction, and
  optionally selected item metadata if available.

The handler should fail clearly when the world is not loaded, no target/direction can
be resolved, or the selected item cannot attack. During implementation, if vanilla
Stardew requires a weapon for reliable combat, the SVE scenario may use existing
neutral inventory setup such as `player.give_item` to provide one.

### Monster State And Wait Filters

Keep `state.location.monsters` as the main observation surface. Add only additive
fields if needed for targeting or debugging, such as an index within the location's
character collection. Avoid promising a stable persistent monster id unless Stardew
exposes one reliably.

Extend runner-side `wait.location_content` filtering for numeric monster fields:
- `health_lt`, `health_lte`, `health_gt`, `health_gte`
- `max_health_lt`, `max_health_lte`, `max_health_gt`, `max_health_gte`
- `damage_lt`, `damage_lte`, `damage_gt`, `damage_gte`

Allow zero/removal-ready waits through existing count semantics where practical. If
the current implementation rejects `max_count: 0`, adjust it so a scenario can wait
for no matching monsters without inventing a SVE-specific action.

### SVE Scenario

Add `tests/sdv/12-sve-combat-monster-damage.test.json`.

Flow:
- Set a fresh deterministic day and weather, matching the established SVE monster
  spawn scenario pattern.
- Warp to `Custom_CrimsonBadlands` near the known corrupt mummy guard.
- Wait for the existing deterministic guard at tile `20,144` with max health `2000`
  and sprite `Characters/Monsters/CorruptMummy`.
- Prepare the player with a reliable vanilla weapon if needed.
- Run `combat.attack` toward the mummy.
- Wait for the matching monster's `health` to drop below `2000`.
- Capture a final frozen screenshot and assert the health-delta condition with a
  meaningful label.

The scenario should use SVE coordinates only in the SVE repo test. Frobby itself
should remain content-agnostic.

## Follow-Up Coverage

After first-pass damage coverage is stable, later hardening can add:
- Death/removal waits for monsters that have deterministic kill mechanics.
- Drop validation through existing or expanded `state.location.objects` filters.
- Player hazard/combat damage assertions using `state.player.health`.
- Stronger target selection if multiple monsters of the same type occupy nearby
  tiles.

## Testing Strategy

Use TDD for each behavior.

Frobby tests:
- Protocol serialization for any new request/result DTOs.
- Harness unit tests around `combat.attack` argument validation and result shape.
- Runner tests proving `wait.location_content` numeric comparisons and zero-count
  behavior before implementation.
- DSL tests if a C# facade is added.

SVE tests:
- Headless run of the new scenario 12.
- Re-run scenario 10 to ensure existing monster observation remains stable.
- Run targeted Frobby unit suites covering location content waits, monster projection,
  combat action dispatch, protocol serialization, and runner behavior.

## Risks

- Stardew combat may require selected weapon state and farmer animation timing; the
  scenario should use polling on monster health instead of fixed sleeps.
- Mummies may be damaged without being easily killed; first-pass coverage intentionally
  stops at health delta.
- Multiple FTM spawns can create noisy monster collections; the scenario should filter
  by tile, sprite, and max health to target the deterministic guard.
- Headless rendering/input timing can differ from visible runs; verification should
  use headless by default.
