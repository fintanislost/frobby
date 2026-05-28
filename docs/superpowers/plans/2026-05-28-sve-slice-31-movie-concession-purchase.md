# SVE Slice 31 Movie Concession Purchase Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add headless SVE coverage for opening the movie-theater concession flow, confirming it exposes a normal Stardew `ShopMenu`, and buying one visible concession through click-based UI input.

**Architecture:** Frobby gets two neutral helpers which are useful beyond SVE: `input.click_tile` can discover and click a nearby tile by map action value, and `shop.click_purchase` can target a visible shop item by zero-based item index. The SVE scenario reuses the Sophia movie invite setup from scenario 38, clicks the vanilla `Concessions` tile action, chooses the confirmation prompt, then buys the first concession item in the resulting `ShopMenu`.

**Tech Stack:** C#/.NET, xUnit, System.Text.Json snake-case protocol serialization, Frobby JSON-RPC harness, Frobby runner JSON scenarios, SMAPI/Stardew Valley runtime APIs, Stardew Valley Expanded repo-local `scripts/sdv-test --headless`.

---

## File Map

Frobby repo: `/home/fintan/stardewRepos/frobby/sdv-test-framework`

- Modify `src/Protocol/Models/InputClickTileRequest.cs`
  - Adds neutral action-tile discovery fields: `action_value`, `radius`, `layers`, and `properties`.
- Modify `src/Harness/Handlers/InputClickTileHandler.cs`
  - Resolves `action_value` by scanning map `Action` / `TouchAction` properties around the supplied center tile before sending the normal gameplay click.
- Modify `tests/Harness.Tests/InputClickTileHandlerTests.cs`
  - Adds RED/GREEN coverage for action-value tile discovery, missing matches, and radius validation.
- Modify `src/Protocol/Models/ShopClickPurchaseRequest.cs`
  - Adds `item_index` as a neutral target for dynamic shops.
- Modify `src/Harness/Handlers/ShopClickPurchaseHandler.cs`
  - Lets `shop.click_purchase` target a shop item by zero-based item index when `item_id` and `display_name` are omitted.
- Modify `src/Runner.Dsl/Shop.cs`
  - Exposes optional `itemIndex` in the C# DSL.
- Modify `tests/Harness.Tests/ShopClickPurchaseHandlerTests.cs`
  - Adds RED/GREEN coverage for click-purchase by index and invalid indexes.
- Modify `README.md`
  - Documents action-value tile clicks and index-based shop click-purchase in the scenario guidance.
- Modify `docs/rpc-schema.md`
  - Documents the new request fields.
- Modify `docs/wiki/examples.md`
  - Adds scenario 39 to the curated examples.
- Modify `SVE_FROBBY_CAPABILITY_TODO.md`
  - Marks Slice 31 done after verification.

SVE repo: `/home/fintan/stardewRepos/StardewValleyExpanded`

- Create `tests/sdv/39-sve-movie-concession-purchase-flow.test.json`
  - Live SVE proof scenario for Sophia invite plus visible concession purchase.
- Modify `docs/FROBBY.md`
  - Adds scenario 39 to SVE's local Frobby scenario guide.

Do not merge the SVE feature branch into `master`. Frobby can merge to `main` only after the user explicitly approves that integration step.

---

## Task 1: Branch And Confirm Research Baseline

**Files:**
- Read: `/home/fintan/stardewRepos/frobby/sdv-test-framework/docs/superpowers/specs/2026-05-28-sve-slice-31-movie-concession-purchase-design.md`
- Read: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/38-sve-movie-ticket-invite-flow.test.json`

- [ ] **Step 1: Confirm clean worktrees**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
```

Expected: no unstaged or uncommitted changes. Current branches may still be the Slice 30 feature branches because this plan stacks from that clean state.

