# SVE Slice 13 Object Interactions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add neutral Frobby object placement and object metadata so SVE's Golden Piggy Bank patched interaction can be tested headlessly.

**Architecture:** Add a `world.place_object` RPC next to `world.place_furniture`, with a small test seam so validation and placement behavior are unit-testable without a live SDV process. Move object summary projection into `LocationContentProjector`, extend runner-side `wait.location_content` object filters, then add a single SVE scenario that places the Golden Piggy Bank and verifies that `world.interact_tile` decreases money through SVE's real Harmony patch.

**Tech Stack:** C#/.NET, SMAPI/StardewValley APIs, System.Text.Json snake-case protocol serialization, xUnit, Frobby JSON scenarios, SVE repo-local `scripts/sdv-test --headless`.

---

## File Structure

- Modify `src/Protocol/Models/LocationState.cs`: add additive object metadata fields.
- Create `src/Protocol/Models/PlaceObjectRequest.cs`: request DTO for `world.place_object`.
- Create `src/Protocol/Models/PlaceObjectResult.cs`: response DTO for `world.place_object`.
- Modify `tests/Protocol.Tests/LocationStateSerializationTests.cs`: assert snake-case object metadata serialization.
- Create `tests/Protocol.Tests/PlaceObjectSerializationTests.cs`: assert request/response JSON shape.
- Modify `src/Harness/Handlers/LocationContentProjector.cs`: add object projection with runtime type, big-craftable, ready, and held-object metadata.
- Modify `src/Harness/Handlers/LocationStateProjector.cs`: use `LocationContentProjector.ProjectObject`.
- Modify `tests/Harness.Tests/LocationContentProjectorTests.cs`: add object projection tests using lightweight fake objects.
- Create `src/Harness/Handlers/WorldPlaceObjectHandler.cs`: neutral object placement handler and production world adapter.
- Create `tests/Harness.Tests/WorldPlaceObjectHandlerTests.cs`: validation and placement unit tests through the fake world seam.
- Modify `src/Harness/ModEntry.cs`: register `world.place_object` and include it in the startup RPC list.
- Modify `src/Runner/Scenarios/ScenarioRunner.cs`: support `big_craftable`, `held_object_id`, and `held_object_qualified_id` in `wait.location_content`.
- Modify `tests/Runner.Tests/ScenarioRunnerTests.cs`: add runner wait coverage for object metadata filters and timeout text.
- Modify `docs/rpc-schema.md`: document state object metadata and `world.place_object`.
- Modify `docs/dsl-quickstart.md`: add a placed object interaction example.
- Modify `schemas/scenario.schema.json`: refresh step/action description to mention `world.place_object` and object metadata waits.
- Modify `SVE_FROBBY_CAPABILITY_TODO.md`: mark Slice 13 active during implementation and done after verification.
- Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/18-sve-object-piggy-bank-interaction.test.json`: SVE proof scenario.

## Task 0: Branch And Baseline

**Files:**
- No file changes.

- [ ] **Step 1: Confirm clean Frobby and SVE state**

Run:
```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
```

Expected: Frobby is clean. SVE may still be on the prior SVE feature branch, but must be clean.

- [ ] **Step 2: Create implementation branches**

Run:
```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework switch -c feature/sve-slice-13-object-interactions
git -C /home/fintan/stardewRepos/StardewValleyExpanded switch -c feature/frobby-sve-slice-13-object-interactions
```

Expected: both branch switches succeed. Do not merge SVE into `master` unless the user explicitly asks.

## Task 1: Protocol Models

**Files:**
- Modify: `src/Protocol/Models/LocationState.cs`
- Create: `src/Protocol/Models/PlaceObjectRequest.cs`
- Create: `src/Protocol/Models/PlaceObjectResult.cs`
- Modify: `tests/Protocol.Tests/LocationStateSerializationTests.cs`
- Create: `tests/Protocol.Tests/PlaceObjectSerializationTests.cs`

- [ ] **Step 1: Write failing protocol serialization tests**

In `tests/Protocol.Tests/LocationStateSerializationTests.cs`, extend the existing `ObjectSummary` literal:

```csharp
RuntimeType = "Object",
BigCraftable = true,
ReadyForHarvest = false,
HeldObjectId = "340",
HeldObjectQualifiedId = "(O)340",
HeldObjectName = "Honey",
```

Replace the existing object JSON assertion with:

```csharp
Assert.Contains("\"objects\":[{\"tile\":{\"x\":10,\"y\":10},\"name\":\"Weeds\",\"id\":\"O771\",\"qualified_id\":\"(O)771\",\"category\":-999,\"stack\":1,\"quality\":0,\"runtime_type\":\"Object\",\"big_craftable\":true,\"ready_for_harvest\":false,\"held_object_id\":\"340\",\"held_object_qualified_id\":\"(O)340\",\"held_object_name\":\"Honey\"}]", json);
```

Create `tests/Protocol.Tests/PlaceObjectSerializationTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class PlaceObjectSerializationTests
{
    [Fact]
    public void Request_UsesSnakeCaseFields()
    {
        var req = new PlaceObjectRequest
        {
            Id = "(BC)Example.Mod_BigCraftable",
            Location = "FarmHouse",
            X = 8,
            Y = 9,
            Stack = 2,
            Quality = 1,
            RemoveExisting = true,
        };

        var json = JsonSerializer.Serialize(req, ProtocolJson.Options);

        Assert.Contains("\"id\":\"(BC)Example.Mod_BigCraftable\"", json);
        Assert.Contains("\"location\":\"FarmHouse\"", json);
        Assert.Contains("\"x\":8", json);
        Assert.Contains("\"y\":9", json);
        Assert.Contains("\"stack\":2", json);
        Assert.Contains("\"quality\":1", json);
        Assert.Contains("\"remove_existing\":true", json);
    }

    [Fact]
    public void Result_UsesSnakeCaseFields()
    {
        var result = new PlaceObjectResult
        {
            Tick = 1234,
            Id = "Example.Mod_BigCraftable",
            QualifiedId = "(BC)Example.Mod_BigCraftable",
            Name = "Example Big Craftable",
            Location = "FarmHouse",
            Tile = new TilePoint { X = 8, Y = 9 },
            BigCraftable = true,
            RuntimeType = "Object",
        };

        var json = JsonSerializer.Serialize(result, ProtocolJson.Options);

        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"tick\":1234", json);
        Assert.Contains("\"id\":\"Example.Mod_BigCraftable\"", json);
        Assert.Contains("\"qualified_id\":\"(BC)Example.Mod_BigCraftable\"", json);
        Assert.Contains("\"name\":\"Example Big Craftable\"", json);
        Assert.Contains("\"location\":\"FarmHouse\"", json);
        Assert.Contains("\"tile\":{\"x\":8,\"y\":9}", json);
        Assert.Contains("\"big_craftable\":true", json);
        Assert.Contains("\"runtime_type\":\"Object\"", json);
    }
}
```

- [ ] **Step 2: Run protocol tests and confirm failure**

Run:
```bash
cd /home/fintan/stardewRepos/frobby/sdv-test-framework
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --configuration Debug --filter "LocationStateSerializationTests|PlaceObjectSerializationTests"
```

Expected: fail because `ObjectSummary` metadata fields, `PlaceObjectRequest`, and `PlaceObjectResult` do not exist.

- [ ] **Step 3: Implement protocol DTOs**

In `src/Protocol/Models/LocationState.cs`, extend `ObjectSummary`:

```csharp
public string RuntimeType { get; set; } = string.Empty;
public bool BigCraftable { get; set; }
public bool? ReadyForHarvest { get; set; }
public string? HeldObjectId { get; set; }
public string? HeldObjectQualifiedId { get; set; }
public string? HeldObjectName { get; set; }
```

Create `src/Protocol/Models/PlaceObjectRequest.cs`:

```csharp
namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape for <c>world.place_object</c>.</summary>
public sealed class PlaceObjectRequest
{
    /// <summary>Qualified or raw SDV object item id accepted by <c>ItemRegistry</c>.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Optional location name. Null means current location.</summary>
    public string? Location { get; set; }

