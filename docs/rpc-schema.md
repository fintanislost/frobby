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

Returns the local farmer's current state.

```json
→ { "jsonrpc": "2.0", "id": 2, "method": "state.player" }
← { "jsonrpc": "2.0", "id": 2, "result": { "name": "Tester", "money": 1000, "stamina": 270, "max_stamina": 270, "health": 100, "location": "Farm", "tile": { "x": 64, "y": 15 } } }
```

**Preconditions:** world loaded (`Game1.gameMode == playingGameMode`). No request-time check yet; result fields will reflect title/loading-screen defaults if invoked too early.
**Side effects:** none.
**Implemented in:** `src/Harness/Handlers/StatePlayerHandler.cs`
**Tested in:** `tests/Runner.Tests/ProbeCommandTests.cs` (end-to-end runner → harness round-trip over a real Unix socket, with a faked harness response).

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
→ { "jsonrpc": "2.0", "id": 4, "method": "state.location", "params": { "name": "Farm" } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 4, "result": {
      "name": "Farm",
      "is_outdoors": true,
      "npcs": [{ "name": "Pierre", "tile": { "x": 4, "y": 17 } }],
      "objects": [{ "tile": { "x": 10, "y": 10 }, "name": "Weeds" }],
      "furniture": [{ "tile": { "x": 7, "y": 8 }, "id": "(F)1302", "name": "Oak Chair" }],
      "terrain": [{ "tile": { "x": 12, "y": 12 }, "kind": "HoeDirt" }]
   } }
```

If no location is loaded (e.g. on the title screen) or the requested name is unknown, the result contains an empty-string `name` with empty `npcs`/`objects`/`furniture`/`terrain` lists.

**Preconditions:** world loaded. Same note as `state.player`.
**Side effects:** none.
**Implemented in:** `src/Harness/Handlers/StateLocationHandler.cs`
**Tested in:** `tests/Protocol.Tests/LocationStateSerializationTests.cs` (DTO shape).

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
      "location": "Town",
      "tile": { "x": 4, "y": 23 },
      "friendship_points": 500,
      "hearts": 2,
      "gift_given_today": false,
      "portrait": "Abigail"
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

**Preconditions:** world loaded (`Game1.gameMode == playingGameMode`); the named NPC must exist in the loaded world.
**Side effects:** none.
**Implemented in:** `src/Harness/Handlers/StateNpcHandler.cs`
**Tested in:** `tests/Protocol.Tests/NpcStateSerializationTests.cs` (DTO shape).

### state.menu

Returns a snapshot of the currently active top-level menu (`Game1.activeClickableMenu`). When no menu is open, `present` is `false` and `type` is empty. `params` is not used.

Request:
```json
→ { "jsonrpc": "2.0", "id": 6, "method": "state.menu" }
```

Response (menu active):
```json
← { "jsonrpc": "2.0", "id": 6, "result": {
      "type": "ShopMenu",
      "present": true,
      "extra": { "currency": "0", "item_count": "42" }
   } }
```

Response (no menu active):
```json
← { "jsonrpc": "2.0", "id": 6, "result": { "type": "", "present": false, "extra": {} } }
```

`type` is the CLR class name of the menu (`ShopMenu`, `DialogueBox`, `GameMenu`, etc.). `extra` carries a small, menu-type-specific payload:

- `ShopMenu`: `currency` (int as string; 0 = gold, 1 = star tokens, 2 = Qi coins), `item_count` (count of `forSale`).
- `DialogueBox`: `character` (name of the speaker, or empty if narration-only).
- Other menu types currently emit `extra: {}`; extend per scenario need.

Nested menus (e.g. inventory inside a shop) are not exposed here — `Game1.activeClickableMenu` is the top-level only, per `.claude/rules/sdv-conventions.md`.

**Preconditions:** none beyond the harness being running. Safe on the title screen (returns `present:false`).
**Side effects:** none.
**Implemented in:** `src/Harness/Handlers/StateMenuHandler.cs`
**Tested in:** `tests/Protocol.Tests/MenuStateSerializationTests.cs` (DTO shape).

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

Advances the active scenario through a deterministic testing day transition and returns the new date. This is not a `time.set` clone, but it also does not run SDV's full sleep/save/end-of-night UI. The handler raises SMAPI `GameLoop.DayEnding`, advances the SDV calendar by exactly one day, sets the clock to 06:00, raises SMAPI `GameLoop.DayStarted`, and returns the post-transition snapshot.

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

**Preconditions:** an active scenario; world loaded; no active menu; no minigame; no event; not mid-warp.
**Side effects:** raises exactly one SMAPI `DayEnding`, advances date/time deterministically, then raises exactly one SMAPI `DayStarted`. It does not save, show sleep/end-of-night menus, run overnight farm simulation, or execute SDV's full sleep transition.
**Fallback seam:** production and unit tests use `DeterministicTimeNextDayTransition`, which applies the same 28-day season/year rollover and fires day-ending then day-started callbacks in order.
**Implemented in:** `src/Harness/Handlers/TimeNextDayHandler.cs`
**Tested in:** `tests/Protocol.Tests/TimeNextDayResultSerializationTests.cs` (DTO shape) + `tests/Harness.Tests/TimeNextDayHandlerTests.cs` (preconditions/projection/seam order).

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
→ { "jsonrpc": "2.0", "id": 12, "method": "world.place_furniture", "params": { "id": "(F)stonks_starberg_terminal_v1", "location": "FarmHouse", "x": 8, "y": 9, "remove_existing": true } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 12, "result": { "ok": true, "tick": 84200, "id": "(F)stonks_starberg_terminal_v1", "location": "FarmHouse", "tile": { "x": 8, "y": 9 } } }
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
          "color": [255, 176, 0, 255],
          "layer_depth": 0.91
        }
      ],
      "meta": { "ticks": 30, "events": 1, "dropped": 0 }
   } }
```

