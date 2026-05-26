# SVE Frobby Capability TODO

Purpose: use Stardew Valley Expanded as a broad mod testbed to identify neutral Frobby capabilities needed beyond Starberg's UI-heavy coverage. Keep every Frobby addition mod-agnostic; SVE scenarios should prove the capability against a real complex mod, not bake SVE assumptions into Frobby.

Status key:
- Pending: not designed yet.
- Planning: design or implementation plan in progress.
- Active: implementation underway.
- Done: implemented, documented, and verified against SVE.

## Capability Slices

- [x] Done: Slice 1, custom locations, maps, warps, and tile actions.
  - SVE pressure: many Content Patcher `CustomLocations`, custom map assets, `TouchAction`/`MagicWarp`/`LoadMap` style behavior, and code patches around location warps.
  - Frobby goal: let tests prove custom locations exist, can be entered, expose map/tile metadata, and support player-like tile action flows.
  - Implementation plan: `docs/superpowers/plans/2026-05-05-sve-slice-1-location-map-tools.md`.
  - Done: introspection foundation (`state.locations`, expanded `state.location`, `state.map_tile`, and `wait.location`) verified against SVE scenario 02.
  - Done: tile-action candidate discovery (`state.tile_actions`), tile-action execution (`world.interact_tile_action`), and SVE scenario 06 (`sve_tile_action_warp`) verified headlessly.

- [x] Done: Slice 2, events and cutscenes observability foundation.
  - SVE pressure: event scripts, world-change events, actor positioning, viewport corrections, grange judging patches, dialogue during scripted scenes.
  - Frobby goal: trigger events deterministically, advance or skip event scripts, assert current event id, actors, dialogue text, camera/viewport, and event completion.
  - Design spec: `docs/superpowers/specs/2026-05-05-sve-slice-2-event-observability-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-05-sve-slice-2-event-observability.md`.
  - Done: `state.event`, `event.start`, `event.skip`, runner-side `wait.event_active` / `wait.event_complete`, readable dialogue/menu text extras, and SVE scenario 03 (`sve_event_observability_krobus`).
  - Done Slice 2 follow-up: structured menu choices on `state.menu` / `state.event`, runner-side `wait.menu`, `input.click_menu_choice`, `input.click_menu_advance`, generic `event.advance` / `ui.acknowledge` menu advancement, `player.add_event_seen`, `state.player.events_seen`, and SVE scenario 11 (`sve_event_dialogue_choice_dusty`).

- [x] Done: Slice 3, Content Patcher asset coverage.
  - SVE pressure: CP `Load` and `Edit*` actions for maps, strings, data assets, portraits, sprites, recolors, and config-gated patches.
  - Frobby goal: inspect loaded asset names and selected asset metadata, prove expected CP assets are available, and verify map/texture assets without relying only on full screenshots.
  - Design spec: `docs/superpowers/specs/2026-05-06-sve-slice-3-runtime-content-assets-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-06-sve-slice-3-runtime-content-assets.md`.
  - Done: runtime `content.asset` query plus JSON scenario assertions for maps, textures, strings, bounded data dictionaries, and selected nested data objects.
  - Verified: SVE scenario 04 (`tests/sdv/04-sve-content-assets-runtime.test.json`) validates CP-loaded maps and `Data/Locations` runtime metadata under headless execution.

- [x] Done: Slice 4, NPC schedules, dialogue, and relationships.
  - SVE pressure: many custom NPCs, custom homes, schedules, movie-theater strings, relationship-gated content, and post-event dialogue patches.
  - Frobby goal: set relationship and mail state, locate NPCs, move time/date, interact with NPCs, and assert speaker/text/location state.
  - Design spec: `docs/superpowers/specs/2026-05-06-sve-slice-4-npc-schedules-dialogue-relationships-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-06-sve-slice-4-npc-schedules-dialogue-relationships.md`.
  - Done: `state.npcs`, expanded `state.npc`, `player.set_friendship`, `world.warp_npc`, parameterized `state.assert`, runner-side `wait.npc_location`, readable `state.menu` dialogue text, active-menu-safe next-frame screenshots, and SVE scenario 05 against Sophia.

