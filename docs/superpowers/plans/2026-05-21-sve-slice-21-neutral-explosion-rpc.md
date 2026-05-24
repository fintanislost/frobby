# SVE Slice 21 Neutral Explosion RPC Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a neutral `world.explode_tile` RPC/action that triggers Stardew-native explosion behavior at a specific tile and proves it against an SVE mod-spawned mummy in the Combat Lab.

**Architecture:** Add protocol DTOs, a small harness handler with an injectable `IExplodeTileWorld`, and an SDV implementation that resolves a loaded location and invokes the native game explosion path. Keep runner support thin because unknown JSON scenario actions already pass through as RPCs; only add readable reports and DSL docs around the new action. Prove the capability in SVE by relocating a real mod-spawned mummy into `Frobby_CombatLab`, damaging it through existing combat, then finishing/removing it through `world.explode_tile`.

**Tech Stack:** C#/.NET 10 runner and DSL, net6.0 SMAPI harness, Stardew Valley 1.6 runtime types, JSON-RPC protocol DTOs, xUnit, JSON scenario files, headless `sdv-test` repo runs.

---

## File Structure

Frobby protocol:

- Create `src/Protocol/Models/ExplodeTileRequest.cs`
  - `ExplodeTileRequest` and `ExplodeTileResult` DTOs for `world.explode_tile`.
- Create `tests/Protocol.Tests/ExplodeTileSerializationTests.cs`
  - Snake-case request/result serialization coverage.

Frobby harness:

- Create `src/Harness/Handlers/WorldExplodeTileHandler.cs`
  - Validate request shape, world readiness, location, map bounds, and radius safety.
  - Invoke an injectable world adapter.
- Create `tests/Harness.Tests/WorldExplodeTileHandlerTests.cs`
  - TDD coverage for validation and a successful invocation.
- Modify `src/Harness/ModEntry.cs`
  - Register `world.explode_tile`.
  - Add it to the startup RPC method list.

Frobby runner and DSL:

- Modify `src/Runner/Scenarios/ScenarioRunner.cs`
  - Add a readable step label for `world.explode_tile`.
- Modify `tests/Runner.Tests/ScenarioRunnerTests.cs`
  - Add pass-through/report-label coverage.
- Modify `src/Runner.Dsl/World.cs`
  - Add `World.ExplodeTile(...)`.
- Modify `tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs`
  - Add DSL invocation/result coverage.

Frobby docs/status:

- Modify `docs/rpc-schema.md`
- Modify `docs/dsl-quickstart.md`
- Modify `docs/wiki/examples.md`
- Modify `SVE_FROBBY_CAPABILITY_TODO.md`

SVE:

- Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/29-sve-combat-lab-explode-mummy.test.json`
- Modify `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`

## Task 1: Protocol DTOs For `world.explode_tile`

**Files:**
- Create: `tests/Protocol.Tests/ExplodeTileSerializationTests.cs`
- Create: `src/Protocol/Models/ExplodeTileRequest.cs`

- [ ] **Step 1: Write failing protocol serialization tests**

Create `tests/Protocol.Tests/ExplodeTileSerializationTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class ExplodeTileSerializationTests
{
    [Fact]
    public void ExplodeTileRequest_DeserializesSnakeCaseFields()
    {
        var req = JsonSerializer.Deserialize<ExplodeTileRequest>(
            "{\"location\":\"Frobby_CombatLab\",\"x\":9,\"y\":8,\"radius\":2,\"damage_player\":false}",
            ProtocolJson.Options)!;

        Assert.Equal("Frobby_CombatLab", req.Location);
        Assert.Equal(9, req.X);
        Assert.Equal(8, req.Y);
        Assert.Equal(2, req.Radius);
        Assert.False(req.DamagePlayer);
    }

    [Fact]
    public void ExplodeTileResult_SerializesDiagnosticsAsSnakeCase()
    {
        var result = new ExplodeTileResult
        {
            Ok = true,
            Tick = 123,
            Location = "Frobby_CombatLab",
            Tile = new TilePoint { X = 9, Y = 8 },
            Radius = 2,
            DamagePlayer = false,
            MonstersBefore = 1,
            MonstersAfter = 0,
            DebrisBefore = 0,
            DebrisAfter = 1,
            Invoked = true,
        };

        var json = JsonSerializer.Serialize(result, ProtocolJson.Options);

        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"tick\":123", json);
        Assert.Contains("\"location\":\"Frobby_CombatLab\"", json);
        Assert.Contains("\"tile\":{\"x\":9,\"y\":8}", json);
        Assert.Contains("\"radius\":2", json);
        Assert.Contains("\"damage_player\":false", json);
        Assert.Contains("\"monsters_before\":1", json);
        Assert.Contains("\"monsters_after\":0", json);
        Assert.Contains("\"debris_before\":0", json);
        Assert.Contains("\"debris_after\":1", json);
        Assert.Contains("\"invoked\":true", json);
        Assert.DoesNotContain("DamagePlayer", json);
    }
}
```

- [ ] **Step 2: Run protocol tests and verify red**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter "FullyQualifiedName~ExplodeTileSerializationTests" -v minimal
```

