# M2 — Bitmap Fallback Design

**Milestone:** M2 subproject 5 (per spec §7 Phase 2 decomposition) — final M2 subproject
**Date:** 2026-04-24
**Author:** fintan + Claude (brainstorming session)
**Status:** Approved — ready for implementation-plan drafting

## Goal

Ship bitmap-diff assertions as a fallback for the ~5% of render checks where draw-call inspection is insufficient — shader effects, procedural/compositing output, post-process overlays. The primary strategy stays draw-call; bitmap is escape-hatch only (spec §2, CLAUDE.md).

Scope for this subproject:

- `bitmap.capture` RPC — FREEZE-phase-only framebuffer grab, optional `region`, writes PNG to a harness-side captures cache, returns the path + dimensions.
- `bitmap` scenario assertion type — baseline-vs-capture SSIM comparison. Fields: `baseline` (path), `tolerance` (float 0-1), optional `region`.
- `sdv-test run --update-baselines` — missing/stale baselines are regenerated from captures instead of failing the run.
- Hand-rolled SSIM (~100 lines, 8×8 block means/variances/covariance → composite score). No heavyweight SSIM dependency — only ImageSharp for PNG codec.

Deferred to M3 (per spec §4.5):

- `method: "pixel-exact"` and `method: "dhash"` — only `method: "ssim"` (implicit default) is wired.
- Three-tier tolerance system (`generic`/`ci-ubuntu`/`self-hosted-nvidia` from `.claude/rules/ci-integration.md`) — M2 takes a single per-assertion tolerance; tiers land once we have >1 CI environment actually feeding baselines.
- Diff-image writing on failure (`tests/diffs/<scenario>/<assertion>.png`) — error messages report the SSIM score + dims; a visual diff is an M3 polish.

## Architecture

**Capture lives in the harness, diff lives in the runner.** This keeps the wire shape small (the harness returns `{path, width, height}` — PNG bytes never cross the socket) and keeps the SSIM kernel in the same process that owns scenario orchestration and can load the baseline from disk directly.

**Capture timing.** The capture handler is dispatched on the game thread (same path as every other RPC handler, via `GameThreadDispatch`). Preconditions: `scenario.begin` ran, `freeze.begin` ran — the FREEZE-phase guard is the same one `FreezeBeginHandler` asserts against, just inverted (`DeterminismController.Frozen == true` required). The backbuffer read uses MonoGame's `GraphicsDevice.GetBackBufferData<Color>(...)` on the current frame. Optional `region` trims to a sub-rect before encode; missing region captures the full resolution.

**Transport.** Harness writes the PNG to `~/.cache/sdv-test-framework/captures/<scenario>/<assertion>.png` (directory auto-created per scenario). Response is the absolute path + width + height. Runner reads both baseline + capture from disk and runs SSIM.

**SSIM.** Hand-rolled. 8×8 non-overlapping blocks, per-block compute (μ_x, μ_y, σ²_x, σ²_y, σ_xy) → standard SSIM formula with constants C1 = (0.01·255)², C2 = (0.03·255)². Composite = mean across blocks. Returns `float ∈ [0, 1]`. ~100 LOC self-contained, no external SSIM library. ImageSharp (`SixLabors.ImageSharp`) carries PNG encode/decode only — one NuGet, multi-TFM (.NET 6 + .NET 10), ~5MB but justified by the codec + `Image<Rgba32>` pixel-access abstractions we'd otherwise reinvent.

**Baseline flow.** Default: assertion fails if the baseline file doesn't exist. `--update-baselines` flips the mode — missing baselines are written from the capture (pass + log); existing baselines are overwritten from the capture (pass + log). Mode is runner-global, toggled via CLI flag.

## Components

**New files (Harness):**
- `src/Harness/Handlers/BitmapCaptureHandler.cs` — `bitmap.capture` RPC. Precondition: scenario active + `DeterminismController.Frozen`. Grabs backbuffer, optional region trim, ImageSharp PNG encode, write to `~/.cache/sdv-test-framework/captures/<scenario>/<assertion>.png`. Returns `{ "path": "...", "width": N, "height": M }`.

