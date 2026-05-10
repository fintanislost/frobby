# SVE Slice 9 Combat Lifecycle Drops Hazards Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add neutral Frobby support for combat outcome testing: runtime debris observation, debris waits, player health waits, and SVE proof scenarios for combat lifecycle/passive combat behavior.

**Architecture:** Keep Frobby content-agnostic. Add debris as an additive `state.location` collection, implement `wait.player` as runner-side polling over existing `state.player`, and add runner-side combat retargeting over generic monster state so moving monsters can be attacked without hard-coded stale coordinates. SVE owns all mod-specific locations, event IDs, item IDs, and scenario coordinates.

**Tech Stack:** C#/.NET 10, xUnit, SMAPI/Stardew Valley runtime APIs, Frobby JSON-RPC protocol, Frobby scenario runner JSON, SVE repo-local `scripts/sdv-test`.

---

## Branch And Repo Notes

Frobby work happens in `/home/fintan/stardewRepos/frobby/sdv-test-framework`.

Recommended Frobby branch:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework switch -c feature/sve-slice-9-combat-lifecycle
```

SVE scenario work happens in `/home/fintan/stardewRepos/StardewValleyExpanded`.

Recommended SVE branch, created from the current Frobby/SVE test branch that already contains scenarios 01-12:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded switch -c feature/frobby-sve-slice-9-combat-lifecycle
```

Do not merge SVE back to `master` unless the user explicitly asks. Frobby can merge to `main` after review and verification.

## File Structure

Frobby:

- Modify `src/Protocol/Models/LocationState.cs`
  - Add `LocationState.Debris`.
  - Add `DebrisSummary`.
- Modify `src/Harness/Handlers/LocationContentProjector.cs`
  - Add `ProjectDebris`.
  - Add reflection-tolerant item debris projection helpers.
- Modify `src/Harness/Handlers/LocationStateProjector.cs`
  - Populate `LocationState.Debris`.
- Modify `src/Runner/Scenarios/ScenarioRunner.cs`
  - Permit `wait.location_content` with `collection: "debris"`.
  - Add generic filters for `runtime_type`, `stack`, `quality`, and `category`.
  - Add `wait.player`.
  - Add runner-side combat target retargeting over monster state.
- Modify docs:
  - `README.md`
  - `docs/rpc-schema.md`
  - `docs/dsl-quickstart.md`
  - `SVE_FROBBY_CAPABILITY_TODO.md`
- Tests:
  - `tests/Protocol.Tests/LocationStateSerializationTests.cs`
  - `tests/Harness.Tests/LocationContentProjectorTests.cs`
  - `tests/Runner.Tests/ScenarioRunnerTests.cs`

SVE:

- Add `tests/sdv/13-sve-combat-lifecycle-debris.test.json` after a stable kill/removal target is confirmed.
- Add `tests/sdv/14-sve-passive-shadow-combat-state.test.json` only if the passive-shadow proof is stable headlessly.
- Modify `docs/FROBBY.md`.

---

### Task 1: Add Debris To The Protocol Model

**Files:**
- Modify: `src/Protocol/Models/LocationState.cs`
- Test: `tests/Protocol.Tests/LocationStateSerializationTests.cs`

- [ ] **Step 1: Write the failing serialization test**

In `tests/Protocol.Tests/LocationStateSerializationTests.cs`, add a `Debris` initializer to the existing `LocationState` object in `Serialize_SnakeCaseFields`:

```csharp
Debris = new()
{
    new DebrisSummary
    {
        Tile = new TilePoint { X = 15, Y = 16 },
        Pixel = new PixelPoint { X = 960, Y = 1024 },
        Kind = "ItemDebris",
        Id = "769",
        QualifiedId = "(O)769",
        Name = "Void Essence",
        Stack = 2,
        Quality = 0,
        Category = -2,
        RuntimeType = "Debris",
    },
},
```

Add this assertion after the existing `objects` assertion:

```csharp
Assert.Contains("\"debris\":[{\"tile\":{\"x\":15,\"y\":16},\"pixel\":{\"x\":960,\"y\":1024},\"kind\":\"ItemDebris\",\"id\":\"769\",\"qualified_id\":\"(O)769\",\"name\":\"Void Essence\",\"stack\":2,\"quality\":0,\"category\":-2,\"runtime_type\":\"Debris\"}]", json);
```

