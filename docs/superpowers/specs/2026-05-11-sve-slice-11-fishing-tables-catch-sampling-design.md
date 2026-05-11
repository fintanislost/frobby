# SVE Slice 11: Fishing Tables And Deterministic Catch Sampling Design

## Context

Slice 10 added neutral special-order and drop-box coverage. The next Stardew
Valley Expanded pressure point is fishing: SVE adds custom fish data, patched map
fish tables, `NoFishing` tiles, a Desert fishing Harmony patch, and optional farm
packs with modern `Data/Locations` fish areas.

This slice should cover both core SVE and Frontier Farm while keeping Frobby
mod-agnostic. Core SVE proves the common Stardew surfaces: `Data/Fish`, map `Fish`
properties, blocked fishing tiles, and patched `GameLocation.getFish` behavior.
Frontier Farm proves richer 1.6-style `Data/Locations` `FishAreas` and `Fish`
entries with modded item IDs.

## Goals

- Add neutral runtime fishing context introspection for a location/tile/time/weather
  setup.
- Add neutral fishing table introspection that summarizes candidate catch data from
  live Stardew content/runtime state.
- Add deterministic catch sampling that calls the live Stardew fishing path without
  requiring the full fishing minigame.
- Add one core SVE proof scenario for custom fish data, map fish metadata, blocked
  fishing tiles, and Desert special reward behavior.
- Add one Frontier Farm proof scenario for custom fish areas and modded fish
  candidates on an alternate farm pack.

## Non-Goals

- Do not implement a full fishing minigame driver or catch-bar automation.
- Do not parse SVE or Frontier Farm Content Patcher files inside Frobby as the
  source of truth.
- Do not hardcode SVE locations, fish IDs, Desert reward tiles, or farm-pack details
  in Frobby production code.
- Do not make Frontier Farm a required dependency for core SVE smoke tests. It should
  be staged only for its own scenario.
- Do not try to exhaustively model every Stardew fish-condition rule if the live game
  can provide a deterministic answer.

## Approaches Considered

### Context, Table, And Sampling

Add three neutral surfaces: `state.fishing_context`, `state.fishing_table`, and
`fishing.sample_catch`. The context RPC explains whether a tile is fishable and why;
the table RPC shows candidate catches for the same context; the sampling RPC calls
the live Stardew catch path under controlled state. This is the recommended approach
because failures become diagnosable: a test can tell whether the problem is the tile,
the data table, the season/time context, or the final catch roll.

### Core SVE First, Frontier Later

This is smaller, but it delays the strongest fish-area pressure test. Core SVE has
useful fishing content, but Frontier Farm is the clearer proof for custom
`FishAreas`, area IDs, and modded fish candidates.

### Sampling Only

Only add `fishing.sample_catch` and assert the returned item. This would be quick,
but brittle. If a catch does not match expectations, the report would not explain
whether the requested tile was blocked, whether the table contained the candidate,
or whether RNG selected another valid result.

## Recommended Design

### Fishing Context State

Add a harness RPC, `state.fishing_context`, that runs on the game thread and reports
the fishing-relevant state for a requested location and bobber tile.

Request shape:

- `location`: optional location name, defaulting to the current location.
- `x` and `y`: optional bobber tile, defaulting to the player's current tile.
- `season`, `time_of_day`, `weather`, and `luck`: optional context overrides where
  Frobby can apply them safely before projection.
- `include_tile_layers`: optional boolean, defaulting to true.

Response shape:

- `location`, `location_name`, and `location_type`.
- `tile`: bobber tile.
- `season`, `time_of_day`, `weather`, and `daily_luck` as observed for the query.
- `is_water`, `is_fishable`, and `blocked_reason`.
- `fish_area_id` when the live location data can resolve one.
- `map_fish`: raw map `Fish` property, if present.
- `has_no_fishing`: true when the tile or relevant map data exposes `NoFishing`.
- `tile_properties`: selected Back/Buildings/Front tile properties for debugging.
- `location_fish_areas`: known fish areas for the location, when available through
  `Data/Locations` or runtime fields.

The RPC should be best-effort. Missing optional metadata should return empty/null
fields, not fail the request. It should fail only for invalid coordinates, missing
locations, or no loaded world.

