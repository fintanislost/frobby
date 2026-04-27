# M1 Phase 2 — RPC Surface + Runner + Scenario Engine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Finish M1 deliverables D1.2 (rest of the RPC method surface), D1.3 (runner CLI `run`/`doctor`/`list`), and D1.4 (scenario format + parser + executor). After this plan, a `sdv-test run tests/<scenario>.test.json` command will launch SDV, execute a scenario end-to-end over RPC, and report results.

**Out of scope:** D1.5 (texture-path resolution Tier 1) and D1.6 (determinism controller) and D1.7 (sample suite) each deserve their own plans — distinct subsystems with non-trivial design trade-offs. See "Follow-ups" at end.

**Architecture:** Harness-side, each RPC method is a pure function `(JsonElement? params) → JsonElement` registered with `RpcDispatcher`, which routes invocations through `GameThreadDispatch` onto SMAPI's update tick so handlers can safely touch `Game1.*`. Runner-side, each CLI subcommand lives in `src/Runner/Commands/` and uses the existing `UnixSocketRpc.ConnectAsync` + `JsonRpcSession.InvokeAsync` plumbing. Scenarios are JSON Lines per `docs/spec.md §4.6`, parsed + validated + executed by a new `Scenarios/` namespace in the runner.

**Tech Stack:**
- .NET 6 for Harness + Protocol (SMAPI runtime), .NET 10 for Runner
- System.Text.Json (no Newtonsoft)
- xUnit for tests
- JSON Schema Draft 2020-12 via `JsonSchema.Net` (NuGet) for scenario validation
- SMAPI 4.5.2 APIs: `Game1.warpFarmer`, `Game1.addMail`, `Farmer.addItemByMenuIfNecessary`, `Farmer.Money`, `Game1.activeClickableMenu`, `Utility.CollectGarbage` (no), `Game1.NPCGameIds`
- Existing infrastructure: `RpcDispatcher`, `GameThreadDispatch`, `ProtocolJson.ToElement()`, `JsonRpcCodec`

## File Structure

**New files created by this plan (by phase):**

```
src/Protocol/Models/
  LocationState.cs              (Task 1)
  NpcState.cs                   (Task 2)
  MenuState.cs                  (Task 3)
  WarpRequest.cs                (Task 4)
  GiveItemRequest.cs            (Task 5)
  SetMoneyRequest.cs            (Task 6)
  TimeAdvanceRequest.cs         (Task 7)
  WeatherRequest.cs             (Task 8)
  DrawArmRequest.cs             (Task 9)
  DrawEventSnapshot.cs          (Task 10)
  DrawFilter.cs                 (Task 11)
  AssertResult.cs               (Task 11)
  ScenarioBeginRequest.cs       (Task 12)
  ScenarioBeginResult.cs        (Task 12)
  ScenarioEndResult.cs          (Task 12)
  FixtureLoadRequest.cs         (Task 13)
  ScenarioSpec.cs               (Task 14 — covers scenario file model)
  ScenarioStep.cs               (Task 14)
  ScenarioAssertion.cs          (Task 14)

src/Harness/Handlers/
  StateLocationHandler.cs       (Task 1)
  StateNpcHandler.cs            (Task 2)
  StateMenuHandler.cs           (Task 3)
  PlayerWarpHandler.cs          (Task 4)
  PlayerGiveItemHandler.cs      (Task 5)
  PlayerSetMoneyHandler.cs      (Task 6)
  TimeAdvanceHandler.cs         (Task 7)
  WorldSetWeatherHandler.cs     (Task 8)
  DrawArmHandler.cs             (Task 9)
  DrawSnapshotHandler.cs        (Task 10)
  DrawFindHandler.cs            (Task 11)
  DrawAssertContainsHandler.cs  (Task 11)
  ScenarioBeginHandler.cs       (Task 12)
  ScenarioEndHandler.cs         (Task 12)
  FixtureLoadHandler.cs         (Task 13)

src/Harness/Scenarios/
  ScenarioState.cs              (Task 12) — cross-handler state for an active scenario

src/Runner/Scenarios/
  ScenarioLoader.cs             (Task 14)
  ScenarioRunner.cs             (Task 15)
  ScenarioReport.cs             (Task 15)

src/Runner/Commands/
  DoctorCommand.cs              (Task 16)
  ListCommand.cs                (Task 17)
  RunCommand.cs                 (Task 18)

schemas/
  scenario.schema.json          (Task 14)

tests/Protocol.Tests/
  ScenarioSpecTests.cs          (Task 14)

tests/Harness.Tests/
  (one test file per handler where unit-testable without SDV)

tests/Runner.Tests/
  ScenarioLoaderTests.cs        (Task 14)
  ScenarioRunnerTests.cs        (Task 15)
  DoctorCommandTests.cs         (Task 16)
  ListCommandTests.cs           (Task 17)
  RunCommandTests.cs            (Task 18)
```

**Modified files (each task updates these as needed):**
- `src/Harness/ModEntry.cs` — register each new method with `_rpc.Register(...)`. Update the loaded-log banner.
- `docs/rpc-schema.md` — each method gets its entry per the template in that file.

**Verification:** Every task ends with `./scripts/ci.sh` passing. Since this repo is not yet git-initialized, "commit" steps are replaced with "confirm ci.sh green". Once git is initialized (out of scope for this plan), commit messages should follow the Conventional Commits style documented in `.claude/rules/commit-style.md`.

---

## Task 1: state.location handler

**Files:**
- Create: `src/Protocol/Models/LocationState.cs`
- Create: `src/Harness/Handlers/StateLocationHandler.cs`
- Modify: `src/Harness/ModEntry.cs` — add `_rpc.Register(StateLocationHandler.Method, ...)`
- Modify: `docs/rpc-schema.md` — add `state.location` entry
- Test: `tests/Protocol.Tests/LocationStateSerializationTests.cs`

**Depends on:** D1.2 walking skeleton (already complete — `StatePlayerHandler` / `StateTimeHandler` / `ProtocolJson`).

**RPC shape:**
```
→ { "jsonrpc":"2.0","id":1,"method":"state.location","params":{"name":"Farm"} }      ← params optional; omit for current location
← { "jsonrpc":"2.0","id":1,"result":{
      "name":"Farm",
      "is_outdoors":true,
      "npcs":[{"name":"Pierre","tile":{"x":4,"y":17}}],
      "objects":[{"tile":{"x":10,"y":10},"name":"Weeds"}],
      "terrain":[{"tile":{"x":12,"y":12},"kind":"HoeDirt"}]
   } }
```

- [ ] **Step 1: Write failing serialization test**

Create `tests/Protocol.Tests/LocationStateSerializationTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class LocationStateSerializationTests
{
    [Fact]
    public void Serialize_SnakeCaseFields()
    {
        var loc = new LocationState
        {
            Name = "Farm",
            IsOutdoors = true,
            Npcs = new() { new NpcSummary { Name = "Pierre", Tile = new TilePoint { X = 4, Y = 17 } } },
            Objects = new() { new ObjectSummary { Tile = new TilePoint { X = 10, Y = 10 }, Name = "Weeds" } },
            Terrain = new() { new TerrainSummary { Tile = new TilePoint { X = 12, Y = 12 }, Kind = "HoeDirt" } },
        };

        var json = JsonSerializer.Serialize(loc, ProtocolJson.Options);
        Assert.Contains("\"name\":\"Farm\"", json);
        Assert.Contains("\"is_outdoors\":true", json);
        Assert.Contains("\"npcs\":[{\"name\":\"Pierre\"", json);
        Assert.Contains("\"terrain\":[{\"tile\":{\"x\":12,\"y\":12},\"kind\":\"HoeDirt\"}]", json);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Protocol.Tests/ --filter LocationState`
Expected: FAIL with `CS0246 ... LocationState could not be found`.

- [ ] **Step 3: Create LocationState DTO**

Create `src/Protocol/Models/LocationState.cs`:

```csharp
using System.Collections.Generic;

namespace SdvTestFramework.Protocol.Models;

public sealed class LocationState
{
    public string Name { get; set; } = string.Empty;
    public bool IsOutdoors { get; set; }
    public List<NpcSummary> Npcs { get; set; } = new();
    public List<ObjectSummary> Objects { get; set; } = new();
    public List<TerrainSummary> Terrain { get; set; } = new();
}

public sealed class NpcSummary
{
    public string Name { get; set; } = string.Empty;
    public TilePoint Tile { get; set; } = new();
}

public sealed class ObjectSummary
{
    public TilePoint Tile { get; set; } = new();
    public string Name { get; set; } = string.Empty;
}

public sealed class TerrainSummary
{
    public TilePoint Tile { get; set; } = new();
    public string Kind { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Run test to verify pass**

Run: `dotnet test tests/Protocol.Tests/ --filter LocationState`
Expected: PASS.

- [ ] **Step 5: Create StateLocationHandler**

Create `src/Harness/Handlers/StateLocationHandler.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

public static class StateLocationHandler
{
    public const string Method = "state.location";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        // Optional `name` param — defaults to current location.
        GameLocation? loc = Game1.currentLocation;
        if (paramsElement is { } p && p.TryGetProperty("name", out var nameEl))
        {
            var name = nameEl.GetString();
            if (!string.IsNullOrEmpty(name))
                loc = Game1.getLocationFromName(name);
        }

        if (loc is null)
            return ProtocolJson.ToElement(new LocationState { Name = string.Empty });

        var state = new LocationState
        {
            Name = loc.Name ?? string.Empty,
            IsOutdoors = loc.IsOutdoors,
        };

        foreach (var npc in loc.characters)
        {
            state.Npcs.Add(new NpcSummary
            {
                Name = npc.Name ?? string.Empty,
                Tile = new TilePoint { X = npc.TilePoint.X, Y = npc.TilePoint.Y },
            });
        }

        foreach (var kv in loc.Objects.Pairs)
        {
            state.Objects.Add(new ObjectSummary
            {
                Tile = new TilePoint { X = (int)kv.Key.X, Y = (int)kv.Key.Y },
                Name = kv.Value.Name ?? kv.Value.GetType().Name,
            });
        }

        foreach (var kv in loc.terrainFeatures.Pairs)
        {
            state.Terrain.Add(new TerrainSummary
            {
                Tile = new TilePoint { X = (int)kv.Key.X, Y = (int)kv.Key.Y },
                Kind = kv.Value.GetType().Name,
            });
        }

