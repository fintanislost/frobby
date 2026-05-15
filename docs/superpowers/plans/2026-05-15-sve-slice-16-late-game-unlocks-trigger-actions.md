# SVE Slice 16 Late-Game Unlocks And Trigger Actions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add neutral Frobby progression-state observability and prove SVE trigger-action and late-game unlock behavior through real game boundaries.

**Architecture:** Extend `state.player` with pending-mail state, then extend runner-side `wait.player` to poll player progression lists. SVE scenarios exercise real `LocationChanged`, `DayEnding`, and Content Patcher map-patch boundaries; Frobby remains content-agnostic.

**Tech Stack:** C#/.NET 10, xUnit, System.Text.Json, SMAPI/Stardew Valley harness, JSON scenario files, SVE repo-local `scripts/sdv-test`.

---

## File Structure

Frobby files:

- Modify `src/Protocol/Models/PlayerState.cs`: add the `MailForTomorrow` DTO field.
- Modify `src/Harness/Handlers/StatePlayerHandler.cs`: project pending mail from the live farmer and fake test world.
- Modify `tests/Protocol.Tests/PlayerStateSerializationTests.cs`: lock the new snake-case JSON field.
- Modify `tests/Harness.Tests/StatePlayerHandlerTests.cs`: lock handler projection.
- Modify `src/Runner/Scenarios/ScenarioRunner.cs`: add progression filters to runner-side `wait.player`.
- Modify `tests/Runner.Tests/ScenarioRunnerTests.cs`: lock wait success and diagnostics.
- Modify `tests/Runner.Tests/ScenarioLoaderTests.cs`: prove scenario validation accepts the new wait args.
- Modify `schemas/scenario.schema.json`: update the free-form action description for discoverability.
- Modify `docs/rpc-schema.md` and `docs/dsl-quickstart.md`: document the new field and filters.
- Modify `SVE_FROBBY_CAPABILITY_TODO.md`: mark Slice 16 active during implementation, then done after verification.

SVE files:

- Create `tests/sdv/21-sve-trigger-action-location-changed.test.json`.
- Create `tests/sdv/22-sve-trigger-action-day-ending-mail.test.json`.
- Create `tests/sdv/23-sve-progression-map-action-unlock.test.json`.
- Modify `docs/FROBBY.md`: document the Slice 16 scenarios and the neutral Frobby capability they cover.

Branch handling:

- Frobby branch: continue on `feature/sve-slice-16-late-game-unlocks`.
- SVE branch: create or switch to `feature/frobby-sve-slice-16-late-game-unlocks` from the current clean SVE feature head. Do not merge SVE back to `master` unless explicitly instructed.

---

### Task 1: Add `mail_for_tomorrow` To `state.player`

**Files:**
- Modify: `src/Protocol/Models/PlayerState.cs`
- Modify: `src/Harness/Handlers/StatePlayerHandler.cs`
- Test: `tests/Protocol.Tests/PlayerStateSerializationTests.cs`
- Test: `tests/Harness.Tests/StatePlayerHandlerTests.cs`

- [ ] **Step 1: Write the failing protocol serialization test**

In `tests/Protocol.Tests/PlayerStateSerializationTests.cs`, update `Serialize_ProducesSnakeCaseFields` so the `PlayerState` initializer includes `MailForTomorrow`, then assert the snake-case output:

```csharp
var p = new PlayerState
{
    Name = "Tester",
    Money = 1000,
    Stamina = 270,
    MaxStamina = 270,
    Health = 100,
    Location = "Farm",
    Tile = new TilePoint { X = 64, Y = 15 },
    MailReceived = new() { "button_tut_1" },
    MailForTomorrow = new() { "HenchmanMarshTonics" },
    EventsSeen = new() { "5532011" },
};
```

Add these assertions near the existing mail/event assertions:

```csharp
Assert.Contains("\"mail_for_tomorrow\":[\"HenchmanMarshTonics\"]", json);
Assert.DoesNotContain("MailForTomorrow", json);
```

