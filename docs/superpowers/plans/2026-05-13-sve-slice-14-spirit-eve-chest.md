# SVE Slice 14 Spirit's Eve Chest Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add neutral Frobby festival-entry and container-content inspection support, then prove it with an SVE Spirit's Eve chest scenario.

**Architecture:** Extend the existing location object projector instead of adding a separate container endpoint. Extend the runner's existing `wait.location_content` and `wait.event_active` polling paths. Add `festival.start` as a normal harness JSON-RPC mutator that starts the current date's festival through Stardew festival APIs without SVE-specific logic.

**Tech Stack:** C#/.NET 10, xUnit, SMAPI/Stardew Valley runtime APIs, Frobby JSON-RPC protocol, Frobby scenario runner JSON, SVE repo-local `scripts/sdv-test`.

---

## Branch And Repo Setup

Work in Frobby:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework switch -c feature/sve-slice-14-spirit-eve-chest
```

Work in SVE only on a feature branch. Do not merge SVE to master unless Fintan explicitly asks:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded switch -c feature/frobby-sve-slice-14-spirit-eve-chest
```

If either branch already exists, switch to it instead of creating it.

## File Map

- Modify `src/Protocol/Models/LocationState.cs`
  - Add contained-item summary model and new fields on `ObjectSummary`.
- Modify `src/Harness/Handlers/LocationContentProjector.cs`
  - Project chest-like object contents.
- Modify `tests/Harness.Tests/LocationContentProjectorTests.cs`
  - Add red tests for chest projection and non-container behavior.
- Modify `src/Runner/Scenarios/ScenarioRunner.cs`
  - Add contained-item filters to `wait.location_content`.
  - Add `is_festival` filter to `wait.event_active`.
- Modify `tests/Runner.Tests/ScenarioRunnerTests.cs`
  - Add red tests for contained-item filtering and festival-state waits.
- Create `src/Harness/Handlers/FestivalStartHandler.cs`
  - Implement neutral `festival.start`.
- Modify `src/Harness/ModEntry.cs`
  - Register `festival.start` and update the SMAPI console method list.
- Create `tests/Harness.Tests/FestivalStartHandlerTests.cs`
  - Add red tests for validation and handler responses through a fake world.
- Modify `docs/rpc-schema.md`, `docs/dsl-quickstart.md`, `README.md`
  - Document container object state, contained-item waits, `festival.start`, and `wait.event_active.is_festival`.
- Modify `SVE_FROBBY_CAPABILITY_TODO.md`
  - Mark Slice 14 complete after verification, leaving movie theater and grange as follow-ups.
- Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/19-sve-spirit-eve-chest.test.json`
  - Prove SVE's year-one Spirit's Eve chest location/content behavior.
- Modify `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`
  - Document the new SVE scenario.

## Task 1: Container Fields And Projection

**Files:**
- Modify: `src/Protocol/Models/LocationState.cs`
- Modify: `src/Harness/Handlers/LocationContentProjector.cs`
- Test: `tests/Harness.Tests/LocationContentProjectorTests.cs`

- [ ] **Step 1: Write the failing chest projection test**

Add this test after `ProjectObject_ReadsHeldObjectMetadata`:

```csharp
[Fact]
public void ProjectObject_ReadsChestContainedItems()
{
    var chest = new FakeChest
    {
        Name = "Treasure Chest",
        ItemId = "130",
        QualifiedItemId = "(BC)130",
        Items =
        {
            new FakeHeldObject
            {
                Name = "Golden Pumpkin",
                ItemId = "373",
                QualifiedItemId = "(O)373",
                Stack = 1,
                Quality = 0,
                Category = -79,
            },
        },
    };

    var summary = LocationContentProjector.ProjectObjectForTests(new Vector2(63, 16), chest);

    Assert.True(summary.IsChest);
    Assert.Equal(1, summary.ItemCount);
    Assert.False(summary.ItemsTruncated);
    var item = Assert.Single(summary.Items);
    Assert.Equal(0, item.Slot);
    Assert.Equal("373", item.Id);
    Assert.Equal("373", item.ItemId);
    Assert.Equal("(O)373", item.QualifiedId);
    Assert.Equal("Golden Pumpkin", item.Name);
    Assert.Equal(1, item.Stack);
    Assert.Equal(0, item.Quality);
    Assert.Equal(-79, item.Category);
    Assert.Equal("FakeHeldObject", item.RuntimeType);
}
```

Add this test to lock non-container behavior:

```csharp
[Fact]
public void ProjectObject_LeavesNonChestItemListEmpty()
{
    var obj = new FakeLocationObject
    {
        Name = "Golden Piggy Bank",
        ItemId = "Example_Golden_Piggy_Bank",
        QualifiedItemId = "(BC)Example_Golden_Piggy_Bank",
    };

    var summary = LocationContentProjector.ProjectObjectForTests(new Vector2(8, 9), obj);

    Assert.False(summary.IsChest);
    Assert.Null(summary.ItemCount);
    Assert.Null(summary.ItemsTruncated);
    Assert.Empty(summary.Items);
}
```

Extend `FakeHeldObject` and add `FakeChest`:

```csharp
private sealed class FakeHeldObject
{
    public string Name = string.Empty;
    public string ItemId = string.Empty;
    public string QualifiedItemId = string.Empty;
    public int Stack;
    public int Quality;
    public int Category;
}

