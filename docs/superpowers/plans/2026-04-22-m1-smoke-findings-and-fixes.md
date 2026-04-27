# M1 Smoke Findings & Fix Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. Steps use checkbox syntax.

**Goal:** Close the usability + correctness bugs surfaced by the M1 Phase 2 smoke test (2026-04-22) before starting D1.5 / D1.6 / D1.7. Each bug is a discrete, mostly-independent task.

**Session context:** After M1 Phase 2 landed (152 Passed + 9 Skipped via `./scripts/ci.sh`), an end-to-end smoke exercised real SDV 1.6.15 + SMAPI 4.5.2 on this workstation. The harness, socket, protocol, and runner-side logic all proved functional end-to-end — but the integration surface (launcher + some handler preconditions) has real bugs that block the "run the headline `sdv-test run` command against a real install" flow.

## Smoke methodology

1. Built `src/Harness/` (Release) → `Harness.dll` + `manifest.json` + `SdvTestFramework.Protocol.dll` into `/tmp/sdv-m1-smoke-*/mods-isolated/SdvTestFramework.Harness/`.
2. Created a trivial scenario file `/tmp/sdv-m1-smoke-*/scenarios/smoke.test.json`:
   ```json
   { "name": "m1_smoke_no_fixture", "config": { "seed": 42 }, "steps": [], "assertions": [] }
   ```
3. **First attempt:** `dotnet run --project src/Runner -- run <scenarios>` — exited 3 fatal with "A task was canceled." after 60s.
4. **Workaround:** manually launched SMAPI under Xvfb with `--mods-path` + `SDV_TEST_SOCKET` env var. Socket appeared at 6 s; harness logged all 16 RPC methods registered.
5. Used two ad-hoc Python probes (`/tmp/sdv-m1-smoke-*/rpc-probe.py`, `rpc-probe2.py`) to exercise all 16 methods via direct Unix socket + JSON-RPC. Results are the ground truth for the bugs below.

## What works (green findings, worth locking in)

- **End-to-end transport:** runner `probe` connects, receives `ready`, invokes `state.player`, prints. Zero round-trip issues.
- **All 16 RPC methods register and dispatch cleanly** per the "Harness loaded" banner emitted on every launch.
- **Handshake payload:** `{version:"0.1.0", sdv:"1.6.15", smapi:"4.5.2"}` — exactly per `docs/rpc-schema.md`.
- **State queries work at title screen** (returning sensible defaults — player name empty, menu type `"TitleMenu"`, location empty).
- **Scenario lifecycle works:** `scenario.begin` → RPC work → `scenario.end` → `scenario.begin` again — no wedging. T15's `finally`-block fix is confirmed good.
- **Error code discipline holds:**
  - Unknown NPC → `-32003 GameStateInvalid` ("no NPC named: Abigail")
  - Unknown location → `-32003` ("no location named: Farm")
  - Unknown method → `-32601 MethodNotFound`
  - Bad params (negative money, fractional minutes, wrong type on `time.advance`) → `-32602 InvalidParams` with clear messages
  - Filter shape violation (3-element color array) → `-32602` via T11's `DrawFilterValidator`
- **`RpcParams.Required`'s JSON parse rewrap works in the wild:** `time.advance {"minutes": "ten"}` correctly surfaced as `InvalidParams` ("params parse error: The JSON value could not be converted…"), not as `InternalError`.

## Bugs

Severity rubric:
- **Critical** — blocks the headline `sdv-test run` command on this workstation.
- **Important** — correctness / UX bugs that will confuse scenario authors or leak bad error codes.
- **Minor** — cosmetic or edge-case issues; defer if schedule-tight.

### Bug #1 — [Critical] `RunCommand`/`SdvLauncher` does not pass `--mods-path`

**What:** `src/Runner/SdvLauncher.cs:Launch(socketPath, installPath, modsPath)` accepts `modsPath` as a parameter and appends `--mods-path <modsPath>` to the process args only when non-null. `src/Runner/Commands/RunCommand.cs:84` calls `SdvLauncher.Launch(socket)` — modsPath is never supplied. SDV therefore loads from its default `$SDV_INSTALL_PATH/Mods/` directory.

**Observed symptom:** `dotnet run --project src/Runner -- run <scenarios>` exited 3 fatal with "A task was canceled." after the 60 s ready-notification timeout. SMAPI log showed the 95 user-installed mods loading from the default Mods folder; our harness was never in that folder so it never ran.

