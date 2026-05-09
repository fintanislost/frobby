# SVE Slice 8 Combat Monster Lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add neutral Frobby combat primitives and prove real monster damage against SVE's deterministic Crimson Badlands corrupt mummy guard.

**Architecture:** Keep Frobby content-agnostic. Runner-side waits gain numeric comparison filters for monster state, while a new `combat.attack` harness RPC performs a player-like weapon attack against a direction or tile. SVE owns the mod-specific scenario coordinates and assertions.

**Tech Stack:** C#/.NET 10, xUnit, SMAPI/Stardew Valley runtime APIs, Frobby JSON-RPC protocol, SVE `sdv-test` repo scenarios.

---

## File Structure

Frobby worktree: `/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-monster-spawn-coverage`

- Create `src/Protocol/Models/CombatAttackRequest.cs`
  - Request DTO for the `combat.attack` RPC.
- Create `src/Protocol/Models/CombatAttackResult.cs`
  - Result DTO for attack tick, player tile, resolved facing direction, and selected weapon summary.
- Create `src/Harness/Handlers/CombatAttackHandler.cs`
  - Parses/validates requests, resolves direction, selects a usable weapon, and calls the Stardew weapon use path.
- Modify `src/Harness/ModEntry.cs`
  - Registers `combat.attack` and lists it in the startup RPC surface.
- Modify `src/Runner/Scenarios/ScenarioRunner.cs`
  - Adds numeric comparison filters to `wait.location_content`.
  - Allows `min_count: 0` and `max_count: 0`.
  - Adds a small runner wrapper for `combat.attack` repeat/delay behavior and step descriptions.
- Create `src/Runner.Dsl/Combat.cs`
  - Optional C# DSL facade for `combat.attack`.
- Modify docs:
  - `README.md`
  - `docs/rpc-schema.md`
  - `docs/dsl-quickstart.md`
  - `SVE_FROBBY_CAPABILITY_TODO.md`
- Add/modify tests:
  - `tests/Runner.Tests/ScenarioRunnerTests.cs`
  - `tests/Protocol.Tests/CombatAttackSerializationTests.cs`
  - `tests/Harness.Tests/CombatAttackHandlerTests.cs`
  - `tests/Runner.Dsl.Tests/Facets/CombatTests.cs`

SVE repo: `/home/fintan/stardewRepos/StardewValleyExpanded`

- Create `tests/sdv/12-sve-combat-monster-damage.test.json`
- Modify `docs/FROBBY.md`

---

### Task 1: Extend `wait.location_content` With Monster Numeric Comparisons And Zero Count

**Files:**
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`
- Test: `tests/Runner.Tests/ScenarioRunnerTests.cs`

- [ ] **Step 1: Write the failing numeric-comparison test**

Append this test near the existing `WaitLocationContent_FiltersByMonsterNumericAndSpriteFields` tests in `tests/Runner.Tests/ScenarioRunnerTests.cs`:

```csharp
[Fact]
public async Task WaitLocationContent_MatchesMonsterNumericComparisons()
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
                    "state.location" => JsonDocument.Parse("{\"name\":\"ExampleDeepCave\",\"resource_clumps\":[],\"objects\":[],\"monsters\":[{\"tile\":{\"x\":12,\"y\":8},\"name\":\"Crystal Bat\",\"type\":\"Bat\",\"health\":125,\"max_health\":180,\"damage\":32,\"sprite_texture\":\"ExampleMod/Monsters/CrystalBat\"},{\"tile\":{\"x\":13,\"y\":8},\"name\":\"Cave Moth\",\"type\":\"Bat\",\"health\":90,\"max_health\":90,\"damage\":18,\"sprite_texture\":\"ExampleMod/Monsters/CaveMoth\"}]}").RootElement,
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
        Name = "wait_location_content_monster_comparisons",
        Steps = new()
        {
            new ScenarioStep
            {
                Action = "wait.location_content",
                Args = JsonDocument.Parse("{\"location\":\"ExampleDeepCave\",\"collection\":\"monsters\",\"type\":\"Bat\",\"health_lt\":180,\"health_gte\":100,\"max_health\":180,\"damage_gte\":30,\"damage_lte\":32,\"sprite_texture\":\"ExampleMod/Monsters/CrystalBat\",\"min_count\":1,\"max_count\":1,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
            },
        },
    }, cts.Token);

    Assert.True(report.Passed);

    cts.Cancel();
    try { await serverTask; } catch (OperationCanceledException) { }
}
```

- [ ] **Step 2: Write the failing zero-count test**

Append this second test in the same file:

```csharp
[Fact]
public async Task WaitLocationContent_AllowsZeroMatchingContent()
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
                    "state.location" => JsonDocument.Parse("{\"name\":\"ExampleDeepCave\",\"resource_clumps\":[],\"objects\":[],\"monsters\":[{\"tile\":{\"x\":13,\"y\":8},\"name\":\"Cave Moth\",\"type\":\"Bat\",\"health\":90,\"max_health\":90,\"damage\":18}]}").RootElement,
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
        Name = "wait_location_content_zero_match",
        Steps = new()
        {
            new ScenarioStep
            {
                Action = "wait.location_content",
                Args = JsonDocument.Parse("{\"location\":\"ExampleDeepCave\",\"collection\":\"monsters\",\"name\":\"Crystal Bat\",\"min_count\":0,\"max_count\":0,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
            },
        },
    }, cts.Token);

    Assert.True(report.Passed);

    cts.Cancel();
    try { await serverTask; } catch (OperationCanceledException) { }
}
```

- [ ] **Step 3: Run the two tests and verify RED**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "WaitLocationContent_MatchesMonsterNumericComparisons|WaitLocationContent_AllowsZeroMatchingContent"
```

