# M3 — C# Fluent DSL Design

**Milestone:** M3 subproject 1 (per spec §7 Phase 3 — Ecosystem)
**Date:** 2026-04-24
**Author:** fintan + Claude (brainstorming session)
**Status:** Approved (auto mode) — ready for implementation-plan drafting

## Goal

Ship a typed C# DSL that lets modders author scenarios as plain xUnit tests in their mod's test project, instead of hand-authoring `*.test.json` files. The DSL wraps the existing JSON-RPC surface in ambient static classes (`Player`, `Time`, `World`, `Draw`, `State`, `Freeze`, `Fixture`, `Bitmap`, `Wait`) so a test reads close to spec Appendix A:

```csharp
[Collection("SDV")]
public class ShopMenuTests
{
    [Scenario(seed: 42, fixture: "m0spike_436515781")]
    public async Task ShopMenu_ShowsPierreInventory()
    {
        await Player.Warp("SeedShop", 4, 19);
        await Player.SetMoney(5000);
        await Time.Advance(60);

        await Draw.Arm();
        await Wait.Ms(500);
        await Freeze.Begin();

        var events = await Draw.Snapshot();
        Assert.Contains(events, e => e.TextureAsset == "LooseSprites/Cursors");

        var player = await State.Player();
        Assert.Equal(5000, player.Money);
    }
}
```

