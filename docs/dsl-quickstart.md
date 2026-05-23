# C# DSL Quickstart

Write scenarios as xUnit test methods instead of hand-authoring `*.test.json`. The DSL
wraps the same JSON-RPC surface the CLI runner uses, so anything you can express in a
JSON scenario you can express in C#.

## 1. Install

In your mod's test project:

```bash
dotnet add package SdvTestFramework.Runner.Dsl
```

You also need the CLI tool (which provides SDV launch + harness deployment):

```bash
dotnet tool install -g SdvTestFramework.Cli
```

(For development against the source tree, you can still use a `<ProjectReference>` —
see `docs/developer-setup.md`.)

## 2. Write a test

```csharp
using SdvTestFramework.Runner.Dsl;
using Xunit;

[Collection("SDV")]
public class ShopMenuTests
{
    [Fact]
    [Scenario(fixture: "m0spike_436515781")]
    public async Task Warp_ToShop_MenuOpens()
    {
        await Player.Warp("SeedShop", 4, 19);
        await Player.SetMoney(5000);
        await Draw.Arm();
        await Wait.Ms(500);
        await Freeze.Begin();

        var events = await Draw.Snapshot();
        Assert.Contains(events.Events, e => e.TextureAsset == "LooseSprites/Cursors");

        var player = await State.Player();
        Assert.Equal(5000, player.Money);
    }
}
```

The `SdvCollection` + `SdvFixture` types are provided by the DSL package — `[Collection("SDV")]` automatically picks up the fixture. No per-assembly boilerplate class needed unless you want to add your own collection members.

Note: you need both `[Fact]` and `[Scenario]`. `[Fact]` tells xUnit to run the method;
`[Scenario]` tells the DSL to wrap it in `scenario.begin`/`scenario.end`. A combined
`[ScenarioFact]` is on the roadmap.

## 3. Run

```bash
dotnet test
```

By default the collection fixture launches one SDV subprocess per `dotnet test`
invocation and reuses it across every `[Collection("SDV")]` test in the assembly.

Use repo profiles for alternate mod/config packs. Keep pack-specific paths in
`sdv-test.config.json`; keep assertions in scenario JSON.

Environment knobs:

- `SDV_MODS_PATH` — override the mods directory the harness is deployed to (default:
  `~/.cache/sdv-test-framework/mods`).
- `DSL_SKIP_SDV_LAUNCH=1` — bypass SDV launch entirely. The fixture becomes a no-op and
  any `[Scenario]` test fails with "SdvTestSession.Current is not initialized." Set this
  in CI when you have DSL tests but no display/SDV available (they'll skip/fail cleanly
  rather than hang on SDV startup).

## Facet reference

- `Player.Warp(location, x, y)` / `SetMoney(amount)` / `AddMail(id)` / `AddEventSeen(id)` / `AddSecretNoteSeen(id)` / `GiveItem(id, count)`
- `Time.Advance(minutes)`
- `World.SetWeather(type)` / `InteractTileAction(x?, y?, location?, property?, layers?)` / `UseTool(tool, x, y, location?, facing?, power?)` / `ExplodeTile(x, y, location?, radius?, damagePlayer?, damageAmount?)`
- `Input.Key(key)` / `Text(text)` / `Click(x, y)` / `ClickText(text)` / `Hover(x, y)` / `HoverText(text)`
- `Fixture.Load(name)`
- `Freeze.Begin()` / `End()` / `Status()`
- `Draw.Arm()` / `Disarm()` / `Snapshot()` / `Find(filter)` / `AssertContains(filter)` / `AssertNotContains(filter)`
- `State.Player()` / `Time()` / `Location(name?)` / `Locations()` / `MapTile(location?, x?, y?, layers?)` / `TileActions(location?, x?, y?, radius?, layers?, properties?)` / `VisualEffects(location?)` / `Npc(name)` / `Menu()` / `Shop()` / `Event()` / `Mods()`
- `Bitmap.Capture(region?)`
- `Screenshot.Capture(name)`
- `Wait.Ms(ms)`

