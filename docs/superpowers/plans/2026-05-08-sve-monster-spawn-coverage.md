# SVE Monster Spawn Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add neutral Frobby monster metadata/filtering and prove it with a deterministic SVE Farm Type Manager monster-spawn scenario.

**Architecture:** Extend the existing `state.location.monsters` projection with additive runtime sprite metadata. Extend runner-side `wait.location_content` filters so JSON scenarios can assert monster HP, max HP, damage, and sprite texture without parsing mod pack data. Add one SVE scenario on the SVE feature branch that validates a Crimson Badlands corrupt mummy spawned with the expected custom configuration.

**Tech Stack:** C#/.NET 10 runner, .NET 6 SMAPI harness, System.Text.Json, xUnit, JSON runner scenarios, SVE repo-local Frobby scaffold.

---

## File Structure

Frobby worktree: `/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-monster-spawn-coverage`

- Modify: `src/Protocol/Models/LocationState.cs`
  Add `MonsterSummary.SpriteTexture`.
- Modify: `tests/Protocol.Tests/LocationStateSerializationTests.cs`
  Prove `sprite_texture` serializes in `state.location`.
- Modify: `src/Harness/Handlers/LocationContentProjector.cs`
  Read monster sprite texture defensively from direct monster fields or nested sprite fields.
- Modify: `tests/Harness.Tests/LocationContentProjectorTests.cs`
  Prove projected monsters expose normalized sprite texture paths.
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`
  Add exact numeric/string filters to `wait.location_content`.
- Modify: `tests/Runner.Tests/ScenarioRunnerTests.cs`
  Prove the new filters affect matching and timeout diagnostics.
- Modify: `docs/rpc-schema.md`, `docs/dsl-quickstart.md`, `README.md`
  Document neutral monster metadata and filters.
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`
  Mark the Slice 5 monster follow-up active, then done after verification.

SVE repo: `/home/fintan/stardewRepos/StardewValleyExpanded`

- Create: `tests/sdv/10-sve-ftm-monster-spawn-config.test.json`
  Live SVE proof scenario.
- Modify: `docs/FROBBY.md`
  Document scenario 10 and its focused run command.

Do not merge the SVE feature branch into `master`.

---

### Task 1: Protocol Monster Sprite Texture Field

**Files:**
- Modify: `tests/Protocol.Tests/LocationStateSerializationTests.cs`
- Modify: `src/Protocol/Models/LocationState.cs`

- [ ] **Step 1: Write the failing protocol serialization test**

In `tests/Protocol.Tests/LocationStateSerializationTests.cs`, update the monster fixture inside `Serialize_SnakeCaseFields`:

```csharp
            Monsters = new()
            {
                new MonsterSummary
                {
                    Tile = new TilePoint { X = 44, Y = 31 },
                    Name = "Mummy",
                    Type = "Mummy",
                    Health = 2000,
                    MaxHealth = 2000,
                    Damage = 100,
                    SpriteTexture = "Characters/Monsters/CorruptMummy",
                },
            },
```

Replace the existing monster assertion with:

```csharp
        Assert.Contains("\"monsters\":[{\"tile\":{\"x\":44,\"y\":31},\"name\":\"Mummy\",\"type\":\"Mummy\",\"health\":2000,\"max_health\":2000,\"damage\":100,\"sprite_texture\":\"Characters/Monsters/CorruptMummy\"}]", json);
```

- [ ] **Step 2: Run the focused test and confirm it fails**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter "FullyQualifiedName~LocationStateSerializationTests"
```

Expected: FAIL at compile time because `MonsterSummary` has no `SpriteTexture` property.

- [ ] **Step 3: Add the protocol field**

In `src/Protocol/Models/LocationState.cs`, update `MonsterSummary`:

```csharp
/// <summary>Hostile creature descriptor for a location snapshot. <see cref="Type"/> is the CLR type name.</summary>
public sealed class MonsterSummary
{
    public TilePoint Tile { get; set; } = new();
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int? Health { get; set; }
    public int? MaxHealth { get; set; }
    public int? Damage { get; set; }

    /// <summary>Runtime sprite texture asset path when Stardew or the mod exposes one.</summary>
    public string? SpriteTexture { get; set; }
}
```

- [ ] **Step 4: Run the focused test and confirm it passes**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter "FullyQualifiedName~LocationStateSerializationTests"
```

Expected: PASS.

- [ ] **Step 5: Commit protocol change**

Run:

