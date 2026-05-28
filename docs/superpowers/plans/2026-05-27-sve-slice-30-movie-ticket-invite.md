# SVE Slice 30 Movie Ticket Invite Flow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add headless SVE coverage for inviting a custom NPC to the movies with a selected movie ticket, hardening Frobby's neutral selected-item tile-click path only where needed.

**Architecture:** SVE scenario 38 drives the player-visible flow with existing neutral actions: seed theater progression, give and select `(O)809`, click Sophia's NPC tile, observe Sophia's acceptance dialogue, acknowledge it, and assert the ticket leaves inventory. Frobby stays mod-neutral by changing only selected-item tile-click and data-asset behavior: selected inventory objects keep Stardew's native target-NPC result, non-target dialogue can be cleared before NPC fallback, and keyed list-style Stardew data assets can be summarized.

**Tech Stack:** C#/.NET, xUnit, System.Text.Json snake-case protocol serialization, Frobby JSON-RPC harness, Frobby runner JSON scenarios, SMAPI/Stardew Valley runtime APIs, Stardew Valley Expanded repo-local `scripts/sdv-test --headless`.

---

## File Map

Frobby repo: `/home/fintan/stardewRepos/frobby/sdv-test-framework`

- Modify `tests/Harness.Tests/InputClickTileHandlerTests.cs`
  - Adds selected-object and selected-tool NPC click regression tests.
  - Adjusts existing generic NPC fallback tests so they represent ordinary no-selected-object interaction.
- Modify `src/Harness/Handlers/InputClickTileHandler.cs`
  - Suppresses generic NPC fallback when the selected item is a real inventory object such as a movie ticket or gift.
  - Keeps generic fallback for no selected item and selected tools.
- Modify `README.md`
  - Documents the selected item plus `input.click_tile` pattern.
- Modify `docs/rpc-schema.md`
  - Documents `input.click_tile` selected-object fallback semantics.
- Modify `docs/wiki/examples.md`
  - Adds scenario 38 to the SVE examples list.
- Modify `SVE_FROBBY_CAPABILITY_TODO.md`
  - Marks Slice 30 done after verification.

SVE repo: `/home/fintan/stardewRepos/StardewValleyExpanded`

- Create `tests/sdv/38-sve-movie-ticket-invite-flow.test.json`
  - Live SVE proof scenario for Sophia movie-ticket invitation.
- Modify `docs/FROBBY.md`
  - Adds scenario 38 to SVE's local Frobby scenario guide.

Do not merge the SVE feature branch into `master`. Frobby can merge to `main` only after the user explicitly approves that integration step.

---

## Task 1: Branch And Add The SVE Red Scenario

**Files:**
- Create: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/38-sve-movie-ticket-invite-flow.test.json`

- [ ] **Step 1: Confirm clean worktrees**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
```

Expected: no unstaged or uncommitted changes. Frobby may be on `feature/sve-slice-29-grange-judging`; SVE must not be on `master`.

- [ ] **Step 2: Create slice branches**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework switch -c feature/sve-slice-30-movie-ticket-invite
git -C /home/fintan/stardewRepos/StardewValleyExpanded switch -c feature/frobby-sve-slice-30-movie-ticket-invite
```

Expected: both commands switch to new feature branches.

- [ ] **Step 3: Create the SVE scenario**

Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/38-sve-movie-ticket-invite-flow.test.json`:

