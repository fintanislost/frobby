# SVE Slice 6 Shop Inventory Items Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add neutral Frobby shop/inventory state support and prove it against Stardew Valley Expanded's Camilla custom vendor.

**Architecture:** Add protocol DTOs for a live `state.shop` snapshot, reuse a shared harness shop projection for both `state.shop` and `shop.purchase`, and enrich `state.player.items` with additive item metadata. The SVE scenario stays repo-local and references SVE IDs only in scenario JSON, never in Frobby production code.

**Tech Stack:** C#/.NET 6, Stardew Valley 1.6/SMAPI harness, System.Text.Json snake-case protocol, xUnit, Frobby JSON scenario runner, repo-local SVE Frobby scaffold.

---

## File Structure

Frobby files:

- Create `src/Protocol/Models/ShopState.cs`
  - Owns `ShopState` and `ShopItemSummary` response DTOs for `state.shop`.
- Modify `src/Protocol/Models/PlayerState.cs`
  - Adds raw/qualified item identity, category, quality, and runtime type to `PlayerItemSummary`.
- Modify `tests/Protocol.Tests/ShopRequestSerializationTests.cs`
  - Covers `ShopState` serialization shape.
- Modify `tests/Protocol.Tests/PlayerStateSerializationTests.cs`
  - Covers enriched inventory serialization.
- Create `src/Harness/Handlers/ShopStateProjector.cs`
  - Owns neutral shop projection interfaces and SDV `ShopMenu` adapters shared by state and purchase paths.
- Create `src/Harness/Handlers/StateShopHandler.cs`
  - Implements RPC method `state.shop`.
- Create `tests/Harness.Tests/StateShopHandlerTests.cs`
  - Unit-tests no-shop and active-shop projections.
- Modify `src/Harness/Handlers/ShopPurchaseHandler.cs`
  - Uses shared shop projection and accepts either raw or qualified item IDs while preserving existing result shape.
- Modify `tests/Harness.Tests/ShopPurchaseHandlerTests.cs`
  - Updates fake shop items for richer metadata and adds raw-ID purchase coverage.
- Modify `src/Harness/Handlers/StatePlayerHandler.cs`
  - Projects enriched runtime item fields.
- Modify `tests/Harness.Tests/StatePlayerHandlerTests.cs`
  - Tests enriched inventory projection.
- Modify `src/Harness/ModEntry.cs`
  - Registers `state.shop` and updates the harness loaded log line.
- Modify `src/Runner.Dsl/State.cs`
  - Adds `State.Shop()` for C# DSL users.
- Modify `tests/Runner.Dsl.Tests/Facets/StateTests.cs`
  - Tests `State.Shop()` invokes `state.shop`.
- Modify `docs/rpc-schema.md`
  - Documents `state.shop` and enriched `state.player.items`.
- Modify `docs/dsl-quickstart.md`
  - Adds a compact custom-shop inspection example.
- Modify `README.md`
  - Mentions shop/inventory custom item coverage.
- Modify `SVE_FROBBY_CAPABILITY_TODO.md`
  - Marks Slice 6 implementation progress and completion after verification.

SVE files:

- Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/08-sve-custom-shop-inventory-items.test.json`
  - Proves Camilla's vendor exposes and sells Gravity Elixir.
- Modify `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`
  - Documents the new SVE shop/inventory scenario.

Branch constraints:

- Frobby may be committed directly to `main`.
- SVE must remain on its feature branch. Do not merge SVE into `master` unless the user explicitly approves that later.

---

### Task 1: Protocol DTOs for Shop State and Enriched Inventory

**Files:**
- Create: `src/Protocol/Models/ShopState.cs`
- Modify: `src/Protocol/Models/PlayerState.cs`
- Modify: `tests/Protocol.Tests/ShopRequestSerializationTests.cs`
- Modify: `tests/Protocol.Tests/PlayerStateSerializationTests.cs`

- [ ] **Step 1: Write failing shop state serialization test**

Add this test to `tests/Protocol.Tests/ShopRequestSerializationTests.cs`:

```csharp
[Fact]
public void ShopState_SerializesLiveShopInventory()
{
    var state = new ShopState
    {
        Present = true,
        MenuType = "ShopMenu",
        ShopId = "FlashShifter.StardewValleyExpandedCP_CamillaVendor",
        Currency = 0,
        Items =
        {
            new ShopItemSummary
            {
                ItemId = "FlashShifter.StardewValleyExpandedCP_Gravity_Elixir",
                QualifiedId = "(O)FlashShifter.StardewValleyExpandedCP_Gravity_Elixir",
                DisplayName = "Gravity Elixir",
                Price = 4000,
                Stock = 5,
                Category = 0,
                Quality = 0,
                RuntimeType = "Object",
            },
        },
    };

    var json = JsonSerializer.Serialize(state, ProtocolJson.Options);

    Assert.Contains("\"present\":true", json);
    Assert.Contains("\"menu_type\":\"ShopMenu\"", json);
    Assert.Contains("\"shop_id\":\"FlashShifter.StardewValleyExpandedCP_CamillaVendor\"", json);
    Assert.Contains("\"currency\":0", json);
    Assert.Contains("\"item_id\":\"FlashShifter.StardewValleyExpandedCP_Gravity_Elixir\"", json);
    Assert.Contains("\"qualified_id\":\"(O)FlashShifter.StardewValleyExpandedCP_Gravity_Elixir\"", json);
    Assert.Contains("\"display_name\":\"Gravity Elixir\"", json);
    Assert.Contains("\"price\":4000", json);
    Assert.Contains("\"stock\":5", json);
    Assert.Contains("\"runtime_type\":\"Object\"", json);
}
```

- [ ] **Step 2: Write failing player item serialization test**

Extend `Serialize_ProducesSnakeCaseFields` in `tests/Protocol.Tests/PlayerStateSerializationTests.cs` by adding an inventory item:

```csharp
p.Items.Add(new PlayerItemSummary
{
    Slot = 12,
    Id = "(O)FlashShifter.StardewValleyExpandedCP_Gravity_Elixir",
    ItemId = "FlashShifter.StardewValleyExpandedCP_Gravity_Elixir",
    QualifiedId = "(O)FlashShifter.StardewValleyExpandedCP_Gravity_Elixir",
    Name = "Gravity Elixir",
    Stack = 1,
    Category = 0,
    Quality = 0,
    RuntimeType = "Object",
});
```

Add these assertions after the existing JSON assertions:

```csharp
Assert.Contains("\"item_id\":\"FlashShifter.StardewValleyExpandedCP_Gravity_Elixir\"", json);
Assert.Contains("\"qualified_id\":\"(O)FlashShifter.StardewValleyExpandedCP_Gravity_Elixir\"", json);
Assert.Contains("\"runtime_type\":\"Object\"", json);
Assert.DoesNotContain("QualifiedId", json);
```

- [ ] **Step 3: Run protocol tests and confirm they fail**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter "FullyQualifiedName~ShopRequestSerializationTests|FullyQualifiedName~PlayerStateSerializationTests"
```

Expected: compile failure for missing `ShopState`, `ShopItemSummary`, and new `PlayerItemSummary` properties.

- [ ] **Step 4: Add protocol models**

Create `src/Protocol/Models/ShopState.cs`:

```csharp
using System.Collections.Generic;

namespace SdvTestFramework.Protocol.Models;

/// <summary>Snapshot of the active shop menu. Response shape of <c>state.shop</c>.</summary>
public sealed class ShopState
{
    public bool Present { get; set; }
    public string MenuType { get; set; } = string.Empty;
    public string ShopId { get; set; } = string.Empty;
    public int Currency { get; set; }
    public List<ShopItemSummary> Items { get; set; } = new();
}

/// <summary>Live shop item descriptor for a shop snapshot.</summary>
public sealed class ShopItemSummary
{
    public string ItemId { get; set; } = string.Empty;
    public string QualifiedId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int Price { get; set; }
    public int? Stock { get; set; }
    public int? Category { get; set; }
    public int? Quality { get; set; }
    public string RuntimeType { get; set; } = string.Empty;
}
```

Modify `PlayerItemSummary` in `src/Protocol/Models/PlayerState.cs`:

```csharp
/// <summary>Minimal inventory item descriptor for a player snapshot.</summary>
public sealed class PlayerItemSummary
{
    public int Slot { get; set; }
    public string Id { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string QualifiedId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Stack { get; set; }
    public int? Category { get; set; }
    public int? Quality { get; set; }
    public string RuntimeType { get; set; } = string.Empty;
}
```

