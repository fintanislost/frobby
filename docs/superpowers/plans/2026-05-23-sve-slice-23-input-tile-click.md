# SVE Slice 23 Input Tile Click Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add neutral player inventory selection and gameplay tile-click RPCs, then prove them with an SVE Combat Lab scenario that places a vanilla bomb by selecting it and clicking a tile.

**Architecture:** Add protocol DTOs first, then two harness handlers: `player.select_item` for selecting an inventory slot and `input.click_tile` for converting tile coordinates into Stardew's native left-click/use-tool path. Keep runner and DSL wrappers thin pass-through layers, and keep SVE IDs only in the SVE scenario/docs.

**Tech Stack:** .NET 6 C#, System.Text.Json snake-case protocol options, SMAPI/StardewValley APIs, xUnit, Frobby JSON scenario runner, SVE repo-local `scripts/sdv-test --headless`.

---

## File Structure

Frobby files:

- Create `src/Protocol/Models/PlayerSelectItemRequest.cs`: request/result DTOs for `player.select_item`.
- Create `src/Protocol/Models/InputClickTileRequest.cs`: request/result DTOs for `input.click_tile`.
- Create `tests/Protocol.Tests/PlayerSelectItemSerializationTests.cs`: snake-case serialization coverage.
- Create `tests/Protocol.Tests/InputClickTileSerializationTests.cs`: snake-case serialization coverage.
- Create `src/Harness/Handlers/PlayerSelectItemHandler.cs`: neutral inventory selection handler plus production Stardew adapter.
- Create `tests/Harness.Tests/PlayerSelectItemHandlerTests.cs`: handler validation and selection behavior.
- Create `src/Harness/Handlers/InputClickTileHandler.cs`: neutral tile-click handler plus production Stardew adapter.
- Create `tests/Harness.Tests/InputClickTileHandlerTests.cs`: handler validation, coordinate conversion, and click dispatch behavior.
- Modify `src/Harness/ModEntry.cs`: register both new RPC methods and update the startup method list.
- Modify `src/Runner/Scenarios/ScenarioRunner.cs`: readable step descriptions and auto-capture expectation for `input.click_tile`.
- Modify `tests/Runner.Tests/ScenarioRunnerTests.cs`: runner pass-through, report label, and auto-capture coverage.
- Modify `src/Runner.Dsl/Player.cs`: add `SelectItem`.
- Modify `src/Runner.Dsl/Input.cs`: add `ClickTile`.
- Modify `tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs`: DSL coverage for `Player.SelectItem`.
- Modify or create `tests/Runner.Dsl.Tests/Facets/InputTests.cs`: DSL coverage for `Input.ClickTile`.
- Modify `docs/rpc-schema.md`: document new RPCs.
- Modify `docs/dsl-quickstart.md`: document semantic placement vs selected-item tile-click placement.
- Modify `docs/wiki/examples.md`: document object/fuse observation guidance and SVE scenario references.
- Modify `SVE_FROBBY_CAPABILITY_TODO.md`: mark Slice 23 active, then done after verification.

SVE files:

- Create `tests/sdv/31-sve-combat-lab-click-bomb-mummy.test.json`: live proof scenario.
- Modify `docs/FROBBY.md`: document scenario 31 and explain how it differs from scenario 30.

## Task 1: Protocol DTOs

**Files:**
- Create: `src/Protocol/Models/PlayerSelectItemRequest.cs`
- Create: `src/Protocol/Models/InputClickTileRequest.cs`
- Create: `tests/Protocol.Tests/PlayerSelectItemSerializationTests.cs`
- Create: `tests/Protocol.Tests/InputClickTileSerializationTests.cs`

- [ ] **Step 1: Write the failing protocol tests**

Add `tests/Protocol.Tests/PlayerSelectItemSerializationTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class PlayerSelectItemSerializationTests
{
    [Fact]
    public void Request_DeserializesIdAndPreferHotbar()
    {
        var req = JsonSerializer.Deserialize<PlayerSelectItemRequest>(
            "{\"id\":\"(O)287\",\"prefer_hotbar\":false}",
            ProtocolJson.Options)!;

        Assert.Equal("(O)287", req.Id);
        Assert.Null(req.Slot);
        Assert.False(req.PreferHotbar);
    }

    [Fact]
    public void Request_DefaultsPreferHotbarToTrue()
    {
        var req = JsonSerializer.Deserialize<PlayerSelectItemRequest>(
            "{\"slot\":13}",
            ProtocolJson.Options)!;

        Assert.Null(req.Id);
        Assert.Equal(13, req.Slot);
        Assert.True(req.PreferHotbar);
    }

    [Fact]
    public void Result_SerializesSelectedItemSummary()
    {
        var result = new PlayerSelectItemResult
        {
            Ok = true,
            Tick = 42,
            Slot = 1,
            Item = new PlayerItemSummary
            {
                Slot = 1,
                Id = "(O)287",
                ItemId = "287",
                QualifiedId = "(O)287",
                Name = "Bomb",
                Stack = 2,
                Category = -95,
                Quality = 0,
                RuntimeType = "Object",
            },
        };

        var json = JsonSerializer.Serialize(result, ProtocolJson.Options);

        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"tick\":42", json);
        Assert.Contains("\"slot\":1", json);
        Assert.Contains("\"qualified_id\":\"(O)287\"", json);
        Assert.Contains("\"runtime_type\":\"Object\"", json);
        Assert.DoesNotContain("PreferHotbar", json);
    }
}
```

Add `tests/Protocol.Tests/InputClickTileSerializationTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class InputClickTileSerializationTests
{
    [Fact]
    public void Request_DeserializesSnakeCaseFields()
    {
        var req = JsonSerializer.Deserialize<InputClickTileRequest>(
            "{\"location\":\"Frobby_CombatLab\",\"x\":9,\"y\":8,\"button\":\"left\",\"require_current_location\":false,\"screen_offset_x\":16,\"screen_offset_y\":48}",
            ProtocolJson.Options)!;

        Assert.Equal("Frobby_CombatLab", req.Location);
        Assert.Equal(9, req.X);
        Assert.Equal(8, req.Y);
        Assert.Equal("left", req.Button);
        Assert.False(req.RequireCurrentLocation);
        Assert.Equal(16, req.ScreenOffsetX);
        Assert.Equal(48, req.ScreenOffsetY);
    }

    [Fact]
    public void Request_DefaultsToLeftCurrentLocationAndTileCenter()
    {
        var req = JsonSerializer.Deserialize<InputClickTileRequest>(
            "{\"x\":9,\"y\":8}",
            ProtocolJson.Options)!;

        Assert.Null(req.Location);
        Assert.Equal("left", req.Button);
        Assert.True(req.RequireCurrentLocation);
        Assert.Equal(32, req.ScreenOffsetX);
        Assert.Equal(32, req.ScreenOffsetY);
    }

    [Fact]
    public void Result_SerializesDiagnosticsAsSnakeCase()
    {
        var result = new InputClickTileResult
        {
            Ok = true,
            Tick = 99,
            Location = "Frobby_CombatLab",
            Tile = new TilePoint { X = 9, Y = 8 },
            Screen = new PixelPoint { X = 608, Y = 544 },
            World = new PixelPoint { X = 608, Y = 544 },
            SelectedItem = new PlayerItemSummary
            {
                Slot = 1,
                Id = "(O)287",
                ItemId = "287",
                QualifiedId = "(O)287",
                Name = "Bomb",
                Stack = 1,
                RuntimeType = "Object",
            },
            Handled = true,
        };

        var json = JsonSerializer.Serialize(result, ProtocolJson.Options);

        Assert.Contains("\"location\":\"Frobby_CombatLab\"", json);
        Assert.Contains("\"tile\":{\"x\":9,\"y\":8}", json);
        Assert.Contains("\"screen\":{\"x\":608,\"y\":544}", json);
        Assert.Contains("\"world\":{\"x\":608,\"y\":544}", json);
        Assert.Contains("\"selected_item\":", json);
        Assert.Contains("\"handled\":true", json);
        Assert.DoesNotContain("SelectedItem", json);
    }
}
```

