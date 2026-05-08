# SVE Slice 5 FTM Spawn World Content Design

## Purpose

Use Stardew Valley Expanded's Farm Type Manager content as a pressure test for neutral Frobby support around spawned world content. Slice 5 answers the question "can a test author set up a save state, wait for a mod's runtime spawn results, and assert the spawned objects or creatures in a location without hard-coding the spawning mod into Frobby?"

The first pass should favor deterministic world-content proof. SVE has stable large-object rules, such as fixed Grandpa's Shed exterior logs, which are a better foundation than starting with monsters. Monster coverage should layer on after the location-content state and wait primitives are solid.

## Goals

- Expand `state.location` with neutral runtime spawn/content collections that are useful for FTM and non-FTM mods.
- Expose resource clumps and large world objects such as logs, stumps, boulders, meteorites, and mine rocks.
- Expose monsters separately from social NPCs, with enough identifying and placement metadata to wait for and assert them.
- Add runner-side waiting for location content counts/types so scenarios do not rely on fixed sleeps.
- Add an SVE scenario proving deterministic FTM large-object spawns first.
- Add a second SVE scenario or follow-up path for monster spawns once the state primitive is stable.
- Keep every Frobby addition mod-agnostic. Frobby observes the Stardew runtime world; SVE supplies real test data.

## Non-Goals

- No Farm Type Manager API integration or FTM content parser in Frobby.
- No command that forces FTM to spawn content directly.
- No SVE-specific spawn rule, location, or item knowledge in Frobby code.
- No broad custom item/reward/shop assertions; Slice 6 owns custom item coverage.
- No visual sprite or animation validation for monsters; Slice 7 owns deeper visual-effect coverage.
- No first-pass requirement to prove every FTM category. Forage, ore, monster, and timed spawns can be added incrementally after the deterministic large-object path works.

## Current State

Frobby already has:

- `state.location` with location name, unique name, map size, warps, NPCs, placed objects, furniture, and terrain features.
- `state.locations` for runtime custom-location registration.
- `state.map_tile` and `state.tile_actions` for map and action introspection.
- State setup helpers: `time.set`, `time.advance`, `time.next_day`, `world.set_weather`, `player.add_mail`, `player.warp`, and fixture load/save.
- Runner-side `wait.location`, `wait.npc_location`, `wait.event_active`, and `wait.event_complete`.
- Parameterized `state.assert` for calling state RPCs with scenario-supplied params.

The gaps for FTM-style spawn testing are:

- Resource clumps are not represented in `state.location`.
- Monsters are mixed into `GameLocation.characters` at runtime but are not exposed as a separate creature collection.
- `state.location.objects` only gives a minimal name and tile, which is not enough for robust spawned-content assertions.
- The scenario DSL can assert `contains`, but it has no count or wait helper for "at least N logs appeared in this location".
- Conditional spawn setup can use existing date/weather/mail/event helpers, but we should not start by depending on the most complex conditional branch.

SVE is a good testbed because its FTM pack includes deterministic and probabilistic rules across large objects, ore, forage, and monsters. The most reliable first anchor found during research is:

- `Grandpas Shed Logs`
  - `MapName`: `Custom_GrandpasShedOutside`
  - `ObjectTypes`: `Log`
  - `MinimumSpawnsPerDay`: 2
  - `MaximumSpawnsPerDay`: 2
  - `IncludeCoordinates`: `21,17;21,17` and `23,17;23,17`
  - condition: `HasSeenEvent |contains=611439` is false

Highlands Cavern and Woods provide useful monster follow-up anchors, but they are more timing/randomness-sensitive and should be layered after the large-object proof.

## Architecture

Slice 5 extends the existing state-first architecture:

- `LocationStateProjector` adds new collections by projecting runtime `GameLocation` data.
- Protocol DTOs define neutral summaries, not FTM terms.
- `ScenarioRunner` adds `wait.location_content`, a runner-only polling action over `state.location`.
- Existing state setup actions remain the way tests influence spawn conditions.
- SVE scenarios assert real runtime outcomes, but Frobby remains unaware of SVE's FTM pack.

This matches earlier slices: the harness reports neutral game-thread state and the runner composes that state into test-friendly waits.

## Expanded `state.location`

The existing response remains compatible. Add fields only.

Example response:

```json
{
  "name": "Custom_GrandpasShedOutside",
  "unique_name": "Custom_GrandpasShedOutside",
  "resource_clumps": [
    {
      "tile": { "x": 21, "y": 17 },
      "kind": "ResourceClump",
      "id": "600",
      "name": "Log",
      "width": 2,
      "height": 2,
      "health": 10
    }
  ],
  "monsters": [
    {
      "tile": { "x": 44, "y": 31 },
      "name": "Green Slime",
      "type": "GreenSlime",
      "health": 50,
      "max_health": 50
    }
  ]
}
```

### Resource Clumps

Add `resource_clumps` as a list of `ResourceClumpSummary`.

Fields:

- `tile`: clump tile origin.
- `kind`: CLR type name, usually `ResourceClump`.
- `id`: best-effort parent-sheet or qualified identifier as a string.
- `name`: best-effort readable label. Known vanilla ids can map to names like `Log`, `Stump`, `Boulder`, `Meteorite`, or `Mine Rock`; unknown ids can fall back to the type/id.
- `width`: tile width when available.
- `height`: tile height when available.
- `health`: current health when available.

Projection should be defensive. If a field is inaccessible across Stardew versions, omit it or use a default rather than failing the whole location query.

### Monsters

Add `monsters` as a list of `MonsterSummary`.

Fields:

- `tile`: current tile.
- `name`: runtime name or display name when available.
- `type`: CLR type name such as `GreenSlime`, `Bat`, `ShadowBrute`, or a modded monster type.
- `health`: current health when available.
- `max_health`: max health when available.
- `damage`: damage when safely available.