- [ ] **Step 2: Run the protocol test and verify RED**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter LocationStateSerializationTests
```

Expected: FAIL because `LocationState.Debris` and `DebrisSummary` do not exist.

- [ ] **Step 3: Implement the protocol model**

In `src/Protocol/Models/LocationState.cs`, add this property to `LocationState` after `Objects`:

```csharp
/// <summary>Transient world debris such as dropped item chunks and combat loot.</summary>
public List<DebrisSummary> Debris { get; set; } = new();
```

Add this class after `ObjectSummary`:

```csharp
/// <summary>Transient debris descriptor. Some fields are best-effort because Stardew debris can be non-item visual debris.</summary>
public sealed class DebrisSummary
{
    public TilePoint Tile { get; set; } = new();
    public PixelPoint? Pixel { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string QualifiedId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int? Stack { get; set; }
    public int? Quality { get; set; }
    public int? Category { get; set; }
    public string RuntimeType { get; set; } = string.Empty;
}
```

`PixelPoint` already exists in `src/Protocol/Models/EventState.cs` in the same namespace.

- [ ] **Step 4: Run the protocol test and verify GREEN**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter LocationStateSerializationTests
```

Expected: PASS.

- [ ] **Step 5: Commit Task 1**

```bash
git add src/Protocol/Models/LocationState.cs tests/Protocol.Tests/LocationStateSerializationTests.cs
git commit -m "feat: add location debris protocol model"
```

---

### Task 2: Project Runtime Debris From Locations

**Files:**
- Modify: `src/Harness/Handlers/LocationContentProjector.cs`
- Modify: `src/Harness/Handlers/LocationStateProjector.cs`
- Test: `tests/Harness.Tests/LocationContentProjectorTests.cs`

- [ ] **Step 1: Write failing harness projection tests**

Append these tests to `tests/Harness.Tests/LocationContentProjectorTests.cs`:

```csharp
[Fact]
public void ProjectDebris_ReadsItemDebrisFields()
{
    var debris = new FakeDebris
    {
        position = new Vector2(960, 1024),
        item = new FakeDebrisItem
        {
            ItemId = "769",
            QualifiedItemId = "(O)769",
            DisplayName = "Void Essence",
            Stack = 2,
            Quality = 0,
            Category = -2,
        },
    };

    var summary = LocationContentProjector.ProjectDebrisForTests(debris);

    Assert.Equal(15, summary.Tile.X);
    Assert.Equal(16, summary.Tile.Y);
    Assert.NotNull(summary.Pixel);
    Assert.Equal(960, summary.Pixel!.X);
    Assert.Equal(1024, summary.Pixel.Y);
    Assert.Equal("ItemDebris", summary.Kind);
    Assert.Equal("769", summary.Id);
    Assert.Equal("(O)769", summary.QualifiedId);
    Assert.Equal("Void Essence", summary.Name);
    Assert.Equal(2, summary.Stack);
    Assert.Equal(0, summary.Quality);
    Assert.Equal(-2, summary.Category);
    Assert.Equal("FakeDebris", summary.RuntimeType);
}

[Fact]
public void ProjectDebris_ToleratesNonItemDebris()
{
    var debris = new FakeVisualDebris
    {
        position = new Vector2(64, 128),
        debrisType = "spark",
    };

    var summary = LocationContentProjector.ProjectDebrisForTests(debris);

    Assert.Equal(1, summary.Tile.X);
    Assert.Equal(2, summary.Tile.Y);
    Assert.Equal("VisualDebris", summary.Kind);
    Assert.Equal("spark", summary.Name);
    Assert.Equal(string.Empty, summary.Id);
    Assert.Equal(string.Empty, summary.QualifiedId);
    Assert.Equal("FakeVisualDebris", summary.RuntimeType);
}
```

Add these fake types near the existing fake types:

```csharp
private sealed class FakeDebris
{
    public Vector2 position;
    public FakeDebrisItem? item;
}

private sealed class FakeVisualDebris
{
    public Vector2 position;
    public string debrisType = string.Empty;
}

private sealed class FakeDebrisItem
{
    public string ItemId = string.Empty;
    public string QualifiedItemId = string.Empty;
    public string DisplayName = string.Empty;
    public int Stack;
    public int Quality;
    public int Category;
}
```

- [ ] **Step 2: Run the harness tests and verify RED**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter LocationContentProjectorTests
```

Expected: FAIL because `ProjectDebrisForTests` does not exist.

- [ ] **Step 3: Implement debris projection**

In `src/Harness/Handlers/LocationContentProjector.cs`, add this public projector near `ProjectMonsters`:

```csharp
public static IEnumerable<DebrisSummary> ProjectDebris(GameLocation loc)
{
    if (ReadMemberRaw(loc, "debris", "Debris") is not IEnumerable debris)
        yield break;

    foreach (var entry in debris)
    {
        if (entry is null)
            continue;

        yield return ProjectDebris(entry);
    }
}
```

Add this test helper near the existing `ProjectMonsterForTests` helper:

```csharp
internal static DebrisSummary ProjectDebrisForTests(object debris)
    => ProjectDebris(debris);
```

Add this private projector near `ProjectMonster`:

```csharp
private static DebrisSummary ProjectDebris(object debris)
{
    var pixel = ReadVector2(debris, "position", "Position", "debrisOrigin", "DebrisOrigin")
        ?? ReadFirstNestedVector2(debris, "chunks", "Chunks");
    var item = ReadMemberRaw(debris, "item", "Item", "debrisItem", "DebrisItem");
    var qualifiedId = ReadString(item, "QualifiedItemId", "qualifiedItemId")
        ?? ReadString(debris, "QualifiedItemId", "qualifiedItemId")
        ?? string.Empty;
    var id = ReadString(item, "ItemId", "itemId")
        ?? ReadString(debris, "itemId", "ItemId")
        ?? StripQualifiedPrefix(qualifiedId);
    var name = ReadString(item, "DisplayName", "displayName", "Name", "name")
        ?? ReadString(debris, "debrisType", "DebrisType", "Name", "name")
        ?? string.Empty;

    return new DebrisSummary
    {
        Tile = pixel is null
            ? new TilePoint()
            : new TilePoint { X = (int)(pixel.Value.X / 64), Y = (int)(pixel.Value.Y / 64) },
        Pixel = pixel is null
            ? null
            : new PixelPoint { X = (int)pixel.Value.X, Y = (int)pixel.Value.Y },
        Kind = item is null ? "VisualDebris" : "ItemDebris",
        Id = id,
        QualifiedId = qualifiedId,
        Name = name,
        Stack = ReadInt(item, "Stack", "stack") ?? ReadInt(debris, "stack", "Stack"),
        Quality = ReadInt(item, "Quality", "quality") ?? ReadInt(debris, "quality", "Quality", "itemQuality", "ItemQuality"),
        Category = ReadInt(item, "Category", "category") ?? ReadInt(debris, "category", "Category"),
        RuntimeType = debris.GetType().Name,
    };
}
```

Change the helper signatures near the bottom of the file to accept nullable objects:

```csharp
private static Vector2? ReadVector2(object? instance, params string[] names)
```

```csharp
private static int? ReadInt(object? instance, params string[] names)
```

```csharp
private static string? ReadString(object? instance, params string[] names)
```

```csharp
private static object? ReadMemberRaw(object? instance, params string[] names)
```

At the start of each changed helper, add:

```csharp
if (instance is null)
    return null;
```

Add these helper methods near `NormalizeAssetName`:

```csharp
private static Vector2? ReadFirstNestedVector2(object instance, params string[] collectionNames)
{
    if (ReadMemberRaw(instance, collectionNames) is not IEnumerable entries)
        return null;

    foreach (var entry in entries)
    {
        var nested = ReadValueProperty(entry) ?? entry;
        var vector = ReadVector2(nested, "position", "Position", "currentPosition", "CurrentPosition");
        if (vector is not null)
            return vector;
    }

    return null;
}

private static string StripQualifiedPrefix(string value)
{
    if (value.Length > 0 && value[0] == '(')
    {
        var close = value.IndexOf(')', StringComparison.Ordinal);
        if (close >= 0 && close + 1 < value.Length)
            return value[(close + 1)..];
    }

    return value;
}
```

In `src/Harness/Handlers/LocationStateProjector.cs`, add this line after object projection and before furniture projection:

```csharp
state.Debris.AddRange(LocationContentProjector.ProjectDebris(loc));
```

- [ ] **Step 4: Run the harness tests and verify GREEN**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter LocationContentProjectorTests
```

Expected: PASS.

- [ ] **Step 5: Commit Task 2**

```bash
git add src/Harness/Handlers/LocationContentProjector.cs src/Harness/Handlers/LocationStateProjector.cs tests/Harness.Tests/LocationContentProjectorTests.cs
git commit -m "feat: project runtime location debris"
```

---

### Task 3: Add Debris Filters To `wait.location_content`

**Files:**
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`
- Test: `tests/Runner.Tests/ScenarioRunnerTests.cs`

- [ ] **Step 1: Write a failing debris wait test**

Append this test near the existing `WaitLocationContent_*` tests:

```csharp
[Fact]
public async Task WaitLocationContent_FiltersDebrisByIdentityTileAndStack()
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
                    "state.location" => JsonDocument.Parse("{\"name\":\"ExampleDeepCave\",\"resource_clumps\":[],\"objects\":[],\"monsters\":[],\"critters\":[],\"debris\":[{\"tile\":{\"x\":15,\"y\":16},\"pixel\":{\"x\":960,\"y\":1024},\"kind\":\"ItemDebris\",\"id\":\"769\",\"qualified_id\":\"(O)769\",\"name\":\"Void Essence\",\"stack\":2,\"quality\":0,\"category\":-2,\"runtime_type\":\"Debris\"}]}").RootElement,
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
        Name = "wait_location_content_debris",
        Steps = new()
        {
            new ScenarioStep
            {
                Action = "wait.location_content",
                Args = JsonDocument.Parse("{\"location\":\"ExampleDeepCave\",\"collection\":\"debris\",\"qualified_id\":\"(O)769\",\"runtime_type\":\"Debris\",\"x\":15,\"y\":16,\"stack_gte\":2,\"quality\":0,\"category\":-2,\"min_count\":1,\"max_count\":1,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
            },
        },
    }, cts.Token);

    Assert.True(report.Passed);

    cts.Cancel();
    try { await serverTask; } catch (OperationCanceledException) { }
}
```

- [ ] **Step 2: Run the debris wait test and verify RED**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter WaitLocationContent_FiltersDebrisByIdentityTileAndStack
```

