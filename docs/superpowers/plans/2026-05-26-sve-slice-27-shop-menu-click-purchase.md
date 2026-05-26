# SVE Slice 27 Shop Menu Click Purchase Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a neutral `shop.click_purchase` RPC that buys through the active Stardew `ShopMenu` click path and prove it with an SVE festival shop scenario.

**Architecture:** Keep semantic purchasing and visible UI purchasing separate. Add protocol DTOs, a harness handler with a small shop-click adapter, runner/DSL affordances, docs, then an SVE scenario that buys an SVE-added Flower Dance shop item through `shop.click_purchase`.

**Tech Stack:** C#/.NET 10 projects in Frobby, SMAPI/Stardew Valley `ShopMenu`, JSON-RPC protocol models, JSON `.test.json` scenarios, SVE repo-local scenario/docs.

---

## File Structure

Frobby files:

- Create `src/Protocol/Models/ShopClickPurchaseRequest.cs`
  - Wire DTO for `shop.click_purchase`.
- Create `src/Protocol/Models/ShopClickPurchaseResult.cs`
  - Response DTO mirroring purchase fields plus screen/bounds metadata.
- Modify `tests/Protocol.Tests/ShopRequestSerializationTests.cs`
  - Protocol serialization and defaults for the new DTOs.
- Create `src/Harness/Handlers/ShopClickPurchaseHandler.cs`
  - Validates the request, finds the active shop item, reveals its visible row, calls `receiveLeftClick`, and reports currency/click metadata.
- Modify `src/Harness/ModEntry.cs`
  - Registers `shop.click_purchase` and updates the console RPC method list.
- Create `tests/Harness.Tests/ShopClickPurchaseHandlerTests.cs`
  - Unit tests validation, matching, click invocation, scrolling metadata, and currency reporting.
- Create `src/Runner.Dsl/Shop.cs`
  - Typed DSL wrapper for `shop.open`, `shop.purchase`, and `shop.click_purchase`.
- Create `tests/Runner.Dsl.Tests/Facets/ShopTests.cs`
  - Tests typed shop wrappers emit the right RPC payloads.
- Modify `src/Runner/Scenarios/ScenarioRunner.cs`
  - Adds a readable report label for `shop.click_purchase`.
- Modify `tests/Runner.Tests/ScenarioRunnerTests.cs`
  - Ensures scenario steps pass through and label the click purchase action.
- Modify `docs/rpc-schema.md`, `docs/dsl-quickstart.md`, and `docs/wiki/examples.md`
  - Documents when to use semantic purchase vs visible menu click purchase.
- Modify `SVE_FROBBY_CAPABILITY_TODO.md`
  - Marks Slice 27 active at start and done after verification.

SVE files:

- Create `tests/sdv/35-sve-flower-dance-shop-click-purchase.test.json`
  - Repo-local proof for visible shop menu click purchase.
- Modify `docs/FROBBY.md`
  - Adds the scenario description and neutral capability notes.

## Task 1: Protocol DTOs

**Files:**
- Create: `src/Protocol/Models/ShopClickPurchaseRequest.cs`
- Create: `src/Protocol/Models/ShopClickPurchaseResult.cs`
- Modify: `tests/Protocol.Tests/ShopRequestSerializationTests.cs`

- [ ] **Step 1: Write failing protocol tests**

Add these tests to `tests/Protocol.Tests/ShopRequestSerializationTests.cs`:

```csharp
[Fact]
public void ShopClickPurchaseRequest_DefaultsCountAndScrollAttempts()
{
    var json = "{\"item_id\":\"(F)example_terminal\"}";
    var req = JsonSerializer.Deserialize<ShopClickPurchaseRequest>(json, ProtocolJson.Options)!;

    Assert.Equal("(F)example_terminal", req.ItemId);
    Assert.Equal(string.Empty, req.DisplayName);
    Assert.Equal(1, req.Count);
    Assert.Equal(16, req.ScrollAttempts);
}

[Fact]
public void ShopClickPurchaseRequest_DeserializesDisplayNameTarget()
{
    var json = "{\"display_name\":\"Decorative Tulips\",\"count\":1,\"scroll_attempts\":4}";
    var req = JsonSerializer.Deserialize<ShopClickPurchaseRequest>(json, ProtocolJson.Options)!;

    Assert.Equal(string.Empty, req.ItemId);
    Assert.Equal("Decorative Tulips", req.DisplayName);
    Assert.Equal(1, req.Count);
    Assert.Equal(4, req.ScrollAttempts);
}

[Fact]
public void ShopClickPurchaseResult_SerializesClickMetadata()
{
    var result = new ShopClickPurchaseResult
    {
        Tick = 45,
        ShopId = "Festival_FlowerDance_Pierre",
        ItemId = "(F)FlashShifter.StardewValleyExpandedCP_Decorative_Tulips",
        DisplayName = "Decorative Tulips",
        Count = 1,
        UnitPrice = 400,
        Currency = 0,
        PreviousCurrencyBalance = 1000,
        CurrencyBalance = 600,
        PreviousMoney = 1000,
        Money = 600,
        Screen = new PixelPoint { X = 880, Y = 420 },
        Bounds = new MenuBounds { X = 500, Y = 380, Width = 760, Height = 80 },
        VisibleIndex = 1,
        ItemIndex = 3,
        Scrolled = true,
    };

    var json = JsonSerializer.Serialize(result, ProtocolJson.Options);

    Assert.Contains("\"shop_id\":\"Festival_FlowerDance_Pierre\"", json);
    Assert.Contains("\"screen\":{\"x\":880,\"y\":420}", json);
    Assert.Contains("\"bounds\":{\"x\":500,\"y\":380,\"width\":760,\"height\":80}", json);
    Assert.Contains("\"visible_index\":1", json);
    Assert.Contains("\"item_index\":3", json);
    Assert.Contains("\"scrolled\":true", json);
}
```

- [ ] **Step 2: Run red protocol tests**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter ShopClickPurchase
```

Expected: fail to compile because `ShopClickPurchaseRequest` and `ShopClickPurchaseResult` do not exist.

- [ ] **Step 3: Add request DTO**

Create `src/Protocol/Models/ShopClickPurchaseRequest.cs`:

```csharp
namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape of <c>shop.click_purchase</c>.</summary>
public sealed class ShopClickPurchaseRequest
{
    /// <summary>Raw or qualified item id to click in the active shop.</summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>Exact display-name target used when <see cref="ItemId"/> is empty.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Stack count to buy. Slice 27 supports one visible click.</summary>
    public int Count { get; set; } = 1;

    /// <summary>Maximum reveal attempts before failing. Defaults to enough to cover ordinary shop lists.</summary>
    public int ScrollAttempts { get; set; } = 16;
}
```

- [ ] **Step 4: Add result DTO**

Create `src/Protocol/Models/ShopClickPurchaseResult.cs`:

```csharp
namespace SdvTestFramework.Protocol.Models;

/// <summary>Response shape of <c>shop.click_purchase</c>.</summary>
public sealed class ShopClickPurchaseResult : MutatorOk
{
    public string ShopId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int Count { get; set; }
    public int UnitPrice { get; set; }
    public int Currency { get; set; }
    public int PreviousCurrencyBalance { get; set; }
    public int CurrencyBalance { get; set; }
    public int PreviousMoney { get; set; }
    public int Money { get; set; }
    public PixelPoint Screen { get; set; } = new();
    public MenuBounds Bounds { get; set; } = new();
    public int VisibleIndex { get; set; }
    public int ItemIndex { get; set; }
    public bool Scrolled { get; set; }
}
```

- [ ] **Step 5: Run green protocol tests**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter ShopClickPurchase
```

Expected: pass.

- [ ] **Step 6: Commit Task 1**

Run:

```bash
git add src/Protocol/Models/ShopClickPurchaseRequest.cs src/Protocol/Models/ShopClickPurchaseResult.cs tests/Protocol.Tests/ShopRequestSerializationTests.cs
git commit -m "feat: add shop click purchase protocol"
```