```json
{
  "name": "sve_movie_ticket_invite_flow",
  "fixture": "m0spike_436515781",
  "config": { "seed": 42 },
  "steps": [
    { "action": "time.set", "args": { "time": 900, "day": 3, "season": "spring", "year": 1 } },
    { "action": "world.set_weather", "args": { "type": "sun" } },
    { "action": "player.add_mail", "args": { "id": "ccMovieTheater" } },
    { "action": "player.add_event_seen", "args": { "id": "191393" } },
    { "action": "player.add_event_seen", "args": { "id": "015305930" } },
    {
      "action": "player.set_friendship",
      "args": {
        "npc": "Sophia",
        "points": 2000,
        "talked_to_today": false,
        "gifts_today": 0,
        "gifts_this_week": 0
      }
    },
    {
      "action": "player.warp",
      "args": { "location": "Custom_SophiaHouse", "x": 18, "y": 11 }
    },
    {
      "action": "wait.location",
      "args": {
        "location": "Custom_SophiaHouse",
        "x": 18,
        "y": 11,
        "timeout_ms": 10000,
        "poll_ms": 100
      }
    },
    {
      "action": "world.warp_npc",
      "args": {
        "name": "Sophia",
        "location": "Custom_SophiaHouse",
        "x": 18,
        "y": 10
      }
    },
    {
      "action": "wait.npc_location",
      "args": {
        "name": "Sophia",
        "location": "Custom_SophiaHouse",
        "x": 18,
        "y": 10,
        "timeout_ms": 10000,
        "poll_ms": 100
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.npc.can_socialize == true",
        "params": { "name": "Sophia" },
        "message": "Sophia should be available for social interaction before the ticket invite"
      }
    },
    { "action": "player.give_item", "args": { "id": "(O)809", "count": 1 } },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.player.items contains qualified_id '(O)809'",
        "message": "The player should hold a movie ticket before inviting Sophia"
      }
    },
    { "action": "player.select_item", "args": { "id": "(O)809", "prefer_hotbar": true } },
    { "action": "wait.ms", "args": { "ms": 500 } },
    {
      "action": "screenshot.capture_next_frame",
      "args": { "name": "before-ticket-click" }
    },
    {
      "action": "input.click_tile",
      "args": {
        "location": "Custom_SophiaHouse",
        "button": "right",
        "x": 18,
        "y": 10
      }
    },
    {
      "action": "wait.menu",
      "args": {
        "text_matches": "movie|Movie|theater|Theater|ticket|Ticket|Sophia",
        "ready": true,
        "timeout_ms": 30000,
        "poll_ms": 100
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.menu.present == true",
        "message": "Using a selected movie ticket on Sophia should open the invite dialogue"
      }
    },
    {
      "action": "screenshot.capture_next_frame",
      "args": { "name": "invite-prompt" }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.menu.extra.character == 'Sophia'",
        "message": "Using a selected movie ticket on Sophia should open Sophia's acceptance dialogue"
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.menu.extra.dialogue_text != ''",
        "message": "Sophia's movie-ticket acceptance dialogue should expose renderable text"
      }
    },
    {
      "action": "ui.acknowledge",
      "args": { "until_closed": true, "max_clicks": 3, "timeout_ms": 10000, "poll_ms": 100, "interval_ms": 100 }
    },
    { "action": "wait.ms", "args": { "ms": 1000 } },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.player.items not contains qualified_id '(O)809'",
        "message": "Accepting Sophia's movie invite should consume the selected movie ticket"
      }
    },
    {
      "action": "screenshot.capture_next_frame",
      "args": { "name": "final" }
    }
  ],
  "assertions": [
    {
      "type": "content.asset",
      "asset": "Data/MoviesReactions",
      "asset_type": "data",
      "entry_keys": ["Sophia"],
      "expr": "asset.entries.Sophia.exists == true",
      "message": "SVE should add Sophia movie reaction data"
    },
    {
      "type": "content.asset",
      "asset": "Data/ConcessionTastes",
      "asset_type": "data",
      "entry_keys": ["Sophia"],
      "expr": "asset.entries.Sophia.exists == true",
      "message": "SVE should add Sophia concession taste data"
    }
  ]
}
```

- [ ] **Step 4: Run the scenario before the Frobby fix**

Run:

```bash
dotnet run --project /home/fintan/stardewRepos/frobby/sdv-test-framework/src/Runner/Runner.csproj -- repo run --repo-root /home/fintan/stardewRepos/StardewValleyExpanded --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-30-red /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/38-sve-movie-ticket-invite-flow.test.json
```

Expected before Task 3: FAIL is acceptable if the selected-ticket click is masked by ordinary dialogue fallback or the ticket is not consumed. The report should show `before-ticket-click` and either `invite-prompt` or the failing step screenshot. If it passes already, keep the scenario and still complete Tasks 2-6 because the unit-level fallback hardening protects other selected-object NPC interactions.

- [ ] **Step 5: Commit only the red SVE scenario**