        return ProtocolJson.ToElement(state);
    }
}
```

- [ ] **Step 6: Register the handler in ModEntry**

In `src/Harness/ModEntry.cs`, find the existing `_rpc.Register(StateTimeHandler.Method, ...)` line and add one below:

```csharp
_rpc.Register(StateLocationHandler.Method, p => StateLocationHandler.Handle(p));
```

Update the `"Harness loaded. …"` log banner to list `state.location`.

- [ ] **Step 7: Update docs/rpc-schema.md**

Add an entry for `state.location` under the existing `### state.time` block, following the template:

```markdown
### state.location

Returns a snapshot of the current location (or a named location via `params.name`).

Request (current location):
...request/response JSON blocks...

**Preconditions:** world loaded.
**Side effects:** none.
**Implemented in:** `src/Harness/Handlers/StateLocationHandler.cs`
**Tested in:** `tests/Protocol.Tests/LocationStateSerializationTests.cs` (DTO shape).
```

- [ ] **Step 8: Run full CI**

Run: `./scripts/ci.sh`
Expected: PASS. Test count increased by 1.

---

## Task 2: state.npc handler

**Files:**
- Create: `src/Protocol/Models/NpcState.cs`
- Create: `src/Harness/Handlers/StateNpcHandler.cs`
- Modify: `src/Harness/ModEntry.cs`
- Modify: `docs/rpc-schema.md`
- Test: `tests/Protocol.Tests/NpcStateSerializationTests.cs`

**Depends on:** Task 1 (follows the established DTO + handler + registration pattern).

**RPC shape:**
```
→ { "id":1,"method":"state.npc","params":{"name":"Abigail"} }    ← name is REQUIRED
← { "id":1,"result":{
      "name":"Abigail",
      "location":"Town",
      "tile":{"x":4,"y":23},
      "friendship_points":500,
      "hearts":2,
      "gift_given_today":false,
      "portrait":"Abigail"
   } }
```

**Error conditions:**
- Missing `params.name` → `InvalidParams` (-32602)
- Unknown NPC name → custom `GameStateInvalid` (-32003) with message `"no NPC named: <name>"`

- [ ] **Step 1: Write DTO + serialization test**

Create `tests/Protocol.Tests/NpcStateSerializationTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class NpcStateSerializationTests
{
    [Fact]
    public void Serialize_SnakeCase()
    {
        var npc = new NpcState
        {
            Name = "Abigail",
            Location = "Town",
            Tile = new TilePoint { X = 4, Y = 23 },
            FriendshipPoints = 500,
            Hearts = 2,
            GiftGivenToday = false,
            Portrait = "Abigail",
        };
        var json = JsonSerializer.Serialize(npc, ProtocolJson.Options);
        Assert.Contains("\"friendship_points\":500", json);
        Assert.Contains("\"gift_given_today\":false", json);
    }
}
```

Run: `dotnet test tests/Protocol.Tests/ --filter NpcState` — expect FAIL (no type yet).

- [ ] **Step 2: Create NpcState DTO**

Create `src/Protocol/Models/NpcState.cs`:

```csharp
namespace SdvTestFramework.Protocol.Models;

public sealed class NpcState
{
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public TilePoint Tile { get; set; } = new();
    public int FriendshipPoints { get; set; }
    public int Hearts { get; set; }
    public bool GiftGivenToday { get; set; }
    public string Portrait { get; set; } = string.Empty;
}
```

Run the test — expect PASS.

- [ ] **Step 3: Create StateNpcHandler**

Create `src/Harness/Handlers/StateNpcHandler.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

public static class StateNpcHandler
{
    public const string Method = "state.npc";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        if (paramsElement is not { } p || !p.TryGetProperty("name", out var nameEl)
            || nameEl.ValueKind != JsonValueKind.String)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.name (string) is required");

        var name = nameEl.GetString()!;
        var npc = Game1.getCharacterFromName(name);
        if (npc is null)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"no NPC named: {name}");

        int friendshipPoints = 0;
        bool giftGivenToday = false;
        if (Game1.player?.friendshipData is { } data && data.TryGetValue(name, out var friendship))
        {
            friendshipPoints = friendship.Points;
            giftGivenToday = friendship.GiftsToday > 0;
        }

        var state = new NpcState
        {
            Name = npc.Name ?? string.Empty,
            Location = npc.currentLocation?.Name ?? string.Empty,
            Tile = new TilePoint { X = npc.TilePoint.X, Y = npc.TilePoint.Y },
            FriendshipPoints = friendshipPoints,
            Hearts = friendshipPoints / 250,
            GiftGivenToday = giftGivenToday,
            Portrait = npc.Portrait?.Name?.BaseName ?? npc.Name ?? string.Empty,
        };
        return ProtocolJson.ToElement(state);
    }
}
```

- [ ] **Step 4: Register in ModEntry + update docs**

Add to `src/Harness/ModEntry.cs` alongside the other registrations:
```csharp
_rpc.Register(StateNpcHandler.Method, p => StateNpcHandler.Handle(p));
```

Update the log banner and add `state.npc` to `docs/rpc-schema.md` with request/response example + preconditions (world loaded; NPC must exist).

- [ ] **Step 5: Run CI**

Run: `./scripts/ci.sh` — PASS.

---

## Task 3: state.menu handler

**Files:**
- Create: `src/Protocol/Models/MenuState.cs`
- Create: `src/Harness/Handlers/StateMenuHandler.cs`
- Modify: `src/Harness/ModEntry.cs`, `docs/rpc-schema.md`
- Test: `tests/Protocol.Tests/MenuStateSerializationTests.cs`

**Depends on:** Task 1 pattern.

**RPC shape:**
```
→ { "id":1,"method":"state.menu" }
← { "id":1,"result":{
      "type":"ShopMenu",
      "present":true,
      "extra":{"currency":"g"}
   } }
```

When no menu is active:
```
← { "id":1,"result":{"type":"","present":false,"extra":{}} }
```

- [ ] **Step 1: DTO + test**

Create `src/Protocol/Models/MenuState.cs`:

```csharp
using System.Collections.Generic;

namespace SdvTestFramework.Protocol.Models;

public sealed class MenuState
{
    public string Type { get; set; } = string.Empty;
    public bool Present { get; set; }
    public Dictionary<string, string> Extra { get; set; } = new();
}
```

Create `tests/Protocol.Tests/MenuStateSerializationTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class MenuStateSerializationTests
{
    [Fact]
    public void Absent_SerializesPresentFalse()
    {
        var json = JsonSerializer.Serialize(new MenuState(), ProtocolJson.Options);
        Assert.Contains("\"present\":false", json);
        Assert.Contains("\"type\":\"\"", json);
    }

    [Fact]
    public void Present_SerializesWithExtra()
    {
        var m = new MenuState
        {
            Type = "ShopMenu",
            Present = true,
            Extra = new() { ["currency"] = "g" },
        };
        var json = JsonSerializer.Serialize(m, ProtocolJson.Options);
        Assert.Contains("\"present\":true", json);
        Assert.Contains("\"currency\":\"g\"", json);
    }
}
```

Run — expect FAIL (type missing), implement, PASS.

- [ ] **Step 2: Handler**

Create `src/Harness/Handlers/StateMenuHandler.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using StardewValley.Menus;

namespace SdvTestFramework.Harness.Handlers;

public static class StateMenuHandler
{
    public const string Method = "state.menu";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var menu = Game1.activeClickableMenu;
        if (menu is null)
            return ProtocolJson.ToElement(new MenuState { Present = false });

        var state = new MenuState
        {
            Type = menu.GetType().Name,
            Present = true,
        };

        // Menu-type-specific extras. Kept small for M1; extend per need.
        if (menu is ShopMenu shop)
        {
            state.Extra["currency"] = shop.currency.ToString();
            state.Extra["item_count"] = shop.forSale.Count.ToString();
        }
        else if (menu is DialogueBox dialog)
        {
            state.Extra["character"] = dialog.characterDialogue?.speaker?.Name ?? string.Empty;
        }

        return ProtocolJson.ToElement(state);
    }
}
```

- [ ] **Step 3: Register + doc + CI**

Add the registration line, update the banner, add `state.menu` entry to `docs/rpc-schema.md`.

Run: `./scripts/ci.sh` — PASS.

---

## Task 4: player.warp manipulator

**Files:**
- Create: `src/Protocol/Models/WarpRequest.cs`
- Create: `src/Harness/Handlers/PlayerWarpHandler.cs`
- Modify: `src/Harness/ModEntry.cs`, `docs/rpc-schema.md`
- Test: `tests/Protocol.Tests/WarpRequestSerializationTests.cs`

**Depends on:** Task 1 pattern. First task that mutates state — handler must validate params strictly.

**RPC shape:**
```
→ { "id":1,"method":"player.warp","params":{"location":"SeedShop","x":4,"y":19} }
← { "id":1,"result":{"ok":true,"tick":84200} }
```

**Errors:**
- Missing or non-string `location` → InvalidParams
- Non-int `x`/`y` → InvalidParams
- Unknown location name → GameStateInvalid (`"no location named: …"`)

- [ ] **Step 1: Create request DTO + test**

Create `src/Protocol/Models/WarpRequest.cs`:

```csharp
namespace SdvTestFramework.Protocol.Models;

public sealed class WarpRequest
{
    public string Location { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
}
```

Create `tests/Protocol.Tests/WarpRequestSerializationTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class WarpRequestSerializationTests
{
    [Fact]
    public void DeserializesFromSnakeCase()
    {
        var json = "{\"location\":\"SeedShop\",\"x\":4,\"y\":19}";
        var req = JsonSerializer.Deserialize<WarpRequest>(json, ProtocolJson.Options)!;
        Assert.Equal("SeedShop", req.Location);
        Assert.Equal(4, req.X);
        Assert.Equal(19, req.Y);
    }
}
```

- [ ] **Step 2: Handler**

Create `src/Harness/Handlers/PlayerWarpHandler.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

public static class PlayerWarpHandler
{
    public const string Method = "player.warp";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var req = Deserialize(paramsElement);

        if (Game1.getLocationFromName(req.Location) is null)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"no location named: {req.Location}");

        Game1.warpFarmer(req.Location, req.X, req.Y, flip: false);

        var json = "{\"ok\":true,\"tick\":" + Game1.ticks + "}";
        return JsonDocument.Parse(json).RootElement;
    }

    private static WarpRequest Deserialize(JsonElement? paramsElement)
    {
        if (paramsElement is not { } p)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params required");
        try
        {
            var req = JsonSerializer.Deserialize<WarpRequest>(p.GetRawText(), ProtocolJson.Options);
            if (req is null || string.IsNullOrEmpty(req.Location))
                throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.location required");
            return req;
        }
        catch (JsonException ex)
        {
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, ex.Message);
        }
    }
}
```