- [ ] **Step 5: Run protocol tests and confirm they pass**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter "FullyQualifiedName~ShopRequestSerializationTests|FullyQualifiedName~PlayerStateSerializationTests"
```

Expected: PASS.

- [ ] **Step 6: Commit protocol DTOs**

Run:

```bash
git add src/Protocol/Models/ShopState.cs src/Protocol/Models/PlayerState.cs tests/Protocol.Tests/ShopRequestSerializationTests.cs tests/Protocol.Tests/PlayerStateSerializationTests.cs
git commit -m "feat: add shop state protocol models"
```

---

### Task 2: Shared Shop Projection and Raw/Qualified Purchase Matching

**Files:**
- Create: `src/Harness/Handlers/ShopStateProjector.cs`
- Create: `src/Harness/Handlers/StateShopHandler.cs`
- Modify: `src/Harness/Handlers/ShopPurchaseHandler.cs`
- Create: `tests/Harness.Tests/StateShopHandlerTests.cs`
- Modify: `tests/Harness.Tests/ShopPurchaseHandlerTests.cs`

- [ ] **Step 1: Write failing state shop handler tests**

Create `tests/Harness.Tests/StateShopHandlerTests.cs`:

```csharp
using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class StateShopHandlerTests
{
    [Fact]
    public void Handle_NoActiveShop_ReturnsEmptyState()
    {
        var result = StateShopHandler.Handle(null, new FakeShopStateWorld { ActiveShop = null });
        var state = JsonSerializer.Deserialize<ShopState>(result, ProtocolJson.Options)!;

        Assert.False(state.Present);
        Assert.Equal(string.Empty, state.MenuType);
        Assert.Equal(string.Empty, state.ShopId);
        Assert.Equal(0, state.Currency);
        Assert.Empty(state.Items);
    }

    [Fact]
    public void Handle_ActiveShop_ReturnsLiveShopItems()
    {
        var result = StateShopHandler.Handle(null, new FakeShopStateWorld());
        var state = JsonSerializer.Deserialize<ShopState>(result, ProtocolJson.Options)!;

        Assert.True(state.Present);
        Assert.Equal("ShopMenu", state.MenuType);
        Assert.Equal("FlashShifter.StardewValleyExpandedCP_CamillaVendor", state.ShopId);
        Assert.Equal(0, state.Currency);

        var item = Assert.Single(state.Items);
        Assert.Equal("FlashShifter.StardewValleyExpandedCP_Gravity_Elixir", item.ItemId);
        Assert.Equal("(O)FlashShifter.StardewValleyExpandedCP_Gravity_Elixir", item.QualifiedId);
        Assert.Equal("Gravity Elixir", item.DisplayName);
        Assert.Equal(4000, item.Price);
        Assert.Equal(5, item.Stock);
        Assert.Equal(0, item.Category);
        Assert.Equal(0, item.Quality);
        Assert.Equal("Object", item.RuntimeType);
    }

    private sealed class FakeShopStateWorld : IShopStateWorld
    {
        public IShopMenuState? ActiveShop { get; init; } = new FakeShop();
    }

    private sealed class FakeShop : IShopMenuState
    {
        public string MenuType => "ShopMenu";
        public string ShopId => "FlashShifter.StardewValleyExpandedCP_CamillaVendor";
        public int Currency => 0;
        public IReadOnlyList<IShopItem> Items { get; } = new[]
        {
            new ShopItem(
                "FlashShifter.StardewValleyExpandedCP_Gravity_Elixir",
                "(O)FlashShifter.StardewValleyExpandedCP_Gravity_Elixir",
                "Gravity Elixir",
                4000,
                5,
                0,
                0,
                "Object"),
        };
    }
}
```

- [ ] **Step 2: Update purchase tests for raw and qualified IDs**

In `tests/Harness.Tests/ShopPurchaseHandlerTests.cs`, update the fake shop item construction after `ShopItem` becomes richer:

```csharp
private sealed class FakeShop : IShopMenuState
{
    public string MenuType => "ShopMenu";
    public string ShopId => "Carpenter";
    public int Currency => 0;
    public IReadOnlyList<IShopItem> Items { get; } = new[]
    {
        new ShopItem("terminal", "(F)terminal", "Terminal", 25000, 1, -9, 0, "Furniture"),
        new ShopItem("388", "(O)388", "Wood", 10, null, -16, 0, "Object"),
    };
}
```

Add a raw-ID purchase test:

```csharp
[Fact]
public void Handle_PurchasesMatchingRawItemId()
{
    var world = new FakeShopPurchaseWorld();
    var p = JsonDocument.Parse("{\"item_id\":\"terminal\",\"count\":1}").RootElement;

    var result = ShopPurchaseHandler.Handle(p, world);
    var purchase = JsonSerializer.Deserialize<ShopPurchaseResult>(result, ProtocolJson.Options)!;

    Assert.True(purchase.Ok);
    Assert.Equal("(F)terminal", purchase.ItemId);
    Assert.Equal("terminal", world.PurchasedRawItemId);
    Assert.Equal("(F)terminal", world.PurchasedQualifiedId);
}
```

Update `FakeShopPurchaseWorld.Purchase` to track both forms:

```csharp
public string? PurchasedRawItemId { get; private set; }
public string? PurchasedQualifiedId { get; private set; }

public bool Purchase(IShopItem item, int count)
{
    PurchasedItemId = item.QualifiedId;
    PurchasedRawItemId = item.ItemId;
    PurchasedQualifiedId = item.QualifiedId;
    PurchasedCount = count;
    if (!PurchaseSucceeds)
        return false;

    Money -= item.UnitPrice * count;
    return true;
}
```

- [ ] **Step 3: Run harness tests and confirm they fail**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~StateShopHandlerTests|FullyQualifiedName~ShopPurchaseHandlerTests"
```