Run:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded add tests/sdv/38-sve-movie-ticket-invite-flow.test.json
git -C /home/fintan/stardewRepos/StardewValleyExpanded commit -m "test: cover SVE movie ticket invite flow"
```

Expected: commit succeeds on `feature/frobby-sve-slice-30-movie-ticket-invite`.

---

## Task 2: Add Failing Frobby Handler Tests

**Files:**
- Modify: `/home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Harness.Tests/InputClickTileHandlerTests.cs`

- [ ] **Step 1: Adjust existing generic fallback tests**

In `Handle_RightClickOnNpcWithBlankDialogueMenu_UsesNpcFallback`, change the fake world setup to:

```csharp
var world = new FakeTileClickWorld
{
    TargetNpcName = "Claire",
    HasBlankDialogueMenuAfterClick = true,
    SelectedItem = null,
};
```

In `Handle_RightClickOnNpcWithNoMenuAfterHandledClick_UsesNpcFallback`, change the fake world setup to:

```csharp
var world = new FakeTileClickWorld
{
    TargetNpcName = "Claire",
    HasActiveMenu = false,
    SelectedItem = null,
};
```

- [ ] **Step 2: Add selected-object and selected-tool tests**

Add these tests after `Handle_RightClickOnNpcWithNoMenuAfterHandledClick_UsesNpcFallback`:

```csharp
[Fact]
public void Handle_RightClickOnNpcWithSelectedObject_DoesNotUseNpcFallback()
{
    var world = new FakeTileClickWorld
    {
        TargetNpcName = "Sophia",
        HasBlankDialogueMenuAfterClick = true,
        SelectedItem = new SelectableInventoryItem(2, "(O)809", "809", "Movie Ticket", 1, 0, 0, "Object"),
    };
    var p = JsonDocument.Parse("{\"x\":18,\"y\":10,\"button\":\"right\"}").RootElement;

    var json = InputClickTileHandler.Handle(p, world);
    var result = JsonSerializer.Deserialize<InputClickTileResult>(json, ProtocolJson.Options)!;

    Assert.True(world.ClickInvoked);
    Assert.False(world.NpcFallbackInvoked);
    Assert.Equal("Sophia", result.TargetNpcName);
    Assert.False(result.NpcFallbackUsed);
    Assert.True(result.Handled);
    Assert.Equal("(O)809", result.SelectedItem!.QualifiedId);
}

[Fact]
public void Handle_RightClickOnNpcWithSelectedTool_StillUsesNpcFallbackForBlankDialogue()
{
    var world = new FakeTileClickWorld
    {
        TargetNpcName = "Claire",
        HasBlankDialogueMenuAfterClick = true,
        SelectedItem = new SelectableInventoryItem(0, "(T)Hoe", "Hoe", "Hoe", 1, null, null, "Hoe"),
    };
    var p = JsonDocument.Parse("{\"x\":7,\"y\":5,\"button\":\"right\"}").RootElement;

    var json = InputClickTileHandler.Handle(p, world);
    var result = JsonSerializer.Deserialize<InputClickTileResult>(json, ProtocolJson.Options)!;

    Assert.True(world.NpcFallbackInvoked);
    Assert.Equal("Claire", result.TargetNpcName);
    Assert.True(result.NpcFallbackUsed);
    Assert.True(result.Handled);
    Assert.Equal("(T)Hoe", result.SelectedItem!.QualifiedId);
}
```

- [ ] **Step 3: Run the focused failing test**

Run:

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Harness.Tests/ --filter "FullyQualifiedName~InputClickTileHandlerTests.Handle_RightClickOnNpcWithSelectedObject_DoesNotUseNpcFallback" --nologo
```

Expected before Task 3: FAIL because `world.NpcFallbackInvoked` is true.

---

## Task 3: Harden `input.click_tile` Selected-Object Fallback

**Files:**
- Modify: `/home/fintan/stardewRepos/frobby/sdv-test-framework/src/Harness/Handlers/InputClickTileHandler.cs`

- [ ] **Step 1: Cache selected item before clicking**

In `InputClickTileHandler.Handle`, replace the target/click/fallback block with:

```csharp
var targetNpcName = button == "right" ? world.FindNpcAtTile(tileX, tileY) : null;
var selectedItem = world.SelectedItem;
var handled = button == "right"
    ? world.ClickRightTile(worldX, worldY, screenX, screenY)
    : world.ClickLeftTile(worldX, worldY, screenX, screenY);
var npcFallbackUsed = false;
if (ShouldUseNpcFallback(button, targetNpcName, handled, world, selectedItem))
{
    npcFallbackUsed = world.InteractNpcAtTile(tileX, tileY);
    handled = handled || npcFallbackUsed;
}
```

In the returned `InputClickTileResult`, replace the `SelectedItem = ...` initializer with:

```csharp
SelectedItem = selectedItem is { } selected
    ? PlayerSelectItemHandler.ToSummary(selected)
    : null,
```

- [ ] **Step 2: Add fallback helper methods**

Add these private methods before `NormalizeButton`:

```csharp
private static bool ShouldUseNpcFallback(
    string button,
    string? targetNpcName,
    bool handled,
    IInputTileClickWorld world,
    ISelectableInventoryItem? selectedItem)
{
    if (button != "right" || targetNpcName is null)
        return false;

    if (IsSelectedInventoryObject(selectedItem))
        return false;

    return !handled || !world.HasActiveMenu || world.HasBlankDialogueMenu;
}

private static bool IsSelectedInventoryObject(ISelectableInventoryItem? selectedItem)
{
    if (selectedItem is null)
        return false;

    return string.Equals(selectedItem.RuntimeType, "Object", StringComparison.Ordinal)
        || selectedItem.QualifiedId.StartsWith("(O)", StringComparison.Ordinal);
}
```

This keeps the fallback available for no selected item and selected tools, while selected objects such as movie tickets, gifts, bombs, and modded objects keep Stardew's native action result visible to the scenario.

- [ ] **Step 3: Run the focused tests**

Run:

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Harness.Tests/ --filter "FullyQualifiedName~InputClickTileHandlerTests" --nologo
```

Expected: PASS for all `InputClickTileHandlerTests`.

- [ ] **Step 4: Commit the Frobby handler fix**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework add src/Harness/Handlers/InputClickTileHandler.cs tests/Harness.Tests/InputClickTileHandlerTests.cs
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework commit -m "fix: preserve selected object NPC tile clicks"
```

Expected: commit succeeds on `feature/sve-slice-30-movie-ticket-invite`.

---

## Task 4: Verify The Live SVE Flow And Adjacent Regression

**Files:**
- Verify: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/38-sve-movie-ticket-invite-flow.test.json`
- Verify: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/36-sve-movie-theater-npc-click.test.json`

- [ ] **Step 1: Build the runner**

Run:

```bash
dotnet build /home/fintan/stardewRepos/frobby/sdv-test-framework/src/Runner/Runner.csproj --nologo
```

Expected: build succeeds with 0 errors.

- [ ] **Step 2: Run scenario 38 headless**

Run:

```bash
dotnet run --project /home/fintan/stardewRepos/frobby/sdv-test-framework/src/Runner/Runner.csproj -- repo run --repo-root /home/fintan/stardewRepos/StardewValleyExpanded --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-30-movie-ticket /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/38-sve-movie-ticket-invite-flow.test.json
```

Expected: PASS. The report should contain `before-ticket-click`, `invite-prompt`, and `final` screenshots.

- [ ] **Step 3: Run scenario 36 regression headless**

Run:

```bash
dotnet run --project /home/fintan/stardewRepos/frobby/sdv-test-framework/src/Runner/Runner.csproj -- repo run --repo-root /home/fintan/stardewRepos/StardewValleyExpanded --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-30-theater-regression /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/36-sve-movie-theater-npc-click.test.json
```

Expected: PASS. This confirms the selected-object fallback change did not break ordinary theater NPC dialogue.

- [ ] **Step 4: Run a click-placement regression headless**

Run:

```bash
dotnet run --project /home/fintan/stardewRepos/frobby/sdv-test-framework/src/Runner/Runner.csproj -- repo run --repo-root /home/fintan/stardewRepos/StardewValleyExpanded --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-30-click-regression /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/31-sve-combat-lab-click-bomb-mummy.test.json
```

