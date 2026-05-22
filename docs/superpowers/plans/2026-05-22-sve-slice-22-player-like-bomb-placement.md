# SVE Slice 22 Player-Like Bomb Placement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add generic inventory-object placement to Frobby and prove a player-like placed bomb/fuse flow against an SVE corrupt mummy.

**Architecture:** Add a neutral `world.place_inventory_object` protocol model, harness handler, runner label, and DSL wrapper. The harness handler selects an existing inventory `StardewValley.Object` and delegates the native placement behavior through an injectable world adapter; object lifecycle observation is exposed separately through `state.location.objects.minutes_until_ready` and runner-side `wait.location_content` filters.

**Tech Stack:** C#/.NET 10 runner and DSL, net6.0 SMAPI harness, Stardew Valley 1.6 runtime APIs, JSON-RPC protocol DTOs, xUnit, JSON scenario files, headless SVE `./scripts/sdv-test`.

---

## File Structure

Frobby protocol:

- Create `src/Protocol/Models/PlaceInventoryObjectRequest.cs`
  - Request/response DTOs for `world.place_inventory_object`.
- Create `tests/Protocol.Tests/PlaceInventoryObjectSerializationTests.cs`
  - Snake-case request/result serialization coverage.
- Modify `src/Protocol/Models/LocationState.cs`
  - Add nullable `MinutesUntilReady` to `ObjectSummary`.

Frobby harness:

- Create `src/Harness/Handlers/WorldPlaceInventoryObjectHandler.cs`
  - Validate params, select inventory object, invoke native placement through an injectable world adapter, and return diagnostics.
- Create `tests/Harness.Tests/WorldPlaceInventoryObjectHandlerTests.cs`
  - TDD validation, selection, slot, non-object, location guard, and success coverage.
- Modify `src/Harness/Handlers/LocationContentProjector.cs`
  - Project `minutes_until_ready` from runtime objects.
- Modify `tests/Harness.Tests/LocationContentProjectorTests.cs`
  - Add object lifecycle projection coverage.
- Modify `src/Harness/ModEntry.cs`
  - Register `world.place_inventory_object` and add it to startup method text.

Frobby runner and DSL:

- Modify `src/Runner/Scenarios/ScenarioRunner.cs`
  - Add readable label for `world.place_inventory_object`.
  - Add `minutes_until_ready_*` filters to `wait.location_content`.
- Modify `tests/Runner.Tests/ScenarioRunnerTests.cs`
  - Add pass-through/report-label coverage and object wait filter coverage.
- Modify `src/Runner.Dsl/World.cs`
  - Add `World.PlaceInventoryObject(...)`.
- Modify `tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs`
  - Add DSL invocation/result coverage.

Frobby docs/status:

- Modify `docs/rpc-schema.md`
- Modify `docs/dsl-quickstart.md`
- Modify `docs/wiki/examples.md`
- Modify `SVE_FROBBY_CAPABILITY_TODO.md`

SVE:

- Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/30-sve-combat-lab-bomb-mummy.test.json`
- Modify `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`

## Task 1: Protocol DTOs For `world.place_inventory_object`

**Files:**
- Create: `tests/Protocol.Tests/PlaceInventoryObjectSerializationTests.cs`
- Create: `src/Protocol/Models/PlaceInventoryObjectRequest.cs`

- [ ] **Step 1: Write failing protocol serialization tests**

Create `tests/Protocol.Tests/PlaceInventoryObjectSerializationTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class PlaceInventoryObjectSerializationTests
{
    [Fact]
    public void Request_DeserializesFromSnakeCase()
    {
        var json = "{\"id\":\"(O)287\",\"location\":\"Frobby_CombatLab\",\"x\":9,\"y\":8,\"slot\":12,\"facing\":\"right\"}";

        var req = JsonSerializer.Deserialize<PlaceInventoryObjectRequest>(json, ProtocolJson.Options)!;

        Assert.Equal("(O)287", req.Id);
        Assert.Equal("Frobby_CombatLab", req.Location);
        Assert.Equal(9, req.X);
        Assert.Equal(8, req.Y);
        Assert.Equal(12, req.Slot);
        Assert.Equal("right", req.Facing);
    }

    [Fact]
    public void Request_OptionalFieldsRemainNullWhenOmitted()
    {
        var json = "{\"id\":\"(O)287\",\"x\":9,\"y\":8}";

        var req = JsonSerializer.Deserialize<PlaceInventoryObjectRequest>(json, ProtocolJson.Options)!;

        Assert.Null(req.Location);
        Assert.Null(req.Slot);
        Assert.Null(req.Facing);
    }

    [Fact]
    public void Result_SerializesToSnakeCase()
    {
        var result = new PlaceInventoryObjectResult
        {
            Ok = true,
            Tick = 42,
            Id = "287",
            QualifiedId = "(O)287",
            Name = "Bomb",
            Location = "Frobby_CombatLab",
            Tile = new TilePoint { X = 9, Y = 8 },
            SourceSlot = 12,
            StackBefore = 2,
            StackAfter = 1,
            RuntimeType = "Object",
            Placed = true,
        };

        var json = JsonSerializer.Serialize(result, ProtocolJson.Options);

        Assert.Equal("{\"id\":\"287\",\"qualified_id\":\"(O)287\",\"name\":\"Bomb\",\"location\":\"Frobby_CombatLab\",\"tile\":{\"x\":9,\"y\":8},\"source_slot\":12,\"stack_before\":2,\"stack_after\":1,\"runtime_type\":\"Object\",\"placed\":true,\"ok\":true,\"tick\":42}", json);
    }
}
```

- [ ] **Step 2: Run protocol tests and verify red**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter "FullyQualifiedName~PlaceInventoryObjectSerializationTests" -v minimal
```

Expected: compile failure because `PlaceInventoryObjectRequest` and `PlaceInventoryObjectResult` do not exist.

- [ ] **Step 3: Add protocol DTOs**

Create `src/Protocol/Models/PlaceInventoryObjectRequest.cs`:

```csharp
namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape for <c>world.place_inventory_object</c>.</summary>
public sealed class PlaceInventoryObjectRequest
{
    /// <summary>Inventory object id to place. Qualified ids such as <c>(O)287</c> are preferred.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Optional current-location guard. Null means no guard.</summary>
    public string? Location { get; set; }

    /// <summary>Tile X coordinate.</summary>
    public int? X { get; set; }

    /// <summary>Tile Y coordinate.</summary>
    public int? Y { get; set; }

    /// <summary>Optional inventory slot override for ambiguous item ids.</summary>
    public int? Slot { get; set; }

    /// <summary>Optional player facing direction before placement.</summary>
    public string? Facing { get; set; }
}

/// <summary>Response shape for <c>world.place_inventory_object</c>.</summary>
public sealed class PlaceInventoryObjectResult : MutatorOk
{
    public string Id { get; set; } = string.Empty;
    public string QualifiedId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public TilePoint Tile { get; set; } = new();
    public int SourceSlot { get; set; }
    public int? StackBefore { get; set; }
    public int? StackAfter { get; set; }
    public string RuntimeType { get; set; } = string.Empty;
    public bool Placed { get; set; }
}
```

