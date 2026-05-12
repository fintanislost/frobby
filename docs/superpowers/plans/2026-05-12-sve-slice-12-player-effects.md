# SVE Slice 12 Player Effects Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add neutral Frobby support for active player buffs, swimming/bathing transient state, and SVE swim-buff validation.

**Architecture:** Enrich the existing `state.player` response with additive transient-state fields and active buff summaries, then add a narrow `player.set_transient_state` setup RPC. Keep runner logic in `ScenarioRunner` by extending the existing runner-only `wait.player` polling path rather than adding a new state RPC.

**Tech Stack:** C#/.NET 6 Harness + Protocol, .NET 10 Runner, xUnit tests, JSON scenarios, SMAPI/Stardew runtime reflection where needed.

---

## File Structure

- Modify `src/Protocol/Models/PlayerState.cs`: add `Swimming`, `BathingClothes`, `IsBusy`, `CanMove`, `Buffs`, `PlayerBuffSummary`, `PlayerBuffEffects`, `SetTransientStateRequest`, and `SetTransientStateResult`.
- Modify `tests/Protocol.Tests/PlayerStateSerializationTests.cs`: prove snake-case transient fields and buff effect serialization.
- Modify `src/Harness/Handlers/StatePlayerHandler.cs`: add world-facing transient and buff interfaces, project fake and live buffs, read live `Farmer.buffs` best-effort.
- Modify `tests/Harness.Tests/StatePlayerHandlerTests.cs`: add fake transient/buff data and assertions.
- Create `src/Harness/Handlers/PlayerSetTransientStateHandler.cs`: neutral setup action for `swimming` and `bathing_clothes`.
- Create `tests/Harness.Tests/PlayerSetTransientStateHandlerTests.cs`: validate missing params, no-op request, and partial updates against a fake world.
- Modify `src/Harness/ModEntry.cs`: register `player.set_transient_state` and add it to startup logging if present nearby.
- Modify `src/Runner/Scenarios/ScenarioRunner.cs`: extend `WaitPlayerStepArgs`, matching, formatting, and timeout diagnostics.
- Modify `tests/Runner.Tests/ScenarioRunnerTests.cs`: add `wait.player` matching and timeout coverage for transient/buff filters.
- Modify `schemas/scenario.schema.json`: document the new step/action and assertion surface.
- Modify `docs/rpc-schema.md` and `docs/dsl-quickstart.md`: document `state.player` additions, `player.set_transient_state`, and player-effect waits.
- Modify `SVE_FROBBY_CAPABILITY_TODO.md`: mark Slice 12 Active during implementation and Done at the end.
- In SVE repo, create `tests/sdv/17-sve-player-effects-swim-buff.test.json` on a feature branch, with no merge to SVE master unless explicitly requested.

---

### Task 1: Protocol Player Effect DTOs

**Files:**
- Modify: `src/Protocol/Models/PlayerState.cs`
- Modify: `tests/Protocol.Tests/PlayerStateSerializationTests.cs`

- [ ] **Step 1: Write the failing protocol serialization test**

Add this test to `tests/Protocol.Tests/PlayerStateSerializationTests.cs`:

```csharp
[Fact]
public void Serialize_PlayerEffects_UsesSnakeCaseFields()
{
    var p = new PlayerState
    {
        Name = "Tester",
        Location = "Custom_SpriteSpring2",
        Tile = new TilePoint { X = 12, Y = 18 },
        Swimming = true,
        BathingClothes = true,
        IsBusy = false,
        CanMove = true,
        Buffs =
        {
            new PlayerBuffSummary
            {
                Id = "1",
                DisplayName = "Fishing",
                Source = "food",
                MillisecondsDuration = 720000,
                TotalMillisecondsDuration = 720000,
                RuntimeType = "Buff",
                Effects = new PlayerBuffEffects
                {
                    FishingLevel = 3,
                    Attack = 0,
                },
            },
        },
    };

    var json = JsonSerializer.Serialize(p, ProtocolJson.Options);

    Assert.Contains("\"swimming\":true", json);
    Assert.Contains("\"bathing_clothes\":true", json);
    Assert.Contains("\"is_busy\":false", json);
    Assert.Contains("\"can_move\":true", json);
    Assert.Contains("\"buffs\":[", json);
    Assert.Contains("\"display_name\":\"Fishing\"", json);
    Assert.Contains("\"milliseconds_duration\":720000", json);
    Assert.Contains("\"total_milliseconds_duration\":720000", json);
    Assert.Contains("\"fishing_level\":3", json);
    Assert.Contains("\"attack\":0", json);
    Assert.DoesNotContain("BathingClothes", json);
    Assert.DoesNotContain("MillisecondsDuration", json);
}
```

