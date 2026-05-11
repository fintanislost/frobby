# SVE Slice 10 Special Orders Drop Boxes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add neutral Frobby support for inspecting Stardew special orders, waiting for order state, depositing into donation drop boxes, and proving the flow against core Stardew Valley Expanded.

**Architecture:** Add additive protocol models and a new `state.special_orders` harness RPC that projects live Stardew `Game1.player.team` order state through small testable abstractions. Add runner-side `wait.special_order` polling over that RPC, then add a neutral `drop_box.deposit` harness action that validates an active donation objective and updates Stardew runtime state through generic special-order APIs or a generic fallback. SVE remains only a testbed: all SVE keys, event ids, item ids, and locations live in SVE scenario JSON.

**Tech Stack:** C#/.NET 10 and .NET 6 projects, xUnit, SMAPI/Stardew Valley runtime APIs, Frobby JSON-RPC protocol, Frobby scenario runner JSON, SVE repo-local `scripts/sdv-test`.

---

## Branch And Repo Notes

Frobby work starts from:

```bash
cd /home/fintan/stardewRepos/frobby/sdv-test-framework
git status --short --branch
git worktree add .worktrees/sve-slice-10-special-orders -b feature/sve-slice-10-special-orders
cd .worktrees/sve-slice-10-special-orders
```

Expected: main is clean before creating the worktree. `.worktrees/` already exists and is ignored in this repo.

SVE scenario work starts from the current SVE Frobby branch that contains scenarios 01-14:

```bash
cd /home/fintan/stardewRepos/StardewValleyExpanded
git status --short --branch
git switch -c feature/frobby-sve-slice-10-special-orders
```

Do not merge SVE back to `master` unless the user explicitly asks. Frobby can merge to `main` after review and verification.

Use headless SVE runs with:

```bash
env SDV_TEST_MOD_CACHE=/home/fintan/stardewRepos/frobby/sdv-test-framework/.cache/deps FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-10-special-orders ./scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-10 /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/15-sve-special-order-drop-box.test.json
```

## File Structure

Frobby:

- Create `src/Protocol/Models/SpecialOrdersState.cs`
  - `SpecialOrdersState`, `SpecialOrderSummary`, `SpecialOrderObjectiveSummary`, `SpecialOrderRewardSummary`, `SpecialOrderItemSummary`, and shared lightweight key/value summaries for selected order data.
- Create `src/Protocol/Models/DropBoxDepositRequest.cs`
  - Request and result models for `drop_box.deposit`.
- Create `src/Harness/Handlers/StateSpecialOrdersHandler.cs`
  - RPC handler, runtime world abstraction, projector, and fake-friendly interfaces.
- Create `src/Harness/Handlers/DropBoxDepositHandler.cs`
  - RPC handler, runtime donation world abstraction, validation, inventory item selection, and deposit result shaping.
- Modify `src/Harness/ModEntry.cs`
  - Register `state.special_orders` and `drop_box.deposit`.
- Modify `src/Runner/Scenarios/ScenarioRunner.cs`
  - Add `wait.special_order` dispatch, filtering, diagnostics, descriptions, and passive screenshot suppression.
- Modify docs after implementation:
  - `README.md`
  - `docs/rpc-schema.md`
  - `docs/dsl-quickstart.md`
  - `SVE_FROBBY_CAPABILITY_TODO.md`
- Tests:
  - Create `tests/Protocol.Tests/SpecialOrdersStateSerializationTests.cs`
  - Create `tests/Protocol.Tests/DropBoxDepositSerializationTests.cs`
  - Create `tests/Harness.Tests/StateSpecialOrdersHandlerTests.cs`
  - Create `tests/Harness.Tests/DropBoxDepositHandlerTests.cs`
  - Modify `tests/Runner.Tests/ScenarioRunnerTests.cs`

SVE:

- Add `tests/sdv/15-sve-special-order-drop-box.test.json` after probing a stable order.
- Modify `docs/FROBBY.md` if the repo-local testing notes need the new scenario listed.

---

### Task 1: Special Order Protocol Model

**Files:**
- Create: `src/Protocol/Models/SpecialOrdersState.cs`
- Test: `tests/Protocol.Tests/SpecialOrdersStateSerializationTests.cs`

- [ ] **Step 1: Write the failing serialization test**

Create `tests/Protocol.Tests/SpecialOrdersStateSerializationTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class SpecialOrdersStateSerializationTests
{
    [Fact]
    public void Serialize_SnakeCaseFields()
    {
        var state = new SpecialOrdersState
        {
            Active =
            {
                new SpecialOrderSummary
                {
                    Key = "Andy",
                    Name = "For The Farm",
                    Description = "Bring supplies.",
                    Requester = "Andy",
                    OrderType = "StardewValleyExpanded",
                    SpecialRule = "NONE",
                    Duration = "TwoWeeks",
                    DueDate = 42,
                    State = "InProgress",
                    ReadyForRemoval = false,
                    IsTimed = false,
                    RuntimeType = "SpecialOrder",
                    SelectedRandomElements = { new SpecialOrderKeyValueSummary { Key = "Treasure", Value = "0" } },
                    PreselectedItems = { new SpecialOrderKeyValueSummary { Key = "FishType", Value = "(O)136" } },
                    Objectives =
                    {
                        new SpecialOrderObjectiveSummary
                        {
                            Index = 0,
                            Type = "Donate",
                            RuntimeType = "DonateObjective",
                            Description = "Place wood in the chest.",
                            CurrentCount = 25,
                            MaxCount = 500,
                            Complete = false,
                            DropBox = "AndyChest",
                            DropBoxLocation = "Custom_AndyHouse",
                            DropBoxTile = new TilePoint { X = 12, Y = 5 },
                            AcceptedContextTags = { "item_wood" },
                            Confirmed = false,
                            MinimumCapacity = -1,
                        },
                    },
                    Rewards =
                    {
                        new SpecialOrderRewardSummary
                        {
                            Index = 0,
                            Type = "MoneyReward",
                            RuntimeType = "MoneyReward",
                            Amount = 5362,
                            Mail = { "AndyCellar" },
                        },
                    },
                    DonatedItems =
                    {
                        new SpecialOrderItemSummary
                        {
                            Id = "(O)388",
                            ItemId = "388",
                            QualifiedId = "(O)388",
                            Name = "Wood",
                            Stack = 25,
                            Quality = 0,
                            Category = -15,
                            RuntimeType = "Object",
                        },
                    },
                },
            },
            Available = { new SpecialOrderSummary { Key = "MarlonFay2", Requester = "MarlonFay" } },
            Completed = { "Andy" },
            AcceptedTypes = { "Qi", "StardewValleyExpanded" },
            ReturnedDonations =
            {
                new SpecialOrderItemSummary { Id = "(O)388", ItemId = "388", QualifiedId = "(O)388", Name = "Wood", Stack = 1 },
            },
        };

        var json = JsonSerializer.Serialize(state, ProtocolJson.Options);

        Assert.Contains("\"active\"", json);
        Assert.Contains("\"available\"", json);
        Assert.Contains("\"completed\":[\"Andy\"]", json);
        Assert.Contains("\"accepted_types\":[\"Qi\",\"StardewValleyExpanded\"]", json);
        Assert.Contains("\"order_type\":\"StardewValleyExpanded\"", json);
        Assert.Contains("\"ready_for_removal\":false", json);
        Assert.Contains("\"selected_random_elements\":[{\"key\":\"Treasure\",\"value\":\"0\"}]", json);
        Assert.Contains("\"drop_box\":\"AndyChest\"", json);
        Assert.Contains("\"drop_box_tile\":{\"x\":12,\"y\":5}", json);
        Assert.Contains("\"accepted_context_tags\":[\"item_wood\"]", json);
        Assert.Contains("\"donated_items\":[{\"id\":\"(O)388\"", json);
        Assert.Contains("\"returned_donations\":[{\"id\":\"(O)388\"", json);
    }
}
```

