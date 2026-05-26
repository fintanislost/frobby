# SVE Slice 28 Movie Theater NPC Interaction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an SVE proof scenario that validates player-like tile-click interaction with a modded NPC working inside Stardew's `MovieTheater`, and only harden Frobby's neutral input tooling if the live scenario exposes a framework gap.

**Architecture:** Start with the live acceptance scenario because existing Frobby primitives may already satisfy this slice. If the scenario fails specifically because `input.click_tile` cannot open NPC dialogue in a special location, add neutral diagnostics and a generic native action-tile fallback for right-clicks on NPC-occupied tiles; keep SVE-specific names, flags, and dialogue text in the SVE scenario only.

**Tech Stack:** .NET/C# Frobby harness and protocol models, JSON runner scenarios, Stardew Valley/SMAPI runtime, SVE Content Patcher content, headless `./scripts/sdv-test`.

---

## File Structure

SVE files:

- Create: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/36-sve-movie-theater-npc-click.test.json`
  - Scenario-level acceptance coverage for Claire's theater schedule and player-like tile click.
- Modify: `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`
  - Documents the new scenario and the intended Frobby capability coverage.

Frobby files for the fast path:

- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`
  - Marks Slice 28 done after live verification.

Frobby files only if the live scenario exposes a neutral `input.click_tile` gap:

- Modify: `src/Protocol/Models/InputClickTileRequest.cs`
  - Adds optional `target_npc_name` diagnostics to `InputClickTileResult`.
- Modify: `src/Harness/Handlers/InputClickTileHandler.cs`
  - Projects NPC occupancy at the clicked tile and, if needed, falls back from an unhandled right-click to the location's native `checkAction` for NPC-occupied tiles.
- Modify: `tests/Protocol.Tests/InputClickTileSerializationTests.cs`
  - Covers snake_case serialization of `target_npc_name`.
- Modify: `tests/Harness.Tests/InputClickTileHandlerTests.cs`
  - Covers diagnostics and fallback behavior with the existing fake world.
- Modify: `docs/rpc-schema.md`
  - Documents the added neutral `input.click_tile` diagnostics/fallback semantics.

## Task 1: Branch Setup And Acceptance Scenario

**Files:**
- Create: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/36-sve-movie-theater-npc-click.test.json`

- [ ] **Step 1: Create the SVE Slice 28 branch**

Run:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded switch -c feature/frobby-sve-slice-28-movie-theater-npc
```

Expected: branch switches from the current Slice 27 SVE branch to `feature/frobby-sve-slice-28-movie-theater-npc`. If the branch already exists, run:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded switch feature/frobby-sve-slice-28-movie-theater-npc
```

- [ ] **Step 2: Write the acceptance scenario**

Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/36-sve-movie-theater-npc-click.test.json` with:

```json
{
  "name": "sve_movie_theater_npc_click",
  "fixture": "m0spike_436515781",
  "config": { "seed": 42 },
  "steps": [
    { "action": "time.set", "args": { "time": 600, "day": 3, "season": "spring", "year": 1 } },
    { "action": "world.set_weather", "args": { "type": "sun" } },
    { "action": "player.add_mail", "args": { "id": "ccMovieTheater" } },
    { "action": "player.add_event_seen", "args": { "id": "191393" } },
    {
      "action": "wait.player",
      "args": {
        "mail_received": "ccMovieTheater",
        "event_seen": "191393",
        "timeout_ms": 10000,
        "poll_ms": 100
      }
    },
    {
      "action": "time.next_day",
      "args": { "settle_timeout_ms": 30000, "poll_ms": 100 }
    },
    { "action": "time.set", "args": { "time": 900, "day": 4, "season": "spring", "year": 1 } },
    {
      "action": "wait.npc_location",
      "args": {
        "name": "Claire",
        "location": "MovieTheater",
        "x": 7,
        "y": 5,
        "timeout_ms": 30000,
        "poll_ms": 100
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.npc.location == 'MovieTheater'",
        "params": { "name": "Claire" },
        "message": "Claire should work inside MovieTheater after theater progression is seeded"
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.npc.tile.x == 7",
        "params": { "name": "Claire" },
        "message": "Claire should be on her SVE theater schedule tile"
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.npc.tile.y == 5",
        "params": { "name": "Claire" },
        "message": "Claire should be on her SVE theater schedule tile"
      }
    },
    {
      "action": "player.warp",
      "args": { "location": "MovieTheater", "x": 7, "y": 6 }
    },
    {
      "action": "wait.location",
      "args": {
        "location": "MovieTheater",
        "x": 7,
        "y": 6,
        "timeout_ms": 10000,
        "poll_ms": 100
      }
    },
    {
      "action": "screenshot.capture_next_frame",
      "args": { "name": "theater-before-npc-click" }
    },
    {
      "action": "input.click_tile",
      "args": {
        "location": "MovieTheater",
        "button": "right",
        "x": 7,
        "y": 5
      }
    },
    {
      "action": "wait.menu",
      "args": {
        "text_matches": "recommend you some movies|Visiting me at work|home at around 9pm|seeing a movie|concessions|movies for free|Management here|wildlife adds",
        "ready": true,
        "timeout_ms": 30000,
        "poll_ms": 100
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.menu.extra.character == 'Claire'",
        "message": "Tile-clicking Claire in MovieTheater should open Claire dialogue"
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.menu.extra.dialogue_text != ''",
        "message": "Claire's MovieTheater dialogue should expose renderable text"
      }
    },
    { "action": "wait.ms", "args": { "ms": 500 } },
    {
      "action": "screenshot.capture_next_frame",
      "args": { "name": "final" }
    }
  ],
  "assertions": [
    {
      "type": "content.asset",
      "asset": "Strings/schedules/Claire",
      "asset_type": "data",
      "include_keys": true,
      "keys_limit": 25,
      "expr": "asset.keys contains 'MovieTheater.000'",
      "message": "SVE should patch Claire's MovieTheater schedule dialogue strings"
    }
  ]
}
```

- [ ] **Step 3: Run the acceptance scenario headless**

Run from `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
./scripts/sdv-test --headless --mod-set core --no-build --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-28-movie-theater tests/sdv/36-sve-movie-theater-npc-click.test.json
```

Expected fast-path outcome: `PASS sve_movie_theater_npc_click`.

If this passes, do not add Frobby production code; continue to Task 2.

If this fails because Claire never reaches `MovieTheater`, inspect the report and adjust only the SVE setup date/progression in this scenario before rerunning this same command. Keep the proof focused on Claire's SVE theater schedule.

If this fails after `input.click_tile` because no Claire dialogue menu opens, continue to Task 3, Task 4, and Task 5 before rerunning this command.

## Task 2: Fast-Path Docs And TODO Completion

Execute this task only when Task 1 Step 3 passes without Frobby production changes.

**Files:**
- Modify: `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Document the SVE scenario**

Add this paragraph to `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md` near the other numbered scenario descriptions:

```markdown
Scenario `tests/sdv/36-sve-movie-theater-npc-click.test.json` covers movie
theater NPC interaction. It seeds the vanilla theater-unlocked progression
state, waits for SVE's Claire to work inside `MovieTheater`, clicks her tile
through Frobby's neutral `input.click_tile` path, and verifies SVE theater
dialogue. This scenario intentionally avoids `world.interact_npc` so it proves
the player-like tile-click path through Stardew's special theater location.
```

- [ ] **Step 2: Mark Slice 28 done in the Frobby TODO**

In `SVE_FROBBY_CAPABILITY_TODO.md`, replace the Slice 28 block with:

```markdown
- [x] Done: Slice 28, movie theater NPC tile-click interaction.
  - SVE pressure: SVE patches `MovieTheater` so worker NPCs such as Claire and Martin can be interacted with inside a special Stardew location that normally has theater-specific click behavior.
  - Frobby goal: prove tests can set theater-ready progression, observe a modded NPC scheduled into `MovieTheater`, click the NPC through `input.click_tile`, and assert the resulting dialogue without using the direct `world.interact_npc` shortcut.
  - Design spec: `docs/superpowers/specs/2026-05-26-sve-slice-28-movie-theater-npc-interaction-design.md`.
  - Done: SVE scenario 36 seeds `ccMovieTheater` plus event `191393`, waits for Claire at `MovieTheater` tile `(7,5)`, right-clicks that tile through `input.click_tile`, and asserts Claire's SVE theater dialogue.
  - Verified: headless SVE scenario 36 passed under the `core` profile.