Expected: compile failure because `ExplodeTileRequest` and `ExplodeTileResult` do not exist.

- [ ] **Step 3: Add protocol DTOs**

Create `src/Protocol/Models/ExplodeTileRequest.cs`:

```csharp
namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape for <c>world.explode_tile</c>.</summary>
public sealed class ExplodeTileRequest
{
    public string? Location { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public int Radius { get; set; } = 2;
    public bool DamagePlayer { get; set; }
}

/// <summary>Response shape for <c>world.explode_tile</c>.</summary>
public sealed class ExplodeTileResult : MutatorOk
{
    public string Location { get; set; } = string.Empty;
    public TilePoint Tile { get; set; } = new();
    public int Radius { get; set; }
    public bool DamagePlayer { get; set; }
    public int? MonstersBefore { get; set; }
    public int? MonstersAfter { get; set; }
    public int? DebrisBefore { get; set; }
    public int? DebrisAfter { get; set; }
    public bool Invoked { get; set; }
}
```

- [ ] **Step 4: Run protocol tests and verify green**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter "FullyQualifiedName~ExplodeTileSerializationTests" -v minimal
```

Expected: all `ExplodeTileSerializationTests` pass.

- [ ] **Step 5: Commit protocol DTOs**

Run:

```bash
git add src/Protocol/Models/ExplodeTileRequest.cs tests/Protocol.Tests/ExplodeTileSerializationTests.cs
git commit -m "Add explode tile protocol models"
```

## Task 2: Harness Handler Validation And Dispatch

**Files:**
- Create: `tests/Harness.Tests/WorldExplodeTileHandlerTests.cs`
- Create: `src/Harness/Handlers/WorldExplodeTileHandler.cs`

- [ ] **Step 1: Write failing harness validation tests**

Create `tests/Harness.Tests/WorldExplodeTileHandlerTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class WorldExplodeTileHandlerTests
{
    [Fact]
    public void Handle_MissingX_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"location\":\"Frobby_CombatLab\",\"y\":8,\"radius\":2}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => WorldExplodeTileHandler.Handle(p, new FakeExplodeTileWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("x and y", ex.Message);
    }

    [Fact]
    public void Handle_MissingY_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"location\":\"Frobby_CombatLab\",\"x\":9,\"radius\":2}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => WorldExplodeTileHandler.Handle(p, new FakeExplodeTileWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("x and y", ex.Message);
    }

    [Fact]
    public void Handle_NegativeTile_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"x\":-1,\"y\":8,\"radius\":2}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => WorldExplodeTileHandler.Handle(p, new FakeExplodeTileWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains(">= 0", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Handle_InvalidRadius_ThrowsInvalidParams(int radius)
    {
        var p = JsonDocument.Parse($"{{\"x\":9,\"y\":8,\"radius\":{radius}}}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => WorldExplodeTileHandler.Handle(p, new FakeExplodeTileWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("radius", ex.Message);
    }

    [Fact]
    public void Handle_NotWorldReady_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"x\":9,\"y\":8,\"radius\":2}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldExplodeTileHandler.Handle(p, new FakeExplodeTileWorld { IsWorldReady = false }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("loaded world", ex.Message);
    }

    [Fact]
    public void Handle_UnknownLocation_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"location\":\"Missing\",\"x\":9,\"y\":8,\"radius\":2}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldExplodeTileHandler.Handle(p, new FakeExplodeTileWorld { LocationExists = false }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("location not found", ex.Message);
    }

    [Fact]
    public void Handle_OutOfBoundsTile_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"location\":\"Frobby_CombatLab\",\"x\":20,\"y\":8,\"radius\":2}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldExplodeTileHandler.Handle(p, new FakeExplodeTileWorld { MapWidth = 20, MapHeight = 14 }));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("map bounds", ex.Message);
    }

    [Fact]
    public void Handle_ValidRequest_InvokesExplosionAndReturnsDiagnostics()
    {
        var world = new FakeExplodeTileWorld
        {
            CurrentLocationName = "Farm",
            ResolvedLocationName = "Frobby_CombatLab",
            MapWidth = 20,
            MapHeight = 14,
            Tick = 456,
            MonstersBefore = 1,
            MonstersAfter = 0,
            DebrisBefore = 0,
            DebrisAfter = 1,
        };
        var p = JsonDocument.Parse("{\"location\":\"Frobby_CombatLab\",\"x\":9,\"y\":8,\"radius\":2,\"damage_player\":false}").RootElement;

        var result = WorldExplodeTileHandler.Handle(p, world);
        var json = result.GetRawText();

        Assert.Equal("Frobby_CombatLab", world.InvokedLocation);
        Assert.Equal(9, world.InvokedX);
        Assert.Equal(8, world.InvokedY);
        Assert.Equal(2, world.InvokedRadius);
        Assert.False(world.InvokedDamagePlayer);
        Assert.Contains("\"location\":\"Frobby_CombatLab\"", json);
        Assert.Contains("\"tile\":{\"x\":9,\"y\":8}", json);
        Assert.Contains("\"monsters_before\":1", json);
        Assert.Contains("\"monsters_after\":0", json);
        Assert.Contains("\"invoked\":true", json);
    }

    [Fact]
    public void Handle_OmittedLocation_UsesCurrentLocation()
    {
        var world = new FakeExplodeTileWorld
        {
            CurrentLocationName = "Farm",
            ResolvedLocationName = "Farm",
            MapWidth = 80,
            MapHeight = 65,
        };
        var p = JsonDocument.Parse("{\"x\":9,\"y\":8}").RootElement;

        var result = WorldExplodeTileHandler.Handle(p, world);

        Assert.Equal("Farm", world.InvokedLocation);
        Assert.Equal(2, world.InvokedRadius);
        Assert.Contains("\"radius\":2", result.GetRawText());
    }

    private sealed class FakeExplodeTileWorld : IExplodeTileWorld
    {
        public bool IsWorldReady { get; set; } = true;
        public string CurrentLocationName { get; set; } = "Frobby_CombatLab";
        public string ResolvedLocationName { get; set; } = "Frobby_CombatLab";
        public bool LocationExists { get; set; } = true;
        public int? MapWidth { get; set; } = 20;
        public int? MapHeight { get; set; } = 14;
        public int Tick { get; set; } = 123;
        public int MonstersBefore { get; set; }
        public int MonstersAfter { get; set; }
        public int DebrisBefore { get; set; }
        public int DebrisAfter { get; set; }
        public string? InvokedLocation { get; private set; }
        public int? InvokedX { get; private set; }
        public int? InvokedY { get; private set; }
        public int? InvokedRadius { get; private set; }
        public bool? InvokedDamagePlayer { get; private set; }

        public ExplodeTileLocation? ResolveLocation(string? location)
        {
            if (!LocationExists)
                return null;

            return new ExplodeTileLocation(
                string.IsNullOrWhiteSpace(location) ? CurrentLocationName : ResolvedLocationName,
                MapWidth,
                MapHeight);
        }

        public ExplodeTileCounts CountContent(ExplodeTileLocation location)
            => InvokedLocation is null
                ? new ExplodeTileCounts(MonstersBefore, DebrisBefore)
                : new ExplodeTileCounts(MonstersAfter, DebrisAfter);

        public void Explode(ExplodeTileLocation location, int x, int y, int radius, bool damagePlayer)
        {
            InvokedLocation = location.Name;
            InvokedX = x;
            InvokedY = y;
            InvokedRadius = radius;
            InvokedDamagePlayer = damagePlayer;
        }
    }
}
```

- [ ] **Step 2: Run harness tests and verify red**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~WorldExplodeTileHandlerTests" -v minimal
```