Expected: compile failure for missing `StateShopHandler`, `IShopStateWorld`, richer `ShopItem` constructor, and `IShopItem.QualifiedId`.

- [ ] **Step 4: Create shared shop projection and state handler**

Create `src/Harness/Handlers/ShopStateProjector.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using StardewValley.Menus;

namespace SdvTestFramework.Harness.Handlers;

internal static class ShopStateProjector
{
    public static ShopState Project(IShopMenuState? shop)
    {
        if (shop is null)
        {
            return new ShopState
            {
                Present = false,
                MenuType = string.Empty,
                ShopId = string.Empty,
                Currency = 0,
            };
        }

        return new ShopState
        {
            Present = true,
            MenuType = shop.MenuType,
            ShopId = shop.ShopId,
            Currency = shop.Currency,
            Items = shop.Items.Select(ProjectItem).ToList(),
        };
    }

    private static ShopItemSummary ProjectItem(IShopItem item)
        => new()
        {
            ItemId = item.ItemId,
            QualifiedId = item.QualifiedId,
            DisplayName = item.DisplayName,
            Price = item.UnitPrice,
            Stock = item.Stock,
            Category = item.Category,
            Quality = item.Quality,
            RuntimeType = item.RuntimeType,
        };

    public static bool MatchesRequestedItem(IShopItem item, string requestedItemId)
        => string.Equals(item.ItemId, requestedItemId, StringComparison.Ordinal)
            || string.Equals(item.QualifiedId, requestedItemId, StringComparison.Ordinal);
}

internal interface IShopMenuState
{
    string MenuType { get; }
    string ShopId { get; }
    int Currency { get; }
    IReadOnlyList<IShopItem> Items { get; }
}

internal interface IShopItem
{
    string ItemId { get; }
    string QualifiedId { get; }
    string DisplayName { get; }
    int UnitPrice { get; }
    int? Stock { get; }
    int? Category { get; }
    int? Quality { get; }
    string RuntimeType { get; }
}

internal sealed record ShopItem(
    string ItemId,
    string QualifiedId,
    string DisplayName,
    int UnitPrice,
    int? Stock,
    int? Category,
    int? Quality,
    string RuntimeType) : IShopItem;

internal sealed class SdvShopMenuState : IShopMenuState
{
    private readonly ShopMenu _shop;

    public SdvShopMenuState(ShopMenu shop)
    {
        _shop = shop;
    }

    public string MenuType => _shop.GetType().Name;
    public string ShopId => _shop.ShopId ?? string.Empty;
    public int Currency => _shop.currency;

    public IReadOnlyList<IShopItem> Items => _shop.forSale
        .Select(item =>
        {
            var price = _shop.itemPriceAndStock.TryGetValue(item, out var stock)
                ? stock.Price
                : item.salePrice();
            int? availableStock = _shop.itemPriceAndStock.TryGetValue(item, out var stockInfo)
                ? ReadStock(stockInfo)
                : null;
            return new SdvShopItem(_shop, item, price, availableStock);
        })
        .ToList();

    private static int? ReadStock(object stockInfo)
    {
        foreach (var name in new[] { "Stock", "Quantity", "AvailableStock" })
        {
            var property = stockInfo.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            var value = property?.GetValue(stockInfo);
            if (value is int intValue)
                return intValue;
        }

        return null;
    }
}

internal sealed class SdvShopItem : IShopItem
{
    private readonly Lazy<Item?> _instance;

    public SdvShopItem(ShopMenu shop, ISalable salable, int unitPrice, int? stock)
    {
        Shop = shop;
        Salable = salable;
        UnitPrice = unitPrice;
        Stock = stock;
        _instance = new Lazy<Item?>(CreateSnapshotItem);
    }

    public ShopMenu Shop { get; }
    public ISalable Salable { get; }
    public int UnitPrice { get; }
    public int? Stock { get; }

    public string QualifiedId => Instance?.QualifiedItemId
        ?? Salable.QualifiedItemId
        ?? string.Empty;

    public string ItemId => Instance?.ItemId
        ?? StripQualifiedPrefix(QualifiedId);

    public string DisplayName => Instance?.DisplayName
        ?? Salable.DisplayName
        ?? string.Empty;

    public int? Category => Instance?.Category;
    public int? Quality => Instance?.Quality;
    public string RuntimeType => Instance?.GetType().Name
        ?? Salable.GetType().Name;

    private Item? Instance => _instance.Value;

    private Item? CreateSnapshotItem()
    {
        try
        {
            return Salable.GetSalableInstance() as Item;
        }
        catch
        {
            return null;
        }
    }

    private static string StripQualifiedPrefix(string value)
        => value.Length > 3 && value[0] == '(' && value[2] == ')'
            ? value[3..]
            : value;
}
```