    /// <summary>Tile X coordinate.</summary>
    public int? X { get; set; }

    /// <summary>Tile Y coordinate.</summary>
    public int? Y { get; set; }

    /// <summary>Optional stack override. Null keeps the created object's stack.</summary>
    public int? Stack { get; set; }

    /// <summary>Optional quality override. Null keeps the created object's quality.</summary>
    public int? Quality { get; set; }

    /// <summary>When true, remove an existing object at the target tile before adding.</summary>
    public bool RemoveExisting { get; set; }
}
```

Create `src/Protocol/Models/PlaceObjectResult.cs`:

```csharp
namespace SdvTestFramework.Protocol.Models;

/// <summary>Response shape for <c>world.place_object</c>.</summary>
public sealed class PlaceObjectResult : MutatorOk
{
    public string Id { get; set; } = string.Empty;
    public string QualifiedId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public TilePoint Tile { get; set; } = new();
    public bool BigCraftable { get; set; }
    public string RuntimeType { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Run protocol tests and confirm pass**

Run:
```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --configuration Debug --filter "LocationStateSerializationTests|PlaceObjectSerializationTests"
```

Expected: pass.

- [ ] **Step 5: Commit protocol changes**

Run:
```bash
git add src/Protocol/Models/LocationState.cs src/Protocol/Models/PlaceObjectRequest.cs src/Protocol/Models/PlaceObjectResult.cs tests/Protocol.Tests/LocationStateSerializationTests.cs tests/Protocol.Tests/PlaceObjectSerializationTests.cs
git commit -m "feat: add object placement protocol models"
```

## Task 2: Object State Projection

**Files:**
- Modify: `src/Harness/Handlers/LocationContentProjector.cs`
- Modify: `src/Harness/Handlers/LocationStateProjector.cs`
- Modify: `tests/Harness.Tests/LocationContentProjectorTests.cs`

- [ ] **Step 1: Write failing object projection tests**

Append these tests and fake classes to `tests/Harness.Tests/LocationContentProjectorTests.cs`:

```csharp
[Fact]
public void ProjectObject_ReadsObjectMetadata()
{
    var obj = new FakeLocationObject
    {
        Name = "Golden Piggy Bank",
        ItemId = "Example_Golden_Piggy_Bank",
        QualifiedItemId = "(BC)Example_Golden_Piggy_Bank",
        Category = -9,
        Stack = 1,
        Quality = 0,
        bigCraftable = new FakeValueWrapper<bool> { Value = true },
        readyForHarvest = new FakeValueWrapper<bool> { Value = false },
    };

    var summary = LocationContentProjector.ProjectObjectForTests(new Vector2(8, 9), obj);

    Assert.Equal(8, summary.Tile.X);
    Assert.Equal(9, summary.Tile.Y);
    Assert.Equal("Golden Piggy Bank", summary.Name);
    Assert.Equal("Example_Golden_Piggy_Bank", summary.Id);
    Assert.Equal("(BC)Example_Golden_Piggy_Bank", summary.QualifiedId);
    Assert.Equal(-9, summary.Category);
    Assert.Equal(1, summary.Stack);
    Assert.Equal(0, summary.Quality);
    Assert.Equal("FakeLocationObject", summary.RuntimeType);
    Assert.True(summary.BigCraftable);
    Assert.False(summary.ReadyForHarvest);
}

[Fact]
public void ProjectObject_ReadsHeldObjectMetadata()
{
    var obj = new FakeLocationObject
    {
        Name = "Example Machine",
        ItemId = "Example_Machine",
        QualifiedItemId = "(BC)Example_Machine",
        bigCraftable = new FakeValueWrapper<bool> { Value = true },
        heldObject = new FakeValueWrapper<FakeHeldObject>
        {
            Value = new FakeHeldObject
            {
                Name = "Honey",
                ItemId = "340",
                QualifiedItemId = "(O)340",
            },
        },
    };

    var summary = LocationContentProjector.ProjectObjectForTests(new Vector2(4, 5), obj);

    Assert.Equal("340", summary.HeldObjectId);
    Assert.Equal("(O)340", summary.HeldObjectQualifiedId);
    Assert.Equal("Honey", summary.HeldObjectName);
}

private sealed class FakeLocationObject
{
    public string Name = string.Empty;
    public string ItemId = string.Empty;
    public string QualifiedItemId = string.Empty;
    public int Category;
    public int Stack;
    public int Quality;
    public FakeValueWrapper<bool>? bigCraftable;
    public FakeValueWrapper<bool>? readyForHarvest;
    public FakeValueWrapper<FakeHeldObject>? heldObject;
}

private sealed class FakeHeldObject
{
    public string Name = string.Empty;
    public string ItemId = string.Empty;
    public string QualifiedItemId = string.Empty;
}
```

- [ ] **Step 2: Run projection tests and confirm failure**

Run:
```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --configuration Debug --filter LocationContentProjectorTests
```

Expected: fail because `ProjectObjectForTests` does not exist.

- [ ] **Step 3: Implement object projection**

In `src/Harness/Handlers/LocationContentProjector.cs`, add public/internal projection entry points near the existing debris methods:

```csharp
public static ObjectSummary ProjectObject(Vector2 tile, object obj)
    => ProjectLocationObject(tile, obj);

internal static ObjectSummary ProjectObjectForTests(Vector2 tile, object obj)
    => ProjectLocationObject(tile, obj);
```

Add this private method:

```csharp
private static ObjectSummary ProjectLocationObject(Vector2 tile, object obj)
{
    var heldObject = ReadValueProperty(ReadMemberRaw(obj, "heldObject", "HeldObject"))
        ?? ReadMemberRaw(obj, "heldObject", "HeldObject");
    var qualifiedId = ReadString(obj, "QualifiedItemId", "qualifiedItemId") ?? string.Empty;

    return new ObjectSummary
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
}
```

Add a bool reader next to `ReadInt`:

```csharp
private static bool? ReadBool(object? instance, params string[] names)
{
    if (instance is null)
        return null;

    var value = ReadMemberRaw(instance, names);
    value = ReadValueProperty(value) ?? value;

    return value switch
    {
        bool b => b,
        _ => null,
    };
}
```

In `src/Harness/Handlers/LocationStateProjector.cs`, replace the inline object summary construction with:

```csharp
foreach (var kv in loc.Objects.Pairs)
{
    state.Objects.Add(LocationContentProjector.ProjectObject(kv.Key, kv.Value));
}
```

- [ ] **Step 4: Run projection tests and confirm pass**

Run:
```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --configuration Debug --filter LocationContentProjectorTests
```

Expected: pass.

- [ ] **Step 5: Commit projection changes**

Run:
```bash
git add src/Harness/Handlers/LocationContentProjector.cs src/Harness/Handlers/LocationStateProjector.cs tests/Harness.Tests/LocationContentProjectorTests.cs
git commit -m "feat: project object interaction metadata"
```

## Task 3: `world.place_object` Handler

**Files:**
- Create: `src/Harness/Handlers/WorldPlaceObjectHandler.cs`
- Create: `tests/Harness.Tests/WorldPlaceObjectHandlerTests.cs`
- Modify: `src/Harness/ModEntry.cs`

- [ ] **Step 1: Write failing handler tests**

Create `tests/Harness.Tests/WorldPlaceObjectHandlerTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class WorldPlaceObjectHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceObjectHandler.Handle(null, new FakeObjectPlacementWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_MissingId_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"x\":8,\"y\":9}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceObjectHandler.Handle(p, new FakeObjectPlacementWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("id", ex.Message);
    }

    [Fact]
    public void Handle_MissingX_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"id\":\"(O)388\",\"y\":9}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceObjectHandler.Handle(p, new FakeObjectPlacementWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("x", ex.Message);
    }

    [Fact]
    public void Handle_MissingY_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"id\":\"(O)388\",\"x\":8}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceObjectHandler.Handle(p, new FakeObjectPlacementWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("y", ex.Message);
    }

    [Theory]
    [InlineData("{\"id\":\"(O)388\",\"x\":-1,\"y\":9}", "x")]
    [InlineData("{\"id\":\"(O)388\",\"x\":8,\"y\":-1}", "y")]
    [InlineData("{\"id\":\"(O)388\",\"x\":8,\"y\":9,\"stack\":0}", "stack")]
    [InlineData("{\"id\":\"(O)388\",\"x\":8,\"y\":9,\"quality\":-1}", "quality")]
    public void Handle_InvalidNumericParams_ThrowsInvalidParams(string json, string field)
    {
        var p = JsonDocument.Parse(json).RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceObjectHandler.Handle(p, new FakeObjectPlacementWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains(field, ex.Message);
    }

    [Fact]
    public void Handle_NoLoadedWorld_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"id\":\"(O)388\",\"x\":8,\"y\":9}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceObjectHandler.Handle(p, new FakeObjectPlacementWorld { IsWorldReady = false }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_UnknownItem_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"id\":\"(O)missing\",\"x\":8,\"y\":9}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceObjectHandler.Handle(p, new FakeObjectPlacementWorld()));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("(O)missing", ex.Message);
    }

    [Fact]
    public void Handle_NonObjectItem_ThrowsGameStateInvalid()
    {
        var world = new FakeObjectPlacementWorld();
        world.Items["(F)1302"] = null;
        var p = JsonDocument.Parse("{\"id\":\"(F)1302\",\"x\":8,\"y\":9}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceObjectHandler.Handle(p, world));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("not an object", ex.Message);
    }

    [Fact]
    public void Handle_PlacesObjectAndReturnsMetadata()
    {
        var world = new FakeObjectPlacementWorld();
        world.Items["(BC)Example_Golden_Piggy_Bank"] = new FakePlaceableObject
        {
            Id = "Example_Golden_Piggy_Bank",
            QualifiedId = "(BC)Example_Golden_Piggy_Bank",
            Name = "Golden Piggy Bank",
            Stack = 1,
            Quality = 0,
            BigCraftable = true,
            RuntimeType = "Object",
        };
        var p = JsonDocument.Parse("{\"id\":\"(BC)Example_Golden_Piggy_Bank\",\"location\":\"FarmHouse\",\"x\":8,\"y\":9,\"stack\":2,\"quality\":1,\"remove_existing\":true}").RootElement;

        var json = WorldPlaceObjectHandler.Handle(p, world);
        var result = JsonSerializer.Deserialize<PlaceObjectResult>(json, ProtocolJson.Options)!;

        Assert.True(result.Ok);
        Assert.Equal(1234, result.Tick);
        Assert.Equal("Example_Golden_Piggy_Bank", result.Id);
        Assert.Equal("(BC)Example_Golden_Piggy_Bank", result.QualifiedId);
        Assert.Equal("Golden Piggy Bank", result.Name);
        Assert.Equal("FarmHouse", result.Location);
        Assert.Equal(8, result.Tile.X);
        Assert.Equal(9, result.Tile.Y);
        Assert.True(result.BigCraftable);
        Assert.Equal("Object", result.RuntimeType);
        Assert.Equal("FarmHouse", world.PlacedLocation);
        Assert.Equal(8, world.PlacedX);
        Assert.Equal(9, world.PlacedY);
        Assert.True(world.LastRemoveExisting);
        Assert.Equal(2, world.PlacedObject!.Stack);
        Assert.Equal(1, world.PlacedObject.Quality);
    }

    private sealed class FakeObjectPlacementWorld : IObjectPlacementWorld
    {
        public bool IsWorldReady { get; init; } = true;
        public int Tick => 1234;
        public string CurrentLocation => "Farm";
        public Dictionary<string, IPlaceableObject?> Items { get; } = new();
        public string? PlacedLocation { get; private set; }
        public int? PlacedX { get; private set; }
        public int? PlacedY { get; private set; }
        public bool LastRemoveExisting { get; private set; }
        public IPlaceableObject? PlacedObject { get; private set; }

        public bool ItemExists(string id) => Items.ContainsKey(id);
        public IPlaceableObject? CreateObject(string id) => Items[id];

        public void PlaceObject(IPlaceableObject obj, string? location, int x, int y, bool removeExisting)
        {
            PlacedObject = obj;
            PlacedLocation = location;
            PlacedX = x;
            PlacedY = y;
            LastRemoveExisting = removeExisting;
        }
    }

    private sealed class FakePlaceableObject : IPlaceableObject
    {
        public string Id { get; init; } = string.Empty;
        public string QualifiedId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public int Stack { get; set; }
        public int Quality { get; set; }
        public bool BigCraftable { get; init; }
        public string RuntimeType { get; init; } = string.Empty;
    }
}
```

- [ ] **Step 2: Run handler tests and confirm failure**

Run:
```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --configuration Debug --filter WorldPlaceObjectHandlerTests
```

Expected: fail because `WorldPlaceObjectHandler`, `IObjectPlacementWorld`, and `IPlaceableObject` do not exist.

- [ ] **Step 3: Implement handler and test seam**

Create `src/Harness/Handlers/WorldPlaceObjectHandler.cs`:

```csharp
using System.Text.Json;
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using SObject = StardewValley.Object;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// Handler for <c>world.place_object</c>. Creates Stardew objects through
/// <see cref="ItemRegistry"/> and places them into a loaded location's object table.
/// </summary>
public static class WorldPlaceObjectHandler
{
    public const string Method = "world.place_object";

    private static readonly IObjectPlacementWorld ProductionWorld = new SdvObjectPlacementWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, IObjectPlacementWorld world)
    {
        var req = RpcParams.Required<PlaceObjectRequest>(paramsElement);
        if (string.IsNullOrWhiteSpace(req.Id))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.id required");
        if (req.X is null)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.x required");
        if (req.X < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.x must be >= 0");
        if (req.Y is null)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.y required");
        if (req.Y < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.y must be >= 0");
        if (req.Stack is not null && req.Stack < 1)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.stack must be >= 1");
        if (req.Quality is not null && req.Quality < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.quality must be >= 0");

        if (!world.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "world.place_object requires a loaded world");

        if (!world.ItemExists(req.Id))
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"unknown item id: {req.Id}");

        var obj = world.CreateObject(req.Id)
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"item is not an object: {req.Id}");

        if (req.Stack is not null)
            obj.Stack = req.Stack.Value;
        if (req.Quality is not null)
            obj.Quality = req.Quality.Value;

        var x = req.X.Value;
        var y = req.Y.Value;
        world.PlaceObject(obj, req.Location, x, y, req.RemoveExisting);

        return ProtocolJson.ToElement(new PlaceObjectResult
        {
            Tick = world.Tick,
            Id = obj.Id,
            QualifiedId = obj.QualifiedId,
            Name = obj.Name,
            Location = req.Location ?? world.CurrentLocation,
            Tile = new TilePoint { X = x, Y = y },
            BigCraftable = obj.BigCraftable,
            RuntimeType = obj.RuntimeType,
        });
    }
}

internal interface IObjectPlacementWorld
{
    bool IsWorldReady { get; }
    int Tick { get; }
    string CurrentLocation { get; }
    bool ItemExists(string id);
    IPlaceableObject? CreateObject(string id);
    void PlaceObject(IPlaceableObject obj, string? location, int x, int y, bool removeExisting);
}

internal interface IPlaceableObject
{
    string Id { get; }
    string QualifiedId { get; }
    string Name { get; }
    int Stack { get; set; }
    int Quality { get; set; }
    bool BigCraftable { get; }
    string RuntimeType { get; }
}

internal sealed class SdvObjectPlacementWorld : IObjectPlacementWorld
{
    public bool IsWorldReady => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;
    public int Tick => Game1.ticks;
    public string CurrentLocation => Game1.currentLocation?.Name ?? string.Empty;

    public bool ItemExists(string id) => ItemRegistry.Exists(id);

    public IPlaceableObject? CreateObject(string id)
    {
        var item = ItemRegistry.Create(id);
        return item is SObject obj ? new SdvPlaceableObject(obj) : null;
    }

    public void PlaceObject(IPlaceableObject obj, string? locationName, int x, int y, bool removeExisting)
    {
        if (obj is not SdvPlaceableObject sdvObject)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "world.place_object can only place live Stardew objects");

        var location = ResolveLocation(locationName);
        var tile = new Vector2(x, y);
        if (removeExisting)
        {
            location.Objects.Remove(tile);
        }
        else if (location.Objects.ContainsKey(tile))
        {
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"object already exists at tile {x},{y}; pass remove_existing=true to replace it");
        }

        location.Objects[tile] = sdvObject.Object;
    }

    private static GameLocation ResolveLocation(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Game1.currentLocation
                ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                    $"{WorldPlaceObjectHandler.Method} requires a current location");

        return Game1.getLocationFromName(name)
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"no location named: {name}");
    }
}

internal sealed class SdvPlaceableObject : IPlaceableObject
{
    public SdvPlaceableObject(SObject obj)
    {
        Object = obj;
    }

    public SObject Object { get; }
    public string Id => Object.ItemId ?? string.Empty;
    public string QualifiedId => Object.QualifiedItemId ?? string.Empty;
    public string Name => Object.Name ?? Object.DisplayName ?? Object.GetType().Name;
    public int Stack { get => Object.Stack; set => Object.Stack = value; }
    public int Quality { get => Object.Quality; set => Object.Quality = value; }
    public bool BigCraftable => Object.bigCraftable.Value;
    public string RuntimeType => Object.GetType().Name;
}
```

In `src/Harness/ModEntry.cs`, register the handler after `world.place_furniture`:

```csharp
_rpc.Register(WorldPlaceObjectHandler.Method, p => WorldPlaceObjectHandler.Handle(p));
```

Also add `world.place_object` to the startup RPC list string.

- [ ] **Step 4: Run handler tests and confirm pass**

Run:
```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --configuration Debug --filter WorldPlaceObjectHandlerTests
```

Expected: pass.

- [ ] **Step 5: Run harness projection and placement tests together**

Run:
```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --configuration Debug --filter "WorldPlaceObjectHandlerTests|LocationContentProjectorTests"
```

Expected: pass.

- [ ] **Step 6: Commit handler changes**

Run:
```bash
git add src/Harness/Handlers/WorldPlaceObjectHandler.cs src/Harness/ModEntry.cs tests/Harness.Tests/WorldPlaceObjectHandlerTests.cs
git commit -m "feat: add neutral object placement rpc"
```

## Task 4: Runner Object Metadata Filters

**Files:**
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`
- Modify: `tests/Runner.Tests/ScenarioRunnerTests.cs`

- [ ] **Step 1: Write failing runner tests**

Add this test near the existing `WaitLocationContent_*` tests in `tests/Runner.Tests/ScenarioRunnerTests.cs`:

```csharp
[Fact]
public async Task WaitLocationContent_FiltersObjectsByInteractionMetadata()
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
                    "state.location" => JsonDocument.Parse("{\"name\":\"FarmHouse\",\"objects\":[{\"tile\":{\"x\":8,\"y\":9},\"name\":\"Golden Piggy Bank\",\"id\":\"FlashShifter.StardewValleyExpandedCP_Golden_Piggy_Bank\",\"qualified_id\":\"(BC)FlashShifter.StardewValleyExpandedCP_Golden_Piggy_Bank\",\"runtime_type\":\"Object\",\"big_craftable\":true,\"held_object_id\":\"340\",\"held_object_qualified_id\":\"(O)340\"}],\"resource_clumps\":[],\"monsters\":[],\"debris\":[]}").RootElement,
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
        Name = "wait_location_content_object_metadata",
        Steps = new()
        {
            new ScenarioStep
            {
                Action = "wait.location_content",
                Args = JsonDocument.Parse("{\"location\":\"FarmHouse\",\"collection\":\"objects\",\"qualified_id\":\"(BC)FlashShifter.StardewValleyExpandedCP_Golden_Piggy_Bank\",\"runtime_type\":\"Object\",\"big_craftable\":true,\"held_object_id\":\"340\",\"held_object_qualified_id\":\"(O)340\",\"x\":8,\"y\":9,\"min_count\":1,\"max_count\":1,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
            },
        },
    }, cts.Token);