Expected: FAIL because `debris` is not an allowed `wait.location_content` collection and the new filters are unsupported.

- [ ] **Step 3: Implement debris collection and generic filters**

In `src/Runner/Scenarios/ScenarioRunner.cs`, add `"debris"` to `AllowedLocationContentCollections`:

```csharp
private static readonly HashSet<string> AllowedLocationContentCollections = new(StringComparer.Ordinal)
{
    "objects",
    "resource_clumps",
    "monsters",
    "critters",
    "debris",
};
```

Update the validation message to:

```csharp
throw new InvalidOperationException("wait.location_content requires args.collection to be one of objects, resource_clumps, monsters, critters, debris");
```

In `LocationContentElementMatches`, add these checks before `SpriteTexture`:

```csharp
&& StringFilterMatches(element, "runtime_type", args.RuntimeType)
&& NumberFilterMatches(element, "stack", args.Stack, args.StackLt, args.StackLte, args.StackGt, args.StackGte)
&& NumberFilterMatches(element, "quality", args.Quality, args.QualityLt, args.QualityLte, args.QualityGt, args.QualityGte)
&& NumberFilterMatches(element, "category", args.Category, args.CategoryLt, args.CategoryLte, args.CategoryGt, args.CategoryGte)
```

In `FormatLocationContentFilters`, add:

```csharp
if (args.RuntimeType is not null) filters.Add($"runtime_type={args.RuntimeType}");
AddNumberFilters(filters, "stack", args.Stack, args.StackLt, args.StackLte, args.StackGt, args.StackGte);
AddNumberFilters(filters, "quality", args.Quality, args.QualityLt, args.QualityLte, args.QualityGt, args.QualityGte);
AddNumberFilters(filters, "category", args.Category, args.CategoryLt, args.CategoryLte, args.CategoryGt, args.CategoryGte);
```

Add these properties to `WaitLocationContentStepArgs`:

```csharp
public string? RuntimeType { get; set; }
public int? Stack { get; set; }
public int? StackLt { get; set; }
public int? StackLte { get; set; }
public int? StackGt { get; set; }
public int? StackGte { get; set; }
public int? Quality { get; set; }
public int? QualityLt { get; set; }
public int? QualityLte { get; set; }
public int? QualityGt { get; set; }
public int? QualityGte { get; set; }
public int? Category { get; set; }
public int? CategoryLt { get; set; }
public int? CategoryLte { get; set; }
public int? CategoryGt { get; set; }
public int? CategoryGte { get; set; }
```

- [ ] **Step 4: Run the debris wait test and verify GREEN**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter WaitLocationContent_FiltersDebrisByIdentityTileAndStack
```

Expected: PASS.

- [ ] **Step 5: Run nearby wait-location-content tests**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter WaitLocationContent
```

Expected: PASS.

- [ ] **Step 6: Commit Task 3**

```bash
git add src/Runner/Scenarios/ScenarioRunner.cs tests/Runner.Tests/ScenarioRunnerTests.cs
git commit -m "feat: wait for location debris content"
```

---

### Task 4: Add Runner-Side `wait.player`