- [ ] **Step 2: Run protocol tests and verify they fail**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter "PlayerSelectItemSerializationTests|InputClickTileSerializationTests"
```

Expected: FAIL with missing `PlayerSelectItemRequest`, `PlayerSelectItemResult`, `InputClickTileRequest`, `InputClickTileResult`, and `PixelPoint`.

- [ ] **Step 3: Add protocol models**

Add `src/Protocol/Models/PlayerSelectItemRequest.cs`:

```csharp
namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape for <c>player.select_item</c>.</summary>
public sealed class PlayerSelectItemRequest
{
    /// <summary>Inventory item id to select. Qualified ids such as <c>(O)287</c> are preferred.</summary>
    public string? Id { get; set; }

    /// <summary>Optional zero-based inventory slot to select.</summary>
    public int? Slot { get; set; }

    /// <summary>When selecting by id, prefer visible hotbar slots 0..11.</summary>
    public bool PreferHotbar { get; set; } = true;
}

/// <summary>Response shape for <c>player.select_item</c>.</summary>
public sealed class PlayerSelectItemResult : MutatorOk
{
    public int Slot { get; set; }
    public PlayerItemSummary Item { get; set; } = new();
}
```

Add `src/Protocol/Models/InputClickTileRequest.cs`:

```csharp
namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape for <c>input.click_tile</c>.</summary>
public sealed class InputClickTileRequest
{
    /// <summary>Optional current-location guard. Null means use the current location.</summary>
    public string? Location { get; set; }

    /// <summary>Tile X coordinate.</summary>
    public int? X { get; set; }

    /// <summary>Tile Y coordinate.</summary>
    public int? Y { get; set; }

    /// <summary>Mouse button to send. Slice 23 only supports <c>left</c>.</summary>
    public string Button { get; set; } = "left";

    /// <summary>Reject when <see cref="Location"/> is supplied and the current location differs.</summary>
    public bool RequireCurrentLocation { get; set; } = true;

    /// <summary>Pixel offset within the tile. Defaults to the tile center.</summary>
    public int ScreenOffsetX { get; set; } = 32;

    /// <summary>Pixel offset within the tile. Defaults to the tile center.</summary>
    public int ScreenOffsetY { get; set; } = 32;
}

/// <summary>Response shape for <c>input.click_tile</c>.</summary>
public sealed class InputClickTileResult : MutatorOk
{
    public string Location { get; set; } = string.Empty;
    public TilePoint Tile { get; set; } = new();
    public PixelPoint Screen { get; set; } = new();
    public PixelPoint World { get; set; } = new();
    public PlayerItemSummary? SelectedItem { get; set; }
    public bool Handled { get; set; }
}

/// <summary>Pixel coordinate pair for click diagnostics.</summary>
public sealed class PixelPoint
{
    public int X { get; set; }
    public int Y { get; set; }
}
```

- [ ] **Step 4: Run protocol tests and verify they pass**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter "PlayerSelectItemSerializationTests|InputClickTileSerializationTests"
```

Expected: PASS.

- [ ] **Step 5: Commit protocol DTOs**

Run:

```bash
git add src/Protocol/Models/PlayerSelectItemRequest.cs src/Protocol/Models/InputClickTileRequest.cs tests/Protocol.Tests/PlayerSelectItemSerializationTests.cs tests/Protocol.Tests/InputClickTileSerializationTests.cs
git commit -m "Add player selection and tile click protocol models"
```

## Task 2: `player.select_item` Harness Handler

**Files:**
- Create: `src/Harness/Handlers/PlayerSelectItemHandler.cs`
- Create: `tests/Harness.Tests/PlayerSelectItemHandlerTests.cs`
- Modify: `src/Harness/ModEntry.cs`

- [ ] **Step 1: Write the failing handler tests**

Add `tests/Harness.Tests/PlayerSelectItemHandlerTests.cs`:

```csharp
using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class PlayerSelectItemHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() =>
            PlayerSelectItemHandler.Handle(null, new FakeSelectionWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("params required", ex.Message);
    }

    [Fact]
    public void Handle_IdAndSlotTogether_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"id\":\"(O)287\",\"slot\":1}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            PlayerSelectItemHandler.Handle(p, new FakeSelectionWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("exactly one", ex.Message);
    }

    [Fact]
    public void Handle_NotWorldReady_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"slot\":1}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            PlayerSelectItemHandler.Handle(p, new FakeSelectionWorld { IsWorldReady = false }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_SlotOutOfRange_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"slot\":99}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            PlayerSelectItemHandler.Handle(p, new FakeSelectionWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("out of range", ex.Message);
    }

    [Fact]
    public void Handle_EmptySlot_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"slot\":2}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            PlayerSelectItemHandler.Handle(p, new FakeSelectionWorld()));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("empty", ex.Message);
    }

    [Fact]
    public void Handle_SelectsBySlot()
    {
        var world = new FakeSelectionWorld();
        var p = JsonDocument.Parse("{\"slot\":13}").RootElement;

        var json = PlayerSelectItemHandler.Handle(p, world);
        var result = JsonSerializer.Deserialize<PlayerSelectItemResult>(json, ProtocolJson.Options)!;

        Assert.Equal(13, world.SelectedSlot);
        Assert.Equal(13, result.Slot);
        Assert.Equal("(O)287", result.Item.QualifiedId);
        Assert.Equal("Bomb", result.Item.Name);
        Assert.Equal(1234, result.Tick);
    }

    [Fact]
    public void Handle_SelectsByQualifiedIdAndPrefersHotbar()
    {
        var world = new FakeSelectionWorld();
        var p = JsonDocument.Parse("{\"id\":\"(O)287\"}").RootElement;

        var json = PlayerSelectItemHandler.Handle(p, world);
        var result = JsonSerializer.Deserialize<PlayerSelectItemResult>(json, ProtocolJson.Options)!;

        Assert.Equal(1, world.SelectedSlot);
        Assert.Equal(1, result.Slot);
        Assert.Equal(2, result.Item.Stack);
    }

    [Fact]
    public void Handle_SelectsByRawIdWhenQualifiedIdNotProvided()
    {
        var world = new FakeSelectionWorld();
        var p = JsonDocument.Parse("{\"id\":\"287\",\"prefer_hotbar\":false}").RootElement;

        var result = PlayerSelectItemHandler.Handle(p, world);

        Assert.Equal(13, JsonSerializer.Deserialize<PlayerSelectItemResult>(result, ProtocolJson.Options)!.Slot);
        Assert.Equal(13, world.SelectedSlot);
    }

    [Fact]
    public void Handle_MissingId_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"id\":\"(O)74\"}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            PlayerSelectItemHandler.Handle(p, new FakeSelectionWorld()));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("inventory item not found", ex.Message);
    }

    private sealed class FakeSelectionWorld : IPlayerInventorySelectionWorld
    {
        public bool IsWorldReady { get; set; } = true;
        public int Tick { get; set; } = 1234;
        public int InventoryCount => 36;
        public int? SelectedSlot { get; private set; }

        public IReadOnlyList<ISelectableInventoryItem> Items { get; } = new ISelectableInventoryItem[]
        {
            new SelectableInventoryItem(1, "(O)287", "287", "Bomb", 2, -95, 0, "Object"),
            new SelectableInventoryItem(13, "(O)287", "287", "Bomb", 1, -95, 0, "Object"),
        };

        public void SelectSlot(int slot) => SelectedSlot = slot;
    }
}
```