Expected: compile failure because `WorldExplodeTileHandler`, `IExplodeTileWorld`, `ExplodeTileLocation`, and `ExplodeTileCounts` do not exist.

- [ ] **Step 3: Implement validation and injectable handler**

Create `src/Harness/Handlers/WorldExplodeTileHandler.cs`:

```csharp
using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using StardewValley.Monsters;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>world.explode_tile</c>. Triggers native SDV explosion behavior at a tile.</summary>
public static class WorldExplodeTileHandler
{
    public const string Method = "world.explode_tile";
    internal const int MaxRadius = 10;

    private static readonly IExplodeTileWorld ProductionWorld = new SdvExplodeTileWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, IExplodeTileWorld world)
    {
        var req = RpcParams.Required<ExplodeTileRequest>(paramsElement);
        ValidateRequest(req);

        if (!world.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "no active save - world.explode_tile requires a loaded world");

        var location = world.ResolveLocation(req.Location);
        if (location is null)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"world.explode_tile location not found: {req.Location}");

        var x = req.X!.Value;
        var y = req.Y!.Value;
        var radius = req.Radius;
        ValidateTileBounds(location, x, y);

        var before = world.CountContent(location);
        world.Explode(location, x, y, radius, req.DamagePlayer);
        var after = world.CountContent(location);

        return ProtocolJson.ToElement(new ExplodeTileResult
        {
            Tick = world.Tick,
            Location = location.Name,
            Tile = new TilePoint { X = x, Y = y },
            Radius = radius,
            DamagePlayer = req.DamagePlayer,
            MonstersBefore = before.MonsterCount,
            MonstersAfter = after.MonsterCount,
            DebrisBefore = before.DebrisCount,
            DebrisAfter = after.DebrisCount,
            Invoked = true,
        });
    }

    private static void ValidateRequest(ExplodeTileRequest req)
    {
        if ((req.X is null) != (req.Y is null))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "world.explode_tile requires both x and y");
        if (req.X is null || req.Y is null)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "world.explode_tile requires target tile x and y");
        if (req.X < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.x must be >= 0");
        if (req.Y < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.y must be >= 0");
        if (req.Radius < 1 || req.Radius > MaxRadius)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                $"params.radius must be between 1 and {MaxRadius}");
    }

    private static void ValidateTileBounds(ExplodeTileLocation location, int x, int y)
    {
        if (location.MapWidth is null || location.MapHeight is null)
            return;

        if (x >= location.MapWidth || y >= location.MapHeight)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "world.explode_tile target tile must be inside the resolved map bounds");
    }
}

internal interface IExplodeTileWorld
{
    bool IsWorldReady { get; }
    int Tick { get; }
    string CurrentLocationName { get; }
    ExplodeTileLocation? ResolveLocation(string? location);
    ExplodeTileCounts CountContent(ExplodeTileLocation location);
    void Explode(ExplodeTileLocation location, int x, int y, int radius, bool damagePlayer);
}

internal sealed record ExplodeTileLocation(string Name, int? MapWidth, int? MapHeight, object? NativeLocation = null);

internal sealed record ExplodeTileCounts(int MonsterCount, int DebrisCount);

internal sealed class SdvExplodeTileWorld : IExplodeTileWorld
{
    private const BindingFlags InstanceMemberFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public bool IsWorldReady => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;
    public int Tick => Game1.ticks;
    public string CurrentLocationName => CurrentLocation.NameOrUniqueName ?? CurrentLocation.Name ?? string.Empty;

    public ExplodeTileLocation? ResolveLocation(string? location)
    {
        var native = string.IsNullOrWhiteSpace(location)
            ? CurrentLocation
            : Game1.getLocationFromName(location);
        if (native is null)
            return null;

        return new ExplodeTileLocation(
            native.NameOrUniqueName ?? native.Name ?? string.Empty,
            native.Map?.Layers.FirstOrDefault()?.LayerWidth,
            native.Map?.Layers.FirstOrDefault()?.LayerHeight,
            native);
    }

    public ExplodeTileCounts CountContent(ExplodeTileLocation location)
    {
        var native = RequireNativeLocation(location);
        return new ExplodeTileCounts(
            native.characters.OfType<Monster>().Count(),
            native.debris?.Count ?? 0);
    }

    public void Explode(ExplodeTileLocation location, int x, int y, int radius, bool damagePlayer)
    {
        var native = RequireNativeLocation(location);
        InvokeNativeExplosion(native, x, y, radius, damagePlayer);
    }

    private static GameLocation RequireNativeLocation(ExplodeTileLocation location)
        => location.NativeLocation as GameLocation
            ?? throw new JsonRpcException(JsonRpcErrorCode.InternalError,
                "world.explode_tile received a non-Stardew location adapter");

    private static void InvokeNativeExplosion(GameLocation location, int x, int y, int radius, bool damagePlayer)
    {
        var tile = new Vector2(x, y);
        var farmer = Game1.player;
        var methods = typeof(GameLocation)
            .GetMethods(InstanceMemberFlags)
            .Where(m => m.Name == "explode")
            .OrderByDescending(m => m.GetParameters().Length)
            .ToList();

        foreach (var method in methods)
        {
            var args = TryBuildExplosionArgs(method, tile, radius, farmer, damagePlayer);
            if (args is null)
                continue;

            method.Invoke(location, args);
            return;
        }

        throw new JsonRpcException(JsonRpcErrorCode.InternalError,
            "world.explode_tile could not find a compatible GameLocation.explode overload");
    }

    private static object?[]? TryBuildExplosionArgs(MethodInfo method, Vector2 tile, int radius, Farmer farmer, bool damagePlayer)
    {
        var parameters = method.GetParameters();
        var args = new object?[parameters.Length];
        var assignedVector = false;
        var assignedRadius = false;
        var assignedFarmer = false;

        for (var i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            if (p.ParameterType == typeof(Vector2) && !assignedVector)
            {
                args[i] = tile;
                assignedVector = true;
            }
            else if (p.ParameterType == typeof(int) && !assignedRadius)
            {
                args[i] = radius;
                assignedRadius = true;
            }
            else if (p.ParameterType == typeof(Farmer) && !assignedFarmer)
            {
                args[i] = farmer;
                assignedFarmer = true;
            }
            else if (p.ParameterType == typeof(bool))
            {
                args[i] = p.Name is not null
                    && (p.Name.Contains("farmer", StringComparison.OrdinalIgnoreCase)
                        || p.Name.Contains("player", StringComparison.OrdinalIgnoreCase))
                    ? damagePlayer
                    : false;
            }
            else if (p.HasDefaultValue)
            {
                args[i] = p.DefaultValue;
            }
            else
            {
                return null;
            }
        }

        return assignedVector && assignedRadius ? args : null;
    }

    private static GameLocation CurrentLocation
        => Game1.currentLocation
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"{WorldExplodeTileHandler.Method} requires a current location");
}
```