- [ ] **Step 4: Run protocol tests and verify green**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter "FullyQualifiedName~PlaceInventoryObjectSerializationTests" -v minimal
```

Expected: 3 tests pass.

- [ ] **Step 5: Commit protocol DTOs**

Run:

```bash
git add src/Protocol/Models/PlaceInventoryObjectRequest.cs tests/Protocol.Tests/PlaceInventoryObjectSerializationTests.cs
git commit -m "Add inventory object placement protocol models"
```

## Task 2: Harness Handler For Inventory Object Placement

**Files:**
- Create: `tests/Harness.Tests/WorldPlaceInventoryObjectHandlerTests.cs`
- Create: `src/Harness/Handlers/WorldPlaceInventoryObjectHandler.cs`
- Modify: `src/Harness/ModEntry.cs`

- [ ] **Step 1: Write failing harness validation and success tests**

Create `tests/Harness.Tests/WorldPlaceInventoryObjectHandlerTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class WorldPlaceInventoryObjectHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceInventoryObjectHandler.Handle(null, new FakeInventoryObjectWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_MissingId_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"x\":9,\"y\":8}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceInventoryObjectHandler.Handle(p, new FakeInventoryObjectWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("id", ex.Message);
    }

    [Fact]
    public void Handle_MissingX_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"id\":\"(O)287\",\"y\":8}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceInventoryObjectHandler.Handle(p, new FakeInventoryObjectWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("x", ex.Message);
    }

    [Fact]
    public void Handle_MissingY_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"id\":\"(O)287\",\"x\":9}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceInventoryObjectHandler.Handle(p, new FakeInventoryObjectWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("y", ex.Message);
    }

    [Theory]
    [InlineData("{\"id\":\"(O)287\",\"x\":-1,\"y\":8}", "x")]
    [InlineData("{\"id\":\"(O)287\",\"x\":9,\"y\":-1}", "y")]
    [InlineData("{\"id\":\"(O)287\",\"x\":9,\"y\":8,\"slot\":-1}", "slot")]
    public void Handle_InvalidNumericParams_ThrowsInvalidParams(string json, string field)
    {
        var p = JsonDocument.Parse(json).RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceInventoryObjectHandler.Handle(p, new FakeInventoryObjectWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains(field, ex.Message);
    }

    [Fact]
    public void Handle_UnknownFacing_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"id\":\"(O)287\",\"x\":9,\"y\":8,\"facing\":\"sideways\"}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceInventoryObjectHandler.Handle(p, new FakeInventoryObjectWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("sideways", ex.Message);
    }

    [Fact]
    public void Handle_NoLoadedWorld_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"id\":\"(O)287\",\"x\":9,\"y\":8}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceInventoryObjectHandler.Handle(p, new FakeInventoryObjectWorld { IsWorldReady = false }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_LocationGuardMismatch_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"id\":\"(O)287\",\"location\":\"Town\",\"x\":9,\"y\":8}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceInventoryObjectHandler.Handle(p, new FakeInventoryObjectWorld { CurrentLocation = "Farm" }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("Town", ex.Message);
        Assert.Contains("Farm", ex.Message);
    }

    [Fact]
    public void Handle_MissingInventoryItem_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"id\":\"(O)287\",\"x\":9,\"y\":8}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceInventoryObjectHandler.Handle(p, new FakeInventoryObjectWorld()));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("(O)287", ex.Message);
    }

    [Fact]
    public void Handle_NonObjectInventoryItem_ThrowsGameStateInvalid()
    {
        var world = new FakeInventoryObjectWorld();
        world.Items.Add(new FakeInventoryObjectItem(3, "(W)5", "5", "Sword", "MeleeWeapon", 1, false));
        var p = JsonDocument.Parse("{\"id\":\"(W)5\",\"x\":9,\"y\":8}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceInventoryObjectHandler.Handle(p, world));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("not an object", ex.Message);
    }

    [Fact]
    public void Handle_SlotOverrideSelectsMatchingSlot()
    {
        var world = new FakeInventoryObjectWorld();
        world.Items.Add(new FakeInventoryObjectItem(2, "(O)287", "287", "Bomb", "Object", 2, true));
        world.Items.Add(new FakeInventoryObjectItem(7, "(O)287", "287", "Bomb", "Object", 5, true));
        var p = JsonDocument.Parse("{\"id\":\"(O)287\",\"x\":9,\"y\":8,\"slot\":7}").RootElement;

        var json = WorldPlaceInventoryObjectHandler.Handle(p, world);
        var result = JsonSerializer.Deserialize<PlaceInventoryObjectResult>(json, ProtocolJson.Options)!;

        Assert.True(result.Ok);
        Assert.Equal(7, result.SourceSlot);
        Assert.Equal(5, result.StackBefore);
        Assert.Equal(4, result.StackAfter);
        Assert.Equal(7, world.PlacedSlot);
    }

    [Fact]
    public void Handle_RawItemIdCanMatchInventoryObject()
    {
        var world = new FakeInventoryObjectWorld();
        world.Items.Add(new FakeInventoryObjectItem(2, "(O)287", "287", "Bomb", "Object", 1, true));
        var p = JsonDocument.Parse("{\"id\":\"287\",\"x\":9,\"y\":8}").RootElement;

        var json = WorldPlaceInventoryObjectHandler.Handle(p, world);
        var result = JsonSerializer.Deserialize<PlaceInventoryObjectResult>(json, ProtocolJson.Options)!;

        Assert.Equal("287", result.Id);
        Assert.Equal("(O)287", result.QualifiedId);
        Assert.Equal(2, result.SourceSlot);
    }

    [Fact]
    public void Handle_NativePlacementFailure_ThrowsGameStateInvalid()
    {
        var world = new FakeInventoryObjectWorld { PlacementSucceeds = false };
        world.Items.Add(new FakeInventoryObjectItem(2, "(O)287", "287", "Bomb", "Object", 1, true));
        var p = JsonDocument.Parse("{\"id\":\"(O)287\",\"x\":9,\"y\":8}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldPlaceInventoryObjectHandler.Handle(p, world));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("could not place", ex.Message);
    }

    [Fact]
    public void Handle_PlacesObjectAndReturnsMetadata()
    {
        var world = new FakeInventoryObjectWorld { CurrentLocation = "Frobby_CombatLab" };
        world.Items.Add(new FakeInventoryObjectItem(12, "(O)287", "287", "Bomb", "Object", 2, true));
        var p = JsonDocument.Parse("{\"id\":\"(O)287\",\"location\":\"Frobby_CombatLab\",\"x\":9,\"y\":8,\"facing\":\"right\"}").RootElement;

        var json = WorldPlaceInventoryObjectHandler.Handle(p, world);
        var result = JsonSerializer.Deserialize<PlaceInventoryObjectResult>(json, ProtocolJson.Options)!;

        Assert.True(result.Ok);
        Assert.Equal(1234, result.Tick);
        Assert.Equal("287", result.Id);
        Assert.Equal("(O)287", result.QualifiedId);
        Assert.Equal("Bomb", result.Name);
        Assert.Equal("Frobby_CombatLab", result.Location);
        Assert.Equal(9, result.Tile.X);
        Assert.Equal(8, result.Tile.Y);
        Assert.Equal(12, result.SourceSlot);
        Assert.Equal(2, result.StackBefore);
        Assert.Equal(1, result.StackAfter);
        Assert.Equal("Object", result.RuntimeType);
        Assert.True(result.Placed);
        Assert.Equal("right", world.FacedDirection);
        Assert.Equal(12, world.PlacedSlot);
        Assert.Equal(9, world.PlacedX);
        Assert.Equal(8, world.PlacedY);
    }

    private sealed class FakeInventoryObjectWorld : IInventoryObjectPlacementWorld
    {
        public bool IsWorldReady { get; init; } = true;
        public int Tick => 1234;
        public string CurrentLocation { get; init; } = "Frobby_CombatLab";
        public List<IInventoryObjectItem> Items { get; } = new();
        public bool PlacementSucceeds { get; init; } = true;
        public string? FacedDirection { get; private set; }
        public int? PlacedSlot { get; private set; }
        public int? PlacedX { get; private set; }
        public int? PlacedY { get; private set; }

        IReadOnlyList<IInventoryObjectItem> IInventoryObjectPlacementWorld.Items => Items;

        public void FaceDirection(string direction) => FacedDirection = direction;

        public bool PlaceObject(IInventoryObjectItem item, int x, int y)
        {
            PlacedSlot = item.Slot;
            PlacedX = x;
            PlacedY = y;
            if (!PlacementSucceeds)
                return false;

            if (item is FakeInventoryObjectItem fake)
                fake.Stack = System.Math.Max(0, fake.Stack - 1);

            return true;
        }
    }

    private sealed class FakeInventoryObjectItem : IInventoryObjectItem
    {
        public FakeInventoryObjectItem(
            int slot,
            string qualifiedId,
            string itemId,
            string name,
            string runtimeType,
            int stack,
            bool isObject)
        {
            Slot = slot;
            QualifiedId = qualifiedId;
            ItemId = itemId;
            Name = name;
            RuntimeType = runtimeType;
            Stack = stack;
            IsObject = isObject;
        }

        public int Slot { get; }
        public string QualifiedId { get; }
        public string ItemId { get; }
        public string Name { get; }
        public string RuntimeType { get; }
        public int? Stack { get; set; }
        public bool IsObject { get; }
    }
}
```

- [ ] **Step 2: Run harness tests and verify red**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~WorldPlaceInventoryObjectHandlerTests" -v minimal
```

Expected: compile failure because `WorldPlaceInventoryObjectHandler`, `IInventoryObjectPlacementWorld`, and `IInventoryObjectItem` do not exist.

- [ ] **Step 3: Add harness handler and production adapter**

Create `src/Harness/Handlers/WorldPlaceInventoryObjectHandler.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using SObject = StardewValley.Object;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>world.place_inventory_object</c>. Places an existing inventory object through Stardew's native object placement path.</summary>
public static class WorldPlaceInventoryObjectHandler
{
    public const string Method = "world.place_inventory_object";

    private static readonly IInventoryObjectPlacementWorld ProductionWorld = new SdvInventoryObjectPlacementWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, IInventoryObjectPlacementWorld world)
    {
        var req = RpcParams.Required<PlaceInventoryObjectRequest>(paramsElement);
        ValidateRequest(req);

        if (!world.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "world.place_inventory_object requires a loaded world");

        if (!string.IsNullOrWhiteSpace(req.Location)
            && !string.Equals(req.Location.Trim(), world.CurrentLocation, StringComparison.Ordinal))
        {
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"world.place_inventory_object location guard expected {req.Location}, current location is {world.CurrentLocation}");
        }

        var item = SelectInventoryObject(world.Items, req);
        if (!item.IsObject)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"inventory item is not an object: {req.Id}");

        if (!string.IsNullOrWhiteSpace(req.Facing))
            world.FaceDirection(NormalizeDirection(req.Facing));

        var stackBefore = item.Stack;
        var x = req.X!.Value;
        var y = req.Y!.Value;
        if (!world.PlaceObject(item, x, y))
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"world.place_inventory_object could not place {req.Id} at tile {x},{y}");

        return ProtocolJson.ToElement(new PlaceInventoryObjectResult
        {
            Ok = true,
            Tick = world.Tick,
            Id = item.ItemId,
            QualifiedId = item.QualifiedId,
            Name = item.Name,
            Location = world.CurrentLocation,
            Tile = new TilePoint { X = x, Y = y },
            SourceSlot = item.Slot,
            StackBefore = stackBefore,
            StackAfter = item.Stack,
            RuntimeType = item.RuntimeType,
            Placed = true,
        });
    }

    private static void ValidateRequest(PlaceInventoryObjectRequest req)
    {
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
        if (req.Slot is < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.slot must be >= 0");
        if (!string.IsNullOrWhiteSpace(req.Facing) && !IsKnownDirection(req.Facing))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, $"unknown direction: {req.Facing}");
    }

    private static IInventoryObjectItem SelectInventoryObject(IReadOnlyList<IInventoryObjectItem> items, PlaceInventoryObjectRequest req)
    {
        var id = req.Id.Trim();
        var matches = items.Where(item =>
            string.Equals(item.QualifiedId, id, StringComparison.Ordinal)
            || string.Equals(item.ItemId, id, StringComparison.Ordinal));

        if (req.Slot is { } slot)
            matches = matches.Where(item => item.Slot == slot);

        return matches.FirstOrDefault()
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                req.Slot is { } slot
                    ? $"inventory item not found: {id} in slot {slot}"
                    : $"inventory item not found: {id}");
    }

    private static bool IsKnownDirection(string direction)
        => NormalizeDirection(direction) is "up" or "right" or "down" or "left";

    private static string NormalizeDirection(string direction)
        => direction.Trim().ToLowerInvariant();
}

internal interface IInventoryObjectPlacementWorld
{
    bool IsWorldReady { get; }
    int Tick { get; }
    string CurrentLocation { get; }
    IReadOnlyList<IInventoryObjectItem> Items { get; }
    void FaceDirection(string direction);
    bool PlaceObject(IInventoryObjectItem item, int x, int y);
}

internal interface IInventoryObjectItem
{
    int Slot { get; }
    string QualifiedId { get; }
    string ItemId { get; }
    string Name { get; }
    string RuntimeType { get; }
    int? Stack { get; }
    bool IsObject { get; }
}

internal sealed class SdvInventoryObjectPlacementWorld : IInventoryObjectPlacementWorld
{
    public bool IsWorldReady => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;
    public int Tick => Game1.ticks;
    public string CurrentLocation => CurrentLocationObject.NameOrUniqueName ?? CurrentLocationObject.Name ?? string.Empty;

    public IReadOnlyList<IInventoryObjectItem> Items
    {
        get
        {
            var items = new List<IInventoryObjectItem>();
            for (var slot = 0; slot < Game1.player.Items.Count; slot++)
            {
                if (Game1.player.Items[slot] is Item item)
                    items.Add(new SdvInventoryObjectItem(slot, item));
            }

            return items;
        }
    }

    public void FaceDirection(string direction)
    {
        Game1.player.faceDirection(DirectionToStardew(direction));
    }

    public bool PlaceObject(IInventoryObjectItem item, int x, int y)
    {
        if (item is not SdvInventoryObjectItem sdvItem)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "world.place_inventory_object can only place live inventory items");
        if (sdvItem.Item is not SObject obj)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"inventory item is not an object: {item.QualifiedId}");

        Game1.player.CurrentToolIndex = item.Slot;
        return obj.placementAction(CurrentLocationObject, x * 64, y * 64, Game1.player);
    }

    private static int DirectionToStardew(string direction)
        => direction switch
        {
            "up" => 0,
            "right" => 1,
            "down" => 2,
            "left" => 3,
            _ => throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, $"unknown direction: {direction}"),
        };

    private static GameLocation CurrentLocationObject
        => Game1.currentLocation
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"{WorldPlaceInventoryObjectHandler.Method} requires a current location");
}

internal sealed class SdvInventoryObjectItem : IInventoryObjectItem
{
    public SdvInventoryObjectItem(int slot, Item item)
    {
        Slot = slot;
        Item = item;
    }

    public int Slot { get; }
    public Item Item { get; }
    public string QualifiedId => Item.QualifiedItemId ?? string.Empty;
    public string ItemId => Item.ItemId ?? string.Empty;
    public string Name => Item.DisplayName ?? Item.Name ?? string.Empty;
    public string RuntimeType => Item.GetType().Name;
    public int? Stack => Item.Stack;
    public bool IsObject => Item is SObject;
}
```

- [ ] **Step 4: Register handler in `ModEntry`**

In `src/Harness/ModEntry.cs`, add registration after `world.place_inventory_furniture`:

```csharp
_rpc.Register(WorldPlaceInventoryObjectHandler.Method, p => WorldPlaceInventoryObjectHandler.Handle(p));
```

Update the startup text method list by changing:

```text
world.place_object, world.place_inventory_furniture, world.interact_tile
```

to:

```text
world.place_object, world.place_inventory_furniture, world.place_inventory_object, world.interact_tile
```

- [ ] **Step 5: Run harness tests and verify green**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~WorldPlaceInventoryObjectHandlerTests" -v minimal
```

Expected: all `WorldPlaceInventoryObjectHandlerTests` pass.

- [ ] **Step 6: Commit harness placement handler**

Run:

```bash
git add src/Harness/Handlers/WorldPlaceInventoryObjectHandler.cs src/Harness/ModEntry.cs tests/Harness.Tests/WorldPlaceInventoryObjectHandlerTests.cs
git commit -m "Add inventory object placement handler"
```

## Task 3: Object Lifecycle Projection And Wait Filters

**Files:**
- Modify: `src/Protocol/Models/LocationState.cs`
- Modify: `src/Harness/Handlers/LocationContentProjector.cs`
- Modify: `tests/Harness.Tests/LocationContentProjectorTests.cs`
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`
- Modify: `tests/Runner.Tests/ScenarioRunnerTests.cs`

- [ ] **Step 1: Write failing object lifecycle projection test**

In `tests/Harness.Tests/LocationContentProjectorTests.cs`, add this test after existing object projection tests:

```csharp
[Fact]
public void ProjectObject_IncludesOptionalMinutesUntilReadyWhenPresent()
{
    var obj = new FakeTimedLocationObject
    {
        Name = "Bomb",
        ItemId = "287",
        QualifiedItemId = "(O)287",
        minutesUntilReady = 2,
    };

    var summary = LocationContentProjector.ProjectObjectForTests(new Vector2(9, 8), obj);

    Assert.Equal(2, summary.MinutesUntilReady);
}
```

Add this fake near the other fake object classes:

```csharp
private sealed class FakeTimedLocationObject
{
    public string Name = string.Empty;
    public string ItemId = string.Empty;
    public string QualifiedItemId = string.Empty;
    public int minutesUntilReady;
}
```

- [ ] **Step 2: Write failing runner wait filter test**

In `tests/Runner.Tests/ScenarioRunnerTests.cs`, add this test near `WaitLocationContent_MatchesMonsterNumericComparisons`:

```csharp
[Fact]
public async Task WaitLocationContent_MatchesObjectMinutesUntilReadyComparisons()
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
                    "state.location" => JsonDocument.Parse("{\"name\":\"Frobby_CombatLab\",\"resource_clumps\":[],\"objects\":[{\"tile\":{\"x\":9,\"y\":8},\"name\":\"Bomb\",\"id\":\"287\",\"qualified_id\":\"(O)287\",\"runtime_type\":\"Object\",\"big_craftable\":false,\"minutes_until_ready\":2},{\"tile\":{\"x\":10,\"y\":8},\"name\":\"Bomb\",\"id\":\"287\",\"qualified_id\":\"(O)287\",\"runtime_type\":\"Object\",\"big_craftable\":false,\"minutes_until_ready\":0}],\"monsters\":[],\"debris\":[]}").RootElement,
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
        Name = "wait_location_content_object_minutes_until_ready",
        Steps = new()
        {
            new ScenarioStep
            {
                Action = "wait.location_content",
                Args = JsonDocument.Parse("{\"location\":\"Frobby_CombatLab\",\"collection\":\"objects\",\"qualified_id\":\"(O)287\",\"minutes_until_ready_gt\":0,\"min_count\":1,\"max_count\":1,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
            },
        },
    }, cts.Token);

    Assert.True(report.Passed, string.Join("\n", report.Failures));

    cts.Cancel();
    try { await serverTask; } catch (OperationCanceledException) { }
}
```

- [ ] **Step 3: Run focused tests and verify red**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~LocationContentProjectorTests.ProjectObject_IncludesOptionalMinutesUntilReadyWhenPresent" -v minimal
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~ScenarioRunnerTests.WaitLocationContent_MatchesObjectMinutesUntilReadyComparisons" -v minimal
```

Expected: harness compile failure because `ObjectSummary.MinutesUntilReady` does not exist; runner test fails because `minutes_until_ready_gt` is ignored.

- [ ] **Step 4: Add `MinutesUntilReady` to object summaries**

In `src/Protocol/Models/LocationState.cs`, add this property to `ObjectSummary` after `ReadyForHarvest`:

```csharp
/// <summary>Optional object lifecycle countdown, exposed when Stardew or a mod provides one.</summary>
public int? MinutesUntilReady { get; set; }
```

- [ ] **Step 5: Project `minutes_until_ready`**

In `src/Harness/Handlers/LocationContentProjector.cs`, add this assignment inside `ProjectLocationObject`:

```csharp
MinutesUntilReady = ReadInt(obj, "minutesUntilReady", "MinutesUntilReady"),
```

Place it with the other runtime object fields:

```csharp
BigCraftable = ReadBool(obj, "bigCraftable", "BigCraftable") ?? false,
ReadyForHarvest = ReadBool(obj, "readyForHarvest", "ReadyForHarvest"),
MinutesUntilReady = ReadInt(obj, "minutesUntilReady", "MinutesUntilReady"),
HeldObjectId = ReadString(heldObject, "ItemId", "itemId"),
```

- [ ] **Step 6: Add runner wait filters**

In `src/Runner/Scenarios/ScenarioRunner.cs`, update `LocationContentElementMatches` by adding this after the `damage` filter:

```csharp
&& NumberFilterMatches(element, "minutes_until_ready", args.MinutesUntilReady, args.MinutesUntilReadyLt, args.MinutesUntilReadyLte, args.MinutesUntilReadyGt, args.MinutesUntilReadyGte)
```

Update `FormatLocationContentFilters` by adding:

```csharp
AddNumberFilters(filters, "minutes_until_ready", args.MinutesUntilReady, args.MinutesUntilReadyLt, args.MinutesUntilReadyLte, args.MinutesUntilReadyGt, args.MinutesUntilReadyGte);
```

Add these properties to `WaitLocationContentStepArgs` after the damage filters:

```csharp
public int? MinutesUntilReady { get; set; }
public int? MinutesUntilReadyLt { get; set; }
public int? MinutesUntilReadyLte { get; set; }
public int? MinutesUntilReadyGt { get; set; }
public int? MinutesUntilReadyGte { get; set; }
```

- [ ] **Step 7: Run focused tests and verify green**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~LocationContentProjectorTests.ProjectObject_IncludesOptionalMinutesUntilReadyWhenPresent" -v minimal
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~ScenarioRunnerTests.WaitLocationContent_MatchesObjectMinutesUntilReadyComparisons" -v minimal
```

Expected: both tests pass.

- [ ] **Step 8: Commit object lifecycle observation**

Run:

```bash
git add src/Protocol/Models/LocationState.cs src/Harness/Handlers/LocationContentProjector.cs src/Runner/Scenarios/ScenarioRunner.cs tests/Harness.Tests/LocationContentProjectorTests.cs tests/Runner.Tests/ScenarioRunnerTests.cs
git commit -m "Expose timed object lifecycle filters"
```

## Task 4: Runner Pass-Through And DSL Surface

**Files:**
- Modify: `tests/Runner.Tests/ScenarioRunnerTests.cs`
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`
- Modify: `tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs`
- Modify: `src/Runner.Dsl/World.cs`

- [ ] **Step 1: Write failing runner report-label test**

In `tests/Runner.Tests/ScenarioRunnerTests.cs`, add this test near `WorldExplodeTile_PassesThroughAndReportsReadableStep`:

```csharp
[Fact]
public async Task WorldPlaceInventoryObject_PassesThroughAndReportsReadableStep()
{
    var socket = SocketPath();
    var tmp = Path.Combine(Path.GetTempPath(), $"place-inventory-object-report-{Guid.NewGuid():N}");
    var rd = RunDirectory.Create(tmp);
    var calls = new List<string>();
    var placeParams = default(JsonElement);
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

    var serverTask = Task.Run(async () =>
    {
        await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
        {
            session.RequestReceived += async req =>
            {
                calls.Add(req.Method);
                if (req.Method == "world.place_inventory_object")
                    placeParams = req.Params!.Value.Clone();

                JsonElement r = req.Method switch
                {
                    "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                    "world.place_inventory_object" => JsonDocument.Parse("{\"ok\":true,\"tick\":123,\"id\":\"287\",\"qualified_id\":\"(O)287\",\"name\":\"Bomb\",\"location\":\"Frobby_CombatLab\",\"tile\":{\"x\":9,\"y\":8},\"source_slot\":12,\"stack_before\":2,\"stack_after\":1,\"runtime_type\":\"Object\",\"placed\":true}").RootElement,
                    "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                    _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                };
                await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
            };
            await session.SendNotificationAsync("ready",
                JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
            await session.RunAsync(tok);
        }, cts.Token);
    }, cts.Token);

    try
    {
        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client, updateBaselines: false, reportDir: rd);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "place_inventory_object_report",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "world.place_inventory_object",
                    Args = JsonDocument.Parse("{\"id\":\"(O)287\",\"location\":\"Frobby_CombatLab\",\"x\":9,\"y\":8}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed, string.Join("\n", report.Failures));
        Assert.Contains("world.place_inventory_object", calls);
        Assert.Equal("(O)287", placeParams.GetProperty("id").GetString());
        Assert.Equal("Frobby_CombatLab", placeParams.GetProperty("location").GetString());
        Assert.Equal(9, placeParams.GetProperty("x").GetInt32());
        Assert.Equal(8, placeParams.GetProperty("y").GetInt32());
        Assert.Equal("Place inventory object (O)287 at Frobby_CombatLab (9,8)", report.Steps[0].Detail);
    }
    finally
    {
        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
        Directory.Delete(rd.Root, recursive: true);
    }
}
```

- [ ] **Step 2: Write failing DSL test**

In `tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs`, add this test after `ExplodeTile_InvokesWorldExplodeTileAndDeserializesResult`:

```csharp
[Fact]
public async Task PlaceInventoryObject_InvokesWorldPlaceInventoryObjectAndDeserializesResult()
{
    var inv = new CapturingInvoker
    {
        NextResponse = JsonDocument.Parse(
            "{\"ok\":true,\"tick\":42,\"id\":\"287\",\"qualified_id\":\"(O)287\",\"name\":\"Bomb\",\"location\":\"Frobby_CombatLab\",\"tile\":{\"x\":9,\"y\":8},\"source_slot\":12,\"stack_before\":2,\"stack_after\":1,\"runtime_type\":\"Object\",\"placed\":true}")
            .RootElement,
    };
    SdvTestSession.InitializeForTests(inv);
    PlaceInventoryObjectResult result;
    try
    {
        result = await World.PlaceInventoryObject("(O)287", 9, 8, location: "Frobby_CombatLab", slot: 12, facing: "right");
    }
    finally { SdvTestSession.ResetForTests(); }

    Assert.Equal("world.place_inventory_object", inv.Calls[0].Method);
    Assert.Contains("\"id\":\"(O)287\"", inv.Calls[0].ParamsJson);
    Assert.Contains("\"location\":\"Frobby_CombatLab\"", inv.Calls[0].ParamsJson);
    Assert.Contains("\"x\":9", inv.Calls[0].ParamsJson);
    Assert.Contains("\"y\":8", inv.Calls[0].ParamsJson);
    Assert.Contains("\"slot\":12", inv.Calls[0].ParamsJson);
    Assert.Contains("\"facing\":\"right\"", inv.Calls[0].ParamsJson);
    Assert.Equal("Bomb", result.Name);
    Assert.Equal("Frobby_CombatLab", result.Location);
    Assert.Equal(9, result.Tile.X);
    Assert.Equal(8, result.Tile.Y);
    Assert.True(result.Placed);
}
```

- [ ] **Step 3: Run runner/DSL tests and verify red**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~WorldPlaceInventoryObject_PassesThroughAndReportsReadableStep" -v minimal
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter "FullyQualifiedName~PlaceInventoryObject_InvokesWorldPlaceInventoryObjectAndDeserializesResult" -v minimal
```

Expected: runner test fails on the default report label; DSL test fails because `World.PlaceInventoryObject` does not exist.

- [ ] **Step 4: Add runner report label**

In `src/Runner/Scenarios/ScenarioRunner.cs`, add this branch to `DescribeStep` near other world actions:

```csharp
"world.place_inventory_object" => $"Place inventory object {GetStringArg(step.Args, "id") ?? "object"} at {GetStringArg(step.Args, "location") ?? "current"} ({GetIntArg(step.Args, "x") ?? 0},{GetIntArg(step.Args, "y") ?? 0})",
```

- [ ] **Step 5: Add DSL method**

In `src/Runner.Dsl/World.cs`, add this method before `UseTool`:

```csharp
/// <summary>Place an existing inventory object through Stardew's native object placement path.</summary>
public static async Task<PlaceInventoryObjectResult> PlaceInventoryObject(
    string id,
    int x,
    int y,
    string? location = null,
    int? slot = null,
    string? facing = null,
    CancellationToken ct = default)
{
    var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
    var p = JsonSerializer.SerializeToElement(new PlaceInventoryObjectRequest
    {
        Id = id,
        Location = location,
        X = x,
        Y = y,
        Slot = slot,
        Facing = facing,
    }, ProtocolJson.Options);
    var resp = await s.InvokeAsync("world.place_inventory_object", p, ct);
    return JsonSerializer.Deserialize<PlaceInventoryObjectResult>(resp, ProtocolJson.Options)
        ?? throw new SdvRpcException("world.place_inventory_object", Protocol.JsonRpcErrorCode.InternalError,
            "empty world.place_inventory_object response");
}
```

- [ ] **Step 6: Run runner/DSL tests and verify green**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~WorldPlaceInventoryObject_PassesThroughAndReportsReadableStep" -v minimal
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter "FullyQualifiedName~PlaceInventoryObject_InvokesWorldPlaceInventoryObjectAndDeserializesResult" -v minimal
```

Expected: both tests pass.

- [ ] **Step 7: Commit runner and DSL support**

Run:

```bash
git add src/Runner/Scenarios/ScenarioRunner.cs src/Runner.Dsl/World.cs tests/Runner.Tests/ScenarioRunnerTests.cs tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs
git commit -m "Expose inventory object placement in runner and DSL"
```

## Task 5: Frobby Documentation And Capability Status

**Files:**
- Modify: `docs/rpc-schema.md`
- Modify: `docs/dsl-quickstart.md`
- Modify: `docs/wiki/examples.md`
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Update `docs/rpc-schema.md` RPC method list and state docs**

In `docs/rpc-schema.md`, update the `state.location` object summary text to include:

```markdown
Object summaries include stable item metadata plus runtime details such as
`runtime_type`, `big_craftable`, `ready_for_harvest`, `minutes_until_ready`,
and held-object fields when Stardew exposes them.
```

In the runner convenience `wait.location_content` section, add `minutes_until_ready` to the exact filters list and add `minutes_until_ready_*` to the numeric-comparison sentence.

- [ ] **Step 2: Add `world.place_inventory_object` schema docs**

Add this section near `world.place_inventory_furniture`:

````markdown
### world.place_inventory_object

Places one existing inventory object into the player's current location through
Stardew's native object placement path. This is for player-like placement flows;
use `world.place_object` for direct setup and `world.explode_tile` for direct
explosion semantics.

Request:
```json
→ { "jsonrpc": "2.0", "id": 22, "method": "world.place_inventory_object",
     "params": { "id": "(O)287", "location": "Frobby_CombatLab", "x": 9, "y": 8, "slot": 12, "facing": "right" } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 22, "result": {
      "ok": true,
      "tick": 123456,
      "id": "287",
      "qualified_id": "(O)287",
      "name": "Bomb",
      "location": "Frobby_CombatLab",
      "tile": { "x": 9, "y": 8 },
      "source_slot": 12,
      "stack_before": 2,
      "stack_after": 1,
      "runtime_type": "Object",
      "placed": true
   } }
```

`location` is a current-location guard, not a remote placement target. Warp the
farmer to the target location first. `id` may match `QualifiedItemId` or
`ItemId`; scenarios should prefer qualified ids. `slot` is optional and should
only be used when the inventory contains multiple matching ids.

**Preconditions:** world loaded; matching inventory item exists; selected item
is a `StardewValley.Object`; current location matches `location` when supplied.
**Side effects:** invokes native object placement and lets Stardew mutate the
inventory stack. Placement may create a timed object such as a bomb.
**Implemented in:** `src/Harness/Handlers/WorldPlaceInventoryObjectHandler.cs`
**Tested in:** `tests/Protocol.Tests/PlaceInventoryObjectSerializationTests.cs`,
`tests/Harness.Tests/WorldPlaceInventoryObjectHandlerTests.cs`,
`tests/Runner.Tests/ScenarioRunnerTests.cs`, and
`tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs`.
````

- [ ] **Step 3: Update `docs/dsl-quickstart.md`**

Add this JSON example near the existing object and explosion examples:

````markdown
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
````

- [ ] **Step 4: Update wiki examples**

In `docs/wiki/examples.md`, add a short entry after the existing `world.explode_tile` example:

```markdown
Use `world.place_inventory_object` when a scenario needs to validate inventory
consumption and native object placement, such as a placed bomb's fuse. Follow it
with `wait.location_content` against `objects` and `minutes_until_ready_*`, then
wait for the object to disappear or for the resulting world-state effect.
```

- [ ] **Step 5: Update SVE TODO active entry**

In `SVE_FROBBY_CAPABILITY_TODO.md`, update Slice 22 with implementation-plan path:

```markdown
  - Implementation plan: `docs/superpowers/plans/2026-05-22-sve-slice-22-player-like-bomb-placement.md`.
```

- [ ] **Step 6: Commit Frobby docs**

Run:

```bash
git add docs/rpc-schema.md docs/dsl-quickstart.md docs/wiki/examples.md SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "Document inventory object placement flow"
```

## Task 6: SVE Scenario 30 Player-Like Bomb Proof

**Files:**
- Create: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/30-sve-combat-lab-bomb-mummy.test.json`
- Modify: `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`

- [ ] **Step 1: Create the first SVE scenario draft**

Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/30-sve-combat-lab-bomb-mummy.test.json`:

```json
{
  "name": "sve_combat_lab_inventory_bomb_mummy_cleanup",
  "fixture": "m0spike_436515781",
  "config": { "seed": 436515781 },
  "steps": [
    {
      "action": "time.set",
      "args": { "time": 600, "day": 1, "season": "spring", "year": 1 }
    },
    {
      "action": "world.set_weather",
      "args": { "type": "sun" }
    },
    {
      "action": "time.next_day",
      "args": { "settle_timeout_ms": 15000, "poll_ms": 100 }
    },
    {
      "action": "player.give_item",
      "args": {
        "id": "(W)FlashShifter.StardewValleyExpandedCP_Monster_Splitter",
        "count": 1
      }
    },
    {
      "action": "player.give_item",
      "args": { "id": "(O)287", "count": 1 }
    },
    {
      "action": "combat_lab.reset",
      "args": {
        "player_x": 8,
        "player_y": 8,
        "width": 20,
        "height": 14,
        "warp_player": false
      }
    },
    {
      "action": "player.warp",
      "args": { "location": "Custom_CrimsonBadlands", "x": 20, "y": 146 }
    },
    {
      "action": "wait.location",
      "args": {
        "location": "Custom_CrimsonBadlands",
        "x": 20,
        "y": 146,
        "timeout_ms": 10000,
        "poll_ms": 100
      }
    },
    {
      "action": "freeze.begin",
      "args": { "settle_timeout_ms": 10000, "poll_ms": 100 }
    },
    {
      "action": "wait.location_content",
      "args": {
        "location": "Custom_CrimsonBadlands",
        "collection": "monsters",
        "x": 20,
        "y": 144,
        "health": 2000,
        "max_health": 2000,
        "damage": 100,
        "sprite_texture": "Characters/Monsters/CorruptMummy",
        "min_count": 1,
        "timeout_ms": 15000,
        "poll_ms": 100
      }
    },
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
          "health": 2000,
          "max_health": 2000,
          "damage": 100,
          "sprite_texture": "Characters/Monsters/CorruptMummy"
        }
      }
    },
    {
      "action": "freeze.end",
      "args": {}
    },
    {
      "action": "player.warp",
      "args": { "location": "Frobby_CombatLab", "x": 8, "y": 8 }
    },
    {
      "action": "wait.location",
      "args": {
        "location": "Frobby_CombatLab",
        "x": 8,
        "y": 8,
        "timeout_ms": 10000,
        "poll_ms": 100
      }
    },
    {
      "action": "wait.location_content",
      "args": {
        "location": "Frobby_CombatLab",
        "collection": "monsters",
        "label": "corrupt-mummy",
        "sprite_texture": "Characters/Monsters/CorruptMummy",
        "min_count": 1,
        "max_count": 1,
        "timeout_ms": 5000,
        "poll_ms": 100
      }
    },
    {
      "action": "combat.attack",
      "args": {
        "qualified_item_id": "(W)FlashShifter.StardewValleyExpandedCP_Monster_Splitter",
        "repeat": 160,
        "delay_ticks": 15,
        "target": {
          "location": "Frobby_CombatLab",
          "label": "corrupt-mummy"
        }
      }
    },
    {
      "action": "wait.location_content",
      "args": {
        "location": "Frobby_CombatLab",
        "collection": "monsters",
        "label": "corrupt-mummy",
        "sprite_texture": "Characters/Monsters/CorruptMummy",
        "revive_timer_gt": 0,
        "min_count": 1,
        "max_count": 1,
        "timeout_ms": 10000,
        "poll_ms": 100
      }
    },
    {
      "action": "combat_lab.relocate_monster",
      "args": {
        "from_location": "Frobby_CombatLab",
        "label": "corrupt-mummy",
        "target_x": 9,
        "target_y": 8,
        "match": {
          "label": "corrupt-mummy"
        }
      }
    },
    {
      "action": "wait.location_content",
      "args": {
        "location": "Frobby_CombatLab",
        "collection": "monsters",
        "label": "corrupt-mummy",
        "x": 9,
        "y": 8,
        "sprite_texture": "Characters/Monsters/CorruptMummy",
        "revive_timer_gt": 0,
        "min_count": 1,
        "max_count": 1,
        "timeout_ms": 5000,
        "poll_ms": 100
      }
    },
    {
      "action": "world.place_inventory_object",
      "args": {
        "id": "(O)287",
        "location": "Frobby_CombatLab",
        "x": 9,
        "y": 9
      }
    },
    {
      "action": "player.warp",
      "args": { "location": "Frobby_CombatLab", "x": 15, "y": 8 }
    },
    {
      "action": "wait.location_content",
      "args": {
        "location": "Frobby_CombatLab",
        "collection": "objects",
        "qualified_id": "(O)287",
        "x": 9,
        "y": 9,
        "min_count": 1,
        "max_count": 1,
        "timeout_ms": 5000,
        "poll_ms": 100
      }
    },
    {
      "action": "wait.location_content",
      "args": {
        "location": "Frobby_CombatLab",
        "collection": "objects",
        "qualified_id": "(O)287",
        "x": 9,
        "y": 9,
        "min_count": 0,
        "max_count": 0,
        "timeout_ms": 15000,
        "poll_ms": 100
      }
    },
    {
      "action": "wait.location_content",
      "args": {
        "location": "Frobby_CombatLab",
        "collection": "monsters",
        "label": "corrupt-mummy",
        "min_count": 0,
        "max_count": 0,
        "timeout_ms": 15000,
        "poll_ms": 100
      }
    },
    {
      "action": "freeze.begin",
      "args": { "settle_timeout_ms": 10000, "poll_ms": 100 }
    },
    {
      "action": "screenshot.capture",
      "args": { "name": "final" }
    }
  ],
  "assertions": [
    {
      "type": "state",
      "expr": "state.player.location == 'Frobby_CombatLab'",
      "message": "Inventory bomb cleanup scenario should finish inside the Frobby combat dev room"
    }
  ]
}
```

- [ ] **Step 2: Run SVE scenario and verify initial live behavior**

Run from `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
./scripts/sdv-test --headless --no-build --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-22-inventory-bomb tests/sdv/30-sve-combat-lab-bomb-mummy.test.json
```

Expected: pass after Frobby implementation. The draft places the bomb adjacent to the downed mummy so native placement does not depend on placing an object on an occupied monster tile. If native placement rejects tile `(9,9)` in the live map, change only the SVE scenario to another adjacent blast-radius tile such as `(8,8)` or `(10,8)` and update the two object waits to the same tile. If the placed bomb exposes no `qualified_id`, inspect the generated report and adjust only the SVE scenario filters to match the generic state fields actually exposed by Frobby. Do not add SVE-specific logic to Frobby.

- [ ] **Step 3: Add `minutes_until_ready` wait if the report exposes it**

If `state.location.objects` for the placed bomb includes `minutes_until_ready`, update the first bomb-object wait to include:

```json
"minutes_until_ready_gt": 0
```

If the field is absent for vanilla bombs, keep the object presence/disappearance waits. The framework still exposes the generic field for timed objects that provide it.

- [ ] **Step 4: Update SVE docs**

In `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`, add this paragraph after the scenario 29 paragraph:

```markdown
Scenario `tests/sdv/30-sve-combat-lab-bomb-mummy.test.json` validates the
player-like placed-object path. It gives the farmer a vanilla bomb, places that
inventory object in `Frobby_CombatLab` through `world.place_inventory_object`,
waits for the placed object/fuse lifecycle to resolve, and then verifies that
the relocated corrupt mummy is removed without using direct `world.explode_tile`.
```

- [ ] **Step 5: Commit SVE scenario/docs**

Run:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded add tests/sdv/30-sve-combat-lab-bomb-mummy.test.json docs/FROBBY.md
git -C /home/fintan/stardewRepos/StardewValleyExpanded commit -m "Add Frobby inventory bomb cleanup scenario"
```

