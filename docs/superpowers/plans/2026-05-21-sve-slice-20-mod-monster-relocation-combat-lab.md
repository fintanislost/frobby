# SVE Slice 20 Mod Monster Relocation Combat Lab Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a neutral Combat Lab relocation flow that moves one already-spawned runtime monster into `Frobby_CombatLab`, assigns a run-local identity/label, and proves exact removal against an SVE/FTM monster.

**Architecture:** Keep direct mod monster construction out of scope. Add protocol DTOs and a harness-side `combat_lab.relocate_monster` handler that matches source monsters through projected `state.location.monsters` metadata, validates before mutation, moves the real monster object into the lab, and marks it as Frobby-bound but not Frobby-spawned. Existing runner identity waits and `combat.attack` targeting then work unchanged against the relocated monster.

**Tech Stack:** C#/.NET 10 runner and DSL, net6.0 SMAPI harness, Stardew Valley 1.6 runtime types, JSON-RPC protocol DTOs, xUnit, JSON scenario files, headless `sdv-test` repo runs.

---

## File Structure

Frobby protocol:

- Modify `src/Protocol/Models/CombatLabRequests.cs`
  - Add `CombatLabMonsterMatchCriteria`.
  - Add `CombatLabRelocateMonsterRequest`.
  - Add `CombatLabRelocateMonsterResult`.
- Modify `tests/Protocol.Tests/CombatLabSerializationTests.cs`
  - Add snake-case serialization tests for relocate request/result.

Frobby harness:

- Modify `src/Harness/Handlers/CombatLabIdentityRegistry.cs`
  - Let callers specify whether a bound monster was spawned by Frobby.
- Modify `tests/Harness.Tests/CombatLabIdentityRegistryTests.cs`
  - Cover relocated identity semantics.
- Create `src/Harness/Handlers/CombatLabMonsterMatcher.cs`
  - Match `MonsterSummary` against `CombatLabMonsterMatchCriteria`.
- Create `tests/Harness.Tests/CombatLabMonsterMatcherTests.cs`
  - Cover exact filters and mismatch behavior.
- Create `src/Harness/Handlers/CombatLabRelocateMonsterHandler.cs`
  - Implement `combat_lab.relocate_monster`.
- Create `tests/Harness.Tests/CombatLabRelocateMonsterHandlerTests.cs`
  - Cover validation, matching, no/multiple matches, mutation order, identity semantics, and result fields.
- Modify `src/Harness/ModEntry.cs`
  - Register the new RPC and update the startup method list.

Frobby runner and DSL:

- Modify `src/Runner/Scenarios/ScenarioRunner.cs`
  - Add readable step detail for `combat_lab.relocate_monster`.
- Modify `tests/Runner.Tests/ScenarioRunnerTests.cs`
  - Add a pass-through/report-label test for the scenario action.
- Modify `src/Runner.Dsl/CombatLab.cs`
  - Add `RelocateMonster`.
- Modify `tests/Runner.Dsl.Tests/Facets/CombatLabTests.cs`
  - Add wrapper shape and typed result coverage.

Frobby docs/status:

- Modify `docs/rpc-schema.md`
- Modify `docs/dsl-quickstart.md`
- Modify `docs/wiki/examples.md`
- Modify `SVE_FROBBY_CAPABILITY_TODO.md`

SVE:

- Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/28-sve-combat-lab-relocate-mod-monster.test.json`
- Modify `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`

## Task 1: Protocol DTOs For Monster Relocation

**Files:**
- Modify: `tests/Protocol.Tests/CombatLabSerializationTests.cs`
- Modify: `src/Protocol/Models/CombatLabRequests.cs`

- [ ] **Step 1: Write failing protocol serialization tests**

Append these tests to `tests/Protocol.Tests/CombatLabSerializationTests.cs`:

```csharp
[Fact]
public void RelocateMonsterRequest_SerializesSnakeCaseFields()
{
    var json = JsonSerializer.Serialize(new CombatLabRelocateMonsterRequest
    {
        FromLocation = "Custom_CrimsonBadlands",
        Label = "corrupt-mummy",
        TargetX = 9,
        TargetY = 8,
        Match = new CombatLabMonsterMatchCriteria
        {
            X = 20,
            Y = 144,
            SpriteTexture = "Characters/Monsters/CorruptMummy",
            Health = 2000,
            MaxHealth = 2000,
            Damage = 100,
        },
    }, ProtocolJson.Options);

    Assert.Contains("\"from_location\":\"Custom_CrimsonBadlands\"", json);
    Assert.Contains("\"label\":\"corrupt-mummy\"", json);
    Assert.Contains("\"target_x\":9", json);
    Assert.Contains("\"target_y\":8", json);
    Assert.Contains("\"match\":", json);
    Assert.Contains("\"sprite_texture\":\"Characters/Monsters/CorruptMummy\"", json);
    Assert.Contains("\"max_health\":2000", json);
}

[Fact]
public void RelocateMonsterResult_SerializesIdentityAndSourceFields()
{
    var json = JsonSerializer.Serialize(new CombatLabRelocateMonsterResult
    {
        Ok = true,
        MonsterId = "frobby-monster-1",
        Label = "corrupt-mummy",
        FromLocation = "Custom_CrimsonBadlands",
        SourceTile = new TilePoint { X = 20, Y = 144 },
        Location = "Frobby_CombatLab",
        Tile = new TilePoint { X = 9, Y = 8 },
        Name = "Mummy",
        Type = "Mummy",
        SpriteTexture = "Characters/Monsters/CorruptMummy",
        Health = 2000,
        MaxHealth = 2000,
    }, ProtocolJson.Options);

    Assert.Contains("\"monster_id\":\"frobby-monster-1\"", json);
    Assert.Contains("\"label\":\"corrupt-mummy\"", json);
    Assert.Contains("\"from_location\":\"Custom_CrimsonBadlands\"", json);
    Assert.Contains("\"source_tile\":{\"x\":20,\"y\":144}", json);
    Assert.Contains("\"location\":\"Frobby_CombatLab\"", json);
    Assert.Contains("\"tile\":{\"x\":9,\"y\":8}", json);
    Assert.Contains("\"sprite_texture\":\"Characters/Monsters/CorruptMummy\"", json);
}
```

- [ ] **Step 2: Run protocol tests and verify red**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter "FullyQualifiedName~CombatLabSerializationTests" -v minimal
```

Expected: compile failure because `CombatLabRelocateMonsterRequest`, `CombatLabMonsterMatchCriteria`, and `CombatLabRelocateMonsterResult` do not exist.

- [ ] **Step 3: Add protocol DTOs**

Append these models to `src/Protocol/Models/CombatLabRequests.cs` after `CombatLabSpawnMonsterResult`:

```csharp
/// <summary>Neutral monster filters used by <c>combat_lab.relocate_monster</c>.</summary>
public sealed class CombatLabMonsterMatchCriteria
{
    public int? X { get; set; }
    public int? Y { get; set; }
    public string? MonsterId { get; set; }
    public string? Label { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? SpriteTexture { get; set; }
    public int? Health { get; set; }
    public int? MaxHealth { get; set; }
    public int? Damage { get; set; }
}

/// <summary>Request shape of <c>combat_lab.relocate_monster</c>.</summary>
public sealed class CombatLabRelocateMonsterRequest
{
    public string FromLocation { get; set; } = string.Empty;
    public string? Label { get; set; }
    public int TargetX { get; set; }
    public int TargetY { get; set; }
    public CombatLabMonsterMatchCriteria Match { get; set; } = new();
}

/// <summary>Response shape of <c>combat_lab.relocate_monster</c>.</summary>
public sealed class CombatLabRelocateMonsterResult
{
    public bool Ok { get; set; } = true;
    public string MonsterId { get; set; } = string.Empty;
    public string? Label { get; set; }
    public string FromLocation { get; set; } = string.Empty;
    public TilePoint SourceTile { get; set; } = new();
    public string Location { get; set; } = string.Empty;
    public TilePoint Tile { get; set; } = new();
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? SpriteTexture { get; set; }
    public int? Health { get; set; }
    public int? MaxHealth { get; set; }
}
```

- [ ] **Step 4: Run protocol tests and verify green**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter "FullyQualifiedName~CombatLabSerializationTests" -v minimal
```

Expected: all `CombatLabSerializationTests` pass.

- [ ] **Step 5: Commit protocol DTOs**

Run:

```bash
git add src/Protocol/Models/CombatLabRequests.cs tests/Protocol.Tests/CombatLabSerializationTests.cs
git commit -m "Add combat lab relocate protocol models"
```

## Task 2: Identity Registry Supports Relocated Monsters

**Files:**
- Modify: `tests/Harness.Tests/CombatLabIdentityRegistryTests.cs`
- Modify: `src/Harness/Handlers/CombatLabIdentityRegistry.cs`
- Modify: `tests/Harness.Tests/LocationContentProjectorTests.cs`

- [ ] **Step 1: Write failing identity tests**

Append this test to `tests/Harness.Tests/CombatLabIdentityRegistryTests.cs`:

```csharp
[Fact]
public void Assign_CanMarkMonsterAsRelocatedInsteadOfSpawned()
{
    CombatLabIdentityRegistry.Clear();
    var monster = FormatterServices.GetUninitializedObject(typeof(StardewValley.Monsters.GreenSlime));

    var identity = CombatLabIdentityRegistry.Assign(monster, "corrupt-mummy", spawnedByFrobby: false);
    var renamed = CombatLabIdentityRegistry.Assign(monster, "renamed", spawnedByFrobby: true);

    Assert.Equal("frobby-monster-1", identity.MonsterId);
    Assert.Equal("corrupt-mummy", identity.Label);
    Assert.False(identity.SpawnedByFrobby);
    Assert.Equal(identity.MonsterId, renamed.MonsterId);
    Assert.Equal("renamed", renamed.Label);
    Assert.False(renamed.SpawnedByFrobby);
}
```

Add this test to `tests/Harness.Tests/LocationContentProjectorTests.cs` after `ProjectMonster_IncludesCombatLabIdentityWhenAssigned`:

```csharp
[Fact]
public void ProjectMonster_IncludesRelocatedCombatLabIdentityAsNotSpawned()
{
    CombatLabIdentityRegistry.Clear();
    var monster = new GreenSlime
    {
        tile = new Vector2(9, 8),
        Name = "Mummy",
    };

    CombatLabIdentityRegistry.Assign(monster, "corrupt-mummy", spawnedByFrobby: false);

    var summary = LocationContentProjector.ProjectMonsterForTests(monster);

    Assert.Equal("frobby-monster-1", summary.MonsterId);
    Assert.Equal("corrupt-mummy", summary.Label);
    Assert.False(summary.SpawnedByFrobby);
}
```

- [ ] **Step 2: Run identity tests and verify red**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~CombatLabIdentityRegistryTests|FullyQualifiedName~ProjectMonster_IncludesRelocatedCombatLabIdentityAsNotSpawned" -v minimal
```

Expected: compile failure because `CombatLabIdentityRegistry.Assign` does not accept `spawnedByFrobby`.

- [ ] **Step 3: Update identity registry**

Modify `src/Harness/Handlers/CombatLabIdentityRegistry.cs` so `Assign` accepts an optional spawned flag and preserves the original spawned flag when relabeling:

```csharp
internal static CombatLabMonsterIdentity Assign(
    object monster,
    string? label,
    bool spawnedByFrobby = true)
{
    ArgumentNullException.ThrowIfNull(monster);

    lock (Gate)
    {
        if (Identities.TryGetValue(monster, out var existing))
        {
            if (label is null || string.Equals(existing.Label, label, StringComparison.Ordinal))
                return existing;

            var renamed = existing with { Label = label };
            Identities[monster] = renamed;
            return renamed;
        }

        var identity = new CombatLabMonsterIdentity(
            $"frobby-monster-{++nextId}",
            label,
            spawnedByFrobby);
        Identities.Add(monster, identity);
        return identity;
    }
}
```

Existing calls to `Assign(monster, label)` keep `spawnedByFrobby: true`.

- [ ] **Step 4: Run identity tests and verify green**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~CombatLabIdentityRegistryTests|FullyQualifiedName~ProjectMonster_IncludesRelocatedCombatLabIdentityAsNotSpawned" -v minimal
```

Expected: selected tests pass.

- [ ] **Step 5: Commit identity semantics**

Run:

```bash
git add src/Harness/Handlers/CombatLabIdentityRegistry.cs tests/Harness.Tests/CombatLabIdentityRegistryTests.cs tests/Harness.Tests/LocationContentProjectorTests.cs
git commit -m "Track relocated combat lab identities"
```

## Task 3: Shared Monster Match Criteria

**Files:**
- Create: `src/Harness/Handlers/CombatLabMonsterMatcher.cs`
- Create: `tests/Harness.Tests/CombatLabMonsterMatcherTests.cs`

- [ ] **Step 1: Write failing matcher tests**

Create `tests/Harness.Tests/CombatLabMonsterMatcherTests.cs`:

```csharp
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public sealed class CombatLabMonsterMatcherTests
{
    [Fact]
    public void Matches_AllSuppliedFilters()
    {
        var summary = new MonsterSummary
        {
            Tile = new TilePoint { X = 20, Y = 144 },
            MonsterId = "frobby-monster-7",
            Label = "source",
            Name = "Mummy",
            Type = "Mummy",
            SpriteTexture = "Characters/Monsters/CorruptMummy",
            Health = 2000,
            MaxHealth = 2000,
            Damage = 100,
        };
        var match = new CombatLabMonsterMatchCriteria
        {
            X = 20,
            Y = 144,
            MonsterId = "frobby-monster-7",
            Label = "source",
            Name = "Mummy",
            Type = "Mummy",
            SpriteTexture = "Characters/Monsters/CorruptMummy",
            Health = 2000,
            MaxHealth = 2000,
            Damage = 100,
        };

        Assert.True(CombatLabMonsterMatcher.Matches(summary, match));
    }

    [Theory]
    [InlineData("type")]
    [InlineData("sprite")]
    [InlineData("health")]
    [InlineData("tile")]
    public void Matches_ReturnsFalseForMismatchedFilters(string mismatch)
    {
        var summary = new MonsterSummary
        {
            Tile = new TilePoint { X = 20, Y = 144 },
            Type = "Mummy",
            SpriteTexture = "Characters/Monsters/CorruptMummy",
            Health = 2000,
        };
        var match = new CombatLabMonsterMatchCriteria
        {
            X = mismatch == "tile" ? 21 : 20,
            Y = 144,
            Type = mismatch == "type" ? "ShadowBrute" : "Mummy",
            SpriteTexture = mismatch == "sprite" ? "Other/Sprite" : "Characters/Monsters/CorruptMummy",
            Health = mismatch == "health" ? 1999 : 2000,
        };

        Assert.False(CombatLabMonsterMatcher.Matches(summary, match));
    }

    [Fact]
    public void HasAnyFilter_ReturnsTrueOnlyWhenAFilterIsSet()
    {
        Assert.False(CombatLabMonsterMatcher.HasAnyFilter(new CombatLabMonsterMatchCriteria()));
        Assert.True(CombatLabMonsterMatcher.HasAnyFilter(new CombatLabMonsterMatchCriteria { SpriteTexture = "Characters/Monsters/CorruptMummy" }));
    }

    [Fact]
    public void Describe_IncludesSuppliedFilters()
    {
        var text = CombatLabMonsterMatcher.Describe(new CombatLabMonsterMatchCriteria
        {
            X = 20,
            Y = 144,
            SpriteTexture = "Characters/Monsters/CorruptMummy",
            Health = 2000,
        });

        Assert.Contains("x=20", text);
        Assert.Contains("y=144", text);
        Assert.Contains("sprite_texture=Characters/Monsters/CorruptMummy", text);
        Assert.Contains("health=2000", text);
    }
}
```

- [ ] **Step 2: Run matcher tests and verify red**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter FullyQualifiedName~CombatLabMonsterMatcherTests -v minimal
```