## Task 2: Harness Handler With Fake Shop Tests

**Files:**
- Create: `src/Harness/Handlers/ShopClickPurchaseHandler.cs`
- Create: `tests/Harness.Tests/ShopClickPurchaseHandlerTests.cs`
- Modify: `src/Harness/ModEntry.cs`

- [ ] **Step 1: Write failing harness tests**

Create `tests/Harness.Tests/ShopClickPurchaseHandlerTests.cs` with tests for:

- missing params throws `InvalidParams`;
- missing both `item_id` and `display_name` throws `InvalidParams`;
- `count != 1` throws `InvalidParams`;
- no active shop throws `GameStateInvalid`;
- target not found throws `GameStateInvalid`;
- unsupported currency throws `GameStateInvalid`;
- item-id target invokes a menu click and reports balances/bounds;
- display-name target can match when no id is supplied.

Use fake implementations of the handler interfaces rather than a live Stardew
`ShopMenu`. The happy-path assertion should verify:

```csharp
Assert.Equal("(F)terminal", purchase.ItemId);
Assert.Equal(25000, purchase.UnitPrice);
Assert.Equal(0, purchase.Currency);
Assert.Equal(30000, purchase.PreviousCurrencyBalance);
Assert.Equal(5000, purchase.CurrencyBalance);
Assert.Equal((860, 420), world.Shop!.LastClick);
Assert.Equal(1, purchase.VisibleIndex);
Assert.Equal(2, purchase.ItemIndex);
Assert.True(purchase.Scrolled);
```

- [ ] **Step 2: Run red harness tests**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter ShopClickPurchaseHandlerTests
```

Expected: fail to compile because `ShopClickPurchaseHandler` does not exist.

- [ ] **Step 3: Implement the handler contracts and validation**

Create `src/Harness/Handlers/ShopClickPurchaseHandler.cs` with:

- public `ShopClickPurchaseHandler.Method = "shop.click_purchase"`;
- internal `IShopClickPurchaseWorld : IShopCurrencyBalances`;
- internal `IShopClickMenuState : IShopMenuState`;
- internal `ShopClickTarget`;
- validation for target, `count == 1`, and `scroll_attempts >= 0`;
- target lookup by item id or exact display name;
- currency balance before/after reporting.

- [ ] **Step 4: Implement fake-friendly click flow**

The handler should call:

```csharp
var target = shop.RevealItem(item, req.ScrollAttempts)
    ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
        $"shop.click_purchase could not reveal item: {TargetLabel(req)}");
shop.Click(target);
```

Then return `ShopClickPurchaseResult` using `target.Screen`, `target.Bounds`,
`target.VisibleIndex`, `target.ItemIndex`, and `target.Scrolled`.

- [ ] **Step 5: Register RPC**

In `src/Harness/ModEntry.cs`, add:

```csharp
_rpc.Register(ShopClickPurchaseHandler.Method, p => ShopClickPurchaseHandler.Handle(p));
```

after `ShopPurchaseHandler`, and add `shop.click_purchase` to the console RPC
method list.

- [ ] **Step 6: Run green harness tests**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter ShopClickPurchaseHandlerTests
```

Expected: pass.

- [ ] **Step 7: Commit Task 2**

Run:

```bash
git add src/Harness/Handlers/ShopClickPurchaseHandler.cs tests/Harness.Tests/ShopClickPurchaseHandlerTests.cs src/Harness/ModEntry.cs
git commit -m "feat: add shop click purchase handler"
```

## Task 3: Production ShopMenu Adapter

**Files:**
- Modify: `src/Harness/Handlers/ShopClickPurchaseHandler.cs`
- Modify: `tests/Harness.Tests/ShopClickPurchaseHandlerTests.cs`

- [ ] **Step 1: Add a failing guard test for unchanged paid balance**