private sealed class FakeChest
{
    public string Name = string.Empty;
    public string ItemId = string.Empty;
    public string QualifiedItemId = string.Empty;
    public List<FakeHeldObject> Items { get; } = new();
}
```

- [ ] **Step 2: Run the focused test and confirm RED**

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~LocationContentProjectorTests"
```

Expected: compile failure because `ObjectSummary.IsChest`, `ItemCount`, `ItemsTruncated`, and `Items` do not exist.

- [ ] **Step 3: Add protocol model fields**

In `ObjectSummary`, add:

```csharp
public bool IsChest { get; set; }
public int? ItemCount { get; set; }
public bool? ItemsTruncated { get; set; }
public List<ContainedItemSummary> Items { get; set; } = new();
```

Add a new model in the same file:

```csharp
/// <summary>Item descriptor for an object-owned container such as a chest.</summary>
public sealed class ContainedItemSummary
{
    public int Slot { get; set; }
    public string Id { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string QualifiedId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int? Stack { get; set; }
    public int? Category { get; set; }
    public int? Quality { get; set; }
    public string RuntimeType { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Add minimal projection implementation**

In `ProjectLocationObject`, create the summary in a local variable, then call a new helper before returning it:

```csharp
var summary = new ObjectSummary
{
    Tile = new TilePoint { X = (int)tile.X, Y = (int)tile.Y },
    Name = ReadString(obj, "Name", "name", "DisplayName", "displayName") ?? obj.GetType().Name,
    Id = ReadString(obj, "ItemId", "itemId") ?? StripQualifiedPrefix(qualifiedId),
    QualifiedId = qualifiedId,
    Category = ReadInt(obj, "Category", "category"),
    Stack = ReadInt(obj, "Stack", "stack"),
    Quality = ReadInt(obj, "Quality", "quality"),
    RuntimeType = obj.GetType().Name,
    BigCraftable = ReadBool(obj, "bigCraftable", "BigCraftable") ?? false,
    ReadyForHarvest = ReadBool(obj, "readyForHarvest", "ReadyForHarvest"),
    HeldObjectId = ReadString(heldObject, "ItemId", "itemId"),
    HeldObjectQualifiedId = ReadString(heldObject, "QualifiedItemId", "qualifiedItemId"),
    HeldObjectName = ReadString(heldObject, "Name", "name", "DisplayName", "displayName"),
};

ProjectContainedItems(obj, summary);
return summary;
```

Add helpers:

```csharp
private const int MaxContainedItems = 72;

private static void ProjectContainedItems(object obj, ObjectSummary summary)
{
    var items = ReadContainedItems(obj);
    if (items is null)
        return;

    summary.IsChest = true;

    var slot = 0;
    foreach (var entry in items)
    {
        if (slot >= MaxContainedItems)
        {
            summary.ItemsTruncated = true;
            break;
        }

        var item = ReadValueProperty(entry) ?? entry;
        if (item is not null)
            summary.Items.Add(ProjectContainedItem(slot, item));
        slot++;
    }

    summary.ItemCount = summary.Items.Count;
    summary.ItemsTruncated ??= false;
}