Cut C from brainstorming: keep the `[Scenario]` attribute + ambient static context that make the DSL feel "fluent," defer the parts with outsize complexity-to-value ratios (FluentAssertions `.Should()` integration, generic menu registry `Wait.ForMenu<ShopMenu>`, `World.InteractNpc` / `Time.Set` — which need RPCs we haven't implemented yet).

## Architecture

**One new source project + one new test project:**

- `src/Runner.Dsl/` (net10.0) — the DSL. References `src/Protocol/` (DTOs + `UnixSocketRpc` + `JsonRpcSession`) AND `src/Runner/` (for `SdvLauncher` + `HarnessDeployer`, which own the SDV-subprocess launch + harness-deploy flow). Pragmatic choice: reusing the launch flow avoids a 3-call-site refactor (RunCommand, RecordCommand, FixtureCommand all use these helpers), and M3 subproject 3 (NuGet package) can restructure the dependency later if the bundled footprint is a concern.
- `tests/Runner.Dsl.Tests/` (net10.0) — unit tests for the typed wrappers (via shim RPC seam) + one skipped live-SDV integration sample.

**Session model:** single `SdvTestSession` per test assembly, owned by a `[CollectionDefinition("SDV")]` collection fixture. Fixture starts SDV via the existing `SdvLauncher` + `UnixSocketRpc` pipeline (same boilerplate as `sdv-test run`'s `RunCommand`) once per `dotnet test` invocation. Tests in the same collection don't run in parallel under xUnit's default settings, so a static `SdvTestSession.Current` accessor is safe — no AsyncLocal gymnastics needed.

**`[Scenario]` attribute:** xUnit `BeforeAfterTestAttribute` subclass. Before test body: calls `scenario.begin` with the attribute's name/seed/fixture. After test body: calls `scenario.end` (even on test failure, via xUnit's finally semantics). No custom `FactDiscoverer` — standard xUnit extension point.

**Ambient statics:** `Player`, `Time`, `World`, etc. are static classes with async methods that delegate to `SdvTestSession.Current.Rpc.InvokeAsync("player.warp", ...)`. If `Current` is null at call time (i.e., no `[Scenario]`-wrapped test running), they throw `InvalidOperationException` with a clear message — "SdvTestSession not initialized; are you in a `[Collection(\"SDV\")]`-decorated class with a `[Scenario]` method?".

**Error handling:** RPC errors (non-null `resp.Error`) throw typed exceptions — `SdvGameStateInvalidException`, `SdvInvalidParamsException`, `SdvInternalErrorException`, or `SdvRpcException` (base) for codes not in the common set. Messages carry the RPC method name and the harness's error text. This turns "`draw.assert_contains` returned `GameStateInvalid`" into a normal xUnit exception trace with line numbers.

## Components

**New files (`src/Runner.Dsl/`):**

- `Runner.Dsl.csproj` — .NET 10, references `src/Protocol/Protocol.csproj` + `xunit` (for `BeforeAfterTestAttribute`).
- `SdvTestSession.cs` — owns the `JsonRpcSession`. Static `Current` accessor. Static `InitializeAsync(socketPath, ct)` / `DisposeAsync()` pair called by the collection fixture. Auto-wires the session's `InvokeAsync` into the ambient statics.
- `SdvFixture.cs` — the `[CollectionDefinition("SDV")]` collection fixture. Launches SDV (reusing `SdvLauncher` + `HarnessDeployer` — both need to be moved or re-exposed; see "Moves" below), awaits the `ready` notification, initializes `SdvTestSession.Current`. Teardown kills SDV.
- `ScenarioAttribute.cs` — `[Scenario(string? name = null, int seed = 42, string? fixture = null)]`. Subclass of `BeforeAfterTestAttribute`. Before: `scenario.begin`. After: `scenario.end`. If `name` omitted, uses the test method's name.
- `Exceptions.cs` — `SdvRpcException` base + `SdvGameStateInvalidException`, `SdvInvalidParamsException`, `SdvInternalErrorException` subclasses.
- Facets (one file each for focus):
  - `Player.cs` — `Warp`, `SetMoney`, `GiveItem`.
  - `Time.cs` — `Advance(int minutes)`. (`Set` deferred.)
  - `World.cs` — `SetWeather(string)`. (`InteractNpc` deferred.)
  - `Freeze.cs` — `Begin`, `End`, `Status`.
  - `Draw.cs` — `Arm`, `Disarm`, `Snapshot` returning `DrawEventDto[]`, `Find(DrawFilter filter)` returning filtered events, `AssertContains(DrawFilter, int minCount = 1)`, `AssertNotContains(DrawFilter)`.
  - `State.cs` — `Player() → PlayerState`, `Time() → TimeState`, `Location(string? name = null) → LocationState`, `Npc(string name) → NpcState`, `Menu() → MenuState`, `Mods() → ModsState`.
  - `Fixture.cs` — `Load(string name)`.
  - `Bitmap.cs` — `Capture(BitmapRegion? region = null) → BitmapCaptureResult`. `BitmapRegion` is a small record `(int X, int Y, int W, int H)` with a `ToJsonElement()` helper.
  - `Wait.cs` — `Ms(int ms)` — client-side `Task.Delay`. (Harness-side `wait.for_*` primitives deferred.)

**New files (`tests/Runner.Dsl.Tests/`):**

- `Runner.Dsl.Tests.csproj` — xUnit test project, references `src/Runner.Dsl/`.
- `DslRpcShimTests.cs` — ~6-8 tests. For each facet, a synthetic JsonRpcSession shim captures the outgoing method + params and returns a canned response. Verifies the typed wrapper produces the right RPC call and correctly deserializes the response.
- `ScenarioAttributeTests.cs` — 2 tests. Attribute's `Before` calls `scenario.begin` with the right params; `After` calls `scenario.end`. Shim out `SdvTestSession.Current` via a test-only `InternalsVisibleTo` setter.
- `ExceptionsTests.cs` — 1-2 tests. RPC error payload → correct typed exception subclass.
- `DslIntegrationTests.cs` — 1 skipped `[Fact(Skip=...)]` placeholder for a live-SDV round-trip. Covered manually by the worked example below.

**Worked example (committed but skipped or run manually):**

- `tests/Runner.Dsl.Tests/Worked/ShopMenuDslSmoke.cs` — ONE realistic test showing the DSL end-to-end. `[Collection("SDV")] [Scenario(fixture: "m0spike_436515781")]` wraps an await-chain of `Player.Warp → Time.Advance → Draw.Arm → Wait.Ms → Freeze.Begin → Draw.Snapshot → Assert`. Marked `[Fact(Skip=...)]` by default so CI doesn't need live SDV; runnable via `dotnet test --filter ShopMenuDsl` when a developer wants to verify.

**New docs:**

- `docs/dsl-quickstart.md` — getting-started doc. ~60 lines. Explains `[Collection("SDV")]` + `[Scenario]` + shows the worked example. Linked from README (M4) and spec Appendix A (update the sketch to point at the real DSL).

**Modified files:**

- CI script (`scripts/ci.sh`) — ensure new projects build + test.
- `docs/milestones/current.md` — add M3-DSL completion subsection.

**Target test count:** 266+34 → ~280+35 (+14 passing, +1 skipped):
- Facet wrappers: ~8 passing (one per facet, parametrized where cheap)
- Scenario attribute: 2 passing
- Exceptions: 2 passing
- Session lifecycle: 2 passing
- Integration placeholder: +1 skipped

## API shape

### `[Scenario]` attribute

```csharp
[Scenario(name: "shop_menu_default", seed: 42, fixture: "m0spike_436515781")]
public async Task MyTest() { ... }

// All args optional; defaults:
//   name    = test method name (via xUnit's MethodInfo reflection; fall back to attribute null-default)
//   seed    = 42 (matches sample suite convention)
//   fixture = null (no fixture.load; scenario starts at whatever state SDV is in)
[Scenario]
public async Task MyOtherTest() { ... }
```

### Facet methods (representative subset)

```csharp
// Player
await Player.Warp("SeedShop", 4, 19);
await Player.SetMoney(5000);
await Player.GiveItem("(O)74", count: 1);    // "(O)74" = prismatic shard

// Time
await Time.Advance(60);    // minutes

// World
await World.SetWeather("rain");

// Draw
await Draw.Arm();
await Wait.Ms(500);
var events = await Draw.Snapshot();                 // DrawEventDto[]
var filtered = await Draw.Find(new DrawFilter { TextureAsset = "LooseSprites/Cursors" });
await Draw.AssertContains(new DrawFilter { TextureAsset = "..." }, minCount: 1);
await Draw.AssertNotContains(new DrawFilter { TextureAsset = "Mods/Unwanted" });

// State (reads)
var p = await State.Player();                  // PlayerState
var loc = await State.Location();              // current
var pierre = await State.Npc("Pierre");        // NpcState
var menu = await State.Menu();                 // MenuState
var mods = await State.Mods();                 // ModsState

// Freeze
await Freeze.Begin();
var status = await Freeze.Status();
await Freeze.End();

// Fixture
await Fixture.Load("m0spike_436515781");

// Bitmap
var cap = await Bitmap.Capture();
var region = new BitmapRegion(0, 0, 640, 480);
var cropped = await Bitmap.Capture(region);

// Wait (client-side sleep — game keeps ticking)
await Wait.Ms(500);
```

### Error handling

```csharp
try
{
    await Freeze.Begin();    // throws if no scenario active
}
catch (SdvGameStateInvalidException ex)
{
    // ex.Message: "freeze.begin requires an active scenario (call scenario.begin first)"
    // ex.Method:  "freeze.begin"
    // ex.Code:    JsonRpcErrorCode.GameStateInvalid
}
```

### Collection fixture setup (user boilerplate — minimal)

```csharp
[CollectionDefinition("SDV")]
public class SdvCollection : ICollectionFixture<SdvFixture> { }

[Collection("SDV")]
public class MyTests
{
    [Scenario(fixture: "m0spike_436515781")]
    public async Task Test1() { ... }
}
```

Users write that 2-line `SdvCollection` declaration once per test assembly and forget about it. The `SdvFixture` class lives in our DSL package — users just reference it.

## Error handling (framework-side)

- **Session not initialized** — any ambient-static call when `SdvTestSession.Current == null` throws `InvalidOperationException` with a message directing the user to `[Collection("SDV")]`.
- **RPC transport error** (socket closed mid-call) — `SdvRpcException` with inner `IOException`. Message: "RPC call <method> failed: <inner>."
- **RPC protocol error** (`resp.Error != null`) — typed exception per error code. `GameStateInvalid` → `SdvGameStateInvalidException`; `InvalidParams` → `SdvInvalidParamsException`; `InternalError` → `SdvInternalErrorException`; other → `SdvRpcException` carrying the raw `JsonRpcError`.
- **Attribute misuse** (`[Scenario]` on a non-async method, or on a method in a class without `[Collection("SDV")]`) — detected at `Before` time; throws with a message explaining the fix.
- **SDV launch failure** (fixture startup) — `SdvFixture` throws; xUnit reports every test in the collection as errored with the same message. User gets a clear "SDV failed to start: <stderr excerpt>" instead of per-test cascading NRE.
- **Fixture-load timeout** (headless Xvfb timing issue) — reuses the existing `ScenarioRunner.WaitForWorldReady` 30s timeout path after `Fixture.Load`; user can pass `waitForReady: false` to skip.

## Testing

**Unit tests (~13-14 new passing):**

- Each facet gets a test using a shim `IJsonRpcInvoker` (a small interface we introduce) or `InternalsVisibleTo` access to seam the outbound call. Verify:
  - Method name is right.
  - Params JSON matches expected shape.
  - Response deserializes into the typed result.
- `[Scenario]` attribute tests verify `Before` emits `scenario.begin` with correct params; `After` emits `scenario.end`.
- Exception tests verify error-code-to-exception-subclass mapping.
- Session-lifecycle tests verify `Current` is null before/after Initialize/Dispose, populated in between.

**Skipped integration (1):**

- `DslIntegrationTests.DslSession_RoundTrip` placeholder, exercised by the worked-example smoke.

**Worked-example smoke (manual, committed as skipped):**

- `ShopMenuDslSmoke.MenuOpens_DrawsCursors` — decorated with `[Fact(Skip="Requires live SDV")]` so CI stays green, but runnable manually with `dotnet test tests/Runner.Dsl.Tests/ --filter Worked` to prove the DSL works end-to-end against a real SDV instance. Same fixture + socket setup as the sample suite.

## Acceptance criteria

1. `./scripts/ci.sh` green at ~280 Passed + 35 Skipped.
2. `src/Runner.Dsl/` + `tests/Runner.Dsl.Tests/` exist as first-class projects in the solution + CI script picks them up.
3. All 9 facets (`Player`, `Time`, `World`, `Freeze`, `Draw`, `State`, `Fixture`, `Bitmap`, `Wait`) expose the RPC surface listed in the API shape section.
4. `[Scenario]` attribute correctly wraps a test in `scenario.begin`/`scenario.end` (unit-verified via shim).
5. `SdvFixture` + `[CollectionDefinition("SDV")]` pattern works — a dev can write a test class using it and run `dotnet test --filter Worked` against live SDV to see it execute (manual verification).
6. Typed exceptions propagate cleanly: a failing `await Freeze.Begin()` without `scenario.begin` throws `SdvGameStateInvalidException` with a helpful message (unit-verified).
7. `docs/dsl-quickstart.md` exists + shows the pattern end-to-end.
8. Sample suite (`./scripts/run-samples.sh`) still 11/11 PASS (no regression — DSL adds but doesn't modify existing Runner code paths).
9. `docs/milestones/current.md` gains an M3-DSL subsection.

## Out of scope (TODO for M3 follow-ups or M4)

- **FluentAssertions `.Should()` integration** — adds a NuGet; users can bring their own FluentAssertions package and use `events.Should().Contain(...)` directly on the DSL's return types without integration work.
- **Generic menu registry** — `Wait.ForMenu<ShopMenu>(TimeSpan)` requires a compile-time map from SDV menu types to detection predicates. Deferred; `Wait.Ms(500) + State.Menu()` covers the case with 2 extra lines.
- **`World.InteractNpc` + `Time.Set`** — need new RPCs (`world.interact_npc`, `time.set_date`) on the harness side. Separate subprojects.
- **Custom xUnit `FactDiscoverer`** — auto-finding `[Scenario]` methods without needing `[Fact]` too. `[Scenario]` currently is a `BeforeAfterTestAttribute`, so users still need `[Fact]` on the method. A future `[ScenarioFact]` attribute that combines both is trivial; a full discoverer is M4.
- **MSBuild / `<Sdk>`-based** installation — users currently add a `<ProjectReference>` or (M3.3) a `<PackageReference>`. A "just `<Sdk>`-import our NuGet and it wires up the collection fixture" experience is M4.
- **Multiple-SDV-subprocess testing** — one session per assembly is the MVP. Running scenarios in parallel across multiple SDV processes is M4.
- **Time extension methods** — `2.Seconds()` per spec Appendix A. Trivial to add but not used by any DSL method in this cut (we take `int minutes` and `int ms` directly).
- **Typed `DrawFilter` builder** — users pass a `DrawFilter` object today. A fluent builder (`DrawFilter.Where(x => x.TextureAsset == "...")`) is polish.

## Links

- Spec: `docs/spec.md` §7 Phase 3 (Ecosystem), Appendix A (C# DSL sketch)
- Brainstorm: 2026-04-24 auto-mode session (this doc)
- M3 tracker: `docs/milestones/current.md` §M3 — Ecosystem (to be added)
- Prior M2 subprojects (implementation patterns): fixture builder, reporters, watch mode, record mode, bitmap fallback
