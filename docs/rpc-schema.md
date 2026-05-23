# JSON-RPC 2.0 Schema

Protocol for runner ↔ harness communication. All methods documented here. Keep this file in sync with implementation — new method without schema entry is a review blocker (see @.claude/rules/commit-style.md).

## Transport

- Linux/macOS: Unix domain socket at `$SDV_TEST_SOCKET` (implemented, D1.1)
- Windows: Named pipe `\\.\pipe\sdv-test-<pid>` (future)
- Framing: one JSON-RPC message per line (newline-delimited JSON)
- Encoding: UTF-8

Implementation: `src/Protocol/` for the codec + session, `src/Protocol/UnixSocketRpc.cs` for the socket binding. The server binds in `ModEntry.OnGameLaunched` so SMAPI initialization finishes before the first connection attempt.

## Handshake

Harness sends `ready` notification immediately after the runner connects. The payload carries versions the runner can use for compatibility checks:

```json
{ "jsonrpc": "2.0", "method": "ready", "params": { "version": "0.1.0", "sdv": "1.6.15", "smapi": "4.5.2" } }
```

Runner must see this within 60s of launching SDV or times out.

Implemented and tested in `tests/Protocol.Tests/UnixSocketRpcTests.cs`.

## Methods

Scenario files may include top-level `"profile": "profile-id"` when run through
`sdv-test repo run`. The profile is resolved from `sdv-test.config.json` before
Stardew launches.

### scenario.begin

Opens a new scenario session. Pins `Game1.random` to `params.seed`, resets the process-wide
`ScenarioState` singleton, and records the start tick + wall-clock timestamp. `params.name` is
**required** (non-empty string); `params.seed` is an `int` (defaults to `0` via JSON deserialization);
`params.fixture` is accepted for forward-compatibility with D1.4 fixture loading but is ignored
by the T12 handler.

Request:
```json
→ { "jsonrpc": "2.0", "id": 1, "method": "scenario.begin", "params": { "name": "shop_shows_custom_item", "seed": 42, "fixture": "spring_day_5_clean" } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 1, "result": { "session_id": "abc123def4...", "tick": 0 } }
```

Response (missing/empty `name`, or missing `params` — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 1, "error": { "code": -32602, "message": "params.name required" } }
```

Response (a scenario is already active — ScenarioNotActive, reused for "scenario state invalid"):
```json
← { "jsonrpc": "2.0", "id": 1, "error": { "code": -32001, "message": "scenario 'shop_shows_custom_item' already active — call scenario.end first" } }
```

`session_id` is a fresh `Guid.NewGuid().ToString("N")` minted at begin time; scenarios embed it
in subsequent logs so a failing run can be traced end-to-end. `tick` is `Game1.ticks` at the
moment begin ran.

**Preconditions:** no other scenario is currently active. RNG pinning is skipped when the
handler's `Monitor` property is null (unit-test path); normal operation always pins via
`SeedPinner.Pin`.
**Side effects:** resets the `ScenarioState` singleton, sets `IsActive=true`, and (if `Monitor`
is wired) pins `Game1.random` to `new Random(req.Seed)`.
**Implemented in:** `src/Harness/Handlers/ScenarioBeginHandler.cs`
**Tested in:** `tests/Harness.Tests/ScenarioBeginHandlerTests.cs` + `tests/Harness.Tests/ScenarioStateTests.cs`.

### scenario.end

Closes the active scenario session. Returns duration (ms since begin) plus running assertion
counters, then clears the `ScenarioState` singleton so the next `scenario.begin` starts clean.
Takes no params.

Request:
```json
→ { "jsonrpc": "2.0", "id": 99, "method": "scenario.end" }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 99, "result": { "duration_ms": 342, "assertions_run": 5, "assertions_passed": 5 } }
```

Response (no scenario active — ScenarioNotActive):
```json
← { "jsonrpc": "2.0", "id": 99, "error": { "code": -32001, "message": "no scenario active" } }
```

`duration_ms` is `(DateTime.UtcNow - ScenarioState.StartUtc).TotalMilliseconds` truncated to
`int`. `assertions_run` / `assertions_passed` are whatever counters the scenario executor has
incremented on `ScenarioState` during the session (D1.4 will drive these; T12 returns the raw
values without opinion).

**Preconditions:** a scenario must be currently active (opened via `scenario.begin`).
**Side effects:** clears `ScenarioState` (sets `IsActive=false` and zeroes counters).
**Implemented in:** `src/Harness/Handlers/ScenarioEndHandler.cs`
**Tested in:** `tests/Harness.Tests/ScenarioEndHandlerTests.cs`.

### state.player

Returns the local farmer's current state, including a compact inventory snapshot.

```json
→ { "jsonrpc": "2.0", "id": 2, "method": "state.player" }
← { "jsonrpc": "2.0", "id": 2, "result": {
      "name": "Tester",
      "money": 1000,
      "stamina": 270,
      "max_stamina": 270,
      "health": 100,
      "location": "Farm",
      "tile": { "x": 64, "y": 15 },
      "mail_received": ["button_tut_1"],
      "mail_for_tomorrow": ["HenchmanMarshTonics"],
      "events_seen": ["5532011"],
      "secret_notes_seen": [18],
      "swimming": true,
      "bathing_clothes": false,
      "is_busy": false,
      "can_move": true,
      "buffs": [
        {
          "id": "1",
          "display_name": "Fishing",
          "source": "food",
          "milliseconds_duration": 720000,
          "total_milliseconds_duration": 720000,
          "effects": { "fishing_level": 3 },
          "runtime_type": "Buff"
        }
      ],
      "items": [
        {
          "slot": 5,
          "id": "(F)example_terminal",
          "item_id": "example_terminal",
          "qualified_id": "(F)example_terminal",
          "name": "Example Terminal",
          "stack": 1,
          "category": -24,
          "quality": 0,
          "runtime_type": "Furniture"
        }
      ]
   } }
```

Inventory `id` remains the backwards-compatible stable identifier. New tests should
prefer `qualified_id` for exact Stardew 1.6 item matching and `item_id` when a
scenario intentionally wants the raw unqualified id. Metadata fields are omitted
when Stardew or a mod does not expose them.
`mail_received`, `mail_for_tomorrow`, `events_seen`, and `secret_notes_seen`
expose the local farmer's save-state flags for relationship, event, mail-gated,
pending-mail, and secret-note scenario setup/verification. `mail_for_tomorrow`
is useful for trigger actions that schedule mail during day-ending without
running Stardew's full overnight sleep/save flow.
`swimming`, `bathing_clothes`, `is_busy`, and `can_move` expose transient local
farmer state for mod behavior that keys off the player's current mode. `buffs`
contains active buff summaries projected from the live Stardew buff manager. Buff
effect fields use snake-case names such as `fishing_level`, `farming_level`,
`attack`, `defense`, and `speed`.

**Preconditions:** world loaded (`Game1.gameMode == playingGameMode`). No request-time check yet; result fields will reflect title/loading-screen defaults if invoked too early.
**Side effects:** none.
**Implemented in:** `src/Harness/Handlers/StatePlayerHandler.cs`
**Tested in:** `tests/Harness.Tests/StatePlayerHandlerTests.cs` + `tests/Runner.Tests/ProbeCommandTests.cs` (end-to-end runner → harness round-trip over a real Unix socket, with a faked harness response).

### state.special_orders

Returns the local team's active, available, and completed Stardew special-order
state. This is runtime state, not content-pack source data, so it works for
vanilla orders and modded orders after the game has registered them.

Request:
```json
→ { "jsonrpc": "2.0", "id": 12, "method": "state.special_orders" }
```

Response:
```json
← { "jsonrpc": "2.0", "id": 12, "result": {
      "active": [
        {
          "key": "ExampleOrder",
          "name": "Example Order",
          "requester": "Riley",
          "order_type": "ExampleMod",
          "state": "InProgress",
          "objectives": [
            {
              "index": 0,
              "type": "Donate",
              "runtime_type": "DonateObjective",
              "drop_box": "ExampleDropBox",
              "drop_box_location": "ExampleTown",
              "accepted_context_tags": ["item_wood"],
              "current_count": 5,
              "max_count": 100,
              "complete": false
            }
          ],
          "rewards": [],
          "donated_items": [
            { "id": "(O)388", "item_id": "388", "qualified_id": "(O)388", "name": "Wood", "stack": 5 }
          ]
        }
      ],
      "available": [],
      "completed": ["CompletedExampleOrder"],
      "accepted_types": ["ExampleMod"],
      "returned_donations": []
   } }
```

Special-order projection is best-effort across Stardew and modded runtime
classes. Tests should filter on the fields relevant to the scenario, such as
`key`, `requester`, `order_type`, objective `type`, `drop_box`,
`accepted_context_tags`, and `current_count`.

**Preconditions:** world loaded.
**Side effects:** none.
**Implemented in:** `src/Harness/Handlers/StateSpecialOrdersHandler.cs`
**Tested in:** `tests/Protocol.Tests/SpecialOrdersStateSerializationTests.cs` and `tests/Harness.Tests/StateSpecialOrdersHandlerTests.cs`.

## Fishing

### state.fishing_context

Returns fishability and tile context for a location/bobber tile. Useful fields
include `is_fishable`, `blocked_reason`, `fish_area_id`, `map_fish`,
`has_no_fishing`, `tile_properties`, and `location_fish_areas`.

Request:
```json
→ { "jsonrpc": "2.0", "id": 13, "method": "state.fishing_context", "params": {
      "location": "Beach",
      "x": 45,
      "y": 12,
      "season": "spring",
      "time_of_day": 900,
      "weather": "sunny"
   } }
```

### state.fishing_table

Returns projected candidate catches for the same context. Candidates can come
from legacy map `Fish` properties, `Data/Fish`, `Data/Locations`, or compact
runtime sources. The table is diagnostic; `fishing.sample_catch` is the
authoritative runtime proof.

Request:
```json
→ { "jsonrpc": "2.0", "id": 14, "method": "state.fishing_table", "params": {
      "location": "Beach",
      "x": 45,
      "y": 12,
      "season": "spring",
      "time_of_day": 900
   } }
```

### fishing.sample_catch

Runs bounded live Stardew catch sampling without the fishing minigame. The
sampler returns projected item results and should use `restore_state: true` for
scenario tests unless the scenario is isolated.

Request:
```json
→ { "jsonrpc": "2.0", "id": 15, "method": "fishing.sample_catch", "params": {
      "location": "Desert",
      "x": 28,
      "y": 6,
      "attempts": 10,
      "seed": 1234,
      "restore_state": true
   } }
```

### state.time

Returns the current in-game date + clock. Always succeeds (safe at title screen).

Response (in save):
```json
→ { "jsonrpc": "2.0", "id": 3, "method": "state.time" }
← { "jsonrpc": "2.0", "id": 3, "result": { "in_save": true, "season": "spring", "day_of_month": 5, "year": 1, "time_of_day": 600, "day_of_week": "monday" } }
```

Response (title screen — pre-save default):
```json
← { "jsonrpc": "2.0", "id": 3, "result": { "in_save": false, "season": "spring", "day_of_month": 0, "year": 1, "time_of_day": 600, "day_of_week": "sunday" } }
```

`in_save` is `true` when a save is loaded and the date/clock fields reflect real world state. When `false`, callers should disregard `day_of_month` etc. — they're SDV's pre-save defaults. This is the reliable signal for "is a save loaded?" from a scenario assertion.

**Preconditions:** none.
**Side effects:** none.
**Implemented in:** `src/Harness/Handlers/StateTimeHandler.cs`
**Tested in:** `tests/Protocol.Tests/TimeStateSerializationTests.cs` (DTO shape); full round-trip via the scenario engine once that lands (D1.4).

### state.location

Returns a snapshot of the current location (or a named location via `params.name`). `params` is optional — omit for current location.

Request (current location):
```json
→ { "jsonrpc": "2.0", "id": 4, "method": "state.location" }
```

Request (named location):
```json
→ { "jsonrpc": "2.0", "id": 4, "method": "state.location", "params": { "name": "ExampleDeepCave" } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 4, "result": {
      "name": "ExampleDeepCave",
      "unique_name": "ExampleDeepCave",
      "is_outdoors": false,
      "map_width": 120,
      "map_height": 80,
      "warps": [
        { "source": { "x": 64, "y": 15 }, "target_location": "ExampleTown", "target": { "x": 8, "y": 10 } }
      ],
      "npcs": [{ "name": "Pierre", "tile": { "x": 4, "y": 17 } }],
      "objects": [{ "tile": { "x": 10, "y": 10 }, "name": "Weeds", "id": "O771", "qualified_id": "(O)771", "category": -999, "stack": 1, "quality": 0, "runtime_type": "Object", "big_craftable": false, "ready_for_harvest": null, "minutes_until_ready": null, "held_object_id": null, "held_object_qualified_id": null, "held_object_name": null, "is_chest": false, "item_count": null, "items_truncated": null, "items": [] }],
      "debris": [{ "tile": { "x": 15, "y": 16 }, "pixel": { "x": 960, "y": 1024 }, "kind": "ItemDebris", "id": "769", "qualified_id": "(O)769", "name": "Void Essence", "stack": 2, "quality": 0, "category": -2, "runtime_type": "Debris" }],
      "resource_clumps": [{ "tile": { "x": 21, "y": 17 }, "kind": "ResourceClump", "id": "602", "name": "Log", "width": 2, "height": 2, "health": 10 }],
      "monsters": [{ "tile": { "x": 44, "y": 31 }, "monster_id": "frobby-monster-1", "label": "target", "spawned_by_frobby": true, "name": "Crystal Bat", "type": "CrystalBat", "health": 180, "max_health": 180, "damage": 32, "revive_timer": null, "sprite_texture": "ExampleMod/Monsters/CrystalBat" }],
      "furniture": [{ "tile": { "x": 7, "y": 8 }, "id": "(F)1302", "name": "Oak Chair" }],
      "terrain": [{ "tile": { "x": 12, "y": 12 }, "kind": "HoeDirt" }]
   } }
```

If no location is loaded (e.g. on the title screen) or the requested name is unknown, the result contains an empty-string `name` with empty `npcs`/`objects`/`furniture`/`terrain` lists.

`resource_clumps` contains large runtime world objects such as logs, stumps,
boulders, meteorites, and mine rocks when Stardew exposes them for the location.
`monsters` contains hostile creatures and is separate from `npcs`, which remains
for social/non-hostile NPCs. Monster summaries include runtime `health`,
`max_health`, `damage`, `revive_timer`, and `sprite_texture` when Stardew or the
mod exposes those values. `revive_timer` is useful for monsters with a downed
or delayed-revival lifecycle, such as mummies. Combat Lab monsters also expose
run-local `monster_id`, optional `label`, and `spawned_by_frobby`. `debris`
contains transient runtime debris
such as item drops and
visual debris. Fields are best-effort because Stardew debris can be item-backed,
animated, or purely visual. Object summaries include stable item metadata plus
runtime details such as `runtime_type`, `big_craftable`, `ready_for_harvest`,
`minutes_until_ready`, and held-object fields when Stardew exposes them. Tests
should filter only on fields relevant to the scenario. Optional object,
monster, and debris metadata fields may be empty or null when the runtime type
does not expose them.
Container objects include `is_chest`, `item_count`, `items_truncated`, and an
`items` array. Contained item summaries expose `slot`, `id`, `item_id`,
`qualified_id`, `name`, `stack`, `quality`, `category`, and `runtime_type`.
Frobby caps very large contained item lists and reports that through
`items_truncated`.

**Preconditions:** world loaded. Same note as `state.player`.
**Side effects:** none.
**Implemented in:** `src/Harness/Handlers/StateLocationHandler.cs`
**Tested in:** `tests/Protocol.Tests/LocationStateSerializationTests.cs` (DTO shape).

### state.visual_effects

Returns a runtime visual-effect snapshot for the current location, or for a named
location via `params.location`. `params` is optional; omit it to inspect the
farmer's current location. This is state-level evidence for temporary sprites,
ambient lighting, light sources, and weather debris counts. Draw, bitmap, and
screenshot tools remain the final proof for what actually rendered on screen.

Request (current location):
```json
→ { "jsonrpc": "2.0", "id": 10, "method": "state.visual_effects" }
```

Request (named location):
```json
→ { "jsonrpc": "2.0", "id": 10, "method": "state.visual_effects", "params": { "location": "Example.VisualLocation" } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 10, "result": {
      "location": "Example.VisualLocation",
      "ambient_light": [255, 240, 220, 255],
      "temporary_sprites": [
        {
          "texture_asset": "ExampleMod/Visuals/Effects",
          "source_rect": [0, 32, 16, 16],
          "position": [128.0, 256.0],
          "motion": [0.0, -0.25],
          "acceleration": [0.0, 0.0],
          "color": [255, 255, 255, 255],
          "alpha": 0.85,
          "alpha_fade": 0.0,
          "scale": 4.0,
          "scale_change": 0.0,
          "rotation": 0.0,
          "rotation_change": 0.0,
          "layer_depth": 0.73,
          "draw_above_always_front": false,
          "runtime_type": "TemporaryAnimatedSprite"
        }
      ],
      "light_sources": [
        {
          "id": "Example.VisualLight",
          "position": [160.0, 288.0],
          "color": [255, 220, 160, 255],
          "radius": 2.0,
          "texture_index": 4,
          "context": "MapLight"
        }
      ],
      "weather_debris_count": 3
   } }