```

- [ ] **Step 3: Leave fast-path changes uncommitted until verification**

Run:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework status --short
```

Expected: SVE shows `docs/FROBBY.md` and scenario 36 dirty; Frobby shows `SVE_FROBBY_CAPABILITY_TODO.md` dirty. Commit these in Task 7 after the regression sweep.

## Task 3: Fallback Protocol Diagnostics For Tile-Clicked NPCs

Execute this task only if Task 1 Step 3 fails after `input.click_tile` because no Claire dialogue menu opens.

**Files:**
- Modify: `src/Protocol/Models/InputClickTileRequest.cs`
- Modify: `tests/Protocol.Tests/InputClickTileSerializationTests.cs`

- [ ] **Step 1: Write the failing protocol test**

In `tests/Protocol.Tests/InputClickTileSerializationTests.cs`, update `Result_SerializesDiagnosticsAsSnakeCase` by adding `TargetNpcName = "Claire",` to the object initializer:

```csharp
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
    TargetNpcName = "Claire",
    Handled = true,
};
```

Add this assertion before `Assert.Contains("\"handled\":true", json);`:

```csharp
Assert.Contains("\"target_npc_name\":\"Claire\"", json);
```

- [ ] **Step 2: Run the protocol test and verify it fails**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter InputClickTileSerializationTests
```

Expected: compile failure similar to `InputClickTileResult does not contain a definition for TargetNpcName`.

- [ ] **Step 3: Add the result property**

In `src/Protocol/Models/InputClickTileRequest.cs`, update `InputClickTileResult` to:

```csharp
/// <summary>Response shape for <c>input.click_tile</c>.</summary>
public sealed class InputClickTileResult : MutatorOk
{
    public string Location { get; set; } = string.Empty;
    public TilePoint Tile { get; set; } = new();
    public PixelPoint Screen { get; set; } = new();
    public PixelPoint World { get; set; } = new();
    public PlayerItemSummary? SelectedItem { get; set; }
    public string? TargetNpcName { get; set; }
    public bool Handled { get; set; }
}
```

- [ ] **Step 4: Run the protocol test and verify it passes**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter InputClickTileSerializationTests
```

Expected: `Passed! - Failed: 0`.

## Task 4: Fallback Harness Behavior For NPC-Occupied Right Clicks

Execute this task only if Task 1 Step 3 fails after `input.click_tile` because no Claire dialogue menu opens.

**Files:**
- Modify: `src/Harness/Handlers/InputClickTileHandler.cs`
- Modify: `tests/Harness.Tests/InputClickTileHandlerTests.cs`

- [ ] **Step 1: Write failing handler tests**

In `tests/Harness.Tests/InputClickTileHandlerTests.cs`, add these tests after `Handle_RightClickConvertsTileToWorldAndScreenCoordinates`:

```csharp
[Fact]
public void Handle_IncludesTargetNpcNameWhenNpcOccupiesClickedTile()
{
    var world = new FakeTileClickWorld
    {
        CurrentLocationName = "MovieTheater",
        TargetNpcName = "Claire",
    };
    var p = JsonDocument.Parse(
        "{\"location\":\"MovieTheater\",\"x\":7,\"y\":5,\"button\":\"right\"}")
        .RootElement;

    var json = InputClickTileHandler.Handle(p, world);
    var result = JsonSerializer.Deserialize<InputClickTileResult>(json, ProtocolJson.Options)!;

    Assert.Equal("Claire", result.TargetNpcName);
    Assert.Equal(7, world.NpcLookupTileX);
    Assert.Equal(5, world.NpcLookupTileY);
}

[Fact]
public void Handle_UnhandledRightClickOnNpcTileFallsBackToLocationCheckAction()
{
    var world = new FakeTileClickWorld
    {
        CurrentLocationName = "MovieTheater",
        TargetNpcName = "Claire",
        RightClickHandled = false,
        CheckActionHandled = true,
    };
    var p = JsonDocument.Parse(
        "{\"location\":\"MovieTheater\",\"x\":7,\"y\":5,\"button\":\"right\"}")
        .RootElement;

    var json = InputClickTileHandler.Handle(p, world);
    var result = JsonSerializer.Deserialize<InputClickTileResult>(json, ProtocolJson.Options)!;

    Assert.True(result.Handled);
    Assert.True(world.CheckActionInvoked);
    Assert.Equal(7, world.CheckActionTileX);
    Assert.Equal(5, world.CheckActionTileY);
}

[Fact]
public void Handle_UnhandledRightClickWithoutNpcDoesNotFallbackToLocationCheckAction()
{
    var world = new FakeTileClickWorld
    {
        CurrentLocationName = "MovieTheater",
        RightClickHandled = false,
        CheckActionHandled = true,
    };
    var p = JsonDocument.Parse(
        "{\"location\":\"MovieTheater\",\"x\":7,\"y\":5,\"button\":\"right\"}")
        .RootElement;

    var json = InputClickTileHandler.Handle(p, world);
    var result = JsonSerializer.Deserialize<InputClickTileResult>(json, ProtocolJson.Options)!;

    Assert.False(result.Handled);
    Assert.False(world.CheckActionInvoked);
}
```

Still in `FakeTileClickWorld`, add properties:

```csharp
public string? TargetNpcName { get; set; }
public int? NpcLookupTileX { get; private set; }
public int? NpcLookupTileY { get; private set; }
public bool RightClickHandled { get; set; } = true;
public bool CheckActionHandled { get; set; }
public bool CheckActionInvoked { get; private set; }
public int? CheckActionTileX { get; private set; }
public int? CheckActionTileY { get; private set; }
```

Replace `ClickRightTile` in `FakeTileClickWorld` with:

```csharp
public bool ClickRightTile(int worldX, int worldY, int screenX, int screenY)
{
    RecordClick("right", worldX, worldY, screenX, screenY);
    return RightClickHandled;
}
```

Add these methods to `FakeTileClickWorld`:

```csharp
public string? FindNpcNameAtTile(int tileX, int tileY)
{
    NpcLookupTileX = tileX;
    NpcLookupTileY = tileY;
    return TargetNpcName;
}

public bool CheckActionTile(int tileX, int tileY)
{
    CheckActionInvoked = true;
    CheckActionTileX = tileX;
    CheckActionTileY = tileY;
    return CheckActionHandled;
}
```

- [ ] **Step 2: Run the handler tests and verify they fail**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter InputClickTileHandlerTests
```

Expected: compile failures for missing `TargetNpcName`, `FindNpcNameAtTile`, and `CheckActionTile` members.

- [ ] **Step 3: Update the handler contract and response**

In `src/Harness/Handlers/InputClickTileHandler.cs`, change the click section in `Handle` from:

```csharp
var worldX = tileX * TileSize + req.ScreenOffsetX;
var worldY = tileY * TileSize + req.ScreenOffsetY;
var screenX = worldX - world.ViewportX;
var screenY = worldY - world.ViewportY;
var handled = button == "right"
    ? world.ClickRightTile(worldX, worldY, screenX, screenY)
    : world.ClickLeftTile(worldX, worldY, screenX, screenY);
```

to:

```csharp
var targetNpcName = world.FindNpcNameAtTile(tileX, tileY);
var worldX = tileX * TileSize + req.ScreenOffsetX;
var worldY = tileY * TileSize + req.ScreenOffsetY;
var screenX = worldX - world.ViewportX;
var screenY = worldY - world.ViewportY;
var handled = button == "right"
    ? world.ClickRightTile(worldX, worldY, screenX, screenY)
    : world.ClickLeftTile(worldX, worldY, screenX, screenY);
if (!handled && button == "right" && !string.IsNullOrWhiteSpace(targetNpcName))
    handled = world.CheckActionTile(tileX, tileY);
