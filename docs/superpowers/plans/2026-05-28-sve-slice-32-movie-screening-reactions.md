# SVE Slice 32 Movie Screening Reactions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add headless SVE coverage for the full movie-theater screening path by inviting Sophia, entering the screening, and validating that SVE movie reaction content appears during the runtime event.

**Architecture:** Frobby gets two neutral improvements needed by any mod with scripted map actions and cutscene dialogue: action-value tile clicks report which map action they resolved, and `wait.event_active` can filter on root event dialogue in addition to actor dialogue. The SVE scenario consumes those generic capabilities to validate content data plus the live movie-screening reaction flow without adding SVE-specific Frobby APIs.

**Tech Stack:** C#/.NET, xUnit, System.Text.Json snake-case protocol serialization, Frobby JSON-RPC harness, Frobby runner JSON scenarios, SMAPI/Stardew Valley runtime APIs, Stardew Valley Expanded repo-local `scripts/sdv-test --headless`.

---

## File Map

Frobby repo worktree: `/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-32-movie-screening`

- Modify `src/Protocol/Models/InputClickTileRequest.cs`
  - Adds neutral result diagnostics for action-value tile clicks: resolved action value, layer, property, tile, and viewport visibility.
- Modify `src/Harness/Handlers/InputClickTileHandler.cs`
  - Carries the selected action-tile candidate through resolution and includes viewport visibility in the result.
- Modify `tests/Harness.Tests/InputClickTileHandlerTests.cs`
  - Adds RED/GREEN coverage for action resolution diagnostics and off-screen detection.
- Modify `tests/Protocol.Tests/InputClickTileSerializationTests.cs`
  - Verifies the new result fields serialize as snake_case.
- Modify `src/Runner/Scenarios/ScenarioRunner.cs`
  - Adds root dialogue filters for `wait.event_active` and readable runner output for action-click diagnostics.
- Modify `tests/Runner.Tests/ScenarioRunnerTests.cs`
  - Adds RED/GREEN coverage for root event dialogue text, regex, speaker filters, and timeout diagnostics.
- Modify `README.md`
  - Documents action-click diagnostics and root event dialogue wait filters.
- Modify `docs/rpc-schema.md`
  - Documents the new `input.click_tile` result fields.
- Modify `docs/wiki/examples.md`
  - Adds scenario 40 to the curated SVE examples once the live scenario passes.
- Modify `SVE_FROBBY_CAPABILITY_TODO.md`
  - Marks Slice 32 complete after verification.

SVE repo: `/home/fintan/stardewRepos/StardewValleyExpanded`

- Create `tests/sdv/40-sve-movie-screening-reaction-flow.test.json`
  - Live SVE proof scenario for Sophia invite, theater entry, screening event, and SVE movie reaction dialogue.
- Modify `docs/FROBBY.md`
  - Adds scenario 40 to SVE's local Frobby scenario guide.

Do not merge the SVE feature branch into `master`. Frobby can merge to `main` only after the user explicitly approves that integration step.

---

## Task 1: Confirm Workspace And Baseline

**Files:**
- Read: `/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-32-movie-screening/docs/superpowers/specs/2026-05-28-sve-slice-32-movie-screening-reactions-design.md`
- Read: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/38-sve-movie-ticket-invite-flow.test.json`
- Read: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/39-sve-movie-concession-purchase-flow.test.json`