- [x] Done: Slice 5, Farm Type Manager spawn and conditional world content.
  - Design spec: `docs/superpowers/specs/2026-05-08-sve-slice-5-ftm-spawn-world-content-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-08-sve-slice-5-ftm-spawn-world-content.md`.
  - SVE pressure: FTM pack content, conditional forage/monster spawns, location-specific spawn rules, config/mail-gated difficulty variants.
  - Frobby goal: control spawn-relevant state, wait for spawns, inspect objects/monsters/critters in a location, and assert spawn counts/types deterministically.
  - Done: `state.location.resource_clumps`, `state.location.monsters`, richer object metadata, runner-side `wait.location_content`, and SVE scenario 07 against Grandpa's Shed exterior logs.
  - Done Slice 5 follow-up: deterministic SVE monster-spawn coverage validates the Crimson Badlands corrupt mummy guard at tile `20,144` through neutral monster metadata (`sprite_texture`, `health`, `max_health`, `damage`) and `wait.location_content` filters.

- [x] Done: Slice 6, custom items, inventory, rewards, and shops.
  - Design spec: `docs/superpowers/specs/2026-05-08-sve-slice-6-shop-inventory-items-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-08-sve-slice-6-shop-inventory-items.md`.
  - SVE pressure: custom items, special rewards, secret-note/buried-item patches, shops and rewards that may reference modded qualified item ids.
  - Frobby goal: give/assert qualified item ids, inspect inventory and shops, trigger reward flows, and validate item icons/sprites when needed.
  - Done: `state.shop`, raw/qualified shop purchase matching, enriched `state.player.items`, and SVE scenario 08 against a custom vendor.

- [x] Done: Slice 7, sprites, temporary animations, lighting, and weather-like visual effects.
  - Design spec: `docs/superpowers/specs/2026-05-08-sve-slice-7-visual-effects-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-08-sve-slice-7-visual-effects.md`.
  - SVE pressure: temporary animated sprites, custom cauldron effects, map lighting changes, recolors, mist effects, and location-specific ambience.
  - Frobby goal: expose enough render/state metadata to assert animated sprites and lighting effects without brittle whole-screen diffs.
  - Done: `state.visual_effects`, runner-side `wait.visual_effects`, DSL access, and SVE scenario 09 against Crimson Badlands sandstorm temporary sprites (`Custom_CrimsonBadlands` / `SandstormEffect`).

- [x] Done: Slice 8, combat, monster lifecycle, drops, and hazards.
  - Design spec: `docs/superpowers/specs/2026-05-09-sve-slice-8-combat-monster-lifecycle-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-09-sve-slice-8-combat-monster-lifecycle.md`.
  - SVE pressure: deterministic custom-location monster spawns plus combat/damage state.
  - Frobby goal: player-like attack action, health-delta waits, zero-match waits, and a path toward later death/drop/hazard checks.
  - Done: `combat.attack` selects/faces a melee weapon through Stardew's native begin-use swing path, runner-side repeats stay outside the harness RPC, `wait.location_content` supports numeric health comparisons, and SVE scenario 12 (`sve_combat_monster_damage`) proves a deterministic corrupt mummy guard takes player-like melee damage.