- [ ] **Step 2: Write the failing handler projection test**

In `tests/Harness.Tests/StatePlayerHandlerTests.cs`, add this assertion inside `Handle_IncludesInventoryItemSummaries` after the `MailReceived` assertion:

```csharp
Assert.Equal(new[] { "HenchmanMarshTonics", "SusanCooking" }, state.MailForTomorrow);
```

Add the fake world property inside `FakePlayerStateWorld`:

```csharp
public IReadOnlyList<string> MailForTomorrow { get; } = new[] { "HenchmanMarshTonics", "SusanCooking" };
```

- [ ] **Step 3: Run the focused failing tests**

Run:

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Protocol.Tests/Protocol.Tests.csproj --filter "FullyQualifiedName~PlayerStateSerializationTests"
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~StatePlayerHandlerTests"
```

Expected: compile failure because `PlayerState.MailForTomorrow` and `IPlayerStateWorld.MailForTomorrow` do not exist yet.

- [ ] **Step 4: Implement the minimal DTO change**

In `src/Protocol/Models/PlayerState.cs`, add the property after `MailReceived`:

```csharp
public List<string> MailReceived { get; set; } = new();
public List<string> MailForTomorrow { get; set; } = new();
public List<string> EventsSeen { get; set; } = new();
```

- [ ] **Step 5: Implement the handler projection**

In `src/Harness/Handlers/StatePlayerHandler.cs`, add this property assignment in `Handle` after `MailReceived`:

```csharp
MailForTomorrow = world.MailForTomorrow.ToList(),
```

Add this property to `IPlayerStateWorld` after `MailReceived`:

```csharp
IReadOnlyList<string> MailForTomorrow { get; }
```

Add this property to `SdvPlayerStateWorld` after `MailReceived`:

```csharp
public IReadOnlyList<string> MailForTomorrow
    => ReflectionValue.ReadStringList(
        ReflectionValue.ReadRaw(Player, "mailForTomorrow", "MailForTomorrow"))
        .ToList();
```

- [ ] **Step 6: Run the focused passing tests**

Run:

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Protocol.Tests/Protocol.Tests.csproj --filter "FullyQualifiedName~PlayerStateSerializationTests"
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~StatePlayerHandlerTests"
```

Expected: all selected tests pass.

- [ ] **Step 7: Commit Task 1**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework add src/Protocol/Models/PlayerState.cs src/Harness/Handlers/StatePlayerHandler.cs tests/Protocol.Tests/PlayerStateSerializationTests.cs tests/Harness.Tests/StatePlayerHandlerTests.cs
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework commit -m "feat: expose pending player mail state"
```

---

### Task 2: Add Progression Filters To `wait.player`

**Files:**
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`
- Test: `tests/Runner.Tests/ScenarioRunnerTests.cs`

- [ ] **Step 1: Write the failing success test**

In `tests/Runner.Tests/ScenarioRunnerTests.cs`, add this test after `WaitPlayer_MatchesTransientStateAndBuffEffects`:

```csharp
[Fact]
public async Task WaitPlayer_MatchesProgressionLists()
{
    var socket = SocketPath();
    var playerPolls = 0;
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
                    "state.player" => JsonDocument.Parse(playerPolls++ == 0
                        ? "{\"name\":\"Tester\",\"health\":100,\"location\":\"Farm\",\"tile\":{\"x\":64,\"y\":15},\"mail_received\":[],\"mail_for_tomorrow\":[],\"events_seen\":[]}"
                        : "{\"name\":\"Tester\",\"health\":100,\"location\":\"Farm\",\"tile\":{\"x\":64,\"y\":15},\"mail_received\":[\"ShedRepaired\"],\"mail_for_tomorrow\":[\"HenchmanMarshTonics\"],\"events_seen\":[\"1000035\"]}").RootElement,
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
        Name = "wait_player_progression",
        Steps =
        {
            new ScenarioStep
            {
                Action = "wait.player",
                Args = JsonDocument.Parse("{\"mail_received\":\"ShedRepaired\",\"mail_for_tomorrow\":\"HenchmanMarshTonics\",\"event_seen\":\"1000035\",\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
            },
        },
    }, cts.Token);

    Assert.True(report.Passed);
    Assert.True(playerPolls >= 2);

    cts.Cancel();
    try { await serverTask; } catch (OperationCanceledException) { }
}
```