- [ ] **Step 2: Create Slice 31 feature branches**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework switch -c feature/sve-slice-31-movie-concessions
git -C /home/fintan/stardewRepos/StardewValleyExpanded switch -c feature/frobby-sve-slice-31-movie-concessions
```

Expected: both commands switch to new feature branches. If either branch already exists, run the matching `git -C <repo> switch <branch>` command and confirm the worktree is clean before continuing.

- [ ] **Step 3: Keep the Stardew concession behavior in view**

Use the already-confirmed decompiled Stardew behavior as the implementation baseline:

```csharp
// StardewValley.Locations.MovieTheater.answerDialogueAction
if (questionAndAnswer == "Concession_Yes")
{
    Utility.TryOpenShopMenu("Concessions", this, null, null, forceOpen: true);
    if (Game1.activeClickableMenu is ShopMenu shopMenu)
        shopMenu.onPurchase = OnPurchaseConcession;
    return true;
}
```

This means the test should click the `Concessions` map action, choose `Yes`, then use Frobby's existing shop-menu surface. Do not add a special SVE concession menu primitive.

---

## Task 2: TDD `input.click_tile` Action-Value Discovery

**Files:**
- Modify: `/home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Harness.Tests/InputClickTileHandlerTests.cs`
- Modify: `/home/fintan/stardewRepos/frobby/sdv-test-framework/src/Protocol/Models/InputClickTileRequest.cs`
- Modify: `/home/fintan/stardewRepos/frobby/sdv-test-framework/src/Harness/Handlers/InputClickTileHandler.cs`

- [ ] **Step 1: Write the failing handler tests**

In `tests/Harness.Tests/InputClickTileHandlerTests.cs`, add these tests before `Handle_NotWorldReady_ThrowsGameStateInvalid`:

```csharp
[Fact]
public void Handle_ActionValue_ClicksNearestMatchingTileWithinRadius()
{
    var world = new FakeTileClickWorld
    {
        CurrentLocationName = "MovieTheater",
        ViewportX = 64,
        ViewportY = 128,
    };
    world.SetTileProperty(8, 4, "Buildings", "Action", "Concessions");
    world.SetTileProperty(12, 9, "Buildings", "Action", "Concessions");
    var p = JsonDocument.Parse(
        "{\"location\":\"MovieTheater\",\"x\":7,\"y\":7,\"button\":\"right\",\"action_value\":\"Concessions\",\"radius\":8}")
        .RootElement;

    var json = InputClickTileHandler.Handle(p, world);
    var result = JsonSerializer.Deserialize<InputClickTileResult>(json, ProtocolJson.Options)!;

    Assert.Equal("right", world.ClickedButton);
    Assert.Equal(8, result.Tile.X);
    Assert.Equal(4, result.Tile.Y);
    Assert.Equal(544, world.ClickedWorldX);
    Assert.Equal(288, world.ClickedWorldY);
    Assert.Equal(480, world.ClickedScreenX);
    Assert.Equal(160, world.ClickedScreenY);
    Assert.True(result.Handled);
}

[Fact]
public void Handle_ActionValueNoMatch_ThrowsGameStateInvalid()
{
    var p = JsonDocument.Parse(
        "{\"x\":7,\"y\":7,\"button\":\"right\",\"action_value\":\"Concessions\",\"radius\":3}")
        .RootElement;

    var ex = Assert.Throws<JsonRpcException>(() =>
        InputClickTileHandler.Handle(p, new FakeTileClickWorld()));

    Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    Assert.Contains("Concessions", ex.Message);
}

[Fact]
public void Handle_ActionValueNegativeRadius_ThrowsInvalidParams()
{
    var p = JsonDocument.Parse(
        "{\"x\":7,\"y\":7,\"button\":\"right\",\"action_value\":\"Concessions\",\"radius\":-1}")
        .RootElement;

    var ex = Assert.Throws<JsonRpcException>(() =>
        InputClickTileHandler.Handle(p, new FakeTileClickWorld()));

    Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    Assert.Contains("radius", ex.Message);
}
```

Extend `FakeTileClickWorld` in the same file:

```csharp
private readonly Dictionary<(int X, int Y, string Layer, string Property), string> _tileProperties = new();

public IReadOnlyList<string> LayerNames { get; } = new[] { "Back", "Buildings" };

public void SetTileProperty(int x, int y, string layer, string property, string value)
    => _tileProperties[(x, y, layer, property)] = value;

public string? GetTileProperty(int x, int y, string layer, string property)
    => _tileProperties.TryGetValue((x, y, layer, property), out var value) ? value : null;
