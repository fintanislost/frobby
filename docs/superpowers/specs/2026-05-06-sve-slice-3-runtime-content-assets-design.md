# SVE Slice 3 Runtime Content Assets Design

## Purpose

Use Stardew Valley Expanded as a Content Patcher-heavy testbed to add neutral Frobby support for inspecting final runtime content assets. Slice 3 answers the question "what did Stardew actually load after Content Patcher, config, locale, and game-state conditions applied?" without parsing Content Patcher packs as the source of truth.

## Goals

- Add a read-only `content.asset` RPC that loads one named asset through the live game content pipeline.
- Return bounded metadata for maps, textures, dictionaries/data assets, strings, and unknown asset types.
- Let scenario authors assert runtime asset facts without relying only on screenshots.
- Add a runner `content.asset` assertion type so JSON scenarios can validate loaded content directly.
- Add one SVE scenario that proves the asset query against real CP `Load`, `EditData`, and texture targets.
- Keep the implementation mod-agnostic; SVE only supplies examples and pressure.

## Non-Goals

- No Content Patcher manifest parser in this slice.
- No attempt to report which CP patch or pack produced an observed runtime result.
- No broad asset graph scan of every loaded asset.
- No full serialization of large data dictionaries, maps, or textures.
- No pixel comparison for textures; texture validation stays limited to dimensions and optional content hash.
- No mutation of Content Patcher config, save state, or asset cache.

## Current State

Frobby already has `state.mods` for loaded mod metadata, `state.locations` / `state.location` / `state.map_tile` for runtime location and map state, and `draw.*` texture filters with best-effort texture asset resolution. That works well once a texture is rendered, but it does not let a test ask whether a named asset exists, whether a CP-loaded map has the expected layers, whether a CP-edited data dictionary contains a key, or whether a mod-owned texture can be loaded before it appears on screen.

SVE is a strong testbed because it includes many CP surfaces: custom map `Load` targets, `EditMap`, `EditData` into vanilla and mod-owned data assets, `EditImage`, seasonal and config-gated `When` conditions, and many `Mods/FlashShifter...` private asset targets.

## Architecture

Slice 3 adds content inspection as a harness-side read-only query and keeps assertion convenience in the runner.

- `ContentAssetHandler` handles `content.asset` on the game thread.
- `ContentAssetProjector` loads the requested asset using Stardew/SMAPI runtime content APIs and converts it into a bounded DTO.
- Protocol models describe the request and response without referencing SVE or Content Patcher internals.
- `ScenarioRunner` adds a `content.asset` assertion type. It invokes `content.asset`, then evaluates the existing small expression DSL against the returned `asset` root.
- Docs and schema files document the RPC, assertion type, and examples.

The core principle is runtime truth: if Content Patcher changes an asset, `content.asset` sees the patched runtime asset. If a `When` condition prevents a patch, `content.asset` reports the actual unpatched or missing state.

## `content.asset` Request

```json
{
  "name": "Maps/Custom_TownEast",
  "asset_type": "map",
  "include_keys": true,
  "keys_limit": 50,
  "entry_keys": ["Custom_TownEast"],
  "hash_texture": true
}
```

Fields:

- `name`: required asset name using Stardew/SMAPI asset naming, such as `Maps/Custom_TownEast`, `Data/Locations`, or `Mods/FlashShifter.StardewValleyExpandedCP/spring_GrampletonFields`.
- `asset_type`: optional hint. Supported values are `map`, `texture`, `data`, `string`, and `unknown`. If omitted, the handler tries known types in a conservative order.
- `include_keys`: optional boolean, default false. For dictionary-like data assets, include up to `keys_limit` keys.
- `keys_limit`: optional integer, default 50, minimum 1, maximum 500.
- `entry_keys`: optional string array. For dictionary-like assets, include summaries for these specific entries.
- `hash_texture`: optional boolean, default false. For textures, include a bounded content hash using the existing texture hashing helper.

## `content.asset` Response

All responses share this envelope:

```json
{
  "name": "Maps/Custom_TownEast",
  "exists": true,
  "kind": "map",
  "runtime_type": "xTile.Map",
  "summary": {
    "width": 120,
    "height": 80,
    "layers": [
      { "name": "Back", "width": 120, "height": 80 },
      { "name": "Buildings", "width": 120, "height": 80 }
    ],
    "tilesheets": [
      { "id": "outdoors", "image_source": "Maps/spring_outdoorsTileSheet" }
    ],
    "properties": {
      "Outdoors": "T"
    }
  }
}
```

Missing assets return a non-error result:

```json
{
  "name": "Maps/Missing_Test_Asset",
  "exists": false,
  "kind": "missing",
  "runtime_type": "",
  "summary": {}
}
```

Invalid requests, unsupported options, or unsafe limits return JSON-RPC `InvalidParams`.

## Supported Summaries

### Map

Map summaries include:

- `width` and `height`
- `layers`: each layer's `name`, `width`, and `height`
- `tilesheets`: each tilesheet's `id`, `image_source`, tile dimensions, and sheet dimensions when available
- `properties`: map-level properties converted to strings

