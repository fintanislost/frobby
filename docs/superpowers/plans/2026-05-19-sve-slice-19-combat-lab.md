# SVE Slice 19 Combat Lab Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a neutral Frobby Combat Lab that can reset a clean test arena, spawn vanilla monsters with stable run-local identity, attack by identity or label, and prove exact monster removal in SVE.

**Architecture:** Add protocol models first, then harness-side lab actions and identity projection, then runner targeting/waits that consume the new identity fields. Keep the combat proof player-like by continuing to use `combat.attack`; the lab creates controlled state but does not directly kill monsters or spawn loot.

**Tech Stack:** C#/.NET 10 runner and tests, net6.0 SMAPI harness, Stardew Valley 1.6 runtime types, JSON-RPC protocol models, xUnit, JSON scenario files.

---

## File Structure

Frobby files:

- Create `src/Protocol/Models/CombatLabRequests.cs`: request/result DTOs for `combat_lab.reset` and `combat_lab.spawn_monster`.
- Modify `src/Protocol/Models/LocationState.cs`: add `monster_id`, `label`, and `spawned_by_frobby` to `MonsterSummary`.
- Modify `src/Protocol/Models/CombatAttackRequest.cs`: add `monster_id` and `label` filters to `CombatTargetCriteria`.
- Create `src/Harness/Handlers/CombatLabIdentityRegistry.cs`: test-run identity registry for lab-spawned monsters.
- Create `src/Harness/Handlers/CombatLabResetHandler.cs`: JSON-RPC handler and production world adapter for resetting/creating `Frobby_CombatLab`.
- Create `src/Harness/Handlers/CombatLabSpawnMonsterHandler.cs`: JSON-RPC handler and production world adapter for spawning supported vanilla monsters.
- Modify `src/Harness/Handlers/LocationContentProjector.cs`: project lab identity metadata onto monster summaries.
- Modify `src/Harness/Handlers/ScenarioEndHandler.cs`: clear lab identity state at scenario end.
- Modify `src/Harness/ModEntry.cs`: register `combat_lab.reset` and `combat_lab.spawn_monster`, and update the startup method list.
- Modify `src/Runner/Scenarios/ScenarioRunner.cs`: add label/id filters to `wait.location_content`, combat target matching, report labels, and timeout text.
- Modify `src/Runner.Dsl/Combat.cs`: add `AttackTarget` helper by monster id or label.
- Create `src/Runner.Dsl/CombatLab.cs`: DSL helpers for lab reset and spawn.
- Modify `docs/rpc-schema.md`, `docs/dsl-quickstart.md`, `docs/wiki/examples.md`, and `docs/wiki/index.md`: document Combat Lab usage.
- Modify `SVE_FROBBY_CAPABILITY_TODO.md`: move Slice 19 from Planning to Active during implementation, then Done after verification.

Frobby tests:

- Modify `tests/Protocol.Tests/LocationStateSerializationTests.cs`.
- Create `tests/Protocol.Tests/CombatLabSerializationTests.cs`.
- Create `tests/Harness.Tests/CombatLabIdentityRegistryTests.cs`.
- Create `tests/Harness.Tests/CombatLabResetHandlerTests.cs`.
- Create `tests/Harness.Tests/CombatLabSpawnMonsterHandlerTests.cs`.
- Modify `tests/Harness.Tests/LocationContentProjectorTests.cs`.
- Modify `tests/Runner.Tests/ScenarioRunnerTests.cs`.
- Modify `tests/Runner.Dsl.Tests/Facets/CombatTests.cs`.
- Create `tests/Runner.Dsl.Tests/Facets/CombatLabTests.cs`.

SVE files:

- Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/27-sve-combat-lab-vanilla-monster.test.json`.
- Modify `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md` to list scenario 27 and the Combat Lab proof.

## Task 1: Protocol Models And Monster Identity Fields

**Files:**
- Create: `src/Protocol/Models/CombatLabRequests.cs`
- Modify: `src/Protocol/Models/LocationState.cs`
- Modify: `src/Protocol/Models/CombatAttackRequest.cs`
- Create: `tests/Protocol.Tests/CombatLabSerializationTests.cs`
- Modify: `tests/Protocol.Tests/LocationStateSerializationTests.cs`

- [ ] **Step 1: Write failing protocol serialization tests**

Add `tests/Protocol.Tests/CombatLabSerializationTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public sealed class CombatLabSerializationTests
{
    [Fact]
    public void ResetRequest_SerializesSnakeCaseFields()
    {
        var json = JsonSerializer.Serialize(new CombatLabResetRequest
        {
            PlayerX = 8,
            PlayerY = 9,
            Width = 20,
            Height = 14,
            WarpPlayer = true,
        }, ProtocolJson.Options);

        Assert.Contains("\"player_x\":8", json);
        Assert.Contains("\"player_y\":9", json);
        Assert.Contains("\"width\":20", json);
        Assert.Contains("\"height\":14", json);
        Assert.Contains("\"warp_player\":true", json);
    }

    [Fact]
    public void SpawnMonsterRequest_SerializesSnakeCaseFields()
    {
        var json = JsonSerializer.Serialize(new CombatLabSpawnMonsterRequest
        {
            Kind = "GreenSlime",
            Label = "target",
            X = 12,
            Y = 8,
            Health = 1,
        }, ProtocolJson.Options);

        Assert.Contains("\"kind\":\"GreenSlime\"", json);
        Assert.Contains("\"label\":\"target\"", json);
        Assert.Contains("\"x\":12", json);
        Assert.Contains("\"y\":8", json);
        Assert.Contains("\"health\":1", json);
    }

    [Fact]
    public void SpawnMonsterResult_SerializesIdentityFields()
    {
        var json = JsonSerializer.Serialize(new CombatLabSpawnMonsterResult
        {
            Ok = true,
            MonsterId = "frobby-monster-1",
            Label = "target",
            Kind = "GreenSlime",
            Location = "Frobby_CombatLab",
            Tile = new TilePoint { X = 12, Y = 8 },
            Health = 1,
            MaxHealth = 24,
        }, ProtocolJson.Options);

        Assert.Contains("\"monster_id\":\"frobby-monster-1\"", json);
        Assert.Contains("\"label\":\"target\"", json);
        Assert.Contains("\"location\":\"Frobby_CombatLab\"", json);
        Assert.Contains("\"tile\":{\"x\":12,\"y\":8}", json);
        Assert.Contains("\"max_health\":24", json);
    }
}
```

In `tests/Protocol.Tests/LocationStateSerializationTests.cs`, update the monster fixture:

```csharp
Monsters = new()
{
    new MonsterSummary
    {
        MonsterId = "frobby-monster-1",
        Label = "target",
        SpawnedByFrobby = true,
        Tile = new TilePoint { X = 44, Y = 31 },
        Name = "Mummy",
        Type = "Mummy",
        Health = 2000,
        MaxHealth = 2000,
        Damage = 100,
        SpriteTexture = "Characters/Monsters/CorruptMummy",
    },
},
```

Then update the monster JSON assertion to include the new leading fields:

```csharp
Assert.Contains("\"monsters\":[{\"tile\":{\"x\":44,\"y\":31},\"monster_id\":\"frobby-monster-1\",\"label\":\"target\",\"spawned_by_frobby\":true,\"name\":\"Mummy\",\"type\":\"Mummy\",\"health\":2000,\"max_health\":2000,\"damage\":100,\"sprite_texture\":\"Characters/Monsters/CorruptMummy\"}]", json);
```

- [ ] **Step 2: Run protocol tests and verify red**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter "FullyQualifiedName~CombatLabSerializationTests|FullyQualifiedName~LocationStateSerializationTests" -v minimal
```