- [ ] **Step 2: Run the test and verify RED**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter SpecialOrdersStateSerializationTests
```

Expected: FAIL because the special-order protocol model types do not exist.

- [ ] **Step 3: Add the protocol model**

Create `src/Protocol/Models/SpecialOrdersState.cs`:

```csharp
using System.Collections.Generic;

namespace SdvTestFramework.Protocol.Models;

/// <summary>Snapshot of Stardew team special-order state. Response shape of <c>state.special_orders</c>.</summary>
public sealed class SpecialOrdersState
{
    public List<SpecialOrderSummary> Active { get; set; } = new();
    public List<SpecialOrderSummary> Available { get; set; } = new();
    public List<string> Completed { get; set; } = new();
    public List<string> AcceptedTypes { get; set; } = new();
    public List<SpecialOrderItemSummary> ReturnedDonations { get; set; } = new();
}

public sealed class SpecialOrderSummary
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Requester { get; set; } = string.Empty;
    public string OrderType { get; set; } = string.Empty;
    public string SpecialRule { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public int? DueDate { get; set; }
    public string State { get; set; } = string.Empty;
    public bool? ReadyForRemoval { get; set; }
    public bool? IsTimed { get; set; }
    public string RuntimeType { get; set; } = string.Empty;
    public List<SpecialOrderKeyValueSummary> SelectedRandomElements { get; set; } = new();
    public List<SpecialOrderKeyValueSummary> PreselectedItems { get; set; } = new();
    public List<SpecialOrderObjectiveSummary> Objectives { get; set; } = new();
    public List<SpecialOrderRewardSummary> Rewards { get; set; } = new();
    public List<SpecialOrderItemSummary> DonatedItems { get; set; } = new();
}

public sealed class SpecialOrderKeyValueSummary
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public sealed class SpecialOrderObjectiveSummary
{
    public int Index { get; set; }
    public string Type { get; set; } = string.Empty;
    public string RuntimeType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? CurrentCount { get; set; }
    public int? MaxCount { get; set; }
    public bool? Complete { get; set; }
    public string DropBox { get; set; } = string.Empty;
    public string DropBoxLocation { get; set; } = string.Empty;
    public TilePoint? DropBoxTile { get; set; }
    public string TargetName { get; set; } = string.Empty;
    public List<string> AcceptedContextTags { get; set; } = new();
    public bool? Confirmed { get; set; }
    public int? MinimumCapacity { get; set; }
}

public sealed class SpecialOrderRewardSummary
{
    public int Index { get; set; }
    public string Type { get; set; } = string.Empty;
    public string RuntimeType { get; set; } = string.Empty;
    public int? Amount { get; set; }
    public List<string> Mail { get; set; } = new();
}