Expected: FAIL. The first test should time out because `health_lt`, `health_gte`, `damage_gte`, and `damage_lte` are not implemented. The second test should fail validation because `min_count` and `max_count` currently require values greater than zero.

- [ ] **Step 4: Implement numeric comparison fields and zero counts**

In `src/Runner/Scenarios/ScenarioRunner.cs`, update `ValidateWaitLocationContentArgs`:

```csharp
if (args.MinCount < 0)
    throw new InvalidOperationException("wait.location_content requires args.min_count >= 0");
if (args.MaxCount is not null && args.MaxCount < 0)
    throw new InvalidOperationException("wait.location_content requires args.max_count >= 0");
```

Replace the existing exact numeric checks in `LocationContentElementMatches` with:

```csharp
&& NumericFieldMatches(element, "health", args.Health, args.HealthLt, args.HealthLte, args.HealthGt, args.HealthGte)
&& NumericFieldMatches(element, "max_health", args.MaxHealth, args.MaxHealthLt, args.MaxHealthLte, args.MaxHealthGt, args.MaxHealthGte)
&& NumericFieldMatches(element, "damage", args.Damage, args.DamageLt, args.DamageLte, args.DamageGt, args.DamageGte)
```

Replace `NumberFilterMatches` with this helper:

```csharp
private static bool NumericFieldMatches(
    JsonElement element,
    string property,
    int? exact,
    int? lt,
    int? lte,
    int? gt,
    int? gte)
{
    if (exact is null && lt is null && lte is null && gt is null && gte is null)
        return true;

    if (element.ValueKind != JsonValueKind.Object
        || !element.TryGetProperty(property, out var value)
        || value.ValueKind != JsonValueKind.Number
        || !value.TryGetInt32(out var actual))
    {
        return false;
    }

    return (exact is null || actual == exact.Value)
        && (lt is null || actual < lt.Value)
        && (lte is null || actual <= lte.Value)
        && (gt is null || actual > gt.Value)
        && (gte is null || actual >= gte.Value);
}
```

Replace the existing `health`, `max_health`, and `damage` filter label lines inside
`FormatLocationContentFilters` with:

```csharp
AddNumericFilters(filters, "health", args.Health, args.HealthLt, args.HealthLte, args.HealthGt, args.HealthGte);
AddNumericFilters(filters, "max_health", args.MaxHealth, args.MaxHealthLt, args.MaxHealthLte, args.MaxHealthGt, args.MaxHealthGte);
AddNumericFilters(filters, "damage", args.Damage, args.DamageLt, args.DamageLte, args.DamageGt, args.DamageGte);
```

Add this helper near `FormatLocationContentFilters`:

```csharp
private static void AddNumericFilters(
    List<string> filters,
    string name,
    int? exact,
    int? lt,
    int? lte,
    int? gt,
    int? gte)
{
    if (exact is not null) filters.Add($"{name}={exact}");
    if (lt is not null) filters.Add($"{name}_lt={lt}");
    if (lte is not null) filters.Add($"{name}_lte={lte}");
    if (gt is not null) filters.Add($"{name}_gt={gt}");
    if (gte is not null) filters.Add($"{name}_gte={gte}");
}
```

Add properties to `WaitLocationContentStepArgs`:

```csharp
public int? HealthLt { get; set; }
public int? HealthLte { get; set; }
public int? HealthGt { get; set; }
public int? HealthGte { get; set; }
public int? MaxHealthLt { get; set; }
public int? MaxHealthLte { get; set; }
public int? MaxHealthGt { get; set; }
public int? MaxHealthGte { get; set; }
public int? DamageLt { get; set; }
public int? DamageLte { get; set; }
public int? DamageGt { get; set; }
public int? DamageGte { get; set; }
```

- [ ] **Step 5: Run the tests and verify GREEN**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "WaitLocationContent_MatchesMonsterNumericComparisons|WaitLocationContent_AllowsZeroMatchingContent|WaitLocationContent_FiltersByMonsterNumericAndSpriteFields|WaitLocationContent_NonNumberMonsterMetadataIsNonMatch"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Runner/Scenarios/ScenarioRunner.cs tests/Runner.Tests/ScenarioRunnerTests.cs
git commit -m "feat: extend location content numeric waits"
```

---

### Task 2: Add Combat Attack Protocol Models

**Files:**
- Create: `src/Protocol/Models/CombatAttackRequest.cs`
- Create: `src/Protocol/Models/CombatAttackResult.cs`
- Test: `tests/Protocol.Tests/CombatAttackSerializationTests.cs`

- [ ] **Step 1: Write the failing serialization tests**

Create `tests/Protocol.Tests/CombatAttackSerializationTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class CombatAttackSerializationTests
{
    [Fact]
    public void CombatAttackRequest_SerializesSnakeCaseFields()
    {
        var req = new CombatAttackRequest
        {
            X = 20,
            Y = 144,
            Direction = "up",
            Repeat = 2,
            DelayTicks = 6,
            QualifiedItemId = "(W)4",
        };

        var json = JsonSerializer.Serialize(req, ProtocolJson.Options);

        Assert.Contains("\"x\":20", json);
        Assert.Contains("\"y\":144", json);
        Assert.Contains("\"direction\":\"up\"", json);
        Assert.Contains("\"repeat\":2", json);
        Assert.Contains("\"delay_ticks\":6", json);
        Assert.Contains("\"qualified_item_id\":\"(W)4\"", json);
    }

    [Fact]
    public void CombatAttackResult_SerializesSnakeCaseFields()
    {
        var result = new CombatAttackResult
        {
            Ok = true,
            Tick = 123,
            Tile = new TilePoint { X = 20, Y = 145 },
            Direction = "up",
            SelectedItemId = "4",
            SelectedItemQualifiedId = "(W)4",
            SelectedItemName = "Galaxy Sword",
            SelectedItemRuntimeType = "MeleeWeapon",
        };

        var json = JsonSerializer.Serialize(result, ProtocolJson.Options);

        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"tick\":123", json);
        Assert.Contains("\"tile\":{\"x\":20,\"y\":145}", json);
        Assert.Contains("\"direction\":\"up\"", json);
        Assert.Contains("\"selected_item_id\":\"4\"", json);
        Assert.Contains("\"selected_item_qualified_id\":\"(W)4\"", json);
        Assert.Contains("\"selected_item_name\":\"Galaxy Sword\"", json);
        Assert.Contains("\"selected_item_runtime_type\":\"MeleeWeapon\"", json);
    }
}
```

- [ ] **Step 2: Run the tests and verify RED**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter CombatAttackSerializationTests
```

