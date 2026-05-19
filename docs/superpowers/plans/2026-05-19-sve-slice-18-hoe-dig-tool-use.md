# SVE Slice 18 Hoe/Dig Tool-Use Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add neutral Hoe-first `world.use_tool` support and prove SVE's relocated Secret Note #18 buried reward through a real player-like dig scenario.

**Architecture:** Keep mod-specific ids and coordinates in SVE scenario JSON. Frobby gains additive protocol DTOs, player seen-secret-note projection/mutation, a small harness `world.use_tool` handler that calls Stardew's native `Tool.DoFunction` path, and runner wait/description/docs support. Use TDD at each layer: protocol serialization first, harness validation/unit behavior second, runner wait/action behavior third, then live SVE verification.

**Tech Stack:** C# 12, .NET 10 runner/tests, .NET 6 SMAPI harness/protocol, System.Text.Json with `ProtocolJson.Options`, xUnit, JSON scenarios, Stardew Valley/SMAPI runtime.

---

## File Structure

- Create `src/Protocol/Models/AddSecretNoteSeenRequest.cs` for `player.add_secret_note_seen` params.
- Create `src/Protocol/Models/UseToolRequest.cs` for `world.use_tool` params and result.
- Modify `src/Protocol/Models/PlayerState.cs` to add `List<int> SecretNotesSeen`.
- Create `tests/Protocol.Tests/SecretNotePlayerStateSerializationTests.cs` for additive player state and request serialization.
- Create `tests/Protocol.Tests/UseToolSerializationTests.cs` for `world.use_tool` request/result serialization.
- Create `src/Harness/Handlers/PlayerAddSecretNoteSeenHandler.cs` for neutral secret-note setup.
- Create `tests/Harness.Tests/PlayerAddSecretNoteSeenHandlerTests.cs`.
- Modify `src/Harness/Handlers/StatePlayerHandler.cs` to project `secret_notes_seen`.
- Modify `tests/Harness.Tests/StatePlayerHandlerTests.cs`.
- Create `src/Harness/Handlers/WorldUseToolHandler.cs` for Hoe-first player-like tool use.
- Create `tests/Harness.Tests/WorldUseToolHandlerTests.cs`.
- Modify `src/Harness/ModEntry.cs` to register `player.add_secret_note_seen` and `world.use_tool`.
- Modify `src/Runner/Scenarios/ScenarioRunner.cs` so `wait.player` can filter `secret_note_seen`, observed progress includes it, and step labels describe `world.use_tool`.
- Modify `tests/Runner.Tests/ScenarioRunnerTests.cs` for `wait.player.secret_note_seen`.
- Modify `src/Runner.Dsl/Player.cs` and `src/Runner.Dsl/World.cs` for C# DSL wrappers.
- Add tests in `tests/Runner.Tests/ScenarioRunnerDslTests.cs` if the current DSL fake session pattern supports these wrappers cleanly.
- Modify docs: `README.md`, `docs/rpc-schema.md`, `docs/dsl-quickstart.md`, `docs/wiki/index.md`, `docs/wiki/examples.md`, `SVE_FROBBY_CAPABILITY_TODO.md`.
- Add SVE scenario `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/26-sve-secret-note-dig.test.json`.

---

### Task 1: Add Protocol DTOs And Player Secret Note State

**Files:**
- Create: `src/Protocol/Models/AddSecretNoteSeenRequest.cs`
- Create: `src/Protocol/Models/UseToolRequest.cs`
- Modify: `src/Protocol/Models/PlayerState.cs`
- Create: `tests/Protocol.Tests/SecretNotePlayerStateSerializationTests.cs`
- Create: `tests/Protocol.Tests/UseToolSerializationTests.cs`

- [ ] **Step 1: Write the failing protocol serialization tests**

Create `tests/Protocol.Tests/SecretNotePlayerStateSerializationTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class SecretNotePlayerStateSerializationTests
{
    [Fact]
    public void PlayerState_SerializesSecretNotesSeenAsSnakeCase()
    {
        var p = new PlayerState
        {
            Name = "Tester",
            Location = "Desert",
            Tile = new TilePoint { X = 9, Y = 43 },
            SecretNotesSeen = new() { 18, 25 },
        };

        var json = JsonSerializer.Serialize(p, ProtocolJson.Options);

        Assert.Contains("\"secret_notes_seen\":[18,25]", json);
        Assert.DoesNotContain("SecretNotesSeen", json);
    }

    [Fact]
    public void AddSecretNoteSeenRequest_DeserializesSnakeCase()
    {
        var req = JsonSerializer.Deserialize<AddSecretNoteSeenRequest>(
            "{\"id\":18}",
            ProtocolJson.Options)!;

        Assert.Equal(18, req.Id);
    }
}
```