- [x] Done: Slice 9, combat lifecycle, drops, and player hazards.
  - Design spec: `docs/superpowers/specs/2026-05-10-sve-slice-9-combat-lifecycle-drops-hazards-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-10-sve-slice-9-combat-lifecycle-drops-hazards.md`.
  - SVE pressure: custom monster packs, disabled/high-health attack patches, custom dungeon areas, and combat outcomes that should be observable beyond a single health delta.
  - Frobby goal: prove monster death/removal, dropped debris or loot, player health/hazard deltas, and disabled-contact behavior through neutral world-state tools.
  - Done: `state.location.debris`, debris-aware `wait.location_content`, runner-side `wait.player`, selector-based combat retargeting, and SVE Slice 9 scenarios (`sve_combat_lifecycle_debris`, `sve_passive_shadow_combat_state`) verify combat lifecycle outcomes beyond a single health delta.
  - Follow-up candidate: add neutral monster instance identity or binding so JSON scenarios can prove a specific moving monster was removed, and add stronger debris attribution when the location has pre-existing debris.

- [x] Done: Slice 10, special orders, quest state, and drop boxes.
  - Design spec: `docs/superpowers/specs/2026-05-10-sve-slice-10-special-orders-drop-boxes-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-10-sve-slice-10-special-orders-drop-boxes.md`.
  - SVE pressure: many event-gated special orders, map drop boxes, and long-running collection objectives.
  - Frobby goal: inspect active special orders, objective progress, drop box state, deposit flows, and completion/reward flags without encoding SVE order IDs in Frobby.
  - Done: `state.special_orders`, runner-side `wait.special_order`, neutral `drop_box.deposit`, and SVE scenario 15 verify runtime special-order activation and donation progress.

## Next Capability Backlog

- [x] Done: Slice 11, fishing tables and deterministic catch sampling.
  - Design spec: `docs/superpowers/specs/2026-05-11-sve-slice-11-fishing-tables-catch-sampling-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-11-sve-slice-11-fishing-tables-catch-sampling.md`.
  - SVE pressure: custom fish, custom fish areas, alternate farm fishing tables, and patched desert fishing rewards.
  - Frobby goal: query effective fish tables for a location/tile/time/weather context and sample deterministic catch outcomes without requiring the full fishing minigame.
  - Done: `state.fishing_context`, `state.fishing_table`, `fishing.sample_catch`, runner JSON assertions for fishing RPC results, and SVE scenario 16 (`sve_fishing_core`) verified headlessly.
  - Follow-up candidate: add a second SVE proof against an alternate farm or late-game custom fishing area once Slice 15 provides isolated alternate farm pack runs.

- [x] Done: Slice 12, buffs, swimming, and timed player state.
  - Design spec: `docs/superpowers/specs/2026-05-12-sve-slice-12-player-effects-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-12-sve-slice-12-player-effects.md`.
  - SVE pressure: custom hot spring and swimming areas that apply timed buffs based on save/day state.
  - Frobby goal: inspect active player buffs/effects, swimming or bathing state, and wait for timed state changes.
  - Done: `state.player` transient-state fields and active buff summaries, `player.set_transient_state`, effect-aware `wait.player`, and SVE scenario 17 (`sve_player_effects_swim_buff`) verified headlessly.

- [x] Done: Slice 13, object, chest, and buried reward interactions.
  - Design spec: `docs/superpowers/specs/2026-05-12-sve-slice-13-object-interactions-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-12-sve-slice-13-object-interactions.md`.
  - SVE pressure: piggy bank behavior, secret-note buried rewards, relocated festival chests, and patched object interactions.
  - Frobby goal: place or inspect objects, big craftables, chests, item debris, mail flags, and interaction side effects.
  - Done: `world.place_object`, richer `state.location.objects` metadata, object-aware `wait.location_content` filters, and SVE scenario 18 (`sve_object_piggy_bank_interaction`) verified headlessly against SVE's Golden Piggy Bank patched object behavior.
  - Follow-up candidate: chest content summaries for festival/runtime storage and hoe/dig support for Secret Note buried rewards.

