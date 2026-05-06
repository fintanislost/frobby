# SVE Frobby Capability TODO

Purpose: use Stardew Valley Expanded as a broad mod testbed to identify neutral Frobby capabilities needed beyond Starberg's UI-heavy coverage. Keep every Frobby addition mod-agnostic; SVE scenarios should prove the capability against a real complex mod, not bake SVE assumptions into Frobby.

Status key:
- Pending: not designed yet.
- Planning: design or implementation plan in progress.
- Active: implementation underway.
- Done: implemented, documented, and verified against SVE.

## Capability Slices

- [ ] Active: Slice 1, custom locations, maps, warps, and tile actions.
  - SVE pressure: many Content Patcher `CustomLocations`, custom map assets, `TouchAction`/`MagicWarp`/`LoadMap` style behavior, and code patches around location warps.
  - Frobby goal: let tests prove custom locations exist, can be entered, expose map/tile metadata, and support player-like tile action flows.
  - Implementation plan: `docs/superpowers/plans/2026-05-05-sve-slice-1-location-map-tools.md`.
  - Done: introspection foundation (`state.locations`, expanded `state.location`, `state.map_tile`, and `wait.location`) verified against SVE scenario 02.
  - Pending Slice 1 follow-up: tile-action execution (`world.interact_tile_action` or equivalent), tile-action candidate discovery, and SVE scenario 03.

- [x] Done: Slice 2, events and cutscenes observability foundation.
  - SVE pressure: event scripts, world-change events, actor positioning, viewport corrections, grange judging patches, dialogue during scripted scenes.
  - Frobby goal: trigger events deterministically, advance or skip event scripts, assert current event id, actors, dialogue text, camera/viewport, and event completion.
  - Design spec: `docs/superpowers/specs/2026-05-05-sve-slice-2-event-observability-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-05-sve-slice-2-event-observability.md`.
  - Done: `state.event`, `event.start`, `event.skip`, runner-side `wait.event_active` / `wait.event_complete`, readable dialogue/menu text extras, and SVE scenario 03 (`sve_event_observability_krobus`).
  - Pending Slice 2 follow-up: event dialogue choice selection, reliable generic menu/global-overlay acknowledgement, script step advancement without full skip, and event-seen/mail mutation helpers for relationship-gated scenes.

- [x] Done: Slice 3, Content Patcher asset coverage.
  - SVE pressure: CP `Load` and `Edit*` actions for maps, strings, data assets, portraits, sprites, recolors, and config-gated patches.
  - Frobby goal: inspect loaded asset names and selected asset metadata, prove expected CP assets are available, and verify map/texture assets without relying only on full screenshots.
  - Design spec: `docs/superpowers/specs/2026-05-06-sve-slice-3-runtime-content-assets-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-06-sve-slice-3-runtime-content-assets.md`.
  - Done: runtime `content.asset` query plus JSON scenario assertions for maps, textures, strings, bounded data dictionaries, and selected nested data objects.
  - Verified: SVE scenario 04 (`tests/sdv/04-sve-content-assets-runtime.test.json`) validates CP-loaded maps and `Data/Locations` runtime metadata under headless execution.

- [ ] Planning: Slice 4, NPC schedules, dialogue, and relationships.
  - SVE pressure: many custom NPCs, custom homes, schedules, movie-theater strings, relationship-gated content, and post-event dialogue patches.
  - Frobby goal: set relationship and mail state, locate NPCs, move time/date, interact with NPCs, and assert speaker/text/location state.
  - Design spec: `docs/superpowers/specs/2026-05-06-sve-slice-4-npc-schedules-dialogue-relationships-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-06-sve-slice-4-npc-schedules-dialogue-relationships.md`.
  - First target: `state.npcs`, expanded `state.npc`, `player.set_friendship`, parameterized `state.assert`, runner-side `wait.npc_location`, and SVE scenario 05 against Sophia.

- [ ] Pending: Slice 5, Farm Type Manager spawn and conditional world content.
  - SVE pressure: FTM pack content, conditional forage/monster spawns, location-specific spawn rules, config/mail-gated difficulty variants.
  - Frobby goal: control spawn-relevant state, wait for spawns, inspect objects/monsters/critters in a location, and assert spawn counts/types deterministically.

- [ ] Pending: Slice 6, custom items, inventory, rewards, and shops.
  - SVE pressure: custom items, special rewards, secret-note/buried-item patches, shops and rewards that may reference modded qualified item ids.
  - Frobby goal: give/assert qualified item ids, inspect inventory and shops, trigger reward flows, and validate item icons/sprites when needed.

- [ ] Pending: Slice 7, sprites, temporary animations, lighting, and weather-like visual effects.
  - SVE pressure: temporary animated sprites, custom cauldron effects, map lighting changes, recolors, mist effects, and location-specific ambience.
  - Frobby goal: expose enough render/state metadata to assert animated sprites and lighting effects without brittle whole-screen diffs.

## Slice 1 Planning: Custom Locations, Maps, Warps, And Tile Actions

