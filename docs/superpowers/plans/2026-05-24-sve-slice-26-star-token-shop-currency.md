# SVE Slice 26 Star-Token Shop Currency Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add neutral Frobby support for active shop currency balances and prove it by buying an SVE Stardew Fair item with star tokens.

**Architecture:** Keep Frobby production code mod-agnostic by adding a small currency helper that maps Stardew shop currency codes to player balances. `state.shop`, `player.set_shop_currency`, and `shop.purchase` all use that helper so gold and star-token behavior stay consistent. SVE receives only a repo-local scenario and docs describing how the neutral Frobby surface validates the Fair shop.

**Tech Stack:** C#/.NET, xUnit, SMAPI/Stardew Valley 1.6, Frobby JSON-RPC protocol DTOs, Frobby repo-local SVE scenario JSON.

---

## File Structure

Frobby files:

- Modify `src/Protocol/Models/ShopState.cs`: add `currency_name` and nullable `currency_balance` response fields.
- Modify `src/Protocol/Models/ShopPurchaseResult.cs`: add active-currency debit fields while preserving money fields.
- Create `src/Protocol/Models/SetShopCurrencyRequest.cs`: request DTO for `player.set_shop_currency`.
- Create `src/Protocol/Models/SetShopCurrencyResult.cs`: response DTO with prior/current balance.
- Create `src/Harness/Handlers/ShopCurrency.cs`: neutral currency code names and backing balance read/write helpers.
- Modify `src/Harness/Handlers/ShopStateProjector.cs`: project known active shop currency balance.
- Modify `src/Harness/Handlers/StateShopHandler.cs`: provide player currency balances to the projector.
- Create `src/Harness/Handlers/PlayerSetShopCurrencyHandler.cs`: RPC handler for setting a known shop currency balance.
- Modify `src/Harness/Handlers/ShopPurchaseHandler.cs`: debit the active shop currency instead of always debiting gold.
- Modify `src/Harness/ModEntry.cs`: register `player.set_shop_currency` and list it in the startup log.
- Modify `src/Runner.Dsl/Player.cs`: add a typed DSL wrapper for the new RPC method.
- Modify `docs/rpc-schema.md`: document `state.shop` currency balance fields, `player.set_shop_currency`, and `shop.purchase` currency result fields.
- Modify Frobby tests:
  - `tests/Protocol.Tests/ShopRequestSerializationTests.cs`
  - `tests/Protocol.Tests/SetMoneyRequestSerializationTests.cs`
  - `tests/Harness.Tests/StateShopHandlerTests.cs`
  - `tests/Harness.Tests/ShopPurchaseHandlerTests.cs`
  - Create `tests/Harness.Tests/PlayerSetShopCurrencyHandlerTests.cs`
  - `tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs`
  - `tests/Runner.Dsl.Tests/Facets/StateTests.cs`

SVE files:

- Create `tests/sdv/34-sve-fair-star-token-shop-currency.test.json`: live Fair proof scenario.
- Modify `docs/FROBBY.md`: add a short Slice 26 scenario note.

## Task 0: Create Feature Branches

**Files:** none

- [ ] **Step 1: Verify both repositories are clean**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
```

Expected: both repos show clean working trees. Frobby may be ahead of `origin/main`; SVE may be ahead of `origin/master`.

- [ ] **Step 2: Create the Frobby feature branch**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework switch -c feature/sve-slice-26-shop-currency
```

Expected: branch switches to `feature/sve-slice-26-shop-currency`.

- [ ] **Step 3: Create the SVE feature branch**

Run:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded switch -c feature/frobby-sve-slice-26-star-token-shop
```

Expected: branch switches to `feature/frobby-sve-slice-26-star-token-shop`.

## Task 1: Protocol DTO Shape

**Files:**

- Modify: `src/Protocol/Models/ShopState.cs`
- Modify: `src/Protocol/Models/ShopPurchaseResult.cs`
- Create: `src/Protocol/Models/SetShopCurrencyRequest.cs`
- Create: `src/Protocol/Models/SetShopCurrencyResult.cs`
- Test: `tests/Protocol.Tests/ShopRequestSerializationTests.cs`
- Test: `tests/Protocol.Tests/SetMoneyRequestSerializationTests.cs`

- [ ] **Step 1: Write failing protocol serialization tests**

In `tests/Protocol.Tests/ShopRequestSerializationTests.cs`, update `ShopPurchaseResult_SerializesPurchaseDetails` to set and assert the new fields:

```csharp
var result = new ShopPurchaseResult
{
    Tick = 44,
    ShopId = "Festival_StardewValleyFair_StarTokens",
    ItemId = "(F)FlashShifter.StardewValleyExpandedCP_Furniture_Catalogue_2",
    DisplayName = "Furniture Catalogue 2",
    Count = 1,
    UnitPrice = 9999,
    Currency = 1,
    PreviousCurrencyBalance = 10000,
    CurrencyBalance = 1,
    PreviousMoney = 5000,
    Money = 5000,
};

var json = JsonSerializer.Serialize(result, ProtocolJson.Options);