- [ ] **Step 2: Run handler tests and verify they fail**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter PlayerSelectItemHandlerTests
```

Expected: FAIL with missing `PlayerSelectItemHandler`, `IPlayerInventorySelectionWorld`, `ISelectableInventoryItem`, and `SelectableInventoryItem`.

- [ ] **Step 3: Add `player.select_item` handler**

Add `src/Harness/Handlers/PlayerSelectItemHandler.cs`:

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

/// <summary>Handler for <c>player.select_item</c>. Selects an existing farmer inventory slot.</summary>
public static class PlayerSelectItemHandler
{
    public const string Method = "player.select_item";

    private static readonly IPlayerInventorySelectionWorld ProductionWorld = new SdvPlayerInventorySelectionWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, IPlayerInventorySelectionWorld world)
    {
        var req = RpcParams.Required<PlayerSelectItemRequest>(paramsElement);
        ValidateRequest(req);

        if (!world.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "player.select_item requires a loaded world");

        var selected = req.Slot is { } slot
            ? SelectBySlot(world, slot)
            : SelectById(world, req.Id!.Trim(), req.PreferHotbar);

        world.SelectSlot(selected.Slot);

        return ProtocolJson.ToElement(new PlayerSelectItemResult
        {
            Ok = true,
            Tick = world.Tick,
            Slot = selected.Slot,
            Item = ToSummary(selected),
        });
    }

    private static void ValidateRequest(PlayerSelectItemRequest req)
    {
        var hasId = !string.IsNullOrWhiteSpace(req.Id);
        var hasSlot = req.Slot is not null;
        if (hasId == hasSlot)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "player.select_item requires exactly one of params.id or params.slot");
        if (req.Slot is < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.slot must be >= 0");
    }

    private static ISelectableInventoryItem SelectBySlot(IPlayerInventorySelectionWorld world, int slot)
    {
        if (slot >= world.InventoryCount)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                $"params.slot {slot} is out of range for inventory size {world.InventoryCount}");

        return world.Items.FirstOrDefault(i => i.Slot == slot)
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"inventory slot {slot} is empty");
    }

    private static ISelectableInventoryItem SelectById(
        IPlayerInventorySelectionWorld world,
        string id,
        bool preferHotbar)
    {
        var matches = world.Items
            .Where(i =>
                string.Equals(i.QualifiedId, id, StringComparison.Ordinal)
                || string.Equals(i.ItemId, id, StringComparison.Ordinal)
                || string.Equals(i.Id, id, StringComparison.Ordinal))
            .ToList();

        if (matches.Count == 0)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"inventory item not found: {id}");

        return preferHotbar
            ? matches.OrderBy(i => i.Slot is >= 0 and <= 11 ? 0 : 1).ThenBy(i => i.Slot).First()
            : matches.OrderBy(i => i.Slot).First();
    }

    internal static PlayerItemSummary ToSummary(ISelectableInventoryItem item)
        => new()
        {
            Slot = item.Slot,
            Id = item.Id,
            ItemId = item.ItemId,
            QualifiedId = item.QualifiedId,
            Name = item.Name,
            Stack = item.Stack,
            Category = item.Category,
            Quality = item.Quality,
            RuntimeType = item.RuntimeType,
        };
}

internal interface IPlayerInventorySelectionWorld
{
    bool IsWorldReady { get; }
    int Tick { get; }
    int InventoryCount { get; }
    IReadOnlyList<ISelectableInventoryItem> Items { get; }
    void SelectSlot(int slot);
}

internal interface ISelectableInventoryItem
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

internal sealed record SelectableInventoryItem(
    int Slot,
    string QualifiedId,
    string ItemId,
    string Name,
    int Stack,
    int? Category,
    int? Quality,
    string RuntimeType) : ISelectableInventoryItem
{
    public string Id => QualifiedId;
}

internal sealed class SdvPlayerInventorySelectionWorld : IPlayerInventorySelectionWorld
{
    public bool IsWorldReady => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;
    public int Tick => Game1.ticks;
    public int InventoryCount => Game1.player?.Items.Count ?? 0;

    public IReadOnlyList<ISelectableInventoryItem> Items
    {
        get
        {
            var items = new List<ISelectableInventoryItem>();
            if (Game1.player is null)
                return items;

            for (var slot = 0; slot < Game1.player.Items.Count; slot++)
            {
                if (Game1.player.Items[slot] is not Item item)
                    continue;

                var qualifiedId = item.QualifiedItemId ?? item.ItemId ?? string.Empty;
                var itemId = item.ItemId ?? SdvPlayerStateWorld.StripQualifiedPrefix(qualifiedId);
                items.Add(new SelectableInventoryItem(
                    slot,
                    qualifiedId,
                    itemId,
                    item.DisplayName ?? item.Name ?? string.Empty,
                    item.Stack,
                    item.Category,
                    item.Quality,
                    item.GetType().Name));
            }

            return items;
        }
    }

    public void SelectSlot(int slot)
    {
        Game1.player.CurrentToolIndex = slot;
    }
}
```

- [ ] **Step 4: Register `player.select_item`**

Modify `src/Harness/ModEntry.cs`:

```csharp
_rpc.Register(PlayerSelectItemHandler.Method, p => PlayerSelectItemHandler.Handle(p));
```

Place it with the other `player.*` registrations, immediately after `PlayerGiveItemHandler`.

In the startup log string, add `player.select_item` after `player.give_item`.

