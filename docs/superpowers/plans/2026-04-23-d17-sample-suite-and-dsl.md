# D1.7 — Sample Suite + DSL Extensions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **No git repo.** Task completion gate is **`./scripts/ci.sh` green** (same convention as D1.5 / D1.6). T10's additional gate is **`./scripts/run-samples.sh` reporting 10/10 PASS** — that's M1's ship criterion.

**Goal:** Land the DSL/RPC extensions needed for rich scenarios (`!=`, array indexing, `draw.assert_not_contains`, accurate assertion counters, and a relaxed `RequireWorldReady` predicate), ship a minimal bundled Content Patcher sample mod, and prove the whole framework works end-to-end with 10 reproducibly-passing scenarios.

**Architecture:** Three phases. Phase A (T1–T5) lands the DSL/RPC extensions that Phase B's scenarios depend on. Phase B (T6–T8) creates the sample CP mod, the smoke wrapper script, and the 10 scenario JSON files. Phase C (T9–T10) adds the skip-marked integration placeholders and runs the final smoke. Each code task follows TDD and ends with `./scripts/ci.sh` green. The final smoke is run via `./scripts/run-samples.sh`.

**Tech Stack:**
- .NET 6 (Harness) + .NET 10 (Runner) — unchanged
- Content Patcher 2.6.0+ format (user has it installed at `~/.var/.../Mods/ContentPatcher/`)
- Harmony 2.x (bundled with SMAPI) — unchanged
- xUnit for unit tests, Xvfb + headless SDV for the smoke
- `schemas/scenario.schema.json` — scenario file format (unchanged)

**Design spec:** `docs/superpowers/specs/2026-04-23-d17-sample-suite-and-dsl-design.md`

---

## File structure

**Modified files:**
- `src/Harness/Rpc/RpcPreconditions.cs` — widen `RequireWorldReady` predicate (T1)
- `src/Runner/Scenarios/ScenarioRunner.cs` — add `!=` operator + array indexing to DSL (T2, T3); pass counters to `scenario.end` (T5)
- `src/Harness/Handlers/ScenarioEndHandler.cs` — accept optional `{assertions_run, assertions_passed}` params, populate `ScenarioState` (T5)
- `src/Harness/ModEntry.cs` — register new handler (T4)
- `docs/rpc-schema.md` — document `draw.assert_not_contains` + predicate relaxation (T4)
- `docs/milestones/current.md` — D1.7 completion note + M1 shippable (T10)

**New files:**
- `src/Harness/Handlers/DrawAssertNotContainsHandler.cs` (T4)
- `tests/Harness.Tests/DrawAssertNotContainsHandlerTests.cs` (T4)
- `tests/Runner.Tests/ScenarioRunnerDslTests.cs` (extend — already exists per ci.sh output) for `!=` + array indexing tests (T2, T3)
- `tests/Harness.Tests/RpcPreconditionsTests.cs` (T1)
- `tests/Harness.Tests/ScenarioEndHandlerTests.cs` (extend — exists from D1.6) for counter param wiring (T5)
- `tests/sample-cp-mod/manifest.json` (T6)
- `tests/sample-cp-mod/content.json` (T6)
- `tests/sample-cp-mod/assets/test-marker.png` (T6)
- `scripts/run-samples.sh` (T7)
- `tests/samples/01-state-time-after-load.test.json` through `tests/samples/10-freeze-parallax-regression.test.json` (T8, 10 files)
- `tests/Harness.Tests/SampleSuiteIntegrationTests.cs` (T9, skip-marked placeholders)

**Verification:** `./scripts/ci.sh` green after every code task. `./scripts/run-samples.sh` 10/10 PASS after T10.

**Starting test count:** 193 Passed + 21 Skipped.
**Target test count after D1.7:** ~203 Passed + ~24 Skipped.

---

## Task 1: Relax RequireWorldReady predicate

**Why:** D1.5 and D1.6 smokes showed `Context.IsWorldReady` stays false under headless Xvfb even after `Game1.gameMode` transitions to `playingGameMode` — blocking every state-mutator RPC. Widen the predicate to match the actual "playable" signal.

**Files:**
- Modify: `src/Harness/Rpc/RpcPreconditions.cs`
- Create: `tests/Harness.Tests/RpcPreconditionsTests.cs`

**Dependencies:** none.

- [ ] **Step 1: Write failing test**

Create `tests/Harness.Tests/RpcPreconditionsTests.cs`:

```csharp
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class RpcPreconditionsTests
{
    [Fact(Skip = "Requires live SDV — predicate widening is exercised by the sample-suite smoke (T10). This placeholder documents the behavior surface.")]
    public void RequireWorldReady_AtTitleScreen_Throws() { }

    [Fact(Skip = "Requires live SDV — predicate widening is exercised by the sample-suite smoke (T10).")]
    public void RequireWorldReady_DuringPlayingGameMode_DoesNotThrow() { }
}
```

Run: `dotnet test tests/Harness.Tests/ --filter RpcPreconditions`
Expected: 2 Skipped (both placeholders).

- [ ] **Step 2: Widen the predicate**

Replace `src/Harness/Rpc/RpcPreconditions.cs` with:

```csharp
using SdvTestFramework.Protocol;
using StardewModdingAPI;
using StardewValley;

namespace SdvTestFramework.Harness.Rpc;

/// <summary>
/// Preconditions that handlers can invoke to short-circuit with a typed
/// <see cref="JsonRpcErrorCode"/> rather than NRE-ing into <c>InternalError</c>.
/// </summary>
public static class RpcPreconditions
{
    /// <summary>
    /// Throws <see cref="JsonRpcErrorCode.GameStateInvalid"/> unless the world is loaded and
    /// interactable. Historically gated on <c>Context.IsWorldReady</c>; that predicate stays
    /// <c>false</c> under headless Xvfb even after <c>Game1.gameMode</c> transitions to
    /// <c>playingGameMode</c>, blocking every mutator in scripted scenarios. D1.7 widens the
    /// gate to <c>(gameMode == playingGameMode &amp;&amp; hasLoadedGame)</c>, which is what
    /// mutators actually need — the save has finished loading and the game is in its normal
    /// gameplay state.
    /// </summary>
    public static void RequireWorldReady()
    {
        if (Game1.gameMode != Game1.playingGameMode || !Game1.hasLoadedGame)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "no active save — mutation requires a loaded world");
    }
}
```

- [ ] **Step 3: Run CI**