### Fishing Table State

Add a harness RPC, `state.fishing_table`, for the effective candidate catches in a
location/context.

Request shape:

- Same location/tile/time/weather fields as `state.fishing_context`.
- `include_raw`: optional boolean for raw backing data snippets.
- `limit`: optional positive integer to bound response size.

Response shape:

- `context`: the same normalized context summary returned by
  `state.fishing_context`.
- `candidates`: ordered list of catch candidates.
- `raw_sources`: optional compact source summary, such as `Data/Fish`,
  `Data/Locations`, map `Fish`, or runtime method.

Each candidate should include:

- `id`, `item_id`, `qualified_id`, and `display_name` where available.
- `type`: fish, object, furniture, trash, unknown, or another neutral category.
- `fish_area_id`, `chance`, `condition`, `season`, `time_range`, and `weather`
  fields where Stardew exposes them.
- `source`: `data_fish`, `data_locations`, `map_fish`, `runtime`, or `unknown`.
- `raw`: optional bounded string/object for diagnostics when `include_raw` is true.

For Stardew 1.6 `Data/Locations` entries, Frobby should project `FishAreas` and
`Fish` entries directly from the live content pipeline. For legacy map `Fish`
properties, Frobby should parse the compact `id chance` pairs into candidates when
possible and preserve the original string for diagnostics. The implementation may
also call native Stardew helper methods if they provide a more accurate candidate
set, but parsing live runtime data is acceptable for table introspection.

### Deterministic Catch Sampling

Add a harness RPC, `fishing.sample_catch`, that asks the live game what would be
caught for a bobber tile under controlled state.

Request shape:

- `location`, `x`, `y`, `season`, `time_of_day`, `weather`, and `luck`, matching the
  context request.
- `attempts`: positive integer, defaulting to 1.
- `seed`: optional integer seed for deterministic sampling.
- `player_fishing_level`: optional override.
- `rod_id`, `bait_id`, and `tackle_id`: optional equipment selectors.
- `restore_state`: optional boolean, defaulting to true.

Response shape:

- `context`: normalized fishing context.
- `attempts`: number of attempts performed.
- `results`: ordered catch result summaries.
- `state_restored`: whether Frobby restored temporary state after sampling.

Each result should include:

- `attempt`, `item_id`, `qualified_id`, `display_name`, `type`, `stack`, `quality`,
  `category`, and `runtime_type`.
- `is_null`: true when Stardew returned no catch.
- `source`: runtime catch path.
- `raw_id` or `parent_sheet_index` where available for compatibility.

The preferred implementation is to call `GameLocation.getFish` or the current
Stardew equivalent on the live location after applying deterministic state. This is
important for SVE's Desert Harmony patch, because a raw data parser would miss
runtime postfix behavior. The sampler should snapshot and restore player location,
time, weather, daily luck, RNG state where feasible, fishing level, and equipment
when `restore_state` is true. If RNG restoration is not safely possible, the response
must say so and tests should use isolated fixtures.

### Runner And Scenario Support

Add JSON assertion support for:

- `state.fishing_context`.
- `state.fishing_table`.
- `fishing.sample_catch`.

Existing expression assertions should be able to inspect fields such as:

- `is_fishable == true`.
- `has_no_fishing == true`.
- `candidates.any(c => c.qualified_id == "(O)FlashShifter...")`.
- `results.any(r => r.display_name == "Pyramid Decal")`.

Add runner-side waits only if implementation proves catch/path initialization needs
polling. The first design target is state/action assertions; fixed sleeps should not
be needed beyond normal fixture load/warp waits already available.

### Core SVE Proof Scenario

Add `tests/sdv/16-sve-fishing-core.test.json` in the SVE repo.

Proof flow:

1. Load the existing SVE fixture with core SVE only.
2. Assert `content.asset` for `Data/Fish` contains known SVE custom fish entries.
3. Query `state.fishing_table` for a stable core location such as Beach or Mountain
   and assert the patched map `Fish` table is visible.
4. Query `state.fishing_context` for a known `NoFishing` tile on the SVE Mountain
   map and assert the tile is blocked with `has_no_fishing`.
5. Query `fishing.sample_catch` in the Desert patch area with controlled attempts and
   assert that the sample path can expose the SVE-added special reward when the
   deterministic seed/attempt count reaches it.