Do not merge the SVE branch.

## Task 7: Final Verification And Status Updates

**Files:**
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Run focused Frobby tests**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter "FullyQualifiedName~PlaceInventoryObjectSerializationTests" -v minimal
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~WorldPlaceInventoryObjectHandlerTests|FullyQualifiedName~LocationContentProjectorTests.ProjectObject_IncludesOptionalMinutesUntilReadyWhenPresent" -v minimal
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~WorldPlaceInventoryObject_PassesThroughAndReportsReadableStep|FullyQualifiedName~WaitLocationContent_MatchesObjectMinutesUntilReadyComparisons" -v minimal
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter "FullyQualifiedName~PlaceInventoryObject_InvokesWorldPlaceInventoryObjectAndDeserializesResult" -v minimal
```

Expected: all focused tests pass.

- [ ] **Step 2: Run broad Frobby regression tests**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj -v minimal
dotnet test tests/Harness.Tests/Harness.Tests.csproj -v minimal
dotnet test tests/Runner.Tests/Runner.Tests.csproj -v minimal
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj -v minimal
dotnet build -v minimal
```

Expected: all tests pass with existing skips; build exits with 0 warnings and 0 errors.

- [ ] **Step 3: Run live SVE scenario verification**

Run from `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
./scripts/sdv-test --headless --no-build --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-22-inventory-bomb tests/sdv/30-sve-combat-lab-bomb-mummy.test.json
./scripts/sdv-test --headless --no-build --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-22-regression-29 tests/sdv/29-sve-combat-lab-explode-mummy.test.json
./scripts/sdv-test --headless --no-build --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-22-regression-28 tests/sdv/28-sve-combat-lab-relocate-mod-monster.test.json
./scripts/sdv-test --headless --no-build --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-22-regression-27 tests/sdv/27-sve-combat-lab-vanilla-monster.test.json
```

