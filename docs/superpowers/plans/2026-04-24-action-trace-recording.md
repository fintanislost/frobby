# Action-Trace Recording — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **No git repo.** Task completion gate is **`./scripts/ci.sh` green**. T4's extra gates:
> - Manual smoke: walk in-game, type `harness_record_actions smoke_walk`, walk more, `harness_record_stop` → trace file at `~/.cache/sdv-test-framework/records/actions/smoke_walk.test.json` contains warp + time-advance steps.
> - Trace file replays cleanly via `dotnet run --project src/Runner -- run <path>` — 0 failures.
> - `./scripts/run-samples.sh` still 11/11 PASS.

**Goal:** Ship the third record-mode flow — capture human input during play, translate to a replayable `.test.json` scenario via SMAPI's high-level events. Pairs with MCP `run_scenario` for round-trip authoring (play → trace → edit → run).

**Architecture:** Coarse-event translation. Hook `Player.Warped` + `Display.MenuChanged` + `GameLoop.TimeChanged`. Buffer `RecordedAction` events. On stop, run a pure translator function (`RecordedAction[] → ScenarioStep[]`) with rules: multi-warp coalesce, NPC-interaction inference from menu+location+nearby-NPCs, time-advance debounce (≥10min threshold). SMAPI console commands `harness_record_actions <name>` + `harness_record_stop` drive the recorder.

**Tech Stack:**
- .NET 6 (Harness target).
- SMAPI events (`IPlayerEvents`, `IDisplayEvents`, `IGameLoopEvents`) — already imported.
- `System.Text.Json` for output (matches existing `harness_record` pattern).
- Reuses M2-record's `IFileSink` + `FileSink` for testable writes.

**Design spec:** `docs/superpowers/specs/2026-04-24-action-trace-recording-design.md`

---

## File structure

**New Harness files:**
- `src/Harness/Recording/RecordedAction.cs` — `internal` record struct + `ActionKind` enum.
- `src/Harness/Recording/ActionTraceTranslator.cs` — `internal static` pure-function translator. `Translate(IReadOnlyList<RecordedAction>) → IReadOnlyList<ScenarioStep>`.
- `src/Harness/Recording/ActionTraceRecorder.cs` — recorder lifecycle, event subscriptions, buffer.

**New tests:**
- `tests/Harness.Tests/ActionTraceTranslatorTests.cs` — 7 unit tests (translation rules).
- `tests/Harness.Tests/ActionTraceRecorderTests.cs` — 3 unit tests (lifecycle).
- `tests/Harness.Tests/ActionTraceIntegrationTests.cs` — 1 skipped placeholder.

**Modified files:**
- `src/Harness/ModEntry.cs` — register `harness_record_actions` + `harness_record_stop` commands; wire to a singleton `ActionTraceRecorder.Current`.

**Starting test count:** 337 Passed + 43 Skipped.
**Target:** ~349 Passed + 44 Skipped (+12 passing, +1 skipped).

---

## Task 1: Translator (pure-function core)

**Why:** The hardest part of the design — the heuristics for multi-warp coalesce, NPC-interaction inference, time-advance debounce. Pure function, fully unit-testable without SMAPI/SDV. Land first so the rest of the recorder is plumbing.

**Files:**
- Create: `src/Harness/Recording/RecordedAction.cs`
- Create: `src/Harness/Recording/ActionTraceTranslator.cs`
- Create: `tests/Harness.Tests/ActionTraceTranslatorTests.cs`

### Step 1: Write failing tests (red phase)

