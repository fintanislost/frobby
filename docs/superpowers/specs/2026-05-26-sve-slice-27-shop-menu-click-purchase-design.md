# SVE Slice 27 Shop Menu Click Purchase Design

## Goal

Use Stardew Valley Expanded festival shops to harden Frobby's neutral support
for visible `ShopMenu` purchase flows. A coding agent should be able to target
a shop item by stable item identity, move the shop list until the item is
visible, click the real menu row, and verify inventory plus currency changes.

## Context

Slices 25 and 26 proved that Frobby can enter festival contexts, inspect live
shops, seed alternate shop currency balances, and purchase through the semantic
`shop.purchase` RPC. That path validates shop data and currency semantics, but
it does not prove that the rendered `ShopMenu` row can be clicked by a player or
that a modded shop's visible purchase controls still work.

Frobby already has:

- `state.shop` for active `ShopMenu` item projection.
- `shop.purchase` for semantic data-backed purchasing.
- `input.click` for raw active-menu coordinates.
- `input.click_menu_button` for reflected custom menu buttons.
- `input.click_tile` for gameplay tile clicks.
- `player.set_money` and `player.set_shop_currency` for deterministic balances.

The missing capability is a neutral bridge between data-backed shop item
identity and the visible `ShopMenu` click surface.

## Research Notes

Stardew's `ShopMenu` exposes the stock list through public fields such as
`forSale`, `itemPriceAndStock`, and `currency`. It also exposes menu behavior
through `receiveLeftClick`, `receiveRightClick`, `receiveScrollWheelAction`, and
hover/update methods. The XML docs list stock and currency contracts but do not
publish row bounds or current visible row metadata.

Because row bounds are UI details rather than data details, Slice 27 should use
reflection defensively inside a narrow shop-menu adapter. Reflection should be
limited to Stardew menu fields that are stable for the active game version,
with clear failure messages when a future Stardew version changes them.

## Options Considered

### Option A: Raw Coordinate Scenario Steps

Scenarios could call `input.click` with hardcoded screen coordinates after
opening a shop.

This is closest to a human click, but it is brittle across resolution, UI scale,
list position, and shop variants. It also gives reports little explanation
beyond "clicked x,y".

### Option B: Extend `shop.purchase` With A Click Flag

`shop.purchase` could add `click: true` and internally route through the menu
click path.

This keeps one purchase RPC, but it blurs semantic data purchase and visible UI
purchase. It also makes existing tests less clear about which path is being
validated.

### Option C: Add `shop.click_purchase`

Add a separate shop-menu action that targets an active `ShopMenu` item by
qualified id, raw id, or display name, scrolls/reveals it if needed, clicks the
visible row, and reports the matched item, clicked bounds, and currency delta.

This keeps click validation explicit while reusing the same item projection and
currency reporting concepts as `shop.purchase`.

Decision: use Option C.

## Frobby Design

Add a new RPC:

```json
{
  "action": "shop.click_purchase",
  "args": {
    "item_id": "(O)FlashShifter.StardewValleyExpandedCP_Decorative_Tulips",
    "count": 1
  }
}
```

Request fields:

- `item_id`: required. Matches raw item id or qualified item id using
  `ShopStateProjector.MatchesRequestedItem`.
- `display_name`: optional alternate target when the caller does not know an
  item id. Exact match by default.
- `count`: optional, default `1`. Initial implementation supports one click
  when `count == 1`; larger counts are rejected with a clear message until a
  safe repeat/right-click strategy is added.
- `scroll_attempts`: optional, default enough attempts to cover the active shop
  list. Prevents infinite scroll loops.

Response fields should mirror `ShopPurchaseResult` and add click context:

```json
{
  "ok": true,
  "tick": 1234,
  "shop_id": "Festival_FlowerDance_Pierre",
  "item_id": "(O)Example.Item",
  "display_name": "Example Item",
  "count": 1,
  "unit_price": 250,
  "currency": 0,
  "previous_currency_balance": 1000,
  "currency_balance": 750,
  "previous_money": 1000,
  "money": 750,
  "screen": { "x": 848, "y": 420 },
  "bounds": { "x": 480, "y": 388, "width": 736, "height": 80 },
  "visible_index": 2,
  "item_index": 5,
  "scrolled": true
}
```

The production adapter should:

1. Require a loaded game and an active `ShopMenu`.
2. Find the target item in `shop.forSale` using the existing neutral matching
   rules.
3. Reveal the target by adjusting the shop menu list through native scroll
   methods or a narrowly-scoped reflected current-index setter.
4. Determine a click point inside the visible row.
5. Record currency balances before the click.
6. Deliver a real `ShopMenu.receiveLeftClick(x, y)` call.
7. Record balances after the click and return click metadata.

If the item cannot be made visible or no visible row bounds can be determined,
the action should fail clearly instead of falling back to `shop.purchase`.

## SVE Scenario Design

Add a new SVE proof scenario after scenario 34.

Preferred proof:

1. Set Spring 24, year 1, with enough gold.
2. Start the Flower Dance festival.
3. Open `Festival_FlowerDance_Pierre`.
4. Assert the shop contains an SVE decorative flower item.
5. Capture the open shop before purchase.
6. Call `shop.click_purchase` for the SVE item.
7. Assert player money decreased.
8. Assert inventory contains the item.
9. Capture the final shop/player state.

The Flower Dance gold shop is the safest first live proof because it avoids the
Fair star-token clamp while exercising a real SVE-added shop entry. Once stable,
the same action can be used against the Fair star-token shop as follow-up
coverage.

## Testing

Frobby unit coverage:

- Protocol serialization for `ShopClickPurchaseRequest` and
  `ShopClickPurchaseResult`.
- Harness handler validation: missing target, invalid count, no active shop,
  unsupported currency, target not found, and target not visible after scroll.
- Harness happy path: item is revealed, the menu click is invoked at the
  reported row center, and balances are reported from the active currency.
- Runner scenario step support: JSON action dispatch, step label, and
  autocapture/report integration.
- DSL wrapper coverage for typed C# callers.

SVE live coverage:

- Run the new scenario headless under the `core` profile.
- Re-run scenario 33 to prove existing semantic festival shop coverage still
  passes.
- Re-run scenario 34 to prove alternate currency shop state still passes.

## Non-Goals

- Full arbitrary coordinate recording or replay.
- A pathfinder for opening distant shops.
- Multi-click stack accumulation for `count > 1`.
- Support for every possible custom shop menu subclass in the first slice.
- SVE-specific production logic in Frobby.

## Acceptance Criteria

- `shop.click_purchase` buys through the active `ShopMenu` click path, not by
  calling `shop.purchase` or adding an item directly.
- A test can target a shop item by raw id or qualified id.
- The result includes item identity, currency delta, and click bounds for
  report screenshots.
- The SVE proof scenario buys an SVE-added festival item through visible menu
  interaction under headless execution.
- Existing shop state, semantic purchase, and alternate-currency tests still
  pass.