This intentionally uses reflection only at the SDV boundary. The handler tests stay deterministic through `IExplodeTileWorld`, and the runtime code still invokes the game's native `GameLocation.explode` overload instead of direct monster deletion or visual-only sprites.

- [ ] **Step 4: Run harness tests and verify green**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~WorldExplodeTileHandlerTests" -v minimal
```

Expected: all `WorldExplodeTileHandlerTests` pass.

- [ ] **Step 5: Commit handler**

Run:

```bash
git add src/Harness/Handlers/WorldExplodeTileHandler.cs tests/Harness.Tests/WorldExplodeTileHandlerTests.cs
git commit -m "Add neutral explode tile handler"
```

## Task 3: Register RPC And Add Runner/DSL Affordances

**Files:**
- Modify: `src/Harness/ModEntry.cs`
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`
- Modify: `tests/Runner.Tests/ScenarioRunnerTests.cs`
- Modify: `src/Runner.Dsl/World.cs`
- Modify: `tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs`

- [ ] **Step 1: Write failing runner pass-through/report-label test**

Add this test to `tests/Runner.Tests/ScenarioRunnerTests.cs` near `CombatLabRelocateMonster_PassesThroughAndReportsReadableStep`:

```csharp
[Fact]
public async Task WorldExplodeTile_PassesThroughAndReportsReadableStep()
{
    var socket = SocketPath();
    var tmp = Path.Combine(Path.GetTempPath(), $"explode-tile-report-{Guid.NewGuid():N}");
    var rd = RunDirectory.Create(tmp);
    var calls = new List<string>();
    var explodeParams = default(JsonElement);
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

    var serverTask = Task.Run(async () =>
    {
        await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
        {
            session.RequestReceived += async req =>
            {
                calls.Add(req.Method);
                if (req.Method == "world.explode_tile")
                    explodeParams = req.Params!.Value.Clone();

                JsonElement r = req.Method switch
                {
                    "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                    "world.explode_tile" => JsonDocument.Parse("{\"ok\":true,\"tick\":123,\"location\":\"Frobby_CombatLab\",\"tile\":{\"x\":9,\"y\":8},\"radius\":2,\"damage_player\":false,\"monsters_before\":1,\"monsters_after\":0,\"debris_before\":0,\"debris_after\":1,\"invoked\":true}").RootElement,
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
            Name = "explode_tile_report",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "world.explode_tile",
                    Args = JsonDocument.Parse("{\"location\":\"Frobby_CombatLab\",\"x\":9,\"y\":8,\"radius\":2,\"damage_player\":false}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed, string.Join("\n", report.Failures));
        Assert.Contains("world.explode_tile", calls);
        Assert.Equal("Frobby_CombatLab", explodeParams.GetProperty("location").GetString());
        Assert.Equal(9, explodeParams.GetProperty("x").GetInt32());
        Assert.Equal(8, explodeParams.GetProperty("y").GetInt32());
        Assert.Equal("Explode tile Frobby_CombatLab (9,8) radius 2", report.Steps[0].Detail);
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

Append this test to `tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs` after `UseTool_InvokesWorldUseToolAndDeserializesResult`:

```csharp
[Fact]
public async Task ExplodeTile_InvokesWorldExplodeTileAndDeserializesResult()
{
    var inv = new CapturingInvoker
    {
        NextResponse = JsonDocument.Parse(
            "{\"ok\":true,\"tick\":42,\"location\":\"Frobby_CombatLab\",\"tile\":{\"x\":9,\"y\":8},\"radius\":2,\"damage_player\":false,\"monsters_before\":1,\"monsters_after\":0,\"debris_before\":0,\"debris_after\":1,\"invoked\":true}")
            .RootElement,
    };
    SdvTestSession.InitializeForTests(inv);
    ExplodeTileResult result;
    try
    {
        result = await World.ExplodeTile(9, 8, location: "Frobby_CombatLab", radius: 2, damagePlayer: false);
    }
    finally { SdvTestSession.ResetForTests(); }

    Assert.Equal("world.explode_tile", inv.Calls[0].Method);
    Assert.Contains("\"location\":\"Frobby_CombatLab\"", inv.Calls[0].ParamsJson);
    Assert.Contains("\"x\":9", inv.Calls[0].ParamsJson);
    Assert.Contains("\"y\":8", inv.Calls[0].ParamsJson);
    Assert.Contains("\"radius\":2", inv.Calls[0].ParamsJson);
    Assert.Contains("\"damage_player\":false", inv.Calls[0].ParamsJson);
    Assert.Equal("Frobby_CombatLab", result.Location);
    Assert.Equal(9, result.Tile.X);
    Assert.Equal(8, result.Tile.Y);
    Assert.True(result.Invoked);
}
```

- [ ] **Step 3: Run focused runner/DSL tests and verify red**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~WorldExplodeTile_PassesThroughAndReportsReadableStep" -v minimal
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter "FullyQualifiedName~ExplodeTile_InvokesWorldExplodeTileAndDeserializesResult" -v minimal
```