- [ ] **Step 3: Register + doc + CI**

Add `_rpc.Register(PlayerWarpHandler.Method, p => PlayerWarpHandler.Handle(p));`

Update `docs/rpc-schema.md` with the method. Note side effect: `Game1.warpFarmer` is queued; the warp actually happens on the next tick.

Run `./scripts/ci.sh` — PASS.

---

## Task 5: player.give_item

**Files:**
- Create: `src/Protocol/Models/GiveItemRequest.cs`
- Create: `src/Harness/Handlers/PlayerGiveItemHandler.cs`
- Modify: `src/Harness/ModEntry.cs`, `docs/rpc-schema.md`
- Test: `tests/Protocol.Tests/GiveItemRequestSerializationTests.cs`

**RPC shape:**
```
→ { "id":1,"method":"player.give_item","params":{"id":"(O)388","count":50} }
← { "id":1,"result":{"ok":true}}
```

`id` is SDV's qualified item ID (e.g. `"(O)388"` for wood). Count defaults to 1.

- [ ] **Step 1: DTO**

`src/Protocol/Models/GiveItemRequest.cs`:

```csharp
namespace SdvTestFramework.Protocol.Models;

public sealed class GiveItemRequest
{
    public string Id { get; set; } = string.Empty;
    public int Count { get; set; } = 1;
}
```

Test the DTO deserialization in `tests/Protocol.Tests/GiveItemRequestSerializationTests.cs` — same pattern as WarpRequest test.

- [ ] **Step 2: Handler**

```csharp
using System.Text.Json;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

public static class PlayerGiveItemHandler
{
    public const string Method = "player.give_item";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        if (paramsElement is not { } p)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params required");

        var req = JsonSerializer.Deserialize<GiveItemRequest>(p.GetRawText(), ProtocolJson.Options);
        if (req is null || string.IsNullOrEmpty(req.Id))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.id required");
        if (req.Count < 1)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.count must be >= 1");

        var item = ItemRegistry.Create(req.Id, req.Count);
        if (item is null)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"unknown item id: {req.Id}");

        Game1.player.addItemByMenuIfNecessary(item);
        return JsonDocument.Parse("{\"ok\":true}").RootElement;
    }
}
```

- [ ] **Step 3: Register + doc + CI**

Same pattern. Run `./scripts/ci.sh` — PASS.

---

## Task 6: player.set_money

**Files:**
- Create: `src/Protocol/Models/SetMoneyRequest.cs`
- Create: `src/Harness/Handlers/PlayerSetMoneyHandler.cs`
- Modify: `src/Harness/ModEntry.cs`, `docs/rpc-schema.md`
- Test: `tests/Protocol.Tests/SetMoneyRequestSerializationTests.cs`

**RPC shape:**
```
→ { "id":1,"method":"player.set_money","params":{"amount":5000} }
← { "id":1,"result":{"ok":true,"previous":1000} }
```

- [ ] **Step 1: DTO**

`src/Protocol/Models/SetMoneyRequest.cs`:

```csharp
namespace SdvTestFramework.Protocol.Models;

public sealed class SetMoneyRequest
{
    public int Amount { get; set; }
}
```

- [ ] **Step 2: Handler**

```csharp
using System.Text.Json;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

public static class PlayerSetMoneyHandler
{
    public const string Method = "player.set_money";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        if (paramsElement is not { } p)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params required");
        var req = JsonSerializer.Deserialize<SetMoneyRequest>(p.GetRawText(), ProtocolJson.Options)
            ?? throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "empty params");
        if (req.Amount < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "amount must be >= 0");

        int previous = Game1.player.Money;
        Game1.player.Money = req.Amount;

        var json = "{\"ok\":true,\"previous\":" + previous + "}";
        return JsonDocument.Parse(json).RootElement;
    }
}
```

- [ ] **Step 3: Register + doc + CI**

Same pattern. `./scripts/ci.sh` — PASS.

---

## Task 7: time.advance

**Files:**
- Create: `src/Protocol/Models/TimeAdvanceRequest.cs`
- Create: `src/Harness/Handlers/TimeAdvanceHandler.cs`
- Modify: `src/Harness/ModEntry.cs`, `docs/rpc-schema.md`
- Test: `tests/Protocol.Tests/TimeAdvanceRequestSerializationTests.cs`

**RPC shape:**
```
→ { "id":1,"method":"time.advance","params":{"minutes":30} }
← { "id":1,"result":{"ok":true,"new_time_of_day":630} }
```

SDV time advances in 10-minute chunks; `minutes` is rounded down to nearest 10. Max 120 per call (scenarios should chain).

- [ ] **Step 1: DTO + handler**

`src/Protocol/Models/TimeAdvanceRequest.cs`:

```csharp
namespace SdvTestFramework.Protocol.Models;

public sealed class TimeAdvanceRequest
{
    public int Minutes { get; set; }
}
```

`src/Harness/Handlers/TimeAdvanceHandler.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

public static class TimeAdvanceHandler
{
    public const string Method = "time.advance";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        if (paramsElement is not { } p)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params required");
        var req = JsonSerializer.Deserialize<TimeAdvanceRequest>(p.GetRawText(), ProtocolJson.Options)
            ?? throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "empty params");
        if (req.Minutes < 10 || req.Minutes > 120 || req.Minutes % 10 != 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "minutes must be multiple of 10, between 10 and 120");

        // SDV represents timeOfDay as HHMM (600 = 06:00). Adding 10min = +10 OR +50 at hour boundary.
        int steps = req.Minutes / 10;
        for (int i = 0; i < steps; i++)
            Game1.performTenMinuteClockUpdate();

        var json = "{\"ok\":true,\"new_time_of_day\":" + Game1.timeOfDay + "}";
        return JsonDocument.Parse(json).RootElement;
    }
}
```

- [ ] **Step 2: Register + doc + CI**

`./scripts/ci.sh` — PASS.

---

## Task 8: world.set_weather

**Files:**
- Create: `src/Protocol/Models/WeatherRequest.cs`
- Create: `src/Harness/Handlers/WorldSetWeatherHandler.cs`
- Modify: `src/Harness/ModEntry.cs`, `docs/rpc-schema.md`
- Test: `tests/Protocol.Tests/WeatherRequestSerializationTests.cs`

**RPC shape:**
```
→ { "id":1,"method":"world.set_weather","params":{"type":"rain"} }
← { "id":1,"result":{"ok":true}}
```

`type`: one of `sun`, `rain`, `storm`, `snow`, `wind`, `festival`.

- [ ] **Step 1: DTO**

```csharp
namespace SdvTestFramework.Protocol.Models;

public sealed class WeatherRequest
{
    public string Type { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Handler**

```csharp
using System;
using System.Text.Json;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

public static class WorldSetWeatherHandler
{
    public const string Method = "world.set_weather";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        if (paramsElement is not { } p)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params required");
        var req = JsonSerializer.Deserialize<WeatherRequest>(p.GetRawText(), ProtocolJson.Options)
            ?? throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "empty params");
        string weatherId = req.Type.ToLowerInvariant() switch
        {
            "sun" => "Sun",
            "rain" => "Rain",
            "storm" => "Storm",
            "snow" => "Snow",
            "wind" => "Wind",
            "festival" => "Festival",
            _ => throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, $"unknown weather type: {req.Type}"),
        };

        var state = Game1.netWorldState.Value;
        state.GetWeatherForLocation(Game1.currentLocation?.GetLocationContextId() ?? "Default").Weather = weatherId;
        Game1.updateWeather(Game1.currentGameTime);

        return JsonDocument.Parse("{\"ok\":true}").RootElement;
    }
}
```

- [ ] **Step 3: Register + doc + CI**

`./scripts/ci.sh` — PASS.

---

## Task 9: draw.arm + draw.disarm

**Files:**
- Create: `src/Protocol/Models/DrawArmRequest.cs`
- Create: `src/Harness/Handlers/DrawArmHandler.cs`
- Create: `src/Harness/Handlers/DrawDisarmHandler.cs`
- Modify: `src/Harness/ModEntry.cs`, `docs/rpc-schema.md`
- Test: `tests/Protocol.Tests/DrawArmRequestSerializationTests.cs`

**Depends on:** Existing `Recorder.Arm()` / `Recorder.Disarm()`. This task wires them to the RPC surface.

**RPC shape:**
```
→ { "id":1,"method":"draw.arm","params":{"ticks":30,"output_path":"/tmp/draws.jsonl"} }   ← output_path optional (defaults to in-memory only)
← { "id":1,"result":{"ok":true,"armed":true} }

→ { "id":2,"method":"draw.disarm" }
← { "id":2,"result":{"ok":true,"flushed":true} }
```

- [ ] **Step 1: DTO**

`src/Protocol/Models/DrawArmRequest.cs`:

```csharp
namespace SdvTestFramework.Protocol.Models;

public sealed class DrawArmRequest
{
    public int Ticks { get; set; } = 30;
    public string? OutputPath { get; set; }
}
```

- [ ] **Step 2: Modify `Recorder` to support in-memory arm**

The current `Recorder.Arm` requires an `outputPath`. For RPC use we want in-memory capture so `draw.snapshot` can return events without file I/O. Change `Recorder.Arm` to accept an optional path; null means "capture in memory, flush only on explicit call". Update the `Flush` method to no-op when no path was given.

Modify `src/Harness/Recording/Recorder.cs`:
- Change `Arm(int ticks, string outputPath)` → `Arm(int ticks, string? outputPath = null)`
- In `Flush`, if `_pendingOutputPath is null`, skip the file write but still log the flush.
- Add a public `TrySnapshotEvents(out DrawEvent[] events)` method that returns the captured buffer contents (read-only copy) — used by `draw.snapshot`.

Update `scripts/ci.sh` expectation: existing tests continue to pass since the signature change is additive.

- [ ] **Step 3: DrawArmHandler**

```csharp
using System.Text.Json;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Handlers;

public static class DrawArmHandler
{
    public const string Method = "draw.arm";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        DrawArmRequest req = paramsElement is { } p
            ? JsonSerializer.Deserialize<DrawArmRequest>(p.GetRawText(), ProtocolJson.Options) ?? new()
            : new();
        if (req.Ticks < 1)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "ticks must be >= 1");

        Recorder.Arm(req.Ticks, req.OutputPath);
        return JsonDocument.Parse("{\"ok\":true,\"armed\":true}").RootElement;
    }
}
```

- [ ] **Step 4: DrawDisarmHandler**

```csharp
using System.Text.Json;
using SdvTestFramework.Harness.Recording;

