# SVE Slice 4 NPC Schedules Dialogue Relationships Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add neutral NPC discovery, relationship setup, NPC-location waits, and named-state assertions so SVE can validate Sophia schedule/dialogue/relationship flows through normal Frobby scenarios.

**Architecture:** Centralize NPC projection in one harness helper used by both `state.npc` and new `state.npcs`, add a focused `player.set_friendship` mutator, and keep schedule metadata best-effort so missing private SDV fields do not break state queries. The runner adds two neutral conveniences: `wait.npc_location` polling over `state.npc`, and optional `params` forwarding for `state.assert` so scenarios can assert parameterized state calls.

**Tech Stack:** C#/.NET 6 harness and protocol projects, .NET 10 runner and DSL projects, xUnit, SMAPI/Stardew Valley 1.6 runtime APIs, System.Text.Json, headless `sdv-test repo run` for SVE verification.

---

## File Structure

- Modify `src/Protocol/Models/NpcState.cs` - add optional display/social/schedule/action fields while preserving existing properties.
- Create `src/Protocol/Models/NpcsState.cs` - response DTO for `state.npcs`.
- Create `src/Protocol/Models/NpcsStateRequest.cs` - optional request DTO for `state.npcs`.
- Create `src/Protocol/Models/SetFriendshipRequest.cs` - request DTO for `player.set_friendship`.
- Modify `src/Protocol/Models/ScenarioAssertion.cs` - add optional `Params` for parameterized `state.assert` evaluation.
- Modify `tests/Protocol.Tests/NpcStateSerializationTests.cs` - cover new snake-case optional fields.
- Create `tests/Protocol.Tests/NpcsStateSerializationTests.cs` - cover `state.npcs` DTO shape.
- Create `tests/Protocol.Tests/SetFriendshipRequestSerializationTests.cs` - cover friendship mutator request shape.
- Create `tests/Protocol.Tests/ScenarioAssertionParamsSerializationTests.cs` - cover `params` passthrough shape.
- Create `src/Harness/Handlers/NpcStateProjector.cs` - single neutral projection helper for NPC state.
- Modify `src/Harness/Handlers/StateNpcHandler.cs` - delegate existing named NPC projection to `NpcStateProjector`.
- Create `src/Harness/Handlers/StateNpcsHandler.cs` - list runtime NPCs with bounded output and validation.
- Create `src/Harness/Handlers/PlayerSetFriendshipHandler.cs` - deterministic friendship mutator.
- Modify `src/Harness/ModEntry.cs` - register `state.npcs` and `player.set_friendship`, and update the startup method list.
- Create `tests/Harness.Tests/NpcStateProjectorTests.cs` - test pure helper behavior such as portrait normalization and friendship projection.
- Modify `tests/Harness.Tests/StateNpcHandlerTests.cs` - update expectations for validation stability.
- Create `tests/Harness.Tests/StateNpcsHandlerTests.cs` - validation tests for missing/invalid `limit`.
- Create `tests/Harness.Tests/PlayerSetFriendshipHandlerTests.cs` - validation tests and skipped live integration marker.
- Modify `src/Runner/Scenarios/ScenarioRunner.cs` - add `wait.npc_location`, `state.assert` params forwarding, descriptions, and screenshot auto-capture policy.
- Modify `tests/Runner.Tests/ScenarioRunnerTests.cs` - fake-harness coverage for `wait.npc_location` pass/timeout and `state.assert` params forwarding.
- Modify `src/Runner.Dsl/State.cs` - add `State.Npcs(...)`.
- Modify `src/Runner.Dsl/Player.cs` - add `Player.SetFriendship(...)`.
- Modify `tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs` - cover new DSL method shapes.
- Modify `schemas/scenario.schema.json` - document assertion-level `params` object.
- Modify `docs/rpc-schema.md`, `docs/dsl-quickstart.md`, and `README.md` - document new RPCs, runner wait, and parameterized state assertions.
- Add `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/05-sve-npc-schedules-dialogue-relationships.test.json` - live SVE proof scenario against Sophia.
- Modify `SVE_FROBBY_CAPABILITY_TODO.md` - record this implementation plan path and status changes during execution.

## Task 1: Protocol DTOs

**Files:**
- Modify: `src/Protocol/Models/NpcState.cs`
- Create: `src/Protocol/Models/NpcsState.cs`
- Create: `src/Protocol/Models/NpcsStateRequest.cs`
- Create: `src/Protocol/Models/SetFriendshipRequest.cs`
- Modify: `src/Protocol/Models/ScenarioAssertion.cs`
- Modify: `tests/Protocol.Tests/NpcStateSerializationTests.cs`
- Create: `tests/Protocol.Tests/NpcsStateSerializationTests.cs`
- Create: `tests/Protocol.Tests/SetFriendshipRequestSerializationTests.cs`
- Create: `tests/Protocol.Tests/ScenarioAssertionParamsSerializationTests.cs`

- [ ] **Step 1: Write failing protocol tests**

Append these tests to `tests/Protocol.Tests/NpcStateSerializationTests.cs`:

```csharp
    [Fact]
    public void Serialize_IncludesOptionalScheduleFieldsWhenPopulated()
    {
        var npc = new NpcState
        {
            Name = "Sophia",
            DisplayName = "Sophia",
            Location = "Custom_BlueMoonVineyard",
            Tile = new TilePoint { X = 20, Y = 32 },
            FriendshipPoints = 500,
            Hearts = 2,
            GiftGivenToday = false,
            TalkedToToday = true,
            Portrait = "Sophia",
            CurrentScheduleKey = "Mon",
            CurrentScheduleTime = 900,
            CurrentScheduleLocation = "Custom_BlueMoonVineyard",
            CurrentScheduleTile = new TilePoint { X = 20, Y = 32 },
            CurrentScheduleDirection = 0,
            CurrentScheduleAnimation = "Sophia_Farm2",
            IsVillager = true,
            CanSocialize = true,
        };

        var json = JsonSerializer.Serialize(npc, ProtocolJson.Options);

        Assert.Contains("\"display_name\":\"Sophia\"", json);
        Assert.Contains("\"talked_to_today\":true", json);
        Assert.Contains("\"current_schedule_key\":\"Mon\"", json);
        Assert.Contains("\"current_schedule_time\":900", json);
        Assert.Contains("\"current_schedule_location\":\"Custom_BlueMoonVineyard\"", json);
        Assert.Contains("\"current_schedule_tile\":{\"x\":20,\"y\":32}", json);
        Assert.Contains("\"current_schedule_direction\":0", json);
        Assert.Contains("\"current_schedule_animation\":\"Sophia_Farm2\"", json);
        Assert.Contains("\"is_villager\":true", json);
        Assert.Contains("\"can_socialize\":true", json);
    }
```