Create `tests/Harness.Tests/ActionTraceTranslatorTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class ActionTraceTranslatorTests
{
    private static DateTime T0 = new(2026, 4, 24, 12, 0, 0, DateTimeKind.Utc);

    private static RecordedAction Warp(int seconds, string loc, int x, int y) =>
        new(T0.AddSeconds(seconds), ActionKind.Warp, Location: loc, X: x, Y: y);
    private static RecordedAction Npc(int seconds, string name) =>
        new(T0.AddSeconds(seconds), ActionKind.NpcInteract, NpcName: name);
    private static RecordedAction Time(int seconds, int minutes) =>
        new(T0.AddSeconds(seconds), ActionKind.TimeAdvance, MinutesElapsed: minutes);

    [Fact]
    public void EmptyBuffer_ReturnsEmptyList()
    {
        var steps = ActionTraceTranslator.Translate(Array.Empty<RecordedAction>());
        Assert.Empty(steps);
    }

    [Fact]
    public void OnlyWarp_EmitsWarpStep()
    {
        var steps = ActionTraceTranslator.Translate(new[] { Warp(0, "Farm", 64, 15) });
        Assert.Single(steps);
        Assert.Equal("player.warp", steps[0].Action);
    }

    [Fact]
    public void WarpThenNpcInteract_EmitsBothSteps()
    {
        var steps = ActionTraceTranslator.Translate(new[]
        {
            Warp(0, "SeedShop", 4, 19),
            Npc(2, "Pierre"),
        });
        Assert.Equal(2, steps.Count);
        Assert.Equal("player.warp", steps[0].Action);
        Assert.Equal("world.interact_npc", steps[1].Action);
    }

    [Fact]
    public void MultipleWarpsWithinOneSecond_CoalescesToLatest()
    {
        // Three warps, each 200ms apart — should produce ONE warp step (the last).
        var steps = ActionTraceTranslator.Translate(new[]
        {
            new RecordedAction(T0,                       ActionKind.Warp, Location: "Farm", X: 60, Y: 15),
            new RecordedAction(T0.AddMilliseconds(200),  ActionKind.Warp, Location: "Farm", X: 62, Y: 15),
            new RecordedAction(T0.AddMilliseconds(400),  ActionKind.Warp, Location: "Farm", X: 64, Y: 15),
        });
        Assert.Single(steps);
        // Verify it's the latest (X=64).
        Assert.Contains("\"x\":64", System.Text.Json.JsonSerializer.Serialize(steps[0].Args));
    }

    [Fact]
    public void LongIdleBeforeWarp_EmitsTimeAdvance()
    {
        // 30 minutes accumulated time, then a warp. Expect: time.advance(30) THEN warp.
        var steps = ActionTraceTranslator.Translate(new[]
        {
            Time(0, 30),
            Warp(60, "Farm", 64, 15),
        });
        Assert.Equal(2, steps.Count);
        Assert.Equal("time.advance", steps[0].Action);
        Assert.Equal("player.warp", steps[1].Action);
    }

    [Fact]
    public void TimeAdvanceBelowThreshold_NotEmitted()
    {
        // 5 minutes accumulated — below the 10-min threshold. Should be dropped.
        var steps = ActionTraceTranslator.Translate(new[]
        {
            Time(0, 5),
            Warp(60, "Farm", 64, 15),
        });
        Assert.Single(steps);
        Assert.Equal("player.warp", steps[0].Action);
    }

    [Fact]
    public void EndOfBufferFlushesPendingTime()
    {
        // 30 minutes accumulated, no other events. Expect: time.advance(30) at flush.
        var steps = ActionTraceTranslator.Translate(new[] { Time(0, 30) });
        Assert.Single(steps);
        Assert.Equal("time.advance", steps[0].Action);
    }
}
```

Run: `cd /home/fintan/stardewRepos/frobby/sdv-test-framework && dotnet test tests/Harness.Tests/ --filter ActionTraceTranslator 2>&1 | tail -5`
Expected: compile failure — types don't exist.

### Step 2: RecordedAction.cs

Create `src/Harness/Recording/RecordedAction.cs`:

```csharp
using System;

namespace SdvTestFramework.Harness.Recording;

/// <summary>What kind of action occurred.</summary>
internal enum ActionKind { Warp, NpcInteract, TimeAdvance }

/// <summary>
/// Buffered action event captured by <see cref="ActionTraceRecorder"/>. Translated to
/// scenario steps by <see cref="ActionTraceTranslator"/>.
/// </summary>
internal sealed record RecordedAction(
    DateTime At,
    ActionKind Kind,
    string? Location = null,
    int? X = null,
    int? Y = null,
    string? NpcName = null,
    int? MinutesElapsed = null);
```

### Step 3: ActionTraceTranslator.cs

Create `src/Harness/Recording/ActionTraceTranslator.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Recording;

/// <summary>
/// Pure-function translator from <see cref="RecordedAction"/> buffer to a
/// <see cref="ScenarioStep"/> sequence. Heuristics:
/// - Multi-warp coalesce: warps within 1 second of the previous warp replace it.
/// - Time-advance debounce: pending minutes only emitted at ≥10 threshold.
/// - End-of-buffer flush: emit any pending time-advance at stop.
/// </summary>
internal static class ActionTraceTranslator
{
    private static readonly TimeSpan WarpCoalesceWindow = TimeSpan.FromSeconds(1);
    private const int TimeAdvanceThresholdMinutes = 10;

    public static IReadOnlyList<ScenarioStep> Translate(IReadOnlyList<RecordedAction> buffer)
    {
        var steps = new List<ScenarioStep>();
        int pendingMinutes = 0;
        DateTime? lastWarpAt = null;

        foreach (var a in buffer)
        {
            switch (a.Kind)
            {
                case ActionKind.Warp:
                    FlushPendingTime();
                    if (lastWarpAt is { } prev && (a.At - prev) < WarpCoalesceWindow && steps.Count > 0)
                    {
                        // Coalesce: replace the previous warp's args.
                        steps[^1] = MakeWarpStep(a.Location!, a.X!.Value, a.Y!.Value);
                    }
                    else
                    {
                        steps.Add(MakeWarpStep(a.Location!, a.X!.Value, a.Y!.Value));
                    }
                    lastWarpAt = a.At;
                    break;

                case ActionKind.NpcInteract:
                    FlushPendingTime();
                    steps.Add(MakeNpcStep(a.NpcName!));
                    lastWarpAt = null; // reset coalesce window
                    break;

                case ActionKind.TimeAdvance:
                    pendingMinutes += a.MinutesElapsed ?? 0;
                    break;
            }
        }
        FlushPendingTime();
        return steps;

        void FlushPendingTime()
        {
            if (pendingMinutes >= TimeAdvanceThresholdMinutes)
            {
                steps.Add(MakeTimeAdvanceStep(pendingMinutes));
                pendingMinutes = 0;
            }
            else
            {
                pendingMinutes = 0; // drop sub-threshold accumulation; don't carry across.
            }
        }
    }

    private static ScenarioStep MakeWarpStep(string location, int x, int y) =>
        new()
        {
            Action = "player.warp",
            Args = JsonSerializer.SerializeToElement(new { location, x, y }, ProtocolJson.Options),
        };

    private static ScenarioStep MakeNpcStep(string name) =>
        new()
        {
            Action = "world.interact_npc",
            Args = JsonSerializer.SerializeToElement(new { name }, ProtocolJson.Options),
        };

    private static ScenarioStep MakeTimeAdvanceStep(int minutes) =>
        new()
        {
            Action = "time.advance",
            Args = JsonSerializer.SerializeToElement(new { minutes }, ProtocolJson.Options),
        };
}
```

### Step 4: Verify CI

Run: `./scripts/ci.sh 2>&1 | grep "Passed:" | head -10`
Expected: +7 tests. Total **344 Passed + 43 Skipped**.

---

## Task 2: ActionTraceRecorder + console commands

**Why:** Wire the translator into a real SMAPI lifecycle. Subscribes to events, buffers, writes file on stop. Console commands provide the in-game UX.