Expected: fail to compile because `CombatLabResetRequest`, `CombatLabSpawnMonsterRequest`, `CombatLabSpawnMonsterResult`, and new `MonsterSummary` fields do not exist.

- [ ] **Step 3: Add protocol models and fields**

Create `src/Protocol/Models/CombatLabRequests.cs`:

```csharp
namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape of <c>combat_lab.reset</c>.</summary>
public sealed class CombatLabResetRequest
{
    public int PlayerX { get; set; } = 8;
    public int PlayerY { get; set; } = 8;
    public int Width { get; set; } = 20;
    public int Height { get; set; } = 14;
    public bool WarpPlayer { get; set; } = true;
}

/// <summary>Response shape of <c>combat_lab.reset</c>.</summary>
public sealed class CombatLabResetResult
{
    public bool Ok { get; set; } = true;
    public string Location { get; set; } = string.Empty;
    public TilePoint PlayerTile { get; set; } = new();
    public int MapWidth { get; set; }
    public int MapHeight { get; set; }
    public int ClearedMonsters { get; set; }
    public int ClearedDebris { get; set; }
}

/// <summary>Request shape of <c>combat_lab.spawn_monster</c>.</summary>
public sealed class CombatLabSpawnMonsterRequest
{
    public string Kind { get; set; } = string.Empty;
    public string? Label { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int? Health { get; set; }
}

/// <summary>Response shape of <c>combat_lab.spawn_monster</c>.</summary>
public sealed class CombatLabSpawnMonsterResult
{
    public bool Ok { get; set; } = true;
    public string MonsterId { get; set; } = string.Empty;
    public string? Label { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public TilePoint Tile { get; set; } = new();
    public int? Health { get; set; }
    public int? MaxHealth { get; set; }
}
```

Modify `MonsterSummary` in `src/Protocol/Models/LocationState.cs`:

```csharp
public sealed class MonsterSummary
{
    public TilePoint Tile { get; set; } = new();

    /// <summary>Run-local Frobby identity. Not save-stable.</summary>
    public string? MonsterId { get; set; }

    /// <summary>Optional Frobby lab label assigned by tests.</summary>
    public string? Label { get; set; }

    /// <summary>True when this monster was spawned by the Frobby Combat Lab.</summary>
    public bool? SpawnedByFrobby { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int? Health { get; set; }
    public int? MaxHealth { get; set; }
    public int? Damage { get; set; }

    /// <summary>Runtime sprite texture asset path when Stardew or the mod exposes one.</summary>
    public string? SpriteTexture { get; set; }
}
```

Modify `CombatTargetCriteria` in `src/Protocol/Models/CombatAttackRequest.cs`:

```csharp
public sealed class CombatTargetCriteria
{
    public string? Location { get; set; }
    public string? MonsterId { get; set; }
    public string? Label { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? SpriteTexture { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public int? HealthGt { get; set; }
    public int? HealthGte { get; set; }
    public int? HealthLt { get; set; }
    public int? HealthLte { get; set; }
}
```

- [ ] **Step 4: Run protocol tests and verify green**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter "FullyQualifiedName~CombatLabSerializationTests|FullyQualifiedName~LocationStateSerializationTests" -v minimal
```

Expected: pass.

- [ ] **Step 5: Commit protocol models**

Run:

```bash
git add src/Protocol/Models/CombatLabRequests.cs src/Protocol/Models/LocationState.cs src/Protocol/Models/CombatAttackRequest.cs tests/Protocol.Tests/CombatLabSerializationTests.cs tests/Protocol.Tests/LocationStateSerializationTests.cs
git commit -m "Add combat lab protocol models"
```

## Task 2: Combat Lab Identity Registry And Monster Projection

**Files:**
- Create: `src/Harness/Handlers/CombatLabIdentityRegistry.cs`
- Modify: `src/Harness/Handlers/LocationContentProjector.cs`
- Create: `tests/Harness.Tests/CombatLabIdentityRegistryTests.cs`
- Modify: `tests/Harness.Tests/LocationContentProjectorTests.cs`

- [ ] **Step 1: Write failing identity registry tests**

Create `tests/Harness.Tests/CombatLabIdentityRegistryTests.cs`:

```csharp
using System.Runtime.Serialization;
using StardewValley.Monsters;
using SdvTestFramework.Harness.Handlers;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public sealed class CombatLabIdentityRegistryTests
{
    [Fact]
    public void Assign_ReturnsStableIdentityForSameMonster()
    {
        CombatLabIdentityRegistry.Clear();
        var monster = (GreenSlime)FormatterServices.GetUninitializedObject(typeof(GreenSlime));

        var first = CombatLabIdentityRegistry.Assign(monster, "target");
        var second = CombatLabIdentityRegistry.Assign(monster, "target");

        Assert.Equal(first.MonsterId, second.MonsterId);
        Assert.Equal("target", second.Label);
        Assert.True(second.SpawnedByFrobby);
    }

    [Fact]
    public void Clear_RemovesPreviouslyAssignedIdentity()
    {
        CombatLabIdentityRegistry.Clear();
        var monster = (GreenSlime)FormatterServices.GetUninitializedObject(typeof(GreenSlime));
        CombatLabIdentityRegistry.Assign(monster, "target");

        CombatLabIdentityRegistry.Clear();

        Assert.False(CombatLabIdentityRegistry.TryGet(monster, out _));
    }
}
```

In `tests/Harness.Tests/LocationContentProjectorTests.cs`, add a projection test near `ProjectMonster_ReadsRuntimeMonsterFields`:

```csharp
[Fact]
public void ProjectMonster_IncludesCombatLabIdentityWhenAssigned()
{
    CombatLabIdentityRegistry.Clear();
    var monster = new GreenSlime
    {
        Name = "Green Slime",
        Health = 12,
        MaxHealth = 24,
    };
    CombatLabIdentityRegistry.Assign(monster, "target");

    var summary = LocationContentProjector.ProjectMonsterForTests(monster);

    Assert.StartsWith("frobby-monster-", summary.MonsterId);
    Assert.Equal("target", summary.Label);
    Assert.True(summary.SpawnedByFrobby);
}
```

- [ ] **Step 2: Run harness tests and verify red**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~CombatLabIdentityRegistryTests|FullyQualifiedName~ProjectMonster_IncludesCombatLabIdentityWhenAssigned" -v minimal
```