Assert.Contains("\"ok\":true", json);
Assert.Contains("\"shop_id\":\"Festival_StardewValleyFair_StarTokens\"", json);
Assert.Contains("\"item_id\":\"(F)FlashShifter.StardewValleyExpandedCP_Furniture_Catalogue_2\"", json);
Assert.Contains("\"unit_price\":9999", json);
Assert.Contains("\"currency\":1", json);
Assert.Contains("\"previous_currency_balance\":10000", json);
Assert.Contains("\"currency_balance\":1", json);
Assert.Contains("\"previous_money\":5000", json);
Assert.Contains("\"money\":5000", json);
```

In the same file, update `ShopState_SerializesLiveShopInventory` to set and assert:

```csharp
Currency = 1,
CurrencyName = "star_tokens",
CurrencyBalance = 10000,
```

and:

```csharp
Assert.Contains("\"currency\":1", json);
Assert.Contains("\"currency_name\":\"star_tokens\"", json);
Assert.Contains("\"currency_balance\":10000", json);
```

In `tests/Protocol.Tests/SetMoneyRequestSerializationTests.cs`, add:

```csharp
[Fact]
public void SetShopCurrencyRequest_DeserializesFromSnakeCase()
{
    var json = "{\"currency\":1,\"amount\":10000}";
    var req = JsonSerializer.Deserialize<SetShopCurrencyRequest>(json, ProtocolJson.Options)!;

    Assert.Equal(1, req.Currency);
    Assert.Equal(10000, req.Amount);
}

[Fact]
public void SetShopCurrencyResult_IncludesPreviousCurrentCurrencyAndTickAndOk()
{
    var r = new SetShopCurrencyResult
    {
        Tick = 42,
        Currency = 1,
        CurrencyName = "star_tokens",
        Previous = 75,
        Amount = 10000,
    };

    var json = JsonSerializer.Serialize(r, ProtocolJson.Options);

    Assert.Contains("\"ok\":true", json);
    Assert.Contains("\"tick\":42", json);
    Assert.Contains("\"currency\":1", json);
    Assert.Contains("\"currency_name\":\"star_tokens\"", json);
    Assert.Contains("\"previous\":75", json);
    Assert.Contains("\"amount\":10000", json);
}
```

- [ ] **Step 2: Run protocol tests and verify they fail**

Run:

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Protocol.Tests/Protocol.Tests.csproj --filter "FullyQualifiedName~ShopRequestSerializationTests|FullyQualifiedName~SetMoneyRequestSerializationTests"
```

Expected: FAIL because the new DTO properties/classes do not exist yet.

- [ ] **Step 3: Add protocol DTO implementation**

Modify `src/Protocol/Models/ShopState.cs`:

```csharp
public int Currency { get; set; }
public string CurrencyName { get; set; } = string.Empty;
public int? CurrencyBalance { get; set; }
public List<ShopItemSummary> Items { get; set; } = new();
```

Modify `src/Protocol/Models/ShopPurchaseResult.cs`:

```csharp
public int UnitPrice { get; set; }
public int Currency { get; set; }
public int PreviousCurrencyBalance { get; set; }
public int CurrencyBalance { get; set; }
public int PreviousMoney { get; set; }
public int Money { get; set; }
```

Create `src/Protocol/Models/SetShopCurrencyRequest.cs`:

```csharp
namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape of <c>player.set_shop_currency</c>.</summary>
public sealed class SetShopCurrencyRequest
{
    /// <summary>Stardew shop currency code. Supported initially: 0 = gold, 1 = star tokens.</summary>
    public int Currency { get; set; }

    /// <summary>Absolute balance to set. Must be <c>&gt;= 0</c>.</summary>
    public int Amount { get; set; }
}
```

Create `src/Protocol/Models/SetShopCurrencyResult.cs`:

```csharp
namespace SdvTestFramework.Protocol.Models;

/// <summary>Response shape for <c>player.set_shop_currency</c>.</summary>
public sealed class SetShopCurrencyResult : MutatorOk
{
    public int Currency { get; set; }
    public string CurrencyName { get; set; } = string.Empty;
    public int Previous { get; set; }
    public int Amount { get; set; }
}
```

- [ ] **Step 4: Run protocol tests and verify they pass**

Run:

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Protocol.Tests/Protocol.Tests.csproj --filter "FullyQualifiedName~ShopRequestSerializationTests|FullyQualifiedName~SetMoneyRequestSerializationTests"
```

Expected: PASS.

- [ ] **Step 5: Commit protocol DTO work**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework add src/Protocol/Models/ShopState.cs src/Protocol/Models/ShopPurchaseResult.cs src/Protocol/Models/SetShopCurrencyRequest.cs src/Protocol/Models/SetShopCurrencyResult.cs tests/Protocol.Tests/ShopRequestSerializationTests.cs tests/Protocol.Tests/SetMoneyRequestSerializationTests.cs
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework commit -m "feat: add shop currency protocol fields"
```

Expected: commit succeeds.

## Task 2: Shared Shop Currency Projection

**Files:**

- Create: `src/Harness/Handlers/ShopCurrency.cs`
- Modify: `src/Harness/Handlers/ShopStateProjector.cs`
- Modify: `src/Harness/Handlers/StateShopHandler.cs`
- Test: `tests/Harness.Tests/StateShopHandlerTests.cs`

- [ ] **Step 1: Write failing state projection tests**

In `tests/Harness.Tests/StateShopHandlerTests.cs`, update `Handle_NoActiveShop_ReturnsAbsentShopState`:

```csharp
Assert.Equal("", state.CurrencyName);
Assert.Null(state.CurrencyBalance);
```