Add a harness test where the fake shop accepts the click but does not debit a
positive-price item. Expect `GameStateInvalid` with a message containing
`did not change currency balance`.

- [ ] **Step 2: Run red guard test**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter ShopClickPurchaseHandlerTests
```

Expected: fail because the handler currently accepts unchanged paid clicks.

- [ ] **Step 3: Add balance-change guard**

After `shop.Click(target)`, if `item.UnitPrice > 0` and the active currency
balance did not decrease, throw:

```csharp
throw new JsonRpcException(
    JsonRpcErrorCode.GameStateInvalid,
    "shop.click_purchase click did not change currency balance; the menu may not have accepted the click");
```

- [ ] **Step 4: Add production adapter**

Implement `SdvShopClickPurchaseWorld` and `SdvShopClickMenuState`.

`SdvShopClickMenuState` should:

- expose `Items` like `SdvShopMenuState`;
- find the target index in `_shop.forSale`;
- read the `forSaleButtons` field as `IList`;
- read/write the `currentItemIndex` field as `int`;
- clamp `currentItemIndex` so the target index appears in the visible button list;
- create `ShopClickTarget` from the matching `ClickableComponent.bounds`;
- call `_shop.performHoverAction(x, y)` and `_shop.receiveLeftClick(x, y)`.

Keep reflection helpers private in this file, and return clear `GameStateInvalid`
errors if `forSaleButtons` or `currentItemIndex` cannot be read.

- [ ] **Step 5: Run focused handler tests and build**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter ShopClickPurchaseHandlerTests
dotnet build src/Harness/Harness.csproj
```

Expected: pass/build cleanly.

- [ ] **Step 6: Commit Task 3**

Run:

```bash
git add src/Harness/Handlers/ShopClickPurchaseHandler.cs tests/Harness.Tests/ShopClickPurchaseHandlerTests.cs
git commit -m "feat: click visible shop menu rows"
```

## Task 4: Runner, DSL, And Docs

**Files:**
- Create: `src/Runner.Dsl/Shop.cs`
- Create: `tests/Runner.Dsl.Tests/Facets/ShopTests.cs`
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`
- Modify: `tests/Runner.Tests/ScenarioRunnerTests.cs`
- Modify: `docs/rpc-schema.md`
- Modify: `docs/dsl-quickstart.md`
- Modify: `docs/wiki/examples.md`
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Write failing runner and DSL tests**

Add a runner test named `ShopClickPurchase_PassesThroughAndReportsReadableStep`
that runs a scenario step:

```json
{ "action": "shop.click_purchase", "args": { "item_id": "(F)terminal" } }
```

and asserts the report detail is:

```text
Click purchase shop item "(F)terminal"
```

Add DSL tests proving `Shop.ClickPurchase("(F)terminal")` invokes
`shop.click_purchase` and deserializes `ShopClickPurchaseResult`.

- [ ] **Step 2: Run red tests**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter ShopClickPurchase_PassesThroughAndReportsReadableStep
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter ShopTests
```

Expected: runner label test fails with generic detail; DSL test fails to compile because `Shop` does not exist.

- [ ] **Step 3: Add DSL wrapper**

Create `src/Runner.Dsl/Shop.cs` with `Open`, `Purchase`, and `ClickPurchase`
methods. `ClickPurchase` should serialize `ShopClickPurchaseRequest`, invoke
`shop.click_purchase`, and deserialize `ShopClickPurchaseResult`.

- [ ] **Step 4: Add readable runner detail**

In `ScenarioRunner.DescribeStep`, add:

```csharp
"shop.click_purchase" => $"Click purchase shop item \"{GetStringArg(step.Args, "item_id") ?? GetStringArg(step.Args, "display_name") ?? "unknown"}\"",
```

- [ ] **Step 5: Update docs and TODO**

Document `shop.click_purchase` in `docs/rpc-schema.md`, add a short example to
`docs/dsl-quickstart.md` and `docs/wiki/examples.md`, and mark Slice 27 as
Active in `SVE_FROBBY_CAPABILITY_TODO.md`.