private static IEnumerable? ReadContainedItems(object obj)
{
    if (!string.Equals(obj.GetType().Name, "Chest", StringComparison.Ordinal)
        && !obj.GetType().Name.EndsWith("Chest", StringComparison.Ordinal)
        && ReadMemberRaw(obj, "Items", "items") is null)
    {
        return null;
    }

    var raw = ReadMemberRaw(obj, "Items", "items", "NetItems", "netItems");
    raw = ReadValueProperty(raw) ?? raw;
    return raw as IEnumerable;
}

private static ContainedItemSummary ProjectContainedItem(int slot, object item)
{
    var qualifiedId = ReadString(item, "QualifiedItemId", "qualifiedItemId") ?? string.Empty;
    var itemId = ReadString(item, "ItemId", "itemId") ?? StripQualifiedPrefix(qualifiedId);
    return new ContainedItemSummary
    {
        Slot = slot,
        Id = itemId,
        ItemId = itemId,
        QualifiedId = qualifiedId,
        Name = ReadString(item, "Name", "name", "DisplayName", "displayName") ?? item.GetType().Name,
        Stack = ReadInt(item, "Stack", "stack"),
        Quality = ReadInt(item, "Quality", "quality"),
        Category = ReadInt(item, "Category", "category"),
        RuntimeType = item.GetType().Name,
    };
}
```

- [ ] **Step 5: Run the focused test and confirm GREEN**

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~LocationContentProjectorTests"
```

Expected: pass.

- [ ] **Step 6: Commit Task 1**

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework add src/Protocol/Models/LocationState.cs src/Harness/Handlers/LocationContentProjector.cs tests/Harness.Tests/LocationContentProjectorTests.cs
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework commit -m "feat: project container item state"
```

## Task 2: Contained-Item Wait Filters

**Files:**
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`
- Test: `tests/Runner.Tests/ScenarioRunnerTests.cs`

- [ ] **Step 1: Write the failing runner test**

Add this test after `WaitLocationContent_FiltersObjectsByInteractionMetadata`:

```csharp
[Fact]
public async Task WaitLocationContent_FiltersObjectsByContainedItem()
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
                    "state.location" => JsonDocument.Parse("{\"name\":\"Town\",\"objects\":[{\"tile\":{\"x\":63,\"y\":16},\"name\":\"Treasure Chest\",\"runtime_type\":\"Chest\",\"is_chest\":true,\"item_count\":1,\"items_truncated\":false,\"items\":[{\"slot\":0,\"id\":\"373\",\"item_id\":\"373\",\"qualified_id\":\"(O)373\",\"name\":\"Golden Pumpkin\",\"stack\":1,\"quality\":0,\"category\":-79,\"runtime_type\":\"Object\"}]}],\"resource_clumps\":[],\"monsters\":[],\"debris\":[]}").RootElement,
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
        Name = "wait_location_content_chest_item",
        Steps = new()
        {
            new ScenarioStep
            {
                Action = "wait.location_content",
                Args = JsonDocument.Parse("{\"location\":\"Town\",\"collection\":\"objects\",\"runtime_type\":\"Chest\",\"x\":63,\"y\":16,\"contains_item_qualified_id\":\"(O)373\",\"contains_item_stack\":1,\"contains_item_quality\":0,\"contains_item_category\":-79,\"min_count\":1,\"max_count\":1,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
            },
        },
    }, cts.Token);

    Assert.True(report.Passed, string.Join("\n", report.Failures));

    cts.Cancel();
    try { await serverTask; } catch (OperationCanceledException) { }
}
```