Create `tests/Protocol.Tests/UseToolSerializationTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class UseToolSerializationTests
{
    [Fact]
    public void UseToolRequest_DeserializesSnakeCase()
    {
        var req = JsonSerializer.Deserialize<UseToolRequest>(
            "{\"tool\":\"Hoe\",\"location\":\"Desert\",\"x\":9,\"y\":43,\"facing\":\"down\",\"power\":0}",
            ProtocolJson.Options)!;

        Assert.Equal("Hoe", req.Tool);
        Assert.Equal("Desert", req.Location);
        Assert.Equal(9, req.X);
        Assert.Equal(43, req.Y);
        Assert.Equal("down", req.Facing);
        Assert.Equal(0, req.Power);
    }

    [Fact]
    public void UseToolResult_SerializesDiagnosticsAsSnakeCase()
    {
        var result = new UseToolResult
        {
            Tick = 123,
            Tool = "Hoe",
            Location = "Desert",
            Tile = new TilePoint { X = 9, Y = 43 },
            SelectedItemId = "Hoe",
            SelectedItemQualifiedId = "(T)Hoe",
            SelectedItemName = "Hoe",
            SelectedItemRuntimeType = "Hoe",
            SelectedToolIndex = 1,
            Invoked = true,
        };

        var json = JsonSerializer.Serialize(result, ProtocolJson.Options);

        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"tick\":123", json);
        Assert.Contains("\"tool\":\"Hoe\"", json);
        Assert.Contains("\"location\":\"Desert\"", json);
        Assert.Contains("\"tile\":{\"x\":9,\"y\":43}", json);
        Assert.Contains("\"selected_tool_index\":1", json);
        Assert.Contains("\"selected_item_runtime_type\":\"Hoe\"", json);
        Assert.Contains("\"invoked\":true", json);
        Assert.DoesNotContain("SelectedToolIndex", json);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter "FullyQualifiedName~SecretNotePlayerStateSerializationTests|FullyQualifiedName~UseToolSerializationTests" -v minimal
```

Expected: FAIL with compile errors for missing `AddSecretNoteSeenRequest`, `UseToolRequest`, `UseToolResult`, and `PlayerState.SecretNotesSeen`.

- [ ] **Step 3: Add the minimal protocol models**

Create `src/Protocol/Models/AddSecretNoteSeenRequest.cs`:

```csharp
namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape for <c>player.add_secret_note_seen</c>.</summary>
public sealed class AddSecretNoteSeenRequest
{
    public int Id { get; set; }
}
```

Create `src/Protocol/Models/UseToolRequest.cs`:

```csharp
namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape for <c>world.use_tool</c>.</summary>
public sealed class UseToolRequest
{
    public string? Tool { get; set; }
    public string? Location { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public string? Facing { get; set; }
    public int Power { get; set; }
}

/// <summary>Response shape for <c>world.use_tool</c>.</summary>
public sealed class UseToolResult : MutatorOk
{
    public string Tool { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public TilePoint Tile { get; set; } = new();
    public string? SelectedItemId { get; set; }
    public string? SelectedItemQualifiedId { get; set; }
    public string? SelectedItemName { get; set; }
    public string? SelectedItemRuntimeType { get; set; }
    public int? SelectedToolIndex { get; set; }
    public bool Invoked { get; set; }
}
```

Modify `src/Protocol/Models/PlayerState.cs`, adding the property after `EventsSeen`:

```csharp
public List<int> SecretNotesSeen { get; set; } = new();
```

- [ ] **Step 4: Run tests to verify they pass**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter "FullyQualifiedName~SecretNotePlayerStateSerializationTests|FullyQualifiedName~UseToolSerializationTests" -v minimal
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Protocol/Models/AddSecretNoteSeenRequest.cs src/Protocol/Models/UseToolRequest.cs src/Protocol/Models/PlayerState.cs tests/Protocol.Tests/SecretNotePlayerStateSerializationTests.cs tests/Protocol.Tests/UseToolSerializationTests.cs
git commit -m "Add tool use protocol models"
```

---

### Task 2: Add Secret Note Seen Projection And Mutator

**Files:**
- Create: `src/Harness/Handlers/PlayerAddSecretNoteSeenHandler.cs`
- Modify: `src/Harness/Handlers/StatePlayerHandler.cs`
- Modify: `src/Harness/ModEntry.cs`
- Create: `tests/Harness.Tests/PlayerAddSecretNoteSeenHandlerTests.cs`
- Modify: `tests/Harness.Tests/StatePlayerHandlerTests.cs`

- [ ] **Step 1: Write the failing handler/projection tests**

Create `tests/Harness.Tests/PlayerAddSecretNoteSeenHandlerTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class PlayerAddSecretNoteSeenHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => PlayerAddSecretNoteSeenHandler.Handle(null));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Handle_InvalidId_ThrowsInvalidParams(int id)
    {
        var p = JsonSerializer.SerializeToElement(new { id });

        var ex = Assert.Throws<JsonRpcException>(() => PlayerAddSecretNoteSeenHandler.Handle(p));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("positive", ex.Message);
    }

    [Fact(Skip = "Requires live SDV (Game1.MasterPlayer.secretNotesSeen read/write).")]
    public void Handle_ValidId_AddsSecretNoteSeen() { /* integration */ }
}
```

Modify `tests/Harness.Tests/StatePlayerHandlerTests.cs`:

```csharp
Assert.Equal(new[] { 18, 25 }, state.SecretNotesSeen);
```

Add this property to `FakePlayerStateWorld`:

```csharp
public IReadOnlyList<int> SecretNotesSeen { get; } = new[] { 18, 25 };
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~PlayerAddSecretNoteSeenHandlerTests|FullyQualifiedName~StatePlayerHandlerTests" -v minimal
```

Expected: FAIL with missing handler and missing `IPlayerStateWorld.SecretNotesSeen`.

- [ ] **Step 3: Implement projection and handler**

Modify `src/Harness/Handlers/StatePlayerHandler.cs`:

```csharp
SecretNotesSeen = world.SecretNotesSeen.ToList(),
```

Add to `IPlayerStateWorld`:

```csharp
IReadOnlyList<int> SecretNotesSeen { get; }
```

Add to `SdvPlayerStateWorld`:

```csharp
public IReadOnlyList<int> SecretNotesSeen => Player.secretNotesSeen.ToList();
```

Create `src/Harness/Handlers/PlayerAddSecretNoteSeenHandler.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// Handler for <c>player.add_secret_note_seen</c>. Adds a secret-note id to the farmer's
/// seen-note set so scenarios can exercise note-gated mod content without custom hooks.
/// </summary>
public static class PlayerAddSecretNoteSeenHandler
{
    public const string Method = "player.add_secret_note_seen";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var req = RpcParams.Required<AddSecretNoteSeenRequest>(paramsElement);
        if (req.Id <= 0)
        {
            throw new JsonRpcException(
                JsonRpcErrorCode.InvalidParams,
                "params.id must be a positive secret note id");
        }