Update `Handle_ActiveShop_ReturnsProjectedShopState`:

```csharp
Assert.Equal(0, state.Currency);
Assert.Equal("gold", state.CurrencyName);
Assert.Equal(30000, state.CurrencyBalance);
```

Add a star-token projection test:

```csharp
[Fact]
public void Handle_StarTokenShop_ReturnsCurrencyNameAndFestivalScoreBalance()
{
    var result = StateShopHandler.Handle(null, new FakeShopStateWorld
    {
        ActiveShop = new FakeShop(currency: 1),
        FestivalScore = 10000,
    });
    var state = JsonSerializer.Deserialize<ShopState>(result, ProtocolJson.Options)!;

    Assert.True(state.Present);
    Assert.Equal(1, state.Currency);
    Assert.Equal("star_tokens", state.CurrencyName);
    Assert.Equal(10000, state.CurrencyBalance);
}
```

Update the fake world and shop in that file:

```csharp
private sealed class FakeShopStateWorld : IShopStateWorld, IShopCurrencyBalances
{
    public IShopMenuState? ActiveShop { get; init; } = new FakeShop();
    public IShopCurrencyBalances Balances => this;
    public int Money { get; set; } = 30000;
    public int FestivalScore { get; set; } = 0;
}

private sealed class FakeShop : IShopMenuState
{
    private readonly int _currency;

    public FakeShop(int currency = 0)
    {
        _currency = currency;
    }

    public string MenuType => "ShopMenu";
    public string ShopId => "ExampleMod.CustomVendor";
    public int Currency => _currency;
    public IReadOnlyList<IShopItem> Items { get; } = new[]
    {
        new ShopItem(
            "ExampleMod.CustomDrink",
            "(O)ExampleMod.CustomDrink",
            "Custom Drink",
            4000,
            5,
            0,
            0,
            "Object"),
    };
}
```

- [ ] **Step 2: Run state shop tests and verify they fail**

Run:

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Harness.Tests/Harness.Tests.csproj --filter FullyQualifiedName~StateShopHandlerTests
```

Expected: FAIL because `IShopCurrencyBalances`, `CurrencyName`, and `CurrencyBalance` projection do not exist.

- [ ] **Step 3: Add neutral currency helper**

Create `src/Harness/Handlers/ShopCurrency.cs`:

```csharp
using SdvTestFramework.Protocol;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

internal interface IShopCurrencyBalances
{
    int Money { get; set; }
    int FestivalScore { get; set; }
}

internal static class ShopCurrency
{
    public const int Gold = 0;
    public const int StarTokens = 1;

    public static string Name(int currency)
        => currency switch
        {
            Gold => "gold",
            StarTokens => "star_tokens",
            _ => $"currency_{currency}",
        };

    public static bool IsSupported(int currency)
        => currency is Gold or StarTokens;

    public static void RequireSupported(int currency, string method)
    {
        if (!IsSupported(currency))
            throw new JsonRpcException(
                JsonRpcErrorCode.GameStateInvalid,
                $"{method} does not support shop currency {currency}");
    }

    public static int GetBalance(int currency, IShopCurrencyBalances balances)
        => currency switch
        {
            Gold => balances.Money,
            StarTokens => balances.FestivalScore,
            _ => throw new JsonRpcException(
                JsonRpcErrorCode.GameStateInvalid,
                $"unsupported shop currency {currency}"),
        };

    public static void SetBalance(int currency, IShopCurrencyBalances balances, int amount)
    {
        switch (currency)
        {
            case Gold:
                balances.Money = amount;
                break;
            case StarTokens:
                balances.FestivalScore = amount;
                break;
            default:
                throw new JsonRpcException(
                    JsonRpcErrorCode.GameStateInvalid,
                    $"unsupported shop currency {currency}");
        }
    }
}

internal sealed class SdvShopCurrencyBalances : IShopCurrencyBalances
{
    public int Money
    {
        get => Game1.player.Money;
        set => Game1.player.Money = value;
    }

    public int FestivalScore
    {
        get => Game1.player.festivalScore;
        set => Game1.player.festivalScore = value;
    }
}
```

- [ ] **Step 4: Project currency name and supported balance**

Modify `src/Harness/Handlers/ShopStateProjector.cs`:

```csharp
public static ShopState Project(IShopMenuState? shop, IShopCurrencyBalances? balances = null)
{
    if (shop is null)
        return new ShopState();

    var currencyBalance = balances is not null && ShopCurrency.IsSupported(shop.Currency)
        ? ShopCurrency.GetBalance(shop.Currency, balances)
        : (int?)null;

    return new ShopState
    {
        Present = true,
        MenuType = shop.MenuType,
        ShopId = shop.ShopId,
        Currency = shop.Currency,
        CurrencyName = ShopCurrency.Name(shop.Currency),
        CurrencyBalance = currencyBalance,
        Items = shop.Items
            .Select(item => new ShopItemSummary
            {
                ItemId = item.ItemId,
                QualifiedId = item.QualifiedId,
                DisplayName = item.DisplayName,
                Price = item.UnitPrice,
                Stock = item.Stock,
                Category = item.Category,
                Quality = item.Quality,
                RuntimeType = item.RuntimeType,
            })
            .ToList(),
    };
}
```

Modify `src/Harness/Handlers/StateShopHandler.cs`:

```csharp
internal static JsonElement Handle(JsonElement? paramsElement, IShopStateWorld world)
    => ProtocolJson.ToElement(ShopStateProjector.Project(world.ActiveShop, world.Balances));
