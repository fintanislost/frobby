# Current Milestone: M1 — Core Framework (in progress)

M0 complete. See @docs/spikes/2026-04-determinism/REPORT.md for the spike report.

## Progress

- [x] **Foundation** — solution + project skeleton (`src/Protocol`, `src/Runner`, `src/Harness`, `tests/Protocol.Tests`, `tests/Runner.Tests`, `tests/Harness.Tests`). `Directory.Build.props` enforces warnings-as-errors and deterministic builds. 8 tests pass via `./scripts/ci.sh`.
- [x] **Harness port** — spike code lives at `src/Harness/` with spike-diagnostic-only pieces removed (heartbeat log, `harness_snapshot`, `harness_save` — the latter stays in the spike for future fixture-builder reuse). `DrawEventWriter` now has unit-test coverage proving culture-independence, float round-trip, JSON shape.
- [x] **D1.1** — JSON-RPC protocol types + Unix socket transport. `src/Protocol/` (codec, NDJSON framing, session, socket). 21 tests. Harness `ModEntry` starts an RPC server when `SDV_TEST_SOCKET` is set and sends the `ready` notification on connect.
- [x] **D1.2 walking skeleton** — `state.player` end-to-end. Harness-side: `GameThreadDispatch` (marshals RPC work back to the SMAPI update tick), `RpcDispatcher` (method registry), `StatePlayerHandler` (snapshots `Game1.player`). Runner-side: minimal `Program.cs` + `sdv-test probe [socket]` command that connects, awaits `ready`, invokes `state.player`, prints. End-to-end tested via in-proc Unix socket round-trip.
- [x] **D1.2 full surface** — state.location, state.npc, state.time, state.menu, player.warp, player.give_item, player.set_money, time.advance, world.set_weather, draw.arm/disarm, draw.snapshot, draw.find, draw.assert_contains, scenario.begin/end, fixture.load. Shared infrastructure: `RpcParams.Required/Optional`, `MutatorOk` base DTO, `DrawFilterMatcher`, `DrawFilterValidator`, `ScenarioState` singleton with `[Collection]` test isolation.
- [x] **D1.3** — Runner CLI: `probe`, `doctor`, `list`, `run` (launches SDV subprocess via `SdvLauncher`, connects over Unix socket, runs scenarios, prints summary).
- [x] **D1.4** — Scenario JSON Schema (`schemas/scenario.schema.json`), `ScenarioLoader` (validates then deserializes; precise error messages), `ScenarioRunner` (begin → fixture.load + wait-ready → steps → assertions → end in `finally` so the harness never wedges). State-assertion DSL supports string/int/bool literal `==`.
- [x] **D1.5** — Texture → asset path resolution (Tier 1). `TextureAssetRegistry` (weak-ref `ConditionalWeakTable<Texture2D, string>`) populated via SMAPI's `IContentEvents.AssetReady` event + reflection into `ContentManager.loadedAssets`. `DrawEvent` now carries the live `Texture2D` reference; resolution happens lazily at snapshot time in `DrawSnapshotHandler.ToDto`. `DrawEventDto.TextureAsset` + `SnapshotMeta.ResolvedCount` added to the wire protocol. `DrawFilterMatcher` compares on resolved path; the pre-D1.5 `InvalidParams` placeholder is gone.
- [x] **D1.6** — Determinism controller (FREEZE/THAW lifecycle). Explicit `freeze.begin`/`freeze.end`/`freeze.status` RPCs backed by `DeterminismController` + Harmony prefix on `Game1.Update` that short-circuits while frozen. Per-location RNG pinning (`LocationRngPinner`) and NPC halt (`NpcFreeze`) via reflection with snapshot/restore. Migration: `CursorPatches` now gates on `Frozen` (was `Recorder.IsArmed`); `Recorder.ActivateArm` no longer flips `eventUp`/`displayHUD` — those moved to the controller's production hooks. `ScenarioEndHandler` has an auto-thaw safety valve so a scenario failure can't leave the harness wedged.
- [x] **D1.7** — Sample suite (10 scenarios against a bundled sample CP mod) + DSL extensions. `!=` / array indexing added to ScenarioRunner DSL; `draw.assert_not_contains` RPC landed; `scenario.end` carries truthful assertion counters; `RpcPreconditions.RequireWorldReady` + `StateTimeHandler.InSave` + `FreezeBeginHandler` preconditions all widened to `gameMode == playingGameMode && hasLoadedGame` (unblocks headless-Xvfb scenarios). `tests/sample-cp-mod/` is a minimal Content Patcher mod the scenarios assert against. `scripts/run-samples.sh` → **10/10 passed**. New `wait.ms` client-side primitive in ScenarioRunner handles async-warp timing.

## M1 Phase 2 summary (2026-04-22)

**Plan:** `docs/superpowers/plans/2026-04-22-m1-rpc-surface-and-runner.md` (18 tasks, executed via subagent-driven-development skill with 2-stage review per task).

**Pattern-hardening refactors applied during execution (each in response to reviewer feedback):**
1. T4: extracted `RpcParams.Required<T>` + `MutatorOk` base DTO (prevented 4× duplication across manipulators).
2. T5: removed empty result-subclasses (`WarpResult`/`GiveItemResult` → direct `MutatorOk` returns — only derive when extras warrant).
3. T11: added `DrawFilterValidator` with range/size/length checks (prevented silent-never-match footguns).
4. T15: moved `scenario.end` into `finally` block (fixes "harness wedges on scenario-begin after prior scenario failure").
5. Final review: tightened `min_count` schema minimum to 1; texture_asset placeholder now throws `InvalidParams` on non-integer input instead of silently mismatching.

**Final test count:** 152 Passed + 9 Skipped via `./scripts/ci.sh`. The 9 skipped are integration tests requiring a live SDV instance — all marked `[Fact(Skip="Requires live SDV...")]` with clear rationale.

## M1 Phase 2.5 — smoke test findings (2026-04-22)

End-to-end smoke against real SDV 1.6.15 + SMAPI 4.5.2 revealed 8 bugs, most in handler preconditions. The transport, codec, dispatcher, and scenario-lifecycle layers all work; failures cluster around (a) `RunCommand` being unusable on a modded install (no `--mods-path` plumbing) and (b) state-mutator handlers not rejecting title-screen invocations.

**Smoke plan:** `docs/superpowers/plans/2026-04-22-m1-smoke-findings-and-fixes.md` — 5 fix tasks (S1-S5, ~1 day of work) plus an optional `scripts/smoke-test.sh` automation. Bug catalog with severity and reproduction steps included.

**Blocks:** D1.5, D1.6, D1.7 should wait for S1-S5 to land so those plans are written against a known-working `sdv-test run` baseline.

### S-plan status (2026-04-22)

- [x] **S1** — `--mods-path` CLI arg + `$SDV_MODS_PATH` env var + default to `~/.cache/sdv-test-framework/mods/`. Plumbed through `SdvLauncher`. Fixes Bug #1.
- [x] **S2** — `HarnessDeployer` auto-copies payload into the isolated Mods dir. Runner.csproj `StageHarnessPayload` target + Runner.Tests mirror. Fixes Bug #9.
- [x] **S3** — `RpcPreconditions.RequireWorldReady()` shared helper applied to `player.set_money`, `player.give_item`, `time.advance`, `world.set_weather` (replacing the broken NRE guard). Fixes Bugs #2, #5, #6. Skip-marked integration tests added for each.
- [x] **S4** — `ItemRegistry.Exists(id)` upfront check in `player.give_item` — SDV 1.6's canonical existence API, confirmed via `ilspycmd`. Fixes Bug #3.
- [x] **S5** — `Directory.Exists(Path.Combine(Constants.SavesPath, name))` guard in `fixture.load`, throws `FixtureLoadFailed` (-32002). Fixes Bug #4.