- [ ] **Step 5: Run handler tests and verify they pass**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter PlayerSelectItemHandlerTests
```

Expected: PASS.

- [ ] **Step 6: Commit selection handler**

Run:

```bash
git add src/Harness/Handlers/PlayerSelectItemHandler.cs tests/Harness.Tests/PlayerSelectItemHandlerTests.cs src/Harness/ModEntry.cs
git commit -m "Add player inventory selection handler"
```

## Task 3: `input.click_tile` Harness Handler

**Files:**
- Create: `src/Harness/Handlers/InputClickTileHandler.cs`
- Create: `tests/Harness.Tests/InputClickTileHandlerTests.cs`
- Modify: `src/Harness/ModEntry.cs`

- [ ] **Step 1: Write the failing handler tests**

Add `tests/Harness.Tests/InputClickTileHandlerTests.cs`:

```csharp
using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class InputClickTileHandlerTests
{
    [Fact]
    public void Handle_MissingX_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"y\":8}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            InputClickTileHandler.Handle(p, new FakeTileClickWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("params.x required", ex.Message);
    }

    [Fact]
    public void Handle_RejectsRightClickForSlice23()
    {
        var p = JsonDocument.Parse("{\"x\":9,\"y\":8,\"button\":\"right\"}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            InputClickTileHandler.Handle(p, new FakeTileClickWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("button must be left", ex.Message);
    }

    [Fact]
    public void Handle_NotWorldReady_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"x\":9,\"y\":8}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            InputClickTileHandler.Handle(p, new FakeTileClickWorld { IsWorldReady = false }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_ActiveMenu_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"x\":9,\"y\":8}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            InputClickTileHandler.Handle(p, new FakeTileClickWorld { HasActiveMenu = true }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("active menu", ex.Message);
    }

    [Fact]
    public void Handle_LocationMismatch_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"location\":\"Farm\",\"x\":9,\"y\":8}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            InputClickTileHandler.Handle(p, new FakeTileClickWorld { CurrentLocationName = "Frobby_CombatLab" }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("location guard expected Farm", ex.Message);
    }

    [Fact]
    public void Handle_OutOfMapTile_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"x\":40,\"y\":8}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            InputClickTileHandler.Handle(p, new FakeTileClickWorld { MapWidth = 20, MapHeight = 14 }));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("outside map bounds", ex.Message);
    }

    [Fact]
    public void Handle_LeftClickConvertsTileToWorldAndScreenCoordinates()
    {
        var world = new FakeTileClickWorld
        {
            CurrentLocationName = "Frobby_CombatLab",
            ViewportX = 64,
            ViewportY = 128,
        };
        var p = JsonDocument.Parse("{\"location\":\"Frobby_CombatLab\",\"x\":9,\"y\":8,\"screen_offset_x\":16,\"screen_offset_y\":48}").RootElement;

        var json = InputClickTileHandler.Handle(p, world);
        var result = JsonSerializer.Deserialize<InputClickTileResult>(json, ProtocolJson.Options)!;

        Assert.Equal(592, world.ClickedWorldX);
        Assert.Equal(560, world.ClickedWorldY);
        Assert.Equal(528, world.ClickedScreenX);
        Assert.Equal(432, world.ClickedScreenY);
        Assert.True(world.ClickInvoked);
        Assert.True(result.Handled);
        Assert.Equal("Frobby_CombatLab", result.Location);
        Assert.Equal(9, result.Tile.X);
        Assert.Equal(8, result.Tile.Y);
        Assert.Equal(528, result.Screen.X);
        Assert.Equal(432, result.Screen.Y);
        Assert.Equal("(O)287", result.SelectedItem!.QualifiedId);
    }

    private sealed class FakeTileClickWorld : IInputTileClickWorld
    {
        public bool IsWorldReady { get; set; } = true;
        public bool HasActiveMenu { get; set; }
        public bool IsWarping { get; set; }
        public bool IsFading { get; set; }
        public bool EventUp { get; set; }
        public int Tick { get; set; } = 55;
        public string CurrentLocationName { get; set; } = "Frobby_CombatLab";
        public int? MapWidth { get; set; } = 20;
        public int? MapHeight { get; set; } = 14;
        public int ViewportX { get; set; }
        public int ViewportY { get; set; }
        public bool ClickInvoked { get; private set; }
        public int? ClickedWorldX { get; private set; }
        public int? ClickedWorldY { get; private set; }
        public int? ClickedScreenX { get; private set; }
        public int? ClickedScreenY { get; private set; }

        public ISelectableInventoryItem? SelectedItem { get; set; }
            = new SelectableInventoryItem(1, "(O)287", "287", "Bomb", 1, -95, 0, "Object");

        public bool ClickLeftTile(int worldX, int worldY, int screenX, int screenY)
        {
            ClickInvoked = true;
            ClickedWorldX = worldX;
            ClickedWorldY = worldY;
            ClickedScreenX = screenX;
            ClickedScreenY = screenY;
            return true;
        }
    }
}
```

- [ ] **Step 2: Run handler tests and verify they fail**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter InputClickTileHandlerTests
```

Expected: FAIL with missing `InputClickTileHandler` and `IInputTileClickWorld`.

- [ ] **Step 3: Add `input.click_tile` handler**

Add `src/Harness/Handlers/InputClickTileHandler.cs`:

```csharp
using System;
using System.Text.Json;
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Determinism;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>input.click_tile</c>. Sends a gameplay left click to a tile.</summary>
public static class InputClickTileHandler
{
    public const string Method = "input.click_tile";

    private const int TileSize = 64;
    private static readonly IInputTileClickWorld ProductionWorld = new SdvInputTileClickWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, IInputTileClickWorld world)
    {
        var req = RpcParams.Required<InputClickTileRequest>(paramsElement);
        ValidateRequest(req);

        if (!world.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "input.click_tile requires a loaded world");
        if (world.HasActiveMenu)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "input.click_tile requires no active menu");
        if (world.IsWarping || world.IsFading || world.EventUp)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "input.click_tile requires settled gameplay state");

        if (!string.IsNullOrWhiteSpace(req.Location)
            && req.RequireCurrentLocation
            && !string.Equals(req.Location.Trim(), world.CurrentLocationName, StringComparison.Ordinal))
        {
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"input.click_tile location guard expected {req.Location}, current location is {world.CurrentLocationName}");
        }

        if (world.MapWidth is { } width && req.X!.Value >= width
            || world.MapHeight is { } height && req.Y!.Value >= height)
        {
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                $"input.click_tile target {req.X},{req.Y} is outside map bounds {world.MapWidth}x{world.MapHeight}");
        }

        var worldX = req.X!.Value * TileSize + req.ScreenOffsetX;
        var worldY = req.Y!.Value * TileSize + req.ScreenOffsetY;
        var screenX = worldX - world.ViewportX;
        var screenY = worldY - world.ViewportY;

        var handled = world.ClickLeftTile(worldX, worldY, screenX, screenY);
        var selected = world.SelectedItem is null
            ? null
            : PlayerSelectItemHandler.ToSummary(world.SelectedItem);

        return ProtocolJson.ToElement(new InputClickTileResult
        {
            Ok = true,
            Tick = world.Tick,
            Location = world.CurrentLocationName,
            Tile = new TilePoint { X = req.X.Value, Y = req.Y.Value },
            Screen = new PixelPoint { X = screenX, Y = screenY },
            World = new PixelPoint { X = worldX, Y = worldY },
            SelectedItem = selected,
            Handled = handled,
        });
    }

    private static void ValidateRequest(InputClickTileRequest req)
    {
        if (req.X is null)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.x required");
        if (req.Y is null)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.y required");
        if (req.X < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.x must be >= 0");
        if (req.Y < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.y must be >= 0");
        if (req.ScreenOffsetX < 0 || req.ScreenOffsetX >= TileSize)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.screen_offset_x must be in tile range 0..63");
        if (req.ScreenOffsetY < 0 || req.ScreenOffsetY >= TileSize)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.screen_offset_y must be in tile range 0..63");

        var button = string.IsNullOrWhiteSpace(req.Button) ? "left" : req.Button.Trim().ToLowerInvariant();
        if (button != "left")
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "params.button must be left for input.click_tile");
    }
}

internal interface IInputTileClickWorld
{
    bool IsWorldReady { get; }
    bool HasActiveMenu { get; }
    bool IsWarping { get; }
    bool IsFading { get; }
    bool EventUp { get; }
    int Tick { get; }
    string CurrentLocationName { get; }
    int? MapWidth { get; }
    int? MapHeight { get; }
    int ViewportX { get; }
    int ViewportY { get; }
    ISelectableInventoryItem? SelectedItem { get; }
    bool ClickLeftTile(int worldX, int worldY, int screenX, int screenY);
}

internal sealed class SdvInputTileClickWorld : IInputTileClickWorld
{
    public bool IsWorldReady => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;
    public bool HasActiveMenu => Game1.activeClickableMenu is not null;
    public bool IsWarping => Game1.isWarping;
    public bool IsFading => Game1.fadeToBlack;
    public bool EventUp => Game1.eventUp;
    public int Tick => Game1.ticks;
    public string CurrentLocationName => CurrentLocation.NameOrUniqueName ?? CurrentLocation.Name ?? string.Empty;
    public int? MapWidth => CurrentLocation.Map?.DisplayWidth / 64;
    public int? MapHeight => CurrentLocation.Map?.DisplayHeight / 64;
    public int ViewportX => Game1.viewport.X;
    public int ViewportY => Game1.viewport.Y;

    public ISelectableInventoryItem? SelectedItem
    {
        get
        {
            var slot = Game1.player.CurrentToolIndex;
            if (slot < 0 || slot >= Game1.player.Items.Count || Game1.player.Items[slot] is not Item item)
                return null;

            var qualifiedId = item.QualifiedItemId ?? item.ItemId ?? string.Empty;
            var itemId = item.ItemId ?? SdvPlayerStateWorld.StripQualifiedPrefix(qualifiedId);
            return new SelectableInventoryItem(
                slot,
                qualifiedId,
                itemId,
                item.DisplayName ?? item.Name ?? string.Empty,
                item.Stack,
                item.Category,
                item.Quality,
                item.GetType().Name);
        }
    }

    public bool ClickLeftTile(int worldX, int worldY, int screenX, int screenY)
    {
        ControlledCursor.Set(screenX, screenY);
        Game1.currentCursorTile = new Vector2(worldX / 64f, worldY / 64f);
        Game1.lastCursorTile = Game1.currentCursorTile;
        Game1.lastCursorMotionWasMouse = true;

        // This is Stardew's normal gameplay use path. It reads the current mouse
        // position through Game1.getMouseX/Y, which Frobby controls through
        // CursorPatches + ControlledCursor without moving the user's OS cursor.
        return Game1.pressUseToolButton();
    }

    private static GameLocation CurrentLocation
        => Game1.currentLocation
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"{InputClickTileHandler.Method} requires a current location");
}
```

- [ ] **Step 4: Register `input.click_tile`**

Modify `src/Harness/ModEntry.cs`:

```csharp
_rpc.Register(InputClickTileHandler.Method, p => InputClickTileHandler.Handle(p));
```

Place it immediately after `InputClickHandler`.

In the startup log string, add `input.click_tile` after `input.click`.

- [ ] **Step 5: Run handler tests and verify they pass**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter InputClickTileHandlerTests
```

Expected: PASS.

- [ ] **Step 6: Build Frobby to catch Stardew API signature issues**

Run:

```bash
dotnet build -v minimal
```

Expected: build succeeds. If it fails only because `Game1.pressUseToolButton()` signature differs, use reflection through `AccessTools.Method(typeof(Game1), "pressUseToolButton")` and keep the handler response `Handled = true` when the reflected call returns `null`.

- [ ] **Step 7: Commit tile-click handler**

Run:

```bash
git add src/Harness/Handlers/InputClickTileHandler.cs tests/Harness.Tests/InputClickTileHandlerTests.cs src/Harness/ModEntry.cs
git commit -m "Add gameplay tile click handler"
```

## Task 4: Runner Step Labels And Auto Screenshots

**Files:**
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`
- Modify: `tests/Runner.Tests/ScenarioRunnerTests.cs`

- [ ] **Step 1: Write failing runner coverage**

Add a test near `WorldPlaceInventoryObject_PassesThroughAndReportsReadableStep` in `tests/Runner.Tests/ScenarioRunnerTests.cs`:

```csharp
[Fact]
public async Task InputClickTile_PassesThroughAndReportsReadableStep()
{
    var socket = SocketPath();
    var tmp = Path.Combine(Path.GetTempPath(), $"click-tile-report-{Guid.NewGuid():N}");
    var rd = RunDirectory.Create(tmp);
    var calls = new List<string>();
    var clickParams = default(JsonElement);
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

    var serverTask = Task.Run(async () =>
    {
        await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
        {
            session.RequestReceived += async req =>
            {
                calls.Add(req.Method);
                if (req.Method == "input.click_tile")
                    clickParams = req.Params!.Value.Clone();

                JsonElement r = req.Method switch
                {
                    "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                    "input.click_tile" => JsonDocument.Parse("{\"ok\":true,\"tick\":123,\"location\":\"Frobby_CombatLab\",\"tile\":{\"x\":9,\"y\":9},\"screen\":{\"x\":576,\"y\":576},\"world\":{\"x\":608,\"y\":608},\"selected_item\":{\"slot\":1,\"id\":\"(O)287\",\"item_id\":\"287\",\"qualified_id\":\"(O)287\",\"name\":\"Bomb\",\"stack\":1,\"runtime_type\":\"Object\"},\"handled\":true}").RootElement,
                    "bitmap.capture_next_frame" => JsonDocument.Parse("{\"path\":\"/tmp/click-tile.png\",\"width\":1280,\"height\":720}").RootElement,
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
            Name = "click_tile_report",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "input.click_tile",
                    Args = JsonDocument.Parse("{\"location\":\"Frobby_CombatLab\",\"x\":9,\"y\":9}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed, string.Join("\n", report.Failures));
        Assert.Contains("input.click_tile", calls);
        Assert.Equal("Frobby_CombatLab", clickParams.GetProperty("location").GetString());
        Assert.Equal(9, clickParams.GetProperty("x").GetInt32());
        Assert.Equal(9, clickParams.GetProperty("y").GetInt32());
        Assert.Equal("Click left tile Frobby_CombatLab (9,9)", report.Steps[0].Detail);
        Assert.Contains("bitmap.capture_next_frame", calls);
    }
    finally
    {
        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
        Directory.Delete(rd.Root, recursive: true);
    }
}
```

