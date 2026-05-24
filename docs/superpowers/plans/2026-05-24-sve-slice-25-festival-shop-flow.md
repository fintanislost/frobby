# SVE Slice 25 Festival Shop Flow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add neutral Frobby support for explicit active-event tile clicks and prove a real SVE festival shop flow against the Flower Dance shop.

**Architecture:** Keep Frobby mod-neutral by adding one opt-in `allow_event_input` request flag to `input.click_tile`; the default event guard remains unchanged. Use SVE only for repo-local proof: enter Flower Dance, discover the shop map action, open the live `ShopMenu`, buy an SVE-added decorative item, and assert player state.

**Tech Stack:** C#/.NET 10 projects in Frobby, SMAPI/Stardew Valley harness RPC, JSON `.test.json` scenarios, Stardew Valley Expanded Content Patcher data.

---

## File Structure

Frobby files:

- Modify `src/Protocol/Models/InputClickTileRequest.cs`
  - Adds `AllowEventInput` to the wire DTO. Snake-case serialization is handled by existing `ProtocolJson.Options`.
- Modify `tests/Protocol.Tests/InputClickTileSerializationTests.cs`
  - Proves `allow_event_input` deserializes and defaults to `false`.
- Modify `src/Harness/Handlers/InputClickTileHandler.cs`
  - Changes only the event-up guard: active event clicks are permitted when the caller opts in.
- Modify `tests/Harness.Tests/InputClickTileHandlerTests.cs`
  - Proves default rejection is preserved and explicit event input invokes the click path.
- Modify `src/Runner.Dsl/Input.cs`
  - Adds optional `allowEventInput` argument to the C# DSL wrapper.
- Modify `tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs`
  - Proves the DSL emits `allow_event_input`.
- Modify `docs/rpc-schema.md`
  - Documents the request flag, default behavior, and festival-use guidance.
- Modify `docs/dsl-quickstart.md`
  - Adds a short festival/event note near selected-item tile click examples.
- Modify `docs/wiki/examples.md`
  - Adds the SVE Flower Dance shop scenario as a real example.
- Modify `SVE_FROBBY_CAPABILITY_TODO.md`
  - Marks Slice 25 as done after verification.

SVE files:

- Create `tests/sdv/33-sve-flower-dance-shop-flow.test.json`
  - Repo-local proof scenario. Uses SVE IDs only in SVE scenario JSON.
- Modify `docs/FROBBY.md`
  - Documents the new SVE scenario and the neutral Frobby capabilities it demonstrates.

## Task 1: `input.click_tile` Event Opt-In

**Files:**
- Modify: `src/Protocol/Models/InputClickTileRequest.cs`
- Modify: `tests/Protocol.Tests/InputClickTileSerializationTests.cs`
- Modify: `src/Harness/Handlers/InputClickTileHandler.cs`
- Modify: `tests/Harness.Tests/InputClickTileHandlerTests.cs`

- [ ] **Step 1: Write the failing protocol tests**

Update `tests/Protocol.Tests/InputClickTileSerializationTests.cs`.

Change `Request_DeserializesSnakeCaseFields` so the JSON includes `allow_event_input`:

```csharp
var req = JsonSerializer.Deserialize<InputClickTileRequest>(
    "{\"location\":\"Frobby_CombatLab\",\"x\":9,\"y\":8,\"button\":\"left\",\"require_current_location\":false,\"screen_offset_x\":16,\"screen_offset_y\":48,\"allow_event_input\":true}",
    ProtocolJson.Options)!;
```

Add the assertion after the existing offset assertions:

```csharp
Assert.True(req.AllowEventInput);
```

In `Request_DefaultsToLeftCurrentLocationAndTileCenter`, add:

```csharp
Assert.False(req.AllowEventInput);
```

- [ ] **Step 2: Write the failing harness test**

Add this test to `tests/Harness.Tests/InputClickTileHandlerTests.cs` after `Handle_EventUp_ThrowsGameStateInvalid`:

```csharp
[Fact]
public void Handle_EventUpWithAllowEventInput_InvokesClick()
{
    var world = new FakeTileClickWorld { EventUp = true };
    var p = JsonDocument.Parse("{\"x\":9,\"y\":8,\"allow_event_input\":true}").RootElement;

    var json = InputClickTileHandler.Handle(p, world);
    var result = JsonSerializer.Deserialize<InputClickTileResult>(json, ProtocolJson.Options)!;

    Assert.True(world.ClickInvoked);
    Assert.True(result.Handled);
    Assert.Equal(9, result.Tile.X);
    Assert.Equal(8, result.Tile.Y);
}
```