```

If no current location is loaded, the result uses an empty `location` with empty
`temporary_sprites`. If a named location cannot be found, the response preserves
the requested `location` and returns empty `temporary_sprites`. Ambient light,
global light sources, and best-effort weather debris counts are still reported.
`texture_asset` is omitted when Stardew exposes a sprite texture object without a
stable asset name.

**Preconditions:** world loaded. Same note as `state.player`.
**Side effects:** none.
**Implemented in:** `src/Harness/Handlers/StateVisualEffectsHandler.cs`
**Tested in:** `tests/Protocol.Tests/VisualEffectsStateSerializationTests.cs` and `tests/Harness.Tests/StateVisualEffectsHandlerTests.cs`.

### state.locations

Returns compact summaries for all runtime-loaded Stardew locations. This is the
preferred state primitive for proving a mod's custom locations registered before
attempting direct warps or tile-action flows.

Request:
```json
→ { "jsonrpc": "2.0", "id": 5, "method": "state.locations" }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 5, "result": {
      "locations": [
        {
          "name": "Farm",
          "unique_name": "Farm",
          "is_outdoors": true,
          "map_width": 120,
          "map_height": 80,
          "warp_count": 12
        }
      ]
   } }
```

`locations` is sorted by `name`, then `unique_name`, to keep reports stable across
runs. `map_width` and `map_height` are tile dimensions from the loaded runtime map;
they are `0` if a location has no map loaded.

**Preconditions:** none beyond the game having initialized enough for `Game1.locations`.
**Side effects:** none.
**Implemented in:** `src/Harness/Handlers/StateLocationsHandler.cs`
**Tested in:** `tests/Protocol.Tests/LocationsStateSerializationTests.cs` and `tests/Harness.Tests/StateLocationsHandlerTests.cs` (live placeholder).

### state.map_tile

Returns layer/tile/property metadata for one map coordinate. Omit all params to
inspect the farmer's current tile in the current location, which also makes the
method usable from scenario state assertions such as
`state.map_tile.layers contains name 'Back'`.

Request (current farmer tile):
```json
→ { "jsonrpc": "2.0", "id": 6, "method": "state.map_tile" }
```

Request (explicit location/tile/layers):
```json
→ { "jsonrpc": "2.0", "id": 6, "method": "state.map_tile",
     "params": { "location": "Farm", "x": 64, "y": 15, "layers": ["Back", "Buildings"] } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 6, "result": {
      "location": "Farm",
      "x": 64,
      "y": 15,
      "layers": [
        {
          "name": "Back",
          "tile_index": 471,
          "tile_sheet": "outdoors",
          "properties": {
            "TouchAction": "MagicWarp ExampleAncientGrove",
            "Passable": "F"
          }
        }
      ]
   } }
```

Tile property keys are preserved exactly as Stardew/xTile exposes them; they are not
snake-cased. Empty tiles return `tile_index: -1`, empty `tile_sheet`, and empty
`properties`.

**Preconditions:** current location and player are required when params omit
`location`, `x`, or `y`.
**Side effects:** none.
**Implemented in:** `src/Harness/Handlers/StateMapTileHandler.cs`
**Tested in:** `tests/Protocol.Tests/MapTileStateSerializationTests.cs` and `tests/Harness.Tests/StateMapTileHandlerTests.cs`.

### state.tile_actions

Returns map tile `Action` and `TouchAction` candidates around one coordinate.
Omit all params to inspect the farmer's current tile in the current location.
This is a discovery/debugging companion for `world.interact_tile_action`, so
scenario authors can assert a map-defined action exists before triggering it.

Request (current farmer tile):
```json
→ { "jsonrpc": "2.0", "id": 7, "method": "state.tile_actions" }
```

Request (explicit tile, radius, layer, and property filters):
```json
→ { "jsonrpc": "2.0", "id": 7, "method": "state.tile_actions",
     "params": { "location": "ExampleVineyard", "x": 56, "y": 48,
                 "radius": 1, "layers": ["Back"], "properties": ["TouchAction"] } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 7, "result": {
      "location": "ExampleVineyard",
      "x": 56,
      "y": 48,
      "radius": 1,
      "actions": [
        {
          "tile": { "x": 56, "y": 48 },
          "layer": "Back",
          "property": "TouchAction",
          "value": "LoadMap Town 50 114 0",
          "distance": 0
        }
      ]
   } }
```

`radius` is a Manhattan-search convenience bounded to `0..25`; results are sorted
by distance, then tile coordinate, for stable reports. `properties` accepts only
`Action` and `TouchAction`. Tile property keys and values are preserved exactly as
Stardew/xTile exposes them.

**Preconditions:** current location and player are required when params omit
`location`, `x`, or `y`.
**Side effects:** none.
**Implemented in:** `src/Harness/Handlers/StateTileActionsHandler.cs`
**Tested in:** `tests/Protocol.Tests/TileActionsStateSerializationTests.cs`, `tests/Harness.Tests/StateTileActionsHandlerTests.cs`, and `tests/Runner.Dsl.Tests/Facets/StateTests.cs`.

### state.npc

Returns a snapshot of a named NPC. `params.name` is **required**.

Request:
```json
→ { "jsonrpc": "2.0", "id": 5, "method": "state.npc", "params": { "name": "Abigail" } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 5, "result": {
      "name": "Abigail",
      "display_name": "Abigail",
      "location": "Town",
      "tile": { "x": 4, "y": 23 },
      "friendship_points": 500,
      "hearts": 2,
      "gift_given_today": false,
      "talked_to_today": false,
      "portrait": "Abigail",
      "current_schedule_time": 900,
      "current_schedule_location": "Town",
      "current_schedule_tile": { "x": 4, "y": 23 },
      "current_schedule_direction": 2,
      "current_schedule_animation": "",
      "is_villager": true,
      "can_socialize": true
   } }
```

Response (missing `params.name` — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 5, "error": { "code": -32602, "message": "params.name (string) is required" } }
```

Response (unknown NPC name — GameStateInvalid):
```json
← { "jsonrpc": "2.0", "id": 5, "error": { "code": -32003, "message": "no NPC named: Nobody" } }
```

Hearts derive from `friendship_points / 250`. If the farmer has no friendship record with the NPC (e.g. never met), `friendship_points` and `hearts` are `0` and `gift_given_today` is `false`.

`display_name`, `talked_to_today`, schedule fields, `is_villager`, and
`can_socialize` are optional relationship/schedule projection fields. They are
included when the runtime exposes the data and may be `null` for non-social NPCs
or NPCs without a current schedule key.

**Preconditions:** world loaded (`Game1.gameMode == playingGameMode`); the named NPC must exist in the loaded world.
**Side effects:** none.
**Implemented in:** `src/Harness/Handlers/StateNpcHandler.cs`
**Tested in:** `tests/Protocol.Tests/NpcStateSerializationTests.cs` (DTO shape).

### state.npcs

Returns compact relationship and location snapshots for NPCs in the loaded world.
Use this when a scenario needs to discover Content Patcher-added NPCs, count
custom villagers, or choose a target before making a focused `state.npc` query.

Request (all NPCs):
```json
→ { "jsonrpc": "2.0", "id": 6, "method": "state.npcs" }
```

Request (current location only with a smaller limit):
```json
→ { "jsonrpc": "2.0", "id": 6, "method": "state.npcs",
     "params": { "include_offscreen": false, "limit": 25 } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 6, "result": {
      "npcs": [
        {
          "name": "Riley",
          "display_name": "Riley",
          "location": "ExampleVineyard",
          "tile": { "x": 20, "y": 32 },
          "friendship_points": 1000,
          "hearts": 4,
          "gift_given_today": false,
          "talked_to_today": false,
          "portrait": "Riley",
          "current_schedule_time": 900,
          "current_schedule_location": "ExampleVineyard",
          "current_schedule_tile": { "x": 20, "y": 32 },
          "current_schedule_direction": 0,
          "current_schedule_animation": "Riley_Work",
          "is_villager": true,
          "can_socialize": true
        }
      ]
   } }
```

`include_offscreen` defaults to `true` and scans NPCs in all currently loaded
locations. Set it to `false` to return only the current location's NPCs. `limit`
defaults to `200` and must be between `1` and `1000`. Duplicate NPC names are
collapsed using the first loaded instance encountered.

**Preconditions:** world loaded (`Game1.gameMode == playingGameMode`).
**Side effects:** none.
**Implemented in:** `src/Harness/Handlers/StateNpcsHandler.cs`
**Tested in:** `tests/Harness.Tests/StateNpcsHandlerTests.cs` and `tests/Runner.Dsl.Tests/Facets/StateTests.cs`.

### state.menu

Returns a snapshot of the currently active top-level menu (`Game1.activeClickableMenu`). When no menu is open, `present` is `false` and `type` is empty. `params` is not used.

Request:
```json
→ { "jsonrpc": "2.0", "id": 6, "method": "state.menu" }
```

Response (menu active):
```json
← { "jsonrpc": "2.0", "id": 6, "result": {
      "type": "DialogueBox",
      "present": true,
      "bounds": { "x": 32, "y": 492, "width": 1216, "height": 192 },
      "choices": [
        { "key": "0", "text": "Pet Dusty" },
        { "key": "1", "text": "Don't pet Dusty" }
      ],
      "extra": { "choice_count": "2" }
   } }
```

Response (no menu active):
```json
← { "jsonrpc": "2.0", "id": 6, "result": { "type": "", "present": false, "extra": {} } }
```

`type` is the CLR class name of the menu (`ShopMenu`, `DialogueBox`, `GameMenu`, etc.). `extra` carries a small, menu-type-specific payload:

- `ShopMenu`: `currency` (int as string; 0 = gold, 1 = star tokens, 2 = Qi coins), `item_count` (count of `forSale`).
- `DialogueBox`: `character` (name of the speaker, or empty if narration-only),
  plus best-effort `dialogue_text` when readable from runtime menu fields.
  Dialogue progress extras include `dialogue_character_index`,
  `dialogue_text_length`, `dialogue_ready`, and `dialogue_safety_timer` when
  Stardew exposes them.
- Dialogue/question menus can expose structured `choices` with reflected
  `key`/`text` pairs. These are best-effort and work even when Stardew renders a
  choice menu with blank dialogue text.
- Other menu types currently emit `extra: {}`; extend per scenario need.

When `SDV_TEST_DIAGNOSTIC_MENU_MEMBERS=1` is set, `extra` may also contain
`diagnostic_*` fields with reflected menu members, response sources, and
clickable component details. This is a debug aid for new menu types and should
not be used as stable test data.

Nested menus (e.g. inventory inside a shop) are not exposed here — `Game1.activeClickableMenu` is the top-level only, per `.claude/rules/sdv-conventions.md`.

**Preconditions:** none beyond the harness being running. Safe on the title screen (returns `present:false`).
**Side effects:** none.
**Implemented in:** `src/Harness/Handlers/StateMenuHandler.cs`
**Tested in:** `tests/Protocol.Tests/MenuStateSerializationTests.cs` (DTO shape) and `tests/Harness.Tests/StateMenuHandlerTests.cs`.

### state.shop

Returns a structured snapshot of the active Stardew `ShopMenu`. This is the
preferred state primitive for asserting custom shop inventories, prices, stock,
and modded item ids after a player-like flow or a direct `shop.open`.

Request:
```json
→ { "jsonrpc": "2.0", "id": 8, "method": "state.shop" }
```

Response (shop active):
```json
← { "jsonrpc": "2.0", "id": 8, "result": {
      "present": true,
      "menu_type": "ShopMenu",
      "shop_id": "Carpenter",
      "currency": 0,
      "items": [
        {
          "item_id": "example_terminal",
          "qualified_id": "(F)example_terminal",
          "display_name": "Example Terminal",
          "price": 25000,
          "stock": 1,
          "category": -24,
          "quality": 0,
          "runtime_type": "Furniture"
        }
      ]
   } }
```

Response (no active shop):
```json
← { "jsonrpc": "2.0", "id": 8, "result": { "present": false, "menu_type": "", "shop_id": "", "currency": 0, "items": [] } }
```

`currency` follows Stardew's shop currency codes; `0` is gold. `item_id` is the
raw item id and `qualified_id` is the Stardew 1.6 qualified id. Item metadata is
best-effort because custom salables may expose only part of the item contract.
Scenarios can assert either raw or qualified ids; qualified ids are the most
precise check for custom item rewards and shop inventory.

**Preconditions:** none beyond the harness running. Safe outside a shop; returns `present:false`.
**Side effects:** none.
**Implemented in:** `src/Harness/Handlers/StateShopHandler.cs` and `src/Harness/Handlers/ShopStateProjector.cs`.
**Tested in:** `tests/Protocol.Tests/ShopRequestSerializationTests.cs`, `tests/Harness.Tests/StateShopHandlerTests.cs`, and `tests/Runner.Dsl.Tests/Facets/StateTests.cs`.

### state.event

Returns a best-effort snapshot of the active Stardew event/cutscene. It is inactive at the title screen, in normal gameplay, and after an event completes. `params` is not used.

Request:
```json
→ { "jsonrpc": "2.0", "id": 9, "method": "state.event" }
```

Inactive response:
```json
← { "jsonrpc": "2.0", "id": 9, "result": {
      "active": false,
      "event_up": false,
      "location": "",
      "id": "",
      "is_festival": false,
      "is_skippable": false,
      "player_control_locked": false,
      "actors": [],
      "dialogue": null,
      "viewport": null
   } }
```

Active response:
```json
← { "jsonrpc": "2.0", "id": 9, "result": {
      "active": true,
      "event_up": true,
      "location": "BusStop",
      "id": "520702",
      "is_festival": false,
      "is_skippable": true,
      "player_control_locked": true,
      "actors": [
        {
          "name": "Krobus",
          "tile": { "x": 16, "y": 23 },
          "pixel": { "x": 1024, "y": 1472 },
          "facing_direction": 3,
          "current_frame": 0
        }
      ],
      "dialogue": {
        "menu_type": "DialogueBox",
        "speaker": "",
        "text": "",
        "choices": [
          { "key": "0", "text": "Pet Dusty" },
          { "key": "1", "text": "Don't pet Dusty" }
        ]
      },
      "choices": [
        { "key": "0", "text": "Pet Dusty" },
        { "key": "1", "text": "Don't pet Dusty" }
      ],
      "viewport": { "x": 896, "y": 1472, "width": 1280, "height": 720 }
   } }
```

`id`, `is_festival`, `is_skippable`, actor list, dialogue, and choices are
best-effort fields read from runtime state through stable public fields when
possible and reflection when needed. Missing Stardew fields are omitted or
returned as default values rather than failing the RPC. `choices` mirrors
`dialogue.choices` for convenient assertions such as
`state.event.choices contains text 'Pet Dusty'`.

**Preconditions:** none beyond the harness running. Safe outside a save; inactive responses use empty/default fields.
**Side effects:** none.
**Implemented in:** `src/Harness/Handlers/StateEventHandler.cs` and `src/Harness/Handlers/EventStateProjector.cs`.
**Tested in:** `tests/Harness.Tests/EventStateProjectorTests.cs`.

### event.start

Starts a location event by id using Stardew's own location event resolver. This is a deterministic test primitive for event/cutscene observability; it does not mark events seen or bypass the event's normal script execution.

Request:
```json
→ { "jsonrpc": "2.0", "id": 10, "method": "event.start", "params": { "id": "520702", "location": "BusStop" } }
```

`params.id` is required. `params.location` is optional; omit it to resolve the event in the current location.

Response (success):
```json
← { "jsonrpc": "2.0", "id": 10, "result": { "ok": true, "tick": 84204, "id": "520702", "location": "BusStop" } }
```

