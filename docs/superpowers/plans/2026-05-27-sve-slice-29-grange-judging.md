# SVE Slice 29 Grange Judging Progression Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add neutral Frobby support and SVE live coverage for Stardew Fair grange judging progression, using a player-like Lewis click first and a generic active-event fallback only if required.

**Architecture:** Add small runner/harness primitives around active festival actors instead of hard-coding SVE coordinates. The SVE scenario starts the Fair, validates SVE actors/content, clicks Lewis through a resolved event-actor tile, waits for SVE judging dialogue side effects, and falls back to a generic active-event method trigger only if the player-facing route cannot be driven headlessly.

**Tech Stack:** C#/.NET 6, xUnit, Frobby JSON-RPC harness, Frobby runner JSON scenarios, Stardew Valley/SMAPI, SVE repo-local SDV scenarios.

---

## File Structure

Frobby files:

- Modify `src/Protocol/Models/EventState.cs`
  - Add optional actor dialogue summary fields to `EventActorState`.
- Modify `src/Harness/Handlers/EventStateProjector.cs`
  - Project actor current dialogue text/key/count through neutral reflection.
- Modify `tests/Harness.Tests/EventStateProjectorTests.cs`
  - Cover actor dialogue projection.
- Modify `src/Runner/Scenarios/ScenarioRunner.cs`
  - Add runner action `input.click_event_actor`, add actor dialogue filters to `wait.event_active`, and improve step labels.
- Modify `tests/Runner.Tests/ScenarioRunnerTests.cs`
  - Cover `input.click_event_actor`, actor dialogue wait filters, and timeout diagnostics.
- Conditionally create `src/Protocol/Models/EventInvokeMethodRequest.cs`
  - Fallback-only request/result DTOs for a generic active-event method trigger.
- Conditionally create `src/Harness/Handlers/EventInvokeMethodHandler.cs`
  - Fallback-only neutral handler for invoking a zero-argument method on the active Stardew event.
- Conditionally modify `src/Harness/ModEntry.cs`
  - Register `event.invoke_method`.
- Conditionally add `tests/Protocol.Tests/EventInvokeMethodSerializationTests.cs`
  - DTO serialization coverage.
- Conditionally add `tests/Harness.Tests/EventInvokeMethodHandlerTests.cs`
  - Handler validation and reflection behavior coverage.
- Modify `README.md`, `docs/rpc-schema.md`, `docs/wiki/examples.md`, and `SVE_FROBBY_CAPABILITY_TODO.md`
  - Document the new neutral pattern and final Slice 29 status.

SVE files:

- Create `tests/sdv/37-sve-fair-grange-judging-progression.test.json`
  - Live SVE scenario.
- Modify `docs/FROBBY.md`
  - Document scenario 37.

## Task 1: Project Active Event Actor Dialogue

**Files:**
- Modify: `src/Protocol/Models/EventState.cs`
- Modify: `src/Harness/Handlers/EventStateProjector.cs`
- Test: `tests/Harness.Tests/EventStateProjectorTests.cs`

- [ ] **Step 1: Write the failing actor dialogue projection test**

Append this test and helper types to `tests/Harness.Tests/EventStateProjectorTests.cs`:

```csharp
[Fact]
public void ToState_ActiveEvent_ProjectsActorDialogueSummary()
{
    var ev = new FakeEvent();
    ev.actors[0] = new FakeActorWithDialogue("Sophia", 47, 60, 3008, 3840, 2, 4)
    {
        CurrentDialogue = new List<FakeDialogue>
        {
            new("Fair_Judging", "I worked hard to make these!"),
        },
    };

    var state = EventStateProjector.ToState(new EventProjectionSource
    {
        CurrentEvent = ev,
        EventUp = true,
        LocationName = "Temp",
        Viewport = new Rectangle(0, 0, 1280, 720),
    });

    var actor = Assert.Single(state.Actors);
    Assert.Equal("Sophia", actor.Name);
    Assert.Equal("Fair_Judging", actor.DialogueKey);
    Assert.Equal("I worked hard to make these!", actor.DialogueText);
    Assert.Equal(1, actor.DialogueCount);
}

private sealed class FakeActorWithDialogue : FakeActor
{
    public FakeActorWithDialogue(string name, int tileX, int tileY, int pixelX, int pixelY, int facing, int frame)
        : base(name, tileX, tileY, pixelX, pixelY, facing, frame)
    {
    }

    public List<FakeDialogue> CurrentDialogue { get; set; } = new();
}

private sealed class FakeDialogue
{
    public FakeDialogue(string key, string text)
    {
        dialogueKey = key;
        Text = text;
    }

    public string dialogueKey;
    public string Text { get; }
}
```

Also change `FakeActor` from `private sealed class FakeActor` to `private class FakeActor` so `FakeActorWithDialogue` can inherit it.

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```bash
dotnet test tests/Harness.Tests/ --filter "FullyQualifiedName~EventStateProjectorTests.ToState_ActiveEvent_ProjectsActorDialogueSummary" --nologo
```

Expected: fail because `EventActorState` does not expose `DialogueKey`, `DialogueText`, or `DialogueCount`.

- [ ] **Step 3: Add actor dialogue fields to the protocol model**

Modify `src/Protocol/Models/EventState.cs`:

```csharp
public sealed class EventActorState
{
    public string Name { get; set; } = string.Empty;
    public TilePoint Tile { get; set; } = new();
    public PixelPoint Pixel { get; set; } = new();
    public int FacingDirection { get; set; }
    public int CurrentFrame { get; set; }
    public string DialogueKey { get; set; } = string.Empty;
    public string DialogueText { get; set; } = string.Empty;
    public int DialogueCount { get; set; }
}
```