Expected: compile failure because `CombatLabMonsterMatcher` does not exist.

- [ ] **Step 3: Add matcher implementation**

Create `src/Harness/Handlers/CombatLabMonsterMatcher.cs`:

```csharp
using System;
using System.Collections.Generic;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Handlers;

internal static class CombatLabMonsterMatcher
{
    public static bool HasAnyFilter(CombatLabMonsterMatchCriteria match)
    {
        ArgumentNullException.ThrowIfNull(match);

        return match.X is not null
            || match.Y is not null
            || match.MonsterId is not null
            || match.Label is not null
            || match.Name is not null
            || match.Type is not null
            || match.SpriteTexture is not null
            || match.Health is not null
            || match.MaxHealth is not null
            || match.Damage is not null;
    }

    public static bool Matches(MonsterSummary summary, CombatLabMonsterMatchCriteria match)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(match);

        return NumberMatches(summary.Tile.X, match.X)
            && NumberMatches(summary.Tile.Y, match.Y)
            && StringMatches(summary.MonsterId, match.MonsterId)
            && StringMatches(summary.Label, match.Label)
            && StringMatches(summary.Name, match.Name)
            && StringMatches(summary.Type, match.Type)
            && StringMatches(summary.SpriteTexture, match.SpriteTexture)
            && NumberMatches(summary.Health, match.Health)
            && NumberMatches(summary.MaxHealth, match.MaxHealth)
            && NumberMatches(summary.Damage, match.Damage);
    }

    public static string Describe(CombatLabMonsterMatchCriteria match)
    {
        ArgumentNullException.ThrowIfNull(match);

        var parts = new List<string>();
        Add(parts, "x", match.X);
        Add(parts, "y", match.Y);
        Add(parts, "monster_id", match.MonsterId);
        Add(parts, "label", match.Label);
        Add(parts, "name", match.Name);
        Add(parts, "type", match.Type);
        Add(parts, "sprite_texture", match.SpriteTexture);
        Add(parts, "health", match.Health);
        Add(parts, "max_health", match.MaxHealth);
        Add(parts, "damage", match.Damage);
        return parts.Count == 0 ? "(no filters)" : string.Join(", ", parts);
    }

    private static bool StringMatches(string? actual, string? expected)
        => expected is null || string.Equals(actual, expected, StringComparison.Ordinal);

    private static bool NumberMatches(int? actual, int? expected)
        => expected is null || actual == expected;

    private static void Add(List<string> parts, string name, object? value)
    {
        if (value is not null)
            parts.Add($"{name}={value}");
    }
}
```