```

The fake world already implements the other required members. This step should fail to compile because `InputClickTileRequest.ActionValue`, `InputClickTileRequest.Radius`, `InputClickTileRequest.Layers`, `InputClickTileRequest.Properties`, `IInputTileClickWorld.LayerNames`, and `IInputTileClickWorld.GetTileProperty` do not exist yet.

- [ ] **Step 2: Run the RED tests**

Run:

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~InputClickTileHandlerTests.Handle_ActionValue" --nologo
```

Expected: FAIL at compile time with missing request/world members.

- [ ] **Step 3: Add request fields**

In `src/Protocol/Models/InputClickTileRequest.cs`, add `using System.Collections.Generic;` at the top and add these properties after `AllowEventInput`:

```csharp
/// <summary>
/// Optional exact map action value to discover near <see cref="X"/> and <see cref="Y"/>
/// before clicking. This keeps scenarios away from brittle coordinates when a
/// stable Stardew map action is available.
/// </summary>
public string? ActionValue { get; set; }

/// <summary>Search radius used with <see cref="ActionValue"/>. Defaults to the supplied tile only.</summary>
public int Radius { get; set; }

/// <summary>Optional map layers to scan when <see cref="ActionValue"/> is set.</summary>
public List<string>? Layers { get; set; }

/// <summary>Optional tile properties to scan when <see cref="ActionValue"/> is set.</summary>
public List<string>? Properties { get; set; }
```

- [ ] **Step 4: Add action-value resolution to the handler**

In `src/Harness/Handlers/InputClickTileHandler.cs`, add:

```csharp
using System.Collections.Generic;
using System.Linq;
```

Add this constant near `TileSize`:

```csharp
private const int MaxActionSearchRadius = 25;
```

Replace the initial tile assignment in `Handle`:

```csharp
var tileX = req.X!.Value;
var tileY = req.Y!.Value;
```

with:

```csharp
var (tileX, tileY) = ResolveTargetTile(req, world);
```

Add this helper after `Handle`:

```csharp
private static (int X, int Y) ResolveTargetTile(InputClickTileRequest req, IInputTileClickWorld world)
{
    var centerX = req.X!.Value;
    var centerY = req.Y!.Value;
    if (string.IsNullOrWhiteSpace(req.ActionValue))
        return (centerX, centerY);

    if (req.Radius < 0 || req.Radius > MaxActionSearchRadius)
        throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
            $"params.radius must be between 0 and {MaxActionSearchRadius}");

    var layers = WorldInteractTileActionHandler.ResolveLayers(req.Layers, world.LayerNames);
    var properties = TileActionPropertyNames.Resolve(req.Properties, "properties");
    var matches = new List<TileActionCandidate>();
    for (var y = centerY - req.Radius; y <= centerY + req.Radius; y++)
    {
        if (y < 0)
            continue;

        for (var x = centerX - req.Radius; x <= centerX + req.Radius; x++)
        {
            if (x < 0)
                continue;

            foreach (var property in properties)
            foreach (var layer in layers)
            {
                var value = world.GetTileProperty(x, y, layer, property);
                if (string.Equals(value, req.ActionValue, StringComparison.Ordinal))
                {
                    matches.Add(new TileActionCandidate
                    {
                        Tile = new TilePoint { X = x, Y = y },
                        Layer = layer,
                        Property = property,
                        Value = value,
                        Distance = Math.Abs(x - centerX) + Math.Abs(y - centerY),
                    });
                }
            }
        }
    }

    var match = matches
        .OrderBy(candidate => candidate.Distance)
        .ThenBy(candidate => candidate.Tile.Y)
        .ThenBy(candidate => candidate.Tile.X)
        .FirstOrDefault();
    if (match is null)
    {
        throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
            $"input.click_tile could not find action_value '{req.ActionValue}' within radius {req.Radius} of tile {centerX},{centerY}");
    }

    return (match.Tile.X, match.Tile.Y);
}
```

Extend `NormalizeButton` after the `x/y` negative validation:

```csharp
if (!string.IsNullOrWhiteSpace(req.ActionValue)
    && (req.Radius < 0 || req.Radius > MaxActionSearchRadius))
{
    throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
        $"params.radius must be between 0 and {MaxActionSearchRadius}");
}
```

Extend `IInputTileClickWorld`:

```csharp
IReadOnlyList<string> LayerNames { get; }
string? GetTileProperty(int x, int y, string layer, string property);
```

Extend `SdvInputTileClickWorld`:

```csharp
public IReadOnlyList<string> LayerNames
    => CurrentLocation.Map?.Layers.Select(layer => layer.Id).ToList() ?? new List<string>();

public string? GetTileProperty(int x, int y, string layer, string property)
    => CurrentLocation.doesTileHaveProperty(x, y, property, layer, ignoreTileSheetProperties: false);
```

- [ ] **Step 5: Run the GREEN tests**

Run:

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~InputClickTileHandlerTests.Handle_ActionValue" --nologo
```

Expected: PASS for the three action-value tests.

- [ ] **Step 6: Run the full input-click regression tests**

Run:

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~InputClickTileHandlerTests" --nologo
```

Expected: PASS for all `InputClickTileHandlerTests`.

---

## Task 3: TDD `shop.click_purchase` By Item Index

**Files:**
- Modify: `/home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Harness.Tests/ShopClickPurchaseHandlerTests.cs`
- Modify: `/home/fintan/stardewRepos/frobby/sdv-test-framework/src/Protocol/Models/ShopClickPurchaseRequest.cs`
- Modify: `/home/fintan/stardewRepos/frobby/sdv-test-framework/src/Harness/Handlers/ShopClickPurchaseHandler.cs`
- Modify: `/home/fintan/stardewRepos/frobby/sdv-test-framework/src/Runner.Dsl/Shop.cs`

- [ ] **Step 1: Write the failing handler tests**

In `tests/Harness.Tests/ShopClickPurchaseHandlerTests.cs`, add these tests before `Handle_ClicksMatchingItemAndReturnsCurrencyAndBounds`:

```csharp
[Fact]
public void Handle_ClicksItemIndexTarget()
{
    var world = new FakeWorld();
    var p = JsonDocument.Parse("{\"item_index\":1,\"count\":1}").RootElement;

    var result = ShopClickPurchaseHandler.Handle(p, world);
    var purchase = JsonSerializer.Deserialize<ShopClickPurchaseResult>(result, ProtocolJson.Options)!;

    Assert.Equal("(O)388", purchase.ItemId);
    Assert.Equal("Wood", purchase.DisplayName);
    Assert.Equal(1, purchase.ItemIndex);
    Assert.Equal("(O)388", world.Shop!.RevealedItemId);
}

[Fact]
public void Handle_NegativeItemIndex_ThrowsInvalidParams()
{
    var p = JsonDocument.Parse("{\"item_index\":-1}").RootElement;

    var ex = Assert.Throws<JsonRpcException>(() =>
        ShopClickPurchaseHandler.Handle(p, new FakeWorld()));

    Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    Assert.Contains("item_index", ex.Message);
}

[Fact]
public void Handle_ItemIndexOutOfRange_ThrowsGameStateInvalid()
{
    var p = JsonDocument.Parse("{\"item_index\":99}").RootElement;

    var ex = Assert.Throws<JsonRpcException>(() =>
        ShopClickPurchaseHandler.Handle(p, new FakeWorld()));

    Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    Assert.Contains("item_index 99", ex.Message);
}
```

This step should fail to compile because `ShopClickPurchaseRequest.ItemIndex` does not exist.

- [ ] **Step 2: Run the RED tests**

Run:

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~ShopClickPurchaseHandlerTests.Handle_ClicksItemIndexTarget|FullyQualifiedName~ShopClickPurchaseHandlerTests.Handle_NegativeItemIndex|FullyQualifiedName~ShopClickPurchaseHandlerTests.Handle_ItemIndexOutOfRange" --nologo
```

Expected: FAIL at compile time with missing `ItemIndex`.

- [ ] **Step 3: Add the request field**

In `src/Protocol/Models/ShopClickPurchaseRequest.cs`, add after `DisplayName`:

```csharp
/// <summary>
/// Zero-based item index in the active shop. Useful for dynamic shops where
/// the test needs to buy a visible option without hard-coding the randomized ID.
/// </summary>
public int? ItemIndex { get; set; }
```

- [ ] **Step 4: Implement target validation and lookup**

In `src/Harness/Handlers/ShopClickPurchaseHandler.cs`, replace the empty-target validation:

```csharp
if (string.IsNullOrWhiteSpace(req.ItemId) && string.IsNullOrWhiteSpace(req.DisplayName))
    throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.item_id or params.display_name required");