Expected: fail to compile because `CombatLabIdentityRegistry` does not exist and monster projection does not set identity fields.

- [ ] **Step 3: Add the registry**

Create `src/Harness/Handlers/CombatLabIdentityRegistry.cs`:

Use reference equality for the dictionary key so two distinct monster objects can never collide through an identity hash.

```csharp
using System.Collections.Generic;

namespace SdvTestFramework.Harness.Handlers;

internal sealed record CombatLabMonsterIdentity(
    string MonsterId,
    string? Label,
    bool SpawnedByFrobby);

internal static class CombatLabIdentityRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<object, CombatLabMonsterIdentity> Identities = new(ReferenceEqualityComparer.Instance);
    private static int _nextId;

    public static CombatLabMonsterIdentity Assign(object monster, string? label)
    {
        lock (Gate)
        {
            if (Identities.TryGetValue(monster, out var existing))
            {
                if (label is null || string.Equals(existing.Label, label, StringComparison.Ordinal))
                    return existing;

                var updated = existing with { Label = label };
                Identities[monster] = updated;
                return updated;
            }

            var identity = new CombatLabMonsterIdentity(
                $"frobby-monster-{++_nextId}",
                label,
                SpawnedByFrobby: true);
            Identities[monster] = identity;
            return identity;
        }
    }

    public static bool TryGet(object monster, out CombatLabMonsterIdentity identity)
    {
        lock (Gate)
            return Identities.TryGetValue(monster, out identity!);
    }

    public static void Clear()
    {
        lock (Gate)
        {
            Identities.Clear();
            _nextId = 0;
        }
    }
}
```

Add `using System;` at the top of this file if the compiler reports that `StringComparison` is missing.

- [ ] **Step 4: Project identity fields**

Modify `ProjectMonster` in `src/Harness/Handlers/LocationContentProjector.cs`:

```csharp
private static MonsterSummary ProjectMonster(object monster)
{
    var tile = ReadTilePoint(monster);
    var summary = new MonsterSummary
    {
        Tile = tile,
        Name = ReadString(monster, "Name", "name", "DisplayName", "displayName") ?? monster.GetType().Name,
        Type = monster.GetType().Name,
        Health = ReadInt(monster, "Health", "health"),
        MaxHealth = ReadInt(monster, "MaxHealth", "maxHealth"),
        Damage = ReadInt(monster, "DamageToFarmer", "damageToFarmer", "damage"),
        SpriteTexture = NormalizeAssetName(ReadSpriteTexture(monster)),
    };

    if (CombatLabIdentityRegistry.TryGet(monster, out var identity))
    {
        summary.MonsterId = identity.MonsterId;
        summary.Label = identity.Label;
        summary.SpawnedByFrobby = identity.SpawnedByFrobby;
    }

    return summary;
}
```

- [ ] **Step 5: Run harness tests and verify green**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~CombatLabIdentityRegistryTests|FullyQualifiedName~ProjectMonster_IncludesCombatLabIdentityWhenAssigned" -v minimal
```

Expected: pass.

- [ ] **Step 6: Commit identity projection**

Run:

```bash
git add src/Harness/Handlers/CombatLabIdentityRegistry.cs src/Harness/Handlers/LocationContentProjector.cs tests/Harness.Tests/CombatLabIdentityRegistryTests.cs tests/Harness.Tests/LocationContentProjectorTests.cs
git commit -m "Add combat lab monster identity projection"
```

## Task 3: Combat Lab Reset Handler

**Files:**
- Create: `src/Harness/Handlers/CombatLabResetHandler.cs`
- Modify: `src/Harness/Handlers/ScenarioEndHandler.cs`
- Modify: `src/Harness/ModEntry.cs`
- Create: `tests/Harness.Tests/CombatLabResetHandlerTests.cs`

- [ ] **Step 1: Write failing reset handler tests**

Create `tests/Harness.Tests/CombatLabResetHandlerTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public sealed class CombatLabResetHandlerTests
{
    [Fact]
    public void Handle_NotWorldReady_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            CombatLabResetHandler.Handle(p, new FakeCombatLabWorld { IsWorldReady = false }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_InvalidDimensions_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"width\":3,\"height\":14}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            CombatLabResetHandler.Handle(p, new FakeCombatLabWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("width", ex.Message);
    }

    [Fact]
    public void Handle_DelegatesResetAndReturnsResult()
    {
        var world = new FakeCombatLabWorld();
        var p = JsonDocument.Parse("{\"player_x\":7,\"player_y\":8,\"width\":20,\"height\":14,\"warp_player\":true}").RootElement;

        var result = CombatLabResetHandler.Handle(p, world);
        var json = result.GetRawText();

        Assert.True(world.ResetCalled);
        Assert.Equal(7, world.PlayerX);
        Assert.Equal(8, world.PlayerY);
        Assert.Contains("\"location\":\"Frobby_CombatLab\"", json);
        Assert.Contains("\"player_tile\":{\"x\":7,\"y\":8}", json);
    }

    private sealed class FakeCombatLabWorld : ICombatLabWorld
    {
        public bool IsWorldReady { get; init; } = true;
        public bool ResetCalled { get; private set; }
        public int PlayerX { get; private set; }
        public int PlayerY { get; private set; }

        public CombatLabResetResult Reset(CombatLabResetRequest request)
        {
            ResetCalled = true;
            PlayerX = request.PlayerX;
            PlayerY = request.PlayerY;
            return new CombatLabResetResult
            {
                Location = CombatLabResetHandler.LocationName,
                PlayerTile = new TilePoint { X = request.PlayerX, Y = request.PlayerY },
                MapWidth = request.Width,
                MapHeight = request.Height,
                ClearedMonsters = 1,
                ClearedDebris = 2,
            };
        }
    }
}
```

- [ ] **Step 2: Run reset tests and verify red**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter FullyQualifiedName~CombatLabResetHandlerTests -v minimal
```

Expected: fail to compile because `CombatLabResetHandler`, `ICombatLabWorld`, and referenced protocol models are not available to the harness test namespace yet.

- [ ] **Step 3: Add reset handler and world seam**