namespace SdvTestFramework.Harness.Handlers;

public static class DrawDisarmHandler
{
    public const string Method = "draw.disarm";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        Recorder.Disarm();
        return JsonDocument.Parse("{\"ok\":true,\"flushed\":true}").RootElement;
    }
}
```

- [ ] **Step 5: Register both + doc + CI**

Register `DrawArmHandler` and `DrawDisarmHandler` in `ModEntry`. Run `./scripts/ci.sh` — PASS.

---

## Task 10: draw.snapshot

**Files:**
- Create: `src/Protocol/Models/DrawEventSnapshot.cs`
- Create: `src/Harness/Handlers/DrawSnapshotHandler.cs`
- Modify: `src/Harness/ModEntry.cs`, `docs/rpc-schema.md`
- Test: `tests/Protocol.Tests/DrawEventSnapshotSerializationTests.cs`
- Test: `tests/Harness.Tests/DrawSnapshotHandlerTests.cs`

**Depends on:** Task 9 (in-memory capture + `Recorder.TrySnapshotEvents`).

**RPC shape:**
```
→ { "id":1,"method":"draw.snapshot" }
← { "id":1,"result":{
      "events":[
        {"tick":5,"call":1,"tex_ref":42,"tex_w":16,"tex_h":16,"src":[0,0,16,16],"dst":[0,0,64,64],"col":[255,255,255,255],"rot":0,"orig":[0,0],"fx":0,"z":0.5}
      ],
      "meta":{"ticks":10,"events":1,"dropped":0}
   } }
```

- [ ] **Step 1: DTO**

```csharp
using System.Collections.Generic;

namespace SdvTestFramework.Protocol.Models;

public sealed class DrawEventSnapshot
{
    public List<DrawEventDto> Events { get; set; } = new();
    public SnapshotMeta Meta { get; set; } = new();
}

public sealed class DrawEventDto
{
    public int Tick { get; set; }
    public int Call { get; set; }
    public int TexRef { get; set; }
    public int TexW { get; set; }
    public int TexH { get; set; }
    public int[]? Src { get; set; }   // null when source rect was null
    public int[] Dst { get; set; } = System.Array.Empty<int>();
    public int[] Col { get; set; } = System.Array.Empty<int>();
    public float Rot { get; set; }
    public float[] Orig { get; set; } = System.Array.Empty<float>();
    public int Fx { get; set; }
    public float Z { get; set; }
}

public sealed class SnapshotMeta
{
    public int Ticks { get; set; }
    public int Events { get; set; }
    public int Dropped { get; set; }
}
```

Property naming note: DTO uses short names (`TexW`, `Src`, `Dst`) to match the JSONL format the M0 spike established in `DrawEventWriter.cs`. The snake-case naming policy converts them to `tex_w`, `src`, `dst`.

- [ ] **Step 2: Handler**

```csharp
using System.Text.Json;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Handlers;

public static class DrawSnapshotHandler
{
    public const string Method = "draw.snapshot";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        Recorder.TrySnapshotEvents(out var events, out var meta);

        var snap = new DrawEventSnapshot
        {
            Meta = new SnapshotMeta
            {
                Ticks = meta.Ticks,
                Events = events.Length,
                Dropped = meta.Dropped,
            },
        };

        foreach (var e in events)
        {
            snap.Events.Add(new DrawEventDto
            {
                Tick = e.Tick,
                Call = e.CallIndex,
                TexRef = e.TextureRefId,
                TexW = e.TextureWidth,
                TexH = e.TextureHeight,
                Src = e.SourceRect is { } sr ? new[] { sr.X, sr.Y, sr.Width, sr.Height } : null,
                Dst = new[] { e.DestRect.X, e.DestRect.Y, e.DestRect.Width, e.DestRect.Height },
                Col = new[] { e.Color.R, e.Color.G, e.Color.B, e.Color.A },
                Rot = e.Rotation,
                Orig = new[] { e.Origin.X, e.Origin.Y },
                Fx = (int)e.Effects,
                Z = e.LayerDepth,
            });
        }

        return ProtocolJson.ToElement(snap);
    }
}
```

- [ ] **Step 3: Modify `Recorder` to expose `TrySnapshotEvents`**

Modify `src/Harness/Recording/Recorder.cs`:

```csharp
public readonly struct SnapshotMetadata
{
    public SnapshotMetadata(int ticks, int dropped) { Ticks = ticks; Dropped = dropped; }
    public int Ticks { get; }
    public int Dropped { get; }
}

public static void TrySnapshotEvents(out DrawEvent[] events, out SnapshotMetadata meta)
{
    var copy = new DrawEvent[_bufferHead];
    System.Array.Copy(_buffer, copy, _bufferHead);
    events = copy;
    meta = new SnapshotMetadata(_capturedTicks, _dropped);
}
```

- [ ] **Step 4: Unit test for the handler**

Create `tests/Harness.Tests/DrawSnapshotHandlerTests.cs`:

```csharp
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Harness.Recording;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class DrawSnapshotHandlerTests
{
    [Fact]
    public void Snapshot_EmptyBuffer_ReturnsEmptyEvents()
    {
        // No IMonitor needed — Recorder.TrySnapshotEvents doesn't log.
        Recorder.Initialize(monitor: null!, capacity: 16);
        var result = DrawSnapshotHandler.Handle(null);
        Assert.Contains("\"events\":[]", result.GetRawText());
    }
}
```

Note: the existing `Recorder.Initialize` requires a non-null `IMonitor`. Decide whether to relax that (accept null and no-op logs) to make `Recorder` unit-testable without SDV, or skip this test and verify via integration in Task 19. Recommendation: relax the requirement in `Recorder.Initialize` — it's a small, safe change.

- [ ] **Step 5: Register + doc + CI**

`./scripts/ci.sh` — PASS.

---

## Task 11: draw.find + draw.assert_contains

**Files:**
- Create: `src/Protocol/Models/DrawFilter.cs`
- Create: `src/Protocol/Models/AssertResult.cs`
- Create: `src/Harness/Handlers/DrawFindHandler.cs`
- Create: `src/Harness/Handlers/DrawAssertContainsHandler.cs`
- Modify: `src/Harness/ModEntry.cs`, `docs/rpc-schema.md`
- Test: `tests/Harness.Tests/DrawFilterTests.cs`

**Depends on:** Task 10 (`Recorder.TrySnapshotEvents`).

**RPC shapes:**
```
draw.find:
→ { "id":1,"method":"draw.find","params":{"in_rect":{"x":0,"y":0,"w":1280,"h":720},"color":[255,255,255,255]} }
← { "id":1,"result":{"events":[...matching DrawEventDto objects...],"count":3} }

draw.assert_contains:
→ { "id":2,"method":"draw.assert_contains","params":{"filter":{"layer_depth_range":[0.0,1.0]},"min_count":1,"message":"expected some draw"} }
← { "id":2,"result":{"passed":true,"matched_count":42,"min_count":1} }
```

**Filter DSL (all fields AND together):**
- `texture_asset` (string) — match by resolved asset path (M1 reserves the key; Tier 1 resolution lands in D1.5, so for now we match on `TextureRefId` stringified)
- `in_rect` ({x,y,w,h}) — dest rect must be contained
- `layer_depth_range` ([min, max])
- `color` ([r,g,b,a]) — exact match
- `source_rect` ({x,y,w,h}) — exact match

- [ ] **Step 1: DTOs**

```csharp
// src/Protocol/Models/DrawFilter.cs
namespace SdvTestFramework.Protocol.Models;

public sealed class DrawFilter
{
    public string? TextureAsset { get; set; }
    public int[]? InRect { get; set; }
    public float[]? LayerDepthRange { get; set; }
    public int[]? Color { get; set; }
    public int[]? SourceRect { get; set; }
}
```

```csharp
// src/Protocol/Models/AssertResult.cs
namespace SdvTestFramework.Protocol.Models;

public sealed class AssertResult
{
    public bool Passed { get; set; }
    public int MatchedCount { get; set; }
    public int MinCount { get; set; }
    public string? Message { get; set; }
}
```

- [ ] **Step 2: Filter evaluator**

Create `src/Harness/Handlers/DrawFilterMatcher.cs`:

```csharp
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Handlers;

internal static class DrawFilterMatcher
{
    public static bool Matches(in DrawEvent e, DrawFilter f)
    {
        if (f.Color is { Length: 4 } c &&
            (e.Color.R != c[0] || e.Color.G != c[1] || e.Color.B != c[2] || e.Color.A != c[3]))
            return false;

        if (f.InRect is { Length: 4 } r)
        {
            var rect = new Rectangle(r[0], r[1], r[2], r[3]);
            if (!rect.Contains(e.DestRect)) return false;
        }

        if (f.LayerDepthRange is { Length: 2 } ldr)
        {
            if (e.LayerDepth < ldr[0] || e.LayerDepth > ldr[1]) return false;
        }

        if (f.SourceRect is { Length: 4 } sr)
        {
            if (e.SourceRect is not { } actual ||
                actual.X != sr[0] || actual.Y != sr[1] || actual.Width != sr[2] || actual.Height != sr[3])
                return false;
        }

        // texture_asset filter: pre-D1.5 placeholder. Match by stringified TextureRefId.
        if (!string.IsNullOrEmpty(f.TextureAsset) &&
            e.TextureRefId.ToString() != f.TextureAsset)
            return false;

        return true;
    }
}
```

- [ ] **Step 3: Unit tests for matcher**

`tests/Harness.Tests/DrawFilterTests.cs`:

```csharp
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class DrawFilterTests
{
    private DrawEvent Event(Rectangle dest, Color col = default, float z = 0f) => new()
    {
        DestRect = dest,
        Color = col == default ? Color.White : col,
        LayerDepth = z,
    };

    [Fact]
    public void EmptyFilter_MatchesEverything()
    {
        Assert.True(DrawFilterMatcher.Matches(Event(new Rectangle(0, 0, 10, 10)), new DrawFilter()));
    }

    [Fact]
    public void ColorMismatch_Rejects()
    {
        var f = new DrawFilter { Color = new[] { 255, 0, 0, 255 } };
        Assert.False(DrawFilterMatcher.Matches(Event(default, Color.White), f));
    }

    [Fact]
    public void InRect_ContainmentChecked()
    {
        var f = new DrawFilter { InRect = new[] { 0, 0, 100, 100 } };
        Assert.True(DrawFilterMatcher.Matches(Event(new Rectangle(10, 10, 50, 50)), f));
        Assert.False(DrawFilterMatcher.Matches(Event(new Rectangle(90, 90, 50, 50)), f));
    }

    [Fact]
    public void LayerDepthRange_Inclusive()
    {
        var f = new DrawFilter { LayerDepthRange = new[] { 0.5f, 1.0f } };
        Assert.False(DrawFilterMatcher.Matches(Event(default, z: 0.4f), f));
        Assert.True(DrawFilterMatcher.Matches(Event(default, z: 0.5f), f));
        Assert.True(DrawFilterMatcher.Matches(Event(default, z: 1.0f), f));
    }
}
```

- [ ] **Step 4: DrawFindHandler**

```csharp
using System.Linq;
using System.Text.Json;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Handlers;

