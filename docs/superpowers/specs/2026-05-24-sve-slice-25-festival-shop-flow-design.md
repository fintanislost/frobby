# SVE Slice 25 Festival Shop Flow Design

## Goal

Use Stardew Valley Expanded festival shops to harden Frobby's neutral support
for player-like festival shop UI flows: enter a festival, discover a shop map
action, open the live `ShopMenu`, inspect the active shop, purchase an item, and
verify inventory/money state.

## Context

Slice 24 made active festival actors interactable through neutral event actor
waits and `world.interact_npc` fallback. Festival shops are the next adjacent UI
surface because they live inside active festival events but are usually opened
from map tile actions rather than ordinary NPC schedules.

Frobby already has:

- `festival.start` and `wait.event_active` for deterministic festival entry.
- `state.tile_actions` and `world.interact_tile_action` for map action
  discovery/execution.
- `state.shop` and `shop.purchase` for active `ShopMenu` inspection and
  semantic purchase.
- `input.click_tile` for selected-item/gameplay tile clicks, but it currently
  rejects active event/festival state through its `Game1.eventUp` guard.

SVE pressure points found during research:

- `code/Shops/vanilla/FlowerFestival.json` adds SVE decorative flower items to
  `Festival_FlowerDance_Pierre`.
- `code/Shops/vanilla/Luau.json` adds the Ice Cream Sundae recipe to
  `Festival_Luau_Pierre`.
- `code/Shops/vanilla/Fair.json` adds star-token items to
  `Festival_StardewValleyFair_StarTokens`.
- SVE festival maps expose real map actions such as
  `Shop Festival_FlowerDance_Pierre` on the Flower Dance map and `Shop shop` on
  Spirit's Eve variants.

## Decision

Start with the Flower Dance shop flow.

The Flower Dance gives a narrow, stable proof:

- It uses a real temporary festival map.
- It exposes an explicit `Shop Festival_FlowerDance_Pierre` map action.
- It uses ordinary gold currency, so the slice can focus on festival UI path
  coverage before tackling star-token handling.
- It contains SVE-added items that prove Content Patcher shop edits are present
  in the live festival shop.

## Frobby Design

Add one neutral option to `input.click_tile`:

```json
{ "action": "input.click_tile", "args": { "x": 28, "y": 37, "button": "right", "allow_event_input": true } }
```

`allow_event_input` defaults to `false`. Existing scenarios keep the current
safety behavior and still fail fast when a normal gameplay tile click is
attempted while `Game1.eventUp` is true. When explicitly true, the handler may
send the click through the same Stardew click path during active events and
festivals.

The response shape does not need to change. The existing `handled`, tile,
screen/world coordinate, selected item, and location fields remain enough for
reports and debugging.

If live validation shows that raw click-to-tile is too brittle for opening a
specific festival shop because of range, viewport, or player-position
constraints, the SVE scenario may use `world.interact_tile_action` for the shop
open step. The Frobby `allow_event_input` option still remains useful for
future player-like festival interactions and is not SVE-specific.

## SVE Scenario Design

Add `tests/sdv/33-sve-flower-dance-shop-flow.test.json`.

Scenario outline:

1. Set Spring 24, year 1, and give the player enough money.
2. Start the Forest festival with `festival.start`.
3. Wait for a festival event in `Temp`.
4. Assert `state.tile_actions` near the Flower Dance shop contains
   `Shop Festival_FlowerDance_Pierre`.
5. Open the shop from the festival map. Preferred path is `input.click_tile`
   with `allow_event_input: true`; fallback path is `world.interact_tile_action`
   at the same action tile.
6. Assert `state.shop.present == true`.
7. Assert `state.shop.shop_id == 'Festival_FlowerDance_Pierre'`.
8. Assert the shop contains
   `FlashShifter.StardewValleyExpandedCP_Decorative_Tulips`.
9. Purchase one decorative tulip.
10. Assert money decreased and the inventory contains the purchased item.
11. Freeze/capture a final screenshot.

## Testing

Frobby unit coverage:

- Protocol serialization/deserialization for the new `allow_event_input`
  request field.
- Harness tests proving default event-up guard still rejects clicks.
- Harness tests proving explicit `allow_event_input` permits the click path
  while event state is active.
- Runner label/autocapture tests only if the step detail changes.

SVE live coverage:

- Run scenario 33 headless under the `core` mod set.
- Re-run adjacent festival scenarios 19 and 32 to ensure Spirit's Eve chest and
  actor interaction were not regressed.
- Run a focused Frobby unit subset for input click tile and shop behavior.

## Non-Goals

- Star-token or non-gold festival currency purchases. The Fair shop is a good
  follow-up after the ordinary festival shop path is stable.
- A generic pathfinder or player movement system for clicking distant tiles.
- Menu-item click purchases inside `ShopMenu`. Existing `shop.purchase` remains
  the purchase primitive for this slice.
- SVE-specific logic in Frobby production code.

## Acceptance Criteria

- `input.click_tile` remains backward-compatible by default and only permits
  active-event clicks with an explicit opt-in flag.
- Frobby docs explain when to use `allow_event_input`.
- The SVE Flower Dance scenario opens the live festival shop and buys an
  SVE-added festival item under headless execution.
- Adjacent SVE festival scenarios still pass.