Response (missing/empty `id` — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 10, "error": { "code": -32602, "message": "params.id required" } }
```

Response (unknown location or event not found — GameStateInvalid):
```json
← { "jsonrpc": "2.0", "id": 10, "error": { "code": -32003, "message": "event not found: 520702 in BusStop" } }
```

After `event.start`, use runner-side `wait.event_active` and `wait.event_complete` to observe script state. Active-event screenshots should use `screenshot.capture_next_frame` because `freeze.begin` rejects cutscenes while `Game1.eventUp` is true.

**Preconditions:** world loaded; requested location must exist; requested event must resolve for the current farmer/save state.
**Side effects:** calls `GameLocation.startEvent` for the resolved event.
**Implemented in:** `src/Harness/Handlers/EventStartHandler.cs`.
**Tested in:** `tests/Harness.Tests/EventStartHandlerTests.cs`.

### festival.start

Starts the active festival for the current in-game date and time through
Stardew festival APIs. This is for active festivals such as Spirit's Eve; passive
festival map replacements remain ordinary location/content tests.

Request:
```json
→ { "jsonrpc": "2.0", "id": 11, "method": "festival.start", "params": { "location": "Town" } }
```

`params.location` is optional. When supplied, Frobby validates that the active
festival is in that location.

Response (success):
```json
← { "jsonrpc": "2.0", "id": 11, "result": { "tick": 8421, "id": "fall27", "location": "Town", "is_festival": true } }
```

Response (no active festival — GameStateInvalid):
```json
← { "jsonrpc": "2.0", "id": 11, "error": { "code": -32003, "message": "festival.start found no active festival for fall26" } }
```

After `festival.start`, use runner-side `wait.event_active` with
`is_festival: true`. Active-festival screenshots should use
`screenshot.capture_next_frame`; `freeze.begin` rejects active events.

**Preconditions:** world loaded; current date must have an active festival; current time must be within the festival's open window.
**Side effects:** starts the current date's festival and warps the farmer into the festival location.
**Implemented in:** `src/Harness/Handlers/FestivalStartHandler.cs`.
**Tested in:** `tests/Harness.Tests/FestivalStartHandlerTests.cs`.

### event.skip

Skips the currently active Stardew event/cutscene. This is useful when a scenario needs to prove an event is observable and then cleanly return the save to normal gameplay without clicking through every dialogue, popup, or long scripted movement.

Request:
```json
→ { "jsonrpc": "2.0", "id": 11, "method": "event.skip" }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 11, "result": { "ok": true, "tick": 84220, "id": "520702" } }
```

Response (no active event — GameStateInvalid):
```json
← { "jsonrpc": "2.0", "id": 11, "error": { "code": -32003, "message": "event.skip requires an active event" } }
```

After `event.skip`, use `wait.event_complete` to wait until `state.event` reports inactive.

**Preconditions:** a Stardew event must be active.
**Side effects:** calls `Event.skipEvent()` on the active event.
**Implemented in:** `src/Harness/Handlers/EventSkipHandler.cs`.
**Tested in:** `tests/Harness.Tests/EventSkipHandlerTests.cs`.

### player.warp

Queues a warp of the local farmer to `(x, y)` in the named location. First **state-mutator** RPC method. `params.location` is required (non-empty string); `params.x` and `params.y` are required integers.

Request:
```json
→ { "jsonrpc": "2.0", "id": 7, "method": "player.warp", "params": { "location": "SeedShop", "x": 4, "y": 19 } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 7, "result": { "ok": true, "tick": 84200 } }
```

Response (missing/non-string `location`, non-int `x`/`y`, or absent `params` — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 7, "error": { "code": -32602, "message": "params.location required" } }
```

Response (unknown location name — GameStateInvalid):
```json
← { "jsonrpc": "2.0", "id": 7, "error": { "code": -32003, "message": "no location named: Nowhere" } }
```

`tick` is `Game1.ticks` at the moment the warp was queued. The warp itself completes asynchronously: `Game1.warpFarmer` sets transition state that SDV advances over the next few update ticks, so callers that need to observe the post-warp world should poll `state.player` (or await a tick-advance RPC in future milestones) rather than assuming the farmer has moved by the time this response arrives.

Non-int `x` or `y` (wrong JSON type — e.g. a string) is handled natively by `System.Text.Json`'s deserializer and surfaces through the handler's `JsonException` catch as `InvalidParams`.

**Preconditions:** world loaded (`Game1.gameMode == playingGameMode`); the named location must resolve via `Game1.getLocationFromName`.
**Side effects:** queues a farmer warp via `Game1.warpFarmer(location, x, y, flip: false)`; the actual warp happens on the next tick.
**Implemented in:** `src/Harness/Handlers/PlayerWarpHandler.cs`
**Tested in:** `tests/Protocol.Tests/WarpRequestSerializationTests.cs` (DTO shape) + `tests/Harness.Tests/PlayerWarpHandlerTests.cs` (error-path unit tests).

### player.give_item

Creates an item via the SDV 1.6 unified `ItemRegistry` and adds it to the local farmer's inventory. `params.id` is required (non-empty qualified item id, e.g. `"(O)388"` for wood); `params.count` is optional and defaults to `1` (must be `>= 1`).

Request:
```json
→ { "jsonrpc": "2.0", "id": 8, "method": "player.give_item", "params": { "id": "(O)388", "count": 50 } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 8, "result": { "ok": true, "tick": 84200 } }
```

Response (missing/empty `id` — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 8, "error": { "code": -32602, "message": "params.id required" } }
```

Response (`count` less than 1 — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 8, "error": { "code": -32602, "message": "params.count must be >= 1" } }
```

Response (unknown item id — GameStateInvalid):
```json
← { "jsonrpc": "2.0", "id": 8, "error": { "code": -32003, "message": "unknown item id: (O)9999" } }
```

`tick` is `Game1.ticks` at the moment the add was performed. The item is handed to `Game1.player.addItemByMenuIfNecessary`, which places it directly in inventory when there's room; if inventory is full SDV will surface its in-game pickup menu to the player. Scenarios correlate post-mutation asserts to this tick and poll `state.player` to observe the added stack.

**Preconditions:** world loaded (`Game1.gameMode == playingGameMode`); `req.Id` must resolve through `ItemRegistry.Create`.
**Side effects:** adds a freshly-created item stack of size `count` to `Game1.player`'s inventory (or opens the pickup menu if full).
**Implemented in:** `src/Harness/Handlers/PlayerGiveItemHandler.cs`
**Tested in:** `tests/Protocol.Tests/GiveItemRequestSerializationTests.cs` (DTO shape) + `tests/Harness.Tests/PlayerGiveItemHandlerTests.cs` (error-path unit tests).

### drop_box.deposit

Deposits items from the player's inventory into an active special-order donation
objective. The handler selects an active order by `order_key`, finds a matching
`Donate` objective and optional `drop_box`, validates item id/context-tag
compatibility, then updates Stardew runtime special-order state.

Request:
```json
→ { "jsonrpc": "2.0", "id": 19, "method": "drop_box.deposit", "params": {
      "order_key": "ExampleOrder",
      "drop_box": "ExampleDropBox",
      "qualified_id": "(O)388",
      "count": 5
   } }
```

Response:
```json
← { "jsonrpc": "2.0", "id": 19, "result": {
      "ok": true,
      "order_key": "ExampleOrder",
      "drop_box": "ExampleDropBox",
      "deposited_count": 5,
      "objective_index": 0,
      "before_count": 0,
      "after_count": 5,
      "item": { "id": "(O)388", "item_id": "388", "qualified_id": "(O)388", "name": "Wood", "stack": 5 }
   } }
```

Response (missing `order_key` — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 19, "error": { "code": -32602, "message": "params.order_key required" } }
```

Response (missing item selector — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 19, "error": { "code": -32602, "message": "params.item_id or params.qualified_id required" } }
```

Response (missing active order — GameStateInvalid):
```json
← { "jsonrpc": "2.0", "id": 19, "error": { "code": -32003, "message": "drop_box.deposit found no active order 'ExampleOrder'" } }
```

Response (missing donation objective — GameStateInvalid):
```json
← { "jsonrpc": "2.0", "id": 19, "error": { "code": -32003, "message": "drop_box.deposit found no matching donation objective for order 'ExampleOrder'" } }
```

Response (insufficient inventory or context mismatch — GameStateInvalid):
```json
← { "jsonrpc": "2.0", "id": 19, "error": { "code": -32003, "message": "drop_box.deposit found not enough matching inventory for objective" } }
```

Prefer proving the active order and objective with `state.special_orders` or
runner-side `wait.special_order` before depositing. Keep order keys, event flags,
and item ids in the repo scenario.

**Preconditions:** world loaded, active special order present, matching inventory present.
**Side effects:** reduces the selected inventory stack, appends a donated item to the active special order, and increments the donation objective count.
**Implemented in:** `src/Harness/Handlers/DropBoxDepositHandler.cs`
**Tested in:** `tests/Protocol.Tests/DropBoxDepositSerializationTests.cs` and `tests/Harness.Tests/DropBoxDepositHandlerTests.cs`.

### player.set_money

Sets the local farmer's money to an absolute value. `params.amount` is required and must be `>= 0`. The response carries the farmer's money value immediately before the mutation so scenarios can verify deltas without an extra query.

Request:
```json
→ { "jsonrpc": "2.0", "id": 9, "method": "player.set_money", "params": { "amount": 5000 } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 9, "result": { "ok": true, "tick": 84200, "previous": 1000 } }
```

Response (missing `params` or missing `amount` — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 9, "error": { "code": -32602, "message": "params required" } }
```

Response (`amount < 0` — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 9, "error": { "code": -32602, "message": "params.amount must be >= 0" } }
```

`tick` is `Game1.ticks` at the moment the assignment was performed. `previous` is `Game1.player.Money` captured immediately before the write. `Game1.player.Money` is a real property in SDV 1.6, so the write applies synchronously — unlike `player.warp`, callers don't need to poll before asserting on the new value.

**Preconditions:** world loaded (`Game1.gameMode == playingGameMode`); no `GameStateInvalid` case for this handler — the valid range is enforced at the `InvalidParams` layer.
**Side effects:** overwrites `Game1.player.Money` with `req.Amount`.
**Implemented in:** `src/Harness/Handlers/PlayerSetMoneyHandler.cs`
**Tested in:** `tests/Protocol.Tests/SetMoneyRequestSerializationTests.cs` (DTO shape) + `tests/Harness.Tests/PlayerSetMoneyHandlerTests.cs` (error-path unit tests).

### player.set_transient_state

Sets selected local farmer transient-state booleans for tests. At least one of
`swimming` or `bathing_clothes` is required.

Request:
```json
→ { "jsonrpc": "2.0", "id": 10, "method": "player.set_transient_state", "params": { "swimming": true } }
```

Response:
```json
← { "jsonrpc": "2.0", "id": 10, "result": {
      "ok": true,
      "tick": 84200,
      "previous_swimming": false,
      "previous_bathing_clothes": false,
      "swimming": true,
      "bathing_clothes": false
   } }
```

**Preconditions:** world loaded.
**Side effects:** updates only the supplied local farmer transient-state fields.
**Implemented in:** `src/Harness/Handlers/PlayerSetTransientStateHandler.cs`.
**Tested in:** `tests/Harness.Tests/PlayerSetTransientStateHandlerTests.cs`.

### player.add_mail

Adds a received-mail flag to the master farmer. This is a neutral save-state
mutator for scenarios that need to exercise vanilla gameplay gates exposed by
mods without adding mod-specific hooks.

Request:
```json
→ { "jsonrpc": "2.0", "id": 13, "method": "player.add_mail", "params": { "id": "jojaVault" } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 13, "result": { "ok": true, "tick": 84204 } }
```

Response (missing `params` or non-numeric `id` — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 13, "error": { "code": -32602, "message": "params.id must be a numeric event id" } }
```

`tick` is `Game1.ticks` at the moment the mail flag was added.

**Preconditions:** world loaded (`Game1.gameMode == playingGameMode`).
**Side effects:** trims `params.id` and adds it to `Game1.MasterPlayer.mailReceived`.
**Implemented in:** `src/Harness/Handlers/PlayerAddMailHandler.cs`
**Tested in:** `tests/Harness.Tests/PlayerAddMailHandlerTests.cs` (error-path unit tests) + `tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs` (DSL shape).

### player.add_event_seen

Adds an event id to the master farmer and local farmer `eventsSeen` lists. This
is a neutral save-state mutator for scenarios that need to set up or verify
relationship/event-gated content without adding mod-specific hooks.

Request:
```json
→ { "jsonrpc": "2.0", "id": 13, "method": "player.add_event_seen", "params": { "id": "5532011" } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 13, "result": { "ok": true, "tick": 84204 } }
```

Response (missing `params` or blank `id` — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 13, "error": { "code": -32602, "message": "params.id must be non-empty" } }
```

`tick` is `Game1.ticks` at the moment the event flag was added. The handler
normalizes numeric string ids and rejects ids that cannot be parsed as Stardew
event ids.

**Preconditions:** world loaded (`Game1.gameMode == playingGameMode`).
**Side effects:** trims/parses `params.id` and adds it to both
`Game1.MasterPlayer.eventsSeen` and `Game1.player.eventsSeen`.
**Implemented in:** `src/Harness/Handlers/PlayerAddEventSeenHandler.cs`
**Tested in:** `tests/Harness.Tests/PlayerAddEventSeenHandlerTests.cs`.

### player.add_secret_note_seen

Adds a secret-note id to the master farmer and local farmer seen-note sets. This
is a neutral save-state mutator for scenarios that need to set up note-gated
map interactions or verify that a gameplay action marked a note as seen.

Request:
```json
→ { "jsonrpc": "2.0", "id": 13, "method": "player.add_secret_note_seen", "params": { "id": 18 } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 13, "result": { "ok": true, "tick": 84204 } }
```

Response (missing `params` or non-positive `id` — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 13, "error": { "code": -32602, "message": "params.id must be a positive secret note id" } }
```

`tick` is `Game1.ticks` at the moment the note flag was added. The handler is
idempotent: adding a note that is already present does not duplicate it.

**Preconditions:** world loaded (`Game1.gameMode == playingGameMode`).
**Side effects:** adds `params.id` to `Game1.MasterPlayer.secretNotesSeen` and
to the local player when the local player is not the master farmer.
**Implemented in:** `src/Harness/Handlers/PlayerAddSecretNoteSeenHandler.cs`
**Tested in:** `tests/Harness.Tests/PlayerAddSecretNoteSeenHandlerTests.cs`.

### player.set_friendship

Sets the master farmer's relationship state for a named NPC. This is a neutral
setup mutator for scenarios that need deterministic relationship gates, birthday
dialogue, gift limits, or social UI state without adding mod-specific hooks.

Request:
```json
→ { "jsonrpc": "2.0", "id": 14, "method": "player.set_friendship",
     "params": { "npc": "Riley", "points": 1000, "talked_to_today": false, "gifts_this_week": 0, "gifts_today": 0 } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 14, "result": { "ok": true, "tick": 84205 } }
```

Response (missing NPC or invalid points — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 14, "error": { "code": -32602, "message": "params.npc must be non-empty" } }
```

`points` must be between `0` and `2500`. Optional `talked_to_today`,
`gifts_this_week`, and `gifts_today` fields set the corresponding save
relationship flags when supplied; omitted optional fields preserve existing
friendship values or remain at Stardew defaults for a new entry.

**Preconditions:** world loaded (`Game1.gameMode == playingGameMode`).
**Side effects:** creates or updates `Game1.MasterPlayer.friendshipData[npc]`.
**Implemented in:** `src/Harness/Handlers/PlayerSetFriendshipHandler.cs`
**Tested in:** `tests/Harness.Tests/PlayerSetFriendshipHandlerTests.cs` and `tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs`.

### world.warp_npc

Places a loaded vanilla or custom NPC at a named location/tile. This is a neutral
setup mutator for scenarios where direct clock writes or fixture state leave an
NPC in a non-interactable schedule position, but the test needs to exercise
normal interaction paths such as `world.interact_npc`.

Request:
```json
→ { "jsonrpc": "2.0", "id": 15, "method": "world.warp_npc",
     "params": { "name": "Riley", "location": "ExampleVineyard", "x": 20, "y": 32 } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 15, "result": { "ok": true, "tick": 84206 } }
```

Response (missing NPC name — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 15, "error": { "code": -32602, "message": "params.name must be non-empty" } }
```

**Preconditions:** world loaded; the named NPC and target location must exist.
**Side effects:** moves the NPC to the requested location/tile through Stardew's
character-warp API.
**Implemented in:** `src/Harness/Handlers/WorldWarpNpcHandler.cs`
**Tested in:** `tests/Protocol.Tests/WarpNpcRequestSerializationTests.cs` and `tests/Harness.Tests/WorldWarpNpcHandlerTests.cs`.

### time.advance

Advances SDV's in-game clock by a multiple of 10 minutes. `params.minutes` is required and must be a multiple of 10 between 10 and 120 inclusive. SDV's clock advances in 10-minute chunks; longer advances chain multiple calls at the scenario layer to keep each RPC bounded.

Request:
```json
→ { "jsonrpc": "2.0", "id": 10, "method": "time.advance", "params": { "minutes": 30 } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 10, "result": { "ok": true, "tick": 84200, "new_time_of_day": 630 } }
```

Response (missing `params` / missing `minutes` / malformed — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 10, "error": { "code": -32602, "message": "params required" } }
```