Create `src/Harness/Handlers/StateShopHandler.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using StardewValley;
using StardewValley.Menus;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for the <c>state.shop</c> RPC method. Runs on the game thread.</summary>
public static class StateShopHandler
{
    public const string Method = "state.shop";

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, new SdvShopStateWorld());

    internal static JsonElement Handle(JsonElement? paramsElement, IShopStateWorld world)
        => ProtocolJson.ToElement(ShopStateProjector.Project(world.ActiveShop));
}

internal interface IShopStateWorld
{
    IShopMenuState? ActiveShop { get; }
}

internal sealed class SdvShopStateWorld : IShopStateWorld
{
    public IShopMenuState? ActiveShop => Game1.activeClickableMenu is ShopMenu shop
        ? new SdvShopMenuState(shop)
        : null;
}
```

- [ ] **Step 5: Refactor `shop.purchase` onto shared projection**

In `src/Harness/Handlers/ShopPurchaseHandler.cs`, remove the old local definitions of `IShopMenuState`, `IShopItem`, `ShopItem`, `SdvShopMenuState`, and `SdvShopItem`. Keep `IShopPurchaseWorld`.

Change matching and result identity in `Handle`:

```csharp
var item = shop.Items.FirstOrDefault(i => ShopStateProjector.MatchesRequestedItem(i, req.ItemId))
    ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
        $"shop.purchase item not found: {req.ItemId}");
```

Return the qualified ID for compatibility:

```csharp
return ProtocolJson.ToElement(new ShopPurchaseResult
{
    Tick = world.Tick,
    ShopId = shop.ShopId,
    ItemId = item.QualifiedId,
    DisplayName = item.DisplayName,
    Count = req.Count,
    UnitPrice = item.UnitPrice,
    PreviousMoney = previousMoney,
    Money = world.Money,
});
```

Leave `SdvShopPurchaseWorld.Purchase` semantic and data-backed:

```csharp
public bool Purchase(IShopItem item, int count)
{
    if (item is not SdvShopItem sdvItem)
        throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
            "shop.purchase can only buy items from the active SDV shop");

    var totalPrice = sdvItem.UnitPrice * count;
    if (Game1.player.Money < totalPrice)
        return false;

    if (sdvItem.Salable.GetSalableInstance() is not Item purchased)
        return false;

    purchased.Stack = count;
    Game1.player.Money -= totalPrice;
    Game1.player.addItemByMenuIfNecessary(purchased);
    sdvItem.Salable.actionWhenPurchased(sdvItem.Shop.ShopId);
    return true;
}
```

- [ ] **Step 6: Run harness tests and confirm shop projection passes**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~StateShopHandlerTests|FullyQualifiedName~ShopPurchaseHandlerTests"
```

Expected: PASS.

- [ ] **Step 7: Commit shared shop projection**

Run:

```bash
git add src/Harness/Handlers/ShopStateProjector.cs src/Harness/Handlers/StateShopHandler.cs src/Harness/Handlers/ShopPurchaseHandler.cs tests/Harness.Tests/StateShopHandlerTests.cs tests/Harness.Tests/ShopPurchaseHandlerTests.cs
git commit -m "feat: project live shop state"
```

---

### Task 3: `state.shop` Registration and C# DSL

**Files:**
- Modify: `src/Harness/ModEntry.cs`
- Modify: `src/Runner.Dsl/State.cs`
- Modify: `tests/Runner.Dsl.Tests/Facets/StateTests.cs`

- [ ] **Step 1: Create failing DSL test**

Add this test to `tests/Runner.Dsl.Tests/Facets/StateTests.cs`:

```csharp
[Fact]
public async Task Shop_InvokesStateShopAndDeserializes()
{
    SdvTestSession.ResetForTests();
    var inv = new StubInvoker
    {
        NextJson = "{\"present\":true,\"menu_type\":\"ShopMenu\",\"shop_id\":\"FlashShifter.StardewValleyExpandedCP_CamillaVendor\",\"currency\":0,\"items\":[{\"item_id\":\"FlashShifter.StardewValleyExpandedCP_Gravity_Elixir\",\"qualified_id\":\"(O)FlashShifter.StardewValleyExpandedCP_Gravity_Elixir\",\"display_name\":\"Gravity Elixir\",\"price\":4000,\"stock\":5,\"category\":0,\"quality\":0,\"runtime_type\":\"Object\"}]}",
    };
    SdvTestSession.InitializeForTests(inv);
    try
    {
        var shop = await State.Shop();

        Assert.Equal("state.shop", inv.LastMethod);
        Assert.Null(inv.LastParams);
        Assert.True(shop.Present);
        Assert.Equal("FlashShifter.StardewValleyExpandedCP_CamillaVendor", shop.ShopId);
        Assert.Equal("FlashShifter.StardewValleyExpandedCP_Gravity_Elixir", Assert.Single(shop.Items).ItemId);
    }
    finally { SdvTestSession.ResetForTests(); }
}
```

- [ ] **Step 2: Run DSL test and confirm it fails**

Run:

```bash
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter FullyQualifiedName~StateTests.Shop_InvokesStateShopAndDeserializes
```

Expected: compile failure because `State.Shop()` does not exist.

- [ ] **Step 3: Register handler in ModEntry**

In `src/Harness/ModEntry.cs`, add registration immediately after `state.menu`:

```csharp
_rpc.Register(StateShopHandler.Method, p => StateShopHandler.Handle(p));
```

Update the loaded log's RPC method list to include `state.shop` after `state.menu`.

- [ ] **Step 4: Add C# DSL method**

In `src/Runner.Dsl/State.cs`, add after `Menu()`:

```csharp
public static async Task<ShopState> Shop(CancellationToken ct = default)
{
    var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
    var resp = await s.InvokeAsync("state.shop", null, ct);
    return Deserialize<ShopState>(resp, "state.shop");
}
```

- [ ] **Step 5: Run targeted handler and DSL tests**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter FullyQualifiedName~StateShopHandlerTests
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter FullyQualifiedName~StateTests.Shop_InvokesStateShopAndDeserializes
```