Expected: runner test fails on the unreadable default step label, and DSL test fails because `World.ExplodeTile` does not exist.

- [ ] **Step 4: Register RPC in the harness**

In `src/Harness/ModEntry.cs`, add this registration immediately after `WorldUseToolHandler`:

```csharp
_rpc.Register(WorldExplodeTileHandler.Method, p => WorldExplodeTileHandler.Handle(p));
```

In the startup monitor string, add `world.explode_tile` after `world.use_tool` in the `Manipulators:` list:

```text
world.use_tool, world.explode_tile, input.key
```

- [ ] **Step 5: Add readable runner label**

In `src/Runner/Scenarios/ScenarioRunner.cs`, add this arm to `DescribeStep` after `world.use_tool`:

```csharp
"world.explode_tile" => $"Explode tile {GetStringArg(step.Args, "location") ?? "current"} ({GetIntArg(step.Args, "x") ?? 0},{GetIntArg(step.Args, "y") ?? 0}) radius {GetIntArg(step.Args, "radius") ?? 2}",
```

- [ ] **Step 6: Add DSL wrapper**

Append this method to `src/Runner.Dsl/World.cs` after `UseTool`:

```csharp
/// <summary>Trigger native Stardew explosion behavior at a tile in the current or named location.</summary>
public static async Task<ExplodeTileResult> ExplodeTile(
    int x,
    int y,
    string? location = null,
    int radius = 2,
    bool damagePlayer = false,
    CancellationToken ct = default)
{
    var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
    var p = JsonSerializer.SerializeToElement(new ExplodeTileRequest
    {
        Location = location,
        X = x,
        Y = y,
        Radius = radius,
        DamagePlayer = damagePlayer,
    }, ProtocolJson.Options);
    var resp = await s.InvokeAsync("world.explode_tile", p, ct);
    return JsonSerializer.Deserialize<ExplodeTileResult>(resp, ProtocolJson.Options)
        ?? throw new SdvRpcException("world.explode_tile", Protocol.JsonRpcErrorCode.InternalError,
            "empty world.explode_tile response");
}
```