```

In the `InputClickTileResult` initializer, add:

```csharp
TargetNpcName = targetNpcName,
```

Update `IInputTileClickWorld` by adding:

```csharp
string? FindNpcNameAtTile(int tileX, int tileY);
bool CheckActionTile(int tileX, int tileY);
```

- [ ] **Step 4: Implement neutral production behavior**

In `SdvInputTileClickWorld`, add:

```csharp
public string? FindNpcNameAtTile(int tileX, int tileY)
{
    var tileRect = new Rectangle(tileX * TileSize, tileY * TileSize, TileSize, TileSize);
    return CurrentLocation.characters
        .FirstOrDefault(npc => npc is not null && npc.GetBoundingBox().Intersects(tileRect))
        ?.Name;
}

public bool CheckActionTile(int tileX, int tileY)
    => CurrentLocation.checkAction(
        new xTile.Dimensions.Location(tileX, tileY),
        Game1.viewport,
        Game1.player);
```

This file already imports `System` and `Microsoft.Xna.Framework`; ensure `System.Linq` remains imported or add:

```csharp
using System.Linq;
```

- [ ] **Step 5: Run the handler tests and verify they pass**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter InputClickTileHandlerTests
```

Expected: `Passed! - Failed: 0`.

## Task 5: Fallback Docs For Input Click Diagnostics

Execute this task only if Task 3 and Task 4 were needed.

**Files:**
- Modify: `docs/rpc-schema.md`

- [ ] **Step 1: Document `target_npc_name` and fallback behavior**

In `docs/rpc-schema.md`, find the `input.click_tile` section and update the response example to include:

```json
"target_npc_name": "Claire",
```

Add this paragraph after the response-field explanation:

```markdown
`target_npc_name` is present when a social NPC's bounding box intersects the
clicked tile before the click is delivered. If a right-click on an NPC-occupied
tile is not handled by Stardew's high-level action-button path, Frobby falls
back to the current location's native `checkAction(tile, viewport, player)`
method. This fallback is generic and exists for special locations whose normal
tile action path owns NPC interactions.
```

- [ ] **Step 2: Run focused documentation-adjacent tests**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter InputClickTileSerializationTests
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter InputClickTileHandlerTests
```

Expected: both commands exit 0.

- [ ] **Step 3: Commit fallback Frobby code**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework add src/Protocol/Models/InputClickTileRequest.cs src/Harness/Handlers/InputClickTileHandler.cs tests/Protocol.Tests/InputClickTileSerializationTests.cs tests/Harness.Tests/InputClickTileHandlerTests.cs docs/rpc-schema.md
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework commit -m "feat: harden tile clicks on NPC tiles"
```

Expected: one Frobby commit containing only neutral `input.click_tile` diagnostics/fallback changes.

## Task 6: Live Verification And Regression Sweep

**Files:**
- No new files unless Task 2 docs were not already applied.

- [ ] **Step 1: Re-run Slice 28 scenario**

Run from `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
./scripts/sdv-test --headless --mod-set core --no-build --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-28-movie-theater-final tests/sdv/36-sve-movie-theater-npc-click.test.json
```

Expected: `PASS sve_movie_theater_npc_click`.

- [ ] **Step 2: Re-run ordinary NPC regression**

Run:

```bash
./scripts/sdv-test --headless --mod-set core --no-build --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-28-regression-05 tests/sdv/05-sve-npc-schedules-dialogue-relationships.test.json
```

Expected: `PASS sve_npc_schedules_dialogue_relationships`.

- [ ] **Step 3: Re-run festival actor regression**

Run:

```bash
./scripts/sdv-test --headless --mod-set core --no-build --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-28-regression-32 tests/sdv/32-sve-spirit-eve-actor-dialogue.test.json
```

Expected: `PASS sve_spirit_eve_actor_dialogue`.

- [ ] **Step 4: Run Frobby focused tests if fallback code was added**