- [ ] **Step 2: Write the failing diagnostic test**

In the same file, add this test after `WaitPlayer_TimeoutIncludesTransientAndBuffSummary`:

```csharp
[Fact]
public async Task WaitPlayer_TimeoutIncludesProgressionSummary()
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
                    "state.player" => JsonDocument.Parse("{\"name\":\"Tester\",\"health\":100,\"location\":\"Farm\",\"tile\":{\"x\":64,\"y\":15},\"mail_received\":[\"ShedRepaired\"],\"mail_for_tomorrow\":[],\"events_seen\":[\"418172\"]}").RootElement,
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
        Name = "wait_player_progression_timeout",
        Steps =
        {
            new ScenarioStep
            {
                Action = "wait.player",
                Args = JsonDocument.Parse("{\"mail_for_tomorrow\":\"HenchmanMarshTonics\",\"timeout_ms\":20,\"poll_ms\":1}").RootElement,
            },
        },
    }, cts.Token);

    Assert.False(report.Passed);
    var failure = Assert.Single(report.Failures);
    Assert.Contains("mail_for_tomorrow contains HenchmanMarshTonics", failure);
    Assert.Contains("mail_received=1 mail_for_tomorrow=0 events_seen=1", failure);

    cts.Cancel();
    try { await serverTask; } catch (OperationCanceledException) { }
}
```

- [ ] **Step 3: Run the focused failing runner tests**

Run:

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Runner.Tests/Runner.Tests.csproj --filter "WaitPlayer"
```

Expected: the new tests fail because the filters are ignored and the timeout detail does not include progression summaries.

- [ ] **Step 4: Add progression filter args**

In `src/Runner/Scenarios/ScenarioRunner.cs`, add these properties to `WaitPlayerStepArgs` after `BathingClothes`:

```csharp
public string? MailReceived { get; set; }
public string? MailForTomorrow { get; set; }
public string? EventSeen { get; set; }
```

- [ ] **Step 5: Validate non-empty progression filters**

In `ValidateWaitPlayerArgs`, add this block after the tile validation:

```csharp
if (args.MailReceived is not null && string.IsNullOrWhiteSpace(args.MailReceived))
    throw new InvalidOperationException("wait.player requires args.mail_received to be non-empty when supplied");
if (args.MailForTomorrow is not null && string.IsNullOrWhiteSpace(args.MailForTomorrow))
    throw new InvalidOperationException("wait.player requires args.mail_for_tomorrow to be non-empty when supplied");
if (args.EventSeen is not null && string.IsNullOrWhiteSpace(args.EventSeen))
    throw new InvalidOperationException("wait.player requires args.event_seen to be non-empty when supplied");
```

- [ ] **Step 6: Match progression lists**

Update `PlayerStateMatches` so `ProgressionFiltersMatch` is checked before buff filters:

```csharp
return StringFilterMatches(root, "location", args.Location)
    && NumberFilterMatches(root, "health", args.Health, args.HealthLt, args.HealthLte, args.HealthGt, args.HealthGte)
    && BoolFilterMatches(root, "swimming", args.Swimming)
    && BoolFilterMatches(root, "bathing_clothes", args.BathingClothes)
    && ProgressionFiltersMatch(root, args)
    && BuffFiltersMatch(root, args)
    && TileFilterMatches(root, args.X, args.Y);
```

Add these helper methods near the existing wait-player helper methods:

```csharp
private static bool ProgressionFiltersMatch(JsonElement root, WaitPlayerStepArgs args)
{
    return StringArrayContains(root, "mail_received", args.MailReceived)
        && StringArrayContains(root, "mail_for_tomorrow", args.MailForTomorrow)
        && StringArrayContains(root, "events_seen", args.EventSeen);
}