Create `tests/Protocol.Tests/NpcsStateSerializationTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class NpcsStateSerializationTests
{
    [Fact]
    public void Request_UsesSnakeCaseAndDefaults()
    {
        var req = JsonSerializer.Deserialize<NpcsStateRequest>("{}", ProtocolJson.Options)!;

        Assert.True(req.IncludeOffscreen);
        Assert.Equal(200, req.Limit);
    }

    [Fact]
    public void Request_DeserializesSnakeCase()
    {
        var req = JsonSerializer.Deserialize<NpcsStateRequest>(
            "{\"include_offscreen\":false,\"limit\":25}",
            ProtocolJson.Options)!;

        Assert.False(req.IncludeOffscreen);
        Assert.Equal(25, req.Limit);
    }

    [Fact]
    public void State_SerializesNpcList()
    {
        var state = new NpcsState
        {
            Npcs =
            {
                new NpcState
                {
                    Name = "Sophia",
                    Location = "Custom_SophiaHouse",
                    Tile = new TilePoint { X = 23, Y = 6 },
                    Portrait = "Sophia",
                },
            },
        };

        var json = JsonSerializer.Serialize(state, ProtocolJson.Options);

        Assert.Contains("\"npcs\":[", json);
        Assert.Contains("\"name\":\"Sophia\"", json);
    }
}
```

Create `tests/Protocol.Tests/SetFriendshipRequestSerializationTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class SetFriendshipRequestSerializationTests
{
    [Fact]
    public void Request_DeserializesSnakeCase()
    {
        var req = JsonSerializer.Deserialize<SetFriendshipRequest>(
            "{\"npc\":\"Sophia\",\"points\":500,\"talked_to_today\":true,\"gifts_today\":1,\"gifts_this_week\":2}",
            ProtocolJson.Options)!;

        Assert.Equal("Sophia", req.Npc);
        Assert.Equal(500, req.Points);
        Assert.True(req.TalkedToToday);
        Assert.Equal(1, req.GiftsToday);
        Assert.Equal(2, req.GiftsThisWeek);
    }
}
```

Create `tests/Protocol.Tests/ScenarioAssertionParamsSerializationTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class ScenarioAssertionParamsSerializationTests
{
    [Fact]
    public void Params_RoundTripsForStateAssertion()
    {
        var assertion = JsonSerializer.Deserialize<ScenarioAssertion>(
            "{\"type\":\"state\",\"expr\":\"state.npc.hearts == 2\",\"params\":{\"name\":\"Sophia\"}}",
            ProtocolJson.Options)!;

        Assert.Equal("state", assertion.Type);
        Assert.Equal("state.npc.hearts == 2", assertion.Expr);
        Assert.NotNull(assertion.Params);
        Assert.Equal("Sophia", assertion.Params.Value.GetProperty("name").GetString());
    }
}
```

- [ ] **Step 2: Run protocol tests to confirm failure**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter "NpcStateSerializationTests|NpcsStateSerializationTests|SetFriendshipRequestSerializationTests|ScenarioAssertionParamsSerializationTests"
```

Expected: compile fails because `NpcsState`, `NpcsStateRequest`, `SetFriendshipRequest`, and new `NpcState`/`ScenarioAssertion` properties are absent.

- [ ] **Step 3: Add DTOs and optional NPC fields**

Modify `src/Protocol/Models/NpcState.cs` by adding these properties after `Name` and after existing social fields:

```csharp
    /// <summary>Best-effort localized display name. Falls back to <see cref="Name"/>.</summary>
    public string? DisplayName { get; set; }
```

Add these properties after `GiftGivenToday`:

```csharp
    /// <summary>True when the farmer has talked to this NPC today, if friendship data exists.</summary>
    public bool? TalkedToToday { get; set; }
```

Add these properties after `Portrait`:

```csharp
    /// <summary>Best-effort schedule key currently selected by SDV or a mod, when discoverable.</summary>
    public string? CurrentScheduleKey { get; set; }

    /// <summary>Best-effort current schedule time or observed in-game time for the NPC snapshot.</summary>
    public int? CurrentScheduleTime { get; set; }

    /// <summary>Best-effort current schedule or runtime location for the NPC snapshot.</summary>
    public string? CurrentScheduleLocation { get; set; }

    /// <summary>Best-effort current schedule or runtime tile for the NPC snapshot.</summary>
    public TilePoint? CurrentScheduleTile { get; set; }

    /// <summary>Best-effort facing direction for the current schedule/action snapshot.</summary>
    public int? CurrentScheduleDirection { get; set; }

    /// <summary>Best-effort current schedule animation/action name, when discoverable.</summary>
    public string? CurrentScheduleAnimation { get; set; }

    /// <summary>Best-effort villager flag, when discoverable.</summary>
    public bool? IsVillager { get; set; }

    /// <summary>Best-effort social interaction flag, when discoverable.</summary>
    public bool? CanSocialize { get; set; }
```

Create `src/Protocol/Models/NpcsState.cs`:

```csharp
using System.Collections.Generic;

namespace SdvTestFramework.Protocol.Models;

/// <summary>Response shape for <c>state.npcs</c>.</summary>
public sealed class NpcsState
{
    public List<NpcState> Npcs { get; set; } = new();
}
```

Create `src/Protocol/Models/NpcsStateRequest.cs`:

```csharp
namespace SdvTestFramework.Protocol.Models;

/// <summary>Optional request shape for <c>state.npcs</c>.</summary>
public sealed class NpcsStateRequest
{
    public bool IncludeOffscreen { get; set; } = true;
    public int Limit { get; set; } = 200;
}
```

Create `src/Protocol/Models/SetFriendshipRequest.cs`:

```csharp
namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape for <c>player.set_friendship</c>.</summary>
public sealed class SetFriendshipRequest
{
    public string Npc { get; set; } = string.Empty;
    public int? Points { get; set; }
    public bool? TalkedToToday { get; set; }
    public int? GiftsToday { get; set; }
    public int? GiftsThisWeek { get; set; }
}
```

Modify `src/Protocol/Models/ScenarioAssertion.cs` by adding this property near the assertion selector fields:

```csharp
    /// <summary>Optional params passed to the state RPC for parameterized <c>state</c> assertions.</summary>
    public JsonElement? Params { get; set; }
```

If the file does not already import `System.Text.Json`, add:

```csharp
using System.Text.Json;
```

- [ ] **Step 4: Run protocol tests to confirm pass**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter "NpcStateSerializationTests|NpcsStateSerializationTests|SetFriendshipRequestSerializationTests|ScenarioAssertionParamsSerializationTests"
```

Expected: all selected protocol tests pass.

- [ ] **Step 5: Commit protocol DTOs**

Run:

```bash
git add src/Protocol/Models/NpcState.cs src/Protocol/Models/NpcsState.cs src/Protocol/Models/NpcsStateRequest.cs src/Protocol/Models/SetFriendshipRequest.cs src/Protocol/Models/ScenarioAssertion.cs tests/Protocol.Tests/NpcStateSerializationTests.cs tests/Protocol.Tests/NpcsStateSerializationTests.cs tests/Protocol.Tests/SetFriendshipRequestSerializationTests.cs tests/Protocol.Tests/ScenarioAssertionParamsSerializationTests.cs
git commit -m "feat: add npc relationship protocol models"
```

Expected: commit succeeds.

## Task 2: Shared NPC Projection And Expanded `state.npc`

**Files:**
- Create: `src/Harness/Handlers/NpcStateProjector.cs`
- Modify: `src/Harness/Handlers/StateNpcHandler.cs`
- Create: `tests/Harness.Tests/NpcStateProjectorTests.cs`
- Modify: `tests/Harness.Tests/StateNpcHandlerTests.cs`

- [ ] **Step 1: Write failing projector tests**

Create `tests/Harness.Tests/NpcStateProjectorTests.cs`:

```csharp
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class NpcStateProjectorTests
{
    [Theory]
    [InlineData("Portraits/Abigail", "Abigail")]
    [InlineData("Portraits\\Sophia", "Sophia")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NormalizePortraitName_ReturnsBaseName(string? raw, string? expected)
    {
        Assert.Equal(expected, NpcStateProjector.NormalizePortraitName(raw));
    }

    [Fact]
    public void ApplyFriendship_MapsPointsHeartsAndFlags()
    {
        var state = new NpcState { Name = "Sophia" };
        var friendship = new Friendship
        {
            Points = 500,
            TalkedToToday = true,
            GiftsToday = 1,
            GiftsThisWeek = 2,
        };

        NpcStateProjector.ApplyFriendship(state, friendship);

        Assert.Equal(500, state.FriendshipPoints);
        Assert.Equal(2, state.Hearts);
        Assert.True(state.GiftGivenToday);
        Assert.True(state.TalkedToToday);
    }
}
```

Add this validation test to `tests/Harness.Tests/StateNpcHandlerTests.cs` if it is not present:

```csharp
    [Fact]
    public void Handle_MissingName_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => StateNpcHandler.Handle(null));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }
```

- [ ] **Step 2: Run harness tests to confirm failure**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "NpcStateProjectorTests|StateNpcHandlerTests"
```

Expected: compile fails because `NpcStateProjector` does not exist.

- [ ] **Step 3: Add the projector**

Create `src/Harness/Handlers/NpcStateProjector.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Projects SDV NPC runtime objects into neutral protocol snapshots.</summary>
public static class NpcStateProjector
{
    public static NpcState Project(NPC npc, Farmer? farmer)
    {
        var name = npc.Name ?? string.Empty;
        var tile = new TilePoint { X = npc.TilePoint.X, Y = npc.TilePoint.Y };
        var state = new NpcState
        {
            Name = name,
            DisplayName = ReadString(npc, "displayName", "DisplayName") ?? name,
            Location = npc.currentLocation?.Name ?? string.Empty,
            Tile = tile,
            Portrait = NormalizePortraitName(npc.Portrait?.Name) ?? name,
            CurrentScheduleKey = ReadString(npc, "currentScheduleKey", "CurrentScheduleKey", "scheduleKey", "ScheduleKey"),
            CurrentScheduleTime = Game1.timeOfDay > 0 ? Game1.timeOfDay : null,
            CurrentScheduleLocation = npc.currentLocation?.Name,
            CurrentScheduleTile = tile,
            CurrentScheduleDirection = npc.FacingDirection,
            CurrentScheduleAnimation = ReadString(npc, "currentScheduleAnimation", "CurrentScheduleAnimation", "endOfRouteBehaviorName", "EndOfRouteBehaviorName"),
            IsVillager = ReadBool(npc, "IsVillager", "isVillager"),
            CanSocialize = ReadBool(npc, "CanSocialize", "canSocialize") ?? ReadBool(npc, "IsVillager", "isVillager"),
        };

        if (farmer?.friendshipData is { } data && data.TryGetValue(name, out var friendship))
            ApplyFriendship(state, friendship);

        return state;
    }

    public static List<NpcState> ProjectMany(IEnumerable<NPC> npcs, Farmer? farmer, int limit)
        => NpcsDistinct(npcs)
            .Take(limit)
            .Select(npc => Project(npc, farmer))
            .ToList();

    public static void ApplyFriendship(NpcState state, Friendship friendship)
    {
        state.FriendshipPoints = friendship.Points;
        state.Hearts = friendship.Points / 250;
        state.GiftGivenToday = friendship.GiftsToday > 0;
        state.TalkedToToday = friendship.TalkedToToday;
    }

    public static string? NormalizePortraitName(string? rawAssetName)
    {
        if (string.IsNullOrEmpty(rawAssetName)) return null;
        return System.IO.Path.GetFileNameWithoutExtension(rawAssetName.Replace('\\', '/'));
    }

    private static IEnumerable<NPC> NpcsDistinct(IEnumerable<NPC> npcs)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var npc in npcs)
        {
            if (npc is null) continue;
            var name = npc.Name ?? string.Empty;
            if (name.Length == 0 || !seen.Add(name)) continue;
            yield return npc;
        }
    }

    private static string? ReadString(object instance, params string[] names)
        => ReadMember<string>(instance, names);

    private static bool? ReadBool(object instance, params string[] names)
        => ReadMember<bool>(instance, names);

    private static T? ReadMember<T>(object instance, params string[] names)
    {
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = instance.GetType();
        foreach (var name in names)
        {
            var property = type.GetProperty(name, flags);
            if (property is not null && property.GetValue(instance) is T propertyValue)
                return propertyValue;

            var field = type.GetField(name, flags);
            if (field is not null && field.GetValue(instance) is T fieldValue)
                return fieldValue;

            var method = type.GetMethod(name, flags, binder: null, Type.EmptyTypes, modifiers: null);
            if (method is not null && method.Invoke(instance, Array.Empty<object>()) is T methodValue)
                return methodValue;
        }

        return default;
    }
}
```

- [ ] **Step 4: Refactor `state.npc` to use the projector**

Modify `src/Harness/Handlers/StateNpcHandler.cs` so the body after the NPC lookup becomes:

```csharp
        var state = NpcStateProjector.Project(npc, Game1.player);
        return ProtocolJson.ToElement(state);
```

Remove the now-duplicated friendship and `NormalizePortraitName` helper code from `StateNpcHandler`.

- [ ] **Step 5: Run harness tests**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "NpcStateProjectorTests|StateNpcHandlerTests"
```

Expected: selected tests pass.

- [ ] **Step 6: Commit projector and expanded `state.npc`**