public sealed class SpecialOrderItemSummary
{
    public string Id { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string QualifiedId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Stack { get; set; }
    public int? Quality { get; set; }
    public int? Category { get; set; }
    public string RuntimeType { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Run the test and verify GREEN**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter SpecialOrdersStateSerializationTests
```

Expected: PASS.

- [ ] **Step 5: Commit Task 1**

```bash
git add src/Protocol/Models/SpecialOrdersState.cs tests/Protocol.Tests/SpecialOrdersStateSerializationTests.cs
git commit -m "feat: add special order state protocol model"
```

---

### Task 2: Project Special Order Runtime State

**Files:**
- Create: `src/Harness/Handlers/StateSpecialOrdersHandler.cs`
- Modify: `src/Harness/ModEntry.cs`
- Test: `tests/Harness.Tests/StateSpecialOrdersHandlerTests.cs`

- [ ] **Step 1: Write failing projection tests**

Create `tests/Harness.Tests/StateSpecialOrdersHandlerTests.cs` with fake interfaces that match the handler abstractions:

```csharp
using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class StateSpecialOrdersHandlerTests
{
    [Fact]
    public void Handle_ProjectsActiveAvailableCompletedAndReturnedDonations()
    {
        var world = new FakeSpecialOrdersWorld
        {
            Active =
            {
                new FakeSpecialOrder
                {
                    Key = "Andy",
                    Name = "For The Farm",
                    Description = "Bring supplies.",
                    Requester = "Andy",
                    OrderType = "StardewValleyExpanded",
                    SpecialRule = "",
                    Duration = "TwoWeeks",
                    DueDate = 42,
                    State = "InProgress",
                    ReadyForRemoval = false,
                    IsTimed = false,
                    RuntimeType = "SpecialOrder",
                    SelectedRandomElements = { ["Treasure"] = "0" },
                    PreselectedItems = { ["FishType"] = "(O)136" },
                    Objectives =
                    {
                        new FakeSpecialOrderObjective
                        {
                            Type = "Donate",
                            RuntimeType = "DonateObjective",
                            Description = "Bring wood.",
                            CurrentCount = 25,
                            MaxCount = 500,
                            Complete = false,
                            DropBox = "AndyChest",
                            DropBoxLocation = "Custom_AndyHouse",
                            DropBoxTile = new TilePoint { X = 12, Y = 5 },
                            AcceptedContextTags = { "item_wood" },
                            Confirmed = false,
                            MinimumCapacity = -1,
                        },
                    },
                    Rewards =
                    {
                        new FakeSpecialOrderReward
                        {
                            Type = "MoneyReward",
                            RuntimeType = "MoneyReward",
                            Amount = 5362,
                            Mail = { "AndyCellar" },
                        },
                    },
                    DonatedItems =
                    {
                        new FakeSpecialOrderItem
                        {
                            Id = "(O)388",
                            ItemId = "388",
                            QualifiedId = "(O)388",
                            Name = "Wood",
                            Stack = 25,
                            Quality = 0,
                            Category = -15,
                            RuntimeType = "Object",
                        },
                    },
                },
            },
            Available = { new FakeSpecialOrder { Key = "MarlonFay2", Requester = "MarlonFay" } },
            Completed = { "Andy" },
            AcceptedTypes = { "StardewValleyExpanded" },
            ReturnedDonations =
            {
                new FakeSpecialOrderItem { Id = "(O)390", ItemId = "390", QualifiedId = "(O)390", Name = "Stone", Stack = 1 },
            },
        };

        var result = StateSpecialOrdersHandler.Handle(paramsElement: null, world);
        var state = JsonSerializer.Deserialize<SpecialOrdersState>(result.GetRawText(), ProtocolJson.Options)!;

        Assert.Collection(state.Active, order =>
        {
            Assert.Equal("Andy", order.Key);
            Assert.Equal("StardewValleyExpanded", order.OrderType);
            Assert.False(order.IsTimed);
            Assert.Collection(order.SelectedRandomElements, item => Assert.Equal("Treasure", item.Key));
            Assert.Collection(order.Objectives, objective =>
            {
                Assert.Equal("Donate", objective.Type);
                Assert.Equal("AndyChest", objective.DropBox);
                Assert.Equal("Custom_AndyHouse", objective.DropBoxLocation);
                Assert.Equal(12, objective.DropBoxTile!.X);
                Assert.Equal(25, objective.CurrentCount);
                Assert.Contains("item_wood", objective.AcceptedContextTags);
            });
            Assert.Collection(order.Rewards, reward =>
            {
                Assert.Equal("MoneyReward", reward.Type);
                Assert.Equal(5362, reward.Amount);
                Assert.Contains("AndyCellar", reward.Mail);
            });
            Assert.Collection(order.DonatedItems, item => Assert.Equal("(O)388", item.QualifiedId));
        });
        Assert.Collection(state.Available, order => Assert.Equal("MarlonFay2", order.Key));
        Assert.Contains("Andy", state.Completed);
        Assert.Contains("StardewValleyExpanded", state.AcceptedTypes);
        Assert.Collection(state.ReturnedDonations, item => Assert.Equal("(O)390", item.QualifiedId));
    }

    [Fact]
    public void Handle_ToleratesSparseUnknownRuntimeTypes()
    {
        var world = new FakeSpecialOrdersWorld
        {
            Active =
            {
                new FakeSpecialOrder
                {
                    Key = "UnknownOrder",
                    RuntimeType = "ModdedOrder",
                    Objectives = { new FakeSpecialOrderObjective { RuntimeType = "CustomObjective" } },
                    Rewards = { new FakeSpecialOrderReward { RuntimeType = "CustomReward" } },
                },
            },
        };

        var result = StateSpecialOrdersHandler.Handle(paramsElement: null, world);
        var state = JsonSerializer.Deserialize<SpecialOrdersState>(result.GetRawText(), ProtocolJson.Options)!;

        Assert.Equal("UnknownOrder", state.Active[0].Key);
        Assert.Equal("ModdedOrder", state.Active[0].RuntimeType);
        Assert.Equal("CustomObjective", state.Active[0].Objectives[0].RuntimeType);
        Assert.Equal("CustomReward", state.Active[0].Rewards[0].RuntimeType);
    }
}
```

The fake classes should implement the interfaces introduced in Step 3:

```csharp
private sealed class FakeSpecialOrdersWorld : ISpecialOrdersWorld
{
    public List<ISpecialOrderSource> Active { get; } = new();
    public List<ISpecialOrderSource> Available { get; } = new();
    public List<string> Completed { get; } = new();
    public List<string> AcceptedTypes { get; } = new();
    public List<ISpecialOrderItemSource> ReturnedDonations { get; } = new();
}
```

Use similar simple fake records for `ISpecialOrderSource`, `ISpecialOrderObjectiveSource`, `ISpecialOrderRewardSource`, and `ISpecialOrderItemSource`.

- [ ] **Step 2: Run the tests and verify RED**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter StateSpecialOrdersHandlerTests
```

Expected: FAIL because `StateSpecialOrdersHandler` and interfaces do not exist.

- [ ] **Step 3: Implement handler and testable projection**

Create `src/Harness/Handlers/StateSpecialOrdersHandler.cs` with this shape:

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using StardewValley.SpecialOrders;

namespace SdvTestFramework.Harness.Handlers;

public static class StateSpecialOrdersHandler
{
    public const string Method = "state.special_orders";

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, new SdvSpecialOrdersWorld());

    internal static JsonElement Handle(JsonElement? paramsElement, ISpecialOrdersWorld world)
    {
        var state = new SpecialOrdersState
        {
            Active = world.Active.Select(ProjectOrder).ToList(),
            Available = world.Available.Select(ProjectOrder).ToList(),
            Completed = world.Completed.Select(s => s ?? string.Empty).ToList(),
            AcceptedTypes = world.AcceptedTypes.Select(s => s ?? string.Empty).ToList(),
            ReturnedDonations = world.ReturnedDonations.Select(ProjectItem).ToList(),
        };
        return ProtocolJson.ToElement(state);
    }

    internal static SpecialOrderSummary ProjectOrder(ISpecialOrderSource order)
    {
        return new SpecialOrderSummary
        {
            Key = order.Key,
            Name = order.Name,
            Description = order.Description,
            Requester = order.Requester,
            OrderType = order.OrderType,
            SpecialRule = order.SpecialRule,
            Duration = order.Duration,
            DueDate = order.DueDate,
            State = order.State,
            ReadyForRemoval = order.ReadyForRemoval,
            IsTimed = order.IsTimed,
            RuntimeType = order.RuntimeType,
            SelectedRandomElements = order.SelectedRandomElements.Select(ProjectKeyValue).ToList(),
            PreselectedItems = order.PreselectedItems.Select(ProjectKeyValue).ToList(),
            Objectives = order.Objectives.Select((objective, index) => ProjectObjective(objective, index)).ToList(),
            Rewards = order.Rewards.Select((reward, index) => ProjectReward(reward, index)).ToList(),
            DonatedItems = order.DonatedItems.Select(ProjectItem).ToList(),
        };
    }

    private static SpecialOrderKeyValueSummary ProjectKeyValue(KeyValuePair<string, string> pair)
        => new() { Key = pair.Key, Value = pair.Value };

    private static SpecialOrderObjectiveSummary ProjectObjective(ISpecialOrderObjectiveSource objective, int index)
        => new()
        {
            Index = index,
            Type = objective.Type,
            RuntimeType = objective.RuntimeType,
            Description = objective.Description,
            CurrentCount = objective.CurrentCount,
            MaxCount = objective.MaxCount,
            Complete = objective.Complete,
            DropBox = objective.DropBox,
            DropBoxLocation = objective.DropBoxLocation,
            DropBoxTile = objective.DropBoxTile,
            TargetName = objective.TargetName,
            AcceptedContextTags = objective.AcceptedContextTags.ToList(),
            Confirmed = objective.Confirmed,
            MinimumCapacity = objective.MinimumCapacity,
        };

    private static SpecialOrderRewardSummary ProjectReward(ISpecialOrderRewardSource reward, int index)
        => new()
        {
            Index = index,
            Type = reward.Type,
            RuntimeType = reward.RuntimeType,
            Amount = reward.Amount,
            Mail = reward.Mail.ToList(),
        };

    internal static SpecialOrderItemSummary ProjectItem(ISpecialOrderItemSource item)
        => new()
        {
            Id = item.Id,
            ItemId = item.ItemId,
            QualifiedId = item.QualifiedId,
            Name = item.Name,
            Stack = item.Stack,
            Quality = item.Quality,
            Category = item.Category,
            RuntimeType = item.RuntimeType,
        };
}
```

Add `internal` interfaces in the same file matching the fake types. Then add `SdvSpecialOrdersWorld`, `SdvSpecialOrderSource`, `SdvSpecialOrderObjectiveSource`, `SdvSpecialOrderRewardSource`, and `SdvSpecialOrderItemSource` wrappers at the bottom of the same file. Runtime wrappers should read:

```csharp
internal sealed class SdvSpecialOrdersWorld : ISpecialOrdersWorld
{
    public IReadOnlyList<ISpecialOrderSource> Active
    {
        get
        {
            RpcPreconditions.RequireWorldLoaded("state.special_orders");
            return Game1.player.team.specialOrders.Select(order => new SdvSpecialOrderSource(order)).ToList();
        }
    }

    public IReadOnlyList<ISpecialOrderSource> Available
        => Game1.player.team.availableSpecialOrders.Select(order => new SdvSpecialOrderSource(order)).ToList();

    public IReadOnlyList<string> Completed
        => Game1.player.team.completedSpecialOrders.Select(value => value ?? string.Empty).ToList();

    public IReadOnlyList<string> AcceptedTypes
        => Game1.player.team.acceptedSpecialOrderTypes.Select(value => value ?? string.Empty).ToList();

    public IReadOnlyList<ISpecialOrderItemSource> ReturnedDonations
        => Game1.player.team.returnedDonations.Where(item => item is not null).Select(item => new SdvSpecialOrderItemSource(item)).ToList();
}
```

For runtime wrappers, use direct Stardew properties when available and reflection helpers for field/property names:

```csharp
private static string ReadString(object source, params string[] names)
private static int? ReadInt(object source, params string[] names)
private static bool? ReadBool(object source, params string[] names)
private static IReadOnlyList<string> ReadStringList(object source, params string[] names)
private static IReadOnlyDictionary<string, string> ReadStringDictionary(object source, params string[] names)
```

Read `Net*` wrapper values by unwrapping `Value` properties before converting. Objective wrappers should detect `DonateObjective` through `GetType().Name` or runtime type and read `dropBox`, `dropBoxGameLocation`, `dropBoxTileLocation`, `acceptableContextTagSets`, `currentCount`, `maxCount`, `description`, `confirmed`, and `minimumCapacity`. Reward wrappers should read `amount`, `grantedMails`, and equivalent scalar/list fields when exposed.

- [ ] **Step 4: Register the RPC**

In `src/Harness/ModEntry.cs`, add the state RPC near the other state handlers:

```csharp
_rpc.Register(StateSpecialOrdersHandler.Method, p => StateSpecialOrdersHandler.Handle(p));
```

- [ ] **Step 5: Run tests and verify GREEN**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter StateSpecialOrdersHandlerTests
```

Expected: PASS.

- [ ] **Step 6: Commit Task 2**

```bash
git add src/Harness/Handlers/StateSpecialOrdersHandler.cs src/Harness/ModEntry.cs tests/Harness.Tests/StateSpecialOrdersHandlerTests.cs
git commit -m "feat: project special order runtime state"
```

---

### Task 3: Runner Wait For Special Orders

**Files:**
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`
- Test: `tests/Runner.Tests/ScenarioRunnerTests.cs`

- [ ] **Step 1: Write failing runner tests**

Append tests to `tests/Runner.Tests/ScenarioRunnerTests.cs`:

```csharp
[Fact]
public async Task WaitSpecialOrder_PollsUntilActiveOrderObjectiveMatches()
{
    var socket = SocketPath();
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var polls = 0;

    var serverTask = Task.Run(async () =>
    {
        await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
        {
            session.RequestReceived += async req =>
            {
                JsonElement r = req.Method switch
                {
                    "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                    "state.special_orders" => JsonDocument.Parse(polls++ == 0
                        ? "{\"active\":[],\"available\":[],\"completed\":[],\"accepted_types\":[],\"returned_donations\":[]}"
                        : "{\"active\":[{\"key\":\"Andy\",\"requester\":\"Andy\",\"order_type\":\"StardewValleyExpanded\",\"state\":\"InProgress\",\"objectives\":[{\"index\":0,\"type\":\"Donate\",\"runtime_type\":\"DonateObjective\",\"drop_box\":\"AndyChest\",\"drop_box_location\":\"Custom_AndyHouse\",\"accepted_context_tags\":[\"item_wood\"],\"current_count\":25,\"max_count\":500,\"complete\":false}],\"rewards\":[],\"donated_items\":[]}],\"available\":[],\"completed\":[],\"accepted_types\":[],\"returned_donations\":[]}").RootElement,
                    "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                    _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                };
                await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
            };
            await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
            await session.RunAsync(tok);
        }, cts.Token);
    }, cts.Token);

    for (int i = 0; i < 40 && !File.Exists(socket); i++)
        await Task.Delay(50, cts.Token);

    using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
    _ = client.RunAsync(cts.Token);

    var runner = new ScenarioRunner(client);
    var report = await runner.RunAsync(new ScenarioSpec
    {
        Name = "wait_special_order",
        Steps =
        {
            new ScenarioStep
            {
                Action = "wait.special_order",
                Args = JsonDocument.Parse("{\"collection\":\"active\",\"key\":\"Andy\",\"requester\":\"Andy\",\"objective_type\":\"Donate\",\"drop_box\":\"AndyChest\",\"accepted_context_tag\":\"item_wood\",\"current_count_gte\":25,\"timeout_ms\":1000,\"poll_ms\":10}").RootElement,
            },
        },
    }, cts.Token);

    Assert.True(report.Passed);
    Assert.True(polls >= 2);

    cts.Cancel();
    try { await serverTask; } catch (OperationCanceledException) { }
}

[Fact]
public async Task WaitSpecialOrder_TimeoutReportsObservedKeys()
{
    var socket = SocketPath();
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

    var serverTask = Task.Run(async () =>
    {
        await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
        {
            session.RequestReceived += async req =>
            {
                JsonElement r = req.Method switch
                {
                    "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                    "state.special_orders" => JsonDocument.Parse("{\"active\":[{\"key\":\"MarlonFay2\",\"objectives\":[],\"rewards\":[],\"donated_items\":[]}],\"available\":[{\"key\":\"Andy\",\"objectives\":[],\"rewards\":[],\"donated_items\":[]}],\"completed\":[\"Emily\"],\"accepted_types\":[],\"returned_donations\":[]}").RootElement,
                    "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                    _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                };
                await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
            };
            await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
            await session.RunAsync(tok);
        }, cts.Token);
    }, cts.Token);

    for (int i = 0; i < 40 && !File.Exists(socket); i++)
        await Task.Delay(50, cts.Token);

    using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
    _ = client.RunAsync(cts.Token);

    var runner = new ScenarioRunner(client);
    var report = await runner.RunAsync(new ScenarioSpec
    {
        Name = "wait_special_order_timeout",
        Steps =
        {
            new ScenarioStep
            {
                Action = "wait.special_order",
                Args = JsonDocument.Parse("{\"collection\":\"active\",\"key\":\"Missing\",\"timeout_ms\":50,\"poll_ms\":10}").RootElement,
            },
        },
    }, cts.Token);

    Assert.False(report.Passed);
    Assert.Contains("active=[MarlonFay2]", report.Failures[0]);
    Assert.Contains("available=[Andy]", report.Failures[0]);
    Assert.Contains("completed=[Emily]", report.Failures[0]);

    cts.Cancel();
    try { await serverTask; } catch (OperationCanceledException) { }
}
```

- [ ] **Step 2: Run the tests and verify RED**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "WaitSpecialOrder"
```

Expected: FAIL because `wait.special_order` is unknown and falls through as an RPC.

- [ ] **Step 3: Implement runner wait dispatch and filters**

In `ScenarioRunner.RunAsync`, add a branch after `wait.player`:

```csharp
else if (step.Action == "wait.special_order")
{
    await InvokeWaitSpecialOrderAsync(step, ct);
}
```

Add these helpers near the existing wait helpers:

```csharp
private async Task InvokeWaitSpecialOrderAsync(ScenarioStep step, CancellationToken ct)
{
    var args = step.Args is { ValueKind: JsonValueKind.Object } obj
        ? JsonSerializer.Deserialize<WaitSpecialOrderStepArgs>(obj.GetRawText(), ProtocolJson.Options) ?? new WaitSpecialOrderStepArgs()
        : new WaitSpecialOrderStepArgs();
    ValidateWaitSpecialOrderArgs(args);

    var elapsed = Stopwatch.StartNew();
    JsonElement? last = null;
    int lastMatched = 0;
    while (elapsed.ElapsedMilliseconds < args.TimeoutMs)
    {
        ct.ThrowIfCancellationRequested();
        var resp = await _session.InvokeAsync("state.special_orders", params_: null, ct);
        if (resp.Error is { } error)
            throw new InvalidOperationException($"wait.special_order failed during state.special_orders: {error.Message}");
        if (resp.Result is { } root)
        {
            last = root.Clone();
            lastMatched = CountSpecialOrderMatches(root, args);
            if (lastMatched >= args.MinCount && (args.MaxCount is null || lastMatched <= args.MaxCount.Value))
                return;
        }
        await Task.Delay(args.PollMs, ct);
    }

    throw new TimeoutException(
        $"wait.special_order timed out after {args.TimeoutMs}ms waiting for {FormatSpecialOrderExpectation(args)}; " +
        $"last observed {lastMatched} matched; {FormatObservedSpecialOrderKeys(last)}");
}
```

Add filtering helpers:

```csharp
private static int CountSpecialOrderMatches(JsonElement root, WaitSpecialOrderStepArgs args)
{
    if (!root.TryGetProperty(args.Collection, out var collection))
        return 0;
    if (args.Collection == "completed")
        return collection.ValueKind == JsonValueKind.Array
            ? collection.EnumerateArray().Count(item => item.ValueKind == JsonValueKind.String && StringEquals(item.GetString(), args.Key))
            : 0;
    return collection.ValueKind == JsonValueKind.Array
        ? collection.EnumerateArray().Count(order => SpecialOrderMatches(order, args))
        : 0;
}

private static bool SpecialOrderMatches(JsonElement order, WaitSpecialOrderStepArgs args)
{
    return StringFilterMatches(order, "key", args.Key)
        && StringFilterMatches(order, "name", args.Name)
        && StringFilterMatches(order, "requester", args.Requester)
        && StringFilterMatches(order, "order_type", args.OrderType)
        && StringFilterMatches(order, "special_rule", args.SpecialRule)
        && StringFilterMatches(order, "state", args.State)
        && BoolFilterMatches(order, "is_timed", args.IsTimed)
        && BoolFilterMatches(order, "ready_for_removal", args.ReadyForRemoval)
        && ObjectiveCriteriaMatches(order, args);
}
```

Implement `ObjectiveCriteriaMatches`, `StringArrayContains`, `BoolFilterMatches`, and diagnostics. Objective matching should require at least one objective to match when any objective filter is present; otherwise it should return true.

Add passive step behavior:

```csharp
"wait.special_order" => false,
```

to `ShouldAutoCaptureStep`.

Add description:

```csharp
"wait.special_order" => $"Wait for special order {GetStringArg(step.Args, "key") ?? "match"}",
```

to `DescribeStep`.

Add `WaitSpecialOrderStepArgs` near other runner arg classes:

```csharp
private sealed class WaitSpecialOrderStepArgs
{
    public string Collection { get; set; } = "active";
    public string? Key { get; set; }
    public string? Name { get; set; }
    public string? Requester { get; set; }
    public string? OrderType { get; set; }
    public string? SpecialRule { get; set; }
    public string? State { get; set; }
    public bool? IsTimed { get; set; }
    public bool? ReadyForRemoval { get; set; }
    public string? ObjectiveType { get; set; }
    public string? ObjectiveRuntimeType { get; set; }
    public string? DropBox { get; set; }
    public string? DropBoxLocation { get; set; }
    public string? TargetName { get; set; }
    public string? AcceptedContextTag { get; set; }
    public int? CurrentCount { get; set; }
    public int? CurrentCountGte { get; set; }
    public int? MaxCount { get; set; }
    public bool? Complete { get; set; }
    public int MinCount { get; set; } = 1;
    public int? MaxCountOrders { get; set; }
    public int TimeoutMs { get; set; } = 10000;
    public int PollMs { get; set; } = 100;
}
```

Use `MaxCountOrders` as the C# property for JSON `max_count_orders` only if needed. Prefer naming the order count `MaxCount` and objective max as `ObjectiveMaxCount` if the implementation finds the collision clearer. Keep JSON names documented in Task 9.

- [ ] **Step 4: Run tests and verify GREEN**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "WaitSpecialOrder"
```

Expected: PASS.

- [ ] **Step 5: Commit Task 3**

```bash
git add src/Runner/Scenarios/ScenarioRunner.cs tests/Runner.Tests/ScenarioRunnerTests.cs
git commit -m "feat: wait for special order state"
```

---

### Task 4: Drop Box Deposit Protocol Model

**Files:**
- Create: `src/Protocol/Models/DropBoxDepositRequest.cs`
- Test: `tests/Protocol.Tests/DropBoxDepositSerializationTests.cs`

- [ ] **Step 1: Write failing serialization tests**

Create `tests/Protocol.Tests/DropBoxDepositSerializationTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class DropBoxDepositSerializationTests
{
    [Fact]
    public void Request_SerializesSnakeCaseFields()
    {
        var json = JsonSerializer.Serialize(new DropBoxDepositRequest
        {
            OrderKey = "Andy",
            DropBox = "AndyChest",
            QualifiedId = "(O)388",
            Count = 25,
        }, ProtocolJson.Options);

        Assert.Contains("\"order_key\":\"Andy\"", json);
        Assert.Contains("\"drop_box\":\"AndyChest\"", json);
        Assert.Contains("\"qualified_id\":\"(O)388\"", json);
        Assert.Contains("\"count\":25", json);
    }

    [Fact]
    public void Result_SerializesBeforeAfterCounts()
    {
        var json = JsonSerializer.Serialize(new DropBoxDepositResult
        {
            Ok = true,
            OrderKey = "Andy",
            DropBox = "AndyChest",
            DepositedCount = 25,
            ObjectiveIndex = 0,
            BeforeCount = 0,
            AfterCount = 25,
            Item = new SpecialOrderItemSummary { QualifiedId = "(O)388", Name = "Wood", Stack = 25 },
        }, ProtocolJson.Options);

        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"order_key\":\"Andy\"", json);
        Assert.Contains("\"deposited_count\":25", json);
        Assert.Contains("\"objective_index\":0", json);
        Assert.Contains("\"before_count\":0", json);
        Assert.Contains("\"after_count\":25", json);
        Assert.Contains("\"qualified_id\":\"(O)388\"", json);
    }
}
```

- [ ] **Step 2: Run tests and verify RED**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter DropBoxDepositSerializationTests
```