This intentionally does not duplicate `state.map_tile`; tile-level inspection remains in Slice 1's map tools.

### Texture

Texture summaries include:

- `width`
- `height`
- `content_hash` when `hash_texture` is true and hashing succeeds

If hashing fails because the texture is GPU-backed, disposed, or otherwise unreadable, the asset still exists and returns dimensions with no hash.

### Data

Dictionary-like data summaries include:

- `count`
- `keys` when `include_keys` is true
- `entries` for requested `entry_keys`

Entry summaries are bounded:

- primitive strings, numbers, and booleans are returned directly
- objects are summarized as `runtime_type`, selected scalar properties, and a compact JSON preview when safe
- large collections return counts instead of full contents

The first implementation should target common Stardew data shapes such as `Dictionary<string, string>`, `Dictionary<string, object>`, and typed dictionaries used by SDV 1.6 data assets.

### String

String summaries include:

- `text`
- `length`

This supports localized string assets and simple mod-owned text assets.

### Unknown

Unknown summaries include:

- `runtime_type`
- `string_preview` when `ToString()` is useful and short

Unknown support keeps the RPC useful when SDV or a mod returns a type Frobby has not specialized yet.

## Runner Assertion

Add a `content.asset` assertion type:

```json
{
  "type": "content.asset",
  "asset": "Maps/Custom_TownEast",
  "asset_type": "map",
  "expr": "asset.layers contains name 'Back'",
  "message": "SVE Town East map should load with a Back layer"
}
```

Assertion fields:

- `asset`: required asset name.
- `asset_type`: optional asset type hint passed to `content.asset`.
- `expr`: required expression evaluated against the response's `summary` as the `asset` root.
- `include_keys`, `keys_limit`, `entry_keys`, and `hash_texture`: optional passthrough fields for the RPC request.

Supported expression forms mirror the current state DSL:

- `asset.width != 0`
- `asset.height == 80`
- `asset.layers contains name 'Back'`
- `asset.keys contains 'Custom_TownEast'`
- `asset.entries.Custom_TownEast.exists == true`

If the asset is missing, the assertion fails with a message that includes the asset name and requested type.

## SVE Scenario Shape

Add `04-sve-content-assets-runtime.test.json` after the event observability scenario. It should:

1. Load the existing SVE fixture.
2. Wait briefly for content and save state to settle.
3. Assert a loaded custom map asset, such as `Maps/Custom_TownEast`, has nonzero dimensions and a `Back` layer.
4. Assert `Data/Locations` contains the matching custom location key.
5. Assert a mod-owned texture asset loads with nonzero dimensions. A stable candidate is a `Mods/FlashShifter.StardewValleyExpandedCP/...` texture target from the SVE CP pack.
6. Capture a final frozen screenshot only as report context, not as the source of truth for asset assertions.

During implementation, exact SVE asset names should be selected from loaded runtime behavior, not from assumptions in this design document. If one candidate is config-gated or unavailable in the core-only testbed, choose another SVE CP asset that is active in the default fixture.

## Error Handling

- Missing assets return `{ "exists": false }` so scenarios can assert absence if needed.
- Invalid asset names, invalid type hints, negative limits, and excessive limits return `InvalidParams`.
- Type mismatch between `asset_type` and actual loaded asset returns `{ "exists": false, "kind": "missing" }` only if that typed load fails cleanly. If a different type loads successfully through auto-detection, the response reports the actual `kind`.
- Projection failures for one optional field omit that field and keep the rest of the summary.
- The handler does not invalidate or reload caches. It observes current runtime state only.

## Compatibility

- Existing RPCs and scenario assertions keep their current behavior.
- `content.asset` is read-only and runs on the game thread like other state queries.
- `draw.*` texture resolution remains unchanged.
- Starberg scenarios should not need changes.
- Any mod can use this surface; SVE-specific asset names appear only in SVE scenario files and docs examples.

## Testing

Frobby unit tests:

- Request validation for missing names, bad limits, and unsupported type hints.
- Projection tests for map summaries using small fake or constructed xTile maps where feasible.
- Projection tests for dictionary/data summaries, key limits, selected entry summaries, and missing entry summaries.
- Projection tests for texture summaries with hash disabled and hash enabled when a texture test double is available.
- Runner tests for `content.asset` assertions passing and failing against mocked RPC responses.

Live verification:

- Run Frobby `dotnet test`.
- Run a small Starberg smoke scenario to prove new RPC registration did not regress existing mod UI tests.
- Run SVE scenarios 01-03 to prove prior SVE slices still pass.
- Run the new SVE scenario 04 headless.

## Future Work

After this runtime foundation is proven, later Slice 3 work can add:

- Content Patcher manifest diagnostics for declared patch intent and `When` condition visibility.
- Asset invalidation/reload observations for hot-reload style workflows.
- More specialized summaries for common Stardew typed data assets.
- Image-region or palette summaries for texture assets where dimensions and hash are too coarse.
- Scenario helpers for comparing two runtime assets or asserting seasonal asset variation.
