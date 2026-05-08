# SVE Monster Spawn Coverage Design

## Goal

Add deterministic monster-spawn coverage using Stardew Valley Expanded as the proof mod, while keeping all Frobby changes neutral and useful for any mod that creates hostile monsters.

The scenario should prove more than "a monster exists." It should verify that a real modded spawn rule produced the expected runtime monster configuration: monster identity, tile/content presence, HP, damage, and sprite texture.

## Context

Slice 5 added the neutral world-content foundation:

- `state.location.resource_clumps`
- `state.location.monsters`
- runner-side `wait.location_content`

That coverage currently proves deterministic SVE FTM large-object spawns at Grandpa's Shed exterior. The remaining Slice 5 follow-up is a deterministic monster-spawn scenario.

SVE's Farm Type Manager pack has several monster spawn areas. A good low-flake target is `Custom_CrimsonBadlands`, where the FTM content defines ungated `Mummy` spawns with custom HP, damage, and sprite settings. The clearest anchor found during research is:

- `UniqueAreaID`: `Crimson Badlands Corrupt Mummy Guard`
- `MapName`: `Custom_CrimsonBadlands`
- `IncludeCoordinates`: `20,144;20,144`
- `MonsterName`: `Mummy`
- `HP`: `2000`
- `Damage`: `100`
- `Sprite`: `Characters/Monsters/CorruptMummy`
- no season/day/weather/game-state query conditions

## Design

Use the existing state-first pattern. Frobby should observe the runtime monster object and runner waits should filter by stable runtime fields. Frobby should not parse or understand Farm Type Manager content packs.

### Frobby Monster Projection

Extend `MonsterSummary` additively:

- keep `tile`
- keep `name`
- keep `type`
- keep `health`
- keep `max_health`
- keep `damage`
- add `sprite_texture`

`sprite_texture` should be read defensively from the monster's runtime sprite/animated sprite data. It may be `null` when Stardew or a mod does not expose an asset name. This is acceptable and keeps the protocol honest. The SVE scenario can require the field only because the chosen FTM rule declares a sprite path that should be present at runtime.

### Runner Filtering

Extend `wait.location_content` with exact-match filters:

- `health`
- `max_health`
- `damage`
- `sprite_texture`

These filters apply to any collection element that exposes those fields. They are neutral and should not be limited to monsters in the implementation, though monster coverage is the immediate use case.

Timeout diagnostics should include these filters in the existing "matching ..." suffix so a failed live run explains which configured field was missing.

### SVE Scenario

Add `tests/sdv/10-sve-ftm-monster-spawn-config.test.json` in the SVE repo.

The scenario should:

1. Set deterministic time/date/weather.
2. Advance to a fresh day so FTM day-start spawns can populate.
3. Warp to `Custom_CrimsonBadlands` near the selected spawn area.
4. Wait for `state.location.monsters` to include the expected custom corrupt mummy:
   - `name`: `Mummy`
   - `type`: `Mummy`
   - `health`: `2000`
   - `damage`: `100`
   - `sprite_texture`: `Characters/Monsters/CorruptMummy`
   - optionally exact `x: 20`, `y: 144` if live probing confirms FTM preserves the configured tile exactly.
5. Treat the `wait.location_content` match as the authoritative state assertion. Add a labeled scenario assertion only if the existing state assertion DSL can express the same check without weakening the numeric filters.
6. Freeze and capture a final screenshot for the HTML report.

If exact tile matching flakes because FTM chooses a nearby passable tile or Stardew movement changes the monster's tile before the wait observes it, the scenario should keep the config filters and drop the exact tile filter. The screenshot still gives visual evidence; the state filters are the real assertion.

## Testing

Use TDD for Frobby changes:

- Protocol serialization test for `MonsterSummary.SpriteTexture`.
- Harness projector test for reading a monster sprite texture from runtime-like objects.
- Runner unit tests for numeric filters in `wait.location_content`.
- Runner unit tests for `sprite_texture` filtering and timeout text.
- Existing Frobby suite after implementation.
- Focused SVE scenario run headlessly.
- Existing SVE smoke run headlessly enough to ensure no scaffold regression.

## Documentation

Update Frobby docs where `state.location.monsters` and `wait.location_content` are described:

- mention `sprite_texture` in monster summaries
- mention `health`, `max_health`, `damage`, and `sprite_texture` filters
- keep examples generic, not SVE-specific

Update `SVE_FROBBY_CAPABILITY_TODO.md`:

- mark this follow-up active during implementation
- mark it done only after the Frobby docs, unit tests, and SVE scenario all pass

## Non-Goals

- Do not parse Farm Type Manager content packs in Frobby.
- Do not add SVE-specific RPC methods, field names, or assumptions.
- Do not require draw-call texture matching for this slice. Draw assertions may be added later if runtime sprite metadata is not enough, but this slice should start from stable state observation.
- Do not merge the SVE feature branch to `master`.

## Risks

`sprite_texture` may be exposed differently across monster classes. The projector should read common runtime paths defensively and return `null` when unavailable.

FTM may place or move a monster before the runner observes the exact tile. Exact tile matching is useful if stable, but the required proof is the custom config tuple: name/type plus HP/damage/sprite texture in the target location.

Crimson Badlands has many hostile spawns. The filters should target the custom corrupt mummy tightly enough that unrelated mummies do not satisfy the scenario.