Run:

```bash
git add src/Harness/Handlers/NpcStateProjector.cs src/Harness/Handlers/StateNpcHandler.cs tests/Harness.Tests/NpcStateProjectorTests.cs tests/Harness.Tests/StateNpcHandlerTests.cs
git commit -m "feat: project expanded npc state"
```

Expected: commit succeeds.

## Task 3: `state.npcs`

**Files:**
- Create: `src/Harness/Handlers/StateNpcsHandler.cs`
- Modify: `src/Harness/ModEntry.cs`
- Create: `tests/Harness.Tests/StateNpcsHandlerTests.cs`
- Modify: `src/Runner.Dsl/State.cs`
- Modify: `tests/Runner.Dsl.Tests/Facets/StateFacetTests.cs` or `tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs`

- [ ] **Step 1: Write failing handler validation tests**

Create `tests/Harness.Tests/StateNpcsHandlerTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class StateNpcsHandlerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public void Handle_InvalidLimit_ThrowsInvalidParams(int limit)
    {
        var p = JsonSerializer.SerializeToElement(new { limit });

        var ex = Assert.Throws<JsonRpcException>(() => StateNpcsHandler.Handle(p));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("limit", ex.Message);
    }

    [Fact(Skip = "Requires live SDV (Game1.locations/currentLocation NPC collections).")]
    public void Handle_DefaultParams_ReturnsRuntimeNpcs() { }
}
```

Add a DSL test near the existing `State.Npc` coverage:

```csharp
    [Fact]
    public async Task Npcs_InvokesStateNpcsWithOptions()
    {
        var inv = new CapturingInvoker
        {
            NextResponse = JsonDocument.Parse("{\"npcs\":[]}").RootElement,
        };
        SdvTestSession.InitializeForTests(inv);
        try { await State.Npcs(includeOffscreen: false, limit: 25); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Equal("state.npcs", inv.Calls[0].Method);
        Assert.Contains("\"include_offscreen\":false", inv.Calls[0].ParamsJson);
        Assert.Contains("\"limit\":25", inv.Calls[0].ParamsJson);
    }
```

- [ ] **Step 2: Run tests to confirm failure**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter StateNpcsHandlerTests
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter Npcs_InvokesStateNpcsWithOptions
```

Expected: compile fails because `StateNpcsHandler` and `State.Npcs` do not exist.

- [ ] **Step 3: Add `state.npcs` handler**

Create `src/Harness/Handlers/StateNpcsHandler.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>state.npcs</c>. Runs on the game thread.</summary>
public static class StateNpcsHandler
{
    public const string Method = "state.npcs";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var req = RpcParams.Optional<NpcsStateRequest>(paramsElement);
        if (req.Limit < 1 || req.Limit > 1000)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.limit must be between 1 and 1000");

        var npcs = req.IncludeOffscreen ? AllLoadedLocationNpcs() : CurrentLocationNpcs();
        var state = new NpcsState
        {
            Npcs = NpcStateProjector.ProjectMany(npcs, Game1.player, req.Limit),
        };
        return ProtocolJson.ToElement(state);
    }

    private static IEnumerable<NPC> AllLoadedLocationNpcs()
        => Game1.locations is null
            ? CurrentLocationNpcs()
            : Game1.locations
                .Where(location => location?.characters is not null)
                .SelectMany(location => location.characters);

    private static IEnumerable<NPC> CurrentLocationNpcs()
        => Game1.currentLocation?.characters ?? Enumerable.Empty<NPC>();
}
```

Modify `src/Harness/ModEntry.cs` by registering after `state.npc`:

```csharp
        _rpc.Register(StateNpcsHandler.Method, p => StateNpcsHandler.Handle(p));
```

Update the startup log's RPC method list to include `state.npcs`.

- [ ] **Step 4: Add DSL wrapper**

Modify `src/Runner.Dsl/State.cs` after `Locations` or `Npc`:

```csharp
    public static async Task<NpcsState> Npcs(
        bool includeOffscreen = true,
        int limit = 200,
        CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new NpcsStateRequest
        {
            IncludeOffscreen = includeOffscreen,
            Limit = limit,
        }, ProtocolJson.Options);
        var resp = await s.InvokeAsync("state.npcs", p, ct);
        return Deserialize<NpcsState>(resp, "state.npcs");
    }
```

- [ ] **Step 5: Run selected tests**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter StateNpcsHandlerTests
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter Npcs_InvokesStateNpcsWithOptions
```

Expected: selected tests pass.

- [ ] **Step 6: Commit `state.npcs`**

Run:

```bash
git add src/Harness/Handlers/StateNpcsHandler.cs src/Harness/ModEntry.cs tests/Harness.Tests/StateNpcsHandlerTests.cs src/Runner.Dsl/State.cs tests/Runner.Dsl.Tests
git commit -m "feat: add npc list state query"
```

Expected: commit succeeds.

## Task 4: `player.set_friendship`

**Files:**
- Create: `src/Harness/Handlers/PlayerSetFriendshipHandler.cs`
- Modify: `src/Harness/ModEntry.cs`
- Create: `tests/Harness.Tests/PlayerSetFriendshipHandlerTests.cs`
- Modify: `src/Runner.Dsl/Player.cs`
- Modify: `tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs`

- [ ] **Step 1: Write failing validation and DSL tests**

Create `tests/Harness.Tests/PlayerSetFriendshipHandlerTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class PlayerSetFriendshipHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => PlayerSetFriendshipHandler.Handle(null));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Handle_BlankNpc_ThrowsInvalidParams(string npc)
    {
        var p = JsonSerializer.SerializeToElement(new { npc, points = 500 });

        var ex = Assert.Throws<JsonRpcException>(() => PlayerSetFriendshipHandler.Handle(p));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("npc", ex.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2501)]
    public void Handle_OutOfRangePoints_ThrowsInvalidParams(int points)
    {
        var p = JsonSerializer.SerializeToElement(new { npc = "Sophia", points });

        var ex = Assert.Throws<JsonRpcException>(() => PlayerSetFriendshipHandler.Handle(p));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("points", ex.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void Handle_OutOfRangeGiftCounts_ThrowsInvalidParams(int gifts)
    {
        var p = JsonSerializer.SerializeToElement(new
        {
            npc = "Sophia",
            points = 500,
            gifts_today = gifts,
        });

        var ex = Assert.Throws<JsonRpcException>(() => PlayerSetFriendshipHandler.Handle(p));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("gifts_today", ex.Message);
    }

    [Fact(Skip = "Requires live SDV (Game1.MasterPlayer.friendshipData read/write).")]
    public void Handle_ValidRequest_SetsFriendshipEntry() { }
}
```

Add to `tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs`:

```csharp
    [Fact]
    public async Task SetFriendship_InvokesPlayerSetFriendship()
    {
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try
        {
            await Player.SetFriendship("Sophia", 500, talkedToToday: true, giftsToday: 1, giftsThisWeek: 2);
        }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Equal("player.set_friendship", inv.Calls[0].Method);
        Assert.Contains("\"npc\":\"Sophia\"", inv.Calls[0].ParamsJson);
        Assert.Contains("\"points\":500", inv.Calls[0].ParamsJson);
        Assert.Contains("\"talked_to_today\":true", inv.Calls[0].ParamsJson);
        Assert.Contains("\"gifts_today\":1", inv.Calls[0].ParamsJson);
        Assert.Contains("\"gifts_this_week\":2", inv.Calls[0].ParamsJson);
    }
```

- [ ] **Step 2: Run tests to confirm failure**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter PlayerSetFriendshipHandlerTests
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter SetFriendship_InvokesPlayerSetFriendship
```

Expected: compile fails because `PlayerSetFriendshipHandler` and `Player.SetFriendship` do not exist.

- [ ] **Step 3: Add friendship mutator handler**

Create `src/Harness/Handlers/PlayerSetFriendshipHandler.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>player.set_friendship</c>. Runs on the game thread.</summary>
public static class PlayerSetFriendshipHandler
{
    public const string Method = "player.set_friendship";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var req = RpcParams.Required<SetFriendshipRequest>(paramsElement);
        var npc = req.Npc.Trim();
        if (npc.Length == 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.npc must be non-empty");
        if (req.Points is null || req.Points < 0 || req.Points > 2500)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.points must be between 0 and 2500");
        ValidateGiftCount(req.GiftsToday, "gifts_today");
        ValidateGiftCount(req.GiftsThisWeek, "gifts_this_week");

        RpcPreconditions.RequireWorldReady();

        if (!Game1.MasterPlayer.friendshipData.TryGetValue(npc, out var friendship))
        {
            friendship = new Friendship();
            Game1.MasterPlayer.friendshipData[npc] = friendship;
        }

        friendship.Points = req.Points.Value;
        if (req.TalkedToToday.HasValue) friendship.TalkedToToday = req.TalkedToToday.Value;
        if (req.GiftsToday.HasValue) friendship.GiftsToday = req.GiftsToday.Value;
        if (req.GiftsThisWeek.HasValue) friendship.GiftsThisWeek = req.GiftsThisWeek.Value;

        return ProtocolJson.ToElement(new MutatorOk { Tick = Game1.ticks });
    }

    private static void ValidateGiftCount(int? value, string field)
    {
        if (value is < 0 or > 2)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, $"params.{field} must be between 0 and 2");
    }
}
```

Modify `src/Harness/ModEntry.cs` by registering after `PlayerAddMailHandler`:

```csharp
        _rpc.Register(PlayerSetFriendshipHandler.Method, p => PlayerSetFriendshipHandler.Handle(p));
```

Update the startup log's manipulator list to include `player.set_friendship`.

- [ ] **Step 4: Add DSL wrapper**

Modify `src/Runner.Dsl/Player.cs` after `AddMail`:

```csharp
    /// <summary>Set friendship state for a vanilla or custom NPC.</summary>
    public static async Task SetFriendship(
        string npc,
        int points,
        bool? talkedToToday = null,
        int? giftsToday = null,
        int? giftsThisWeek = null,
        CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new SetFriendshipRequest
        {
            Npc = npc,
            Points = points,
            TalkedToToday = talkedToToday,
            GiftsToday = giftsToday,
            GiftsThisWeek = giftsThisWeek,
        }, ProtocolJson.Options);
        await s.InvokeAsync("player.set_friendship", p, ct);
    }
```

- [ ] **Step 5: Run selected tests**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter PlayerSetFriendshipHandlerTests
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter SetFriendship_InvokesPlayerSetFriendship
```

Expected: selected tests pass, with the live integration marker skipped.

- [ ] **Step 6: Commit friendship mutator**

Run:

```bash
git add src/Harness/Handlers/PlayerSetFriendshipHandler.cs src/Harness/ModEntry.cs tests/Harness.Tests/PlayerSetFriendshipHandlerTests.cs src/Runner.Dsl/Player.cs tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs
git commit -m "feat: add friendship state mutator"
```

Expected: commit succeeds.

## Task 5: Runner `wait.npc_location` And Parameterized `state.assert`