**New files (Runner):**
- `src/Runner/Bitmap/SsimDiff.cs` — pure-function SSIM kernel. Takes two `Image<Rgba32>`, returns float score ∈ [0,1]. Throws on dim mismatch with a clear `(W,H) vs (W',H')` message.
- `src/Runner/Bitmap/BitmapAssertion.cs` — evaluator called by `ScenarioRunner`. Flow: call `bitmap.capture` over RPC → load baseline PNG + capture PNG → SsimDiff → compare vs tolerance → produce pass/fail with SSIM score in the failure message. Honors `--update-baselines`: if flag set, write capture-bytes to the baseline path and short-circuit to pass.
- `src/Runner/Bitmap/BaselineManager.cs` — tiny helper. `TryResolveBaseline(scenarioPath, relPath) → absPath`, `IsUpdateMode()` flag, `WriteBaseline(absPath, captureBytes)`. Scenario-relative path resolution: baseline `"baselines/shop_open.png"` in a scenario at `tests/samples/11-bitmap-basic.test.json` resolves to `tests/samples/baselines/shop_open.png`.

**Modified (Runner):**
- `src/Runner/Scenarios/ScenarioRunner.cs` — `EvaluateAssertionAsync` switch gains `case "bitmap":` → `BitmapAssertion.EvaluateAsync(...)`. Wire the `UpdateBaselines` flag + `--update-baselines` flow through the runner context.
- `src/Runner/Commands/RunCommand.cs` — parse `--update-baselines` (bool flag), thread into the scenario-runner context.
- `src/Runner/Scenarios/ScenarioLoader.cs` + `schemas/scenario.schema.json` — register `bitmap` assertion type with `baseline` (string, required), `tolerance` (number ∈ (0,1], default 0.95), optional `region` (`{x,y,w,h}` — all non-negative ints, w+h > 0).

**Modified (Harness):**
- `src/Harness/ModEntry.cs` — register `bitmap.capture` in the dispatcher.
- `src/Harness/Harness.csproj` — add `SixLabors.ImageSharp` package.
- `src/Runner/Runner.csproj` — same package (multi-TFM compat ships fine).

**Modified (docs + schema):**
- `docs/rpc-schema.md` — `bitmap.capture` params/response.
- `docs/milestones/current.md` — M2-bitmap completion subsection.

**New tests (8 passing + 1 skipped):**

- `tests/Runner.Tests/Bitmap/SsimDiffTests.cs` — 3 tests.
  - `IdenticalImages_ReturnsOne` — two copies of a 64×64 gradient → 1.0.
  - `SlightlyPerturbedImages_ReturnsHighScore` — identical + 1% gaussian noise (pre-baked PNG fixture) → score > 0.95.
  - `DifferentDimensions_Throws` — 32×32 vs 64×64 → `ArgumentException` with dim message.
- `tests/Runner.Tests/Bitmap/BitmapAssertionTests.cs` — 2 tests.
  - `MatchingCapture_Passes` — shim RPC returns path to a PNG identical to the baseline; assertion passes.
  - `MissingBaseline_WithoutUpdateFlag_Fails` — baseline path doesn't exist; evaluator returns fail with "baseline not found".
- `tests/Runner.Tests/Bitmap/BaselineManagerTests.cs` — 2 tests.
  - `TryResolveBaseline_RelativePath_ResolvesAgainstScenarioDir` — `baselines/x.png` relative to `/tmp/s/1.test.json` → `/tmp/s/baselines/x.png`.
  - `WriteBaseline_CreatesParentDir_WritesBytes` — parent dir auto-created, bytes round-trip.
- `tests/Harness.Tests/BitmapCaptureHandlerTests.cs` — 1 test.
  - `MissingScenario_ReturnsGameStateInvalid` — pure param/precondition test; the GraphicsDevice path is live-SDV-only so covered by the smoke, not a unit test.

**Skipped integration:**
- `tests/Runner.Tests/BitmapFallbackIntegrationTests.cs` — 1 skipped placeholder, `BitmapAssertion_LiveSession_ProducesAndMatchesBaseline`. Exercised by the T-final smoke.

**Target test count:** 253+33 → ~261+34 (+8 Passed, +1 Skipped).

## CLI / DSL surface

### `sdv-test run [--update-baselines] ...`

New bool flag. When present:
- Missing baseline during a `bitmap` assertion → write capture bytes to baseline path, log `[bitmap] wrote baseline: <path>`, assertion passes.
- Existing baseline → overwrite with the capture bytes, log `[bitmap] updated baseline: <path>`, assertion passes.

Default (no flag): missing baseline fails with `baseline not found: <path>`; present-but-mismatched baseline fails with the SSIM score.

### Scenario DSL — `bitmap` assertion