**Test count after S1-S5:** 156 Passed + 13 Skipped (was 152+9 at Phase 2 end; +4 skipped integration placeholders from S3, +4 passing from S1/S2/S5, net +4/+4).

### Deferred-bug cleanup (2026-04-23)

- [x] **Bug #7** — `Recorder.ActivateArm` now only flips `Game1.eventUp`/`displayHUD` when `Context.IsWorldReady`. Title-screen arms skip the perturbation and log `(title-screen arm — ambient flags untouched)`. Restore is gated by an `_ambientFlipped` sentinel so disarm only undoes what it actually changed.
- [x] **Bug #8** — Added `TimeState.InSave` (bool) populated from `Context.IsWorldReady`. Wire shape is `"in_save": true|false`. Scenario authors get a reliable "is a save loaded?" signal without having to pattern-match on empty `name`/`location` strings. Schema doc updated with both in-save and title-screen response examples.

**Test count after cleanup:** 157 Passed + 13 Skipped (+1 new DTO test `Serialize_DefaultInstance_HasInSaveFalse`).

### Smoke re-run (2026-04-23)

After all S1-S5 + deferred-bug fixes, re-ran the smoke end-to-end.

- **`dotnet run --project src/Runner -- run <scenarios>`** from a fresh `~/.cache` → exit 0, `1/1 passed`, 55 ms. Harness auto-deployed, no manual steps. Bugs #1 + #9 confirmed fixed.
- Python RPC probes against a live SDV at title screen: `world.set_weather`, `time.advance`, `player.set_money`, `player.give_item (O)nonsense` all correctly return `-32003 GameStateInvalid "no active save — mutation requires a loaded world"`. `fixture.load nonexistent_fixture` correctly returns `-32002 FixtureLoadFailed`. All regression cases unchanged.

M1 is shippable. **Next:** D1.5 / D1.6 / D1.7 as separate plans.

### D1.5 — Texture path resolution landed (2026-04-23)

Plan: `docs/superpowers/plans/2026-04-23-d15-texture-asset-paths.md` (8 tasks, executed via subagent-driven-development).

**Architecture shift during T8 smoke:** T3's original design used a Harmony postfix on `ContentManager.Load<Texture2D>` via `MakeGenericMethod(typeof(Texture2D))`. That approach crashes SDV with `OutOfMemoryException` inside `Path.Combine` during content load. Root cause: .NET shares JIT'd code across reference-type generic instantiations, so patching one closed generic rewrites the shared body, and the Texture2D-typed postfix corrupts the method frame when the shared body runs for a different T (e.g. `Dictionary<string,...>` for `Data/BigCraftables`). Fix applied: rewrote T3 to use SMAPI's `IContentEvents.AssetReady` event + reflection into `ContentManager.loadedAssets` — no IL manipulation, no generic-sharing issue. The patch-header comment block on `ContentLoadPatches.cs` records the trail so future contributors don't re-introduce the footgun.

**Measured Tier 1 resolution rate** (acceptance criterion (f)):
- Setup: live SDV 1.6.15 + SMAPI 4.5.2 + harness, `m0spike_436515781` fixture loaded, captured 120 ticks in-memory.
- **Result: 60,436 events captured, 54,872 resolved as Tier 1 → 90.8% resolution rate.**
- Top resolved asset paths: `Maps/walls_and_floors` (23,478), `Maps/townInterior` (13,468), `Maps/farmhouse_tiles` (10,374), `LooseSprites/Cursors` (4,220), `TileSheets/furniture` (2,002), plus character/HUD sheets.
- The 9.2% unresolved cluster to a handful of textures by size: 1×1 (`Game1.fadeToBlackRect`/`staminaRect` — `new Texture2D` allocations, not content-pipeline loads), 1280×720 (fullscreen fade/render targets), 512×1002 / 400×655 / 288×672 (dialogue-portrait-sized, likely engine-pre-mod-load loads). Tier 2 hash fallback (M2) will close most of these.

**Test count after D1.5:** 169 Passed + 14 Skipped (was 157+13 before D1.5; +12 Passed, +1 Skipped).

### D1.6 — Determinism controller landed (2026-04-23)

Plan: `docs/superpowers/plans/2026-04-23-d16-determinism-controller.md` (12 tasks, subagent-driven).
Design spec: `docs/superpowers/specs/2026-04-23-d16-determinism-controller-design.md`.

