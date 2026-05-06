# SVE Slice 2 Event Observability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add neutral Frobby event/cutscene observability and prove it against a core-only SVE event scenario.

**Architecture:** Add protocol DTOs for `state.event`, a harness-side projector/handler registered beside the other `state.*` handlers, runner-side polling waits, and optional dialogue text extras on `state.menu`. Keep event triggering, skipping, and event-seen mutation out of this slice.

**Tech Stack:** C#/.NET, SMAPI/Stardew Valley runtime APIs, xUnit, JSON scenario runner, SVE repo-local Frobby scaffold.

---

## File Structure

- Create `src/Protocol/Models/EventState.cs`: response DTOs for `state.event`.
- Modify `src/Runner.Dsl/State.cs`: add `State.Event()`.
- Modify `tests/Runner.Dsl.Tests/Facets/StateTests.cs`: cover the new DSL method.
- Create `src/Harness/Handlers/EventStateProjector.cs`: isolated projection/reflection logic.
- Create `src/Harness/Handlers/StateEventHandler.cs`: RPC handler that builds a projector source from `Game1`.
- Modify `src/Harness/ModEntry.cs`: register and advertise `state.event`.
- Create `tests/Harness.Tests/EventStateProjectorTests.cs`: projection tests without live SDV.
- Modify `src/Harness/Handlers/StateMenuHandler.cs`: add best-effort readable text extras.
- Modify `tests/Harness.Tests/StateMenuHandlerTests.cs`: cover readable extras through fake menu objects.
- Modify `src/Runner/Scenarios/ScenarioRunner.cs`: add `wait.event_active` and `wait.event_complete`.
- Modify `tests/Runner.Tests/ScenarioRunnerTests.cs`: cover wait success, timeout, and id guard.
- Modify `docs/rpc-schema.md`, `docs/dsl-quickstart.md`, and `README.md`: document `state.event` and waits.
- Modify `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/03-sve-event-observability-krobus.test.json`: add live SVE scenario.
- Modify `/home/fintan/stardewRepos/frobby/sdv-test-framework/SVE_FROBBY_CAPABILITY_TODO.md`: local untracked progress note only; do not stage unless the user changes that convention.

---

### Task 1: Protocol DTOs And DSL Facet

**Files:**
- Create: `src/Protocol/Models/EventState.cs`
- Modify: `src/Runner.Dsl/State.cs`
- Test: `tests/Runner.Dsl.Tests/Facets/StateTests.cs`

- [ ] **Step 1: Write the failing DSL test**

Add this test to `tests/Runner.Dsl.Tests/Facets/StateTests.cs`:

```csharp
[Fact]
public async Task Event_InvokesStateEventAndDeserializes()
{
    SdvTestSession.ResetForTests();
    var inv = new StubInvoker
    {
        NextJson = "{\"active\":true,\"event_up\":true,\"location\":\"BusStop\",\"id\":\"520702\",\"is_festival\":false,\"is_skippable\":true,\"player_control_locked\":true,\"actors\":[{\"name\":\"Krobus\",\"tile\":{\"x\":16,\"y\":23},\"pixel\":{\"x\":1024,\"y\":1472},\"facing_direction\":3,\"current_frame\":0}],\"dialogue\":null,\"viewport\":{\"x\":896,\"y\":1472,\"width\":1280,\"height\":720}}",
    };
    SdvTestSession.InitializeForTests(inv);
    try
    {
        var state = await State.Event();

        Assert.Equal("state.event", inv.LastMethod);
        Assert.Null(inv.LastParams);
        Assert.True(state.Active);
        Assert.Equal("520702", state.Id);
        Assert.Equal("Krobus", Assert.Single(state.Actors).Name);
        Assert.Equal(1280, state.Viewport?.Width);
    }
    finally { SdvTestSession.ResetForTests(); }
}
```

- [ ] **Step 2: Run the failing DSL test**

Run:

```bash
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter StateTests.Event_InvokesStateEventAndDeserializes
```

Expected: FAIL because `State.Event` and `EventState` do not exist.

- [ ] **Step 3: Add protocol models**

Create `src/Protocol/Models/EventState.cs`:

```csharp
using System.Collections.Generic;

namespace SdvTestFramework.Protocol.Models;

/// <summary>Snapshot of the active Stardew event/cutscene. Response shape of <c>state.event</c>.</summary>
public sealed class EventState
{
    public bool Active { get; set; }
    public bool EventUp { get; set; }
    public string Location { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public bool IsFestival { get; set; }
    public bool IsSkippable { get; set; }
    public bool PlayerControlLocked { get; set; }
    public List<EventActorState> Actors { get; set; } = new();
    public EventDialogueState? Dialogue { get; set; }
    public EventViewportState? Viewport { get; set; }
}

public sealed class EventActorState
{
    public string Name { get; set; } = string.Empty;
    public TilePoint Tile { get; set; } = new();
    public PixelPoint Pixel { get; set; } = new();
    public int FacingDirection { get; set; }
    public int CurrentFrame { get; set; }
}

public sealed class EventDialogueState
{
    public string MenuType { get; set; } = string.Empty;
    public string Speaker { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

public sealed class EventViewportState
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class PixelPoint
{
    public int X { get; set; }
    public int Y { get; set; }
}
```

- [ ] **Step 4: Add the DSL method**

Modify `src/Runner.Dsl/State.cs` after `Menu()`:

```csharp
public static async Task<EventState> Event(CancellationToken ct = default)
{
    var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
    var resp = await s.InvokeAsync("state.event", null, ct);
    return Deserialize<EventState>(resp, "state.event");
}
```

- [ ] **Step 5: Run the DSL test**

Run:

```bash
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter StateTests.Event_InvokesStateEventAndDeserializes
```

Expected: PASS.

- [ ] **Step 6: Commit protocol and DSL**

```bash
git add src/Protocol/Models/EventState.cs src/Runner.Dsl/State.cs tests/Runner.Dsl.Tests/Facets/StateTests.cs
git commit -m "feat: add event state protocol model"
```

---

### Task 2: Harness Event Projection And RPC Handler

**Files:**
- Create: `src/Harness/Handlers/EventStateProjector.cs`
- Create: `src/Harness/Handlers/StateEventHandler.cs`
- Modify: `src/Harness/ModEntry.cs`
- Test: `tests/Harness.Tests/EventStateProjectorTests.cs`

- [ ] **Step 1: Write projection tests**

Create `tests/Harness.Tests/EventStateProjectorTests.cs`:

```csharp
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Handlers;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class EventStateProjectorTests
{
    private sealed class FakeEvent
    {
        public string id = "520702";
        public bool skippable = true;
        public bool isFestival = false;
        public List<object> actors = new()
        {
            new FakeActor("Krobus", 16, 23, 1024, 1472, 3, 0),
        };
    }

    private sealed class FakeActor
    {
        public FakeActor(string name, int tileX, int tileY, int pixelX, int pixelY, int facing, int frame)
        {
            Name = name;
            TilePoint = new Point(tileX, tileY);
            Position = new Vector2(pixelX, pixelY);
            FacingDirection = facing;
            Sprite = new FakeSprite { CurrentFrame = frame };
        }

        public string Name { get; }
        public Point TilePoint { get; }
        public Vector2 Position { get; }
        public int FacingDirection { get; }
        public FakeSprite Sprite { get; }
    }

    private sealed class FakeSprite
    {
        public int CurrentFrame { get; set; }
    }

    [Fact]
    public void ToState_Inactive_ReturnsEmptyState()
    {
        var state = EventStateProjector.ToState(new EventProjectionSource
        {
            EventUp = false,
            LocationName = "",
            Viewport = new Rectangle(0, 0, 1280, 720),
        });

        Assert.False(state.Active);
        Assert.False(state.EventUp);
        Assert.Equal("", state.Location);
        Assert.Equal("", state.Id);
        Assert.Empty(state.Actors);
        Assert.Null(state.Dialogue);
        Assert.Null(state.Viewport);
    }

    [Fact]
    public void ToState_ActiveEvent_ProjectsIdActorsFlagsAndViewport()
    {
        var state = EventStateProjector.ToState(new EventProjectionSource
        {
            CurrentEvent = new FakeEvent(),
            EventUp = true,
            LocationName = "BusStop",
            Viewport = new Rectangle(896, 1472, 1280, 720),
        });

        Assert.True(state.Active);
        Assert.True(state.EventUp);
        Assert.Equal("BusStop", state.Location);
        Assert.Equal("520702", state.Id);
        Assert.False(state.IsFestival);
        Assert.True(state.IsSkippable);
        Assert.True(state.PlayerControlLocked);
        Assert.Equal(896, state.Viewport?.X);
        Assert.Equal(1280, state.Viewport?.Width);

        var actor = Assert.Single(state.Actors);
        Assert.Equal("Krobus", actor.Name);
        Assert.Equal(16, actor.Tile.X);
        Assert.Equal(23, actor.Tile.Y);
        Assert.Equal(1024, actor.Pixel.X);
        Assert.Equal(1472, actor.Pixel.Y);
        Assert.Equal(3, actor.FacingDirection);
        Assert.Equal(0, actor.CurrentFrame);
    }
}
```