Create `src/Harness/Handlers/CombatLabResetHandler.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

public static class CombatLabResetHandler
{
    public const string Method = "combat_lab.reset";
    public const string LocationName = "Frobby_CombatLab";

    private static readonly ICombatLabWorld ProductionWorld = new SdvCombatLabWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, ICombatLabWorld world)
    {
        var req = RpcParams.Optional<CombatLabResetRequest>(paramsElement) ?? new CombatLabResetRequest();
        Validate(req);

        if (!world.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "no active save - combat_lab.reset requires a loaded world");

        CombatLabIdentityRegistry.Clear();
        return ProtocolJson.ToElement(world.Reset(req));
    }

    private static void Validate(CombatLabResetRequest req)
    {
        if (req.Width < 8)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "combat_lab.reset requires width >= 8");
        if (req.Height < 8)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "combat_lab.reset requires height >= 8");
        if (req.PlayerX < 0 || req.PlayerX >= req.Width || req.PlayerY < 0 || req.PlayerY >= req.Height)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "combat_lab.reset player tile must be inside the lab bounds");
    }
}

internal interface ICombatLabWorld
{
    bool IsWorldReady { get; }
    CombatLabResetResult Reset(CombatLabResetRequest request);
}

internal sealed class SdvCombatLabWorld : ICombatLabWorld
{
    private const string MapAsset = "Maps/Mines/1";

    public bool IsWorldReady => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;

    public CombatLabResetResult Reset(CombatLabResetRequest request)
    {
        var lab = GetOrCreateLab();
        var clearedMonsters = lab.characters.Count;
        var clearedDebris = CountDebris(lab);

        lab.characters.Clear();
        ClearDebris(lab);
        lab.objects.Clear();

        if (request.WarpPlayer)
            Game1.warpFarmer(CombatLabResetHandler.LocationName, request.PlayerX, request.PlayerY, flip: false);

        return new CombatLabResetResult
        {
            Location = CombatLabResetHandler.LocationName,
            PlayerTile = new TilePoint { X = request.PlayerX, Y = request.PlayerY },
            MapWidth = lab.Map?.Layers.FirstOrDefault()?.LayerWidth ?? request.Width,
            MapHeight = lab.Map?.Layers.FirstOrDefault()?.LayerHeight ?? request.Height,
            ClearedMonsters = clearedMonsters,
            ClearedDebris = clearedDebris,
        };
    }

    private static GameLocation GetOrCreateLab()
    {
        var existing = Game1.getLocationFromName(CombatLabResetHandler.LocationName);
        if (existing is not null)
            return existing;

        var lab = new GameLocation(MapAsset, CombatLabResetHandler.LocationName);
        Game1.locations.Add(lab);
        return lab;
    }

    private static int CountDebris(GameLocation location)
        => location.debris?.Count ?? 0;

    private static void ClearDebris(GameLocation location)
        => location.debris?.Clear();
}
```

If this file needs `System.Linq`, add `using System.Linq;` at the top. If `RpcParams.Optional<T>` is not available in the local helper, replace that line with the existing optional-deserialization pattern used by nearby handlers:

```csharp
var req = paramsElement is { ValueKind: JsonValueKind.Object } element
    ? JsonSerializer.Deserialize<CombatLabResetRequest>(element.GetRawText(), ProtocolJson.Options) ?? new CombatLabResetRequest()
    : new CombatLabResetRequest();
```

- [ ] **Step 4: Clear identity at scenario end**

Modify `src/Harness/Handlers/ScenarioEndHandler.cs` so successful scenario end clears lab identity state. Place this immediately before or after `ScenarioState.Clear()`:

```csharp
CombatLabIdentityRegistry.Clear();
```

- [ ] **Step 5: Register reset RPC**

Modify `src/Harness/ModEntry.cs` near the combat registration:

```csharp
_rpc.Register(CombatLabResetHandler.Method, p => CombatLabResetHandler.Handle(p));
_rpc.Register(CombatAttackHandler.Method, p => CombatAttackHandler.Handle(p));
```

Update the startup log string so the Combat section reads:

```text
Combat: combat_lab.reset, combat_lab.spawn_monster, combat.attack.
```

Only `combat_lab.reset` exists after this task. The `combat_lab.spawn_monster` registration will be added in Task 4; the log can be updated again there if preferred.

- [ ] **Step 6: Run reset tests and verify green**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~CombatLabResetHandlerTests|FullyQualifiedName~ScenarioEnd" -v minimal
```

Expected: pass.

- [ ] **Step 7: Commit reset handler**

Run:

```bash
git add src/Harness/Handlers/CombatLabResetHandler.cs src/Harness/Handlers/ScenarioEndHandler.cs src/Harness/ModEntry.cs tests/Harness.Tests/CombatLabResetHandlerTests.cs
git commit -m "Add combat lab reset handler"
```

## Task 4: Combat Lab Vanilla Monster Spawn Handler

**Files:**
- Create: `src/Harness/Handlers/CombatLabSpawnMonsterHandler.cs`
- Modify: `src/Harness/Handlers/CombatLabResetHandler.cs`
- Modify: `src/Harness/ModEntry.cs`
- Create: `tests/Harness.Tests/CombatLabSpawnMonsterHandlerTests.cs`

- [ ] **Step 1: Write failing spawn handler tests**

Create `tests/Harness.Tests/CombatLabSpawnMonsterHandlerTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public sealed class CombatLabSpawnMonsterHandlerTests
{
    [Fact]
    public void Handle_NotWorldReady_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"kind\":\"GreenSlime\",\"x\":12,\"y\":8}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            CombatLabSpawnMonsterHandler.Handle(p, new FakeCombatLabSpawnWorld { IsWorldReady = false }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_UnsupportedKind_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"kind\":\"CustomBoss\",\"x\":12,\"y\":8}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            CombatLabSpawnMonsterHandler.Handle(p, new FakeCombatLabSpawnWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("unsupported monster kind", ex.Message);
    }

    [Fact]
    public void Handle_DelegatesSpawnAndReturnsIdentity()
    {
        var world = new FakeCombatLabSpawnWorld();
        var p = JsonDocument.Parse("{\"kind\":\"GreenSlime\",\"label\":\"target\",\"x\":12,\"y\":8,\"health\":1}").RootElement;

        var result = CombatLabSpawnMonsterHandler.Handle(p, world);
        var json = result.GetRawText();

        Assert.Equal("GreenSlime", world.Kind);
        Assert.Equal("target", world.Label);
        Assert.Equal(12, world.X);
        Assert.Equal(8, world.Y);
        Assert.Equal(1, world.Health);
        Assert.Contains("\"monster_id\":\"frobby-monster-1\"", json);
        Assert.Contains("\"label\":\"target\"", json);
    }

    private sealed class FakeCombatLabSpawnWorld : ICombatLabSpawnWorld
    {
        public bool IsWorldReady { get; init; } = true;
        public string? Kind { get; private set; }
        public string? Label { get; private set; }
        public int X { get; private set; }
        public int Y { get; private set; }
        public int? Health { get; private set; }

        public CombatLabSpawnMonsterResult SpawnMonster(CombatLabSpawnMonsterRequest request)
        {
            Kind = request.Kind;
            Label = request.Label;
            X = request.X;
            Y = request.Y;
            Health = request.Health;
            return new CombatLabSpawnMonsterResult
            {
                MonsterId = "frobby-monster-1",
                Label = request.Label,
                Kind = request.Kind,
                Location = CombatLabResetHandler.LocationName,
                Tile = new TilePoint { X = request.X, Y = request.Y },
                Health = request.Health,
                MaxHealth = 24,
            };
        }
    }
}
```

- [ ] **Step 2: Run spawn tests and verify red**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter FullyQualifiedName~CombatLabSpawnMonsterHandlerTests -v minimal
```

Expected: fail to compile because the spawn handler and spawn world seam do not exist.

- [ ] **Step 3: Add spawn world interface and handler**