- [ ] **Step 2: Run the focused test and confirm RED**

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~WaitLocationContent_FiltersObjectsByContainedItem"
```

Expected: failure because the contained-item fields are ignored and should not yet match the intended behavior.

- [ ] **Step 3: Add contained-item args**

Add properties to `WaitLocationContentStepArgs`:

```csharp
public string? ContainsItemId { get; set; }
public string? ContainsItemQualifiedId { get; set; }
public string? ContainsItemName { get; set; }
public int? ContainsItemStack { get; set; }
public int? ContainsItemStackGte { get; set; }
public int? ContainsItemQuality { get; set; }
public int? ContainsItemCategory { get; set; }
```

- [ ] **Step 4: Add contained-item matching**

Append to `LocationContentElementMatches`:

```csharp
&& ContainedItemFilterMatches(element, args);
```

Add helpers near the other filter helpers:

```csharp
private static bool ContainedItemFilterMatches(JsonElement element, WaitLocationContentStepArgs args)
{
    var hasContainedFilter = args.ContainsItemId is not null
        || args.ContainsItemQualifiedId is not null
        || args.ContainsItemName is not null
        || args.ContainsItemStack is not null
        || args.ContainsItemStackGte is not null
        || args.ContainsItemQuality is not null
        || args.ContainsItemCategory is not null;
    if (!hasContainedFilter)
        return true;

    if (element.ValueKind != JsonValueKind.Object
        || !element.TryGetProperty("items", out var items)
        || items.ValueKind != JsonValueKind.Array)
    {
        return false;
    }

    foreach (var item in items.EnumerateArray())
    {
        if (StringFilterMatches(item, "id", args.ContainsItemId)
            && StringFilterMatches(item, "qualified_id", args.ContainsItemQualifiedId)
            && StringFilterMatches(item, "name", args.ContainsItemName)
            && NumberFilterMatches(item, "stack", args.ContainsItemStack, null, null, null, args.ContainsItemStackGte)
            && NumberFilterMatches(item, "quality", args.ContainsItemQuality, null, null, null, null)
            && NumberFilterMatches(item, "category", args.ContainsItemCategory, null, null, null, null))
        {
            return true;
        }
    }

    return false;
}
```

- [ ] **Step 5: Include contained-item filters in timeout text**

In `FormatLocationContentFilters`, append:

```csharp
if (args.ContainsItemId is not null) filters.Add($"contains_item_id={args.ContainsItemId}");
if (args.ContainsItemQualifiedId is not null) filters.Add($"contains_item_qualified_id={args.ContainsItemQualifiedId}");
if (args.ContainsItemName is not null) filters.Add($"contains_item_name={args.ContainsItemName}");
if (args.ContainsItemStack is not null) filters.Add($"contains_item_stack={args.ContainsItemStack}");
if (args.ContainsItemStackGte is not null) filters.Add($"contains_item_stack_gte={args.ContainsItemStackGte}");
if (args.ContainsItemQuality is not null) filters.Add($"contains_item_quality={args.ContainsItemQuality}");
if (args.ContainsItemCategory is not null) filters.Add($"contains_item_category={args.ContainsItemCategory}");
```

- [ ] **Step 6: Run focused runner tests and confirm GREEN**

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~WaitLocationContent"
```

Expected: pass.

- [ ] **Step 7: Commit Task 2**

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework add src/Runner/Scenarios/ScenarioRunner.cs tests/Runner.Tests/ScenarioRunnerTests.cs
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework commit -m "feat: wait for contained location items"
```

## Task 3: Festival State Wait Filter

**Files:**
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`
- Test: `tests/Runner.Tests/ScenarioRunnerTests.cs`

- [ ] **Step 1: Write the failing runner test**

Add this test after `WaitEventActive_PollsStateEventUntilActive`:

```csharp
[Fact]
public async Task WaitEventActive_FiltersByFestivalState()
{
    var socket = SocketPath();
    var eventPolls = 0;
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
                    "state.event" when eventPolls++ == 0 => JsonDocument.Parse("{\"active\":true,\"event_up\":true,\"location\":\"Town\",\"id\":\"fall27\",\"is_festival\":false,\"actors\":[],\"dialogue\":null,\"viewport\":{\"x\":0,\"y\":0,\"width\":1280,\"height\":720}}").RootElement,
                    "state.event" => JsonDocument.Parse("{\"active\":true,\"event_up\":true,\"location\":\"Town\",\"id\":\"fall27\",\"is_festival\":true,\"actors\":[],\"dialogue\":null,\"viewport\":{\"x\":0,\"y\":0,\"width\":1280,\"height\":720}}").RootElement,
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
        Name = "wait_event_active_festival",
        Steps = new()
        {
            new ScenarioStep
            {
                Action = "wait.event_active",
                Args = JsonDocument.Parse("{\"id\":\"fall27\",\"location\":\"Town\",\"is_festival\":true,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
            },
        },
    }, cts.Token);

    Assert.True(report.Passed, string.Join("\n", report.Failures));
    Assert.True(eventPolls >= 2);

    cts.Cancel();
    try { await serverTask; } catch (OperationCanceledException) { }
}
```