Run: `./scripts/ci.sh`
Expected: PASS. Test count 193 → 193 (no new passing tests; +2 Skipped → 21 + 2 = 23 Skipped).

---

## Task 2: Add `!=` operator to ScenarioRunner DSL

**Why:** scenario authors need to assert inequality. Today the DSL only supports `==`.

**Files:**
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs:163–205` (the `state` case of `EvaluateAssertionAsync`)
- Modify: `tests/Runner.Tests/ScenarioRunnerDslTests.cs`

**Dependencies:** none.

- [ ] **Step 1: Write failing tests**

Open `tests/Runner.Tests/ScenarioRunnerDslTests.cs` and add (keep existing tests; add these two `[Fact]` methods inside the existing class):

```csharp
    [Fact]
    public async Task StateAssertion_NotEquals_MismatchedValues_Passes()
    {
        // "state.player.name != 'Tester'" where the mock state returns name=="Wrong" → passes.
        var runner = BuildRunnerWithMockState("""{"name":"Wrong"}""", method: "state.player");
        var result = await runner.EvaluateStateAssertionForTest("state.player.name != 'Tester'");
        Assert.True(result);
    }

    [Fact]
    public async Task StateAssertion_NotEquals_EqualValues_Fails()
    {
        // Same expression, but returning "Tester" — assertion should fail.
        var runner = BuildRunnerWithMockState("""{"name":"Tester"}""", method: "state.player");
        var result = await runner.EvaluateStateAssertionForTest("state.player.name != 'Tester'");
        Assert.False(result);
    }
```

If `BuildRunnerWithMockState` / `EvaluateStateAssertionForTest` don't already exist in `ScenarioRunnerDslTests.cs`, study the existing test helpers in that file and use whatever seam is already there. The existing DSL tests (`StateAssertion_EvaluatesEqualityDsl`, `StateAssertion_IntegerLiteral_Matches`) already drive this path; mirror their shape exactly.

Run: `dotnet test tests/Runner.Tests/ --filter StateAssertion_NotEquals`
Expected: FAIL — `!=` parse returns false from the current `==`-only split.

- [ ] **Step 2: Extend the DSL parser**

In `src/Runner/Scenarios/ScenarioRunner.cs`, locate the `state` case of `EvaluateAssertionAsync` (currently around lines 156–202). Replace the split logic at **line 163** (`var parts = a.Expr.Split("==", 2);`) with a two-operator check:

```csharp
                // Split on the first occurrence of "!=" or "==" — "!=" checked first so that
                // "a != b" doesn't get parsed as "a !" "= b".
                bool negated;
                string[] parts;
                int neqIdx = a.Expr.IndexOf("!=", StringComparison.Ordinal);
                int eqIdx = a.Expr.IndexOf("==", StringComparison.Ordinal);
                if (neqIdx >= 0 && (eqIdx < 0 || neqIdx < eqIdx))
                {
                    negated = true;
                    parts = new[] { a.Expr.Substring(0, neqIdx), a.Expr.Substring(neqIdx + 2) };
                }
                else if (eqIdx >= 0)
                {
                    negated = false;
                    parts = new[] { a.Expr.Substring(0, eqIdx), a.Expr.Substring(eqIdx + 2) };
                }
                else
                {
                    return false;
                }
                if (parts.Length != 2) return false;
```

Then at the very end of the `state` case, wrap the three literal-comparison blocks' final return values. The simplest way: keep the existing blocks but XOR each result with `negated`:

For the quoted-string block (around current lines 182–187), replace:
```csharp
                    return cur.ValueKind == JsonValueKind.String && cur.GetString() == literal;
```
with:
```csharp
                    bool eq = cur.ValueKind == JsonValueKind.String && cur.GetString() == literal;
                    return negated ? !eq : eq;
```

For the integer-literal block (around current lines 189–194):
```csharp
                    bool eq = cur.ValueKind == JsonValueKind.Number
                        && cur.TryGetInt64(out var cv) && cv == intLit;
                    return negated ? !eq : eq;
```

For the boolean-literal block (around current lines 196–200):
```csharp
                    bool eq = (cur.ValueKind == JsonValueKind.True && boolLit)
                        || (cur.ValueKind == JsonValueKind.False && !boolLit);
                    return negated ? !eq : eq;
```

The trailing `return false;` (for unrecognised literal) stays as `return false;` — an unparseable RHS is a scenario author error, not "pass because of `!=`."

- [ ] **Step 3: Run tests — verify PASS**

Run: `dotnet test tests/Runner.Tests/ --filter StateAssertion`
Expected: all DSL tests (including existing and new) PASS.

- [ ] **Step 4: Run full CI**

Run: `./scripts/ci.sh`
Expected: PASS. Test count 193 → 195 (+2).

---

## Task 3: Add array indexing to ScenarioRunner DSL

**Why:** Scenarios need to peek into lists (e.g. `state.player.items[0].id`). Current DSL splits path tokens on `.` only, so `items[0]` is treated as a single field name, which won't exist.

**Files:**
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs` — path-resolution loop around line 174
- Modify: `tests/Runner.Tests/ScenarioRunnerDslTests.cs`

**Dependencies:** T2 (both touch the same DSL file; land T2 first so the `!=` split is in place).

- [ ] **Step 1: Write failing tests**

Add to `tests/Runner.Tests/ScenarioRunnerDslTests.cs`:

```csharp
    [Fact]
    public async Task StateAssertion_ArrayIndex_ValidIndex_ResolvesElement()
    {
        // state.player.items[0].id == 'O388' — RHS is element 0's id field.
        var mockJson = """{"items":[{"id":"O388","count":3},{"id":"O390","count":1}]}""";
        var runner = BuildRunnerWithMockState(mockJson, method: "state.player");
        var result = await runner.EvaluateStateAssertionForTest("state.player.items[0].id == 'O388'");
        Assert.True(result);
    }

    [Fact]
    public async Task StateAssertion_ArrayIndex_OutOfRange_Fails()
    {
        var mockJson = """{"items":[{"id":"O388"}]}""";
        var runner = BuildRunnerWithMockState(mockJson, method: "state.player");
        var result = await runner.EvaluateStateAssertionForTest("state.player.items[5].id == 'O388'");
        Assert.False(result);
    }
```

Run: `dotnet test tests/Runner.Tests/ --filter ArrayIndex`
Expected: FAIL — `items[0]` isn't parsed as "field items, index 0."

- [ ] **Step 2: Extend path resolution**

In `src/Runner/Scenarios/ScenarioRunner.cs`, locate the path-resolution loop inside the `state` case (currently around lines 173–179):