**Files:**
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`
- Test: `tests/Runner.Tests/ScenarioRunnerTests.cs`

- [ ] **Step 1: Write failing `wait.player` health test**

Append this test near `WaitLocation_PollsStatePlayerUntilLocationMatches`:

```csharp
[Fact]
public async Task WaitPlayer_PollsStatePlayerUntilHealthMatches()
{
    var socket = SocketPath();
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var playerPolls = 0;

    var serverTask = Task.Run(async () =>
    {
        await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
        {
            session.RequestReceived += async req =>
            {
                JsonElement r = req.Method switch
                {
                    "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                    "state.player" => JsonDocument.Parse(playerPolls++ == 0
                        ? "{\"name\":\"Tester\",\"health\":100,\"location\":\"ExampleDeepCave\",\"tile\":{\"x\":10,\"y\":20}}"
                        : "{\"name\":\"Tester\",\"health\":82,\"location\":\"ExampleDeepCave\",\"tile\":{\"x\":10,\"y\":20}}").RootElement,
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
        Name = "wait_player_health",
        Steps = new()
        {
            new ScenarioStep
            {
                Action = "wait.player",
                Args = JsonDocument.Parse("{\"location\":\"ExampleDeepCave\",\"x\":10,\"y\":20,\"health_lt\":100,\"health_gte\":80,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
            },
        },
    }, cts.Token);

    Assert.True(report.Passed);
    Assert.True(playerPolls >= 2);

    cts.Cancel();
    try { await serverTask; } catch (OperationCanceledException) { }
}
```

- [ ] **Step 2: Write failing `wait.player` validation/timeout test**

Append this test:

```csharp
[Fact]
public async Task WaitPlayer_TimeoutIncludesLastObservedHealth()
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
                    "state.player" => JsonDocument.Parse("{\"name\":\"Tester\",\"health\":100,\"location\":\"Farm\",\"tile\":{\"x\":64,\"y\":15}}").RootElement,
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
        Name = "wait_player_timeout",
        Steps = new()
        {
            new ScenarioStep
            {
                Action = "wait.player",
                Args = JsonDocument.Parse("{\"health_lt\":90,\"timeout_ms\":20,\"poll_ms\":1}").RootElement,
            },
        },
    }, cts.Token);

    Assert.False(report.Passed);
    var failure = Assert.Single(report.Failures);
    Assert.Contains("wait.player timed out after 20ms waiting for player matching health_lt=90", failure);
    Assert.Contains("last observed health=100 location=Farm tile=64,15", failure);

    cts.Cancel();
    try { await serverTask; } catch (OperationCanceledException) { }
}
```

- [ ] **Step 3: Run the `wait.player` tests and verify RED**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "WaitPlayer_PollsStatePlayerUntilHealthMatches|WaitPlayer_TimeoutIncludesLastObservedHealth"
```

Expected: FAIL because `wait.player` is not implemented.

- [ ] **Step 4: Implement `wait.player` dispatch**

In the main step switch in `ScenarioRunner.RunAsync`, add near `wait.location`:

```csharp
else if (step.Action == "wait.player")
{
    await InvokeWaitPlayerAsync(step, ct);
}
```

In `DescribeStep`, add:

```csharp
"wait.player" => "Wait for player state",
```

In the helper that identifies passive/wait steps for step screenshots, add:

```csharp
"wait.player" => false,
```

- [ ] **Step 5: Implement `InvokeWaitPlayerAsync`**

Add this method near `InvokeWaitLocationAsync`:

```csharp
private async Task InvokeWaitPlayerAsync(ScenarioStep step, CancellationToken ct)
{
    var args = step.Args is { ValueKind: JsonValueKind.Object } obj
        ? JsonSerializer.Deserialize<WaitPlayerStepArgs>(obj.GetRawText(), ProtocolJson.Options) ?? new WaitPlayerStepArgs()
        : new WaitPlayerStepArgs();

    ValidateWaitPlayerArgs(args);

    var elapsed = Stopwatch.StartNew();
    JsonElement? last = null;
    while (elapsed.ElapsedMilliseconds < args.TimeoutMs)
    {
        ct.ThrowIfCancellationRequested();
        var resp = await _session.InvokeAsync("state.player", params_: null, ct);
        if (resp.Error is { } error)
            throw new InvalidOperationException($"wait.player failed during state.player: {error.Message}");

        if (resp.Result is { } root)
        {
            last = root.Clone();
            if (PlayerStateMatches(root, args))
                return;
        }

        await Task.Delay(args.PollMs, ct);
    }

    throw new TimeoutException(
        $"wait.player timed out after {args.TimeoutMs}ms waiting for player{FormatWaitPlayerFilters(args)}; " +
        $"last observed {FormatObservedPlayer(last)}");
}
```

Add these helpers near the other wait helpers:

```csharp
private static void ValidateWaitPlayerArgs(WaitPlayerStepArgs args)
{
    if (args.TimeoutMs < 1)
        throw new InvalidOperationException("wait.player requires args.timeout_ms >= 1");
    if (args.PollMs < 1)
        throw new InvalidOperationException("wait.player requires args.poll_ms >= 1");
    if ((args.X is null) != (args.Y is null))
        throw new InvalidOperationException("wait.player requires both args.x and args.y when filtering by tile");
}

private static bool PlayerStateMatches(JsonElement root, WaitPlayerStepArgs args)
{
    return StringFilterMatches(root, "location", args.Location)
        && NumberFilterMatches(root, "health", args.Health, args.HealthLt, args.HealthLte, args.HealthGt, args.HealthGte)
        && TileFilterMatches(root, args.X, args.Y);
}

private static string FormatWaitPlayerFilters(WaitPlayerStepArgs args)
{
    var filters = new List<string>();
    if (args.Location is not null) filters.Add($"location={args.Location}");
    AddNumberFilters(filters, "health", args.Health, args.HealthLt, args.HealthLte, args.HealthGt, args.HealthGte);
    if (args.X is not null && args.Y is not null) filters.Add($"tile={args.X},{args.Y}");
    return filters.Count == 0 ? string.Empty : $" matching {string.Join(", ", filters)}";
}

private static string FormatObservedPlayer(JsonElement? root)
{
    if (root is null || root.Value.ValueKind != JsonValueKind.Object)
        return "nothing";

    var health = root.Value.TryGetProperty("health", out var h) && h.TryGetInt32(out var hv)
        ? hv.ToString(CultureInfo.InvariantCulture)
        : "?";
    var location = root.Value.TryGetProperty("location", out var l) && l.ValueKind == JsonValueKind.String
        ? l.GetString() ?? string.Empty
        : "?";
    var tile = "?";
    if (root.Value.TryGetProperty("tile", out var t)
        && t.ValueKind == JsonValueKind.Object
        && t.TryGetProperty("x", out var x)
        && t.TryGetProperty("y", out var y)
        && x.TryGetInt32(out var xv)
        && y.TryGetInt32(out var yv))
    {
        tile = $"{xv},{yv}";
    }

    return $"health={health} location={location} tile={tile}";
}
```

If `ScenarioRunner.cs` does not already import `System.Globalization`, add:

```csharp
using System.Globalization;
```

Add this args class near `WaitLocationContentStepArgs`:

```csharp
private sealed class WaitPlayerStepArgs
{
    public string? Location { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public int? Health { get; set; }
    public int? HealthLt { get; set; }
    public int? HealthLte { get; set; }
    public int? HealthGt { get; set; }
    public int? HealthGte { get; set; }
    public int TimeoutMs { get; set; } = 10000;
    public int PollMs { get; set; } = 100;
}
```

