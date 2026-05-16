# SVE Slice 17 Frontier Farm Fixtures Design

## Goal

Add neutral Frobby support for alternate farm-type save fixtures, then use Stardew Valley Expanded's Frontier Farm as the proof case for real modded farm map and shortcut coverage.

## Context

Slice 15 added repo profiles, inherited profile resolution, profile-specific mod caches, scenario profile selection, and config overlays. It proved the profile mechanism against Grandpa's Farm, but that test only validated loaded content and registered custom locations.

Slice 16 added progression-state tools and trigger-action support. Its follow-up called out Frontier Farm minecart, bridge, and desert shortcut coverage once farm-type fixtures exist.

Frontier Farm is a stronger testbed than Grandpa's Farm because many Content Patcher conditions depend on the active farm type:

- `FarmType: FrontierFarm`
- `Game1.whichFarm == Farm.mod_layout`
- `Game1.whichModFarm.Id == "FrontierFarm"`
- config-gated instant unlock options such as `InstantlyUnlockBridge` and `InstantlyUnlockDesertShortcut`

The current migrated fixture, `m0spike_436515781`, is a standard farm save. Loading the Frontier Farm content packs alone is not enough to prove runtime farm behavior, because the `Farm` location will still be evaluated as a standard farm unless the staged save is converted to a modded farm type before SDV loads it.

## Design

### Neutral Fixture Save Overrides

Frobby will support a generic fixture save override block for staged saves. The first override is alternate farm type metadata:

```json
{
  "saveOverrides": {
    "farmType": {
      "whichFarm": "mod",
      "modFarmId": "FrontierFarm"
    }
  }
}
```

This belongs in fixture/scenario staging, not in an SVE-specific helper. Frobby should treat `modFarmId` as an opaque string. The same primitive should later work for Immersive Farm 2 Remastered, other additional farms, and non-SVE mod projects.

The staged save XML mutation should happen after the base fixture is copied into SDV's save directory and before the harness asks SDV to load that fixture. The source fixture under `tests/fixtures/` must remain unchanged.

Expected XML behavior:

- set the save's vanilla farm type field to the modded farm layout value;
- populate the mod-farm id field SDV/FTM reads when evaluating farm-type conditions;
- fail loudly if the save XML is missing the expected root farm metadata fields, rather than silently producing an ambiguous fixture.

The implementation should keep this isolated behind a small save-mutator class so later fixture mutations, such as wallet state or progression bundles, do not get mixed into staging code.

### Scenario/Profile Usage

SVE will add a Frontier Farm profile:

```json
"sve-frontier-farm": {
  "inherits": "sve-core",
  "extraMods": [
    "Frontier Farm/[CP] Frontier Farm",
    "Frontier Farm/[FTM] Frontier Farm"
  ],
  "cacheNamespace": "sve-frontier-farm"
}
```

SVE will add an instant-unlock profile that inherits the Frontier profile and applies config overlays to the Frontier Farm Content Patcher pack:

```json
"sve-frontier-farm-instant-unlocks": {
  "inherits": "sve-frontier-farm",
  "configOverlays": [
    {
      "modId": "flashshifter.FrontierFarm",
      "values": {
        "InstantlyUnlockBridge": true,
        "InstantlyUnlockDesertShortcut": true
      }
    }
  ],
  "cacheNamespace": "sve-frontier-farm-instant-unlocks"
}
```

The exact config overlay shape should follow the existing Slice 15 implementation. If the repo config uses a different field name than shown above, the implementation should follow the current local schema and keep the semantic intent the same.

### SVE Proof Scenarios

Add at least two SVE scenarios:

1. `24-sve-frontier-farm-profile.test.json`
   - uses `profile: "sve-frontier-farm"`;
   - loads `m0spike_436515781` with a Frontier farm-type save override;
   - asserts `flashshifter.FrontierFarm` and `FlashShifter.FrontierFarmFTM` are loaded;
   - asserts `Data/AdditionalFarms` includes `FlashShifter.FrontierFarm/FrontierFarm`;
   - warps to `Farm` and asserts the runtime farm map resolves as Frontier Farm, not a standard farm.

2. `25-sve-frontier-farm-instant-unlocks.test.json`
   - uses `profile: "sve-frontier-farm-instant-unlocks"`;
   - loads the same base fixture with the same farm-type override;
   - proves the instant bridge and desert shortcut config changes affect runtime map state.

The shortcut proof should prefer durable runtime signals in this order:

1. explicit map properties or warps exposed by `state.location` / `content.asset`;
2. tile actions exposed by `state.map_tile` or `state.tile_actions`;
3. a deterministic `world.interact_tile_action` flow that proves the shortcut can be used.

If existing Frobby map introspection cannot expose the needed runtime signal, this slice should add the smallest neutral introspection improvement needed. It should not add hard-coded Frontier Farm coordinates or SVE-only helper logic to Frobby.

### Reports

The SVE scenarios should capture screenshots after loading/warping under freeze conditions, following the existing report conventions. Screenshots are useful for tester confidence, but assertions should remain state-first so map visual changes do not become brittle full-screen image checks.

## Testing Strategy

Use TDD for Frobby changes.

Unit tests:

- save XML farm-type override converts a standard farm save to a modded farm save;
- the override leaves unrelated save data unchanged;
- missing required save fields fail with a clear error;
- fixture/scenario config parsing accepts the new override block and rejects invalid farm override values.

Runner tests:

- staging applies the save override only to the staged copy;
- profile/scenario metadata carries the override through to fixture load;
- existing scenarios without overrides continue to stage fixtures unchanged.

SVE verification:

- run the new Frontier Farm scenarios headlessly;
- run a small SVE smoke subset that includes the existing core baseline and one previously passing profile scenario;
- if the slice touches shared fixture staging code, run the Frobby runner test suite that covers fixture staging and scenario loading.

## Non-Goals

- Do not implement full Frontier Farm questline automation for minecart, bridge, or desert shortcut special orders in this slice.
- Do not encode SVE-specific farm ids, map coordinates, or config keys inside Frobby.
- Do not mutate tracked fixture save files in place.
- Do not require a user's personal SDV save or game Mods folder to contain Frontier Farm.

## Risks And Mitigations

Risk: SDV's mod-farm save XML fields may differ slightly from the initial expectation.

Mitigation: keep the mutator covered by unit tests built from a minimal representative save fragment, then validate against a live headless SVE run before committing implementation.

Risk: Content Patcher conditions may not refresh after config overlays unless the profile-specific mod cache is rebuilt.

Mitigation: use the existing profile cache namespace separation and verify the loaded config through runtime behavior, not just copied file content.

Risk: Shortcut map changes may be difficult to assert without brittle coordinates.

Mitigation: inspect runtime map properties and tile action summaries first. Add a neutral map/warp projection only if existing tools cannot expose the change cleanly.

## Completion Criteria

- Frobby can stage a fixture as an alternate modded farm type without changing the source fixture.
- SVE has Frontier Farm profile coverage and an instant-unlock runtime shortcut proof.
- All Frobby additions are documented as generic mod testing capabilities.
- The SVE capability TODO records Slice 17 as done after implementation and verification.
