# SVE Slice 22 Player-Like Bomb Placement Design

## Goal

Add a generic Frobby inventory-object placement primitive so scenarios can test
player-like placed-object flows, then prove it against SVE by placing an actual
inventory bomb in `Frobby_CombatLab`, waiting through its fuse, and validating
that a real SVE/FTM corrupt mummy is removed by the resulting explosion.

This slice follows Slice 21. `world.explode_tile` is the direct deterministic
explosion primitive; Slice 22 should cover the more player-real path where an
inventory object is selected, placed into the world, allowed to tick naturally,
and observed through normal world state.

## Context

Frobby already has several adjacent capabilities:

- `player.give_item` can add vanilla or modded items to the farmer inventory.
- `world.place_inventory_furniture` moves furniture from inventory into a
  loaded location.
- `world.place_object` directly creates and inserts a location object.
- `world.use_tool` exercises a selected player tool against the current
  location.
- `state.location.objects` and `wait.location_content` can observe placed
  runtime objects.
- `world.explode_tile` can trigger the final explosion behavior directly.

The missing capability is the middle path between direct insertion and direct
explosion: "use this inventory object at this tile and let the game process the
placed object." Bombs are the first pressure case, but the primitive should be
useful for other modded placeable objects too.

## Recommended Approach

Add a new harness RPC and scenario action:

- `world.place_inventory_object`

The action selects a matching inventory `StardewValley.Object`, invokes
Stardew's native object placement behavior at a tile in the player's current
location, and reports the selected slot plus stack before/after. It should not
directly insert into `location.Objects`, directly create an explosion, or
special-case bomb IDs.

The `location` field should be treated as a guard for the current location, like
`world.use_tool`, rather than a remote mutation target. This keeps the action
player-like: tests should warp the farmer to the intended location before
placing the object.

## Request Shape

Example JSON scenario step:

```json
{
  "action": "world.place_inventory_object",
  "args": {
    "id": "(O)287",
    "location": "Frobby_CombatLab",
    "x": 9,
    "y": 8
  }
}
```

Request fields:

- `id`: required inventory item id. Match either `QualifiedItemId` or `ItemId`;
  prefer qualified ids such as `(O)287` in scenarios.
- `location`: optional current-location guard. If supplied, it must match the
  farmer's current location.
- `x` and `y`: required non-negative tile coordinates.
- `slot`: optional inventory slot override for ambiguous inventory states. If
  omitted, select the first matching inventory slot.
- `facing`: optional player facing direction before placement if the native
  placement path depends on it.

The first implementation should place one object from the selected stack. Stack
splitting beyond native Stardew behavior is out of scope.

## Response Shape

Example response:

```json
{
  "ok": true,
  "tick": 123456,
  "id": "287",
  "qualified_id": "(O)287",
  "name": "Bomb",
  "location": "Frobby_CombatLab",
  "tile": { "x": 9, "y": 8 },
  "source_slot": 12,
  "stack_before": 2,
  "stack_after": 1,
  "runtime_type": "Object",
  "placed": true
}
```

The response is diagnostic. Scenarios should assert effects through
`wait.location_content`, `state.location`, and screenshots.

## Object Lifecycle Observation

Extend `state.location.objects` with best-effort lifecycle fields for placed
objects:

- `minutes_until_ready`: read from `minutesUntilReady`, `MinutesUntilReady`, or
  equivalent wrapped values when exposed.

Extend `wait.location_content` object filters with numeric comparison support:

- `minutes_until_ready`
- `minutes_until_ready_lt`
- `minutes_until_ready_lte`
- `minutes_until_ready_gt`
- `minutes_until_ready_gte`

This keeps the fuse observation generic. Bombs are one use case, but the field
also applies to machines, crops-as-objects, timed objects, or modded placeables
that expose the same runtime state.

## Placement Semantics

The handler should follow native Stardew placement behavior as closely as
possible:

- select an item already in the farmer inventory;
- verify it is a placeable `StardewValley.Object`;
- set the current tool/item slot if needed;
- invoke the object's placement path against the current location and tile;
- leave inventory stack mutation to the native placement path when possible;
- report failure if the native placement path rejects the tile.

The handler must not:

- create a new object from `ItemRegistry` for placement;
- insert directly into `location.Objects` for the main path;
- delete monsters or objects directly;
- encode SVE, Farm Type Manager, or bomb-specific IDs.