Add `input.click_tile` to the `ShouldAutoCaptureStep_SkipsTimingAndInstrumentationSteps` theory:

```csharp
[InlineData("input.click_tile", true)]
```

- [ ] **Step 2: Run runner tests and verify they fail**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "InputClickTile_PassesThroughAndReportsReadableStep|ShouldAutoCaptureStep_SkipsTimingAndInstrumentationSteps"
```

Expected: FAIL because `DescribeStep` falls back to raw JSON for `input.click_tile`.

- [ ] **Step 3: Add runner label**

Modify `DescribeStep` in `src/Runner/Scenarios/ScenarioRunner.cs`:

```csharp
"input.click_tile" => $"Click {GetStringArg(step.Args, "button") ?? "left"} tile {GetStringArg(step.Args, "location") ?? "current"} ({GetIntArg(step.Args, "x") ?? 0},{GetIntArg(step.Args, "y") ?? 0})",
```

Place it immediately after the existing `"input.click"` label.

No `ShouldAutoCaptureStep` implementation change is needed if the default branch remains `true`; the new theory row locks in that behavior.

- [ ] **Step 4: Run runner tests and verify they pass**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "InputClickTile_PassesThroughAndReportsReadableStep|ShouldAutoCaptureStep_SkipsTimingAndInstrumentationSteps"
```

Expected: PASS.

- [ ] **Step 5: Commit runner support**

Run:

```bash
git add src/Runner/Scenarios/ScenarioRunner.cs tests/Runner.Tests/ScenarioRunnerTests.cs
git commit -m "Report gameplay tile click steps"
```

## Task 5: DSL Wrappers

**Files:**
- Modify: `src/Runner.Dsl/Player.cs`
- Modify: `src/Runner.Dsl/Input.cs`
- Modify: `tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs`
- Create or modify: `tests/Runner.Dsl.Tests/Facets/InputTests.cs`

- [ ] **Step 1: Write failing DSL tests**

Add to `tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs`:

```csharp
[Fact]
public async Task SelectItem_InvokesPlayerSelectItemById()
{
    SdvTestSession.ResetForTests();
    var inv = new CapturingInvoker
    {
        NextResponse = JsonDocument.Parse(
            "{\"ok\":true,\"tick\":42,\"slot\":1,\"item\":{\"slot\":1,\"id\":\"(O)287\",\"item_id\":\"287\",\"qualified_id\":\"(O)287\",\"name\":\"Bomb\",\"stack\":1,\"runtime_type\":\"Object\"}}")
            .RootElement,
    };
    SdvTestSession.InitializeForTests(inv);
    PlayerSelectItemResult result;
    try { result = await Player.SelectItem(id: "(O)287"); }
    finally { SdvTestSession.ResetForTests(); }

    Assert.Equal("player.select_item", inv.Calls[0].Method);
    Assert.Contains("\"id\":\"(O)287\"", inv.Calls[0].ParamsJson);
    Assert.Equal(1, result.Slot);
    Assert.Equal("(O)287", result.Item.QualifiedId);
}

[Fact]
public async Task SelectItem_InvokesPlayerSelectItemBySlot()
{
    SdvTestSession.ResetForTests();
    var inv = new CapturingInvoker
    {
        NextResponse = JsonDocument.Parse(
            "{\"ok\":true,\"tick\":42,\"slot\":13,\"item\":{\"slot\":13,\"id\":\"(O)287\",\"item_id\":\"287\",\"qualified_id\":\"(O)287\",\"name\":\"Bomb\",\"stack\":1,\"runtime_type\":\"Object\"}}")
            .RootElement,
    };
    SdvTestSession.InitializeForTests(inv);
    try { await Player.SelectItem(slot: 13, preferHotbar: false); }
    finally { SdvTestSession.ResetForTests(); }

    Assert.Equal("player.select_item", inv.Calls[0].Method);
    Assert.Contains("\"slot\":13", inv.Calls[0].ParamsJson);
    Assert.Contains("\"prefer_hotbar\":false", inv.Calls[0].ParamsJson);
}
```

If `tests/Runner.Dsl.Tests/Facets/InputTests.cs` does not exist, create it:

```csharp
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Models;
using SdvTestFramework.Runner.Dsl;
using Xunit;

namespace SdvTestFramework.Runner.Dsl.Tests.Facets;

public class InputTests
{
    private sealed class CapturingInvoker : ISdvTestInvoker
    {
        public List<(string Method, string ParamsJson)> Calls { get; } = new();
        public JsonElement NextResponse { get; set; } = JsonDocument.Parse("{\"ok\":true,\"tick\":42}").RootElement;

        public Task<JsonElement> InvokeAsync(string method, JsonElement? p, CancellationToken ct)
        {
            Calls.Add((method, p?.GetRawText() ?? ""));
            return Task.FromResult(NextResponse);
        }
    }

    [Fact]
    public async Task ClickTile_InvokesInputClickTileAndDeserializesResult()
    {
        SdvTestSession.ResetForTests();
        var inv = new CapturingInvoker
        {
            NextResponse = JsonDocument.Parse(
                "{\"ok\":true,\"tick\":42,\"location\":\"Frobby_CombatLab\",\"tile\":{\"x\":9,\"y\":9},\"screen\":{\"x\":576,\"y\":576},\"world\":{\"x\":608,\"y\":608},\"handled\":true}")
                .RootElement,
        };
        SdvTestSession.InitializeForTests(inv);
        InputClickTileResult result;
        try
        {
            result = await Input.ClickTile(9, 9, location: "Frobby_CombatLab");
        }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Single(inv.Calls);
        Assert.Equal("input.click_tile", inv.Calls[0].Method);
        Assert.Contains("\"location\":\"Frobby_CombatLab\"", inv.Calls[0].ParamsJson);
        Assert.Contains("\"x\":9", inv.Calls[0].ParamsJson);
        Assert.Contains("\"y\":9", inv.Calls[0].ParamsJson);
        Assert.True(result.Handled);
        Assert.Equal(608, result.World.X);
    }
}
```

- [ ] **Step 2: Run DSL tests and verify they fail**

Run:

```bash
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter "SelectItem|ClickTile"
```

Expected: FAIL with missing `Player.SelectItem` and `Input.ClickTile`.