Create `src/Harness/Handlers/CombatLabSpawnMonsterHandler.cs`:

```csharp
using System.Text.Json;
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using StardewValley.Monsters;

namespace SdvTestFramework.Harness.Handlers;

public static class CombatLabSpawnMonsterHandler
{
    public const string Method = "combat_lab.spawn_monster";

    private static readonly ICombatLabSpawnWorld ProductionWorld = new SdvCombatLabSpawnWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, ICombatLabSpawnWorld world)
    {
        var req = RpcParams.Required<CombatLabSpawnMonsterRequest>(paramsElement);
        Validate(req);

        if (!world.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "no active save - combat_lab.spawn_monster requires a loaded world");

        return ProtocolJson.ToElement(world.SpawnMonster(req));
    }

    private static void Validate(CombatLabSpawnMonsterRequest req)
    {
        if (!IsSupportedKind(req.Kind))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                $"unsupported monster kind: {req.Kind}");
        if (req.X < 0 || req.Y < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "combat_lab.spawn_monster requires non-negative x and y");
        if (req.Health is <= 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "combat_lab.spawn_monster health must be positive when supplied");
    }

    internal static bool IsSupportedKind(string? kind)
        => kind is "GreenSlime" or "Bat";
}

internal interface ICombatLabSpawnWorld
{
    bool IsWorldReady { get; }
    CombatLabSpawnMonsterResult SpawnMonster(CombatLabSpawnMonsterRequest request);
}

internal sealed class SdvCombatLabSpawnWorld : ICombatLabSpawnWorld
{
    public bool IsWorldReady => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;

    public CombatLabSpawnMonsterResult SpawnMonster(CombatLabSpawnMonsterRequest request)
    {
        var lab = Game1.getLocationFromName(CombatLabResetHandler.LocationName)
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "combat_lab.spawn_monster requires combat_lab.reset first");

        var monster = CreateMonster(request);
        if (request.Health is { } health)
        {
            monster.Health = health;
            monster.MaxHealth = Math.Max(monster.MaxHealth, health);
        }

        monster.currentLocation = lab;
        lab.characters.Add(monster);

        var identity = CombatLabIdentityRegistry.Assign(monster, request.Label);
        return new CombatLabSpawnMonsterResult
        {
            MonsterId = identity.MonsterId,
            Label = identity.Label,
            Kind = request.Kind,
            Location = CombatLabResetHandler.LocationName,
            Tile = new TilePoint { X = request.X, Y = request.Y },
            Health = monster.Health,
            MaxHealth = monster.MaxHealth,
        };
    }

    private static Monster CreateMonster(CombatLabSpawnMonsterRequest request)
    {
        var position = new Vector2(request.X * 64, request.Y * 64);
        return request.Kind switch
        {
            "GreenSlime" => new GreenSlime(position, 0),
            "Bat" => new Bat(position, 0),
            _ => throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                $"unsupported monster kind: {request.Kind}"),
        };
    }
}
```

Add `using System;` if `Math` is missing.

- [ ] **Step 4: Register spawn RPC**

Modify `src/Harness/ModEntry.cs` near combat registration:

```csharp
_rpc.Register(CombatLabResetHandler.Method, p => CombatLabResetHandler.Handle(p));
_rpc.Register(CombatLabSpawnMonsterHandler.Method, p => CombatLabSpawnMonsterHandler.Handle(p));
_rpc.Register(CombatAttackHandler.Method, p => CombatAttackHandler.Handle(p));
```

Update the startup log Combat section to:

```text
Combat: combat_lab.reset, combat_lab.spawn_monster, combat.attack.
```

- [ ] **Step 5: Run spawn tests and verify green**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~CombatLabSpawnMonsterHandlerTests|FullyQualifiedName~CombatLabResetHandlerTests" -v minimal
```

Expected: pass.

- [ ] **Step 6: Build harness to catch Stardew constructor mistakes**

Run:

```bash
dotnet build src/Harness/Harness.csproj -v minimal
```

Expected: pass. If the installed Stardew assembly exposes a different constructor for `Bat`, replace `new Bat(position, 0)` with the constructor reported by the compiler and add a test note in the commit message.

- [ ] **Step 7: Commit spawn handler**

Run:

```bash
git add src/Harness/Handlers/CombatLabSpawnMonsterHandler.cs src/Harness/Handlers/CombatLabResetHandler.cs src/Harness/ModEntry.cs tests/Harness.Tests/CombatLabSpawnMonsterHandlerTests.cs
git commit -m "Add combat lab vanilla monster spawning"
```

## Task 5: Runner Targeting And Wait Filters By Identity

**Files:**
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`
- Modify: `tests/Runner.Tests/ScenarioRunnerTests.cs`

- [ ] **Step 1: Write failing runner tests for label filters and retargeting**

Add tests to `tests/Runner.Tests/ScenarioRunnerTests.cs` near the existing combat and location-content tests:

```csharp
[Fact]
public async Task CombatAttack_TargetSelectorMatchesMonsterLabelEachRepeat()
{
    var attacks = new List<string>();
    var locationPolls = 0;
    var session = new FakeJsonRpcSession((method, @params) =>
    {
        if (method == "combat.attack" && @params is { } p)
            attacks.Add(p.GetRawText());

        return method switch
        {
            "state.location" when locationPolls++ == 0 => JsonDocument.Parse("{\"name\":\"Frobby_CombatLab\",\"monsters\":[{\"monster_id\":\"frobby-monster-1\",\"label\":\"target\",\"tile\":{\"x\":12,\"y\":8},\"type\":\"GreenSlime\",\"health\":1}]}").RootElement,
            "state.location" => JsonDocument.Parse("{\"name\":\"Frobby_CombatLab\",\"monsters\":[{\"monster_id\":\"frobby-monster-1\",\"label\":\"target\",\"tile\":{\"x\":11,\"y\":8},\"type\":\"GreenSlime\",\"health\":1}]}").RootElement,
            "combat.attack" => JsonDocument.Parse("{\"ok\":true,\"tick\":1}").RootElement,
            _ => JsonDocument.Parse("{}").RootElement,
        };
    });
    var runner = new ScenarioRunner(session, new NoopReporter());

    var spec = new ScenarioSpec
    {
        Name = "combat_lab_label_retarget",
        Steps =
        [
            new ScenarioStep
            {
                Action = "combat.attack",
                Args = JsonDocument.Parse("{\"qualified_item_id\":\"(W)4\",\"repeat\":2,\"delay_ticks\":0,\"target\":{\"location\":\"Frobby_CombatLab\",\"label\":\"target\"}}").RootElement,
            },
        ],
    };

    var report = await runner.RunAsync(spec, CancellationToken.None);

    Assert.Empty(report.Failures);
    Assert.Equal(2, attacks.Count);
    Assert.Contains("\"x\":12", attacks[0]);
    Assert.Contains("\"x\":11", attacks[1]);
}

[Fact]
public async Task WaitLocationContent_FiltersMonstersByIdentityAndLabel()
{
    var session = new FakeJsonRpcSession((method, _) => method switch
    {
        "state.location" => JsonDocument.Parse("{\"name\":\"Frobby_CombatLab\",\"monsters\":[{\"monster_id\":\"frobby-monster-1\",\"label\":\"target\",\"tile\":{\"x\":12,\"y\":8},\"type\":\"GreenSlime\",\"health\":1}]}").RootElement,
        _ => JsonDocument.Parse("{}").RootElement,
    });
    var runner = new ScenarioRunner(session, new NoopReporter());

    var spec = new ScenarioSpec
    {
        Name = "wait_lab_monster_identity",
        Steps =
        [
            new ScenarioStep
            {
                Action = "wait.location_content",
                Args = JsonDocument.Parse("{\"location\":\"Frobby_CombatLab\",\"collection\":\"monsters\",\"monster_id\":\"frobby-monster-1\",\"label\":\"target\",\"min_count\":1,\"max_count\":1,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
            },
        ],
    };

    var report = await runner.RunAsync(spec, CancellationToken.None);

    Assert.Empty(report.Failures);
}
```