public static class DrawFindHandler
{
    public const string Method = "draw.find";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var filter = paramsElement is { } p
            ? JsonSerializer.Deserialize<DrawFilter>(p.GetRawText(), ProtocolJson.Options) ?? new()
            : new();

        Recorder.TrySnapshotEvents(out var events, out _);

        // Reuse DrawSnapshotHandler's DTO shape for returned events.
        var matches = events.Where(e => DrawFilterMatcher.Matches(e, filter)).ToArray();
        var snap = new DrawEventSnapshot
        {
            Meta = new SnapshotMeta { Events = matches.Length }
        };
        foreach (var e in matches)
            snap.Events.Add(DrawSnapshotHandler.ToDto(e));

        return ProtocolJson.ToElement(snap);
    }
}
```

This references `DrawSnapshotHandler.ToDto` — add that as a `public static` helper in Task 10's handler.

- [ ] **Step 5: DrawAssertContainsHandler**

```csharp
using System.Linq;
using System.Text.Json;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Handlers;

public static class DrawAssertContainsHandler
{
    public const string Method = "draw.assert_contains";

    private sealed class AssertRequest
    {
        public DrawFilter Filter { get; set; } = new();
        public int MinCount { get; set; } = 1;
        public string? Message { get; set; }
    }

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        if (paramsElement is not { } p)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params required");
        var req = JsonSerializer.Deserialize<AssertRequest>(p.GetRawText(), ProtocolJson.Options)
            ?? throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "empty params");

        Recorder.TrySnapshotEvents(out var events, out _);
        var matched = events.Count(e => DrawFilterMatcher.Matches(e, req.Filter));

        return ProtocolJson.ToElement(new AssertResult
        {
            MinCount = req.MinCount,
            MatchedCount = matched,
            Passed = matched >= req.MinCount,
            Message = req.Message,
        });
    }
}
```

- [ ] **Step 6: Register both + doc + CI**

`./scripts/ci.sh` — PASS.

---

## Task 12: scenario.begin + scenario.end

**Files:**
- Create: `src/Protocol/Models/ScenarioBeginRequest.cs`
- Create: `src/Protocol/Models/ScenarioBeginResult.cs`
- Create: `src/Protocol/Models/ScenarioEndResult.cs`
- Create: `src/Harness/Scenarios/ScenarioState.cs`
- Create: `src/Harness/Handlers/ScenarioBeginHandler.cs`
- Create: `src/Harness/Handlers/ScenarioEndHandler.cs`
- Modify: `src/Harness/ModEntry.cs`, `docs/rpc-schema.md`
- Test: `tests/Harness.Tests/ScenarioStateTests.cs`

**Depends on:** Task 9 (draw arm plumbing), existing `SeedPinner`.

**Design:** `ScenarioState` is a simple mutable struct that tracks whether a scenario is active, its session_id, start tick, and running assertion counts. `scenario.begin` pins RNG + records start tick; `scenario.end` returns duration + assertion stats. FREEZE/THAW lifecycle (D1.6) hooks in later.

**RPC shapes:**
```
scenario.begin:
→ { "id":1,"method":"scenario.begin","params":{"name":"shop_visible","seed":42} }
← { "id":1,"result":{"session_id":"abc123","tick":0} }

scenario.end:
→ { "id":99,"method":"scenario.end" }
← { "id":99,"result":{"duration_ms":342,"assertions_run":5,"assertions_passed":5} }
```

- [ ] **Step 1: DTOs + ScenarioState**

```csharp
// src/Protocol/Models/ScenarioBeginRequest.cs
namespace SdvTestFramework.Protocol.Models;

public sealed class ScenarioBeginRequest
{
    public string Name { get; set; } = string.Empty;
    public int Seed { get; set; }
    public string? Fixture { get; set; }
}

// src/Protocol/Models/ScenarioBeginResult.cs
public sealed class ScenarioBeginResult
{
    public string SessionId { get; set; } = string.Empty;
    public int Tick { get; set; }
}

// src/Protocol/Models/ScenarioEndResult.cs
public sealed class ScenarioEndResult
{
    public int DurationMs { get; set; }
    public int AssertionsRun { get; set; }
    public int AssertionsPassed { get; set; }
}
```

```csharp
// src/Harness/Scenarios/ScenarioState.cs
using System;

namespace SdvTestFramework.Harness.Scenarios;

public sealed class ScenarioState
{
    public static ScenarioState Current { get; private set; } = new();

    public bool IsActive { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int StartTick { get; set; }
    public DateTime StartUtc { get; set; }
    public int AssertionsRun { get; set; }
    public int AssertionsPassed { get; set; }

    public void Reset()
    {
        IsActive = false;
        SessionId = string.Empty;
        Name = string.Empty;
        StartTick = 0;
        StartUtc = DateTime.UtcNow;
        AssertionsRun = 0;
        AssertionsPassed = 0;
    }
}
```

- [ ] **Step 2: Handler — scenario.begin**

```csharp
using System;
using System.Text.Json;
using SdvTestFramework.Harness.Determinism;
using SdvTestFramework.Harness.Scenarios;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewModdingAPI;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

public static class ScenarioBeginHandler
{
    public const string Method = "scenario.begin";

    // Set by ModEntry so the handler can log.
    public static IMonitor Monitor { get; set; } = null!;

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        if (paramsElement is not { } p)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params required");
        var req = JsonSerializer.Deserialize<ScenarioBeginRequest>(p.GetRawText(), ProtocolJson.Options)
            ?? throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "empty params");
        if (string.IsNullOrEmpty(req.Name))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.name required");

        if (ScenarioState.Current.IsActive)
            throw new JsonRpcException(JsonRpcErrorCode.ScenarioNotActive,
                $"scenario '{ScenarioState.Current.Name}' already active — call scenario.end first");

        SeedPinner.Pin(req.Seed, Monitor);

        var s = ScenarioState.Current;
        s.Reset();
        s.IsActive = true;
        s.Name = req.Name;
        s.SessionId = Guid.NewGuid().ToString("N");
        s.StartTick = Game1.ticks;
        s.StartUtc = DateTime.UtcNow;

        return ProtocolJson.ToElement(new ScenarioBeginResult
        {
            SessionId = s.SessionId,
            Tick = s.StartTick,
        });
    }
}
```

- [ ] **Step 3: Handler — scenario.end**

```csharp
using System;
using System.Text.Json;
using SdvTestFramework.Harness.Scenarios;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Handlers;

public static class ScenarioEndHandler
{
    public const string Method = "scenario.end";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var s = ScenarioState.Current;
        if (!s.IsActive)
            throw new JsonRpcException(JsonRpcErrorCode.ScenarioNotActive, "no scenario active");

        var elapsed = (DateTime.UtcNow - s.StartUtc).TotalMilliseconds;
        var result = new ScenarioEndResult
        {
            DurationMs = (int)elapsed,
            AssertionsRun = s.AssertionsRun,
            AssertionsPassed = s.AssertionsPassed,
        };

        s.Reset();
        return ProtocolJson.ToElement(result);
    }
}
```

- [ ] **Step 4: Wire Monitor from ModEntry**

In `ModEntry.Entry`, after `_rpc.Register(...)` lines for ScenarioBegin, add:

```csharp
ScenarioBeginHandler.Monitor = this.Monitor;
```

- [ ] **Step 5: Unit tests for ScenarioState**

`tests/Harness.Tests/ScenarioStateTests.cs`:

```csharp
using SdvTestFramework.Harness.Scenarios;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class ScenarioStateTests
{
    [Fact]
    public void Reset_ClearsEverything()
    {
        var s = new ScenarioState { IsActive = true, Name = "x", AssertionsRun = 5 };
        s.Reset();
        Assert.False(s.IsActive);
        Assert.Empty(s.Name);
        Assert.Equal(0, s.AssertionsRun);
    }
}
```

- [ ] **Step 6: Register handlers + doc + CI**

`./scripts/ci.sh` — PASS.

---

## Task 13: fixture.load

**Files:**
- Create: `src/Protocol/Models/FixtureLoadRequest.cs`
- Create: `src/Harness/Handlers/FixtureLoadHandler.cs`
- Modify: `src/Harness/ModEntry.cs`, `docs/rpc-schema.md`
- Test: `tests/Protocol.Tests/FixtureLoadRequestSerializationTests.cs`

**Depends on:** Existing `harness_load` console command logic (we're porting it to an RPC method).

**RPC shape:**
```
→ { "id":1,"method":"fixture.load","params":{"name":"spring_day_1_clean"} }
← { "id":1,"result":{"ok":true,"loading":true} }
```

Note: load is asynchronous — SDV takes multiple ticks to complete. Result indicates the load was *initiated*. Callers should follow up with `state.player` in a wait-for-ready loop.

- [ ] **Step 1: DTO**

```csharp
namespace SdvTestFramework.Protocol.Models;