- [ ] **Step 2: Run the protocol test and verify RED**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter PlayerStateSerializationTests.Serialize_PlayerEffects_UsesSnakeCaseFields
```

Expected: FAIL at compile time because `PlayerState.Swimming`, `PlayerBuffSummary`, and `PlayerBuffEffects` do not exist.

- [ ] **Step 3: Implement the minimal protocol DTOs**

In `src/Protocol/Models/PlayerState.cs`, add `Buffs` to `PlayerState`:

```csharp
public bool Swimming { get; set; }
public bool BathingClothes { get; set; }
public bool IsBusy { get; set; }
public bool CanMove { get; set; }
public List<PlayerBuffSummary> Buffs { get; set; } = new();
```

Add these classes below `PlayerItemSummary`:

```csharp
/// <summary>Compact active-buff descriptor for a player snapshot.</summary>
public sealed class PlayerBuffSummary
{
    public string? Id { get; set; }
    public string? DisplayName { get; set; }
    public string? Source { get; set; }
    public int? MillisecondsDuration { get; set; }
    public int? TotalMillisecondsDuration { get; set; }
    public PlayerBuffEffects Effects { get; set; } = new();
    public string? RuntimeType { get; set; }
}

/// <summary>Known numeric buff effects. Unknown effects are omitted by the projector.</summary>
public sealed class PlayerBuffEffects
{
    public int FarmingLevel { get; set; }
    public int FishingLevel { get; set; }
    public int MiningLevel { get; set; }
    public int ForagingLevel { get; set; }
    public int LuckLevel { get; set; }
    public int Attack { get; set; }
    public int Defense { get; set; }
    public int Speed { get; set; }
    public int MagnetRadius { get; set; }
}
```

Add request/response DTOs below `TilePoint`:

```csharp
/// <summary>Request shape for <c>player.set_transient_state</c>.</summary>
public sealed class SetTransientStateRequest
{
    public bool? Swimming { get; set; }
    public bool? BathingClothes { get; set; }
}

/// <summary>Response shape for <c>player.set_transient_state</c>.</summary>
public sealed class SetTransientStateResult : MutatorOk
{
    public bool PreviousSwimming { get; set; }
    public bool PreviousBathingClothes { get; set; }
    public bool Swimming { get; set; }
    public bool BathingClothes { get; set; }
}
```

- [ ] **Step 4: Run the protocol test and verify GREEN**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter PlayerStateSerializationTests.Serialize_PlayerEffects_UsesSnakeCaseFields
```

Expected: PASS.

- [ ] **Step 5: Run the full protocol project**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj
```

Expected: PASS with 118+ tests.

- [ ] **Step 6: Commit protocol DTOs**

```bash
git add src/Protocol/Models/PlayerState.cs tests/Protocol.Tests/PlayerStateSerializationTests.cs
git commit -m "feat: add player effect protocol models"
```

---

### Task 2: Harness `state.player` Buff And Transient Projection

**Files:**
- Modify: `src/Harness/Handlers/StatePlayerHandler.cs`
- Modify: `tests/Harness.Tests/StatePlayerHandlerTests.cs`

- [ ] **Step 1: Write the failing harness projection test**

In `tests/Harness.Tests/StatePlayerHandlerTests.cs`, extend `Handle_IncludesInventoryItemSummaries` after existing inventory assertions:

```csharp
Assert.True(state.Swimming);
Assert.True(state.BathingClothes);
Assert.False(state.IsBusy);
Assert.True(state.CanMove);
var buff = Assert.Single(state.Buffs);
Assert.Equal("1", buff.Id);
Assert.Equal("Fishing", buff.DisplayName);
Assert.Equal("food", buff.Source);
Assert.Equal(720000, buff.MillisecondsDuration);
Assert.Equal(720000, buff.TotalMillisecondsDuration);
Assert.Equal("Buff", buff.RuntimeType);
Assert.Equal(3, buff.Effects.FishingLevel);
Assert.Equal(1, buff.Effects.Speed);
```

Update `FakePlayerStateWorld` with properties and buffs:

```csharp
public bool Swimming => true;
public bool BathingClothes => true;
public bool IsBusy => false;
public bool CanMove => true;
public IReadOnlyList<IPlayerBuffSummary> Buffs { get; } = new[]
{
    new PlayerBuffProjection(
        "1",
        "Fishing",
        "food",
        720000,
        720000,
        new PlayerBuffEffects { FishingLevel = 3, Speed = 1 },
        "Buff"),
};
```

- [ ] **Step 2: Run the harness test and verify RED**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter StatePlayerHandlerTests.Handle_IncludesInventoryItemSummaries
```

Expected: FAIL at compile time because `IPlayerStateWorld` does not expose transient fields or buffs.

- [ ] **Step 3: Add world interfaces and projection**

In `src/Harness/Handlers/StatePlayerHandler.cs`, add to `PlayerState` construction:

```csharp
Swimming = world.Swimming,
BathingClothes = world.BathingClothes,
IsBusy = world.IsBusy,
CanMove = world.CanMove,
Buffs = world.Buffs
    .Select(b => new PlayerBuffSummary
    {
        Id = b.Id,
        DisplayName = b.DisplayName,
        Source = b.Source,
        MillisecondsDuration = b.MillisecondsDuration,
        TotalMillisecondsDuration = b.TotalMillisecondsDuration,
        Effects = b.Effects,
        RuntimeType = b.RuntimeType,
    })
    .ToList(),
```

Extend `IPlayerStateWorld`:

```csharp
bool Swimming { get; }
bool BathingClothes { get; }
bool IsBusy { get; }
bool CanMove { get; }
IReadOnlyList<IPlayerBuffSummary> Buffs { get; }
```

Add interfaces/record after `PlayerInventoryItem`:

```csharp
internal interface IPlayerBuffSummary
{
    string? Id { get; }
    string? DisplayName { get; }
    string? Source { get; }
    int? MillisecondsDuration { get; }
    int? TotalMillisecondsDuration { get; }
    PlayerBuffEffects Effects { get; }
    string? RuntimeType { get; }
}

internal sealed record PlayerBuffProjection(
    string? Id,
    string? DisplayName,
    string? Source,
    int? MillisecondsDuration,
    int? TotalMillisecondsDuration,
    PlayerBuffEffects Effects,
    string? RuntimeType) : IPlayerBuffSummary;
```

- [ ] **Step 4: Add live player projection**

In `SdvPlayerStateWorld`, add:

```csharp
public bool Swimming => Player.swimming.Value;
public bool BathingClothes => Player.bathingClothes.Value;
public bool IsBusy => Game1.player?.isBusy() ?? false;
public bool CanMove => Game1.player?.CanMove ?? false;
public IReadOnlyList<IPlayerBuffSummary> Buffs => ProjectBuffs(Player.buffs).ToList();
```

Add helpers inside `SdvPlayerStateWorld`:

```csharp
private static IEnumerable<IPlayerBuffSummary> ProjectBuffs(object? buffManager)
{
    foreach (var buff in EnumerateBuffs(buffManager))
    {
        yield return new PlayerBuffProjection(
            ReadBuffString(buff, "id", "Id", "which", "Which"),
            ReadBuffString(buff, "displayName", "DisplayName", "displaySource", "DisplaySource"),
            ReadBuffString(buff, "source", "Source"),
            ReadBuffInt(buff, "millisecondsDuration", "MillisecondsDuration"),
            ReadBuffInt(buff, "totalMillisecondsDuration", "TotalMillisecondsDuration"),
            ProjectEffects(ReflectionValue.ReadRaw(buff, "effects", "Effects")),
            buff.GetType().Name);
    }
}

private static IEnumerable<object> EnumerateBuffs(object? buffManager)
{
    var raw = ReflectionValue.ReadRaw(buffManager, "AppliedBuffs", "appliedBuffs", "Buffs", "buffs");
    if (raw is System.Collections.IDictionary dictionary)
    {
        foreach (var value in dictionary.Values)
            if (value is not null)
                yield return value;
        yield break;
    }

    if (raw is System.Collections.IEnumerable enumerable && raw is not string)
    {
        foreach (var value in enumerable)
            if (value is not null)
                yield return value;
    }
}

private static PlayerBuffEffects ProjectEffects(object? effects)
{
    return new PlayerBuffEffects
    {
        FarmingLevel = ReadBuffInt(effects, "FarmingLevel", "farmingLevel") ?? 0,
        FishingLevel = ReadBuffInt(effects, "FishingLevel", "fishingLevel") ?? 0,
        MiningLevel = ReadBuffInt(effects, "MiningLevel", "miningLevel") ?? 0,
        ForagingLevel = ReadBuffInt(effects, "ForagingLevel", "foragingLevel") ?? 0,
        LuckLevel = ReadBuffInt(effects, "LuckLevel", "luckLevel", "Luck", "luck") ?? 0,
        Attack = ReadBuffInt(effects, "Attack", "attack") ?? 0,
        Defense = ReadBuffInt(effects, "Defense", "defense") ?? 0,
        Speed = ReadBuffInt(effects, "Speed", "speed") ?? 0,
        MagnetRadius = ReadBuffInt(effects, "MagnetRadius", "magnetRadius") ?? 0,
    };
}

private static string? ReadBuffString(object? source, params string[] names)
{
    var value = ReflectionValue.ReadString(source, names);
    return string.IsNullOrWhiteSpace(value) ? null : value;
}

private static int? ReadBuffInt(object? source, params string[] names)
{
    var raw = ReflectionValue.ReadRaw(source, names);
    if (raw is null)
        return null;

    if (raw.GetType().Name.Contains("NetField", StringComparison.Ordinal))
        raw = ReflectionValue.ReadRaw(raw, "Value", "value");

    try
    {
        return Convert.ToInt32(raw, System.Globalization.CultureInfo.InvariantCulture);
    }
    catch
    {
        return int.TryParse(raw.ToString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}
```

If `AppliedBuffs` is not the right live member, run a focused live probe later and update only `EnumerateBuffs`; keep DTO/test behavior stable.

- [ ] **Step 5: Run the state player test and verify GREEN**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter StatePlayerHandlerTests.Handle_IncludesInventoryItemSummaries
```

Expected: PASS.

- [ ] **Step 6: Run full harness tests**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj
```

Expected: PASS, with existing live-integration skips.

- [ ] **Step 7: Commit state projection**

```bash
git add src/Harness/Handlers/StatePlayerHandler.cs tests/Harness.Tests/StatePlayerHandlerTests.cs
git commit -m "feat: expose player transient state and buffs"
```