- [ ] **Step 2: Run the failing projection tests**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter EventStateProjectorTests
```

Expected: FAIL because `EventStateProjector` and `EventProjectionSource` do not exist.

- [ ] **Step 3: Add the projector**

Create `src/Harness/Handlers/EventStateProjector.cs` with these public internal shapes and reflection helpers:

```csharp
using System.Collections;
using System.Reflection;
using Microsoft.Xna.Framework;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Handlers;

internal sealed class EventProjectionSource
{
    public object? CurrentEvent { get; init; }
    public object? LocationEvent { get; init; }
    public bool EventUp { get; init; }
    public string LocationName { get; init; } = string.Empty;
    public Rectangle Viewport { get; init; }
    public object? ActiveMenu { get; init; }
    public IEnumerable<object?> AdditionalActors { get; init; } = Array.Empty<object?>();
}

internal static class EventStateProjector
{
    public static EventState ToState(EventProjectionSource source)
    {
        var ev = source.CurrentEvent ?? source.LocationEvent;
        var active = ev is not null || source.EventUp;
        var state = new EventState
        {
            Active = active,
            EventUp = source.EventUp,
            Location = active ? source.LocationName : string.Empty,
            Id = ev is null ? string.Empty : ReadString(ev, "id", "eventId", "EventId", "ID"),
            IsFestival = ev is not null && ReadBool(ev, "isFestival", "IsFestival"),
            IsSkippable = ev is not null && ReadBool(ev, "skippable", "Skippable", "isSkippable", "IsSkippable"),
            PlayerControlLocked = active,
            Viewport = active
                ? new EventViewportState { X = source.Viewport.X, Y = source.Viewport.Y, Width = source.Viewport.Width, Height = source.Viewport.Height }
                : null,
            Dialogue = StateMenuHandler.TryProjectDialogue(source.ActiveMenu),
        };

        if (!active)
            return state;

        foreach (var actor in ReadActors(ev).Concat(source.AdditionalActors).Where(a => a is not null))
        {
            var projected = ProjectActor(actor!);
            if (!string.IsNullOrWhiteSpace(projected.Name)
                && state.Actors.All(a => !string.Equals(a.Name, projected.Name, StringComparison.Ordinal)))
            {
                state.Actors.Add(projected);
            }
        }

        return state;
    }

    private static IEnumerable<object?> ReadActors(object? ev)
    {
        if (ev is null)
            yield break;

        foreach (var name in new[] { "actors", "Actors", "characters", "Characters", "festivalActors" })
        {
            var value = ReadMember(ev, name);
            if (value is IEnumerable enumerable && value is not string)
            {
                foreach (var item in enumerable)
                    yield return item;
            }
        }
    }

    private static EventActorState ProjectActor(object actor)
    {
        var tile = ReadPoint(actor, "TilePoint", "Tile", "tilePoint", "tile");
        var pixel = ReadVector(actor, "Position", "position");
        var sprite = ReadMember(actor, "Sprite", "sprite");
        return new EventActorState
        {
            Name = ReadString(actor, "Name", "name", "displayName", "DisplayName"),
            Tile = new TilePoint { X = tile.X, Y = tile.Y },
            Pixel = new PixelPoint { X = (int)pixel.X, Y = (int)pixel.Y },
            FacingDirection = ReadInt(actor, "FacingDirection", "facingDirection", "FacingDirectionValue"),
            CurrentFrame = sprite is null ? ReadInt(actor, "CurrentFrame", "currentFrame") : ReadInt(sprite, "CurrentFrame", "currentFrame"),
        };
    }

    private static object? ReadMember(object source, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = source.GetType();
        return type.GetField(name, flags)?.GetValue(source)
            ?? type.GetProperty(name, flags)?.GetValue(source);
    }

    private static string ReadString(object source, params string[] names)
    {
        foreach (var name in names)
        {
            var value = ReadMember(source, name);
            if (value is string s)
                return s;
            if (value is not null && (value.GetType().IsPrimitive || value.GetType().IsEnum))
                return value.ToString() ?? string.Empty;
        }
        return string.Empty;
    }

    private static bool ReadBool(object source, params string[] names)
    {
        foreach (var name in names)
            if (ReadMember(source, name) is bool value)
                return value;
        return false;
    }