Expected: FAIL because `CombatAttackRequest` and `CombatAttackResult` do not exist.

- [ ] **Step 3: Add the request DTO**

Create `src/Protocol/Models/CombatAttackRequest.cs`:

```csharp
namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape of <c>combat.attack</c>.</summary>
public sealed class CombatAttackRequest
{
    /// <summary>Optional target tile X coordinate in the current location.</summary>
    public int? X { get; set; }

    /// <summary>Optional target tile Y coordinate in the current location.</summary>
    public int? Y { get; set; }

    /// <summary>Optional attack direction: up, right, down, or left.</summary>
    public string? Direction { get; set; }

    /// <summary>Number of attack calls to issue. Runner may space calls using <see cref="DelayTicks"/>.</summary>
    public int Repeat { get; set; } = 1;

    /// <summary>Approximate game ticks to wait between repeated attacks at the runner layer.</summary>
    public int DelayTicks { get; set; }

    /// <summary>Optional qualified weapon id to select before attacking, such as <c>(W)4</c>.</summary>
    public string? QualifiedItemId { get; set; }
}
```

- [ ] **Step 4: Add the result DTO**

Create `src/Protocol/Models/CombatAttackResult.cs`:

```csharp
namespace SdvTestFramework.Protocol.Models;

/// <summary>Result shape of <c>combat.attack</c>.</summary>
public sealed class CombatAttackResult
{
    public bool Ok { get; set; } = true;
    public int Tick { get; set; }
    public TilePoint Tile { get; set; } = new();
    public string Direction { get; set; } = string.Empty;
    public string? SelectedItemId { get; set; }
    public string? SelectedItemQualifiedId { get; set; }
    public string? SelectedItemName { get; set; }
    public string? SelectedItemRuntimeType { get; set; }
}
```

- [ ] **Step 5: Run the tests and verify GREEN**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter CombatAttackSerializationTests
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Protocol/Models/CombatAttackRequest.cs src/Protocol/Models/CombatAttackResult.cs tests/Protocol.Tests/CombatAttackSerializationTests.cs
git commit -m "feat: add combat attack protocol models"
```

---

### Task 3: Add The Harness `combat.attack` Handler

**Files:**
- Create: `src/Harness/Handlers/CombatAttackHandler.cs`
- Modify: `src/Harness/ModEntry.cs`
- Test: `tests/Harness.Tests/CombatAttackHandlerTests.cs`

- [ ] **Step 1: Write failing handler validation and direction tests**

Create `tests/Harness.Tests/CombatAttackHandlerTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class CombatAttackHandlerTests
{
    [Fact]
    public void Handle_MissingDirectionAndTarget_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => CombatAttackHandler.Handle(p, new FakeCombatWorld()));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("direction or target tile", ex.Message);
    }

    [Fact]
    public void Handle_PartialTargetTile_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"x\":20}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => CombatAttackHandler.Handle(p, new FakeCombatWorld()));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("both x and y", ex.Message);
    }

    [Fact]
    public void Handle_UnknownDirection_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"direction\":\"northish\"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => CombatAttackHandler.Handle(p, new FakeCombatWorld()));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("unknown direction", ex.Message);
    }

    [Fact]
    public void Handle_NotWorldReady_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"direction\":\"up\"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => CombatAttackHandler.Handle(p, new FakeCombatWorld { IsWorldReady = false }));
        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_TargetTileFacesCardinalDirectionAndAttacks()
    {
        var world = new FakeCombatWorld { TileX = 20, TileY = 145 };
        var p = JsonDocument.Parse("{\"x\":20,\"y\":144,\"qualified_item_id\":\"(W)4\"}").RootElement;

        var result = CombatAttackHandler.Handle(p, world);
        var json = result.GetRawText();

        Assert.Equal("up", world.FacedDirection);
        Assert.Equal(1, world.AttackCount);
        Assert.Equal("(W)4", world.SelectedQualifiedItemId);
        Assert.Contains("\"direction\":\"up\"", json);
        Assert.Contains("\"selected_item_qualified_id\":\"(W)4\"", json);
    }

    [Fact]
    public void Handle_DirectionRepeatsAttack()
    {
        var world = new FakeCombatWorld();
        var p = JsonDocument.Parse("{\"direction\":\"left\",\"repeat\":3}").RootElement;

        CombatAttackHandler.Handle(p, world);

        Assert.Equal("left", world.FacedDirection);
        Assert.Equal(3, world.AttackCount);
    }

    private sealed class FakeCombatWorld : ICombatAttackWorld
    {
        public bool IsWorldReady { get; set; } = true;
        public int Tick { get; set; } = 456;
        public int TileX { get; set; } = 20;
        public int TileY { get; set; } = 145;
        public string? SelectedQualifiedItemId { get; private set; }
        public string? FacedDirection { get; private set; }
        public int AttackCount { get; private set; }

        public CombatAttackSelectedItem SelectWeapon(string? qualifiedItemId)
        {
            SelectedQualifiedItemId = qualifiedItemId ?? "(W)4";
            return new CombatAttackSelectedItem("4", SelectedQualifiedItemId, "Galaxy Sword", "MeleeWeapon");
        }

        public void FaceDirection(string direction) => FacedDirection = direction;
        public void AttackOnce() => AttackCount++;
    }
}
```

- [ ] **Step 2: Run the tests and verify RED**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter CombatAttackHandlerTests
```