```bash
git add src/Protocol/Models/LocationState.cs tests/Protocol.Tests/LocationStateSerializationTests.cs
git commit -m "feat: expose monster sprite texture"
```

---

### Task 2: Harness Monster Sprite Projection

**Files:**
- Modify: `tests/Harness.Tests/LocationContentProjectorTests.cs`
- Modify: `src/Harness/Handlers/LocationContentProjector.cs`

- [ ] **Step 1: Write the failing harness projector test**

In `tests/Harness.Tests/LocationContentProjectorTests.cs`, update `ProjectMonster_ReadsRuntimeMonsterFields`:

```csharp
    [Fact]
    public void ProjectMonster_ReadsRuntimeMonsterFields()
    {
        var monster = new GreenSlime
        {
            tile = new Vector2(44, 31),
            Name = "Mummy",
            Health = 2000,
            MaxHealth = 2000,
            DamageToFarmer = 100,
            Sprite = new FakeAnimatedSprite { textureName = "Characters\\Monsters\\CorruptMummy" },
        };

        var summary = LocationContentProjector.ProjectMonsterForTests(monster);

        Assert.Equal(44, summary.Tile.X);
        Assert.Equal(31, summary.Tile.Y);
        Assert.Equal("Mummy", summary.Name);
        Assert.Equal("GreenSlime", summary.Type);
        Assert.Equal(2000, summary.Health);
        Assert.Equal(2000, summary.MaxHealth);
        Assert.Equal(100, summary.Damage);
        Assert.Equal("Characters/Monsters/CorruptMummy", summary.SpriteTexture);
    }
```

Add this fake sprite type near the existing `GreenSlime` fake:

```csharp
    private sealed class FakeAnimatedSprite
    {
        public string textureName = string.Empty;
    }
```

Update the existing fake `GreenSlime` type:

```csharp
    private sealed class GreenSlime
    {
        public Vector2 tile;
        public string Name = string.Empty;
        public int Health;
        public int MaxHealth;
        public int DamageToFarmer;
        public FakeAnimatedSprite? Sprite;
    }
```

- [ ] **Step 2: Run the focused test and confirm it fails**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~LocationContentProjectorTests"
```

Expected: FAIL because `SpriteTexture` is null.

- [ ] **Step 3: Implement sprite texture projection**

In `src/Harness/Handlers/LocationContentProjector.cs`, update `ProjectMonster`:

```csharp
    private static MonsterSummary ProjectMonster(object monster)
    {
        var tile = ReadTilePoint(monster);
        return new MonsterSummary
        {
            Tile = tile,
            Name = ReadString(monster, "Name", "name", "DisplayName", "displayName") ?? monster.GetType().Name,
            Type = monster.GetType().Name,
            Health = ReadInt(monster, "Health", "health"),
            MaxHealth = ReadInt(monster, "MaxHealth", "maxHealth"),
            Damage = ReadInt(monster, "DamageToFarmer", "damageToFarmer", "damage"),
            SpriteTexture = NormalizeAssetName(ReadSpriteTexture(monster)),
        };
    }
```

Add these helpers after `ResourceClumpName`:

```csharp
    private static string? ReadSpriteTexture(object monster)
    {
        var direct = ReadString(monster, "spriteTexture", "SpriteTexture", "textureName", "TextureName");
        if (!string.IsNullOrWhiteSpace(direct))
            return direct;

        var sprite = ReadMemberRaw(monster, "Sprite", "sprite", "AnimatedSprite", "animatedSprite");
        return sprite is null
            ? null
            : ReadString(sprite, "textureName", "TextureName");
    }

    private static string? NormalizeAssetName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Replace('\\', '/');
    }
```

- [ ] **Step 4: Run the focused test and confirm it passes**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~LocationContentProjectorTests"
```

Expected: PASS.

- [ ] **Step 5: Commit harness projection change**

Run:

```bash
git add src/Harness/Handlers/LocationContentProjector.cs tests/Harness.Tests/LocationContentProjectorTests.cs
git commit -m "feat: project monster sprite textures"
```

---

### Task 3: Runner Metadata Filters For Location Content

