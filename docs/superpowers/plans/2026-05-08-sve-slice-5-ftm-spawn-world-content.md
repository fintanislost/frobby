# SVE Slice 5 FTM Spawn World Content Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add neutral Frobby support for observing and waiting on spawned location content, then prove it against SVE Farm Type Manager large-object spawns.

**Architecture:** Extend the existing `state.location` contract with additive `resource_clumps` and `monsters` collections projected from Stardew runtime state. Add a runner-only `wait.location_content` action that polls `state.location` and filters any supported collection by stable fields, keeping Frobby independent from Farm Type Manager internals. SVE scenario 07 uses those neutral primitives to assert deterministic Grandpa's Shed exterior log spawns before monster coverage is layered in a separate pass.

**Tech Stack:** C#/.NET 10 runner, .NET 6 SMAPI harness, StardewValley/SMAPI runtime types, xUnit, JSON scenario files.

---

## File Structure

- Modify `src/Protocol/Models/LocationState.cs`
  Add `ResourceClumps`, `Monsters`, richer optional object fields, and DTO types.
- Modify `tests/Protocol.Tests/LocationStateSerializationTests.cs`
  Lock the snake_case JSON contract for the new fields.
- Create `src/Harness/Handlers/LocationContentProjector.cs`
  Project resource clumps and monsters from runtime objects with defensive reflection where SDV fields vary.
- Modify `src/Harness/Handlers/LocationStateProjector.cs`
  Attach projected content to `state.location` and keep social NPCs separate from monsters.
- Create `tests/Harness.Tests/LocationContentProjectorTests.cs`
  Unit-test label mapping, object-field reflection, and monster projection without a live game.
- Modify `src/Runner/Scenarios/ScenarioRunner.cs`
  Add `wait.location_content`, validation, filtering, report text, and no-auto-screenshot policy.
- Modify `tests/Runner.Tests/ScenarioRunnerTests.cs`
  Add fake-harness runner tests for pass, timeout, filtering, max-count, and validation.
- Modify `docs/rpc-schema.md`, `docs/dsl-quickstart.md`, and `README.md`
  Document the new `state.location` fields and runner action.
- Modify `SVE_FROBBY_CAPABILITY_TODO.md`
  Mark Slice 5 active during implementation and complete after live verification.
- Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/07-sve-ftm-spawn-world-content.test.json`
  Add the first SVE proof scenario.
- Modify `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`
  Mention scenario 07 as the FTM spawned-content coverage example.

## Task 1: Protocol Location Content DTOs

**Files:**
- Modify: `src/Protocol/Models/LocationState.cs`
- Test: `tests/Protocol.Tests/LocationStateSerializationTests.cs`

- [ ] **Step 1: Write the failing serialization test**

Modify `tests/Protocol.Tests/LocationStateSerializationTests.cs` inside `Serialize_SnakeCaseFields` so the `LocationState` initializer includes new fields:

```csharp
            Objects = new()
            {
                new ObjectSummary
                {
                    Tile = new TilePoint { X = 10, Y = 10 },
                    Name = "Weeds",
                    Id = "O771",
                    QualifiedId = "(O)771",
                    Category = -999,
                    Stack = 1,
                    Quality = 0,
                },
            },
            ResourceClumps = new()
            {
                new ResourceClumpSummary
                {
                    Tile = new TilePoint { X = 21, Y = 17 },
                    Kind = "ResourceClump",
                    Id = "602",
                    Name = "Log",
                    Width = 2,
                    Height = 2,
                    Health = 10,
                },
            },
            Monsters = new()
            {
                new MonsterSummary
                {
                    Tile = new TilePoint { X = 44, Y = 31 },
                    Name = "Green Slime",
                    Type = "GreenSlime",
                    Health = 50,
                    MaxHealth = 50,
                    Damage = 10,
                },
            },
```

Add these assertions after the existing `objects`/`npcs` assertions:

```csharp
        Assert.Contains("\"objects\":[{\"tile\":{\"x\":10,\"y\":10},\"name\":\"Weeds\",\"id\":\"O771\",\"qualified_id\":\"(O)771\",\"category\":-999,\"stack\":1,\"quality\":0}]", json);
        Assert.Contains("\"resource_clumps\":[{\"tile\":{\"x\":21,\"y\":17},\"kind\":\"ResourceClump\",\"id\":\"602\",\"name\":\"Log\",\"width\":2,\"height\":2,\"health\":10}]", json);
        Assert.Contains("\"monsters\":[{\"tile\":{\"x\":44,\"y\":31},\"name\":\"Green Slime\",\"type\":\"GreenSlime\",\"health\":50,\"max_health\":50,\"damage\":10}]", json);
```

- [ ] **Step 2: Run the protocol test and verify it fails**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter LocationStateSerializationTests
```

Expected: FAIL because `LocationState.ResourceClumps`, `LocationState.Monsters`, and the new `ObjectSummary` fields do not exist.