- [ ] **Step 1: Confirm branch state**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-32-movie-screening status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
```

Expected:

```text
## feature/sve-slice-32-movie-screening
## feature/frobby-sve-slice-32-movie-screening
```

If the Frobby output includes only this plan file after Task 1, continue. If SVE has unrelated dirty files, stop and inspect them before writing tests.

- [ ] **Step 2: Confirm Frobby baseline**

Run from the Frobby Slice 32 worktree:

```bash
dotnet test --nologo
```

Expected: all Frobby test projects pass with the current baseline counts:

```text
Protocol.Tests: 167 passed
Harness.Tests: 695 passed, 50 skipped
Runner.Dsl.Tests: 59 passed, 3 skipped
Runner.Mcp.Tests: 51 passed, 1 skipped
Runner.Tests: 398 passed, 6 skipped
```

- [ ] **Step 3: Keep the SVE content target in view**

Use the already-researched SVE content as the live scenario's expected mod behavior:

```text
[CP] Stardew Valley Expanded/code/NPCs/Sophia.json
Data/MoviesReactions -> Sophia -> SpecialResponses -> summer_movie_0
BeforeMovie: Sophia.Movies.01
DuringMovie: Sophia.Movies.02
AfterMovie: Sophia.Movies.03
```

Use Summer for the scenario date so the movie-specific assertions can target the Prairie King/cosplay/movie-reaction path. Do not add a Frobby primitive like `movie.start`; this slice proves the generic event and click tooling.

---

## Task 2: TDD `input.click_tile` Action Resolution Diagnostics

**Files:**
- Modify: `/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-32-movie-screening/tests/Harness.Tests/InputClickTileHandlerTests.cs`
- Modify: `/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-32-movie-screening/src/Protocol/Models/InputClickTileRequest.cs`
- Modify: `/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-32-movie-screening/src/Harness/Handlers/InputClickTileHandler.cs`

- [ ] **Step 1: Write the failing diagnostics assertions**

In `tests/Harness.Tests/InputClickTileHandlerTests.cs`, extend `Handle_ActionValue_ClicksNearestMatchingTileWithinRadius` with these assertions after the existing `Handled` assertion:

```csharp
Assert.Equal("Concessions", result.ResolvedActionValue);
Assert.Equal("Buildings", result.ResolvedActionLayer);
Assert.Equal("Action", result.ResolvedActionProperty);
Assert.NotNull(result.ResolvedActionTile);
Assert.Equal(8, result.ResolvedActionTile!.X);
Assert.Equal(4, result.ResolvedActionTile.Y);
Assert.True(result.ScreenVisible);
```

Then add this test before `Handle_NotWorldReady_ThrowsGameStateInvalid`:

```csharp
[Fact]
public void Handle_ActionValueOffscreen_ReportsScreenVisibleFalse()
{
    var world = new FakeTileClickWorld
    {
        CurrentLocationName = "MovieTheater",
        ViewportX = 0,
        ViewportY = 0,
        ViewportWidth = 1280,
        ViewportHeight = 720,
        MapWidth = 80,
        MapHeight = 80,
    };
    world.SetTileProperty(30, 30, "Buildings", "Action", "Theater_Doors");
    var p = JsonDocument.Parse(
        "{\"location\":\"MovieTheater\",\"x\":25,\"y\":25,\"button\":\"right\",\"action_value\":\"Theater_Doors\",\"radius\":10}")
        .RootElement;

    var json = InputClickTileHandler.Handle(p, world);
    var result = JsonSerializer.Deserialize<InputClickTileResult>(json, ProtocolJson.Options)!;

    Assert.Equal("Theater_Doors", result.ResolvedActionValue);
    Assert.Equal("Buildings", result.ResolvedActionLayer);
    Assert.Equal("Action", result.ResolvedActionProperty);
    Assert.NotNull(result.ResolvedActionTile);
    Assert.Equal(30, result.ResolvedActionTile!.X);
    Assert.Equal(30, result.ResolvedActionTile.Y);
    Assert.Equal(1952, result.Screen.X);
    Assert.Equal(1952, result.Screen.Y);
    Assert.False(result.ScreenVisible);
}
```

Add viewport members to `FakeTileClickWorld`:

```csharp
public int ViewportWidth { get; set; } = 1280;
public int ViewportHeight { get; set; } = 720;
```

- [ ] **Step 2: Run the RED handler tests**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~InputClickTileHandlerTests.Handle_ActionValue" --nologo
```

Expected: FAIL at compile time because `InputClickTileResult.ResolvedActionValue`, `ResolvedActionLayer`, `ResolvedActionProperty`, `ResolvedActionTile`, `ScreenVisible`, `IInputTileClickWorld.ViewportWidth`, and `IInputTileClickWorld.ViewportHeight` do not exist.

- [ ] **Step 3: Add protocol result fields**

In `src/Protocol/Models/InputClickTileRequest.cs`, add these properties to `InputClickTileResult` after `Handled`:

```csharp
public string? ResolvedActionValue { get; set; }
public string? ResolvedActionLayer { get; set; }
public string? ResolvedActionProperty { get; set; }
public TilePoint? ResolvedActionTile { get; set; }
public bool ScreenVisible { get; set; }
```

- [ ] **Step 4: Carry the resolved action candidate through the handler**

In `src/Harness/Handlers/InputClickTileHandler.cs`, replace the tuple target return with this private record near the other private helper types:

```csharp
private sealed record ResolvedTileTarget(int X, int Y, TileActionCandidate? Action);
```

Change the call site from:

```csharp
var target = ResolveTargetTile(request, world);
var worldX = target.X * TileSize + TileSize / 2;
var worldY = target.Y * TileSize + TileSize / 2;
```

to:

```csharp
var target = ResolveTargetTile(request, world);
var worldX = target.X * TileSize + TileSize / 2;
var worldY = target.Y * TileSize + TileSize / 2;
```

The local variable names stay the same; the type changes. After `screenX` and `screenY` are computed, add:

```csharp
var screenVisible = screenX >= 0
    && screenY >= 0
    && screenX < world.ViewportWidth
    && screenY < world.ViewportHeight;
```

Populate the new result fields:

```csharp
var result = new InputClickTileResult
{
    Ok = true,
    Tick = world.Tick,
    Location = world.CurrentLocationName,
    Tile = new TilePoint { X = target.X, Y = target.Y },
    Screen = new PixelPoint { X = screenX, Y = screenY },
    World = new PixelPoint { X = worldX, Y = worldY },
    SelectedItem = selectedItem,
    Handled = handled,
    TargetNpcName = request.TargetNpcName,
    NpcFallbackUsed = npcFallbackUsed,
    ResolvedActionValue = target.Action?.Value,
    ResolvedActionLayer = target.Action?.Layer,
    ResolvedActionProperty = target.Action?.Property,
    ResolvedActionTile = target.Action is null ? null : new TilePoint { X = target.Action.X, Y = target.Action.Y },
    ScreenVisible = screenVisible,
};
```

Change `ResolveTargetTile` to return `ResolvedTileTarget`:

```csharp
private static ResolvedTileTarget ResolveTargetTile(InputClickTileRequest request, IInputTileClickWorld world)
{
    if (string.IsNullOrWhiteSpace(request.ActionValue))
    {
        return new ResolvedTileTarget(request.X, request.Y, null);
    }

    var action = ResolveActionTile(request, world);
    return new ResolvedTileTarget(action.X, action.Y, action);
}
```