---

### Task 3: `player.set_transient_state` Handler

**Files:**
- Create: `src/Harness/Handlers/PlayerSetTransientStateHandler.cs`
- Create: `tests/Harness.Tests/PlayerSetTransientStateHandlerTests.cs`
- Modify: `src/Harness/ModEntry.cs`

- [ ] **Step 1: Write failing handler tests**

Create `tests/Harness.Tests/PlayerSetTransientStateHandlerTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class PlayerSetTransientStateHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => PlayerSetTransientStateHandler.Handle(null, new FakeTransientStateWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_EmptyRequest_ThrowsInvalidParams()
    {
        var req = ProtocolJson.ToElement(new SetTransientStateRequest());

        var ex = Assert.Throws<JsonRpcException>(() => PlayerSetTransientStateHandler.Handle(req, new FakeTransientStateWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("swimming", ex.Message);
    }

    [Fact]
    public void Handle_UpdatesOnlyProvidedFields()
    {
        var world = new FakeTransientStateWorld { Swimming = false, BathingClothes = true };
        var req = ProtocolJson.ToElement(new SetTransientStateRequest { Swimming = true });

        var result = PlayerSetTransientStateHandler.Handle(req, world);
        var state = JsonSerializer.Deserialize<SetTransientStateResult>(result, ProtocolJson.Options)!;

        Assert.False(state.PreviousSwimming);
        Assert.True(state.PreviousBathingClothes);
        Assert.True(state.Swimming);
        Assert.True(state.BathingClothes);
        Assert.True(world.Swimming);
        Assert.True(world.BathingClothes);
    }

    private sealed class FakeTransientStateWorld : ITransientPlayerStateWorld
    {
        public bool Swimming { get; set; }
        public bool BathingClothes { get; set; }
        public int Tick => 42;
        public void RequireWorldReady() { }
    }
}
```

- [ ] **Step 2: Run tests and verify RED**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter PlayerSetTransientStateHandlerTests
```

Expected: FAIL at compile time because `PlayerSetTransientStateHandler` and `ITransientPlayerStateWorld` do not exist.

- [ ] **Step 3: Implement handler**

Create `src/Harness/Handlers/PlayerSetTransientStateHandler.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>player.set_transient_state</c>. Runs on the game thread.</summary>
public static class PlayerSetTransientStateHandler
{
    public const string Method = "player.set_transient_state";

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, new SdvTransientPlayerStateWorld());

    internal static JsonElement Handle(JsonElement? paramsElement, ITransientPlayerStateWorld world)
    {
        var req = RpcParams.Required<SetTransientStateRequest>(paramsElement);
        if (req.Swimming is null && req.BathingClothes is null)
        {
            throw new JsonRpcException(
                JsonRpcErrorCode.InvalidParams,
                "params.swimming or params.bathing_clothes is required");
        }

        world.RequireWorldReady();

        var previousSwimming = world.Swimming;
        var previousBathingClothes = world.BathingClothes;

        if (req.Swimming is not null)
            world.Swimming = req.Swimming.Value;
        if (req.BathingClothes is not null)
            world.BathingClothes = req.BathingClothes.Value;

        return ProtocolJson.ToElement(new SetTransientStateResult
        {
            Ok = true,
            Tick = world.Tick,
            PreviousSwimming = previousSwimming,
            PreviousBathingClothes = previousBathingClothes,
            Swimming = world.Swimming,
            BathingClothes = world.BathingClothes,
        });
    }
}

internal interface ITransientPlayerStateWorld
{
    bool Swimming { get; set; }
    bool BathingClothes { get; set; }
    int Tick { get; }
    void RequireWorldReady();
}

internal sealed class SdvTransientPlayerStateWorld : ITransientPlayerStateWorld
{
    public bool Swimming
    {
        get => Game1.player.swimming.Value;
        set => Game1.player.swimming.Value = value;
    }

    public bool BathingClothes
    {
        get => Game1.player.bathingClothes.Value;
        set => Game1.player.bathingClothes.Value = value;
    }

    public int Tick => Game1.ticks;

    public void RequireWorldReady() => RpcPreconditions.RequireWorldReady();
}
```

- [ ] **Step 4: Register the handler**

In `src/Harness/ModEntry.cs`, after `PlayerSetFriendshipHandler` registration, add:

```csharp
_rpc.Register(PlayerSetTransientStateHandler.Method, p => PlayerSetTransientStateHandler.Handle(p));
```

- [ ] **Step 5: Run handler tests and verify GREEN**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter PlayerSetTransientStateHandlerTests
```

Expected: PASS.

- [ ] **Step 6: Run full harness tests**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj
```

Expected: PASS.

- [ ] **Step 7: Commit transient-state action**

```bash
git add src/Harness/Handlers/PlayerSetTransientStateHandler.cs tests/Harness.Tests/PlayerSetTransientStateHandlerTests.cs src/Harness/ModEntry.cs
git commit -m "feat: add player transient state mutator"
```

---

### Task 4: Runner `wait.player` Effect Filters

**Files:**
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`
- Modify: `tests/Runner.Tests/ScenarioRunnerTests.cs`