**Files:**
- Create: `src/Harness/Recording/ActionTraceRecorder.cs`
- Create: `tests/Harness.Tests/ActionTraceRecorderTests.cs`
- Modify: `src/Harness/ModEntry.cs` — register console commands + wire singleton.

### Step 1: Failing tests for the recorder lifecycle

Create `tests/Harness.Tests/ActionTraceRecorderTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Protocol.Scenarios;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class ActionTraceRecorderTests
{
    private sealed class FakeFileSink : IFileSink
    {
        public List<(string Path, string Contents)> Writes { get; } = new();
        public void Write(string path, string contents) => Writes.Add((path, contents));
    }

    [Fact]
    public void Start_ThenStop_FlushesBuffer()
    {
        var sink = new FakeFileSink();
        var messages = new List<string>();
        var rec = new ActionTraceRecorder(sink, messages.Add);

        rec.Start("test_session", "/tmp/records-test");
        // Inject a synthetic warp via internal seam.
        rec.RecordForTests(new RecordedAction(
            DateTime.UtcNow, ActionKind.Warp, Location: "Farm", X: 64, Y: 15));
        rec.Stop();

        Assert.Single(sink.Writes);
        var (path, contents) = sink.Writes[0];
        Assert.Equal("/tmp/records-test/test_session.test.json", path);
        Assert.Contains("player.warp", contents);
        Assert.Contains("\"location\":\"Farm\"", contents);

        // Verify the emitted JSON is loadable by ScenarioLoader.
        var tmp = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.test.json");
        File.WriteAllText(tmp, contents);
        try { ScenarioLoader.Load(tmp); }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void DoubleStart_LogsWarning_KeepsFirstSession()
    {
        var sink = new FakeFileSink();
        var messages = new List<string>();
        var rec = new ActionTraceRecorder(sink, messages.Add);

        rec.Start("first", "/tmp/records-test");
        rec.Start("second", "/tmp/records-test");

        Assert.Contains(messages, m => m.Contains("already in progress"));
    }

    [Fact]
    public void StopBeforeStart_LogsWarning_NoFile()
    {
        var sink = new FakeFileSink();
        var messages = new List<string>();
        var rec = new ActionTraceRecorder(sink, messages.Add);

        rec.Stop();

        Assert.Empty(sink.Writes);
        Assert.Contains(messages, m => m.Contains("no active recording"));
    }
}
```

Run: expect compile failure — `ActionTraceRecorder` doesn't exist.

### Step 2: ActionTraceRecorder.cs