- [ ] **Step 7: Run focused runner/DSL tests and verify green**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~WorldExplodeTile_PassesThroughAndReportsReadableStep" -v minimal
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter "FullyQualifiedName~ExplodeTile_InvokesWorldExplodeTileAndDeserializesResult" -v minimal
```

Expected: both tests pass.

- [ ] **Step 8: Commit registration and affordances**

Run:

```bash
git add src/Harness/ModEntry.cs src/Runner/Scenarios/ScenarioRunner.cs tests/Runner.Tests/ScenarioRunnerTests.cs src/Runner.Dsl/World.cs tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs
git commit -m "Wire explode tile into runner and DSL"
```

## Task 4: Frobby Documentation And TODO Status

**Files:**
- Modify: `docs/rpc-schema.md`
- Modify: `docs/dsl-quickstart.md`
- Modify: `docs/wiki/examples.md`
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Update RPC schema docs**

Add this section to `docs/rpc-schema.md` near the other `world.*` methods:

````markdown
### `world.explode_tile`

Triggers Stardew-native explosion behavior at a tile in the current or named loaded location. This is a direct deterministic test primitive: it does not require a bomb item, fuse timing, inventory state, or player proximity.

Request:

```json
{
  "location": "Frobby_CombatLab",
  "x": 9,
  "y": 8,
  "radius": 2,
  "damage_player": false
}
```

Response:

```json
{
  "ok": true,
  "tick": 123,
  "location": "Frobby_CombatLab",
  "tile": { "x": 9, "y": 8 },
  "radius": 2,
  "damage_player": false,
  "monsters_before": 1,
  "monsters_after": 0,
  "debris_before": 0,
  "debris_after": 1,
  "invoked": true
}
```

Use `wait.location_content` for the assertion that matters, such as waiting for a labelled monster to be removed. The count fields are diagnostics for reports and debugging.
````

- [ ] **Step 2: Update DSL quickstart docs**

Add this example to `docs/dsl-quickstart.md` near world/combat examples:

````markdown
```csharp
await CombatLab.Reset(playerX: 8, playerY: 8);
await World.ExplodeTile(9, 8, location: "Frobby_CombatLab", radius: 2);
```

`World.ExplodeTile` is the direct deterministic explosion primitive. Use it when a test needs native explosion behavior without proving bomb placement or inventory UX.
````

- [ ] **Step 3: Update wiki examples**

Add this to `docs/wiki/examples.md`:

````markdown
## Explosion Cleanup

Use `world.explode_tile` when a mod feature depends on native Stardew explosion behavior, such as mummy cleanup or object blast effects:

```json
{
  "action": "world.explode_tile",
  "args": {
    "location": "Frobby_CombatLab",
    "x": 9,
    "y": 8,
    "radius": 2,
    "damage_player": false
  }
}
```

Follow it with `wait.location_content` to assert the actual world-state change.
````

- [ ] **Step 4: Update TODO status**

In `SVE_FROBBY_CAPABILITY_TODO.md`, add Slice 21 as active/in progress before implementation verification:

```markdown
## Slice 21: Neutral Explosion Support

