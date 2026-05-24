# SVE Slice 23 Input Tile Click Design

## Summary

Slice 23 adds a neutral world-click testing path to Frobby so mod scenarios can
click gameplay tiles through Stardew's normal player input behavior instead of
using semantic world mutators. The first proof is an SVE Combat Lab scenario
that selects a vanilla bomb from inventory, clicks a target tile, waits for the
vanilla fuse sprite, and verifies corrupt-mummy cleanup without calling
`world.place_inventory_object` or `world.explode_tile`.

This follows Slice 22. Slice 22 proved the semantic inventory-object placement
primitive. Slice 23 should prove the more player-real path where the selected
hotbar item and tile click drive the game behavior.

## Current State

Frobby already has:

- `input.click`, `input.hover`, and text/menu click helpers for UI/menu work.
- `world.place_inventory_object`, which selects an inventory object and invokes
  Stardew's native object placement path directly.
- `state.player.items`, which exposes inventory slots and item metadata.
- Combat Lab scenarios 27-30 in SVE for isolated monster, relocation, explosion,
  and player-like inventory-object placement coverage.

Important observations from Slice 22 and source research:

- Current `input.click` requires an active menu, so it cannot click the gameplay
  world.
- Stardew's normal `Game1.pressUseToolButton()` path uses the mouse/world
  position, the selected `Farmer.ActiveObject` or `CurrentTool`, placement
  validation, and location hooks.
- Vanilla bomb `(O)287` placement does not appear in `GameLocation.objects`; it
  broadcasts temporary sprites, including the fuse sprite from
  `LooseSprites/Cursors` source rect `[598, 1279, 3, 4]`.
- Tests should not rely on screen coordinates where tile coordinates are the
  real intent. Viewport, zoom, UI scale, and headless resolution can all make raw
  screen coordinates brittle.

## Goals

1. Add a neutral input primitive for clicking a gameplay tile.
2. Add a neutral player inventory selection primitive so scenarios can choose a
   hotbar/inventory item without hard-coding key timing.
3. Keep the new Frobby surface usable by any mod test suite, not just SVE.
4. Prove the feature with an SVE scenario that uses a real selected bomb and a
   tile click instead of direct placement or direct explosion.
5. Update docs so users understand when to use semantic placement, tile-click
   placement, object waits, and visual-effect waits.

## Non-Goals

- Do not replace `world.place_inventory_object`; it remains the deterministic
  semantic placement tool.
- Do not add a bomb-specific RPC.
- Do not add SVE IDs, SVE locations, or SVE sprite assumptions to Frobby source.
- Do not simulate OS-level mouse movement. This should run headless and avoid
  taking the user's cursor.
- Do not implement drag, hold, scroll, or multi-click gestures in this slice.
- Do not broaden raw `input.click` semantics in a way that changes existing menu
  behavior.

## Proposed Frobby Surface

### `player.select_item`

Select an existing inventory item by slot or item id.

Example:

```json
{
  "method": "player.select_item",
  "params": {
    "id": "(O)287"
  }
}
```

Request fields:

- `id`: optional qualified or unqualified item id.
- `slot`: optional zero-based inventory slot.
- `prefer_hotbar`: optional boolean, default `true`. When selecting by id, prefer
  a matching item in slots `0..11` because those are visible hotbar slots.

Validation:

- Require exactly one of `id` or `slot`.
- Reject missing loaded world/player state.
- Reject an out-of-range slot.
- Reject empty slots.
- Reject an id that is not present in inventory.

Response fields:

- `ok`
- `tick`
- `slot`
- `item`: same lightweight inventory summary shape used by `state.player.items`
  where practical.

Behavior:

- Set `Game1.player.CurrentToolIndex` to the resolved slot.
- Let Stardew's current-item bookkeeping run through the existing property
  setter rather than replacing inventory contents.
- Return the selected item after selection.

### `input.click_tile`

Click a gameplay tile through Stardew's normal world input path.

Example:

```json
{
  "method": "input.click_tile",
  "params": {
    "location": "Frobby_CombatLab",
    "x": 9,
    "y": 9,
    "button": "left"
  }
}
```

Request fields:

- `location`: optional expected current location. If omitted, use the current
  location.
- `x`, `y`: required tile coordinates.
- `button`: optional, default `left`. Slice 23 only accepts `left`; right-click
  should be added in a later slice after the left-click gameplay path is proven.
- `require_current_location`: optional boolean, default `true`. When true,
  reject requests whose `location` does not match the current location.
- `screen_offset_x`, `screen_offset_y`: optional pixel offsets from the tile
  center for advanced cases. Defaults to `32,32`.

Validation:

- Require loaded world/player state.
- Reject active menus, cutscenes, fade/warp state, or missing current location
  with `GameStateInvalid`.
- Reject negative tile coordinates.
- Reject out-of-map tiles when map bounds are available.
- Reject `location` mismatches when `require_current_location` is true.
- Reject unsupported button values.

Response fields:

- `ok`
- `tick`
- `location`
- `tile`: `{ "x": 9, "y": 9 }`
- `screen`: viewport-relative click coordinates
- `world`: pixel coordinates used for the click
- `selected_item`: current selected item summary, if any
- `handled`: whether Stardew reported that the click/use action was handled

Behavior:

- Convert tile coordinates to world pixels at the requested offset.
- Convert world pixels to viewport-relative screen coordinates for diagnostics.
- Update the deterministic cursor/old mouse position enough for Stardew's
  mouse-position based placement path to see the requested tile.
- For a left click, invoke Stardew's gameplay use-tool/click path, preferably
  `Game1.pressUseToolButton()` after setting cursor state.
- Keep menu clicks on the existing `input.click`; `input.click_tile` is
  gameplay-only.

## Runner And DSL

Runner:

- Treat `input.click_tile` as a direct RPC step.
- Add a readable report label such as
  `Click left tile Frobby_CombatLab (9,9)`.
- Auto-capture a step screenshot like other meaningful input actions.
- Preserve normal timeout handling.

DSL:

- Add `Player.SelectItem(...)`.
- Add `Input.ClickTile(...)`.
- Keep `Input.Click(...)` as screen/menu coordinates.

## SVE Proof Scenario

Add `tests/sdv/31-sve-combat-lab-click-bomb-mummy.test.json`.

Scenario shape:

1. Start from the same deterministic fixture and day/weather setup as scenarios
   29 and 30.
2. Give the Monster Splitter and vanilla bomb `(O)287`.
3. Reset `Frobby_CombatLab`.
4. Relocate a real SVE/FTM corrupt mummy into the lab.
5. Down the mummy and relocate it to a known tile.
6. Select `(O)287` through `player.select_item`.
7. Click tile `(9,9)` through `input.click_tile`.
8. Warp or position the player safely if needed.
9. Wait for the vanilla bomb fuse temporary sprite:
   `texture_asset: "LooseSprites/Cursors"`,
   `source_rect: [598, 1279, 3, 4]`,
   `runtime_type: "TemporaryAnimatedSprite"`.
10. Wait for the corrupt mummy label to disappear.
11. Capture a final frozen screenshot and assert the scenario finishes in
    `Frobby_CombatLab`.

The SVE scenario may use vanilla fuse sprite details because those are part of
the SVE proof. Frobby production code must not hard-code that sprite.

## Documentation Updates

Update:

- `docs/rpc-schema.md`
  - Add `player.select_item`.
  - Add `input.click_tile`.
- `docs/dsl-quickstart.md`
  - Show semantic placement vs tile-click placement examples.
  - Correct the bomb guidance from Slice 22: vanilla bombs should be observed via
    temporary sprites and outcome waits, not `location.objects`.
- `docs/wiki/examples.md`
  - Add SVE scenario 30 for semantic placement.
  - Add SVE scenario 31 for click-based placement after it exists.
- `SVE_FROBBY_CAPABILITY_TODO.md`
  - Add Slice 23 as active during implementation, then done after verification.
- SVE `docs/FROBBY.md`
  - Document scenario 31 and the difference from scenario 30.

## Testing Strategy

Use TDD for all new Frobby behavior.

Protocol tests:

- `player.select_item` serializes by id and slot.
- `input.click_tile` serializes tile, location, button, and optional offsets.

Harness tests:

- `player.select_item` resolves by slot.
- `player.select_item` resolves by qualified/unqualified id.
- `player.select_item` prefers hotbar slots by default.
- `player.select_item` rejects empty/missing/out-of-range selections.
- `input.click_tile` converts tile to world and screen coordinates.
- `input.click_tile` rejects active menus and location mismatches.
- `input.click_tile` invokes the gameplay click path and returns selected item
  diagnostics.

Runner tests:

- `input.click_tile` passes through and reports a readable label.
- Timeouts and RPC failures remain reported through existing step handling.

DSL tests:

- `Player.SelectItem(...)` invokes `player.select_item` and deserializes result.
- `Input.ClickTile(...)` invokes `input.click_tile`.

Live SVE verification:

- Run scenario 31 headless.
- Rerun scenarios 30, 29, 28, and 27 as adjacent Combat Lab regressions.

## Risks And Mitigations

- **Input path accidentally becomes another semantic placement shortcut.**
  Mitigation: route through Stardew's gameplay click/use path and keep
  `world.place_inventory_object` as the explicit semantic shortcut.

- **World clicking depends on mouse state internals.**
  Mitigation: isolate cursor/update details behind a small handler adapter and
  unit-test tile-to-screen diagnostics. Document any internal SDV calls used.

- **Raw screen coordinates vary in headless runs.**
  Mitigation: scenarios use tile coordinates. Screen coordinates are returned
  only as diagnostics.

- **Right-click semantics are broader than needed.**
  Mitigation: left-click is required for this slice. Right-click can be added
  only if it fits the same neutral path without expanding scope.

- **Vanilla bomb fuse sprite details could change in a future Stardew release.**
  Mitigation: keep the sprite filter in SVE scenario proof, not Frobby source,
  and keep the main assertion on the gameplay outcome.

## Acceptance Criteria

- Frobby exposes neutral `player.select_item` and `input.click_tile` RPCs.
- Frobby runner and DSL can use both new RPCs.
- Existing `input.click` menu behavior is unchanged.
- SVE scenario 31 proves click-based bomb placement and corrupt-mummy cleanup.
- Docs distinguish semantic placement from click-based placement.
- Focused and broad Frobby tests pass.
- SVE scenarios 31, 30, 29, 28, and 27 pass headless.