`x` and `y` are the integer projection of the `DrawString` position. `color` is `[r, g, b, a]`. `meta.dropped` counts writes that overflowed the text ring buffer.

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
- `case_sensitive` (bool) — defaults to `true`.
- `in_rect` (`[x, y, w, h]`) — captured text position must be inside the rect.
- `color` (`[r, g, b, a]`) — exact match.
- `layer_depth_range` (`[min, max]`) — inclusive on both ends.

**Implemented in:** `src/Harness/Handlers/DrawTextFindHandler.cs`
**Tested in:** `tests/Harness.Tests/TextDrawFilterTests.cs`.

### draw.assert_text_contains

Assertion primitive for captured text. Counts matches of a `TextDrawFilter` against the text buffer and returns `passed: (matched_count >= min_count)`. `min_count` defaults to `1`; `message` is echoed.

Request:
```json
→ { "jsonrpc": "2.0", "id": 20, "method": "draw.assert_text_contains", "params": {
      "filter": { "text_contains": "CASH & WIRES", "case_sensitive": true },
      "min_count": 1,
      "message": "Cash panel should be visible"
   } }
```

Response:
```json
← { "jsonrpc": "2.0", "id": 20, "result": { "passed": true, "matched_count": 2, "min_count": 1, "message": "Cash panel should be visible" } }
```

**Errors:** `InvalidParams (-32602)` if params are missing, `min_count < 1`, or the filter shape is invalid.

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
{"frozen": true, "tick": 8421}
```

## `bitmap.capture`

Capture the current backbuffer as a PNG. FREEZE-phase only.

**Preconditions:**
- `scenario.begin` has been called (active scenario required).
- `freeze.begin` has been called (`DeterminismController.Frozen == true`).

**Params:**
```json
{ "region": { "x": 0, "y": 0, "w": 640, "h": 480 } }
```
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

## Recording (via `sdv-test record`)

The `sdv-test record <name>` CLI subcommand (M2 subproject 4) subscribes to the harness's `JsonRpcSession.RequestReceived` event and captures incoming mutator calls as scenario steps.

**Filtered out (not captured):**
- `state.*` — read-only queries, no replay value.
- `scenario.begin`, `scenario.end` — the recorded scenario has its own lifecycle.

**Captured:** all other methods — `player.*`, `time.*`, `world.*`, `input.*`, `fixture.load`, `draw.*`, `freeze.*`.

On Ctrl-C (in an interactive terminal), the recorder writes `tests/samples/<name>.test.json` with `config.seed = 42` + recorded steps + empty `assertions` (user adds assertions post-hoc). Background-job SIGINT hits the same TTY/pipe quirk as watch mode — documented as a limitation.

### `world.interact_npc`

Trigger an interaction with an NPC by name. Mirrors what SDV does when the player presses
action while facing the NPC at conversation distance — calls `NPC.checkAction(player, location)`
directly. The NPC must be in the player's current location; otherwise returns
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

Return the list of loaded mod UniqueIDs in SMAPI load order. Used by the fixture builder to populate `.meta.json`'s `mods_installed` field.

**Params:** none.

**Response:**

```json
{"mods": ["Pathoschild.ContentPatcher", "SdvTestFramework.Harness"]}
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