Expected: FAIL because `CombatAttackHandler`, `ICombatAttackWorld`, and `CombatAttackSelectedItem` do not exist.

- [ ] **Step 3: Implement `CombatAttackHandler`**

Create `src/Harness/Handlers/CombatAttackHandler.cs`:

```csharp
using System;
using System.Linq;
using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using StardewValley.Tools;

namespace SdvTestFramework.Harness.Handlers;

public static class CombatAttackHandler
{
    public const string Method = "combat.attack";

    private static readonly ICombatAttackWorld ProductionWorld = new SdvCombatAttackWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, ICombatAttackWorld world)
    {
        var req = RpcParams.Required<CombatAttackRequest>(paramsElement);
        ValidateRequest(req);

        if (!world.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "no active save — combat.attack requires a loaded world");

        var direction = ResolveDirection(req, world.TileX, world.TileY);
        var selected = world.SelectWeapon(req.QualifiedItemId);
        for (int i = 0; i < req.Repeat; i++)
        {
            world.FaceDirection(direction);
            world.AttackOnce();
        }

        return ProtocolJson.ToElement(new CombatAttackResult
        {
            Ok = true,
            Tick = world.Tick,
            Tile = new TilePoint { X = world.TileX, Y = world.TileY },
            Direction = direction,
            SelectedItemId = selected.ItemId,
            SelectedItemQualifiedId = selected.QualifiedItemId,
            SelectedItemName = selected.Name,
            SelectedItemRuntimeType = selected.RuntimeType,
        });
    }

    private static void ValidateRequest(CombatAttackRequest req)
    {
        if ((req.X is null) != (req.Y is null))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "combat.attack requires both x and y when targeting a tile");
        if (req.X is null && string.IsNullOrWhiteSpace(req.Direction))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "combat.attack requires a direction or target tile");
        if (req.Repeat < 1)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "combat.attack requires repeat >= 1");
        if (req.DelayTicks < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "combat.attack requires delay_ticks >= 0");
        if (!string.IsNullOrWhiteSpace(req.Direction) && !IsKnownDirection(req.Direction))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                $"unknown direction: {req.Direction}");
    }

    private static string ResolveDirection(CombatAttackRequest req, int playerX, int playerY)
    {
        if (!string.IsNullOrWhiteSpace(req.Direction))
            return NormalizeDirection(req.Direction);

        var dx = req.X!.Value - playerX;
        var dy = req.Y!.Value - playerY;
        if (dx == 0 && dy == 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "combat.attack target tile must differ from the player tile");

        if (Math.Abs(dx) > Math.Abs(dy))
            return dx > 0 ? "right" : "left";

        return dy > 0 ? "down" : "up";
    }

    private static bool IsKnownDirection(string direction)
        => NormalizeDirection(direction) is "up" or "right" or "down" or "left";

    private static string NormalizeDirection(string direction)
        => direction.Trim().ToLowerInvariant();
}

internal interface ICombatAttackWorld
{
    bool IsWorldReady { get; }
    int Tick { get; }
    int TileX { get; }
    int TileY { get; }
    CombatAttackSelectedItem SelectWeapon(string? qualifiedItemId);
    void FaceDirection(string direction);
    void AttackOnce();
}

internal sealed record CombatAttackSelectedItem(
    string? ItemId,
    string? QualifiedItemId,
    string? Name,
    string? RuntimeType);

internal sealed class SdvCombatAttackWorld : ICombatAttackWorld
{
    public bool IsWorldReady => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;
    public int Tick => Game1.ticks;
    public int TileX => Game1.player.TilePoint.X;
    public int TileY => Game1.player.TilePoint.Y;

    public CombatAttackSelectedItem SelectWeapon(string? qualifiedItemId)
    {
        var player = Game1.player;
        if (player.CurrentTool is MeleeWeapon current
            && (string.IsNullOrWhiteSpace(qualifiedItemId)
                || string.Equals(current.QualifiedItemId, qualifiedItemId, StringComparison.Ordinal)))
        {
            return SummarizeWeapon(current);
        }

        for (int slot = 0; slot < player.Items.Count; slot++)
        {
            if (player.Items[slot] is not MeleeWeapon weapon)
                continue;
            if (!string.IsNullOrWhiteSpace(qualifiedItemId)
                && !string.Equals(weapon.QualifiedItemId, qualifiedItemId, StringComparison.Ordinal))
                continue;

            player.CurrentToolIndex = slot;
            return SummarizeWeapon(weapon);
        }

        var message = string.IsNullOrWhiteSpace(qualifiedItemId)
            ? "combat.attack requires a melee weapon in the farmer inventory"
            : $"combat.attack could not find melee weapon {qualifiedItemId} in the farmer inventory";
        throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, message);
    }

    public void FaceDirection(string direction)
    {
        Game1.player.faceDirection(DirectionToStardew(direction));
    }

    public void AttackOnce()
    {
        if (Game1.currentLocation is null)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "combat.attack requires a current location");
        if (Game1.player.CurrentTool is not MeleeWeapon weapon)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "combat.attack requires a selected melee weapon");

        var toolLocation = Game1.player.GetToolLocation();
        weapon.DoFunction(Game1.currentLocation, (int)toolLocation.X, (int)toolLocation.Y, 0, Game1.player);
    }

    private static CombatAttackSelectedItem SummarizeWeapon(MeleeWeapon weapon)
        => new(
            weapon.ItemId,
            weapon.QualifiedItemId,
            weapon.DisplayName ?? weapon.Name,
            weapon.GetType().Name);

    private static int DirectionToStardew(string direction)
        => direction switch
        {
            "up" => 0,
            "right" => 1,
            "down" => 2,
            "left" => 3,
            _ => throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, $"unknown direction: {direction}"),
        };
}
```