- [ ] **Step 3: Add DSL methods**

Modify `src/Runner.Dsl/Player.cs`:

```csharp
/// <summary>Select an existing farmer inventory item by id or slot.</summary>
public static async Task<PlayerSelectItemResult> SelectItem(
    string? id = null,
    int? slot = null,
    bool preferHotbar = true,
    CancellationToken ct = default)
{
    var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
    var p = JsonSerializer.SerializeToElement(new PlayerSelectItemRequest
    {
        Id = id,
        Slot = slot,
        PreferHotbar = preferHotbar,
    }, ProtocolJson.Options);
    var resp = await s.InvokeAsync("player.select_item", p, ct);
    return JsonSerializer.Deserialize<PlayerSelectItemResult>(resp, ProtocolJson.Options)
        ?? throw new SdvRpcException("player.select_item", Protocol.JsonRpcErrorCode.InternalError,
            "empty player.select_item response");
}
```

Modify `src/Runner.Dsl/Input.cs`:

```csharp
/// <summary>Click a gameplay tile through Stardew's native left-click path.</summary>
public static async Task<InputClickTileResult> ClickTile(
    int x,
    int y,
    string? location = null,
    bool requireCurrentLocation = true,
    int screenOffsetX = 32,
    int screenOffsetY = 32,
    CancellationToken ct = default)
{
    var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
    var p = JsonSerializer.SerializeToElement(new InputClickTileRequest
    {
        Location = location,
        X = x,
        Y = y,
        RequireCurrentLocation = requireCurrentLocation,
        ScreenOffsetX = screenOffsetX,
        ScreenOffsetY = screenOffsetY,
    }, ProtocolJson.Options);
    var resp = await s.InvokeAsync("input.click_tile", p, ct);
    return JsonSerializer.Deserialize<InputClickTileResult>(resp, ProtocolJson.Options)
        ?? throw new SdvRpcException("input.click_tile", Protocol.JsonRpcErrorCode.InternalError,
            "empty input.click_tile response");
}
```

If `Player.cs` or `Input.cs` does not already import `SdvTestFramework.Protocol`, add:

```csharp
using SdvTestFramework.Protocol;
```

- [ ] **Step 4: Run DSL tests and verify they pass**

Run:

```bash
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter "SelectItem|ClickTile"
```

Expected: PASS.

- [ ] **Step 5: Commit DSL wrappers**

Run:

```bash
git add src/Runner.Dsl/Player.cs src/Runner.Dsl/Input.cs tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs tests/Runner.Dsl.Tests/Facets/InputTests.cs
git commit -m "Add DSL wrappers for selection and tile clicks"
```

## Task 6: SVE Scenario 31 And Documentation

**Files:**
- Create: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/31-sve-combat-lab-click-bomb-mummy.test.json`
- Modify: `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`
- Modify: `docs/rpc-schema.md`
- Modify: `docs/dsl-quickstart.md`
- Modify: `docs/wiki/examples.md`
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Mark Slice 23 active in the capability backlog**

Modify `SVE_FROBBY_CAPABILITY_TODO.md` immediately after Slice 22:

```markdown
- [ ] Active: Slice 23, input-level hotbar selection and gameplay tile click.
  - SVE pressure: semantic inventory-object placement proves object behavior, but mod UI testing also needs player-real selected-item click paths that do not bypass active object selection or gameplay click hooks.
  - Frobby goal: add neutral `player.select_item` and `input.click_tile` RPCs, route left-click through Stardew's gameplay use path, and prove click-based bomb placement against the existing Combat Lab corrupt-mummy cleanup scenario.
  - Design spec: `docs/superpowers/specs/2026-05-23-sve-slice-23-input-tile-click-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-23-sve-slice-23-input-tile-click.md`.
```

- [ ] **Step 2: Add SVE scenario 31**

Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/31-sve-combat-lab-click-bomb-mummy.test.json` with this complete content:

```json
{
  "name": "sve_combat_lab_click_bomb_mummy_cleanup",
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
      "action": "player.select_item",
      "args": {
        "id": "(O)287"
      }
    },
    {
      "action": "input.click_tile",
      "args": {
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
      "action": "wait.visual_effects",
      "args": {
        "location": "Frobby_CombatLab",
        "temporary_sprites": {
          "texture_asset": "LooseSprites/Cursors",
          "source_rect": [598, 1279, 3, 4],
          "runtime_type": "TemporaryAnimatedSprite",
          "min_count": 1
        },
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
      "message": "Click bomb cleanup scenario should finish inside the Frobby combat dev room"
    }
  ]
}
```

- [ ] **Step 3: Document RPC schema**

In `docs/rpc-schema.md`, add `player.select_item` after `player.give_item`:

````markdown
### player.select_item

Selects an existing farmer inventory item by exact slot or item id. This does
not create or consume items; it only sets the current selected inventory slot so
later gameplay input can use Stardew's normal selected-item path.

Request:
```json
{ "jsonrpc": "2.0", "id": 42, "method": "player.select_item", "params": { "id": "(O)287" } }
```

Alternative slot request:
```json
{ "jsonrpc": "2.0", "id": 43, "method": "player.select_item", "params": { "slot": 1, "prefer_hotbar": false } }
```

Response:
```json
{
  "ok": true,
  "tick": 123,
  "slot": 1,
  "item": {
    "slot": 1,
    "id": "(O)287",
    "item_id": "287",
    "qualified_id": "(O)287",
    "name": "Bomb",
    "stack": 1,
    "runtime_type": "Object"
  }
}
```

Validation: exactly one of `id` or `slot` is required. Selecting by id matches
`qualified_id`, `item_id`, or `id`; by default it prefers hotbar slots `0..11`.

**Implemented in:** `src/Harness/Handlers/PlayerSelectItemHandler.cs`
**Tested in:** `tests/Harness.Tests/PlayerSelectItemHandlerTests.cs`.
````

Add `input.click_tile` after `input.click`:

````markdown
### input.click_tile

Clicks a gameplay tile using Stardew's native left-click/use-tool path. Use this
when a scenario needs selected-item behavior, location click hooks, or Harmony
patches that observe normal gameplay input. This does not move the user's OS
cursor; Frobby drives the deterministic in-game cursor state.

Request:
```json
{
  "jsonrpc": "2.0",
  "id": 44,
  "method": "input.click_tile",
  "params": {
    "location": "Frobby_CombatLab",
    "x": 9,
    "y": 9,
    "button": "left"
  }
}
```

Response:
```json
{
  "ok": true,
  "tick": 124,
  "location": "Frobby_CombatLab",
  "tile": { "x": 9, "y": 9 },
  "screen": { "x": 576, "y": 576 },
  "world": { "x": 608, "y": 608 },
  "selected_item": {
    "slot": 1,
    "id": "(O)287",
    "item_id": "287",
    "qualified_id": "(O)287",
    "name": "Bomb",
    "stack": 1,
    "runtime_type": "Object"
  },
  "handled": true
}
```