If the local test helpers use different constructor names than `FakeJsonRpcSession` or `NoopReporter`, copy the exact helper pattern from the surrounding tests in `ScenarioRunnerTests.cs`.

- [ ] **Step 2: Run runner tests and verify red**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~CombatAttack_TargetSelectorMatchesMonsterLabelEachRepeat|FullyQualifiedName~WaitLocationContent_FiltersMonstersByIdentityAndLabel" -v minimal
```

Expected: fail because `label` and `monster_id` are ignored by both target matching and wait filters.

- [ ] **Step 3: Add wait args and matching filters**

Modify `WaitLocationContentStepArgs` in `src/Runner/Scenarios/ScenarioRunner.cs`:

```csharp
public string? MonsterId { get; set; }
public string? Label { get; set; }
```

Add these fields near the other string filters.

Modify `LocationContentElementMatches` so the first filters include:

```csharp
return StringFilterMatches(element, "monster_id", args.MonsterId)
    && StringFilterMatches(element, "label", args.Label)
    && StringFilterMatches(element, "name", args.Name)
```

Modify `FormatLocationContentFilters` so identity appears in timeout text:

```csharp
if (args.MonsterId is not null) filters.Add($"monster_id={args.MonsterId}");
if (args.Label is not null) filters.Add($"label={args.Label}");
```

- [ ] **Step 4: Add combat target matching by identity**

Modify `CombatTargetMatches` in `src/Runner/Scenarios/ScenarioRunner.cs`:

```csharp
private static bool CombatTargetMatches(JsonElement monster, CombatTargetCriteria target)
{
    return StringFilterMatches(monster, "monster_id", target.MonsterId)
        && StringFilterMatches(monster, "label", target.Label)
        && StringFilterMatches(monster, "name", target.Name)
        && StringFilterMatches(monster, "type", target.Type)
        && StringFilterMatches(monster, "sprite_texture", target.SpriteTexture)
        && NumberFilterMatches(monster, "health", null, target.HealthLt, target.HealthLte, target.HealthGt, target.HealthGte)
        && TileFilterMatches(monster, target.X, target.Y);
}
```

- [ ] **Step 5: Add report labels for lab actions**

Modify the step label switch in `DescribeStep`:

```csharp
"combat_lab.reset" => "Reset Combat Lab",
"combat_lab.spawn_monster" => $"Spawn {GetStringArg(step.Args, "kind") ?? "monster"} in Combat Lab",
```

Place these before the fallback case.

- [ ] **Step 6: Run runner tests and verify green**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~CombatAttack_TargetSelectorMatchesMonsterLabelEachRepeat|FullyQualifiedName~WaitLocationContent_FiltersMonstersByIdentityAndLabel|FullyQualifiedName~WaitLocationContent_TimeoutIncludesMonsterMetadataFilters" -v minimal
```

Expected: pass.

- [ ] **Step 7: Commit runner identity targeting**

Run:

```bash
git add src/Runner/Scenarios/ScenarioRunner.cs tests/Runner.Tests/ScenarioRunnerTests.cs
git commit -m "Support combat lab identity targeting"
```

## Task 6: DSL Helpers

**Files:**
- Modify: `src/Runner.Dsl/Combat.cs`
- Create: `src/Runner.Dsl/CombatLab.cs`
- Modify: `tests/Runner.Dsl.Tests/Facets/CombatTests.cs`
- Create: `tests/Runner.Dsl.Tests/Facets/CombatLabTests.cs`

- [ ] **Step 1: Write failing DSL tests**

Add this test to `tests/Runner.Dsl.Tests/Facets/CombatTests.cs`:

```csharp
[Fact]
public async Task AttackTarget_InvokesCombatAttackWithLabel()
{
    SdvTestSession.ResetForTests();
    var inv = new CapturingInvoker();
    SdvTestSession.InitializeForTests(inv);
    try { await Combat.AttackTarget(label: "target", location: "Frobby_CombatLab", qualifiedItemId: "(W)4", repeat: 3, delayTicks: 1); }
    finally { SdvTestSession.ResetForTests(); }

    Assert.Single(inv.Calls);
    Assert.Equal("combat.attack", inv.Calls[0].Method);
    Assert.Contains("\"label\":\"target\"", inv.Calls[0].ParamsJson);
    Assert.Contains("\"location\":\"Frobby_CombatLab\"", inv.Calls[0].ParamsJson);
    Assert.Contains("\"qualified_item_id\":\"(W)4\"", inv.Calls[0].ParamsJson);
    Assert.Contains("\"repeat\":3", inv.Calls[0].ParamsJson);
}
```

Create `tests/Runner.Dsl.Tests/Facets/CombatLabTests.cs`:

```csharp
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Dsl;
using Xunit;

namespace SdvTestFramework.Runner.Dsl.Tests.Facets;

public sealed class CombatLabTests
{
    private sealed class CapturingInvoker : ISdvTestInvoker
    {
        public List<(string Method, string ParamsJson)> Calls { get; } = new();

        public Task<JsonElement> InvokeAsync(string method, JsonElement? @params, CancellationToken ct)
        {
            Calls.Add((method, @params?.GetRawText() ?? ""));
            return Task.FromResult(JsonDocument.Parse(
                method == "combat_lab.spawn_monster"
                    ? "{\"ok\":true,\"monster_id\":\"frobby-monster-1\",\"label\":\"target\",\"kind\":\"GreenSlime\",\"location\":\"Frobby_CombatLab\",\"tile\":{\"x\":12,\"y\":8},\"health\":1,\"max_health\":24}"
                    : "{\"ok\":true,\"location\":\"Frobby_CombatLab\",\"player_tile\":{\"x\":8,\"y\":8},\"map_width\":20,\"map_height\":14,\"cleared_monsters\":0,\"cleared_debris\":0}").RootElement.Clone());
        }
    }

    [Fact]
    public async Task Reset_InvokesCombatLabReset()
    {
        SdvTestSession.ResetForTests();
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try { await CombatLab.Reset(playerX: 8, playerY: 8); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Single(inv.Calls);
        Assert.Equal("combat_lab.reset", inv.Calls[0].Method);
        Assert.Contains("\"player_x\":8", inv.Calls[0].ParamsJson);
    }

    [Fact]
    public async Task SpawnMonster_InvokesCombatLabSpawnMonsterAndReturnsResult()
    {
        SdvTestSession.ResetForTests();
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        CombatLabSpawnMonsterResult result;
        try { result = await CombatLab.SpawnMonster("GreenSlime", "target", 12, 8, health: 1); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Single(inv.Calls);
        Assert.Equal("combat_lab.spawn_monster", inv.Calls[0].Method);
        Assert.Contains("\"kind\":\"GreenSlime\"", inv.Calls[0].ParamsJson);
        Assert.Equal("frobby-monster-1", result.MonsterId);
    }
}
```