Location/map helpers are useful for complex Content Patcher or code mods with custom
areas:

```csharp
var locations = await State.Locations();
Assert.Contains(locations.Locations, l => l.Name == "ExampleTownEast");

await Player.Warp("ExampleTownEast", 10, 20);
await Wait.Ms(500);

var location = await State.Location();
Assert.NotEqual(0, location.MapWidth);

var tile = await State.MapTile(layers: new[] { "Back" });
Assert.Contains(tile.Layers, l => l.Name == "Back");

var actions = await State.TileActions(
    location: "ExampleVineyard",
    x: 56,
    y: 48,
    layers: new[] { "Back" },
    properties: new[] { "TouchAction" });
Assert.Contains(actions.Actions, a => a.Value == "LoadMap Town 50 114 0");

await World.InteractTileAction(
    location: "ExampleVineyard",
    x: 56,
    y: 48,
    property: "TouchAction",
    layers: new[] { "Back" });
```

Visual-effect state is useful for proving that runtime temporary sprites,
lighting, or weather debris exist before taking final render evidence:

```csharp
var effects = await State.VisualEffects("Example.VisualLocation");
Assert.Contains(effects.TemporarySprites, s =>
    s.TextureAsset == "ExampleMod/Visuals/Effects");
```

`world.interact_tile_action` is for map-defined `Action` and `TouchAction`
properties. For `TouchAction`, Frobby moves the farmer onto the tile before
calling Stardew's direct touch-action path; use JSON `wait.location` or DSL
`Wait.Ms` after actions that warp or resolve on the next game tick.
`world.interact_tile` still targets furniture and placed objects.

JSON runner scenarios can observe cutscenes and other Stardew events with
`state.event` and event waits:

```json
{ "action": "event.start", "args": { "id": "520702", "location": "BusStop" } },
{ "action": "wait.event_active", "args": { "id": "520702", "timeout_ms": 10000 } },
{ "action": "state.assert", "args": { "expr": "state.event.actors contains name 'Krobus'" } },
{ "action": "screenshot.capture_next_frame", "args": { "name": "active-event" } },
{ "action": "event.skip", "args": {} },
{ "action": "wait.event_complete", "args": { "id": "520702", "timeout_ms": 30000 } }
```

State array assertions support both presence and absence checks. Use
`state.<method>.<array> contains '<value>'` for string arrays,
`state.<method>.<array> contains 18` for numeric arrays, and
`state.<method>.<array> contains <field> '<value>'` for object arrays. Insert
`not` before `contains` to assert the value is absent.

Use `screenshot.capture_next_frame` for active events because `freeze.begin`
intentionally rejects cutscenes while `Game1.eventUp` is true.

Active festival scenarios can enter the current date's festival and assert
container contents without coordinate clicking through the map entrance:

```json
{ "action": "time.set", "args": { "time": 2200, "day": 27, "season": "fall", "year": 1 } },
{ "action": "festival.start", "args": { "location": "Town" } },
{ "action": "wait.event_active", "args": { "location": "Town", "is_festival": true } },
{
  "action": "wait.location_content",
  "args": {
    "location": "Town",
    "collection": "objects",
    "runtime_type": "Chest",
    "x": 63,
    "y": 16,
    "contains_item_qualified_id": "(O)373",
    "contains_item_stack": 1
  }
}
```

For Stardew-native dialogue choices, use `wait.menu` and choice-targeted
`event.advance` instead of coordinates:

```json
{ "action": "wait.menu", "args": { "choice_text": "Pet Dusty", "timeout_ms": 45000 } },
{ "action": "state.assert", "args": { "expr": "state.event.choices contains text 'Pet Dusty'" } },
{ "action": "event.advance", "args": { "choice_text": "Pet Dusty", "timeout_ms": 10000 } },
{ "action": "wait.menu", "args": { "text": "!!!", "ready": true, "timeout_ms": 45000 } },
{ "action": "event.advance", "args": { "repeat": 2, "interval_ms": 150 } },
{ "action": "wait.event_complete", "args": { "id": "5532011", "timeout_ms": 30000 } }
```