**Blast radius:** Any machine with a non-empty default Mods folder (i.e. any real development workstation or CI target where SDV was installed via Steam). The command is effectively unusable today.

**Fix sketch:** Add `--mods-path <path>` CLI arg to `RunCommand` (default: an auto-provisioned isolated dir like `~/.cache/sdv-test-framework/mods/<hash>/`). Also accept `SDV_MODS_PATH` env var. Plumb through `SdvLauncher.Launch(socket, installPath: null, modsPath: resolved)`. Update help text.

**Stretch fix:** same command should **deploy the harness** into the isolated mods dir before launching (currently users have to copy the DLL manually — this is Bug #9 below, a sibling of #1).

### Bug #2 — [Important] `world.set_weather` at title screen throws NRE → `InternalError` instead of `GameStateInvalid`

**What:** `WorldSetWeatherHandler.Handle` already has a guard (added in T8 review): `if (Game1.netWorldState?.Value is null) throw GameStateInvalid`. But empirically, at title screen the handler returns `-32603 InternalError "Object reference not set to an instance of an object."` — the NRE is firing from *past* the guard, most likely from `state.GetWeatherForLocation("Default").Weather = weatherId` where either `GetWeatherForLocation` returns null or `currentLocation` is null in a way the guard doesn't catch.

**Reproduction:** Launch SMAPI with harness; at title screen call `world.set_weather` with `{"type":"rain"}`.

**Blast radius:** Scenarios calling `world.set_weather` before a save is fully loaded get a confusing `-32603` (usually interpreted as "harness bug") instead of the documented `-32003 GameStateInvalid` "no active save".

**Fix sketch:** Tighten the precondition: require `Context.IsWorldReady` (which implies save loaded + `Game1.currentLocation` populated). Add a test under `tests/Harness.Tests/WorldSetWeatherHandlerTests.cs` that asserts the new `GameStateInvalid` path.

### Bug #3 — [Important] `player.give_item` accepts unknown item IDs silently (`ok:true`)

**What:** `PlayerGiveItemHandler.Handle` checks `if (item is null) throw GameStateInvalid("unknown item id: …")`. But `ItemRegistry.Create("(O)nonsense", 1)` in SDV 1.6 returns a **placeholder "Error Item"**, not null. The guard never fires; the handler adds a bogus item to the farmer's inventory and returns `ok:true`.

**Reproduction:** Probed `player.give_item` with `{"id":"(O)nonsense","count":1}` → `{"ok":true,"tick":…}`.

**Blast radius:** Scenarios that typo an item id silently insert garbage rather than failing loudly.

**Fix sketch:** Use `ItemRegistry.Exists(id)` (if available in SDV 1.6 — verify) or `ItemRegistry.GetMetadata(id)` which returns null for unknown IDs — as the upfront validation before `Create`. If neither exists, compare the created item's `QualifiedItemId` against `req.Id`. Add a test against a fake registry shim or skip-marked integration.

### Bug #4 — [Important] `fixture.load` with a non-existent save returns `ok:true`

**What:** `FixtureLoadHandler.Handle` calls `Game1.currentLoader = SaveGame.getLoadEnumerator(req.Name); Game1.gameMode = 6;` — without validating that the save folder exists. SDV's `getLoadEnumerator` doesn't validate upfront; it sets up the coroutine, which will fail silently later when it tries to read the file.

**Reproduction:** Probed `fixture.load` with `{"name":"nonexistent_fixture"}` → `{"ok":true,"tick":…}`.

**Blast radius:** Scenario authors get a false "load initiated" response, then their subsequent `state.player` wait-for-ready polls time out after 30 seconds with a generic error. Very hard to diagnose.

**Fix sketch:** Before kicking off the loader, check `Directory.Exists(Path.Combine(Constants.SavesPath, req.Name))` (or whatever the SDV 1.6 canonical path is). Throw `FixtureLoadFailed` (-32002 — already reserved in the enum!) with message `"no save named: <name>"`. Document the check in `rpc-schema.md`.

### Bug #5 — [Important] `player.set_money` at title screen mutates throwaway Farmer state silently

**What:** `Game1.player` exists even at the title screen (it's a default Farmer instance). `PlayerSetMoneyHandler.Handle` captures `previous = Game1.player.Money` (== 500, the SDV-default starting gold) and sets it. Returns `ok:true` with `previous:500`. The change has no user-visible effect because the throwaway Farmer is replaced when a save loads.

**Reproduction:** Probed `player.set_money` with `{"amount":9999}` at title screen → `{"previous":500,"ok":true,"tick":…}`.

**Blast radius:** Scenarios that accidentally call `player.set_money` before `fixture.load` has completed silently no-op. Authors see the `ok:true` and assume it took.

**Fix sketch:** Add `Context.IsWorldReady` guard at the top of **all four game-mutating player/world handlers** (`player.warp` already has this via its location lookup; `player.give_item`, `player.set_money`, `time.advance` do not). Throw `GameStateInvalid` with message "no active save — mutation requires a loaded world." Add shared precondition helper — a candidate for `RpcPreconditions.RequireWorldReady()` alongside `RpcParams.Required/Optional`.

### Bug #6 — [Important] `time.advance` at title screen runs `performTenMinuteClockUpdate` on unloaded world

**What:** Same class as Bug #5 — `TimeAdvanceHandler.Handle` has no world-loaded precondition. Calling `time.advance {"minutes":10}` at the title screen succeeded and reported `new_time_of_day:610` (advanced from default 600). This silently mutated `Game1.timeOfDay` and probably fired schedule hooks / weather tick logic on an un-started world.

**Reproduction:** Same session; `time.advance {"minutes":10}` at title → `{"new_time_of_day":610, …}`.

**Blast radius:** Similar to Bug #5. May also leave SDV in a partially-initialized state that causes downstream bugs when a save is later loaded.

**Fix sketch:** Same as Bug #5 — add `RequireWorldReady()` guard. Fix #5 + #6 together.

### Bug #7 — [Minor] `draw.arm` at title screen flips `Game1.eventUp` + `Game1.displayHUD` unnecessarily

**What:** The harness `Recorder.ActivateArm` deliberately flips `Game1.eventUp = true` and `displayHUD = false` during capture (T11 finding: suppresses ambient animations for determinism). At title screen these flags have no user-visible draw implications, but the write still happens, and the restore on disarm writes them back.

**Reproduction:** `draw.arm {"ticks":10}` at title → `ok:true`. `draw.disarm` → `ok:true`. No crash, no visible harm.

**Blast radius:** Negligible at title screen. But `draw.arm` without a meaningful scene is semantically meaningless. Consider guarding for clarity.

**Fix sketch:** Low priority; either add a warn-level log note ("arming without an active save captures title-screen draws") or a `RequireWorldReady` guard. Defer behind #5 / #6.

### Bug #8 — [Minor] `state.time.day_of_month` returns `0` at title screen

**What:** SDV's `Game1.Date.DayOfMonth` is `0` before a save is loaded (valid day values start at 1). Our handler returns the raw value. Scenario authors querying `state.time` at a bad moment get `day_of_month:0`, `year:1`, `season:"spring"` — looks valid, isn't real.

**Reproduction:** `state.time` at title → `{"season":"spring","day_of_month":0,"year":1,"time_of_day":600,"day_of_week":"sunday"}`.

**Blast radius:** Low. Scenarios that assert `state.time.day_of_month == 1` at Spring Day 1 would mis-diagnose a timing issue.

**Fix sketch:** Document the title-screen default in the schema entry. Or surface an explicit `"in_save": false` field so callers can disambiguate. Defer.

### Bug #9 — [Important] `sdv-test run` doesn't deploy the harness; users need a separate ops step

**What:** Even after Bug #1 is fixed, `run` would expect `--mods-path` to point at a dir containing a pre-built `SdvTestFramework.Harness/` subdir. There's no "build and deploy the harness" step in the command — users have to `dotnet build src/Harness && cp …` by hand.

**Blast radius:** Makes the command hard to use out-of-the-box. Every invocation that references a fresh mods-path needs manual prep.

**Fix sketch:** `RunCommand` should:
1. Resolve (or create) an isolated mods dir (default: `~/.cache/sdv-test-framework/mods/`)
2. `dotnet build src/Harness -c Release` via `Process.Start` (or pre-build at CLI build time and embed a resource-directory pointer — simpler)
3. Copy the built artefacts into `<mods-path>/SdvTestFramework.Harness/` if missing or outdated (compare timestamps)
4. Launch SMAPI

Alternatively, a sibling `sdv-test deploy` command just does the build + copy, and `run` requires it to have been run first. Lower-effort but worse UX.

### Bug #10 — [Minor] Leftover socket files on abnormal exit

**What:** `UnixSocketRpc.RunServerAsync` has `finally { File.Delete(path); }`. When SDV is force-killed (e.g., the runner's 5-second graceful-shutdown timeout elapses), the `finally` may not run and the socket stays. Also applies when the harness crashes during startup before the listener's `finally` can fire.

**Blast radius:** Low. Subsequent `bind` on the same path errors with `AddressAlreadyInUse` — but runs use `Guid.NewGuid()` paths so collisions are effectively impossible.

**Fix sketch:** `UnixSocketRpc.RunServerAsync` should also delete any existing file at `path` on startup (it already does: `if (File.Exists(path)) File.Delete(path)` at line 28). No fix needed — this is already handled. **Crossing off as non-bug.**

## Fix plan (task-sized)

Each task is a standalone checkpoint. Sequential where noted; otherwise independent.

### Task S1: `--mods-path` support in `SdvLauncher` + `RunCommand` (fixes Bug #1)

**Files:**
- Modify: `src/Runner/SdvLauncher.cs` — already has `modsPath` parameter; no change needed here
- Modify: `src/Runner/Commands/RunCommand.cs` — parse `--mods-path` arg, pass through to `SdvLauncher.Launch`
- Modify: `src/Runner/Program.cs` — update help text
- Test: `tests/Runner.Tests/RunCommandTests.cs` — add a test that verifies `--mods-path` is parsed and forwarded (can verify via inspecting `SdvLauncher` — or via a test-double for the launcher)

- [ ] Write failing test: `Run_ModsPathArg_ForwardedToLauncher` (refactor `SdvLauncher.Launch` into an `ISdvLauncher` interface if needed, or use a test mode that just echoes the resolved path without launching)
- [ ] Parse `--mods-path <value>` in `RunCommand.RunAsync` alongside existing `--filter` parsing
- [ ] Resolve default when unset: `~/.cache/sdv-test-framework/mods` (auto-create)
- [ ] Pass resolved path into `SdvLauncher.Launch(socket, installPath: null, modsPath: resolved)`
- [ ] Update `PrintHelp` in `Program.cs` to include `--mods-path` flag
- [ ] Run `./scripts/ci.sh` — green

### Task S2: Auto-deploy harness into isolated Mods dir (fixes Bug #9)

**Depends on:** S1 (need the `--mods-path` plumbing in place).

**Approach:** When `RunCommand` starts, ensure `<mods-path>/SdvTestFramework.Harness/` contains up-to-date `Harness.dll`, `manifest.json`, `SdvTestFramework.Protocol.dll`. The simplest implementation embeds paths to these files at runner build time and copies via `File.Copy` with overwrite.

**Files:**
- Modify: `src/Runner/Runner.csproj` — copy harness build output (and Protocol.dll) as `None` items into Runner's `bin/.../harness-payload/` on build
- Modify: `src/Runner/Commands/RunCommand.cs` — new `DeployHarness(modsPath)` that copies from `AppContext.BaseDirectory/harness-payload/` into `modsPath/SdvTestFramework.Harness/`
- Test: `tests/Runner.Tests/RunCommandTests.cs` — `Run_AutoDeploysHarness` verifies the target directory receives Harness.dll

- [ ] Add `<Target>` in Runner.csproj that depends on Harness build and copies outputs into a `harness-payload` output dir
- [ ] Write `DeployHarness` helper
- [ ] Call it from `RunCommand.RunAsync` before `SdvLauncher.Launch`
- [ ] Add test verifying deploy happens on a fresh mods dir
- [ ] CI green

### Task S3: Shared `RequireWorldReady` precondition + apply to mutators (fixes Bugs #5, #6, and tightens #2)

**Files:**
- Create: `src/Harness/Rpc/RpcPreconditions.cs` — `public static void RequireWorldReady()` throws `JsonRpcException(GameStateInvalid, "no active save — mutation requires a loaded world")` unless `Context.IsWorldReady`
- Modify: `src/Harness/Handlers/PlayerSetMoneyHandler.cs` — add `RpcPreconditions.RequireWorldReady()` at top
- Modify: `src/Harness/Handlers/PlayerGiveItemHandler.cs` — same
- Modify: `src/Harness/Handlers/TimeAdvanceHandler.cs` — same
- Modify: `src/Harness/Handlers/WorldSetWeatherHandler.cs` — replace the existing `Game1.netWorldState?.Value is null` check with `RpcPreconditions.RequireWorldReady()` (which also covers the NRE path of Bug #2)
- Test: `tests/Harness.Tests/` — add `*_AtTitleScreen_ThrowsGameStateInvalid` tests for each handler (skip-marked since `Context.IsWorldReady` is a static SMAPI property that can't be faked without more plumbing)

- [ ] Write `RpcPreconditions.cs`
- [ ] Apply to four handlers
- [ ] Skip-marked integration tests for each (consistent with existing skip-marked pattern from T5/T6/T7/T8)
- [ ] Remove the NRE-prone `Game1.netWorldState?.Value is null` guard in `WorldSetWeatherHandler` (superseded)
- [ ] CI green

### Task S4: Validate item IDs upfront in `player.give_item` (fixes Bug #3)

**Files:**
- Modify: `src/Harness/Handlers/PlayerGiveItemHandler.cs` — replace `item is null` check with a proper ItemRegistry-exists check

**Research required:** Verify which SDV 1.6 API validates item-id existence. Candidates: `ItemRegistry.Exists(id)`, `ItemRegistry.GetMetadata(id) is null`, comparing `created.QualifiedItemId` to requested id. Read `ItemRegistry.cs` source via decompiler or the content-pack wiki.

- [ ] Investigate SDV 1.6 `ItemRegistry` API surface (web-search or Read SDV source)
- [ ] Replace the guard with the reliable upfront check
- [ ] Update the test to not be skip-marked if feasible (depends on whether `ItemRegistry` can be exercised without a full SDV context)
- [ ] CI green

### Task S5: Validate save folder exists in `fixture.load` (fixes Bug #4)

**Files:**
- Modify: `src/Harness/Handlers/FixtureLoadHandler.cs` — add `Directory.Exists(Path.Combine(Constants.SavesPath, req.Name))` check, throw `FixtureLoadFailed` (-32002) when missing
- Modify: `docs/rpc-schema.md` — document the check
- Test: `tests/Harness.Tests/FixtureLoadHandlerTests.cs` — `Handle_SaveMissing_ThrowsFixtureLoadFailed` (the save-folder check uses the Constants.SavesPath static which can be hard to isolate; may be skip-marked)

- [ ] Find canonical `Constants.SavesPath` in SDV 1.6 (verify)
- [ ] Add the guard + error
- [ ] Test + schema update
- [ ] CI green

### Task S6: (Optional) Smoke test shell script

Create `scripts/smoke-test.sh` that automates the steps I ran manually this session:
1. Build `src/Harness`
2. Stage harness into `/tmp/sdv-m1-smoke-.../mods-isolated/`
3. Write the minimal smoke scenario
4. Launch SMAPI under Xvfb with `--mods-path`
5. Poll for socket, run the Python probes
6. Tear down

This makes smoke-repeating deterministic and provides a CI hook for post-S1-S5 regression checking (eventually — CI currently has no SDV install).

- [ ] Port the bash + python from `/tmp/sdv-m1-smoke-1776903924/` into the repo
- [ ] Add to `./scripts/` alongside `ci.sh`
- [ ] Gate on `$SDV_INSTALL_PATH` being set — skip gracefully in CI

## Non-bugs ruled out

- **Error message wording** for `time.advance` bad params — worded fine.
- **Leftover socket files** (Bug #10) — `UnixSocketRpc.RunServerAsync` already guards with `if (File.Exists(path)) File.Delete(path)` on startup. No fix needed.
- **Scenario wedging on failure** — confirmed fixed (T15 `finally` block). Smoke tested scenario.begin → (failing step would go here) → scenario.end cleanly.

## Recommended order of operations

1. **S1** first (unblocks running any future smoke test end-to-end)
2. **S2** (close the UX gap so `sdv-test run` becomes actually useful)
3. **S3** (bundle Bugs #2, #5, #6 — single shared guard)
4. **S4 / S5** (parallel-safe after S3)
5. **S6** (optional; makes future smoke-testing free)

After S1-S5, re-run the smoke test; confirm the same 16 probes + `sdv-test run <scenarios>` via the actual CLI works end-to-end. *Then* start D1.5.

## Execution handoff

Plan saved to `docs/superpowers/plans/2026-04-22-m1-smoke-findings-and-fixes.md`. Two execution options:
1. **Subagent-Driven (recommended)** — Dispatch one subagent per S-task; review between each.
2. **Inline** — Execute sequentially with checkpoints.

Which approach?