Expected: PASS.

- [ ] **Step 6: Commit state.shop registration and DSL**

Run:

```bash
git add src/Harness/ModEntry.cs src/Runner.Dsl/State.cs tests/Runner.Dsl.Tests/Facets/StateTests.cs
git commit -m "feat: expose state shop rpc"
```

---

### Task 4: Enrich `state.player.items`

**Files:**
- Modify: `src/Harness/Handlers/StatePlayerHandler.cs`
- Modify: `tests/Harness.Tests/StatePlayerHandlerTests.cs`

- [ ] **Step 1: Write failing player projection test**

Update the item assertions in `tests/Harness.Tests/StatePlayerHandlerTests.cs`:

```csharp
Assert.Collection(state.Items,
    item =>
    {
        Assert.Equal(5, item.Slot);
        Assert.Equal("(F)example_terminal", item.Id);
        Assert.Equal("example_terminal", item.ItemId);
        Assert.Equal("(F)example_terminal", item.QualifiedId);
        Assert.Equal("Example Terminal", item.Name);
        Assert.Equal(1, item.Stack);
        Assert.Equal(-9, item.Category);
        Assert.Equal(0, item.Quality);
        Assert.Equal("Furniture", item.RuntimeType);
    });
```

Update the fake item:

```csharp
public IReadOnlyList<IPlayerInventoryItem> Items { get; } = new[]
{
    new PlayerInventoryItem(5, "(F)example_terminal", "example_terminal", "(F)example_terminal", "Example Terminal", 1, -9, 0, "Furniture"),
};
```

- [ ] **Step 2: Run state player test and confirm it fails**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter FullyQualifiedName~StatePlayerHandlerTests
```

Expected: compile failure because the internal player inventory interface and record lack the new fields.

- [ ] **Step 3: Update handler projection**

Modify the `Items` projection in `src/Harness/Handlers/StatePlayerHandler.cs`:

```csharp
Items = world.Items
    .Select(i => new PlayerItemSummary
    {
        Slot = i.Slot,
        Id = i.Id,
        ItemId = i.ItemId,
        QualifiedId = i.QualifiedId,
        Name = i.Name,
        Stack = i.Stack,
        Category = i.Category,
        Quality = i.Quality,
        RuntimeType = i.RuntimeType,
    })
    .ToList(),
```

Update `IPlayerInventoryItem`:

```csharp
internal interface IPlayerInventoryItem
{
    int Slot { get; }
    string Id { get; }
    string ItemId { get; }
    string QualifiedId { get; }
    string Name { get; }
    int Stack { get; }
    int? Category { get; }
    int? Quality { get; }
    string RuntimeType { get; }
}
```

Replace the record:

```csharp
internal sealed record PlayerInventoryItem(
    int Slot,
    string Id,
    string ItemId,
    string QualifiedId,
    string Name,
    int Stack,
    int? Category,
    int? Quality,
    string RuntimeType) : IPlayerInventoryItem;
```

Update `SdvPlayerStateWorld.Items` item creation:

```csharp
var qualifiedId = item.QualifiedItemId ?? item.ItemId ?? string.Empty;
var itemId = item.ItemId ?? StripQualifiedPrefix(qualifiedId);

items.Add(new PlayerInventoryItem(
    slot,
    qualifiedId,
    itemId,
    qualifiedId,
    item.DisplayName ?? item.Name ?? string.Empty,
    item.Stack,
    item.Category,
    item.Quality,
    item.GetType().Name));