If the Desert special reward remains too RNG-heavy for a short live scenario, keep
the Desert assertion as a focused table/context assertion and add a targeted harness
unit test with a deterministic fake/random abstraction for the sampler. The TODO
should note the live Desert reward proof as a follow-up only if it cannot be made
stable without long waits.

### Frontier Farm Proof Scenario

Add `tests/sdv/17-sve-frontier-farm-fishing.test.json` in the SVE repo.

Proof flow:

1. Stage the Frontier Farm pack through the repo-local Frobby test scaffold for this
   scenario only.
2. Load the existing fixture or a small Frontier-compatible fixture.
3. Query `state.fishing_context` for `Custom_FerngillRepublicFrontier` ocean and
   river tiles.
4. Assert the correct `FishAreas` are visible and the selected tile resolves to the
   expected area ID.
5. Query `state.fishing_table` for each area and assert expected vanilla and SVE
   modded fish candidates are present, including a custom qualified item ID such as
   the SVE Starfish entry.
6. Run a small deterministic `fishing.sample_catch` attempt set on one stable tile
   and assert the result is a valid candidate from the projected table.

The scenario should not require changing the user's real game install. Frontier Farm
must be staged into the Frobby-managed mods directory/cache like other test
dependencies.

## Testing Strategy

Use TDD for every Frobby behavior.

Frobby protocol tests:

- Fishing context, table, candidate, and sample result models serialize in
  snake_case.
- Request models handle omitted optional fields and reject invalid count/limit values
  at the handler layer.

Frobby harness tests:

- `state.fishing_context` reports map `Fish`, `NoFishing`, water/fishable status, and
  fish-area metadata from fake abstractions.
- `state.fishing_table` projects Stardew 1.6 `Data/Locations` fish areas/fish entries
  and legacy map `Fish` entries.
- `fishing.sample_catch` validates invalid locations/tiles, bounded attempts, state
  restore behavior, and result item projection.
- Sampling calls the runtime catch abstraction rather than only reading content data.

Frobby runner/schema tests:

- JSON scenario schema accepts the three new assertion/action types.
- Scenario runner can evaluate meaningful expressions against context, table, and
  sample responses.
- Failure output includes the selected location/tile/context and observed candidates
  or catch results.

SVE verification:

- Run the new core SVE scenario headlessly.
- Run the new Frontier Farm scenario headlessly with its own staged mod set.
- Re-run nearby SVE scenarios that exercise runtime content assets, map tile state,
  location registration, and item identity.
- Keep reports under the existing
  `/tmp/stardew-valley-expanded-frobby-results-0.1.0/` grouping.

## Risks And Mitigations

- **Fishing internals vary across Stardew versions.** Keep the public Frobby contract
  neutral and isolate Stardew reflection/native calls behind small harness services.
- **Desert special reward is probabilistic.** Use seeded attempts and bounded attempt
  counts; if live RNG cannot be restored safely, rely on isolated fixtures and report
  the sampling seed/attempt trail.
- **Frontier Farm adds optional dependency complexity.** Stage it only for scenario
  17 through the repo-local dependency cache and keep core SVE scenarios independent.
- **Table projection can drift from live catch behavior.** Treat `state.fishing_table`
  as diagnostics/candidate visibility, and treat `fishing.sample_catch` as the
  authoritative runtime proof.
- **NoFishing and fish-area resolution may be tile-sensitive.** Choose coordinates
  during implementation by probing live maps and record them in scenario assertions
  rather than Frobby code.

## Acceptance Criteria

- Frobby production code contains no SVE-specific or Frontier-Farm-specific branches.
- `state.fishing_context` explains fishable/blocked tile state for live Stardew
  locations.
- `state.fishing_table` exposes both legacy map `Fish` candidates and modern
  `Data/Locations` fish-area candidates.
- `fishing.sample_catch` can call the live Stardew catch path deterministically enough
  for bounded tests and returns projected item results.
- SVE has one core fishing scenario and one Frontier Farm fishing scenario.
- Frobby docs, schema, and SVE capability TODO are updated during implementation.
- Targeted Frobby tests and the new SVE headless scenarios pass before the TODO item
  is marked complete.