- [ ] **Step 4: Implement neutral dialogue projection**

Modify `ProjectActor` in `src/Harness/Handlers/EventStateProjector.cs` to assign dialogue fields:

```csharp
private static EventActorState ProjectActor(object actor)
{
    var tile = ReadPoint(actor, "TilePoint", "Tile", "tilePoint", "tile");
    var pixel = ReadVector(actor, "Position", "position");
    var sprite = ReadMember(actor, "Sprite") ?? ReadMember(actor, "sprite");
    var dialogue = ProjectActorDialogue(actor);
    return new EventActorState
    {
        Name = ReadString(actor, "Name", "name", "displayName", "DisplayName"),
        Tile = new TilePoint { X = tile.X, Y = tile.Y },
        Pixel = new PixelPoint { X = (int)pixel.X, Y = (int)pixel.Y },
        FacingDirection = ReadInt(actor, "FacingDirection", "facingDirection", "FacingDirectionValue"),
        CurrentFrame = sprite is null
            ? ReadInt(actor, "CurrentFrame", "currentFrame")
            : ReadInt(sprite, "CurrentFrame", "currentFrame"),
        DialogueKey = dialogue.Key,
        DialogueText = dialogue.Text,
        DialogueCount = dialogue.Count,
    };
}
```

Add these helpers below `ProjectActor`:

```csharp
private static (string Key, string Text, int Count) ProjectActorDialogue(object actor)
{
    var currentDialogue = ReadMember(actor, "CurrentDialogue", "currentDialogue");
    if (currentDialogue is not IEnumerable enumerable || currentDialogue is string)
        return (string.Empty, string.Empty, 0);

    object? first = null;
    var count = 0;
    foreach (var item in enumerable)
    {
        if (item is null)
            continue;

        first ??= item;
        count++;
    }

    if (first is null)
        return (string.Empty, string.Empty, 0);

    return (
        ReadString(first, "dialogueKey", "DialogueKey", "key", "Key"),
        ReadDialogueText(first),
        count);
}

private static string ReadDialogueText(object dialogue)
{
    var text = ReadString(dialogue, "Text", "text", "currentDialogue", "CurrentDialogue", "dialogue", "Dialogue");
    if (!string.IsNullOrWhiteSpace(text))
        return text;

    const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    var method = dialogue.GetType().GetMethod("getCurrentDialogue", flags, Type.EmptyTypes)
        ?? dialogue.GetType().GetMethod("GetCurrentDialogue", flags, Type.EmptyTypes);
    return method?.Invoke(dialogue, Array.Empty<object>()) as string ?? string.Empty;
}
```

- [ ] **Step 5: Run focused projection tests and verify GREEN**

Run:

```bash
dotnet test tests/Harness.Tests/ --filter "FullyQualifiedName~EventStateProjectorTests" --nologo
```

Expected: all `EventStateProjectorTests` pass.

- [ ] **Step 6: Commit Task 1**

Run:

```bash
git add src/Protocol/Models/EventState.cs src/Harness/Handlers/EventStateProjector.cs tests/Harness.Tests/EventStateProjectorTests.cs
git commit -m "feat: expose event actor dialogue summaries"
```

## Task 2: Add Runner Actor Dialogue Wait Filters And Event Actor Clicks