Response (out-of-range or not-multiple-of-10 — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 10, "error": { "code": -32602, "message": "params.minutes must be a multiple of 10, between 10 and 120" } }
```

`tick` is `Game1.ticks` at the moment of the advance. `new_time_of_day` is `Game1.timeOfDay` after the advance (e.g. 630 = 06:30am, 1530 = 3:30pm), returned so scenarios don't need an extra `state.time` round-trip just to confirm the advance.

Internally the handler calls `Game1.performTenMinuteClockUpdate` once per 10-minute step, so a 30-minute advance triggers three clock updates and the usual side effects (scheduled NPC pathing advances, shop restock boundaries at known thresholds, weather tick).

**Preconditions:** world loaded; mutations during festival day may be ignored — consult sdv-conventions.md.
**Side effects:** advances clock state and triggers any NPC/event updates that react to the advance.
**Implemented in:** `src/Harness/Handlers/TimeAdvanceHandler.cs`
**Tested in:** `tests/Protocol.Tests/TimeAdvanceRequestSerializationTests.cs` (DTO shape) + `tests/Harness.Tests/TimeAdvanceHandlerTests.cs` (error-path unit tests).

### time.next_day

Advances the active scenario through a deterministic testing day transition and returns the new date. This is not a `time.set` clone, but it also does not run SDV's full sleep/save/end-of-night UI. The handler raises SDV `DayEnding` trigger actions, raises SMAPI `GameLoop.DayEnding`, advances the SDV calendar by exactly one day, sets the clock to 06:00, raises SDV `DayStarted` trigger actions, raises SMAPI `GameLoop.DayStarted`, and returns the post-transition snapshot.

Request:
```json
→ { "jsonrpc": "2.0", "id": 11, "method": "time.next_day" }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 11, "result": { "ok": true, "tick": 90123, "year": 1, "season": "spring", "day_of_month": 2, "time_of_day": 600 } }
```

Response (invalid game state):
```json
← { "jsonrpc": "2.0", "id": 11, "error": { "code": -32003, "message": "time.next_day requires an active scenario (call scenario.begin first)" } }
```

`tick` is `Game1.ticks` after the transition. `year`, `season`, `day_of_month`, and `time_of_day` reflect the post-transition SDV date; `time_of_day` is always `600`.

When `time.next_day` is used as a runner scenario step, the runner retries the RPC briefly if the harness reports `time.next_day requires no active warp`. This covers the common UI-testing case where a semantic click has just closed a menu and the game is still settling. Scenario authors may override the retry window with `args.settle_timeout_ms` and `args.poll_ms`.

**Preconditions:** an active scenario; world loaded; no active menu; no minigame; no event; not mid-warp.
**Side effects:** raises SDV `DayEnding` trigger actions, raises exactly one SMAPI `DayEnding`, advances date/time deterministically, raises SDV `DayStarted` trigger actions, then raises exactly one SMAPI `DayStarted`. It does not save, show sleep/end-of-night menus, run overnight farm simulation, or execute SDV's full sleep transition.
**Fallback seam:** production and unit tests use `DeterministicTimeNextDayTransition`, which applies the same 28-day season/year rollover and fires trigger-action and SMAPI day-ending/day-started callbacks in order.
**Implemented in:** `src/Harness/Handlers/TimeNextDayHandler.cs`
**Tested in:** `tests/Protocol.Tests/TimeNextDayResultSerializationTests.cs` (DTO shape) + `tests/Harness.Tests/TimeNextDayHandlerTests.cs` (preconditions/projection/seam order) + `tests/Runner.Tests/ScenarioRunnerTests.cs` (runner active-warp retry).

### world.set_weather

Sets the current location's weather to one of six documented values. `params.type` is required (non-empty string) and must be one of `sun`, `rain`, `storm`, `snow`, `wind`, `festival` (case-insensitive on input; mapped to the SDV 1.6 canonical ids `Sun`/`Rain`/`Storm`/`Snow`/`Wind`/`Festival`).

Request:
```json
→ { "jsonrpc": "2.0", "id": 11, "method": "world.set_weather", "params": { "type": "rain" } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 11, "result": { "ok": true, "tick": 84200 } }
```

Response (missing `params` / missing `type` / malformed — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 11, "error": { "code": -32602, "message": "params.type required" } }
```

Response (`type` not in the allowed set — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 11, "error": { "code": -32602, "message": "unknown weather type: hurricane" } }
```

`tick` is `Game1.ticks` at the moment the weather was written. The six allowed values map to SDV 1.6's canonical weather ids — other weather ids shipped by future patches or mods are intentionally not exposed here; scenarios needing them should extend the allow-list with a schema-review step first.

**Preconditions:** world loaded (`Game1.gameMode == playingGameMode`); `Game1.netWorldState.Value` available and `Game1.currentLocation` resolves to a valid location context (falls back to `"Default"` if null).
**Side effects:** overwrites the weather for the current location's context via `Game1.netWorldState.Value.GetWeatherForLocation(contextId).Weather = <id>`, then triggers `Game1.updateWeather(Game1.currentGameTime)` so the visual/audio transition applies immediately. **Scope:** only the current location's context is mutated — e.g. setting weather while the farmer is on Ginger Island changes the `"Island"` context and leaves the valley's weather untouched.
**Implemented in:** `src/Harness/Handlers/WorldSetWeatherHandler.cs`
**Tested in:** `tests/Protocol.Tests/WeatherRequestSerializationTests.cs` (DTO shape) + `tests/Harness.Tests/WorldSetWeatherHandlerTests.cs` (error-path unit tests).

### world.place_furniture

Creates furniture via SDV's `ItemRegistry` and adds it to a loaded location's furniture collection. `params.id` is required (non-empty qualified furniture item id, e.g. `"(F)1308"`); `params.location` is optional and defaults to the current location; `params.x` and `params.y` are required nonnegative tile coordinates; `params.remove_existing` is optional and defaults to `false`.

Request:
```json
→ { "jsonrpc": "2.0", "id": 12, "method": "world.place_furniture", "params": { "id": "(F)example_terminal", "location": "FarmHouse", "x": 8, "y": 9, "remove_existing": true } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 12, "result": { "ok": true, "tick": 84200, "id": "(F)example_terminal", "location": "FarmHouse", "tile": { "x": 8, "y": 9 } } }
```

Response (missing/empty `id` — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 12, "error": { "code": -32602, "message": "params.id required" } }
```

Response (`x` or `y` less than 0 — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 12, "error": { "code": -32602, "message": "params.x must be >= 0" } }
```

Response (unknown item id, non-furniture item, or unknown location — GameStateInvalid):
```json
← { "jsonrpc": "2.0", "id": 12, "error": { "code": -32003, "message": "unknown item id: (F)missing" } }
```

`tick` is `Game1.ticks` at the moment the furniture was added. When `remove_existing` is true, existing furniture whose top-left `TileLocation` exactly matches `x`/`y` is removed before the new furniture is added. Collision and placement-rule checks are intentionally not enforced by this RPC; it is a test harness mutator for constructing deterministic scenes.

**Preconditions:** world loaded (`Game1.gameMode == playingGameMode`); `params.id` must resolve through `ItemRegistry.Exists` and create a `StardewValley.Objects.Furniture`; the requested location must exist when provided, otherwise `Game1.currentLocation` must be available.
**Side effects:** mutates `GameLocation.furniture` by adding a freshly-created furniture instance and optionally removing furniture already anchored at the target tile.
**Implemented in:** `src/Harness/Handlers/WorldPlaceFurnitureHandler.cs`
**Tested in:** `tests/Protocol.Tests/PlaceFurnitureRequestSerializationTests.cs` (DTO shape) + `tests/Harness.Tests/WorldPlaceFurnitureHandlerTests.cs` (error-path unit tests).

### world.place_object

Creates a Stardew object or big craftable via SDV's `ItemRegistry` and adds it to
a loaded location's object table. This is a deterministic test setup action; it
does not simulate player inventory, placement sounds, or collision rules.

`params.id` is required. Qualified ids such as `"(O)388"` and
`"(BC)Example.Mod_BigCraftable"` are accepted when Stardew accepts them.
`params.location` is optional and defaults to the current location. `params.x`
and `params.y` are required nonnegative tile coordinates. `params.stack` and
`params.quality` are optional overrides. `params.remove_existing` is optional and
defaults to `false`; pass `true` to replace an object already at the tile.

Request:
```json
→ { "jsonrpc": "2.0", "id": 12, "method": "world.place_object", "params": { "id": "(BC)Example.Mod_Golden_Piggy_Bank", "location": "FarmHouse", "x": 8, "y": 9, "remove_existing": true } }
```

Response:
```json
← { "jsonrpc": "2.0", "id": 12, "result": { "ok": true, "tick": 84200, "id": "Example.Mod_Golden_Piggy_Bank", "qualified_id": "(BC)Example.Mod_Golden_Piggy_Bank", "name": "Golden Piggy Bank", "location": "FarmHouse", "tile": { "x": 8, "y": 9 }, "big_craftable": true, "runtime_type": "Object" } }
```

Response (missing/empty `id` — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 12, "error": { "code": -32602, "message": "params.id required" } }
```

Response (unknown item id, non-object item, occupied tile, or unknown location — GameStateInvalid):
```json
← { "jsonrpc": "2.0", "id": 12, "error": { "code": -32003, "message": "unknown item id: (O)missing" } }
```

`tick` is `Game1.ticks` at the moment the object was added. When
`remove_existing` is true, an existing object at the same tile is removed before
the new object is added.

**Preconditions:** world loaded (`Game1.gameMode == playingGameMode`); `params.id` must resolve through `ItemRegistry.Exists` and create a `StardewValley.Object`; the requested location must exist when provided, otherwise `Game1.currentLocation` must be available.
**Side effects:** mutates `GameLocation.Objects` by adding a freshly-created object instance and optionally removing an object already at the target tile.
**Implemented in:** `src/Harness/Handlers/WorldPlaceObjectHandler.cs`
**Tested in:** `tests/Protocol.Tests/PlaceObjectSerializationTests.cs` (DTO shape) + `tests/Harness.Tests/WorldPlaceObjectHandlerTests.cs` (error-path and placement seam unit tests).

### world.interact_tile

Invokes the current location's furniture or object interaction at a tile. Furniture whose top-left `TileLocation` exactly matches the tile is tried first; if none matches, the handler checks `GameLocation.Objects` at that tile.

Request:
```json
→ { "jsonrpc": "2.0", "id": 13, "method": "world.interact_tile", "params": { "x": 8, "y": 9, "just_checking_for_activity": false } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 13, "result": { "ok": true, "tick": 84201, "handled": true, "target_type": "Furniture", "tile": { "x": 8, "y": 9 } } }
```

Response (`x` or `y` missing or less than 0 — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 13, "error": { "code": -32602, "message": "params.x required" } }
```

Response (no furniture or object at the tile — GameStateInvalid):
```json
← { "jsonrpc": "2.0", "id": 13, "error": { "code": -32003, "message": "no furniture or object at tile 8,9 in FarmHouse" } }
```

`tick` is `Game1.ticks` at the moment the interaction was attempted. `handled` is the boolean returned by SDV's `checkForAction` implementation.

**Preconditions:** world loaded (`Game1.gameMode == playingGameMode` and `Game1.hasLoadedGame`); `Game1.currentLocation` must contain furniture or an object at the tile.
**Side effects:** calls SDV's `checkForAction` on the matched furniture or object, which may open menus or mutate game state depending on the target.
**Implemented in:** `src/Harness/Handlers/WorldInteractTileHandler.cs`
**Tested in:** `tests/Protocol.Tests/InteractTileRequestSerializationTests.cs` (DTO shape) + `tests/Harness.Tests/WorldInteractTileHandlerTests.cs` (error-path unit tests) + `tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs` (DSL wrapper shape).

### world.interact_tile_action

Invokes a map tile `Action` or `TouchAction` property in the current location.
This is separate from `world.interact_tile`, which remains focused on furniture
and placed objects. Use `state.tile_actions` or `state.map_tile` first when a
scenario needs to discover or prove the map property before executing it.

Request:
```json
→ { "jsonrpc": "2.0", "id": 14, "method": "world.interact_tile_action",
     "params": { "location": "ExampleVineyard", "x": 56, "y": 48,
                 "property": "TouchAction", "layers": ["Back"] } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 14, "result": {
      "ok": true,
      "tick": 84201,
      "handled": true,
      "target_type": "MapTileAction",
      "action_type": "TouchAction",
      "action": "LoadMap Town 50 114 0",
      "tile": { "x": 56, "y": 48 }
   } }
```

Response (`property` is not `Action` or `TouchAction` — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 14, "error": { "code": -32602, "message": "params.property must be Action or TouchAction" } }
```

Response (no map action property at the requested tile — GameStateInvalid):
```json
← { "jsonrpc": "2.0", "id": 14, "error": { "code": -32003, "message": "no Action or TouchAction at tile 56,48 in Farm" } }
```

If `property` is omitted, the handler tries `Action` before `TouchAction` across
the requested layers, matching Stardew's activate-first interaction shape. For
`TouchAction`, the RPC first moves the farmer onto the requested tile, then calls
Stardew's direct touch-action path. That preserves native behavior while also
giving update/tick-driven mods the same tile-transition signal a real player
would create. The RPC returns `handled: true` once the direct call completes; a
follow-up `wait.location` or `wait.ms` step should observe asynchronous effects.

**Preconditions:** world loaded (`Game1.gameMode == playingGameMode` and `Game1.hasLoadedGame`); the current location must contain a matching map property at the requested tile.
**Side effects:** calls SDV's `performAction` or `performTouchAction`, which may warp the farmer, open menus, show messages, or mutate game state depending on the map action.
**Implemented in:** `src/Harness/Handlers/WorldInteractTileActionHandler.cs`
**Tested in:** `tests/Protocol.Tests/InteractTileActionRequestSerializationTests.cs`, `tests/Harness.Tests/WorldInteractTileActionHandlerTests.cs`, and `tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs`.

### world.use_tool

Uses an equipped or inventory tool at a tile in the current location or a named
location. The initial implementation supports Stardew's hoe path, which is
useful for player-like tests of dig spots and other tile effects that must run
through tool behavior instead of direct state mutation.

Request:
```json
→ { "jsonrpc": "2.0", "id": 15, "method": "world.use_tool",
     "params": { "tool": "Hoe", "location": "Farm", "x": 21, "y": 12, "facing": "up", "power": 0 } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 15, "result": {
      "ok": true,
      "tick": 84201,
      "tool": "Hoe",
      "location": "Farm",
      "tile": { "x": 21, "y": 12 },
      "selected_item_id": "Hoe",
      "selected_item_qualified_id": "(T)Hoe",
      "selected_item_name": "Hoe",
      "selected_item_runtime_type": "Hoe",
      "selected_tool_index": 0,
      "invoked": true
   } }
```

Response (`tool`, `x`, or `y` missing, unsupported tool, or invalid coordinate — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 15, "error": { "code": -32602, "message": "world.use_tool currently only supports Hoe" } }
```

Response (location mismatch or no matching tool available — GameStateInvalid):
```json
← { "jsonrpc": "2.0", "id": 15, "error": { "code": -32003, "message": "world.use_tool could not find Hoe in the farmer inventory" } }
```

`facing` is optional and accepts Stardew cardinal directions such as `up`,
`down`, `left`, and `right`. `power` defaults to `0` and must be non-negative.
After the RPC, use runtime state waits or assertions to prove the desired side
effect, such as a spawned object, inventory item, or `secret_notes_seen` flag.

**Preconditions:** world loaded (`Game1.gameMode == playingGameMode` and
`Game1.hasLoadedGame`); the current or requested location must match the
farmer's current location; the farmer must have the requested tool.
**Side effects:** selects the matching tool and calls Stardew's tool function at
the requested tile, so modded tool hooks and native tile effects can run.
**Implemented in:** `src/Harness/Handlers/WorldUseToolHandler.cs`
**Tested in:** `tests/Protocol.Tests/UseToolSerializationTests.cs`,
`tests/Harness.Tests/WorldUseToolHandlerTests.cs`, and
`tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs`.

### world.explode_tile

Triggers Stardew-native explosion behavior at a tile in the current or named
loaded location. This is a direct deterministic test primitive: it does not
require a bomb item, fuse timing, inventory state, or player proximity.

Request:
```json
→ { "jsonrpc": "2.0", "id": 16, "method": "world.explode_tile",
     "params": { "location": "Frobby_CombatLab", "x": 9, "y": 8,
                 "radius": 2, "damage_player": false, "damage_amount": 5000 } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 16, "result": {
      "ok": true,
      "tick": 123,
      "location": "Frobby_CombatLab",
      "tile": { "x": 9, "y": 8 },
      "radius": 2,
      "damage_player": false,
      "damage_amount": 5000,
      "monsters_before": 1,
      "monsters_after": 0,
      "debris_before": 0,
      "debris_after": 1,
      "invoked": true
   } }
```

Response (`x`, `y`, or `radius` invalid — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 16, "error": { "code": -32602, "message": "params.radius must be between 1 and 10" } }
```

Response (`damage_amount` invalid — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 16, "error": { "code": -32602, "message": "params.damage_amount must be >= 0" } }
```

Response (out-of-bounds tile — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 16, "error": { "code": -32602, "message": "world.explode_tile target tile must be inside the resolved map bounds" } }
```

Response (world not ready or unknown location — GameStateInvalid):
```json
← { "jsonrpc": "2.0", "id": 16, "error": { "code": -32003, "message": "world.explode_tile location not found: ExampleMine" } }
```

Use `wait.location_content` for the assertion that matters, such as waiting for
a labelled monster to be removed. The count fields are diagnostics for reports
and debugging. `damage_amount` is optional; omit it to preserve Stardew's native
default explosion damage for the resolved overload, or set it when a test needs
a deterministic high-damage blast while still using native explosion behavior.

**Preconditions:** world loaded; the current or requested location must be
loaded; `x` and `y` must be in map bounds; `radius` must be between 1 and 10.
**Side effects:** invokes Stardew's native location explosion path at the
requested tile, which may damage monsters, create debris, remove objects, or
mutate terrain depending on the active game/mod state.
**Implemented in:** `src/Harness/Handlers/WorldExplodeTileHandler.cs`
**Tested in:** `tests/Protocol.Tests/ExplodeTileSerializationTests.cs`,
`tests/Harness.Tests/WorldExplodeTileHandlerTests.cs`,
`tests/Runner.Tests/ScenarioRunnerTests.cs`, and
`tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs`.