**Files:**
- Modify: `tests/Runner.Tests/ScenarioRunnerTests.cs`
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`

- [ ] **Step 1: Add a failing pass-case runner test**

In `tests/Runner.Tests/ScenarioRunnerTests.cs`, add this test after `WaitLocationContent_FiltersByTileAndMaxCount`:

```csharp
    [Fact]
    public async Task WaitLocationContent_FiltersByMonsterNumericAndSpriteFields()
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
                        "state.location" => JsonDocument.Parse("{\"name\":\"Custom_CrimsonBadlands\",\"resource_clumps\":[],\"objects\":[],\"monsters\":[{\"tile\":{\"x\":20,\"y\":144},\"name\":\"Mummy\",\"type\":\"Mummy\",\"health\":2000,\"max_health\":2000,\"damage\":100,\"sprite_texture\":\"Characters/Monsters/CorruptMummy\"},{\"tile\":{\"x\":21,\"y\":144},\"name\":\"Mummy\",\"type\":\"Mummy\",\"health\":240,\"max_health\":240,\"damage\":60,\"sprite_texture\":\"Characters/Monsters/Mummy\"}]}").RootElement,
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
            Name = "wait_location_content_monster_metadata",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.location_content",
                    Args = JsonDocument.Parse("{\"location\":\"Custom_CrimsonBadlands\",\"collection\":\"monsters\",\"name\":\"Mummy\",\"type\":\"Mummy\",\"health\":2000,\"max_health\":2000,\"damage\":100,\"sprite_texture\":\"Characters/Monsters/CorruptMummy\",\"min_count\":1,\"max_count\":1,\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.True(report.Passed);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }
```

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~WaitLocationContent_FiltersByMonsterNumericAndSpriteFields"
```

Expected: FAIL because the filters are ignored, both mummies match by name/type, and `max_count: 1` fails.

- [ ] **Step 2: Add a failing timeout-diagnostics test**

Add this test after the pass-case test:

```csharp
    [Fact]
    public async Task WaitLocationContent_TimeoutIncludesMonsterMetadataFilters()
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
                        "state.location" => JsonDocument.Parse("{\"name\":\"Custom_CrimsonBadlands\",\"resource_clumps\":[],\"objects\":[],\"monsters\":[{\"tile\":{\"x\":21,\"y\":144},\"name\":\"Mummy\",\"type\":\"Mummy\",\"health\":240,\"max_health\":240,\"damage\":60,\"sprite_texture\":\"Characters/Monsters/Mummy\"}]}").RootElement,
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
            Name = "wait_location_content_monster_timeout",
            Steps = new()
            {
                new ScenarioStep
                {
                    Action = "wait.location_content",
                    Args = JsonDocument.Parse("{\"location\":\"Custom_CrimsonBadlands\",\"collection\":\"monsters\",\"name\":\"Mummy\",\"type\":\"Mummy\",\"health\":2000,\"max_health\":2000,\"damage\":100,\"sprite_texture\":\"Characters/Monsters/CorruptMummy\",\"min_count\":1,\"timeout_ms\":20,\"poll_ms\":1}").RootElement,
                },
            },
        }, cts.Token);

        Assert.False(report.Passed);
        var failure = Assert.Single(report.Failures);
        Assert.Contains("matching name=Mummy, type=Mummy, health=2000, max_health=2000, damage=100, sprite_texture=Characters/Monsters/CorruptMummy", failure);
        Assert.Contains("last observed 0 matched out of 1 monsters", failure);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }
```

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~WaitLocationContent_TimeoutIncludesMonsterMetadataFilters"
```

Expected: FAIL because the timeout text does not include the new filters.

- [ ] **Step 3: Add filter fields and matcher logic**

In `src/Runner/Scenarios/ScenarioRunner.cs`, update `LocationContentElementMatches`:

```csharp
    private static bool LocationContentElementMatches(JsonElement element, WaitLocationContentStepArgs args)
    {
        return StringFilterMatches(element, "name", args.Name)
            && StringFilterMatches(element, "type", args.Type)
            && StringFilterMatches(element, "kind", args.Kind)
            && StringFilterMatches(element, "id", args.Id)
            && StringFilterMatches(element, "qualified_id", args.QualifiedId)
            && NumberFilterMatches(element, "health", args.Health)
            && NumberFilterMatches(element, "max_health", args.MaxHealth)
            && NumberFilterMatches(element, "damage", args.Damage)
            && StringFilterMatches(element, "sprite_texture", args.SpriteTexture)
            && TileFilterMatches(element, args.X, args.Y);
    }
```

Add this helper after `StringFilterMatches`:

```csharp
    private static bool NumberFilterMatches(JsonElement element, string property, int? expected)
    {
        if (expected is null)
            return true;

        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var actual)
            && actual == expected.Value;
    }