`wait.menu.ready` uses dialogue progress telemetry when Stardew exposes it, which
keeps event advancement from clicking before the current line finishes typing.

JSON runner scenarios can assert custom NPC relationship and schedule state with
parameterized `state.assert` calls and runner-side NPC waits:

```json
{ "action": "player.set_friendship", "args": { "npc": "Riley", "points": 1000 } },
{ "action": "world.warp_npc", "args": { "name": "Riley", "location": "ExampleVineyard", "x": 20, "y": 32 } },
{ "action": "wait.npc_location", "args": { "name": "Riley", "location": "ExampleVineyard", "timeout_ms": 10000 } },
{ "action": "state.assert", "args": {
  "params": { "name": "Riley" },
  "expr": "state.npc.hearts == 4",
  "message": "Riley should be at least four hearts after setup"
} }
```

Use `state.npcs` for discovery/count checks and `state.npc` when a scenario needs
a focused assertion for one NPC. These primitives are mod-neutral; the same shape
works for vanilla villagers and Content Patcher-added NPCs. `wait.location` and
`wait.npc_location` wait for SDV warp/fade transitions to settle before they
return, which keeps follow-up assertions and screenshots out of black transition
frames.

Custom shop and inventory scenarios can assert runtime shop entries and purchased
items by qualified id:

```json
{ "action": "player.set_money", "args": { "amount": 10000 } },
{ "action": "shop.open", "args": { "shop_id": "ExampleShop", "force_open": true } },
{ "action": "state.assert", "args": {
  "expr": "state.shop.items contains qualified_id '(O)ExampleMod.CustomItem'",
  "message": "Example shop should sell the custom item"
} },
{ "action": "shop.purchase", "args": { "item_id": "(O)ExampleMod.CustomItem", "count": 1 } },
{ "action": "state.assert", "args": {
  "expr": "state.player.items contains qualified_id '(O)ExampleMod.CustomItem'",
  "message": "Purchased custom item should be visible in player inventory"
} }
```

Use `State.Shop()` in C# DSL tests when a test needs to inspect the active shop
snapshot directly after a click flow or setup helper.

Special-order scenarios should prove the active order and donation objective
before mutating it:

```json
{
  "action": "wait.special_order",
  "args": {
    "collection": "active",
    "key": "ExampleOrder",
    "objective_type": "Donate",
    "drop_box": "ExampleDropBox",
    "current_count": 0,
    "timeout_ms": 15000
  }
}
```

Then use `drop_box.deposit` to deposit from player inventory into the matching
runtime objective:

```json
{
  "action": "drop_box.deposit",
  "args": {
    "order_key": "ExampleOrder",
    "drop_box": "ExampleDropBox",
    "qualified_id": "(O)388",
    "count": 5
  }
}
```

Follow with another `wait.special_order` using `current_count_gte` to prove
progress. Keep order keys, event prerequisites, and item ids in the mod repo
scenario, not in reusable Frobby code.

For spawned world content, prefer `wait.location_content` over fixed sleeps:

```json
{
  "action": "wait.location_content",
  "args": {
    "location": "ExampleForestEdge",
    "collection": "resource_clumps",
    "name": "Log",
    "min_count": 2,
    "timeout_ms": 10000,
    "poll_ms": 100
  }
}
```

This is mod-neutral: Frobby observes the runtime location state and does not call
Farm Type Manager or parse its content packs.

For chests and chest-like objects, the same wait can require an item inside the
container with `contains_item_*` filters. The object itself still needs to match
the normal object filters, such as tile or runtime type.

Placed object interactions can be staged without touching the player's inventory:

```json
{
  "action": "world.place_object",
  "args": {
    "id": "(BC)Example.Mod_Golden_Piggy_Bank",
    "location": "FarmHouse",
    "x": 8,
    "y": 9,
    "remove_existing": true
  }
},
{
  "action": "wait.location_content",
  "args": {
    "location": "FarmHouse",
    "collection": "objects",
    "qualified_id": "(BC)Example.Mod_Golden_Piggy_Bank",
    "big_craftable": true,
    "x": 8,
    "y": 9,
    "min_count": 1
  }
},
{
  "action": "world.interact_tile",
  "args": { "x": 8, "y": 9 }
}
```

Use `world.place_inventory_object` when the test needs player-like placement
from inventory rather than direct setup:

```json
{ "action": "player.give_item", "args": { "id": "(O)287", "count": 1 } },
{
  "action": "world.place_inventory_object",
  "args": {
    "id": "(O)287",
    "location": "Frobby_CombatLab",
    "x": 9,
    "y": 8
  }
},
{
  "action": "wait.location_content",
  "args": {
    "location": "Frobby_CombatLab",
    "collection": "objects",
    "qualified_id": "(O)287",
    "minutes_until_ready_gt": 0,
    "min_count": 1,
    "timeout_ms": 5000,
    "poll_ms": 100
  }
}
```

In C# DSL tests, call:

```csharp
await Player.GiveItem("(O)287");
var placed = await World.PlaceInventoryObject("(O)287", 9, 8, location: "Frobby_CombatLab");
Assert.True(placed.Placed);
```

Transient debris and combat drops are exposed through the same wait:

```json
{
  "action": "wait.location_content",
  "args": {
    "location": "ExampleDeepCave",
    "collection": "debris",
    "qualified_id": "(O)769",
    "min_count": 1,
    "timeout_ms": 10000
  }
}
```

For combat checks, establish the monster baseline, trigger a player-like attack,
and wait for observed monster state to change:

```json
{
  "action": "wait.location_content",
  "args": {
    "location": "ExampleDeepCave",
    "collection": "monsters",
    "x": 20,
    "y": 144,
    "health": 2000,
    "max_health": 2000,
    "min_count": 1
  }
}
```

```json
{
  "action": "combat.attack",
  "args": {
    "x": 20,
    "y": 144,
    "qualified_item_id": "(W)4"
  }
}
```

For moving targets, the runner can retarget each repeated attack from current
monster state before sending the single-shot harness RPC:

```json
{
  "action": "combat.attack",
  "args": {
    "qualified_item_id": "(W)4",
    "repeat": 3,
    "delay_ticks": 10,
    "target": {
      "location": "ExampleDeepCave",
      "type": "Serpent",
      "sprite_texture": "ExampleMod/Serpent",
      "health_gt": 0
    }
  }
}
```

If a retargeted monster moves onto the player's tile, `combat.attack` falls back
to the farmer's current facing direction instead of failing the scenario.

Then wait for damage instead of sleeping:

```json
{
  "action": "wait.location_content",
  "args": {
    "location": "ExampleDeepCave",
    "collection": "monsters",
    "x": 20,
    "y": 144,
    "max_health": 2000,
    "health_lt": 2000,
    "min_count": 1
  }
}
```

For isolated combat hardening, use the Combat Lab. It creates a clean test-only
arena and lets JSON scenarios target a specific monster by lab label:

```json
{ "action": "combat_lab.reset", "args": { "player_x": 8, "player_y": 8, "warp_player": true } }
```

```json
{ "action": "combat_lab.spawn_monster", "args": { "kind": "GreenSlime", "label": "target", "x": 9, "y": 8, "health": 1 } }
```

```json
{
  "action": "combat.attack",
  "args": {
    "qualified_item_id": "(W)4",
    "repeat": 1,
    "target": { "location": "Frobby_CombatLab", "label": "target" }
  }
}
```

C# DSL tests can use `CombatLab.Reset`, `CombatLab.SpawnMonster`, and
`Combat.AttackTarget` for the same flow.

For mod-spawned monsters, let the mod create the monster first and then relocate
that exact runtime instance into the lab:

```json
{
  "action": "combat_lab.relocate_monster",
  "args": {
    "from_location": "Custom_CrimsonBadlands",
    "label": "corrupt-mummy",
    "target_x": 9,
    "target_y": 8,
    "match": {
      "x": 20,
      "y": 144,
      "sprite_texture": "Characters/Monsters/CorruptMummy"
    }
  }
}
```

The relocation action moves the existing monster object. It does not construct a
mod monster or parse mod spawn data.

Player health waits are also runner-side polling over `state.player`:

```json
{
  "action": "wait.player",
  "args": { "health_lt": 100, "timeout_ms": 10000, "poll_ms": 100 }
}
```

Player effect waits can poll for transient state and active buffs:

```json
{
  "action": "player.set_transient_state",
  "args": { "swimming": true }
},
{
  "action": "wait.player",
  "args": {
    "swimming": true,
    "buff_count_gte": 1,
    "buff_any_effect_gte": {
      "effects": ["fishing_level", "farming_level", "mining_level", "foraging_level", "attack"],
      "value": 3
    },
    "timeout_ms": 10000,
    "poll_ms": 100
  }
}
```

Player progression waits can poll received mail, pending mail, and seen events:

```json
{
  "action": "wait.player",
  "args": {
    "mail_for_tomorrow": "HenchmanMarshTonics",
    "event_seen": "1000035",
    "timeout_ms": 10000,
    "poll_ms": 100
  }
}
```

They can also poll seen secret notes after setup or a player-like world action:

```json
{ "action": "player.add_secret_note_seen", "args": { "id": 18 } },
{
  "action": "world.use_tool",
  "args": { "tool": "Hoe", "location": "Farm", "x": 21, "y": 12, "facing": "up" }
},
{
  "action": "wait.player",
  "args": { "secret_note_seen": 18, "timeout_ms": 10000, "poll_ms": 100 }
}
```

In C# DSL tests, the same RPCs are available through `Player.AddSecretNoteSeen`
and `World.UseTool`:

```csharp
await Player.AddSecretNoteSeen(18);
var result = await World.UseTool("Hoe", 21, 12, location: "Farm", facing: "up");
Assert.True(result.Invoked);
```

Use `World.ExplodeTile` when the feature depends on Stardew's native explosion
behavior, but the test does not need to prove bomb inventory, placement, or fuse
timing:

```csharp
await CombatLab.Reset(playerX: 8, playerY: 8);
var result = await World.ExplodeTile(9, 8, location: "Frobby_CombatLab", radius: 2, damageAmount: 5000);
Assert.True(result.Invoked);
```

The same runner wait can target hostile monsters with exact metadata filters:

```json
{
  "action": "wait.location_content",
  "args": {
    "location": "ExampleCombatMap",
    "collection": "monsters",
    "name": "Crystal Bat",
    "type": "CrystalBat",
    "health": 180,
    "max_health": 180,
    "damage": 32,
    "revive_timer": 0,
    "sprite_texture": "ExampleMod/Monsters/CrystalBat",
    "min_count": 1,
    "timeout_ms": 10000,
    "poll_ms": 100
  }
}
```

For monsters with a downed/revival lifecycle, scenarios can wait on the optional
`revive_timer` field with the same numeric suffixes as health, for example
`revive_timer_gt: 0` before triggering an explosion cleanup.

JSON runner scenarios can validate final runtime content assets directly with
`content.asset` assertions. These load through Stardew's live content pipeline,
so the assertion sees the result after Content Patcher patches and conditions,
not just the content pack source files:

```json
{
  "type": "content.asset",
  "asset": "Maps/ExampleTownEast",
  "asset_type": "map",
  "expr": "asset.layers contains name 'Back'",
  "message": "Town East map should load with a Back layer"
},
{
  "type": "content.asset",
  "asset": "Data/Locations",
  "asset_type": "data",
  "entry_keys": ["ExampleTownEast"],
  "expr": "asset.entries.ExampleTownEast.value.display_name == 'Town East'"
},
{
  "type": "content.asset",
  "asset": "Data/Locations",
  "asset_type": "data",
  "entry_keys": ["ExampleAncientGrove"],
  "expr": "asset.entries.ExampleAncientGrove.value.create_on_load.map_path != ''"
}
```