Update the no-action code paths if the current method body is inlined rather than split; the invariant is that only action-value clicks set `ResolvedAction*`.

- [ ] **Step 5: Add viewport dimensions to the world abstraction**

In `src/Harness/Handlers/InputClickTileHandler.cs`, extend `IInputTileClickWorld`:

```csharp
int ViewportWidth { get; }
int ViewportHeight { get; }
```

In the production world adapter in the same file, implement:

```csharp
public int ViewportWidth => Game1.viewport.Width;
public int ViewportHeight => Game1.viewport.Height;
```

Use the fake properties from Step 1 for tests.

- [ ] **Step 6: Run the GREEN handler tests**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~InputClickTileHandlerTests.Handle_ActionValue" --nologo
```

Expected: PASS for all action-value click handler tests.

- [ ] **Step 7: Commit the handler diagnostics**

Run:

```bash
git add src/Protocol/Models/InputClickTileRequest.cs src/Harness/Handlers/InputClickTileHandler.cs tests/Harness.Tests/InputClickTileHandlerTests.cs
git commit -m "feat: report action click resolution"
```

Expected: commit created on `feature/sve-slice-32-movie-screening`.

---

## Task 3: TDD Protocol And Runner Output For Action Diagnostics

**Files:**
- Modify: `/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-32-movie-screening/tests/Protocol.Tests/InputClickTileSerializationTests.cs`
- Modify: `/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-32-movie-screening/tests/Runner.Tests/ScenarioRunnerTests.cs`
- Modify: `/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-32-movie-screening/src/Runner/Scenarios/ScenarioRunner.cs`

- [ ] **Step 1: Write the failing protocol serialization assertions**

In `tests/Protocol.Tests/InputClickTileSerializationTests.cs`, extend `Result_SerializesDiagnosticsAsSnakeCase` by setting these fields on the result object:

```csharp
ResolvedActionValue = "Theater_Doors",
ResolvedActionLayer = "Buildings",
ResolvedActionProperty = "Action",
ResolvedActionTile = new TilePoint { X = 14, Y = 16 },
ScreenVisible = true,
```

Then add these assertions after the existing serialized-field assertions:

```csharp
Assert.Contains("\"resolved_action_value\":\"Theater_Doors\"", json);
Assert.Contains("\"resolved_action_layer\":\"Buildings\"", json);
Assert.Contains("\"resolved_action_property\":\"Action\"", json);
Assert.Contains("\"resolved_action_tile\":{\"x\":14,\"y\":16}", json);
Assert.Contains("\"screen_visible\":true", json);
```

- [ ] **Step 2: Write the failing runner detail test**

In `tests/Runner.Tests/ScenarioRunnerTests.cs`, add this test near the existing `InputClickTile_PassesThroughAndReportsReadableStep` test:

```csharp
[Fact]
public async Task InputClickTile_ReportsResolvedActionDiagnostics()
{
    var socket = SocketPath();
    var tmp = Path.Combine(Path.GetTempPath(), $"click-action-details-{Guid.NewGuid():N}");
    var rd = RunDirectory.Create(tmp);
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
                    "input.click_tile" => JsonDocument.Parse("{\"ok\":true,\"tick\":5,\"location\":\"MovieTheater\",\"tile\":{\"x\":4,\"y\":13},\"screen\":{\"x\":288,\"y\":544},\"world\":{\"x\":288,\"y\":864},\"handled\":false,\"resolved_action_value\":\"Theater_Doors\",\"resolved_action_layer\":\"Buildings\",\"resolved_action_property\":\"Action\",\"resolved_action_tile\":{\"x\":4,\"y\":13},\"screen_visible\":false}").RootElement,
                    "bitmap.capture" => JsonDocument.Parse("{\"path\":\"/tmp/click-action-details.png\",\"width\":1280,\"height\":720}").RootElement,
                    "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                    _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                };
                await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
            };
            await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
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
            Name = "click_action_details",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "input.click_tile",
                    Args = JsonDocument.Parse("{\"location\":\"MovieTheater\",\"x\":5,\"y\":14,\"button\":\"right\",\"action_value\":\"Theater_Doors\",\"radius\":8}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed, string.Join("\n", report.Failures));
        Assert.Contains("resolved_action=Theater_Doors@4,13 Buildings/Action", report.Steps[0].Detail);
        Assert.Contains("screen_visible=false", report.Steps[0].Detail);
    }
    finally
    {
        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
        Directory.Delete(rd.Root, recursive: true);
    }
}
```

- [ ] **Step 3: Run the RED protocol and runner tests**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter "FullyQualifiedName~InputClickTileSerializationTests.Result_SerializesDiagnosticsAsSnakeCase" --nologo
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~ScenarioRunnerTests.InputClickTile_ReportsResolvedActionDiagnostics" --nologo
```

Expected: protocol test passes once Task 2 fields exist; runner test FAILS because `DescribeInputClickTileResult` does not print the new diagnostics yet.

- [ ] **Step 4: Print action diagnostics in runner step details**

In `src/Runner/Scenarios/ScenarioRunner.cs`, update `DescribeInputClickTileResult` after the existing `handled=` detail is appended:

```csharp
var resolvedActionValue = TryGetString(result, "resolved_action_value");
if (!string.IsNullOrWhiteSpace(resolvedActionValue))
{
    var resolvedTile = TryGetObject(result, "resolved_action_tile");
    var actionX = resolvedTile.HasValue ? TryGetInt32(resolvedTile.Value, "x") : null;
    var actionY = resolvedTile.HasValue ? TryGetInt32(resolvedTile.Value, "y") : null;
    var resolvedLayer = TryGetString(result, "resolved_action_layer");
    var resolvedProperty = TryGetString(result, "resolved_action_property");
    var tileSuffix = actionX.HasValue && actionY.HasValue ? $"@{actionX.Value},{actionY.Value}" : string.Empty;
    var layerSuffix = !string.IsNullOrWhiteSpace(resolvedLayer) && !string.IsNullOrWhiteSpace(resolvedProperty)
        ? $" {resolvedLayer}/{resolvedProperty}"
        : string.Empty;
    detail.Append(" resolved_action=");
    detail.Append(resolvedActionValue);
    detail.Append(tileSuffix);
    detail.Append(layerSuffix);
}

var screenVisible = TryGetBoolean(result, "screen_visible");
if (screenVisible.HasValue)
{
    detail.Append(" screen_visible=");
    detail.Append(screenVisible.Value ? "true" : "false");
}
```

If `TryGetBoolean` does not exist, add this helper near the other JSON helpers in the same file:

```csharp
private static bool? TryGetBoolean(JsonElement element, string propertyName)
{
    if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
    {
        return null;
    }

    return property.GetBoolean();
}
```

- [ ] **Step 5: Run the GREEN protocol and runner tests**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter "FullyQualifiedName~InputClickTileSerializationTests.Result_SerializesDiagnosticsAsSnakeCase" --nologo
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~ScenarioRunnerTests.InputClickTile_ReportsResolvedActionDiagnostics" --nologo
```

Expected: both tests PASS.

- [ ] **Step 6: Commit serialization and runner output**

Run:

```bash
git add tests/Protocol.Tests/InputClickTileSerializationTests.cs tests/Runner.Tests/ScenarioRunnerTests.cs src/Runner/Scenarios/ScenarioRunner.cs
git commit -m "feat: surface action click diagnostics"
```

Expected: commit created on `feature/sve-slice-32-movie-screening`.

---

## Task 4: TDD Root Event Dialogue Filters

**Files:**
- Modify: `/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-32-movie-screening/tests/Runner.Tests/ScenarioRunnerTests.cs`
- Modify: `/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-32-movie-screening/src/Runner/Scenarios/ScenarioRunner.cs`

- [ ] **Step 1: Write the failing root dialogue wait tests**

In `tests/Runner.Tests/ScenarioRunnerTests.cs`, add this helper near the top of the test class after `SocketPath()`:

```csharp
private static async Task<(ScenarioReport Report, int EventPolls)> RunWaitEventActiveScenarioAsync(
    string argsJson,
    params string[] eventResponses)
{
    var socket = SocketPath();
    var eventPolls = 0;
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
                    "state.event" => JsonDocument.Parse(eventResponses[Math.Min(eventPolls++, eventResponses.Length - 1)]).RootElement,
                    "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                    _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                };
                await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
            };
            await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
            await session.RunAsync(tok);
        }, cts.Token);
    }, cts.Token);

    try
    {
        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var runner = new ScenarioRunner(client);
        var report = await runner.RunAsync(new ScenarioSpec
        {
            Name = "wait_event_active_root_dialogue",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.event_active",
                    Args = JsonDocument.Parse(argsJson).RootElement,
                },
            },
        }, cts.Token);

        return (report, eventPolls);
    }
    finally
    {
        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }
}
```

Add these tests near the existing `WaitEventActive_*` tests:

```csharp
[Fact]
public async Task WaitEventActive_FiltersByRootDialogueTextAndSpeaker()
{
    var (report, eventPolls) = await RunWaitEventActiveScenarioAsync(
        "{\"id\":\"movie\",\"dialogue_speaker\":\"Sophia\",\"dialogue_text\":\"so so great\",\"timeout_ms\":1000,\"poll_ms\":1}",
        """
        {
          "active": true,
          "event_up": true,
          "id": "movie",
          "location": "MovieTheater",
          "is_festival": false,
          "dialogue": { "speaker": "Lewis", "text": "Welcome to the theater." },
          "actors": [],
          "viewport": { "x": 0, "y": 0, "width": 1280, "height": 720 }
        }
        """,
        """
        {
          "active": true,
          "event_up": true,
          "id": "movie",
          "location": "MovieTheater",
          "is_festival": false,
          "dialogue": { "speaker": "Sophia", "text": "The movie was so so great! Thanks for taking me!" },
          "actors": [],
          "viewport": { "x": 0, "y": 0, "width": 1280, "height": 720 }
        }
        """);

    Assert.True(report.Passed, string.Join("\n", report.Failures));
    Assert.Equal(2, eventPolls);
}