- [ ] **Step 3: Run red tests**

Run from `/home/fintan/stardewRepos/frobby/sdv-test-framework`:

```bash
dotnet test tests/Protocol.Tests/ --filter InputClickTileSerializationTests
dotnet test tests/Harness.Tests/ --filter InputClickTileHandlerTests
```

Expected: protocol tests fail because `InputClickTileRequest.AllowEventInput` does not exist. Harness tests fail for the same missing property or, after adding only the property, because `input.click_tile requires !Game1.eventUp`.

- [ ] **Step 4: Add the DTO property**

Update `src/Protocol/Models/InputClickTileRequest.cs` after `RequireCurrentLocation`:

```csharp
/// <summary>Allow gameplay click delivery during active events or festivals. Defaults to false.</summary>
public bool AllowEventInput { get; set; }
```

- [ ] **Step 5: Implement the minimal handler change**

In `src/Harness/Handlers/InputClickTileHandler.cs`, replace:

```csharp
if (world.EventUp)
    throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
        "input.click_tile requires !Game1.eventUp");
```

with:

```csharp
if (world.EventUp && !req.AllowEventInput)
    throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
        "input.click_tile requires !Game1.eventUp");
```

Do not change the active-menu, warping, fading, location, map-bounds, coordinate, or button guards.

- [ ] **Step 6: Run green tests**

Run:

```bash
dotnet test tests/Protocol.Tests/ --filter InputClickTileSerializationTests
dotnet test tests/Harness.Tests/ --filter InputClickTileHandlerTests
```

Expected: both commands pass. The existing `Handle_EventUp_ThrowsGameStateInvalid` proves the default remains strict; the new test proves the opt-in.

- [ ] **Step 7: Commit Task 1**

Run:

```bash
git add src/Protocol/Models/InputClickTileRequest.cs tests/Protocol.Tests/InputClickTileSerializationTests.cs src/Harness/Handlers/InputClickTileHandler.cs tests/Harness.Tests/InputClickTileHandlerTests.cs
git commit -m "Support event opt-in for tile clicks"
```

## Task 2: DSL And Documentation For Festival/Event Tile Clicks

**Files:**
- Modify: `src/Runner.Dsl/Input.cs`
- Modify: `tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs`
- Modify: `docs/rpc-schema.md`
- Modify: `docs/dsl-quickstart.md`
- Modify: `docs/wiki/examples.md`

- [ ] **Step 1: Write the failing DSL test**

In `tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs`, update `InputClickTile_InvokesInputClickTileAndDeserializesResult`.

Change the call:

```csharp
result = await Input.ClickTile(9, 9, location: "Frobby_CombatLab", button: "right");
```

to:

```csharp
result = await Input.ClickTile(
    9,
    9,
    location: "Frobby_CombatLab",
    button: "right",
    allowEventInput: true);
```

Add this assertion after the existing button assertion:

```csharp
Assert.Contains("\"allow_event_input\":true", inv.Calls[0].ParamsJson);
```

- [ ] **Step 2: Run the red DSL test**

Run from `/home/fintan/stardewRepos/frobby/sdv-test-framework`:

```bash
dotnet test tests/Runner.Dsl.Tests/ --filter InputClickTile_InvokesInputClickTileAndDeserializesResult
```

Expected: fail because `Input.ClickTile` has no `allowEventInput` named parameter.

- [ ] **Step 3: Add the DSL argument**

In `src/Runner.Dsl/Input.cs`, change the `ClickTile` signature from:

```csharp
public static async Task<InputClickTileResult> ClickTile(
    int x,
    int y,
    string? location = null,
    string button = "left",
    bool requireCurrentLocation = true,
    int screenOffsetX = 32,
    int screenOffsetY = 32,
    CancellationToken ct = default)
```

to:

```csharp
public static async Task<InputClickTileResult> ClickTile(
    int x,
    int y,
    string? location = null,
    string button = "left",
    bool requireCurrentLocation = true,
    bool allowEventInput = false,
    int screenOffsetX = 32,
    int screenOffsetY = 32,
    CancellationToken ct = default)
```