Expected: FAIL because request/result types do not exist.

- [ ] **Step 3: Add request/result models**

Create `src/Protocol/Models/DropBoxDepositRequest.cs`:

```csharp
namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape for <c>drop_box.deposit</c>.</summary>
public sealed class DropBoxDepositRequest
{
    public string OrderKey { get; set; } = string.Empty;
    public string? DropBox { get; set; }
    public string? ItemId { get; set; }
    public string? QualifiedId { get; set; }
    public int Count { get; set; } = 1;
}

/// <summary>Result shape for <c>drop_box.deposit</c>.</summary>
public sealed class DropBoxDepositResult
{
    public bool Ok { get; set; }
    public string OrderKey { get; set; } = string.Empty;
    public string DropBox { get; set; } = string.Empty;
    public int DepositedCount { get; set; }
    public int ObjectiveIndex { get; set; }
    public int? BeforeCount { get; set; }
    public int? AfterCount { get; set; }
    public SpecialOrderItemSummary? Item { get; set; }
}
```

- [ ] **Step 4: Run tests and verify GREEN**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter DropBoxDepositSerializationTests
```

Expected: PASS.

- [ ] **Step 5: Commit Task 4**

```bash
git add src/Protocol/Models/DropBoxDepositRequest.cs tests/Protocol.Tests/DropBoxDepositSerializationTests.cs
git commit -m "feat: add drop box deposit protocol model"
```

---

### Task 5: Drop Box Deposit Handler

**Files:**
- Create: `src/Harness/Handlers/DropBoxDepositHandler.cs`
- Modify: `src/Harness/ModEntry.cs`
- Test: `tests/Harness.Tests/DropBoxDepositHandlerTests.cs`

- [ ] **Step 1: Write failing handler tests**

Create `tests/Harness.Tests/DropBoxDepositHandlerTests.cs`:

```csharp
using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class DropBoxDepositHandlerTests
{
    [Fact]
    public void Handle_DepositsMatchingInventoryIntoDonationObjective()
    {
        var world = FakeDropBoxWorld.WithOrderAndInventory();
        var req = ProtocolJson.ToElement(new DropBoxDepositRequest
        {
            OrderKey = "Andy",
            DropBox = "AndyChest",
            QualifiedId = "(O)388",
            Count = 25,
        });

        var result = DropBoxDepositHandler.Handle(req, world);
        var parsed = JsonSerializer.Deserialize<DropBoxDepositResult>(result.GetRawText(), ProtocolJson.Options)!;

        Assert.True(parsed.Ok);
        Assert.Equal("Andy", parsed.OrderKey);
        Assert.Equal("AndyChest", parsed.DropBox);
        Assert.Equal(0, parsed.BeforeCount);
        Assert.Equal(25, parsed.AfterCount);
        Assert.Equal(25, parsed.DepositedCount);
        Assert.Equal(25, world.Order.Objectives[0].CurrentCount);
        Assert.Equal(75, world.Inventory[0].Stack);
        Assert.Collection(world.Order.DonatedItems, item => Assert.Equal(25, item.Stack));
    }

    [Fact]
    public void Handle_RejectsInsufficientInventory()
    {
        var world = FakeDropBoxWorld.WithOrderAndInventory();
        var req = ProtocolJson.ToElement(new DropBoxDepositRequest
        {
            OrderKey = "Andy",
            DropBox = "AndyChest",
            QualifiedId = "(O)388",
            Count = 125,
        });

        var ex = Assert.Throws<JsonRpcException>(() => DropBoxDepositHandler.Handle(req, world));
        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("not enough matching inventory", ex.Message);
    }

    [Fact]
    public void Handle_RejectsWrongDropBox()
    {
        var world = FakeDropBoxWorld.WithOrderAndInventory();
        var req = ProtocolJson.ToElement(new DropBoxDepositRequest
        {
            OrderKey = "Andy",
            DropBox = "OtherBox",
            QualifiedId = "(O)388",
            Count = 1,
        });

        var ex = Assert.Throws<JsonRpcException>(() => DropBoxDepositHandler.Handle(req, world));
        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("no matching donation objective", ex.Message);
    }
}
```

Define fakes implementing handler interfaces:

```csharp
private sealed class FakeDropBoxWorld : IDropBoxDepositWorld
{
    public FakeDepositOrder Order { get; } = new();
    public List<FakeDepositItem> Inventory { get; } = new();
    public IReadOnlyList<IDropBoxDepositOrder> ActiveOrders => new[] { Order };
    public IReadOnlyList<IDropBoxInventoryItem> PlayerInventory => Inventory;

