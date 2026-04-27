# Bitmap Completion Bundle — Design Spec

**Status:** Approved design, pre-implementation
**Author:** Brainstorm 2026-04-26
**Source:** roadmap.md Tier 3 — bundle of 4 related items
**Predecessor:** diff-image-on-failure (`docs/superpowers/specs/2026-04-25-diff-image-on-failure-design.md`)

## 1. Problem

Four Tier 3 items share enough infrastructure to design together:

1. **Pixel-exact + dHash bitmap methods.** Spec §4.5 lists three diff algorithms; only SSIM ships today. Pixel-exact catches strict regressions where any drift is a bug; dHash catches "vaguely similar" mismatches where SSIM is too noisy.
2. **Three-tier baseline tolerance.** `.claude/rules/ci-integration.md` describes per-environment tolerance presets (`generic`/`ci-ubuntu`/`self-hosted-nvidia`). Today every assertion picks a one-off number, which doesn't scale across environments.
3. **`sdv-test baselines` subcommand.** The current `--update-baselines` flag is a static field on `RunCommand` that the rest of the codebase reaches into. Moving it behind a real subcommand removes the hack and unlocks `list`/`show`/`delete` operations.
4. **Capture-cache cleanup.** `~/.cache/sdv-test-framework/captures/` accumulates indefinitely. After a few hundred runs that's gigabytes.

These four are independently shippable but share data structures (`BitmapAssertionSpec`, `BaselineManager`) and benefit from a single design pass.

## 2. Goal

Complete spec §4.5 (bitmap diff methods) and the `.claude/rules/ci-integration.md` tier model in tolerance-preset form. Replace the `--update-baselines` static-field hack with a proper `baselines` command. Cap capture-cache disk usage with a sweep that runs automatically + on demand.

## 3. Non-goals