- [ ] **Step 1: Write the matching runner test**

Add a test near existing `wait.player` tests in `tests/Runner.Tests/ScenarioRunnerTests.cs`:

```csharp
[Fact]
public async Task WaitPlayer_MatchesTransientStateAndBuffEffects()
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
                        ? "{\"name\":\"Tester\",\"health\":100,\"location\":\"Custom_SpriteSpring2\",\"tile\":{\"x\":10,\"y\":20},\"swimming\":true,\"bathing_clothes\":false,\"buffs\":[]}"
                        : "{\"name\":\"Tester\",\"health\":100,\"location\":\"Custom_SpriteSpring2\",\"tile\":{\"x\":10,\"y\":20},\"swimming\":true,\"bathing_clothes\":false,\"buffs\":[{\"id\":\"1\",\"display_name\":\"Fishing\",\"effects\":{\"fishing_level\":3},\"milliseconds_duration\":720000}]}").RootElement,
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
        Name = "wait_player_buff",
        Steps =
        {
            new ScenarioStep
            {
                Action = "wait.player",
                Args = JsonDocument.Parse("{\"location\":\"Custom_SpriteSpring2\",\"swimming\":true,\"buff_count_gte\":1,\"buff_any_effect_gte\":{\"effects\":[\"fishing_level\",\"farming_level\",\"mining_level\",\"foraging_level\",\"attack\"],\"value\":3},\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
            },
        },
    }, cts.Token);

    Assert.True(report.Passed);
    Assert.True(playerPolls >= 2);

    cts.Cancel();
    try { await serverTask; } catch (OperationCanceledException) { }
}
```

- [ ] **Step 2: Write timeout diagnostics test**

Add:

```csharp
[Fact]
public async Task WaitPlayer_TimeoutIncludesTransientAndBuffSummary()
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
                    "state.player" => JsonDocument.Parse("{\"name\":\"Tester\",\"health\":100,\"location\":\"Farm\",\"tile\":{\"x\":64,\"y\":15},\"swimming\":false,\"bathing_clothes\":false,\"buffs\":[]}").RootElement,
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
        Name = "wait_player_buff_timeout",
        Steps =
        {
            new ScenarioStep
            {
                Action = "wait.player",
                Args = JsonDocument.Parse("{\"swimming\":true,\"buff_count_gte\":1,\"timeout_ms\":20,\"poll_ms\":1}").RootElement,
            },
        },
    }, cts.Token);

    Assert.False(report.Passed);
    var failure = Assert.Single(report.Failures);
    Assert.Contains("swimming=true", failure);
    Assert.Contains("last observed health=100 location=Farm tile=64,15 swimming=false bathing_clothes=false buffs=0", failure);

    cts.Cancel();
    try { await serverTask; } catch (OperationCanceledException) { }
}
```

- [ ] **Step 3: Run runner tests and verify RED**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "WaitPlayer_MatchesTransientStateAndBuffEffects|WaitPlayer_TimeoutIncludesTransientAndBuffSummary"
```

Expected: FAIL because `wait.player` ignores `swimming`, `buff_count_gte`, and `buff_any_effect_gte`.

- [ ] **Step 4: Extend wait args and validation**

In `WaitPlayerStepArgs`, add:

```csharp
public bool? Swimming { get; set; }
public bool? BathingClothes { get; set; }
public string? BuffId { get; set; }
public string? BuffSource { get; set; }
public string? BuffEffect { get; set; }
public int? BuffEffectGte { get; set; }
public int? BuffCountGte { get; set; }
public BuffAnyEffectFilter? BuffAnyEffectGte { get; set; }
```

Add nested args class near `WaitPlayerStepArgs`:

```csharp
private sealed class BuffAnyEffectFilter
{
    public List<string> Effects { get; set; } = new();
    public int Value { get; set; }
}
```

Extend `ValidateWaitPlayerArgs`:

```csharp
if (args.BuffCountGte is < 0)
    throw new InvalidOperationException("wait.player requires args.buff_count_gte >= 0");
if (args.BuffEffectGte is not null && string.IsNullOrWhiteSpace(args.BuffEffect))
    throw new InvalidOperationException("wait.player requires args.buff_effect when using args.buff_effect_gte");
if (args.BuffAnyEffectGte is not null && args.BuffAnyEffectGte.Effects.Count == 0)
    throw new InvalidOperationException("wait.player requires args.buff_any_effect_gte.effects");
```

- [ ] **Step 5: Implement matching helpers**

Update `PlayerStateMatches`:

```csharp
return StringFilterMatches(root, "location", args.Location)
    && NumberFilterMatches(root, "health", args.Health, args.HealthLt, args.HealthLte, args.HealthGt, args.HealthGte)
    && BoolFilterMatches(root, "swimming", args.Swimming)
    && BoolFilterMatches(root, "bathing_clothes", args.BathingClothes)
    && BuffFiltersMatch(root, args)
    && TileFilterMatches(root, args.X, args.Y);