Inside the `new InputClickTileRequest` initializer, add:

```csharp
AllowEventInput = allowEventInput,
```

Place it after `RequireCurrentLocation = requireCurrentLocation,`.

- [ ] **Step 4: Run the green DSL test**

Run:

```bash
dotnet test tests/Runner.Dsl.Tests/ --filter InputClickTile_InvokesInputClickTileAndDeserializesResult
```

Expected: pass.

- [ ] **Step 5: Update `docs/rpc-schema.md`**

In the `input.click_tile` request example, add:

```json
"allow_event_input": false
```

Use valid JSON with commas. The request block should show:

```json
{
  "jsonrpc": "2.0",
  "id": 44,
  "method": "input.click_tile",
  "params": {
    "location": "Frobby_CombatLab",
    "x": 9,
    "y": 9,
    "button": "left",
    "allow_event_input": false
  }
}
```

After the paragraph about `screen_offset_x` / `screen_offset_y`, add:

```markdown
By default, `input.click_tile` rejects active events and festivals
(`Game1.eventUp`) to catch accidental gameplay clicks while a cutscene owns the
world. Set `allow_event_input: true` only when the scenario intentionally clicks
inside a player-controlled event or festival map. This flag does not bypass
active-menu, warp, fade, location, bounds, or button validation.
```

Update the tested-in line to include protocol coverage:

```markdown
**Tested in:** `tests/Protocol.Tests/InputClickTileSerializationTests.cs`,
`tests/Harness.Tests/InputClickTileHandlerTests.cs`, and
`tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs`.
```

- [ ] **Step 6: Update `docs/dsl-quickstart.md`**

After the selected-item click-path JSON example, add:

```markdown
For player-controlled festival or event maps, keep the normal guard unless the
click is intentionally part of the event surface:

```json
{
  "action": "input.click_tile",
  "args": {
    "location": "Temp",
    "button": "right",
    "x": 28,
    "y": 37,
    "allow_event_input": true
  }
}
```

If the test needs to prove a map action such as a festival shop tile without
depending on player distance or pathing, discover it with `state.tile_actions`
and execute it with `world.interact_tile_action`.
```

After the C# `Input.ClickTile` example, add:

```csharp
var festivalClick = await Input.ClickTile(
    28,
    37,
    location: "Temp",
    button: "right",
    allowEventInput: true);
Assert.True(festivalClick.Handled);
```

- [ ] **Step 7: Update `docs/wiki/examples.md`**

In the `NPCs, Dialogue, Events, And Festivals` section, add this bullet after the Spirit's Eve actor scenario:

```markdown
- SVE Flower Dance festival shop flow:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/33-sve-flower-dance-shop-flow.test.json`
```

Update the section guidance paragraph to:

```markdown
Use these when testing events, dialogue choice menus, relationship state,
festival maps, or festival shops. For active event or festival actors, wait with
`wait.event_active.actor_name`, then use `world.interact_npc`; the RPC will
prefer ordinary current-location NPCs and fall back to active event actors. For
festival shops, use `state.tile_actions` to prove the shop action exists, then
open it with `world.interact_tile_action` or a deliberate
`input.click_tile.allow_event_input` click when player-like event input matters.
```

- [ ] **Step 8: Run focused docs-adjacent tests**

Run:

```bash
dotnet test tests/Protocol.Tests/ --filter InputClickTileSerializationTests
dotnet test tests/Harness.Tests/ --filter InputClickTileHandlerTests
dotnet test tests/Runner.Dsl.Tests/ --filter InputClickTile_InvokesInputClickTileAndDeserializesResult
```

Expected: all pass.

- [ ] **Step 9: Commit Task 2**

Run:

```bash
git add src/Runner.Dsl/Input.cs tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs docs/rpc-schema.md docs/dsl-quickstart.md docs/wiki/examples.md
git commit -m "Document festival tile click opt-in"
```

## Task 3: SVE Flower Dance Festival Shop Scenario

**Files:**
- Create: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/33-sve-flower-dance-shop-flow.test.json`
- Modify: `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`