```

with:

```csharp
if (string.IsNullOrWhiteSpace(req.ItemId)
    && string.IsNullOrWhiteSpace(req.DisplayName)
    && req.ItemIndex is null)
{
    throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
        "params.item_id, params.display_name, or params.item_index required");
}
if (req.ItemIndex is < 0)
    throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.item_index must be >= 0");
```

Replace `FindTarget` with:

```csharp
private static IShopItem? FindTarget(IShopMenuState shop, ShopClickPurchaseRequest req)
{
    if (!string.IsNullOrWhiteSpace(req.ItemId))
        return shop.Items.FirstOrDefault(i => ShopStateProjector.MatchesRequestedItem(i, req.ItemId));

    if (!string.IsNullOrWhiteSpace(req.DisplayName))
    {
        return shop.Items.FirstOrDefault(i =>
            string.Equals(i.DisplayName, req.DisplayName, StringComparison.Ordinal));
    }

    if (req.ItemIndex is int index)
        return index < shop.Items.Count ? shop.Items[index] : null;

    return null;
}
```

Replace `TargetLabel` with:

```csharp
private static string TargetLabel(ShopClickPurchaseRequest req)
{
    if (!string.IsNullOrWhiteSpace(req.ItemId))
        return req.ItemId;
    if (!string.IsNullOrWhiteSpace(req.DisplayName))
        return req.DisplayName;
    return req.ItemIndex is int index ? $"item_index {index}" : string.Empty;
}
```

- [ ] **Step 5: Expose item index in the C# DSL**

In `src/Runner.Dsl/Shop.cs`, change the `ClickPurchase` signature to:

```csharp
public static async Task<ShopClickPurchaseResult> ClickPurchase(
    string itemId = "",
    string displayName = "",
    int? itemIndex = null,
    int count = 1,
    int scrollAttempts = 16,
    CancellationToken ct = default)
```

and set `ItemIndex = itemIndex` in the request initializer:

```csharp
var p = JsonSerializer.SerializeToElement(new ShopClickPurchaseRequest
{
    ItemId = itemId,
    DisplayName = displayName,
    ItemIndex = itemIndex,
    Count = count,
    ScrollAttempts = scrollAttempts,
}, ProtocolJson.Options);
```

- [ ] **Step 6: Run the GREEN tests**

Run:

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~ShopClickPurchaseHandlerTests" --nologo
```

Expected: PASS for all `ShopClickPurchaseHandlerTests`.

---

## Task 4: Add The SVE Concession Scenario

**Files:**
- Create: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/39-sve-movie-concession-purchase-flow.test.json`

- [ ] **Step 1: Create scenario 39**

Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/39-sve-movie-concession-purchase-flow.test.json`:

```json
{
  "name": "sve_movie_concession_purchase_flow",
  "fixture": "m0spike_436515781",
  "config": { "seed": 42 },
  "steps": [
    { "action": "time.set", "args": { "time": 900, "day": 3, "season": "spring", "year": 1 } },
    { "action": "world.set_weather", "args": { "type": "sun" } },
    { "action": "player.set_money", "args": { "amount": 5000 } },
    { "action": "player.add_mail", "args": { "id": "ccMovieTheater" } },
    { "action": "player.add_event_seen", "args": { "id": "191393" } },
    { "action": "player.add_event_seen", "args": { "id": "015305930" } },
    {
      "action": "player.set_friendship",
      "args": {
        "npc": "Sophia",
        "points": 2000,
        "talked_to_today": false,
        "gifts_today": 0,
        "gifts_this_week": 0
      }
    },
    {
      "action": "player.warp",
      "args": { "location": "Custom_SophiaHouse", "x": 18, "y": 11 }
    },
    {
      "action": "wait.location",
      "args": {
        "location": "Custom_SophiaHouse",
        "x": 18,
        "y": 11,
        "timeout_ms": 10000,
        "poll_ms": 100
      }
    },
    {
      "action": "world.warp_npc",
      "args": {
        "name": "Sophia",
        "location": "Custom_SophiaHouse",
        "x": 18,
        "y": 10
      }
    },
    {
      "action": "wait.npc_location",
      "args": {
        "name": "Sophia",
        "location": "Custom_SophiaHouse",
        "x": 18,
        "y": 10,
        "timeout_ms": 10000,
        "poll_ms": 100
      }
    },
    { "action": "player.give_item", "args": { "id": "(O)809", "count": 1 } },
    { "action": "player.select_item", "args": { "id": "(O)809", "prefer_hotbar": true } },
    { "action": "wait.ms", "args": { "ms": 500 } },
    {
      "action": "input.click_tile",
      "args": {
        "location": "Custom_SophiaHouse",
        "button": "right",
        "x": 18,
        "y": 10
      }
    },
    {
      "action": "wait.menu",
      "args": {
        "text_matches": "movie|Movie|theater|Theater|ticket|Ticket|Sophia",
        "ready": true,
        "timeout_ms": 30000,
        "poll_ms": 100
      }
    },
    {
      "action": "ui.acknowledge",
      "args": { "until_closed": true, "max_clicks": 3, "timeout_ms": 10000, "poll_ms": 100, "interval_ms": 100 }
    },
    { "action": "wait.ms", "args": { "ms": 1000 } },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.player.items not contains qualified_id '(O)809'",
        "message": "Accepting Sophia's movie invite should consume the movie ticket before concession testing"
      }
    },
    {
      "action": "player.warp",
      "args": { "location": "MovieTheater", "x": 7, "y": 7 }
    },
    {
      "action": "wait.location",
      "args": {
        "location": "MovieTheater",
        "x": 7,
        "y": 7,
        "timeout_ms": 10000,
        "poll_ms": 100
      }
    },
    { "action": "wait.ms", "args": { "ms": 1000 } },
    {
      "action": "state.assert",
      "args": {
        "params": {
          "location": "MovieTheater",
          "x": 7,
          "y": 7,
          "radius": 10
        },
        "expr": "state.tile_actions.actions contains value 'Concessions'",
        "message": "MovieTheater should expose the vanilla concession map action"
      }
    },
    {
      "action": "screenshot.capture_next_frame",
      "args": { "name": "before-concession-click" }
    },
    {
      "action": "input.click_tile",
      "args": {
        "location": "MovieTheater",
        "button": "right",
        "x": 7,
        "y": 7,
        "action_value": "Concessions",
        "radius": 10
      }
    },
    {
      "action": "wait.menu",
      "args": {
        "choice_text_matches": "^Yes\\.?$",
        "ready": true,
        "timeout_ms": 30000,
        "poll_ms": 100
      }
    },
    {
      "action": "screenshot.capture_next_frame",
      "args": { "name": "concession-prompt" }
    },
    {
      "action": "event.advance",
      "args": {
        "choice_text_matches": "^Yes\\.?$",
        "ready": true,
        "timeout_ms": 30000,
        "poll_ms": 100
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
    { "action": "wait.ms", "args": { "ms": 500 } },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.shop.present == true",
        "message": "Accepting the concession prompt should open a live ShopMenu"
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.shop.shop_id == 'Concessions'",
        "message": "The movie concession menu should expose the vanilla Concessions shop ID"
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.shop.items contains runtime_type 'MovieConcession'",
        "message": "The concession shop should expose Stardew MovieConcession entries"
      }
    },
    {
      "action": "screenshot.capture_next_frame",
      "args": { "name": "concession-shop-open" }
    },
    {
      "action": "shop.click_purchase",
      "args": {
        "item_index": 0,
        "count": 1
      }
    },
    { "action": "wait.ms", "args": { "ms": 500 } },
    {
      "action": "wait.menu",
      "args": {
        "text_matches": "Sophia|bought|purchased|concession|Stardrop|Sorbet|Cotton Candy|Popcorn|Panzanella",
        "ready": true,
        "timeout_ms": 10000,
        "poll_ms": 100
      }
    },
    {
      "action": "screenshot.capture_next_frame",
      "args": { "name": "final" }
    }
  ],
  "assertions": [
    {
      "type": "content.asset",
      "asset": "Data/ConcessionTastes",
      "asset_type": "data",
      "entry_keys": ["Sophia"],
      "expr": "asset.entries.Sophia.exists == true",
      "message": "SVE should add Sophia concession taste data"
    }
  ]
}
```