Expression roots are `asset.<field>`. Top-level result fields such as
`asset.exists`, `asset.kind`, and `asset.runtime_type` are available; otherwise
paths resolve against the bounded `summary` object, so `asset.layers` means
`summary.layers`. Supported operators are `==`, `!=`, and array membership like
`asset.layers contains name 'Back'`. Selected `Data/*` entries include public
scalar fields/properties and bounded nested runtime data objects, with property
names converted to snake_case.

JSON scenarios can inspect fishing tables and sample live catches:

```json
{
  "type": "state.fishing_table",
  "params": {
    "location": "Beach",
    "x": 45,
    "y": 12,
    "season": "spring",
    "time_of_day": 900
  },
  "expr": "result.candidates contains qualified_id '(O)128'",
  "message": "Beach fishing table should expose pufferfish as a candidate"
},
{
  "type": "fishing.sample_catch",
  "params": {
    "location": "Desert",
    "x": 28,
    "y": 6,
    "attempts": 10,
    "seed": 1234,
    "restore_state": true
  },
  "expr": "result.results contains display_name 'Pyramid Decal'",
  "message": "Runtime Desert sampling should exercise patched catch results"
}
```

Fishing table assertions are diagnostic and useful for authoring. Use
`fishing.sample_catch` when a scenario needs to prove the runtime catch path
under controlled seed/time/weather state.

## Error handling

RPC errors throw typed exceptions:

```csharp
try
{
    await Freeze.Begin();
}
catch (SdvGameStateInvalidException ex)
{
    // ex.Method = "freeze.begin"
    // ex.Code   = JsonRpcErrorCode.GameStateInvalid
    // ex.Message = "RPC 'freeze.begin' failed (GameStateInvalid): freeze.begin requires an active scenario..."
}
```

Subclasses: `SdvGameStateInvalidException`, `SdvInvalidParamsException`,
`SdvInternalErrorException`. Base: `SdvRpcException`.

## What's deferred

See the M3-DSL design spec
(`docs/superpowers/specs/2026-04-24-m3-csharp-dsl-design.md`) for what's out of scope:
FluentAssertions `.Should()` integration, generic menu registry
(`Wait.ForMenu<ShopMenu>`), `[ScenarioFact]` combined attribute, parallel SDV-subprocess
execution across multiple collections.

## HTML Run Reports

Every test run produces a directory at `./test-results/<run-id>/` containing:
- `index.html` — pass/fail dashboard, opens in any browser.
- `summary.json` — machine-readable run data (LLM-friendly).
- `scenarios/<name>/` — per-scenario page + step/assertion data + screenshots.

Auto-screenshots fire at `freeze.begin` and on assertion failure. Add explicit named
captures via `await Screenshot.Capture("after_my_action")` from the DSL or
`{ "action": "screenshot.capture", "args": { "name": "after_my_action" } }` in JSON.
When the screenshot should reflect a click, hover tooltip, or other input that changes the current
menu, prefer the JSON runner action
`{ "action": "screenshot.capture_next_frame", "args": { "name": "after_click", "timeout_ms": 3000 } }`;
it waits for the next rendered frame before copying the PNG into the report.

CLI flag: `sdv-test run --report-dir <path>` to override the default location, or
`--no-report` to skip generation.

Use `sdv-test run --headless` or `sdv-test run-suite --headless` on Linux to
launch SDV through `xvfb-run` so the game does not take over the active desktop
display or mouse cursor.

### Text-fit assertions

The CLI runner supports `draw.text_all_within` for UI layout guardrails. It snapshots
captured `SpriteBatch.DrawString` text, applies the optional `filter`, and fails if any
matching text bounds fall outside the required `region` rectangle.