```

and:

```csharp
internal interface IShopStateWorld
{
    IShopMenuState? ActiveShop { get; }
    IShopCurrencyBalances Balances { get; }
}
```

Implement balances in `SdvShopStateWorld`:

```csharp
public IShopCurrencyBalances Balances { get; } = new SdvShopCurrencyBalances();
```

- [ ] **Step 5: Run state shop tests and verify they pass**

Run:

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Harness.Tests/Harness.Tests.csproj --filter FullyQualifiedName~StateShopHandlerTests
```

Expected: PASS.

- [ ] **Step 6: Commit shop state currency projection**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework add src/Harness/Handlers/ShopCurrency.cs src/Harness/Handlers/ShopStateProjector.cs src/Harness/Handlers/StateShopHandler.cs tests/Harness.Tests/StateShopHandlerTests.cs
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework commit -m "feat: expose active shop currency balance"
```

Expected: commit succeeds.

## Task 3: `player.set_shop_currency` Handler

**Files:**

- Create: `src/Harness/Handlers/PlayerSetShopCurrencyHandler.cs`
- Modify: `src/Harness/ModEntry.cs`
- Test: `tests/Harness.Tests/PlayerSetShopCurrencyHandlerTests.cs`

- [ ] **Step 1: Write failing handler tests**

Create `tests/Harness.Tests/PlayerSetShopCurrencyHandlerTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class PlayerSetShopCurrencyHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() =>
            PlayerSetShopCurrencyHandler.Handle(null, new FakeWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_NegativeAmount_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"currency\":1,\"amount\":-1}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            PlayerSetShopCurrencyHandler.Handle(p, new FakeWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("amount", ex.Message);
    }

    [Fact]
    public void Handle_NotWorldReady_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"currency\":1,\"amount\":10000}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            PlayerSetShopCurrencyHandler.Handle(p, new FakeWorld { IsWorldReady = false }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_UnsupportedCurrency_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"currency\":99,\"amount\":10000}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            PlayerSetShopCurrencyHandler.Handle(p, new FakeWorld()));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("99", ex.Message);
    }

    [Fact]
    public void Handle_SetsGoldBalance()
    {
        var p = JsonDocument.Parse("{\"currency\":0,\"amount\":5000}").RootElement;
        var world = new FakeWorld { Money = 100 };

        var result = PlayerSetShopCurrencyHandler.Handle(p, world);
        var set = JsonSerializer.Deserialize<SetShopCurrencyResult>(result, ProtocolJson.Options)!;

        Assert.True(set.Ok);
        Assert.Equal(1234, set.Tick);
        Assert.Equal(0, set.Currency);
        Assert.Equal("gold", set.CurrencyName);
        Assert.Equal(100, set.Previous);
        Assert.Equal(5000, set.Amount);
        Assert.Equal(5000, world.Money);
    }

    [Fact]
    public void Handle_SetsStarTokenBalance()
    {
        var p = JsonDocument.Parse("{\"currency\":1,\"amount\":10000}").RootElement;
        var world = new FakeWorld { FestivalScore = 75 };

        var result = PlayerSetShopCurrencyHandler.Handle(p, world);
        var set = JsonSerializer.Deserialize<SetShopCurrencyResult>(result, ProtocolJson.Options)!;

        Assert.True(set.Ok);
        Assert.Equal(1, set.Currency);
        Assert.Equal("star_tokens", set.CurrencyName);
        Assert.Equal(75, set.Previous);
        Assert.Equal(10000, set.Amount);
        Assert.Equal(10000, world.FestivalScore);
    }

    private sealed class FakeWorld : IPlayerSetShopCurrencyWorld
    {
        public bool IsWorldReady { get; init; } = true;
        public int Tick => 1234;
        public int Money { get; set; } = 30000;
        public int FestivalScore { get; set; }
    }
}
```

- [ ] **Step 2: Run handler tests and verify they fail**

Run:

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Harness.Tests/Harness.Tests.csproj --filter FullyQualifiedName~PlayerSetShopCurrencyHandlerTests
```

Expected: FAIL because `PlayerSetShopCurrencyHandler` and `IPlayerSetShopCurrencyWorld` do not exist.

- [ ] **Step 3: Add the handler implementation**

Create `src/Harness/Handlers/PlayerSetShopCurrencyHandler.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>player.set_shop_currency</c>. Sets a supported shop currency balance.</summary>
public static class PlayerSetShopCurrencyHandler
{
    public const string Method = "player.set_shop_currency";

    private static readonly IPlayerSetShopCurrencyWorld ProductionWorld = new SdvPlayerSetShopCurrencyWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, IPlayerSetShopCurrencyWorld world)
    {
        var req = RpcParams.Required<SetShopCurrencyRequest>(paramsElement);
        if (req.Amount < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.amount must be >= 0");

        if (!world.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "player.set_shop_currency requires a loaded world");

        ShopCurrency.RequireSupported(req.Currency, Method);
        var previous = ShopCurrency.GetBalance(req.Currency, world);
        ShopCurrency.SetBalance(req.Currency, world, req.Amount);

        return ProtocolJson.ToElement(new SetShopCurrencyResult
        {
            Tick = world.Tick,
            Currency = req.Currency,
            CurrencyName = ShopCurrency.Name(req.Currency),
            Previous = previous,
            Amount = req.Amount,
        });
    }
}

internal interface IPlayerSetShopCurrencyWorld : IShopCurrencyBalances
{
    bool IsWorldReady { get; }
    int Tick { get; }
}

internal sealed class SdvPlayerSetShopCurrencyWorld : IPlayerSetShopCurrencyWorld
{
    private readonly SdvShopCurrencyBalances _balances = new();

    public bool IsWorldReady => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;
    public int Tick => Game1.ticks;
    public int Money { get => _balances.Money; set => _balances.Money = value; }
    public int FestivalScore { get => _balances.FestivalScore; set => _balances.FestivalScore = value; }
}
```