```json
{
  "type": "bitmap",
  "baseline": "baselines/shop_open.png",
  "tolerance": 0.95,
  "region": { "x": 0, "y": 0, "w": 1280, "h": 720 }
}
```

- `baseline` — required string. Relative paths resolve against the scenario file's directory. Absolute paths accepted but not encouraged.
- `tolerance` — optional number in (0, 1]. Default `0.95` (matches the `baselines/generic/` tier from `.claude/rules/ci-integration.md` as our single-environment starting point).
- `region` — optional `{x, y, w, h}`, all non-negative ints, `w > 0`, `h > 0`. Captures a sub-rect instead of the full framebuffer. When omitted, captures the full resolution. The region applies to both the capture call and the baseline — if the baseline was taken with a region, the assertion must use the same region (enforced implicitly via dim match).
- `message` — optional, follows the pattern of other assertion types.

### RPC: `bitmap.capture`

**Params:** `{ "region": { "x": int, "y": int, "w": int, "h": int } }` — region optional; all fields required if `region` present.

**Response:** `{ "path": "<absolute>", "width": int, "height": int }`.

**Preconditions:**
- Scenario active (`ScenarioState.Current.Name != null`) — else `GameStateInvalid -32003 "bitmap.capture requires an active scenario (call scenario.begin first)"`.
- `DeterminismController.Frozen == true` — else `GameStateInvalid -32003 "bitmap.capture requires FREEZE phase (call freeze.begin first)"`.
- If `region` present, bounds must fit inside the current backbuffer — else `InvalidParams -32602 "region {x,y,w,h} exceeds backbuffer <W>×<H>"`.

## Wire shapes

### `bitmap.capture` — full capture

```json
→ { "id": 7, "method": "bitmap.capture" }
← { "id": 7, "result": {
      "path": "/home/fintan/.cache/sdv-test-framework/captures/shop_menu_test/bitmap_0.png",
      "width": 1280,
      "height": 720
    } }
```

### `bitmap.capture` — region

```json
→ { "id": 7, "method": "bitmap.capture", "params": { "region": { "x": 100, "y": 100, "w": 320, "h": 240 } } }
← { "id": 7, "result": {
      "path": "/home/fintan/.cache/sdv-test-framework/captures/shop_menu_test/bitmap_0.png",
      "width": 320,
      "height": 240
    } }
```

### Full scenario using bitmap assertion

```json
{
  "name": "shop_menu_custom_sprite_visual",
  "fixture": "m0spike_436515781",
  "config": { "seed": 42 },
  "steps": [
    { "action": "player.warp", "args": { "location": "SeedShop", "x": 4, "y": 19 } },
    { "action": "draw.arm", "args": {} },
    { "action": "wait.ms", "args": { "ms": 200 } },
    { "action": "freeze.begin", "args": {} }
  ],
  "assertions": [
    {
      "type": "bitmap",
      "baseline": "baselines/shop_menu_custom.png",
      "tolerance": 0.95
    }
  ]
}
```

## Error handling

- **Precondition fail: no scenario** — `GameStateInvalid -32003`, same shape as `FreezeBeginHandler`'s "no scenario" path.
- **Precondition fail: not frozen** — `GameStateInvalid -32003 "bitmap.capture requires FREEZE phase (call freeze.begin first)"`. Mirrors the FREEZE-gated contract in spec §4.5.
- **Region out-of-bounds** — `InvalidParams -32602 "region exceeds backbuffer"` with actual backbuffer dims included.
- **ImageSharp encode failure** — harness-side try/catch around the encode path emits `Internal -32603 "bitmap encode failed: <msg>"`. Capture cache directory creation failures fold into the same error path.
- **Baseline missing (default mode)** — runner-side assertion fails with `baseline not found: <absPath>` (no RPC error; assertion-level failure).
- **Baseline dim mismatch** — `SsimDiff` throws; assertion fails with `baseline is 1280×720 but capture is 1280×1080 — regenerate baseline with --update-baselines`.
- **SSIM below tolerance** — assertion fails with `SSIM <actual> < tolerance <configured>; capture: <absPath>`.
- **`--update-baselines` mode** — missing or present baselines both succeed silently; log line indicates which happened (`wrote` vs `updated`).
- **Parent dir creation on baseline write** — auto-created via `Directory.CreateDirectory(Path.GetDirectoryName(path))`. Write-failure (permission, disk full) surfaces as the runner-level exception with the assertion marked errored.

## Testing (beyond unit tests)

### Live smoke — scripted