Create `src/Harness/Recording/ActionTraceRecorder.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SdvTestFramework.Harness.Recording;

/// <summary>
/// Records gameplay actions during a session for later replay. Subscribes to SMAPI
/// events when started, buffers <see cref="RecordedAction"/> events, translates +
/// writes a scenario JSON when stopped.
/// </summary>
/// <remarks>
/// Test-friendly: takes <see cref="IFileSink"/> + log delegate via constructor; SMAPI
/// event hookup happens via <c>StartWithSmapi(IModHelper)</c> so unit tests use the
/// plain <c>Start</c> + <c>RecordForTests</c> seams without needing live SDV.
/// </remarks>
public sealed class ActionTraceRecorder
{
    private readonly IFileSink _sink;
    private readonly Action<string> _log;
    private readonly object _lock = new();

    private string? _activeName;
    private string? _activeOutputDir;
    private List<RecordedAction>? _buffer;

    public ActionTraceRecorder(IFileSink sink, Action<string> log)
    {
        _sink = sink;
        _log = log;
    }

    /// <summary>True when a recording session is active.</summary>
    public bool IsRecording { get { lock (_lock) return _buffer is not null; } }

    /// <summary>Start a session. Logs + no-ops if one is already active.</summary>
    public void Start(string name, string outputDir)
    {
        lock (_lock)
        {
            if (_buffer is not null)
            {
                _log($"[harness_record_actions] session '{_activeName}' already in progress; type harness_record_stop first");
                return;
            }
            _activeName = name;
            _activeOutputDir = outputDir;
            _buffer = new List<RecordedAction>();
            var path = Path.Combine(outputDir, $"{name}.test.json");
            _log($"[harness_record_actions] recording session '{name}' — type harness_record_stop to finalize. Output: {path}");
        }
    }

    /// <summary>Record an action. Wired by SMAPI event handlers in production.</summary>
    public void Record(RecordedAction action)
    {
        lock (_lock)
        {
            _buffer?.Add(action);
        }
    }

    /// <summary>Internal-visible test seam — same as <see cref="Record"/>, named for clarity.</summary>
    internal void RecordForTests(RecordedAction action) => Record(action);

    /// <summary>Stop, translate, write. Logs + no-ops if no session active.</summary>
    public void Stop()
    {
        List<RecordedAction>? buffer;
        string? name;
        string? outputDir;
        lock (_lock)
        {
            if (_buffer is null)
            {
                _log("[harness_record_stop] no active recording session");
                return;
            }
            buffer = _buffer;
            name = _activeName;
            outputDir = _activeOutputDir;
            _buffer = null;
            _activeName = null;
            _activeOutputDir = null;
        }

        var steps = ActionTraceTranslator.Translate(buffer);
        var stepsArray = new JsonArray();
        foreach (var s in steps)
        {
            stepsArray.Add(new JsonObject
            {
                ["action"] = s.Action,
                ["args"] = s.Args is { } args ? JsonNode.Parse(args.GetRawText()) : new JsonObject(),
            });
        }
        var obj = new JsonObject
        {
            ["name"] = name!,
            ["config"] = new JsonObject { ["seed"] = 42 },
            ["steps"] = stepsArray,
            ["assertions"] = new JsonArray(),
        };
        var contents = obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var path = Path.Combine(outputDir!, $"{name}.test.json");
        try
        {
            _sink.Write(path, contents);
            _log($"[harness_record_stop] wrote {steps.Count} steps to {path}");
        }
        catch (Exception ex)
        {
            _log($"[harness_record_stop] write failed: {ex.Message}");
        }
    }
}
```

The `IFileSink` interface already exists from M2-record. Reuse it via `using SdvTestFramework.Harness.Recording;` (same namespace).

### Step 3: Wire in ModEntry

Open `src/Harness/ModEntry.cs`. Find the existing `harness_record` console-command registration. Add two new commands alongside it:

```csharp
helper.ConsoleCommands.Add("harness_record_actions",
    "harness_record_actions <name> — start recording gameplay actions to ~/.cache/sdv-test-framework/records/actions/<name>.test.json. Stop with harness_record_stop.",
    this.OnRecordActions);
helper.ConsoleCommands.Add("harness_record_stop",
    "harness_record_stop — finalize the active action-trace recording session.",
    this.OnRecordActionsStop);
```

Add the singleton:

```csharp
// Near other singleton-style fields, alongside _gameThread / _rpc:
private Recording.ActionTraceRecorder? _actionRecorder;
```

In `Entry(IModHelper helper)`, near the other recording infrastructure setup:

```csharp
_actionRecorder = new Recording.ActionTraceRecorder(
    new Recording.FileSink(),
    msg => this.Monitor.Log(msg, LogLevel.Info));

// Subscribe to SMAPI events so action-trace can capture them.
helper.Events.Player.Warped += this.OnPlayerWarped;
helper.Events.Display.MenuChanged += this.OnMenuChanged;
helper.Events.GameLoop.TimeChanged += this.OnTimeChanged;
```

Add the handler methods:

```csharp
private void OnRecordActions(string cmd, string[] args)
{
    if (args.Length < 1)
    {
        this.Monitor.Log("Usage: harness_record_actions <name>", LogLevel.Error);
        return;
    }
    var name = args[0];
    if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9_-]+$"))
    {
        this.Monitor.Log("[harness_record_actions] name must match [A-Za-z0-9_-]+", LogLevel.Error);
        return;
    }
    var outDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".cache", "sdv-test-framework", "records", "actions");
    Directory.CreateDirectory(outDir);
    _actionRecorder!.Start(name, outDir);
}

private void OnRecordActionsStop(string cmd, string[] args) => _actionRecorder?.Stop();

private void OnPlayerWarped(object? sender, StardewModdingAPI.Events.WarpedEventArgs e)
{
    if (_actionRecorder is null || !_actionRecorder.IsRecording) return;
    _actionRecorder.Record(new Recording.RecordedAction(
        DateTime.UtcNow,
        Recording.ActionKind.Warp,
        Location: e.NewLocation?.Name ?? string.Empty,
        X: Game1.player.TilePoint.X,
        Y: Game1.player.TilePoint.Y));
}

private void OnMenuChanged(object? sender, StardewModdingAPI.Events.MenuChangedEventArgs e)
{
    if (_actionRecorder is null || !_actionRecorder.IsRecording) return;
    if (e.NewMenu is null) return;

    // Heuristic: if a DialogueBox or ShopMenu just opened, find the nearest NPC in the
    // player's location and emit world.interact_npc(name).
    var menuType = e.NewMenu.GetType().Name;
    if (menuType is not ("DialogueBox" or "ShopMenu")) return;

    var npc = Game1.currentLocation?.characters?
        .OrderBy(c => Math.Abs(c.TilePoint.X - Game1.player.TilePoint.X)
                     + Math.Abs(c.TilePoint.Y - Game1.player.TilePoint.Y))
        .FirstOrDefault();
    if (npc is null) return;

    _actionRecorder.Record(new Recording.RecordedAction(
        DateTime.UtcNow,
        Recording.ActionKind.NpcInteract,
        NpcName: npc.Name));
}

private int _lastTimeOfDay = -1;
private void OnTimeChanged(object? sender, StardewModdingAPI.Events.TimeChangedEventArgs e)
{
    if (_actionRecorder is null || !_actionRecorder.IsRecording) return;
    // Compute minutes elapsed since last tick. SDV time advances 10-min chunks (HHMM),
    // so just use NewTime - OldTime translated back.
    var oldHHMM = e.OldTime;
    var newHHMM = e.NewTime;
    int oldMinutes = (oldHHMM / 100) * 60 + (oldHHMM % 100);
    int newMinutes = (newHHMM / 100) * 60 + (newHHMM % 100);
    var delta = newMinutes - oldMinutes;
    if (delta <= 0) return;
    _actionRecorder.Record(new Recording.RecordedAction(
        DateTime.UtcNow,
        Recording.ActionKind.TimeAdvance,
        MinutesElapsed: delta));
}
```

Adjust `using` directives + namespace prefixes as needed (the actual existing ModEntry uses `using SdvTestFramework.Harness.Recording;` — check). Add `using System.Linq;` for `OrderBy` + `FirstOrDefault`.

The startup log line should mention the new console commands. Append to the existing string:
- After `harness_record` add `, harness_record_actions, harness_record_stop`.

### Step 4: Verify

Run: `./scripts/ci.sh 2>&1 | grep "Passed:" | head -10`
Expected: +3 tests. Total **347 Passed + 43 Skipped**.

---

## Task 3: Smoke + integration placeholder + docs + roadmap

**Why:** Final task. Ship the skipped placeholder, run live smoke, update docs.

**Files:**
- Create: `tests/Harness.Tests/ActionTraceIntegrationTests.cs` (skipped)
- Modify: `docs/milestones/current.md` — completion subsection.
- Modify: `docs/roadmap.md` — move from Tier 1 to Completed.
- Modify: `docs/rpc-schema.md` — short note that the file shape matches existing scenario JSON; no new RPCs.

### Step 1: Integration placeholder

Create `tests/Harness.Tests/ActionTraceIntegrationTests.cs`:

```csharp
using Xunit;

namespace SdvTestFramework.Harness.Tests;

/// <summary>Integration surface for action-trace recording — verified manually via T3 smoke.</summary>
public class ActionTraceIntegrationTests
{
    [Fact(Skip = "Requires interactive SDV — record a play session via harness_record_actions/_stop and verify the trace.")]
    public void RecordRealPlaySession_ProducesReplayableTrace() { }
}
```

Run: `./scripts/ci.sh 2>&1 | grep "Passed:\|Skipped:" | head -10`
Expected: **347 Passed + 44 Skipped** (+1 Skipped).

### Step 2: Manual smoke

This step requires interactive play. Skip in CI; verify by hand.

```bash
cd /home/fintan/stardewRepos/frobby/sdv-test-framework
pkill -9 -f StardewModdingAPI 2>/dev/null; pkill Xvfb 2>/dev/null; sleep 1
rm -rf ~/.cache/sdv-test-framework-samples/mods
dotnet build -c Release 2>&1 | tail -3

SAMPLES_MODS="$HOME/.cache/sdv-test-framework-samples/mods"
mkdir -p "$SAMPLES_MODS"
cp -r ~/.cache/sdv-test-framework/mods/SdvTestFramework.Harness "$SAMPLES_MODS/"
cp -r "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Stardew Valley/Mods/ContentPatcher" "$SAMPLES_MODS/"

# Launch interactive — DO NOT use Xvfb here.
SMAPI_MODS_PATH="$SAMPLES_MODS" \
    "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Stardew Valley/StardewModdingAPI"
```

In the SMAPI console (after loading a save):
1. Type `harness_record_actions smoke_walk`. See "[harness_record_actions] recording session..." log.
2. Walk player around: Farm → FarmHouse (warp) → out to BusStop (warp) → wait an in-game hour.
3. Type `harness_record_stop`. See "[harness_record_stop] wrote N steps to ...".
4. Inspect the file:
```bash
cat ~/.cache/sdv-test-framework/records/actions/smoke_walk.test.json
```
Expect 3 warp steps + at least one time.advance step.

5. Replay:
```bash
mv ~/.cache/sdv-test-framework/records/actions/smoke_walk.test.json /tmp/smoke_walk.test.json
dotnet run --project src/Runner -c Release --no-build -- run /tmp/smoke_walk.test.json
```
Expect `[run] 1/1 passed` (no assertions, just step replay).

If replay fails because of missing `fixture` field — the trace doesn't ship one;
user adds it manually. Document this as expected: traces produce step-only scenarios;
fixture/assertions are user-added.

### Step 3: docs/milestones/current.md update

After the existing "World.InteractNpc + Time.Set" subsection, add:

```markdown
### Action-trace recording landed (2026-04-24)

Plan: `docs/superpowers/plans/2026-04-24-action-trace-recording.md` (3 tasks, subagent-driven).
Design spec: `docs/superpowers/specs/2026-04-24-action-trace-recording-design.md`.

**Scope:** the third record-mode flow (after M2's state-snapshot + RPC-trace).
`harness_record_actions <name>` + `harness_record_stop` SMAPI console commands capture
human input during play, translate via `ActionTraceTranslator` to a `.test.json`
scenario at `~/.cache/sdv-test-framework/records/actions/<name>.test.json`. Pairs with
MCP `run_scenario` for round-trip authoring (play → trace → edit → run).

**Architecture:** coarse-event translation. Hooks SMAPI's `Player.Warped`,
`Display.MenuChanged`, `GameLoop.TimeChanged` events into a buffered `RecordedAction`
stream. On stop, a pure-function translator (`ActionTraceTranslator.Translate`)
applies heuristics: multi-warp coalesce within 1-second window, NPC-interaction
inference from menu-open + nearest NPC, time-advance debounce at ≥10 in-game minutes.
Result is a readable scenario like
`[warp Farm, time.advance 30, warp SeedShop, world.interact_npc Pierre]`.

**Out of scope (M4):** tick-perfect input replay, tool-use / pickup / combat capture
(needs new RPCs), auto-flush on game exit.

**Test count after action-trace:** 337+43 → ~349+44 (+12 passed, +1 skipped).
```

