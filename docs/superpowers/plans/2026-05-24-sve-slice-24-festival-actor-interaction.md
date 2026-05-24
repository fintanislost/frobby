# SVE Slice 24 Festival Actor Interaction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add neutral event/festival actor waits and NPC interaction fallback, then prove it against an SVE Fair actor dialogue scenario.

**Architecture:** Extend the existing event state and NPC interaction paths instead of adding a new event-only RPC. `wait.event_active` will filter the already-projected `state.event.actors`, and `world.interact_npc` will search current-location NPCs first, then active event actors.

**Tech Stack:** C#/.NET 10 runner and tests, C#/.NET 6 SMAPI harness and tests, Frobby JSON scenario runner, SVE repo-local headless scenarios.

---

## File Map

- Modify `src/Runner/Scenarios/ScenarioRunner.cs`
  - Add `actor_name`, `actor_x`, and `actor_y` filters to `wait.event_active`.
  - Include actor filters and last observed actor names in timeout diagnostics.
- Modify `tests/Runner.Tests/ScenarioRunnerTests.cs`
  - Add tests for actor wait success and actor timeout diagnostics.
- Modify `src/Harness/Handlers/WorldInteractNpcHandler.cs`
  - Resolve ordinary current-location NPCs first, then active event actors.
  - Include active event actor names in missing-NPC errors.
- Modify `tests/Harness.Tests/WorldInteractNpcHandlerTests.cs`
  - Add tests for location priority, event actor fallback, and enriched missing-NPC errors.
- Modify `docs/rpc-schema.md`, `docs/dsl-quickstart.md`, `docs/wiki/examples.md`
  - Document event actor waits and the `world.interact_npc` fallback.
- Modify `SVE_FROBBY_CAPABILITY_TODO.md`
  - Add and complete Slice 24 after verification.
- Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/32-sve-fair-actor-dialogue.test.json`
  - Prove the SVE Fair actor dialogue path.
- Modify `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`
  - Document scenario 32.

## Task 1: Runner Event Actor Filters

**Files:**
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`
- Test: `tests/Runner.Tests/ScenarioRunnerTests.cs`

- [ ] **Step 1: Write failing actor wait success test**

Add this test near `WaitEventActive_FiltersByFestivalState`:

```csharp
[Fact]
public async Task WaitEventActive_FiltersByActorNameAndTile()
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
                    "state.event" when eventPolls++ == 0 => JsonDocument.Parse("{\"active\":true,\"event_up\":true,\"location\":\"Town\",\"id\":\"fall16\",\"is_festival\":true,\"actors\":[{\"name\":\"Andy\",\"tile\":{\"x\":18,\"y\":77},\"pixel\":{\"x\":1152,\"y\":4928},\"facing_direction\":1,\"current_frame\":0}],\"dialogue\":null,\"viewport\":{\"x\":0,\"y\":0,\"width\":1280,\"height\":720}}").RootElement,
                    "state.event" => JsonDocument.Parse("{\"active\":true,\"event_up\":true,\"location\":\"Town\",\"id\":\"fall16\",\"is_festival\":true,\"actors\":[{\"name\":\"Sophia\",\"tile\":{\"x\":19,\"y\":77},\"pixel\":{\"x\":1216,\"y\":4928},\"facing_direction\":1,\"current_frame\":0}],\"dialogue\":null,\"viewport\":{\"x\":0,\"y\":0,\"width\":1280,\"height\":720}}").RootElement,
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
        Name = "wait_event_active_actor",
        Steps =
        {
            new ScenarioStep
            {
                Action = "wait.event_active",
                Args = JsonDocument.Parse("{\"id\":\"fall16\",\"location\":\"Town\",\"is_festival\":true,\"actor_name\":\"Sophia\",\"actor_x\":19,\"actor_y\":77,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
            },
        },
    }, cts.Token);

    Assert.True(report.Passed, string.Join("\n", report.Failures));
    Assert.True(eventPolls >= 2);

    cts.Cancel();
    try { await serverTask; } catch (OperationCanceledException) { }
}
```

- [ ] **Step 2: Write failing actor timeout diagnostic test**

Add:

```csharp
[Fact]
public async Task WaitEventActive_ActorTimeoutIncludesObservedActorNames()
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
                    "state.event" => JsonDocument.Parse("{\"active\":true,\"event_up\":true,\"location\":\"Town\",\"id\":\"fall16\",\"is_festival\":true,\"actors\":[{\"name\":\"Andy\",\"tile\":{\"x\":18,\"y\":77},\"pixel\":{\"x\":1152,\"y\":4928},\"facing_direction\":1,\"current_frame\":0}],\"dialogue\":null,\"viewport\":{\"x\":0,\"y\":0,\"width\":1280,\"height\":720}}").RootElement,
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
        Name = "wait_event_active_missing_actor",
        Steps =
        {
            new ScenarioStep
            {
                Action = "wait.event_active",
                Args = JsonDocument.Parse("{\"id\":\"fall16\",\"actor_name\":\"Sophia\",\"timeout_ms\":20,\"poll_ms\":1}").RootElement,
            },
        },
    }, cts.Token);

    Assert.False(report.Passed);
    var failure = Assert.Single(report.Failures);
    Assert.Contains("actor_name=Sophia", failure);
    Assert.Contains("actors=[Andy@18,77]", failure);

    cts.Cancel();
    try { await serverTask; } catch (OperationCanceledException) { }
}
```

- [ ] **Step 3: Run the focused tests and confirm RED**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "WaitEventActive_FiltersByActorNameAndTile|WaitEventActive_ActorTimeoutIncludesObservedActorNames" -v minimal
```

Expected: first test fails because actor filters are ignored or unknown; second test fails because timeout diagnostics do not include actor filters/names.

- [ ] **Step 4: Implement actor filters**

In `ScenarioRunner.cs`, update `InvokeWaitEventActiveAsync`:

```csharp
if (lastObserved.Active
    && (string.IsNullOrWhiteSpace(args.Id) || string.Equals(lastObserved.Id, args.Id, StringComparison.Ordinal))
    && (string.IsNullOrWhiteSpace(args.Location) || string.Equals(lastObserved.Location, args.Location, StringComparison.Ordinal))
    && (args.IsFestival is null || lastObserved.IsFestival == args.IsFestival.Value)
    && EventActorMatches(lastObserved, args))
{
    return;
}
```

Change the timeout throw in `InvokeWaitEventActiveAsync` to:

```csharp
throw new TimeoutException($"{step.Action} timed out after {args.TimeoutMs}ms waiting for event matching {FormatWaitEventFilters(args)}; last observed {FormatEventState(lastObserved)}");
```

Add helpers near `FormatEventState`:

```csharp
private static bool EventActorMatches(EventState state, WaitEventStepArgs args)
{
    if (string.IsNullOrWhiteSpace(args.ActorName))
        return true;

    foreach (var actor in state.Actors)
    {
        if (!string.Equals(actor.Name, args.ActorName, StringComparison.Ordinal))
            continue;
        if (args.ActorX is null && args.ActorY is null)
            return true;
        if (actor.Tile.X == args.ActorX && actor.Tile.Y == args.ActorY)
            return true;
    }

    return false;
}

private static string FormatWaitEventFilters(WaitEventStepArgs args)
{
    var filters = new List<string>();
    if (!string.IsNullOrWhiteSpace(args.Id)) filters.Add($"id={args.Id}");
    if (!string.IsNullOrWhiteSpace(args.Location)) filters.Add($"location={args.Location}");
    if (args.IsFestival is not null) filters.Add($"is_festival={args.IsFestival.Value.ToString().ToLowerInvariant()}");
    if (!string.IsNullOrWhiteSpace(args.ActorName)) filters.Add($"actor_name={args.ActorName}");
    if (args.ActorX is not null && args.ActorY is not null) filters.Add($"actor_tile={args.ActorX},{args.ActorY}");
    return filters.Count == 0 ? "any active event" : string.Join(", ", filters);
}

private static string FormatEventActors(IReadOnlyList<EventActorState> actors)
{
    if (actors.Count == 0)
        return "[]";

    return "[" + string.Join(", ", actors.Select(a => $"{a.Name}@{a.Tile.X},{a.Tile.Y}")) + "]";
}
```

Update `FormatEventState`:

```csharp
private static string FormatEventState(EventState? state)
    => state is null
        ? "nothing"
        : $"active={state.Active}, event_up={state.EventUp}, id='{state.Id}', location='{state.Location}', is_festival={state.IsFestival}, actors={FormatEventActors(state.Actors)}";
```

Update `ParseWaitEventArgs`:

```csharp
if ((args.ActorX is null) != (args.ActorY is null))
    throw new InvalidOperationException($"{step.Action} requires args.actor_x and args.actor_y together");