### combat.attack

Performs one player-like melee attack in the loaded world. Supply either a
complete target tile (`x` and `y`) or a cardinal `direction`. If both a complete
target tile and `direction` are supplied, `direction` wins. Supported directions
are `up`, `right`, `down`, and `left`. If a target tile overlaps the player and
no explicit direction is supplied, the harness attacks in the farmer's current
facing direction so moving monsters that collide with the player can still be
tested.

The harness RPC is intentionally single-shot: it faces the farmer, selects the
requested melee weapon when `qualified_item_id` is provided, and invokes
Stardew's weapon-use path once. Runner scenarios may pass `repeat` and
`delay_ticks`; the runner owns those fields and spaces repeated single-shot RPC
calls outside the game thread. Runner scenarios may also pass `target` with
`location`, `name`, `type`, `sprite_texture`, optional `x`/`y`, and health
comparison filters; the runner resolves that selector through
`state.location.monsters` before each repeat and sends the harness a normal
single-shot tile attack.

Request:
```json
→ { "jsonrpc": "2.0", "id": 15, "method": "combat.attack", "params": { "x": 20, "y": 144, "qualified_item_id": "(W)4" } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 15, "result": { "ok": true, "tick": 84205, "tile": { "x": 20, "y": 145 }, "direction": "up", "selected_item_qualified_id": "(W)4", "selected_item_runtime_type": "MeleeWeapon" } }
```

Response (no matching melee weapon — GameStateInvalid):
```json
← { "jsonrpc": "2.0", "id": 15, "error": { "code": -32003, "message": "combat.attack could not find melee weapon (W)4 in the farmer inventory" } }
```

**Preconditions:** world loaded; the farmer must have a melee weapon selected or
available in inventory. When `qualified_item_id` is supplied, the available
weapon must match it.
**Side effects:** faces the farmer and calls the selected weapon's Stardew use
path once. Follow with `wait.location_content` and monster health comparisons to
observe damage instead of sleeping.
**Implemented in:** `src/Harness/Handlers/CombatAttackHandler.cs`
**Tested in:** `tests/Protocol.Tests/CombatAttackSerializationTests.cs`,
`tests/Harness.Tests/CombatAttackHandlerTests.cs`,
`tests/Runner.Tests/ScenarioRunnerTests.cs`, and
`tests/Runner.Dsl.Tests/Facets/CombatTests.cs`.

### combat_lab.reset

Creates or resets the test-only `Frobby_CombatLab` location. This is a neutral
dev room for combat tests; it is active only in harness-driven test runs and
should not be used by production mods.

Request:
```json
→ { "jsonrpc": "2.0", "id": 16, "method": "combat_lab.reset", "params": { "player_x": 8, "player_y": 8, "width": 20, "height": 14, "warp_player": true } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 16, "result": {
      "ok": true,
      "location": "Frobby_CombatLab",
      "player_tile": { "x": 8, "y": 8 },
      "map_width": 20,
      "map_height": 14,
      "cleared_monsters": 0,
      "cleared_debris": 0
   } }
```

### combat_lab.spawn_monster

Spawns a supported vanilla monster in `Frobby_CombatLab` and assigns a run-local
identity. Supported first-slice kinds are `GreenSlime` and `Bat`.

Request:
```json
→ { "jsonrpc": "2.0", "id": 17, "method": "combat_lab.spawn_monster", "params": { "kind": "GreenSlime", "label": "target", "x": 9, "y": 8, "health": 1 } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 17, "result": {
      "ok": true,
      "monster_id": "frobby-monster-1",
      "label": "target",
      "kind": "GreenSlime",
      "location": "Frobby_CombatLab",
      "tile": { "x": 9, "y": 8 },
      "health": 1,
      "max_health": 24
   } }
```

**Preconditions:** world loaded; call `combat_lab.reset` before spawning.
**Side effects:** creates test monsters in the temporary Combat Lab location and
tracks run-local identity metadata until scenario end or the next lab reset.
**Implemented in:** `src/Harness/Handlers/CombatLabResetHandler.cs` and
`src/Harness/Handlers/CombatLabSpawnMonsterHandler.cs`
**Tested in:** `tests/Protocol.Tests/CombatLabSerializationTests.cs`,
`tests/Harness.Tests/CombatLabResetHandlerTests.cs`,
`tests/Harness.Tests/CombatLabSpawnMonsterHandlerTests.cs`,
`tests/Runner.Tests/ScenarioRunnerTests.cs`, and
`tests/Runner.Dsl.Tests/Facets/CombatLabTests.cs`.

### combat_lab.relocate_monster

Moves one already-spawned runtime monster into `Frobby_CombatLab` and assigns a
run-local Frobby identity. This isolates mod-created monsters without Frobby
constructing or parsing mod monster definitions.

Request:
```json
→ { "jsonrpc": "2.0", "id": 18, "method": "combat_lab.relocate_monster", "params": { "from_location": "Custom_CrimsonBadlands", "label": "corrupt-mummy", "target_x": 9, "target_y": 8, "match": { "x": 20, "y": 144, "sprite_texture": "Characters/Monsters/CorruptMummy", "health": 2000, "max_health": 2000 } } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 18, "result": {
      "ok": true,
      "monster_id": "frobby-monster-1",
      "label": "corrupt-mummy",
      "from_location": "Custom_CrimsonBadlands",
      "source_tile": { "x": 20, "y": 144 },
      "location": "Frobby_CombatLab",
      "tile": { "x": 9, "y": 8 },
      "name": "Mummy",
      "type": "Mummy",
      "sprite_texture": "Characters/Monsters/CorruptMummy",
      "health": 2000,
      "max_health": 2000
   } }
```

`match` filters are exact and use the same observable metadata exposed by
`state.location.monsters`. The top-level `match.x` and `match.y` filters compare
against `state.location.monsters[].tile.x` / `tile.y`; the remaining filters
compare against `monster_id`, `label`, `name`, `type`, `sprite_texture`,
`health`, `max_health`, and `damage`. The handler rejects zero matches and
multiple matches so scenarios must identify exactly one source monster before
mutation.

**Preconditions:** world loaded; call `combat_lab.reset` before relocating; the
source location must be loaded; target tile must be inside the lab map.
**Side effects:** removes the matching monster object from the source location,
moves it into `Frobby_CombatLab`, and binds run-local identity metadata with
`spawned_by_frobby: false`.
**Implemented in:** `src/Harness/Handlers/CombatLabRelocateMonsterHandler.cs`
**Tested in:** `tests/Protocol.Tests/CombatLabSerializationTests.cs`,
`tests/Harness.Tests/CombatLabRelocateMonsterHandlerTests.cs`,
`tests/Harness.Tests/CombatLabMonsterMatcherTests.cs`,
`tests/Runner.Tests/ScenarioRunnerTests.cs`, and
`tests/Runner.Dsl.Tests/Facets/CombatLabTests.cs`.

### input.key

Sends a MonoGame key press to the currently active top-level menu (`Game1.activeClickableMenu`). `params.key` is required and is parsed case-insensitively as `Microsoft.Xna.Framework.Input.Keys`.

Request:
```json
→ { "jsonrpc": "2.0", "id": 14, "method": "input.key", "params": { "key": "Enter" } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 14, "result": { "ok": true, "tick": 84202 } }
```

Response (missing `params` — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 14, "error": { "code": -32602, "message": "params required" } }
```

Response (missing/empty `key` — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 14, "error": { "code": -32602, "message": "params.key required" } }
```

Response (unknown, numeric, combined, or reserved key — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 14, "error": { "code": -32602, "message": "unknown key: Return" } }
```

Response (no active menu — GameStateInvalid):
```json
← { "jsonrpc": "2.0", "id": 14, "error": { "code": -32003, "message": "input.key requires an active menu" } }
```

`tick` is `Game1.ticks` at the moment the key press was delivered.

**Preconditions:** a menu must be open (`Game1.activeClickableMenu != null`).
**Side effects:** calls `Game1.activeClickableMenu.receiveKeyPress(key)`, so behavior is menu-specific.
**Implemented in:** `src/Harness/Handlers/InputKeyHandler.cs`
**Tested in:** `tests/Protocol.Tests/InputKeyRequestSerializationTests.cs` (DTO shape) + `tests/Harness.Tests/InputKeyHandlerTests.cs` (validation and menu dispatch) + `tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs` (DSL wrapper shape).

### input.text

Sends text to the currently active top-level menu (`Game1.activeClickableMenu`). `params.text` is required. If the concrete menu exposes a `receiveTextInput(char)` or `receiveTextInput(string)` text-entry method, Frobby sends each character through that path. Otherwise it falls back to `receiveKeyPress` for supported characters: `A-Z`, `a-z`, `0-9`, and space. When `params.submit` is `true`, Frobby sends `Enter` after the text.

Request:
```json
→ { "jsonrpc": "2.0", "id": 15, "method": "input.text", "params": { "text": "OE", "submit": true } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 15, "result": { "ok": true, "tick": 84203 } }
```

Response (missing `params` — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 15, "error": { "code": -32602, "message": "params required" } }
```

Response (missing `text` — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 15, "error": { "code": -32602, "message": "params.text required" } }
```

Response (unsupported fallback character — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 15, "error": { "code": -32602, "message": "unsupported character for input.text fallback: U+0021" } }
```

Response (no active menu — GameStateInvalid):
```json
← { "jsonrpc": "2.0", "id": 15, "error": { "code": -32003, "message": "input.text requires an active menu" } }
```

`tick` is `Game1.ticks` at the moment the text input completed.

**Preconditions:** a menu must be open (`Game1.activeClickableMenu != null`).
**Side effects:** calls the active menu's text-input method when available, otherwise calls `receiveKeyPress` for each mapped character; optionally calls `receiveKeyPress(Keys.Enter)`.
**Implemented in:** `src/Harness/Handlers/InputTextHandler.cs`
**Tested in:** `tests/Protocol.Tests/InputTextRequestSerializationTests.cs` (DTO shape) + `tests/Harness.Tests/InputTextHandlerTests.cs` (validation, text-entry dispatch, and fallback dispatch) + `tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs` (DSL wrapper shape).

### input.click

Sends a mouse click to the currently active top-level menu (`Game1.activeClickableMenu`). `params.x` and `params.y` are required screen-space coordinates. `params.button` is optional and defaults to `left`; supported values are `left` and `right`.

Request:
```json
→ { "jsonrpc": "2.0", "id": 16, "method": "input.click", "params": { "x": 144, "y": 134 } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 16, "result": { "ok": true, "tick": 84204 } }
```

Response (missing `params` — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 16, "error": { "code": -32602, "message": "params required" } }
```

Response (missing coordinate — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 16, "error": { "code": -32602, "message": "params.x required" } }
```

Response (bad button — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 16, "error": { "code": -32602, "message": "params.button must be left or right" } }
```

Response (no active menu — GameStateInvalid):
```json
← { "jsonrpc": "2.0", "id": 16, "error": { "code": -32003, "message": "input.click requires an active menu" } }
```

`tick` is `Game1.ticks` at the moment the click was delivered.

**Preconditions:** a menu must be open (`Game1.activeClickableMenu != null`).
**Side effects:** calls `Game1.activeClickableMenu.receiveLeftClick(x, y)` for left clicks or `receiveRightClick(x, y)` for right clicks, so behavior is menu-specific.
**Implemented in:** `src/Harness/Handlers/InputClickHandler.cs`
**Tested in:** `tests/Harness.Tests/InputClickHandlerTests.cs` (validation and menu dispatch) + `tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs` (DSL wrapper shape).

### input.hover

Moves the deterministic test cursor to a screen-space coordinate and sends hover to the currently active top-level menu (`Game1.activeClickableMenu`). `params.x` and `params.y` are required non-negative screen-space coordinates.

Request:
```json
→ { "jsonrpc": "2.0", "id": 17, "method": "input.hover", "params": { "x": 690, "y": 270 } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 17, "result": { "ok": true, "tick": 84204 } }
```

Response (missing coordinate — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 17, "error": { "code": -32602, "message": "params.x required" } }
```

Response (no active menu — GameStateInvalid):
```json
← { "jsonrpc": "2.0", "id": 17, "error": { "code": -32003, "message": "input.hover requires an active menu" } }
```

`tick` is `Game1.ticks` at the moment the hover was delivered.

**Preconditions:** a menu must be open (`Game1.activeClickableMenu != null`).
**Side effects:** sets the scenario-scoped controlled cursor to `(x, y)` and calls `Game1.activeClickableMenu.performHoverAction(x, y)`. During `freeze.begin`, this controlled cursor overrides the default frozen `(0,0)` cursor so intentional hover screenshots remain deterministic. `scenario.begin` and `scenario.end` clear the controlled cursor.
**Implemented in:** `src/Harness/Handlers/InputHoverHandler.cs`
**Tested in:** `tests/Harness.Tests/InputHoverHandlerTests.cs` (validation and menu dispatch), `tests/Harness.Tests/ControlledCursorTests.cs` (frozen cursor policy), and `tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs` (DSL wrapper shape).

### input.click_text

Clicks the center of a captured `SpriteBatch.DrawString` text event in the currently active top-level menu. Supply `params.text` for `draw.text_find`'s `text_contains` behavior, `params.text_equals` for an exact text match, or `params.text_matches` for a regular expression match. `params.button` is optional and defaults to `left`; supported values are `left` and `right`. `params.case_sensitive` defaults to `true`, and `params.occurrence` is one-based for choosing among multiple matches.

`input.click_text` also accepts the text-draw region filters `in_rect`, `bounds_within_rect`, and `bounds_intersects_rect` to disambiguate duplicate labels.

Request:
```json
→ { "jsonrpc": "2.0", "id": 17, "method": "input.click_text", "params": { "text": "SUBMIT ORDER" } }
```

Request with a region filter:
```json
→ { "jsonrpc": "2.0", "id": 17, "method": "input.click_text", "params": { "text": "CLOSE", "bounds_intersects_rect": [730, 58, 70, 22] } }
```

Request with exact text matching:
```json
→ { "jsonrpc": "2.0", "id": 17, "method": "input.click_text", "params": { "text_equals": "CONTINUE" } }
```

Request with regex text matching:
```json
→ { "jsonrpc": "2.0", "id": 17, "method": "input.click_text", "params": { "text_matches": "^LAST TICK [0-9]{2}:[0-9]{2}.*BARS [0-9]+$" } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 17, "result": { "ok": true, "tick": 84204 } }
```

Response (missing text — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 17, "error": { "code": -32602, "message": "params.text, params.text_equals, or params.text_matches required" } }
```

Response (no active menu — GameStateInvalid):
```json
← { "jsonrpc": "2.0", "id": 17, "error": { "code": -32003, "message": "input.click_text requires an active menu" } }
```

Response (no captured match — GameStateInvalid):
```json
← { "jsonrpc": "2.0", "id": 17, "error": { "code": -32003, "message": "input.click_text could not find captured text: SUBMIT ORDER" } }
```

**Preconditions:** a menu must be open (`Game1.activeClickableMenu != null`). Text events must already be captured by `draw.arm` plus enough wait time for the menu to draw before calling `input.click_text`.
**Side effects:** calls `receiveLeftClick(centerX, centerY)` or `receiveRightClick(centerX, centerY)` on the active menu, where the coordinates come from the captured text bounds.
**Implemented in:** `src/Harness/Handlers/InputClickTextHandler.cs`
**Tested in:** `tests/Harness.Tests/InputClickTextHandlerTests.cs` (validation, matching, occurrence, region filters, and menu dispatch) + `tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs` (DSL wrapper shape).

### input.click_menu_button

Clicks the center of a reflected button region on the current custom menu panel. Supply either `params.id` for a stable internal button id or `params.label` / `params.text_equals` for an exact visible button label. `params.button` is optional and defaults to `left`; supported values are `left` and `right`. `params.repeat` is optional and defaults to `1`.

This RPC is intended for custom mod menus whose button labels are short, repeated, or otherwise awkward to target through draw-text bounds. It still clicks through the real active menu, but resolves the button center from the mod's exposed `Id`, `Label`, and `Bounds` button-region properties instead of hard-coded scenario coordinates.

Request:
```json
-> { "jsonrpc": "2.0", "id": 18, "method": "input.click_menu_button", "params": { "id": "shares-plus", "repeat": 10 } }
```

Response (success):
```json
<- { "jsonrpc": "2.0", "id": 18, "result": { "ok": true, "tick": 84204 } }
```

Response (missing target - InvalidParams):
```json
<- { "jsonrpc": "2.0", "id": 18, "error": { "code": -32602, "message": "params.id or params.label required" } }
```

Response (bad repeat - InvalidParams):
```json
<- { "jsonrpc": "2.0", "id": 18, "error": { "code": -32602, "message": "params.repeat must be >= 1" } }
```

Response (no button match - GameStateInvalid):
```json
<- { "jsonrpc": "2.0", "id": 18, "error": { "code": -32003, "message": "input.click_menu_button could not find menu button: shares-plus" } }
```

**Preconditions:** a custom active menu must be open and expose its current panel through a `_currentPanel` field. Matching button-region fields or properties on that panel must expose `Id`, `Label`, and `Bounds` properties.
**Side effects:** calls `receiveLeftClick(centerX, centerY)` or `receiveRightClick(centerX, centerY)` on the active menu once per `repeat`.
**Implemented in:** `src/Harness/Handlers/InputClickMenuButtonHandler.cs`
**Tested in:** `tests/Harness.Tests/InputClickMenuButtonHandlerTests.cs` (validation, id/label matching, repeat, right-click dispatch, and menu dispatch).

### input.click_menu_choice

Clicks a structured Stardew menu choice by reflected response key or visible
response text. This is intended for `DialogueBox` question menus and similar
Stardew-native menus where `state.menu.choices` exposes `key`/`text` entries.
The handler hovers the matched response component before clicking so menus that
track selected response via hover behave like a player click.

Request:
```json
→ { "jsonrpc": "2.0", "id": 19, "method": "input.click_menu_choice", "params": { "text_equals": "Pet Dusty" } }
```

Request by key:
```json
→ { "jsonrpc": "2.0", "id": 19, "method": "input.click_menu_choice", "params": { "key": "0" } }
```

Request with regex text matching:
```json
→ { "jsonrpc": "2.0", "id": 19, "method": "input.click_menu_choice", "params": { "text_matches": "^Pet " } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 19, "result": { "ok": true, "tick": 84204 } }
```

Response (missing target — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 19, "error": { "code": -32602, "message": "params.key, params.text, params.text_equals, or params.text_matches required" } }
```