If native placement requires pixel coordinates, the handler should use tile
coordinates converted to the usual 64-pixel tile space, matching nearby tool
usage conventions.

## Error Handling

The handler should validate before placement:

- reject calls before a world is loaded;
- reject missing or blank `id`;
- reject unknown current location when placement needs one;
- reject current-location guard mismatches;
- reject missing or negative `x` / `y`;
- reject unknown `slot` values;
- reject a selected inventory item that is not a `StardewValley.Object`;
- reject native placement failure with a useful `GameStateInvalid` message.

Validation should follow nearby handler style and use structured JSON-RPC
errors.

## SVE Proof Scenario

Add a new SVE scenario:

- `tests/sdv/30-sve-combat-lab-bomb-mummy.test.json`

Preferred flow:

1. Load the SVE core profile and standard fixture.
2. Set time/weather and cross a day boundary so Farm Type Manager spawns the
   Crimson Badlands corrupt mummy.
3. Warp to `Custom_CrimsonBadlands` near the known guard spawn.
4. Freeze and wait for the corrupt mummy at tile `(20,144)` with sprite texture
   `Characters/Monsters/CorruptMummy`.
5. Relocate that exact runtime mummy into `Frobby_CombatLab` with label
   `corrupt-mummy`.
6. Give the farmer SVE's Monster Splitter and a vanilla bomb.
7. Warp to the lab and use existing combat to put the mummy into its
   downed/revive lifecycle state.
8. Place the inventory bomb at or near the labelled mummy with
   `world.place_inventory_object`.
9. If the farmer is within the expected blast radius after placement, move or
   warp the farmer to a safe tile before the fuse completes.
10. Wait for the placed bomb object to appear. Include a `minutes_until_ready`
    filter when the runtime exposes the field.
11. Wait for the placed bomb object to disappear.
12. Wait for zero lab monsters with label `corrupt-mummy`.
13. Freeze and capture the final screenshot.

This scenario proves inventory-backed placement plus fuse timing. It should not
use `world.explode_tile` except as a separate diagnostic probe during
implementation debugging.

## Alternatives Considered

### Bomb-Specific `world.place_bomb`

This would be fast but too narrow. It would likely accumulate assumptions about
vanilla bomb IDs, radius, fuse timing, and player damage. A generic inventory
object placement action is more reusable and cleaner for mod developers.

### Direct `world.place_object`

Frobby already has direct object creation/insertion. That is useful setup, but
it does not prove inventory consumption or native placement behavior. Slice 22
needs the player-like inventory path.

### Hotbar Click Placement

Clicking through the hotbar and world is the most user-real path, but it adds
screen coordinates, active toolbelt UI, viewport position, and input timing. It
is a good later layer once semantic inventory-object placement is stable.

## Testing Strategy

Use TDD.

Protocol tests:

- serialize and deserialize `PlaceInventoryObjectRequest` /
  `PlaceInventoryObjectResult` with snake-case JSON.

Harness tests:

- reject missing id, missing coordinates, negative coordinates, invalid slot,
  world-not-ready, and current-location guard mismatch;
- reject inventory items that are not objects;
- invoke the placement service for a valid object and report slot/stack
  diagnostics;
- project `minutes_until_ready` from fake objects.

Runner tests:

- pass through `world.place_inventory_object`;
- include a readable report label;
- filter object waits with `minutes_until_ready_*`.

DSL tests:

- add `World.PlaceInventoryObject(...)` and deserialize the result.

Live verification:

- run the new SVE scenario 30 headlessly;
- rerun SVE scenario 29 to ensure the direct explosion primitive still works;
- rerun SVE scenarios 27 and 28 as adjacent Combat Lab regressions;
- run focused Frobby protocol, harness, runner, and DSL tests touched by the
  slice;
- run the broad Frobby unit suites and build before committing implementation.

## Documentation

Update Frobby docs with:

- the `world.place_inventory_object` RPC schema;
- `minutes_until_ready` object projection and wait filters;
- a JSON quickstart example for inventory-backed object placement;
- guidance that `world.place_inventory_object` is player-like placement, while
  `world.place_object` is direct setup and `world.explode_tile` is direct
  explosion.

Update SVE docs with the new scenario summary and why it exists: validating that
Frobby can exercise a real inventory bomb placement/fuse flow against a real
mod-spawned mummy.

## Follow-Up Work

After this slice, a later input-level slice can cover hotbar selection and
mouse-click placement for full UI parity. That should build on the semantic
coverage here rather than replace it.