- [ ] **Step 4: Run matcher tests and verify green**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter FullyQualifiedName~CombatLabMonsterMatcherTests -v minimal
```

Expected: matcher tests pass.

- [ ] **Step 5: Commit matcher**

Run:

```bash
git add src/Harness/Handlers/CombatLabMonsterMatcher.cs tests/Harness.Tests/CombatLabMonsterMatcherTests.cs
git commit -m "Add combat lab monster match criteria"
```

## Task 4: Relocate Monster Harness Handler

**Files:**
- Create: `src/Harness/Handlers/CombatLabRelocateMonsterHandler.cs`
- Create: `tests/Harness.Tests/CombatLabRelocateMonsterHandlerTests.cs`
- Modify: `src/Harness/ModEntry.cs`

- [ ] **Step 1: Write failing handler tests**

Create `tests/Harness.Tests/CombatLabRelocateMonsterHandlerTests.cs`:

```csharp
using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public sealed class CombatLabRelocateMonsterHandlerTests
{
    [Fact]
    public void Handle_NotWorldReady_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("""{"from_location":"Custom_CrimsonBadlands","target_x":9,"target_y":8,"match":{"type":"Mummy"}}""").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            CombatLabRelocateMonsterHandler.Handle(p, new FakeRelocateWorld { IsWorldReady = false }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Theory]
    [InlineData("""{"target_x":9,"target_y":8,"match":{"type":"Mummy"}}""", "from_location")]
    [InlineData("""{"from_location":"Custom_CrimsonBadlands","target_x":-1,"target_y":8,"match":{"type":"Mummy"}}""", "target")]
    [InlineData("""{"from_location":"Custom_CrimsonBadlands","target_x":9,"target_y":8,"match":{}}""", "match")]
    public void Handle_InvalidParams_ThrowsInvalidParams(string json, string messagePart)
    {
        var p = JsonDocument.Parse(json).RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            CombatLabRelocateMonsterHandler.Handle(p, new FakeRelocateWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains(messagePart, ex.Message);
    }

    [Fact]
    public void Handle_DelegatesRelocationAndReturnsIdentity()
    {
        var world = new FakeRelocateWorld();
        var p = JsonDocument.Parse("""{"from_location":"Custom_CrimsonBadlands","label":"corrupt-mummy","target_x":9,"target_y":8,"match":{"x":20,"y":144,"sprite_texture":"Characters/Monsters/CorruptMummy"}}""").RootElement;

        var result = CombatLabRelocateMonsterHandler.Handle(p, world);
        var json = result.GetRawText();

        Assert.Equal("Custom_CrimsonBadlands", world.Request!.FromLocation);
        Assert.Equal("corrupt-mummy", world.Request.Label);
        Assert.Equal(9, world.Request.TargetX);
        Assert.Equal(8, world.Request.TargetY);
        Assert.Equal("Characters/Monsters/CorruptMummy", world.Request.Match.SpriteTexture);
        Assert.Contains("\"monster_id\":\"frobby-monster-1\"", json);
        Assert.Contains("\"label\":\"corrupt-mummy\"", json);
        Assert.Contains("\"from_location\":\"Custom_CrimsonBadlands\"", json);
    }

    [Fact]
    public void ValidateTargetTileAgainstMap_OutsideActualMap_ThrowsInvalidParams()
    {
        var req = new CombatLabRelocateMonsterRequest
        {
            FromLocation = "Custom_CrimsonBadlands",
            TargetX = 120,
            TargetY = 8,
            Match = new CombatLabMonsterMatchCriteria { Type = "Mummy" },
        };

        var ex = Assert.Throws<JsonRpcException>(() =>
            SdvCombatLabRelocateWorld.ValidateTargetTileAgainstMap(req, mapWidth: 120, mapHeight: 60));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("map bounds", ex.Message);
    }

    [Fact]
    public void RelocatePreparedMonster_NoMatches_ThrowsGameStateInvalidWithoutMutating()
    {
        var source = new FakeRelocateLocation("Custom_CrimsonBadlands");
        var lab = new FakeRelocateLocation(CombatLabResetHandler.LocationName) { MapWidth = 20, MapHeight = 14 };
        var req = new CombatLabRelocateMonsterRequest
        {
            FromLocation = source.Name,
            TargetX = 9,
            TargetY = 8,
            Match = new CombatLabMonsterMatchCriteria { Type = "Mummy" },
        };

        var ex = Assert.Throws<JsonRpcException>(() =>
            SdvCombatLabRelocateWorld.RelocatePreparedMonster(req, source, lab));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("matched no monsters", ex.Message);
        Assert.Empty(lab.Monsters);
    }

    [Fact]
    public void RelocatePreparedMonster_MultipleMatches_ThrowsGameStateInvalidWithoutMutating()
    {
        var first = new FakeRelocatableMonster(new MonsterSummary { Tile = new TilePoint { X = 20, Y = 144 }, Type = "Mummy" });
        var second = new FakeRelocatableMonster(new MonsterSummary { Tile = new TilePoint { X = 21, Y = 144 }, Type = "Mummy" });
        var source = new FakeRelocateLocation("Custom_CrimsonBadlands", first, second);
        var lab = new FakeRelocateLocation(CombatLabResetHandler.LocationName) { MapWidth = 20, MapHeight = 14 };
        var req = new CombatLabRelocateMonsterRequest
        {
            FromLocation = source.Name,
            TargetX = 9,
            TargetY = 8,
            Match = new CombatLabMonsterMatchCriteria { Type = "Mummy" },
        };

        var ex = Assert.Throws<JsonRpcException>(() =>
            SdvCombatLabRelocateWorld.RelocatePreparedMonster(req, source, lab));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("matched 2 monsters", ex.Message);
        Assert.Equal(2, source.Monsters.Count);
        Assert.Empty(lab.Monsters);
    }

    [Fact]
    public void RelocatePreparedMonster_ExactMatch_MovesMonsterAndAssignsRelocatedIdentity()
    {
        CombatLabIdentityRegistry.Clear();
        var target = new FakeRelocatableMonster(new MonsterSummary
        {
            Tile = new TilePoint { X = 20, Y = 144 },
            Name = "Mummy",
            Type = "Mummy",
            SpriteTexture = "Characters/Monsters/CorruptMummy",
            Health = 2000,
            MaxHealth = 2000,
        });
        var decoy = new FakeRelocatableMonster(new MonsterSummary
        {
            Tile = new TilePoint { X = 21, Y = 144 },
            Type = "Mummy",
        });
        var source = new FakeRelocateLocation("Custom_CrimsonBadlands", target, decoy);
        var lab = new FakeRelocateLocation(CombatLabResetHandler.LocationName) { MapWidth = 20, MapHeight = 14 };
        var req = new CombatLabRelocateMonsterRequest
        {
            FromLocation = source.Name,
            Label = "corrupt-mummy",
            TargetX = 9,
            TargetY = 8,
            Match = new CombatLabMonsterMatchCriteria
            {
                X = 20,
                Y = 144,
                SpriteTexture = "Characters/Monsters/CorruptMummy",
            },
        };

        var result = SdvCombatLabRelocateWorld.RelocatePreparedMonster(req, source, lab);

        Assert.DoesNotContain(target, source.Monsters);
        Assert.Contains(decoy, source.Monsters);
        Assert.Single(lab.Monsters);
        Assert.Same(target, lab.Monsters[0]);
        Assert.Equal(9, target.Tile.X);
        Assert.Equal(8, target.Tile.Y);
        Assert.Equal(CombatLabResetHandler.LocationName, target.CurrentLocationName);
        Assert.Equal("frobby-monster-1", result.MonsterId);
        Assert.Equal("corrupt-mummy", result.Label);
        Assert.Equal(source.Name, result.FromLocation);
        Assert.Equal(20, result.SourceTile.X);
        Assert.Equal(144, result.SourceTile.Y);
        Assert.Equal("Mummy", result.Type);
        Assert.Equal("Characters/Monsters/CorruptMummy", result.SpriteTexture);
        Assert.True(CombatLabIdentityRegistry.TryGet(target.IdentityKey, out var identity));
        Assert.False(identity.SpawnedByFrobby);
    }

    private sealed class FakeRelocateWorld : ICombatLabRelocateWorld
    {
        public bool IsWorldReady { get; init; } = true;
        public CombatLabRelocateMonsterRequest? Request { get; private set; }

        public CombatLabRelocateMonsterResult RelocateMonster(CombatLabRelocateMonsterRequest request)
        {
            Request = request;
            return new CombatLabRelocateMonsterResult
            {
                MonsterId = "frobby-monster-1",
                Label = request.Label,
                FromLocation = request.FromLocation,
                SourceTile = new TilePoint { X = request.Match.X ?? 0, Y = request.Match.Y ?? 0 },
                Location = CombatLabResetHandler.LocationName,
                Tile = new TilePoint { X = request.TargetX, Y = request.TargetY },
                Name = "Mummy",
                Type = "Mummy",
                SpriteTexture = request.Match.SpriteTexture,
                Health = request.Match.Health,
                MaxHealth = request.Match.MaxHealth,
            };
        }
    }

    private sealed class FakeRelocateLocation : ICombatLabRelocateLocation
    {
        public FakeRelocateLocation(string name, params ICombatLabRelocatableMonster[] monsters)
        {
            Name = name;
            Monsters.AddRange(monsters);
        }

        public string Name { get; }
        public int? MapWidth { get; init; }
        public int? MapHeight { get; init; }
        public List<ICombatLabRelocatableMonster> Monsters { get; } = new();

        IReadOnlyList<ICombatLabRelocatableMonster> ICombatLabRelocateLocation.Monsters => Monsters;

        public void Remove(ICombatLabRelocatableMonster monster)
            => Monsters.Remove(monster);

        public void Add(ICombatLabRelocatableMonster monster)
            => Monsters.Add(monster);
    }

    private sealed class FakeRelocatableMonster : ICombatLabRelocatableMonster
    {
        private MonsterSummary summary;

        public FakeRelocatableMonster(MonsterSummary summary)
        {
            this.summary = summary;
        }

        public object IdentityKey => this;
        public TilePoint Tile => summary.Tile;
        public string? CurrentLocationName { get; private set; }

        public MonsterSummary Project()
            => summary;

        public void MoveTo(ICombatLabRelocateLocation location, int x, int y)
        {
            CurrentLocationName = location.Name;
            summary = new MonsterSummary
            {
                Tile = new TilePoint { X = x, Y = y },
                MonsterId = summary.MonsterId,
                Label = summary.Label,
                SpawnedByFrobby = summary.SpawnedByFrobby,
                Name = summary.Name,
                Type = summary.Type,
                Health = summary.Health,
                MaxHealth = summary.MaxHealth,
                Damage = summary.Damage,
                SpriteTexture = summary.SpriteTexture,
            };
        }
    }
}
```

- [ ] **Step 2: Run handler tests and verify red**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter FullyQualifiedName~CombatLabRelocateMonsterHandlerTests -v minimal
```

Expected: compile failure because relocate handler and relocation interfaces do not exist.

- [ ] **Step 3: Implement relocate handler and abstractions**

Create `src/Harness/Handlers/CombatLabRelocateMonsterHandler.cs` with these public/internal types and methods:

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
using StardewValley.Monsters;

namespace SdvTestFramework.Harness.Handlers;

public static class CombatLabRelocateMonsterHandler
{
    public const string Method = "combat_lab.relocate_monster";

    private static readonly ICombatLabRelocateWorld ProductionWorld = new SdvCombatLabRelocateWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, ICombatLabRelocateWorld world)
    {
        var req = RpcParams.Required<CombatLabRelocateMonsterRequest>(paramsElement);
        Validate(req);

        if (!world.IsWorldReady)
            throw new JsonRpcException(
                JsonRpcErrorCode.GameStateInvalid,
                "no active save - combat_lab.relocate_monster requires a loaded world");

        return ProtocolJson.ToElement(world.RelocateMonster(req));
    }

    private static void Validate(CombatLabRelocateMonsterRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.FromLocation))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "combat_lab.relocate_monster requires from_location");
        if (req.TargetX < 0 || req.TargetY < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "combat_lab.relocate_monster requires non-negative target_x and target_y");
        if (req.Match is null || !CombatLabMonsterMatcher.HasAnyFilter(req.Match))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "combat_lab.relocate_monster requires at least one match filter");
    }
}