Status: In progress.

- Add Frobby `world.explode_tile` RPC/action.
- Keep implementation mod-neutral and direct; do not require player bomb placement.
- Prove explosion cleanup against an SVE runtime monster isolated in `Frobby_CombatLab`.
- Follow-up: player-like bomb placement remains separate.
```

- [ ] **Step 5: Commit docs**

Run:

```bash
git add docs/rpc-schema.md docs/dsl-quickstart.md docs/wiki/examples.md SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "Document explode tile test primitive"
```

## Task 5: SVE Proof Scenario

**Files:**
- Create: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/29-sve-combat-lab-explode-mummy.test.json`
- Modify: `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`

- [ ] **Step 1: Add the SVE scenario**

Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/29-sve-combat-lab-explode-mummy.test.json`:

```json
{
  "name": "sve_combat_lab_explode_mummy_cleanup",
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
      "action": "player.warp",
      "args": { "location": "Custom_CrimsonBadlands", "x": 22, "y": 145 }
    },
    {
      "action": "wait.location",
      "args": {
        "location": "Custom_CrimsonBadlands",
        "x": 22,
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
        "type": "Mummy",
        "sprite_texture": "Characters/Monsters/CorruptMummy",
        "min_count": 1,
        "max_count": 1,
        "timeout_ms": 15000,
        "poll_ms": 100
      }
    },
    {
      "action": "combat_lab.reset",
      "args": {
        "player_x": 8,
        "player_y": 8,
        "width": 20,
        "height": 14,
        "warp_player": true
      }
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
      "action": "combat_lab.relocate_monster",
      "args": {
        "from_location": "Custom_CrimsonBadlands",
        "label": "corrupt-mummy",
        "target_x": 9,
        "target_y": 8,
        "match": {
          "x": 20,
          "y": 144,
          "type": "Mummy",
          "sprite_texture": "Characters/Monsters/CorruptMummy"
        }
      }
    },
    {
      "action": "wait.location_content",
      "args": {
        "location": "Frobby_CombatLab",
        "collection": "monsters",
        "label": "corrupt-mummy",
        "type": "Mummy",
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
        "repeat": 3,
        "delay_ticks": 8,
        "target": {
          "location": "Frobby_CombatLab",
          "label": "corrupt-mummy"
        }
      }
    },
    {
      "action": "world.explode_tile",
      "args": {
        "location": "Frobby_CombatLab",
        "x": 9,
        "y": 8,
        "radius": 3,
        "damage_player": false
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
      "message": "Explosion cleanup scenario should finish inside the Frobby combat dev room"
    }
  ]
}
```

If the first live run shows the corrupt mummy still has too much health after three Monster Splitter swings, change only the `combat.attack.repeat` value and rerun the scenario. Do not add direct-damage or direct-kill shortcuts.

- [ ] **Step 2: Document the new SVE scenario**

In `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`, add a scenario row/paragraph matching the local format:

```markdown
- `tests/sdv/29-sve-combat-lab-explode-mummy.test.json` validates that Frobby can relocate a real SVE/FTM corrupt mummy into the neutral Combat Lab and finish cleanup through `world.explode_tile`.
```

- [ ] **Step 3: Commit SVE scenario/docs on the current SVE feature branch**

Run:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded add tests/sdv/29-sve-combat-lab-explode-mummy.test.json docs/FROBBY.md
git -C /home/fintan/stardewRepos/StardewValleyExpanded commit -m "Add Frobby explosion cleanup scenario"
```

