# SVE Slice 26 Star-Token Shop Currency Design

## Goal

Use Stardew Valley Expanded's Stardew Fair star-token shop to harden
Frobby's neutral support for non-gold shop currencies. A coding agent should be
able to inspect the active shop currency, set an appropriate test balance,
purchase an item through the existing semantic shop primitive, and verify that
the correct currency changed.

## Context

Slice 25 proved that Frobby can enter a festival, discover or open a live
festival shop, inspect `ShopMenu`, and buy SVE-added gold shop items. The next
gap is the Stardew Fair shop, which uses star tokens instead of gold.

Frobby already has:

- `state.shop`, which exposes the active `ShopMenu` and its numeric currency.
- `shop.purchase`, which can buy an item from an open shop.
- `player.set_money` and inventory assertions for ordinary shop scenarios.
- SVE festival start and shop scenarios proving Content Patcher shop edits are
  visible in live festival menus.

The current purchase path is gold-oriented. It records and mutates
`Game1.player.Money`, which is correct for ordinary shops but wrong for Fair
star-token shops.

SVE pressure points found during research:

- `code/Shops/vanilla/Fair.json` edits `Data/Shops`.
- It adds items to `Festival_StardewValleyFair_StarTokens`.
- The SVE Fair catalogue entry costs `9999` with `Currency: 1`.
- The SVE Prismatic Pop recipe costs `3000` with `Currency: 1` and is gated to
  year 2.

## Decision

Implement neutral support for active shop currency balances, then prove it with
the SVE Fair star-token shop.

This slice should support currency `0` and currency `1`:

- `0`: gold, backed by the player's money.
- `1`: Stardew Fair star tokens, backed by `Game1.player.festivalScore`.

Unsupported shop currencies should fail with a clear error instead of silently
charging gold.

## Frobby Design

Extend shop state with balance-oriented fields while keeping existing fields
compatible:

```json
{
  "currency": 1,
  "currency_name": "star_tokens",
  "currency_balance": 10000
}
```

`currency` remains the raw `ShopMenu.currency` value. `currency_name` is a
friendly label for known currencies. `currency_balance` is the player's current
balance for the active shop currency.

Add a neutral balance setter:

```json
{
  "action": "player.set_shop_currency",
  "args": { "currency": 1, "amount": 10000 }
}
```

The command should be generic and not named after the Fair. For currency `0`, it
may route to the same backing state as `player.set_money`. For currency `1`, it
sets `Game1.player.festivalScore`. Unsupported currencies return an explicit
unsupported-currency error.

Update `shop.purchase` so it validates and debits the active shop currency. The
result should preserve legacy money fields and add currency-specific fields:

```json
{
  "purchased": true,
  "currency": 1,
  "previous_currency_balance": 10000,
  "currency_balance": 1,
  "previous_money": 5000,
  "money": 5000
}
```

For gold shops, the currency-balance fields mirror the money fields. For
star-token shops, the money fields remain unchanged and the currency-balance
fields show the token debit.

## SVE Scenario Design

Add a new SVE live scenario for the Stardew Fair star-token shop.

Scenario outline:

1. Set Fall 16, year 1, and start the Stardew Fair festival.
2. Open `Festival_StardewValleyFair_StarTokens` with the existing shop open
   helper or live festival shop path.
3. Assert `state.shop.present == true`.
4. Assert `state.shop.shop_id == 'Festival_StardewValleyFair_StarTokens'`.
5. Assert `state.shop.currency == 1`.
6. Assert the shop contains
   `FlashShifter.StardewValleyExpandedCP_Furniture_Catalogue_2`.
7. Set shop currency `1` to `10000`.
8. Purchase the SVE Fair catalogue item.
9. Assert the currency balance is now `1`.
10. Assert player money did not change because the purchase used tokens.
11. Assert the player inventory contains the purchased item.
12. Capture a final screenshot.

The catalogue item is preferred for the first proof because it is available in
year 1. The year-2 Prismatic Pop recipe remains useful future coverage for
conditional Fair shop entries.

## Testing

Frobby unit coverage:

- Protocol model tests for the new shop state and purchase result fields.
- Harness tests for currency balance projection on gold and star-token shops.
- Harness tests for `player.set_shop_currency`.
- Harness tests proving `shop.purchase` debits the active shop currency instead
  of always debiting gold.
- Negative coverage for unsupported currencies.

SVE live coverage:

- Run the new Fair star-token scenario headless under the `core` mod set.
- Re-run the Slice 25 festival shop scenario to ensure gold shop purchases still
  pass.
- Re-run a focused Frobby test subset for shop state, shop purchase, and the new
  currency command.

## Non-Goals

- Menu-item click purchasing inside `ShopMenu`. This remains a follow-up after
  currency semantics are correct.
- Full support for every Stardew shop currency. This slice covers gold and
  Fair star tokens, then returns clear errors for unsupported currencies.
- SVE-specific production logic in Frobby.
- Year-2 Prismatic Pop recipe purchase coverage.

## Acceptance Criteria

- Frobby exposes the active shop currency balance in a neutral way.
- A scenario can set the active shop currency balance without relying on a
  Stardew Fair-specific command name.
- `shop.purchase` uses the active shop currency and preserves backward
  compatibility for gold shops.
- The new SVE Fair scenario buys an SVE-added star-token item and proves money
  remains unchanged.
- Existing gold festival shop coverage still passes.