Modify `src/Harness/ModEntry.cs` to register the handler after `player.set_money`:

```csharp
_rpc.Register(PlayerSetShopCurrencyHandler.Method, p => PlayerSetShopCurrencyHandler.Handle(p));
```

Update the startup log string so the manipulator list includes:

```text
player.set_money, player.set_shop_currency, player.add_mail
```

- [ ] **Step 4: Run handler tests and verify they pass**

Run:

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Harness.Tests/Harness.Tests.csproj --filter FullyQualifiedName~PlayerSetShopCurrencyHandlerTests
```

Expected: PASS.

- [ ] **Step 5: Commit the setter handler**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework add src/Harness/Handlers/PlayerSetShopCurrencyHandler.cs src/Harness/ModEntry.cs tests/Harness.Tests/PlayerSetShopCurrencyHandlerTests.cs
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework commit -m "feat: add shop currency balance setter"
```

Expected: commit succeeds.

## Task 4: Active-Currency Shop Purchases

**Files:**

- Modify: `src/Harness/Handlers/ShopPurchaseHandler.cs`
- Test: `tests/Harness.Tests/ShopPurchaseHandlerTests.cs`

- [ ] **Step 1: Write failing purchase behavior tests**

In `tests/Harness.Tests/ShopPurchaseHandlerTests.cs`, update `Handle_PurchasesMatchingItemAndReturnsMoneyDelta` to assert gold currency fields:

```csharp
Assert.Equal(0, purchase.Currency);
Assert.Equal(30000, purchase.PreviousCurrencyBalance);
Assert.Equal(5000, purchase.CurrencyBalance);
```

Add a star-token purchase test:

```csharp
[Fact]
public void Handle_StarTokenShop_DebitsFestivalScoreAndPreservesMoney()
{
    var world = new FakeShopPurchaseWorld
    {
        ActiveShop = new FakeShop(currency: 1),
        FestivalScore = 30000,
    };
    var p = JsonDocument.Parse("{\"item_id\":\"(F)terminal\",\"count\":1}").RootElement;

    var result = ShopPurchaseHandler.Handle(p, world);
    var purchase = JsonSerializer.Deserialize<ShopPurchaseResult>(result, ProtocolJson.Options)!;

    Assert.True(purchase.Ok);
    Assert.Equal(1, purchase.Currency);
    Assert.Equal(30000, purchase.PreviousCurrencyBalance);
    Assert.Equal(5000, purchase.CurrencyBalance);
    Assert.Equal(30000, purchase.PreviousMoney);
    Assert.Equal(30000, purchase.Money);
    Assert.Equal(5000, world.FestivalScore);
}
```

Add an unsupported currency test:

```csharp
[Fact]
public void Handle_UnsupportedCurrency_ThrowsGameStateInvalid()
{
    var p = JsonDocument.Parse("{\"item_id\":\"(F)terminal\",\"count\":1}").RootElement;
    var ex = Assert.Throws<JsonRpcException>(() =>
        ShopPurchaseHandler.Handle(p, new FakeShopPurchaseWorld
        {
            ActiveShop = new FakeShop(currency: 99),
        }));

    Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    Assert.Contains("99", ex.Message);
}
```

Update `FakeShopPurchaseWorld`:

```csharp
private sealed class FakeShopPurchaseWorld : IShopPurchaseWorld
{
    public bool IsWorldReady { get; init; } = true;
    public int Tick => 1234;
    public int Money { get; set; } = 30000;
    public int FestivalScore { get; set; }
    public bool PurchaseSucceeds { get; init; } = true;
    public string? PurchasedItemId { get; private set; }
    public string? PurchasedRawItemId { get; private set; }
    public string? PurchasedQualifiedId { get; private set; }
    public int PurchasedCount { get; private set; }
    public IShopMenuState? ActiveShop { get; init; } = new FakeShop();

    public bool Purchase(IShopItem item, int count)
    {
        PurchasedItemId = item.QualifiedId;
        PurchasedRawItemId = item.ItemId;
        PurchasedQualifiedId = item.QualifiedId;
        PurchasedCount = count;
        if (!PurchaseSucceeds)
            return false;

        var total = item.UnitPrice * count;
        var balance = ShopCurrency.GetBalance(ActiveShop!.Currency, this);
        if (balance < total)
            return false;

        ShopCurrency.SetBalance(ActiveShop.Currency, this, balance - total);
        return true;
    }
}
```

Update `FakeShop`:

```csharp
private sealed class FakeShop : IShopMenuState
{
    private readonly int _currency;

    public FakeShop(int currency = 0)
    {
        _currency = currency;
    }

    public string MenuType => "ShopMenu";
    public string ShopId => "Carpenter";
    public int Currency => _currency;
    public IReadOnlyList<IShopItem> Items { get; } = new[]
    {
        new ShopItem("terminal", "(F)terminal", "Terminal", 25000, 1, -9, 0, "Furniture"),
        new ShopItem("388", "(O)388", "Wood", 10, null, -16, 0, "Object"),
    };
}
```

- [ ] **Step 2: Run purchase tests and verify they fail**

Run:

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Harness.Tests/Harness.Tests.csproj --filter FullyQualifiedName~ShopPurchaseHandlerTests
```

Expected: FAIL because `IShopPurchaseWorld` does not expose festival score and the result does not set currency fields.

- [ ] **Step 3: Update purchase handler interface and result**

Modify `src/Harness/Handlers/ShopPurchaseHandler.cs`:

```csharp
ShopCurrency.RequireSupported(shop.Currency, Method);

var previousCurrencyBalance = ShopCurrency.GetBalance(shop.Currency, world);
var previousMoney = world.Money;
if (!world.Purchase(item, req.Count))
    throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
        $"shop.purchase failed for item: {req.ItemId}");

return ProtocolJson.ToElement(new ShopPurchaseResult
{
    Tick = world.Tick,
    ShopId = shop.ShopId,
    ItemId = item.QualifiedId,
    DisplayName = item.DisplayName,
    Count = req.Count,
    UnitPrice = item.UnitPrice,
    Currency = shop.Currency,
    PreviousCurrencyBalance = previousCurrencyBalance,
    CurrencyBalance = ShopCurrency.GetBalance(shop.Currency, world),
    PreviousMoney = previousMoney,
    Money = world.Money,
});
```

Update the interface:

```csharp
internal interface IShopPurchaseWorld : IShopCurrencyBalances
{
    bool IsWorldReady { get; }
    int Tick { get; }
    IShopMenuState? ActiveShop { get; }
    bool Purchase(IShopItem item, int count);
}
```

Update `SdvShopPurchaseWorld` to use `SdvShopCurrencyBalances`:

```csharp
private readonly SdvShopCurrencyBalances _balances = new();

public int Money { get => _balances.Money; set => _balances.Money = value; }
public int FestivalScore { get => _balances.FestivalScore; set => _balances.FestivalScore = value; }
```

Replace the gold-only debit inside `Purchase`:

```csharp
var totalPrice = checked(sdvItem.UnitPrice * count);
ShopCurrency.RequireSupported(sdvItem.Shop.currency, Method);
var balance = ShopCurrency.GetBalance(sdvItem.Shop.currency, this);
if (balance < totalPrice)
    return false;

if (sdvItem.Salable.GetSalableInstance() is not Item purchased)
    return false;

purchased.Stack = count;
ShopCurrency.SetBalance(sdvItem.Shop.currency, this, balance - totalPrice);
Game1.player.addItemByMenuIfNecessary(purchased);
sdvItem.Salable.actionWhenPurchased(sdvItem.Shop.ShopId);
return true;
```

- [ ] **Step 4: Run purchase tests and verify they pass**

Run:

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Harness.Tests/Harness.Tests.csproj --filter FullyQualifiedName~ShopPurchaseHandlerTests
```

Expected: PASS.

- [ ] **Step 5: Commit active-currency purchase work**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework add src/Harness/Handlers/ShopPurchaseHandler.cs tests/Harness.Tests/ShopPurchaseHandlerTests.cs
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework commit -m "feat: debit active shop currency on purchase"
```

Expected: commit succeeds.

## Task 5: DSL and Documentation

**Files:**

- Modify: `src/Runner.Dsl/Player.cs`
- Modify: `tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs`
- Modify: `tests/Runner.Dsl.Tests/Facets/StateTests.cs`
- Modify: `docs/rpc-schema.md`

- [ ] **Step 1: Write failing DSL/state tests**

In `tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs`, add near `SetMoney_InvokesPlayerSetMoneyWithAmount`:

```csharp
[Fact]
public async Task SetShopCurrency_InvokesPlayerSetShopCurrencyWithCurrencyAndAmount()
{
    SdvTestSession.ResetForTests();
    var inv = new CapturingInvoker();
    SdvTestSession.InitializeForTests(inv);
    try { await Player.SetShopCurrency(1, 10000); }
    finally { SdvTestSession.ResetForTests(); }

    Assert.Equal("player.set_shop_currency", inv.Calls[0].Method);
    Assert.Contains("\"currency\":1", inv.Calls[0].ParamsJson);
    Assert.Contains("\"amount\":10000", inv.Calls[0].ParamsJson);
}
```

In `tests/Runner.Dsl.Tests/Facets/StateTests.cs`, update the `Shop_InvokesStateShopAndDeserializes` fake response:

```json
{"present":true,"menu_type":"ShopMenu","shop_id":"ExampleMod.CustomVendor","currency":1,"currency_name":"star_tokens","currency_balance":10000,"items":[{"item_id":"ExampleMod.CustomDrink","qualified_id":"(O)ExampleMod.CustomDrink","display_name":"Custom Drink","price":4000,"stock":5,"category":0,"quality":0,"runtime_type":"Object"}]}
```

Add assertions:

```csharp
Assert.Equal(1, shop.Currency);
Assert.Equal("star_tokens", shop.CurrencyName);
Assert.Equal(10000, shop.CurrencyBalance);
```

- [ ] **Step 2: Run DSL tests and verify they fail**

Run:

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter "FullyQualifiedName~PlayerWorldTimeTests|FullyQualifiedName~StateTests"
```