```

Add helpers near `PlayerStateMatches`:

```csharp
private static bool BuffFiltersMatch(JsonElement root, WaitPlayerStepArgs args)
{
    var hasBuffFilters = args.BuffId is not null
        || args.BuffSource is not null
        || args.BuffEffect is not null
        || args.BuffCountGte is not null
        || args.BuffAnyEffectGte is not null;
    if (!hasBuffFilters)
        return true;

    if (root.ValueKind != JsonValueKind.Object
        || !root.TryGetProperty("buffs", out var buffs)
        || buffs.ValueKind != JsonValueKind.Array)
        return false;

    var buffList = buffs.EnumerateArray().ToList();
    if (args.BuffCountGte is not null && buffList.Count < args.BuffCountGte.Value)
        return false;

    return buffList.Any(buff =>
        StringFilterMatches(buff, "id", args.BuffId)
        && StringFilterMatches(buff, "source", args.BuffSource)
        && BuffEffectMatches(buff, args.BuffEffect, args.BuffEffectGte)
        && BuffAnyEffectMatches(buff, args.BuffAnyEffectGte));
}

private static bool BuffEffectMatches(JsonElement buff, string? effect, int? minimum)
{
    if (string.IsNullOrWhiteSpace(effect))
        return true;
    if (!TryReadBuffEffect(buff, effect, out var value))
        return false;
    return minimum is null || value >= minimum.Value;
}

private static bool BuffAnyEffectMatches(JsonElement buff, BuffAnyEffectFilter? filter)
{
    if (filter is null)
        return true;

    return filter.Effects.Any(effect =>
        TryReadBuffEffect(buff, effect, out var value) && value >= filter.Value);
}

private static bool TryReadBuffEffect(JsonElement buff, string effect, out int value)
{
    value = 0;
    return buff.ValueKind == JsonValueKind.Object
        && buff.TryGetProperty("effects", out var effects)
        && effects.ValueKind == JsonValueKind.Object
        && effects.TryGetProperty(effect, out var effectValue)
        && effectValue.ValueKind == JsonValueKind.Number
        && effectValue.TryGetInt32(out value);
}
```

- [ ] **Step 6: Extend formatting and timeout detail**

In `FormatWaitPlayerFilters`, add:

```csharp
if (args.Swimming is not null) filters.Add($"swimming={args.Swimming.Value.ToString().ToLowerInvariant()}");
if (args.BathingClothes is not null) filters.Add($"bathing_clothes={args.BathingClothes.Value.ToString().ToLowerInvariant()}");
if (args.BuffCountGte is not null) filters.Add($"buff_count_gte={args.BuffCountGte}");
if (args.BuffId is not null) filters.Add($"buff_id={args.BuffId}");
if (args.BuffSource is not null) filters.Add($"buff_source={args.BuffSource}");
if (args.BuffEffect is not null) filters.Add(args.BuffEffectGte is null ? $"buff_effect={args.BuffEffect}" : $"buff_effect={args.BuffEffect}>={args.BuffEffectGte}");
if (args.BuffAnyEffectGte is not null) filters.Add($"buff_any_effect_gte={string.Join("|", args.BuffAnyEffectGte.Effects)}>={args.BuffAnyEffectGte.Value}");
```

In `FormatObservedPlayer`, append transient/buff detail:

```csharp
var swimming = ReadBoolText(root.Value, "swimming");
var bathing = ReadBoolText(root.Value, "bathing_clothes");
var buffSummary = FormatObservedBuffSummary(root.Value);
return $"health={health} location={location} tile={tile} swimming={swimming} bathing_clothes={bathing} {buffSummary}";
```

Add helpers:

```csharp
private static string ReadBoolText(JsonElement root, string property)
{
    return root.TryGetProperty(property, out var value)
        && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
        ? value.GetBoolean().ToString().ToLowerInvariant()
        : "?";
}

private static string FormatObservedBuffSummary(JsonElement root)
{
    if (!root.TryGetProperty("buffs", out var buffs) || buffs.ValueKind != JsonValueKind.Array)
        return "buffs=?";

    var list = buffs.EnumerateArray().ToList();
    if (list.Count == 0)
        return "buffs=0";

    var details = list.Take(3).Select(buff =>
    {
        var id = buff.TryGetProperty("id", out var idValue) && idValue.ValueKind == JsonValueKind.String
            ? idValue.GetString()
            : "?";
        var effects = buff.TryGetProperty("effects", out var effectsValue) && effectsValue.ValueKind == JsonValueKind.Object
            ? string.Join("|", effectsValue.EnumerateObject()
                .Where(prop => prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out var v) && v != 0)
                .Select(prop => $"{prop.Name}={prop.Value.GetInt32()}"))
            : string.Empty;
        return string.IsNullOrWhiteSpace(effects) ? id ?? "?" : $"{id}:{effects}";
    });
    return $"buffs={list.Count} [{string.Join(", ", details)}]";
}
```

- [ ] **Step 7: Run runner tests and verify GREEN**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "WaitPlayer_MatchesTransientStateAndBuffEffects|WaitPlayer_TimeoutIncludesTransientAndBuffSummary"
```

Expected: PASS.