Response (no matching choice — GameStateInvalid):
```json
← { "jsonrpc": "2.0", "id": 19, "error": { "code": -32003, "message": "input.click_menu_choice could not find menu choice: Pet Dusty" } }
```

**Preconditions:** an active menu must expose a response collection (`responses`,
`answers`, or `questionChoices`) and matching clickable response components
(`responseCC`, `choices`, or equivalent reflected collection).
**Side effects:** calls `performHoverAction(centerX, centerY)` and then
`receiveLeftClick` or `receiveRightClick` on the active menu.
**Implemented in:** `src/Harness/Handlers/InputClickMenuChoiceHandler.cs`
**Tested in:** `tests/Harness.Tests/InputClickMenuChoiceHandlerTests.cs`.

### input.click_menu_advance

Acknowledges the active menu without relying on text capture. This is useful for
generic event/dialogue advancement where the menu has a next/OK/done button or
where a bottom-right dialogue click is the standard player action.

Request:
```json
→ { "jsonrpc": "2.0", "id": 20, "method": "input.click_menu_advance", "params": {} }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 20, "result": { "ok": true, "tick": 84204 } }
```

Response (no active menu — GameStateInvalid):
```json
← { "jsonrpc": "2.0", "id": 20, "error": { "code": -32003, "message": "input.click_menu_advance requires an active menu" } }
```

The handler first looks for known reflected advance buttons such as
`nextDialogueButton`, `nextButton`, `okButton`, or `doneButton`. If none exist,
it clicks the menu/dialogue bottom-right fallback and also sends common
acknowledgement keys (`X`, `Enter`, and `Space`) to support Stardew-native
dialogue boxes.

**Preconditions:** an active menu must be open.
**Side effects:** calls hover/click or key acknowledgement on the active menu.
**Implemented in:** `src/Harness/Handlers/InputClickMenuAdvanceHandler.cs`
**Tested in:** `tests/Harness.Tests/InputClickMenuAdvanceHandlerTests.cs`.

### input.hover_text

Hovers the center of a captured `SpriteBatch.DrawString` text event in the currently active top-level menu. Supply `params.text` for `draw.text_find`'s `text_contains` behavior, `params.text_equals` for an exact text match, or `params.text_matches` for a regular expression match. `params.case_sensitive` defaults to `true`, and `params.occurrence` is one-based for choosing among multiple matches.

`input.hover_text` also accepts the text-draw region filters `in_rect`, `bounds_within_rect`, and `bounds_intersects_rect` to disambiguate duplicate labels.

Request:
```json
→ { "jsonrpc": "2.0", "id": 18, "method": "input.hover_text", "params": { "text_equals": "2.15B g", "bounds_within_rect": [560, 238, 308, 74] } }
```

Request with regex text matching:
```json
→ { "jsonrpc": "2.0", "id": 18, "method": "input.hover_text", "params": { "text_matches": "^CASH [0-9,]+g$" } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 18, "result": { "ok": true, "tick": 84204 } }
```

Response (missing text — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 18, "error": { "code": -32602, "message": "params.text, params.text_equals, or params.text_matches required" } }
```

Response (no captured match — GameStateInvalid):
```json
← { "jsonrpc": "2.0", "id": 18, "error": { "code": -32003, "message": "input.hover_text could not find captured text: 2.15B g" } }
```

**Preconditions:** a menu must be open (`Game1.activeClickableMenu != null`). Text events must already be captured by `draw.arm` plus enough wait time for the menu to draw before calling `input.hover_text`.
**Side effects:** sets the scenario-scoped controlled cursor to the captured text center and calls `performHoverAction(centerX, centerY)` on the active menu.
**Implemented in:** `src/Harness/Handlers/InputHoverTextHandler.cs`
**Tested in:** `tests/Harness.Tests/InputHoverTextHandlerTests.cs` (validation, matching, occurrence, and menu dispatch) + `tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs` (DSL wrapper shape).

Runner scenario convenience:

- `{ "action": "ui.wait_text", "args": { "text_matches": "^SUBMIT [A-Z]+$" } }` is a runner-only step, not an RPC method. It repeatedly calls `draw.arm`, waits briefly, and polls `draw.text_find` with `disarm_after_snapshot: true` until the label is captured.
- `{ "action": "ui.click_text", "args": { "text": "SUBMIT ORDER" } }` performs the same wait and then calls `input.click_text`.
- `{ "action": "ui.hover_text", "args": { "text_equals": "2.15B g" } }` performs the same wait and then calls `input.hover_text`.
- Generic runner RPC steps, `state.assert`, `ui.click_text`, and `ui.hover_text`
  accept `timeout_ms` so a stalled harness call fails the scenario instead of
  hanging the run. The default per-step RPC timeout is 10000 ms.
- `{ "action": "wait.location", "args": { "location": "ExampleTownEast", "x": 10, "y": 20 } }` is also runner-only. It polls `state.player` until the farmer reaches the requested location and optional tile, then waits for `freeze.status` to report no active warp/fade transition. It accepts `timeout_ms` and `poll_ms` and reports the last observed location/tile on timeout.
- `{ "action": "wait.player", "args": { "health_lt": 100, "location": "ExampleDeepCave", "timeout_ms": 10000, "poll_ms": 100 } }` is runner-only. It polls `state.player` until player-state filters match. Supported filters are `location`, paired `x`/`y`, `health`, `health_lt`, `health_lte`, `health_gt`, `health_gte`, `swimming`, `bathing_clothes`, `mail_received`, `mail_for_tomorrow`, `event_seen`, `secret_note_seen`, `buff_id`, `buff_source`, `buff_effect`, `buff_effect_gte`, `buff_count_gte`, and `buff_any_effect_gte`; timeout details include the last observed health, location, tile, transient state, buff summary, and progression-list counts.
- `{ "action": "wait.special_order", "args": { "collection": "active", "key": "ExampleOrder", "objective_type": "Donate", "drop_box": "ExampleDropBox" } }` is runner-only. It polls `state.special_orders` until order and optional objective filters match. Supported collections are `active`, `available`, and `completed`. Supported order filters include `key`, `name`, `requester`, `order_type`, `special_rule`, `state`, `is_timed`, and `ready_for_removal`; supported objective filters include `objective_type`, `objective_runtime_type`, `drop_box`, `drop_box_location`, `target_name`, `accepted_context_tag`, `current_count`, `current_count_gte`, `objective_max_count`, and `complete`. It accepts `min_count`, optional `max_count`, `timeout_ms`, and `poll_ms`; timeout details include last observed active/available/completed keys.
- `{ "action": "wait.npc_location", "args": { "name": "Riley", "location": "ExampleVineyard", "x": 20, "y": 32 } }` is runner-only. It polls `state.npc` until the named NPC reaches the requested location and optional tile, then waits for `freeze.status` to report no active warp/fade transition. It accepts `timeout_ms` and `poll_ms` and reports the last observed location/tile on timeout.
- `{ "action": "wait.location_content", "args": { "location": "ExampleForestEdge", "collection": "resource_clumps", "name": "Log", "min_count": 2 } }` is runner-only.
  It polls `state.location` for the named location until the selected collection
  has enough matching entries. Supported collections are `objects`,
  `resource_clumps`, `monsters`, `critters`, and `debris`. Filters are exact-match and
  optional: `name`, `type`, `kind`, `id`, `qualified_id`, `health`,
  `max_health`, `damage`, `revive_timer`, `runtime_type`,
  `minutes_until_ready`, `stack`, `quality`, `category`, `sprite_texture`,
  `big_craftable`, `held_object_id`,
  `held_object_qualified_id`, and `x`/`y` tile. For `objects`, contained-item
  filters can require a matching item inside the object: `contains_item_id`,
  `contains_item_qualified_id`, `contains_item_name`, `contains_item_stack`,
  `contains_item_stack_gte`, `contains_item_quality`, and
  `contains_item_category`. It accepts
  `min_count`, optional `max_count`, `timeout_ms`, and `poll_ms`. Monster
  numeric comparisons are supported with `health_lt`, `health_lte`,
  `health_gt`, `health_gte`, matching `max_health_*` filters, matching
  `damage_*` filters, and matching `revive_timer_*` filters. Debris and object
  numeric comparisons are supported with `minutes_until_ready_*`,
  `stack_*`, `quality_*`, and `category_*` filters. Use `min_count: 0` with `max_count: 0` to wait for no
  matching content. On timeout, it reports the last matched and total counts for
  the selected collection.
- `{ "action": "wait.visual_effects", "args": { "location": "Example.VisualLocation", "temporary_sprites": { "texture_asset": "ExampleMod/Visuals/Effects", "source_rect": [0, 32, 16, 16], "min_count": 1 } } }` is runner-only. It polls `state.visual_effects` until temporary sprite, light source, ambient light, or weather debris criteria match. Supported temporary sprite filters include `texture_asset`, `source_rect`, `color`, `runtime_type`, `min_count`, and `max_count`; light source filters include `id`, `id_contains`, `color`, `min_count`, and `max_count`. It also accepts `ambient_light`, `weather_debris_min_count`, `timeout_ms`, and `poll_ms`, and reports the last observed match counts on timeout. This is state-level evidence; use draw, bitmap, or screenshot actions for final rendered proof.
- `{ "action": "wait.event_active", "args": { "id": "520702", "location": "BusStop", "is_festival": false } }` is runner-only. It polls `state.event` until an active event matches the optional `id`, `location`, and `is_festival` filters.
- `{ "action": "wait.event_complete", "args": { "id": "520702" } }` is runner-only. It polls `state.event` until the event has completed; when `id` is supplied it must first observe that active id before accepting completion.
- `{ "action": "wait.menu", "args": { "choice_text": "Pet Dusty" } }` is runner-only. It polls `state.menu` until an active menu matches optional `present`, `type`, text, choice key/text, or `ready` filters. Text filters inspect readable menu extras such as `dialogue_text`, `message_text`, and `question_text`; choice filters inspect `state.menu.choices`.
- `{ "action": "event.advance", "args": { "choice_text": "Pet Dusty" } }` waits for the matching menu choice and then calls `input.click_menu_choice`. Without a choice/text target it waits for an active menu and calls `input.click_menu_advance`; `repeat` and `interval_ms` can advance multi-page dialogue. `ui.acknowledge` uses the same menu-advance path.
- `{ "action": "state.assert", "args": { "params": { "name": "Riley" }, "expr": "state.npc.hearts == 4" } }` can pass `args.params` through to the state RPC named in the expression before evaluating it.

### wait.player runner action

`wait.player` is a runner-only scenario action; it is not a harness RPC. It
polls `state.player` until the supplied player-state filters match, then
continues to the next step.

Request shape:
```json
{ "action": "wait.player", "args": { "health_lt": 100, "location": "ExampleDeepCave", "timeout_ms": 10000, "poll_ms": 100 } }
```

Supported filters are `location`, paired `x`/`y`, `health`, `health_lt`,
`health_lte`, `health_gt`, `health_gte`, `swimming`, `bathing_clothes`,
`mail_received`, `mail_for_tomorrow`, `event_seen`, `secret_note_seen`,
`buff_id`, `buff_source`, `buff_effect`, `buff_effect_gte`, `buff_count_gte`, and
`buff_any_effect_gte`; timeout details include the last observed health,
location, tile, transient state, buff summary, and progression-list counts,
including `secret_notes_seen=<count>`. Tile filters must be supplied as a
complete `x`/`y` pair. `secret_note_seen` is a positive integer id and matches
when `state.player.secret_notes_seen` contains that id.

### wait.special_order runner action

`wait.special_order` is a runner-only scenario action; it is not a harness RPC.
It polls `state.special_orders` until order and optional objective filters match.

Request shape:
```json
{ "action": "wait.special_order", "args": { "collection": "active", "key": "ExampleOrder", "objective_type": "Donate", "drop_box": "ExampleDropBox", "timeout_ms": 15000, "poll_ms": 100 } }
```

Supported collections are `active`, `available`, and `completed`. Supported
order filters are `key`, `name`, `requester`, `order_type`, `special_rule`,
`state`, `is_timed`, and `ready_for_removal`. Supported objective filters are
`objective_type`, `objective_runtime_type`, `drop_box`, `drop_box_location`,
`target_name`, `accepted_context_tag`, `current_count`, `current_count_gte`,
`objective_max_count`, and `complete`. Count filters use `min_count` and
optional `max_count`. Timeout diagnostics include the last observed
active/available/completed order keys.

The three `ui.*_text` convenience steps accept `text`, `text_equals`,
`text_matches`, `case_sensitive`, `occurrence`, `min_count`, `timeout_ms`,
`poll_ms`, `capture_ticks`, `in_rect`, `bounds_within_rect`, and
`bounds_intersects_rect`. `ui.click_text` also accepts `button`.

`wait.event_active` and `wait.event_complete` accept `id`, `location`,
`timeout_ms`, and `poll_ms`. Active-event screenshots should use live or
next-frame capture because `freeze.begin` rejects cutscenes while `Game1.eventUp`
is true.

`wait.menu` accepts `present`, `type`, `text`, `text_equals`, `text_matches`,
`choice_key`, `choice_text`, `choice_text_contains`, `choice_text_matches`,
`ready`, `button`, `case_sensitive`, `timeout_ms`, and `poll_ms`.

### shop.open

Opens a data-backed Stardew shop by shop ID. This is a semantic test primitive for
shop-data validation; use `world.interact_npc` plus click/text steps when the NPC
conversation path itself is under test.

Request:
```json
→ { "jsonrpc": "2.0", "id": 18, "method": "shop.open", "params": { "shop_id": "Carpenter", "owner_name": "Robin", "force_open": true } }
```

Response:
```json
← { "jsonrpc": "2.0", "id": 18, "result": { "ok": true, "tick": 84204, "shop_id": "Carpenter", "menu_type": "ShopMenu" } }
```

`force_open` defaults to `true`, which opens the shop data directly and bypasses
schedule/open-hours checks for deterministic scenarios. Set it to `false` when the
test specifically needs Stardew's normal shop-availability behavior.

**Preconditions:** a world must be loaded.
**Side effects:** opens `Game1.activeClickableMenu` via Stardew's shop-opening API.
**Implemented in:** `src/Harness/Handlers/ShopOpenHandler.cs`
**Tested in:** `tests/Protocol.Tests/ShopRequestSerializationTests.cs` + `tests/Harness.Tests/ShopOpenHandlerTests.cs`.

### shop.purchase

Purchases an item from the active `ShopMenu` by qualified or raw item ID. The handler searches
the full shop inventory, not just the currently visible page, checks the player's gold,
creates the salable instance, debits the total price, and adds the item to inventory.

Request:
```json
→ { "jsonrpc": "2.0", "id": 19, "method": "shop.purchase", "params": { "item_id": "(F)example_terminal", "count": 1 } }
```

Response:
```json
← { "jsonrpc": "2.0", "id": 19, "result": { "ok": true, "tick": 84205, "shop_id": "Carpenter", "item_id": "(F)example_terminal", "display_name": "Example Terminal", "count": 1, "unit_price": 25000, "previous_money": 30000, "money": 5000 } }
```

Use the exact `qualified_id` from `state.shop.items` for the strictest match. Raw
`item_id` matching is also supported for mods whose scenario data naturally refers
to item ids without Stardew's qualifier prefix.

**Preconditions:** a world must be loaded and `Game1.activeClickableMenu` must be a `ShopMenu`.
**Side effects:** debits player gold and adds the purchased item to inventory.
**Implemented in:** `src/Harness/Handlers/ShopPurchaseHandler.cs`
**Tested in:** `tests/Protocol.Tests/ShopRequestSerializationTests.cs` + `tests/Harness.Tests/ShopPurchaseHandlerTests.cs`.

### world.place_inventory_furniture

Moves a furniture item from the player's inventory into a loaded location. This differs
from `world.place_furniture`, which creates a new item through `ItemRegistry`; use this
when a test needs to prove a purchased or otherwise obtained furniture item is usable.

Request:
```json
→ { "jsonrpc": "2.0", "id": 20, "method": "world.place_inventory_furniture", "params": { "id": "(F)example_terminal", "location": "FarmHouse", "x": 8, "y": 9, "remove_existing": true } }
```

Response:
```json
← { "jsonrpc": "2.0", "id": 20, "result": { "ok": true, "tick": 84206, "id": "(F)example_terminal", "location": "FarmHouse", "tile": { "x": 8, "y": 9 }, "source_slot": 5 } }
```

**Preconditions:** a world must be loaded; the player inventory must contain the requested qualified item ID; the matching item must be furniture.
**Side effects:** removes the matched inventory item from its source slot and adds it to the target location's furniture collection.
**Implemented in:** `src/Harness/Handlers/WorldPlaceInventoryFurnitureHandler.cs`
**Tested in:** `tests/Protocol.Tests/PlaceInventoryFurnitureRequestSerializationTests.cs` + `tests/Harness.Tests/WorldPlaceInventoryFurnitureHandlerTests.cs`.

### world.place_inventory_object

Places one existing inventory object into the player's current location through
Stardew's native object placement path. This is for player-like placement flows;
use `world.place_object` for direct setup and `world.explode_tile` for direct
explosion semantics.

Request:
```json
→ { "jsonrpc": "2.0", "id": 22, "method": "world.place_inventory_object",
     "params": { "id": "(O)287", "location": "Frobby_CombatLab", "x": 9, "y": 8, "slot": 12, "facing": "right" } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 22, "result": {
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
   } }
