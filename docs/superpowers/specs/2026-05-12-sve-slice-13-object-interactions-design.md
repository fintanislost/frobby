# SVE Slice 13: Object Placement And Patched Interactions Design

## Context

Slice 12 added neutral player-effect state and a SVE proof for timed swim buffs.
The next Stardew Valley Expanded pressure point is object interaction. SVE patches
standard Stardew object behavior for its Golden Piggy Bank, relocates and edits
festival chests, and changes buried reward locations such as Secret Note #18.

Frobby can already inspect basic `state.location.objects` entries and can call
`world.interact_tile` for furniture or existing placed objects in the current
location. It cannot yet place normal Stardew objects or big craftables into a
loaded location, and object summaries do not expose enough metadata to assert
that a placed item is a big craftable, a chest, or another modded runtime object.
That leaves patched object interactions hard to test without manual setup.

## Goals

- Add a neutral `world.place_object` RPC for Stardew `Object` and big-craftable
  placement.
- Enrich `state.location.objects` with additive metadata useful for object
  interaction tests.
- Extend runner-side `wait.location_content` object filters where needed so tests
  can wait for placed object metadata without sleeps.
- Add one SVE proof scenario that validates the Golden Piggy Bank interaction
  through real `world.interact_tile` behavior.
- Keep all Frobby production code mod-agnostic.

## Non-Goals

- Do not hardcode SVE item IDs, locations, or piggy-bank behavior in Frobby
  production code.
- Do not implement chest inventory editing in this first pass. Chest contents are
  a follow-up after object placement and object metadata are stable.
- Do not implement hoe/dig tooling or Secret Note buried reward tests in this
  first pass.
- Do not change `world.interact_tile` into a generic action router. It should
  continue to invoke Stardew's furniture/object interaction path.
- Do not require festival setup. Spirit's Eve chest coverage belongs with the
  festival and special-map slice.

## Approaches Considered

### Recommended: Generic Object Placement And Object State

Add `world.place_object` alongside the existing `world.place_furniture` handler.
Create objects through Stardew's `ItemRegistry`, place them into a loaded
location's object dictionary, and project richer object metadata through
`state.location`. This gives scenario authors a deterministic setup primitive and
keeps the SVE proof focused on the patched interaction itself.

This is the smallest neutral slice that proves SVE object patches can be tested.
It also lays the foundation for later chest and buried-reward scenarios.

### Chest-First

Project chest contents and storage metadata before adding object placement. This
would help validate relocated festival chests, but it is coupled to festival map
setup and special runtime timing. It is better as a follow-up once neutral object
placement exists.

### Buried-Reward-First

Add tool-use or hoe/dig support and validate SVE's Secret Note #18 buried reward
tile. This directly covers one SVE pressure point, but it introduces tool
selection, tile mutation, inventory/debris deltas, and progression prerequisites.
Those are useful but riskier than the object-placement foundation.

## Recommended Design

### Object Placement RPC

Add `world.place_object`.

Request fields:

- `id`: required item id. Qualified ids such as `(O)Stone` or
  `(BC)SomeBigCraftable` should be accepted when Stardew's `ItemRegistry` accepts
  them.
- `location`: optional location name. If omitted, use the current location.
- `x`: required tile x, non-negative.
- `y`: required tile y, non-negative.
- `stack`: optional stack value. Default to the created object's stack.
- `quality`: optional quality value. Default to the created object's quality.
- `remove_existing`: optional boolean. When true, remove an existing object at
  the target tile before placement.

The handler should require a loaded world, resolve the location with the same
rules as `world.place_furniture`, validate that the item exists, create it through
`ItemRegistry`, and reject non-`StardewValley.Object` items with a clear error.

For successful placement, set stack and quality only when supplied, insert the
object into `location.Objects` at the tile, and return:

- `tick`
- `id`
- `qualified_id`
- `name`
- `location`
- `tile`
- `big_craftable`
- `runtime_type`

The handler should not attempt to simulate player inventory, placement sounds, or
collision rules. It is a deterministic test setup action, matching the style of
existing Frobby setup helpers.