Projection should use Stardew's monster base type where available. This keeps social NPCs in `npcs` and hostile creatures in `monsters`, even though both may live in the same runtime character collection.

### Objects

Keep the existing `objects` shape, but add optional non-breaking metadata if it is cheap and stable:

- `id`
- `qualified_id`
- `category`
- `stack`
- `quality`

This helps FTM forage/ore follow-ups, but the large-object first pass should not depend on this metadata.

### Critters

Critter support is a follow-up unless implementation shows a stable public collection. The design target is a future `critters` list with type/name/tile, but Slice 5 should not block deterministic FTM coverage on critter projection.

## Runner `wait.location_content`

Add a runner-only polling action:

```json
{
  "action": "wait.location_content",
  "args": {
    "location": "Custom_GrandpasShedOutside",
    "collection": "resource_clumps",
    "name": "Log",
    "min_count": 2,
    "timeout_ms": 10000,
    "poll_ms": 100
  }
}
```

Required args:

- `location`: location name passed to `state.location`.
- `collection`: one of `objects`, `resource_clumps`, `monsters`, or later `critters`.

Optional filters:

- `name`: exact match against an element's `name` string.
- `type`: exact match against an element's `type` string.
- `kind`: exact match against an element's `kind` string.
- `id`: exact match against an element's `id` string.
- `qualified_id`: exact match against an element's `qualified_id` string.
- `x` and `y`: exact match against an element's tile.

Count args:

- `min_count`: default 1.
- `max_count`: optional.
- `timeout_ms`: default 10000.
- `poll_ms`: default 100.

Behavior:

- Poll `state.location` with `{ "name": location }`.
- Filter the selected collection.
- Pass when the filtered count is at least `min_count` and at most `max_count` when supplied.
- On timeout, report the last observed filtered count and total collection count.

This avoids growing the minimal expression DSL into a general query language and gives failures useful context.

## SVE Scenario 07: Deterministic FTM Large Object Spawn

Add `tests/sdv/07-sve-ftm-spawn-world-content.test.json`.

Scenario shape:

1. Load the existing SVE fixture.
2. Set deterministic state:
   - spring year 1
   - time 600
   - sunny weather
   - no event flag for `611439`, relying on the current fixture unless implementation proves an explicit event-state mutator is required.
3. Advance to a fresh day or reload the fixture in the way live testing confirms triggers FTM's large-object spawn pass.
4. Warp to `Custom_GrandpasShedOutside`.
5. Wait for location settle.
6. Use `wait.location_content` for at least two `resource_clumps` named `Log`.
7. Assert the named location exposes those resource clumps through `state.location`.
8. Freeze and capture a final screenshot.

The first implementation should be allowed to adjust the date or use `time.next_day` if live behavior shows FTM spawns on day start rather than immediately after fixture load.

## Monster Follow-Up Layer

After resource clump coverage passes, add monster coverage using the same primitives:

- Warp to a known spawn location such as `Custom_HighlandsCavern` or `Woods`.
- Use existing state setup for date/time/weather and any necessary mail/event flags.
- Prefer a high-volume spawn rule with broad coordinates and player-present timing.
- Wait for `monsters` with `min_count >= 1`.
- Assert monster `type` or `name`, not an exact total, unless live runs prove the count is stable.

Monster coverage should prioritize low flake over breadth. If SVE monster rules are too random for an exact count, the scenario should assert presence/type and preserve richer exact-count tests for later deterministic controls.

## Error Handling

- `state.location` should not fail when resource clumps, monsters, or optional fields cannot be read. It should return empty collections or partial summaries.
- `wait.location_content` rejects blank `location`, unsupported `collection`, invalid counts, invalid timeouts, and invalid poll intervals with `InvalidOperationException`, following existing runner wait behavior.
- Timeout messages include the selected location, collection, filters, expected count range, last filtered count, and last total collection count.
- Unknown collection fields in filters should be rejected by the runner rather than silently ignored.

## Compatibility

- Existing `state.location` consumers keep working because fields are added only.
- Existing SVE scenarios and Starberg scenarios do not need changes.
- `npcs` remains social/non-monster NPC summaries. Monsters get a dedicated collection.
- Resource clump names are best-effort. Tests should prefer stable names for vanilla clump ids and type/id filters for unknown modded clumps.
- No dependency on Farm Type Manager internals means the same Frobby features can test vanilla resource clumps, custom dungeon mods, farm maps, and other spawn systems.

## Verification Plan

Frobby unit/contract tests:

- Protocol serialization test for `resource_clumps`, `monsters`, and any optional object metadata.
- Harness projector tests for defensive resource clump and monster projection where feasible.
- Runner tests for `wait.location_content`:
  - passes after a later poll reaches `min_count`
  - respects `max_count`
  - filters by name/type/kind/id/tile
  - timeout includes useful last-observed details
  - rejects unsupported collections and invalid counts
- Docs tests or schema validation as needed.

Live SVE verification:

- Run scenario 07 headless against the repo-local dependency cache.
- If scenario 07 is stable, run a small SVE smoke subset including scenarios 01, 02, 06, and 07.
- After monster follow-up is added, repeat scenario 07 and the monster scenario at least twice headlessly to catch timing flakes.

## Open Follow-Ups

- Event-seen mutation may become useful if SVE spawn gates need explicit `eventsSeen` control rather than fixture assumptions.
- Global flag/config mutation may become useful for SVE challenging/low-memory monster variants.
- Forage and ore exact assertions can build on richer object metadata once the large-object path is stable.
- Critter projection should be added only after confirming a stable Stardew runtime collection.