```

`location` is a current-location guard, not a remote placement target. Warp the
farmer to the target location first. `id` may match `QualifiedItemId` or
`ItemId`; scenarios should prefer qualified ids. `slot` is optional and should
only be used when the inventory contains multiple matching ids.

**Preconditions:** world loaded; matching inventory item exists; selected item
is a `StardewValley.Object`; current location matches `location` when supplied.
**Side effects:** invokes native object placement and consumes one active
inventory item through Stardew's player inventory path. Placement may create a
timed object such as a bomb.
**Implemented in:** `src/Harness/Handlers/WorldPlaceInventoryObjectHandler.cs`
**Tested in:** `tests/Protocol.Tests/PlaceInventoryObjectSerializationTests.cs`,
`tests/Harness.Tests/WorldPlaceInventoryObjectHandlerTests.cs`,
`tests/Runner.Tests/ScenarioRunnerTests.cs`, and
`tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs`.

### draw.arm

Arms the draw-event recorder for the next N update ticks. When `params.output_path` is set, the buffer is also flushed to a JSONL file at disarm time; when omitted, capture is in-memory only and retrievable via `draw.snapshot`. `params` is entirely optional — omit to arm for the default 30-tick budget with in-memory capture.

Request (in-memory only, defaults):
```json
→ { "jsonrpc": "2.0", "id": 12, "method": "draw.arm" }
```

Request (explicit ticks + output file):
```json
→ { "jsonrpc": "2.0", "id": 12, "method": "draw.arm", "params": { "ticks": 60, "output_path": "/tmp/draws.jsonl" } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 12, "result": { "ok": true, "tick": 84200 } }
```

Response (`ticks < 1`, malformed `ticks`, or unparseable params — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 12, "error": { "code": -32602, "message": "params.ticks must be >= 1" } }
```

`tick` is `Game1.ticks` at the moment the recorder was armed. If the world isn't ready (title screen / mid-load), arming is deferred until `Game1.gameMode == playingGameMode` — the response still returns `ok:true` immediately; the actual capture begins on the first qualifying tick. Disarm via `draw.disarm` at any time (or let the tick budget expire).

**Preconditions:** none beyond the harness running — works from the title screen via deferred-arm.
**Side effects:** primes the ring buffer, saves current `Game1.eventUp`/`displayHUD`, sets both per `.claude/rules/determinism.md`. Every `SpriteBatch.Draw` call and supported `SpriteBatch.DrawString` call until disarm is captured. If `output_path` is set, on disarm the texture draw buffer is written to disk as NDJSON.
**Implemented in:** `src/Harness/Handlers/DrawArmHandler.cs`
**Tested in:** `tests/Protocol.Tests/DrawArmRequestSerializationTests.cs` (DTO shape) + `tests/Harness.Tests/DrawArmHandlerTests.cs` (error-path unit tests).

### draw.disarm

Stops the recorder, restores suppressed state (`eventUp`/`displayHUD`), and — if `output_path` was set at arm time — flushes the ring buffer to disk. In-memory captures remain accessible via `draw.snapshot` until the next `draw.arm` resets the buffer. Takes no params.

Request:
```json
→ { "jsonrpc": "2.0", "id": 13, "method": "draw.disarm" }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 13, "result": { "ok": true, "tick": 84260 } }
```

Calling `draw.disarm` when not armed is a no-op — `Recorder.Disarm` short-circuits and the handler still returns `ok:true` with the current tick. No dedicated error path.

**Preconditions:** none.
**Side effects:** unarms the recorder, restores saved `Game1.eventUp`/`displayHUD`, and flushes to disk if an output path was registered at arm time.
**Implemented in:** `src/Harness/Handlers/DrawDisarmHandler.cs`
**Tested in:** end-to-end via scenario integration once the runner's draw assertions land (D1.4+).

### draw.snapshot

Returns the currently-buffered draw events from the recorder's in-memory ring buffer without flushing to disk. Takes no params. Safe to call whether the recorder is armed or disarmed — the buffer is retained until the next `draw.arm` call resets it.

Request:
```json
→ { "jsonrpc": "2.0", "id": 14, "method": "draw.snapshot" }
```

Response (success — events captured):
```json
← { "jsonrpc": "2.0", "id": 14, "result": {
      "events": [
        { "tick": 5, "call": 1, "tex_ref": 42, "tex_w": 16, "tex_h": 16, "src": [0,0,16,16], "dst": [0,0,64,64], "col": [255,255,255,255], "rot": 0, "orig": [0,0], "fx": 0, "z": 0.5 }
      ],
      "meta": { "ticks": 10, "events": 1, "dropped": 0, "resolved_count": 1 }
   } }
```

Response (empty buffer — never armed, or freshly disarmed with no draws captured):
```json
← { "jsonrpc": "2.0", "id": 14, "result": { "events": [], "meta": { "ticks": 0, "events": 0, "dropped": 0, "resolved_count": 0 } } }
```

Event fields match the JSONL capture format established in the M0 spike's `DrawEventWriter`: `tex_ref` is the per-process-stable texture identity (see `DrawEvent.TextureRefId`); `src` is `null` when the overload didn't supply a source rect, otherwise `[x, y, w, h]`; `dst`, `col`, `orig` are fixed-length arrays for the corresponding fields; `fx` is the `SpriteEffects` enum cast to int. `meta.events` equals `events.Length` (after ring-buffer drops); `meta.dropped` counts writes that overflowed the fixed-size buffer; `meta.ticks` is the number of update ticks observed while armed.

`resolved_count` — how many events' `texture_asset` resolved via Tier 1 per `.claude/rules/draw-call-recorder.md`. Divide by `events` for the resolution rate. Primary diagnostic for "my texture_asset filter silently doesn't match" — low rates indicate engine-preloaded textures the harness's ContentLoad patch didn't see. Tier 2 hash fallback (M2) will raise this rate for vanilla content.

**Preconditions:** none — safe to call from the title screen before any arm; returns an empty snapshot.
**Side effects:** none — read-only.
**Implemented in:** `src/Harness/Handlers/DrawSnapshotHandler.cs`
**Tested in:** `tests/Protocol.Tests/DrawEventSnapshotSerializationTests.cs` (DTO shape) + `tests/Harness.Tests/DrawSnapshotHandlerTests.cs` (empty-buffer + field-mapping unit tests).

### draw.find

Query the captured draw-event buffer with a filter DSL and return every matching event. `params` is entirely optional — omit for an unfiltered dump equivalent to `draw.snapshot` minus the `meta` envelope. All supplied filter fields AND together; a filter with no fields matches every event.

Request (filtered):
```json
→ { "jsonrpc": "2.0", "id": 15, "method": "draw.find", "params": { "in_rect": [0, 0, 1280, 720], "color": [255, 255, 255, 255] } }
```

Request (no filter — returns every buffered event):
```json
→ { "jsonrpc": "2.0", "id": 15, "method": "draw.find" }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 15, "result": {
      "events": [
        { "tick": 5, "call": 1, "tex_ref": 42, "tex_w": 16, "tex_h": 16, "src": [0,0,16,16], "dst": [0,0,64,64], "col": [255,255,255,255], "rot": 0, "orig": [0,0], "fx": 0, "z": 0.5 }
      ],
      "count": 1
   } }
```

Response (empty buffer or no matches):
```json
← { "jsonrpc": "2.0", "id": 15, "result": { "events": [], "count": 0 } }
```

Filter DSL fields (all optional, all ANDed):

- `texture_asset` (string) — exact match on the texture's resolved asset path (e.g. `"Characters/Abigail"`, `"Mods/MyMod/sprites"`). Resolution is Tier 1 per `.claude/rules/draw-call-recorder.md`: a Harmony postfix on `ContentManager.Load<Texture2D>` populates a weak-ref map at content-load time, queried at snapshot time. Textures not seen by the loader (dynamic render targets, or textures loaded before the harness's patch activated) resolve as null and won't match a `texture_asset` filter — use secondary fields (`tex_w`, `source_rect`) to query those.
- `in_rect` (`[x, y, w, h]`) — event `dst` rect must be fully contained in the filter rect.
- `layer_depth_range` (`[min, max]`) — inclusive on both ends.
- `color` (`[r, g, b, a]`) — exact match.
- `source_rect` (`[x, y, w, h]`) — exact match; events with a null source rect (per-overload default) do not match when this field is supplied.