Skip this step if Task 3 through Task 5 were not executed. Otherwise run from `/home/fintan/stardewRepos/frobby/sdv-test-framework`:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter InputClickTileSerializationTests
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter InputClickTileHandlerTests
```

Expected: both commands exit 0.

## Task 7: Final Docs, Completion Mark, And Commits

**Files:**
- Modify: `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Ensure SVE docs include scenario 36**

If Task 2 was skipped because fallback code was needed first, add this paragraph now to `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`:

```markdown
Scenario `tests/sdv/36-sve-movie-theater-npc-click.test.json` covers movie
theater NPC interaction. It seeds the vanilla theater-unlocked progression
state, waits for SVE's Claire to work inside `MovieTheater`, clicks her tile
through Frobby's neutral `input.click_tile` path, and verifies SVE theater
dialogue. This scenario intentionally avoids `world.interact_npc` so it proves
the player-like tile-click path through Stardew's special theater location.
```

- [ ] **Step 2: Ensure the Frobby TODO marks Slice 28 complete**

If Task 2 was skipped because fallback code was needed first, replace the Slice 28 block in `SVE_FROBBY_CAPABILITY_TODO.md` with:

```markdown
- [x] Done: Slice 28, movie theater NPC tile-click interaction.
  - SVE pressure: SVE patches `MovieTheater` so worker NPCs such as Claire and Martin can be interacted with inside a special Stardew location that normally has theater-specific click behavior.
  - Frobby goal: prove tests can set theater-ready progression, observe a modded NPC scheduled into `MovieTheater`, click the NPC through `input.click_tile`, and assert the resulting dialogue without using the direct `world.interact_npc` shortcut.
  - Design spec: `docs/superpowers/specs/2026-05-26-sve-slice-28-movie-theater-npc-interaction-design.md`.
  - Done: SVE scenario 36 seeds `ccMovieTheater` plus event `191393`, waits for Claire at `MovieTheater` tile `(7,5)`, right-clicks that tile through `input.click_tile`, and asserts Claire's SVE theater dialogue.
  - Verified: headless SVE scenario 36 passed under the `core` profile.
```

- [ ] **Step 3: Commit SVE test/docs**

Run:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded add docs/FROBBY.md tests/sdv/36-sve-movie-theater-npc-click.test.json
git -C /home/fintan/stardewRepos/StardewValleyExpanded commit -m "test: cover movie theater NPC tile click"
```

Expected: one SVE commit containing scenario 36 and its docs.

- [ ] **Step 4: Commit Frobby TODO/docs if still dirty**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework status --short
```

If only `SVE_FROBBY_CAPABILITY_TODO.md` is dirty, run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework add SVE_FROBBY_CAPABILITY_TODO.md
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework commit -m "docs: complete movie theater NPC slice"
```

If `docs/rpc-schema.md` is also dirty from Task 5, include it:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework add SVE_FROBBY_CAPABILITY_TODO.md docs/rpc-schema.md
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework commit -m "docs: complete movie theater NPC slice"
```

Expected: Frobby branch ends clean.

- [ ] **Step 5: Final status check**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
```

Expected: both working trees are clean on their Slice 28 feature branches. Do not merge SVE into `master` unless the user explicitly asks.

## Self-Review

Spec coverage:

- Theater progression setup is covered by Task 1 Step 2.
- Claire `MovieTheater` schedule observation is covered by Task 1 Step 2 and Task 6 Step 1.
- Player-like tile-click interaction is covered by Task 1 Step 2; the scenario does not call `world.interact_npc`.
- Neutral Frobby fallback is covered by Task 3 through Task 5 and is only executed if the live scenario exposes a gap.
- SVE and regression verification are covered by Task 6.
- TODO/docs completion is covered by Task 2 and Task 7.

Placeholder scan:

- No placeholder implementation steps are intentional. Conditional tasks have explicit trigger conditions, exact file paths, code snippets, commands, and expected outcomes.

Type consistency:

- `InputClickTileResult.TargetNpcName` serializes to `target_npc_name` through existing `ProtocolJson` snake_case options.
- `IInputTileClickWorld.FindNpcNameAtTile` and `CheckActionTile` are both introduced in tests before production implementation.
- `FakeTileClickWorld` properties match the asserted test names.
