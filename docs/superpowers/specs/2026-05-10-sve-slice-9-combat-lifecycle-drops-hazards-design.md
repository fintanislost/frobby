# SVE Slice 9: Combat Lifecycle, Drops, And Player Hazards Design

## Context

Slice 8 proved that Frobby can perform a player-like melee attack and wait for a
monster health delta in Stardew Valley Expanded. That is enough to validate the
combat input path, but not enough to validate the outcomes a mod developer usually
cares about: whether a monster can be defeated, whether it leaves loot or debris,
whether contact/projectile hazards affect the player, and whether modded combat
patches change monster danger as intended.

SVE is a good pressure test for the next layer because it has several combat styles:
deterministic Farm Type Manager monster spawns, custom loot definitions, high-health
patched monsters, and the `DisableShadowAttacks` patch that should make shadow
monsters passive after event `1090508`. Frobby should use those as real-world proof
cases while keeping all new tools generic.

## Goals

- Add neutral location debris observation so tests can detect dropped items and other
  transient world debris after combat.
- Let scenarios wait on player health changes without fixed sleeps.
- Let scenarios wait for monster removal using existing `wait.location_content`
  zero-count behavior, with enough targeting support to keep repeat attacks stable.
- Add one SVE proof that extends combat past a health delta into lifecycle or drop
  observation.
- Add one SVE proof for disabled shadow combat behavior after event `1090508`, using
  generic monster/player state assertions.

## Non-Goals

- Do not parse SVE's FTM content packs inside Frobby.
- Do not encode SVE event IDs, monster names, locations, or loot rules in Frobby.
- Do not add direct monster kill or direct loot-spawn mutation as the proof path.
- Do not attempt to simulate full combat AI. Slice 9 should observe real runtime state
  and drive player-like attacks only as far as needed for deterministic assertions.
- Do not solve special orders, fishing, buffs, festivals, or object/chest interactions;
  those stay in later backlog slices.

## Approaches Considered

### Outcome-First Observation

Add debris projection, player-state waits, and scenario flows that use existing
combat actions. This is the recommended path because it proves real runtime outcomes
while keeping Frobby's API surface small and reusable.

### Combat Control Expansion

Add richer combat commands such as "attack nearest matching monster" or "repeat until
matching monster is gone." This may be necessary if fixed tile targeting flakes when
monsters move, but it should be runner-side targeting over generic monster state, not
an SVE-specific harness command.

### Direct Fixture Mutation

Add commands that directly damage, kill, or spawn loot. This would be useful for some
future fixture setup tasks, but it would not prove player-facing gameplay behavior.
It should not be the Slice 9 proof.

## Recommended Design

### Location Debris State

Add an additive `debris` collection to the `state.location` response. This should
project Stardew `Debris` instances with best-effort fields:

- `tile`: approximate tile location when available.
- `pixel`: optional pixel position for moving debris.
- `kind`: runtime debris class or broad kind.
- `item_id` and `qualified_id`: item identity when the debris wraps an item.
- `name`: item display/name when available.
- `stack`, `quality`, and `category`: optional item metadata.
- `runtime_type`: CLR runtime type for debugging modded debris.

The projection should be reflection-tolerant. Not all debris is item debris, and tests
should be able to filter by the fields that exist without failing the whole snapshot.

### Debris Waits

Extend `wait.location_content` so `collection: "debris"` is valid. It should reuse
the existing filtering model where possible:

- string filters: `name`, `kind`, `id`, `qualified_id`, `runtime_type`
- tile filters: `x`, `y`
- numeric filters where applicable: `stack`, `quality`, `category`
- count filters: `min_count` and `max_count`, including zero

This gives combat tests a stable way to wait for dropped loot or confirm no matching
drop appeared.

### Player Health Waits

Add a runner-side `wait.player` action that polls `state.player`. Slice 9 only needs
health comparisons, but the shape should leave room for later player-state waits.

Initial fields:

- `health`, `health_lt`, `health_lte`, `health_gt`, `health_gte`
- optional `location`, `x`, `y` filters for context
- `timeout_ms` and `poll_ms`