Expected: all four live scenarios pass headlessly.

- [ ] **Step 4: Mark Slice 22 done**

In `SVE_FROBBY_CAPABILITY_TODO.md`, replace the Slice 22 block with:

```markdown
- [x] Done: Slice 22, player-like inventory object placement and bomb fuse flow.
  - SVE pressure: direct explosions prove cleanup semantics, but mod UI/testing also needs the player-like path where an inventory object is placed, ticks naturally, and produces game-state effects.
  - Frobby goal: add generic `world.place_inventory_object` plus timed object observation such as `minutes_until_ready`, without adding bomb-specific or SVE-specific framework code.
  - Design spec: `docs/superpowers/specs/2026-05-21-sve-slice-22-player-like-bomb-placement-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-22-sve-slice-22-player-like-bomb-placement.md`.
  - Done: protocol models, harness handler, runner label, DSL helper, object `minutes_until_ready` projection/waits, docs, and SVE scenario 30.
  - Verified: headless SVE scenario 30 proved an inventory bomb placement/fuse cleanup in `Frobby_CombatLab`; scenarios 27, 28, and 29 were rerun as adjacent Combat Lab regressions.
  - Follow-up candidate: input-level hotbar/click placement after semantic inventory-object placement is stable.
```

- [ ] **Step 5: Commit final Frobby status update**

Run:

```bash
git add SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "Mark inventory bomb placement slice complete"
```

- [ ] **Step 6: Final git status check**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
```

Expected: both worktrees clean. SVE remains on its feature branch and is not merged.