- [ ] **Step 2: Run the focused test and confirm RED**

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~WaitEventActive_FiltersByFestivalState"
```

Expected: failure because `is_festival` is ignored.

- [ ] **Step 3: Add `IsFestival` to wait args and matching**

Add to `WaitEventStepArgs`:

```csharp
public bool? IsFestival { get; set; }
```

In `InvokeWaitEventActiveAsync`, include:

```csharp
&& (args.IsFestival is null || lastObserved.IsFestival == args.IsFestival.Value)
```

Update `FormatEventState`:

```csharp
private static string FormatEventState(EventState? state)
    => state is null
        ? "nothing"
        : $"active={state.Active}, event_up={state.EventUp}, id='{state.Id}', location='{state.Location}', is_festival={state.IsFestival}";
```

- [ ] **Step 4: Run focused runner tests and confirm GREEN**

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~WaitEvent"
```

Expected: pass.

- [ ] **Step 5: Commit Task 3**

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework add src/Runner/Scenarios/ScenarioRunner.cs tests/Runner.Tests/ScenarioRunnerTests.cs
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework commit -m "feat: wait for festival event state"
```

## Task 4: Neutral `festival.start` Handler

**Files:**
- Create: `src/Harness/Handlers/FestivalStartHandler.cs`
- Modify: `src/Harness/ModEntry.cs`
- Test: `tests/Harness.Tests/FestivalStartHandlerTests.cs`

- [ ] **Step 1: Write failing handler tests**

Create `tests/Harness.Tests/FestivalStartHandlerTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Harness.Rpc;

namespace SdvTestFramework.Harness.Tests;

public sealed class FestivalStartHandlerTests
{
    [Fact]
    public void Handle_ReturnsFestivalStartResult()
    {
        var world = new FakeFestivalStartWorld
        {
            Result = new FestivalStartResultForTests
            {
                Tick = 123,
                Id = "fall27",
                Location = "Town",
                IsFestival = true,
            },
        };
        var p = JsonDocument.Parse("{\"location\":\"Town\"}").RootElement;

        var result = FestivalStartHandler.Handle(p, world);

        Assert.Equal("Town", world.ExpectedLocation);
        Assert.Equal(123, result.GetProperty("tick").GetInt32());
        Assert.Equal("fall27", result.GetProperty("id").GetString());
        Assert.Equal("Town", result.GetProperty("location").GetString());
        Assert.True(result.GetProperty("is_festival").GetBoolean());
    }

    [Fact]
    public void Handle_RejectsMismatchedExpectedLocation()
    {
        var world = new FakeFestivalStartWorld
        {
            ErrorCode = JsonRpcErrorCode.GameStateInvalid,
            ErrorMessage = "festival.start expected location Town but festival is in Forest",
        };
        var p = JsonDocument.Parse("{\"location\":\"Town\"}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => FestivalStartHandler.Handle(p, world));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("expected location Town", ex.Message);
    }

    private sealed class FakeFestivalStartWorld : IFestivalStartWorld
    {
        public string? ExpectedLocation { get; private set; }
        public FestivalStartResultForTests Result { get; set; } = new();
        public JsonRpcErrorCode? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }

        public FestivalStartResultForTests StartCurrentFestival(string? expectedLocation)
        {
            ExpectedLocation = expectedLocation;
            if (ErrorCode is { } code)
                throw new JsonRpcException(code, ErrorMessage ?? "festival.start failed");
            return Result;
        }
    }
}
```

- [ ] **Step 2: Run the focused test and confirm RED**

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~FestivalStartHandlerTests"
```

Expected: compile failure because `FestivalStartHandler`, `IFestivalStartWorld`, and `FestivalStartResultForTests` do not exist.