- [ ] **Step 3: Add the DTO fields and types**

Modify `src/Protocol/Models/LocationState.cs`:

```csharp
    /// <summary>Resource clumps and other large world objects in this location.</summary>
    public List<ResourceClumpSummary> ResourceClumps { get; set; } = new();

    /// <summary>Hostile monsters currently in this location, separated from social NPCs.</summary>
    public List<MonsterSummary> Monsters { get; set; } = new();
```

Replace `ObjectSummary` with:

```csharp
/// <summary>Minimal placeable-object descriptor for a location snapshot.</summary>
public sealed class ObjectSummary
{
    public TilePoint Tile { get; set; } = new();
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string QualifiedId { get; set; } = string.Empty;
    public int? Category { get; set; }
    public int? Stack { get; set; }
    public int? Quality { get; set; }
}
```

Add these classes after `TerrainSummary`:

```csharp
/// <summary>Resource clump or large map object descriptor. <see cref="Kind"/> is the CLR type name.</summary>
public sealed class ResourceClumpSummary
{
    public TilePoint Tile { get; set; } = new();
    public string Kind { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? Health { get; set; }
}

/// <summary>Hostile creature descriptor for a location snapshot. <see cref="Type"/> is the CLR type name.</summary>
public sealed class MonsterSummary
{
    public TilePoint Tile { get; set; } = new();
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int? Health { get; set; }
    public int? MaxHealth { get; set; }
    public int? Damage { get; set; }
}
```

- [ ] **Step 4: Run the protocol test and verify it passes**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter LocationStateSerializationTests
```

Expected: PASS.

- [ ] **Step 5: Commit protocol contract**

Run:

```bash
git add src/Protocol/Models/LocationState.cs tests/Protocol.Tests/LocationStateSerializationTests.cs
git commit -m "feat: expand location content state"
```

## Task 2: Harness Location Content Projection

**Files:**
- Create: `src/Harness/Handlers/LocationContentProjector.cs`
- Modify: `src/Harness/Handlers/LocationStateProjector.cs`
- Test: `tests/Harness.Tests/LocationContentProjectorTests.cs`

- [ ] **Step 1: Write failing projector tests**

Create `tests/Harness.Tests/LocationContentProjectorTests.cs`:

```csharp
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Handlers;
using StardewValley;
using StardewValley.Monsters;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class LocationContentProjectorTests
{
    [Theory]
    [InlineData("602", "Log")]
    [InlineData("600", "Stump")]
    [InlineData("622", "Meteorite")]
    [InlineData("672", "Boulder")]
    [InlineData("9999", "ResourceClump 9999")]
    [InlineData("", "ResourceClump")]
    public void ResourceClumpName_MapsKnownIds(string id, string expected)
    {
        Assert.Equal(expected, LocationContentProjector.ResourceClumpNameForTests(id));
    }

    [Fact]
    public void ProjectResourceClump_ReadsPlainFieldsAndProperties()
    {
        var clump = new FakeResourceClump
        {
            tile = new Vector2(21, 17),
            parentSheetIndex = 602,
            width = 2,
            height = 2,
            health = 10,
        };

        var summary = LocationContentProjector.ProjectResourceClumpForTests(clump);

        Assert.Equal(21, summary.Tile.X);
        Assert.Equal(17, summary.Tile.Y);
        Assert.Equal("ResourceClump", summary.Kind);
        Assert.Equal("602", summary.Id);
        Assert.Equal("Log", summary.Name);
        Assert.Equal(2, summary.Width);
        Assert.Equal(2, summary.Height);
        Assert.Equal(10, summary.Health);
    }

    [Fact]
    public void ProjectMonster_ReadsRuntimeMonsterFields()
    {
        var monster = new GreenSlime(new Vector2(44 * 64, 31 * 64), 0)
        {
            Name = "Green Slime",
            Health = 50,
            MaxHealth = 50,
            DamageToFarmer = 10,
        };

        var summary = LocationContentProjector.ProjectMonsterForTests(monster);

        Assert.Equal(44, summary.Tile.X);
        Assert.Equal(31, summary.Tile.Y);
        Assert.Equal("Green Slime", summary.Name);
        Assert.Equal("GreenSlime", summary.Type);
        Assert.Equal(50, summary.Health);
        Assert.Equal(50, summary.MaxHealth);
        Assert.Equal(10, summary.Damage);
    }

    [Fact]
    public void IsMonster_ReturnsFalseForSocialNpc()
    {
        var npc = new NPC(new AnimatedSprite("Characters\\Abigail"), Vector2.Zero, "Town", 2, "Abigail", false, null);

        Assert.False(LocationContentProjector.IsMonster(npc));
    }

    private sealed class FakeResourceClump
    {
        public Vector2 tile;
        public int parentSheetIndex;
        public int width;
        public int height;
        public int health;
    }
}
```

- [ ] **Step 2: Run the harness projector tests and verify they fail**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter LocationContentProjectorTests
```