- [ ] **Step 2: Run scenario 39**

Run:

```bash
/home/fintan/stardewRepos/StardewValleyExpanded/scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-31-red /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/39-sve-movie-concession-purchase-flow.test.json
```

Expected after Tasks 2 and 3: PASS. If it fails before buying because `action_value` found the wrong tile, inspect the report's `state.tile_actions` step and narrow `layers` to `["Buildings"]` and `properties` to `["Action"]` in both the `state.assert` params and the `input.click_tile` args. Keep the match value `Concessions`.

---

## Task 5: Documentation And TODO Updates

**Files:**
- Modify: `/home/fintan/stardewRepos/frobby/sdv-test-framework/README.md`
- Modify: `/home/fintan/stardewRepos/frobby/sdv-test-framework/docs/rpc-schema.md`
- Modify: `/home/fintan/stardewRepos/frobby/sdv-test-framework/docs/wiki/examples.md`
- Modify: `/home/fintan/stardewRepos/frobby/sdv-test-framework/SVE_FROBBY_CAPABILITY_TODO.md`
- Modify: `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`

- [ ] **Step 1: Update Frobby README**

Add this guidance near the existing click/menu scenario guidance:

```markdown
- When a map action has a stable value but its exact tile can vary by map patch,
  prefer `input.click_tile` with `action_value` plus a small `radius` around a
  nearby player tile. This still sends a real gameplay click, but avoids baking
  fragile map coordinates into scenarios.
- For dynamic shops where the test only needs to prove a visible purchase path,
  use `shop.click_purchase` with `item_index` after asserting `state.shop.items`
  contains the expected runtime type or shop family.
```

- [ ] **Step 2: Update RPC schema for `input.click_tile`**

In `docs/rpc-schema.md`, update the `input.click_tile` section with this example:

```json
→ { "jsonrpc": "2.0", "id": 17, "method": "input.click_tile",
    "params": { "location": "MovieTheater", "x": 7, "y": 7, "button": "right", "action_value": "Concessions", "radius": 10 } }
← { "jsonrpc": "2.0", "id": 17, "result": { "ok": true, "location": "MovieTheater", "tile": { "x": 8, "y": 4 } } }
```

Add this field note:

```markdown
When `action_value` is set, `x` and `y` are the center of a map-action search
rather than necessarily the final click target. Frobby scans `Action` and
`TouchAction` properties on the active map, chooses the nearest exact value
match within `radius`, and then sends the normal tile click to that resolved
tile. Optional `layers` and `properties` narrow the scan.
```

- [ ] **Step 3: Update RPC schema for `shop.click_purchase`**

In `docs/rpc-schema.md`, update the `shop.click_purchase` section with this example:

```json
→ { "jsonrpc": "2.0", "id": 20, "method": "shop.click_purchase", "params": { "item_index": 0, "count": 1 } }
← { "jsonrpc": "2.0", "id": 20, "result": { "ok": true, "item_index": 0, "visible_index": 0 } }
```

Add this field note:

```markdown
Use `item_index` for dynamic shops where a stable item ID is not the test
subject. Indexes are zero-based in the current `state.shop.items` order.
```

- [ ] **Step 4: Update Frobby examples and TODO**

Add to `docs/wiki/examples.md` near the SVE examples:

```markdown
- `39-sve-movie-concession-purchase-flow.test.json` uses a nearby
  `input.click_tile` `action_value` lookup to click the vanilla theater
  `Concessions` action, accepts the prompt, asserts the `Concessions` ShopMenu,
  and click-buys the first visible `MovieConcession` entry by `item_index`.
```