public sealed class FixtureLoadRequest
{
    public string Name { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Handler**

```csharp
using System.Text.Json;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewModdingAPI;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

public static class FixtureLoadHandler
{
    public const string Method = "fixture.load";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        if (paramsElement is not { } p)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params required");
        var req = JsonSerializer.Deserialize<FixtureLoadRequest>(p.GetRawText(), ProtocolJson.Options)
            ?? throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "empty params");
        if (string.IsNullOrEmpty(req.Name))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "name required");

        if (Context.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "already in a save — return to title first");

        Game1.currentLoader = SaveGame.getLoadEnumerator(req.Name);
        Game1.gameMode = 6;

        return JsonDocument.Parse("{\"ok\":true,\"loading\":true}").RootElement;
    }
}
```

- [ ] **Step 3: Register + doc + CI**

`./scripts/ci.sh` — PASS.

---

## Task 14: Scenario JSON schema + parser

**Files:**
- Create: `schemas/scenario.schema.json`
- Create: `src/Protocol/Models/ScenarioSpec.cs`
- Create: `src/Protocol/Models/ScenarioStep.cs`
- Create: `src/Protocol/Models/ScenarioAssertion.cs`
- Create: `src/Runner/Scenarios/ScenarioLoader.cs`
- Modify: `src/Protocol/Protocol.csproj` — add `JsonSchema.Net` NuGet
- Test: `tests/Runner.Tests/ScenarioLoaderTests.cs`

**Depends on:** Nothing on harness side. Pure runner/parser work.

**Scenario file shape (per `docs/spec.md §4.6`):**

```json
{
  "name": "shop_menu_shows_custom_item",
  "fixture": "fixtures/spring_day_5_clean.sav",
  "mods": ["MyCustomShopMod"],
  "config": {
    "seed": 42,
    "zoom": 1.0,
    "resolution": [1280, 720]
  },
  "steps": [
    { "action": "player.warp", "args": { "location": "SeedShop", "x": 4, "y": 19 } },
    { "action": "world.interact_npc", "args": { "name": "Pierre" } }
  ],
  "assertions": [
    { "type": "state", "expr": "state.menu.type == 'ShopMenu'" },
    { "type": "draw.contains", "filter": { "texture_asset": "Mods/Foo" }, "min_count": 1 }
  ]
}
```

- [ ] **Step 1: JSON Schema**

Create `schemas/scenario.schema.json`. This validates the structure; semantic validation (does the action exist?) happens in the runner.

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://sdv-test-framework/schemas/scenario.schema.json",
  "title": "SDV Test Framework Scenario",
  "type": "object",
  "required": ["name", "steps"],
  "properties": {
    "name": { "type": "string", "minLength": 1 },
    "fixture": { "type": "string" },
    "mods": { "type": "array", "items": { "type": "string" } },
    "config": {
      "type": "object",
      "properties": {
        "seed": { "type": "integer" },
        "zoom": { "type": "number" },
        "resolution": {
          "type": "array",
          "items": { "type": "integer" },
          "minItems": 2,
          "maxItems": 2
        }
      },
      "additionalProperties": false
    },
    "steps": {
      "type": "array",
      "items": {
        "type": "object",
        "required": ["action"],
        "properties": {
          "action": { "type": "string" },
          "args": { "type": "object" }
        },
        "additionalProperties": false
      }
    },
    "assertions": {
      "type": "array",
      "items": {
        "type": "object",
        "required": ["type"],
        "properties": {
          "type": { "type": "string" },
          "expr": { "type": "string" },
          "filter": { "type": "object" },
          "min_count": { "type": "integer", "minimum": 0 },
          "message": { "type": "string" }
        }
      }
    }
  },
  "additionalProperties": false
}
```

- [ ] **Step 2: DTOs**

```csharp
// src/Protocol/Models/ScenarioSpec.cs
using System.Collections.Generic;

namespace SdvTestFramework.Protocol.Models;

public sealed class ScenarioSpec
{
    public string Name { get; set; } = string.Empty;
    public string? Fixture { get; set; }
    public List<string> Mods { get; set; } = new();
    public ScenarioConfig Config { get; set; } = new();
    public List<ScenarioStep> Steps { get; set; } = new();
    public List<ScenarioAssertion> Assertions { get; set; } = new();
}

public sealed class ScenarioConfig
{
    public int Seed { get; set; } = 42;
    public double Zoom { get; set; } = 1.0;
    public int[] Resolution { get; set; } = { 1280, 720 };
}
```

```csharp
// src/Protocol/Models/ScenarioStep.cs
using System.Text.Json;

namespace SdvTestFramework.Protocol.Models;

public sealed class ScenarioStep
{
    public string Action { get; set; } = string.Empty;
    public JsonElement? Args { get; set; }
}
```

```csharp
// src/Protocol/Models/ScenarioAssertion.cs
using System.Text.Json;

namespace SdvTestFramework.Protocol.Models;

public sealed class ScenarioAssertion
{
    public string Type { get; set; } = string.Empty;
    public string? Expr { get; set; }
    public JsonElement? Filter { get; set; }
    public int MinCount { get; set; } = 1;
    public string? Message { get; set; }
}
```

- [ ] **Step 3: Add JsonSchema.Net to Protocol.csproj**

Modify `src/Protocol/Protocol.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="JsonSchema.Net" Version="7.0.2" />
</ItemGroup>
```

Run `dotnet restore` to pull the package.

- [ ] **Step 4: ScenarioLoader**

Create `src/Runner/Scenarios/ScenarioLoader.cs`:

```csharp
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Scenarios;

public sealed class ScenarioLoadException : Exception
{
    public ScenarioLoadException(string file, string message) : base($"{file}: {message}") { }
}

public static class ScenarioLoader
{
    private static readonly JsonSchema Schema = LoadSchema();

    private static JsonSchema LoadSchema()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "schemas", "scenario.schema.json"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "schemas", "scenario.schema.json"),
            // repo-relative during dev
            Path.Combine(Directory.GetCurrentDirectory(), "schemas", "scenario.schema.json"),
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return JsonSchema.FromFile(c);
        throw new FileNotFoundException("scenario.schema.json not found in any known location");
    }

    public static ScenarioSpec Load(string path)
    {
        if (!File.Exists(path))
            throw new ScenarioLoadException(path, "file not found");

        var json = File.ReadAllText(path);
        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch (JsonException ex) { throw new ScenarioLoadException(path, $"invalid JSON: {ex.Message}"); }
        if (node is null) throw new ScenarioLoadException(path, "empty file");

        var result = Schema.Evaluate(node, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
        });
        if (!result.IsValid)
        {
            var messages = string.Join("; ", result.Details.Where(d => !d.IsValid).Select(d => $"{d.InstanceLocation}: {d.Errors?.FirstOrDefault().Value ?? "invalid"}"));
            throw new ScenarioLoadException(path, $"schema validation failed: {messages}");
        }

        try
        {
            return JsonSerializer.Deserialize<ScenarioSpec>(json, ProtocolJson.Options)
                ?? throw new ScenarioLoadException(path, "deserialization returned null");
        }
        catch (JsonException ex)
        {
            throw new ScenarioLoadException(path, $"deserialization failed: {ex.Message}");
        }
    }
}
```

Add `using System.Linq;` to the top.

- [ ] **Step 5: Tests**

Create `tests/Runner.Tests/ScenarioLoaderTests.cs`:

```csharp
using System.IO;
using SdvTestFramework.Runner.Scenarios;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class ScenarioLoaderTests
{
    private static string WriteTemp(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"scenario-{System.Guid.NewGuid():N}.test.json");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Load_Valid_ReturnsSpec()
    {
        var path = WriteTemp("""
{ "name":"smoke","steps":[{"action":"player.warp","args":{"location":"Farm","x":1,"y":1}}] }
""");
        var spec = ScenarioLoader.Load(path);
        Assert.Equal("smoke", spec.Name);
        Assert.Single(spec.Steps);
        Assert.Equal("player.warp", spec.Steps[0].Action);
    }

    [Fact]
    public void Load_MissingRequired_Throws()
    {
        var path = WriteTemp("{ \"steps\":[] }");
        var ex = Assert.Throws<ScenarioLoadException>(() => ScenarioLoader.Load(path));
        Assert.Contains("name", ex.Message);
    }

    [Fact]
    public void Load_InvalidJson_Throws()
    {
        var path = WriteTemp("{ not json");
        var ex = Assert.Throws<ScenarioLoadException>(() => ScenarioLoader.Load(path));
        Assert.Contains("invalid JSON", ex.Message);
    }

    [Fact]
    public void Load_UnknownFile_Throws()
    {
        var ex = Assert.Throws<ScenarioLoadException>(() => ScenarioLoader.Load("/tmp/nope-" + System.Guid.NewGuid()));
        Assert.Contains("file not found", ex.Message);
    }
}
```

- [ ] **Step 6: Ensure schema is copied to Runner output**

Modify `src/Runner/Runner.csproj`:

```xml
<ItemGroup>
  <None Include="..\..\schemas\scenario.schema.json">
    <Link>schemas/scenario.schema.json</Link>
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

Same block in `tests/Runner.Tests/Runner.Tests.csproj` so tests find the schema.

- [ ] **Step 7: Run CI**

`./scripts/ci.sh` — PASS. Expect 4 new tests.

---

## Task 15: ScenarioRunner (executor)

**Files:**
- Create: `src/Runner/Scenarios/ScenarioRunner.cs`
- Create: `src/Runner/Scenarios/ScenarioReport.cs`
- Test: `tests/Runner.Tests/ScenarioRunnerTests.cs`

**Depends on:** Tasks 1–13 (all RPC methods), Task 14 (ScenarioLoader).

**Design:** `ScenarioRunner` takes a loaded `ScenarioSpec` and a `JsonRpcSession`, and runs: `scenario.begin` → optional `fixture.load` → iterate steps → run assertions → `scenario.end`. Each step's `action` maps to an RPC method; `args` is passed through as `params`. Assertions of type `state` use a mini DSL (evaluated via `JsonPath`-like eval — for M1 we support only direct equality checks); assertions of type `draw.contains` map to `draw.assert_contains`.

- [ ] **Step 1: ScenarioReport + ScenarioRunner skeleton**

```csharp
// src/Runner/Scenarios/ScenarioReport.cs
using System.Collections.Generic;

namespace SdvTestFramework.Runner.Scenarios;

public sealed class ScenarioReport
{
    public string Name { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public int DurationMs { get; set; }
    public int AssertionsRun { get; set; }
    public int AssertionsPassed { get; set; }
    public List<string> Failures { get; set; } = new();
}
```