    private static int ReadInt(object source, params string[] names)
    {
        foreach (var name in names)
        {
            var value = ReadMember(source, name);
            if (value is int i)
                return i;
            if (value is short s)
                return s;
        }
        return 0;
    }

    private static Point ReadPoint(object source, params string[] names)
    {
        foreach (var name in names)
        {
            var value = ReadMember(source, name);
            if (value is Point p)
                return p;
            if (value is Vector2 v)
                return new Point((int)v.X, (int)v.Y);
        }
        return Point.Zero;
    }

    private static Vector2 ReadVector(object source, params string[] names)
    {
        foreach (var name in names)
            if (ReadMember(source, name) is Vector2 v)
                return v;
        return Vector2.Zero;
    }
}
```

- [ ] **Step 4: Add the RPC handler**

Create `src/Harness/Handlers/StateEventHandler.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>state.event</c>. Runs on the game thread.</summary>
public static class StateEventHandler
{
    public const string Method = "state.event";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var currentLocation = Game1.currentLocation;
        var state = EventStateProjector.ToState(new EventProjectionSource
        {
            CurrentEvent = Game1.CurrentEvent,
            LocationEvent = currentLocation?.currentEvent,
            EventUp = Game1.eventUp,
            LocationName = currentLocation?.NameOrUniqueName ?? currentLocation?.Name ?? string.Empty,
            Viewport = Game1.viewport,
            ActiveMenu = Game1.activeClickableMenu,
            AdditionalActors = Game1.player is null ? Array.Empty<object?>() : new object?[] { Game1.player },
        });
        return ProtocolJson.ToElement(state);
    }
}
```

- [ ] **Step 5: Register the RPC**

Modify `src/Harness/ModEntry.cs`:

```csharp
_rpc.Register(StateEventHandler.Method, p => StateEventHandler.Handle(p));
```

Place it with the other state registrations after `StateMenuHandler`.

Also update the harness log string so the state methods include `state.event`.

- [ ] **Step 6: Run projection tests**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter EventStateProjectorTests
```

Expected: PASS.

- [ ] **Step 7: Commit harness event RPC**

```bash
git add src/Harness/Handlers/EventStateProjector.cs src/Harness/Handlers/StateEventHandler.cs src/Harness/ModEntry.cs tests/Harness.Tests/EventStateProjectorTests.cs
git commit -m "feat: expose active event state"
```

---

### Task 3: Dialogue And Message Text Extras

**Files:**
- Modify: `src/Harness/Handlers/StateMenuHandler.cs`
- Test: `tests/Harness.Tests/StateMenuHandlerTests.cs`

- [ ] **Step 1: Write failing menu text tests**

Add fake menu types and tests to `tests/Harness.Tests/StateMenuHandlerTests.cs`:

```csharp
[Fact]
public void AddReadableTextExtras_AddsDialogueTextFromFakeMenu()
{
    var state = new MenuState { Type = "DialogueBox", Present = true };

    StateMenuHandler.AddReadableTextExtras(state, new FakeDialogueMenu());

    Assert.Equal("Camilla", state.Extra["character"]);
    Assert.Equal("Welcome to the grove.", state.Extra["dialogue_text"]);
}

[Fact]
public void TryProjectDialogue_ReturnsNullWhenMenuIsNull()
{
    Assert.Null(StateMenuHandler.TryProjectDialogue(null));
}

[Fact]
public void TryProjectDialogue_ProjectsReadableDialogue()
{
    var projected = StateMenuHandler.TryProjectDialogue(new FakeDialogueMenu());

    Assert.NotNull(projected);
    Assert.Equal("FakeDialogueMenu", projected!.MenuType);
    Assert.Equal("Camilla", projected.Speaker);
    Assert.Equal("Welcome to the grove.", projected.Text);
}

private sealed class FakeDialogueMenu
{
    public object characterDialogue = new FakeCharacterDialogue();
    public string dialogue = "Welcome to the grove.";
}

private sealed class FakeCharacterDialogue
{
    public FakeSpeaker speaker = new();
}

private sealed class FakeSpeaker
{
    public string Name = "Camilla";
}
```