        RpcPreconditions.RequireWorldReady();

        Game1.MasterPlayer.secretNotesSeen.Add(req.Id);
        if (!ReferenceEquals(Game1.player, Game1.MasterPlayer))
            Game1.player.secretNotesSeen.Add(req.Id);

        return ProtocolJson.ToElement(new MutatorOk
        {
            Tick = Game1.ticks,
        });
    }
}
```

Modify `src/Harness/ModEntry.cs`, near the other `player.*` registrations:

```csharp
_rpc.Register(PlayerAddSecretNoteSeenHandler.Method, p => PlayerAddSecretNoteSeenHandler.Handle(p));
```

- [ ] **Step 4: Run tests to verify they pass**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~PlayerAddSecretNoteSeenHandlerTests|FullyQualifiedName~StatePlayerHandlerTests" -v minimal
```

Expected: PASS with the live-only integration test still skipped.

- [ ] **Step 5: Commit**

```bash
git add src/Harness/Handlers/PlayerAddSecretNoteSeenHandler.cs src/Harness/Handlers/StatePlayerHandler.cs src/Harness/ModEntry.cs tests/Harness.Tests/PlayerAddSecretNoteSeenHandlerTests.cs tests/Harness.Tests/StatePlayerHandlerTests.cs
git commit -m "Add secret note seen player state"
```

---

### Task 3: Add Hoe-First `world.use_tool` Harness Action

**Files:**
- Create: `src/Harness/Handlers/WorldUseToolHandler.cs`
- Create: `tests/Harness.Tests/WorldUseToolHandlerTests.cs`
- Modify: `src/Harness/ModEntry.cs`

- [ ] **Step 1: Write the failing handler tests**

Create `tests/Harness.Tests/WorldUseToolHandlerTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class WorldUseToolHandlerTests
{
    [Fact]
    public void Handle_MissingTool_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"x\":9,\"y\":43}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => WorldUseToolHandler.Handle(p, new FakeUseToolWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("tool", ex.Message);
    }

    [Fact]
    public void Handle_UnsupportedTool_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"tool\":\"Pickaxe\",\"x\":9,\"y\":43}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => WorldUseToolHandler.Handle(p, new FakeUseToolWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("only supports Hoe", ex.Message);
    }

    [Fact]
    public void Handle_PartialTile_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"tool\":\"Hoe\",\"x\":9}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => WorldUseToolHandler.Handle(p, new FakeUseToolWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("both x and y", ex.Message);
    }

    [Fact]
    public void Handle_NegativeTile_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"tool\":\"Hoe\",\"x\":-1,\"y\":43}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => WorldUseToolHandler.Handle(p, new FakeUseToolWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains(">= 0", ex.Message);
    }

    [Fact]
    public void Handle_NotWorldReady_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"tool\":\"Hoe\",\"x\":9,\"y\":43}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldUseToolHandler.Handle(p, new FakeUseToolWorld { IsWorldReady = false }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_LocationGuardMismatch_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"tool\":\"Hoe\",\"location\":\"Desert\",\"x\":9,\"y\":43}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldUseToolHandler.Handle(p, new FakeUseToolWorld { CurrentLocationName = "Farm" }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("location guard expected Desert", ex.Message);
    }

    [Fact]
    public void Handle_MissingHoe_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"tool\":\"Hoe\",\"x\":9,\"y\":43}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldUseToolHandler.Handle(p, new FakeUseToolWorld { HasHoe = false }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("could not find Hoe", ex.Message);
    }

    [Fact]
    public void Handle_HoeAtTileFacesAndInvokesNativeToolUse()
    {
        var world = new FakeUseToolWorld { CurrentLocationName = "Desert" };
        var p = JsonDocument.Parse("{\"tool\":\"hoe\",\"location\":\"Desert\",\"x\":9,\"y\":43,\"facing\":\"down\",\"power\":0}").RootElement;

        var result = WorldUseToolHandler.Handle(p, world);
        var json = result.GetRawText();

        Assert.Equal("down", world.FacedDirection);
        Assert.Equal(9, world.InvokedX);
        Assert.Equal(43, world.InvokedY);
        Assert.Equal(0, world.InvokedPower);
        Assert.Equal(1, world.SelectCount);
        Assert.Contains("\"tool\":\"Hoe\"", json);
        Assert.Contains("\"location\":\"Desert\"", json);
        Assert.Contains("\"selected_tool_index\":1", json);
        Assert.Contains("\"invoked\":true", json);
    }

    private sealed class FakeUseToolWorld : IUseToolWorld
    {
        public bool IsWorldReady { get; set; } = true;
        public string CurrentLocationName { get; set; } = "Desert";
        public int Tick { get; set; } = 456;
        public bool HasHoe { get; set; } = true;
        public string? FacedDirection { get; private set; }
        public int? InvokedX { get; private set; }
        public int? InvokedY { get; private set; }
        public int? InvokedPower { get; private set; }
        public int SelectCount { get; private set; }

        public UseToolSelectedItem SelectTool(string tool)
        {
            SelectCount++;
            if (!HasHoe)
                throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, "world.use_tool could not find Hoe in the farmer inventory");

            return new UseToolSelectedItem("Hoe", "(T)Hoe", "Hoe", "Hoe", 1);
        }

        public void FaceDirection(string direction) => FacedDirection = direction;

        public void UseToolAtTile(int x, int y, int power)
        {
            InvokedX = x;
            InvokedY = y;
            InvokedPower = power;
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter FullyQualifiedName~WorldUseToolHandlerTests -v minimal
```