```

Update `FormatLocationContentFilters`:

```csharp
    private static string FormatLocationContentFilters(WaitLocationContentStepArgs args)
    {
        var filters = new List<string>();
        if (args.Name is not null) filters.Add($"name={args.Name}");
        if (args.Type is not null) filters.Add($"type={args.Type}");
        if (args.Kind is not null) filters.Add($"kind={args.Kind}");
        if (args.Id is not null) filters.Add($"id={args.Id}");
        if (args.QualifiedId is not null) filters.Add($"qualified_id={args.QualifiedId}");
        if (args.Health is not null) filters.Add($"health={args.Health.Value}");
        if (args.MaxHealth is not null) filters.Add($"max_health={args.MaxHealth.Value}");
        if (args.Damage is not null) filters.Add($"damage={args.Damage.Value}");
        if (args.SpriteTexture is not null) filters.Add($"sprite_texture={args.SpriteTexture}");
        if (args.X is not null && args.Y is not null) filters.Add($"tile={args.X},{args.Y}");
        return filters.Count == 0 ? string.Empty : $" matching {string.Join(", ", filters)}";
    }
```

Update `WaitLocationContentStepArgs`:

```csharp
    private sealed class WaitLocationContentStepArgs
    {
        public string? Location { get; set; }
        public string? Collection { get; set; }
        public string? Name { get; set; }
        public string? Type { get; set; }
        public string? Kind { get; set; }
        public string? Id { get; set; }
        public string? QualifiedId { get; set; }
        public int? Health { get; set; }
        public int? MaxHealth { get; set; }
        public int? Damage { get; set; }
        public string? SpriteTexture { get; set; }
        public int? X { get; set; }
        public int? Y { get; set; }
        public int MinCount { get; set; } = 1;
        public int? MaxCount { get; set; }
        public int TimeoutMs { get; set; } = 10000;
        public int PollMs { get; set; } = 100;
    }
```

- [ ] **Step 4: Run focused runner tests**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~WaitLocationContent"
```

Expected: PASS.

- [ ] **Step 5: Commit runner filter change**

Run:

```bash
git add src/Runner/Scenarios/ScenarioRunner.cs tests/Runner.Tests/ScenarioRunnerTests.cs
git commit -m "feat: filter location content by monster metadata"
```

---

### Task 4: Frobby Documentation And Slice Status

**Files:**
- Modify: `docs/rpc-schema.md`
- Modify: `docs/dsl-quickstart.md`
- Modify: `README.md`
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Update `state.location` docs**

In `docs/rpc-schema.md`, update the monster example in the `state.location` response:

```json
      "monsters": [{ "tile": { "x": 44, "y": 31 }, "name": "Mummy", "type": "Mummy", "health": 2000, "max_health": 2000, "damage": 100, "sprite_texture": "Characters/Monsters/CorruptMummy" }],
```

Update the paragraph below the response to:

```markdown
`monsters` contains hostile creatures and is separate from `npcs`, which remains
for social/non-hostile NPCs. Monster summaries include runtime health, max
health, damage, and `sprite_texture` when Stardew or the mod exposes those
values. Optional object and monster metadata fields may be empty or null when
the runtime type does not expose them.
```

- [ ] **Step 2: Update runner convenience docs**

In `docs/rpc-schema.md`, update the `wait.location_content` bullet:

```markdown
- `{ "action": "wait.location_content", "args": { "location": "ExampleForestEdge", "collection": "resource_clumps", "name": "Log", "min_count": 2 } }` is runner-only. It polls `state.location` for the named location until the selected collection has enough matching entries. Supported collections are `objects`, `resource_clumps`, `monsters`, and `critters`. Filters are exact-match and optional: `name`, `type`, `kind`, `id`, `qualified_id`, `health`, `max_health`, `damage`, `sprite_texture`, and `x`/`y` tile. It accepts `min_count`, optional `max_count`, `timeout_ms`, and `poll_ms`, and reports the last matched/total counts on timeout.
```

- [ ] **Step 3: Add a generic monster example to the quickstart**

In `docs/dsl-quickstart.md`, after the existing resource clump `wait.location_content` example, add:

````markdown
The same runner wait can target hostile monsters with exact metadata filters:

```json
{
  "action": "wait.location_content",
  "args": {
    "location": "ExampleCombatMap",
    "collection": "monsters",
    "name": "Mummy",
    "type": "Mummy",
    "health": 2000,
    "max_health": 2000,
    "damage": 100,
    "sprite_texture": "Characters/Monsters/CorruptMummy",
    "min_count": 1,
    "timeout_ms": 10000,
    "poll_ms": 100
  }
}
```
````