Slice 23 supports `button: "left"` only. Use `screen_offset_x` and
`screen_offset_y` when the click must target a non-center pixel within the tile.

**Implemented in:** `src/Harness/Handlers/InputClickTileHandler.cs`
**Tested in:** `tests/Harness.Tests/InputClickTileHandlerTests.cs`.
````

- [ ] **Step 4: Document usage examples**

In `docs/dsl-quickstart.md`, update the `world.place_inventory_object` section so it no longer says vanilla bombs should be observed through `location.objects`. Use this wording:

```markdown
Use `world.place_inventory_object` when the test needs deterministic inventory
object placement from inventory without depending on cursor state. Some objects
remain observable in `state.location.objects`; vanilla bombs in Stardew 1.6 do
not. For vanilla bombs, wait on the fuse temporary sprite through
`wait.visual_effects`, then assert the gameplay outcome.
```

Add the click-based JSON example:

```json
{ "action": "player.give_item", "args": { "id": "(O)287", "count": 1 } },
{ "action": "player.select_item", "args": { "id": "(O)287" } },
{
  "action": "input.click_tile",
  "args": {
    "location": "Frobby_CombatLab",
    "x": 9,
    "y": 9
  }
},
{
  "action": "wait.visual_effects",
  "args": {
    "location": "Frobby_CombatLab",
    "temporary_sprites": {
      "texture_asset": "LooseSprites/Cursors",
      "source_rect": [598, 1279, 3, 4],
      "runtime_type": "TemporaryAnimatedSprite",
      "min_count": 1
    },
    "timeout_ms": 15000,
    "poll_ms": 100
  }
}
```

Add the C# DSL example:

```csharp
await Player.GiveItem("(O)287");
await Player.SelectItem(id: "(O)287");
var click = await Input.ClickTile(9, 9, location: "Frobby_CombatLab");
Assert.True(click.Handled);
```

In `docs/wiki/examples.md`, update `Explosion Cleanup`:

```markdown
Use `world.place_inventory_object` when a scenario needs deterministic
inventory-object placement. Use `player.select_item` plus `input.click_tile`
when a scenario needs selected-item, gameplay-click behavior. Vanilla bombs
should be observed through `state.visual_effects` fuse sprites plus the final
world-state outcome, not through `state.location.objects`.
```

- [ ] **Step 5: Document SVE scenario 31**

In `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`, add after scenario 30:

```markdown
Scenario `tests/sdv/31-sve-combat-lab-click-bomb-mummy.test.json` validates the
input-level selected-item path. It gives the farmer a vanilla bomb, selects it
through Frobby's neutral `player.select_item`, clicks a gameplay tile through
`input.click_tile`, waits for Stardew's live fuse sprite, and verifies the
relocated corrupt mummy is removed. This differs from scenario 30 by routing
through gameplay input instead of `world.place_inventory_object`.
```

- [ ] **Step 6: Commit scenario and docs**

Run:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded add tests/sdv/31-sve-combat-lab-click-bomb-mummy.test.json docs/FROBBY.md
git -C /home/fintan/stardewRepos/StardewValleyExpanded commit -m "Add click-based bomb cleanup scenario"
git add docs/rpc-schema.md docs/dsl-quickstart.md docs/wiki/examples.md SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "Document selected-item tile click flow"
```

## Task 7: Focused, Broad, And Live Verification

**Files:**
- Modify only if verification reveals a failing test tied to this slice.

- [ ] **Step 1: Run focused Frobby tests**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter "PlayerSelectItemSerializationTests|InputClickTileSerializationTests"
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "PlayerSelectItemHandlerTests|InputClickTileHandlerTests"
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "InputClickTile_PassesThroughAndReportsReadableStep|ShouldAutoCaptureStep_SkipsTimingAndInstrumentationSteps"
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter "SelectItem|ClickTile"
```

Expected: all focused tests pass.

- [ ] **Step 2: Run broad Frobby tests**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj
dotnet test tests/Harness.Tests/Harness.Tests.csproj
dotnet test tests/Runner.Tests/Runner.Tests.csproj
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj
dotnet build -v minimal
```

Expected: all tests pass and build has 0 errors.

- [ ] **Step 3: Run live SVE click scenario headless**

Run from `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-23-click-bomb tests/sdv/31-sve-combat-lab-click-bomb-mummy.test.json
```

Expected: scenario 31 passes. The report should show `player.select_item`, `input.click_tile`, the fuse `wait.visual_effects`, corrupt-mummy count `0`, and a frozen final screenshot.

- [ ] **Step 4: Run adjacent Combat Lab regressions headless**

Run from `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-23-regressions tests/sdv/30-sve-combat-lab-bomb-mummy.test.json tests/sdv/29-sve-combat-lab-explode-mummy.test.json tests/sdv/28-sve-combat-lab-relocate-mod-monster.test.json tests/sdv/27-sve-combat-lab-vanilla-monster.test.json
```

Expected: scenarios 30, 29, 28, and 27 pass headless.

- [ ] **Step 5: Mark Slice 23 done**

Modify `SVE_FROBBY_CAPABILITY_TODO.md` Slice 23 entry:

```markdown
- [x] Done: Slice 23, input-level hotbar selection and gameplay tile click.
  - SVE pressure: semantic inventory-object placement proves object behavior, but mod UI testing also needs player-real selected-item click paths that do not bypass active object selection or gameplay click hooks.
  - Frobby goal: add neutral `player.select_item` and `input.click_tile` RPCs, route left-click through Stardew's gameplay use path, and prove click-based bomb placement against the existing Combat Lab corrupt-mummy cleanup scenario.
  - Design spec: `docs/superpowers/specs/2026-05-23-sve-slice-23-input-tile-click-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-23-sve-slice-23-input-tile-click.md`.
  - Done: protocol models, harness handlers, runner label/autocapture, DSL wrappers, docs, and SVE scenario 31.
  - Verified: headless SVE scenario 31 selected a real vanilla bomb, clicked tile `(9,9)` in `Frobby_CombatLab`, observed Stardew's fuse sprite, and verified corrupt-mummy cleanup. Adjacent scenarios 30, 29, 28, and 27 also passed headless.
```

Commit the completion note:

```bash
git add SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "Mark input tile click slice complete"
```

- [ ] **Step 6: Final branch status check**

Run:

```bash
git status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
```

Expected:

- Frobby branch contains the Slice 23 commits and no untracked work except user-owned local files.
- SVE branch contains scenario 31/docs and no untracked work except user-owned local files.

## Scope And Safety Notes

- `input.click` stays menu-only. World clicks use `input.click_tile`.
- `input.click_tile` is left-click only in this slice.
- Frobby production code must not hard-code `Frobby_CombatLab`, SVE sprite paths, SVE item IDs, SVE monster labels, or vanilla bomb fuse filters.
- Scenario 31 may use `Frobby_CombatLab`, `(O)287`, `Characters/Monsters/CorruptMummy`, and the vanilla fuse sprite because it is SVE proof content, not Frobby production code.
- Live tests must run headless by default.