### Step 4: docs/roadmap.md

Remove the "Action-trace recording" item from Tier 1. The Tier 1 section becomes
empty — that's expected and worth noting. Add a sentence at the bottom of the empty
Tier 1 section:

```markdown
## Tier 1 — LLM-workflow enablers

Items that directly unblock "Claude drives test authoring on real mods." Do these first.

_(All Tier 1 items shipped 2026-04-24. Next: pick from Tier 2 ecosystem work — NuGet
packaging, docs site, example mod suites — or Tier 3 polish.)_
```

Add to the Completed section under "2026-04-24 (even later)" or a new bucket:

```markdown
- **Action-trace recording**. Third record-mode flow. `harness_record_actions` + `harness_record_stop`
  SMAPI commands capture warps, NPC interactions, and time-advance via SMAPI's high-level
  events. `ActionTraceTranslator` applies multi-warp coalesce + NPC inference + time
  debounce heuristics to produce readable scenarios. 337+43 → 349+44.
```

### Step 5: Final CI

Run: `./scripts/ci.sh 2>&1 | grep "Passed:\|Skipped:" | head -10`
Expected: **349 Passed + 44 Skipped**.

---

## Self-review

**1. Spec coverage:**
- `RecordedAction` + `ActionKind` → T1 ✓
- `ActionTraceTranslator` pure function with all heuristics → T1 ✓
- `ActionTraceRecorder` lifecycle → T2 ✓
- SMAPI console commands → T2 ✓
- SMAPI event subscriptions (Warped, MenuChanged, TimeChanged) → T2 ✓
- 7 translator tests + 3 recorder tests → T1 + T2 ✓
- 1 skipped integration placeholder → T3 ✓
- Manual smoke documented → T3 ✓
- Docs updates (milestones, roadmap) → T3 ✓
- All 8 acceptance criteria covered.

**2. Placeholder scan:** No TBD / TODO / vague items. The "(M4 followup)" notes are
explicit deferrals.

**3. Type consistency:**
- `RecordedAction(DateTime At, ActionKind Kind, string? Location, int? X, int? Y, string? NpcName, int? MinutesElapsed)` — defined T1 step 2, consumed T1 + T2.
- `ActionTraceTranslator.Translate(IReadOnlyList<RecordedAction>) → IReadOnlyList<ScenarioStep>` — defined T1 step 3, consumed T2's `Stop()`.
- `ActionTraceRecorder(IFileSink, Action<string>)` ctor, `Start(string, string)`, `Stop()`, `Record(RecordedAction)` — defined T2, consumed by ModEntry handlers.
- `IFileSink` from M2-record — already exists; reused.

**4. Hazards:**
- **NPC inference is best-effort** — picks the spatially-nearest NPC. False positives possible (if 2 NPCs are equidistant, picks one arbitrarily). Acceptable for MVP — user can edit the trace.
- **Time-advance threshold of 10min may filter intended short jumps** — a player who explicitly advances time by 5min via debug command would have that dropped. Acceptable — too rare to warrant complexity.
- **Multi-warp coalesce window of 1s may merge intended distinct warps** — e.g. two quick warps via `warp` debug commands. Rare; acceptable.
- **Trace doesn't include fixture or assertions** — user's responsibility to add. Documented.
- **SMAPI event timing on title screen** — events shouldn't fire pre-load, so `_actionRecorder.IsRecording` guards work. But if something fires unexpectedly, the recorder no-ops gracefully.

---

## Execution handoff

Plan complete and saved to `docs/superpowers/plans/2026-04-24-action-trace-recording.md`. Two execution options:

**1. Subagent-Driven (recommended)** — fresh subagent per task, two-stage review.

**2. Inline Execution** — tasks run in this session via executing-plans.

**Which approach?**