- [ ] **Step 4: Register the RPC**

In `src/Harness/ModEntry.cs`, add registration after the world interaction handlers:

```csharp
_rpc.Register(CombatAttackHandler.Method, p => CombatAttackHandler.Handle(p));
```

Update the startup log string to include `combat.attack` after the `world.*` entries:

```text
Combat: combat.attack.
```

- [ ] **Step 5: Run the handler tests and verify GREEN**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter CombatAttackHandlerTests
```

Expected: PASS.

- [ ] **Step 6: Build the harness**

Run:

```bash
dotnet build src/Harness/Harness.csproj
```

Expected: Build succeeds. If `MeleeWeapon.QualifiedItemId` or `Farmer.CurrentToolIndex` differs in this SDV target, adjust only `SdvCombatAttackWorld` to use the available equivalent property while keeping the handler interface and tests unchanged.

- [ ] **Step 7: Commit**

```bash
git add src/Harness/Handlers/CombatAttackHandler.cs src/Harness/ModEntry.cs tests/Harness.Tests/CombatAttackHandlerTests.cs
git commit -m "feat: add combat attack harness action"
```

---

### Task 4: Add Runner Repeat/Delay Routing And DSL Facade

**Files:**
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`
- Create: `src/Runner.Dsl/Combat.cs`
- Test: `tests/Runner.Tests/ScenarioRunnerTests.cs`
- Test: `tests/Runner.Dsl.Tests/Facets/CombatTests.cs`

- [ ] **Step 1: Write the failing runner repeat test**

Append this test in `tests/Runner.Tests/ScenarioRunnerTests.cs`:

```csharp
[Fact]
public async Task CombatAttack_RepeatsAndStripsRunnerOnlyDelay()
{
    var socket = SocketPath();
    var calls = new List<JsonElement>();
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

    var serverTask = Task.Run(async () =>
    {
        await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
        {
            session.RequestReceived += async req =>
            {
                if (req.Method == "combat.attack" && req.Params is { } attack)
                    calls.Add(attack);

                JsonElement r = req.Method switch
                {
                    "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                    "combat.attack" => JsonDocument.Parse("{\"ok\":true,\"tick\":1,\"tile\":{\"x\":20,\"y\":145},\"direction\":\"up\",\"selected_item_qualified_id\":\"(W)4\"}").RootElement,
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
        Name = "combat_attack_repeat",
        Steps = new()
        {
            new ScenarioStep
            {
                Action = "combat.attack",
                Args = JsonDocument.Parse("{\"x\":20,\"y\":144,\"repeat\":2,\"delay_ticks\":1,\"qualified_item_id\":\"(W)4\"}").RootElement,
            },
        },
    }, cts.Token);

    Assert.True(report.Passed);
    Assert.Equal(2, calls.Count);
    Assert.All(calls, call =>
    {
        Assert.Equal(20, call.GetProperty("x").GetInt32());
        Assert.Equal(144, call.GetProperty("y").GetInt32());
        Assert.Equal("(W)4", call.GetProperty("qualified_item_id").GetString());
        Assert.False(call.TryGetProperty("repeat", out _));
        Assert.False(call.TryGetProperty("delay_ticks", out _));
    });

    cts.Cancel();
    try { await serverTask; } catch (OperationCanceledException) { }
}
```