    public static FakeDropBoxWorld WithOrderAndInventory()
    {
        var world = new FakeDropBoxWorld();
        world.Order.Key = "Andy";
        world.Order.Objectives.Add(new FakeDepositObjective
        {
            Index = 0,
            Type = "Donate",
            DropBox = "AndyChest",
            AcceptedContextTags = { "item_wood" },
            CurrentCount = 0,
            MaxCount = 500,
        });
        world.Inventory.Add(new FakeDepositItem { QualifiedId = "(O)388", ItemId = "388", Name = "Wood", Stack = 100, ContextTags = { "item_wood" } });
        return world;
    }
}
```

- [ ] **Step 2: Run tests and verify RED**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter DropBoxDepositHandlerTests
```

Expected: FAIL because `DropBoxDepositHandler` and its interfaces do not exist.

- [ ] **Step 3: Implement the handler**

Create `src/Harness/Handlers/DropBoxDepositHandler.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

public static class DropBoxDepositHandler
{
    public const string Method = "drop_box.deposit";

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, new SdvDropBoxDepositWorld());

    internal static JsonElement Handle(JsonElement? paramsElement, IDropBoxDepositWorld world)
    {
        var req = RpcParams.Required<DropBoxDepositRequest>(paramsElement);
        Validate(req);

        var order = world.ActiveOrders.FirstOrDefault(order => string.Equals(order.Key, req.OrderKey, StringComparison.Ordinal))
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"drop_box.deposit found no active order '{req.OrderKey}'");
        var objective = order.Objectives.FirstOrDefault(objective => ObjectiveMatches(objective, req))
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"drop_box.deposit found no matching donation objective for order '{req.OrderKey}'");
        var selected = SelectInventory(world.PlayerInventory, req, objective);
        var before = objective.CurrentCount;

        order.Deposit(objective, selected, req.Count);

        var result = new DropBoxDepositResult
        {
            Ok = true,
            OrderKey = order.Key,
            DropBox = objective.DropBox,
            DepositedCount = req.Count,
            ObjectiveIndex = objective.Index,
            BeforeCount = before,
            AfterCount = objective.CurrentCount,
            Item = selected.ToSummary(req.Count),
        };
        return ProtocolJson.ToElement(result);
    }

    private static void Validate(DropBoxDepositRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.OrderKey))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.order_key required");
        if (string.IsNullOrWhiteSpace(req.ItemId) && string.IsNullOrWhiteSpace(req.QualifiedId))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.item_id or params.qualified_id required");
        if (req.Count < 1)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.count must be >= 1");
    }

    private static bool ObjectiveMatches(IDropBoxDepositObjective objective, DropBoxDepositRequest req)
        => string.Equals(objective.Type, "Donate", StringComparison.Ordinal)
            && (string.IsNullOrWhiteSpace(req.DropBox) || string.Equals(objective.DropBox, req.DropBox, StringComparison.Ordinal));

    private static IDropBoxInventoryItem SelectInventory(IReadOnlyList<IDropBoxInventoryItem> inventory, DropBoxDepositRequest req, IDropBoxDepositObjective objective)
    {
        var item = inventory.FirstOrDefault(item =>
            (string.IsNullOrWhiteSpace(req.QualifiedId) || string.Equals(item.QualifiedId, req.QualifiedId, StringComparison.Ordinal))
            && (string.IsNullOrWhiteSpace(req.ItemId) || string.Equals(item.ItemId, req.ItemId, StringComparison.Ordinal))
            && item.Stack >= req.Count
            && ItemMatchesObjective(item, objective));

        return item ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, "drop_box.deposit found not enough matching inventory for objective");
    }

    private static bool ItemMatchesObjective(IDropBoxInventoryItem item, IDropBoxDepositObjective objective)
        => objective.AcceptedContextTags.Count == 0
            || objective.AcceptedContextTags.Any(tag => item.ContextTags.Contains(tag, StringComparer.OrdinalIgnoreCase));
}
```