```

Add helper method inside `SdvPlayerStateWorld`:

```csharp
private static string StripQualifiedPrefix(string value)
    => value.Length > 3 && value[0] == '(' && value[2] == ')'
        ? value[3..]
        : value;
```

- [ ] **Step 4: Run player handler tests**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter FullyQualifiedName~StatePlayerHandlerTests
```

Expected: PASS.

- [ ] **Step 5: Commit enriched player state**

Run:

```bash
git add src/Harness/Handlers/StatePlayerHandler.cs tests/Harness.Tests/StatePlayerHandlerTests.cs
git commit -m "feat: enrich player inventory state"
```

---

### Task 5: SVE Scenario 08 for Camilla Custom Shop Purchase

**Files:**
- Create: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/08-sve-custom-shop-inventory-items.test.json`
- Modify: `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`

- [ ] **Step 1: Add scenario JSON**

Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/08-sve-custom-shop-inventory-items.test.json`:

```json
{
  "name": "sve_custom_shop_inventory_items",
  "fixture": "m0spike_436515781",
  "config": { "seed": 42 },
  "steps": [
    {
      "action": "player.set_money",
      "args": { "money": 10000 }
    },
    {
      "action": "shop.open",
      "args": {
        "shop_id": "FlashShifter.StardewValleyExpandedCP_CamillaVendor",
        "force_open": true
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.shop.present == true",
        "message": "Camilla custom vendor should open as a live shop"
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.shop.shop_id == 'FlashShifter.StardewValleyExpandedCP_CamillaVendor'",
        "message": "state.shop should expose the Camilla vendor shop ID"
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.shop.items contains item_id 'FlashShifter.StardewValleyExpandedCP_Gravity_Elixir'",
        "message": "Camilla vendor should expose Gravity Elixir raw item ID"
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.shop.items contains qualified_id '(O)FlashShifter.StardewValleyExpandedCP_Gravity_Elixir'",
        "message": "Camilla vendor should expose Gravity Elixir qualified item ID"
      }
    },
    {
      "action": "shop.purchase",
      "args": {
        "item_id": "FlashShifter.StardewValleyExpandedCP_Gravity_Elixir",
        "count": 1
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.player.money == 6000",
        "message": "Buying Gravity Elixir should debit 4000g from a 10000g setup balance"
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.player.items contains qualified_id '(O)FlashShifter.StardewValleyExpandedCP_Gravity_Elixir'",
        "message": "Purchased Gravity Elixir should be visible in player inventory"
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

- [ ] **Step 2: Dry-run the SVE wrapper**

Run:

```bash
cd /home/fintan/stardewRepos/StardewValleyExpanded
scripts/sdv-test --dry-run --headless --mod-set core tests/sdv/08-sve-custom-shop-inventory-items.test.json
```

Expected: output includes the Frobby command and the SVE packaged mod paths under `.cache/frobby-game-mods/StardewValleyExpanded`.

- [ ] **Step 3: Update SVE Frobby docs**

Append this paragraph to `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`:

```markdown
Scenario `tests/sdv/08-sve-custom-shop-inventory-items.test.json` covers custom
item identity and shop purchase flow. It opens Camilla's data-backed vendor,
asserts `state.shop` exposes Gravity Elixir by raw and qualified item ID, buys
one elixir by raw item ID, and confirms the player's money and inventory update.
This scenario depends on neutral Frobby shop and inventory state; no SVE IDs are
compiled into Frobby itself.
```

- [ ] **Step 4: Commit SVE scenario and docs on the existing feature branch**

Run:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded add tests/sdv/08-sve-custom-shop-inventory-items.test.json docs/FROBBY.md
git -C /home/fintan/stardewRepos/StardewValleyExpanded commit -m "test: add custom shop inventory scenario"
```

---

### Task 6: Frobby Docs and Capability Tracker

**Files:**
- Modify: `docs/rpc-schema.md`
- Modify: `docs/dsl-quickstart.md`
- Modify: `README.md`
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Document RPC schema**

In `docs/rpc-schema.md`, add a `state.shop` section near `state.menu`:

````markdown
### `state.shop`

Returns the active Stardew `ShopMenu` as neutral runtime state. When no shop is
open, `present` is false and `items` is empty.

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
      "quality": 0,
      "runtime_type": "Object"
    }
  ]
}
```
````

In the `state.player` inventory section, document the additive fields:

```markdown
Inventory entries keep the existing `id` field and add `item_id`,
`qualified_id`, `category`, `quality`, and `runtime_type`. New custom-item tests
should prefer `qualified_id` for Stardew 1.6 identity checks.
```

- [ ] **Step 2: Document DSL custom shop flow**

Add this example to `docs/dsl-quickstart.md` in the state/shop area:

```csharp
await Player.SetMoney(10000);
await Shop.Open("FlashShifter.StardewValleyExpandedCP_CamillaVendor", forceOpen: true);