[Fact]
public async Task WaitEventActive_FiltersByRootDialogueTextMatches()
{
    var (report, _) = await RunWaitEventActiveScenarioAsync(
        "{\"dialogue_speaker\":\"Sophia\",\"dialogue_text_matches\":\"movie\\\\s+was\\\\s+so\\\\s+so\\\\s+great\",\"timeout_ms\":1000,\"poll_ms\":1}",
        """
        {
          "active": true,
          "event_up": true,
          "id": "movie",
          "location": "MovieTheater",
          "is_festival": false,
          "dialogue": { "speaker": "Sophia", "text": "The movie was so so great! Thanks for taking me!" },
          "actors": [],
          "viewport": { "x": 0, "y": 0, "width": 1280, "height": 720 }
        }
        """);

    Assert.True(report.Passed, string.Join("\n", report.Failures));
}

[Fact]
public async Task WaitEventActive_RootDialogueTimeoutIncludesObservedDialogue()
{
    var (report, _) = await RunWaitEventActiveScenarioAsync(
        "{\"dialogue_speaker\":\"Sophia\",\"dialogue_text\":\"missing text\",\"timeout_ms\":20,\"poll_ms\":1}",
        """
        {
          "active": true,
          "event_up": true,
          "id": "movie",
          "location": "MovieTheater",
          "is_festival": false,
          "dialogue": { "speaker": "Sophia", "text": "The movie was so so great! Thanks for taking me!" },
          "actors": [],
          "viewport": { "x": 0, "y": 0, "width": 1280, "height": 720 }
        }
        """);

    Assert.False(report.Passed);
    var failure = Assert.Single(report.Failures);
    Assert.Contains("dialogue_text contains missing text", failure);
    Assert.Contains("dialogue=Sophia", failure);
    Assert.Contains("movie was so so great", failure);
}
```

- [ ] **Step 2: Run the RED root dialogue tests**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~ScenarioRunnerTests.WaitEventActive_FiltersByRootDialogue|FullyQualifiedName~ScenarioRunnerTests.WaitEventActive_RootDialogueTimeoutIncludesObservedDialogue" --nologo
```

Expected: FAIL because `dialogue_text`, `dialogue_text_matches`, and `dialogue_speaker` are not recognized by `WaitEventStepArgs`.

- [ ] **Step 3: Add wait argument fields**

In `src/Runner/Scenarios/ScenarioRunner.cs`, add these properties to `WaitEventStepArgs` after `ActorDialogueKey`:

```csharp
public string? DialogueText { get; set; }
public string? DialogueTextMatches { get; set; }
public string? DialogueSpeaker { get; set; }
```

- [ ] **Step 4: Add root dialogue matching**

In `InvokeWaitEventActiveAsync`, extend the `matched` condition:

```csharp
var matched = lastObserved.Active
    && (string.IsNullOrWhiteSpace(args.Id) || string.Equals(lastObserved.Id, args.Id, StringComparison.Ordinal))
    && (string.IsNullOrWhiteSpace(args.Location) || string.Equals(lastObserved.Location, args.Location, StringComparison.Ordinal))
    && (!args.IsFestival.HasValue || lastObserved.IsFestival == args.IsFestival.Value)
    && EventActorMatches(lastObserved, args)
    && EventDialogueMatches(lastObserved, args);
```

Add this helper near `EventActorMatches`:

```csharp
private static bool EventDialogueMatches(EventStateResult state, WaitEventStepArgs args)
{
    if (string.IsNullOrWhiteSpace(args.DialogueText)
        && string.IsNullOrWhiteSpace(args.DialogueTextMatches)
        && string.IsNullOrWhiteSpace(args.DialogueSpeaker))
    {
        return true;
    }

    if (state.Dialogue is null)
    {
        return false;
    }

    if (!string.IsNullOrWhiteSpace(args.DialogueSpeaker)
        && !string.Equals(state.Dialogue.Speaker, args.DialogueSpeaker, StringComparison.Ordinal))
    {
        return false;
    }

    if (!string.IsNullOrWhiteSpace(args.DialogueText)
        && (state.Dialogue.Text is null
            || state.Dialogue.Text.IndexOf(args.DialogueText, StringComparison.OrdinalIgnoreCase) < 0))
    {
        return false;
    }

    if (!string.IsNullOrWhiteSpace(args.DialogueTextMatches)
        && (state.Dialogue.Text is null
            || !Regex.IsMatch(state.Dialogue.Text, args.DialogueTextMatches, RegexOptions.IgnoreCase)))
    {
        return false;
    }

    return true;
}
```

- [ ] **Step 5: Include root dialogue in timeout filters and observed state**

In `FormatWaitEventFilters`, append root dialogue filters after actor filters:

```csharp
if (!string.IsNullOrWhiteSpace(args.DialogueSpeaker))
{
    parts.Add($"dialogue_speaker={args.DialogueSpeaker}");
}

if (!string.IsNullOrWhiteSpace(args.DialogueText))
{
    parts.Add($"dialogue_text contains {args.DialogueText}");
}

if (!string.IsNullOrWhiteSpace(args.DialogueTextMatches))
{
    parts.Add($"dialogue_text matches {args.DialogueTextMatches}");
}
```

In `FormatEventState`, include the observed dialogue when present:

```csharp
if (state.Dialogue is not null)
{
    var text = state.Dialogue.Text ?? string.Empty;
    if (text.Length > 120)
    {
        text = text[..120] + "...";
    }

    parts.Add($"dialogue={state.Dialogue.Speaker} \"{text}\"");
}
```