private static bool StringArrayContains(JsonElement root, string property, string? expected)
{
    if (expected is null)
        return true;

    return root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(property, out var array)
        && array.ValueKind == JsonValueKind.Array
        && array.EnumerateArray().Any(value =>
            value.ValueKind == JsonValueKind.String
            && string.Equals(value.GetString(), expected, StringComparison.Ordinal));
}
```

- [ ] **Step 7: Add filter labels and diagnostics**

In `FormatWaitPlayerFilters`, add these lines after the bathing-clothes line:

```csharp
if (args.MailReceived is not null) filters.Add($"mail_received contains {args.MailReceived}");
if (args.MailForTomorrow is not null) filters.Add($"mail_for_tomorrow contains {args.MailForTomorrow}");
if (args.EventSeen is not null) filters.Add($"events_seen contains {args.EventSeen}");
```

In `FormatObservedPlayer`, replace the final return with this:

```csharp
var progression = FormatObservedProgressionSummary(root.Value);
return $"health={health} location={location} tile={tile} swimming={swimming} bathing_clothes={bathing} {buffSummary} {progression}";
```

Add these helper methods near `FormatObservedBuffSummary`:

```csharp
private static string FormatObservedProgressionSummary(JsonElement root)
{
    return $"mail_received={CountStringArray(root, "mail_received")} " +
           $"mail_for_tomorrow={CountStringArray(root, "mail_for_tomorrow")} " +
           $"events_seen={CountStringArray(root, "events_seen")}";
}

private static string CountStringArray(JsonElement root, string property)
{
    return root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
        ? value.GetArrayLength().ToString(CultureInfo.InvariantCulture)
        : "?";
}
```

- [ ] **Step 8: Run the focused passing runner tests**

Run:

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Runner.Tests/Runner.Tests.csproj --filter "WaitPlayer"
```

Expected: all selected tests pass.

- [ ] **Step 9: Commit Task 2**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework add src/Runner/Scenarios/ScenarioRunner.cs tests/Runner.Tests/ScenarioRunnerTests.cs
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework commit -m "feat: wait for player progression flags"
```

---

### Task 3: Document Frobby Progression State

**Files:**
- Modify: `docs/rpc-schema.md`
- Modify: `docs/dsl-quickstart.md`
- Modify: `schemas/scenario.schema.json`
- Modify: `tests/Runner.Tests/ScenarioLoaderTests.cs`
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Write the schema-loader coverage test**

In `tests/Runner.Tests/ScenarioLoaderTests.cs`, add this test after `Load_WithProfile_RoundTripsProfile`:

```csharp
[Fact]
public void Load_WaitPlayerProgressionArgs_RoundTrips()
{
    var path = WriteTemp("""
{
  "name": "wait_progression",
  "steps": [
    {
      "action": "wait.player",
      "args": {
        "mail_received": "ShedRepaired",
        "mail_for_tomorrow": "HenchmanMarshTonics",
        "event_seen": "1000035"
      }
    }
  ]
}
""");

    var spec = ScenarioLoader.Load(path);

    Assert.Equal("wait.player", spec.Steps[0].Action);
    Assert.Equal("HenchmanMarshTonics", spec.Steps[0].Args!.Value.GetProperty("mail_for_tomorrow").GetString());
}
```

- [ ] **Step 2: Run the loader test**

Run:

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~ScenarioLoaderTests"
```

Expected: pass. The schema already permits free-form `args`; this test protects the documented scenario shape.

- [ ] **Step 3: Update the schema description**

In `schemas/scenario.schema.json`, change the `action.description` string to:

```json
"description": "Scenario step action. Unknown actions are invoked as RPC methods; runner-only actions include wait.player with health, buff, mail_received, mail_for_tomorrow, and event_seen filters."
```