- [x] Done: Slice 14, festivals, movie theater, and special map variants.
  - Design spec: `docs/superpowers/specs/2026-05-13-sve-slice-14-spirit-eve-chest-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-13-sve-slice-14-spirit-eve-chest.md`.
  - SVE pressure: custom festival maps, grange judging patches, Spirit's Eve chest edits, and movie theater NPC behavior.
  - Frobby goal: set up festival/theater contexts, inspect event or festival state, interact with festival shops/chests/NPCs, and assert variant-specific content.
  - Done: neutral container item projection, contained-item waits, `festival.start`, `wait.event_active.is_festival`, and SVE scenario 19 (`sve_spirit_eve_chest`) verified headlessly against SVE's Spirit's Eve Golden Pumpkin chest behavior.
  - Follow-up candidates: movie theater NPC interaction coverage, grange judging assertions, festival shops, and passive festival map variants.

- [x] Done: Slice 15, config packs and alternate farm variants.
  - SVE pressure: Grandpa's Farm, Immersive Farm 2 Remastered, Frontier Farm, low-memory options, and config-gated map/content changes.
  - Frobby goal: run tests against isolated mod/config sets, cache shared dependencies, and assert alternate farm registration and runtime map/content differences.
  - Candidate SVE proof: execute the same neutral location/content assertions against a selected alternate farm pack in an isolated dependency cache.
  - Design spec: `docs/superpowers/specs/2026-05-14-sve-slice-15-config-pack-profiles-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-14-sve-slice-15-config-pack-profiles.md`.
  - Done: repo profiles, inherited profile resolution, profile-specific test Mods caches, scenario `profile` selection, config overlays, profile report metadata, and SVE scenario 20 against Grandpa's Farm.
  - Follow-up candidates: add Immersive Farm 2 Remastered and Frontier Farm profiles, and add config-overlay proofs for low-memory or bridge layout variants.

- [x] Done: Slice 16, late-game unlocks and trigger actions.
  - SVE pressure: event/mail-gated regions, minecart or bridge unlocks, trigger actions, shrines, and map mutations over progression.
  - Frobby goal: seed progression state, observe trigger-action effects, assert map/action changes, and verify unlocks across day/event boundaries.
  - Design spec: `docs/superpowers/specs/2026-05-15-sve-slice-16-late-game-unlocks-trigger-actions-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-15-sve-slice-16-late-game-unlocks-trigger-actions.md`.
  - Done: `state.player.mail_for_tomorrow`, progression-aware `wait.player` filters for `mail_received`, `mail_for_tomorrow`, and `event_seen`, absent-array assertions, vanilla `DayEnding`/`DayStarted` trigger-action raises in `time.next_day`, and SVE scenarios 21-23 covering LocationChanged trigger actions, DayEnding mail scheduling, and Enchanted Grove map-action unlocks.
  - Follow-up candidates: Frontier Farm minecart/bridge/desert shortcut coverage once farm-type fixtures exist; direct trigger-action diagnostics if future mods need richer trigger introspection.

- [x] Done: Slice 17, alternate farm fixtures and Frontier Farm shortcut coverage.
  - SVE pressure: Frontier Farm profile coverage requires the active save to resolve as an additional farm type before Content Patcher `FarmType: FrontierFarm` conditions and instant shortcut config patches can be proven.
  - Frobby goal: stage neutral save overrides for modded farm types without mutating source fixtures or assuming SVE ids.
  - Design spec: `docs/superpowers/specs/2026-05-15-sve-slice-17-frontier-farm-fixtures-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-15-sve-slice-17-frontier-farm-fixtures.md`.
  - Done: scenario `save_overrides.farm_type` stages derived fixture copies, source fixtures stay unchanged, and SVE scenarios 24-25 prove Frontier Farm profile loading plus instant bridge/desert shortcut runtime map changes.