```csharp
// src/Runner/Scenarios/ScenarioRunner.cs
using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Scenarios;

public sealed class ScenarioRunner
{
    private readonly JsonRpcSession _session;
    public ScenarioRunner(JsonRpcSession session) { _session = session; }

    public async Task<ScenarioReport> RunAsync(ScenarioSpec spec, CancellationToken ct)
    {
        var report = new ScenarioReport { Name = spec.Name };
        var sw = Stopwatch.StartNew();

        try
        {
            // 1. begin
            var beginReq = JsonSerializer.SerializeToElement(new ScenarioBeginRequest
            {
                Name = spec.Name,
                Seed = spec.Config.Seed,
                Fixture = spec.Fixture,
            }, Protocol.Json.ProtocolJson.Options);
            var beginResp = await _session.InvokeAsync("scenario.begin", beginReq, ct);
            if (beginResp.Error is { } e) throw new InvalidOperationException($"scenario.begin failed: {e.Message}");

            // 2. fixture.load (if specified)
            if (!string.IsNullOrEmpty(spec.Fixture))
            {
                var fxReq = JsonSerializer.SerializeToElement(new FixtureLoadRequest { Name = spec.Fixture }, Protocol.Json.ProtocolJson.Options);
                var fxResp = await _session.InvokeAsync("fixture.load", fxReq, ct);
                if (fxResp.Error is { } fe) throw new InvalidOperationException($"fixture.load failed: {fe.Message}");

                // Poll for world-ready via state.player (it'll succeed with real data once loaded).
                await WaitForWorldReady(ct);
            }

            // 3. steps
            foreach (var step in spec.Steps)
            {
                var resp = await _session.InvokeAsync(step.Action, step.Args, ct);
                if (resp.Error is { } ex)
                    throw new InvalidOperationException($"step '{step.Action}' failed: {ex.Message}");
            }

            // 4. assertions
            foreach (var a in spec.Assertions)
            {
                report.AssertionsRun++;
                bool passed = await EvaluateAssertionAsync(a, ct);
                if (passed) report.AssertionsPassed++;
                else report.Failures.Add($"{a.Type}: {a.Message ?? "failed"}");
            }

            // 5. end
            var endResp = await _session.InvokeAsync("scenario.end", params_: null, ct);
            report.Passed = report.Failures.Count == 0;
        }
        catch (Exception ex)
        {
            report.Failures.Add(ex.Message);
            report.Passed = false;
        }
        finally
        {
            report.DurationMs = (int)sw.ElapsedMilliseconds;
        }

        return report;
    }

    private async Task WaitForWorldReady(CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var resp = await _session.InvokeAsync("state.player", params_: null, ct);
            if (resp.Result is { } r && r.TryGetProperty("location", out var loc) && !string.IsNullOrEmpty(loc.GetString()))
                return;
            await Task.Delay(500, ct);
        }
        throw new TimeoutException("world never became ready after fixture.load");
    }

    private async Task<bool> EvaluateAssertionAsync(ScenarioAssertion a, CancellationToken ct)
    {
        switch (a.Type)
        {
            case "draw.contains":
            {
                if (a.Filter is null) return false;
                var req = JsonSerializer.SerializeToElement(new
                {
                    filter = a.Filter,
                    min_count = a.MinCount,
                    message = a.Message,
                }, Protocol.Json.ProtocolJson.Options);
                var resp = await _session.InvokeAsync("draw.assert_contains", req, ct);
                if (resp.Error is not null) return false;
                return resp.Result!.Value.GetProperty("passed").GetBoolean();
            }
            case "state":
            {
                // Mini DSL: "state.menu.type == 'ShopMenu'" — parse LHS path + RHS literal.
                // Minimal M1 implementation: support only the `==` operator against a string literal.
                if (string.IsNullOrWhiteSpace(a.Expr)) return false;
                var parts = a.Expr.Split("==", 2);
                if (parts.Length != 2) return false;
                var path = parts[0].Trim().Split('.');      // e.g. ["state", "menu", "type"]
                var literal = parts[1].Trim().Trim('\'', '"');

                if (path.Length < 2 || path[0] != "state") return false;
                var resp = await _session.InvokeAsync($"state.{path[1]}", params_: null, ct);
                if (resp.Error is not null) return false;
                JsonElement? cur = resp.Result;
                for (int i = 2; i < path.Length; i++)
                {
                    if (cur is not { } el || el.ValueKind != JsonValueKind.Object) return false;
                    if (!el.TryGetProperty(path[i], out var nested)) return false;
                    cur = nested;
                }
                return cur is { } leaf && leaf.ValueKind == JsonValueKind.String && leaf.GetString() == literal;
            }
            default:
                return false;
        }
    }
}
```

- [ ] **Step 2: Tests using a fake harness**

`tests/Runner.Tests/ScenarioRunnerTests.cs` uses the same in-proc Unix-socket pattern as `ProbeCommandTests`:

```csharp
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Models;
using SdvTestFramework.Runner.Scenarios;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class ScenarioRunnerTests
{
    [Fact]
    public async Task EmptyScenario_WithNoAssertions_Passes()
    {
        var socket = Path.Combine(Path.GetTempPath(), $"sdv-test-{Guid.NewGuid():N}.sock");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    JsonElement r;
                    switch (req.Method)
                    {
                        case "scenario.begin": r = JsonDocument.Parse("""{"session_id":"t","tick":0}""").RootElement; break;
                        case "scenario.end":   r = JsonDocument.Parse("""{"duration_ms":10,"assertions_run":0,"assertions_passed":0}""").RootElement; break;
                        default:               r = JsonDocument.Parse("""{"ok":true}""").RootElement; break;
                    }
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("""{"version":"0"}""").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec { Name = "t" }, cts.Token);

        Assert.True(report.Passed);
        Assert.Empty(report.Failures);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }
}
```

- [ ] **Step 3: Run CI**

`./scripts/ci.sh` — PASS.

---

## Task 16: Runner `doctor` command

**Files:**
- Create: `src/Runner/Commands/DoctorCommand.cs`
- Modify: `src/Runner/Program.cs` — route `doctor` subcommand
- Test: `tests/Runner.Tests/DoctorCommandTests.cs`

**Depends on:** Nothing runtime-specific — this is a pure local-check command.

**Checks performed:**
1. `dotnet --version` runs
2. `SDV_INSTALL_PATH` (or auto-detect at Flatpak path) exists + contains `StardewModdingAPI`
3. `$HOME/.config/StardewValley` or Flatpak variant exists (saves location)
4. Reports pass/fail for each, exits 0 on all green, 1 otherwise.

- [ ] **Step 1: DoctorCommand**

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SdvTestFramework.Runner.Commands;

public static class DoctorCommand
{
    public static Task<int> RunAsync(ReadOnlyMemory<string> args, CancellationToken ct)
    {
        int failed = 0;

        failed += Check("dotnet runtime available", () => !string.IsNullOrEmpty(Environment.Version?.ToString())) ? 0 : 1;

        var install = Environment.GetEnvironmentVariable("SDV_INSTALL_PATH")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Stardew Valley");
        failed += Check($"SDV install at {install}", () => Directory.Exists(install)) ? 0 : 1;
        failed += Check("SMAPI binary present", () => File.Exists(Path.Combine(install, "StardewModdingAPI"))) ? 0 : 1;

        var savesA = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StardewValley", "Saves");
        var savesB = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".var/app/com.valvesoftware.Steam/.config/StardewValley/Saves");
        failed += Check("Saves directory found", () => Directory.Exists(savesA) || Directory.Exists(savesB)) ? 0 : 1;

        Console.WriteLine(failed == 0 ? "[doctor] all checks passed" : $"[doctor] {failed} check(s) failed");
        return Task.FromResult(failed == 0 ? 0 : 1);
    }

    private static bool Check(string name, Func<bool> predicate)
    {
        bool ok;
        try { ok = predicate(); }
        catch { ok = false; }
        Console.WriteLine($"  [{(ok ? "ok" : "FAIL")}] {name}");
        return ok;
    }
}
```

- [ ] **Step 2: Wire into Program.cs**

In `src/Runner/Program.cs`, extend the switch:

```csharp
return args[0] switch
{
    "probe" => await ProbeCommand.RunAsync(args.AsMemory()[1..], cts.Token),
    "doctor" => await DoctorCommand.RunAsync(args.AsMemory()[1..], cts.Token),
    _ => Unknown(args[0]),
};
```

Update help text to list `doctor`.

- [ ] **Step 3: Test**

`tests/Runner.Tests/DoctorCommandTests.cs`:

```csharp
using System.IO;
using System.Threading;
using SdvTestFramework.Runner.Commands;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class DoctorCommandTests
{
    [Fact]
    public async Task Run_OnThisMachine_ReturnsZero()
    {
        // This workstation is known to have a valid SDV install (per memory sdv_install_path.md).
        var outW = new StringWriter();
        var prior = System.Console.Out;
        System.Console.SetOut(outW);
        try
        {
            int exit = await DoctorCommand.RunAsync(System.ReadOnlyMemory<string>.Empty, CancellationToken.None);
            Assert.Equal(0, exit);
        }
        finally { System.Console.SetOut(prior); }

        Assert.Contains("all checks passed", outW.ToString());
    }
}
```

- [ ] **Step 4: CI**

`./scripts/ci.sh` — PASS.

---

## Task 17: Runner `list` command

**Files:**
- Create: `src/Runner/Commands/ListCommand.cs`
- Modify: `src/Runner/Program.cs`
- Test: `tests/Runner.Tests/ListCommandTests.cs`

**Depends on:** Task 14 (ScenarioLoader).

**Behavior:** Scan a path (default `.`) recursively for `*.test.json` files, validate each via `ScenarioLoader`, print one line per scenario with `[ok]` or `[invalid]`.

- [ ] **Step 1: ListCommand**

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Scenarios;

namespace SdvTestFramework.Runner.Commands;

public static class ListCommand
{
    public static Task<int> RunAsync(ReadOnlyMemory<string> args, CancellationToken ct)
    {
        string root = args.Length > 0 ? args.Span[0] : Directory.GetCurrentDirectory();
        if (!Directory.Exists(root))
        {
            Console.Error.WriteLine($"not a directory: {root}");
            return Task.FromResult(2);
        }

        int ok = 0, bad = 0;
        foreach (var path in Directory.EnumerateFiles(root, "*.test.json", SearchOption.AllDirectories))
        {
            try
            {
                var spec = ScenarioLoader.Load(path);
                Console.WriteLine($"[ok] {spec.Name} ({path})");
                ok++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[invalid] {path}: {ex.Message}");
                bad++;
            }
        }
        Console.WriteLine($"[list] {ok} ok, {bad} invalid");
        return Task.FromResult(bad == 0 ? 0 : 1);
    }
}
```

- [ ] **Step 2: Program.cs route**

Add `"list" => await ListCommand.RunAsync(args.AsMemory()[1..], cts.Token)` to switch + help text.

- [ ] **Step 3: Test**

`tests/Runner.Tests/ListCommandTests.cs`:

```csharp
using System.IO;
using SdvTestFramework.Runner.Commands;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class ListCommandTests
{
    [Fact]
    public async Task Run_ValidAndInvalidFiles_CountsBoth()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"list-{System.Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "ok.test.json"),
            """{"name":"x","steps":[]}""");
        File.WriteAllText(Path.Combine(dir, "bad.test.json"),
            """{"oops":true}""");

        var outW = new StringWriter();
        System.Console.SetOut(outW);
        int exit = await ListCommand.RunAsync(new System.ReadOnlyMemory<string>(new[] { dir }),
            System.Threading.CancellationToken.None);

        Assert.Equal(1, exit); // because one invalid
        var output = outW.ToString();
        Assert.Contains("[ok]", output);
        Assert.Contains("[invalid]", output);
        Assert.Contains("1 ok, 1 invalid", output);
    }
}
```