- **Per-tier baseline directories.** Option A (tolerance preset only) is what we're shipping. Tier names map to tolerance defaults; baseline files are shared across tiers. Per-environment baseline storage is a future tier upgrade if real CI environments diverge enough to matter.
- **dHash diff heatmap.** dHash localises poorly per-pixel (it's a perceptual hash). On dHash failure we emit baseline + capture and skip the diff overlay.
- **`baselines regenerate` / `baselines validate`.** Lean subcommand surface — `update` covers regeneration; orphaned-baseline detection is a Tier 4 polish.
- **Triptych composite for pixel-exact / dHash.** SSIM only for now; opt-in `--diff-format=triptych` already exists for SSIM. Adding it for the new methods is mechanical but defers.
- **LFS for baselines.** Separate Tier 4 item; kicks in once we have >5 baseline files in the repo.
- **Real environment detection** (`generic` vs `ci-ubuntu` autodetect from hostname/env). Tier is set explicitly via `--tier=<name>` flag; default `generic`.

## 4. Architecture

### 4.1 Method dispatch

`ScenarioAssertion` gains a `Method` field (string, default `"ssim"`). `BitmapAssertion.EvaluateAsync` branches on it:

```csharp
var method = a.Method ?? "ssim";
switch (method)
{
    case "ssim":         (passed, msg, ssim) = EvaluateSsim(...);
    case "pixel-exact":  (passed, msg) = EvaluatePixelExact(...);
    case "dhash":        (passed, msg) = EvaluateDHash(...);
    default:             return Fail($"unknown method '{method}'");
}
```

Each evaluator is a private static helper inside `BitmapAssertion`. The pure diff functions (`PixelExactDiff`, `DHashDiff`) live in their own files for testability.

### 4.2 Tolerance polymorphism (Q2 option A)

Single `tolerance` field; semantics depend on method:
- **SSIM**: float in (0, 1]; pass iff `score ≥ tolerance`. Default 0.95.
- **Pixel-exact**: int ≥ 0; pass iff `max per-channel RGB delta ≤ tolerance`. Default 0.
- **dHash**: int 0-64; pass iff `Hamming distance ≤ tolerance`. Default 5.

Validation is method-specific:
- SSIM: rejects `tolerance ≤ 0 || tolerance > 1` (existing).
- Pixel-exact: rejects negative; treats float input as truncation to int.
- dHash: rejects negative or >64.

When `tolerance` is unspecified in the assertion, the method-specific default applies (NOT the SSIM default of 0.95).

### 4.3 Tier resolution (Q1 option A)

`RunCommand` gains `--tier=<name>` flag. Default `generic`. Valid values: `generic | ci-ubuntu | self-hosted-nvidia`. Unknown tier → exit 2 with diagnostic.

Tier maps to a default tolerance per method via a static table:

| method        | generic | ci-ubuntu | self-hosted-nvidia |
| ------------- | ------- | --------- | ------------------ |
| ssim          | 0.95    | 0.98      | 0.999              |
| pixel-exact   | 5       | 2         | 0                  |
| dhash         | 5       | 3         | 1                  |

Resolution order for the effective tolerance on any bitmap assertion:
1. Per-assertion `tolerance` (if specified) — highest priority.
2. Tier default for the assertion's method (if `tolerance` unspecified).
3. Method default (if no `--tier` flag).

Per-assertion `Tier` field also exists, mirroring `DiffFormat` from the diff-image work — overrides the run-wide flag for that one assertion. Useful when one assertion in a suite needs strict tolerance while the rest are lenient.

### 4.4 PixelExactDiff

```csharp
public static class PixelExactDiff
{
    public static int MaxChannelDelta(Image<Rgba32> a, Image<Rgba32> b);
    // Throws ArgumentException on dimension mismatch (same shape as SsimDiff).
}
```

Implementation: iterate every pixel, compute `max(|aR-bR|, |aG-bG|, |aB-bB|)`, track running max. Alpha ignored (consistent with SSIM). Returns `int` in [0, 255]. ~20 LOC.

### 4.5 DHashDiff

```csharp
public static class DHashDiff
{
    public static int HammingDistance(Image<Rgba32> a, Image<Rgba32> b);
    public static ulong Compute(Image<Rgba32> img);  // 64-bit perceptual hash
}
```

Algorithm (standard difference hash):
1. Resize image to 9×8 grayscale (use ImageSharp `Resize` with `Mode.Stretch`, `Sampler.Bicubic`).
2. For each row, compare adjacent pixel pairs (8 comparisons × 8 rows = 64 bits): `bit = leftPixel < rightPixel ? 1 : 0`.
3. Pack into a `ulong`.

`HammingDistance` = `BitOperations.PopCount(hashA ^ hashB)`. Range [0, 64]. ~50 LOC.

### 4.6 Diff renderer extensions

`DiffImageRenderer.Render` already produces baseline + capture + diff (heatmap). Method-specific behaviour:

- **SSIM** (existing): bilinear-smoothed per-block redness heatmap.
- **Pixel-exact**: per-pixel redness based on `max channel delta / 255`. No bilinear smoothing — block grid concept doesn't apply. Same red-tint formula.
- **dHash**: skip diff.png. Output is `baseline.png` + `capture.png` only. The renderer signature accepts a "no heatmap" mode; when invoked with that mode the returned `DiffSet.Diff` is the empty string; HtmlReportGenerator's forensics block already handles the case (renders 2 figures instead of 3 when diff path is empty).

The renderer's signature gains a `BitmapMethod method` discriminator and shifts to a single numeric tolerance:

```csharp
public static DiffSet Render(
    byte[] baselineBytes, byte[] captureBytes,
    SsimResult? ssim,            // populated only when method == Ssim
    double tolerance,            // SSIM: 0-1 score; pixel-exact: max channel delta; dHash: ignored
    BitmapMethod method,
    DiffFormat format,
    string outputDir);
```

Branches:
- `Ssim` — existing path; `ssim` non-null; bilinear-smoothed block redness.
- `PixelExact` — `ssim` null; per-pixel `redness = max(|aR-bR|, |aG-bG|, |aB-bB|) / 255` clamped, with the same red-tint formula. Pixels where delta ≤ tolerance get redness=0 (visually clean — heatmap surfaces only failing pixels).
- `DHash` — writes baseline.png + capture.png only; `DiffSet.Diff` returned as empty string. No decode of either image needed beyond the byte copy.

### 4.7 `sdv-test baselines` subcommand

New top-level command; `Program.cs` registers it alongside `run`/`record`/`fixture`/`mcp`. `BaselinesCommand.RunAsync(args, ct)` dispatches on first arg:

**`baselines list [--scenarios=<dir>]`**
- Default scenarios dir: `Directory.GetCurrentDirectory()`.
- Walks `*.test.json` files; for each bitmap assertion, resolves the baseline path via `BaselineManager.ResolveBaseline`.
- Output: table per baseline `<path> [PRESENT|MISSING] <bytes> <last referenced by scenario name>`.
- Exit 0 if at least one scenario found; 1 if no scenarios in dir.

**`baselines update <path-or-glob> [--tier=<name>] [--mods-path=<p>]`**
- Resolves the path-or-glob into a list of `*.test.json` files (same logic as `run`).
- Effectively runs `sdv-test run <args> --update-baselines`. Reuses RunCommand internals.
- Removes the static-field `_updateBaselinesFlag` hack: instead of setting the field, builds a `RunCommandOptions` record passed explicitly into the run path. (Refactor scope: introduce `RunCommandOptions` to thread these flags formally; `update` builds one with `UpdateBaselines = true`.)

**`baselines show <path>`**
- Reads PNG metadata via ImageSharp `Image.Identify(path)` (no full decode — fast).
- Prints: dimensions, file size, mtime, MD5 hash (8 hex chars for visual ID), last modified date.
- Optional: walks scenarios dir to find which scenarios reference this baseline; appends "Used by:" list.

**`baselines delete <path> [--force]`**
- If `--force` absent, prompts on stdin: `delete <path>? [y/N]`.
- Removes the file. Doesn't check for references (orphan detection is out of scope).

### 4.8 Capture-cache cleanup

`~/.cache/sdv-test-framework/captures/<scenario>/bitmap_N.png` accumulates. New `CaptureCacheCleaner` static class:

```csharp
public static int CleanCache(string cacheDir, int maxAgeDays, int keepRuns, bool dryRun);
```

Logic:
- Enumerate all PNG files under `cacheDir` recursively.
- A file is kept if BOTH conditions hold: mtime within `maxAgeDays` AND file's parent scenario dir is among the `keepRuns` most-recently-modified.
- A file is deleted if EITHER condition fails: too old OR not in recent-runs window.
- "Most recent runs" is determined by `Directory.GetLastWriteTimeUtc` on the per-scenario subdir, taking the top N.
- Dry run: report what would delete without touching files.
- Returns count of files deleted (or would-delete in dry-run).

**Two invocation paths:**
1. **Automatic.** `RunCommand` calls `CleanCache(defaultCacheDir, maxAgeDays: 7, keepRuns: 5, dryRun: false)` at end of every successful invocation. Opt out: `--no-cache-cleanup`. Failures during cleanup log to stderr; never fail the run.
2. **Manual.** `sdv-test cache clean [--max-age=<days>] [--keep-runs=<n>] [--dry-run]`. Defaults match the auto path. Useful for one-shot bulk cleanup when the auto-sweep hasn't been running.

Path normalization: cache dir resolved as `Path.Combine(Environment.GetFolderPath(SpecialFolder.UserProfile), ".cache", "sdv-test-framework", "captures")`. Override via `SDV_CACHE_DIR` env var (consistent with existing `SDV_MODS_PATH`/`SDV_REPORT_DIR` pattern).

## 5. Wire format

**`bitmap` assertion in scenario JSON** gains two optional properties:

```json
{
  "type": "bitmap",
  "baseline": "baselines/shop.png",
  "method": "pixel-exact",
  "tolerance": 2,
  "tier": "ci-ubuntu"
}
```

`method` enum: `"ssim" | "pixel-exact" | "dhash"`. Default `"ssim"`.
`tier` enum: `"generic" | "ci-ubuntu" | "self-hosted-nvidia"`. Optional; falls back to run-wide flag.

JSON schema validates: when `method == "ssim"`, `tolerance` is `number` in `(0, 1]`. When `method ∈ {"pixel-exact", "dhash"}`, `tolerance` is `integer`.

The existing `tolerance` field stays — semantics polymorphism is at the runtime layer, not the schema (the schema accepts both number and integer; runtime validates the range based on method).

## 6. Testing

### 6.1 PixelExactDiff (3 tests)
- `IdenticalImages_ReturnsZero`
- `OffByOneChannel_ReturnsOne`
- `MaxChannelDeltaAcrossPixels_ReturnsLargestSingleDelta`

### 6.2 DHashDiff (3 tests)
- `IdenticalImages_HammingDistanceZero`
- `MinorNoise_HammingDistanceLowSingleDigit` — fixture: gradient + ±2 LSB noise → expect distance ≤ 5.
- `Inverted_HammingDistanceHigh` — inverted image → expect distance ≥ 30.

### 6.3 BitmapAssertion dispatch (3 tests)
- `MethodPixelExact_DispatchesToPixelExactDiff` — failing case asserts the failure message references max-channel-delta, not SSIM score.
- `MethodDHash_DispatchesToDHashDiff` — failure message references Hamming distance.
- `UnknownMethod_FailsWithDiagnostic` — `method: "garbage"` returns `BitmapAssertionResult(false, "unknown method 'garbage'")`.

### 6.4 Tier resolution (2 tests)
- `RunCommandFlag_SetsRunWideTier` — `--tier=ci-ubuntu` populates the static thread-through.
- `PerAssertionTolerance_OverridesTier` — assertion with explicit `tolerance: 0.99` wins over tier default.

### 6.5 BaselinesCommand (4 tests)
- `List_EnumeratesReferencedBaselines_MarksMissingPresent`
- `Update_DispatchesToRunCommandWithUpdateMode` — uses a stub run executor; asserts `UpdateBaselines = true` was set.
- `Show_PrintsPngMetadata` — captures stdout, asserts dimensions + file size present.
- `Delete_WithForce_RemovesFile` — file exists before, doesn't after.

### 6.6 CaptureCacheCleaner (3 tests)
- `MaxAgeZero_DeletesAllFiles`
- `KeepRuns_RetainsNMostRecentScenarioDirs`
- `DryRun_ReportsButDoesntDelete`

### 6.7 Diff renderer extensions (2 tests)
- `PixelExactMethod_RendersPerPixelHeatmap` — assert sample pixels in regions of high delta show red dominance.
- `DHashMethod_SkipsDiffPng` — output set's `Diff` field is empty string; only baseline + capture written.

### 6.8 Integration placeholder (1 skipped)
- `BitmapMethods_AllThreeWorkAgainstLiveSDV` — `[Fact(Skip="Requires live SDV")]`.

**Total: 20 new passing + 1 skipped placeholder. Target: 368+47 → 388+48.**

## 7. Risks + open questions

**Tolerance semantics confusion.** Same `tolerance` field meaning different things across methods is a documented footgun (Q2 option A). Mitigation: schema validates per-method ranges; failure messages always print `tolerance` with units (e.g. `SSIM 0.7234 < tolerance 0.95`, `pixel-exact max delta 17 > tolerance 5`, `dHash distance 12 > tolerance 5`).

**dHash test brittleness.** Hamming distance for "minor noise" is empirically 0-5; setting threshold at 5 might be flaky depending on the noise injection. Mitigation: the test uses a fixed RNG seed (existing `GradientWithNoise(seed=123)` pattern from `SsimDiffTests`).

**Static-field hack removal.** `_updateBaselinesFlag` is read by `RunCommand` AND by `BaselinesCommand` (the latter via the shared run path). Refactor introduces `RunCommandOptions` record passed explicitly. Cascade: every callsite that touches the static field needs a small update. Plan task should enumerate them up front.

**Cache cleanup running on every invocation.** Default behaviour. Could surprise users by deleting captures they wanted to inspect. Mitigation: `--no-cache-cleanup` opt-out; default `keep-runs=5` is generous.

**Tier name validation.** Three tier names hardcoded. Adding a fourth needs a code change in three places (CLI flag validator, tolerance table, schema enum). Not a problem for v1; refactor when a real fourth tier shows up.

**Cache cleanup vs HTML report dir.** Cache cleanup targets `~/.cache/sdv-test-framework/captures/`. HTML run reports live in `./test-results/`. Different dirs, different lifecycles. Cleanup does NOT touch report dirs.

## 8. Out of scope (Tier 3/4 followups)

- LFS for baselines.
- Per-tier baseline directories (option B from brainstorm — defer).
- `baselines regenerate` / `baselines validate`.
- Triptych composite for pixel-exact / dHash (mechanical extension of existing renderer; defer).
- dHash diff heatmap (per data flow §4.6 — explicitly skipped, dHash localises poorly).
- Real environment autodetection for tier (`generic` is the unconditional default).
- Compression / WebP encoding for cache + baseline storage.
- Test-results dir cleanup (`./test-results/` — pairs with this work but separate concern).
- MCP `run_scenario` exposing `tier` arg (today `diff_format` is the only forward-compat field).
- Cleanup metrics (delete count summary at run end). Auto path is silent unless errors.

## 9. Implementation plan handoff

Single plan, 7 tasks across 4 phases:

- **Phase 1 (Methods + Tier):** T1 PixelExactDiff + tests, T2 DHashDiff + tests, T3 BitmapAssertion dispatch + diff renderer extensions.
- **Phase 2 (Tier resolution):** T4 `--tier` CLI flag + tolerance resolution table + schema updates.
- **Phase 3 (Baselines subcommand):** T5 RunCommandOptions refactor (removes static-field hack), T6 BaselinesCommand with 4 subcommands.
- **Phase 4 (Cache cleanup):** T7 CaptureCacheCleaner + auto-hook in RunCommand + manual `cache clean` command.

Save to `docs/superpowers/plans/2026-04-26-bitmap-completion-bundle.md`.