- [ ] **Step 4: Update README neutral capability summary**

In `README.md`, update the spawned-world-content bullet:

```markdown
- Use `state.location.resource_clumps`, `state.location.monsters`, and
  runner-side `wait.location_content` when testing spawned world content such as
  logs, boulders, forage-like objects, ore, or monsters. Monster summaries can
  expose runtime HP, max HP, damage, and sprite texture, and the wait helper can
  filter on those fields. These helpers observe runtime Stardew state and stay
  independent from specific spawn frameworks.
```

- [ ] **Step 5: Mark the SVE follow-up active**

In `SVE_FROBBY_CAPABILITY_TODO.md`, replace:

```markdown
  - Pending Slice 5 follow-up: add an SVE monster-spawn scenario once a deterministic spawned-monster location/date is selected.
```

with:

```markdown
  - Active Slice 5 follow-up: add deterministic SVE monster-spawn coverage using neutral monster metadata (`sprite_texture`, HP, max HP, damage) and `wait.location_content` filters.
```

- [ ] **Step 6: Run Frobby documentation-adjacent focused tests**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter "FullyQualifiedName~LocationStateSerializationTests"
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~LocationContentProjectorTests"
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~WaitLocationContent"
```

Expected: PASS for all three commands.

- [ ] **Step 7: Commit Frobby docs and active status**

Run:

```bash
git add README.md docs/rpc-schema.md docs/dsl-quickstart.md SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "docs: document monster spawn metadata filters"
```

---

### Task 5: SVE Monster Spawn Scenario

**Files:**
- Create: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/10-sve-ftm-monster-spawn-config.test.json`
- Modify: `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`

- [ ] **Step 1: Add the SVE scenario**

Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/10-sve-ftm-monster-spawn-config.test.json`:

```json
{
  "name": "sve_ftm_monster_spawn_config",
  "fixture": "m0spike_436515781",
  "config": { "seed": 436515781 },
  "steps": [
    {
      "action": "time.set",
      "args": { "time": 600, "day": 1, "season": "spring", "year": 1 }
    },
    {
      "action": "world.set_weather",
      "args": { "type": "sun" }
    },
    {
      "action": "time.next_day",
      "args": { "settle_timeout_ms": 15000, "poll_ms": 100 }
    },
    {
      "action": "player.warp",
      "args": { "location": "Custom_CrimsonBadlands", "x": 20, "y": 146 }
    },
    {
      "action": "wait.location",
      "args": {
        "location": "Custom_CrimsonBadlands",
        "x": 20,
        "y": 146,
        "timeout_ms": 10000,
        "poll_ms": 100
      }
    },
    {
      "action": "wait.location_content",
      "args": {
        "location": "Custom_CrimsonBadlands",
        "collection": "monsters",
        "name": "Mummy",
        "type": "Mummy",
        "health": 2000,
        "max_health": 2000,
        "damage": 100,
        "sprite_texture": "Characters/Monsters/CorruptMummy",
        "min_count": 1,
        "timeout_ms": 15000,
        "poll_ms": 100
      }
    },
    {
      "action": "freeze.begin",
      "args": { "settle_timeout_ms": 10000, "poll_ms": 100 }
    },
    {
      "action": "screenshot.capture",
      "args": { "name": "final" }
    }
  ],
  "assertions": []
}
```

- [ ] **Step 2: Document the SVE scenario**

In `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`, add this after the scenario 09 paragraph:

```markdown
Scenario `tests/sdv/10-sve-ftm-monster-spawn-config.test.json` covers a
deterministic Farm Type Manager monster spawn. It advances to a fresh day,
warps to the Crimson Badlands, and uses Frobby's neutral `wait.location_content`
monster metadata filters to assert a corrupt mummy spawned with the expected
HP, max HP, damage, and sprite texture. The scenario proves runtime state only;
Frobby does not parse the SVE FTM content pack.
```

Replace the final focused command block with:

```sh
FROBBY_ROOT=/path/to/sdv-test-framework scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-7-visual-effects tests/sdv/09-sve-visual-effects.test.json
FROBBY_ROOT=/path/to/sdv-test-framework scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-5-monster-spawn tests/sdv/10-sve-ftm-monster-spawn-config.test.json
```

- [ ] **Step 3: Dry-run the SVE scenario through the repo wrapper**

Run from `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-monster-spawn-coverage scripts/sdv-test --dry-run --headless --mod-set core tests/sdv/10-sve-ftm-monster-spawn-config.test.json
```

Expected: PASS dry-run output that resolves the SVE scenario path and includes `--headless`.

- [ ] **Step 4: Commit SVE scenario work on the current SVE feature branch**

Run:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded add tests/sdv/10-sve-ftm-monster-spawn-config.test.json docs/FROBBY.md
git -C /home/fintan/stardewRepos/StardewValleyExpanded commit -m "test: add SVE monster spawn coverage"
```