Event `events[]` entries use the same shape as `draw.snapshot` — `DrawSnapshotHandler.ToDto` is the shared projection. `count` equals `events.Length` (no ring-buffer drop accounting at this layer; consult `draw.snapshot`'s `meta.dropped` for that).

**Preconditions:** none — safe from title / pre-arm (returns empty).
**Side effects:** none — read-only.
**Implemented in:** `src/Harness/Handlers/DrawFindHandler.cs`
**Tested in:** `tests/Harness.Tests/DrawFilterTests.cs` (matcher DSL — 8 cases covering the 5 filter fields + empty-filter + AND composition + null-source-rect edge).

### draw.assert_contains

Assertion primitive. Counts matches of a filter against the captured draw buffer and returns `passed: (matched_count >= min_count)`. The `filter` field is **required** on the request (assertions without a filter are degenerate — always-pass). `min_count` defaults to `1`; `message` is an optional scenario-authored description echoed in the response for reporter output.

Request:
```json
→ { "jsonrpc": "2.0", "id": 16, "method": "draw.assert_contains", "params": { "filter": { "layer_depth_range": [0.0, 1.0] }, "min_count": 1, "message": "expected some draw" } }
```

Response (success — passed):
```json
← { "jsonrpc": "2.0", "id": 16, "result": { "passed": true, "matched_count": 42, "min_count": 1, "message": "expected some draw" } }
```

Response (success — failed; the RPC layer doesn't convert failed asserts into errors, the scenario runner does):
```json
← { "jsonrpc": "2.0", "id": 16, "result": { "passed": false, "matched_count": 0, "min_count": 3, "message": "expected 3+ magenta cursors" } }
```

Response (missing `params` or empty body — InvalidParams; `filter` is required even though its own fields are all optional):
```json
← { "jsonrpc": "2.0", "id": 16, "error": { "code": -32602, "message": "params required" } }
```

Filter DSL is identical to `draw.find` — see that section for field semantics. An empty-object `filter: {}` is legal and matches every event (useful for "at least N draws occurred during the armed window").

**Preconditions:** none — safe from title / pre-arm (empty buffer → `matched_count: 0` → `passed: false` unless `min_count` is `0`).
**Side effects:** none — read-only.
**Implemented in:** `src/Harness/Handlers/DrawAssertContainsHandler.cs`
**Tested in:** `tests/Harness.Tests/DrawFilterTests.cs` (matcher DSL shared with `draw.find`).

### draw.assert_not_contains

Inverse of `draw.assert_contains` — succeeds when no captured draw event matches the filter.

**Params:**

```json
{"filter": {...DrawFilter shape...}, "message": "optional"}
```

**Response:**

```json
{"passed": true, "matched_count": 0, "min_count": 0, "message": null}
```

`passed` is `true` iff `matched_count == 0`. `min_count` is always `0` in the response (kept in the shape for parity with `draw.assert_contains`). `message` passes through from the request for consumer display.

**Errors:** `InvalidParams (-32602)` if the filter fails validation (same code path as `draw.assert_contains`).

### draw.text_snapshot

Returns the currently-buffered `SpriteBatch.DrawString` events from the recorder's in-memory text ring buffer. Takes no params. The text buffer is armed and reset by the same `draw.arm` window as texture draw capture.

Request:
```json
→ { "jsonrpc": "2.0", "id": 18, "method": "draw.text_snapshot" }
```

Response:
```json
← { "jsonrpc": "2.0", "id": 18, "result": {
      "events": [
        {
          "tick": 101,
          "call": 7,
          "text": "STARBERG TERMINAL v0.1.0",
          "x": 64,
          "y": 48,
          "width": 180,
          "height": 24,
          "color": [255, 176, 0, 255],
          "layer_depth": 0.91
        }
      ],
      "meta": { "ticks": 30, "events": 1, "dropped": 0 }
   } }
```

`x` and `y` are the integer projection of the `DrawString` position. `width` and `height` are the measured text bounds from `SpriteFont.MeasureString`, scaled by the `DrawString` scale, normalized to non-negative dimensions, and rounded up to integer pixels. Bounds are axis-aligned at the `DrawString` position; rotation, origin, and sprite effects are not expanded into transformed bounds. `color` is `[r, g, b, a]`. `meta.dropped` counts writes that overflowed the text ring buffer.

**Implemented in:** `src/Harness/Handlers/DrawTextSnapshotHandler.cs`
**Tested in:** `tests/Protocol.Tests/TextDrawEventSnapshotSerializationTests.cs` + `tests/Harness.Tests/DrawTextSnapshotHandlerTests.cs`.

### draw.text_find

Query the captured text draw buffer with a `TextDrawFilter` and return every matching event. `params` is optional; omit it to return every captured text event.

Request:
```json
→ { "jsonrpc": "2.0", "id": 19, "method": "draw.text_find", "params": { "text_contains": "CASH", "case_sensitive": false } }
```

Response:
```json
← { "jsonrpc": "2.0", "id": 19, "result": { "events": [/* TextDrawEventDto */], "count": 1 } }
```

Filter DSL fields (all optional, all ANDed):

- `text_contains` (string) — substring match.
- `text_equals` (string) — whole-string match.
- `text_matches` (string) — regular expression match.
- `case_sensitive` (bool) — defaults to `true`.
- `in_rect` (`[x, y, w, h]`) — captured text position must be inside the rect.
- `bounds_within_rect` (`[x, y, w, h]`) — captured text bounds must be fully contained in the rect.
- `bounds_intersects_rect` (`[x, y, w, h]`) — captured text bounds must intersect the rect.
- `color` (`[r, g, b, a]`) — exact match.
- `color_any` (`[[r, g, b, a], ...]`) — event color must match one listed color.
- `layer_depth_range` (`[min, max]`) — inclusive on both ends.
- `disarm_after_snapshot` (bool) — when `true`, atomically disarm draw recording
  immediately after copying the text buffer. Runner UI helpers use this to avoid
  a separate `draw.disarm` RPC racing with the next freeze or UI action.

**Implemented in:** `src/Harness/Handlers/DrawTextFindHandler.cs`
**Tested in:** `tests/Harness.Tests/TextDrawFilterTests.cs`.

### draw.assert_text_contains

Assertion primitive for captured text. Counts matching logical visible text instances against the text buffer and returns `passed: (matched_count >= min_count && matched_count <= max_count)` when `max_count` is supplied, otherwise `passed: (matched_count >= min_count)`. Repeated samples of the same text in the same nearby bounds are collapsed so multi-frame captures and shadowed/multi-pass text rendering don't inflate `matched_count`. `min_count` defaults to `1`; `max_count` is optional; `message` is echoed.

Request:
```json
→ { "jsonrpc": "2.0", "id": 20, "method": "draw.assert_text_contains", "params": {
      "filter": { "text_contains": "CASH & WIRES", "case_sensitive": true },
      "min_count": 1,
      "max_count": 1,
      "message": "Cash panel should be visible"
   } }
```

Response:
```json
← { "jsonrpc": "2.0", "id": 20, "result": { "passed": true, "matched_count": 1, "min_count": 1, "max_count": 1, "message": "Cash panel should be visible" } }
```

**Errors:** `InvalidParams (-32602)` if params are missing, `min_count < 1`, `max_count < min_count`, or the filter shape is invalid.

**Implemented in:** `src/Harness/Handlers/DrawAssertTextContainsHandler.cs`
**Tested in:** `tests/Harness.Tests/DrawAssertTextContainsHandlerTests.cs`.

### draw.assert_text_not_contains

Inverse text assertion. Succeeds when no captured text event matches the filter.

Request:
```json
→ { "jsonrpc": "2.0", "id": 21, "method": "draw.assert_text_not_contains", "params": { "filter": { "text_contains": "ERROR" }, "message": "Error text should be absent" } }
```

Response:
```json
← { "jsonrpc": "2.0", "id": 21, "result": { "passed": true, "matched_count": 0, "min_count": 0, "message": "Error text should be absent" } }
```

**Implemented in:** `src/Harness/Handlers/DrawAssertTextNotContainsHandler.cs`
**Tested in:** `tests/Harness.Tests/DrawAssertTextContainsHandlerTests.cs`.

### fixture.load

Initiates an asynchronous load of a save by folder name. RPC equivalent of the `harness_load` console command. `params.name` is **required** (non-empty string) — the save folder name (not a full path), e.g. `spring_day_1_clean`.

Request:
```json
→ { "jsonrpc": "2.0", "id": 17, "method": "fixture.load", "params": { "name": "spring_day_1_clean" } }
```

Response (success — load initiated):
```json
← { "jsonrpc": "2.0", "id": 17, "result": { "ok": true, "tick": 84200 } }
```

Response (missing `params` / missing/empty `name` — InvalidParams):
```json
← { "jsonrpc": "2.0", "id": 17, "error": { "code": -32602, "message": "params.name required" } }
```

Response (already in a save — GameStateInvalid):
```json
← { "jsonrpc": "2.0", "id": 17, "error": { "code": -32003, "message": "already in a save — return to title first" } }
```

Response (no such save — FixtureLoadFailed):
```json
← { "jsonrpc": "2.0", "id": 17, "error": { "code": -32002, "message": "no save named 'typo_fixture' (looked in /…/Saves/typo_fixture)" } }
```

`tick` is `Game1.ticks` at the moment the load was queued — a temporal anchor for "load started here; poll later." **Load is asynchronous:** SDV's `SaveGame.getLoadEnumerator` produces a coroutine that `Game1` advances over many update ticks (typically dozens to a few hundred, depending on save size). The handler returns as soon as the load is *initiated*, not when it completes. Callers should follow up with `state.player` in a wait-for-ready loop — the farmer's `name`/`location` will reflect the loaded save once the coroutine finishes, and SMAPI's `SaveLoaded` event fires at that point.

**Preconditions:** not currently in a save — `Context.IsWorldReady` must be `false` (i.e. on the title or loading screen); the named save folder must exist under `Constants.SavesPath` (SDV's enumerator is lazy and would otherwise silently accept a typo, surfacing as a world-ready timeout). Returning to title before a re-load is the caller's responsibility; there is no "switch save" shortcut in this handler.
**Side effects:** assigns `Game1.currentLoader = SaveGame.getLoadEnumerator(name)` and sets `Game1.gameMode = 6` (loadingMode); the save-load coroutine runs over the next several ticks.
**Implemented in:** `src/Harness/Handlers/FixtureLoadHandler.cs`
**Tested in:** `tests/Protocol.Tests/FixtureLoadRequestSerializationTests.cs` (DTO shape) + `tests/Harness.Tests/FixtureLoadHandlerTests.cs` (error-path unit tests).

### freeze.begin

Enter FREEZE: pin `Game1.currentGameTime`, halt NPCs, pin per-location RNG, flip
`eventUp`/`displayHUD`, gate the cursor-freeze patch. Multiple queries issued during
a FREEZE window see a consistent moment — draws captured while frozen all share a
tick number.

**Params:** none. Seed is inherited from `ScenarioState.Current.Seed` (set at
`scenario.begin`).

**Preconditions (strict):**

- `Context.IsWorldReady` — save loaded
- `!Game1.eventUp` — no cutscene
- `Game1.currentMinigame == null` — no minigame
- `!Game1.isWarping` — not mid-warp
- `DeterminismController.Frozen == false` — not already frozen
- An active scenario (scenario.begin ran)

Any violation → `GameStateInvalid (-32003)` with the failing check named.

When `freeze.begin` is used as a runner scenario step, the runner retries briefly if
the harness reports `freeze.begin requires !Game1.isWarping (mid-warp)`. This covers
UI tests that open/close menus or warp immediately before freezing. Scenario authors
may override the retry window with `args.settle_timeout_ms` and `args.poll_ms`.

**Response:**

```json
{"ok": true, "locations_pinned": 27, "npcs_halted": 145, "tick": 8421}
```

### freeze.end

Exit FREEZE: restore per-location RNGs, NPC states, and ambient flags in reverse order.

**Params:** none.

**Precondition:** `DeterminismController.Frozen == true`. Else `GameStateInvalid`.

**Response:**

```json
{"ok": true, "tick": 8421}
```

### freeze.status

Pure query — returns the current FREEZE state without mutating anything.

**Params:** none.

**Response:**

```json
{"frozen": true, "is_warping": false, "is_fading": false, "tick": 8421}
```

`is_fading` tracks Stardew's global fade/fade-to-black transition state. Runner
wait helpers use `is_warping` and `is_fading` together so scenarios do not
continue while a warp has changed state but the screen is still visually black.

## `bitmap.capture`

Capture the current backbuffer as a PNG. FREEZE-phase only unless
`allow_unfrozen` is explicitly set.

**Preconditions:**
- `scenario.begin` has been called (active scenario required).
- `freeze.begin` has been called (`DeterminismController.Frozen == true`).

**Params:**
```json
{ "allow_unfrozen": false, "region": { "x": 0, "y": 0, "w": 640, "h": 480 } }
```
- `allow_unfrozen` — optional bool, default `false`. Set to `true` for best-effort
  report screenshots outside a frozen assertion phase.
- `region` — optional object. All four fields required if present. Region must fit within the backbuffer; otherwise `InvalidParams -32602`.
- Omit `region` to capture the full backbuffer.

**Response:**
```json
{
  "path": "/home/user/.cache/sdv-test-framework/captures/shop_menu/bitmap_0.png",
  "width": 1280,
  "height": 720
}
```

- `path` — absolute path to the written PNG.
- `width`, `height` — dimensions of the written image (equal to `region.w/h` when a region was passed).

**Output path:** `~/.cache/sdv-test-framework/captures/<scenario-name>/bitmap_<N>.png` where `N` is chosen at capture time as `count(existing bitmap_*.png in dir)`. This means `N` monotonically increases across runs of the same scenario until the cache is cleaned. Captures across different scenarios are isolated in separate directories. See current.md "Capture-cache cleanup" TODO for M3 sweep semantics.

**Errors:**
- `GameStateInvalid -32003` — no active scenario.
- `GameStateInvalid -32003` — not in FREEZE phase.
- `InvalidParams -32602` — region out of bounds.
- `InternalError -32603` — backbuffer read / PNG encode / write failure.

## `bitmap.capture_next_frame`

Queue a bitmap capture after the next render pass and complete it on the following
update tick. Use this after a state-changing input RPC when immediate backbuffer
capture might race the render that reflects the new UI state, including active
menus and dialogue boxes.

The written PNG and response shape are identical to `bitmap.capture`; the capture
callback uses the same `allow_unfrozen` and `region` params at render time.

**Preconditions:**
- `scenario.begin` has been called (active scenario required).
- Unless `allow_unfrozen` is true, the scenario is still in FREEZE phase when the
  next render event fires.

**Params:**
```json
{ "allow_unfrozen": false, "timeout_ms": 2000, "region": { "x": 0, "y": 0, "w": 640, "h": 480 } }
```
- `timeout_ms` — optional int, default `2000`, must be `>= 1`. The request fails if
  no render/update cycle arrives before the timeout.
- `allow_unfrozen` and `region` match `bitmap.capture`.

**Response:** same as `bitmap.capture`.

**Errors:**
- `InvalidParams -32602` — `timeout_ms < 1` or region out of bounds.
- `GameStateInvalid -32003` — no active scenario, or not frozen when required.
- `InternalError -32603` — timeout, backbuffer read, PNG encode, or write failure.

### content.asset

Read one named asset through Stardew's live game-content pipeline and return a
bounded JSON summary. This is for validating the final runtime result after
Content Patcher, config, locale, and game-state conditions have applied; it does
not parse content-pack files as source of truth.

**Params:**
```json
{
  "name": "Data/Locations",
  "asset_type": "data",
  "include_keys": true,
  "keys_limit": 25,
  "entry_keys": ["ExampleTownEast"],
  "hash_texture": false
}
```

- `name` — required asset name, e.g. `Maps/ExampleTownEast`, `Data/Locations`,
  or a mod-owned texture path.
- `asset_type` — optional hint: `map`, `texture`, `data`, `string`, or `unknown`.
  Omit it to let the harness probe common runtime types.
- `include_keys` — for data dictionaries, include a bounded key list.
- `keys_limit` — max key count when `include_keys` is true. Valid range: 1-500.
- `entry_keys` — selected data dictionary entries to summarize by exact key.
- `hash_texture` — for textures, include a bounded content hash when possible.

Selected data entries include public scalar fields/properties and bounded nested
runtime data objects, with names converted to snake_case. Collections are
summarized by runtime type and count instead of expanded.

**Response (map):**
```json
{
  "name": "Maps/ExampleTownEast",
  "exists": true,
  "kind": "map",
  "runtime_type": "xTile.Map",
  "summary": {
    "width": 90,
    "height": 64,
    "layers": [{ "name": "Back", "width": 90, "height": 64 }],
    "tilesheets": [{ "id": "example_tilesheet", "image_source": "Maps/spring_example_tilesheet" }],
    "properties": {}
  }
}
```

**Response (data):**
```json
{
  "name": "Data/Locations",
  "exists": true,
  "kind": "data",
  "runtime_type": "System.Collections.Generic.Dictionary`2[...]",
  "summary": {
    "count": 482,
    "keys": ["ExampleTownEast"],
    "entries": {
      "ExampleTownEast": {
        "exists": true,
        "value": {
          "runtime_type": "StardewValley.GameData.Locations.LocationData",
          "display_name": "Town East",
          "can_plant_here": false,
          "create_on_load": {
            "runtime_type": "StardewValley.GameData.Locations.CreateLocationData",
            "always_active": false,
            "map_path": "Maps\\ExampleTownEast"
          }
        }
      }
    }
  }
}
```

**Response (missing):**
```json
{ "name": "Maps/Missing", "exists": false, "kind": "missing", "runtime_type": "", "summary": {} }
```

**Preconditions:** runs on the game thread. Some assets require the game content
helper to be initialized, which is true during normal scenario execution.
**Side effects:** read-only asset load through SMAPI/Stardew content APIs.
**Errors:**
- `InvalidParams -32602` — missing `name`, unsupported `asset_type`, or invalid
  `keys_limit`.
- `GameStateInvalid -32003` — harness content loader was not initialized.
**Tested in:** `tests/Protocol.Tests/ContentAssetSerializationTests.cs`,
`tests/Harness.Tests/ContentAssetProjectorTests.cs`,
`tests/Harness.Tests/ContentAssetHandlerTests.cs`, and
`tests/Runner.Tests/ScenarioRunnerContentAssetTests.cs`.

### fixture.save

Trigger SDV's save flow, writing the current game state to a folder in `Constants.SavesPath`. Drives `SaveGame.Save()` to completion on the game thread (blocks one update tick's worth of logic, typically <1 second). Used by the M2 fixture-builder CLI to capture reproducible save-state fixtures.

**Params:** `{name: string}` — destination folder name. SDV's `SaveGame.Save()` writes to `<farmName>_<uniqueID>` regardless of this value; the Runner uses `save_path` in the response to locate the actual output and rename on copy.

**Preconditions (strict):**
- `Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame` — world is loaded and playable.
- `!Game1.eventUp` — no cutscene active.
- `Game1.currentMinigame == null` — no minigame active.
- `!Game1.isWarping` — not mid-warp.

**Response:**

```json
{"ok": true, "tick": 8421, "save_path": "/home/user/.config/StardewValley/Saves/Tester_436515781"}
```

**Errors:** `GameStateInvalid (-32003)` for any precondition violation. `InvalidParams (-32602)` if `name` is missing or empty. `InternalError (-32603)` if the save coroutine exceeds its 30-second budget.

### game.return_to_title

Leave the currently loaded save and return to Stardew's title flow. This is a
generic lifecycle RPC used by runners that need to reload the same save inside a
scenario. It does not save by itself; compose it with `fixture.save`.

**Params:** none.

**Preconditions (strict):**
- `Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame` — world is loaded and playable.
- `!Game1.eventUp` — no cutscene active.
- `Game1.currentMinigame == null` — no minigame active.
- `!Game1.isWarping` — not mid-warp.

**Response:**

```json
{"ok": true, "tick": 8421}
```

**Errors:** `GameStateInvalid (-32003)` for any precondition violation.

Runner scenario authors should normally use the runner-level action below
instead of calling this RPC directly:

```json
{ "action": "fixture.save_reload", "args": { "settle_timeout_ms": 30000, "poll_ms": 100 } }
```

`fixture.save_reload` calls `fixture.save`, `game.return_to_title`, polls
`state.time.in_save == false`, calls `fixture.load`, then polls world readiness
before continuing to the next step. `args.name` defaults to the scenario
`fixture`; `args.load_name` can override the folder used for the final load.
By default the runner restores the pre-save Stardew save files at scenario
cleanup using Stardew's `_old` files, so save/reload tests do not pollute the
shared fixture for later runs. Set `"restore_original": false` only when the
scenario intentionally wants to leave the saved state on disk.

## Recording (via `sdv-test record`)

The `sdv-test record <name>` CLI subcommand (M2 subproject 4) subscribes to the harness's `JsonRpcSession.RequestReceived` event and captures incoming mutator calls as scenario steps.

**Filtered out (not captured):**
- `state.*` — read-only queries, no replay value.
- `scenario.begin`, `scenario.end` — the recorded scenario has its own lifecycle.

**Captured:** all other methods — `player.*`, `time.*`, `world.*`, `input.*`, `fixture.load`, `draw.*`, `freeze.*`.

On Ctrl-C (in an interactive terminal), the recorder writes `tests/samples/<name>.test.json` with `config.seed = 42` + recorded steps + empty `assertions` (user adds assertions post-hoc). Background-job SIGINT hits the same TTY/pipe quirk as watch mode — documented as a limitation.

### `world.interact_npc`

Trigger an interaction with an NPC by name. Mirrors what SDV does when the player presses
action while facing the NPC at conversation distance by calling `NPC.checkAction(player, location)`.
If that call does not open a renderable menu and the target NPC can talk, Frobby
refreshes the NPC's current dialogue and falls back to Stardew's dialogue opener
for that NPC. The NPC must be in the player's current location; otherwise returns
`GameStateInvalid`.

**Params:** `{name: string}` — NPC name (e.g. `"Pierre"`, `"Abigail"`).

**Response:** `MutatorOk { ok: true, tick: <int> }`.

**Errors:**
- `InvalidParams -32602` — `name` missing or empty.
- `GameStateInvalid -32003` — no scenario active / world not ready.
- `GameStateInvalid -32003` — NPC not in current location (warp first).

### `time.set`

Set in-game clock and/or date directly. All fields optional; at least one required.

**Params:**
```json
{
  "time": 1530,        // HHMM (600-2599); H<26, M<60
  "day": 5,            // 1-28
  "season": "spring",  // spring|summer|fall|winter (case-insensitive)
  "year": 1            // >=1
}
```

**Response:** `MutatorOk { ok: true, tick: <int> }`.

**Errors:**
- `InvalidParams -32602` — no fields provided / invalid time format / day out of range / unknown season / year < 1.
- `GameStateInvalid -32003` — no scenario active / world not ready.

### state.mods

Return loaded SMAPI mod metadata in load order. `unique_ids` is a compact list for
state assertions and fixture metadata; `mods` contains richer per-mod information.

**Params:** none.

**Response:**

```json
{
  "unique_ids": ["Pathoschild.ContentPatcher", "SdvTestFramework.Harness"],
  "mods": [
    {
      "unique_id": "Pathoschild.ContentPatcher",
      "name": "Content Patcher",
      "version": "2.7.0",
      "is_content_pack": false
    },
    {
      "unique_id": "Example.Mod.CP",
      "name": "Example Content Pack",
      "version": "1.0.0",
      "is_content_pack": true,
      "content_pack_for": "Pathoschild.ContentPatcher"
    }
  ]
}
```

_(Additional methods documented here as they are implemented. Template below.)_

## Template for new entries

```
### namespace.method

One-line description.

Request:
```json
{ "id": N, "method": "namespace.method", "params": { ... } }
```

Response (success):
```json
{ "id": N, "result": { ... } }
```

Response (error):
```json
{ "id": N, "error": { "code": -32000, "message": "...", "data": { ... } } }
```

**Preconditions:** <what must be true for this to succeed>
**Side effects:** <what this mutates, if anything>
**Tested in:** <path to test file>
```

## Error codes

Standard JSON-RPC codes (-32700 parse error, -32600 invalid request, etc.) plus custom:

- `-32001` — scenario not active
- `-32002` — fixture load failed
- `-32003` — SDV not in valid state (e.g., on title screen when expected in-game)
- `-32004` — determinism violation detected
- `-32005` — Harmony patch not applied (SDV version mismatch)