- [ ] **Step 2: Run the failing menu tests**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "StateMenuHandlerTests"
```

Expected: FAIL because `AddReadableTextExtras` and `TryProjectDialogue` do not exist.

- [ ] **Step 3: Add readable text helpers**

Modify `src/Harness/Handlers/StateMenuHandler.cs`:

```csharp
using SdvTestFramework.Protocol.Models;
```

Keep the existing `DialogueBox` branch, then call this before returning:

```csharp
AddReadableTextExtras(state, menu);
```

Add these internal helpers:

```csharp
internal static EventDialogueState? TryProjectDialogue(object? menu)
{
    if (menu is null)
        return null;

    var text = ReadFirstString(menu, "dialogue", "currentDialogue", "message", "text", "question");
    var speaker = ReadNestedSpeaker(menu);
    if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(speaker))
        return null;

    return new EventDialogueState
    {
        MenuType = menu.GetType().Name,
        Speaker = speaker,
        Text = text,
    };
}

internal static void AddReadableTextExtras(MenuState state, object menu)
{
    var projected = TryProjectDialogue(menu);
    if (projected is null)
        return;

    if (!string.IsNullOrWhiteSpace(projected.Speaker))
        state.Extra["character"] = projected.Speaker;

    if (!string.IsNullOrWhiteSpace(projected.Text))
    {
        var key = state.Type.Contains("Question", StringComparison.OrdinalIgnoreCase)
            ? "question_text"
            : state.Type.Contains("Message", StringComparison.OrdinalIgnoreCase)
                ? "message_text"
                : "dialogue_text";
        state.Extra[key] = projected.Text;
    }
}
```

Add private reflection helpers:

```csharp
private static string ReadFirstString(object source, params string[] names)
{
    foreach (var name in names)
    {
        var value = ReadMember(source, name);
        if (value is string text && !string.IsNullOrWhiteSpace(text))
            return text;
    }
    return string.Empty;
}

private static string ReadNestedSpeaker(object source)
{
    var dialogue = ReadMember(source, "characterDialogue");
    var speaker = dialogue is null ? null : ReadMember(dialogue, "speaker");
    return speaker is null ? string.Empty : ReadFirstString(speaker, "Name", "name", "displayName");
}

private static object? ReadMember(object source, string name)
{
    const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    var type = source.GetType();
    return type.GetField(name, flags)?.GetValue(source)
        ?? type.GetProperty(name, flags)?.GetValue(source);
}
```

- [ ] **Step 4: Run menu tests**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "StateMenuHandlerTests"
```

Expected: PASS.

- [ ] **Step 5: Commit menu observability**

```bash
git add src/Harness/Handlers/StateMenuHandler.cs tests/Harness.Tests/StateMenuHandlerTests.cs
git commit -m "feat: expose readable event dialogue text"
```

---

### Task 4: Runner Event Wait Actions

**Files:**
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`
- Test: `tests/Runner.Tests/ScenarioRunnerTests.cs`

- [ ] **Step 1: Write failing wait tests**

Add these tests to `tests/Runner.Tests/ScenarioRunnerTests.cs`:

```csharp
[Fact]
public async Task WaitEventActive_PollsStateEventUntilActive()
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
                    "state.event" when eventPolls++ == 0 => JsonDocument.Parse("{\"active\":false,\"event_up\":false,\"location\":\"\",\"id\":\"\",\"actors\":[],\"dialogue\":null,\"viewport\":null}").RootElement,
                    "state.event" => JsonDocument.Parse("{\"active\":true,\"event_up\":true,\"location\":\"BusStop\",\"id\":\"520702\",\"actors\":[],\"dialogue\":null,\"viewport\":{\"x\":0,\"y\":0,\"width\":1280,\"height\":720}}").RootElement,
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
        Name = "wait_event_active",
        Steps = new()
        {
            new ScenarioStep
            {
                Action = "wait.event_active",
                Args = JsonDocument.Parse("{\"id\":\"520702\",\"location\":\"BusStop\",\"timeout_ms\":1000,\"poll_ms\":10}").RootElement,
            },
        },
    }, cts.Token);

    Assert.True(report.Passed, string.Join("\n", report.Failures));
    Assert.True(eventPolls >= 2);

    cts.Cancel();
    try { await serverTask; } catch (OperationCanceledException) { }
}