Add interfaces in the same file:

```csharp
internal interface IDropBoxDepositWorld
{
    IReadOnlyList<IDropBoxDepositOrder> ActiveOrders { get; }
    IReadOnlyList<IDropBoxInventoryItem> PlayerInventory { get; }
}
```

Runtime wrappers should use `Game1.player.team.specialOrders`, `Game1.player.Items`, and `SpecialOrder.donatedItems`. The generic fallback for `Deposit` should:

- clone or split the selected inventory item for the donated count;
- reduce the selected inventory stack by `count` or remove it when stack reaches zero;
- add the donated item to `SpecialOrder.donatedItems`;
- increment the matching objective's `currentCount` by `count`, capped at `maxCount` when `maxCount` exists;
- set `confirmed` if the objective exposes a `confirmed` field and Stardew expects it after donation.

Keep all SVE-specific names out of the handler.

- [ ] **Step 4: Register the RPC**

In `src/Harness/ModEntry.cs`, register the handler near the world/combat mutators:

```csharp
_rpc.Register(DropBoxDepositHandler.Method, p => DropBoxDepositHandler.Handle(p));
```

- [ ] **Step 5: Run tests and verify GREEN**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter DropBoxDepositHandlerTests
```

Expected: PASS.

- [ ] **Step 6: Commit Task 5**

```bash
git add src/Harness/Handlers/DropBoxDepositHandler.cs src/Harness/ModEntry.cs tests/Harness.Tests/DropBoxDepositHandlerTests.cs
git commit -m "feat: deposit special order drop box items"
```

---

### Task 6: Probe Core SVE Special Order Candidate

**Files:**
- Temporary probe files under `/tmp`
- No commit in this task unless a stable scenario file is added in Task 7

- [ ] **Step 1: Create a read-only Clint2 candidate probe**

Create `/tmp/sve-slice-10-order-probe.test.json`:

```json
{
  "name": "sve_slice_10_order_probe",
  "fixture": "m0spike_436515781",
  "config": { "seed": 436515781 },
  "steps": [
    { "action": "time.set", "args": { "time": 600, "day": 1, "season": "spring", "year": 1 } },
    { "action": "player.add_event_seen", "args": { "id": "8050107" } },
    { "action": "player.add_event_seen", "args": { "id": "8050108" } },
    { "action": "time.next_day", "args": { "settle_timeout_ms": 15000, "poll_ms": 100 } },
    {
      "action": "wait.special_order",
      "args": {
        "collection": "active",
        "key": "Clint2",
        "requester": "Clint",
        "order_type": "StardewValleyExpanded",
        "timeout_ms": 15000,
        "poll_ms": 100
      }
    },
    { "action": "freeze.begin", "args": { "settle_timeout_ms": 10000, "poll_ms": 100 } },
    { "action": "screenshot.capture", "args": { "name": "probe" } }
  ],
  "assertions": [
    {
      "type": "state",
      "expr": "state.special_orders.active any key == 'Clint2'",
      "message": "SVE should add Clint2 after seeded railroad boulder events"
    }
  ]
}
```

`Clint2` is the first concrete candidate because core SVE defines it as an event-gated donation order with `DropBox` `ClintCrate` in `Blacksmith`, accepted tags `item_iridium_ore` and `item_coal`, and `AddSpecialOrdersAfterEvents` adds it after event `8050108` while event `8050109` is unseen. The probe also seeds event `8050107` because the Content Patcher data entry requires it.

- [ ] **Step 2: Run the probe**

Run from SVE:

```bash
env SDV_TEST_MOD_CACHE=/home/fintan/stardewRepos/frobby/sdv-test-framework/.cache/deps FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-10-special-orders ./scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-10-probe /tmp/sve-slice-10-order-probe.test.json
```

Expected: PASS for the first stable candidate. If it fails because the seeded event is insufficient, inspect the timeout diagnostics from `wait.special_order`, then adjust only the SVE probe prerequisites, not Frobby production code.

- [ ] **Step 3: Probe deposit metadata**

After `Clint2` appears active, add objective filters to the probe:

```json
{
  "action": "wait.special_order",
  "args": {
    "collection": "active",
    "key": "Clint2",
    "objective_type": "Donate",
    "drop_box": "ClintCrate",
    "accepted_context_tag": "item_iridium_ore",
    "current_count": 0,
    "timeout_ms": 15000,
    "poll_ms": 100
  }
}
```

Expected: PASS and report shows the order/objective data. If this exact metadata fails because SVE does not add the order from event seeding alone, stop and inspect the `wait.special_order` diagnostics before changing the candidate. The fallback candidate should still be a core SVE donation order from `AddSpecialOrdersAfterEvents.cs`; do not switch to Frontier Farm or Grandpa's Farm for Slice 10.

---

### Task 7: Add SVE Slice 10 Scenario

**Files:**
- Create: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/15-sve-special-order-drop-box.test.json`