### Object State Metadata

Extend `ObjectSummary` with additive fields:

- `runtime_type`: CLR type name for diagnostics.
- `big_craftable`: true when the object is a big craftable.
- `ready_for_harvest`: best-effort nullable boolean when Stardew exposes a stable
  ready/harvest state.
- `held_object_id`: optional id for machines or objects holding another object.
- `held_object_qualified_id`: optional qualified id for the held object.
- `held_object_name`: optional display/internal name for the held object.

Only `runtime_type` and `big_craftable` are required for the Golden Piggy Bank
proof. Held-object fields are additive foundation for machines and chest-like
objects if they are stable to read without fragile reflection. Chest contents are
intentionally excluded from this first pass.

### Runner Wait Support

Extend `wait.location_content` object filtering so scenarios can wait for:

- `runtime_type`
- `big_craftable`
- `held_object_id`
- `held_object_qualified_id`

The existing tile, name, id, qualified id, stack, quality, and count filters
remain unchanged. Timeout diagnostics should include the last matched/total
counts and enough object metadata to explain common failures.

### SVE Proof Scenario

Add a SVE scenario, likely
`tests/sdv/18-sve-object-piggy-bank-interaction.test.json`.

Initial proof flow:

1. Load the standard core SVE fixture.
2. Warp to a stable location and tile with space for an object.
3. Set player money to a known value such as `5000`.
4. Place `(BC)FlashShifter.StardewValleyExpandedCP_Golden_Piggy_Bank` with
   `world.place_object`.
5. Wait for `state.location.objects` at that tile with `big_craftable: true` and
   the expected qualified id or name.
6. Call `world.interact_tile` at the tile.
7. Assert `state.player.money == 4999`.
8. Assert the object still exists at the tile.
9. Capture a frozen final screenshot for report context.

If the qualified id form differs under Content Patcher or Stardew's item registry,
the scenario should use the id form that works through `ItemRegistry`. Frobby
production code should remain generic either way.

## Documentation And Schema

Update:

- `docs/rpc-schema.md` for `world.place_object` and new object metadata.
- `docs/dsl-quickstart.md` with a short placed-object interaction example.
- `schemas/scenario.schema.json` so scenario validation accepts
  `world.place_object` and new `wait.location_content` filters.
- `SVE_FROBBY_CAPABILITY_TODO.md` to mark Slice 13 active and then done.

## Test Strategy

Use TDD for each behavior:

- Protocol serialization tests for the new object metadata and placement result.
- Harness tests for `world.place_object` validation, non-object rejection,
  remove-existing behavior, stack/quality handling, and successful placement.
- Harness projection tests for `runtime_type`, `big_craftable`, and held-object
  fields where stable.
- Runner tests for object metadata filters in `wait.location_content`.
- A focused headless SVE scenario run proving the Golden Piggy Bank interaction.
- A small Starberg smoke run only if shared runner behavior changes beyond
  object-filter matching.

## Risks And Mitigations

- Stardew item ids can differ between object and big-craftable namespaces.
  Mitigation: accept whatever `ItemRegistry` accepts and document scenario ids
  using qualified ids when possible.
- Some object metadata may not be stable across Stardew versions or subclasses.
  Mitigation: require only `runtime_type` and `big_craftable`; make held-object
  fields best-effort and omit unavailable values.
- Direct object insertion bypasses placement restrictions. Mitigation: document
  `world.place_object` as a deterministic setup action, not an end-to-end player
  placement simulation.
- The piggy bank requires the player not to hold an active object. Mitigation:
  the scenario should start from a fixture state with no active object or clear it
  through existing neutral setup if needed.

## Completion Criteria

- `world.place_object` can place Stardew objects and big craftables in loaded
  locations.
- `state.location.objects` exposes at least `runtime_type` and `big_craftable`.
- `wait.location_content` can filter objects by the new stable metadata.
- Docs and schema describe the new surface.
- Frobby unit tests pass.
- A headless SVE scenario proves the Golden Piggy Bank decreases player money
  through the real placed-object interaction path.