[Fact]
public async Task WaitEventComplete_WithId_WaitsForTargetEventBeforeCompletion()
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
                    "state.event" when eventPolls++ == 0 => JsonDocument.Parse("{\"active\":false,\"event_up\":false,\"location\":\"\",\"id\":\"\",\"actors\":[],\"dialogue\":null,\"viewport\":null}").RootElement,
                    "state.event" when eventPolls == 2 => JsonDocument.Parse("{\"active\":true,\"event_up\":true,\"location\":\"BusStop\",\"id\":\"520702\",\"actors\":[],\"dialogue\":null,\"viewport\":{\"x\":0,\"y\":0,\"width\":1280,\"height\":720}}").RootElement,
                    "state.event" => JsonDocument.Parse("{\"active\":false,\"event_up\":false,\"location\":\"BusStop\",\"id\":\"\",\"actors\":[],\"dialogue\":null,\"viewport\":null}").RootElement,
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
        Name = "wait_event_complete",
        Steps = new()
        {
            new ScenarioStep
            {
                Action = "wait.event_complete",
                Args = JsonDocument.Parse("{\"id\":\"520702\",\"timeout_ms\":1000,\"poll_ms\":10}").RootElement,
            },
        },
    }, cts.Token);

    Assert.True(report.Passed, string.Join("\n", report.Failures));
    Assert.True(eventPolls >= 3);

    cts.Cancel();
    try { await serverTask; } catch (OperationCanceledException) { }
}
```

- [ ] **Step 2: Run the failing wait tests**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "WaitEvent"
```

Expected: FAIL because the runner sends unknown RPC methods `wait.event_active` and `wait.event_complete`.

- [ ] **Step 3: Add wait dispatch**

Modify the step dispatch in `src/Runner/Scenarios/ScenarioRunner.cs` after `wait.location`:

```csharp
else if (step.Action == "wait.event_active")
{
    await InvokeWaitEventActiveAsync(step, ct);
}
else if (step.Action == "wait.event_complete")
{
    await InvokeWaitEventCompleteAsync(step, ct);
}
```

- [ ] **Step 4: Add wait helpers**

Add these methods near `InvokeWaitLocationAsync`:

```csharp
private async Task InvokeWaitEventActiveAsync(ScenarioStep step, CancellationToken ct)
{
    var args = ParseWaitEventArgs(step);
    var elapsed = Stopwatch.StartNew();
    EventState? lastObserved = null;

    while (elapsed.ElapsedMilliseconds < args.TimeoutMs)
    {
        ct.ThrowIfCancellationRequested();
        lastObserved = await ReadEventStateAsync(step.Action, ct);
        if (lastObserved.Active
            && (string.IsNullOrWhiteSpace(args.Id) || string.Equals(lastObserved.Id, args.Id, StringComparison.Ordinal))
            && (string.IsNullOrWhiteSpace(args.Location) || string.Equals(lastObserved.Location, args.Location, StringComparison.Ordinal)))
        {
            return;
        }

        await Task.Delay(args.PollMs, ct);
    }

    throw new TimeoutException($"{step.Action} timed out after {args.TimeoutMs}ms; last observed {FormatEventState(lastObserved)}");
}

private async Task InvokeWaitEventCompleteAsync(ScenarioStep step, CancellationToken ct)
{
    var args = ParseWaitEventArgs(step);
    var elapsed = Stopwatch.StartNew();
    var sawRequestedId = string.IsNullOrWhiteSpace(args.Id);
    EventState? lastObserved = null;

    while (elapsed.ElapsedMilliseconds < args.TimeoutMs)
    {
        ct.ThrowIfCancellationRequested();
        lastObserved = await ReadEventStateAsync(step.Action, ct);
        if (!sawRequestedId
            && lastObserved.Active
            && string.Equals(lastObserved.Id, args.Id, StringComparison.Ordinal))
        {
            sawRequestedId = true;
        }

        if (sawRequestedId && !lastObserved.Active && !lastObserved.EventUp)
            return;

        await Task.Delay(args.PollMs, ct);
    }

    throw new TimeoutException($"{step.Action} timed out after {args.TimeoutMs}ms; last observed {FormatEventState(lastObserved)}");
}

private async Task<EventState> ReadEventStateAsync(string action, CancellationToken ct)
{
    var resp = await _session.InvokeAsync("state.event", params_: null, ct);
    if (resp.Error is { } error)
        throw new InvalidOperationException($"{action} failed during state.event: {error.Message}");
    if (resp.Result is not { } result)
        return new EventState();
    return JsonSerializer.Deserialize<EventState>(result.GetRawText(), ProtocolJson.Options) ?? new EventState();
}

private static WaitEventStepArgs ParseWaitEventArgs(ScenarioStep step)
{
    var args = step.Args is { ValueKind: JsonValueKind.Object } obj
        ? JsonSerializer.Deserialize<WaitEventStepArgs>(obj.GetRawText(), ProtocolJson.Options) ?? new WaitEventStepArgs()
        : new WaitEventStepArgs();

    if (args.TimeoutMs < 1)
        throw new InvalidOperationException($"{step.Action} requires args.timeout_ms >= 1");
    if (args.PollMs < 1)
        throw new InvalidOperationException($"{step.Action} requires args.poll_ms >= 1");
    return args;
}

private static string FormatEventState(EventState? state)
    => state is null
        ? "nothing"
        : $"active={state.Active}, event_up={state.EventUp}, id='{state.Id}', location='{state.Location}'";
```