Expected: FAIL with missing `WorldUseToolHandler`, `IUseToolWorld`, and `UseToolSelectedItem`.

- [ ] **Step 3: Implement the handler and production world**

Create `src/Harness/Handlers/WorldUseToolHandler.cs`:

```csharp
using System;
using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using StardewValley.Tools;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>world.use_tool</c>. Runs a player inventory tool against a target tile.</summary>
public static class WorldUseToolHandler
{
    public const string Method = "world.use_tool";

    private static readonly IUseToolWorld ProductionWorld = new SdvUseToolWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, IUseToolWorld world)
    {
        var req = RpcParams.Required<UseToolRequest>(paramsElement);
        var tool = NormalizeTool(req.Tool);
        ValidateRequest(req, tool);

        if (!world.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "no active save - world.use_tool requires a loaded world");

        if (!string.IsNullOrWhiteSpace(req.Location)
            && !string.Equals(req.Location, world.CurrentLocationName, StringComparison.Ordinal))
        {
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"world.use_tool location guard expected {req.Location}, current location is {world.CurrentLocationName}");
        }

        var selected = world.SelectTool(tool);
        if (!string.IsNullOrWhiteSpace(req.Facing))
            world.FaceDirection(NormalizeDirection(req.Facing));
        world.UseToolAtTile(req.X!.Value, req.Y!.Value, req.Power);

        return ProtocolJson.ToElement(new UseToolResult
        {
            Tick = world.Tick,
            Tool = tool,
            Location = world.CurrentLocationName,
            Tile = new TilePoint { X = req.X.Value, Y = req.Y.Value },
            SelectedItemId = selected.ItemId,
            SelectedItemQualifiedId = selected.QualifiedItemId,
            SelectedItemName = selected.Name,
            SelectedItemRuntimeType = selected.RuntimeType,
            SelectedToolIndex = selected.ToolIndex,
            Invoked = true,
        });
    }

    private static void ValidateRequest(UseToolRequest req, string tool)
    {
        if (tool != "Hoe")
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "world.use_tool currently only supports Hoe");
        if ((req.X is null) != (req.Y is null))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "world.use_tool requires both x and y");
        if (req.X is null || req.Y is null)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "world.use_tool requires target tile x and y");
        if (req.X < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.x must be >= 0");
        if (req.Y < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.y must be >= 0");
        if (req.Power < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.power must be >= 0");
        if (!string.IsNullOrWhiteSpace(req.Facing) && !IsKnownDirection(req.Facing))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, $"unknown direction: {req.Facing}");
    }

    private static string NormalizeTool(string? tool)
    {
        if (string.IsNullOrWhiteSpace(tool))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "world.use_tool requires params.tool");

        return tool.Trim().Equals("hoe", StringComparison.OrdinalIgnoreCase) ? "Hoe" : tool.Trim();
    }

    private static bool IsKnownDirection(string direction)
        => NormalizeDirection(direction) is "up" or "right" or "down" or "left";

    private static string NormalizeDirection(string direction)
        => direction.Trim().ToLowerInvariant();
}

internal interface IUseToolWorld
{
    bool IsWorldReady { get; }
    string CurrentLocationName { get; }
    int Tick { get; }
    UseToolSelectedItem SelectTool(string tool);
    void FaceDirection(string direction);
    void UseToolAtTile(int x, int y, int power);
}

internal sealed record UseToolSelectedItem(
    string? ItemId,
    string? QualifiedItemId,
    string? Name,
    string? RuntimeType,
    int? ToolIndex);

internal sealed class SdvUseToolWorld : IUseToolWorld
{
    public bool IsWorldReady => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;
    public string CurrentLocationName => CurrentLocation.NameOrUniqueName ?? CurrentLocation.Name ?? string.Empty;
    public int Tick => Game1.ticks;

    public UseToolSelectedItem SelectTool(string tool)
    {
        var player = Game1.player;
        if (player.CurrentTool is Hoe current)
            return SummarizeTool(current, player.CurrentToolIndex);

        for (var slot = 0; slot < player.Items.Count; slot++)
        {
            if (player.Items[slot] is not Hoe hoe)
                continue;

            player.CurrentToolIndex = slot;
            return SummarizeTool(hoe, slot);
        }

        throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
            "world.use_tool could not find Hoe in the farmer inventory");
    }

    public void FaceDirection(string direction)
    {
        Game1.player.faceDirection(DirectionToStardew(direction));
    }

    public void UseToolAtTile(int x, int y, int power)
    {
        if (Game1.player.CurrentTool is not Hoe hoe)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "world.use_tool requires a selected Hoe");

        // Stardew Tool.DoFunction receives pixel coordinates and converts them to tile
        // coordinates internally. Calling the tool path lets location/Harmony patches
        // observe buried-item behavior without direct reward mutation.
        hoe.DoFunction(CurrentLocation, x * 64, y * 64, power, Game1.player);
    }

    private static UseToolSelectedItem SummarizeTool(Tool tool, int? slot)
        => new(
            tool.ItemId,
            tool.QualifiedItemId,
            tool.DisplayName ?? tool.Name,
            tool.GetType().Name,
            slot);

    private static int DirectionToStardew(string direction)
        => direction switch
        {
            "up" => 0,
            "right" => 1,
            "down" => 2,
            "left" => 3,
            _ => throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, $"unknown direction: {direction}"),
        };

    private static GameLocation CurrentLocation
        => Game1.currentLocation
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"{WorldUseToolHandler.Method} requires a current location");
}
```