- [ ] **Step 6: Run green runner/DSL tests**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter ShopClickPurchase_PassesThroughAndReportsReadableStep
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter ShopTests
```

Expected: pass.

- [ ] **Step 7: Commit Task 4**

Run:

```bash
git add src/Runner.Dsl/Shop.cs tests/Runner.Dsl.Tests/Facets/ShopTests.cs src/Runner/Scenarios/ScenarioRunner.cs tests/Runner.Tests/ScenarioRunnerTests.cs docs/rpc-schema.md docs/dsl-quickstart.md docs/wiki/examples.md SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "feat: expose shop click purchase to runners"
```

## Task 5: SVE Proof Scenario

**Files:**
- Create: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/35-sve-flower-dance-shop-click-purchase.test.json`
- Modify: `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`
- Modify: `/home/fintan/stardewRepos/frobby/sdv-test-framework/SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Add failing SVE scenario**

Create scenario 35 by copying scenario 33's setup, then replace the purchase
step with:

```json
{
  "action": "wait.ms",
  "args": { "ms": 500 }
},
{
  "action": "shop.click_purchase",
  "args": {
    "item_id": "FlashShifter.StardewValleyExpandedCP_Decorative_Tulips",
    "count": 1
  }
}
```

Keep money and inventory assertions from scenario 33.

- [ ] **Step 2: Run red live scenario**

Run from `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
scripts/sdv-test --headless --mod-set core --scenario tests/sdv/35-sve-flower-dance-shop-click-purchase.test.json --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-27-shop-click
```

Expected before the Frobby harness is rebuilt into the SVE run: fail because
`shop.click_purchase` is not available, or fail in the live click adapter if row
reflection needs adjustment.

- [ ] **Step 3: Update SVE docs**

Add a short `docs/FROBBY.md` paragraph for scenario 35 explaining that it buys
through the visible `ShopMenu` click path.

- [ ] **Step 4: Run live scenario and adjacent regressions**

Run:

```bash
scripts/sdv-test --headless --mod-set core --scenario tests/sdv/35-sve-flower-dance-shop-click-purchase.test.json --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-27-shop-click
scripts/sdv-test --headless --mod-set core --scenario tests/sdv/33-sve-flower-dance-shop-flow.test.json --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-27-regression-33
scripts/sdv-test --headless --mod-set core --scenario tests/sdv/34-sve-fair-star-token-shop-currency.test.json --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-27-regression-34
```

Expected: all pass.

- [ ] **Step 5: Mark Slice 27 done**

Update `SVE_FROBBY_CAPABILITY_TODO.md` from Active to Done and include the live
scenario verification summary.

- [ ] **Step 6: Commit Task 5**

Commit Frobby TODO and SVE scenario/docs separately:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework add SVE_FROBBY_CAPABILITY_TODO.md
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework commit -m "docs: mark SVE slice 27 complete"
git -C /home/fintan/stardewRepos/StardewValleyExpanded add tests/sdv/35-sve-flower-dance-shop-click-purchase.test.json docs/FROBBY.md
git -C /home/fintan/stardewRepos/StardewValleyExpanded commit -m "Add Flower Dance shop click purchase scenario"
```

## Task 6: Full Verification

**Files:** no planned edits.

- [ ] **Step 1: Run focused Frobby test suite**

Run from `/home/fintan/stardewRepos/frobby/sdv-test-framework`:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter ShopClickPurchase
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter ShopClickPurchaseHandlerTests
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter ShopClickPurchase_PassesThroughAndReportsReadableStep
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter ShopTests
```

Expected: pass.

- [ ] **Step 2: Run relevant full projects**

Run one at a time:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj
dotnet test tests/Harness.Tests/Harness.Tests.csproj
dotnet test tests/Runner.Tests/Runner.Tests.csproj
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj
```

Expected: pass with only existing skipped tests.

- [ ] **Step 3: Check git state**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
```

Expected: clean branches except any intentionally untracked report output outside the repos.