- [x] Done: Slice 18, Hoe/dig tool-use support for buried rewards.
  - SVE pressure: SVE relocates Secret Note #18's Desert buried reward through a Harmony patch on Stardew's buried-item check path.
  - Frobby goal: add neutral player-like tool-use support, starting with Hoe, plus secret-note seen-state setup/projection so mods can validate buried rewards without direct state shortcuts.
  - Design spec: `docs/superpowers/specs/2026-05-19-sve-slice-18-hoe-dig-tool-use-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-19-sve-slice-18-hoe-dig-tool-use.md`.
  - Done: `world.use_tool`, `player.add_secret_note_seen`, `state.player.secret_notes_seen`, runner-side `wait.player.secret_note_seen`, numeric `state` contains assertions, DSL wrappers, and docs.
  - Verified: SVE scenario 26 marks Secret Note #18 seen, hoes tile `(9,43)` in the Desert, then asserts `SecretNote18_done` mail and item debris `127` through the repo-local wrapper.

- [x] Done: Slice 19, vanilla-first Combat Lab for monster identity and lifecycle hardening.
  - SVE pressure: existing combat scenarios can prove matching monster state changes, but crowded or moving combat locations make it hard to prove a specific monster instance was removed.
  - Frobby goal: add a neutral test-only combat dev room that can reset a clean arena, spawn vanilla monsters, assign stable run-local monster identities, and let scenarios attack/wait by identity or lab label.
  - Design spec: `docs/superpowers/specs/2026-05-19-sve-slice-19-combat-lab-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-19-sve-slice-19-combat-lab.md`.
  - Done: `combat_lab.reset`, `combat_lab.spawn_monster`, run-local monster identity fields, runner target/wait filters by `monster_id` and `label`, DSL helpers, docs, and SVE scenario 27.
  - Verified: SVE scenario 27 resets `Frobby_CombatLab`, spawns a vanilla `GreenSlime`, attacks by lab label, and waits for that exact monster to be removed.
  - Follow-up candidate: add mod monster support after researching stable SVE custom monster construction or relocation.

- [x] Done: Slice 20, relocate mod-spawned monsters into the Combat Lab.
  - SVE pressure: SVE/FTM monsters carry runtime mod settings that Frobby should not recreate directly.
  - Frobby goal: move exactly one already-spawned runtime monster into `Frobby_CombatLab`, assign a run-local identity/label, and test attack/removal there.
  - Design spec: `docs/superpowers/specs/2026-05-21-sve-slice-20-mod-monster-relocation-combat-lab-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-21-sve-slice-20-mod-monster-relocation-combat-lab.md`.
  - Done: `combat_lab.relocate_monster`, neutral monster match criteria, relocated identity semantics with `spawned_by_frobby: false`, DSL helper, tile-state synchronization for relocated runtime monsters, overlap-tolerant target combat, docs, and SVE scenario 28.
  - Verified: SVE scenario 28 lets FTM spawn the fixed Crimson Badlands `ShadowShaman` sentry at `(22,144)`, relocates that exact runtime monster into `Frobby_CombatLab`, attacks by lab label, and waits for the relocated instance to be removed.
  - Follow-up completed in Slice 21: corrupt mummy cleanup now uses neutral explosion support after observing the monster's downed/revive lifecycle state.

- [x] Done: Slice 21, neutral explosion support.
  - SVE pressure: mummy-style monsters and object/terrain effects can require Stardew-native explosion semantics rather than direct deletion or visual-only effects.
  - Frobby goal: add generic `world.explode_tile` so tests can trigger native explosion behavior at a tile without bomb inventory, placement, or fuse timing.
  - Design spec: `docs/superpowers/specs/2026-05-21-sve-slice-21-neutral-explosion-rpc-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-21-sve-slice-21-neutral-explosion-rpc.md`.
  - Done: protocol models, harness handler, runner labels, DSL helper, `damage_amount`, optional monster `revive_timer` projection/waits, docs, and SVE scenario 29.
  - Verified: headless SVE scenario 29 proved corrupt-mummy cleanup in `Frobby_CombatLab`; scenarios 27 and 28 were rerun as adjacent Combat Lab regressions.
  - Follow-up moved to Slice 22: player-like bomb placement and fuse timing.