- [ ] **Step 6: Run the GREEN root dialogue tests**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~ScenarioRunnerTests.WaitEventActive_FiltersByRootDialogue|FullyQualifiedName~ScenarioRunnerTests.WaitEventActive_RootDialogueTimeoutIncludesObservedDialogue" --nologo
```

Expected: all three root dialogue tests PASS.

- [ ] **Step 7: Commit root dialogue filters**

Run:

```bash
git add src/Runner/Scenarios/ScenarioRunner.cs tests/Runner.Tests/ScenarioRunnerTests.cs
git commit -m "feat: wait for root event dialogue"
```

Expected: commit created on `feature/sve-slice-32-movie-screening`.

---

## Task 5: SVE Live Probe For Theater Screening Entry

**Files:**
- Create temporary file only: `/tmp/sve-40-movie-screening-probe.test.json`
- Read: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/38-sve-movie-ticket-invite-flow.test.json`
- Read: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/39-sve-movie-concession-purchase-flow.test.json`

- [ ] **Step 1: Write the probe scenario**

Create `/tmp/sve-40-movie-screening-probe.test.json` with this content:

```json
{
  "name": "sve_movie_screening_entry_probe",
  "description": "Temporary probe for stable click-based movie screening entry. Do not commit.",
  "default_timeout_ms": 30000,
  "steps": [
    {
      "name": "Load summer save",
      "rpc": "fixture.load",
      "args": {
        "profile": "sve-core",
        "date": { "year": 1, "season": "summer", "day": 3 },
        "time": 1500,
        "money": 50000,
        "friendships": { "Sophia": 2500 },
        "mail": [ "ccMovieTheater" ]
      }
    },
    {
      "name": "Warp to Sophia",
      "rpc": "player.warp",
      "args": { "location": "Custom_SophiaHouse", "x": 13, "y": 10 }
    },
    {
      "name": "Give movie ticket to Sophia",
      "rpc": "input.use_item_on_npc",
      "args": {
        "npc": "Sophia",
        "item_id": "(O)809",
        "amount": 1,
        "button": "right",
        "allow_dialogue": true,
        "allow_event_input": true
      }
    },
    {
      "name": "Confirm invite prompt",
      "rpc": "input.choose_response",
      "args": { "response": "Yes" }
    },
    {
      "name": "Warp to movie lobby near screening doors",
      "rpc": "player.warp",
      "args": { "location": "MovieTheater", "x": 5, "y": 14 }
    },
    {
      "name": "List nearby theater door actions",
      "rpc": "state.tile_actions",
      "args": { "location": "MovieTheater", "x": 5, "y": 14, "radius": 12 }
    },
    {
      "name": "Click theater doors by action value",
      "rpc": "input.click_tile",
      "args": {
        "location": "MovieTheater",
        "x": 5,
        "y": 14,
        "button": "right",
        "action_value": "Theater_Doors",
        "radius": 12
      }
    },
    {
      "name": "Wait for movie event reaction dialogue",
      "rpc": "wait.event_active",
      "args": {
        "dialogue_speaker": "Sophia",
        "dialogue_text_matches": "Prairie King|cosplay|movie was so so great|Thanks for taking me",
        "timeout_ms": 15000,
        "poll_ms": 100
      }
    }
  ]
}
```

- [ ] **Step 2: Run the probe**

Run from `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-32-movie-screening ./scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-32-probe /tmp/sve-40-movie-screening-probe.test.json
```

Expected if the anchor is correct: PASS. The `Click theater doors by action value` step detail includes:

```text
resolved_action=Theater_Doors@
screen_visible=true
handled=true
```

- [ ] **Step 3: If the probe fails before the wait step, change only the exact anchor pair in all three theater-door steps**

Use this ordered anchor list and rerun Step 2 after each single edit:

```text
5,14
5,13
6,14
4,13
4,14
6,13
7,14
```

The three edits are:

```json
"args": { "location": "MovieTheater", "x": 5, "y": 14 }
```

```json
"args": { "location": "MovieTheater", "x": 5, "y": 14, "radius": 12 }
```

```json
"args": {
  "location": "MovieTheater",
  "x": 5,
  "y": 14,
  "button": "right",
  "action_value": "Theater_Doors",
  "radius": 12
}
```

Expected: one anchor produces `screen_visible=true` and `handled=true`. Use that anchor in Task 6. If every right-click anchor resolves the action but reports `handled=false`, repeat the same ordered anchor list with `"button": "left"` in the click step and keep the first anchor/button combination that starts the event.

- [ ] **Step 4: If the probe reaches the wait step but times out, capture the observed dialogue from the report**

Open:

```text
/tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-32-probe/index.html
```

Read the failed `Wait for movie event reaction dialogue` row. The new timeout detail should include `dialogue=<speaker> "<text>"`. Replace the regex in the probe with the shortest stable Sophia phrase visible in that timeout, then rerun Step 2. Keep the passing phrase for Task 6.

Do not commit `/tmp/sve-40-movie-screening-probe.test.json`.

---

## Task 6: Add SVE Scenario 40

**Files:**
- Create: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/40-sve-movie-screening-reaction-flow.test.json`
- Modify: `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`

- [ ] **Step 1: Write the failing SVE scenario**

Create `tests/sdv/40-sve-movie-screening-reaction-flow.test.json` in the SVE repo. Use the working theater anchor and button from Task 5. If Task 5 passed with the first probe values, the file content is:

```json
{
  "name": "sve_movie_screening_reaction_flow",
  "description": "Invites Sophia to a movie, enters the screening through click-based UI, and validates SVE movie reaction dialogue.",
  "default_timeout_ms": 30000,
  "steps": [
    {
      "name": "Load summer movie save",
      "rpc": "fixture.load",
      "args": {
        "profile": "sve-core",
        "date": { "year": 1, "season": "summer", "day": 3 },
        "time": 1500,
        "money": 50000,
        "friendships": { "Sophia": 2500 },
        "mail": [ "ccMovieTheater" ]
      }
    },
    {
      "name": "Assert Sophia movie reaction data is patched",
      "rpc": "content.asset",
      "args": {
        "asset_name": "Data/MoviesReactions",
        "entry_keys": [ "Sophia" ]
      },
      "assert": {
        "label": "Data/MoviesReactions includes Sophia's summer movie response",
        "expr": "asset.entries.Sophia.exists == true && asset.entries.Sophia.value contains 'summer_movie_0' && asset.entries.Sophia.value contains 'Sophia.Movies.03'"
      }
    },
    {
      "name": "Warp to Sophia",
      "rpc": "player.warp",
      "args": { "location": "Custom_SophiaHouse", "x": 13, "y": 10 }
    },
    {
      "name": "Give movie ticket to Sophia",
      "rpc": "input.use_item_on_npc",
      "args": {
        "npc": "Sophia",
        "item_id": "(O)809",
        "amount": 1,
        "button": "right",
        "allow_dialogue": true,
        "allow_event_input": true
      }
    },
    {
      "name": "Confirm movie invite",
      "rpc": "input.choose_response",
      "args": { "response": "Yes" }
    },
    {
      "name": "Warp to movie lobby near screening doors",
      "rpc": "player.warp",
      "args": { "location": "MovieTheater", "x": 5, "y": 14 }
    },
    {
      "name": "Assert theater door action is discoverable",
      "rpc": "state.tile_actions",
      "args": { "location": "MovieTheater", "x": 5, "y": 14, "radius": 12 },
      "assert": {
        "label": "Movie lobby exposes Theater_Doors map action near the player",
        "expr": "actions any (value == 'Theater_Doors')"
      }
    },
    {
      "name": "Click theater doors",
      "rpc": "input.click_tile",
      "args": {
        "location": "MovieTheater",
        "x": 5,
        "y": 14,
        "button": "right",
        "action_value": "Theater_Doors",
        "radius": 12
      },
      "assert": {
        "label": "Theater door click resolves visibly and starts handling",
        "expr": "resolved_action_value == 'Theater_Doors' && screen_visible == true && handled == true"
      }
    },
    {
      "name": "Wait for Sophia movie reaction",
      "rpc": "wait.event_active",
      "args": {
        "dialogue_speaker": "Sophia",
        "dialogue_text_matches": "Prairie King|cosplay|movie was so so great|Thanks for taking me",
        "timeout_ms": 15000,
        "poll_ms": 100
      }
    }
  ]
}
```

If Task 5 found a different working anchor, replace only the `x`, `y`, and `button` values in the three theater-door steps with the passing probe values before committing.

- [ ] **Step 2: Run the RED or GREEN SVE scenario**

Run from `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-32-movie-screening ./scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-32-scenario-40 tests/sdv/40-sve-movie-screening-reaction-flow.test.json
```

Expected: PASS. Existing SVE compiler warnings are acceptable if they match the known warnings from prior runs:

```text
NPC.isVillager() obsolete
Utility.getItemFromStandardTextDescription obsolete
unreachable code in JA ItemMigrator
```

If the content assertion fails because `asset.entries.Sophia.value` does not expose the raw string shape, replace that expression with the stricter supported shape shown by the failed report. Keep these three facts asserted: the Sophia entry exists, it references `summer_movie_0`, and it references `Sophia.Movies.03`.

- [x] **Step 3: Update SVE Frobby docs**

In `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`, add scenario 40 after scenario 39 in the scenario list:

```markdown
- `tests/sdv/40-sve-movie-screening-reaction-flow.test.json` validates the Sophia movie-screening path end to end: queued introduction clearing, movie-ticket invite, click-based theater-door entry, SVE movie reaction content, and visible movie reaction menu text.
```

Add this capability note near the movie-theater section:

```markdown
Scenario 40 uses Frobby's generic `input.click_tile` action diagnostics and `wait.menu` for the final visible movie reaction. Root dialogue filters remain available for cutscenes where Stardew stores dialogue directly on active event state, but the movie reaction surfaces through normal dialogue/message UI.
```

- [ ] **Step 4: Commit the SVE scenario**

Run from `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
git add tests/sdv/40-sve-movie-screening-reaction-flow.test.json docs/FROBBY.md
git commit -m "test: cover movie screening reactions"
```

Expected: commit created on `feature/frobby-sve-slice-32-movie-screening`. Do not merge this branch into `master`.

---

## Task 7: Frobby Docs And Capability Tracking

**Files:**
- Modify: `/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-32-movie-screening/README.md`
- Modify: `/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-32-movie-screening/docs/rpc-schema.md`
- Modify: `/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-32-movie-screening/docs/wiki/examples.md`
- Modify: `/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-32-movie-screening/SVE_FROBBY_CAPABILITY_TODO.md`