This should not require a new harness RPC because `state.player` already exposes
health, location, and tile.

### Combat Target Stability

Keep `combat.attack` as the player-like input primitive. If SVE lifecycle tests prove
fixed-tile repeat attacks are too brittle, extend the runner action with an optional
monster selector:

- `target.collection`: implicitly `monsters`
- `target` filters matching `wait.location_content` monster filters
- `max_distance`: optional search radius from the farmer
- `repeat` and `delay_ticks`: already runner-managed

The runner can poll `state.location`, pick the nearest matching monster each repeat,
and call the existing harness `combat.attack` against that monster's current tile.
The harness remains content-agnostic and still only performs one player-like attack.

### SVE Proof Scenario: Lifecycle Or Drop

Preferred scenario:

- Load the existing deterministic fixture.
- Give the player a reliable vanilla weapon.
- Warp to a deterministic SVE combat area with a killable target.
- Wait for the target monster by generic metadata.
- Repeat player-like attacks until the matching monster count reaches zero.
- Wait for matching item debris when the target has stable loot, or assert removal
  only if the loot is probabilistic.
- Capture a frozen final screenshot.

The current Crimson Badlands corrupt mummy is useful for damage, but it may be a poor
death target because mummy mechanics can require special handling. During planning,
prefer a lower-health deterministic SVE spawn such as a Highlands shadow, slime,
bat, or other non-mummy target if it can be made stable.

### SVE Proof Scenario: Passive Shadows

Add a separate SVE scenario for the `DisableShadowAttacks` behavior:

- Add event `1090508` to the player with the existing neutral event-seeding tool.
- Warp to `Custom_HighlandsCavern`.
- Wait for a shadow monster spawn.
- Assert the runtime monster reports `damage = 0` and very high `health/max_health`
  after the patch has applied.
- Optionally use `wait.player` to prove the farmer health does not drop while near
  the passive target, if this can be made deterministic without depending on monster
  pathing.

This scenario is SVE-specific only in the test file. Frobby only exposes generic
monster and player state.

## Testing Strategy

Use TDD for each Frobby behavior.

Frobby unit tests:

- Protocol serialization for `LocationState.debris` and `DebrisSummary`.
- Harness projection tests for item debris, non-item debris, and missing/unknown
  fields.
- Runner tests proving `wait.location_content` supports `debris` collection filters.
- Runner tests proving `wait.player` health comparisons, location filters, timeout
  messages, and validation.
- Runner tests for optional selector-based combat targeting if implemented.

SVE verification:

- Run the new Slice 9 scenario(s) headlessly.
- Re-run SVE scenario 12 to prove existing combat damage coverage still works.
- Re-run the closest existing FTM monster scenario to ensure location content waits
  and monster projection were not regressed.

Full Frobby verification:

- Targeted test suites for Protocol, Harness, Runner, Runner.Dsl, and Runner.Mcp
  areas touched by the implementation.
- Full build before commit.

## Risks And Mitigations

- Monster movement can make fixed-tile repeated attacks flaky. Mitigation: add
  runner-side selector targeting only if the first proof scenario needs it.
- Stardew item drops may be probabilistic. Mitigation: choose a deterministic drop
  target where possible; otherwise assert monster removal first and keep drop
  validation to a stable separate case.
- Debris internals vary by Stardew version and debris kind. Mitigation: project
  best-effort fields and avoid requiring all fields for every debris instance.
- Shadow passivity may depend on timing after warp or time change. Mitigation: poll
  monster state and use existing event seeding before entering the location.

## Completion Criteria

- Frobby exposes neutral debris state through `state.location`.
- Frobby can wait on player health through `wait.player`.
- SVE has at least one Slice 9 scenario proving combat lifecycle/removal or stable
  drops.
- SVE has passive-shadow coverage for event `1090508` if the runtime behavior is
  stable enough under headless tests.
- Existing SVE combat and FTM monster scenarios still pass headlessly.
- The Frobby TODO marks Slice 9 as Done only after implementation, docs, and SVE
  verification are complete.