- [ ] **Step 1: Add the scenario using the Clint2 candidate**

Create `tests/sdv/15-sve-special-order-drop-box.test.json` in the SVE repo:

```json
{
  "name": "sve_special_order_drop_box",
  "fixture": "m0spike_436515781",
  "config": { "seed": 436515781 },
  "steps": [
    { "action": "time.set", "args": { "time": 600, "day": 1, "season": "spring", "year": 1 } },
    { "action": "player.add_event_seen", "args": { "id": "8050107" } },
    { "action": "player.add_event_seen", "args": { "id": "8050108" } },
    { "action": "time.next_day", "args": { "settle_timeout_ms": 15000, "poll_ms": 100 } },
    {
      "action": "wait.special_order",
      "args": {
        "collection": "active",
        "key": "Clint2",
        "requester": "Clint",
        "order_type": "StardewValleyExpanded",
        "objective_type": "Donate",
        "drop_box": "ClintCrate",
        "accepted_context_tag": "item_iridium_ore",
        "current_count": 0,
        "timeout_ms": 15000,
        "poll_ms": 100
      }
    },
    { "action": "player.give_item", "args": { "id": "(O)386", "count": 5 } },
    {
      "action": "drop_box.deposit",
      "args": {
        "order_key": "Clint2",
        "drop_box": "ClintCrate",
        "qualified_id": "(O)386",
        "count": 5
      }
    },
    {
      "action": "wait.special_order",
      "args": {
        "collection": "active",
        "key": "Clint2",
        "objective_type": "Donate",
        "drop_box": "ClintCrate",
        "accepted_context_tag": "item_iridium_ore",
        "current_count_gte": 5,
        "timeout_ms": 10000,
        "poll_ms": 100
      }
    },
    { "action": "freeze.begin", "args": { "settle_timeout_ms": 10000, "poll_ms": 100 } },
    { "action": "screenshot.capture", "args": { "name": "final" } }
  ],
  "assertions": [
    {
      "type": "state",
      "expr": "state.special_orders.active any key == 'Clint2'",
      "message": "SVE special order should remain active after a partial drop-box deposit"
    }
  ]
}
```