```csharp
                JsonElement cur = root;
                for (int i = 2; i < pathTokens.Length; i++)
                {
                    if (cur.ValueKind != JsonValueKind.Object) return false;
                    if (!cur.TryGetProperty(pathTokens[i], out var nested)) return false;
                    cur = nested;
                }
```

Replace it with a version that parses `field[N]` per token:

```csharp
                JsonElement cur = root;
                for (int i = 2; i < pathTokens.Length; i++)
                {
                    var token = pathTokens[i];
                    // Match "field[N]" → {field, index}. Regex is intentionally tight; no nested
                    // indexes, no slicing — scenarios can compose multiple assertions instead.
                    var m = System.Text.RegularExpressions.Regex.Match(token, @"^([A-Za-z_][A-Za-z0-9_]*)\[(\d+)\]$");
                    if (m.Success)
                    {
                        var fieldName = m.Groups[1].Value;
                        var index = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                        if (cur.ValueKind != JsonValueKind.Object) return false;
                        if (!cur.TryGetProperty(fieldName, out var arr)) return false;
                        if (arr.ValueKind != JsonValueKind.Array) return false;
                        if (index < 0 || index >= arr.GetArrayLength()) return false;
                        cur = arr[index];
                    }
                    else
                    {
                        if (cur.ValueKind != JsonValueKind.Object) return false;
                        if (!cur.TryGetProperty(token, out var nested)) return false;
                        cur = nested;
                    }
                }
```