- [ ] **Step 2: Run DSL tests and verify red**

Run:

```bash
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter "FullyQualifiedName~CombatLabTests|FullyQualifiedName~AttackTarget_InvokesCombatAttackWithLabel" -v minimal
```

Expected: fail to compile because `CombatLab` and `Combat.AttackTarget` do not exist.

- [ ] **Step 3: Add Combat.AttackTarget**

Modify `src/Runner.Dsl/Combat.cs`:

```csharp
/// <summary>Attack a monster selected from current location state by identity or metadata.</summary>
public static async Task AttackTarget(
    string? monsterId = null,
    string? label = null,
    string? location = null,
    string? type = null,
    string? qualifiedItemId = null,
    int repeat = 1,
    int delayTicks = 0,
    CancellationToken ct = default)
{
    var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
    var p = JsonSerializer.SerializeToElement(new CombatAttackRequest
    {
        QualifiedItemId = qualifiedItemId,
        Repeat = repeat,
        DelayTicks = delayTicks,
        Target = new CombatTargetCriteria
        {
            MonsterId = monsterId,
            Label = label,
            Location = location,
            Type = type,
        },
    }, ProtocolJson.Options);
    await s.InvokeAsync("combat.attack", p, ct);
}
```

- [ ] **Step 4: Add CombatLab DSL**

Create `src/Runner.Dsl/CombatLab.cs`:

```csharp
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>Ambient static DSL for the test-only Frobby Combat Lab.</summary>
public static class CombatLab
{
    public static async Task<CombatLabResetResult> Reset(
        int playerX = 8,
        int playerY = 8,
        int width = 20,
        int height = 14,
        bool warpPlayer = true,
        CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new CombatLabResetRequest
        {
            PlayerX = playerX,
            PlayerY = playerY,
            Width = width,
            Height = height,
            WarpPlayer = warpPlayer,
        }, ProtocolJson.Options);
        var resp = await s.InvokeAsync("combat_lab.reset", p, ct);
        return JsonSerializer.Deserialize<CombatLabResetResult>(resp, ProtocolJson.Options)
            ?? throw new SdvRpcException("combat_lab.reset", JsonRpcErrorCode.InternalError,
                "empty combat_lab.reset response");
    }

    public static async Task<CombatLabSpawnMonsterResult> SpawnMonster(
        string kind,
        string? label,
        int x,
        int y,
        int? health = null,
        CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new CombatLabSpawnMonsterRequest
        {
            Kind = kind,
            Label = label,
            X = x,
            Y = y,
            Health = health,
        }, ProtocolJson.Options);
        var resp = await s.InvokeAsync("combat_lab.spawn_monster", p, ct);
        return JsonSerializer.Deserialize<CombatLabSpawnMonsterResult>(resp, ProtocolJson.Options)
            ?? throw new SdvRpcException("combat_lab.spawn_monster", JsonRpcErrorCode.InternalError,
                "empty combat_lab.spawn_monster response");
    }
}
```

- [ ] **Step 5: Run DSL tests and verify green**

Run:

```bash
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter "FullyQualifiedName~CombatLabTests|FullyQualifiedName~AttackTarget_InvokesCombatAttackWithLabel" -v minimal
```

Expected: pass.

- [ ] **Step 6: Commit DSL helpers**

Run:

```bash
git add src/Runner.Dsl/Combat.cs src/Runner.Dsl/CombatLab.cs tests/Runner.Dsl.Tests/Facets/CombatTests.cs tests/Runner.Dsl.Tests/Facets/CombatLabTests.cs
git commit -m "Add combat lab DSL helpers"
```

## Task 7: SVE Scenario 27 Proof

**Files:**
- Create: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/27-sve-combat-lab-vanilla-monster.test.json`
- Modify: `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`

- [ ] **Step 1: Add SVE scenario 27**

Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/27-sve-combat-lab-vanilla-monster.test.json`:

```json
{
  "name": "sve_combat_lab_vanilla_monster",
  "fixture": "m0spike_436515781",
  "config": { "seed": 436515781 },
  "steps": [
    {
      "action": "time.set",
      "args": { "time": 900, "day": 1, "season": "spring", "year": 1 }
    },
    {
      "action": "player.give_item",
      "args": { "id": "(W)4", "count": 1 }
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
      "action": "combat_lab.spawn_monster",
      "args": {
        "kind": "GreenSlime",
        "label": "target",
        "x": 9,
        "y": 8,
        "health": 1
      }
    },
    {
      "action": "wait.location_content",
      "args": {
        "location": "Frobby_CombatLab",
        "collection": "monsters",
        "label": "target",
        "type": "GreenSlime",
        "health": 1,
        "min_count": 1,
        "max_count": 1,
        "timeout_ms": 5000,
        "poll_ms": 100
      }
    },
    {
      "action": "combat.attack",
      "args": {
        "qualified_item_id": "(W)4",
        "repeat": 3,
        "delay_ticks": 8,
        "target": {
          "location": "Frobby_CombatLab",
          "label": "target"
        }
      }
    },
    {
      "action": "wait.location_content",
      "args": {
        "location": "Frobby_CombatLab",
        "collection": "monsters",
        "label": "target",
        "max_count": 0,
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
      "expr": "state.player.location == 'Frobby_CombatLab'",
      "message": "Combat Lab scenario should finish inside the Frobby combat dev room"
    }
  ]
}
```

- [ ] **Step 2: Update SVE Frobby docs**

Modify `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md` by adding a scenario entry:

```markdown
- `tests/sdv/27-sve-combat-lab-vanilla-monster.test.json` proves Frobby's neutral Combat Lab against the SVE core profile by spawning a vanilla `GreenSlime`, attacking by lab label, and waiting for that exact monster to be removed.
```

- [ ] **Step 3: Commit SVE scenario**

Run from `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
git add tests/sdv/27-sve-combat-lab-vanilla-monster.test.json docs/FROBBY.md
git commit -m "Add Frobby Combat Lab scenario"
```

## Task 8: Documentation And Backlog Status

**Files:**
- Modify: `docs/rpc-schema.md`
- Modify: `docs/dsl-quickstart.md`
- Modify: `docs/wiki/examples.md`
- Modify: `docs/wiki/index.md`
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Update RPC docs**