The scenario intentionally deposits 5 iridium ore into a 20-item objective so the order remains active while still proving donation progress.

- [ ] **Step 2: Run the SVE scenario**

Run:

```bash
env SDV_TEST_MOD_CACHE=/home/fintan/stardewRepos/frobby/sdv-test-framework/.cache/deps FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-10-special-orders ./scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-10 /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/15-sve-special-order-drop-box.test.json
```

Expected: PASS.

- [ ] **Step 3: Commit SVE scenario**

```bash
git add tests/sdv/15-sve-special-order-drop-box.test.json
git commit -m "test: add special order drop box scenario"
```

---

### Task 8: Documentation And Capability Tracker

**Files:**
- Modify: `README.md`
- Modify: `docs/rpc-schema.md`
- Modify: `docs/dsl-quickstart.md`
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Update README usage guidance**

In `README.md`, extend the SVE/Frobby guidance section with:

```markdown
- Use `state.special_orders` and runner-side `wait.special_order` for special
  order registration, event-gated order activation, objective progress, donated
  item state, completion keys, and returned donations. Keep mod-specific order
  keys and event prerequisites in repo scenarios, not in Frobby.
- Use `drop_box.deposit` for neutral donation-objective tests after proving the
  active order and drop box through `state.special_orders`. The action works from
  Stardew runtime special-order state and should not parse a mod's content packs.
```

- [ ] **Step 2: Update RPC schema**

In `docs/rpc-schema.md`, add sections for:

```markdown
### state.special_orders

Projects active, available, and completed Stardew special-order state...
```

Include one compact JSON response showing `active`, `available`, `completed`, objective `drop_box`, `accepted_context_tags`, and `donated_items`.

Add:

```markdown
### drop_box.deposit

Deposits items from player inventory into an active special-order donation
objective...
```

Document validation errors for missing `order_key`, missing item selector, missing active order, missing donation objective, and insufficient inventory.

Add `wait.special_order` to the runner convenience list with supported filters.

- [ ] **Step 3: Update DSL quickstart**

In `docs/dsl-quickstart.md`, add a special-order example:

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

Then add:

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

- [ ] **Step 4: Update SVE capability tracker**

In `SVE_FROBBY_CAPABILITY_TODO.md`, mark Slice 10 as Done only after the SVE scenario passes. Add:

```markdown
- [x] Done: Slice 10, special orders, quest state, and drop boxes.
  - Design spec: `docs/superpowers/specs/2026-05-10-sve-slice-10-special-orders-drop-boxes-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-10-sve-slice-10-special-orders-drop-boxes.md`.
  - Done: `state.special_orders`, runner-side `wait.special_order`, neutral `drop_box.deposit`, and SVE scenario 15 verify runtime special-order activation and donation progress.
```

- [ ] **Step 5: Run docs diff check**

Run:

```bash
git diff --check
```

Expected: no output.

- [ ] **Step 6: Commit docs**

```bash
git add README.md docs/rpc-schema.md docs/dsl-quickstart.md SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "docs: document special order testing tools"
```

---

### Task 9: Final Verification

**Files:**
- No source edits expected

- [ ] **Step 1: Run Frobby targeted tests**

Run from the Frobby worktree:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter "SpecialOrdersStateSerializationTests|DropBoxDepositSerializationTests"
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "StateSpecialOrdersHandlerTests|DropBoxDepositHandlerTests"
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "WaitSpecialOrder"
```

Expected: all pass with 0 failures.

- [ ] **Step 2: Run broader nearby Frobby tests**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "WaitLocation|WaitPlayer|WaitSpecialOrder|CombatAttack"
dotnet build src/Runner/Runner.csproj
```

Expected: all pass/build with 0 errors.

- [ ] **Step 3: Run SVE Slice 10 and nearby regression scenarios**

Run from SVE:

```bash
env SDV_TEST_MOD_CACHE=/home/fintan/stardewRepos/frobby/sdv-test-framework/.cache/deps FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-10-special-orders ./scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-10-final /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/11-sve-event-dialogue-choice.test.json /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/15-sve-special-order-drop-box.test.json
```

Expected: suite passes. Existing SVE compiler warnings may remain, but there must be 0 build errors and 0 scenario failures.

- [ ] **Step 4: Check final git state**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-10-special-orders status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
```

Expected: both branches clean after commits.

---

## Self-Review Checklist

- Spec coverage:
  - `state.special_orders`: Task 1 and Task 2.
  - `wait.special_order`: Task 3.
  - `drop_box.deposit`: Task 4 and Task 5.
  - Core SVE proof: Task 6 and Task 7.
  - Docs and tracker: Task 8.
  - Verification: Task 9.
- Placeholder scan:
  - The concrete Slice 10 SVE candidate is `Clint2`, seeded by events `8050107` and `8050108`, with drop box `ClintCrate` and iridium ore `(O)386`.
  - Production code tasks use concrete type and method names.
- Type consistency:
  - Protocol state models are consumed by harness projection and runner JSON filtering.
  - `DropBoxDepositRequest` and `DropBoxDepositResult` are shared by protocol serialization, handler, and docs.