Add this args class near `WaitLocationStepArgs`:

```csharp
private sealed class WaitEventStepArgs
{
    public string? Id { get; set; }
    public string? Location { get; set; }
    public int TimeoutMs { get; set; } = 10000;
    public int PollMs { get; set; } = 100;
}
```

- [ ] **Step 5: Add labels and screenshot policy**

Modify `DescribeStep`:

```csharp
"wait.event_active" => $"Wait for event {GetStringArg(step.Args, "id") ?? "active"}",
"wait.event_complete" => $"Wait for event {GetStringArg(step.Args, "id") ?? "active"} to complete",
```

Modify `ShouldAutoCaptureStep` so both event waits return false:

```csharp
"wait.event_active" => false,
"wait.event_complete" => false,
```

- [ ] **Step 6: Run wait tests**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "WaitEvent"
```

Expected: PASS.

- [ ] **Step 7: Commit runner waits**

```bash
git add src/Runner/Scenarios/ScenarioRunner.cs tests/Runner.Tests/ScenarioRunnerTests.cs
git commit -m "feat: add event wait scenario steps"
```

---

### Task 5: Docs Coverage

**Files:**
- Modify: `docs/rpc-schema.md`
- Modify: `docs/dsl-quickstart.md`
- Modify: `README.md`

- [ ] **Step 1: Confirm schema remains compatible**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter ScenarioLoaderTests
```

Expected: PASS because scenario steps already accept any non-empty `action` string and object `args`.

- [ ] **Step 2: Document `state.event` in `docs/rpc-schema.md`**

Add a new `### state.event` section after `state.menu`:

````markdown
### state.event

Returns a best-effort snapshot of the active Stardew event/cutscene. It is inactive at the title screen, in normal gameplay, and after an event completes.

Request:

```json
{ "jsonrpc": "2.0", "id": 9, "method": "state.event" }
```

Inactive response:

```json
{
  "active": false,
  "event_up": false,
  "location": "",
  "id": "",
  "actors": [],
  "dialogue": null,
  "viewport": null
}
```

Active response:

```json
{
  "active": true,
  "event_up": true,
  "location": "BusStop",
  "id": "520702",
  "is_festival": false,
  "is_skippable": true,
  "player_control_locked": true,
  "actors": [
    { "name": "Krobus", "tile": { "x": 16, "y": 23 }, "pixel": { "x": 1024, "y": 1472 }, "facing_direction": 3, "current_frame": 0 }
  ],
  "dialogue": null,
  "viewport": { "x": 896, "y": 1472, "width": 1280, "height": 720 }
}
```

`id`, `is_festival`, `is_skippable`, actors, and dialogue are best-effort fields read from runtime state. Missing Stardew fields are omitted or returned as default values rather than failing the RPC.
````

- [ ] **Step 3: Document waits in `docs/rpc-schema.md`**

Add runner action docs near `wait.location`:

````markdown
### wait.event_active

Runner-only scenario action. Polls `state.event` until an event is active.

Args:

```json
{ "id": "520702", "location": "BusStop", "timeout_ms": 10000, "poll_ms": 100 }
```

`id` and `location` are optional filters.

### wait.event_complete

Runner-only scenario action. Polls `state.event` until `active == false` and `event_up == false`.

Args:

```json
{ "id": "520702", "timeout_ms": 30000, "poll_ms": 100 }
```

When `id` is supplied, the wait must first observe that active id before completion is accepted.
````

- [ ] **Step 4: Update quickstart docs**

Add this short scenario fragment to `docs/dsl-quickstart.md`:

```json
{ "action": "wait.event_active", "args": { "id": "520702", "timeout_ms": 10000 } },
{ "action": "state.assert", "args": { "expr": "state.event.actors contains name 'Krobus'" } },
{ "action": "screenshot.capture_next_frame", "args": { "name": "active-event" } },
{ "action": "wait.event_complete", "args": { "id": "520702", "timeout_ms": 30000 } }
```

Add one sentence to `README.md` near the current RPC/tool list:

```markdown
Event/cutscene observation is available through `state.event`, `wait.event_active`, and `wait.event_complete`; active-event screenshots should use live or next-frame capture because `freeze.begin` still rejects cutscenes.
```