In `docs/rpc-schema.md`, add a Combat Lab section near `combat.attack`:

````markdown
### combat_lab.reset

Creates or resets the test-only `Frobby_CombatLab` location. This is a neutral dev room for combat tests; it is active only in harness-driven test runs and should not be used by production mods.

Request:
```json
{ "player_x": 8, "player_y": 8, "width": 20, "height": 14, "warp_player": true }
```

Response:
```json
{
  "ok": true,
  "location": "Frobby_CombatLab",
  "player_tile": { "x": 8, "y": 8 },
  "map_width": 20,
  "map_height": 14,
  "cleared_monsters": 0,
  "cleared_debris": 0
}
```

### combat_lab.spawn_monster

Spawns a supported vanilla monster in `Frobby_CombatLab` and assigns a run-local identity. Supported first-slice kinds are `GreenSlime` and `Bat`.

Request:
```json
{ "kind": "GreenSlime", "label": "target", "x": 9, "y": 8, "health": 1 }
```

Response:
```json
{
  "ok": true,
  "monster_id": "frobby-monster-1",
  "label": "target",
  "kind": "GreenSlime",
  "location": "Frobby_CombatLab",
  "tile": { "x": 9, "y": 8 },
  "health": 1,
  "max_health": 24
}
```
````

Also update the `state.location` monster example to show `monster_id`, `label`, and `spawned_by_frobby`.

- [ ] **Step 2: Update DSL quickstart combat section**

In `docs/dsl-quickstart.md`, add after the moving-target combat example:

````markdown
For isolated combat hardening, use the Combat Lab. It creates a clean test-only arena and lets JSON scenarios target a specific monster by lab label:

```json
{ "action": "combat_lab.reset", "args": { "player_x": 8, "player_y": 8, "warp_player": true } }
```

```json
{ "action": "combat_lab.spawn_monster", "args": { "kind": "GreenSlime", "label": "target", "x": 9, "y": 8, "health": 1 } }
```

```json
{
  "action": "combat.attack",
  "args": {
    "qualified_item_id": "(W)4",
    "repeat": 3,
    "target": { "location": "Frobby_CombatLab", "label": "target" }
  }
}
```
````

- [ ] **Step 3: Update wiki docs**

In `docs/wiki/examples.md`, add:

```markdown
- Combat Lab vanilla monster lifecycle:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/27-sve-combat-lab-vanilla-monster.test.json`
```

In `docs/wiki/index.md`, add Combat Lab to the combat capability sentence:

```markdown
Combat coverage includes player-like melee attacks, monster/debris state, and the test-only Combat Lab for isolated monster identity/removal checks.
```

- [ ] **Step 4: Mark Slice 19 Active before live verification**

In `SVE_FROBBY_CAPABILITY_TODO.md`, change the Slice 19 header:

```markdown
- [ ] Active: Slice 19, vanilla-first Combat Lab for monster identity and lifecycle hardening.
```

Add an implementation plan line:

```markdown
  - Implementation plan: `docs/superpowers/plans/2026-05-19-sve-slice-19-combat-lab.md`.
```

- [ ] **Step 5: Commit docs**

Run:

```bash
git add docs/rpc-schema.md docs/dsl-quickstart.md docs/wiki/examples.md docs/wiki/index.md SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "Document combat lab testing flow"
```

## Task 9: Full Verification And Completion

**Files:**
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Run focused Frobby tests**

Run from `/home/fintan/stardewRepos/frobby/sdv-test-framework`:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter "FullyQualifiedName~CombatLab|FullyQualifiedName~LocationStateSerializationTests" -v minimal
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~CombatLab|FullyQualifiedName~LocationContentProjectorTests|FullyQualifiedName~CombatAttackHandlerTests" -v minimal
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~CombatAttack_TargetSelector|FullyQualifiedName~WaitLocationContent" -v minimal
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter "FullyQualifiedName~CombatLabTests|FullyQualifiedName~CombatTests" -v minimal
```

Expected: all pass.

- [ ] **Step 2: Run full Frobby build**

Run:

```bash
dotnet build sdv-test-framework.slnx -v minimal
```

Expected: build succeeds with 0 errors.

- [ ] **Step 3: Run SVE scenario 27 headlessly**

Run from `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework ./scripts/sdv-test --headless --no-build --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-19-combat-lab tests/sdv/27-sve-combat-lab-vanilla-monster.test.json
```

Expected: `1/1 passed` and an HTML report under `/tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-19-combat-lab/27-sve-combat-lab-vanilla-monster/index.html`.

If the command fails only under the Codex sandbox with a cache write or silent `dotnet run` build failure, rerun the same command with escalated execution and record that in the final verification notes.

- [ ] **Step 4: Run closest SVE combat regression**

Run from `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework ./scripts/sdv-test --headless --no-build --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-19-combat-regression tests/sdv/12-sve-combat-monster-damage.test.json
```

Expected: `1/1 passed`.

- [ ] **Step 5: Mark Slice 19 Done**

Modify `SVE_FROBBY_CAPABILITY_TODO.md`:

```markdown
- [x] Done: Slice 19, vanilla-first Combat Lab for monster identity and lifecycle hardening.
  - SVE pressure: existing combat scenarios can prove matching monster state changes, but crowded or moving combat locations make it hard to prove a specific monster instance was removed.
  - Frobby goal: add a neutral test-only combat dev room that can reset a clean arena, spawn vanilla monsters, assign stable run-local monster identities, and let scenarios attack/wait by identity or lab label.
  - Design spec: `docs/superpowers/specs/2026-05-19-sve-slice-19-combat-lab-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-19-sve-slice-19-combat-lab.md`.
  - Done: `combat_lab.reset`, `combat_lab.spawn_monster`, run-local monster identity fields, runner target/wait filters by `monster_id` and `label`, DSL helpers, docs, and SVE scenario 27.
  - Verified: SVE scenario 27 resets `Frobby_CombatLab`, spawns a vanilla `GreenSlime`, attacks by lab label, and waits for that exact monster to be removed.
  - Follow-up candidate: add mod monster support after researching stable SVE custom monster construction or relocation.
```

- [ ] **Step 6: Commit final Frobby completion**

Run:

```bash
git add SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "Complete SVE combat lab slice"
```

- [ ] **Step 7: Confirm clean worktrees**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
```

Expected: both show only the branch header, with no dirty files.

## Self-Review

- Spec coverage: the plan covers Combat Lab reset, vanilla monster spawn, identity projection, attack/wait by identity or label, SVE scenario proof, docs, and defers mod monster support as specified.
- Type consistency: protocol fields use `MonsterId`/`monster_id`, `Label`/`label`, and `SpawnedByFrobby`/`spawned_by_frobby` consistently across DTOs, projection, runner filters, and scenario JSON.
- Scope control: the plan does not add direct kill, direct damage, direct loot spawning, or custom SVE monster construction.
- Verification: focused unit tests, full build, SVE scenario 27, and SVE combat scenario 12 are required before marking the slice done.