**Files:**
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`
- Test: `tests/Runner.Tests/ScenarioRunnerTests.cs`

- [ ] **Step 1: Write failing runner test for `input.click_event_actor`**

Append this test near `InputClickTile_PassesThroughAndReportsReadableStep` in `tests/Runner.Tests/ScenarioRunnerTests.cs`:

```csharp
[Fact]
public async Task InputClickEventActor_ResolvesActorTileThenClicksTile()
{
    var socket = SocketPath();
    var calls = new List<string>();
    var clickParams = default(JsonElement);
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

    var serverTask = Task.Run(async () =>
    {
        await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
        {
            session.RequestReceived += async req =>
            {
                calls.Add(req.Method);
                if (req.Method == "input.click_tile")
                    clickParams = req.Params!.Value.Clone();

                JsonElement r = req.Method switch
                {
                    "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                    "state.event" => JsonDocument.Parse("{\"active\":true,\"event_up\":true,\"location\":\"Temp\",\"id\":\"fall16\",\"is_festival\":true,\"actors\":[{\"name\":\"Lewis\",\"tile\":{\"x\":54,\"y\":69},\"pixel\":{\"x\":3456,\"y\":4416},\"facing_direction\":2,\"current_frame\":0,\"dialogue_key\":\"\",\"dialogue_text\":\"\",\"dialogue_count\":0}],\"dialogue\":null,\"viewport\":{\"x\":0,\"y\":0,\"width\":1280,\"height\":720}}").RootElement,
                    "input.click_tile" => JsonDocument.Parse("{\"ok\":true,\"tick\":12,\"location\":\"Temp\",\"tile\":{\"x\":54,\"y\":69},\"screen\":{\"x\":0,\"y\":0},\"world\":{\"x\":3456,\"y\":4416},\"handled\":true}").RootElement,
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
        Name = "click_event_actor",
        Steps = new()
        {
            new ScenarioStep
            {
                Action = "input.click_event_actor",
                Args = JsonDocument.Parse("{\"actor_name\":\"Lewis\",\"button\":\"right\",\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
            },
        },
    }, cts.Token);

    Assert.True(report.Passed, string.Join("\n", report.Failures));
    Assert.Contains("state.event", calls);
    Assert.Contains("input.click_tile", calls);
    Assert.Equal("Temp", clickParams.GetProperty("location").GetString());
    Assert.Equal("right", clickParams.GetProperty("button").GetString());
    Assert.Equal(54, clickParams.GetProperty("x").GetInt32());
    Assert.Equal(69, clickParams.GetProperty("y").GetInt32());
    Assert.True(clickParams.GetProperty("allow_event_input").GetBoolean());

    cts.Cancel();
    try { await serverTask; } catch (OperationCanceledException) { }
}
```

- [ ] **Step 2: Write failing runner test for actor dialogue wait filter**

Append this test near the existing `WaitEventActive_*` tests:

```csharp
[Fact]
public async Task WaitEventActive_FiltersByActorDialogueText()
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
                    "state.event" when eventPolls++ == 0 => JsonDocument.Parse("{\"active\":true,\"event_up\":true,\"location\":\"Temp\",\"id\":\"fall16\",\"is_festival\":true,\"actors\":[{\"name\":\"Sophia\",\"tile\":{\"x\":47,\"y\":60},\"pixel\":{\"x\":3008,\"y\":3840},\"facing_direction\":2,\"current_frame\":0,\"dialogue_key\":\"Fair_Judging\",\"dialogue_text\":\"I'm presenting my best aged wine from Blue Moon Vineyard!\",\"dialogue_count\":1}],\"dialogue\":null,\"viewport\":{\"x\":0,\"y\":0,\"width\":1280,\"height\":720}}").RootElement,
                    "state.event" => JsonDocument.Parse("{\"active\":true,\"event_up\":true,\"location\":\"Temp\",\"id\":\"fall16\",\"is_festival\":true,\"actors\":[{\"name\":\"Sophia\",\"tile\":{\"x\":47,\"y\":60},\"pixel\":{\"x\":3008,\"y\":3840},\"facing_direction\":2,\"current_frame\":0,\"dialogue_key\":\"AfterJudgding\",\"dialogue_text\":\"Don't forget to clear out your grange display, okay.\",\"dialogue_count\":1}],\"dialogue\":null,\"viewport\":{\"x\":0,\"y\":0,\"width\":1280,\"height\":720}}").RootElement,
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
        Name = "wait_actor_dialogue",
        Steps = new()
        {
            new ScenarioStep
            {
                Action = "wait.event_active",
                Args = JsonDocument.Parse("{\"id\":\"fall16\",\"is_festival\":true,\"actor_name\":\"Sophia\",\"actor_dialogue_text_matches\":\"clear out your grange display\",\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
            },
        },
    }, cts.Token);

    Assert.True(report.Passed, string.Join("\n", report.Failures));
    Assert.True(eventPolls >= 2);

    cts.Cancel();
    try { await serverTask; } catch (OperationCanceledException) { }
}
```

- [ ] **Step 3: Run focused runner tests and verify RED**

Run:

```bash
dotnet test tests/Runner.Tests/ --filter "FullyQualifiedName~InputClickEventActor_ResolvesActorTileThenClicksTile|FullyQualifiedName~WaitEventActive_FiltersByActorDialogueText" --nologo
```

Expected: fail because `input.click_event_actor` is not handled and `actor_dialogue_text_matches` is ignored.

- [ ] **Step 4: Add runner action dispatch**

In `src/Runner/Scenarios/ScenarioRunner.cs`, add this branch after `wait.event_complete` and before screenshot handling:

```csharp
else if (step.Action == "input.click_event_actor")
{
    await InvokeInputClickEventActorAsync(step, ct);
}
```

- [ ] **Step 5: Add click-event-actor implementation**

Add this method near the event wait helpers in `ScenarioRunner.cs`:

```csharp
private async Task InvokeInputClickEventActorAsync(ScenarioStep step, CancellationToken ct)
{
    var args = step.Args is { ValueKind: JsonValueKind.Object } obj
        ? JsonSerializer.Deserialize<InputClickEventActorStepArgs>(obj.GetRawText(), ProtocolJson.Options) ?? new InputClickEventActorStepArgs()
        : new InputClickEventActorStepArgs();

    if (string.IsNullOrWhiteSpace(args.ActorName))
        throw new InvalidOperationException("input.click_event_actor requires args.actor_name");
    if (args.TimeoutMs < 1)
        throw new InvalidOperationException("input.click_event_actor requires args.timeout_ms >= 1");
    if (args.PollMs < 1)
        throw new InvalidOperationException("input.click_event_actor requires args.poll_ms >= 1");

    var elapsed = Stopwatch.StartNew();
    EventState? lastObserved = null;
    while (elapsed.ElapsedMilliseconds < args.TimeoutMs)
    {
        ct.ThrowIfCancellationRequested();
        lastObserved = await ReadEventStateAsync(step.Action, ct);
        var actor = lastObserved.Actors.FirstOrDefault(a => string.Equals(a.Name, args.ActorName, StringComparison.Ordinal));
        if (lastObserved.Active && actor is not null)
        {
            var clickParams = ProtocolJson.ToElement(new InputClickTileRequest
            {
                Location = string.IsNullOrWhiteSpace(args.Location) ? lastObserved.Location : args.Location,
                X = actor.Tile.X,
                Y = actor.Tile.Y,
                Button = string.IsNullOrWhiteSpace(args.Button) ? "right" : args.Button,
                AllowEventInput = args.AllowEventInput ?? true,
                ScreenOffsetX = args.ScreenOffsetX ?? 32,
                ScreenOffsetY = args.ScreenOffsetY ?? 32,
            });
            var resp = await _session.InvokeAsync("input.click_tile", clickParams, ct);
            if (resp.Error is { } clickError)
                throw new InvalidOperationException($"step '{step.Action}' failed during input.click_tile: {clickError.Message}");
            return;
        }

        await Task.Delay(args.PollMs, ct);
    }

    throw new TimeoutException($"{step.Action} timed out after {args.TimeoutMs}ms waiting for actor_name={args.ActorName}; last observed {FormatEventState(lastObserved)}");
}
```

Add this nested args class near `WaitEventStepArgs`:

```csharp
private sealed class InputClickEventActorStepArgs
{
    public string? ActorName { get; set; }
    public string? Location { get; set; }
    public string? Button { get; set; } = "right";
    public bool? AllowEventInput { get; set; }
    public int? ScreenOffsetX { get; set; }
    public int? ScreenOffsetY { get; set; }
    public int TimeoutMs { get; set; } = 10000;
    public int PollMs { get; set; } = 100;
}
```

- [ ] **Step 6: Add actor dialogue filter support**

Modify `EventActorMatches`:

```csharp
private static bool EventActorMatches(EventState state, WaitEventStepArgs args)
{
    if (string.IsNullOrWhiteSpace(args.ActorName))
        return true;

    foreach (var actor in state.Actors)
    {
        if (!string.Equals(actor.Name, args.ActorName, StringComparison.Ordinal))
            continue;
        if (args.ActorX is not null || args.ActorY is not null)
        {
            if (actor.Tile.X != args.ActorX || actor.Tile.Y != args.ActorY)
                continue;
        }
        if (!string.IsNullOrWhiteSpace(args.ActorDialogueText)
            && !actor.DialogueText.Contains(args.ActorDialogueText, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }
        if (!string.IsNullOrWhiteSpace(args.ActorDialogueTextMatches)
            && !System.Text.RegularExpressions.Regex.IsMatch(actor.DialogueText, args.ActorDialogueTextMatches, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            continue;
        }
        if (!string.IsNullOrWhiteSpace(args.ActorDialogueKey)
            && !string.Equals(actor.DialogueKey, args.ActorDialogueKey, StringComparison.Ordinal))
        {
            continue;
        }
        return true;
    }

    return false;
}
```

Modify `FormatWaitEventFilters` to include the new filters:

```csharp
if (!string.IsNullOrWhiteSpace(args.ActorDialogueText)) filters.Add($"actor_dialogue_text={args.ActorDialogueText}");
if (!string.IsNullOrWhiteSpace(args.ActorDialogueTextMatches)) filters.Add($"actor_dialogue_text_matches={args.ActorDialogueTextMatches}");
if (!string.IsNullOrWhiteSpace(args.ActorDialogueKey)) filters.Add($"actor_dialogue_key={args.ActorDialogueKey}");
```

Modify `FormatEventActors`:

```csharp
return "[" + string.Join(", ", actors.Select(a =>
{
    var dialogue = string.IsNullOrWhiteSpace(a.DialogueText)
        ? string.Empty
        : $" dialogue=\"{a.DialogueText}\"";
    return $"{a.Name}@{a.Tile.X},{a.Tile.Y}{dialogue}";
})) + "]";
```

Add fields to `WaitEventStepArgs`:

```csharp
public string? ActorDialogueText { get; set; }
public string? ActorDialogueTextMatches { get; set; }
public string? ActorDialogueKey { get; set; }
```

- [ ] **Step 7: Add readable step label and screenshot behavior**

In `DescribeStep`, add:

```csharp
"input.click_event_actor" => $"Click event actor {GetStringArg(step.Args, "actor_name") ?? "unknown"}",
```

In `ShouldAutoCaptureStep` test data, add:

```csharp
[InlineData("input.click_event_actor", true)]
```

- [ ] **Step 8: Run focused runner tests and verify GREEN**

Run:

```bash
dotnet test tests/Runner.Tests/ --filter "FullyQualifiedName~InputClickEventActor_ResolvesActorTileThenClicksTile|FullyQualifiedName~WaitEventActive_FiltersByActorDialogueText|FullyQualifiedName~ShouldAutoCaptureStep" --nologo
```

Expected: tests pass.

- [ ] **Step 9: Commit Task 2**

Run:

```bash
git add src/Runner/Scenarios/ScenarioRunner.cs tests/Runner.Tests/ScenarioRunnerTests.cs
git commit -m "feat: click active event actors from scenarios"
```

## Task 3: Add Primary SVE Grange Judging Scenario

**Files:**
- Create: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/37-sve-fair-grange-judging-progression.test.json`

- [ ] **Step 1: Switch/create the matching SVE feature branch**

Run:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded switch -c feature/frobby-sve-slice-29-grange-judging
```

If the branch already exists, use:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded switch feature/frobby-sve-slice-29-grange-judging
```

- [ ] **Step 2: Create the primary click-path scenario**

Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/37-sve-fair-grange-judging-progression.test.json`:

```json
{
  "name": "sve_fair_grange_judging_progression",
  "fixture": "m0spike_436515781",
  "config": { "seed": 42 },
  "steps": [
    { "action": "player.set_money", "args": { "amount": 5000 } },
    { "action": "time.set", "args": { "time": 900, "day": 16, "season": "fall", "year": 1 } },
    { "action": "festival.start", "args": { "location": "Town" } },
    {
      "action": "wait.event_active",
      "args": {
        "location": "Temp",
        "is_festival": true,
        "actor_name": "Lewis",
        "timeout_ms": 30000,
        "poll_ms": 100
      }
    },
    {
      "action": "wait.event_active",
      "args": {
        "location": "Temp",
        "is_festival": true,
        "actor_name": "Sophia",
        "timeout_ms": 30000,
        "poll_ms": 100
      }
    },
    {
      "action": "wait.event_active",
      "args": {
        "location": "Temp",
        "is_festival": true,
        "actor_name": "Andy",
        "timeout_ms": 30000,
        "poll_ms": 100
      }
    },
    {
      "action": "wait.event_active",
      "args": {
        "location": "Temp",
        "is_festival": true,
        "actor_name": "Susan",
        "timeout_ms": 30000,
        "poll_ms": 100
      }
    },
    { "action": "screenshot.capture_next_frame", "args": { "name": "fair-before-grange-judging" } },
    {
      "action": "input.click_event_actor",
      "args": {
        "actor_name": "Lewis",
        "button": "right",
        "timeout_ms": 30000,
        "poll_ms": 100
      }
    },
    {
      "action": "wait.menu",
      "args": {
        "text_matches": "grange|judge|judging|display|ready",
        "ready": true,
        "timeout_ms": 15000,
        "poll_ms": 100
      }
    },
    {
      "action": "event.advance",
      "args": {
        "text_matches": "Yes|Ready|ready|judge|Judge|judging|start|Start|begin|Begin",
        "timeout_ms": 15000,
        "poll_ms": 100
      }
    },
    {
      "action": "wait.event_active",
      "args": {
        "location": "Temp",
        "is_festival": true,
        "actor_name": "Sophia",
        "actor_dialogue_text_matches": "clear out your grange display|How did you place|Everyone worked so hard|Yay! I won",
        "timeout_ms": 70000,
        "poll_ms": 250
      }
    },
    {
      "action": "world.interact_npc",
      "args": { "name": "Sophia" }
    },
    {
      "action": "wait.menu",
      "args": {
        "text_matches": "clear out your grange display|How did you place|Everyone worked so hard|Yay! I won",
        "ready": true,
        "timeout_ms": 30000,
        "poll_ms": 100
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.menu.extra.character == 'Sophia'",
        "message": "After grange judging, interacting with Sophia should open Sophia's SVE judging dialogue"
      }
    },
    { "action": "screenshot.capture_next_frame", "args": { "name": "final" } }
  ],
  "assertions": [
    {
      "type": "content.asset",
      "asset": "Data/Festivals/fall16",
      "asset_type": "data",
      "entry_keys": ["Set-Up_additionalCharacters"],
      "expr": "asset.entries.Set-Up_additionalCharacters.value contains 'Sophia'",
      "message": "SVE should add Sophia to the year-one Stardew Fair actor list"
    },
    {
      "type": "content.asset",
      "asset": "Data/Festivals/fall16",
      "asset_type": "data",
      "entry_keys": ["Set-Up_additionalCharacters"],
      "expr": "asset.entries.Set-Up_additionalCharacters.value contains 'Andy'",
      "message": "SVE should add Andy to the year-one Stardew Fair actor list"
    },
    {
      "type": "content.asset",
      "asset": "Data/Festivals/fall16",
      "asset_type": "data",
      "entry_keys": ["Set-Up_additionalCharacters"],
      "expr": "asset.entries.Set-Up_additionalCharacters.value contains 'Susan'",
      "message": "SVE should add Susan to the year-one Stardew Fair actor list"
    },
    {
      "type": "content.asset",
      "asset": "Strings/StringsFromCSFiles",
      "asset_type": "data",
      "entry_keys": ["SVE_AfterJudging_Sophia", "SVE_AfterJudging_Andy", "SVE_AfterJudging_Susan"],
      "expr": "asset.entries.SVE_AfterJudging_Sophia.value != ''",
      "message": "SVE should add Sophia's after-judging dialogue string"
    },
    {
      "type": "content.asset",
      "asset": "Characters/Dialogue/Sophia",
      "asset_type": "data",
      "entry_keys": ["Fair_Judging"],
      "expr": "asset.entries.Fair_Judging.value != ''",
      "message": "SVE should expose Sophia's Fair judging dialogue"
    }
  ]
}
```

- [ ] **Step 3: Run scenario 37 and verify the primary path**

Run:

```bash
./scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-29-primary tests/sdv/37-sve-fair-grange-judging-progression.test.json
```

Expected if primary path works: `1/1 passed`.

If it fails at `wait.menu` because Lewis click starts judging directly, remove the `wait.menu` and `event.advance` steps and rerun this same command.

If it fails because Lewis click opens dialogue but cannot select the judging prompt, inspect the report screenshots and update the `event.advance.text_matches` regex with the exact visible response text, then rerun this same command.

If it fails because Lewis cannot trigger judging through click/menu automation, leave the scenario in the best failing primary-path form and continue to Task 4.

- [ ] **Step 4: Commit primary SVE scenario only if it passes without fallback**

If scenario 37 passed without Task 4, run:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded add tests/sdv/37-sve-fair-grange-judging-progression.test.json
git -C /home/fintan/stardewRepos/StardewValleyExpanded commit -m "test: add grange judging progression scenario"
```

Then skip Task 4 and continue to Task 5.

## Task 4: Fallback Generic Active Event Method Trigger

Only execute this task if Task 3 proves the player-like Lewis route cannot reliably start grange judging headlessly.

**Files:**
- Create: `src/Protocol/Models/EventInvokeMethodRequest.cs`
- Create: `src/Harness/Handlers/EventInvokeMethodHandler.cs`
- Modify: `src/Harness/ModEntry.cs`
- Test: `tests/Protocol.Tests/EventInvokeMethodSerializationTests.cs`
- Test: `tests/Harness.Tests/EventInvokeMethodHandlerTests.cs`
- Modify: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/37-sve-fair-grange-judging-progression.test.json`

- [ ] **Step 1: Write failing protocol tests**

Create `tests/Protocol.Tests/EventInvokeMethodSerializationTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class EventInvokeMethodSerializationTests
{
    [Fact]
    public void Request_DeserializesSnakeCase()
    {
        var req = JsonSerializer.Deserialize<EventInvokeMethodRequest>(
            "{\"method\":\"initiateGrangeJudging\",\"allow_private\":true}",
            ProtocolJson.Options)!;

        Assert.Equal("initiateGrangeJudging", req.Method);
        Assert.True(req.AllowPrivate);
    }

    [Fact]
    public void Result_SerializesSnakeCase()
    {
        var json = JsonSerializer.Serialize(new EventInvokeMethodResult
        {
            Ok = true,
            Tick = 99,
            Method = "initiateGrangeJudging",
            EventId = "fall16",
            BooleanResult = false,
        }, ProtocolJson.Options);

        Assert.Contains("\"method\":\"initiateGrangeJudging\"", json);
        Assert.Contains("\"event_id\":\"fall16\"", json);
        Assert.Contains("\"boolean_result\":false", json);
    }
}
```

- [ ] **Step 2: Run protocol test and verify RED**

Run:

```bash
dotnet test tests/Protocol.Tests/ --filter "FullyQualifiedName~EventInvokeMethodSerializationTests" --nologo
```

Expected: compile failure because DTOs do not exist.

- [ ] **Step 3: Add fallback protocol DTOs**

Create `src/Protocol/Models/EventInvokeMethodRequest.cs`:

```csharp
namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape for <c>event.invoke_method</c>.</summary>
public sealed class EventInvokeMethodRequest
{
    public string Method { get; set; } = string.Empty;
    public bool AllowPrivate { get; set; }
}

/// <summary>Result shape for <c>event.invoke_method</c>.</summary>
public sealed class EventInvokeMethodResult : MutatorOk
{
    public string Method { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public bool? BooleanResult { get; set; }
}
```

- [ ] **Step 4: Run protocol test and verify GREEN**

Run:

```bash
dotnet test tests/Protocol.Tests/ --filter "FullyQualifiedName~EventInvokeMethodSerializationTests" --nologo
```

Expected: tests pass.

- [ ] **Step 5: Write failing handler tests**

Create `tests/Harness.Tests/EventInvokeMethodHandlerTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class EventInvokeMethodHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => EventInvokeMethodHandler.Handle(null, new FakeEventInvokeMethodWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Handle_BlankMethod_ThrowsInvalidParams(string method)
    {
        var p = JsonSerializer.SerializeToElement(new { method });

        var ex = Assert.Throws<JsonRpcException>(() => EventInvokeMethodHandler.Handle(p, new FakeEventInvokeMethodWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_NoActiveEvent_ThrowsGameStateInvalid()
    {
        var p = JsonSerializer.SerializeToElement(new { method = "PublicMethod" });
        var world = new FakeEventInvokeMethodWorld { ActiveEvent = null };

        var ex = Assert.Throws<JsonRpcException>(() => EventInvokeMethodHandler.Handle(p, world));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_PublicMethod_InvokesActiveEventMethod()
    {
        var p = JsonSerializer.SerializeToElement(new { method = "PublicMethod" });
        var ev = new FakeEvent();
        var world = new FakeEventInvokeMethodWorld { ActiveEvent = ev, Tick = 42, EventId = "fall16" };

        var result = EventInvokeMethodHandler.Handle(p, world);

        Assert.True(ev.PublicInvoked);
        Assert.Equal("PublicMethod", result.GetProperty("method").GetString());
        Assert.Equal("fall16", result.GetProperty("event_id").GetString());
        Assert.Equal(42, result.GetProperty("tick").GetInt32());
        Assert.True(result.GetProperty("boolean_result").GetBoolean());
    }

    [Fact]
    public void Handle_PrivateMethodWithoutAllowPrivate_ThrowsInvalidParams()
    {
        var p = JsonSerializer.SerializeToElement(new { method = "PrivateMethod" });
        var world = new FakeEventInvokeMethodWorld { ActiveEvent = new FakeEvent() };

        var ex = Assert.Throws<JsonRpcException>(() => EventInvokeMethodHandler.Handle(p, world));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_PrivateMethodWithAllowPrivate_InvokesActiveEventMethod()
    {
        var p = JsonSerializer.SerializeToElement(new { method = "PrivateMethod", allow_private = true });
        var ev = new FakeEvent();
        var world = new FakeEventInvokeMethodWorld { ActiveEvent = ev };

        EventInvokeMethodHandler.Handle(p, world);

        Assert.True(ev.PrivateInvoked);
    }

    private sealed class FakeEvent
    {
        public bool PublicInvoked { get; private set; }
        public bool PrivateInvoked { get; private set; }

        public bool PublicMethod()
        {
            PublicInvoked = true;
            return true;
        }

        private bool PrivateMethod()
        {
            PrivateInvoked = true;
            return false;
        }
    }

    private sealed class FakeEventInvokeMethodWorld : IEventInvokeMethodWorld
    {
        public int Tick { get; set; } = 1;
        public string EventId { get; set; } = "event";
        public object? ActiveEvent { get; set; } = new FakeEvent();
    }
}
```

- [ ] **Step 6: Run handler tests and verify RED**

Run:

```bash
dotnet test tests/Harness.Tests/ --filter "FullyQualifiedName~EventInvokeMethodHandlerTests" --nologo
```

Expected: compile failure because handler/interface do not exist.

- [ ] **Step 7: Implement fallback handler**

Create `src/Harness/Handlers/EventInvokeMethodHandler.cs`:

```csharp
using System;
using System.Reflection;
using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>event.invoke_method</c>. Runs on the game thread.</summary>
public static class EventInvokeMethodHandler
{
    public const string Method = "event.invoke_method";

    private static readonly IEventInvokeMethodWorld ProductionWorld = new SdvEventInvokeMethodWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, IEventInvokeMethodWorld world)
    {
        var req = RpcParams.Required<EventInvokeMethodRequest>(paramsElement);
        var method = req.Method?.Trim() ?? string.Empty;
        if (method.Length == 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.method must be non-empty");

        var activeEvent = world.ActiveEvent
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, "event.invoke_method requires an active event");

        var flags = BindingFlags.Instance | BindingFlags.Public;
        if (req.AllowPrivate)
            flags |= BindingFlags.NonPublic;

        var methodInfo = activeEvent.GetType().GetMethod(method, flags, Type.EmptyTypes)
            ?? throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, $"active event has no zero-argument method '{method}'");

        var result = methodInfo.Invoke(activeEvent, Array.Empty<object>());

        return ProtocolJson.ToElement(new EventInvokeMethodResult
        {
            Ok = true,
            Tick = world.Tick,
            Method = method,
            EventId = world.EventId,
            BooleanResult = result is bool b ? b : null,
        });
    }
}

internal interface IEventInvokeMethodWorld
{
    int Tick { get; }
    string EventId { get; }
    object? ActiveEvent { get; }
}

internal sealed class SdvEventInvokeMethodWorld : IEventInvokeMethodWorld
{
    public int Tick => Game1.ticks;
    public object? ActiveEvent => Game1.CurrentEvent ?? Game1.currentLocation?.currentEvent;
    public string EventId
    {
        get
        {
            var ev = this.ActiveEvent;
            if (ev is null)
                return string.Empty;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            return ev.GetType().GetField("id", flags)?.GetValue(ev) as string
                ?? ev.GetType().GetProperty("id", flags)?.GetValue(ev) as string
                ?? string.Empty;
        }
    }
}
```

- [ ] **Step 8: Register fallback RPC**

In `src/Harness/ModEntry.cs`, add registration near the other event methods:

```csharp
_rpc.Register(EventInvokeMethodHandler.Method, p => EventInvokeMethodHandler.Handle(p));
```

Add `event.invoke_method` to the startup log list near `event.start`.

- [ ] **Step 9: Run fallback tests and verify GREEN**

Run:

```bash
dotnet test tests/Protocol.Tests/ --filter "FullyQualifiedName~EventInvokeMethodSerializationTests" --nologo
dotnet test tests/Harness.Tests/ --filter "FullyQualifiedName~EventInvokeMethodHandlerTests" --nologo
```

Expected: tests pass.

- [ ] **Step 10: Update SVE scenario to use fallback trigger**

In `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/37-sve-fair-grange-judging-progression.test.json`, replace the Lewis click/menu trigger block with:

```json
    {
      "action": "event.invoke_method",
      "args": {
        "method": "initiateGrangeJudging",
        "allow_private": true
      }
    },
```

Keep the existing `wait.event_active` actor dialogue and post-judging assertions unchanged.

- [ ] **Step 11: Commit fallback Frobby work**

Run:

```bash
git add src/Protocol/Models/EventInvokeMethodRequest.cs src/Harness/Handlers/EventInvokeMethodHandler.cs src/Harness/ModEntry.cs tests/Protocol.Tests/EventInvokeMethodSerializationTests.cs tests/Harness.Tests/EventInvokeMethodHandlerTests.cs
git commit -m "feat: invoke active event methods for test fallback"
```

- [ ] **Step 12: Run scenario 37 with fallback**

Run:

```bash
./scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-29-fallback tests/sdv/37-sve-fair-grange-judging-progression.test.json
```

Expected: `1/1 passed`.

- [ ] **Step 13: Commit fallback SVE scenario**

Run:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded add tests/sdv/37-sve-fair-grange-judging-progression.test.json
git -C /home/fintan/stardewRepos/StardewValleyExpanded commit -m "test: add grange judging progression scenario"
```

## Task 5: Documentation, TODO, And Regression Verification

**Files:**
- Modify: `README.md`
- Modify: `docs/rpc-schema.md`
- Modify: `docs/wiki/examples.md`
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`
- Modify: `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`

- [ ] **Step 1: Update Frobby README authoring guidance**

In `README.md`, extend the festival/event bullet to mention the new pattern:

```md
- Use `wait.event_active`, `input.click_event_actor`, actor dialogue filters,
  and `event.advance` when testing active festival actors or event-owned
  interactions. Prefer actor-name targeting over hard-coded festival actor
  coordinates; the runner resolves the actor tile and still drives
  `input.click_tile` under the hood.
```

If Task 4 was used, add one sentence:

```md
  Use `event.invoke_method` only as an explicit fallback for active event phases
  that cannot be reached reliably through player-like input.
```

- [ ] **Step 2: Update Frobby RPC docs**

In `docs/rpc-schema.md`, update `state.event` docs so `actors[]` includes:

```json
{
  "name": "Sophia",
  "tile": { "x": 47, "y": 60 },
  "pixel": { "x": 3008, "y": 3840 },
  "facing_direction": 2,
  "current_frame": 4,
  "dialogue_key": "Fair_Judging",
  "dialogue_text": "I worked hard to make these!",
  "dialogue_count": 1
}
```

Add a runner-only subsection for `input.click_event_actor`:

```md
### input.click_event_actor (runner action)

Resolves an active event actor by `actor_name`, then invokes `input.click_tile`
at that actor's current tile. This is useful for festival/event maps where actor
coordinates come from content patches and should not be hard-coded in scenarios.
```

If Task 4 was used, add:

```md
### event.invoke_method

Invokes a zero-argument method on the active Stardew event. This is an advanced
fallback for event phases that cannot be driven reliably through player-like
input. `allow_private` must be true for private event methods. The handler is
mod-neutral and does not special-case event ids or method names.
```

- [ ] **Step 3: Update Frobby wiki examples**

In `docs/wiki/examples.md`, add scenario 37 under "NPCs, Dialogue, Events, And Festivals":

```md
- SVE Fair grange judging progression:
  `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/37-sve-fair-grange-judging-progression.test.json`
```

Add:

```md
Use `input.click_event_actor` when an active event actor should be clicked by
name while still routing through `input.click_tile`.
```

- [ ] **Step 4: Update Frobby SVE TODO**

Change Slice 29 in `SVE_FROBBY_CAPABILITY_TODO.md` from planning to done:

```md
- [x] Done: Slice 29, Stardew Fair grange judging progression.
  - SVE pressure: SVE replaces Stardew's live grange judging flow with custom advanced moves and SVE actor dialogue before and after judging.
  - Frobby goal: prove tests can start the Fair, trigger judging through a player-like Lewis interaction when possible, wait for the live festival progression, and assert SVE judging dialogue side effects.
  - Design spec: `docs/superpowers/specs/2026-05-27-sve-slice-29-grange-judging-design.md`.
  - Done: active event actor tile-click helper, event actor dialogue observability/waits, SVE scenario 37, and generic fallback trigger if Task 4 was needed.
  - Verified: headless SVE scenario 37 passed under the `core` mod set, with scenarios 34 and 32 passing as regressions.
```

If Task 4 was not used, remove "and generic fallback trigger if Task 4 was needed" before committing.

- [ ] **Step 5: Update SVE Frobby docs**

In `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`, add after scenario 34:

```md
Scenario `tests/sdv/37-sve-fair-grange-judging-progression.test.json` covers
SVE's custom Stardew Fair grange judging progression. It starts the Fair, waits
for SVE-added actors, triggers judging through Lewis using Frobby's active event
actor click path when possible, waits for SVE after-judging dialogue to become
observable, and verifies Sophia's post-judging dialogue opens.
```

If Task 4 was used, change "using Frobby's active event actor click path when possible" to "using Frobby's generic active-event fallback trigger after the player-like click path proved unreliable headlessly".

- [ ] **Step 6: Run targeted Frobby test suite**

Run:

```bash
dotnet test tests/Harness.Tests/ --filter "FullyQualifiedName~EventStateProjectorTests|FullyQualifiedName~EventInvokeMethodHandlerTests" --nologo
dotnet test tests/Runner.Tests/ --filter "FullyQualifiedName~InputClickEventActor_ResolvesActorTileThenClicksTile|FullyQualifiedName~WaitEventActive_FiltersByActorDialogueText|FullyQualifiedName~WaitEventActive_FiltersByActorNameAndTile|FullyQualifiedName~WaitEventActive_ActorTimeoutIncludesObservedActorNames" --nologo
dotnet test tests/Protocol.Tests/ --filter "FullyQualifiedName~EventInvokeMethodSerializationTests" --nologo
```

If Task 4 was not used, omit the `EventInvokeMethod*` filters.

Expected: all targeted tests pass.

- [ ] **Step 7: Run SVE live verification headless**

Run scenario 37:

```bash
./scripts/sdv-test --headless --mod-set core --no-build --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-29-final tests/sdv/37-sve-fair-grange-judging-progression.test.json
```

Run regressions:

```bash
./scripts/sdv-test --headless --mod-set core --no-build --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-29-regressions tests/sdv/34-sve-fair-star-token-shop-currency.test.json tests/sdv/32-sve-spirit-eve-actor-dialogue.test.json
```

If `input.click_tile` or `input.click_event_actor` behavior was changed after Task 2, also run:

```bash
./scripts/sdv-test --headless --mod-set core --no-build --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-29-click-regression tests/sdv/36-sve-movie-theater-npc-click.test.json
```

Expected: all live scenarios pass.

- [ ] **Step 8: Commit documentation**

Run:

```bash
git add README.md docs/rpc-schema.md docs/wiki/examples.md SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "docs: document grange judging test flow"
git -C /home/fintan/stardewRepos/StardewValleyExpanded add docs/FROBBY.md
git -C /home/fintan/stardewRepos/StardewValleyExpanded commit -m "docs: document grange judging scenario"
```

- [ ] **Step 9: Final status check**

Run:

```bash
git status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
```

Expected: both repos are clean on their Slice 29 feature branches.

## Self-Review Checklist

- Spec coverage:
  - Player-like Lewis path: Task 2 + Task 3.
  - Generic fallback only if needed: Task 4.
  - SVE Fair actors/content assertions: Task 3.
  - SVE judging/after-judging dialogue side effects: Task 1 + Task 2 + Task 3.
  - Neutral Frobby production code: Tasks 1, 2, and conditional Task 4 avoid SVE identifiers.
  - Headless verification and regressions: Task 5.
- Placeholder scan: no `TBD`, no unspecified test commands, no unnamed files.
- Type consistency:
  - `dialogue_key`, `dialogue_text`, and `dialogue_count` map to `DialogueKey`, `DialogueText`, and `DialogueCount`.
  - `actor_dialogue_text_matches` maps to `ActorDialogueTextMatches`.
  - `input.click_event_actor` is runner-only and calls existing `input.click_tile`.
  - Conditional `event.invoke_method` uses `EventInvokeMethodRequest` / `EventInvokeMethodResult`.