Modify `src/Harness/ModEntry.cs`, near the world registrations:

```csharp
_rpc.Register(WorldUseToolHandler.Method, p => WorldUseToolHandler.Handle(p));
```

If the startup log has a human-readable RPC list, add `world.use_tool` there too.

- [ ] **Step 4: Run tests to verify they pass**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter FullyQualifiedName~WorldUseToolHandlerTests -v minimal
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Harness/Handlers/WorldUseToolHandler.cs src/Harness/ModEntry.cs tests/Harness.Tests/WorldUseToolHandlerTests.cs
git commit -m "Add Hoe tool use harness action"
```

---

### Task 4: Add Runner Wait Filter, Step Labels, And DSL Wrappers

**Files:**
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`
- Modify: `tests/Runner.Tests/ScenarioRunnerTests.cs`
- Modify: `src/Runner.Dsl/Player.cs`
- Modify: `src/Runner.Dsl/World.cs`
- Modify: `tests/Runner.Tests/ScenarioRunnerDslTests.cs` if needed by existing DSL coverage.

- [ ] **Step 1: Write failing runner tests for `secret_note_seen`**

Add to `tests/Runner.Tests/ScenarioRunnerTests.cs`, near the existing `wait.player` progression tests:

```csharp
[Fact]
public async Task RunAsync_WaitPlayerSecretNoteSeenPollsUntilPresent()
{
    int stateCalls = 0;
    var inv = new FakeSessionInvoker(method =>
    {
        if (method == "scenario.begin" || method == "scenario.end")
            return JsonDocument.Parse("{}").RootElement;
        if (method == "freeze.status")
            return JsonDocument.Parse("{\"is_warping\":false,\"is_fading\":false}").RootElement;
        if (method == "state.player")
        {
            stateCalls++;
            var json = stateCalls == 1
                ? "{\"name\":\"Tester\",\"health\":100,\"location\":\"Desert\",\"tile\":{\"x\":9,\"y\":43},\"mail_received\":[],\"mail_for_tomorrow\":[],\"events_seen\":[],\"secret_notes_seen\":[]}"
                : "{\"name\":\"Tester\",\"health\":100,\"location\":\"Desert\",\"tile\":{\"x\":9,\"y\":43},\"mail_received\":[],\"mail_for_tomorrow\":[],\"events_seen\":[],\"secret_notes_seen\":[18]}";
            return JsonDocument.Parse(json).RootElement;
        }
        return JsonDocument.Parse("{}").RootElement;
    });

    var spec = new ScenarioSpec
    {
        Name = "wait_secret_note_seen",
        Steps =
        {
            new ScenarioStep
            {
                Action = "wait.player",
                Args = JsonDocument.Parse("{\"secret_note_seen\":18,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
            },
        },
    };

    var report = await new ScenarioRunner(inv.Session).RunAsync(spec, CancellationToken.None);

    Assert.True(report.Passed, string.Join("\n", report.Failures));
    Assert.True(stateCalls >= 2);
}

[Fact]
public async Task RunAsync_WaitPlayerSecretNoteSeenTimeoutIncludesObservedSummary()
{
    var inv = new FakeSessionInvoker(method =>
    {
        if (method == "scenario.begin" || method == "scenario.end")
            return JsonDocument.Parse("{}").RootElement;
        if (method == "freeze.status")
            return JsonDocument.Parse("{\"is_warping\":false,\"is_fading\":false}").RootElement;
        if (method == "state.player")
            return JsonDocument.Parse("{\"name\":\"Tester\",\"health\":100,\"location\":\"Desert\",\"tile\":{\"x\":9,\"y\":43},\"mail_received\":[],\"mail_for_tomorrow\":[],\"events_seen\":[],\"secret_notes_seen\":[12]}").RootElement;
        return JsonDocument.Parse("{}").RootElement;
    });

    var spec = new ScenarioSpec
    {
        Name = "wait_secret_note_seen_timeout",
        Steps =
        {
            new ScenarioStep
            {
                Action = "wait.player",
                Args = JsonDocument.Parse("{\"secret_note_seen\":18,\"timeout_ms\":1,\"poll_ms\":1}").RootElement,
            },
        },
    };

    var report = await new ScenarioRunner(inv.Session).RunAsync(spec, CancellationToken.None);

    Assert.False(report.Passed);
    var failure = Assert.Single(report.Failures);
    Assert.Contains("secret_notes_seen contains 18", failure);
    Assert.Contains("secret_notes_seen=1", failure);
}
```

If `FakeSessionInvoker` in the file uses a different constructor shape, adapt only the invoker setup to the current local pattern while preserving the assertions above.

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~WaitPlayerSecretNoteSeen" -v minimal
```

Expected: FAIL because `WaitPlayerStepArgs` has no `SecretNoteSeen` and diagnostics ignore `secret_notes_seen`.

- [ ] **Step 3: Implement runner wait and labels**

Modify `src/Runner/Scenarios/ScenarioRunner.cs`.

Add validation in `ValidateWaitPlayerArgs`:

```csharp
if (args.SecretNoteSeen is <= 0)
    throw new InvalidOperationException("wait.player requires args.secret_note_seen > 0");
