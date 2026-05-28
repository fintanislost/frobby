# SVE Slice 31: Movie Concession Purchase

## Purpose

Use Stardew Valley Expanded as a real testbed for the Stardew movie-theater
concession flow. Slice 30 proved that a selected movie ticket can invite a
custom SVE NPC. Slice 31 should prove that a test can reach the concession UI,
observe the available concession choices, and buy or select a concession through
player-like UI interaction.

The Frobby capability added by this slice must stay mod-neutral. SVE-specific
NPC names, event flags, dialogue text, and asset keys belong only in the SVE
scenario.

## Current Context

Already available in Frobby:

- `input.click_tile` can click real map tiles and now preserves selected-object
  NPC interactions.
- `shop.open`, `state.shop`, `shop.purchase`, and `shop.click_purchase` can
  inspect and buy from ordinary `ShopMenu` instances.
- `content.asset` can inspect runtime content assets including
  `Data/ConcessionTastes`.
- Scenario reports capture step screenshots and menu state labels.

Relevant SVE coverage:

- Scenario 36 reaches `MovieTheater` and clicks a theater worker NPC.
- Scenario 38 invites Sophia with a movie ticket and verifies her movie reaction
  and concession taste data is present.
- Prior design notes list concession purchase and taste validation as the next
  movie-theater follow-up.

## Acceptance Target

Add one SVE scenario for a visible concession flow:

1. Seed movie-theater progression using the same event/mail flags as scenarios
   36 and 38.
2. If vanilla Stardew requires an invited guest before concessions are useful,
   reuse the Sophia invite setup from scenario 38.
3. Warp the player to `MovieTheater` in front of the concession interaction
   point.
4. Open the concession UI through a player-like tile click, not by directly
   mutating game state.
5. Capture the visible concession menu or menu-equivalent state.
6. Buy or select one visible concession item through click-based UI input.
7. Assert a meaningful post-action effect, such as money decreasing, the held
   concession changing, inventory/item state changing, or a menu state that
   confirms the selection.
8. Assert the relevant SVE `Data/ConcessionTastes` entry exists for the target
   NPC.
9. Capture pre-open, menu-open, and post-purchase screenshots.

The scenario should stop at the concession purchase boundary. Full screening
and post-movie reaction validation belong in a later slice.

## Frobby Capability Strategy

Start by treating the concession UI as an ordinary Stardew menu:

- If it opens a `ShopMenu`, reuse `state.shop` and `shop.click_purchase`.
- If it opens a dedicated vanilla movie-concession menu, add neutral Frobby
  support for that Stardew menu type.

Any new support should expose the active menu's visible item choices and click a
visible item by stable identity or display text. It must not contain SVE NPC
names, SVE location assumptions, or hard-coded concession lists.

Preferred generic shape if a new menu primitive is needed:

- `state.menu` remains the broad active-menu summary.
- A domain-specific projector may be added for the vanilla movie concession
  menu if the underlying Stardew type exposes strongly typed fields.
- The click action should follow the existing `shop.click_purchase` pattern:
  locate a visible entry, click its current UI bounds, and report the selected
  item, price or currency delta when available, and menu type.

## Out Of Scope

- Running the full movie screening.
- Asserting before/during/after movie reactions.
- Testing every concession item or every SVE NPC taste.
- Adding SVE-specific helpers to Frobby.
- Parsing SVE content packs outside the running game. Runtime checks should use
  the existing `content.asset` behavior.

## Testing

Frobby tests, only if new framework behavior is needed:

- Unit tests for the new concession/menu projector, including visible item
  summaries and stable item identity.
- Unit tests for the concession/menu click handler, including successful clicks,
  missing item failures, and readable result metadata.
- Runner tests only if a new scenario action or result label is introduced.

SVE tests:

- New scenario 39 for the movie concession purchase flow.
- Rerun scenario 38 as a regression because it shares theater setup and Sophia
  invite state.
- Rerun scenario 36 if tile-click theater behavior changes.

Verification:

- Targeted Frobby unit tests for changed handlers or projectors.
- `dotnet build src/Runner/Runner.csproj --nologo`.
- Headless SVE scenario 39.
- Headless SVE scenario 38 regression.
- Headless SVE scenario 36 regression if shared click code changes.

## Follow-Ups

- Full movie screening reaction flow.
- Guest-specific concession taste outcome validation after the movie.
- Claire/Martin worker-specific concession and theater edge cases.
- A broader custom-NPC movie reaction and concession taste matrix.

## Open Risks

- The concession UI may be a specialized Stardew menu rather than a `ShopMenu`,
  requiring a new neutral Frobby primitive.
- Some concession selection state may live inside private menu or theater
  fields, so the first stable assertion may need to use visible menu state plus
  money/item deltas rather than a single named state field.
- The live scenario may need the full invite setup before the concession flow is
  enabled, which would make scenario 39 depend on the scenario 38 setup pattern.