internal interface ICombatLabRelocateWorld
{
    bool IsWorldReady { get; }
    CombatLabRelocateMonsterResult RelocateMonster(CombatLabRelocateMonsterRequest request);
}

internal interface ICombatLabRelocateLocation
{
    string Name { get; }
    int? MapWidth { get; }
    int? MapHeight { get; }
    IReadOnlyList<ICombatLabRelocatableMonster> Monsters { get; }
    void Remove(ICombatLabRelocatableMonster monster);
    void Add(ICombatLabRelocatableMonster monster);
}

internal interface ICombatLabRelocatableMonster
{
    object IdentityKey { get; }
    TilePoint Tile { get; }
    MonsterSummary Project();
    void MoveTo(ICombatLabRelocateLocation location, int x, int y);
}
```

Add this production world and prepared relocation method in the same file:

```csharp
internal sealed class SdvCombatLabRelocateWorld : ICombatLabRelocateWorld
{
    public bool IsWorldReady => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;

    public CombatLabRelocateMonsterResult RelocateMonster(CombatLabRelocateMonsterRequest request)
    {
        var source = Game1.getLocationFromName(request.FromLocation)
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"combat_lab.relocate_monster source location not found: {request.FromLocation}");
        var lab = Game1.getLocationFromName(CombatLabResetHandler.LocationName)
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, "combat_lab.relocate_monster requires combat_lab.reset first");

        return RelocatePreparedMonster(
            request,
            new SdvCombatLabRelocateLocation(source),
            new SdvCombatLabRelocateLocation(lab));
    }

    internal static CombatLabRelocateMonsterResult RelocatePreparedMonster(
        CombatLabRelocateMonsterRequest request,
        ICombatLabRelocateLocation source,
        ICombatLabRelocateLocation lab)
    {
        ValidateTargetTileAgainstMap(request, lab.MapWidth, lab.MapHeight);

        var matches = source.Monsters
            .Select(monster => new { Monster = monster, Summary = monster.Project() })
            .Where(entry => CombatLabMonsterMatcher.Matches(entry.Summary, request.Match))
            .ToList();

        if (matches.Count == 0)
        {
            throw new JsonRpcException(
                JsonRpcErrorCode.GameStateInvalid,
                $"combat_lab.relocate_monster matched no monsters in {request.FromLocation} with {CombatLabMonsterMatcher.Describe(request.Match)}");
        }
        if (matches.Count > 1)
        {
            throw new JsonRpcException(
                JsonRpcErrorCode.GameStateInvalid,
                $"combat_lab.relocate_monster matched {matches.Count} monsters in {request.FromLocation}; use a tighter selector than {CombatLabMonsterMatcher.Describe(request.Match)}");
        }

        var selected = matches[0];
        var sourceTile = selected.Summary.Tile;
        source.Remove(selected.Monster);
        selected.Monster.MoveTo(lab, request.TargetX, request.TargetY);
        lab.Add(selected.Monster);

        var identity = CombatLabIdentityRegistry.Assign(selected.Monster.IdentityKey, request.Label, spawnedByFrobby: false);
        var relocated = selected.Monster.Project();
        return new CombatLabRelocateMonsterResult
        {
            MonsterId = identity.MonsterId,
            Label = identity.Label,
            FromLocation = request.FromLocation,
            SourceTile = sourceTile,
            Location = CombatLabResetHandler.LocationName,
            Tile = relocated.Tile,
            Name = relocated.Name,
            Type = relocated.Type,
            SpriteTexture = relocated.SpriteTexture,
            Health = relocated.Health,
            MaxHealth = relocated.MaxHealth,
        };
    }

    internal static void ValidateTargetTileAgainstMap(CombatLabRelocateMonsterRequest request, int? mapWidth, int? mapHeight)
    {
        if (mapWidth is null || mapHeight is null)
            return;

        if (request.TargetX >= mapWidth || request.TargetY >= mapHeight)
        {
            throw new JsonRpcException(
                JsonRpcErrorCode.InvalidParams,
                "combat_lab.relocate_monster target tile must be inside the combat lab map bounds");
        }
    }
}
```

Add these production adapters in the same file:

```csharp
internal sealed class SdvCombatLabRelocateLocation : ICombatLabRelocateLocation
{
    private readonly GameLocation location;

    public SdvCombatLabRelocateLocation(GameLocation location)
        => this.location = location;

    public string Name => location.Name;
    public int? MapWidth => location.Map?.Layers.FirstOrDefault()?.LayerWidth;
    public int? MapHeight => location.Map?.Layers.FirstOrDefault()?.LayerHeight;
    public IReadOnlyList<ICombatLabRelocatableMonster> Monsters
        => location.characters.OfType<Monster>().Select(monster => new SdvCombatLabRelocatableMonster(monster)).ToList();