```

Update `WaitEventStepArgs`:

```csharp
public string? ActorName { get; set; }
public int? ActorX { get; set; }
public int? ActorY { get; set; }
```

- [ ] **Step 5: Run focused runner tests and confirm GREEN**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "WaitEventActive_FiltersByActorNameAndTile|WaitEventActive_ActorTimeoutIncludesObservedActorNames|WaitEventActive_FiltersByFestivalState" -v minimal
```

Expected: all selected tests pass.

## Task 2: `world.interact_npc` Event Actor Fallback

**Files:**
- Modify: `src/Harness/Handlers/WorldInteractNpcHandler.cs`
- Test: `tests/Harness.Tests/WorldInteractNpcHandlerTests.cs`

- [ ] **Step 1: Write failing fallback and priority tests**

Replace `FakeInteractNpcWorld` with a configurable fake that can expose location and event NPCs. Add these tests:

```csharp
[Fact]
public void Handle_NpcPresentInLocationAndEvent_PrefersLocationNpc()
{
    var world = new FakeInteractNpcWorld
    {
        LocationNpcs = { new FakeNpc("Sophia", "location") },
        EventNpcs = { new FakeNpc("Sophia", "event") },
    };
    var p = JsonDocument.Parse("{\"name\":\"Sophia\"}").RootElement;

    WorldInteractNpcHandler.Handle(p, world);

    Assert.Contains("check:location:Sophia", world.Calls);
    Assert.DoesNotContain("check:event:Sophia", world.Calls);
}

[Fact]
public void Handle_NpcMissingFromLocation_InteractsWithEventActor()
{
    var world = new FakeInteractNpcWorld
    {
        EventNpcs = { new FakeNpc("Sophia", "event") },
    };
    var p = JsonDocument.Parse("{\"name\":\"Sophia\"}").RootElement;

    WorldInteractNpcHandler.Handle(p, world);

    Assert.Equal(new[] { "prepare:event:Sophia", "check:event:Sophia", "draw:event:Sophia" }, world.Calls);
}

[Fact]
public void Handle_NpcMissing_IncludesEventActorNamesInError()
{
    var world = new FakeInteractNpcWorld
    {
        EventNpcs = { new FakeNpc("Andy", "event") },
    };
    var p = JsonDocument.Parse("{\"name\":\"Sophia\"}").RootElement;

    var ex = Assert.Throws<JsonRpcException>(() => WorldInteractNpcHandler.Handle(p, world));

    Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    Assert.Contains("Sophia", ex.Message);
    Assert.Contains("Custom_BlueMoonVineyard", ex.Message);
    Assert.Contains("event actors: Andy", ex.Message);
}
```

- [ ] **Step 2: Run focused harness tests and confirm RED**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "WorldInteractNpcHandlerTests" -v minimal
```

Expected: compile failure because the fake/interface does not expose event actors, or assertion failure because the handler does not search active event actors.

- [ ] **Step 3: Implement fallback in the interface and handler**

Update the handler resolution:

```csharp
var npc = world.FindNpcInCurrentLocation(req.Name) ?? world.FindNpcInActiveEvent(req.Name);
if (npc is null)
{
    var actors = string.Join(", ", world.ActiveEventActorNames);
    var suffix = string.IsNullOrWhiteSpace(actors) ? string.Empty : $"; active event actors: {actors}";
    throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
        $"NPC '{req.Name}' not found in current location '{world.CurrentLocationName}'{suffix}");
}
```

Add to `IWorldInteractNpcWorld`:

```csharp
object? FindNpcInActiveEvent(string name);
IReadOnlyList<string> ActiveEventActorNames { get; }
```

In `SdvWorldInteractNpcWorld`, add:

```csharp
public IReadOnlyList<string> ActiveEventActorNames
    => ReadActiveEventNpcs()
        .Select(npc => npc.Name)
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Distinct(StringComparer.Ordinal)
        .ToList();

public object? FindNpcInActiveEvent(string name)
    => ReadActiveEventNpcs().FirstOrDefault(npc => string.Equals(npc.Name, name, StringComparison.Ordinal));

private static IEnumerable<NPC> ReadActiveEventNpcs()
{
    foreach (var ev in new object?[] { Game1.CurrentEvent, Game1.currentLocation?.currentEvent })
    {
        foreach (var actor in ReadActors(ev).OfType<NPC>())
            yield return actor;
    }
}

private static IEnumerable<object?> ReadActors(object? ev)
{
    if (ev is null)
        yield break;

    const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    foreach (var name in new[] { "actors", "Actors", "characters", "Characters", "festivalActors" })
    {
        var type = ev.GetType();
        var value = type.GetField(name, flags)?.GetValue(ev)
            ?? type.GetProperty(name, flags)?.GetValue(ev);
        if (value is IEnumerable enumerable && value is not string)
        {
            foreach (var item in enumerable)
                yield return item;
        }
    }
}
```

Add the needed namespaces:

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
```