```

Add matching in `ProgressionFiltersMatch`:

```csharp
&& IntArrayContains(root, "secret_notes_seen", args.SecretNoteSeen)
```

Add this helper near `StringArrayContains`:

```csharp
private static bool IntArrayContains(JsonElement element, string property, int? expected)
{
    if (expected is null)
        return true;

    if (element.ValueKind != JsonValueKind.Object
        || !element.TryGetProperty(property, out var value))
    {
        return false;
    }

    return value.ValueKind == JsonValueKind.Array
        && value.EnumerateArray().Any(item =>
            item.ValueKind == JsonValueKind.Number
            && item.TryGetInt32(out var actual)
            && actual == expected.Value);
}
```

Add formatting in `FormatWaitPlayerFilters`:

```csharp
if (args.SecretNoteSeen is not null) filters.Add($"secret_notes_seen contains {args.SecretNoteSeen}");
```

Add observed summary in `FormatObservedProgressionSummary`:

```csharp
+ $" secret_notes_seen={CountJsonArray(root, "secret_notes_seen")}";
```

Rename or add helper:

```csharp
private static string CountJsonArray(JsonElement root, string property)
{
    return root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
        ? value.GetArrayLength().ToString(CultureInfo.InvariantCulture)
        : "?";
}
```

Use `CountJsonArray` from the existing mail/event count summary too, or keep `CountStringArray` for strings and call `CountJsonArray` only for secret notes.

Add to `WaitPlayerStepArgs`:

```csharp
public int? SecretNoteSeen { get; set; }
```

In `DescribeStep`, add or extend the switch arm so `world.use_tool` produces a readable label:

```csharp
"world.use_tool" => $"{GetStringArg(step.Args, "tool") ?? "tool"} at {GetIntArg(step.Args, "x")},{GetIntArg(step.Args, "y")}",
```

If helper names differ, follow the existing `combat.attack`/`player.warp` description helper pattern in this file.

- [ ] **Step 4: Add DSL wrappers**

Modify `src/Runner.Dsl/Player.cs`:

```csharp
/// <summary>Add secret note id <paramref name="id"/> to the master farmer's seen-note set.</summary>
public static async Task AddSecretNoteSeen(int id, CancellationToken ct = default)
{
    var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
    var p = JsonSerializer.SerializeToElement(new AddSecretNoteSeenRequest { Id = id }, ProtocolJson.Options);
    await s.InvokeAsync("player.add_secret_note_seen", p, ct);
}
```

Modify `src/Runner.Dsl/World.cs`:

```csharp
/// <summary>Use a player inventory tool against a target tile in the current location.</summary>
public static async Task<UseToolResult> UseTool(
    string tool,
    int x,
    int y,
    string? location = null,
    string? facing = null,
    int power = 0,
    CancellationToken ct = default)
{
    var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
    var p = JsonSerializer.SerializeToElement(new UseToolRequest
    {
        Tool = tool,
        Location = location,
        X = x,
        Y = y,
        Facing = facing,
        Power = power,
    }, ProtocolJson.Options);
    var resp = await s.InvokeAsync("world.use_tool", p, ct);
    return JsonSerializer.Deserialize<UseToolResult>(resp, ProtocolJson.Options)
        ?? throw new SdvRpcException("world.use_tool", Protocol.JsonRpcErrorCode.InternalError,
            "empty world.use_tool response");
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~WaitPlayerSecretNoteSeen" -v minimal
dotnet build sdv-test-framework.slnx -v minimal
```

Expected: PASS and build succeeds. If DSL tests were added, include their filter in the first command.

- [ ] **Step 6: Commit**

```bash
git add src/Runner/Scenarios/ScenarioRunner.cs src/Runner.Dsl/Player.cs src/Runner.Dsl/World.cs tests/Runner.Tests/ScenarioRunnerTests.cs tests/Runner.Tests/ScenarioRunnerDslTests.cs
git commit -m "Add runner support for tool use scenarios"
```

If `tests/Runner.Tests/ScenarioRunnerDslTests.cs` was not changed, omit it from `git add`.

---

### Task 5: Document Neutral Tool Use

**Files:**
- Modify: `README.md`
- Modify: `docs/rpc-schema.md`
- Modify: `docs/dsl-quickstart.md`
- Modify: `docs/wiki/index.md`
- Modify: `docs/wiki/examples.md`

- [ ] **Step 1: Update RPC docs**

In `docs/rpc-schema.md`, add method entries near other `player.*` and `world.*` actions:

````markdown
### player.add_secret_note_seen

Adds a secret note id to the farmer's seen-note set. Use this to set up
note-gated content before exercising the real in-game interaction.

Request:

```json
{ "id": 18 }
```

Response:

```json
{ "ok": true, "tick": 12345 }
```

### world.use_tool

Uses a tool from the farmer inventory against a target tile in the current
location. The first supported tool is `Hoe`; unsupported tool names fail with
`InvalidParams`. Scenarios should warp or move the player explicitly first.

Request:

```json
{
  "tool": "Hoe",
  "location": "Desert",
  "x": 9,
  "y": 43,
  "facing": "down",
  "power": 0
}
```

Response:

```json
{
  "ok": true,
  "tick": 12345,
  "tool": "Hoe",
  "location": "Desert",
  "tile": { "x": 9, "y": 43 },
  "selected_item_id": "Hoe",
  "selected_item_qualified_id": "(T)Hoe",
  "selected_item_name": "Hoe",
  "selected_item_runtime_type": "Hoe",
  "selected_tool_index": 1,
  "invoked": true
}
```
````

Also update the `state.player` field list to include:

```markdown
- `secret_notes_seen`: numeric secret-note ids seen by the farmer.
```

- [ ] **Step 2: Update README authoring guidance**

In `README.md`, add a bullet after the progression/setup helper bullet:

```markdown
- Use `player.add_secret_note_seen` plus `world.use_tool` when testing note-gated
  buried rewards or other player-like tool interactions. Keep note ids, item ids,
  and tile coordinates in the mod repo scenario; Frobby only drives the neutral
  tool path.
```

- [ ] **Step 3: Update DSL quickstart**

In `docs/dsl-quickstart.md`, add a short example near world/action examples:

````markdown
```csharp
await Player.AddSecretNoteSeen(18);
await Player.Warp("Desert", 9, 44);
await World.UseTool("Hoe", 9, 43, location: "Desert", facing: "down");
```
````

Add JSON equivalent:

```json
{ "action": "player.add_secret_note_seen", "args": { "id": 18 } },
{ "action": "world.use_tool", "args": { "tool": "Hoe", "location": "Desert", "x": 9, "y": 43, "facing": "down" } }
```

- [ ] **Step 4: Update wiki examples**

In `docs/wiki/examples.md`, add a new entry under "Shops, Inventory, Combat, Fishing, And World Content" after the world object interaction entry:

```markdown
- SVE Secret Note buried reward:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/26-sve-secret-note-dig.test.json`
```

Update the paragraph below that list:

```markdown
Use these when testing runtime state, player-like tool use, or world rewards
rather than parsing a mod's content files.
```

- [ ] **Step 5: Run doc checks**

Run:

```bash
rg -n "world.use_tool|player.add_secret_note_seen|secret_notes_seen|Secret Note buried reward" README.md docs/rpc-schema.md docs/dsl-quickstart.md docs/wiki/index.md docs/wiki/examples.md
git diff --check
```

Expected: all terms appear in the relevant docs and whitespace check is clean.

- [ ] **Step 6: Commit**

```bash
git add README.md docs/rpc-schema.md docs/dsl-quickstart.md docs/wiki/index.md docs/wiki/examples.md
git commit -m "Document neutral tool use scenarios"
```

---

### Task 6: Add SVE Secret Note #18 Proof Scenario

**Files:**
- Create: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/26-sve-secret-note-dig.test.json`
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Write the SVE scenario**

Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/26-sve-secret-note-dig.test.json`:

```json
{
  "name": "sve_secret_note_dig",
  "fixture": "m0spike_436515781",
  "config": { "seed": 436515781 },
  "steps": [
    { "action": "time.set", "args": { "time": 900, "day": 1, "season": "spring", "year": 1 } },
    { "action": "player.add_secret_note_seen", "args": { "id": 18 } },
    {
      "action": "wait.player",
      "args": {
        "secret_note_seen": 18,
        "timeout_ms": 5000,
        "poll_ms": 100
      }
    },
    { "action": "player.warp", "args": { "location": "Desert", "x": 9, "y": 44 } },
    {
      "action": "wait.location",
      "args": {
        "location": "Desert",
        "timeout_ms": 10000,
        "poll_ms": 100
      }
    },
    {
      "action": "world.use_tool",
      "args": {
        "tool": "Hoe",
        "location": "Desert",
        "x": 9,
        "y": 43,
        "facing": "up",
        "power": 0
      }
    },
    {
      "action": "wait.player",
      "args": {
        "mail_received": "SecretNote18_done",
        "timeout_ms": 10000,
        "poll_ms": 100
      }
    },
    {
      "action": "wait.location_content",
      "args": {
        "location": "Desert",
        "collection": "debris",
        "item_id": "127",
        "min_count": 1,
        "timeout_ms": 10000,
        "poll_ms": 100
      }
    },
    { "action": "freeze.begin", "args": {} },
    { "action": "screenshot.capture", "args": { "name": "final" } }
  ],
  "assertions": [
    {
      "type": "state",
      "expr": "state.player.secret_notes_seen contains 18",
      "message": "Secret Note #18 should be seeded as seen before digging"
    },
    {
      "type": "state",
      "expr": "state.player.mail_received contains 'SecretNote18_done'",
      "message": "Hoeing SVE's relocated Secret Note #18 tile should set the completion mail flag"
    },
    {
      "type": "state",
      "expr": "state.location.debris contains item_id '127'",
      "message": "Hoeing SVE's relocated Secret Note #18 tile should create item 127 debris"
    }
  ]
}
```

If the debris projector exposes the item as `qualified_id` rather than `item_id`, change only the wait and final assertion to use the existing debris field name proven by `13-sve-combat-lifecycle-debris.test.json`; do not change Frobby production code for SVE.

- [ ] **Step 2: Mark Slice 18 active while implementing**

Modify `SVE_FROBBY_CAPABILITY_TODO.md` entry:

```markdown
- [ ] Active: Slice 18, Hoe/dig tool-use support for buried rewards.
```

Add a note under the entry:

```markdown
  - Implementation target: `world.use_tool`, `player.add_secret_note_seen`, `state.player.secret_notes_seen`, and SVE scenario 26.
```

- [ ] **Step 3: Run scenario loader validation**

From Frobby repo:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter FullyQualifiedName~ScenarioLoaderTests -v minimal
```

Expected: PASS. This validates JSON loading broadly but not the external SVE scenario path.

From SVE repo, if it has the generated repo script:

```bash
cd /home/fintan/stardewRepos/StardewValleyExpanded
./scripts/sdv-test --headless tests/sdv/26-sve-secret-note-dig.test.json
```

Expected: initially PASS only after Tasks 1-5 are implemented. If it fails on debris field naming, inspect the report/state JSON and adjust the scenario field as allowed in Step 1.

- [ ] **Step 4: Commit**

In Frobby repo, commit the TODO update:

```bash
git add SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "Track SVE slice 18 implementation"
```

In SVE repo, commit the scenario on its current feature branch if the user has approved committing SVE changes:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded add tests/sdv/26-sve-secret-note-dig.test.json
git -C /home/fintan/stardewRepos/StardewValleyExpanded commit -m "Add Frobby Secret Note dig scenario"
```

Do not merge the SVE branch to `master` unless the user explicitly asks.

---

### Task 7: Full Verification, Completion Notes, And Final TODO Status

**Files:**
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`
- Modify: `docs/roadmap.md` and `docs/milestones/current.md` only if the slice belongs in current milestone notes.

- [ ] **Step 1: Run focused unit suites**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter "FullyQualifiedName~SecretNotePlayerStateSerializationTests|FullyQualifiedName~UseToolSerializationTests|FullyQualifiedName~PlayerStateSerializationTests" -v minimal
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~PlayerAddSecretNoteSeenHandlerTests|FullyQualifiedName~StatePlayerHandlerTests|FullyQualifiedName~WorldUseToolHandlerTests" -v minimal
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~WaitPlayerSecretNoteSeen|FullyQualifiedName~ScenarioRunnerDslTests" -v minimal
```

Expected: all focused tests pass; only pre-existing skipped tests remain skipped.

- [ ] **Step 2: Run build and diff checks**

Run:

```bash
dotnet build sdv-test-framework.slnx -v minimal
git diff --check
```

Expected: build succeeds with 0 warnings/errors, diff check clean.

- [ ] **Step 3: Run live SVE verification headlessly**

Run from SVE repo:

```bash
cd /home/fintan/stardewRepos/StardewValleyExpanded
./scripts/sdv-test --headless tests/sdv/26-sve-secret-note-dig.test.json
```

Expected: PASS. Report should show:

- `player.add_secret_note_seen` before digging;
- `world.use_tool` at `Desert` tile `9,43`;
- `state.player.mail_received` contains `SecretNote18_done`;
- `state.location.debris` contains item `127`;
- final screenshot captured under `freeze.begin`.

- [ ] **Step 4: Run a small SVE regression subset**

Run from SVE repo:

```bash
cd /home/fintan/stardewRepos/StardewValleyExpanded
./scripts/sdv-test --headless tests/sdv/18-sve-object-piggy-bank-interaction.test.json tests/sdv/19-sve-spirit-eve-chest.test.json tests/sdv/25-sve-frontier-farm-instant-unlocks.test.json tests/sdv/26-sve-secret-note-dig.test.json
```

Expected: PASS. This catches regressions in nearby object/container/progression flows.

- [ ] **Step 5: Run a Starberg smoke check**

Run from Starberg repo:

```bash
cd /home/fintan/stardewRepos/stonks
./scripts/sdv-test --headless tests/sdv/01-starberg-core-loads.test.json
```

Expected: PASS. This verifies the new shared harness/protocol payload still works with a non-SVE mod suite.

- [ ] **Step 6: Mark Slice 18 done**

Modify `SVE_FROBBY_CAPABILITY_TODO.md`:

```markdown
- [x] Done: Slice 18, Hoe/dig tool-use support for buried rewards.
  - SVE pressure: SVE relocates Secret Note #18's Desert buried reward through a Harmony patch on Stardew's buried-item check path.
  - Frobby goal: add neutral player-like tool-use support, starting with Hoe, plus secret-note seen-state setup/projection so mods can validate buried rewards without direct state shortcuts.
  - Design spec: `docs/superpowers/specs/2026-05-19-sve-slice-18-hoe-dig-tool-use-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-19-sve-slice-18-hoe-dig-tool-use.md`.
  - Done: `world.use_tool`, `player.add_secret_note_seen`, `state.player.secret_notes_seen`, runner-side `wait.player.secret_note_seen`, and SVE scenario 26 verify SVE's relocated Secret Note #18 buried reward through player-like Hoe use.
  - Follow-up candidates: add Axe/Pickaxe/Watering Can/Scythe variants as real mod scenarios require them; keep Hoe-first behavior stable.
```

- [ ] **Step 7: Commit final Frobby completion notes**

```bash
git add SVE_FROBBY_CAPABILITY_TODO.md docs/roadmap.md docs/milestones/current.md
git commit -m "Mark SVE slice 18 complete"
```

If `docs/roadmap.md` or `docs/milestones/current.md` were not changed, omit them from `git add`.

- [ ] **Step 8: Final status evidence**

Before final response, capture:

```bash
git status --short --branch
git log --oneline -5
```

Final response must report the exact verification commands run and whether any live SVE/Starberg smoke was skipped or failed.

---

## Self-Review

**Spec coverage:** Task 1 covers protocol DTOs. Task 2 covers secret-note state setup/projection. Task 3 covers Hoe-first `world.use_tool` and native `Tool.DoFunction` invocation. Task 4 covers runner waits, labels, and DSL. Task 5 covers neutral docs. Task 6 covers the SVE proof scenario. Task 7 covers focused tests, live SVE verification, Starberg smoke, and TODO completion.

**Completeness scan:** The plan has no open-ended markers or deferred-work language. The only adaptive branch is the allowed debris field-name adjustment after inspecting actual runtime projection.

**Type consistency:** The plan consistently uses `AddSecretNoteSeenRequest`, `UseToolRequest`, `UseToolResult`, `SecretNotesSeen`, `secret_notes_seen`, `secret_note_seen`, `player.add_secret_note_seen`, and `world.use_tool` across protocol, harness, runner, docs, and scenario JSON.