- [ ] **Step 8: Run full runner tests**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj
```

Expected: PASS with existing skipped live-integration tests.

- [ ] **Step 9: Commit wait support**

```bash
git add src/Runner/Scenarios/ScenarioRunner.cs tests/Runner.Tests/ScenarioRunnerTests.cs
git commit -m "feat: wait for player effects"
```

---

### Task 5: Docs, Schema, And Slice Tracker

**Files:**
- Modify: `schemas/scenario.schema.json`
- Modify: `docs/rpc-schema.md`
- Modify: `docs/dsl-quickstart.md`
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Update schema text**

In `schemas/scenario.schema.json`, update the step action description:

```json
"description": "Scenario step action. Unknown actions are invoked as RPC methods, including player.set_transient_state and fishing.sample_catch for ad hoc probes."
```

Update the assertion `params` description:

```json
"description": "Optional params passed to RPC-backed assertion types such as state.fishing_table or state.player."
```

- [ ] **Step 2: Validate schema JSON**

Run:

```bash
python3 -c "import json; json.load(open('schemas/scenario.schema.json', encoding='utf-8')); print('schema json ok')"
```

Expected: `schema json ok`.

- [ ] **Step 3: Update RPC docs**

In `docs/rpc-schema.md` under `state.player`, update the JSON example with:

```json
      "swimming": true,
      "bathing_clothes": false,
      "is_busy": false,
      "can_move": true,
      "buffs": [
        {
          "id": "1",
          "display_name": "Fishing",
          "source": "food",
          "milliseconds_duration": 720000,
          "total_milliseconds_duration": 720000,
          "effects": { "fishing_level": 3 },
          "runtime_type": "Buff"
        }
      ],
```

Add text after inventory/event flag explanation:

```markdown
`swimming`, `bathing_clothes`, `is_busy`, and `can_move` expose transient local
farmer state for mod behavior that keys off the player's current mode. `buffs`
contains active buff summaries projected from the live Stardew buff manager. Buff
effect fields use snake-case names such as `fishing_level`, `farming_level`,
`attack`, `defense`, and `speed`.
```

Add a new section after `player.set_money`:

````markdown
### player.set_transient_state

Sets selected local farmer transient-state booleans for tests. At least one of
`swimming` or `bathing_clothes` is required.

Request:
```json
→ { "jsonrpc": "2.0", "id": 10, "method": "player.set_transient_state", "params": { "swimming": true } }
```

Response:
```json
← { "jsonrpc": "2.0", "id": 10, "result": {
      "ok": true,
      "tick": 84200,
      "previous_swimming": false,
      "previous_bathing_clothes": false,
      "swimming": true,
      "bathing_clothes": false
   } }
```

**Preconditions:** world loaded.
**Side effects:** updates only the supplied local farmer transient-state fields.
**Implemented in:** `src/Harness/Handlers/PlayerSetTransientStateHandler.cs`.
**Tested in:** `tests/Harness.Tests/PlayerSetTransientStateHandlerTests.cs`.
````

Update the runner `wait.player` bullet and section to list:

```markdown
Additional supported filters are `swimming`, `bathing_clothes`, `buff_id`,
`buff_source`, `buff_effect`, `buff_effect_gte`, `buff_count_gte`, and
`buff_any_effect_gte`.
```

- [ ] **Step 4: Update quickstart**

In `docs/dsl-quickstart.md` after the player health wait example, add:

````markdown
Player effect waits can poll for transient state and active buffs:

```json
{
  "action": "player.set_transient_state",
  "args": { "swimming": true }
},
{
  "action": "wait.player",
  "args": {
    "swimming": true,
    "buff_count_gte": 1,
    "buff_any_effect_gte": {
      "effects": ["fishing_level", "farming_level", "mining_level", "foraging_level", "attack"],
      "value": 3
    },
    "timeout_ms": 10000,
    "poll_ms": 100
  }
}
```
````

- [ ] **Step 5: Update TODO to Active**

In `SVE_FROBBY_CAPABILITY_TODO.md`, change Slice 12 from Pending to Active:

```markdown
- [ ] Active: Slice 12, buffs, swimming, and timed player state.
  - Design spec: `docs/superpowers/specs/2026-05-12-sve-slice-12-player-effects-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-12-sve-slice-12-player-effects.md`.
```

Add an active line:

```markdown
  - Active: adding player transient-state projection, active buff summaries, `player.set_transient_state`, and an SVE swim-buff proof scenario.
```

- [ ] **Step 6: Run docs/schema checks**

Run:

```bash
git diff --check
python3 -c "import json; json.load(open('schemas/scenario.schema.json', encoding='utf-8')); print('schema json ok')"
```

Expected: no diff whitespace output and `schema json ok`.

- [ ] **Step 7: Commit docs**

```bash
git add schemas/scenario.schema.json docs/rpc-schema.md docs/dsl-quickstart.md SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "docs: document player effect testing"
```

---

### Task 6: SVE Proof Scenario

**Files:**
- In SVE repo, create: `tests/sdv/17-sve-player-effects-swim-buff.test.json`

- [ ] **Step 1: Prepare SVE feature branch**

Run:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded switch -c feature/frobby-sve-slice-12-player-effects
```