Expected: FAIL because `LocationContentProjector` does not exist.

- [ ] **Step 3: Add the neutral content projector**

Create `src/Harness/Handlers/LocationContentProjector.cs`:

```csharp
using System.Collections;
using System.Reflection;
using Microsoft.Xna.Framework;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using StardewValley.Monsters;

namespace SdvTestFramework.Harness.Handlers;

internal static class LocationContentProjector
{
    public static IEnumerable<ResourceClumpSummary> ProjectResourceClumps(GameLocation loc)
    {
        if (ReadMemberRaw(loc, "resourceClumps", "ResourceClumps") is not IEnumerable clumps)
            yield break;

        foreach (var clump in clumps)
        {
            if (clump is null) continue;
            yield return ProjectResourceClump(clump);
        }
    }

    public static IEnumerable<MonsterSummary> ProjectMonsters(GameLocation loc)
    {
        foreach (var character in loc.characters)
        {
            if (character is Monster monster)
                yield return ProjectMonster(monster);
        }
    }

    public static bool IsMonster(NPC npc) => npc is Monster;

    internal static ResourceClumpSummary ProjectResourceClumpForTests(object clump)
        => ProjectResourceClump(clump);

    internal static MonsterSummary ProjectMonsterForTests(Monster monster)
        => ProjectMonster(monster);

    internal static string ResourceClumpNameForTests(string id)
        => ResourceClumpName(id);

    private static ResourceClumpSummary ProjectResourceClump(object clump)
    {
        var tile = ReadVector2(clump, "tile", "Tile") ?? Vector2.Zero;
        var id = ReadInt(clump, "parentSheetIndex", "ParentSheetIndex")?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        return new ResourceClumpSummary
        {
            Tile = new TilePoint { X = (int)tile.X, Y = (int)tile.Y },
            Kind = clump.GetType().Name,
            Id = id,
            Name = ResourceClumpName(id),
            Width = ReadInt(clump, "width", "Width"),
            Height = ReadInt(clump, "height", "Height"),
            Health = ReadInt(clump, "health", "Health"),
        };
    }

    private static MonsterSummary ProjectMonster(Monster monster)
    {
        return new MonsterSummary
        {
            Tile = new TilePoint { X = monster.TilePoint.X, Y = monster.TilePoint.Y },
            Name = monster.Name ?? monster.DisplayName ?? monster.GetType().Name,
            Type = monster.GetType().Name,
            Health = ReadInt(monster, "Health", "health"),
            MaxHealth = ReadInt(monster, "MaxHealth", "maxHealth"),
            Damage = ReadInt(monster, "DamageToFarmer", "damageToFarmer", "damage"),
        };
    }

    private static string ResourceClumpName(string id)
        => id switch
        {
            "" => "ResourceClump",
            "600" => "Stump",
            "602" => "Log",
            "622" => "Meteorite",
            "672" => "Boulder",
            "668" or "670" or "845" or "846" or "847" => "Mine Rock",
            _ => $"ResourceClump {id}",
        };

    private static Vector2? ReadVector2(object instance, params string[] names)
    {
        var value = ReadMemberRaw(instance, names);
        if (value is Vector2 vector)
            return vector;

        var nested = ReadValueProperty(value);
        return nested is Vector2 nestedVector ? nestedVector : null;
    }

    private static int? ReadInt(object instance, params string[] names)
    {
        var value = ReadMemberRaw(instance, names);
        value = ReadValueProperty(value) ?? value;

        return value switch
        {
            int i => i,
            long l when l >= int.MinValue && l <= int.MaxValue => (int)l,
            short s => s,
            byte b => b,
            _ => null,
        };
    }

    private static object? ReadValueProperty(object? value)
    {
        if (value is null) return null;
        var prop = value.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return prop?.GetValue(value);
    }

    private static object? ReadMemberRaw(object instance, params string[] names)
    {
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = instance.GetType();
        foreach (var name in names)
        {
            var property = type.GetProperty(name, flags);
            if (property is not null)
                return property.GetValue(instance);

            var field = type.GetField(name, flags);
            if (field is not null)
                return field.GetValue(instance);
        }

        return null;
    }
}
```

- [ ] **Step 4: Wire projected content into `state.location`**

Modify `src/Harness/Handlers/LocationStateProjector.cs`.

In the `foreach (var npc in loc.characters)` loop, skip monsters:

```csharp
        foreach (var npc in loc.characters)
        {
            if (LocationContentProjector.IsMonster(npc))
                continue;

            state.Npcs.Add(new NpcSummary
            {
                Name = npc.Name ?? string.Empty,
                Tile = new TilePoint { X = npc.TilePoint.X, Y = npc.TilePoint.Y },
            });
        }
```

In the objects loop, add optional object metadata:

```csharp
        foreach (var kv in loc.Objects.Pairs)
        {
            state.Objects.Add(new ObjectSummary
            {
                Tile = new TilePoint { X = (int)kv.Key.X, Y = (int)kv.Key.Y },
                Name = kv.Value.Name ?? kv.Value.GetType().Name,
                Id = kv.Value.ItemId ?? string.Empty,
                QualifiedId = kv.Value.QualifiedItemId ?? string.Empty,
                Category = kv.Value.Category,
                Stack = kv.Value.Stack,
                Quality = kv.Value.Quality,
            });
        }
```

Before `return state;`, add:

```csharp
        state.ResourceClumps.AddRange(LocationContentProjector.ProjectResourceClumps(loc));
        state.Monsters.AddRange(LocationContentProjector.ProjectMonsters(loc));
```

- [ ] **Step 5: Run the harness projector tests and fix compile errors only in the new projection surface**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter LocationContentProjectorTests
```

Expected: PASS. If SDV's `DamageToFarmer` property name differs at compile time, remove the object initializer assignment from the test and keep projection defensive through reflection.

- [ ] **Step 6: Run protocol and harness focused tests**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter LocationStateSerializationTests
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "LocationContentProjectorTests|StateLocationHandlerTests"
```

Expected: PASS with `StateLocationHandlerTests` still skipped where marked.

- [ ] **Step 7: Commit harness projection**

Run:

```bash
git add src/Harness/Handlers/LocationContentProjector.cs src/Harness/Handlers/LocationStateProjector.cs tests/Harness.Tests/LocationContentProjectorTests.cs
git commit -m "feat: project spawned location content"
```

## Task 3: Runner `wait.location_content`