var shop = await State.Shop();
Assert.Contains(shop.Items, item =>
    item.QualifiedId == "(O)FlashShifter.StardewValleyExpandedCP_Gravity_Elixir"
    && item.Price == 4000);

await Shop.Purchase("FlashShifter.StardewValleyExpandedCP_Gravity_Elixir");

var player = await State.Player();
Assert.Equal(6000, player.Money);
Assert.Contains(player.Items, item =>
    item.QualifiedId == "(O)FlashShifter.StardewValleyExpandedCP_Gravity_Elixir");
```

- [ ] **Step 3: Update README capability summary**

In `README.md`, add one sentence to the feature/capability list:

```markdown
- Runtime shop and inventory inspection, including modded raw and qualified item IDs for custom shop purchase tests.
```

- [ ] **Step 4: Update capability tracker**

In `SVE_FROBBY_CAPABILITY_TODO.md`, under Slice 6, ensure the implementation
plan line is present and add the active target line:

```markdown
  - Implementation plan: `docs/superpowers/plans/2026-05-08-sve-slice-6-shop-inventory-items.md`.
  - Active target: `state.shop`, enriched `state.player.items`, and SVE scenario 08 against Camilla's vendor.
```

After all verification in Task 7 passes, change Slice 6 from `[ ] Planning` to `[x] Done` and add:

```markdown
  - Done: `state.shop`, raw/qualified inventory metadata, raw-or-qualified `shop.purchase`, docs, and SVE scenario 08.
```

- [ ] **Step 5: Commit docs**

Run:

```bash
git add docs/rpc-schema.md docs/dsl-quickstart.md README.md SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "docs: document shop inventory testing"
```

---

### Task 7: Verification and Final Hardening

**Files:**
- Modify only files required by failures found in this task.

- [ ] **Step 1: Run focused Frobby tests**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter "FullyQualifiedName~ShopRequestSerializationTests|FullyQualifiedName~PlayerStateSerializationTests"
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~StateShopHandlerTests|FullyQualifiedName~ShopPurchaseHandlerTests|FullyQualifiedName~StatePlayerHandlerTests"
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter FullyQualifiedName~StateTests
```

Expected: PASS.

- [ ] **Step 2: Run full Frobby unit suite**

Run:

```bash
dotnet test sdv-test-framework.slnx
```

Expected: PASS.

- [ ] **Step 3: Run SVE scenario 08 headlessly**

Run:

```bash
cd /home/fintan/stardewRepos/StardewValleyExpanded
scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-6-shop-inventory tests/sdv/08-sve-custom-shop-inventory-items.test.json
```

Expected: PASS and report hub under `/tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-6-shop-inventory/index.html`.

- [ ] **Step 4: Run SVE smoke subset**

Run:

```bash
cd /home/fintan/stardewRepos/StardewValleyExpanded
scripts/sdv-test --headless --mod-set core --no-build --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-6-smoke tests/sdv/01-sve-core-loads.test.json tests/sdv/04-sve-content-assets-runtime.test.json tests/sdv/08-sve-custom-shop-inventory-items.test.json
```

Expected: PASS for all three scenarios.

- [ ] **Step 5: Inspect for SVE-specific production leakage**

Run:

```bash
rg -n "FlashShifter|StardewValleyExpanded|Gravity_Elixir|CamillaVendor" src tests docs README.md
```

Expected: matches are limited to docs, tests, scenario references, and plan/spec files. No matches in Frobby production source under `src/Harness`, `src/Protocol`, `src/Runner`, `src/Runner.Dsl`, or `src/Runner.Mcp`.

- [ ] **Step 6: Confirm no uncommitted verification fixes remain**

Run:

```bash
git status --short
```

Expected: no output in Frobby after all Frobby commits, and no output in SVE
after the scenario/docs commit. If this command prints changed files, finish
those fixes, rerun the failed verification command from this task, and commit
the exact file paths shown by `git status --short`.

- [ ] **Step 7: Final status check**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
```

Expected:

- Frobby: clean `main`.
- SVE: clean feature branch, not `master`.

---

## Completion Criteria

- `state.shop` is registered and callable.
- `state.shop` reports no-shop and active-shop states.
- Active shop item summaries include raw item ID, qualified ID, display name, price, stock, category, quality, and runtime type when available.
- `state.player.items` preserves `id` and adds raw/qualified identity plus runtime metadata.
- `shop.purchase` can match either raw or qualified item IDs.
- SVE scenario 08 passes headlessly against Camilla's custom vendor.
- Frobby docs explain the neutral custom shop/inventory flow.
- Frobby production source contains no SVE-specific item or shop IDs.
