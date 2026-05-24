# Frobby Scenario Examples

This page points to real scenario files that demonstrate Frobby patterns. The
examples live in sibling mod repos when those repos are available locally. Do not
copy scenario bodies into this page; inspect the source files so examples stay
current.

## Repo Profiles And Dependencies

- SVE core smoke:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/01-sve-core-loads.test.json`
- SVE Grandpa's Farm profile:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/20-sve-grandpas-farm-profile.test.json`
- SVE Frontier Farm profile:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/24-sve-frontier-farm-profile.test.json`

Use these when adding profile coverage, external dependency cache coverage, or
alternate content-pack runs.

## Alternate Farm Fixtures

- Frontier Farm fixture override:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/24-sve-frontier-farm-profile.test.json`
- Frontier Farm config-gated shortcut coverage:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/25-sve-frontier-farm-instant-unlocks.test.json`

Use these when a test needs `save_overrides.farm_type` to stage the same source
fixture as a modded/additional farm.

## Click-First UI Testing

- Starberg click navigation:
  `/home/fintan/stardewRepos/stonks/tests/sdv/10-starberg-panel-click-navigation.test.json`
- Starberg order entry click flow:
  `/home/fintan/stardewRepos/stonks/tests/sdv/27-starberg-click-text-buy-order.test.json`
- Starberg activity panel click flow:
  `/home/fintan/stardewRepos/stonks/tests/sdv/34-starberg-click-text-activity-panel.test.json`

Use these when testing menu panels through player-like clicks instead of command
shortcuts.

## Text Bounds, Screenshots, And Reports

- Starberg visual baseline:
  `/home/fintan/stardewRepos/stonks/tests/sdv/26-starberg-ui-visual-baseline.test.json`
- Starberg chart panel:
  `/home/fintan/stardewRepos/stonks/tests/sdv/38-starberg-chart-panel-live.test.json`
- Starberg news/intel document flow:
  `/home/fintan/stardewRepos/stonks/tests/sdv/77-starberg-news-intel-depth.test.json`

Use these when adding `draw.text_all_within`, step screenshots,
`screenshot.capture_next_frame`, or final frozen screenshot coverage.

## Runtime Map And Content Assertions

- SVE content asset runtime checks:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/04-sve-content-assets-runtime.test.json`
- SVE custom location and tile-action warp:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/06-sve-tile-action-warp.test.json`
- SVE secret-note hoe dig:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/26-sve-secret-note-dig.test.json`
- SVE Frontier Farm runtime map checks:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/24-sve-frontier-farm-profile.test.json`

Use these when proving Content Patcher maps, data assets, Stardew tool-driven
tile effects, and runtime location metadata.

## NPCs, Dialogue, Events, And Festivals

- SVE NPC relationship/dialogue:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/05-sve-npc-schedules-dialogue-relationships.test.json`
- SVE event dialogue choice:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/11-sve-event-dialogue-choice.test.json`
- SVE Spirit's Eve festival chest:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/19-sve-spirit-eve-chest.test.json`
- SVE Spirit's Eve festival actor dialogue:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/32-sve-spirit-eve-actor-dialogue.test.json`
- SVE Flower Dance festival shop flow:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/33-sve-flower-dance-shop-flow.test.json`

Use these when testing events, dialogue choice menus, relationship state, or
festival maps, or festival shops. For active event or festival actors, wait with
`wait.event_active.actor_name`, then use `world.interact_npc`; the RPC will
prefer ordinary current-location NPCs and fall back to active event actors. For
festival shops, use `state.tile_actions` to prove the shop action exists, then
open it with `world.interact_tile_action` or a deliberate
`input.click_tile.allow_event_input` click when player-like event input matters.

## Shops, Inventory, Combat, Fishing, And World Content

- SVE custom shop and inventory:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/08-sve-custom-shop-inventory-items.test.json`
- SVE combat damage:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/12-sve-combat-monster-damage.test.json`
- Combat Lab vanilla monster lifecycle:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/27-sve-combat-lab-vanilla-monster.test.json`
- Combat Lab relocated mod monster lifecycle:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/28-sve-combat-lab-relocate-mod-monster.test.json`
- Combat Lab native explosion cleanup:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/29-sve-combat-lab-explode-mummy.test.json`
- SVE fishing table and catch sampling:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/16-sve-fishing-core.test.json`
- SVE world object interaction:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/18-sve-object-piggy-bank-interaction.test.json`

Use these when testing runtime state rather than parsing a mod's content files.

## Explosion Cleanup

Use `world.explode_tile` when a mod feature depends on native Stardew explosion
behavior, such as mummy cleanup or object blast effects. This keeps the
assertion focused on world-state behavior without also depending on bomb
inventory, placement, or fuse timing. Follow it with `wait.location_content` to
assert the actual world-state change.

Use `world.place_inventory_object` when a scenario needs deterministic
inventory-object placement. Use `player.select_item` plus `input.click_tile`
when a scenario needs selected-item, gameplay-click behavior; wait for
`can_move: true` and `is_busy: false` before issuing player-like clicks after
combat or tool animations. Vanilla bomb placement uses the action/right-click
path and should be observed through `state.visual_effects` fuse sprites plus
the final world-state outcome, not through `state.location.objects`.

## Save, Reload, And Long-Running State

- Starberg save/reload smoke:
  `/home/fintan/stardewRepos/stonks/tests/sdv/70-starberg-save-reload-persistence.test.json`
- Starberg pending settlement persistence:
  `/home/fintan/stardewRepos/stonks/tests/sdv/83-starberg-sell-settlement-save-reload.test.json`
- Starberg depleted book side persistence:
  `/home/fintan/stardewRepos/stonks/tests/sdv/84-starberg-empty-book-side-save-reload.test.json`

Use these when testing Frobby's neutral save/reload flow and mod state that must
survive title-screen reloads.