- [x] **Step 1: Document `input.click_tile` diagnostics**

In `README.md`, find the `input.click_tile` scenario guidance and add:

```markdown
When `action_value` is used, the result includes `resolved_action_value`, `resolved_action_layer`, `resolved_action_property`, `resolved_action_tile`, and `screen_visible`. Prefer asserting `screen_visible == true` before relying on a click to open a menu or start an event; it catches cases where the action was found but the viewport could not actually click the tile.
```

In `docs/rpc-schema.md`, add these result fields to `input.click_tile`:

```markdown
- `resolved_action_value` (string, optional): action value that was matched when `action_value` was supplied.
- `resolved_action_layer` (string, optional): map layer that supplied the matched action property.
- `resolved_action_property` (string, optional): property name that supplied the matched action value.
- `resolved_action_tile` (`{ "x": number, "y": number }`, optional): exact tile selected by action-value discovery.
- `screen_visible` (boolean): whether the click's computed screen coordinate is inside the current viewport.
```

- [x] **Step 2: Document root dialogue waits**

In `README.md`, find the `wait.event_active` guidance and add:

```markdown
`wait.event_active` can filter the root event dialogue with `dialogue_speaker`, `dialogue_text`, and `dialogue_text_matches`. Use these for cutscenes where the active dialogue is not attached to a named actor row in the event actor list.
```

In `docs/rpc-schema.md`, add these wait arguments:

```markdown
- `dialogue_speaker` (string, optional): required speaker for the active root event dialogue.
- `dialogue_text` (string, optional): case-insensitive substring required in the active root event dialogue text.
- `dialogue_text_matches` (string, optional): case-insensitive regex required in the active root event dialogue text.
```

- [x] **Step 3: Add scenario 40 to examples**

In `docs/wiki/examples.md`, add:

```markdown
### SVE Scenario 40: Movie Screening Reaction

`tests/sdv/40-sve-movie-screening-reaction-flow.test.json` demonstrates a full click-driven theater screening flow. It combines content asset assertions, action-value tile click diagnostics, queued dialogue cleanup through real NPC clicks, and a visible movie reaction wait through `wait.menu`.
```

- [x] **Step 4: Mark Slice 32 complete in the capability tracker**

In `SVE_FROBBY_CAPABILITY_TODO.md`, move or update the Slice 32 entry so it reads:

```markdown
- [x] Slice 32: Movie screening reaction flow
  - Frobby: `input.click_tile` action resolution diagnostics and `wait.event_active` root dialogue filters.
  - SVE: `tests/sdv/40-sve-movie-screening-reaction-flow.test.json`.
```

Preserve any active future items below the completed Slice 32 entry.

- [ ] **Step 5: Commit Frobby docs**

Run:

```bash
git add README.md docs/rpc-schema.md docs/wiki/examples.md SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "docs: document movie screening test capabilities"
```

Expected: commit created on `feature/sve-slice-32-movie-screening`.

---

## Task 8: Full Verification

**Files:**
- Read generated report: `/tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-32-final/index.html`
- Read generated report: `/tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-32-smoke/index.html`

- [ ] **Step 1: Run the full Frobby test suite**

Run from the Frobby Slice 32 worktree:

```bash
dotnet test --nologo
```

Expected: all Frobby test projects pass. Counts may increase from the baseline because this plan adds tests; no test should fail.

- [ ] **Step 2: Run the SVE Slice 32 focused suite**

Run from `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-32-movie-screening ./scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-32-final tests/sdv/40-sve-movie-screening-reaction-flow.test.json tests/sdv/39-sve-movie-concession-purchase-flow.test.json tests/sdv/38-sve-movie-ticket-invite-flow.test.json tests/sdv/36-sve-movie-theater-claims.test.json
```

Expected: PASS for scenarios 40, 39, 38, and 36. The report index exists at:

```text
/tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-32-final/index.html
```

- [ ] **Step 3: Run a small Starberg smoke guard against framework regressions**

Run from `/home/fintan/stardewRepos/stonks`:

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-32-movie-screening ./scripts/sdv-test --headless --report-dir /tmp/starberg-frobby-results-0.1.0/slice-32-smoke tests/sdv/01-open-terminal.test.json tests/sdv/38-chart-panel-live-spacing.test.json tests/sdv/67-news-article-detail.test.json
```

Expected: PASS for the selected Starberg smoke scenarios. The smoke scenarios exercise existing command, chart, and news flows while using the updated runner/harness assemblies.

- [ ] **Step 4: Check formatting and worktree state**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-32-movie-screening diff --check
git -C /home/fintan/stardewRepos/StardewValleyExpanded diff --check
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-32-movie-screening status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
```

Expected:

```text
diff --check produces no output
Frobby branch is feature/sve-slice-32-movie-screening with no dirty files
SVE branch is feature/frobby-sve-slice-32-movie-screening with no dirty files
```

- [ ] **Step 5: Prepare the merge summary**

Record these items in the final response:

```text
Frobby commits created on feature/sve-slice-32-movie-screening
SVE commit created on feature/frobby-sve-slice-32-movie-screening
Frobby dotnet test result
SVE focused suite report path
Starberg smoke report path
SVE branch was not merged to master
Frobby branch is ready for user-approved merge to main
```

Do not merge Frobby into `main` until the user explicitly approves that integration step.