- [ ] **Step 4: CI**

`./scripts/ci.sh` — PASS.

---

## Task 18: Runner `run` command (end-to-end)

**Files:**
- Create: `src/Runner/Commands/RunCommand.cs`
- Create: `src/Runner/SdvLauncher.cs` — process launcher for SMAPI
- Modify: `src/Runner/Program.cs`
- Test: `tests/Runner.Tests/RunCommandTests.cs`

**Depends on:** Tasks 14, 15, 16. This is the headline user-facing command.

**Behavior:**
1. Parse `--filter <pattern>` / file args / default `.`
2. Launch SMAPI as subprocess with `SDV_TEST_SOCKET` set
3. Wait for `ready` notification (60s)
4. For each scenario: load, run via `ScenarioRunner`, collect report
5. Exit 0 if all pass, 1 otherwise, report summary

- [ ] **Step 1: SdvLauncher**

```csharp
using System;
using System.Diagnostics;
using System.IO;

namespace SdvTestFramework.Runner;

public static class SdvLauncher
{
    public static Process Launch(string socketPath, string? installPath = null, string? modsPath = null)
    {
        installPath ??= Environment.GetEnvironmentVariable("SDV_INSTALL_PATH")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Stardew Valley");

        var smapi = Path.Combine(installPath, "StardewModdingAPI");
        if (!File.Exists(smapi))
            throw new FileNotFoundException($"SMAPI not found at {smapi}");

        var psi = new ProcessStartInfo(smapi)
        {
            UseShellExecute = false,
            WorkingDirectory = installPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.Environment["SDV_TEST_SOCKET"] = socketPath;
        if (!string.IsNullOrEmpty(modsPath))
            psi.ArgumentList.Add($"--mods-path");
        if (!string.IsNullOrEmpty(modsPath))
            psi.ArgumentList.Add(modsPath);

        var p = Process.Start(psi)
            ?? throw new InvalidOperationException("failed to start SMAPI process");
        return p;
    }
}
```

- [ ] **Step 2: RunCommand**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Runner.Scenarios;

namespace SdvTestFramework.Runner.Commands;

public static class RunCommand
{
    public static async Task<int> RunAsync(ReadOnlyMemory<string> args, CancellationToken ct)
    {
        var paths = new List<string>();
        string? filter = null;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args.Span[i];
            if (a == "--filter" && i + 1 < args.Length) { filter = args.Span[++i]; continue; }
            paths.Add(a);
        }
        if (paths.Count == 0) paths.Add(Directory.GetCurrentDirectory());

        var scenarios = new List<(string Path, ScenarioSpec Spec)>();
        foreach (var root in paths)
        {
            if (File.Exists(root)) scenarios.Add((root, ScenarioLoader.Load(root)));
            else if (Directory.Exists(root))
            {
                foreach (var f in Directory.EnumerateFiles(root, "*.test.json", SearchOption.AllDirectories))
                    scenarios.Add((f, ScenarioLoader.Load(f)));
            }
        }
        if (filter != null)
            scenarios = scenarios.Where(s => s.Spec.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        if (scenarios.Count == 0) { Console.WriteLine("no scenarios matched"); return 0; }

        var socket = Path.Combine(Path.GetTempPath(), $"sdv-test-{Guid.NewGuid():N}.sock");
        using var sdv = SdvLauncher.Launch(socket);
        try
        {
            // Wait for listener to exist.
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(TimeSpan.FromSeconds(60));
            for (int i = 0; i < 120 && !File.Exists(socket); i++)
                await Task.Delay(500, connectCts.Token);

            using var session = await UnixSocketRpc.ConnectAsync(socket, connectCts.Token);
            var readyTcs = new TaskCompletionSource<JsonRpcNotification>();
            session.NotificationReceived += n => { if (n.Method == "ready") readyTcs.TrySetResult(n); };
            _ = session.RunAsync(ct);
            await readyTcs.Task.WaitAsync(TimeSpan.FromSeconds(60), ct);

            var runner = new ScenarioRunner(session);
            int failed = 0;
            foreach (var (path, spec) in scenarios)
            {
                var report = await runner.RunAsync(spec, ct);
                var status = report.Passed ? "PASS" : "FAIL";
                Console.WriteLine($"  {status} {spec.Name} ({report.DurationMs}ms) — {path}");
                foreach (var f in report.Failures) Console.WriteLine($"        {f}");
                if (!report.Passed) failed++;
            }

            Console.WriteLine();
            Console.WriteLine($"[run] {scenarios.Count - failed}/{scenarios.Count} passed");
            return failed == 0 ? 0 : 1;
        }
        finally
        {
            try { sdv.Kill(); sdv.WaitForExit(5000); } catch { }
        }
    }
}
```

- [ ] **Step 3: Wire into Program.cs**

Extend switch + help. `using System.Linq;` at top of RunCommand.

- [ ] **Step 4: Test — skeleton only (full integration requires live SDV)**

`tests/Runner.Tests/RunCommandTests.cs`:

```csharp
using System;
using System.IO;
using System.Threading;
using SdvTestFramework.Runner.Commands;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class RunCommandTests
{
    [Fact]
    public async Task Run_NoScenarios_ReturnsZero()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"run-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var outW = new StringWriter();
        Console.SetOut(outW);
        var exit = await RunCommand.RunAsync(new ReadOnlyMemory<string>(new[] { dir }), CancellationToken.None);
        Assert.Equal(0, exit);
        Assert.Contains("no scenarios matched", outW.ToString());
    }
}
```

Full end-to-end execution against live SDV is an integration test — run manually via a scratch scenario file. Document that in the Task 18 acceptance criteria below.

- [ ] **Step 5: Manual integration test**

Create a temp scenario file:

```bash
mkdir -p /tmp/sdv-m1-test
cat > /tmp/sdv-m1-test/smoke.test.json <<'JSON'
{
  "name": "m1_smoke_no_fixture",
  "config": { "seed": 42 },
  "steps": [],
  "assertions": []
}
JSON
dotnet run --project src/Runner -- run /tmp/sdv-m1-test
```

Expected: SMAPI launches, `ready` received, scenario runs (no steps, no assertions), exits with `1/1 passed`.

Document result in `docs/milestones/current.md` under M1 progress.

- [ ] **Step 6: Run CI**

`./scripts/ci.sh` — PASS. 1 new test.

---

## Self-Review

**1. Spec coverage.**
- M1-core.md D1.2 lists methods: state.player ✓ (prior), state.time ✓ (prior), state.location ✓ (Task 1), state.npc ✓ (Task 2), state.menu ✓ (Task 3), player.warp ✓ (4), player.give_item ✓ (5), player.set_money ✓ (6), time.advance ✓ (7), world.set_weather ✓ (8), draw.arm ✓ (9), draw.snapshot ✓ (10), draw.find ✓ (11), draw.assert_contains ✓ (11), scenario.begin ✓ (12), scenario.end ✓ (12), fixture.load ✓ (13).
- D1.3 runner commands: run ✓ (18), doctor ✓ (16), list ✓ (17), --filter ✓ (18).
- D1.4 scenario format: JSON schema ✓ (14), parser with validation ✓ (14), loader resolves fixtures/mods paths ✓ (14 — via `ScenarioSpec.Fixture` path resolution in runner).
- **Gap:** Scenario assertion DSL in `ScenarioRunner` (Task 15) only supports `==` against string literals for `state` assertions. Good enough to cover M1's 10-scenario target if scenarios are written within those constraints. Broader expression support is a D1.4+ extension — noted for follow-up.

**2. Placeholder scan.** No `TBD` / `implement later` / `similar to task N` (patterns are referenced by file path only, and the cited files exist at the time of task execution). Every code step contains the actual code.

**3. Type consistency.** `TilePoint` type used in `PlayerState`, `LocationState`, `NpcState` — same type from `src/Protocol/Models/PlayerState.cs` (created in D1.2 walking skeleton). Verified. `DrawFilter`, `DrawEventDto`, `DrawEventSnapshot`, `AssertResult` — each defined once, referenced consistently. `ScenarioSpec` / `ScenarioStep` / `ScenarioAssertion` — defined in Task 14, used in Task 15. Handler `Method` constants use consistent naming (`state.*`, `player.*`, `draw.*`, `scenario.*`, `fixture.*`). `JsonRpcErrorCode.GameStateInvalid` used for "unknown NPC / location / item id" consistently.

**Fixed inline:** `DrawSnapshotHandler.ToDto` referenced from `DrawFindHandler` — Task 10 needs to expose this helper publicly so Task 11 can reuse it. Noted in Task 10 Step 2.

---

## Follow-up plans (out of scope here)

- **D1.5 — Texture → asset path resolution (Tier 1).** Hook SMAPI's `IGameContentHelper.Load<Texture2D>` + `AssetRequested` event, maintain `ConditionalWeakTable<Texture2D, string>`, resolve at snapshot time. Needs its own plan: hook design, cache invalidation on `InvalidateCache`, fallback-to-anonymous semantics. Unblocks `draw.find` filtering by real asset path (currently stringified `TextureRefId`).
- **D1.6 — Determinism controller (FREEZE/THAW).** Per-location RNG pin, NPC `Halt()` + schedule null, cursor patches (already exist), `eventUp=true` toggling via scenario lifecycle, and — crucially — the parallax-background fix from the M0 spike (see `docs/open-questions.md`). Needs its own plan: controller API shape, interaction with `scenario.begin` / `.end`, restoration ordering.
- **D1.7 — Sample suite.** Pick a small, stable CP mod (maintainer buy-in TBD), author 10 scenarios, ensure they pass twice in a row. Needs its own plan: mod selection criteria, scenario authoring playbook, documentation template.

---

## Execution Handoff

Plan saved to `docs/superpowers/plans/2026-04-22-m1-rpc-surface-and-runner.md`. Two execution options:

**1. Subagent-Driven (recommended)** — Dispatch a fresh subagent per task with full context, review between tasks, fast iteration. Best for this plan because the 18 tasks follow clear patterns and each task's code is self-contained.

**2. Inline Execution** — Execute tasks in this session via `superpowers:executing-plans` with review checkpoints. Slower overall but no context-switching.

**Which approach?**