- [x] Done: Slice 22, player-like inventory object placement and bomb fuse flow.
  - SVE pressure: direct explosions prove cleanup semantics, but mod UI/testing also needs the player-like path where an inventory object is placed, ticks naturally, and produces game-state effects.
  - Frobby goal: add generic `world.place_inventory_object` plus timed object observation such as `minutes_until_ready`, without adding bomb-specific or SVE-specific framework code.
  - Design spec: `docs/superpowers/specs/2026-05-21-sve-slice-22-player-like-bomb-placement-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-22-sve-slice-22-player-like-bomb-placement.md`.
  - Done: protocol models, harness handler, runner label, DSL helper, object `minutes_until_ready` projection/waits, docs, and SVE scenario 30.
  - Verified: headless SVE scenario 30 placed a real inventory bomb in `Frobby_CombatLab`, waited for Stardew's vanilla bomb fuse sprite, and validated corrupt-mummy removal without calling `world.explode_tile`.
  - Verified: adjacent Combat Lab regression scenarios 27, 28, and 29 still pass headless after the placement slice.
  - Follow-up candidate: input-level hotbar/click placement after semantic inventory-object placement is stable.

- [x] Done: Slice 23, input-level hotbar selection and gameplay tile click.
  - SVE pressure: semantic inventory-object placement proves object behavior, but mod UI testing also needs player-real selected-item click paths that do not bypass active object selection or gameplay click hooks.
  - Frobby goal: add neutral `player.select_item` and `input.click_tile` RPCs, route tile clicks through Stardew's gameplay use/action paths, and prove click-based bomb placement against the existing Combat Lab corrupt-mummy cleanup scenario.
  - Design spec: `docs/superpowers/specs/2026-05-23-sve-slice-23-input-tile-click-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-23-sve-slice-23-input-tile-click.md`.
  - Done: protocol models, harness handlers, runner label/autocapture, DSL wrappers, right/action tile-click support, `wait.player` movement-state filters, docs, and SVE scenario 31.
  - Verified: headless SVE scenario 31 selected a real vanilla bomb, waited for player control after combat, right-clicked tile `(9,9)` in `Frobby_CombatLab`, observed Stardew's fuse sprite, and verified corrupt-mummy cleanup. Adjacent scenarios 30, 29, 28, and 27 also passed headless.

- [x] Done: Slice 24, active festival actor interaction.
  - SVE pressure: festival actors can live inside active event state instead of `currentLocation.characters`, so ordinary NPC interaction coverage can miss modded festival dialogue.
  - Frobby goal: add neutral event actor waits and let `world.interact_npc` fall back to active event actors without changing ordinary NPC priority.
  - Design spec: `docs/superpowers/specs/2026-05-24-sve-slice-24-festival-actor-interaction-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-24-sve-slice-24-festival-actor-interaction.md`.
  - Done: `wait.event_active.actor_name` plus optional actor tile filters, event actor names in timeout diagnostics, active-event fallback for `world.interact_npc`, docs, and SVE scenario 32.
  - Verified: headless SVE scenario 32 entered Spirit's Eve, waited for the active Wizard festival actor, interacted through `world.interact_npc`, and observed his dialogue.
  - Follow-up candidates: movie theater NPC setup, grange judging command progression, and festival shop UI/purchase flows.

- [x] Done: Slice 25, festival shop UI and purchase flows.
  - SVE pressure: festival shops live inside active festival events, are opened by map tile actions, and can include Content Patcher shop edits for ordinary-gold and alternate-currency festival shops.
  - Frobby goal: let tests open a live festival `ShopMenu` through a player-like or map-action path, inspect the active shop, purchase an item, and assert inventory/money state without SVE-specific code.
  - Design spec: `docs/superpowers/specs/2026-05-24-sve-slice-25-festival-shop-flow-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-24-sve-slice-25-festival-shop-flow.md`.
  - Done: `input.click_tile.allow_event_input` opt-in for player-controlled event/festival maps, docs, and SVE scenario 33 against the Flower Dance festival shop.
  - Verified: headless SVE scenario 33 entered the Flower Dance, proved the active festival map exposes `Shop Festival_FlowerDance_Pierre`, opened the same data-backed shop through `shop.open`, bought SVE decorative tulips, and verified money/inventory state. Adjacent festival scenarios 19 and 32 still pass.
  - Caveat: direct `world.interact_tile_action` and `input.click_tile.allow_event_input` did not leave the event-owned Flower Dance shop menu open in live SDV; the stable neutral flow is map-action discovery plus `shop.open` for the discovered shop ID.
  - Follow-up candidates: menu-item click purchasing inside `ShopMenu`, movie theater NPC setup, and grange judging command progression.