- [ ] **Step 4: Update the fake world**

Use this fake shape:

```csharp
private sealed class FakeInteractNpcWorld : IWorldInteractNpcWorld
{
    public int Tick => 123;
    public bool IsWorldReady => true;
    public string CurrentLocationName => "Custom_BlueMoonVineyard";
    public bool HasActiveMenuAfterCheckAction { get; init; }
    public bool HasRenderableDialogueMenuAfterCheckAction { get; init; }
    public bool NpcCanTalk { get; init; } = true;
    public List<FakeNpc> LocationNpcs { get; } = new() { new("Sophia", "location") };
    public List<FakeNpc> EventNpcs { get; } = new();
    public List<string> Calls { get; } = new();

    public object? FindNpcInCurrentLocation(string name)
        => LocationNpcs.FirstOrDefault(npc => npc.Name == name);

    public object? FindNpcInActiveEvent(string name)
        => EventNpcs.FirstOrDefault(npc => npc.Name == name);

    public IReadOnlyList<string> ActiveEventActorNames
        => EventNpcs.Select(npc => npc.Name).ToList();

    public void CheckAction(object npc)
        => Calls.Add($"check:{((FakeNpc)npc).Source}:{((FakeNpc)npc).Name}");

    public void PrepareDialogue(object npc)
        => Calls.Add($"prepare:{((FakeNpc)npc).Source}:{((FakeNpc)npc).Name}");

    public bool HasActiveMenu => HasActiveMenuAfterCheckAction;

    public bool HasEmptyDialogueMenu => HasActiveMenuAfterCheckAction && !HasRenderableDialogueMenuAfterCheckAction;

    public bool CanTalk(object npc)
        => NpcCanTalk;

    public void DrawDialogue(object npc)
        => Calls.Add($"draw:{((FakeNpc)npc).Source}:{((FakeNpc)npc).Name}");
}

private sealed record FakeNpc(string Name, string Source);
```

Update existing assertions to include `location:` in call labels.

- [ ] **Step 5: Run focused harness tests and confirm GREEN**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "WorldInteractNpcHandlerTests" -v minimal
```

Expected: all `WorldInteractNpcHandlerTests` pass, except any intentionally skipped live integration tests remain skipped.

## Task 3: Docs And TODO

**Files:**
- Modify: `docs/rpc-schema.md`
- Modify: `docs/dsl-quickstart.md`
- Modify: `docs/wiki/examples.md`
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Update Frobby RPC docs**

Document that `wait.event_active` supports `actor_name`, `actor_x`, and `actor_y`, and that `world.interact_npc` falls back to active event actors after current-location NPCs.

- [ ] **Step 2: Add quickstart example**

Add a compact festival actor snippet:

```json
{ "action": "festival.start", "args": { "location": "Town" } },
{
  "action": "wait.event_active",
  "args": { "location": "Town", "is_festival": true, "actor_name": "ExampleNpc" }
},
{ "action": "world.interact_npc", "args": { "name": "ExampleNpc" } },
{ "action": "wait.menu", "args": { "text": "festival", "ready": true } }
```

- [ ] **Step 3: Update capability TODO**

Add Slice 24 below Slice 23:

```markdown
- [x] Done: Slice 24, active festival actor interaction.
  - SVE pressure: festival actors can live inside active event state instead of `currentLocation.characters`, so ordinary NPC interaction coverage can miss modded festival dialogue.
  - Frobby goal: add neutral event actor waits and let `world.interact_npc` fall back to active event actors without changing ordinary NPC priority.
  - Design spec: `docs/superpowers/specs/2026-05-24-sve-slice-24-festival-actor-interaction-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-24-sve-slice-24-festival-actor-interaction.md`.
  - Done: `wait.event_active.actor_name` plus optional actor tile filters, event actor names in timeout diagnostics, active-event fallback for `world.interact_npc`, docs, and SVE scenario 32.
  - Verified: headless SVE scenario 32 entered the Stardew Valley Fair, waited for the SVE-added Sophia actor, interacted through `world.interact_npc`, and observed her Fair dialogue.
  - Follow-up candidates: movie theater NPC setup, grange judging command progression, and festival shop UI/purchase flows.
```

## Task 4: SVE Fair Actor Scenario

**Files:**
- Create: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/32-sve-fair-actor-dialogue.test.json`
- Modify: `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`