**Files:**
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`
- Modify: `tests/Runner.Tests/ScenarioRunnerTests.cs`

- [ ] **Step 1: Write failing runner tests**

Add to `tests/Runner.Tests/ScenarioRunnerTests.cs`:

```csharp
    [Fact]
    public async Task WaitNpcLocation_PollsStateNpcUntilLocationMatches()
    {
        var socket = SocketPath();
        var calls = new List<string>();
        var npcPolls = 0;
        JsonElement? lastNpcParams = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    calls.Add(req.Method);
                    if (req.Method == "state.npc")
                        lastNpcParams = req.Params;

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.npc" => JsonDocument.Parse(npcPolls++ == 0
                            ? "{\"name\":\"Sophia\",\"location\":\"Custom_SophiaHouse\",\"tile\":{\"x\":23,\"y\":6},\"friendship_points\":0,\"hearts\":0,\"gift_given_today\":false,\"portrait\":\"Sophia\"}"
                            : "{\"name\":\"Sophia\",\"location\":\"Custom_BlueMoonVineyard\",\"tile\":{\"x\":20,\"y\":32},\"friendship_points\":0,\"hearts\":0,\"gift_given_today\":false,\"portrait\":\"Sophia\"}").RootElement,
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

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_npc_location",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.npc_location",
                    Args = JsonDocument.Parse("{\"name\":\"Sophia\",\"location\":\"Custom_BlueMoonVineyard\",\"x\":20,\"y\":32,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);
        Assert.Equal(2, npcPolls);
        Assert.DoesNotContain("wait.npc_location", calls);
        Assert.Contains("state.npc", calls);
        Assert.Equal("Sophia", lastNpcParams!.Value.GetProperty("name").GetString());

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WaitNpcLocation_TimeoutIncludesLastObservedNpcLocation()
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
                        "state.npc" => JsonDocument.Parse("{\"name\":\"Sophia\",\"location\":\"Custom_SophiaHouse\",\"tile\":{\"x\":23,\"y\":6},\"friendship_points\":0,\"hearts\":0,\"gift_given_today\":false,\"portrait\":\"Sophia\"}").RootElement,
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

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_npc_location_timeout",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.npc_location",
                    Args = JsonDocument.Parse("{\"name\":\"Sophia\",\"location\":\"Custom_BlueMoonVineyard\",\"timeout_ms\":20,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.False(report.Passed);
        var failure = Assert.Single(report.Failures);
        Assert.Contains("wait.npc_location timed out after 20ms waiting for Sophia in Custom_BlueMoonVineyard", failure);
        Assert.Contains("last observed Custom_SophiaHouse at 23,6", failure);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }
```

Add a focused state assert params test:

```csharp
    [Fact]
    public async Task StateAssert_ForwardsParamsToStateMethod()
    {
        var socket = SocketPath();
        JsonElement? stateNpcParams = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    if (req.Method == "state.npc")
                        stateNpcParams = req.Params;

                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "state.npc" => JsonDocument.Parse("{\"name\":\"Sophia\",\"location\":\"Custom_BlueMoonVineyard\",\"tile\":{\"x\":20,\"y\":32},\"friendship_points\":500,\"hearts\":2,\"gift_given_today\":false,\"portrait\":\"Sophia\"}").RootElement,
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

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "state_assert_params",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "state.assert",
                    Args = JsonDocument.Parse("{\"expr\":\"state.npc.hearts == 2\",\"params\":{\"name\":\"Sophia\"}}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);
        Assert.Equal("Sophia", stateNpcParams!.Value.GetProperty("name").GetString());

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }
```

- [ ] **Step 2: Run runner tests to confirm failure**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "WaitNpcLocation|StateAssert_ForwardsParamsToStateMethod"
```

Expected: tests fail because `wait.npc_location` is dispatched as a normal RPC and `state.assert` ignores `args.params`.

- [ ] **Step 3: Add `wait.npc_location` dispatch and parser**

Modify the step dispatch in `src/Runner/Scenarios/ScenarioRunner.cs` after `wait.location`:

```csharp
                    else if (step.Action == "wait.npc_location")
                    {
                        await InvokeWaitNpcLocationAsync(step, ct);
                    }
```

Add this method near `InvokeWaitLocationAsync`:

```csharp
    private async Task InvokeWaitNpcLocationAsync(ScenarioStep step, CancellationToken ct)
    {
        var args = step.Args is { ValueKind: JsonValueKind.Object } obj
            ? JsonSerializer.Deserialize<WaitNpcLocationStepArgs>(obj.GetRawText(), ProtocolJson.Options)
                ?? new WaitNpcLocationStepArgs()
            : new WaitNpcLocationStepArgs();

        if (string.IsNullOrWhiteSpace(args.Name))
            throw new InvalidOperationException("wait.npc_location requires args.name");
        if (string.IsNullOrWhiteSpace(args.Location))
            throw new InvalidOperationException("wait.npc_location requires args.location");
        if (args.TimeoutMs < 1)
            throw new InvalidOperationException("wait.npc_location requires args.timeout_ms >= 1");
        if (args.PollMs < 1)
            throw new InvalidOperationException("wait.npc_location requires args.poll_ms >= 1");

        var elapsed = Stopwatch.StartNew();
        NpcState? lastObserved = null;
        while (elapsed.ElapsedMilliseconds < args.TimeoutMs)
        {
            ct.ThrowIfCancellationRequested();
            var p = ProtocolJson.ToElement(new { name = args.Name });
            var resp = await _session.InvokeAsync("state.npc", p, ct);
            if (resp.Error is { } error)
                throw new InvalidOperationException($"wait.npc_location failed during state.npc: {error.Message}");
            if (resp.Result is { } result)
                lastObserved = JsonSerializer.Deserialize<NpcState>(result.GetRawText(), ProtocolJson.Options);

            if (lastObserved is not null
                && string.Equals(lastObserved.Location, args.Location, StringComparison.Ordinal)
                && (args.X is null || args.X == lastObserved.Tile.X)
                && (args.Y is null || args.Y == lastObserved.Tile.Y))
            {
                return;
            }

            await Task.Delay(args.PollMs, ct);
        }

        var expectedTile = args.X is not null && args.Y is not null
            ? $" at {args.X},{args.Y}"
            : string.Empty;
        var last = lastObserved is null
            ? "nothing"
            : $"{lastObserved.Location} at {lastObserved.Tile.X},{lastObserved.Tile.Y}";
        throw new TimeoutException(
            $"wait.npc_location timed out after {args.TimeoutMs}ms waiting for {args.Name} in {args.Location}{expectedTile}; " +
            $"last observed {last}");
    }
```

Add this private class near `WaitLocationStepArgs`:

```csharp
    private sealed class WaitNpcLocationStepArgs
    {
        public string? Name { get; set; }
        public string? Location { get; set; }
        public int? X { get; set; }
        public int? Y { get; set; }
        public int TimeoutMs { get; set; } = 10000;
        public int PollMs { get; set; } = 100;
    }
```

Update `DescribeStep`:

```csharp
            "wait.npc_location" => $"Wait for NPC {GetStringArg(step.Args, "name") ?? "unknown"} in {GetStringArg(step.Args, "location") ?? "unknown"}",
```

Update `ShouldAutoCaptureStep`:

```csharp
            "wait.npc_location" => false,
```

- [ ] **Step 4: Add `state.assert` params forwarding**

Modify `InvokeStateAssertAsync`:

```csharp
        JsonElement? stateParams = null;
        if (step.Args is { ValueKind: JsonValueKind.Object } args
            && args.TryGetProperty("params", out var paramsElement))
        {
            stateParams = paramsElement.Clone();
        }

        var (passed, detail) = await EvaluateAssertionAsync(
            new ScenarioAssertion
            {
                Type = "state",
                Expr = expr,
                Message = message,
                Params = stateParams,
            },
            assertionIndex: -1,
            ct);
```

In `EvaluateAssertionAsync`, replace both state-method invocations in the `"state"` case:

```csharp
                    var containsResp = await _session.InvokeAsync(containsMethod, a.Params, ct);
```

and:

```csharp
                var resp = await _session.InvokeAsync(method, a.Params, ct);
```

- [ ] **Step 5: Run runner tests**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "WaitNpcLocation|StateAssert_ForwardsParamsToStateMethod"
```

Expected: selected runner tests pass.

- [ ] **Step 6: Commit runner conveniences**

Run:

```bash
git add src/Runner/Scenarios/ScenarioRunner.cs tests/Runner.Tests/ScenarioRunnerTests.cs
git commit -m "feat: add npc location wait"
```

Expected: commit succeeds.

## Task 6: Docs And Schema

**Files:**
- Modify: `schemas/scenario.schema.json`
- Modify: `docs/rpc-schema.md`
- Modify: `docs/dsl-quickstart.md`
- Modify: `README.md`
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Update scenario schema**

Modify `schemas/scenario.schema.json` under `assertions.items.properties`:

```json
          "params": {
            "type": "object",
            "description": "Optional state RPC params for type=state assertions."
          },
```

No schema change is required for `wait.npc_location` because `steps.items.properties.action` accepts any non-empty string and `args` accepts any object.

- [ ] **Step 2: Update RPC docs**

In `docs/rpc-schema.md`, extend the `state.npc` response example with optional fields:

```json
      "display_name": "Sophia",
      "talked_to_today": false,
      "current_schedule_time": 900,
      "current_schedule_location": "Custom_BlueMoonVineyard",
      "current_schedule_tile": { "x": 20, "y": 32 },
      "current_schedule_direction": 0,
      "current_schedule_animation": "Sophia_Farm2",
      "is_villager": true,
      "can_socialize": true
```

Add a new `state.npcs` section after `state.npc`:

````markdown
### state.npcs

Lists known runtime NPCs from loaded locations. Params are optional.

Request:
```json
-> { "jsonrpc": "2.0", "id": 14, "method": "state.npcs", "params": { "include_offscreen": true, "limit": 200 } }
```

Response:
```json
<- { "jsonrpc": "2.0", "id": 14, "result": { "npcs": [
      { "name": "Sophia", "display_name": "Sophia", "location": "Custom_SophiaHouse",
        "tile": { "x": 23, "y": 6 }, "friendship_points": 0, "hearts": 0,
        "gift_given_today": false, "talked_to_today": false, "portrait": "Sophia" }
   ] } }
```

`include_offscreen=false` restricts output to `Game1.currentLocation.characters`.
`limit` defaults to 200 and must be between 1 and 1000. Schedule/action fields are
best-effort and may be omitted when Stardew or a mod does not expose them safely.

**Preconditions:** none beyond a loaded runtime with locations; empty runtime state returns an empty list.
**Side effects:** none.
**Implemented in:** `src/Harness/Handlers/StateNpcsHandler.cs`
**Tested in:** `tests/Protocol.Tests/NpcsStateSerializationTests.cs` and `tests/Harness.Tests/StateNpcsHandlerTests.cs`.
````

Add a `player.set_friendship` section near `player.add_mail`:

````markdown
### player.set_friendship

Creates or updates a friendship entry for a vanilla or custom NPC.

Request:
```json
-> { "jsonrpc": "2.0", "id": 15, "method": "player.set_friendship",
     "params": { "npc": "Sophia", "points": 500, "talked_to_today": false, "gifts_today": 0, "gifts_this_week": 0 } }
```

Response:
```json
<- { "jsonrpc": "2.0", "id": 15, "result": { "ok": true, "tick": 84204 } }
```

`points` must be 0-2500. Gift counts must be 0-2. Omitted optional fields preserve
existing friendship values or remain at Stardew defaults for a new entry.

**Preconditions:** world loaded.
**Side effects:** mutates `Game1.MasterPlayer.friendshipData[npc]`.
**Implemented in:** `src/Harness/Handlers/PlayerSetFriendshipHandler.cs`
**Tested in:** `tests/Protocol.Tests/SetFriendshipRequestSerializationTests.cs` and `tests/Harness.Tests/PlayerSetFriendshipHandlerTests.cs`.
````

Update the runner convenience list near `wait.location`:

```markdown
- `{ "action": "wait.npc_location", "args": { "name": "Sophia", "location": "Custom_BlueMoonVineyard", "x": 20, "y": 32 } }` is runner-only. It polls `state.npc` with `params.name` until the NPC reaches the requested location and optional tile. It accepts `timeout_ms` and `poll_ms` and reports the last observed NPC location/tile on timeout.
- `state.assert` accepts optional `args.params` and forwards that object to the state RPC named in the expression. Example: `{ "action": "state.assert", "args": { "expr": "state.npc.hearts == 2", "params": { "name": "Sophia" } } }`.
```

- [ ] **Step 3: Update README and DSL quickstart**

Add to `README.md` near the repo-local testing capability bullets:

```markdown
- Use `state.npcs`, parameterized `state.npc` assertions, `player.set_friendship`,
  and runner-side `wait.npc_location` for custom NPC relationship, schedule, and
  dialogue flows. These helpers are mod-neutral and work for vanilla or Content
  Patcher-added NPCs.
```

Add to `docs/dsl-quickstart.md` near state assertion examples:

````markdown
Parameterized state assertions pass `args.params` to the state RPC:

```json
{
  "action": "state.assert",
  "args": {
    "expr": "state.npc.hearts == 2",
    "params": { "name": "Sophia" },
    "message": "Sophia friendship should be two hearts"
  }
}
```

NPC schedule waits compose the same RPC:

```json
{
  "action": "wait.npc_location",
  "args": {
    "name": "Sophia",
    "location": "Custom_BlueMoonVineyard",
    "timeout_ms": 10000,
    "poll_ms": 100
  }
}
```
````

- [ ] **Step 4: Update SVE capability status**

Modify `SVE_FROBBY_CAPABILITY_TODO.md` Slice 4 entry:

```markdown
  - Implementation plan: `docs/superpowers/plans/2026-05-06-sve-slice-4-npc-schedules-dialogue-relationships.md`.
  - Active target: `state.npcs`, expanded `state.npc`, `player.set_friendship`, parameterized `state.assert`, runner-side `wait.npc_location`, and SVE scenario 05 against Sophia.
```

- [ ] **Step 5: Run doc/schema-adjacent tests**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter ScenarioAssertionParamsSerializationTests
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter StateAssert_ForwardsParamsToStateMethod
```

Expected: selected tests pass.

- [ ] **Step 6: Commit docs and schema**

Run:

```bash
git add schemas/scenario.schema.json docs/rpc-schema.md docs/dsl-quickstart.md README.md SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "docs: document npc relationship testing"
```

Expected: commit succeeds.

## Task 7: SVE Scenario 05

**Files:**
- Add: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/05-sve-npc-schedules-dialogue-relationships.test.json`

- [ ] **Step 1: Add the SVE scenario**

Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/05-sve-npc-schedules-dialogue-relationships.test.json`:

```json
{
  "name": "sve_npc_schedules_dialogue_relationships",
  "fixture": "m0spike_436515781",
  "config": { "seed": 42 },
  "steps": [
    {
      "action": "time.set",
      "args": { "time": 900, "day": 1, "season": "spring", "year": 1 }
    },
    {
      "action": "world.set_weather",
      "args": { "type": "sun" }
    },
    {
      "action": "wait.npc_location",
      "args": {
        "name": "Sophia",
        "location": "Custom_BlueMoonVineyard",
        "timeout_ms": 15000,
        "poll_ms": 100
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.npcs.npcs contains name 'Sophia'",
        "message": "SVE should register Sophia in runtime NPC discovery"
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.npc.location == 'Custom_BlueMoonVineyard'",
        "params": { "name": "Sophia" },
        "message": "Sophia should be observable at Blue Moon Vineyard"
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.npc.portrait == 'Sophia'",
        "params": { "name": "Sophia" },
        "message": "Sophia should expose her custom portrait base name"
      }
    },
    {
      "action": "player.set_friendship",
      "args": {
        "npc": "Sophia",
        "points": 500,
        "talked_to_today": false,
        "gifts_today": 0,
        "gifts_this_week": 0
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.npc.friendship_points == 500",
        "params": { "name": "Sophia" },
        "message": "Sophia friendship points should be set deterministically"
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.npc.hearts == 2",
        "params": { "name": "Sophia" },
        "message": "Sophia friendship should be two hearts"
      }
    },
    {
      "action": "player.warp",
      "args": { "location": "Custom_BlueMoonVineyard", "x": 20, "y": 33 }
    },
    {
      "action": "wait.location",
      "args": {
        "location": "Custom_BlueMoonVineyard",
        "timeout_ms": 10000,
        "poll_ms": 100
      }
    },
    {
      "action": "world.interact_npc",
      "args": { "name": "Sophia" }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.menu.present == true",
        "message": "Talking to Sophia should open a menu"
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.menu.extra.character == 'Sophia'",
        "message": "Talking to Sophia should open Sophia dialogue"
      }
    },
    {
      "action": "freeze.begin",
      "args": {}
    },
    {
      "action": "screenshot.capture",
      "args": { "name": "final" }
    }
  ],
  "assertions": [
    {
      "type": "content.asset",
      "asset": "Data/Characters",
      "asset_type": "data",
      "entry_keys": ["Sophia"],
      "expr": "asset.entries.Sophia.exists == true",
      "message": "SVE runtime Data/Characters should include Sophia"
    },
    {
      "type": "content.asset",
      "asset": "Characters/Dialogue/Sophia",
      "asset_type": "data",
      "include_keys": true,
      "keys_limit": 25,
      "expr": "asset.keys contains 'Introduction'",
      "message": "SVE runtime dialogue asset should include Sophia dialogue"
    }
  ]
}
```

- [ ] **Step 2: Run only the new SVE scenario headless**

Run from `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
./scripts/sdv-test --headless --mod-set core --report-dir /tmp/sve-frobby-results-0.1.0 tests/sdv/05-sve-npc-schedules-dialogue-relationships.test.json
```

Expected: one scenario passes and the report contains a final screenshot with Sophia dialogue open.

- [ ] **Step 3: If the 09:00 schedule is not reached, use the stable home schedule target**

If Step 2 fails only at `wait.npc_location` because the live save-load path does not recompute Sophia's Monday 09:00 route, replace the first wait target and warp target with Sophia's stable day-start home placement from SVE's schedule asset:

```json
{
  "action": "wait.npc_location",
  "args": {
    "name": "Sophia",
    "location": "Custom_SophiaHouse",
    "x": 23,
    "y": 6,
    "timeout_ms": 15000,
    "poll_ms": 100
  }
}
```

and update the location assertions and player warp to:

```json
{ "location": "Custom_SophiaHouse", "x": 23, "y": 7 }
```

Run the Step 2 command again. Expected: one scenario passes. Keep the scenario name unchanged; the test still proves custom NPC discovery, named `state.npc`, relationship setup, and dialogue interaction. The richer 09:00 schedule route remains covered by `wait.npc_location` itself and can be revisited after Frobby gains explicit schedule refresh controls.

- [ ] **Step 4: Commit SVE scenario**

Run from `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
git add tests/sdv/05-sve-npc-schedules-dialogue-relationships.test.json
git commit -m "test: add npc relationship scenario"
```

Expected: SVE repo commit succeeds.

## Task 8: Full Verification And Slice Status

**Files:**
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Run focused Frobby test projects**

Run from `/home/fintan/stardewRepos/frobby/sdv-test-framework`:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj
dotnet test tests/Harness.Tests/Harness.Tests.csproj
dotnet test tests/Runner.Tests/Runner.Tests.csproj
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj
```

Expected: all test projects pass, with existing live-SDV integration tests skipped.

- [ ] **Step 2: Run SVE core suite headless**

Run from `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
./scripts/sdv-test --headless --mod-set core --report-dir /tmp/sve-frobby-results-0.1.0 tests/sdv
```

Expected: SVE scenarios 01-05 pass and `/tmp/sve-frobby-results-0.1.0/index.html` links to each scenario report.

- [ ] **Step 3: Update Slice 4 TODO status**

Modify `SVE_FROBBY_CAPABILITY_TODO.md` Slice 4 entry:

```markdown
- [x] Done: Slice 4, NPC schedules, dialogue, and relationships.
  - SVE pressure: many custom NPCs, custom homes, schedules, movie-theater strings, relationship-gated content, and post-event dialogue patches.
  - Frobby goal: set relationship state, locate NPCs, move time/date, interact with NPCs, and assert speaker/text/location state.
  - Design spec: `docs/superpowers/specs/2026-05-06-sve-slice-4-npc-schedules-dialogue-relationships-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-06-sve-slice-4-npc-schedules-dialogue-relationships.md`.
  - Done: `state.npcs`, expanded `state.npc`, `player.set_friendship`, parameterized `state.assert`, runner-side `wait.npc_location`, and SVE scenario 05 against Sophia.
  - Pending Slice 4 follow-up: dialogue-choice selection, richer schedule-source reporting, and event-seen/mail helpers for relationship-gated cutscenes.
```

- [ ] **Step 4: Commit final Frobby status**

Run from `/home/fintan/stardewRepos/frobby/sdv-test-framework`:

```bash
git add SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "docs: mark sve npc slice complete"
```

Expected: commit succeeds.

- [ ] **Step 5: Confirm both repos are clean**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
```

Expected: both worktrees show only branch headers and no unstaged or staged changes.

## Self-Review Notes

- Spec coverage: Tasks 1-3 cover `state.npcs` and expanded `state.npc`; Task 4 covers `player.set_friendship`; Task 5 covers `wait.npc_location` and the additional parameterized state assertion needed to assert named NPC state; Task 7 proves Sophia runtime NPC, schedule/location, friendship, and dialogue in SVE; Task 6 covers docs; Task 8 covers verification and Slice 4 status.
- Neutrality check: All Frobby code paths are named for Stardew concepts (`NPC`, friendship, state query, runner wait). The only SVE-specific content is the external scenario under `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/`.
- Type consistency: `NpcsState`, `NpcsStateRequest`, `SetFriendshipRequest`, `NpcStateProjector`, `state.npcs`, `player.set_friendship`, `wait.npc_location`, and `ScenarioAssertion.Params` use the same names across tests, implementation snippets, docs, and scenario JSON.
- Placeholder scan: no unresolved implementation placeholders are intentionally left in the plan; conditional schedule fallback in Task 7 is a concrete live-runtime contingency with exact replacement JSON.