- [ ] **Step 4: Update `state.player` docs**

In `docs/rpc-schema.md`, update the `state.player` response example by adding `mail_for_tomorrow` after `mail_received`:

```json
"mail_received": ["button_tut_1"],
"mail_for_tomorrow": ["HenchmanMarshTonics"],
"events_seen": ["5532011"],
```

Replace the progression paragraph below the example with:

```markdown
`mail_received`, `mail_for_tomorrow`, and `events_seen` expose the local
farmer's save-state flags for relationship, event, mail-gated, and pending-mail
scenario setup/verification. `mail_for_tomorrow` is useful for trigger actions
that schedule mail during day-ending without running Stardew's full overnight
sleep/save flow.
```

- [ ] **Step 5: Update `wait.player` docs**

In the runner convenience bullet near `wait.player`, add the progression filters to the supported-filter sentence so it reads:

```markdown
Supported filters are `location`, paired `x`/`y`, `health`, `health_lt`,
`health_lte`, `health_gt`, `health_gte`, `swimming`, `bathing_clothes`,
`mail_received`, `mail_for_tomorrow`, `event_seen`, `buff_id`, `buff_source`,
`buff_effect`, `buff_effect_gte`, `buff_count_gte`, and
`buff_any_effect_gte`; timeout details include the last observed health,
location, tile, transient state, buff summary, and progression-list counts.
```

In the `### wait.player runner action` section, update the supported-filter list with the same progression filter names and diagnostic wording.

- [ ] **Step 6: Update the quickstart**

In `docs/dsl-quickstart.md`, add this example after the existing player-effect wait example:

```markdown
Player progression waits can poll received mail, pending mail, and seen events:

```json
{
  "action": "wait.player",
  "args": {
    "mail_for_tomorrow": "HenchmanMarshTonics",
    "event_seen": "1000035",
    "timeout_ms": 10000,
    "poll_ms": 100
  }
}
```
```

- [ ] **Step 7: Mark Slice 16 active**

In `SVE_FROBBY_CAPABILITY_TODO.md`, change the Slice 16 heading from `Planning` to `Active`:

```markdown
- [ ] Active: Slice 16, late-game unlocks and trigger actions.
```

- [ ] **Step 8: Run doc/schema checks**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework diff --check
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~ScenarioLoaderTests"
```

Expected: no whitespace errors and selected loader tests pass.

- [ ] **Step 9: Commit Task 3**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework add docs/rpc-schema.md docs/dsl-quickstart.md schemas/scenario.schema.json tests/Runner.Tests/ScenarioLoaderTests.cs SVE_FROBBY_CAPABILITY_TODO.md
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework commit -m "docs: explain player progression waits"
```

---

### Task 4: Add SVE Trigger And Unlock Scenarios

**Files:**
- Create: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/21-sve-trigger-action-location-changed.test.json`
- Create: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/22-sve-trigger-action-day-ending-mail.test.json`
- Create: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/23-sve-progression-map-action-unlock.test.json`
- Modify: `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`

- [ ] **Step 1: Create or switch the SVE feature branch**

Run:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded switch -c feature/frobby-sve-slice-16-late-game-unlocks
```

Expected: the first command shows a clean tree. If the branch already exists, run:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded switch feature/frobby-sve-slice-16-late-game-unlocks
```

- [ ] **Step 2: Add Scenario 21**

Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/21-sve-trigger-action-location-changed.test.json` with:

```json
{
  "name": "sve_trigger_action_location_changed",
  "fixture": "m0spike_436515781",
  "config": { "seed": 436515781 },
  "steps": [
    { "action": "time.set", "args": { "time": 900, "day": 1, "season": "spring", "year": 1 } },
    { "action": "player.add_event_seen", "args": { "id": "418172" } },
    { "action": "player.warp", "args": { "location": "Farm", "x": 64, "y": 15 } },
    { "action": "wait.location", "args": { "location": "Farm", "timeout_ms": 10000, "poll_ms": 100 } },
    { "action": "player.warp", "args": { "location": "Custom_WizardBasement", "x": 8, "y": 21 } },
    { "action": "wait.location", "args": { "location": "Custom_WizardBasement", "timeout_ms": 10000, "poll_ms": 100 } },
    { "action": "wait.player", "args": { "event_seen": "1000035", "timeout_ms": 10000, "poll_ms": 100 } },
    { "action": "freeze.begin", "args": { "settle_timeout_ms": 10000, "poll_ms": 100 } },
    { "action": "screenshot.capture", "args": { "name": "final" } }
  ],
  "assertions": [
    {
      "type": "content.asset",
      "asset": "Data/TriggerActions",
      "asset_type": "data",
      "include_keys": true,
      "keys_limit": 500,
      "expr": "asset.keys contains 'FlashShifter.StardewValleyExpandedCP_WizardBasementEvent'",
      "message": "SVE should register the Wizard basement LocationChanged trigger action"
    },
    {
      "type": "state",
      "expr": "state.player.events_seen contains '418172'",
      "message": "Scenario should seed the magic ink prerequisite event"
    },
    {
      "type": "state",
      "expr": "state.player.events_seen contains '1000035'",
      "message": "SVE LocationChanged trigger action should mark Wizard basement event as seen"
    }
  ]
}
```

- [ ] **Step 3: Add Scenario 22**

Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/22-sve-trigger-action-day-ending-mail.test.json` with:

```json
{
  "name": "sve_trigger_action_day_ending_mail",
  "fixture": "m0spike_436515781",
  "config": { "seed": 436515781 },
  "steps": [
    { "action": "time.set", "args": { "time": 900, "day": 1, "season": "spring", "year": 1 } },
    { "action": "player.add_event_seen", "args": { "id": "1337737" } },
    { "action": "time.next_day", "args": { "settle_timeout_ms": 15000, "poll_ms": 100 } },
    { "action": "wait.player", "args": { "mail_for_tomorrow": "HenchmanMarshTonics", "timeout_ms": 10000, "poll_ms": 100 } },
    { "action": "freeze.begin", "args": { "settle_timeout_ms": 10000, "poll_ms": 100 } },
    { "action": "screenshot.capture", "args": { "name": "final" } }
  ],
  "assertions": [
    {
      "type": "content.asset",
      "asset": "Data/TriggerActions",
      "asset_type": "data",
      "include_keys": true,
      "keys_limit": 500,
      "expr": "asset.keys contains 'FlashShifter.StardewValleyExpandedCP_HenchmanTonics'",
      "message": "SVE should register the Henchman tonic DayEnding trigger action"
    },
    {
      "type": "state",
      "expr": "state.player.events_seen contains '1337737'",
      "message": "Scenario should seed the Henchman tonic prerequisite event"
    },
    {
      "type": "state",
      "expr": "state.player.mail_for_tomorrow contains 'HenchmanMarshTonics'",
      "message": "SVE DayEnding trigger action should schedule Henchman tonic mail"
    }
  ]
}
```

- [ ] **Step 4: Add Scenario 23**

Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/23-sve-progression-map-action-unlock.test.json` with:

```json
{
  "name": "sve_progression_map_action_unlock",
  "fixture": "m0spike_436515781",
  "config": { "seed": 436515781 },
  "steps": [
    { "action": "time.set", "args": { "time": 900, "day": 1, "season": "spring", "year": 1 } },
    { "action": "player.add_event_seen", "args": { "id": "908071" } },
    { "action": "player.warp", "args": { "location": "Backwoods", "x": 22, "y": 7 } },
    { "action": "wait.location", "args": { "location": "Backwoods", "timeout_ms": 10000, "poll_ms": 100 } },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.tile_actions.actions contains value 'LoadMap Custom_EnchantedGrove 30 32 0'",
        "params": {
          "location": "Backwoods",
          "x": 22,
          "y": 7,
          "radius": 25,
          "layers": ["Back"],
          "properties": ["TouchAction"]
        },
        "message": "SVE Enchanted Grove progression should add Backwoods warp TouchAction"
      }
    },
    { "action": "freeze.begin", "args": { "settle_timeout_ms": 10000, "poll_ms": 100 } },
    { "action": "screenshot.capture", "args": { "name": "final" } }
  ],
  "assertions": [
    {
      "type": "state",
      "expr": "state.player.events_seen contains '908071'",
      "message": "Scenario should seed the Enchanted Grove progression event"
    }
  ]
}
```