    Assert.True(report.Passed);

    cts.Cancel();
    try { await serverTask; } catch (OperationCanceledException) { }
}
```

Add this timeout-format test:

```csharp
[Fact]
public async Task WaitLocationContent_TimeoutIncludesObjectMetadataFilters()
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
                    "state.location" => JsonDocument.Parse("{\"name\":\"FarmHouse\",\"objects\":[{\"tile\":{\"x\":8,\"y\":9},\"name\":\"Plain Chest\",\"id\":\"130\",\"qualified_id\":\"(BC)130\",\"runtime_type\":\"Chest\",\"big_craftable\":true}],\"resource_clumps\":[],\"monsters\":[],\"debris\":[]}").RootElement,
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
        Name = "wait_location_content_object_timeout",
        Steps = new()
        {
            new ScenarioStep
            {
                Action = "wait.location_content",
                Args = JsonDocument.Parse("{\"location\":\"FarmHouse\",\"collection\":\"objects\",\"name\":\"Golden Piggy Bank\",\"runtime_type\":\"Object\",\"big_craftable\":true,\"held_object_id\":\"340\",\"timeout_ms\":20,\"poll_ms\":1}").RootElement,
            },
        },
    }, cts.Token);

    Assert.False(report.Passed);
    var failure = Assert.Single(report.Failures);
    Assert.Contains("matching name=Golden Piggy Bank, runtime_type=Object, big_craftable=True, held_object_id=340", failure);
    Assert.Contains("last observed 0 matched out of 1 objects", failure);

    cts.Cancel();
    try { await serverTask; } catch (OperationCanceledException) { }
}
```

- [ ] **Step 2: Run runner tests and confirm failure**

Run:
```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter "WaitLocationContent_FiltersObjectsByInteractionMetadata|WaitLocationContent_TimeoutIncludesObjectMetadataFilters"
```

Expected: first test fails because `big_craftable` and held-object filters are ignored; timeout test fails because filter text omits them.

- [ ] **Step 3: Implement wait filters**

In `src/Runner/Scenarios/ScenarioRunner.cs`, extend `LocationContentElementMatches`:

```csharp
&& BoolFilterMatches(element, "big_craftable", args.BigCraftable)
&& StringFilterMatches(element, "held_object_id", args.HeldObjectId)
&& StringFilterMatches(element, "held_object_qualified_id", args.HeldObjectQualifiedId)
```

In `FormatLocationContentFilters`, add:

```csharp
if (args.BigCraftable is not null) filters.Add($"big_craftable={args.BigCraftable}");
if (args.HeldObjectId is not null) filters.Add($"held_object_id={args.HeldObjectId}");
if (args.HeldObjectQualifiedId is not null) filters.Add($"held_object_qualified_id={args.HeldObjectQualifiedId}");
```

In `WaitLocationContentStepArgs`, add:

```csharp
public bool? BigCraftable { get; set; }
public string? HeldObjectId { get; set; }
public string? HeldObjectQualifiedId { get; set; }
```

- [ ] **Step 4: Run runner tests and confirm pass**

Run:
```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter "WaitLocationContent_FiltersObjectsByInteractionMetadata|WaitLocationContent_TimeoutIncludesObjectMetadataFilters"
```

Expected: pass.

- [ ] **Step 5: Commit runner changes**

Run:
```bash
git add src/Runner/Scenarios/ScenarioRunner.cs tests/Runner.Tests/ScenarioRunnerTests.cs
git commit -m "feat: filter location objects by interaction metadata"
```

## Task 5: Docs, Schema, And Capability Backlog

**Files:**
- Modify: `docs/rpc-schema.md`
- Modify: `docs/dsl-quickstart.md`
- Modify: `schemas/scenario.schema.json`
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Update RPC schema**

In `docs/rpc-schema.md`, update the `state.location` object example so the object entry includes:

```json
"runtime_type": "Object",
"big_craftable": false,
"ready_for_harvest": null,
"held_object_id": null,
"held_object_qualified_id": null,
"held_object_name": null
```

Add a new section after `world.place_furniture`:

````markdown
### world.place_object

Creates a Stardew object or big craftable via SDV's `ItemRegistry` and adds it to
a loaded location's object table. This is a deterministic test setup action; it
does not simulate player inventory, placement sounds, or collision rules.

`params.id` is required. Qualified ids such as `"(O)388"` and
`"(BC)Example.Mod_BigCraftable"` are accepted when Stardew accepts them.
`params.location` is optional and defaults to the current location. `params.x`
and `params.y` are required nonnegative tile coordinates. `params.stack` and
`params.quality` are optional overrides. `params.remove_existing` is optional and
defaults to `false`; pass `true` to replace an object already at the tile.

Request:
```json
→ { "jsonrpc": "2.0", "id": 12, "method": "world.place_object", "params": { "id": "(BC)Example.Mod_Golden_Piggy_Bank", "location": "FarmHouse", "x": 8, "y": 9, "remove_existing": true } }
```

Response:
```json
← { "jsonrpc": "2.0", "id": 12, "result": { "ok": true, "tick": 84200, "id": "Example.Mod_Golden_Piggy_Bank", "qualified_id": "(BC)Example.Mod_Golden_Piggy_Bank", "name": "Golden Piggy Bank", "location": "FarmHouse", "tile": { "x": 8, "y": 9 }, "big_craftable": true, "runtime_type": "Object" } }
```
````

Update the `wait.location_content` docs to include `big_craftable`,
`held_object_id`, and `held_object_qualified_id`.

- [ ] **Step 2: Update quickstart**

In `docs/dsl-quickstart.md`, add this example after the spawned-world-content section:

````markdown
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
````

- [ ] **Step 3: Update schema description**

In `schemas/scenario.schema.json`, change the `action.description` to:

```json
"description": "Scenario step action. Unknown actions are invoked as RPC methods, including player.set_transient_state, fishing.sample_catch, and world.place_object for ad hoc probes."
```

Change the assertion `type.description` only if it has become inaccurate during implementation. Do not add a rigid action enum; the schema intentionally allows new RPC actions.

- [ ] **Step 4: Mark Slice 13 active**

In `SVE_FROBBY_CAPABILITY_TODO.md`, change the Slice 13 checkbox block heading from:

```markdown
- [ ] Pending: Slice 13, object, chest, and buried reward interactions.
```

to:

```markdown
- [ ] Active: Slice 13, object, chest, and buried reward interactions.
  - Design spec: `docs/superpowers/specs/2026-05-12-sve-slice-13-object-interactions-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-12-sve-slice-13-object-interactions.md`.
