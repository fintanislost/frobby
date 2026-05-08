# SVE Slice 6 Shop Inventory Items Design

## Purpose

Use Stardew Valley Expanded's custom items and data-backed shops as a pressure
test for neutral Frobby support around modded item IDs, shop inventory, and
purchase validation. Slice 6 answers the question "can a test author prove a
modded shop exposes the expected live item data, buy a custom item, and assert
that the player's inventory and money changed correctly without hard-coding the
mod into Frobby?"

The first pass should focus on the shop and inventory foundation. SVE has
reward, secret-note, and buried-item content too, but those flows require more
event/mail/save setup and should layer on after the core item/shop state is
solid.

## Goals

- Add a neutral `state.shop` snapshot for the active Stardew `ShopMenu`.
- Expose enough live shop item data to assert modded item IDs, display names,
  prices, and stock.
- Enrich `state.player.items` with stable item metadata while preserving its
  existing fields.
- Keep `shop.open` and `shop.purchase` mod-agnostic and data-backed.
- Add an SVE scenario proving a custom SVE shop can expose and sell a custom
  item by qualified ID.
- Document the shop/inventory test flow for other mod repos.

## Non-Goals

- No SVE-specific item, shop, or reward knowledge in Frobby code.
- No custom reward-flow support in the first pass.
- No secret-note, buried-item, or special-order reward scenario in the first
  pass.
- No item icon/sprite visual validation in the first pass; Slice 7 owns deeper
  sprite and visual-effect coverage.
- No general-purpose shop-data parser in Frobby. Frobby should inspect the live
  Stardew shop menu and runtime item instances.
- No rewrite of `shop.purchase` into a player-click simulation. It remains a
  semantic data-backed shop test primitive; UI purchase flows can still use
  click/text helpers.

## Current State

Frobby already has:

- `player.give_item`, which creates items via Stardew 1.6 `ItemRegistry`.
- `state.player.items`, which reports inventory `slot`, `id`, `name`, and
  `stack`.
- `shop.open`, which opens a data-backed shop by shop ID.
- `shop.purchase`, which buys by item ID from the active `ShopMenu`, debits
  player money, and adds the salable instance to inventory.
- `state.menu`, which reports the active menu type and minimal shop extras such
  as currency and item count.
- `content.asset`, which can assert `Data/Objects`, `Data/Weapons`,
  `Data/Boots`, `Data/Shops`, and related runtime assets through Stardew's live
  content pipeline.
- `player.set_money` and parameterized `state.assert` for scenario setup and
  assertions.

The gaps for custom item/shop testing are:

- There is no `state.shop` response with live shop items.
- `state.menu` only gives a shop item count, not item IDs, prices, or stock.
- `state.player.items` does not distinguish raw `item_id` from
  `qualified_id`, and does not expose category, quality, or runtime type.
- `shop.purchase` can buy from the active shop, but test authors cannot inspect
  the active shop first to debug IDs/prices.
- SVE currently has no scenario proving a custom item can be discovered in a
  custom shop and purchased through Frobby.

SVE is a strong testbed because its Content Patcher pack adds many custom item
types and custom shops. The best first anchor is:

- `FlashShifter.StardewValleyExpandedCP_CamillaVendor`
  - data target: `Data/Shops`
  - items include:
    - `FlashShifter.StardewValleyExpandedCP_Gravity_Elixir`, price `4000`
    - `FlashShifter.StardewValleyExpandedCP_Lightning_Elixir`, price `8000`
    - `FlashShifter.StardewValleyExpandedCP_Barbarian_Elixir`, price `20000`
    - `FlashShifter.StardewValleyExpandedCP_Aegis_Elixir`, price `28000`

Camilla's vendor is cheaper and simpler than Isaac or Alesia, whose stock
includes expensive weapons and boots. Buying one Gravity Elixir is a good first
proof for a custom object item in a custom shop.

## Architecture

Slice 6 extends the state-first pattern used by earlier slices:

- The harness reports neutral game-thread state through a new `state.shop`
  handler.
- Protocol DTOs define a reusable shop snapshot and shop item summaries.
- Existing `SdvShopMenuState` / `SdvShopItem` concepts from `shop.purchase` can
  be reused or extracted so purchase and state projection read the same live
  shop surface.
- `state.player` gains additive optional item metadata.
- SVE scenario 08 proves the behavior against real SVE content, but Frobby code
  remains unaware of SVE item IDs beyond scenario JSON.

This keeps Frobby useful for any mod that adds data-backed shops or custom
items through Content Patcher or code.

## `state.shop`

Add a new RPC method:

```json
{ "jsonrpc": "2.0", "id": 20, "method": "state.shop" }
```

Example response when a shop is open:

```json
{
  "present": true,
  "menu_type": "ShopMenu",
  "shop_id": "FlashShifter.StardewValleyExpandedCP_CamillaVendor",
  "currency": 0,
  "items": [
    {
      "item_id": "FlashShifter.StardewValleyExpandedCP_Gravity_Elixir",
      "qualified_id": "(O)FlashShifter.StardewValleyExpandedCP_Gravity_Elixir",
      "display_name": "Gravity Elixir",
      "price": 4000,
      "stock": 5,
      "category": 0,
      "runtime_type": "Object"
    }
  ]
}
```

Example response when no shop is open:

```json
{
  "present": false,
  "menu_type": "",
  "shop_id": "",
  "currency": 0,
  "items": []
}
```

Fields:

- `present`: true only when `Game1.activeClickableMenu` is a `ShopMenu`.
- `menu_type`: active menu CLR type name when available.
- `shop_id`: active `ShopMenu.ShopId`, empty when no shop is open.
- `currency`: active shop currency integer when available.
- `items`: live salable item summaries from the active shop.

Each `ShopItemSummary` should include:

- `item_id`: raw item ID from the salable, such as
  `FlashShifter.StardewValleyExpandedCP_Gravity_Elixir`.
- `qualified_id`: qualified ID when available, such as
  `(O)FlashShifter.StardewValleyExpandedCP_Gravity_Elixir`.
- `display_name`: live display name after localization/content patches.
- `price`: unit price from `ShopMenu.itemPriceAndStock` when available,
  otherwise the salable sale price.
- `stock`: available stock when available; null if Stardew does not expose it.
- `category`: item category when the salable can produce an item instance.
- `quality`: quality when the salable can produce an item instance.
- `runtime_type`: CLR type name for the salable or item instance.

Projection should be defensive. If generating a salable instance for metadata
fails, `state.shop` should still return the item ID, display name, price, and
stock where possible.

## Enriched `state.player.items`

Keep existing fields stable:

- `slot`
- `id`
- `name`
- `stack`

Add fields:

- `item_id`: raw item ID.
- `qualified_id`: qualified item ID.
- `category`: item category when available.
- `quality`: item quality when available.
- `runtime_type`: CLR type name.

The existing `id` should remain the qualified ID for backwards compatibility.
New tests should prefer `qualified_id` when checking modern Stardew 1.6 item
identity.

Example:

```json
{
  "slot": 12,
  "id": "(O)FlashShifter.StardewValleyExpandedCP_Gravity_Elixir",
  "item_id": "FlashShifter.StardewValleyExpandedCP_Gravity_Elixir",
  "qualified_id": "(O)FlashShifter.StardewValleyExpandedCP_Gravity_Elixir",
  "name": "Gravity Elixir",
  "stack": 1,
  "category": 0,
  "quality": 0,
  "runtime_type": "Object"
}
```

## SVE Scenario 08: Custom Shop Purchase

Add `tests/sdv/08-sve-custom-shop-inventory-items.test.json`.

Scenario shape:

1. Load the existing SVE fixture.
2. Set money to a known value, such as `10000`.
3. Open `FlashShifter.StardewValleyExpandedCP_CamillaVendor` with `shop.open`.
4. Assert `state.shop.present == true`.
5. Assert `state.shop.shop_id == 'FlashShifter.StardewValleyExpandedCP_CamillaVendor'`.
6. Assert `state.shop.items contains item_id 'FlashShifter.StardewValleyExpandedCP_Gravity_Elixir'`.
7. Assert `state.shop.items contains price 4000` once the expression helper can
   address the chosen item robustly. If the existing expression DSL cannot
   express item-specific price checks cleanly, rely on a runner test for price
   projection and keep the SVE scenario focused on item presence and purchase.
8. Run `shop.purchase` for
   `FlashShifter.StardewValleyExpandedCP_Gravity_Elixir`.
9. Assert `state.player.money == 6000`.
10. Assert `state.player.items contains qualified_id '(O)FlashShifter.StardewValleyExpandedCP_Gravity_Elixir'`.
11. Capture a final screenshot if the shop remains open and visually useful.

The exact qualified ID should be validated during implementation with live
`state.shop` output. If Stardew returns an unqualified ID for shop salables but a
qualified ID for inventory items, the scenario should assert both fields in
their appropriate state snapshots.

## Testing Strategy

Frobby unit tests:

- Protocol serialization for `ShopState` and enriched `PlayerItemSummary`.
- `StateShopHandler` tests for no active shop, active shop metadata, item price,
  item stock, and salable instance metadata.
- `StatePlayerHandler` tests for raw/qualified IDs, category, quality, and
  runtime type.
- Regression tests that `shop.purchase` still buys the same active-shop item
  IDs after any shared projection refactor.

Runner/SVE tests:

- SVE scenario 08 headless single run.
- SVE smoke subset including scenarios 01, 04, and 08 because runtime content
  assets and shop item identity are related.
- Repeat scenario 08 twice if stock or player inventory behavior looks
  sensitive to save state.

Docs:

- Update `docs/rpc-schema.md` with `state.shop` and enriched inventory fields.
- Update `docs/dsl-quickstart.md` and `README.md` with a short custom shop item
  testing example.
- Mark Slice 6 active/done in `SVE_FROBBY_CAPABILITY_TODO.md`.

## Deferred Follow-Ups

- Reward-flow primitives for secret notes, buried items, event rewards, and
  special orders.
- More expressive collection assertions such as "find item by ID, then assert
  price" if current `contains` expressions are too coarse.
- Item icon/texture validation using draw events or bitmap assertions.
- Custom weapon and boot purchase scenarios against Isaac or Alesia vendors.
- Inventory capacity/full-inventory behavior around purchases and
  `player.give_item`.

## Acceptance Criteria

- Frobby exposes `state.shop` with live item IDs, qualified IDs, display names,
  prices, and stock for an active `ShopMenu`.
- `state.player.items` includes raw and qualified item IDs while preserving
  existing `id` behavior.
- `shop.purchase` continues to work with active shops after any projection
  refactor.
- SVE scenario 08 passes headlessly against Camilla's custom vendor.
- Docs explain how a mod developer can test custom shop inventory and purchase
  flows.
- No Frobby production code contains SVE-specific item or shop IDs.