- [ ] **Step 1: Add scenario 32**

Create:

```json
{
  "name": "sve_fair_actor_dialogue",
  "fixture": "m0spike_436515781",
  "config": { "seed": 42 },
  "steps": [
    { "action": "time.set", "args": { "time": 900, "day": 16, "season": "fall", "year": 1 } },
    { "action": "festival.start", "args": { "location": "Town" } },
    {
      "action": "wait.event_active",
      "args": {
        "location": "Town",
        "is_festival": true,
        "actor_name": "Sophia",
        "timeout_ms": 30000,
        "poll_ms": 100
      }
    },
    { "action": "world.interact_npc", "args": { "name": "Sophia" } },
    {
      "action": "wait.menu",
      "args": {
        "text": "Blue Moon Vineyard",
        "ready": true,
        "timeout_ms": 30000,
        "poll_ms": 100
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.menu.extra.character == 'Sophia'",
        "message": "Fair actor interaction should open Sophia dialogue"
      }
    },
    {
      "action": "wait.ms",
      "args": { "ms": 500 }
    },
    {
      "action": "screenshot.capture_next_frame",
      "args": { "name": "final" }
    }
  ],
  "assertions": []
}
```

- [ ] **Step 2: Update SVE docs**

In `docs/FROBBY.md`, add a short paragraph after the scenario 19 festival paragraph:

```markdown
Scenario `tests/sdv/32-sve-fair-actor-dialogue.test.json` covers active festival actor interaction. It enters the Stardew Valley Fair, waits for SVE's Sophia festival actor through Frobby's neutral event actor filters, interacts with her through `world.interact_npc`, and asserts her Fair dialogue opens.
```

- [ ] **Step 3: Validate scenario JSON/listing**

Run:

```bash
python3 -m json.tool /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/32-sve-fair-actor-dialogue.test.json >/tmp/sve32-jsoncheck.out
dotnet run --project /home/fintan/stardewRepos/frobby/sdv-test-framework/src/Runner/Runner.csproj -- list /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv
```

Expected: JSON command exits 0; list reports `32 ok, 0 invalid`.

## Task 5: Verification And Commits

**Files:**
- No new code files. This task verifies the full changed surface and commits.

- [ ] **Step 1: Run focused unit tests**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "WaitEventActive_FiltersByActorNameAndTile|WaitEventActive_ActorTimeoutIncludesObservedActorNames|WaitEventActive_FiltersByFestivalState" -v minimal
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "WorldInteractNpcHandlerTests" -v minimal
```

Expected: all selected non-skipped tests pass.

- [ ] **Step 2: Run broader Frobby suites**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj -v minimal
dotnet test tests/Harness.Tests/Harness.Tests.csproj -v minimal
```

Expected: both suites pass with only existing skipped integration tests.

- [ ] **Step 3: Run headless SVE scenarios**

Run:

```bash
dotnet run --project /home/fintan/stardewRepos/frobby/sdv-test-framework/src/Runner/Runner.csproj -- repo run --repo-root /home/fintan/stardewRepos/StardewValleyExpanded --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-24-festival-actor /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/32-sve-fair-actor-dialogue.test.json /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/19-sve-spirit-eve-chest.test.json
```

Expected: both scenarios pass. If scenario 32 fails because the stable text differs, inspect the report and use another stable fragment from the projected Sophia Fair dialogue without changing Frobby code.

- [ ] **Step 4: Check diffs**

Run in both repos:

```bash
git diff --check
git status --short --branch
```

Expected: no whitespace errors; only intended files are dirty.

- [ ] **Step 5: Commit Frobby work**

Run in `/home/fintan/stardewRepos/frobby/sdv-test-framework`:

```bash
git add src/Runner/Scenarios/ScenarioRunner.cs tests/Runner.Tests/ScenarioRunnerTests.cs src/Harness/Handlers/WorldInteractNpcHandler.cs tests/Harness.Tests/WorldInteractNpcHandlerTests.cs docs/rpc-schema.md docs/dsl-quickstart.md docs/wiki/examples.md SVE_FROBBY_CAPABILITY_TODO.md docs/superpowers/plans/2026-05-24-sve-slice-24-festival-actor-interaction.md
git commit -m "Support active festival actor interactions"
```

- [ ] **Step 6: Commit SVE work**

Run in `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
git add tests/sdv/32-sve-fair-actor-dialogue.test.json docs/FROBBY.md
git commit -m "Add Fair actor dialogue Frobby scenario"
```

Do not merge the SVE branch unless Fintan explicitly asks.