- [ ] **Step 2: Run the runner test and verify RED**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter CombatAttack_RepeatsAndStripsRunnerOnlyDelay
```

Expected: FAIL because the default scenario path invokes `combat.attack` once and passes `repeat` / `delay_ticks` through.

- [ ] **Step 3: Implement runner repeat/delay routing**

In `ScenarioRunner.RunAsync`, add this branch before the default RPC invocation:

```csharp
else if (step.Action == "combat.attack")
{
    await InvokeCombatAttackAsync(step, ct);
}
```

Add this helper near the other `Invoke*` helpers:

```csharp
private async Task InvokeCombatAttackAsync(ScenarioStep step, CancellationToken ct)
{
    var args = step.Args is { ValueKind: JsonValueKind.Object } obj
        ? JsonSerializer.Deserialize<CombatAttackRequest>(obj.GetRawText(), ProtocolJson.Options) ?? new CombatAttackRequest()
        : new CombatAttackRequest();

    if (args.Repeat < 1)
        throw new InvalidOperationException("combat.attack requires args.repeat >= 1");
    if (args.DelayTicks < 0)
        throw new InvalidOperationException("combat.attack requires args.delay_ticks >= 0");

    var singleAttack = new JsonObject();
    if (args.X is not null) singleAttack["x"] = args.X.Value;
    if (args.Y is not null) singleAttack["y"] = args.Y.Value;
    if (!string.IsNullOrWhiteSpace(args.Direction)) singleAttack["direction"] = args.Direction;
    if (!string.IsNullOrWhiteSpace(args.QualifiedItemId)) singleAttack["qualified_item_id"] = args.QualifiedItemId;
    var singleAttackElement = JsonDocument.Parse(singleAttack.ToJsonString()).RootElement;

    for (int i = 0; i < args.Repeat; i++)
    {
        var resp = await _session.InvokeAsync("combat.attack", singleAttackElement, ct);
        if (resp.Error is { } ex)
            throw new InvalidOperationException($"step '{step.Action}' failed: {ex.Message}");

        if (i + 1 < args.Repeat && args.DelayTicks > 0)
            await Task.Delay(args.DelayTicks * 17, ct);
    }
}
```

Add this using at the top of `ScenarioRunner.cs`:

```csharp
using System.Text.Json.Nodes;
```

In `DescribeStep`, add:

```csharp
"combat.attack" => GetStringArg(step.Args, "direction") is { } direction
    ? $"Attack {direction}{GetRepeatSuffix(step.Args)}"
    : $"Attack tile ({GetIntArg(step.Args, "x") ?? 0},{GetIntArg(step.Args, "y") ?? 0}){GetRepeatSuffix(step.Args)}",
```

- [ ] **Step 4: Run the runner test and verify GREEN**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter CombatAttack_RepeatsAndStripsRunnerOnlyDelay
```

Expected: PASS.

- [ ] **Step 5: Write the failing DSL test**

Create `tests/Runner.Dsl.Tests/Facets/CombatTests.cs`:

```csharp
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Dsl;
using Xunit;

namespace SdvTestFramework.Runner.Dsl.Tests.Facets;

public class CombatTests
{
    private sealed class CapturingInvoker : ISdvTestInvoker
    {
        public string LastMethod { get; private set; } = string.Empty;
        public JsonElement? LastParams { get; private set; }

        public Task<JsonElement> InvokeAsync(string method, JsonElement? @params, CancellationToken ct)
        {
            LastMethod = method;
            LastParams = @params?.Clone();
            return Task.FromResult(JsonDocument.Parse("{\"ok\":true,\"tick\":42}").RootElement);
        }
    }

    [Fact]
    public async Task AttackTile_InvokesCombatAttack()
    {
        SdvTestSession.ResetForTests();
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try
        {
            await Combat.AttackTile(20, 144, qualifiedItemId: "(W)4");
        }
        finally
        {
            SdvTestSession.ResetForTests();
        }

        Assert.Equal("combat.attack", inv.LastMethod);
        var args = inv.LastParams!.Value;
        Assert.Equal(20, args.GetProperty("x").GetInt32());
        Assert.Equal(144, args.GetProperty("y").GetInt32());
        Assert.Equal("(W)4", args.GetProperty("qualified_item_id").GetString());
    }

    [Fact]
    public async Task AttackDirection_InvokesCombatAttack()
    {
        SdvTestSession.ResetForTests();
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try
        {
            await Combat.AttackDirection("up", repeat: 2, delayTicks: 1);
        }
        finally
        {
            SdvTestSession.ResetForTests();
        }

        Assert.Equal("combat.attack", inv.LastMethod);
        var args = inv.LastParams!.Value;
        Assert.Equal("up", args.GetProperty("direction").GetString());
        Assert.Equal(2, args.GetProperty("repeat").GetInt32());
        Assert.Equal(1, args.GetProperty("delay_ticks").GetInt32());
    }
}
```

- [ ] **Step 6: Run the DSL test and verify RED**

Run:

```bash
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter CombatTests
```

Expected: FAIL because `Combat` does not exist.

- [ ] **Step 7: Add the DSL facade**

Create `src/Runner.Dsl/Combat.cs`:

```csharp
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>Ambient static DSL for the <c>combat.*</c> RPC surface.</summary>
public static class Combat
{
    public static async Task AttackTile(
        int x,
        int y,
        string? qualifiedItemId = null,
        int repeat = 1,
        int delayTicks = 0,
        CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new CombatAttackRequest
        {
            X = x,
            Y = y,
            QualifiedItemId = qualifiedItemId,
            Repeat = repeat,
            DelayTicks = delayTicks,
        }, ProtocolJson.Options);
        await s.InvokeAsync("combat.attack", p, ct);
    }

    public static async Task AttackDirection(
        string direction,
        string? qualifiedItemId = null,
        int repeat = 1,
        int delayTicks = 0,
        CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new CombatAttackRequest
        {
            Direction = direction,
            QualifiedItemId = qualifiedItemId,
            Repeat = repeat,
            DelayTicks = delayTicks,
        }, ProtocolJson.Options);
        await s.InvokeAsync("combat.attack", p, ct);
    }
}
```

- [ ] **Step 8: Run the runner and DSL tests and verify GREEN**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter CombatAttack_RepeatsAndStripsRunnerOnlyDelay
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter CombatTests
```

Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/Runner/Scenarios/ScenarioRunner.cs src/Runner.Dsl/Combat.cs tests/Runner.Tests/ScenarioRunnerTests.cs tests/Runner.Dsl.Tests/Facets/CombatTests.cs
git commit -m "feat: route combat attack scenarios"
```

---

### Task 5: Document Combat And Wait Filter Capabilities

**Files:**
- Modify: `README.md`
- Modify: `docs/rpc-schema.md`
- Modify: `docs/dsl-quickstart.md`
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Update the README**