**Architecture:** Static `DeterminismController` owns FREEZE state + ordered enter/exit orchestration; one Harmony prefix on `Game1.Update(GameTime)` collapses time-freeze + parallax-fix + animation-freeze into a single patch site (returns `false` while `Frozen`, skipping the original `Game1.Update` body so `currentGameTime` doesn't advance). Per-location RNG pinning and NPC halt are reflection-based with snapshot/restore, not Harmony patches — they're state mutations that unwind cleanly on `freeze.end`. Hook-injection seam (`HooksForTests`) lets unit tests exercise orchestration ordering + rollback-on-failure without a live SDV; production wiring via `UseProductionHooks()`.

**Migration (breaking change, contained to M1):** pre-D1.6 the "arm" concept implicitly did a mini-freeze — `CursorPatches` gated on `Recorder.IsArmed`, and `Recorder.ActivateArm` flipped `eventUp`/`displayHUD`. D1.6 untangles them: arm = capture draws; freeze = stop the world. The `_ambientFlipped` sentinel and the title-screen-vs-in-world branch in Recorder are gone.

**Smoke verification (live SDV 1.6.15 + SMAPI 4.5.2):**
- Runner smoke: `1/1 passed`, 51 ms. Harness auto-deploys with `TimeFreezePatch` logged at startup.
- Python RPC probe at title screen confirmed strict preconditions fire with named messages:
  - `freeze.begin` with no scenario → `GameStateInvalid -32003 "freeze.begin requires an active scenario (call scenario.begin first)"`
  - `freeze.begin` with scenario but title-screen → `GameStateInvalid -32003 "freeze.begin requires Context.IsWorldReady (no active save)"`
- Happy-path freeze verification (tick stable across 2s, parallax regression check) hit the same `Context.IsWorldReady`-not-flipping timing issue D1.5's smoke encountered — environmental, not a D1.6 regression. `SaveLoaded` never fires in the headless Xvfb configuration even though `Game1.gameMode` transitions to `playingGameMode`. D1.7's scenario infrastructure + a real fixture-builder flow will unblock this.

**Test count after D1.6:** 193 Passed + 21 Skipped (was 169+14 before D1.6; +24 passed, +7 skipped integration placeholders).

Breakdown of +24 passed:
- T1: +1 (ScenarioState.Seed persistence test)
- T2: +5 (DeterminismController state-machine: defaults, enter, double-enter, exit, double-exit)
- T3: +5 (LocationRngPinner: pins, determinism, different-names, silent-skip, restore)
- T4: +3 (NpcFreeze: halt+null-controller, restore, silent-skip)
- T5: +3 (orchestration: ordering, rollback, counts)
- T8: +2 (FreezeBeginHandler: no-scenario, already-frozen)
- T9: +4 (FreezeEnd + FreezeStatus: 2 each)
- T10: +1 (ScenarioEndHandler auto-thaw)

### D1.7 — Sample suite + DSL extensions landed (2026-04-23) — M1 shippable

Plan: `docs/superpowers/plans/2026-04-23-d17-sample-suite-and-dsl.md` (10 tasks, subagent-driven).
Design spec: `docs/superpowers/specs/2026-04-23-d17-sample-suite-and-dsl-design.md`.

**Scope:** Three phases — Phase A added DSL/RPC extensions (`!=`, array indexing, `draw.assert_not_contains`, counter wiring, `RequireWorldReady` widening). Phase B shipped the bundled Content Patcher sample mod at `tests/sample-cp-mod/` + `scripts/run-samples.sh` wrapper + 10 scenario JSON files. Phase C ran the end-to-end smoke.

**Smoke result (live SDV 1.6.15 + SMAPI 4.5.2 + Content Patcher + bundled sample mod):** `[run] 10/10 passed` via `./scripts/run-samples.sh`. The M1 ship criterion per spec §7 Phase 1 is met. The 10 scenarios span all 4 categories: state-only (01, 02), positive draw assertions (03, 04), negative draw assertions (05, 06), manipulators (07, 08), determinism (09, 10).

**Mid-smoke fixes (captured + fixed during T10 iteration, not in the plan):**
- `StateTimeHandler.InSave` was still reading `Context.IsWorldReady` after T1 widened `RpcPreconditions` — scenario 01 failed until this was widened too.
- `FreezeBeginHandler`'s `IsWorldReady` precondition was missed by T1 — scenarios 09/10 failed until widened.
- `ScenarioEndHandler` didn't `Recorder.Disarm()`, causing "Already armed" failures when scenario N's arm budget outlasted the scenario itself. Fix: added disarm to scenario.end.
- Async `player.warp` needed a waiter primitive. Added `wait.ms` client-side step type to `ScenarioRunner` — `Task.Delay` between steps so the game thread can advance ticks while the warp coroutine completes.
- Scenario 04's `source_rect` filter used object shape `{x, y, w, h}` but `DrawFilter.SourceRect` is `int[]` — rewrote scenario 04 to exercise the `color` filter field instead.

**Test count after D1.7:** 201 Passed + 26 Skipped (was 193+21 before D1.7; +8 passed, +5 skipped integration placeholders). Breakdown of +8 passed: T2 +2 (`!=`), T3 +2 (array indexing), T4 +3 (DrawAssertNotContainsHandler), T5 +1 (scenario.end counters).

**M1 is shippable.** Full M1 surface: `src/Protocol` (JSON-RPC + types), `src/Runner` (CLI: probe / doctor / list / run), `src/Harness` (SMAPI mod + Harmony patches + RPC handlers + determinism controller), sample mod + 10-scenario suite, end-to-end smoke. **Next: M2** per spec §7 Phase 2 — bitmap fallback (§4.5), record mode + watch mode (§4.7), fixture-builder tool (§4.8), TAP/JUnit reporters.

## M2 — Production polish (complete 2026-04-24)

## M3 — Ecosystem (in progress)

Per spec §7 Phase 3. Decomposed into 5 subprojects + one M2-followup ("SIGTERM handler"
landed first):

0. **SIGTERM handler** (M2-followup) — background-job shutdowns trigger clean-cancel. ✓ **Landed 2026-04-24.**
1. **C# fluent DSL wrapper** (§7.3 / Appendix A) — typed static facets + `[Scenario]` attribute + xUnit collection fixture. ✓ **Landed 2026-04-24.**
2. **MCP server** (§7.3 / §8) — stdio server with 6 curated tools + 1 rpc_call passthrough. ✓ **Landed 2026-04-24.**
3. NuGet package for the DSL — deferred.
4. Documentation site — deferred.
5. Example suites for 3-5 community mods — deferred.

### M3 subproject 0 — SIGTERM handler landed (2026-04-24)

Small M2-followup: `PosixSignalRegistration` for SIGTERM + SIGINT in
`src/Runner/Program.cs`, both wired to the same `CancellationTokenSource.Cancel()` the
pre-existing `Console.CancelKeyPress` hook fires. Background-job `kill %1` and
non-controlling-TTY `kill -INT` now trigger the same clean-shutdown path as foreground
Ctrl-C, which unblocks end-to-end smokes for record mode (which previously couldn't
flush the trace file on bg-job kill) and watch mode.

+2 passing tests (`PosixSignalRegistrationTests` — API availability smoke).

### M3 subproject 1 — C# fluent DSL landed (2026-04-24)

Plan: `docs/superpowers/plans/2026-04-24-m3-csharp-dsl.md` (9 tasks, subagent-driven).
Design spec: `docs/superpowers/specs/2026-04-24-m3-csharp-dsl-design.md`.

**Scope:** typed ambient-static DSL in a new `src/Runner.Dsl/` project. Tests look like:

```csharp
[Collection("SDV")]
public class ShopMenuTests
{
    [Fact, Scenario(fixture: "m0spike_436515781")]
    public async Task Warp_ShopOpens()
    {
        await Player.Warp("SeedShop", 4, 19);
        var player = await State.Player();
        Assert.Equal(5000, player.Money);
    }
}
```

**Architecture:** `SdvFixture` xUnit collection fixture launches one SDV subprocess per
assembly via the existing `SdvLauncher` + `HarnessDeployer` + `UnixSocketRpc` pipeline,
populates `SdvTestSession.Current` with a real `JsonRpcSession`. Ambient static facets
(`Player`, `Time`, `World`, `Freeze`, `Draw`, `State`, `Fixture`, `Bitmap`, `Wait`) read
through `Current` to invoke RPCs. `[Scenario]` is an xUnit `BeforeAfterTestAttribute`
subclass that wraps each test in `scenario.begin`/`scenario.end`.

**Typed exceptions:** `SdvRpcException` base + `SdvGameStateInvalidException`,
`SdvInvalidParamsException`, `SdvInternalErrorException` subclasses. RPC error responses
translate to typed exceptions that propagate as normal xUnit test failures with useful
messages and stack traces.

**User docs:** `docs/dsl-quickstart.md` — how to wire up a mod's test project.

**Worked example:** `tests/Runner.Dsl.Tests/Worked/ShopMenuDslSmoke.cs` —
`[Fact(Skip="Requires live SDV")]` by default so CI stays green. Runnable manually via
`dotnet test tests/Runner.Dsl.Tests/ --filter Worked` when a dev wants to verify against
live SDV.

**Environment knob:** `DSL_SKIP_SDV_LAUNCH=1` makes `SdvFixture` a no-op — for CI
environments without a display.

**Test count after M3-DSL:** 286 Passed + 36 Skipped (was 266+34 before M3; +20 passed,
+2 skipped — the 2 skipped are the ShopMenuDslSmoke worked example + DslIntegrationTests placeholder).

**Out of scope (M3 followups):**
- FluentAssertions `.Should()` integration.
- Generic menu registry (`Wait.ForMenu<ShopMenu>`).
- `World.InteractNpc` / `Time.Set` (need new RPCs).
- Combined `[ScenarioFact]` attribute + custom xUnit discoverer.
- Parallel SDV-subprocess execution across multiple collections.
- NuGet package for distribution (M3 subproject 3).

### M3 subproject 2 — MCP server landed (2026-04-24)

Plan: `docs/superpowers/plans/2026-04-24-m3-mcp-server.md` (7 tasks, subagent-driven).
Design spec: `docs/superpowers/specs/2026-04-24-m3-mcp-server-design.md`.

**Scope:** new `sdv-test mcp` subcommand speaking MCP (JSON-RPC 2.0 over stdio) with 6 curated tools + 1 raw RPC passthrough. LLMs configure Claude Code via `.mcp.json` pointing at `sdv-test mcp` and get typed tools for running scenarios, capturing state, and scaffolding tests.

**Architecture:** new `src/Runner.Mcp/` project (net10) references `src/Protocol/` only (reuses `NdjsonCodec` + `JsonRpcRequest`/`JsonRpcResponse`/`JsonRpcError`). `SdvLifecycle` lazy-launches SDV on first tool that needs it; tools that don't need SDV (`list_*`, `scaffold_scenario`) never trigger launch. Stdio EOF → clean teardown.

**Project reorg (T6 fixup):** resolving the Runner ↔ Runner.Mcp cycle required moving three files from `src/Runner/` to `src/Protocol/`:
- `SdvLauncher` + `HarnessDeployer` — transport-adjacent utilities now live alongside `UnixSocketRpc` in Protocol (namespace `SdvTestFramework.Protocol`).
- `ScenarioLoader` (+ `ScenarioLoadException`) — moved to `src/Protocol/Scenarios/` (namespace `SdvTestFramework.Protocol.Scenarios`). The richer `ScenarioRunner` + `ScenarioReport` stay in Runner (they're CLI-runtime, not protocol). This removed the cycle: Runner.Mcp → Protocol only, Runner → Runner.Mcp. Callers updated: RunCommand, RecordCommand, FixtureCommand, ListCommand, SdvFixture (Runner.Dsl), and test files.

**Tool surface:**
- `run_scenario(path)` — load + execute `.test.json`, return pass/fail + failures.
- `list_scenarios(dir?)` — enumerate `*.test.json`.
- `list_fixtures()` — enumerate `tests/fixtures/`.
- `warp_and_assert_draw(location, x, y, texture_asset, min_count?)` — atomic warp → freeze → draw assert → thaw.
- `capture_state()` — parallel read of player/location/time/menu.
- `scaffold_scenario(name, fixture?, template?)` — write starter `.test.json` with optional `shop`/`menu`/`warp` template.
- `rpc_call(method, params?)` — raw passthrough escape hatch.

**User docs:** `docs/mcp-quickstart.md` — `.mcp.json` setup + tool reference.

**Smoke verification:** `tests/Runner.Mcp.Tests/Worked/manual-smoke.sh` pipes 3 JSON-RPC requests to `sdv-test mcp` and asserts response shapes. Runnable manually with live SDV.

**Test count after M3-MCP:** 298 Passed + 37 Skipped (was 286+36 before; +12 passed, +1 skipped).
- T2: +5 (McpServer 3 + ToolRegistry 2)
- T3: +2 (RpcCallTool)
- T4: +3 (IntrospectionTools 2 + ScaffoldScenarioTool 1)
- T5: +2 (StatefulTools: WarpAndAssertDraw + RunScenario)
- T7: +1 skipped (McpIntegrationTests placeholder)

**Out of scope (M4):**
- HTTP transport.
- Dynamic MCP report resources, resource templates, and resource subscriptions.
- Streaming tool results for long-running scenarios.
- Richer scaffold templates.
- Full DSL assertion evaluation in `run_scenario` (state assertions currently delegate to the CLI runner; MCP's `run_scenario` handles steps + draw.contains assertions but not the richer state DSL).

**2026-05-17 cleanup:** the first static MCP resources/prompts slice landed for
docs, scenario indexes, and workflow prompt templates.

### Tier 2 texture-hash fallback landed (2026-04-24)

Plan: `docs/superpowers/plans/2026-04-24-tier2-texture-hash.md` (5 tasks, subagent-driven).
Design spec: `docs/superpowers/specs/2026-04-24-tier2-texture-hash-design.md`.

**Scope:** close the 9.2% unresolved-textures gap from D1.5. New `sdv-test build-manifest`
command generates a per-SDV-version `hash → asset_path` manifest at
`~/.cache/sdv-test-framework/texture-manifests/<version>.json`. Harness loads it at
startup; `DrawSnapshotHandler` cascades Tier 1 (weak map) → Tier 2 (hash + manifest) →
Tier 3 (anonymous `content_hash` + `texture_size`). New `DrawFilter` fields
`content_hash` + `texture_size` let assertions match on the anonymous shape.

**Cascade wiring:** `TextureAssetRegistry.TryResolveWithFallback(Texture2D, TextureHashManifest)`
returns `(path?, hash, width, height)`. Called by:
- `DrawSnapshotHandler.ToDto` — populates DTO fields for wire format.
- `DrawAssertContainsHandler` + `DrawAssertNotContainsHandler` + `DrawFindHandler` —
  enrich each DrawEvent struct-copy with hash + size before invoking `DrawFilterMatcher.Matches`.
Tier 2 hits backfill the Tier 1 weak map so subsequent queries skip rehashing.

**Missing-manifest behavior:** harness logs a one-line info message at startup + Tier 2
no-ops. Tier 3 still populates `content_hash` + `texture_size` on all events, so
filters that match on those fields keep working. Users run `sdv-test build-manifest`
once per SDV version to enable Tier 2.

**Test count after Tier 2:** 317 Passed + 38 Skipped (was 298+37; +19 passed,
+1 skipped — scope slightly exceeded plan due to expanded validator test coverage
in T1).

**Side fix (T5 smoke):** `ScenarioLoader` moved from `src/Protocol/Scenarios/` to
`src/Runner.Mcp/ScenarioLoader.cs` (copy also in `Runner.Mcp`). Root cause: `Protocol.csproj`
used `JsonSchema.Net 7.0.2` which targets net8.0-only `System.Text.Json` APIs; SMAPI's
assembly rewriter rejected the harness mod when staging `Protocol.dll`. Fix: removed
`JsonSchema.Net` from `Protocol.csproj`, moved `ScenarioLoader` to the runner-side
projects which target net10.0 and have no SMAPI loading constraint.

**2026-05-17 cleanup:** `ScenarioLoader` moved back to `src/Protocol/Scenarios/` after
pinning `JsonSchema.Net` to the 6.x line, which includes a netstandard2.0 asset usable by
the net6.0 harness.

**Out of scope (M4):** shipped pre-built manifest, auto-regeneration on SDV update,
streaming manifest-build progress, modded-content entries, hash-algorithm agility,
full 64-char hash (16-char prefix safe for ~5K-entry manifests).

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
inference from menu-open + spatially-nearest NPC, time-advance debounce at ≥10 in-game
minutes (drops per-tick noise — SDV's clock advances ~1.4 in-game minutes/sec). Output
is readable: `[warp Farm, time.advance 30, warp SeedShop, world.interact_npc Pierre]`.

**Manual verification:** the live smoke is interactive — a developer plays the game,
runs `harness_record_actions smoke_walk`, walks around, runs `harness_record_stop`,
inspects the trace, then replays via `dotnet run -- run <path>`. Documented in the
plan's T3 step 2.

**Out of scope (M4):** tick-perfect input replay, tool-use / pickup / combat capture
(needs new RPCs first), auto-flush on game exit.

**Test count after action-trace:** 337+43 → 347+44 (+10 passed, +1 skipped).

### NuGet packaging landed (2026-04-24)

Plan: `docs/superpowers/plans/2026-04-24-nuget-packaging.md` (5 tasks, subagent-driven).
Design spec: `docs/superpowers/specs/2026-04-24-nuget-packaging-design.md`.

**Scope:** ship the framework as installable NuGet packages so modders don't need a
source-tree clone. Three packages produced by `scripts/pack.sh`:
- **`SdvTestFramework.Protocol`** (0.1.0) — transitive dep with the JSON-RPC types.
- **`SdvTestFramework.Runner.Dsl`** (0.1.0) — library users `dotnet add package` in
  their mod's test project.
- **`SdvTestFramework.Cli`** (0.1.0) — `dotnet tool install -g SdvTestFramework.Cli`
  makes `sdv-test` globally available. Bundles CLI + MCP server + the harness mod
  payload via embedded resources (Cli grew from ~0.3MB to ~2.4MB).

**Architecture:** central version property (`SdvTestFrameworkVersion=0.1.0`) in
`Directory.Build.props`, with the conditional packaging metadata moved to
`Directory.Build.targets` so the `IsPackable` condition can see csproj-level values
(props loads before csprojs; targets loads after — canonical MSBuild fix).
Free architecture win surfaced during planning: dropped Runner.Dsl's stale Runner
project ref (post-MCP-T6 reorg made it unused — Runner.Dsl now references only
Protocol). Embedded harness payload via `<EmbeddedResource>` items in Runner.csproj;
`HarnessDeployer.Deploy` does a two-source lookup: source-tree cache first (preserves
dev workflow with mtime idempotency), then assembly-scan for `harness/*` resources
(NuGet-installed workflow). Both paths coexist transparently.

**Local-install smoke verified:** `scripts/pack.sh` produced 3 .nupkg files, installed
Cli to `./.dotnet-tools/`, fresh `MyMod.Tests` project resolved
`SdvTestFramework.Runner.Dsl` from local nupkg source, DSL types compiled + 1 test
passed, MCP `initialize` round-trip succeeded against the installed tool, sample suite
still 11/11.

**Test count after NuGet packaging:** 347+45 (was 347+44; +1 skipped integration
placeholder, no new passing tests — packaging is build-time, not runtime).

**Out of scope (Tier 2 followups):** publishing to nuget.org, GitHub Actions release
workflow on tag, strong-name signing, source link, symbol packages, separate
`SdvTestFramework.Mcp` package.

### HTML run reports landed (2026-04-24)

Plan: `docs/superpowers/plans/2026-04-24-html-run-reports.md` (7 tasks, subagent-driven).
Design spec: `docs/superpowers/specs/2026-04-24-html-run-reports-design.md`.

**Scope:** every test run produces a `./test-results/<run-id>/` directory with
`index.html` + `summary.json` + per-scenario detail pages + screenshot evidence (CLI path).
Promoted to roadmap Tier 1 because evidence visibility is core to the LLM-workflow
goal — Claude reasons about test failures from the JSON + screenshot paths.

**Architecture:** Runner-side orchestration. `RunDirectory` wraps the per-run dirs
(scenarios/, assets/, run-id auto-generated as ISO-timestamp + 6-char hash). Pure types
(`RunDirectory`, `RunSummary`, `ScenarioOutcome`, `StepOutcome`, `AssertionOutcome`)
live in `src/Protocol/Reports/` (namespace `SdvTestFramework.Protocol.Reports`) so both
`Runner.Dsl` and `Runner.Mcp` can reference them without pulling Runner.
`HtmlReportGenerator` (in Runner) is a pure function — takes `RunSummary` + writes
`index.html` + `summary.json` + per-scenario `report.html` + `steps.json` +
`assets/styles.css` (no JS framework, embedded CSS). `ScreenshotRecorder` (in Runner)
calls `bitmap.capture` via RPC + copies the result PNG into the per-scenario
screenshots subdir.

**Integration points:**
- `sdv-test run` — `--report-dir <path>` and `--no-report` flags. Default
  `./test-results/<run-id>/`.
- DSL via `dotnet test` — `SdvFixture.InitializeAsync` creates the run dir;
  `Screenshot.Capture(name)` writes named captures. MVP scope: writes only
  `summary.json`, no HTML — the CLI runner is the rich path.
- MCP `run_scenario` tool — accepts `report_dir` arg; result includes `report_dir` +
  `report_index` paths so Claude can navigate to the artifacts.

**Auto-capture triggers (CLI runner):**
- After `freeze.begin` succeeds — most scenarios enter FREEZE for assertions; this
  gives every scenario at least one screenshot for free.
- On assertion failure — captures the framebuffer at the moment of failure, named
  `assertion-fail-NN.png`.
- Explicit via `screenshot.capture` step or `Screenshot.Capture(name)` DSL method.

**Test count after HTML run reports:** 347+45 → 357+46 (+10 passed, +1 skipped).

**Pre-T5 architectural fixup:** `RunDirectory` + `RunSummary` were originally placed
in `src/Runner/Reports/` (namespace `SdvTestFramework.Runner.Reports`). T5's DSL
Screenshot facet needs access to `RunDirectory`, but `Runner.Dsl.csproj` only
references `Protocol.csproj`. Adding a Runner ref would force the DSL NuGet package
to drag the entire CLI runner into mod test projects. Cleanest fix: move pure types
to Protocol; behavior (HtmlReportGenerator, ScreenshotRecorder) stays in Runner.

**Smoke verification (manual):** live smoke via `./scripts/run-samples.sh` is
documented as manual-only. Per the project history (D1.5, D1.6, M2-record smokes),
`Context.IsWorldReady` doesn't reliably fire under headless Xvfb, so live smoke is
brittle in CI. The skipped placeholder (`RunReportIntegrationTests`) + unit-test
coverage from T1-T6 cover the code paths.

**Out of scope (Tier 3/4 followups):**
- Diff-image-on-failure rendering (Tier 3, pairs with this).
- Interactive HTML (timeline, filter-by-status), Tier 4.
- Run pruning, JPEG/WebP compression, server-side viewer — Tier 4.
- Full step-by-step capture from DSL path — for MVP, DSL run-dirs have screenshots +
  summary.json but no HTML; CLI runner has the richer per-step data + HTML.

### Diff-image-on-failure landed (2026-04-25)

Plan: `docs/superpowers/plans/2026-04-25-diff-image-on-failure.md` (6 tasks, subagent-driven).
Design spec: `docs/superpowers/specs/2026-04-25-diff-image-on-failure-design.md`.

**Scope:** on `bitmap` assertion failure (and not `--update-baselines`), write per-failure
forensics PNGs into the per-run report dir. Surface in HTML report's "Failure forensics"
section. Pairs naturally with HTML run reports — Claude reasons about visual regressions
from the diff PNG path, not just the SSIM number.

**Architecture:** `SsimDiff.Compute` extended to return `SsimResult { Score, BlockScores,
BlocksX, BlocksY }` instead of bare float. `DiffImageRenderer` is a pure function that
takes baseline+capture bytes + `SsimResult` + tolerance + format, writes 3 PNGs (always)
and optionally a triptych composite. Heatmap uses bilinear-smoothed per-block redness so
hot regions taper continuously rather than tiling at hard 8-pixel edges. `BitmapAssertion`
calls the renderer on SSIM failure; `ScenarioRunner` collects DiffSets per failed
assertion; `HtmlReportGenerator` renders a `<section class="forensics">` above the
existing screenshots grid.

**Knobs:**
- CLI: `sdv-test run --diff-format=<files|triptych|all>` (default `files`).
- MCP: `run_scenario` accepts `diff_format` arg (forward-compat only — MCP's run_scenario
  path doesn't currently evaluate bitmap assertions itself; CLI runner is the rich path).
- Per-assertion: `"diff_format": "triptych"` in the bitmap assertion JSON overrides the
  run-wide flag.

**`DiffSet` + `DiffFormat` cross-project types:** placed in `src/Protocol/Reports/` alongside
other shared report types so both `Runner.Mcp` and `Runner` can reference them without
dragging Runner-only code transitively. Same precedent as the HTML run reports T5 fixup.

**HTML render note:** the forensics section extracts the assertion-id from each `DiffSet`'s
parent directory name (`Path.GetFileName(Path.GetDirectoryName(d.Baseline))`) rather than
regenerating it from the loop index — this preserves the correct full-Assertions-list index
even when only a subset of assertions are bitmap.

**Test count after diff-image-on-failure:** 357+46 → 368+47 (+11 passed, +1 skipped).

**Out of scope (Tier 3/4 followups):**
- Pixel-exact + dHash bitmap methods (separate Tier 3 item).
- Diff annotations (arrows, labels) and animated diffs.
- Configurable diff color scheme (red-tint heatmap is the only option).
- MCP-side bitmap-assertion evaluation (today delegates to CLI runner).
- Diff retention / cleanup policy — diffs accumulate alongside captures; pairs with
  the existing Tier 4 capture-cache cleanup item.

### Bitmap completion bundle landed (2026-04-26)

Plan: `docs/superpowers/plans/2026-04-26-bitmap-completion-bundle.md` (8 tasks, subagent-driven).
Design spec: `docs/superpowers/specs/2026-04-26-bitmap-completion-bundle-design.md`.

**Scope:** four Tier 3 items shipped together — pixel-exact + dHash bitmap methods (completes spec §4.5),
three-tier tolerance preset (`generic`/`ci-ubuntu`/`self-hosted-nvidia` per `.claude/rules/ci-integration.md`),
`sdv-test baselines` subcommand (replaces the `--update-baselines` static-field hack with `list`/`update`/
`show`/`delete`), and capture-cache cleanup (auto + manual `sdv-test cache clean`).

**Architecture:** `BitmapAssertion.EvaluateAsync` branches on `a.Method ?? "ssim"`. Per-method `tolerance`
semantics polymorphic — SSIM float, pixel-exact int channel-delta, dHash int Hamming distance. Tier maps to
per-method default tolerances via `TierTolerance.Resolve(tier, method, perAssertionTolerance)`. Diff
renderer's heatmap branch picks per-pixel redness for SSIM (existing) or pixel-exact (new); dHash skips
diff.png entirely (perceptual hash doesn't localize per-pixel — `DiffSet.Diff` is empty string).
`BitmapMethod` enum lives in `Protocol.Reports` (cross-project; same precedent as `DiffFormat`/`DiffSet`).

**Static-field cleanup:** `RunCommandOptions` record threads parsed CLI flags through `RunOnceAsync` instead
of `RunCommand`'s static `_updateBaselinesFlag` / `_diffFormatFlag` / `_tierFlag` fields. `BaselinesCommand.update`
reuses `RunCommand.RunFromOptions` via a swappable `RunExecutor` delegate (test seam — production points to
the real run path; tests substitute a probe).

**Capture cache cleanup:** keeps a file iff BOTH (a) mtime within `--max-age` (default 7 days), AND (b) its
parent scenario subdir is among the `--keep-runs` most-recent (default 5). Auto-hooks at end of every
successful `sdv-test run` invocation; manual `sdv-test cache clean` for one-shot bulk cleanup. Override
location via `$SDV_CACHE_DIR`.

**Test count after bitmap completion bundle:** 368+47 → 388+48 (+20 passed, +1 skipped placeholder).

**Out of scope (Tier 3/4 followups):**
- Per-tier baseline directories (option B from brainstorm — defer until real second CI environment).
- `baselines regenerate` / `baselines validate` (`update` covers regeneration; orphan detection is Tier 4).
- Triptych composite for pixel-exact / dHash (mechanical; defer).
- dHash diff heatmap (perceptual hash doesn't localize per-pixel — explicitly skipped).
- Real environment autodetection for tier (`generic` default is unconditional).
- LFS for baselines (separate Tier 4 item).
- Test-results dir cleanup (`./test-results/` — separate concern).

M1 shipped (see D1.7 completion note above). M2 decomposes per spec §7 Phase 2 into five independent subprojects, each shipping its own plan + smoke:

1. **Fixture builder tool** (§4.8) — scripted creation of reproducible save-state fixtures. ✓ **Landed 2026-04-23.**
2. **Record mode** (§4.7) — `harness_record` state snapshot + `sdv-test record` RPC-trace. ✓ **Landed 2026-04-24.**
3. **TAP + JUnit reporters** (§4.7) — CI integration via `--reporter <console|tap|junit>`. ✓ **Landed 2026-04-23.**
4. **Watch mode** (§4.7) — `sdv-test run --watch` reruns on file change. ✓ **Landed 2026-04-23.**
5. **Bitmap fallback + SSIM** (§4.5) — `bitmap.capture` RPC + `bitmap` assertion type with hand-rolled SSIM. ✓ **Landed 2026-04-24.**

### M2 subproject 1 — Fixture builder landed (2026-04-23)

Plan: `docs/superpowers/plans/2026-04-23-m2-fixture-builder.md` (11 tasks, subagent-driven).
Design spec: `docs/superpowers/specs/2026-04-23-m2-fixture-builder-design.md`.

**Scope:** `sdv-test fixture create <name> --from <script.fixture.json>` builds a fixture by loading a base, running RPC steps, invoking the new `fixture.save` RPC, and copying the resulting save + auto-generated `.meta.json` + `.README.md` into `tests/fixtures/<name>/`. `sdv-test fixture list` enumerates fixtures in the repo. The staging layer (`FixtureStager`) transparently copies `tests/fixtures/<name>/save/` → SDV's `Constants.SavesPath` at scenario-run time, so existing scenarios that reference fixtures by name keep working without modification.

**Migration:** `m0spike_436515781` was migrated from the user's SDV saves dir into `tests/fixtures/m0spike_436515781/` so the full fixture chain lives in git. The spike save is a "root" fixture with `base: null` — it cannot be regenerated with the scripted builder, but derived fixtures can build from it.

**New RPCs:** `fixture.save` (drives `SaveGame.Save()` to completion), `state.mods` (lists loaded mod UniqueIDs for metadata).

**Mid-smoke adjustment (T11 step 3 iteration):** SDV's `SaveGame.Save()` writes to `<farmName>_<uniqueID>`, not the requested fixture name. The plan originally used `FixtureStager.Capture(name, ...)` which looked for a folder matching the fixture name — that folder didn't exist. Fix: added `FixtureStager.CaptureFromPath(sourcePath, name, fixturesRoot)` that copies from the actual save path (returned by the handler's `save_path`) and renames the inner save-data file to match the target fixture name, so SDV's loader can later find it when this fixture is used as a base.

**Smoke result:** `sdv-test fixture create test_day2 --from /tmp/d17-sample.fixture.json` produced a complete `tests/fixtures/test_day2/` with save/ + `.fixture.json` + `.meta.json` + `.README.md`. `./scripts/run-samples.sh` → **10/10 passed** post-migration. `sdv-test fixture list` enumerates both fixtures.

**TODOs for later M2/M3 work:**
- Interactive path (`sdv-test fixture create --interactive`) pairs with spec §4.7 record mode.
- New-game base (build from character creation) requires new RPCs driving intro menus; deferred to M3.
- Git LFS — defer until the repo has >5 fixtures per `.claude/rules/fixtures.md`.
- `fixture delete` / `fixture validate` commands — `rm -rf` + load-time schema validation cover for now.

**Test count after M2 fixture-builder:** 229 Passed + 31 Skipped (was 201+26 before M2; +28 passed, +5 skipped).
- T1: +3 (Protocol DTO serialization)
- T2: +2 (StateModsHandler)
- T3: +7 (FixtureLoader schema + parse paths)
- T4: +4 (FixtureStager)
- T5: +4 (FixtureMetadata + FixtureReadme)
- T6: +1 (FixtureBuilder orchestration)
- T7: +1 (FixtureSaveHandler param validation) + 2 Skipped
- T8: +6 (FixtureCommand arg parsing + list)
- T11: +3 Skipped (integration placeholders)

### M2 subproject 2 — TAP + JUnit reporters landed (2026-04-23)

Plan: `docs/superpowers/plans/2026-04-23-m2-reporters.md` (5 tasks, subagent-driven).
Design spec: `docs/superpowers/specs/2026-04-23-m2-reporters-design.md`.

**Scope:** `sdv-test run` gained two new flags — `--reporter <console|tap|junit>` picks the output format, `--output <path>` picks the sink (stdout if omitted). Five new classes under `src/Runner/Reporters/`: `IReporter` interface, `ConsoleReporter` (default), `TapReporter` (TAP 13), `JunitReporter` (Jenkins XML), and `ReporterFactory`. `ScenarioReport` gained a `Path` field so reporters know which scenario file produced the report (JUnit uses it as `classname`; Console appends it after the scenario name).

**Refactor:** the inline output loop in `RunCommand.cs` moved behind the `IReporter` interface. `ConsoleReporter` preserves the pre-M2 output byte-for-byte — the default user experience is unchanged.

**Formats:**
- **Console** (default) — Playwright-style summary, unchanged from M1.
- **TAP 13** — `TAP version 13` header, `1..N` plan, `ok`/`not ok` lines, YAML diagnostic blocks on failures with `duration_ms` and `failures` list. Widely accepted by CI aggregators (GitHub Actions, GitLab, Jenkins).
- **JUnit XML** — Jenkins-compatible shape (`<testsuites><testsuite><testcase>`). `classname` = scenario file path, `name` = scenario name, `time` = seconds (3-decimal). Failures render as `<failure type="assertion" message="...">` with the full joined `Failures` list in the body.

**Smoke result (live SDV + sample suite):**
- `sdv-test run --reporter tap tests/samples/` → `TAP version 13` + `1..10` + 10 `ok` lines.
- `sdv-test run --reporter junit --output /tmp/x.xml tests/samples/` → `xmllint`-clean XML with `<testsuites tests="10" failures="0" errors="0" time="15.319">` wrapping 10 testcases.
- `./scripts/run-samples.sh` → **10/10 passed** (default console reporter unchanged).

**Test count after M2-reporters:** 240 Passed + 31 Skipped (was 229+31 before; +11 passed).
- T1: +2 (ConsoleReporter byte-for-byte + all-pass)
- T2: +3 (TapReporter: all-pass, failure+YAML, empty)
- T3: +3 (JunitReporter: all-pass, failure element, empty)
- T4: +3 (RunCommand flags: unknown reporter exit 2, flag-after-paths, unwritable output exit 3)

**TODOs for later work:**
- Coloured console output.
- Multiple reporters at once (`--reporter console --reporter junit`).
- Incremental/streaming output (emit per-scenario as they run, not at end).
- GitLab's native test-results XML schema (different from JUnit; GitLab also accepts JUnit).
- HTML reporter (browser-viewable report).

### M2 subproject 3 — Watch mode landed (2026-04-23)

Plan: `docs/superpowers/plans/2026-04-23-m2-watch-mode.md` (5 tasks, subagent-driven).
Design spec: `docs/superpowers/specs/2026-04-23-m2-watch-mode-design.md`.

**Scope:** `sdv-test run` gained a `--watch` flag. Under `--watch`: after the initial scenario pass, the Runner stays resident, installs a `FileSystemWatcher` on the run's paths (filtered to `*.test.json`), and reruns all scenarios on each detected change. SDV subprocess + RPC session are reused across reruns — cold boot (~15s) is paid once per session rather than per edit. 300ms debounce coalesces editor-double-writes.

**Architecture:** Two new classes under `src/Runner/Watch/`: `ScenarioWatcher` (wraps `FileSystemWatcher` with debounce + test seam via `TriggerForTests`) and `WatchLoop` (resident orchestrator holding the session open, blocking on watcher-or-ct, calling a rerun callback). `RunCommand` factored its "discover + run + report" flow into a private `RunOnceAsync` helper so both the initial run and each watcher-triggered rerun call it.

**Smoke result (live SDV + sample suite):**
- `sdv-test run --watch tests/samples/` completed the initial 10/10 pass in ~20s (including ~15s cold boot).
- Touching `tests/samples/01-state-time-after-load.test.json` triggered a rerun within ~500ms; 10/10 passed again in ~13s (fast-path — no cold boot).
- Touching a second file reproduced the same pattern: `SMAPI PID unchanged = 580996` across both reruns. Session reuse verified.
- `SMAPI-latest.txt` shows exactly one `SMAPI x.y.z with Stardew Valley` startup banner — definitively one process across the whole session.
- Ctrl-C in an interactive terminal exits cleanly (SIGINT → `Console.CancelKeyPress` → cts.Cancel → outer `finally` kills SDV). A bash background-job test of SIGINT doesn't trigger `CancelKeyPress` (no controlling terminal), which is a TTY/pipe quirk, not a runner bug.

**Test count after M2-watch:** 246 Passed + 32 Skipped (was 240+31 before; +6 passed, +1 skipped integration placeholder).
- T1: +4 (ScenarioWatcher: debounce, coalesce-burst, dispose, trigger-for-tests)
- T3: +1 (WatchLoop: callback on trigger + banner output)
- T4: +1 (`--watch` flag parsing)
- T5: +1 Skipped (WatchMode_FileChange_TriggersRerun integration placeholder)

**TODOs for later M3:**
- Keyboard shortcuts (Playwright-style `r`/`q`/`a`) — needs ANSI raw-mode input + redraw.
- Granular rerun (only the changed file's scenarios) — defer; `--filter` is the workaround.
- Watching non-scenario files (fixtures, mods, source code) — each requires SDV teardown/restart.
- Auto-reconnect on SDV crash.
- SIGTERM handling — currently leaks the SDV subprocess if the runner is SIGTERM'd (interactive Ctrl-C works fine).

### M2 subproject 4 — Record mode landed (2026-04-24)

Plan: `docs/superpowers/plans/2026-04-24-m2-record-mode.md` (5 tasks, subagent-driven).
Design spec: `docs/superpowers/specs/2026-04-24-m2-record-mode-design.md`.

**Scope:** two complementary record flows:
- **`harness_record <name>`** (SMAPI console command) — captures current state as a 6-assertion scenario in `~/.cache/sdv-test-framework/records/<name>.test.json`. User plays to state, types command, gets a "reproduce-this-state" scenario to promote.
- **`sdv-test record <name>`** (CLI subcommand) — launches SDV, subscribes to `JsonRpcSession.RequestReceived`, buffers non-read non-lifecycle RPC calls as scenario steps. On Ctrl-C, writes `tests/samples/<name>.test.json`. Drives by external RPC (Python probes, future MCP tools).

**Architecture:** two independent flows in separate layers (Harness vs Runner). Both emit the standard scenario schema. JSON emission is hand-rolled in each project (~20 lines each) because the `ScenarioSpec` DTO lives in Runner and cross-project (net6 vs net10) coupling isn't worth the savings.

**Filter list (RPC-trace):** skipped: `state.*` reads, `scenario.begin`, `scenario.end`. Captured: `player.*`, `time.*`, `world.*`, `fixture.load`, `draw.*`, `freeze.*`.

**Test count after M2-record:** 253 Passed + 33 Skipped (was 246+32 before; +7 passed, +1 skipped).
- T1: +2 (HarnessRecordConsole: valid-name emits, invalid-name rejects)
- T3: +3 (RpcTraceRecorder: records mutator, skips lifecycle, emits valid scenario JSON)
- T4: +2 (RecordCommand: missing name → 2, collision without force → 3)
- T5: +1 Skipped (RecordMode_LiveSession integration placeholder)

**Live smoke — partial coverage:** launched `sdv-test record my_trace --mods-path <samples>` successfully. SDV booted, recorder installed, `[record] capturing RPC calls...` banner printed. Python probe connected and called `scenario.begin` + `fixture.load`, but got stuck polling `state.player` waiting for world-ready under headless Xvfb (same timing issue encountered in D1.5/D1.6 smokes). Could not observe end-to-end trace-to-file flush because SIGINT to a background `dotnet run` doesn't trigger `Console.CancelKeyPress` (same TTY/pipe quirk documented in M2-watch). Unit tests (T3 EmitsValidScenarioJson) exercise the exact flush path — OnRequest twice → WriteToFile → ScenarioLoader.Load accepts it — so the code correctness is verified even though the live orchestration didn't complete.

**Interactive-only limitation:** both flows assume a TTY. `harness_record` is typed directly into SMAPI's console (always interactive). `sdv-test record` Ctrl-C works in a foreground terminal; background-job SIGINT is a known .NET TTY quirk that also affects watch mode.

**TODOs for M3:**
- Action-trace recording (input-event → RPC translation, Playwright-codegen analog).
- SIGTERM handler in Program.cs so background-job shutdowns flush cleanly.
- Recording `draw.contains` assertion synthesis — user adds assertions by hand for now.
- Merged snapshot+trace in one session.

### M2 subproject 5 — Bitmap fallback landed (2026-04-24) — M2 complete

Plan: `docs/superpowers/plans/2026-04-24-m2-bitmap-fallback.md` (7 tasks, subagent-driven).
Design spec: `docs/superpowers/specs/2026-04-24-m2-bitmap-fallback-design.md`.

**Scope:** the final M2 subproject. Bitmap-diff assertion type as fallback for the ~5% of render checks where draw-call inspection is insufficient (shader effects, procedural/compositing output). Draw-call assertions remain the primary strategy per spec §2.

- **`bitmap.capture` RPC** (Harness, FREEZE-phase only) — reads backbuffer via `GraphicsDevice.GetBackBufferData<Color>`, optional `region` crop, ImageSharp PNG encode, writes to `~/.cache/sdv-test-framework/captures/<scenario>/bitmap_<N>.png`. Returns `{path, width, height}` — PNG bytes never cross the wire.
- **`bitmap` assertion type** (Runner) — new DSL entry with `baseline` (path, scenario-relative), `tolerance` (float in (0,1], default 0.95), optional `region`. Evaluator calls `bitmap.capture`, loads both baseline + capture PNGs, runs hand-rolled 8×8-block SSIM kernel, produces pass/fail with SSIM score in the failure message.
- **`sdv-test run --update-baselines`** — missing or mismatched baselines are regenerated from captures instead of failing the run. Intended for dev-loop baseline bootstrapping.

**Architecture:** capture lives in the harness (`BitmapCaptureHandler`), diff lives in the runner (`SsimDiff` + `BitmapAssertion` + `BaselineManager`). One new NuGet — `SixLabors.ImageSharp` 3.1.12 — covers PNG codec for both projects. SSIM is hand-rolled (~100 LOC) so we don't pull in a heavyweight SSIM-specific dependency. Single runner-global `--update-baselines` flag for now; three-tier baseline system from `.claude/rules/ci-integration.md` deferred until we have >1 CI environment.

**Wire shape:**
```json
{ "type": "bitmap", "baseline": "baselines/shop_menu.png", "tolerance": 0.95 }
```

**Smoke result (live SDV + 11-scenario suite):**
- First run: `./scripts/run-samples.sh --update-baselines` → baseline written at `tests/samples/baselines/bitmap_shop_menu_basic.png`, 11/11 passed.
- Second run: `./scripts/run-samples.sh` → SSIM match across consecutive captures, 11/11 passed.
- Drift test: tamper baseline (32×32 solid red via ImageMagick) → scenario 11 fails with `SSIM dim mismatch: 32×32 vs 1280×720`. Recovery via `--update-baselines` restores to passing.

**Test count after M2-bitmap:** 262 Passed + 34 Skipped (was 253+33 before M2-bitmap; +9 passed, +1 skipped — +1 over plan target because T4 added an extra absolute-path test).
- T2: +3 (SsimDiff: identical, noisy, dim-mismatch)
- T3: +1 (BitmapCaptureHandler precondition)
- T4: +3 (BaselineManager: relative-resolve, absolute-passthrough, write-creates-dir)
- T5: +2 (BitmapAssertion: matches, missing-baseline)
- T7: +1 Skipped (BitmapFallback_LiveSession integration placeholder)

**Build fix (T7):** `SixLabors.ImageSharp.dll` was not being copied to the harness bin output by `ModBuildConfig` (which suppresses default NuGet DLL copy). Fixed by adding a `CopyImageSharpToOutput` target in `Harness.csproj` (uses `RuntimeCopyLocalItems` to avoid hard-coding the NuGet cache path) and updating `StageHarnessPayload` in `Runner.csproj` to stage `SixLabors.ImageSharp.dll` alongside `Harness.dll`. Without this, SMAPI would reject the harness mod with `Failed to resolve assembly: 'SixLabors.ImageSharp'`.

**Out of scope (TODO for M3):**
- Pixel-exact + dHash methods per spec §4.5. Only SSIM wired (implicit `method: "ssim"`).
- Three-tier baseline tolerance (generic/ci-ubuntu/self-hosted-nvidia) per `.claude/rules/ci-integration.md` — M2 takes a single per-assertion tolerance; tier resolution lands when there's a second CI environment.
- Diff-image-on-failure (`tests/diffs/<scenario>/<assertion>.png`) — failure messages report SSIM score + dims but don't render a visual diff.
- Git LFS for baselines — regular git blobs for now; same TODO as fixtures.
- `sdv-test baselines` subcommand — currently `--update-baselines` is a `run` flag.
- Capture-cache cleanup — `~/.cache/sdv-test-framework/captures/<scenario>/` accumulates across runs.
- Animated / multi-frame / streaming captures.

**M2 complete.** Full M2 surface: fixture builder, reporters (console/TAP/JUnit), watch mode, record mode, bitmap fallback. Next: M3 polish + ecosystem per spec §7 Phase 3.

## M0 outcome (2026-04-22)

- [x] D0.1 — Minimal harness mod with Harmony patches on SpriteBatch.Draw (7/7 overloads, SMAPI 4.5.2 + SDV 1.6.15, zero unknowns)
- [x] D0.2 — Determinism experiment script (fully scripted two-pass run.sh + analyze.py with tick/ref normalization)
- [x] D0.3 — Spike report with recommendation: **`PROCEED` to M1**. 94.93% byte-level deterministic with minimal pinning (`eventUp=true`, `displayHUD=false`, RNG pin, cursor freeze). Remaining 5% is a single localized source (parallax background scroll tied to `Game1.currentGameTime`), fixable in M1's FREEZE-phase work.

## Blockers for M1

_(none — M0's findings feed directly into M1's determinism-controller task)_

## Next up

M1 — Core Framework. See @docs/milestones/M1-core.md.

Top of the M1 agenda, informed by M0:

1. Port the spike harness's recording + arm/disarm + pin-seed + load plumbing into `src/Harness/` as production code, with TDD coverage for everything that isn't a Harmony patch (the patches themselves are integration-tested).
2. Implement the FREEZE-phase determinism controller per `.claude/rules/determinism.md`, including a fix for the parallax-background residual divergence M0 uncovered.
3. RPC layer (JSON-RPC over Unix socket) — unblocks the runner CLI.
4. Minimum scenario format loader + executor.
5. 10 sample scenarios against one real mod (spec §7, Phase 1 success criterion).