One new scenario file: `tests/samples/11-bitmap-basic.test.json`. Structure:

```json
{
  "name": "bitmap_shop_menu_basic",
  "fixture": "m0spike_436515781",
  "config": { "seed": 42 },
  "steps": [
    { "action": "draw.arm", "args": {} },
    { "action": "wait.ms", "args": { "ms": 500 } },
    { "action": "freeze.begin", "args": {} }
  ],
  "assertions": [
    {
      "type": "bitmap",
      "baseline": "baselines/bitmap_shop_menu_basic.png",
      "tolerance": 0.95
    }
  ]
}
```

### Smoke sequence

1. **Baseline generation.** First run: `./scripts/run-samples.sh --update-baselines` — scenario 11's capture is written as `tests/samples/baselines/bitmap_shop_menu_basic.png`, the assertion short-circuits to pass, log line reports `[bitmap] wrote baseline: ...`. Scenarios 01-10 unchanged (their assertions aren't `bitmap`). Expect **11/11 pass**.
2. **Replay.** Second run: `./scripts/run-samples.sh` (no flag). Scenario 11 captures fresh bytes, compares against baseline → SSIM ≥ 0.95 → passes. Expect **11/11 pass**.
3. **Drift detection (manual).** Tamper the baseline (overwrite with a different frame). Re-run: scenario 11 fails with SSIM score; `tests/samples/baselines/` left as user modified it. Confirms the diff logic catches drift.
4. **`--update-baselines` regen.** Re-run with `--update-baselines`; the tampered baseline is overwritten with the current capture. Next normal run: pass. Confirms the update mode recovers.

### Acceptance criteria

1. `./scripts/ci.sh` green with ~8 new unit tests + 1 skipped integration placeholder.
2. Fresh `tests/samples/baselines/bitmap_shop_menu_basic.png` generated via `--update-baselines` on first run; next plain run matches with SSIM ≥ 0.95.
3. Tampered baseline → deterministic failure with SSIM score in the error message; re-running with `--update-baselines` restores to passing.
4. `./scripts/run-samples.sh` stays 11/11 across consecutive invocations (no drift between runs).
5. `docs/milestones/current.md` gains an M2-bitmap subsection.
6. `docs/rpc-schema.md` documents `bitmap.capture` params + response + error shapes.
7. Scenarios 01-10 (pre-bitmap suite) remain untouched and passing.

## Out of scope (TODO for M3+)

- **Pixel-exact + dHash methods** (spec §4.5) — only SSIM wired. `method` field in DSL deferred until there's a second algorithm.
- **Three-tier baseline tolerance** (generic/ci-ubuntu/self-hosted-nvidia from `.claude/rules/ci-integration.md`) — M2 takes a single per-assertion tolerance. Tier resolution lands with the first CI environment that actually feeds baselines from somewhere other than a dev workstation.
- **Diff-image on failure** (`tests/diffs/<scenario>/<assertion>.png`) — M2 errors report SSIM + dims; the visual artifact is M3 polish.
- **Git LFS for baselines** — same TODO as fixtures (`.claude/rules/fixtures.md`). Regular git blobs until the repo has >5 baselines.
- **Per-scenario capture-cache cleanup** — `~/.cache/sdv-test-framework/captures/<scenario>/` accumulates across runs. M3 adds cache-sweep semantics (age-based or run-scoped).
- **`sdv-test baselines` subcommand** — currently `--update-baselines` is a `run` flag. A dedicated subcommand (`list`, `update`, `delete`, `show <scenario>`) is M3 ergonomics.
- **Animated / multi-frame capture** — capture is one backbuffer grab. Sequence captures (N frames, assert per frame) deferred.
- **Non-FREEZE captures** — spec explicitly forbids; M2 enforces. A future streaming-capture mode (e.g., video for regression review) would need its own design.
- **Windows backbuffer quirks** — harness captures via `GraphicsDevice.GetBackBufferData<Color>(...)` which is cross-platform. Any Windows-specific coordinate/orientation fixup surfaces if/when we test on Windows.

## Links

- Spec: `docs/spec.md` §4.5 (Bitmap Fallback), §2 (Core insight — draw-call primary, bitmap 5% fallback)
- CI guide: `.claude/rules/ci-integration.md` (tier definitions, Xvfb caveats)
- M2 tracker: `docs/milestones/current.md` §M2 — Production polish
- Prior M2 (this batch): fixture builder, reporters, watch mode, record mode