Update `SVE_FROBBY_CAPABILITY_TODO.md` Slice 31 entry or follow-up section:

```markdown
- [x] Slice 31: Movie concession purchase flow.
  - Done: neutral `input.click_tile` action-value discovery, index-based
    `shop.click_purchase`, and SVE scenario 39 for Sophia invite plus
    concession purchase.
  - Verified: headless SVE scenario 39 and regressions 38/36.
```

- [ ] **Step 5: Update SVE docs**

Add to `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`:

```markdown
### 39. Movie Concession Purchase Flow

Scenario 39 reuses the Sophia movie-ticket invite setup, enters
`MovieTheater`, discovers the vanilla `Concessions` action with
`input.click_tile` `action_value`, accepts the confirmation prompt, and buys the
first visible `MovieConcession` entry from the `Concessions` ShopMenu by
`item_index`.
```

---

## Task 6: Verification

**Files:**
- Read: Frobby and SVE worktrees

- [ ] **Step 1: Run focused Frobby tests**

Run:

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~InputClickTileHandlerTests|FullyQualifiedName~ShopClickPurchaseHandlerTests" --nologo
```

Expected: PASS.

- [ ] **Step 2: Build the runner**

Run:

```bash
dotnet build /home/fintan/stardewRepos/frobby/sdv-test-framework/src/Runner/Runner.csproj --nologo
```

Expected: build succeeds with 0 errors.

- [ ] **Step 3: Run the live SVE scenario suite for this slice**

Run:

```bash
/home/fintan/stardewRepos/StardewValleyExpanded/scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-31-final /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/39-sve-movie-concession-purchase-flow.test.json /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/38-sve-movie-ticket-invite-flow.test.json /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/36-sve-movie-theater-npc-click.test.json
```

Expected: 3/3 scenarios pass. The report hub should be written at:

```text
/tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-31-final/index.html
```

- [ ] **Step 4: Inspect git state**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
```

Expected:

- Frobby branch: `feature/sve-slice-31-movie-concessions`
- SVE branch: `feature/frobby-sve-slice-31-movie-concessions`
- Only the files listed in this plan are modified or created.

---

## Task 7: Commit Work

**Files:**
- All modified files from Tasks 2-6

- [ ] **Step 1: Commit Frobby changes**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework add src/Protocol/Models/InputClickTileRequest.cs src/Harness/Handlers/InputClickTileHandler.cs tests/Harness.Tests/InputClickTileHandlerTests.cs src/Protocol/Models/ShopClickPurchaseRequest.cs src/Harness/Handlers/ShopClickPurchaseHandler.cs src/Runner.Dsl/Shop.cs tests/Harness.Tests/ShopClickPurchaseHandlerTests.cs README.md docs/rpc-schema.md docs/wiki/examples.md SVE_FROBBY_CAPABILITY_TODO.md
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework commit -m "feat: support dynamic tile and shop clicks"
```

Expected: one Frobby commit containing only generic framework changes and docs.

- [ ] **Step 2: Commit SVE scenario changes**

Run:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded add tests/sdv/39-sve-movie-concession-purchase-flow.test.json docs/FROBBY.md
git -C /home/fintan/stardewRepos/StardewValleyExpanded commit -m "test: cover movie concession purchase flow"
```

Expected: one SVE commit containing only scenario/docs changes.

- [ ] **Step 3: Final status check**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
```

Expected: both worktrees are clean on their Slice 31 feature branches.

---

## Self-Review Notes

- Spec coverage: the plan opens concessions through a player-like tile click, verifies the normal `ShopMenu`, buys one visible concession, waits for Stardew's purchased-concession dialogue, captures screenshots, and keeps Frobby mod-neutral.
- Frobby neutrality: new `action_value` tile discovery and `item_index` shop targeting know only Stardew map actions and shop item order; they contain no SVE NPCs, locations, or concession IDs.
- TDD coverage: Tasks 2 and 3 require failing unit tests before production code. Scenario 39 then validates the integrated live path.
- Regression coverage: final live verification reruns scenarios 39, 38, and 36 because they share movie-theater setup, selected-item NPC clicks, and theater tile behavior.