- [ ] **Step 3: Implement handler and world abstraction**

Create `src/Harness/Handlers/FestivalStartHandler.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol.Json;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>festival.start</c>. Starts the current date's active festival through Stardew festival APIs.</summary>
public static class FestivalStartHandler
{
    public const string Method = "festival.start";

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, new SdvFestivalStartWorld());

    internal static JsonElement Handle(JsonElement? paramsElement, IFestivalStartWorld world)
    {
        var req = RpcParams.Optional<FestivalStartRequest>(paramsElement);
        if (req.Location is { Length: 0 })
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.location must not be empty");

        return ProtocolJson.ToElement(world.StartCurrentFestival(req.Location));
    }
}

internal sealed class FestivalStartRequest
{
    public string? Location { get; set; }
}

internal interface IFestivalStartWorld
{
    FestivalStartResultForTests StartCurrentFestival(string? expectedLocation);
}

internal sealed class FestivalStartResultForTests
{
    public int Tick { get; set; }
    public string Id { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public bool IsFestival { get; set; }
}
```

Then implement `SdvFestivalStartWorld` in the same file:

```csharp
internal sealed class SdvFestivalStartWorld : IFestivalStartWorld
{
    public FestivalStartResultForTests StartCurrentFestival(string? expectedLocation)
    {
        RpcPreconditions.RequireWorldReady();

        var festivalId = $"{Game1.currentSeason}{Game1.dayOfMonth}";
        if (!Utility.isFestivalDay())
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"festival.start found no active festival for {festivalId}");

        if (!Event.tryToLoadFestivalData(
                festivalId,
                out var assetName,
                out Dictionary<string, string>? data,
                out var locationName,
                out var startTime,
                out var endTime))
        {
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"festival.start could not load festival data for {festivalId}");
        }

        if (!string.IsNullOrWhiteSpace(expectedLocation)
            && !string.Equals(expectedLocation, locationName, StringComparison.Ordinal))
        {
            throw new JsonRpcException(
                JsonRpcErrorCode.GameStateInvalid,
                $"festival.start expected location {expectedLocation} but festival is in {locationName}");
        }

        if (Game1.timeOfDay < startTime || Game1.timeOfDay > endTime)
        {
            throw new JsonRpcException(
                JsonRpcErrorCode.GameStateInvalid,
                $"festival.start requires time between {startTime} and {endTime} for {festivalId}; current time is {Game1.timeOfDay}");
        }

        var location = Game1.getLocationFromName(locationName)
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"festival.start could not find location {locationName}");

        Game1.whereIsTodaysFest = locationName;
        if (!Event.tryToLoadFestival(festivalId, out var ev))
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"festival.start could not create festival event {festivalId}");

        // The event must already be on the destination location before the warp so SMAPI
        // Player.Warped observers see e.NewLocation.currentEvent as a festival.
        location.currentEvent = ev;
        Game1.warpFarmer(locationName, Game1.player.TilePoint.X, Game1.player.TilePoint.Y, false);
        location.startEvent(ev);

        return new FestivalStartResultForTests
        {
            Tick = Game1.ticks,
            Id = festivalId,
            Location = locationName,
            IsFestival = ev.isFestival,
        };
    }
}
```

The live SVE scenario is the acceptance test for the `location.currentEvent` before-warp lifecycle. If it fails because Stardew clears `currentEvent` during the warp, keep the same neutral API and adjust only `SdvFestivalStartWorld` to start through the game's touch-action/festival entry path.

- [ ] **Step 4: Register handler**

In `ModEntry.Entry`, add near `event.start`:

```csharp
_rpc.Register(FestivalStartHandler.Method, p => FestivalStartHandler.Handle(p));
```

Update the console method list to include `festival.start` in the manipulators section.

- [ ] **Step 5: Run focused handler tests and build**

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~FestivalStartHandlerTests"
dotnet build /home/fintan/stardewRepos/frobby/sdv-test-framework/sdv-test-framework.sln
```

Expected: tests pass and build succeeds.

- [ ] **Step 6: Commit Task 4**

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework add src/Harness/Handlers/FestivalStartHandler.cs src/Harness/ModEntry.cs tests/Harness.Tests/FestivalStartHandlerTests.cs
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework commit -m "feat: start active festivals"
```