- [ ] **Step 5: Run docs-related tests**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "ScenarioLoaderTests|RunCommandTests"
```

Expected: PASS.

- [ ] **Step 6: Commit docs**

```bash
git add docs/rpc-schema.md docs/dsl-quickstart.md README.md
git commit -m "docs: document event observability"
```

---

### Task 6: SVE Core Event Scenario And Verification

**Files:**
- Create: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/03-sve-event-observability-krobus.test.json`
- Modify: `/home/fintan/stardewRepos/frobby/sdv-test-framework/SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Add the SVE scenario**

Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/03-sve-event-observability-krobus.test.json`:

```json
{
  "name": "sve_event_observability_krobus",
  "fixture": "m0spike_436515781",
  "config": { "seed": 42 },
  "steps": [
    {
      "action": "time.set",
      "args": { "time": 900, "day": 2, "season": "spring", "year": 1 }
    },
    {
      "action": "player.warp",
      "args": { "location": "BusStop", "x": 0, "y": 23 }
    },
    {
      "action": "wait.event_active",
      "args": { "id": "520702", "location": "BusStop", "timeout_ms": 10000, "poll_ms": 100 }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.event.active == true",
        "message": "SVE Krobus BusStop event should become active"
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.event.actors contains name 'Krobus'",
        "message": "Event actor list should include Krobus"
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.event.viewport.width != 0",
        "message": "Active event should expose viewport dimensions"
      }
    },
    {
      "action": "screenshot.capture_next_frame",
      "args": { "name": "active-event" }
    },
    {
      "action": "wait.event_complete",
      "args": { "id": "520702", "timeout_ms": 30000, "poll_ms": 100 }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.event.active == false",
        "message": "SVE Krobus BusStop event should complete without manual input"
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
  "assertions": []
}
```

- [ ] **Step 2: Run full Frobby tests**

Run:

```bash
dotnet test
```

Expected: PASS, with the existing live-SDV tests still skipped.

- [ ] **Step 3: Run Starberg smoke**

Run from `/home/fintan/stardewRepos/stonks`:

```bash
./scripts/sdv-test --no-build tests/sdv/01-starberg-terminal-open.test.json
```

Expected: PASS under headless mode.

- [ ] **Step 4: Run existing SVE scenarios**

Run from `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
./scripts/sdv-test --no-build tests/sdv/01-sve-core-loads.test.json tests/sdv/02-sve-custom-locations-register.test.json
```

Expected: PASS under headless mode.

- [ ] **Step 5: Run the new SVE event scenario**

Run from `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
./scripts/sdv-test --no-build tests/sdv/03-sve-event-observability-krobus.test.json
```

Expected: PASS. The report should include `active-event` and `final` screenshots.

- [ ] **Step 6: Update the local capability list**

Edit `/home/fintan/stardewRepos/frobby/sdv-test-framework/SVE_FROBBY_CAPABILITY_TODO.md` so Slice 2 records:

```markdown
- Done: event observability foundation (`state.event`, `wait.event_active`, `wait.event_complete`, and readable dialogue/menu extras) verified against SVE scenario 03.
- Pending Slice 2 follow-up: deterministic event triggering, event skipping/advance controls, choice/fork selection, and event-seen setup helpers.
```

Leave the file untracked.

- [ ] **Step 7: Commit the SVE scenario**

Run from `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
git add tests/sdv/03-sve-event-observability-krobus.test.json
git commit -m "test: add SVE event observability scenario"
```

- [ ] **Step 8: Confirm final Frobby implementation state**

Run from `/home/fintan/stardewRepos/frobby/sdv-test-framework`:

```bash
git status --short
```

Expected tracked dirty files: none. Expected untracked file: `SVE_FROBBY_CAPABILITY_TODO.md`.

---

## Self-Review Checklist

- Spec coverage: `state.event`, runner waits, dialogue/menu text extras, freeze compatibility, SVE scenario, Starberg smoke, and docs are all assigned to tasks.
- Scope guard: no `event.trigger`, no skip/advance, no fork selection, no event-seen mutation.
- Type consistency: plan uses `EventState`, `EventActorState`, `EventDialogueState`, `EventViewportState`, `PixelPoint`, and existing `TilePoint` consistently.
- State assertion compatibility: scenario expressions use the existing `state.<method>` and `contains name` assertion grammar.
- SVE core target: scenario uses SVE’s `data/events/busstop` entry `520702`, which is part of the core SVE Content Patcher pack and has no relationship/event-seen prerequisites.