Expected: FAIL because `Player.SetShopCurrency` does not exist.

- [ ] **Step 3: Add DSL wrapper**

Modify `src/Runner.Dsl/Player.cs` after `SetMoney`:

```csharp
/// <summary>Set a supported shop currency balance. Supported initially: 0 = gold, 1 = star tokens.</summary>
public static async Task SetShopCurrency(int currency, int amount, CancellationToken ct = default)
{
    var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
    var p = JsonSerializer.SerializeToElement(new SetShopCurrencyRequest
    {
        Currency = currency,
        Amount = amount,
    }, ProtocolJson.Options);
    await s.InvokeAsync("player.set_shop_currency", p, ct);
}
```

- [ ] **Step 4: Update RPC docs**

In `docs/rpc-schema.md`, update the `state.shop` example to include:

```json
"currency": 1,
"currency_name": "star_tokens",
"currency_balance": 10000,
```

Update the `state.shop` text:

```markdown
`currency` follows Stardew's shop currency codes; `0` is gold and `1` is
Stardew Fair star tokens. `currency_name` is Frobby's stable label for known
codes. `currency_balance` is the player's current balance for that shop
currency when Frobby knows how to read it; unsupported currencies omit the
balance so scenarios do not accidentally treat gold as a fallback.
```

Add a `player.set_shop_currency` section near `player.set_money`:

````markdown
### player.set_shop_currency

Sets a supported shop currency balance to an absolute amount. This is useful for
festival or special shops whose active `ShopMenu.currency` is not gold.

Request:
```json
→ { "jsonrpc": "2.0", "id": 17, "method": "player.set_shop_currency",
    "params": { "currency": 1, "amount": 10000 } }
```

Response:
```json
← { "jsonrpc": "2.0", "id": 17, "result": {
      "ok": true,
      "tick": 42,
      "currency": 1,
      "currency_name": "star_tokens",
      "previous": 75,
      "amount": 10000
   } }
```

Supported currencies are `0` for gold and `1` for Stardew Fair star tokens.
Unsupported currencies fail explicitly.

**Preconditions:** loaded world.
**Side effects:** sets `Game1.player.Money` for currency `0` or
`Game1.player.festivalScore` for currency `1`.
**Implemented in:** `src/Harness/Handlers/PlayerSetShopCurrencyHandler.cs`
**Tested in:** `tests/Protocol.Tests/SetMoneyRequestSerializationTests.cs` and
`tests/Harness.Tests/PlayerSetShopCurrencyHandlerTests.cs`.
````

Update the `shop.purchase` section so its response includes:

```json
"currency": 1,
"previous_currency_balance": 10000,
"currency_balance": 1,
"previous_money": 5000,
"money": 5000
```

and note that money remains unchanged for non-gold shop purchases.

- [ ] **Step 5: Run DSL tests and a docs smoke check**

Run:

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter "FullyQualifiedName~PlayerWorldTimeTests|FullyQualifiedName~StateTests"
rg -n "player.set_shop_currency|currency_balance|previous_currency_balance" /home/fintan/stardewRepos/frobby/sdv-test-framework/docs/rpc-schema.md
```

Expected: tests PASS; `rg` finds all documented fields/methods.

- [ ] **Step 6: Commit DSL and docs**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework add src/Runner.Dsl/Player.cs tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs tests/Runner.Dsl.Tests/Facets/StateTests.cs docs/rpc-schema.md
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework commit -m "docs: document shop currency testing flow"
```

Expected: commit succeeds.

## Task 6: SVE Fair Scenario

**Files:**

- Create: `tests/sdv/34-sve-fair-star-token-shop-currency.test.json`
- Modify: `docs/FROBBY.md`

- [ ] **Step 1: Add the SVE scenario**

Create `tests/sdv/34-sve-fair-star-token-shop-currency.test.json`:

```json
{
  "name": "sve_fair_star_token_shop_currency",
  "fixture": "m0spike_436515781",
  "config": { "seed": 42 },
  "steps": [
    { "action": "player.set_money", "args": { "amount": 5000 } },
    { "action": "time.set", "args": { "time": 900, "day": 16, "season": "fall", "year": 1 } },
    { "action": "festival.start", "args": { "location": "Town" } },
    {
      "action": "wait.event_active",
      "args": {
        "location": "Town",
        "id": "fall16",
        "is_festival": true,
        "timeout_ms": 30000,
        "poll_ms": 100
      }
    },
    {
      "action": "shop.open",
      "args": {
        "shop_id": "Festival_StardewValleyFair_StarTokens",
        "force_open": true
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
        "message": "Stardew Fair star-token shop should open a live ShopMenu"
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.shop.shop_id == 'Festival_StardewValleyFair_StarTokens'",
        "message": "Fair shop should expose the star-token shop ID"
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.shop.currency == 1",
        "message": "Fair shop should use Stardew star-token currency"
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.shop.currency_name == 'star_tokens'",
        "message": "Frobby should label Stardew Fair currency as star tokens"
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.shop.items contains item_id 'FlashShifter.StardewValleyExpandedCP_Furniture_Catalogue_2'",
        "message": "Fair star-token shop should include SVE Furniture Catalogue 2"
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.shop.items contains qualified_id '(F)FlashShifter.StardewValleyExpandedCP_Furniture_Catalogue_2'",
        "message": "SVE Fair catalogue should be exposed as furniture"
      }
    },
    {
      "action": "player.set_shop_currency",
      "args": {
        "currency": 1,
        "amount": 10000
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.shop.currency_balance == 10000",
        "message": "Fair star-token test balance should be visible through state.shop"
      }
    },
    {
      "action": "shop.purchase",
      "args": {
        "item_id": "FlashShifter.StardewValleyExpandedCP_Furniture_Catalogue_2",
        "count": 1
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.shop.currency_balance == 1",
        "message": "Buying the 9999-token SVE catalogue from 10000 tokens should leave 1 token"
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.player.money == 5000",
        "message": "Star-token purchase should not debit player gold"
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.player.items contains qualified_id '(F)FlashShifter.StardewValleyExpandedCP_Furniture_Catalogue_2'",
        "message": "Purchased SVE Fair catalogue should be visible in player inventory"
      }
    },
    {
      "action": "screenshot.capture_next_frame",
      "args": { "name": "final" }
    }
  ],
  "assertions": []
}
```

- [ ] **Step 2: Document the scenario in SVE**

Append to `docs/FROBBY.md` near the scenario list:

```markdown
Scenario `tests/sdv/34-sve-fair-star-token-shop-currency.test.json` covers
non-gold festival shop currency. It enters the Stardew Fair, opens
`Festival_StardewValleyFair_StarTokens`, proves SVE's Furniture Catalogue 2 is
present, sets the neutral Frobby shop currency balance for currency `1`, buys
the catalogue, and verifies star tokens changed while player gold did not.
Frobby owns the neutral currency primitives; SVE IDs remain in the repo-local
scenario only.
```

- [ ] **Step 3: Commit SVE scenario work**

Run:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded add tests/sdv/34-sve-fair-star-token-shop-currency.test.json docs/FROBBY.md
git -C /home/fintan/stardewRepos/StardewValleyExpanded commit -m "test: add Fair star-token shop currency scenario"
```

Expected: commit succeeds on the SVE feature branch.

## Task 7: Verification

**Files:** none

- [ ] **Step 1: Run focused Frobby tests**

Run:

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Protocol.Tests/Protocol.Tests.csproj --filter "FullyQualifiedName~ShopRequestSerializationTests|FullyQualifiedName~SetMoneyRequestSerializationTests"
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~StateShopHandlerTests|FullyQualifiedName~ShopPurchaseHandlerTests|FullyQualifiedName~PlayerSetShopCurrencyHandlerTests"
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter "FullyQualifiedName~PlayerWorldTimeTests|FullyQualifiedName~StateTests"
```

Expected: all focused Frobby test commands PASS.

- [ ] **Step 2: Run broader Frobby regression tests**

Run:

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Protocol.Tests/Protocol.Tests.csproj
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Harness.Tests/Harness.Tests.csproj
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Runner.Tests/Runner.Tests.csproj
```

Expected: all projects PASS with the existing skipped-test counts.

- [ ] **Step 3: Run SVE live scenarios headless**

Run:

```bash
cd /home/fintan/stardewRepos/StardewValleyExpanded
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-26-star-token-shop tests/sdv/34-sve-fair-star-token-shop-currency.test.json
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-26-flower-regression tests/sdv/33-sve-flower-dance-shop-flow.test.json
```

Expected: both scenario runs PASS. The new run proves star tokens debit to `1` while gold remains `5000`; the regression run proves gold festival shop purchase still debits gold.

- [ ] **Step 4: Inspect git state**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
```

Expected: both feature branches are clean.

## Task 8: Integration Prep

**Files:**

- Frobby branch: `feature/sve-slice-26-shop-currency`
- SVE branch: `feature/frobby-sve-slice-26-star-token-shop`

- [ ] **Step 1: Summarize verification evidence**

Record the exact pass counts and report directories from Task 7 in the final handoff.

- [ ] **Step 2: Wait for merge approval**

Frobby may be merged to `main` after passing verification. SVE must not be merged to `master` until the user explicitly approves that merge.

## Self-Review

Spec coverage:

- Active shop currency projection is covered by Task 2.
- Neutral balance setting is covered by Task 3.
- Active-currency purchase debit and legacy money fields are covered by Task 4.
- DSL/docs updates are covered by Task 5.
- SVE Fair proof scenario is covered by Task 6.
- Existing gold festival regression is covered by Task 7.

Placeholder scan:

- The plan contains no unresolved placeholder steps or deferred test instructions.

Type consistency:

- Protocol DTO names are `SetShopCurrencyRequest`, `SetShopCurrencyResult`, `ShopState.CurrencyName`, `ShopState.CurrencyBalance`, `ShopPurchaseResult.Currency`, `ShopPurchaseResult.PreviousCurrencyBalance`, and `ShopPurchaseResult.CurrencyBalance`.
- Harness interfaces consistently use `IShopCurrencyBalances` for `Money` and `FestivalScore`.
- The JSON-RPC method name is consistently `player.set_shop_currency`.