- [ ] **Step 5: Document the new SVE scenarios**

In `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`, add this paragraph after the Slice 15 profile paragraph:

```markdown
Scenarios `tests/sdv/21-sve-trigger-action-location-changed.test.json`,
`tests/sdv/22-sve-trigger-action-day-ending-mail.test.json`, and
`tests/sdv/23-sve-progression-map-action-unlock.test.json` cover Slice 16.
They use Frobby's neutral player progression waits to prove SVE
`LocationChanged`, `DayEnding`, and event-gated map action changes through real
game boundaries.
```

- [ ] **Step 6: Run scenario validation dry run**

Run:

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework /home/fintan/stardewRepos/StardewValleyExpanded/scripts/sdv-test --dry-run --headless --mod-set core /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/21-sve-trigger-action-location-changed.test.json /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/22-sve-trigger-action-day-ending-mail.test.json /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/23-sve-progression-map-action-unlock.test.json
```

Expected: the wrapper prints the Frobby repo-run command without schema validation errors.

- [ ] **Step 7: Commit Task 4 in SVE**

Run:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded add tests/sdv/21-sve-trigger-action-location-changed.test.json tests/sdv/22-sve-trigger-action-day-ending-mail.test.json tests/sdv/23-sve-progression-map-action-unlock.test.json docs/FROBBY.md
git -C /home/fintan/stardewRepos/StardewValleyExpanded commit -m "test: add SVE progression trigger scenarios"
```

---

### Task 5: Verify Slice 16 End To End

**Files:**
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Run Frobby focused unit tests**

Run:

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Protocol.Tests/Protocol.Tests.csproj --filter "FullyQualifiedName~PlayerStateSerializationTests"
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~StatePlayerHandlerTests"
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Runner.Tests/Runner.Tests.csproj --filter "WaitPlayer|ScenarioLoaderTests"
```

Expected: all selected tests pass.

- [ ] **Step 2: Run SVE Slice 16 scenarios headlessly**

Run:

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework /home/fintan/stardewRepos/StardewValleyExpanded/scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-16-progression-triggers /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/21-sve-trigger-action-location-changed.test.json /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/22-sve-trigger-action-day-ending-mail.test.json /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/23-sve-progression-map-action-unlock.test.json
```

Expected: all three SVE scenarios pass and generate reports under `/tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-16-progression-triggers`.

- [ ] **Step 3: Run SVE smoke regression subset**

Run:

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework /home/fintan/stardewRepos/StardewValleyExpanded/scripts/sdv-test --headless --mod-set core --no-build --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-16-smoke /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/01-sve-core-loads.test.json /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/02-sve-custom-locations-register.test.json /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/06-sve-tile-action-warp.test.json /home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/14-sve-passive-shadow-combat-state.test.json
```

Expected: selected earlier SVE state/action scenarios pass.

- [ ] **Step 4: Run Starberg smoke regression subset**

Run:

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework /home/fintan/stardewRepos/stonks/scripts/sdv-test --headless --no-build --report-dir /tmp/starberg-frobby-results-0.1.0/slice-16-smoke /home/fintan/stardewRepos/stonks/tests/sdv/01-starberg-terminal-open.test.json /home/fintan/stardewRepos/stonks/tests/sdv/20-starberg-ui-quote-shell.test.json /home/fintan/stardewRepos/stonks/tests/sdv/37-starberg-live-chart-heartbeat.test.json /home/fintan/stardewRepos/stonks/tests/sdv/70-starberg-save-reload-persistence.test.json
```