```

Keep the existing pressure/goal/proof bullets below it.

- [ ] **Step 5: Commit docs and backlog updates**

Run:
```bash
git add docs/rpc-schema.md docs/dsl-quickstart.md schemas/scenario.schema.json SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "docs: document object placement testing"
```

## Task 6: SVE Golden Piggy Bank Scenario

**Files:**
- Create: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/18-sve-object-piggy-bank-interaction.test.json`

- [ ] **Step 1: Add the SVE proof scenario**

Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/18-sve-object-piggy-bank-interaction.test.json`:

```json
{
  "name": "sve_object_piggy_bank_interaction",
  "fixture": "m0spike_436515781",
  "config": { "seed": 436515781 },
  "steps": [
    { "action": "time.set", "args": { "time": 900, "day": 1, "season": "spring", "year": 1 } },
    { "action": "player.warp", "args": { "location": "FarmHouse", "x": 8, "y": 10 } },
    { "action": "wait.location", "args": { "location": "FarmHouse", "x": 8, "y": 10, "timeout_ms": 10000, "poll_ms": 100 } },
    { "action": "player.set_money", "args": { "amount": 5000 } },
    {
      "action": "world.place_object",
      "args": {
        "id": "(BC)FlashShifter.StardewValleyExpandedCP_Golden_Piggy_Bank",
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
        "name": "Golden Piggy Bank",
        "big_craftable": true,
        "runtime_type": "Object",
        "x": 8,
        "y": 9,
        "min_count": 1,
        "timeout_ms": 10000,
        "poll_ms": 100
      }
    },
    { "action": "world.interact_tile", "args": { "x": 8, "y": 9 } },
    {
      "action": "wait.location_content",
      "args": {
        "location": "FarmHouse",
        "collection": "objects",
        "name": "Golden Piggy Bank",
        "big_craftable": true,
        "x": 8,
        "y": 9,
        "min_count": 1,
        "timeout_ms": 5000,
        "poll_ms": 100
      }
    },
    { "action": "freeze.begin", "args": { "settle_timeout_ms": 10000, "poll_ms": 100 } },
    { "action": "screenshot.capture", "args": { "name": "final" } }
  ],
  "assertions": [
    {
      "type": "state",
      "expr": "state.player.money == 4999",
      "message": "Golden Piggy Bank interaction should consume exactly one gold"
    }
  ]
}
```

- [ ] **Step 2: Run the SVE scenario headlessly and inspect failure**

Run from the SVE repo:
```bash
cd /home/fintan/stardewRepos/StardewValleyExpanded
./scripts/sdv-test --headless --mod-set core tests/sdv/18-sve-object-piggy-bank-interaction.test.json
```

Expected first live result may fail if the SVE big-craftable id needs raw form instead of qualified form, or if the chosen tile is occupied. If the id fails, rerun with:

```json
"id": "FlashShifter.StardewValleyExpandedCP_Golden_Piggy_Bank"
```

Expected final result: the scenario passes and writes an HTML report under `/tmp/stardew-valley-expanded-frobby-results-0.1.0/`.

- [ ] **Step 3: Commit SVE scenario**

Run:
```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded add tests/sdv/18-sve-object-piggy-bank-interaction.test.json
git -C /home/fintan/stardewRepos/StardewValleyExpanded commit -m "test: add object interaction frobby scenario"
```

## Task 7: Final Verification And Completion Marker

**Files:**
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Run focused Frobby tests**

Run:
```bash
cd /home/fintan/stardewRepos/frobby/sdv-test-framework
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --configuration Debug --filter "LocationStateSerializationTests|PlaceObjectSerializationTests"
dotnet test tests/Harness.Tests/Harness.Tests.csproj --configuration Debug --filter "WorldPlaceObjectHandlerTests|LocationContentProjectorTests"
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter "WaitLocationContent"
```

Expected: all pass.

- [ ] **Step 2: Run broader Frobby build/tests**

Run:
```bash
dotnet build sdv-test-framework.slnx --configuration Debug
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --configuration Debug
dotnet test tests/Harness.Tests/Harness.Tests.csproj --configuration Debug
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug
```

Expected: build passes and all three test projects pass. If an unrelated skipped live-SDV test appears, record it in the final summary without changing it.

- [ ] **Step 3: Run SVE proof headlessly**

Run:
```bash
cd /home/fintan/stardewRepos/StardewValleyExpanded
./scripts/sdv-test --headless --mod-set core tests/sdv/18-sve-object-piggy-bank-interaction.test.json
```

Expected: scenario passes.

- [ ] **Step 4: Optionally run a small Starberg smoke only if runner behavior regressed**

Run this only if Task 4 changed shared runner wait behavior in a risky way:

```bash
cd /home/fintan/stardewRepos/stonks
./scripts/sdv-test --headless --mod-set core tests/sdv/01-starberg-terminal-opens.test.json
```

Expected: smoke passes. If that exact scenario path has changed, use the smallest current Starberg scenario that exercises runner lifecycle and a state assertion.

- [ ] **Step 5: Mark Slice 13 done in Frobby backlog**

In `SVE_FROBBY_CAPABILITY_TODO.md`, change the Slice 13 block heading to:

```markdown
- [x] Done: Slice 13, object, chest, and buried reward interactions.
```

Add a done bullet:

```markdown
  - Done: `world.place_object`, richer `state.location.objects` metadata, object-aware `wait.location_content` filters, and SVE scenario 18 (`sve_object_piggy_bank_interaction`) verified headlessly against SVE's Golden Piggy Bank patched object behavior.
  - Follow-up candidate: chest content summaries for festival/runtime storage and hoe/dig support for Secret Note buried rewards.
```

- [ ] **Step 6: Commit completion marker**

Run:
```bash
git add SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "docs: mark sve object interaction slice done"
```

- [ ] **Step 7: Final git status**

Run:
```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
```

Expected: both repos clean on their implementation branches.

## Self-Review

- Spec coverage: `world.place_object` is covered in Task 3, object metadata in Tasks 1-2, runner object filters in Task 4, docs/schema/backlog in Task 5, SVE Golden Piggy Bank proof in Task 6, and verification/completion in Task 7.
- Scope check: chest inventory and buried reward tooling stay out of this implementation and are recorded as follow-up candidates in Task 7.
- Type consistency: protocol uses `PlaceObjectRequest`, `PlaceObjectResult`, `ObjectSummary.RuntimeType`, `ObjectSummary.BigCraftable`, `WaitLocationContentStepArgs.BigCraftable`, `HeldObjectId`, and `HeldObjectQualifiedId` consistently across tasks.
- Neutrality check: all Frobby production code uses Stardew/ItemRegistry abstractions only; the only SVE-specific id appears in the SVE scenario.