---

### Task 6: Live Verification And Completion Status

**Files:**
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Run the complete Frobby suite**

Run from the Frobby worktree:

```bash
dotnet test sdv-test-framework.slnx
```

Expected: PASS. Baseline before this slice was 868 passed, 58 skipped.

- [ ] **Step 2: Run the new SVE scenario headlessly**

Run from `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
env FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-monster-spawn-coverage SDV_TEST_MOD_CACHE=/home/fintan/stardewRepos/frobby/sdv-test-framework/.cache/deps scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-5-monster-spawn tests/sdv/10-sve-ftm-monster-spawn-config.test.json
```

Expected: PASS. The report should include a final screenshot and the scenario should pass because `wait.location_content` observed a `Mummy` in `Custom_CrimsonBadlands` with `health=2000`, `max_health=2000`, `damage=100`, and `sprite_texture=Characters/Monsters/CorruptMummy`.

- [ ] **Step 3: Run the SVE scenario suite headlessly**

Run from `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
env FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-monster-spawn-coverage SDV_TEST_MOD_CACHE=/home/fintan/stardewRepos/frobby/sdv-test-framework/.cache/deps scripts/sdv-test --headless --mod-set core --no-build --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/monster-spawn-suite tests/sdv
```

Expected: PASS for all SVE scenarios in `tests/sdv`.

- [ ] **Step 4: Mark the Frobby capability item done**

In `SVE_FROBBY_CAPABILITY_TODO.md`, replace:

```markdown
  - Active Slice 5 follow-up: add deterministic SVE monster-spawn coverage using neutral monster metadata (`sprite_texture`, HP, max HP, damage) and `wait.location_content` filters.
```

with:

```markdown
  - Done Slice 5 follow-up: deterministic SVE monster-spawn coverage validates a Crimson Badlands corrupt mummy through neutral monster metadata (`sprite_texture`, HP, max HP, damage) and `wait.location_content` filters.
```

- [ ] **Step 5: Commit Frobby completion status**

Run:

```bash
git add SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "docs: complete SVE monster spawn follow-up"
```

- [ ] **Step 6: Final status checks**

Run:

```bash
git status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
```

Expected:

- Frobby branch `feature/sve-monster-spawn-coverage` is clean.
- SVE branch remains `feature/frobby-sve-slice-1-tile-action-warp` or another non-`master` feature branch, clean after its scenario commit.

---

## Debugging Guidance

If the live SVE scenario fails because `sprite_texture` is null, inspect `state.location` for `Custom_CrimsonBadlands` with the same scenario state. Add only neutral projector fallbacks that read runtime sprite/animated-sprite properties. Do not parse SVE or FTM JSON in Frobby.

If `max_health` differs from `health`, verify the observed runtime monster state. Keep `health`, `damage`, and `sprite_texture` as the minimum config proof. Only remove `max_health` from the SVE scenario if the runtime object genuinely does not expose the configured max HP.

If the monster moves before observation, keep the scenario free of exact tile filters. The approved proof is the custom config tuple in `Custom_CrimsonBadlands`, not exact tile stability.

## Self-Review

Spec coverage:

- Additive monster field: Task 1 and Task 2.
- Runner filters: Task 3.
- Neutral docs: Task 4.
- SVE proof scenario: Task 5.
- Verification and completion status: Task 6.
- Non-goal protection against FTM parsing and SVE-specific Frobby code: Debugging Guidance and file structure.

No action-specific schema task is included because `schemas/scenario.schema.json` allows object-shaped `args` for all runner actions and does not enumerate `wait.location_content` fields.

Type consistency:

- Protocol field name: `SpriteTexture`.
- JSON field name: `sprite_texture`.
- Runner argument name: `SpriteTexture`, deserialized from `sprite_texture`.
- Scenario filter key: `sprite_texture`.
- SVE expected path: `Characters/Monsters/CorruptMummy`.