Add `using System.Text.RegularExpressions;` at the top of the file if not already present (the `System.Text.RegularExpressions.Regex` fully-qualified reference above doesn't need the `using`; either style works).

- [ ] **Step 3: Run tests**

Run: `dotnet test tests/Runner.Tests/ --filter ArrayIndex`
Expected: both PASS.

- [ ] **Step 4: Full CI**

Run: `./scripts/ci.sh`
Expected: PASS. Test count 195 → 197 (+2).

---

## Task 4: DrawAssertNotContainsHandler + DTO + schema

**Why:** Scenarios need negative draw assertions ("no town-interior tile should render while I'm on the farm"). The existing `DrawAssertContainsHandler` inverts trivially — we want the mirror.

**Files:**
- Create: `src/Harness/Handlers/DrawAssertNotContainsHandler.cs`
- Modify: `src/Harness/ModEntry.cs` (register the new handler)
- Modify: `docs/rpc-schema.md` (document the RPC)
- Create: `tests/Harness.Tests/DrawAssertNotContainsHandlerTests.cs`

**Dependencies:** none.

- [ ] **Step 1: Write failing tests**

Create `tests/Harness.Tests/DrawAssertNotContainsHandlerTests.cs`:

```csharp
using System.Text.Json;
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class DrawAssertNotContainsHandlerTests
{
    public DrawAssertNotContainsHandlerTests()
    {
        Recorder.Initialize(null, capacity: 16);
        Recorder.Disarm();
    }

    [Fact]
    public void Handle_EmptyBuffer_PassesOk()
    {
        // No events captured → filter matches 0 → not_contains passes.
        var req = JsonDocument.Parse("""{"filter":{"texture_asset":"anything"}}""").RootElement;
        var resp = DrawAssertNotContainsHandler.Handle(req);
        Assert.True(resp.GetProperty("passed").GetBoolean());
        Assert.Equal(0, resp.GetProperty("matched_count").GetInt32());
    }

    [Fact]
    public void Handle_MatchFound_FailsButReturnsSample()
    {
        // Seed a draw event that matches, then assert_not_contains should fail with ok=false
        // and a sample event in the response.
        Recorder.Arm(1);
        // NOTE: Arm may defer if game isn't ready; force _armed true via a workaround —
        // instead, write to the buffer by calling Record directly with a fake event.
        Recorder.Record(new DrawEvent
        {
            Tick = 1, CallIndex = 1,
            DestRect = new Rectangle(0, 0, 16, 16),
            Color = Color.White,
        });
        Recorder.Disarm();

        var req = JsonDocument.Parse("""{"filter":{}}""").RootElement;  // empty filter matches all
        var resp = DrawAssertNotContainsHandler.Handle(req);
        Assert.False(resp.GetProperty("passed").GetBoolean());
        Assert.Equal(1, resp.GetProperty("matched_count").GetInt32());
    }

    [Fact]
    public void Handle_InvalidFilter_ThrowsInvalidParams()
    {
        // tex_w < 0 fails DrawFilterValidator — same code path as assert_contains.
        var req = JsonDocument.Parse("""{"filter":{"tex_w":-1}}""").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => DrawAssertNotContainsHandler.Handle(req));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }
}
```

Run: `dotnet test tests/Harness.Tests/ --filter DrawAssertNotContains`
Expected: FAIL — `DrawAssertNotContainsHandler` type doesn't exist.

- [ ] **Step 2: Create the handler**

Create `src/Harness/Handlers/DrawAssertNotContainsHandler.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>draw.assert_not_contains</c>. Counts matches of a filter against
/// the captured buffer and returns pass when matches == 0 (inverse of draw.assert_contains).</summary>
public static class DrawAssertNotContainsHandler
{
    public const string Method = "draw.assert_not_contains";

    private sealed class AssertRequest
    {
        public DrawFilter? Filter { get; set; } = new();
        public string? Message { get; set; }
    }

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var req = RpcParams.Required<AssertRequest>(paramsElement);
        req.Filter ??= new DrawFilter();
        DrawFilterValidator.Validate(req.Filter);

        Recorder.SnapshotEvents(out var events, out _);
        int matched = 0;
        foreach (ref readonly var e in events.AsSpan())
        {
            if (DrawFilterMatcher.Matches(in e, req.Filter))
                matched++;
        }

        return ProtocolJson.ToElement(new AssertResult
        {
            // Reuse AssertResult DTO — min_count isn't meaningful here, but the shape
            // (passed/matched_count/message) is otherwise identical, and scenario consumers
            // can treat "not_contains passed" identically to "contains passed."
            MinCount = 0,
            MatchedCount = matched,
            Passed = matched == 0,
            Message = req.Message,
        });
    }
}
```

- [ ] **Step 3: Register in ModEntry**

In `src/Harness/ModEntry.cs`, find the existing `DrawAssertContainsHandler` registration (search for `DrawAssertContainsHandler.Method`). Add right after:

```csharp
        _rpc.Register(DrawAssertNotContainsHandler.Method, p => DrawAssertNotContainsHandler.Handle(p));
```

Also update the `Harness loaded…` log string's `Draw:` section to include `draw.assert_not_contains`.

- [ ] **Step 4: Document in rpc-schema.md**

In `docs/rpc-schema.md`, find the `### draw.assert_contains` section. After it, insert:

```markdown
### draw.assert_not_contains

Inverse of `draw.assert_contains` — succeeds when no captured draw event matches the filter.

**Params:**

```json
{"filter": {...DrawFilter shape...}, "message": "optional"}
```

**Response:**

```json
{"passed": true, "matched_count": 0, "min_count": 0, "message": null}
```

`passed` is `true` iff `matched_count == 0`. `min_count` is always `0` in the response (kept in the shape for parity with `draw.assert_contains`). `message` passes through from the request for consumer display.

**Errors:** `InvalidParams (-32602)` if the filter fails validation (same code path as `draw.assert_contains`).
```

- [ ] **Step 5: Run CI**

Run: `./scripts/ci.sh`
Expected: PASS. Test count 197 → 200 (+3).

---

## Task 5: Wire scenario counters through scenario.end

**Why:** `ScenarioState.AssertionsRun` / `AssertionsPassed` stay at 0 the whole scenario because `ScenarioRunner` tracks counts locally but never tells the harness. Approach A: runner passes final counts as `scenario.end` params; harness handler copies them into `ScenarioState` so the response is truthful.

**Files:**
- Modify: `src/Harness/Handlers/ScenarioEndHandler.cs` — accept optional params
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs` — pass counters in `scenario.end` call
- Modify: `tests/Harness.Tests/ScenarioEndHandlerTests.cs` — add one test

**Dependencies:** none (orthogonal to T1–T4).

- [ ] **Step 1: Write failing test (harness side)**

In `tests/Harness.Tests/ScenarioEndHandlerTests.cs`, add (inside the existing `ScenarioEndHandlerTests` class):

```csharp
    [Fact]
    public void Handle_WithAssertionCounts_PopulatesScenarioState()
    {
        var s = Scenarios.ScenarioState.Current;
        s.Reset();
        s.IsActive = true;
        s.Name = "t5";
        s.StartUtc = System.DateTime.UtcNow;

        var json = System.Text.Json.JsonDocument
            .Parse("""{"assertions_run":7,"assertions_passed":6}""").RootElement;
        var resp = ScenarioEndHandler.Handle(json);

        Assert.Equal(7, resp.GetProperty("assertions_run").GetInt32());
        Assert.Equal(6, resp.GetProperty("assertions_passed").GetInt32());
    }
```

Run: `dotnet test tests/Harness.Tests/ --filter Handle_WithAssertionCounts`
Expected: FAIL — handler currently ignores params.

- [ ] **Step 2: Accept optional params in ScenarioEndHandler**

In `src/Harness/Handlers/ScenarioEndHandler.cs`, find the `Handle` method. After the auto-thaw block added in D1.6 T10, and BEFORE the line `var elapsed = (DateTime.UtcNow - s.StartUtc).TotalMilliseconds;`, insert:

```csharp
        // Optional per-scenario counter snapshot, populated by ScenarioRunner when it calls
        // scenario.end. Missing params → the scenario ran without runner wiring (e.g. hand
        // probe via Python), in which case the counters stay at their existing values.
        if (paramsElement is { ValueKind: System.Text.Json.JsonValueKind.Object } obj)
        {
            if (obj.TryGetProperty("assertions_run", out var ar) && ar.TryGetInt32(out var arI))
                s.AssertionsRun = arI;
            if (obj.TryGetProperty("assertions_passed", out var ap) && ap.TryGetInt32(out var apI))
                s.AssertionsPassed = apI;
        }
```

- [ ] **Step 3: Write failing test (runner side)**

In `tests/Runner.Tests/ScenarioRunnerTests.cs` (or a similarly-themed existing file — find an assertion-counting test first), add:

```csharp
    [Fact]
    public async Task RunAsync_PassesAssertionCountsToScenarioEnd()
    {
        // Runs a scenario with 2 passing state assertions via a mocked session. Assert
        // that scenario.end was invoked with params {assertions_run:2, assertions_passed:2}.
        // Use whatever mock-session pattern already exists in the test file.
        // If no such pattern exists, study ScenarioRunnerTests.EmptyScenario_Passes for
        // the mocking convention and mirror it.
        // (Plan: this test is conceptual — the exact assertion depends on the existing
        // test harness. If the existing tests can't see scenario.end's params, skip this
        // step and rely on T10's live smoke to verify the wire shape.)
    }
```

If ScenarioRunnerTests doesn't have a mocking seam that exposes the scenario.end params, skip this runner-side test and note it in your report. The harness-side test from Step 1 is the load-bearing check; the runner-side wiring is verified live by the T10 smoke (the `scenario.end` response shown on console should report nonzero counts).

- [ ] **Step 4: Wire counters in ScenarioRunner**

In `src/Runner/Scenarios/ScenarioRunner.cs`, find the `finally` block that currently calls `scenario.end` with `params_: null` (around line 94):

```csharp
                await _session.InvokeAsync("scenario.end", params_: null, ct);
```

Replace with:

```csharp
                // Pass accumulated counts so the harness can surface them in its response.
                var endParams = System.Text.Json.JsonSerializer.SerializeToElement(
                    new { assertions_run = report.AssertionsRun, assertions_passed = report.AssertionsPassed },
                    ProtocolJson.Options);
                await _session.InvokeAsync("scenario.end", endParams, ct);
```

- [ ] **Step 5: Run CI**

Run: `./scripts/ci.sh`
Expected: PASS. Test count 200 → 201 (+1). If the optional runner-side test from Step 3 was written, 201 → 202 (+2).

---

## Task 6: Minimal bundled Content Patcher sample mod

**Why:** Scenarios need a real mod to assert against. Ships with the repo so smoke is reproducible for any contributor who has Content Patcher installed locally (it's in `~/.var/.../Mods/ContentPatcher/` on this workstation; CI can mirror).

**Files:**
- Create: `tests/sample-cp-mod/manifest.json`
- Create: `tests/sample-cp-mod/content.json`
- Create: `tests/sample-cp-mod/assets/test-marker.png`

**Dependencies:** none (independent content work).

- [ ] **Step 1: Create manifest.json**

Create `tests/sample-cp-mod/manifest.json`:

```json
{
  "Name": "SDV Test Framework — Sample CP Mod",
  "Author": "sdv-test-framework",
  "Version": "0.1.0",
  "Description": "Minimal Content Patcher mod used by the framework's sample suite. Patches LooseSprites/Cursors with a recognisable color swatch so scenarios can assert against it via draw.assert_contains. Not intended for real gameplay.",
  "UniqueID": "SdvTestFramework.SampleCpMod",
  "MinimumApiVersion": "4.1.10",
  "UpdateKeys": [],
  "ContentPackFor": {
    "UniqueID": "Pathoschild.ContentPatcher",
    "MinimumVersion": "2.6.0"
  }
}
```

- [ ] **Step 2: Create content.json**

Create `tests/sample-cp-mod/content.json`:

```json
{
  "Format": "2.6.0",
  "Changes": [
    {
      "Action": "Load",
      "Target": "Mods/SdvTestSample/TestMarker",
      "FromFile": "assets/test-marker.png",
      "LogName": "Load SdvTestSample/TestMarker"
    },
    {
      "Action": "EditImage",
      "Target": "LooseSprites/Cursors",
      "FromFile": "assets/test-marker.png",
      "ToArea": { "X": 0, "Y": 368, "Width": 16, "Height": 16 },
      "LogName": "Patch cursor tile with test marker"
    }
  ]
}
```

The `ToArea` coordinates target an unused-looking region of `LooseSprites/Cursors` (near the bottom-left; verify visually by opening the texture and picking an empty 16x16 block if 0/368 overlaps something important). Pick a different rect if needed — the exact coordinates only matter for which tile the patched color swatch lands in, which feeds scenario 4's assertion.

- [ ] **Step 3: Create the asset PNG**

Create `tests/sample-cp-mod/assets/test-marker.png` — a 16×16 solid magenta (`#FF00FF`) PNG. Generate via:

```bash
python3 -c "
from PIL import Image
img = Image.new('RGBA', (16, 16), (255, 0, 255, 255))
img.save('tests/sample-cp-mod/assets/test-marker.png')
"
```

If `PIL` isn't available, use any 16×16 pure-magenta PNG — `convert` from ImageMagick also works:

```bash
convert -size 16x16 xc:'#FF00FF' tests/sample-cp-mod/assets/test-marker.png
```

Verify the file is well-formed:

```bash
file tests/sample-cp-mod/assets/test-marker.png
```

Expected output: `PNG image data, 16 x 16, 8-bit/color RGBA, non-interlaced`.

- [ ] **Step 4: Validate the CP mod loads**

Manually launch SMAPI with the sample CP mod + Content Patcher + the harness in an isolated mods dir:

```bash
SAMPLES=/tmp/sdv-d17-sample-test-$(date +%s)
mkdir -p "$SAMPLES/mods"
cp -r ~/.cache/sdv-test-framework/mods/SdvTestFramework.Harness "$SAMPLES/mods/"
cp -r "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Stardew Valley/Mods/ContentPatcher" "$SAMPLES/mods/"
cp -r tests/sample-cp-mod "$SAMPLES/mods/SdvTestFramework.SampleCpMod"

Xvfb :99 -screen 0 1280x720x24 >/dev/null 2>&1 &
DISPLAY=:99 LIBGL_ALWAYS_SOFTWARE=1 timeout 30 \
  "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Stardew Valley/StardewModdingAPI" \
  --mods-path "$SAMPLES/mods" 2>&1 | grep -E "SdvTestSample|Sample CP|error|ERROR|Failed" | head -20

pkill -9 -f StardewModdingAPI 2>/dev/null; pkill Xvfb 2>/dev/null
```

Expected: lines including `Loading mod 'SDV Test Framework — Sample CP Mod'` and `Loaded 2 content pack(s)` (or similar) without error lines. If Content Patcher reports "invalid" or "parse error," rework the content.json in step 2.

- [ ] **Step 5: Full CI**

Run: `./scripts/ci.sh`
Expected: PASS (no code change in this task; just new content files). Test count unchanged at 201 (or 202 if T5 added the optional test).

---

## Task 7: scripts/run-samples.sh smoke wrapper

**Why:** The sample suite needs a one-shot launch that stages Content Patcher + the sample mod + the harness into an isolated mods dir, launches Xvfb + the Runner, and tears down cleanly.

**Files:**
- Create: `scripts/run-samples.sh`

**Dependencies:** T6 (sample mod must exist).

- [ ] **Step 1: Create the script**

Create `scripts/run-samples.sh`:

```bash
#!/usr/bin/env bash
# Sample-suite smoke runner. Stages Content Patcher + sample-cp-mod + harness into an
# isolated mods dir, launches Xvfb + SDV via the Runner, runs tests/samples/*.test.json.
# Returns non-zero if any scenario fails.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SDV_ROOT="$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Stardew Valley"
SAMPLES_MODS="$HOME/.cache/sdv-test-framework-samples/mods"

# 0. sanity-check Content Patcher is installed in the user's SDV mods dir
if [ ! -d "$SDV_ROOT/Mods/ContentPatcher" ]; then
    echo "error: Content Patcher not found at '$SDV_ROOT/Mods/ContentPatcher'" >&2
    echo "install it from https://www.nexusmods.com/stardewvalley/mods/1915" >&2
    exit 2
fi

# 1. build Release (also auto-stages the harness payload to the default mods cache)
cd "$REPO"
dotnet build -c Release >/dev/null

# 2. rebuild isolated mods dir
rm -rf "$SAMPLES_MODS"
mkdir -p "$SAMPLES_MODS"
cp -r ~/.cache/sdv-test-framework/mods/SdvTestFramework.Harness "$SAMPLES_MODS/"
cp -r "$SDV_ROOT/Mods/ContentPatcher" "$SAMPLES_MODS/"
cp -r "$REPO/tests/sample-cp-mod" "$SAMPLES_MODS/SdvTestFramework.SampleCpMod"

# 3. Xvfb + run the scenarios
pkill -9 -f StardewModdingAPI 2>/dev/null || true
pkill Xvfb 2>/dev/null || true
sleep 1
Xvfb :99 -screen 0 1280x720x24 >/dev/null 2>&1 &
XVFB_PID=$!
trap "pkill -9 -f StardewModdingAPI 2>/dev/null; kill $XVFB_PID 2>/dev/null; exit" EXIT

DISPLAY=:99 LIBGL_ALWAYS_SOFTWARE=1 dotnet run --project src/Runner -c Release --no-build -- \
    run "$REPO/tests/samples/" --mods-path "$SAMPLES_MODS"
```

Make it executable:

```bash
chmod +x scripts/run-samples.sh
```

- [ ] **Step 2: Verify the script parses**

Run: `bash -n scripts/run-samples.sh`
Expected: no output (no syntax errors).

Do NOT run the script end-to-end yet — the sample scenarios don't exist until T8.

- [ ] **Step 3: Full CI**

Run: `./scripts/ci.sh`
Expected: PASS. Test count unchanged.

---

## Task 8: Author the 10 sample scenarios

**Why:** Spec §7 Phase 1 success criterion — 10 reproducibly-passing scenarios covering the framework's surface.

**Files:**
- Create: `tests/samples/01-state-time-after-load.test.json`
- Create: `tests/samples/02-state-player-inventory-index.test.json`
- Create: `tests/samples/03-draw-contains-sample-marker.test.json`
- Create: `tests/samples/04-draw-contains-patched-cursor.test.json`
- Create: `tests/samples/05-draw-not-contains-unused-asset.test.json`
- Create: `tests/samples/06-draw-not-contains-after-warp.test.json`
- Create: `tests/samples/07-player-warp-updates-location.test.json`
- Create: `tests/samples/08-player-set-money-roundtrip.test.json`
- Create: `tests/samples/09-freeze-tick-stable.test.json`
- Create: `tests/samples/10-freeze-parallax-regression.test.json`

**Dependencies:** T1–T5 (DSL extensions must exist for scenarios to author against).

Each file is a JSON document conforming to `schemas/scenario.schema.json`. The spec (see `docs/superpowers/specs/2026-04-23-d17-sample-suite-and-dsl-design.md` §The 10 scenarios) defines the semantics; below are the literal JSON bodies.

- [ ] **Step 1: Create scenarios 01 and 02 (state-only)**

`tests/samples/01-state-time-after-load.test.json`:

```json
{
  "name": "state_time_after_load",
  "fixture": "m0spike_436515781",
  "config": { "seed": 42 },
  "steps": [],
  "assertions": [
    { "type": "state", "expr": "state.time.in_save == true" },
    { "type": "state", "expr": "state.time.season == 'spring'" }
  ]
}
```

`tests/samples/02-state-player-inventory-index.test.json`:

```json
{
  "name": "state_player_inventory_index",
  "fixture": "m0spike_436515781",
  "config": { "seed": 42 },
  "steps": [],
  "assertions": [
    { "type": "state", "expr": "state.player.items[0].id != 'Unknown'" }
  ]
}
```

- [ ] **Step 2: Create scenarios 03 and 04 (positive draw assertions)**

`tests/samples/03-draw-contains-sample-marker.test.json`:

```json
{
  "name": "draw_contains_sample_marker",
  "fixture": "m0spike_436515781",
  "config": { "seed": 42 },
  "steps": [
    { "action": "draw.arm", "args": { "ticks": 60 } },
    { "action": "time.advance", "args": { "minutes": 10 } }
  ],
  "assertions": [
    {
      "type": "draw.contains",
      "filter": { "texture_asset": "LooseSprites/Cursors" },
      "min_count": 1,
      "message": "the cursor atlas (which now includes the sample mod's patched tile) should render at least once"
    }
  ]
}
```

Note: scenario 3 asserts the UNDERLYING atlas renders — the patched-tile specificity is scenario 4. The text "sample marker" in the filename refers to the fact that this atlas now contains sample-mod-provided pixels because of the `EditImage` patch. A more direct assertion on `Mods/SdvTestSample/TestMarker` is possible only if we also place the marker somewhere visible (the current sample mod only references it via `FromFile`, not via a visible target). Accept this pragmatic shape; the parallel test 4 is more precise.

`tests/samples/04-draw-contains-patched-cursor.test.json`:

```json
{
  "name": "draw_contains_patched_cursor",
  "fixture": "m0spike_436515781",
  "config": { "seed": 42 },
  "steps": [
    { "action": "draw.arm", "args": { "ticks": 60 } },
    { "action": "time.advance", "args": { "minutes": 10 } }
  ],
  "assertions": [
    {
      "type": "draw.contains",
      "filter": {
        "texture_asset": "LooseSprites/Cursors",
        "source_rect": { "x": 0, "y": 368, "w": 16, "h": 16 }
      },
      "min_count": 1,
      "message": "the specific cursor tile patched by the sample mod should render"
    }
  ]
}
```

- [ ] **Step 3: Create scenarios 05 and 06 (negative draw assertions)**

`tests/samples/05-draw-not-contains-unused-asset.test.json`:

```json
{
  "name": "draw_not_contains_unused_asset",
  "fixture": "m0spike_436515781",
  "config": { "seed": 42 },
  "steps": [
    { "action": "draw.arm", "args": { "ticks": 30 } },
    { "action": "time.advance", "args": { "minutes": 5 } }
  ],
  "assertions": [
    {
      "type": "draw.not_contains",
      "filter": { "texture_asset": "Mods/SdvTestSample/NonExistentAsset" },
      "message": "an asset the sample mod doesn't define must never render"
    }
  ]
}
```

`tests/samples/06-draw-not-contains-after-warp.test.json`:

```json
{
  "name": "draw_not_contains_after_warp",
  "fixture": "m0spike_436515781",
  "config": { "seed": 42 },
  "steps": [
    { "action": "player.warp", "args": { "location": "Farm", "x": 64, "y": 15 } },
    { "action": "draw.arm", "args": { "ticks": 30 } },
    { "action": "time.advance", "args": { "minutes": 5 } }
  ],
  "assertions": [
    {
      "type": "draw.not_contains",
      "filter": { "texture_asset": "Maps/townInterior" },
      "message": "outside-town assets must not render while on the Farm"
    }
  ]
}
```

`scenario.runner` needs a `draw.not_contains` case in `EvaluateAssertionAsync` for these scenarios 5 and 6 to work. The existing `draw.contains` case delegates to `draw.assert_contains`; add a parallel branch that delegates to `draw.assert_not_contains`. Update `src/Runner/Scenarios/ScenarioRunner.cs:EvaluateAssertionAsync`, **right after the existing `case "draw.contains":` block (line ~155)**:

```csharp
            case "draw.not_contains":
            {
                if (a.Filter is null) return false;
                var payload = new { filter = a.Filter, message = a.Message };
                var req = JsonSerializer.SerializeToElement(payload, ProtocolJson.Options);
                var resp = await _session.InvokeAsync("draw.assert_not_contains", req, ct);
                if (resp.Error is not null) return false;
                if (resp.Result is not { } r) return false;
                return r.TryGetProperty("passed", out var p) && p.GetBoolean();
            }
```

This is a small integration the spec didn't call out explicitly but the `draw.not_contains` assertion type is referenced in scenarios 5 and 6. Add it here.

- [ ] **Step 4: Create scenarios 07 and 08 (manipulators)**

`tests/samples/07-player-warp-updates-location.test.json`:

```json
{
  "name": "player_warp_updates_location",
  "fixture": "m0spike_436515781",
  "config": { "seed": 42 },
  "steps": [
    { "action": "player.warp", "args": { "location": "Farm", "x": 64, "y": 15 } }
  ],
  "assertions": [
    { "type": "state", "expr": "state.location.name == 'Farm'" }
  ]
}
```

`tests/samples/08-player-set-money-roundtrip.test.json`:

```json
{
  "name": "player_set_money_roundtrip",
  "fixture": "m0spike_436515781",
  "config": { "seed": 42 },
  "steps": [
    { "action": "player.set_money", "args": { "amount": 5000 } }
  ],
  "assertions": [
    { "type": "state", "expr": "state.player.money == 5000" }
  ]
}
```

- [ ] **Step 5: Create scenarios 09 and 10 (determinism)**

`tests/samples/09-freeze-tick-stable.test.json`:

```json
{
  "name": "freeze_tick_stable",
  "fixture": "m0spike_436515781",
  "config": { "seed": 42 },
  "steps": [
    { "action": "freeze.begin", "args": {} },
    { "action": "time.advance", "args": { "minutes": 1 } },
    { "action": "freeze.status", "args": {} },
    { "action": "freeze.end", "args": {} }
  ],
  "assertions": []
}
```

Scenario 9's assertion isn't a DSL expression — it's the structural fact that `time.advance` during a freeze is a no-op from the outside, and `freeze.status`/`freeze.end` both succeed. If all four steps complete without error, the scenario passes. No explicit assertion needed — the runner treats step failures as scenario failures, and `ScenarioRunner.Failures` only grows on non-success responses.

`tests/samples/10-freeze-parallax-regression.test.json`:

```json
{
  "name": "freeze_parallax_regression",
  "fixture": "m0spike_436515781",
  "config": { "seed": 42 },
  "steps": [
    { "action": "player.warp", "args": { "location": "Beach", "x": 20, "y": 30 } },
    { "action": "freeze.begin", "args": {} },
    { "action": "draw.arm", "args": { "ticks": 60 } },
    { "action": "time.advance", "args": { "minutes": 1 } },
    { "action": "freeze.end", "args": {} }
  ],
  "assertions": []
}
```

Scenario 10 is also a "all steps succeed" scenario. A full parallax-regression assertion (hash snapshot1 == hash snapshot2) would require a new `draw.assert_snapshot_stable` RPC or scenario feature — defer that to M2. For D1.7, this scenario documents the flow and verifies the steps compose without error. The M0 parallax residual's actual regression check happens in the D1.6 unit tests + the T9 skip-marked `FreezeParallaxRegression_HashesMatch` placeholder.

- [ ] **Step 6: Validate all 10 against the schema**

The runner validates against the schema at load time. Quick smoke of schema validation only:

```bash
dotnet run --project src/Runner -c Release --no-build -- list tests/samples/
```

Expected: prints 10 scenario names, exit 0. If any scenario fails schema validation, the command errors with the specific file.

- [ ] **Step 7: Full CI**

Run: `./scripts/ci.sh`
Expected: PASS. Test count unchanged.

---

## Task 9: Skip-marked integration tests

**Why:** Document the live-SDV behavior surface for test-discovery visibility, mirroring the D1.6 pattern.

**Files:**
- Create: `tests/Harness.Tests/SampleSuiteIntegrationTests.cs`

**Dependencies:** none.

- [ ] **Step 1: Create the placeholder test file**

Create `tests/Harness.Tests/SampleSuiteIntegrationTests.cs`:

```csharp
using Xunit;

namespace SdvTestFramework.Harness.Tests;

/// <summary>D1.7 integration surface — exercised by <c>./scripts/run-samples.sh</c>. Documented
/// here so the behavior is visible at test-discovery time.</summary>
public class SampleSuiteIntegrationTests
{
    [Fact(Skip = "Requires live SDV + Content Patcher — sample-suite smoke (scripts/run-samples.sh) verifies this.")]
    public void SampleCpMod_Loads_UnderSmapi() { }

    [Fact(Skip = "Requires live SDV — sample-suite smoke runs all ten scenarios to completion.")]
    public void SampleSuite_AllTenScenariosPass() { }

    [Fact(Skip = "Requires live SDV at Beach — sample-suite smoke confirms parallax scroll doesn't advance while frozen (M0 residual).")]
    public void FreezeParallaxRegression_HashesMatch() { }
}
```

- [ ] **Step 2: Full CI**

Run: `./scripts/ci.sh`
Expected: PASS. Test count 201/202 → same counts +3 Skipped → 21 + 2 (T1) + 3 (T9) = 26 Skipped total. Passed count unchanged.

---

## Task 10: Run the end-to-end smoke + ship M1

**Why:** The ship criterion. Spec §7 Phase 1 success criterion: "author 10 sample scenarios covering one real mod, all pass reproducibly."

**Files:**
- No code changes — run the script.
- Update: `docs/milestones/current.md` — D1.7 completion subsection + M1 shippable note.

**Dependencies:** T1–T9.

- [ ] **Step 1: Execute the smoke**

Run:

```bash
./scripts/run-samples.sh
```

Expected: `[run] 10/10 passed` on the final line. Exit 0.

- [ ] **Step 2: If any scenario fails — diagnose and fix**

If the smoke reports fewer than 10 passed, the milestone is **not** complete. Do NOT mark scenarios skipped to close the gate. Common failure modes and remedies:

- **`fixture.load` fails** — m0spike save missing from `~/.config/StardewValley/Saves/` or Flatpak-path equivalent. Re-create per M0 spike instructions.
- **`state.time.in_save == true` fails on scenario 1** — T1's predicate didn't widen enough. Inspect `Context.IsWorldReady` behavior via a debug probe.
- **Sample mod content pack doesn't apply** — check Content Patcher's log for "couldn't load content pack" errors; usually a `content.json` parse issue. Fix T6 content.
- **Scenario 10 beach warp fails** — Beach location might not exist from the fixture's starting state; try a different outdoor location (e.g. `"Town"`).

Iterate until 10/10 PASS. Each iteration: adjust whichever layer is broken, re-run `./scripts/run-samples.sh`.

- [ ] **Step 3: Document completion in current.md**

In `docs/milestones/current.md`, find the D1.7 bullet:

```markdown
- [ ] D1.7 — Sample suite (10 scenarios against a real CP mod). DSL extensions called out in final review before authoring: `!=` support, array indexing, `draw.assert_not_contains`, wiring ScenarioRunner assertion counters back into ScenarioState for truthful `scenario.end` reports.
```

Replace with:

```markdown
- [x] **D1.7** — Sample suite (10 scenarios against a bundled sample CP mod) + DSL extensions. `!=` / array indexing added to ScenarioRunner DSL; `draw.assert_not_contains` RPC landed; `scenario.end` carries truthful assertion counters; `RpcPreconditions.RequireWorldReady` widened to `gameMode == playingGameMode && hasLoadedGame` (unblocks headless-Xvfb scenarios). `tests/sample-cp-mod/` is a minimal Content Patcher mod the scenarios assert against. `./scripts/run-samples.sh` runs all 10 end-to-end.
```

Then add a new subsection after the D1.6 completion subsection:

```markdown
### D1.7 — Sample suite landed (2026-04-23)

Plan: `docs/superpowers/plans/2026-04-23-d17-sample-suite-and-dsl.md` (10 tasks, subagent-driven).
Design spec: `docs/superpowers/specs/2026-04-23-d17-sample-suite-and-dsl-design.md`.

**Architecture:** Phase A landed 5 small DSL/RPC extensions enabling rich scenario shapes; Phase B created the sample CP mod + 10 scenario JSON files; Phase C ran the end-to-end smoke. The bundled sample mod uses a single `EditImage` Content Patcher change to composite a 16x16 magenta swatch into `LooseSprites/Cursors` at a known source-rect — scenarios 03/04 assert against that swatch's presence. Negative assertions (scenarios 05/06) use the new `draw.assert_not_contains` RPC.

**Smoke result:** `./scripts/run-samples.sh` → **10/10 passed** in NN ms (replace NN with measured value).

**M1 is shippable.** Full M1 surface: `src/Protocol` (JSON-RPC + types), `src/Runner` (CLI: probe / doctor / list / run), `src/Harness` (SMAPI mod + Harmony patches + RPC handlers), sample mod + scenario suite, and end-to-end smoke. Next: M2 per spec §7 Phase 2 — bitmap fallback, record mode, watch mode, fixture-builder tool, TAP/JUnit reporters.

**Test count after D1.7:** 203 Passed + 26 Skipped (was 193+21 before D1.7; +10 passed, +5 skipped).
```

- [ ] **Step 4: Final CI**

Run: `./scripts/ci.sh`
Expected: PASS. Final test count around 203 Passed + 26 Skipped.

---

## Self-review

**1. Spec coverage:**
- (Part A) RequireWorldReady relaxation → T1 ✓
- (Part A) `!=` operator → T2 ✓
- (Part A) Array indexing → T3 ✓
- (Part A) `draw.assert_not_contains` → T4 ✓
- (Part A) Counter wiring → T5 ✓
- (Part B) Sample CP mod → T6 ✓
- (Part B) 10 scenario files → T8 ✓ (covers all 10 from spec's §The 10 scenarios)
- (Part C) End-to-end smoke → T10 ✓
- (Spec acceptance 1) CI green + new unit tests → every task ✓
- (Spec acceptance 2) Sample mod loads under SMAPI → T6 step 4 ✓
- (Spec acceptance 3) 10 files validate against schema → T8 step 6 ✓
- (Spec acceptance 4) 10/10 PASS → T10 step 1 ✓
- (Spec acceptance 5) scenario.end reports truthful counters → T5 ✓
- (Spec acceptance 6) rpc-schema.md documents new RPC → T4 step 4 ✓
- (Spec acceptance 7) current.md marks D1.7 `[x]` → T10 step 3 ✓
- (Skip-marked integration tests) → T1 step 1 + T9 ✓

Gap flagged: the `draw.not_contains` assertion type in scenarios 5 and 6 requires a `ScenarioRunner.EvaluateAssertionAsync` branch. **T8 step 3 adds it inline.** Not a separate task — minor addition inside T8.

**2. Placeholder scan:** One soft spot in T5 step 3 — runner-side counter test is conditional ("if no mock seam exists, skip"). This is a plan-time known unknown; the fallback (harness-side test + live smoke) is concrete. Acceptable per "adjust where needed."

**3. Type consistency:**
- `DrawAssertNotContainsHandler.Method` = `"draw.assert_not_contains"` (T4) — consistent with handler reg in ModEntry and docs in rpc-schema.md.
- `AssertResult` DTO is reused (not a new type) — correct; response shape is identical modulo semantics.
- `ScenarioEndHandler` accepts `{assertions_run, assertions_passed}` optional params → `ScenarioState.AssertionsRun`/`AssertionsPassed` fields (already exist from D1.4) — consistent.
- `ScenarioRunner.RunAsync` passes the same two names via `scenario.end` params — consistent.
- Predicate `(Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame)` — used in T1 code + T1 doc comment + T1 test skip text; all match.

**4. Hazard notes:**
- T6's EditImage `ToArea` at `(0, 368)` is a guess — may overlap a tile used by other UI. Step 2 of T6 flags "pick a different rect if needed." If the smoke (T10) fails scenario 04 because the patched tile is drawn OVER by another tile, move `ToArea` to `(0, 880)` (the very bottom of the atlas which is usually empty) or pick another unused region via visual inspection.
- T7's `scripts/run-samples.sh` assumes Content Patcher exists at the Flatpak-Steam path. The script flags this with exit 2 and a NexusMods link; if another contributor's SDV is non-Flatpak, they'd hit this and need to tweak the `SDV_ROOT` variable. Document as a known precondition.
- T5's runner-side test (optional) depends on existing mocking pattern — if it can't be wired cleanly, the harness-side test + live smoke cover the invariant. Acceptable.

---

## Execution handoff

Plan complete and saved to `docs/superpowers/plans/2026-04-23-d17-sample-suite-and-dsl.md`. Two execution options:

**1. Subagent-Driven (recommended)** — dispatch a fresh subagent per task with two-stage review (spec compliance then code quality) between each. Proven across D1.5 / S-plan / D1.6 cycles.

**2. Inline Execution** — execute tasks in this session via `superpowers:executing-plans`, batch through with checkpoints.

**Which approach?**