In `README.md`, add this bullet near the existing world-content guidance:

```markdown
- Use `combat.attack` with `state.location.monsters` and `wait.location_content`
  numeric filters for player-like combat checks. Prefer health-delta waits such as
  `health_lt` over fixed sleeps, and keep mod-specific monster coordinates in the
  repo scenario rather than in Frobby code.
```

- [ ] **Step 2: Update the RPC schema**

In `docs/rpc-schema.md`, add a `combat.attack` section near the world/input actions:

```markdown
### combat.attack

Player-like combat action. The harness faces the farmer toward a direction or target
tile, selects the requested melee weapon when `qualified_item_id` is provided, and
invokes Stardew's weapon use path. The runner accepts `repeat` and `delay_ticks` and
spaces repeated RPC calls outside the game thread.

```json
→ { "jsonrpc": "2.0", "id": 42, "method": "combat.attack", "params": { "x": 20, "y": 144, "qualified_item_id": "(W)4" } }
← { "jsonrpc": "2.0", "id": 42, "result": { "ok": true, "tick": 123, "tile": { "x": 20, "y": 145 }, "direction": "up", "selected_item_qualified_id": "(W)4", "selected_item_runtime_type": "MeleeWeapon" } }
```

Use either `x`/`y` or `direction`. If both are supplied, `direction` wins. Supported
directions are `up`, `right`, `down`, and `left`.
```

Update the runner-only `wait.location_content` bullet to mention:

```markdown
Monster numeric comparisons are supported with `health_lt`, `health_lte`,
`health_gt`, `health_gte`, `max_health_*`, and `damage_*`. `min_count: 0` with
`max_count: 0` can wait for no matching content.
```

- [ ] **Step 3: Update the DSL quickstart**

Add this short example to `docs/dsl-quickstart.md`:

```markdown
```json
{
  "action": "combat.attack",
  "args": {
    "x": 20,
    "y": 144,
    "qualified_item_id": "(W)4"
  }
}
```

Then wait for the damage instead of sleeping:

```json
{
  "action": "wait.location_content",
  "args": {
    "location": "ExampleDeepCave",
    "collection": "monsters",
    "x": 20,
    "y": 144,
    "health_lt": 2000,
    "min_count": 1
  }
}
```
```

- [ ] **Step 4: Update the SVE capability todo**

In `SVE_FROBBY_CAPABILITY_TODO.md`, add Slice 8 after Slice 7:

```markdown
- [ ] Active: Slice 8, combat, monster lifecycle, drops, and hazards.
  - Design spec: `docs/superpowers/specs/2026-05-09-sve-slice-8-combat-monster-lifecycle-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-09-sve-slice-8-combat-monster-lifecycle.md`.
  - SVE pressure: deterministic custom-location monster spawns plus combat/damage state.
  - Frobby goal: player-like attack action, health-delta waits, zero-match waits, and a path toward later death/drop/hazard checks.
```

- [ ] **Step 5: Run a docs smoke check**

Run:

```bash
rg -n "combat.attack|health_lt|min_count: 0|Slice 8" README.md docs/rpc-schema.md docs/dsl-quickstart.md SVE_FROBBY_CAPABILITY_TODO.md
```

Expected: each file has at least one relevant hit and no command failure.

- [ ] **Step 6: Commit**

```bash
git add README.md docs/rpc-schema.md docs/dsl-quickstart.md SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "docs: document combat testing primitives"
```

---

### Task 6: Add The SVE Combat Damage Scenario

**Files:**
- Create: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/12-sve-combat-monster-damage.test.json`
- Modify: `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`

- [ ] **Step 1: Add the SVE scenario**

Create `tests/sdv/12-sve-combat-monster-damage.test.json` in the SVE repo:

```json
{
  "name": "sve_combat_monster_damage",
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
      "args": { "id": "(W)4", "count": 1 }
    },
    {
      "action": "player.warp",
      "args": { "location": "Custom_CrimsonBadlands", "x": 20, "y": 145 }
    },
    {
      "action": "wait.location",
      "args": {
        "location": "Custom_CrimsonBadlands",
        "x": 20,
        "y": 145,
        "timeout_ms": 10000,
        "poll_ms": 100
      }
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
      "action": "combat.attack",
      "args": {
        "x": 20,
        "y": 144,
        "qualified_item_id": "(W)4",
        "repeat": 1,
        "delay_ticks": 0
      }
    },
    {
      "action": "wait.location_content",
      "args": {
        "location": "Custom_CrimsonBadlands",
        "collection": "monsters",
        "x": 20,
        "y": 144,
        "health_lt": 2000,
        "max_health": 2000,
        "damage": 100,
        "sprite_texture": "Characters/Monsters/CorruptMummy",
        "min_count": 1,
        "timeout_ms": 10000,
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
      "expr": "state.player.location == 'Custom_CrimsonBadlands'",
      "message": "Combat damage scenario should finish in the Crimson Badlands"
    }
  ]
}
```

- [ ] **Step 2: Update SVE docs**

Add this paragraph to `docs/FROBBY.md` after scenario 10:

```markdown
Scenario `tests/sdv/12-sve-combat-monster-damage.test.json` extends the Crimson
Badlands monster coverage into player-like combat. It gives the farmer a vanilla
weapon, waits for the deterministic corrupt mummy guard, runs Frobby's neutral
`combat.attack` action toward the guard tile, and waits for the same monster's
health to drop below its spawned max health.
```

Add an example command near the existing scenario commands:

```sh
FROBBY_ROOT=/path/to/sdv-test-framework scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-8-combat tests/sdv/12-sve-combat-monster-damage.test.json
```

- [ ] **Step 3: Run SVE scenario 12 and verify GREEN**

From the SVE repo, run with the Frobby feature worktree:

```bash
env SDV_MODS_PATH=/tmp/sve-frobby-mods-advance SDV_TEST_MOD_CACHE=/home/fintan/stardewRepos/frobby/sdv-test-framework/.cache/deps FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-monster-spawn-coverage dotnet /home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-monster-spawn-coverage/src/Runner/bin/Debug/net10.0/sdv-test.dll repo run --repo-root /home/fintan/stardewRepos/StardewValleyExpanded --headless --mod-set core --no-build --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-8-combat tests/sdv/12-sve-combat-monster-damage.test.json
```

Expected: PASS. If the live run proves tile `20,145` is blocked or not close enough for attack, change only the SVE scenario player warp/wait tile to `20,146` and set `combat.attack.direction` to `"up"` while keeping the monster target and Frobby code unchanged.

- [ ] **Step 4: Re-run scenario 10 to protect existing monster observation**

Run:

```bash
env SDV_MODS_PATH=/tmp/sve-frobby-mods-advance SDV_TEST_MOD_CACHE=/home/fintan/stardewRepos/frobby/sdv-test-framework/.cache/deps FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-monster-spawn-coverage dotnet /home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-monster-spawn-coverage/src/Runner/bin/Debug/net10.0/sdv-test.dll repo run --repo-root /home/fintan/stardewRepos/StardewValleyExpanded --headless --mod-set core --no-build --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-8-combat-regression tests/sdv/10-sve-ftm-monster-spawn-config.test.json
```

Expected: PASS.

- [ ] **Step 5: Commit SVE scenario work**

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded add tests/sdv/12-sve-combat-monster-damage.test.json docs/FROBBY.md
git -C /home/fintan/stardewRepos/StardewValleyExpanded commit -m "test: cover SVE monster combat damage"
```