- [ ] **Step 1: Add the SVE scenario**

Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/33-sve-flower-dance-shop-flow.test.json` with:

```json
{
  "name": "sve_flower_dance_shop_flow",
  "fixture": "m0spike_436515781",
  "config": { "seed": 42 },
  "steps": [
    { "action": "player.set_money", "args": { "amount": 1000 } },
    { "action": "time.set", "args": { "time": 900, "day": 24, "season": "spring", "year": 1 } },
    { "action": "festival.start", "args": { "location": "Forest" } },
    {
      "action": "wait.event_active",
      "args": {
        "location": "Temp",
        "is_festival": true,
        "timeout_ms": 30000,
        "poll_ms": 100
      }
    },
    {
      "action": "state.assert",
      "args": {
        "params": {
          "location": "Temp",
          "x": 28,
          "y": 37,
          "radius": 0
        },
        "expr": "state.tile_actions.actions contains value 'Shop Festival_FlowerDance_Pierre'",
        "message": "Flower Dance festival map should expose Pierre's festival shop action"
      }
    },
    {
      "action": "world.interact_tile_action",
      "args": {
        "location": "Temp",
        "x": 28,
        "y": 37,
        "property": "Action"
      }
    },
    {
      "action": "wait.menu",
      "args": {
        "type": "ShopMenu",
        "timeout_ms": 10000,
        "poll_ms": 100
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.shop.present == true",
        "message": "Flower Dance shop should open a live ShopMenu"
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.shop.shop_id == 'Festival_FlowerDance_Pierre'",
        "message": "Flower Dance shop should expose the festival shop ID"
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.shop.items contains item_id 'FlashShifter.StardewValleyExpandedCP_Decorative_Tulips'",
        "message": "Flower Dance shop should include SVE decorative tulips"
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.shop.items contains qualified_id '(F)FlashShifter.StardewValleyExpandedCP_Decorative_Tulips'",
        "message": "Flower Dance shop should expose decorative tulips as furniture"
      }
    },
    {
      "action": "shop.purchase",
      "args": {
        "item_id": "FlashShifter.StardewValleyExpandedCP_Decorative_Tulips",
        "count": 1
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.player.money == 600",
        "message": "Buying decorative tulips should debit 400g from a 1000g setup balance"
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.player.items contains qualified_id '(F)FlashShifter.StardewValleyExpandedCP_Decorative_Tulips'",
        "message": "Purchased decorative tulips should be visible in player inventory"
      }
    },
    {
      "action": "freeze.begin",
      "args": { "settle_timeout_ms": 10000, "poll_ms": 100 }
    },
    {
      "action": "screenshot.capture_next_frame",
      "args": { "name": "final" }
    }
  ],
  "assertions": []
}
```

The scenario intentionally opens the shop with `world.interact_tile_action` after proving the map action exists. This avoids depending on player distance/pathing while still exercising the live festival `ShopMenu` and purchase path.

- [ ] **Step 2: Validate scenario syntax**

Run from `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework scripts/sdv-test --headless --mod-set core --dry-run tests/sdv/33-sve-flower-dance-shop-flow.test.json
```

Expected: dry run exits successfully and includes the scenario target in the planned command.

- [ ] **Step 3: Update SVE docs**

In `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`, add this paragraph after the scenario 32 paragraph:

```markdown
Scenario `tests/sdv/33-sve-flower-dance-shop-flow.test.json` covers an active
festival shop flow. It enters the Flower Dance, proves the live festival map
contains `Shop Festival_FlowerDance_Pierre`, opens the resulting `ShopMenu`,
asserts SVE's decorative tulips are present, buys one, and verifies the
farmer's money and inventory update. Frobby owns the neutral festival, map
action, shop, and inventory primitives; SVE IDs remain in the repo-local
scenario only.
```

- [ ] **Step 4: Commit Task 3 in SVE**

Run from any directory:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded add tests/sdv/33-sve-flower-dance-shop-flow.test.json docs/FROBBY.md
git -C /home/fintan/stardewRepos/StardewValleyExpanded commit -m "Add Flower Dance shop Frobby scenario"
```

## Task 4: Live Verification And Capability Completion Notes

**Files:**
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Run focused Frobby unit tests**

Run from `/home/fintan/stardewRepos/frobby/sdv-test-framework`:

```bash
dotnet test tests/Protocol.Tests/ --filter InputClickTileSerializationTests
dotnet test tests/Harness.Tests/ --filter InputClickTileHandlerTests
dotnet test tests/Runner.Dsl.Tests/ --filter InputClickTile_InvokesInputClickTileAndDeserializesResult
```