    public void Remove(ICombatLabRelocatableMonster monster)
    {
        if (monster is not SdvCombatLabRelocatableMonster sdvMonster)
            throw new InvalidOperationException("combat_lab.relocate_monster received an incompatible monster adapter");

        location.characters.Remove(sdvMonster.Monster);
    }

    public void Add(ICombatLabRelocatableMonster monster)
    {
        if (monster is not SdvCombatLabRelocatableMonster sdvMonster)
            throw new InvalidOperationException("combat_lab.relocate_monster received an incompatible monster adapter");

        location.characters.Add(sdvMonster.Monster);
    }
}

internal sealed class SdvCombatLabRelocatableMonster : ICombatLabRelocatableMonster
{
    public SdvCombatLabRelocatableMonster(Monster monster)
        => Monster = monster;

    public Monster Monster { get; }
    public object IdentityKey => Monster;
    public TilePoint Tile => LocationContentProjector.ProjectMonsterForTests(Monster).Tile;

    public MonsterSummary Project()
        => LocationContentProjector.ProjectMonsterForTests(Monster);

    public void MoveTo(ICombatLabRelocateLocation location, int x, int y)
    {
        if (location is not SdvCombatLabRelocateLocation)
            throw new InvalidOperationException("combat_lab.relocate_monster received an incompatible location adapter");

        Monster.Position = new Vector2(x * 64f, y * 64f);
        Monster.currentLocation = Game1.getLocationFromName(location.Name);
    }
}
```

After implementing, inspect `SdvCombatLabRelocatableMonster.MoveTo`. If live testing shows `Monster.Position` is not enough for a specific monster type, add an internal helper to also set a public `tile` field when reflection finds one. Do not add that reflection unless a test or live run proves it is needed.

- [ ] **Step 4: Register the RPC**

Modify `src/Harness/ModEntry.cs`:

```csharp
_rpc.Register(CombatLabRelocateMonsterHandler.Method, p => CombatLabRelocateMonsterHandler.Handle(p));
```

Place it next to the existing `combat_lab.reset` and `combat_lab.spawn_monster` registrations.

In the startup log string, change:

```text
Combat: combat_lab.reset, combat_lab.spawn_monster, combat.attack.
```

to:

```text
Combat: combat_lab.reset, combat_lab.spawn_monster, combat_lab.relocate_monster, combat.attack.
```

- [ ] **Step 5: Run handler tests and verify green**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~CombatLabRelocateMonsterHandlerTests|FullyQualifiedName~CombatLabMonsterMatcherTests|FullyQualifiedName~CombatLabIdentityRegistryTests|FullyQualifiedName~LocationContentProjectorTests" -v minimal
```

Expected: selected tests pass.

- [ ] **Step 6: Run full harness tests**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj -v minimal
```

Expected: harness tests pass.

- [ ] **Step 7: Commit relocate handler**

Run:

```bash
git add src/Harness/Handlers/CombatLabRelocateMonsterHandler.cs src/Harness/ModEntry.cs tests/Harness.Tests/CombatLabRelocateMonsterHandlerTests.cs
git commit -m "Add combat lab monster relocation handler"
```

## Task 5: Runner And DSL Surface

**Files:**
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`
- Modify: `tests/Runner.Tests/ScenarioRunnerTests.cs`
- Modify: `src/Runner.Dsl/CombatLab.cs`
- Modify: `tests/Runner.Dsl.Tests/Facets/CombatLabTests.cs`

- [ ] **Step 1: Write failing runner and DSL tests**

Add this test to `tests/Runner.Tests/ScenarioRunnerTests.cs` near the other Combat Lab runner tests:

```csharp
[Fact]
public async Task CombatLabRelocateMonster_PassesThroughAndReportsReadableStep()
{
    var socket = SocketPath();
    var tmp = Path.Combine(Path.GetTempPath(), $"combat-lab-relocate-report-{Guid.NewGuid():N}");
    var rd = RunDirectory.Create(tmp);
    var calls = new List<string>();
    var relocateParams = default(JsonElement);
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

    var serverTask = Task.Run(async () =>
    {
        await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
        {
            session.RequestReceived += async req =>
            {
                calls.Add(req.Method);
                if (req.Method == "combat_lab.relocate_monster")
                    relocateParams = req.Params!.Value.Clone();

                JsonElement r = req.Method switch
                {
                    "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                    "combat_lab.relocate_monster" => JsonDocument.Parse("{\"ok\":true,\"monster_id\":\"frobby-monster-1\",\"label\":\"corrupt-mummy\",\"from_location\":\"Custom_CrimsonBadlands\",\"source_tile\":{\"x\":20,\"y\":144},\"location\":\"Frobby_CombatLab\",\"tile\":{\"x\":9,\"y\":8},\"name\":\"Mummy\",\"type\":\"Mummy\",\"sprite_texture\":\"Characters/Monsters/CorruptMummy\",\"health\":2000,\"max_health\":2000}").RootElement,
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
            Name = "combat_lab_relocate_report",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "combat_lab.relocate_monster",
                    Args = JsonDocument.Parse("{\"from_location\":\"Custom_CrimsonBadlands\",\"label\":\"corrupt-mummy\",\"target_x\":9,\"target_y\":8,\"match\":{\"sprite_texture\":\"Characters/Monsters/CorruptMummy\"}}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed, string.Join("\n", report.Failures));
        Assert.Contains("combat_lab.relocate_monster", calls);
        Assert.Equal("Custom_CrimsonBadlands", relocateParams.GetProperty("from_location").GetString());
        Assert.Equal("corrupt-mummy", relocateParams.GetProperty("label").GetString());
        Assert.Equal("Relocate monster from Custom_CrimsonBadlands to Combat Lab", report.Steps[0].Detail);
    }
    finally
    {
        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
        Directory.Delete(rd.Root, recursive: true);
    }
}
```

Modify `tests/Runner.Dsl.Tests/Facets/CombatLabTests.cs`:

1. Update the fake invoker response switch:

```csharp
return Task.FromResult(JsonDocument.Parse(method switch
{
    "combat_lab.spawn_monster" => "{\"ok\":true,\"monster_id\":\"frobby-monster-1\",\"label\":\"target\",\"kind\":\"GreenSlime\",\"location\":\"Frobby_CombatLab\",\"tile\":{\"x\":12,\"y\":8},\"health\":1,\"max_health\":24}",
    "combat_lab.relocate_monster" => "{\"ok\":true,\"monster_id\":\"frobby-monster-2\",\"label\":\"corrupt-mummy\",\"from_location\":\"Custom_CrimsonBadlands\",\"source_tile\":{\"x\":20,\"y\":144},\"location\":\"Frobby_CombatLab\",\"tile\":{\"x\":9,\"y\":8},\"name\":\"Mummy\",\"type\":\"Mummy\",\"sprite_texture\":\"Characters/Monsters/CorruptMummy\",\"health\":2000,\"max_health\":2000}",
    _ => "{\"ok\":true,\"location\":\"Frobby_CombatLab\",\"player_tile\":{\"x\":8,\"y\":8},\"map_width\":20,\"map_height\":14,\"cleared_monsters\":0,\"cleared_debris\":0}",
}).RootElement.Clone());
```

2. Add this test:

```csharp
[Fact]
public async Task RelocateMonster_InvokesCombatLabRelocateMonsterAndReturnsResult()
{
    SdvTestSession.ResetForTests();
    var inv = new CapturingInvoker();
    SdvTestSession.InitializeForTests(inv);
    CombatLabRelocateMonsterResult result;
    try
    {
        result = await CombatLab.RelocateMonster(
            fromLocation: "Custom_CrimsonBadlands",
            label: "corrupt-mummy",
            targetX: 9,
            targetY: 8,
            match: new CombatLabMonsterMatchCriteria
            {
                X = 20,
                Y = 144,
                SpriteTexture = "Characters/Monsters/CorruptMummy",
                Health = 2000,
                MaxHealth = 2000,
            });
    }
    finally { SdvTestSession.ResetForTests(); }

    Assert.Single(inv.Calls);
    Assert.Equal("combat_lab.relocate_monster", inv.Calls[0].Method);
    Assert.Contains("\"from_location\":\"Custom_CrimsonBadlands\"", inv.Calls[0].ParamsJson);
    Assert.Contains("\"target_x\":9", inv.Calls[0].ParamsJson);
    Assert.Contains("\"sprite_texture\":\"Characters/Monsters/CorruptMummy\"", inv.Calls[0].ParamsJson);
    Assert.Equal("frobby-monster-2", result.MonsterId);
    Assert.Equal("corrupt-mummy", result.Label);
}
```

- [ ] **Step 2: Run runner and DSL tests and verify red**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter FullyQualifiedName~CombatLabRelocateMonster_PassesThroughAndReportsReadableStep -v minimal
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter "FullyQualifiedName~CombatLabTests" -v minimal
```

Expected: runner test fails on missing readable detail; DSL test fails because `CombatLab.RelocateMonster` does not exist.

- [ ] **Step 3: Add runner step detail**

Modify `DescribeStep` in `src/Runner/Scenarios/ScenarioRunner.cs`:

```csharp
"combat_lab.relocate_monster" => $"Relocate monster from {GetStringArg(step.Args, "from_location") ?? "unknown"} to Combat Lab",
```

Place it after `combat_lab.spawn_monster`.

- [ ] **Step 4: Add DSL wrapper**

Append this method to `src/Runner.Dsl/CombatLab.cs`:

```csharp
public static async Task<CombatLabRelocateMonsterResult> RelocateMonster(
    string fromLocation,
    string? label,
    int targetX,
    int targetY,
    CombatLabMonsterMatchCriteria match,
    CancellationToken ct = default)
{
    var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
    var p = JsonSerializer.SerializeToElement(new CombatLabRelocateMonsterRequest
    {
        FromLocation = fromLocation,
        Label = label,
        TargetX = targetX,
        TargetY = targetY,
        Match = match,
    }, ProtocolJson.Options);
    var resp = await s.InvokeAsync("combat_lab.relocate_monster", p, ct);
    return JsonSerializer.Deserialize<CombatLabRelocateMonsterResult>(resp, ProtocolJson.Options)
        ?? throw new SdvRpcException(
            "combat_lab.relocate_monster",
            JsonRpcErrorCode.InternalError,
            "empty combat_lab.relocate_monster response");
}
```

- [ ] **Step 5: Run runner and DSL tests and verify green**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter FullyQualifiedName~CombatLabRelocateMonster_PassesThroughAndReportsReadableStep -v minimal
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter "FullyQualifiedName~CombatLabTests" -v minimal
```

Expected: selected tests pass.

- [ ] **Step 6: Commit runner and DSL surface**

Run:

```bash
git add src/Runner/Scenarios/ScenarioRunner.cs tests/Runner.Tests/ScenarioRunnerTests.cs src/Runner.Dsl/CombatLab.cs tests/Runner.Dsl.Tests/Facets/CombatLabTests.cs
git commit -m "Expose combat lab relocation in runner and DSL"
```

## Task 6: SVE Scenario 28 Relocation Proof

**Files:**
- Create: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/28-sve-combat-lab-relocate-mod-monster.test.json`
- Modify: `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`

- [ ] **Step 1: Add SVE scenario 28**

Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/28-sve-combat-lab-relocate-mod-monster.test.json`:

```json
{
  "name": "sve_combat_lab_relocate_mod_monster",
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
          "health": 2000,
          "max_health": 2000,
          "damage": 100,
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
        "sprite_texture": "Characters/Monsters/CorruptMummy",
        "min_count": 1,
        "max_count": 1,
        "timeout_ms": 5000,
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
        "sprite_texture": "Characters/Monsters/CorruptMummy",
        "min_count": 0,
        "max_count": 0,
        "timeout_ms": 5000,
        "poll_ms": 100
      }
    },
    {
      "action": "combat.attack",
      "args": {
        "qualified_item_id": "(W)4",
        "repeat": 40,
        "delay_ticks": 5,
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
      "message": "Relocated mod monster scenario should finish inside the Frobby combat dev room"
    }
  ]
}
```

This first version uses `repeat: 40`, matching scenario 13's lifecycle proof. If live verification proves the relocated mummy cannot be killed because relocation changes combat/pathing, stop and debug before changing the scenario.

- [ ] **Step 2: Update SVE Frobby docs**

In `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`, add a paragraph after scenario 27:

```markdown
Scenario `tests/sdv/28-sve-combat-lab-relocate-mod-monster.test.json` proves
Frobby's Combat Lab relocation path against an SVE/FTM runtime monster. It lets
Farm Type Manager spawn the Crimson Badlands corrupt mummy normally, moves that
exact monster object into `Frobby_CombatLab`, attacks it by lab label, and waits
for the relocated instance to be removed. Frobby does not construct or parse the
SVE monster definition.
```

- [ ] **Step 3: Validate scenario JSON**

Run:

```bash
python3 -m json.tool tests/sdv/28-sve-combat-lab-relocate-mod-monster.test.json
```

from `/home/fintan/stardewRepos/StardewValleyExpanded`.

Expected: command exits 0 and prints formatted JSON.

- [ ] **Step 4: Commit SVE scenario**

Run from `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
git add tests/sdv/28-sve-combat-lab-relocate-mod-monster.test.json docs/FROBBY.md
git commit -m "Add Frobby Combat Lab relocation scenario"
```

## Task 7: Documentation And Backlog Status

**Files:**
- Modify: `docs/rpc-schema.md`
- Modify: `docs/dsl-quickstart.md`
- Modify: `docs/wiki/examples.md`
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Update RPC docs**

In `docs/rpc-schema.md`, add this section after `combat_lab.spawn_monster`:

````markdown
### combat_lab.relocate_monster

Moves one already-spawned runtime monster into `Frobby_CombatLab` and assigns a
run-local Frobby identity. This isolates mod-created monsters without Frobby
constructing or parsing mod monster definitions.

Request:
```json
→ { "jsonrpc": "2.0", "id": 18, "method": "combat_lab.relocate_monster", "params": { "from_location": "Custom_CrimsonBadlands", "label": "corrupt-mummy", "target_x": 9, "target_y": 8, "match": { "x": 20, "y": 144, "sprite_texture": "Characters/Monsters/CorruptMummy", "health": 2000, "max_health": 2000 } } }
```