### Current Frobby Surface

Available now:
- `player.warp` queues a warp to a named location and tile.
- `state.location` returns current or named location with `name`, `is_outdoors`, NPCs, objects, furniture, and terrain.
- `world.interact_tile` interacts with furniture or placed objects in the current location.
- `draw.*`, `bitmap.capture`, `screenshot.*`, and `freeze.*` can verify the rendered outcome once a location is loaded.

Observed gaps for SVE-style map testing:
- No way to list loaded locations or assert that many CP custom locations registered.
- `state.location` does not report map dimensions, map asset name, layers, warps, tile properties, or location context.
- No neutral RPC for inspecting a specific tile's layer properties such as `TouchAction`, `Action`, `Passable`, `NoSpawn`, or map-defined warp strings.
- `world.interact_tile` does not exercise map tile actions; it only checks furniture and objects.
- Scenario authors must guess when a queued warp has settled unless they write repeated `state.player` checks.

### Recommended Approach

Use a state-first map introspection slice, then layer one SVE scenario on top.

This keeps Frobby neutral and gives future mod tests durable primitives:
- `state.locations` returns loaded location summaries.
- `state.location` grows map metadata and warp summaries.
- `state.map_tile` returns tile/layer metadata for one coordinate.
- `world.interact_tile_action` or an enhanced `world.interact_tile` can trigger map tile actions in the current location after tile metadata is observable.

Alternatives considered:
- Image-first smoke only: easy to add, but it would not tell us whether the map, tile properties, or warp metadata are correct.
- SVE-only scenario research first: useful for examples, but risks hiding the missing Frobby primitives behind one-off coordinates.

### Candidate Frobby Capabilities

1. `state.locations`
   - Response: list of loaded locations with `name`, `unique_name`, `is_outdoors`, optional `map_width`, `map_height`, and optional `context_id`.
   - Tests: prove custom CP locations are visible after SVE loads, starting with known names such as `Custom_TownEast`, `Custom_GrandpasShed`, and `Custom_EnchantedGrove`.

2. Expanded `state.location`
   - Add non-breaking fields:
     - `unique_name`
     - `map_width`
     - `map_height`
     - `map_asset`
     - `warps`: normalized destination summaries from `GameLocation.warps`
   - Keep existing fields stable so current Starberg tests do not change.

3. `state.map_tile`
   - Request: `location`, `x`, `y`, and optional `layers`.
   - Response: layer entries with tile index, tilesheet id, and tile properties.
   - Needed properties: `Action`, `TouchAction`, `Passable`, `NoSpawn`, `Water`, and any raw key/value map properties present at the tile.

4. Tile action execution
   - Preferred action name: `world.interact_tile_action`.
   - Request: `x`, `y`, optional `location`, optional `just_checking_for_activity`.
   - Behavior: run the same map tile action path a player would hit when activating or stepping onto a tile, returning whether it handled and any resulting location after the warp settles if feasible.
   - This should come after `state.map_tile` so we can debug failures with metadata instead of guessing coordinates.

5. Runner convenience wait
   - Add a scenario-level helper/action for "wait until player is in location X at tile Y or timeout".
   - Could be implemented entirely in Runner using existing `state.player` polling before requiring a harness RPC.

### Candidate SVE Scenarios

1. `01-sve-core-loads` stays as the baseline mod-load smoke.

2. `02-sve-custom-locations-register`
   - Assert `state.locations` contains key SVE locations:
     - `Custom_TownEast`
     - `Custom_GrandpasShed`
     - `Custom_EnchantedGrove`
   - Assert each has nonzero map dimensions.

3. `03-sve-custom-location-warp`
   - Warp directly to `Custom_TownEast`.
   - Wait until `state.player.location == Custom_TownEast`.
   - Capture under freeze.
   - Assert `state.location.name` and map dimensions.

4. `04-sve-map-tile-properties`
   - Inspect one known SVE map tile with a warp or action property.
   - Assert the tile property key/value is visible through `state.map_tile`.
   - Exact coordinate should be selected during implementation by reading SVE map metadata or by probing a loaded map.

5. `05-sve-tile-action-warp`
   - Move/warp to the source tile for a known `TouchAction` or `Action` warp.
   - Trigger `world.interact_tile_action`.
   - Wait until the destination location is reached.
   - Assert current location, player tile, and final screenshot.

### First Implementation Target

Start with `state.locations` and expanded `state.location` metadata before adding tile action execution.

Reasoning:
- It gives immediate coverage for SVE custom map registration.
- It is low risk for existing Starberg scenarios because it only adds response fields.
- It creates a debugging foundation for choosing reliable tile-action coordinates later.

### Open Questions

- Should `state.locations` include all loaded `Game1.locations`, only locations currently in memory, or both with a `loaded` flag if discoverable?
- Should map asset names come from SMAPI/content metadata when available, or should Frobby initially expose only runtime map dimensions/properties?
- Should tile-action execution be a separate RPC (`world.interact_tile_action`) or an option on `world.interact_tile`?