- [x] Done: Slice 26, alternate shop currency handling.
  - SVE pressure: the Stardew Valley Fair star-token shop uses a non-gold currency, so tests need to inspect, seed, spend, and assert the active shop currency without assuming player money.
  - Frobby goal: expose active shop currency metadata and balances, set supported shop currencies through a neutral player RPC, and make purchases debit the same currency Stardew uses for the active `ShopMenu`.
  - Done: `state.shop.currency`, `state.shop.currency_name`, `state.shop.currency_balance`, `player.set_shop_currency`, star-token support, alternate-currency debit in `shop.purchase`, and docs.
  - Verified: headless SVE scenario 34 entered the Stardew Valley Fair, opened `Festival_StardewValleyFair_StarTokens`, set the Fair star-token balance to Stardew's applied clamp of `9999`, bought SVE Furniture Catalogue 2, verified star tokens dropped to `0`, and verified gold was unchanged.
  - Follow-up candidate: menu-item click purchasing inside `ShopMenu` so tests can validate visible purchase controls instead of only semantic `shop.purchase`.

- [x] Done: Slice 27, menu-item click purchasing inside `ShopMenu`.
  - SVE pressure: previous shop slices can inspect and semantically purchase from shops, but real mod UI testing should also prove the visible `ShopMenu` row/click path works for ordinary and alternate-currency shops.
  - Frobby goal: add a neutral click-based purchase action for active shop menus that can target an item by id/display name, scroll or reveal it when needed, click the visible purchase region, and report enough bounds/currency details for screenshots and assertions.
  - Done: `shop.click_purchase` targets a live `ShopMenu` row by item id/display name, scrolls/reveals the row, clicks it through Stardew's visible menu path, deposits any resulting `ShopMenu.heldItem` into the first empty menu inventory slot, and reports row/currency/deposit metadata.
  - Verified: headless SVE scenario 35 buys SVE decorative tulips from the Flower Dance shop through the visible row click path and asserts gold plus inventory.

## Slice 1 Planning: Custom Locations, Maps, Warps, And Tile Actions

### Current Frobby Surface

Available now:
- `player.warp` queues a warp to a named location and tile.
- `state.locations` lists loaded runtime locations.
- `state.location` returns current or named location with `name`, `unique_name`, map dimensions, warps, NPCs, objects, furniture, and terrain.
- `state.map_tile` returns tile/layer metadata and raw map properties.
- `state.tile_actions` lists nearby `Action` and `TouchAction` candidates.
- `wait.location` waits for player location/tile transitions to settle.
- `world.interact_tile` interacts with furniture or placed objects in the current location.
- `world.interact_tile_action` executes map `Action` properties and simulates stepping onto `TouchAction` tiles before invoking Stardew's direct touch-action path.
- `draw.*`, `bitmap.capture`, `screenshot.*`, and `freeze.*` can verify the rendered outcome once a location is loaded.

Observed gaps for SVE-style map testing:
- Slice 1's core map and tile-action coverage is implemented. Future map work should be driven by more specialized slices, such as spawn/conditional content, custom items, or visual effects.

### Recommended Approach

Use a state-first map introspection slice, then layer one SVE scenario on top.