Expected: PASS. This confirms selected-object tile clicking still works for non-NPC gameplay tiles.

If scenario 38 still cannot observe a target-NPC acceptance dialogue or ticket-consumption state after Task 3, do not add SVE-specific code. Save the failing report path and write a follow-up design for a neutral `state.movie_theater` projector that exposes Stardew's live invited-patron state.

---

## Task 5: Update Docs And Mark Slice 30 Done

**Files:**
- Modify: `/home/fintan/stardewRepos/frobby/sdv-test-framework/README.md`
- Modify: `/home/fintan/stardewRepos/frobby/sdv-test-framework/docs/rpc-schema.md`
- Modify: `/home/fintan/stardewRepos/frobby/sdv-test-framework/docs/wiki/examples.md`
- Modify: `/home/fintan/stardewRepos/frobby/sdv-test-framework/SVE_FROBBY_CAPABILITY_TODO.md`
- Modify: `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`

- [ ] **Step 1: Update Frobby README**

In `README.md`, extend the existing selected-item or NPC-click guidance with:

````markdown
For player-like item-on-NPC flows, give/select the real item first and then
click the NPC's tile:

```json
{ "action": "player.give_item", "args": { "id": "(O)809", "count": 1 } }
{ "action": "player.select_item", "args": { "id": "(O)809" } }
{ "action": "input.click_tile", "args": { "location": "Custom_SophiaHouse", "button": "right", "x": 18, "y": 10 } }
```

When the selected item is an inventory object, `input.click_tile` preserves
Stardew's native object-on-NPC behavior instead of replacing a missing or blank
result with generic dialogue fallback. This keeps tests honest for tickets,
gifts, bombs, and modded objects.
````

- [ ] **Step 2: Update RPC schema**

In `docs/rpc-schema.md`, under `### input.click_tile`, add:

```markdown
Selected-object NPC clicks keep the native Stardew action result. If the player
has an inventory object selected, such as a movie ticket or gift, Frobby reports
the selected item in the result and does not synthesize generic NPC dialogue
fallback. Generic fallback remains available for ordinary right-clicks with no
selected object and for selected tools, which keeps dialogue tests stable while
allowing item-on-NPC tests to fail visibly when the native path fails.
```

- [ ] **Step 3: Update Frobby wiki examples**

In `docs/wiki/examples.md`, add this entry near scenario 36:

```markdown
- SVE movie ticket invite flow:
  `tests/sdv/38-sve-movie-ticket-invite-flow.test.json` gives the farmer a
  vanilla movie ticket, selects it, right-clicks Sophia's NPC tile, asserts the
  target-NPC acceptance dialogue, acknowledges it, and verifies the
  ticket is consumed.
```

- [ ] **Step 4: Mark Slice 30 done in the capability backlog**

In `SVE_FROBBY_CAPABILITY_TODO.md`, replace the Slice 30 planning line with:

```markdown
- [x] Done: Slice 30, movie ticket invite flow.
  - Design spec: `docs/superpowers/specs/2026-05-27-sve-slice-30-movie-ticket-invite-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-27-sve-slice-30-movie-ticket-invite.md`.
  - SVE pressure: movie theater support needs selected-item NPC interactions, custom NPC movie reaction data, and player-visible invite dialogue.
  - Frobby goal: prove selected inventory objects can be used through `input.click_tile` on an NPC without generic dialogue fallback masking native ticket/gift behavior.
  - Done: selected-object NPC tile-click fallback hardening plus SVE scenario 38 against Sophia's movie ticket invite.
  - Verified: headless SVE scenario 38 passed, with scenarios 36 and 31 passing as adjacent regressions.
```

- [ ] **Step 5: Update SVE Frobby docs**

In `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`, add this entry after scenario 36:

```markdown
Scenario `tests/sdv/38-sve-movie-ticket-invite-flow.test.json` covers the
movie-ticket invite path for a custom SVE NPC. It seeds theater progression,
warps Sophia into a stable reachable tile, gives and selects a vanilla movie
ticket, clicks Sophia through Frobby's neutral `input.click_tile` action, waits
for Sophia's acceptance dialogue, acknowledges it, and verifies the ticket is
consumed. The scenario also asserts SVE's runtime movie reaction and concession
taste data for Sophia.
```