- [ ] **Step 6: Run `wait.player` tests and verify GREEN**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "WaitPlayer_PollsStatePlayerUntilHealthMatches|WaitPlayer_TimeoutIncludesLastObservedHealth"
```

Expected: PASS.

- [ ] **Step 7: Commit Task 4**

```bash
git add src/Runner/Scenarios/ScenarioRunner.cs tests/Runner.Tests/ScenarioRunnerTests.cs
git commit -m "feat: wait for player health state"
```

---

### Task 5: Probe The SVE Combat Lifecycle Target

**Files:**
- No committed code in this task unless a probe scenario is intentionally kept.
- Use SVE repo: `/home/fintan/stardewRepos/StardewValleyExpanded`

- [ ] **Step 1: Run existing SVE combat scenarios with the Frobby feature branch**

Run:

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework /home/fintan/stardewRepos/StardewValleyExpanded/scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-9-precheck /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/10-sve-ftm-monster-spawn-config.test.json /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/12-sve-combat-monster-damage.test.json
```

Expected: both scenarios pass. If either fails, stop and fix the regression before adding Slice 9 scenarios.

- [ ] **Step 2: Create a local probe copy for a likely killable target**

Create `/tmp/sve-slice-9-lifecycle-probe.test.json` with this content:

```json
{
  "name": "sve_combat_lifecycle_probe",
  "fixture": "m0spike_436515781",
  "config": { "seed": 436515781 },
  "steps": [
    { "action": "time.set", "args": { "time": 600, "day": 1, "season": "spring", "year": 1 } },
    { "action": "world.set_weather", "args": { "type": "sun" } },
    { "action": "time.next_day", "args": { "settle_timeout_ms": 15000, "poll_ms": 100 } },
    { "action": "player.give_item", "args": { "id": "(W)4", "count": 1 } },
    { "action": "player.warp", "args": { "location": "Custom_CrimsonBadlands", "x": 20, "y": 145 } },
    { "action": "wait.location", "args": { "location": "Custom_CrimsonBadlands", "x": 20, "y": 145, "timeout_ms": 10000, "poll_ms": 100 } },
    {
      "action": "wait.location_content",
      "args": {
        "location": "Custom_CrimsonBadlands",
        "collection": "monsters",
        "sprite_texture": "Characters/Monsters/BadlandsSerpent",
        "health_lte": 245,
        "min_count": 1,
        "timeout_ms": 15000,
        "poll_ms": 100
      }
    },
    { "action": "freeze.begin", "args": { "settle_timeout_ms": 10000, "poll_ms": 100 } },
    { "action": "screenshot.capture", "args": { "name": "probe" } }
  ],
  "assertions": [
    {
      "type": "state",
      "expr": "state.player.location == 'Custom_CrimsonBadlands'",
      "message": "Lifecycle probe should finish in the Crimson Badlands"
    }
  ]
}
```

- [ ] **Step 3: Run the probe**

Run:

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework /home/fintan/stardewRepos/StardewValleyExpanded/scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-9-lifecycle-probe /tmp/sve-slice-9-lifecycle-probe.test.json
```

Expected: PASS if a stable serpent target is available. If it fails because no serpent appears, update the probe filter to a lower-health visible target from `state.location.monsters` in the report, preferring non-mummy targets with `max_health <= 500`.

- [ ] **Step 4: Confirm selector retargeting will target the probed monster**

Use the probe report to confirm the chosen monster exposes stable `sprite_texture`, `type`, or `name` metadata. Scenario 13 should use selector-based `combat.attack` retargeting from Task 6 rather than a fixed tile, so the repeated attack follows the monster's current tile.

Do not commit probe files from `/tmp`.

---

### Task 6: Add Runner-Side Combat Target Retargeting

**Files:**
- Modify: `src/Protocol/Models/CombatAttackRequest.cs`
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`
- Test: `tests/Runner.Tests/ScenarioRunnerTests.cs`

- [ ] **Step 1: Write failing combat retargeting test**

Append this test near existing combat runner tests:

```csharp
[Fact]
public async Task CombatAttack_TargetSelectorRetargetsNearestMonsterEachRepeat()
{
    var socket = SocketPath();
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var locationPolls = 0;
    var attackTargets = new List<string>();

    var serverTask = Task.Run(async () =>
    {
        await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
        {
            session.RequestReceived += async req =>
            {
                if (req.Method == "combat.attack" && req.Params is { } p)
                {
                    attackTargets.Add(p.GetRawText());
                }

                JsonElement r = req.Method switch
                {
                    "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                    "state.location" => JsonDocument.Parse(locationPolls++ == 0
                        ? "{\"name\":\"ExampleDeepCave\",\"monsters\":[{\"tile\":{\"x\":12,\"y\":8},\"type\":\"Serpent\",\"health\":245,\"max_health\":245,\"sprite_texture\":\"ExampleMod/Serpent\"}]}"
                        : "{\"name\":\"ExampleDeepCave\",\"monsters\":[{\"tile\":{\"x\":11,\"y\":8},\"type\":\"Serpent\",\"health\":100,\"max_health\":245,\"sprite_texture\":\"ExampleMod/Serpent\"}]}").RootElement,
                    "combat.attack" => JsonDocument.Parse("{\"ok\":true,\"tick\":1}").RootElement,
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
        Name = "combat_attack_retargeting",
        Steps = new()
        {
            new ScenarioStep
            {
                Action = "combat.attack",
                Args = JsonDocument.Parse("{\"qualified_item_id\":\"(W)4\",\"repeat\":2,\"delay_ticks\":0,\"target\":{\"location\":\"ExampleDeepCave\",\"type\":\"Serpent\",\"sprite_texture\":\"ExampleMod/Serpent\"}}").RootElement,
            },
        },
    }, cts.Token);

    Assert.True(report.Passed);
    Assert.Equal(2, attackTargets.Count);
    Assert.Contains("\"x\":12", attackTargets[0]);
    Assert.Contains("\"y\":8", attackTargets[0]);
    Assert.Contains("\"x\":11", attackTargets[1]);
    Assert.Contains("\"y\":8", attackTargets[1]);

    cts.Cancel();
    try { await serverTask; } catch (OperationCanceledException) { }
}
```