This keeps Frobby neutral and gives future mod tests durable primitives:
- `state.locations` returns loaded location summaries.
- `state.location` grows map metadata and warp summaries.
- `state.map_tile` returns tile/layer metadata for one coordinate.
- `world.interact_tile_action` or an enhanced `world.interact_tile` can trigger map tile actions in the current location after tile metadata is observable.

Alternatives considered:
- Image-first smoke only: easy to add, but it would not tell us whether the map, tile properties, or warp metadata are correct.
- SVE-only scenario research first: useful for examples, but risks hiding the missing Frobby primitives behind one-off coordinates.

### Candidate Frobby Capabilities

1. `state.locations`
   - Response: list of loaded locations with `name`, `unique_name`, `is_outdoors`, optional `map_width`, `map_height`, and optional `context_id`.
   - Tests: prove custom CP locations are visible after SVE loads, starting with known names such as `Custom_TownEast`, `Custom_GrandpasShed`, and `Custom_EnchantedGrove`.

2. Expanded `state.location`
   - Add non-breaking fields:
     - `unique_name`
     - `map_width`
     - `map_height`
     - `map_asset`
     - `warps`: normalized destination summaries from `GameLocation.warps`
   - Keep existing fields stable so current Starberg tests do not change.

3. `state.map_tile`
   - Request: `location`, `x`, `y`, and optional `layers`.
   - Response: layer entries with tile index, tilesheet id, and tile properties.
   - Needed properties: `Action`, `TouchAction`, `Passable`, `NoSpawn`, `Water`, and any raw key/value map properties present at the tile.

4. Tile action execution
   - Preferred action name: `world.interact_tile_action`.
   - Request: `x`, `y`, optional `location`, optional `just_checking_for_activity`.
   - Behavior: run the same map tile action path a player would hit when activating or stepping onto a tile, returning whether it handled and any resulting location after the warp settles if feasible.
   - This should come after `state.map_tile` so we can debug failures with metadata instead of guessing coordinates.

5. Runner convenience wait
   - Add a scenario-level helper/action for "wait until player is in location X at tile Y or timeout".
   - Could be implemented entirely in Runner using existing `state.player` polling before requiring a harness RPC.

### Candidate SVE Scenarios

1. `01-sve-core-loads` stays as the baseline mod-load smoke.

2. `02-sve-custom-locations-register`
   - Assert `state.locations` contains key SVE locations:
     - `Custom_TownEast`
     - `Custom_GrandpasShed`
     - `Custom_EnchantedGrove`
   - Assert each has nonzero map dimensions.

3. `03-sve-custom-location-warp`
   - Warp directly to `Custom_TownEast`.
   - Wait until `state.player.location == Custom_TownEast`.
   - Capture under freeze.
   - Assert `state.location.name` and map dimensions.

4. `04-sve-map-tile-properties`
   - Inspect one known SVE map tile with a warp or action property.
   - Assert the tile property key/value is visible through `state.map_tile`.
   - Exact coordinate should be selected during implementation by reading SVE map metadata or by probing a loaded map.

5. `05-sve-tile-action-warp`
   - Move/warp to the source tile for a known `TouchAction` or `Action` warp.
   - Trigger `world.interact_tile_action`.
   - Wait until the destination location is reached.
   - Assert current location, player tile, and final screenshot.

### First Implementation Target

Start with `state.locations` and expanded `state.location` metadata before adding tile action execution.

Reasoning:
- It gives immediate coverage for SVE custom map registration.
- It is low risk for existing Starberg scenarios because it only adds response fields.
- It creates a debugging foundation for choosing reliable tile-action coordinates later.

### Open Questions

- Should `state.locations` include all loaded `Game1.locations`, only locations currently in memory, or both with a `loaded` flag if discoverable?
- Should map asset names come from SMAPI/content metadata when available, or should Frobby initially expose only runtime map dimensions/properties?
- Should tile-action execution be a separate RPC (`world.interact_tile_action`) or an option on `world.interact_tile`?