If already on a different SVE feature branch with unmerged work, stop and report. Do not merge to SVE master.

- [ ] **Step 2: Add initial SVE scenario**

Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/17-sve-player-effects-swim-buff.test.json`:

```json
{
  "name": "sve_player_effects_swim_buff",
  "fixture": "m0spike_436515781",
  "config": { "seed": 436515781 },
  "steps": [
    { "action": "time.set", "args": { "time": 900, "day": 1, "season": "spring", "year": 1 } },
    { "action": "player.warp", "args": { "location": "Custom_SpriteSpring2", "x": 12, "y": 18 } },
    { "action": "wait.location", "args": { "location": "Custom_SpriteSpring2", "timeout_ms": 10000, "poll_ms": 100 } },
    { "action": "player.set_transient_state", "args": { "swimming": true, "bathing_clothes": true } },
    {
      "action": "wait.player",
      "args": {
        "location": "Custom_SpriteSpring2",
        "swimming": true,
        "bathing_clothes": true,
        "buff_count_gte": 1,
        "buff_any_effect_gte": {
          "effects": ["farming_level", "fishing_level", "mining_level", "foraging_level", "attack"],
          "value": 3
        },
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
      "expr": "state.player.swimming == true",
      "message": "Player should remain in swimming state for SVE swim-buff check"
    },
    {
      "type": "state",
      "expr": "state.player.bathing_clothes == true",
      "message": "Player should remain in bathing clothes for SVE swim-buff check"
    }
  ]
}
```

- [ ] **Step 3: Dry-run SVE scenario**

Run:

```bash
env FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-12-player-effects SDV_TEST_MOD_CACHE=/home/fintan/stardewRepos/frobby/sdv-test-framework/.cache/deps ./tests/scripts/sdv-test-dry-run.sh tests/sdv/17-sve-player-effects-swim-buff.test.json
```

Expected: PASS dry-run behavior. If the dry-run script still points at an old worktree, update only the environment value in the command, not the script.

- [ ] **Step 4: Run focused headless SVE scenario**

Run:

```bash
env FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-12-player-effects SDV_TEST_MOD_CACHE=/home/fintan/stardewRepos/frobby/sdv-test-framework/.cache/deps ./scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-12-player-effects tests/sdv/17-sve-player-effects-swim-buff.test.json
```

Expected: `1/1 passed`.

If the run fails because `Custom_SpriteSpring2` is not loaded/reachable in the fixture, inspect `state.locations` or the generated report, then try `Custom_GrandpasGrove` with the same neutral Frobby assertions. Do not change Frobby to special-case SVE.

- [ ] **Step 5: Commit SVE scenario**

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded add tests/sdv/17-sve-player-effects-swim-buff.test.json
git -C /home/fintan/stardewRepos/StardewValleyExpanded commit -m "test: add player effects frobby scenario"
```

---

### Task 7: Final Verification And Slice Completion

**Files:**
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Mark Slice 12 done**

In `SVE_FROBBY_CAPABILITY_TODO.md`, change Slice 12:

```markdown
- [x] Done: Slice 12, buffs, swimming, and timed player state.
  - Design spec: `docs/superpowers/specs/2026-05-12-sve-slice-12-player-effects-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-12-sve-slice-12-player-effects.md`.
  - SVE pressure: custom hot spring and swimming areas that apply timed buffs based on save/day state.
  - Frobby goal: inspect active player buffs/effects, swimming or bathing state, and wait for timed state changes.
  - Done: `state.player` transient-state fields and active buff summaries, `player.set_transient_state`, effect-aware `wait.player`, and SVE scenario 17 (`sve_player_effects_swim_buff`) verified headlessly.
```

- [ ] **Step 2: Run full Frobby verification**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj
dotnet test tests/Harness.Tests/Harness.Tests.csproj
dotnet test tests/Runner.Tests/Runner.Tests.csproj
git diff --check
```

Expected: all tests pass with only existing skips; `git diff --check` prints nothing.

- [ ] **Step 3: Commit completion marker**

```bash
git add SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "docs: mark player effects slice complete"
```

- [ ] **Step 4: Report branch status**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-12-player-effects status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
```

Expected:

- Frobby branch `feature/sve-slice-12-player-effects` clean.
- SVE branch `feature/frobby-sve-slice-12-player-effects` clean.

Stop before merging SVE. Frobby can be merged to `main` only after the user approves or asks for merge.

---

## Self-Review

- Spec coverage: transient state, active buffs, setup action, wait support, docs/schema, and SVE proof are all covered by tasks.
- Placeholder scan: no TBD/TODO/fill-in steps remain. The only fallback is a specific debug branch for selecting `Custom_GrandpasGrove` if `Custom_SpriteSpring2` is not loaded.
- Type consistency: DTO names are `PlayerBuffSummary`, `PlayerBuffEffects`, `SetTransientStateRequest`, and `SetTransientStateResult`; handler names use `PlayerSetTransientStateHandler` and method `player.set_transient_state`; runner args use snake-case JSON through `ProtocolJson.Options`.