Response (success):
```json
← { "jsonrpc": "2.0", "id": 18, "result": {
      "ok": true,
      "monster_id": "frobby-monster-1",
      "label": "corrupt-mummy",
      "from_location": "Custom_CrimsonBadlands",
      "source_tile": { "x": 20, "y": 144 },
      "location": "Frobby_CombatLab",
      "tile": { "x": 9, "y": 8 },
      "name": "Mummy",
      "type": "Mummy",
      "sprite_texture": "Characters/Monsters/CorruptMummy",
      "health": 2000,
      "max_health": 2000
   } }
```

`match` filters are exact and use the same observable fields exposed by
`state.location.monsters`: `x`, `y`, `monster_id`, `label`, `name`, `type`,
`sprite_texture`, `health`, `max_health`, and `damage`. The handler rejects zero
matches and multiple matches so scenarios must identify exactly one source
monster before mutation.

**Preconditions:** world loaded; call `combat_lab.reset` before relocating; the
source location must be loaded; target tile must be inside the lab map.
**Side effects:** removes the matching monster object from the source location,
moves it into `Frobby_CombatLab`, and binds run-local identity metadata with
`spawned_by_frobby: false`.
**Implemented in:** `src/Harness/Handlers/CombatLabRelocateMonsterHandler.cs`
**Tested in:** `tests/Protocol.Tests/CombatLabSerializationTests.cs`,
`tests/Harness.Tests/CombatLabRelocateMonsterHandlerTests.cs`,
`tests/Harness.Tests/CombatLabMonsterMatcherTests.cs`,
`tests/Runner.Tests/ScenarioRunnerTests.cs`, and
`tests/Runner.Dsl.Tests/Facets/CombatLabTests.cs`.
````

- [ ] **Step 2: Update DSL quickstart**

In `docs/dsl-quickstart.md`, after the Combat Lab vanilla example, add:

````markdown
For mod-spawned monsters, let the mod create the monster first and then relocate
that exact runtime instance into the lab:

```json
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
      "sprite_texture": "Characters/Monsters/CorruptMummy"
    }
  }
}
```

The relocation action moves the existing monster object. It does not construct a
mod monster or parse mod spawn data.
````

- [ ] **Step 3: Update wiki examples**

In `docs/wiki/examples.md`, add this item under combat examples:

```markdown
- Combat Lab relocated mod monster lifecycle:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/28-sve-combat-lab-relocate-mod-monster.test.json`
```

- [ ] **Step 4: Mark Slice 20 Active**

In `SVE_FROBBY_CAPABILITY_TODO.md`, change the Slice 20 header to:

```markdown
- [ ] Active: Slice 20, relocate mod-spawned monsters into the Combat Lab.
```

Add the implementation plan line:

```markdown
  - Implementation plan: `docs/superpowers/plans/2026-05-21-sve-slice-20-mod-monster-relocation-combat-lab.md`.
```

- [ ] **Step 5: Commit docs**

Run:

```bash
git add docs/rpc-schema.md docs/dsl-quickstart.md docs/wiki/examples.md SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "Document combat lab monster relocation"
```

## Task 8: Full Verification And Completion

**Files:**
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Run focused Frobby tests**

Run from `/home/fintan/stardewRepos/frobby/sdv-test-framework`:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter "FullyQualifiedName~CombatLabSerializationTests" -v minimal
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~CombatLabRelocateMonsterHandlerTests|FullyQualifiedName~CombatLabMonsterMatcherTests|FullyQualifiedName~CombatLabIdentityRegistryTests|FullyQualifiedName~LocationContentProjectorTests|FullyQualifiedName~CombatLabSpawnMonsterHandlerTests|FullyQualifiedName~CombatLabResetHandlerTests" -v minimal
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~CombatLabRelocateMonster_PassesThroughAndReportsReadableStep|FullyQualifiedName~CombatAttack_TargetSelector|FullyQualifiedName~WaitLocationContent" -v minimal
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter "FullyQualifiedName~CombatLabTests|FullyQualifiedName~CombatTests" -v minimal
```

Expected: all focused tests pass.

- [ ] **Step 2: Run full Frobby build**

Run:

```bash
dotnet build sdv-test-framework.slnx -v minimal
```

Expected: build succeeds with 0 errors.

- [ ] **Step 3: Run SVE scenario 28 headlessly**

Run from `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework ./scripts/sdv-test --headless --no-build --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-20-combat-lab-relocation tests/sdv/28-sve-combat-lab-relocate-mod-monster.test.json
```

Expected: `1/1 passed` and an HTML report under `/tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-20-combat-lab-relocation/28-sve-combat-lab-relocate-mod-monster/index.html`.

If the command fails only under the Codex sandbox with a cache/write/build wrapper error, rerun the same command with escalated execution and record that in the final notes.

- [ ] **Step 4: Run SVE Combat Lab vanilla regression**

Run from `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework ./scripts/sdv-test --headless --no-build --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-20-combat-lab-vanilla-regression tests/sdv/27-sve-combat-lab-vanilla-monster.test.json
```

Expected: `1/1 passed`.

- [ ] **Step 5: Run closest live-map combat regression**

Run from `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework ./scripts/sdv-test --headless --no-build --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-20-combat-regression tests/sdv/12-sve-combat-monster-damage.test.json
```

Expected: `1/1 passed`.

- [ ] **Step 6: Mark Slice 20 Done**

Modify `SVE_FROBBY_CAPABILITY_TODO.md`:

```markdown
- [x] Done: Slice 20, relocate mod-spawned monsters into the Combat Lab.
  - SVE pressure: SVE/FTM monsters such as the Crimson Badlands corrupt mummy carry runtime mod settings that Frobby should not recreate directly.
  - Frobby goal: move exactly one already-spawned runtime monster into `Frobby_CombatLab`, assign a run-local identity/label, and test attack/removal there.
  - Design spec: `docs/superpowers/specs/2026-05-21-sve-slice-20-mod-monster-relocation-combat-lab-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-21-sve-slice-20-mod-monster-relocation-combat-lab.md`.
  - Done: `combat_lab.relocate_monster`, neutral monster match criteria, relocated identity semantics with `spawned_by_frobby: false`, DSL helper, docs, and SVE scenario 28.
  - Verified: SVE scenario 28 lets FTM spawn the Crimson Badlands corrupt mummy, relocates that exact runtime monster into `Frobby_CombatLab`, attacks by lab label, and waits for the relocated instance to be removed.
  - Follow-up candidate: direct mod monster construction only after researching a stable spawn API or cross-mod factory pattern.
```

- [ ] **Step 7: Commit completion status**

Run:

```bash
git add SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "Complete SVE combat lab relocation slice"
```

- [ ] **Step 8: Confirm clean worktrees**

Run:

```bash
git status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
```

Expected: Frobby and SVE worktrees are clean after their respective commits.

## Self-Review Notes

- Spec coverage: the plan implements `combat_lab.relocate_monster`, neutral match filters, relocated identity semantics, SVE scenario 28, docs, and verification.
- Scope control: the plan avoids direct mod monster construction and does not add SVE-specific selectors to Frobby.
- TDD coverage: each code task starts with failing tests, then implementation, green tests, and a focused commit.