## Task 6: Verification And Stabilization

**Files:**
- Modify only files needed to fix failures found by the commands below.
- Update `SVE_FROBBY_CAPABILITY_TODO.md` with final verification notes.

- [ ] **Step 1: Run focused Frobby test suites**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter "FullyQualifiedName~ExplodeTileSerializationTests" -v minimal
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~WorldExplodeTileHandlerTests" -v minimal
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~WorldExplodeTile_PassesThroughAndReportsReadableStep" -v minimal
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter "FullyQualifiedName~ExplodeTile_InvokesWorldExplodeTileAndDeserializesResult" -v minimal
```

Expected: all focused tests pass.

- [ ] **Step 2: Run broader Frobby regression suites**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj -v minimal
dotnet test tests/Harness.Tests/Harness.Tests.csproj -v minimal
dotnet test tests/Runner.Tests/Runner.Tests.csproj -v minimal
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj -v minimal
dotnet build -v minimal
```

Expected: all tests pass and build has no new errors.

- [ ] **Step 3: Deploy the harness payload before live SVE runs**

Run from `/home/fintan/stardewRepos/frobby/sdv-test-framework`:

```bash
./scripts/sdv-test doctor --repo /home/fintan/stardewRepos/StardewValleyExpanded
```

Expected: doctor succeeds and confirms the harness payload is usable for the SVE repo. If doctor surfaces missing harness transitive dependencies, fix the Frobby deploy/package path before running live scenarios.

- [ ] **Step 4: Run the new SVE scenario headlessly**

Run:

```bash
./scripts/sdv-test run --repo /home/fintan/stardewRepos/StardewValleyExpanded --scenario tests/sdv/29-sve-combat-lab-explode-mummy.test.json --headless --report
```

Expected: scenario passes, final report screenshot shows `Frobby_CombatLab`, and the labelled corrupt mummy count reaches zero after `world.explode_tile`.

- [ ] **Step 5: Run targeted live regressions**

Run:

```bash
./scripts/sdv-test run --repo /home/fintan/stardewRepos/StardewValleyExpanded --scenario tests/sdv/28-sve-combat-lab-relocate-mod-monster.test.json --headless --report
./scripts/sdv-test run --repo /home/fintan/stardewRepos/StardewValleyExpanded --scenario tests/sdv/27-sve-combat-lab-vanilla-monster.test.json --headless --report
```

Expected: both scenarios still pass.

- [ ] **Step 6: Update TODO completion notes**

In `SVE_FROBBY_CAPABILITY_TODO.md`, change Slice 21 status to complete and record the exact verification commands that passed.

- [ ] **Step 7: Commit final Frobby verification/status docs**

Run:

```bash
git add SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "Mark neutral explosion slice complete"
```

- [ ] **Step 8: Final status check**

Run:

```bash
git status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
```

Expected: both worktrees are clean on their current feature branches. Do not merge SVE into its main/master branch unless the user explicitly requests it.

## Self-Review

Spec coverage:

- Protocol request/result: Task 1.
- Harness validation and native explosion path: Task 2.
- Runner labels and DSL affordance: Task 3.
- Frobby docs/wiki/TODO: Task 4 and Task 6.
- SVE corrupt mummy proof: Task 5.
- Verification and regressions: Task 6.
- Player-like bomb placement remains deferred, as required by the design.

Placeholder scan:

- No `TBD`, `TODO`, or unspecified "add tests" steps remain.
- The only conditional adjustment is live scenario tuning of combat repeat count, with an explicit constraint not to add direct kill shortcuts.

Type consistency:

- DTO names are `ExplodeTileRequest` and `ExplodeTileResult`.
- RPC/action name is consistently `world.explode_tile`.
- Handler/interface names are `WorldExplodeTileHandler`, `IExplodeTileWorld`, `ExplodeTileLocation`, and `ExplodeTileCounts`.
- Result fields match snake-case serialization expectations: `damage_player`, `monsters_before`, `monsters_after`, `debris_before`, `debris_after`.