- [ ] **Step 2: Run the retargeting test and verify RED**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter CombatAttack_TargetSelectorRetargetsNearestMonsterEachRepeat
```

Expected: FAIL because `combat.attack` ignores `target`.

- [ ] **Step 3: Extend combat attack args**

In `src/Protocol/Models/CombatAttackRequest.cs`, add:

```csharp
public CombatTargetCriteria? Target { get; set; }
```

Add this class to the same file:

```csharp
public sealed class CombatTargetCriteria
{
    public string? Location { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? SpriteTexture { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public int? HealthGt { get; set; }
    public int? HealthGte { get; set; }
    public int? HealthLt { get; set; }
    public int? HealthLte { get; set; }
}
```

- [ ] **Step 4: Retarget before each repeated attack**

In `InvokeCombatAttackAsync`, before building `singleAttack`, branch on `args.Target`:

```csharp
for (int i = 0; i < args.Repeat; i++)
{
    var singleAttackElement = args.Target is null
        ? BuildCombatAttackElement(args)
        : await BuildRetargetedCombatAttackElementAsync(args, ct);

    var resp = await _session.InvokeAsync("combat.attack", singleAttackElement, ct);
    if (resp.Error is { } ex)
        throw new InvalidOperationException($"step '{step.Action}' failed: {ex.Message}");

    if (i + 1 < args.Repeat && args.DelayTicks > 0)
        await Task.Delay(TimeSpan.FromMilliseconds(args.DelayTicks * 17.0), ct);
}
```

Move the existing single-attack JSON construction into:

```csharp
private static JsonElement BuildCombatAttackElement(CombatAttackRequest args)
{
    var singleAttack = new JsonObject();
    if (args.X is { } x) singleAttack["x"] = x;
    if (args.Y is { } y) singleAttack["y"] = y;
    if (!string.IsNullOrWhiteSpace(args.Direction)) singleAttack["direction"] = args.Direction;
    if (!string.IsNullOrWhiteSpace(args.QualifiedItemId)) singleAttack["qualified_item_id"] = args.QualifiedItemId;
    return JsonDocument.Parse(singleAttack.ToJsonString()).RootElement.Clone();
}
```

Add:

```csharp
private async Task<JsonElement> BuildRetargetedCombatAttackElementAsync(CombatAttackRequest args, CancellationToken ct)
{
    var target = args.Target ?? throw new InvalidOperationException("combat.attack target missing");
    var location = target.Location;
    var request = string.IsNullOrWhiteSpace(location)
        ? null
        : ProtocolJson.ToElement(new { name = location });
    var resp = await _session.InvokeAsync("state.location", request, ct);
    if (resp.Error is { } error)
        throw new InvalidOperationException($"combat.attack failed during target state.location: {error.Message}");
    if (resp.Result is not { } root
        || !root.TryGetProperty("monsters", out var monsters)
        || monsters.ValueKind != JsonValueKind.Array)
    {
        throw new InvalidOperationException("combat.attack target found no monster collection");
    }

    JsonElement? best = null;
    foreach (var monster in monsters.EnumerateArray())
    {
        if (!CombatTargetMatches(monster, target))
            continue;
        best = monster.Clone();
        break;
    }

    if (best is null)
        throw new InvalidOperationException("combat.attack target matched no monsters");
    if (!best.Value.TryGetProperty("tile", out var tile)
        || !tile.TryGetProperty("x", out var x)
        || !tile.TryGetProperty("y", out var y)
        || !x.TryGetInt32(out var xv)
        || !y.TryGetInt32(out var yv))
    {
        throw new InvalidOperationException("combat.attack target monster has no tile");
    }

    var selected = new CombatAttackRequest
    {
        X = xv,
        Y = yv,
        Direction = args.Direction,
        QualifiedItemId = args.QualifiedItemId,
    };
    return BuildCombatAttackElement(selected);
}
```

Add:

```csharp
private static bool CombatTargetMatches(JsonElement monster, CombatTargetCriteria target)
{
    return StringFilterMatches(monster, "name", target.Name)
        && StringFilterMatches(monster, "type", target.Type)
        && StringFilterMatches(monster, "sprite_texture", target.SpriteTexture)
        && NumberFilterMatches(monster, "health", null, target.HealthLt, target.HealthLte, target.HealthGt, target.HealthGte)
        && TileFilterMatches(monster, target.X, target.Y);
}
```

- [ ] **Step 5: Run the retargeting test and verify GREEN**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter CombatAttack_TargetSelectorRetargetsNearestMonsterEachRepeat
```

Expected: PASS.

- [ ] **Step 6: Commit Task 6**

```bash
git add src/Protocol/Models/CombatAttackRequest.cs src/Runner/Scenarios/ScenarioRunner.cs tests/Runner.Tests/ScenarioRunnerTests.cs
git commit -m "feat: retarget combat attacks from monster state"
```

---

### Task 7: Add SVE Combat Lifecycle Scenario

**Files:**
- Create: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/13-sve-combat-lifecycle-debris.test.json`
- Modify: `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`

- [ ] **Step 1: Create scenario 13**

Create `tests/sdv/13-sve-combat-lifecycle-debris.test.json` with:

```json
{
  "name": "sve_combat_lifecycle_debris",
  "fixture": "m0spike_436515781",
  "config": { "seed": 436515781 },
  "steps": [
    { "action": "time.set", "args": { "time": 600, "day": 1, "season": "spring", "year": 1 } },
    { "action": "world.set_weather", "args": { "type": "sun" } },
    { "action": "time.next_day", "args": { "settle_timeout_ms": 15000, "poll_ms": 100 } },
    { "action": "player.give_item", "args": { "id": "(W)4", "count": 1 } },
    { "action": "player.warp", "args": { "location": "Custom_CrimsonBadlands", "x": 20, "y": 145 } },
    { "action": "wait.location", "args": { "location": "Custom_CrimsonBadlands", "x": 20, "y": 145, "timeout_ms": 10000, "poll_ms": 100 } },
    {
      "action": "wait.location_content",
      "args": {
        "location": "Custom_CrimsonBadlands",
        "collection": "monsters",
        "sprite_texture": "Characters/Monsters/BadlandsSerpent",
        "health_lte": 245,
        "min_count": 1,
        "timeout_ms": 15000,
        "poll_ms": 100
      }
    },
    {
      "action": "combat.attack",
      "args": {
        "qualified_item_id": "(W)4",
        "repeat": 12,
        "delay_ticks": 10,
        "target": {
          "location": "Custom_CrimsonBadlands",
          "sprite_texture": "Characters/Monsters/BadlandsSerpent",
          "health_gt": 0
        }
      }
    },
    {
      "action": "wait.location_content",
      "args": {
        "location": "Custom_CrimsonBadlands",
        "collection": "monsters",
        "sprite_texture": "Characters/Monsters/BadlandsSerpent",
        "max_count": 0,
        "timeout_ms": 15000,
        "poll_ms": 100
      }
    },
    {
      "action": "wait.location_content",
      "args": {
        "location": "Custom_CrimsonBadlands",
        "collection": "debris",
        "min_count": 1,
        "timeout_ms": 10000,
        "poll_ms": 100
      }
    },
    { "action": "freeze.begin", "args": { "settle_timeout_ms": 10000, "poll_ms": 100 } },
    { "action": "screenshot.capture", "args": { "name": "final" } }
  ],
  "assertions": [
    {
      "type": "state",
      "expr": "state.player.location == 'Custom_CrimsonBadlands'",
      "message": "Combat lifecycle scenario should finish in the Crimson Badlands"
    }
  ]
}
```

- [ ] **Step 2: Run scenario 13 and verify GREEN**

Run:

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework /home/fintan/stardewRepos/StardewValleyExpanded/scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-9-lifecycle /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/13-sve-combat-lifecycle-debris.test.json
```

Expected: PASS.

If the debris assertion fails because the selected target has no stable debris, keep the monster-removal assertion and change the debris wait step to a zero-count guard for a known absent impossible item:

```json
{
  "action": "wait.location_content",
  "args": {
    "location": "Custom_CrimsonBadlands",
    "collection": "debris",
    "qualified_id": "(O)__never_spawned_slice_9_probe__",
    "max_count": 0,
    "timeout_ms": 1000,
    "poll_ms": 100
  }
}
```

That still proves `debris` is queryable without making probabilistic loot a scenario blocker.

- [ ] **Step 3: Document scenario 13**

In `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`, add after scenario 12:

```markdown
Scenario `tests/sdv/13-sve-combat-lifecycle-debris.test.json` extends combat
coverage beyond a health delta. It uses Frobby's neutral debris and player/combat
waits to prove a runtime monster can be removed and that post-combat debris state
is queryable without parsing SVE's Farm Type Manager content pack.
```

- [ ] **Step 4: Commit SVE scenario 13**

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded add tests/sdv/13-sve-combat-lifecycle-debris.test.json docs/FROBBY.md
git -C /home/fintan/stardewRepos/StardewValleyExpanded commit -m "test: add combat lifecycle scenario"
```

---

### Task 8: Probe And Add Passive Shadow Scenario If Stable

**Files:**
- Create: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/14-sve-passive-shadow-combat-state.test.json`
- Modify: `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`
- Modify: `/home/fintan/stardewRepos/frobby/sdv-test-framework/SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Create a passive shadow probe in `/tmp`**

Create `/tmp/sve-passive-shadow-probe.test.json`:

```json
{
  "name": "sve_passive_shadow_probe",
  "fixture": "m0spike_436515781",
  "config": { "seed": 436515781 },
  "steps": [
    { "action": "time.set", "args": { "time": 600, "day": 1, "season": "spring", "year": 1 } },
    { "action": "player.add_event_seen", "args": { "id": "1090508" } },
    { "action": "player.warp", "args": { "location": "Custom_HighlandsCavern", "x": 120, "y": 147 } },
    { "action": "wait.location", "args": { "location": "Custom_HighlandsCavern", "timeout_ms": 10000, "poll_ms": 100 } },
    {
      "action": "wait.location_content",
      "args": {
        "location": "Custom_HighlandsCavern",
        "collection": "monsters",
        "type": "ShadowBrute",
        "min_count": 1,
        "timeout_ms": 15000,
        "poll_ms": 100
      }
    },
    {
      "action": "wait.location_content",
      "args": {
        "location": "Custom_HighlandsCavern",
        "collection": "monsters",
        "type": "ShadowBrute",
        "damage": 0,
        "health": 999999,
        "max_health": 999999,
        "min_count": 1,
        "timeout_ms": 15000,
        "poll_ms": 100
      }
    },
    { "action": "freeze.begin", "args": { "settle_timeout_ms": 10000, "poll_ms": 100 } },
    { "action": "screenshot.capture", "args": { "name": "probe" } }
  ],
  "assertions": [
    {
      "type": "state",
      "expr": "state.player.events_seen contains '1090508'",
      "message": "Passive shadow probe should seed the SVE shadow peace event"
    }
  ]
}
```

- [ ] **Step 2: Run the passive shadow probe**

Run:

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework /home/fintan/stardewRepos/StardewValleyExpanded/scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-9-passive-shadow-probe /tmp/sve-passive-shadow-probe.test.json
```

Expected: PASS only if SVE actually spawns a shadow target in `Custom_HighlandsCavern` after event `1090508` without the betrayal flag. If it fails because no shadow monster spawns under that progression state, do not add scenario 14. Instead, add this note under Slice 9 in `SVE_FROBBY_CAPABILITY_TODO.md`:

```markdown
  - Deferred proof: passive-shadow event `1090508` needs a stable shadow spawn state; current SVE FTM conditions may remove normal Highlands shadow spawns after peace unless the betrayal flag is active, which intentionally bypasses the passive patch.
```

- [ ] **Step 3: Add scenario 14 if the probe passes**

If the probe passes, copy `/tmp/sve-passive-shadow-probe.test.json` to:

```bash
cp /tmp/sve-passive-shadow-probe.test.json /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/14-sve-passive-shadow-combat-state.test.json
```

Then change the scenario `"name"` to:

```json
"sve_passive_shadow_combat_state"
```

- [ ] **Step 4: Run scenario 14 if added**

Run:

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework /home/fintan/stardewRepos/StardewValleyExpanded/scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-9-passive-shadow /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/14-sve-passive-shadow-combat-state.test.json
```

Expected: PASS.

- [ ] **Step 5: Document scenario 14 or the deferral**

If scenario 14 was added, append to `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`:

```markdown
Scenario `tests/sdv/14-sve-passive-shadow-combat-state.test.json` validates
SVE's passive shadow combat behavior after event `1090508`. The scenario uses
neutral Frobby event-state seeding plus monster metadata assertions; Frobby does
not encode the SVE event, location, or monster rules.
```

If scenario 14 was deferred, no SVE docs entry is needed.

- [ ] **Step 6: Commit passive shadow result**

If scenario 14 was added:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded add tests/sdv/14-sve-passive-shadow-combat-state.test.json docs/FROBBY.md
git -C /home/fintan/stardewRepos/StardewValleyExpanded commit -m "test: add passive shadow combat scenario"
```

If scenario 14 was deferred:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework add SVE_FROBBY_CAPABILITY_TODO.md
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework commit -m "docs: note passive shadow proof caveat"
```

---

### Task 9: Update Frobby Documentation

**Files:**
- Modify: `README.md`
- Modify: `docs/rpc-schema.md`
- Modify: `docs/dsl-quickstart.md`
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Update README authoring guidance**

In `README.md`, update the world-content bullet to include debris:

```markdown
- Use `state.location.resource_clumps`, `state.location.monsters`,
  `state.location.debris`, and runner-side `wait.location_content` when testing
  spawned world content such as logs, boulders, forage-like objects, ore,
  monsters, dropped combat loot, or transient item debris.
```

Add a combat/wait note after the existing `combat.attack` bullet:

```markdown
- Use `wait.player` when a scenario needs to wait for player health changes after
  contact damage, hazards, or defensive effects. Prefer health comparison waits
  such as `health_lt` or `health_gte` over fixed sleeps.
```

- [ ] **Step 2: Update RPC schema**

In `docs/rpc-schema.md`, extend the `state.location` example with:

```json
"debris": [{ "tile": { "x": 15, "y": 16 }, "pixel": { "x": 960, "y": 1024 }, "kind": "ItemDebris", "id": "769", "qualified_id": "(O)769", "name": "Void Essence", "stack": 2, "quality": 0, "category": -2, "runtime_type": "Debris" }]
```

Add this paragraph near the `state.location` optional metadata paragraph:

```markdown
`debris` contains transient runtime debris such as item drops and visual debris.
Fields are best-effort because Stardew debris can be item-backed, animated, or
purely visual. Tests should filter only on fields relevant to the scenario.
```

Add a runner action section for `wait.player` near existing scenario action docs:

````markdown
### wait.player

Runner-side action. Polls `state.player` until the requested player-state filters
match.

```json
{ "action": "wait.player", "args": { "health_lt": 100, "location": "ExampleDeepCave", "timeout_ms": 10000, "poll_ms": 100 } }
```

Supported filters: `location`, paired `x`/`y`, `health`, `health_lt`,
`health_lte`, `health_gt`, and `health_gte`.
````

- [ ] **Step 3: Update DSL quickstart**

In `docs/dsl-quickstart.md`, add a short JSON scenario example in the combat/world-content section:

```json
{
  "action": "wait.location_content",
  "args": {
    "location": "ExampleDeepCave",
    "collection": "debris",
    "qualified_id": "(O)769",
    "min_count": 1,
    "timeout_ms": 10000
  }
}
```

Add:

```json
{
  "action": "wait.player",
  "args": { "health_lt": 100, "timeout_ms": 10000, "poll_ms": 100 }
}
```

- [ ] **Step 4: Mark Slice 9 active/done appropriately**

In `SVE_FROBBY_CAPABILITY_TODO.md`, update Slice 9 from `Planning` to `Active` when implementation starts:

```markdown
- [ ] Active: Slice 9, combat lifecycle, drops, and player hazards.
```

After Frobby implementation, docs, and SVE verification pass, change it to:

```markdown
- [x] Done: Slice 9, combat lifecycle, drops, and player hazards.
```

Add implementation-plan and completed notes under the Slice 9 item:

```markdown
  - Implementation plan: `docs/superpowers/plans/2026-05-10-sve-slice-9-combat-lifecycle-drops-hazards.md`.
  - Done: `state.location.debris`, debris-aware `wait.location_content`, runner-side `wait.player`, and SVE Slice 9 scenario coverage verify combat lifecycle outcomes beyond a single health delta.
```

- [ ] **Step 5: Run docs diff check**

Run:

```bash
git diff --check
```

Expected: no output.

- [ ] **Step 6: Commit Task 9**

```bash
git add README.md docs/rpc-schema.md docs/dsl-quickstart.md SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "docs: document combat lifecycle testing tools"
```

---

### Task 10: Final Verification

**Files:**
- Verification only.

- [ ] **Step 1: Run targeted Frobby tests**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter LocationStateSerializationTests
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter LocationContentProjectorTests
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "WaitLocationContent|WaitPlayer|CombatAttack"
```

Expected: all pass.

- [ ] **Step 2: Run broader Frobby verification**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj
dotnet test tests/Harness.Tests/Harness.Tests.csproj
dotnet test tests/Runner.Tests/Runner.Tests.csproj
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj
dotnet test tests/Runner.Mcp.Tests/Runner.Mcp.Tests.csproj
dotnet build src/Runner/Runner.csproj
```

Expected: all pass; build exits 0.

- [ ] **Step 3: Run SVE Slice 9 verification**

Run scenario 13:

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework /home/fintan/stardewRepos/StardewValleyExpanded/scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-9-final /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/13-sve-combat-lifecycle-debris.test.json
```

If scenario 14 exists, run it:

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework /home/fintan/stardewRepos/StardewValleyExpanded/scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-9-passive-shadow-final /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/14-sve-passive-shadow-combat-state.test.json
```

Expected: all existing Slice 9 scenarios pass.

- [ ] **Step 4: Run SVE regression scenarios**

Run:

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework /home/fintan/stardewRepos/StardewValleyExpanded/scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-9-regression /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/10-sve-ftm-monster-spawn-config.test.json /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/12-sve-combat-monster-damage.test.json
```

Expected: both pass.

- [ ] **Step 5: Check git status in both repos**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
```

Expected: no uncommitted Frobby changes. SVE should be clean on the Slice 9 feature branch, not `master`.

---

## Self-Review Checklist

- Spec coverage:
  - Debris state is covered by Tasks 1-3.
  - Player health waits are covered by Task 4.
  - Combat lifecycle proof is covered by Tasks 5, 7, and 10.
  - Passive shadow proof or explicit caveat is covered by Task 8.
  - Documentation is covered by Task 9.
- Type consistency:
  - `DebrisSummary` fields match the JSON filters used by `wait.location_content`.
  - `wait.player` uses existing `state.player` fields: `health`, `location`, and `tile`.
  - Combat target criteria reuse existing monster fields: `name`, `type`, `sprite_texture`, `health`, and `tile`.
- Scope:
  - No direct monster mutation.
  - No SVE-specific IDs or rules inside Frobby implementation files.
  - Later backlog slices remain out of scope.