---

### Task 7: Final Verification And Frobby Commit

**Files:**
- Verify all Frobby files changed in Tasks 1-5.
- Verify SVE scenario from Task 6 remains committed on the SVE feature branch.

- [ ] **Step 1: Run targeted Frobby unit suites**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter "CombatAttackSerializationTests|LocationStateSerializationTests"
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "CombatAttackHandlerTests|LocationContentProjectorTests"
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "WaitLocationContent_MatchesMonsterNumericComparisons|WaitLocationContent_AllowsZeroMatchingContent|CombatAttack_RepeatsAndStripsRunnerOnlyDelay|WaitLocationContent_FiltersByMonsterNumericAndSpriteFields|WaitLocationContent_NonNumberMonsterMetadataIsNonMatch"
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter CombatTests
```

Expected: PASS for all four commands.

- [ ] **Step 2: Build the runner**

Run:

```bash
dotnet build src/Runner/Runner.csproj
```

Expected: Build succeeds with zero errors.

- [ ] **Step 3: Run live SVE combat and monster-regression scenarios**

Run:

```bash
env SDV_MODS_PATH=/tmp/sve-frobby-mods-advance SDV_TEST_MOD_CACHE=/home/fintan/stardewRepos/frobby/sdv-test-framework/.cache/deps FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-monster-spawn-coverage dotnet /home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-monster-spawn-coverage/src/Runner/bin/Debug/net10.0/sdv-test.dll repo run --repo-root /home/fintan/stardewRepos/StardewValleyExpanded --headless --mod-set core --no-build --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-8-final tests/sdv/10-sve-ftm-monster-spawn-config.test.json tests/sdv/12-sve-combat-monster-damage.test.json
```

Expected: `2/2 passed`.

- [ ] **Step 4: Mark Slice 8 done in the Frobby capability todo**

In `SVE_FROBBY_CAPABILITY_TODO.md`, change:

```markdown
- [ ] Active: Slice 8, combat, monster lifecycle, drops, and hazards.
```

to:

```markdown
- [x] Done: Slice 8, combat, monster lifecycle, drops, and hazards.
```

Append:

```markdown
  - Done: `combat.attack`, monster numeric wait filters, zero-match waits, and SVE scenario 12 proving player-like damage against the Crimson Badlands corrupt mummy guard.
  - Pending Slice 8 follow-up: deterministic death/removal, dropped object validation, and player hazard damage once a low-flake target is selected.
```

- [ ] **Step 5: Commit final Frobby docs/todo state**

```bash
git add SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "docs: complete SVE combat slice"
```

- [ ] **Step 6: Report final status**

Include:
- Frobby commit hashes created during the plan.
- SVE commit hash for scenario 12.
- Unit test command results.
- Live headless report directory:
  `/tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-8-final`

Do not merge SVE to master. Only merge Frobby to main when the user explicitly approves that integration step.

---

## Self-Review Notes

**Spec coverage:** Task 1 covers health-delta and zero-count waits. Tasks 2-4 cover the neutral `combat.attack` protocol, harness action, runner behavior, and DSL. Task 5 covers Frobby docs. Task 6 covers SVE scenario 12. Task 7 covers final verification and marks the slice complete.

**Scope:** Death/removal, drops, and player hazard damage are intentionally recorded as Slice 8 follow-ups. The first pass proves player-like damage only, matching the approved design.

**Type consistency:** The plan consistently uses `CombatAttackRequest`, `CombatAttackResult`, `combat.attack`, `qualified_item_id`, `health_lt`, `health_lte`, `health_gt`, `health_gte`, `max_health_*`, and `damage_*` across tests, implementation, docs, and scenario JSON.