## Task 5: Documentation

**Files:**
- Modify: `docs/rpc-schema.md`
- Modify: `docs/dsl-quickstart.md`
- Modify: `README.md`

- [ ] **Step 1: Update RPC schema**

In `docs/rpc-schema.md`, update `state.location` object docs with:

```markdown
Container objects include:

- `is_chest`: true for chest-like containers;
- `item_count`: count of projected contained items;
- `items_truncated`: true when the contained item list was capped;
- `items`: contained item summaries with `slot`, `id`, `item_id`, `qualified_id`, `name`, `stack`, `quality`, `category`, and `runtime_type`.
```

Add a `festival.start` method section after `event.start`:

```markdown
### festival.start

Starts the active festival for the current in-game date/time through Stardew festival APIs.

→ `{ "jsonrpc": "2.0", "id": 11, "method": "festival.start", "params": { "location": "Town" } }`

← `{ "jsonrpc": "2.0", "id": 11, "result": { "tick": 8421, "id": "fall27", "location": "Town", "is_festival": true } }`

`params.location` is optional. When supplied, Frobby validates that the active festival is in that location. Follow with `wait.event_active` using `is_festival: true`.
```

Update runner-only waits:

```markdown
- `wait.location_content` supports contained-item filters for `objects`: `contains_item_id`, `contains_item_qualified_id`, `contains_item_name`, `contains_item_stack`, `contains_item_stack_gte`, `contains_item_quality`, and `contains_item_category`.
- `wait.event_active` accepts `is_festival` to require an active festival event.
```

- [ ] **Step 2: Update DSL quickstart**

Add a festival/container example:

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

- [ ] **Step 3: Update README capability list**

Add to the SVE capability guidance:

```markdown
- Use `festival.start`, `wait.event_active` with `is_festival`, and object contained-item filters when testing festival maps, festival chests, or other runtime containers.
```

- [ ] **Step 4: Run doc smoke searches**

```bash
rg -n "festival.start|contains_item_qualified_id|is_festival|item_count|items_truncated" /home/fintan/stardewRepos/frobby/sdv-test-framework/README.md /home/fintan/stardewRepos/frobby/sdv-test-framework/docs/rpc-schema.md /home/fintan/stardewRepos/frobby/sdv-test-framework/docs/dsl-quickstart.md
```

Expected: all new terms appear in the relevant docs.

- [ ] **Step 5: Commit Task 5**

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework add README.md docs/rpc-schema.md docs/dsl-quickstart.md
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework commit -m "docs: document festival and container testing"
```

## Task 6: SVE Spirit's Eve Scenario

**Files:**
- Create: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/19-sve-spirit-eve-chest.test.json`
- Modify: `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`

- [ ] **Step 1: Add the SVE scenario**

Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/19-sve-spirit-eve-chest.test.json`:

```json
{
  "name": "sve_spirit_eve_chest",
  "fixture": "m0spike_436515781",
  "config": { "seed": 436515781 },
  "steps": [
    { "action": "time.set", "args": { "time": 2200, "day": 27, "season": "fall", "year": 1 } },
    { "action": "festival.start", "args": { "location": "Town" } },
    {
      "action": "wait.event_active",
      "args": {
        "location": "Town",
        "is_festival": true,
        "timeout_ms": 10000,
        "poll_ms": 100
      }
    },
    {
      "action": "wait.location_content",
      "args": {
        "location": "Town",
        "collection": "objects",
        "runtime_type": "Chest",
        "x": 63,
        "y": 16,
        "contains_item_qualified_id": "(O)373",
        "contains_item_stack": 1,
        "min_count": 1,
        "max_count": 1,
        "timeout_ms": 15000,
        "poll_ms": 250
      }
    },
    { "action": "screenshot.capture_next_frame", "args": { "name": "final" } }
  ],
  "assertions": [
    {
      "type": "state",
      "expr": "state.event.is_festival == true",
      "message": "Spirit's Eve should be active as a festival event"
    },
    {
      "type": "state",
      "expr": "state.location.objects contains qualified_id '(O)130'",
      "message": "Spirit's Eve festival chest should be present in the active festival map"
    }
  ]
}
```

If the active chest object's `qualified_id` differs in the live result, keep the contained-item wait as the source of truth and update the second assertion to a stable exposed field from `state.location.objects`.

- [ ] **Step 2: Run the scenario headless and confirm RED or GREEN**

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework /home/fintan/stardewRepos/StardewValleyExpanded/scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-14 /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/19-sve-spirit-eve-chest.test.json
```