**Files:**
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`
- Test: `tests/Runner.Tests/ScenarioRunnerTests.cs`

- [ ] **Step 1: Write failing runner tests**

Add these tests near the existing `wait.npc_location` tests in `tests/Runner.Tests/ScenarioRunnerTests.cs`:

```csharp
    [Fact]
    public async Task WaitLocationContent_PollsStateLocationUntilFilteredCountMatches()
    {
        var socket = SocketPath();
        var calls = new List<string>();
        var locationPolls = 0;
        JsonElement? lastLocationParams = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    calls.Add(req.Method);
                    if (req.Method == "state.location")
                        lastLocationParams = req.Params;

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.location" => JsonDocument.Parse(locationPolls++ == 0
                            ? "{\"name\":\"Custom_GrandpasShedOutside\",\"resource_clumps\":[],\"monsters\":[],\"objects\":[]}"
                            : "{\"name\":\"Custom_GrandpasShedOutside\",\"resource_clumps\":[{\"tile\":{\"x\":21,\"y\":17},\"kind\":\"ResourceClump\",\"id\":\"602\",\"name\":\"Log\"},{\"tile\":{\"x\":23,\"y\":17},\"kind\":\"ResourceClump\",\"id\":\"602\",\"name\":\"Log\"}],\"monsters\":[],\"objects\":[]}").RootElement,
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
            Name = "wait_location_content",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.location_content",
                    Args = JsonDocument.Parse("{\"location\":\"Custom_GrandpasShedOutside\",\"collection\":\"resource_clumps\",\"name\":\"Log\",\"min_count\":2,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);
        Assert.Equal(2, locationPolls);
        Assert.DoesNotContain("wait.location_content", calls);
        Assert.Contains("state.location", calls);
        Assert.Equal("Custom_GrandpasShedOutside", lastLocationParams!.Value.GetProperty("name").GetString());

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitLocationContent_FiltersByTileAndMaxCount()
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
                        "state.location" => JsonDocument.Parse("{\"name\":\"Custom_GrandpasShedOutside\",\"resource_clumps\":[{\"tile\":{\"x\":21,\"y\":17},\"kind\":\"ResourceClump\",\"id\":\"602\",\"name\":\"Log\"},{\"tile\":{\"x\":23,\"y\":17},\"kind\":\"ResourceClump\",\"id\":\"602\",\"name\":\"Log\"}],\"monsters\":[],\"objects\":[]}").RootElement,
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
            Name = "wait_location_content_tile",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.location_content",
                    Args = JsonDocument.Parse("{\"location\":\"Custom_GrandpasShedOutside\",\"collection\":\"resource_clumps\",\"name\":\"Log\",\"x\":21,\"y\":17,\"min_count\":1,\"max_count\":1,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitLocationContent_TimeoutIncludesLastObservedCounts()
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
                        "state.location" => JsonDocument.Parse("{\"name\":\"Custom_GrandpasShedOutside\",\"resource_clumps\":[{\"tile\":{\"x\":21,\"y\":17},\"kind\":\"ResourceClump\",\"id\":\"602\",\"name\":\"Log\"}],\"monsters\":[],\"objects\":[]}").RootElement,
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
            Name = "wait_location_content_timeout",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.location_content",
                    Args = JsonDocument.Parse("{\"location\":\"Custom_GrandpasShedOutside\",\"collection\":\"resource_clumps\",\"name\":\"Log\",\"min_count\":2,\"timeout_ms\":20,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.False(report.Passed);
        var failure = Assert.Single(report.Failures);
        Assert.Contains("wait.location_content timed out after 20ms waiting for at least 2 resource_clumps in Custom_GrandpasShedOutside", failure);
        Assert.Contains("last observed 1 matched out of 1 resource_clumps", failure);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitLocationContent_RejectsUnsupportedCollection()
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
            Name = "wait_location_content_bad_collection",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.location_content",
                    Args = JsonDocument.Parse("{\"location\":\"Farm\",\"collection\":\"mailboxes\",\"timeout_ms\":20,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.False(report.Passed);
        Assert.Contains("wait.location_content requires args.collection to be one of objects, resource_clumps, monsters, critters", Assert.Single(report.Failures));

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }
```

- [ ] **Step 2: Run the runner tests and verify they fail**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter WaitLocationContent
```

Expected: FAIL because the runner currently sends `wait.location_content` as an RPC instead of handling it client-side.

- [ ] **Step 3: Add dispatch, description, and auto-capture policy**

Modify `src/Runner/Scenarios/ScenarioRunner.cs`.

Add after `wait.npc_location` dispatch:

```csharp
                    else if (step.Action == "wait.location_content")
                    {
                        await InvokeWaitLocationContentAsync(step, ct);
                    }
```

Add to `DescribeStep`:

```csharp
            "wait.location_content" => $"Wait for {GetStringArg(step.Args, "collection") ?? "content"} in {GetStringArg(step.Args, "location") ?? "unknown"}",
```

Add to `ShouldAutoCaptureStep`:

```csharp
            "wait.location_content" => false,
```

- [ ] **Step 4: Add wait args, validation, filtering, and timeout details**

In `src/Runner/Scenarios/ScenarioRunner.cs`, add this method near `InvokeWaitNpcLocationAsync`:

```csharp
    private async Task InvokeWaitLocationContentAsync(ScenarioStep step, CancellationToken ct)
    {
        var args = step.Args is { ValueKind: JsonValueKind.Object } obj
            ? JsonSerializer.Deserialize<WaitLocationContentStepArgs>(obj.GetRawText(), ProtocolJson.Options)
                ?? new WaitLocationContentStepArgs()
            : new WaitLocationContentStepArgs();

        ValidateWaitLocationContentArgs(args);

        var request = ProtocolJson.ToElement(new { name = args.Location });
        var elapsed = Stopwatch.StartNew();
        int lastMatched = 0;
        int lastTotal = 0;
        while (elapsed.ElapsedMilliseconds < args.TimeoutMs)
        {
            ct.ThrowIfCancellationRequested();
            var resp = await _session.InvokeAsync("state.location", request, ct);
            if (resp.Error is { } error)
                throw new InvalidOperationException($"wait.location_content failed during state.location: {error.Message}");

            if (resp.Result is { } root)
            {
                lastMatched = CountLocationContentMatches(root, args, out lastTotal);
                var withinMin = lastMatched >= args.MinCount;
                var withinMax = args.MaxCount is null || lastMatched <= args.MaxCount.Value;
                if (withinMin && withinMax)
                    return;
            }

            await Task.Delay(args.PollMs, ct);
        }

        throw new TimeoutException(
            $"wait.location_content timed out after {args.TimeoutMs}ms waiting for {FormatExpectedContentCount(args)} " +
            $"{args.Collection} in {args.Location}{FormatLocationContentFilters(args)}; " +
            $"last observed {lastMatched} matched out of {lastTotal} {args.Collection}");
    }
```

Add helper methods near the other private helpers:

```csharp
    private static void ValidateWaitLocationContentArgs(WaitLocationContentStepArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.Location))
            throw new InvalidOperationException("wait.location_content requires args.location");
        if (string.IsNullOrWhiteSpace(args.Collection))
            throw new InvalidOperationException("wait.location_content requires args.collection");
        if (!AllowedLocationContentCollections.Contains(args.Collection))
            throw new InvalidOperationException("wait.location_content requires args.collection to be one of objects, resource_clumps, monsters, critters");
        if (args.MinCount < 1)
            throw new InvalidOperationException("wait.location_content requires args.min_count >= 1");
        if (args.MaxCount is not null && args.MaxCount < 1)
            throw new InvalidOperationException("wait.location_content requires args.max_count >= 1");
        if (args.MaxCount is not null && args.MaxCount < args.MinCount)
            throw new InvalidOperationException("wait.location_content requires args.max_count >= args.min_count");
        if (args.TimeoutMs < 1)
            throw new InvalidOperationException("wait.location_content requires args.timeout_ms >= 1");
        if (args.PollMs < 1)
            throw new InvalidOperationException("wait.location_content requires args.poll_ms >= 1");
        if ((args.X is null) != (args.Y is null))
            throw new InvalidOperationException("wait.location_content requires both args.x and args.y when filtering by tile");
    }

    private static int CountLocationContentMatches(JsonElement root, WaitLocationContentStepArgs args, out int totalCount)
    {
        totalCount = 0;
        if (args.Collection is null || !root.TryGetProperty(args.Collection, out var array) || array.ValueKind != JsonValueKind.Array)
            return 0;

        var matched = 0;
        foreach (var element in array.EnumerateArray())
        {
            totalCount++;
            if (LocationContentElementMatches(element, args))
                matched++;
        }
        return matched;
    }

    private static bool LocationContentElementMatches(JsonElement element, WaitLocationContentStepArgs args)
    {
        return StringFilterMatches(element, "name", args.Name)
            && StringFilterMatches(element, "type", args.Type)
            && StringFilterMatches(element, "kind", args.Kind)
            && StringFilterMatches(element, "id", args.Id)
            && StringFilterMatches(element, "qualified_id", args.QualifiedId)
            && TileFilterMatches(element, args.X, args.Y);
    }

    private static bool StringFilterMatches(JsonElement element, string property, string? expected)
    {
        if (expected is null) return true;
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            && string.Equals(value.GetString(), expected, StringComparison.Ordinal);
    }

    private static bool TileFilterMatches(JsonElement element, int? x, int? y)
    {
        if (x is null && y is null) return true;
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("tile", out var tile)
            && tile.ValueKind == JsonValueKind.Object
            && tile.TryGetProperty("x", out var tileX)
            && tile.TryGetProperty("y", out var tileY)
            && tileX.TryGetInt32(out var actualX)
            && tileY.TryGetInt32(out var actualY)
            && actualX == x
            && actualY == y;
    }

    private static string FormatExpectedContentCount(WaitLocationContentStepArgs args)
        => args.MaxCount is null
            ? $"at least {args.MinCount}"
            : args.MinCount == args.MaxCount.Value
                ? $"exactly {args.MinCount}"
                : $"between {args.MinCount} and {args.MaxCount.Value}";

    private static string FormatLocationContentFilters(WaitLocationContentStepArgs args)
    {
        var filters = new List<string>();
        if (args.Name is not null) filters.Add($"name={args.Name}");
        if (args.Type is not null) filters.Add($"type={args.Type}");
        if (args.Kind is not null) filters.Add($"kind={args.Kind}");
        if (args.Id is not null) filters.Add($"id={args.Id}");
        if (args.QualifiedId is not null) filters.Add($"qualified_id={args.QualifiedId}");
        if (args.X is not null && args.Y is not null) filters.Add($"tile={args.X},{args.Y}");
        return filters.Count == 0 ? string.Empty : $" matching {string.Join(", ", filters)}";
    }
```

Add the collection set near the other static helper fields or before the nested args classes:

```csharp
    private static readonly HashSet<string> AllowedLocationContentCollections = new(StringComparer.Ordinal)
    {
        "objects",
        "resource_clumps",
        "monsters",
        "critters",
    };
```

Add the nested args class next to `WaitNpcLocationStepArgs`:

```csharp
    private sealed class WaitLocationContentStepArgs
    {
        public string? Location { get; set; }
        public string? Collection { get; set; }
        public string? Name { get; set; }
        public string? Type { get; set; }
        public string? Kind { get; set; }
        public string? Id { get; set; }
        public string? QualifiedId { get; set; }
        public int? X { get; set; }
        public int? Y { get; set; }
        public int MinCount { get; set; } = 1;
        public int? MaxCount { get; set; }
        public int TimeoutMs { get; set; } = 10000;
        public int PollMs { get; set; } = 100;
    }
```

- [ ] **Step 5: Run the runner tests and verify they pass**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter WaitLocationContent
```

Expected: PASS.

- [ ] **Step 6: Commit runner wait support**

Run:

```bash
git add src/Runner/Scenarios/ScenarioRunner.cs tests/Runner.Tests/ScenarioRunnerTests.cs
git commit -m "feat: wait for location content"
```

## Task 4: Frobby Docs And TODO State

**Files:**
- Modify: `docs/rpc-schema.md`
- Modify: `docs/dsl-quickstart.md`
- Modify: `README.md`
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Update `state.location` docs**

Modify the `state.location` example in `docs/rpc-schema.md` so the response includes:

```json
      "objects": [{ "tile": { "x": 10, "y": 10 }, "name": "Weeds", "id": "O771", "qualified_id": "(O)771", "category": -999, "stack": 1, "quality": 0 }],
      "resource_clumps": [{ "tile": { "x": 21, "y": 17 }, "kind": "ResourceClump", "id": "602", "name": "Log", "width": 2, "height": 2, "health": 10 }],
      "monsters": [{ "tile": { "x": 44, "y": 31 }, "name": "Green Slime", "type": "GreenSlime", "health": 50, "max_health": 50, "damage": 10 }],
```

After the paragraph about unknown locations, add:

```markdown
`resource_clumps` contains large runtime world objects such as logs, stumps,
boulders, meteorites, and mine rocks when Stardew exposes them for the location.
`monsters` contains hostile creatures and is separate from `npcs`, which remains
for social/non-hostile NPCs. Optional object metadata fields may be empty or null
when Stardew or a mod does not expose them.
```

- [ ] **Step 2: Document `wait.location_content`**

Near the runner-only actions section in `docs/rpc-schema.md`, add:

```markdown
- `{ "action": "wait.location_content", "args": { "location": "Custom_GrandpasShedOutside", "collection": "resource_clumps", "name": "Log", "min_count": 2 } }` is runner-only. It polls `state.location` for the named location until the selected collection has enough matching entries. Supported collections are `objects`, `resource_clumps`, `monsters`, and `critters`. Filters are exact-match and optional: `name`, `type`, `kind`, `id`, `qualified_id`, and `x`/`y` tile. It accepts `min_count`, optional `max_count`, `timeout_ms`, and `poll_ms`, and reports the last matched/total counts on timeout.
```

- [ ] **Step 3: Update quickstart and README guidance**

In `docs/dsl-quickstart.md`, add this example near custom-location testing:

```markdown
For spawned world content, prefer `wait.location_content` over fixed sleeps:

```json
{
  "action": "wait.location_content",
  "args": {
    "location": "Custom_GrandpasShedOutside",
    "collection": "resource_clumps",
    "name": "Log",
    "min_count": 2,
    "timeout_ms": 10000,
    "poll_ms": 100
  }
}
```

This is mod-neutral: Frobby observes the runtime location state and does not call
Farm Type Manager or parse its content packs.
```

In `README.md`, add one bullet after the custom NPC bullet:

```markdown
- Use `state.location.resource_clumps`, `state.location.monsters`, and
  runner-side `wait.location_content` when testing spawned world content such as
  logs, boulders, forage-like objects, ore, or monsters. These helpers observe
  runtime Stardew state and stay independent from specific spawn frameworks.
```

- [ ] **Step 4: Mark Slice 5 active**

Modify `SVE_FROBBY_CAPABILITY_TODO.md` Slice 5 from:

```markdown
- [ ] Pending: Slice 5, Farm Type Manager spawn and conditional world content.
```

to:

```markdown
- [ ] Active: Slice 5, Farm Type Manager spawn and conditional world content.
  - Design spec: `docs/superpowers/specs/2026-05-08-sve-slice-5-ftm-spawn-world-content-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-08-sve-slice-5-ftm-spawn-world-content.md`.
```

Keep the existing SVE pressure and Frobby goal bullets under the new status.

- [ ] **Step 5: Run docs-adjacent validation**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter LocationStateSerializationTests
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter WaitLocationContent
```

Expected: PASS.

- [ ] **Step 6: Commit docs**

Run:

```bash
git add docs/rpc-schema.md docs/dsl-quickstart.md README.md SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "docs: describe location content waits"
```

## Task 5: SVE Scenario 07

**Files:**
- Create: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/07-sve-ftm-spawn-world-content.test.json`
- Modify: `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`

- [ ] **Step 1: Add the SVE scenario**

Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/07-sve-ftm-spawn-world-content.test.json`:

```json
{
  "name": "sve_ftm_spawn_world_content",
  "fixture": "m0spike_436515781",
  "config": { "seed": 42 },
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
      "action": "player.warp",
      "args": { "location": "Custom_GrandpasShedOutside", "x": 22, "y": 20 }
    },
    {
      "action": "wait.location",
      "args": {
        "location": "Custom_GrandpasShedOutside",
        "x": 22,
        "y": 20,
        "timeout_ms": 10000,
        "poll_ms": 100
      }
    },
    {
      "action": "wait.location_content",
      "args": {
        "location": "Custom_GrandpasShedOutside",
        "collection": "resource_clumps",
        "name": "Log",
        "min_count": 2,
        "timeout_ms": 10000,
        "poll_ms": 100
      }
    },
    {
      "action": "wait.location_content",
      "args": {
        "location": "Custom_GrandpasShedOutside",
        "collection": "resource_clumps",
        "name": "Log",
        "x": 21,
        "y": 17,
        "min_count": 1,
        "max_count": 1,
        "timeout_ms": 5000,
        "poll_ms": 100
      }
    },
    {
      "action": "wait.location_content",
      "args": {
        "location": "Custom_GrandpasShedOutside",
        "collection": "resource_clumps",
        "name": "Log",
        "x": 23,
        "y": 17,
        "min_count": 1,
        "max_count": 1,
        "timeout_ms": 5000,
        "poll_ms": 100
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.location.resource_clumps contains name 'Log'",
        "params": { "name": "Custom_GrandpasShedOutside" },
        "message": "SVE FTM should expose spawned Grandpa's Shed exterior logs"
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
  "assertions": []
}
```

- [ ] **Step 2: Update the SVE Frobby docs**

In `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`, add this paragraph after the core run command section:

```markdown
Scenario `tests/sdv/07-sve-ftm-spawn-world-content.test.json` covers the first
Farm Type Manager spawned-content path. It advances to a fresh day, warps to
`Custom_GrandpasShedOutside`, and uses Frobby's neutral `wait.location_content`
helper to assert the two Grandpa's Shed exterior logs spawned as runtime
resource clumps.
```

- [ ] **Step 3: Run the new scenario headlessly and verify the expected red/green boundary**

From `/home/fintan/stardewRepos/StardewValleyExpanded`, run:

```bash
scripts/sdv-test --headless --mod-set core tests/sdv/07-sve-ftm-spawn-world-content.test.json
```

Expected after Tasks 1-4 are implemented: PASS. If it fails because the live resource clump id maps to a different vanilla log id, update only `ResourceClumpName` in `LocationContentProjector` with that observed id, rerun the focused harness tests, and rerun this scenario.

- [ ] **Step 4: Run a small SVE smoke subset**

From `/home/fintan/stardewRepos/StardewValleyExpanded`, run:

```bash
scripts/sdv-test --headless --mod-set core tests/sdv/01-sve-core-loads.test.json tests/sdv/02-sve-custom-locations-register.test.json tests/sdv/06-sve-tile-action-warp.test.json tests/sdv/07-sve-ftm-spawn-world-content.test.json
```

Expected: PASS for all four scenarios.

- [ ] **Step 5: Commit SVE scenario work on the current SVE feature branch**

Run:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded add tests/sdv/07-sve-ftm-spawn-world-content.test.json docs/FROBBY.md
git -C /home/fintan/stardewRepos/StardewValleyExpanded commit -m "test: cover ftm spawned world content"
```

Do not merge SVE into `master`.

## Task 6: Final Verification And Slice 5 Status

**Files:**
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Mark Slice 5 complete in Frobby TODO**

Modify Slice 5 in `SVE_FROBBY_CAPABILITY_TODO.md` to:

```markdown
- [x] Done: Slice 5, Farm Type Manager spawn and conditional world content.
  - SVE pressure: FTM pack content, conditional forage/monster spawns, location-specific spawn rules, config/mail-gated difficulty variants.
  - Frobby goal: control spawn-relevant state, wait for spawns, inspect objects/monsters/critters in a location, and assert spawn counts/types deterministically.
  - Design spec: `docs/superpowers/specs/2026-05-08-sve-slice-5-ftm-spawn-world-content-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-08-sve-slice-5-ftm-spawn-world-content.md`.
  - Done: `state.location.resource_clumps`, `state.location.monsters`, optional object metadata, runner-side `wait.location_content`, and SVE scenario 07 for deterministic FTM large-object spawns.
  - Pending Slice 5 follow-up: monster spawn scenario using the same `monsters` and `wait.location_content` primitives once a low-flake SVE anchor is confirmed.
```

- [ ] **Step 2: Run the full Frobby test suite**

From `/home/fintan/stardewRepos/frobby/sdv-test-framework`, run:

```bash
dotnet test sdv-test-framework.slnx --configuration Debug
```

Expected: PASS with existing skipped live-SDV tests unchanged.

- [ ] **Step 3: Run SVE scenario 07 twice headlessly**

From `/home/fintan/stardewRepos/StardewValleyExpanded`, run:

```bash
scripts/sdv-test --headless --mod-set core tests/sdv/07-sve-ftm-spawn-world-content.test.json
scripts/sdv-test --headless --mod-set core tests/sdv/07-sve-ftm-spawn-world-content.test.json
```

Expected: PASS both times. The repeated run is the flake check for day-start spawn timing and resource-clump projection.

- [ ] **Step 4: Commit final Frobby status update**

Run:

```bash
git add SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "docs: complete sve slice 5"
```

- [ ] **Step 5: Inspect final git state**

Run:

```bash
git status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
```

Expected:

- Frobby on `main`, clean.
- SVE on its feature branch, clean.
- No SVE merge to `master`.

## Self-Review

**Spec coverage:** Tasks 1-2 cover additive `state.location` resource clump, monster, and object metadata. Task 3 covers runner-side waiting and count/type/tile filters. Task 4 covers Frobby docs and Slice 5 active state. Task 5 covers the deterministic SVE large-object scenario. Task 6 covers full verification and completion status. Monster scenario coverage is explicitly recorded as the next Slice 5 follow-up after the stable primitive lands, matching the approved direction.

**Placeholder scan:** No placeholder markers or vague deferred-work instructions are used. Each code-changing task has concrete code, exact files, commands, and expected results.

**Type consistency:** The plan consistently uses `ResourceClumpSummary`, `MonsterSummary`, `resource_clumps`, `monsters`, `wait.location_content`, `WaitLocationContentStepArgs`, and `LocationContentProjector` across tests, implementation, docs, and SVE scenario JSON.