```json
{
  "type": "draw.text_all_within",
  "filter": {
    "in_rect": [64, 78, 816, 566],
    "color_any": [[255, 214, 128, 255], [236, 229, 206, 255]]
  },
  "region": { "x": 64, "y": 78, "w": 816, "h": 566 },
  "message": "Main pane text should remain inside the menu body"
}
```

Use this for fixed UI panes, tables, button bars, and terminal/status areas where text
overflow is a regression. `min_count` defaults to `1`; set it higher when the assertion
should also prove that several expected visible text instances were captured. `draw.text_contains`
also accepts `max_count` for exact-occurrence checks, such as proving a persisted
headline was restored once rather than duplicated after reload. Text occurrence assertions
collapse repeated samples of the same nearby text, so multi-frame capture and
shadowed/multi-pass text rendering don't inflate the count.
The optional `color_any` filter is useful when the game HUD or another mod draws text
near the same screen area and the assertion should target a known UI palette.

**DSL caveat:** when running tests via `dotnet test` (the DSL path), only `summary.json` is
written today — the rich `index.html` + per-scenario reports come from the CLI runner
(`sdv-test run`). Per-test screenshots from `Screenshot.Capture` ARE saved into the run
directory in both modes.

## Diff-image on failure

When a `bitmap` assertion fails, the runner writes forensics PNGs into the per-run report
directory at `scenarios/<scenario>/diffs/assertion-NN-bitmap/`:

- `baseline.png` — the expected image.
- `capture.png` — what was actually rendered.
- `diff.png` — baseline with a bilinear-smoothed red heatmap overlaid where blocks fell
  below the SSIM tolerance.

Optional composite via `--diff-format=triptych` (CLI) or `"diff_format": "triptych"`
(per-assertion in the scenario JSON) writes a 4th `triptych.png` with all three side-by-side.
The HTML report's per-scenario page surfaces all of these in a "Failure forensics" section.

`--update-baselines` short-circuits diff generation — the capture overwrites the baseline
instead, so there's nothing to forensics.

### Bitmap diff methods

The `bitmap` assertion supports three methods via the `method` field:

- `"ssim"` (default) — perceptual structural similarity. `tolerance` is a float in (0, 1]; higher = stricter.
- `"pixel-exact"` — strict per-pixel RGB compare. `tolerance` is an integer; max per-channel delta allowed.
- `"dhash"` — perceptual difference hash. `tolerance` is an integer 0-64; max Hamming distance allowed.

Choose `pixel-exact` for UI elements that should be bit-stable; `dhash` for "vaguely the same scene" checks. SSIM is the right default for everything else.

### Tolerance tiers

`sdv-test run --tier=<generic|ci-ubuntu|self-hosted-nvidia>` selects per-method default tolerances. Useful when running the same suite across environments with different rendering determinism.

| method      | generic | ci-ubuntu | self-hosted-nvidia |
| ----------- | ------- | --------- | ------------------ |
| ssim        | 0.95    | 0.98      | 0.999              |
| pixel-exact | 5       | 2         | 0                  |
| dhash       | 5       | 3         | 1                  |

Per-assertion `tolerance` always overrides the tier default. Per-assertion `tier` field overrides the run-wide flag.

### `sdv-test baselines` subcommand

Manage bitmap baselines:
- `sdv-test baselines list [--scenarios <dir>]` — enumerate referenced baselines + presence.
- `sdv-test baselines update <path-or-glob> [--tier <n>]` — rerun with `--update-baselines`.
- `sdv-test baselines show <path>` — print PNG metadata.
- `sdv-test baselines delete <path> [--force]` — remove file (prompts unless `--force`).

### Capture cache cleanup

`sdv-test run` automatically sweeps `~/.cache/sdv-test-framework/captures/` at end of every successful invocation, deleting files older than 7 days OR outside the 5 most-recent run subdirs. Opt out with `--no-cache-cleanup`.

Manual: `sdv-test cache clean [--max-age <days>] [--keep-runs <n>] [--dry-run]`.