Expected: selected Starberg scenarios pass. If `--no-build` fails because the Starberg Release output is missing, rerun the same command without `--no-build`.

- [ ] **Step 5: Mark Slice 16 done**

In `SVE_FROBBY_CAPABILITY_TODO.md`, replace the Slice 16 block with:

```markdown
- [x] Done: Slice 16, late-game unlocks and trigger actions.
  - SVE pressure: event/mail-gated regions, minecart or bridge unlocks, trigger actions, shrines, and map mutations over progression.
  - Frobby goal: seed progression state, observe trigger-action effects, assert map/action changes, and verify unlocks across day/event boundaries.
  - Design spec: `docs/superpowers/specs/2026-05-15-sve-slice-16-late-game-unlocks-trigger-actions-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-15-sve-slice-16-late-game-unlocks-trigger-actions.md`.
  - Done: `state.player.mail_for_tomorrow`, progression-aware `wait.player` filters for `mail_received`, `mail_for_tomorrow`, and `event_seen`, and SVE scenarios 21-23 covering LocationChanged trigger actions, DayEnding mail scheduling, and Enchanted Grove map-action unlocks.
  - Follow-up candidates: Frontier Farm minecart/bridge/desert shortcut coverage once farm-type fixtures exist; direct trigger-action diagnostics if future mods need richer trigger introspection.
```

- [ ] **Step 6: Commit final Frobby TODO status**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework add SVE_FROBBY_CAPABILITY_TODO.md
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework commit -m "docs: mark sve slice 16 complete"
```

---

### Task 6: Final Full Checks And Status

**Files:**
- No code changes expected.

- [ ] **Step 1: Run Frobby aggregate tests for touched projects**

Run:

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Protocol.Tests/Protocol.Tests.csproj
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Harness.Tests/Harness.Tests.csproj
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Runner.Tests/Runner.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 2: Check both worktrees**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
```

Expected: both trees are clean and on their Slice 16 feature branches.

- [ ] **Step 3: Report verification evidence**

Summarize:

```text
Frobby branch:
- branch from `git -C /home/fintan/stardewRepos/frobby/sdv-test-framework branch --show-current`
- commits from `git -C /home/fintan/stardewRepos/frobby/sdv-test-framework log --oneline main..HEAD`
- unit test commands and pass/fail status from Task 6 Step 1

SVE branch:
- branch from `git -C /home/fintan/stardewRepos/StardewValleyExpanded branch --show-current`
- commits from `git -C /home/fintan/stardewRepos/StardewValleyExpanded log --oneline master..HEAD`
- scenario report directory `/tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-16-progression-triggers`
- scenario pass/fail status from Task 5 Step 2

Starberg smoke:
- command used in Task 5 Step 4
- pass/fail status from Task 5 Step 4
```

Do not merge SVE to `master`. Ask for the next integration action after reporting.

---

## Self-Review

Spec coverage:

- `state.player.mail_for_tomorrow`: Task 1.
- `wait.player` progression filters and diagnostics: Task 2.
- Runtime trigger-action inspection through `content.asset`: Task 4 scenarios 21 and 22.
- SVE LocationChanged trigger proof: Task 4 scenario 21.
- SVE DayEnding mail proof: Task 4 scenario 22.
- SVE progression-gated map/action proof: Task 4 scenario 23.
- Docs and TODO updates: Tasks 3 and 5.
- Starberg/SVE regression checks: Task 5.

Type consistency:

- JSON `mail_for_tomorrow` maps to C# `MailForTomorrow` through existing `ProtocolJson.Options`.
- JSON `event_seen` maps to `WaitPlayerStepArgs.EventSeen`.
- Existing `events_seen` remains the `state.player` response list name.
- `content.asset` trigger-action assertions use `asset.keys contains` because SVE trigger-action keys contain dots and cannot be addressed through the existing dotted expression-path resolver.