- [ ] **Step 6: Commit Frobby docs and backlog**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework add README.md docs/rpc-schema.md docs/wiki/examples.md SVE_FROBBY_CAPABILITY_TODO.md
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework commit -m "docs: document selected item NPC click testing"
```

Expected: commit succeeds.

- [ ] **Step 7: Commit SVE docs**

Run:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded add docs/FROBBY.md
git -C /home/fintan/stardewRepos/StardewValleyExpanded commit -m "docs: document SVE movie ticket invite scenario"
```

Expected: commit succeeds.

---

## Task 6: Final Verification And Status

**Files:**
- Verify Frobby and SVE working trees.

- [ ] **Step 1: Run final Frobby tests**

Run:

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Harness.Tests/ --filter "FullyQualifiedName~InputClickTileHandlerTests" --nologo
dotnet build /home/fintan/stardewRepos/frobby/sdv-test-framework/src/Runner/Runner.csproj --nologo
```

Expected: both commands pass.

- [ ] **Step 2: Run final live SVE verification**

Run:

```bash
dotnet run --project /home/fintan/stardewRepos/frobby/sdv-test-framework/src/Runner/Runner.csproj -- repo run --repo-root /home/fintan/stardewRepos/StardewValleyExpanded --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-30-final /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/38-sve-movie-ticket-invite-flow.test.json
dotnet run --project /home/fintan/stardewRepos/frobby/sdv-test-framework/src/Runner/Runner.csproj -- repo run --repo-root /home/fintan/stardewRepos/StardewValleyExpanded --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-30-final-regressions /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/36-sve-movie-theater-npc-click.test.json /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/31-sve-combat-lab-click-bomb-mummy.test.json
```

Expected: all listed scenarios pass.

- [ ] **Step 3: Confirm clean status**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
```

Expected: both worktrees are clean. Frobby is on `feature/sve-slice-30-movie-ticket-invite`; SVE is on `feature/frobby-sve-slice-30-movie-ticket-invite`.

- [ ] **Step 4: Report verification evidence**

Final implementation report should include:

```text
Frobby commits:
- <hash> fix: preserve selected object NPC tile clicks
- <hash> docs: document selected item NPC click testing

SVE commits:
- <hash> test: cover SVE movie ticket invite flow
- <hash> docs: document SVE movie ticket invite scenario

Verification:
- dotnet test tests/Harness.Tests --filter InputClickTileHandlerTests: PASS
- dotnet build src/Runner/Runner.csproj: PASS
- SVE scenario 38 headless: PASS, report /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-30-final
- SVE scenarios 36 and 31 headless: PASS, report /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-30-final-regressions
```

Do not merge SVE to `master`. Ask before merging Frobby to `main`.

---

## Self-Review

Spec coverage:

- Uses Sophia as the first custom-NPC movie invite target.
- Seeds movie theater progression with the same mail/event flags used by scenario 36.
- Uses real item flow: `player.give_item`, `player.select_item`, and `input.click_tile`.
- Keeps Frobby production code neutral: the handler change knows only selected object vs tool, not SVE, Sophia, or movie tickets.
- Adds SVE content assertions for movie reactions and concession tastes.
- Verifies adjacent theater NPC click and selected-object tile-click regression scenarios.
- Updates Frobby docs, SVE docs, and the capability backlog after verification.

Placeholder scan:

- No open implementation placeholders remain.
- The only stop condition is explicit: if live Stardew does not expose an observable target-NPC acceptance dialogue or ticket-consumption state, save the report and design a neutral `state.movie_theater` projector instead of adding SVE-specific shortcuts.

Type consistency:

- `ISelectableInventoryItem.RuntimeType` and `QualifiedId` already exist in `PlayerSelectItemHandler.cs`.
- `InputClickTileResult.SelectedItem` already uses `PlayerSelectItemHandler.ToSummary`.
- `ui.acknowledge` already routes through the same menu-choice path as `event.advance`.
- The scenario uses existing actions and assertions already proven in SVE scenarios 05, 11, 31, and 36.