Expected: all pass.

- [ ] **Step 2: Run broader adjacent Frobby unit tests**

Run:

```bash
dotnet test tests/Harness.Tests/ --filter "InputClickTileHandlerTests|ShopPurchaseHandlerTests|StateShopHandlerTests"
dotnet test tests/Runner.Tests/ --filter "click_tile|state.assert|wait.menu"
```

Expected: all selected tests pass. If the filter expression is not supported by the installed test runner, run the whole projects instead:

```bash
dotnet test tests/Harness.Tests/
dotnet test tests/Runner.Tests/
```

- [ ] **Step 3: Run the new SVE scenario headless**

Run from `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-25-festival-shop tests/sdv/33-sve-flower-dance-shop-flow.test.json
```

Expected: scenario 33 passes. Report hub should be written under:

```text
/tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-25-festival-shop/index.html
```

- [ ] **Step 4: Run adjacent festival regressions**

Run from `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework scripts/sdv-test --headless --mod-set core --no-build --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-25-festival-regressions tests/sdv/19-sve-spirit-eve-chest.test.json tests/sdv/32-sve-spirit-eve-actor-dialogue.test.json
```

Expected: scenarios 19 and 32 pass.

- [ ] **Step 5: Mark Slice 25 done**

Update `SVE_FROBBY_CAPABILITY_TODO.md` Slice 25 entry from:

```markdown
- [ ] Planning: Slice 25, festival shop UI and purchase flows.
```

to:

```markdown
- [x] Done: Slice 25, festival shop UI and purchase flows.
```

Under the existing candidate proof line, add:

```markdown
  - Implementation plan: `docs/superpowers/plans/2026-05-24-sve-slice-25-festival-shop-flow.md`.
  - Done: `input.click_tile.allow_event_input` opt-in for player-controlled event/festival maps, docs, and SVE scenario 33 against the Flower Dance festival shop.
  - Verified: headless SVE scenario 33 opened the Flower Dance shop, bought SVE decorative tulips, and verified money/inventory state. Adjacent festival scenarios 19 and 32 still pass.
  - Follow-up candidates: star-token Fair shop currency handling, menu-item click purchasing inside `ShopMenu`, movie theater NPC setup, and grange judging command progression.
```

- [ ] **Step 6: Commit Task 4 in Frobby**

Run from `/home/fintan/stardewRepos/frobby/sdv-test-framework`:

```bash
git add SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "Complete festival shop flow slice"
```

## Task 5: Final Status Check

**Files:**
- No edits expected.

- [ ] **Step 1: Check Frobby git state**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework status --short --branch
```

Expected: branch `feature/sve-slice-25-festival-shop-flow`, clean worktree.

- [ ] **Step 2: Check SVE git state**

Run:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
```

Expected: branch `feature/frobby-sve-slice-25-festival-shop-flow`, clean worktree.

- [ ] **Step 3: Summarize verification**

Final response should include:

- Frobby commits made in this slice.
- SVE commits made in this slice.
- Unit test commands and pass/fail result.
- SVE scenario 33 report path.
- Adjacent festival regression report path.
- Any fallback used. If scenario 33 opens the shop with `world.interact_tile_action`, explicitly state that selected approach avoids pathing/range brittleness while `allow_event_input` is still unit-tested and documented.

## Self-Review

Spec coverage:

- `allow_event_input` default strict behavior: Task 1 protocol/harness tests and implementation.
- Active-event opt-in: Task 1 harness test and implementation.
- DSL exposure: Task 2 DSL test and implementation.
- Docs: Task 2 Frobby docs, Task 3 SVE docs, Task 4 TODO completion notes.
- SVE Flower Dance proof: Task 3 scenario and Task 4 live run.
- Adjacent regressions: Task 4 scenarios 19 and 32.

Placeholder scan:

- No placeholders, unresolved TBDs, or open implementation choices remain. The plan chooses `world.interact_tile_action` for scenario 33 and keeps `input.click_tile.allow_event_input` as the neutral capability addition from the approved design.

Type consistency:

- DTO property name is `AllowEventInput`; wire field is `allow_event_input`; DSL argument is `allowEventInput`; scenario JSON uses `allow_event_input` only when a scenario intentionally sends event-owned gameplay input.