Expected after Tasks 1-5: pass. If it fails in `festival.start`, inspect the report and adjust only the neutral festival entry implementation.

- [ ] **Step 3: Update SVE Frobby docs**

Add a paragraph to `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`:

```markdown
Scenario `tests/sdv/19-sve-spirit-eve-chest.test.json` covers an active
festival map variant. It sets Fall 27 Year 1, enters Spirit's Eve through
Frobby's neutral `festival.start` action, waits for an active festival event,
and validates SVE's relocated festival chest contains one Golden Pumpkin.
```

- [ ] **Step 4: Commit SVE scenario**

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded add tests/sdv/19-sve-spirit-eve-chest.test.json docs/FROBBY.md
git -C /home/fintan/stardewRepos/StardewValleyExpanded commit -m "test: add spirit eve frobby scenario"
```

## Task 7: TODO And Final Verification

**Files:**
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Mark Slice 14 complete in the Frobby TODO**

Change the Slice 14 entry to:

```markdown
- [x] Done: Slice 14, festivals, movie theater, and special map variants.
  - Design spec: `docs/superpowers/specs/2026-05-13-sve-slice-14-spirit-eve-chest-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-13-sve-slice-14-spirit-eve-chest.md`.
  - SVE pressure: custom festival maps, grange judging patches, Spirit's Eve chest edits, and movie theater NPC behavior.
  - Frobby goal: set up festival/theater contexts, inspect event or festival state, interact with festival shops/chests/NPCs, and assert variant-specific content.
  - Done: neutral container item projection, contained-item waits, `festival.start`, `wait.event_active.is_festival`, and SVE scenario 19 (`sve_spirit_eve_chest`) verified headlessly against SVE's Spirit's Eve Golden Pumpkin chest behavior.
  - Follow-up candidates: movie theater NPC interaction coverage, grange judging assertions, festival shops, and passive festival map variants.
```

- [ ] **Step 2: Run Frobby focused and full tests**

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~LocationContentProjectorTests|FullyQualifiedName~FestivalStartHandlerTests"
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~WaitLocationContent|FullyQualifiedName~WaitEvent"
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/sdv-test-framework.sln
```

Expected: all pass.

- [ ] **Step 3: Run SVE and Starberg smoke tests headless**

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework /home/fintan/stardewRepos/StardewValleyExpanded/scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-14-final /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/19-sve-spirit-eve-chest.test.json
```

```bash
cd /home/fintan/stardewRepos/stonks
./scripts/sdv-test --headless tests/sdv/01-starberg-terminal-open.test.json
```

Expected: both pass.

- [ ] **Step 4: Commit final Frobby TODO update**

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework add SVE_FROBBY_CAPABILITY_TODO.md
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework commit -m "docs: mark spirit eve slice complete"
```

- [ ] **Step 5: Final status check**

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
git -C /home/fintan/stardewRepos/stonks status --short --branch
```

Expected:

- Frobby is on `feature/sve-slice-14-spirit-eve-chest` with only committed changes.
- SVE is on `feature/frobby-sve-slice-14-spirit-eve-chest` with only committed changes.
- Stonks has no new changes from this slice.

## Self-Review Checklist

- Spec coverage: container projection is Task 1, contained waits are Task 2, festival event waits are Task 3, `festival.start` is Task 4, docs are Task 5, SVE proof is Task 6, TODO/final verification is Task 7.
- Neutrality: no Frobby production code references SVE, Golden Pumpkin, Spirit's Eve, or SVE tile coordinates.
- TDD: every production change starts with a failing focused test.
- Verification: focused unit tests, full Frobby tests, live SVE headless scenario, and Starberg smoke are required before completion.
